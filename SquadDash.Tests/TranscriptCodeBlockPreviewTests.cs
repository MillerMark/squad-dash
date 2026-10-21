using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class TranscriptCodeBlockPreviewTests
{
    [Test]
    public void TryReadFenceStart_UsesOnlyFirstInfoToken()
    {
        var parsed = MarkdownDocumentRenderer.TryReadFenceStart("```csharp title=\"Demo\" extra", out var fenceLength, out var language);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(fenceLength, Is.EqualTo(3));
            Assert.That(language, Is.EqualTo("csharp"));
        });
    }

    [TestCase("````  ts\tfilename=demo.ts", true, 4, "ts")]
    [TestCase("```\t\r", true, 3, "")]
    [TestCase("``cs", false, 2, "")]
    [TestCase("ordinary text", false, 0, "")]
    public void TryReadFenceStart_WhitespaceAbsentLanguageAndNonFences(
        string source, bool expected, int length, string token)
    {
        Assert.That(MarkdownDocumentRenderer.TryReadFenceStart(source, out var fenceLength, out var language),
            Is.EqualTo(expected));
        Assert.Multiple(() =>
        {
            Assert.That(fenceLength, Is.EqualTo(length));
            Assert.That(language, Is.EqualTo(token));
        });
    }

    [Test]
    public void IsFenceClose_RequiresAtLeastOpeningLength_AndNoTrailingContent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MarkdownDocumentRenderer.IsFenceClose("```", openingFenceLength: 4), Is.False);
            Assert.That(MarkdownDocumentRenderer.IsFenceClose("````", openingFenceLength: 4), Is.True);
            Assert.That(MarkdownDocumentRenderer.IsFenceClose("`````", openingFenceLength: 4), Is.True);
            Assert.That(MarkdownDocumentRenderer.IsFenceClose("````   ", openingFenceLength: 4), Is.True);
            Assert.That(MarkdownDocumentRenderer.IsFenceClose("```` trailing", openingFenceLength: 4), Is.False);
        });
    }

    [Test]
    public void ResolveGrammar_ReturnsNull_ForAbsentPlainAndUnknownTokens()
    {
        var engine = new RegionHighlightingEngine();

        Assert.Multiple(() =>
        {
            Assert.That(TranscriptCodeBlockPreview.ResolveGrammar(null, "using System;\npublic class C {}", engine), Is.Null);
            Assert.That(TranscriptCodeBlockPreview.ResolveGrammar("", "using System;\npublic class C {}", engine), Is.Null);
            Assert.That(TranscriptCodeBlockPreview.ResolveGrammar("   ", "x", engine), Is.Null);
            Assert.That(TranscriptCodeBlockPreview.ResolveGrammar(RegionLanguagePicker.PlainTextToken, "x", engine), Is.Null);
            Assert.That(TranscriptCodeBlockPreview.ResolveGrammar("not-a-real-language", "x", engine), Is.Null);
        });
    }

    [Test]
    public void ResolveGrammar_EnforcesCharacterCap_InclusiveAndExclusive()
    {
        var engine = new RegionHighlightingEngine();
        var atCap = new string('a', TranscriptCodeBlockPreview.MaximumCharacters);
        var overCap = new string('a', TranscriptCodeBlockPreview.MaximumCharacters + 1);

        Assert.Multiple(() =>
        {
            Assert.That(TranscriptCodeBlockPreview.ResolveGrammar("cs", atCap, engine), Is.EqualTo("C#"));
            Assert.That(TranscriptCodeBlockPreview.ResolveGrammar("cs", overCap, engine), Is.Null);
        });
    }

    [Test]
    public void ResolveGrammar_EnforcesLineCap_InclusiveAndExclusive()
    {
        var engine = new RegionHighlightingEngine();
        var atCap = string.Join("\n", Enumerable.Repeat("x", TranscriptCodeBlockPreview.MaximumLines));
        var overCap = string.Join("\n", Enumerable.Repeat("x", TranscriptCodeBlockPreview.MaximumLines + 1));

        Assert.Multiple(() =>
        {
            Assert.That(TranscriptCodeBlockPreview.ResolveGrammar("cs", atCap, engine), Is.EqualTo("C#"));
            Assert.That(TranscriptCodeBlockPreview.ResolveGrammar("cs", overCap, engine), Is.Null);
        });
    }

    [TestCase("\r\n")]
    [TestCase("\r")]
    public void ResolveGrammar_CountsLogicalLinesAcrossLineEndings(string newline)
    {
        var text = string.Join(newline, Enumerable.Repeat("x", TranscriptCodeBlockPreview.MaximumLines));
        Assert.That(TranscriptCodeBlockPreview.ResolveGrammar("cs", text), Is.EqualTo("C#"));
        Assert.That(TranscriptCodeBlockPreview.ResolveGrammar("cs", text + newline), Is.Null);
    }

    [Test]
    public void ResolveGrammar_AliasTokensResolveCanonicalGrammar_AndPoolReturnsSameDefinition()
    {
        var engine = new RegionHighlightingEngine();
        var fromCs = TranscriptCodeBlockPreview.ResolveGrammar("cs", "class C { }", engine);
        var fromCSharp = TranscriptCodeBlockPreview.ResolveGrammar("csharp", "class C { }", engine);
        var pool = new TranscriptHighlightingPool(engine, PaletteFrom(engine, isDark: false));

        var first = pool.GetDefinition(fromCs!);
        var second = pool.GetDefinition(fromCSharp!);

        Assert.Multiple(() =>
        {
            Assert.That(fromCs, Is.EqualTo("C#"));
            Assert.That(fromCSharp, Is.EqualTo("C#"));
            Assert.That(first, Is.SameAs(second));
        });
    }

    [Test]
    public void PoolPalettePush_RecolorsPooledDefinition_WithoutMutatingIndependentPromptDefinition()
    {
        var engine = new RegionHighlightingEngine();
        var light = PaletteFrom(engine, isDark: false);
        var dark = PaletteFrom(engine, isDark: true);

        var pool = new TranscriptHighlightingPool(engine, light);
        var pooled = pool.GetDefinition("C#");
        var independentPromptDefinition = engine.CreateDefinition("C#", light);

        var independentBefore = ColorOf(independentPromptDefinition.GetNamedColor("Keyword"));
        pool.ApplyPalette(dark);

        var pooledAfter = ColorOf(pooled.GetNamedColor("Keyword"));
        var independentAfter = ColorOf(independentPromptDefinition.GetNamedColor("Keyword"));
        var expectedDark = ColorOf(engine.CreateDefinition("C#", dark).GetNamedColor("Keyword"));

        Assert.Multiple(() =>
        {
            Assert.That(pooledAfter, Is.EqualTo(expectedDark));
            Assert.That(independentAfter, Is.EqualTo(independentBefore));
        });
    }

    [Test]
    public void BuildResponseBlocks_LongFenceRoundTrip_PreservesShorterInnerFenceContent()
    {
        EnsureApplicationResources();
        var renderer = MakeRenderer();
        var thread = new TranscriptThreadState("t1", TranscriptThreadKind.Coordinator, "Test", DateTimeOffset.Now);
        var section = new Section();
        var turn = new TranscriptTurnView(thread, "prompt", DateTimeOffset.Now, section, []);
        var entry = new TranscriptResponseEntry(turn, 1, section, allowQuickReplies: false);

        var blocks = renderer.BuildResponseBlocks(entry, """
            Intro

            ````markdown
            # Artifact prompt

            ```json
            { "ok": true }
            ```

            Still inside the artifact.
            ````

            Outro
            """, allowQuickReplies: false).ToList();

        var codeBlock = blocks.OfType<BlockUIContainer>().Single();
        var codeText = FindCodeTextBox(codeBlock).Text.Replace("\r\n", "\n");

        Assert.Multiple(() =>
        {
            Assert.That(codeText, Does.Contain("```json"));
            Assert.That(codeText, Does.Contain("{ \"ok\": true }"));
            Assert.That(codeText, Does.Contain("Still inside the artifact."));
            Assert.That(blocks.OfType<Paragraph>().Select(paragraph => paragraph.Tag), Does.Contain("Intro"));
            Assert.That(blocks.OfType<Paragraph>().Select(paragraph => paragraph.Tag), Does.Contain("Outro"));
        });
    }

    [Test]
    public void BuildTranscript_CheapFirst_UsesTextBoxSurfaceOnly()
    {
        EnsureApplicationResources();
        var block = (BlockUIContainer)CopyableCodeBlockBuilder.Build(
            "var value = 1;", baseFontSize: 14, transcript: true, language: "cs");
        var container = (Grid)block.Child;
        var textBox = container.Children.OfType<TextBox>().Single();
        var preview = (TranscriptCodeBlockPreview)block.Tag!;

        Assert.Multiple(() =>
        {
            Assert.That(container.Children.OfType<TextEditor>(), Is.Empty);
            Assert.That(preview.Surface, Is.SameAs(textBox));
            Assert.That(textBox.MaxHeight, Is.EqualTo(850));
            Assert.That(textBox.IsReadOnly, Is.True);
        });
    }

    [Test]
    public void SharedBuilder_RemainsUnboundedPlainTextWithoutTranscriptOptIn()
    {
        EnsureApplicationResources();
        var block = (BlockUIContainer)CopyableCodeBlockBuilder.Build("sample", 14);
        var container = (Grid)block.Child;
        var textBox = container.Children.OfType<TextBox>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(block.Tag, Is.Null);
            Assert.That(container.Tag, Is.Null);
            Assert.That(container.Children.OfType<TextBlock>(), Is.Empty);
            Assert.That(textBox.MaxHeight, Is.EqualTo(double.PositiveInfinity));
            Assert.That(textBox.IsReadOnly, Is.True);
        });
    }

    [TestCase(12)]
    [TestCase(14)]
    [TestCase(20)]
    public void LanguageLabel_UsesEightyPercentOfBaseFont_BeforeAndAfterResize(double baseFontSize)
    {
        EnsureApplicationResources();
        var block = (BlockUIContainer)CopyableCodeBlockBuilder.Build(
            "var value = 1;", baseFontSize, transcript: true, language: "cs");
        var container = (Grid)block.Child;
        var preview = (TranscriptCodeBlockPreview)block.Tag;
        var label = container.Children.OfType<TextBlock>().Single();

        Assert.That(label.FontSize, Is.EqualTo(baseFontSize * 0.8).Within(0.001));
        var initialPadding = preview.PlainTextBox.Padding;
        preview.UpdateFontSize(baseFontSize);
        Assert.That(preview.PlainTextBox.Padding, Is.EqualTo(initialPadding));

        preview.UpdateFontSize(baseFontSize + 4);
        Assert.Multiple(() =>
        {
            Assert.That(label.FontSize, Is.EqualTo((baseFontSize + 4) * 0.8).Within(0.001));
            Assert.That(label.FontSize, Is.LessThan(preview.PlainTextBox.FontSize));
            Assert.That(label.Margin.Top, Is.EqualTo(5));
            Assert.That(preview.PlainTextBox.Padding.Top, Is.EqualTo(Math.Max(24, (baseFontSize + 4) * 1.5 + 2)));
        });
    }

    [TestCase("Light", "Dark")]
    [TestCase("Dark", "Light")]
    public void UpgradeAndThemeSwitch_KeepLiveSubtleLabelAndCodeSurface(string initialTheme, string nextTheme)
    {
        EnsureApplicationResources();
        var block = (BlockUIContainer)CopyableCodeBlockBuilder.Build(
            "var value = 1;", 14, transcript: true, language: "cs");
        var container = (Grid)block.Child;
        var preview = (TranscriptCodeBlockPreview)block.Tag;
        var label = container.Children.OfType<TextBlock>().Single();
        var referenceBlock = (BlockUIContainer)CopyableCodeBlockBuilder.Build("plain", 14);
        var reference = FindCodeTextBox(referenceBlock);
        var document = new FlowDocument(block);
        document.Blocks.Add(referenceBlock);
        var window = new Window
        {
            Content = new RichTextBox { Document = document, IsReadOnly = true },
            Width = 400, Height = 300, ShowActivated = false, ShowInTaskbar = false,
        };
        var initialPalette = PaletteFrom(new RegionHighlightingEngine(), initialTheme == "Dark");
        try
        {
            window.Resources.MergedDictionaries.Add(LoadTheme(initialTheme));
            Assert.That(preview.PlainTextBox.Background, Is.SameAs(reference.Background));
            window.Show();
            window.UpdateLayout();
            // Exercise styling independently of deferred viewport scheduling.
            typeof(TranscriptCodeBlockPreview).GetMethod("Upgrade", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(preview, null);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.That(preview.Surface, Is.TypeOf<TextEditor>());
            var editor = (TextEditor)preview.Surface;
            Assert.Multiple(() =>
            {
                Assert.That(editor.Background, Is.SameAs(reference.Background));
                Assert.That(label.Foreground, Is.SameAs(window.FindResource("SubtleText")));
                Assert.That(editor.Padding, Is.EqualTo(preview.PlainTextBox.Padding));
            });

            window.Resources.MergedDictionaries[0] = LoadTheme(nextTheme);
            var palette = PaletteFrom(new RegionHighlightingEngine(), nextTheme == "Dark");
            TranscriptCodeBlockPreview.ApplyTheme(window.Dispatcher, palette);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            preview.UpdateFontSize(20);
            Assert.Multiple(() =>
            {
                Assert.That(label.Foreground, Is.SameAs(window.FindResource("SubtleText")));
                Assert.That(editor.Background, Is.SameAs(window.FindResource("CodeSurface")));
                Assert.That(editor.Background, Is.SameAs(reference.Background));
                Assert.That(editor.Foreground, Is.SameAs(palette.Text));
                Assert.That(editor.Padding, Is.EqualTo(preview.PlainTextBox.Padding));
                Assert.That(DependencyPropertyHelper.GetValueSource(editor, Control.BackgroundProperty).IsExpression, Is.True);
            });
        }
        finally
        {
            window.Close();
            TranscriptCodeBlockPreview.ApplyTheme(window.Dispatcher, initialPalette);
        }
    }

    private static ResourceDictionary LoadTheme(string theme)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "SquadDash")))
            directory = directory.Parent;
        using var stream = File.OpenRead(Path.Combine(
            directory?.FullName ?? throw new DirectoryNotFoundException("SquadDash source not found."),
            "SquadDash", "Themes", theme + ".xaml"));
        return (ResourceDictionary)XamlReader.Load(stream);
    }

    [Test]
    public void OverCapBlock_RetainsFullTextAndCopy()
    {
        EnsureApplicationResources();
        var code = new string('x', TranscriptCodeBlockPreview.MaximumCharacters + 1);
        var block = (BlockUIContainer)CopyableCodeBlockBuilder.Build(code, 14, transcript: true, language: "cs");
        var preview = (TranscriptCodeBlockPreview)block.Tag;
        Assert.Multiple(() =>
        {
            Assert.That(preview.PlainTextBox.Text, Is.EqualTo(code));
            Assert.That(preview.PlainTextBox.MaxHeight, Is.EqualTo(double.PositiveInfinity));
            Assert.That(preview.GetCopyText(), Is.EqualTo(code));
        });
    }

    [Test]
    public void TranscriptCodeProjectionAndWholeSelection_ExcludeLanguageLabel()
    {
        EnsureApplicationResources();
        const string code = "var sample = 1;";
        var block = (BlockUIContainer)CopyableCodeBlockBuilder.Build(code, 14, transcript: true, language: "cs");
        var document = new FlowDocument(block);
        var preview = (TranscriptCodeBlockPreview)block.Tag;
        MarkdownVisibleContent.Bind(document);
        var host = new RichTextBox { Document = document };
        host.SelectAll();
        Assert.Multiple(() =>
        {
            Assert.That(MarkdownVisibleContent.GetText(document), Is.EqualTo(code));
            Assert.That(TranscriptCopyService.BuildSelectionText(host), Is.EqualTo(code));
            Assert.That(preview.GetCopyText(), Is.EqualTo(code));
        });
    }

    [Test]
    public void BuildTranscript_FallbackCopyPreservesContent_HonorsVisibleProvider_AndExcludesLabel()
    {
        EnsureApplicationResources();
        const string code = "line 1\nline 2";
        var block = (BlockUIContainer)CopyableCodeBlockBuilder.Build(
            code, baseFontSize: 14, transcript: true, language: RegionLanguagePicker.PlainTextToken);
        var container = (Grid)block.Child;
        var textBox = container.Children.OfType<TextBox>().Single();
        var label = container.Children.OfType<TextBlock>().Single();
        var preview = (TranscriptCodeBlockPreview)block.Tag!;

        textBox.Select(0, 4);
        var fullCopy = preview.GetCopyText(false);
        var selectedCopy = preview.GetCopyText(true);
        CopyableCodeBlockBuilder.SetVisibleTextProvider(textBox, () => "");
        var suppressedCopy = preview.GetCopyText(false);

        Assert.Multiple(() =>
        {
            Assert.That(label.Text, Is.EqualTo(RegionLanguagePicker.PlainTextToken));
            Assert.That(fullCopy.Replace("\r\n", "\n"), Is.EqualTo(code));
            Assert.That(selectedCopy, Is.EqualTo("line"));
            Assert.That(fullCopy, Does.Not.Contain(label.Text));
            Assert.That(suppressedCopy, Is.Empty);
        });
    }

    [TestCase(-120, 400, 500, 100, true)]
    [TestCase(-120, 399.4, 500, 100, false)]
    [TestCase(120, 0.5, 500, 100, true)]
    [TestCase(120, 0.6, 500, 100, false)]
    [TestCase(0, 0, 500, 100, false)]
    [TestCase(120, 0, 100, 100, true)]
    [TestCase(-120, 0, 100, 100, true)]
    [TestCase(120, 400, 500, 100, false)]
    public void ShouldBubbleMouseWheel_BoundariesAndNoDelta(
        int delta, double verticalOffset, double extentHeight, double viewportHeight, bool expected)
    {
        Assert.That(AvalonEditRegionEditor.ShouldBubbleMouseWheel(
            delta, verticalOffset, extentHeight, viewportHeight), Is.EqualTo(expected));
    }

    private static MarkdownDocumentRenderer MakeRenderer() =>
        new(
            getFontSize: () => 14.0,
            getWorkspaceGitHubUrl: () => "https://github.com/owner/repo",
            onLinkClicked: _ => { },
            onException: (_, _) => { },
            resolveContinuationThread: _ => null,
            onQuickReplyButtonClick: (_, _) => { },
            appendResponseSegment: (_, _, _) => { },
            scrollToEndIfAtBottom: _ => { },
            getCoordinatorThread: () => null!,
            resolvePlanDisplayName: null,
            resolvesToCommit: _ => true);

    private static TextBox FindCodeTextBox(BlockUIContainer block)
    {
        var panel = (Panel)block.Child;
        return panel.Children.OfType<TextBox>().Single();
    }

    private static RegionEditorPalette PaletteFrom(RegionHighlightingEngine engine, bool isDark)
    {
        var surface = new SolidColorBrush(isDark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White);
        var text = new SolidColorBrush(isDark ? Colors.White : Colors.Black);
        return new RegionEditorPalette(surface, text, engine.CreatePaletteColors(isDark));
    }

    private static Color? ColorOf(HighlightingColor? color)
    {
        var brush = color?.Foreground;
        if (brush is null)
            return null;
        return brush.GetBrush(null!) is SolidColorBrush solid ? solid.Color : brush.GetColor(null!);
    }

    private static void EnsureApplicationResources()
    {
        var app = Application.Current ?? new Application();
        app.Resources["FontSizeNormal"] = 14.0;
    }
}
