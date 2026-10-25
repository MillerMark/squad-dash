using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ICSharpCode.AvalonEdit;

namespace SquadDash.Tests;

public sealed partial class RegionHighlightingTests
{
    private const string RazorComponent = "@page \"/counter\"\n<h1>@Title</h1>\n@code { private int count; }";

    [TestCase("razor")]
    [TestCase("RAZOR")]
    [TestCase("blazor")]
    [TestCase("BLAZOR")]
    [TestCase("cshtml")]
    [TestCase("CSHTML")]
    [TestCase(".razor")]
    [TestCase(".RAZOR")]
    [TestCase(".cshtml")]
    [TestCase(".CSHTML")]
    public void Razor_Aliases_LoadCanonicalGrammarAcrossSurfaces(string token)
    {
        var engine = new RegionHighlightingEngine();
        Assert.That(engine.Resolve(token, "using System;"), Is.EqualTo("Razor"));
        var option = engine.Options.Single(o => o.GrammarName == "Razor");
        Assert.That(option, Is.EqualTo(new RegionLanguageOption("razor", "Razor (Blazor)", "Razor")));
        Assert.That(RegionLanguagePicker.GetLabel("razor"), Is.EqualTo("Razor (Blazor)"));
        Assert.That(TranscriptCodeBlockPreview.ResolveGrammar(token, RazorComponent, engine), Is.EqualTo("Razor"));
        var attachment = AttachmentTextClassifier.Classify($"```{token}\n{RazorComponent}\n```", engine);
        Assert.That(attachment, Is.EqualTo(new AttachmentTextClassification(
            AttachmentTextRenderMode.WholeCode, RazorComponent, "Razor", "Razor")));

        var host = new RichTextBox();
        var serialized = $"before\n```{token}\n{RazorComponent}\n```\nafter";
        host.SetPlainText(serialized);
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(host), Is.True);
        var region = RichTextBoxExtensions.EnumerateEmbeddedRegions(host).Single();
        Assert.That(region.Language, Is.EqualTo(token), "aliases must remain lossless on serialization");
        Assert.That(host.GetPlainText(), Is.EqualTo(serialized));
        foreach (var dark in new[] { false, true })
        {
            region.NotifyThemeChanged(PaletteFrom(engine, dark));
            var editor = LogicalDescendants<TextEditor>(region).Single();
            Assert.That(editor.SyntaxHighlighting.Name, Is.EqualTo("Razor"));
            Assert.That(HighlightAll(editor.Document, editor.SyntaxHighlighting), Is.Not.Empty);
        }
    }

    [TestCase(".razor.cs")]
    [TestCase("razor.cs")]
    [TestCase(".cshtml.cs")]
    [TestCase("cshtml.cs")]
    [TestCase(".RAZOR.CS")]
    public void Razor_CodeBehindAliases_RemainCSharp(string token)
    {
        var engine = new RegionHighlightingEngine();
        Assert.That(engine.Resolve(token, RazorComponent), Is.EqualTo("C#"));
        Assert.That(TranscriptCodeBlockPreview.ResolveGrammar(token, RazorComponent), Is.EqualTo("C#"));
        Assert.That(AttachmentTextClassifier.Classify($"```{token}\n{RazorComponent}\n```").Grammar, Is.EqualTo("C#"));
        using var editor = new AvalonEditRegionEditor(RazorComponent, engine);
        editor.SetLanguage(token);
        Assert.That(((TextEditor)editor.Control).SyntaxHighlighting.Name, Is.EqualTo("C#"));
    }

    [TestCase("@page \"/counter\"")]
    [TestCase("@code { private int count; }")]
    [TestCase("@functions\n{ public void Run() {} }")]
    [TestCase("@inject IService Service")]
    [TestCase("@using System.Text")]
    [TestCase("@model Demo.Model")]
    [TestCase("@inherits ComponentBase")]
    [TestCase("@implements IDisposable")]
    [TestCase("@namespace Demo.Pages")]
    [TestCase("@layout MainLayout")]
    [TestCase("@typeparam TItem")]
    [TestCase("@rendermode InteractiveServer")]
    [TestCase("<input @bind=\"value\">")]
    [TestCase("<Widget @bind-Value:after=\"Changed\" />")]
    [TestCase("<button @onclick:preventDefault=\"true\">Go</button>")]
    [TestCase("<Widget @ref=\"component\" @key=\"id\" />")]
    [TestCase("<Widget @attributes=\"attributes\" />")]
    [TestCase("<Widget @rendermode=\"InteractiveServer\" />")]
    [TestCase("<!-- intro -->\n@page \"/counter\"\n<div>Counter</div>")]
    public void Razor_AutoDetect_RequiresStructuralSignals(string content)
    {
        Assert.That(RegionRazorDetector.IsRazor(content), Is.True);
        Assert.That(new RegionHighlightingEngine().Resolve("", content), Is.EqualTo("Razor"));
        Assert.That(RegionGrammarResolver.Detect(content, _ => false), Is.Null,
            "detection must not return an unavailable grammar");
    }

    [TestCase("Contact person@example.com", null)]
    [TestCase("@Name", null)]
    [TestCase("@(value)", null)]
    [TestCase("@@page \"/counter\"", null)]
    [TestCase("@@code { private int count; }", null)]
    [TestCase("@page { size: A4; }", "CSS")]
    [TestCase("@media screen { p { color: red; } }", "CSS")]
    [TestCase("<div>ordinary HTML</div>", "HTML")]
    [TestCase("<p>person@example.com</p>", "HTML")]
    [TestCase("<p>@@code { }</p>", "HTML")]
    [TestCase("<input @@bind=\"value\">", "HTML")]
    [TestCase("<input data-bind=\"value\">", "HTML")]
    [TestCase("<div title='@bind=\"value\"'>text</div>", "HTML")]
    [TestCase("<div data-note=\"@page '/counter'\">text</div>", "HTML")]
    [TestCase("using System;\npublic class C { }", "C#")]
    [TestCase("var @code = 1;", null)]
    [TestCase("/*\n@code { }\n*/", null)]
    [TestCase("@* @page \"/counter\" *@", null)]
    [TestCase("var s = \"@code { }\";", null)]
    public void Razor_AutoDetect_NegativesDoNotStealExistingLanguages(string content, string? expected)
    {
        Assert.That(RegionRazorDetector.IsRazor(content), Is.False, content);
        Assert.That(new RegionHighlightingEngine().Resolve("", content), Is.EqualTo(expected), content);
    }

    [TestCase("<!--\n@page \"/hidden\"\n-->")]
    [TestCase("var text = @\"\n@code { }\n\";")]
    [TestCase("var text = \"\"\"\n@page \"/hidden\"\n\"\"\";")]
    [TestCase("<script>\n@code { }\n</script>")]
    [TestCase("<style>\n@page { size: A4; }\n</style>")]
    [TestCase("@@using System.Text")]
    public void Razor_AutoDetect_IgnoresSignalsInOpaqueRegions(string content)
    {
        Assert.That(RegionRazorDetector.IsRazor(content), Is.False);
        Assert.That(new RegionHighlightingEngine().Resolve("", content), Is.Not.EqualTo("Razor"));
    }

    [TestCase("text", null)]
    [TestCase("TEXT", null)]
    [TestCase("html", "HTML")]
    [TestCase("cs", "C#")]
    [TestCase("unsupported", null)]
    [TestCase("blazor", "Razor")]
    public void Razor_ExplicitFenceBypassesDetection(string token, string? expected)
    {
        var engine = new RegionHighlightingEngine();
        var result = RegionGrammarResolver.Resolve(token, RazorComponent, engine.ResolveFenceToken,
            _ => throw new AssertionException("Pinned languages must never auto-detect."));
        Assert.That(result, Is.EqualTo(expected));
        using var editor = new AvalonEditRegionEditor(RazorComponent, engine);
        editor.SetLanguage(token);
        Assert.That(((TextEditor)editor.Control).SyntaxHighlighting?.Name, Is.EqualTo(expected));
        editor.SetLanguage("");
        Assert.That(((TextEditor)editor.Control).SyntaxHighlighting?.Name, Is.EqualTo("Razor"));
    }

    [Test]
    public void Razor_DetectionBudget_IncludesOnlyLeading8192Characters()
    {
        var engine = new RegionHighlightingEngine();
        const string marker = "@code {";
        var atBoundary = new string(' ', RegionGrammarResolver.MaxScanLength - marker.Length) + marker;
        Assert.That(engine.Resolve("", atBoundary), Is.EqualTo("Razor"));
        Assert.That(engine.Resolve("", new string(' ', RegionGrammarResolver.MaxScanLength) + marker), Is.Null);
        Assert.That(engine.Resolve("", new string(' ', RegionGrammarResolver.MaxScanLength - marker.Length + 1) + marker), Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Razor_AttachmentRawAndUnlabeledFence_LoadReadOnlyGrammar(bool dark)
    {
        var engine = new RegionHighlightingEngine();
        foreach (var text in new[] { RazorComponent, $"```\r\n{RazorComponent.Replace("\n", "\r\n")}\r\n```\r\n" })
        {
            var classification = AttachmentTextClassifier.Classify(text, engine);
            Assert.That(classification, Is.EqualTo(new AttachmentTextClassification(
                AttachmentTextRenderMode.WholeCode, RazorComponent, "Razor", "Razor")));
            var host = ReadOnlyHighlightedEditorFactory.Build(classification.Content, classification.Grammar,
                classification.LanguageLabel!, 14);
            var editor = host.Children.OfType<TextEditor>().Single();
            ReadOnlyHighlightedEditorFactory.ApplyTheme(editor.Dispatcher, PaletteFrom(engine, dark));
            Assert.That(editor.IsReadOnly, Is.True);
            Assert.That(editor.Text, Is.EqualTo(RazorComponent));
            Assert.That(editor.SyntaxHighlighting.Name, Is.EqualTo("Razor"));
            AssertRazorToken(editor.Text, new("@page", "Keyword"),
                HighlightAll(editor.Document, editor.SyntaxHighlighting), engine, dark);
        }
    }

    [Test]
    public void Razor_MixedMarkdown_PreservesProseAliasesAndUnlabeledTranscriptPolicy()
    {
        var text = $"Before\n```blazor\n{RazorComponent}\n```\nBetween\n```\n{RazorComponent}\n```\nAfter";
        var classification = AttachmentTextClassifier.Classify(text);
        Assert.That(classification, Is.EqualTo(new AttachmentTextClassification(
            AttachmentTextRenderMode.MixedMarkdown, text, null, null)));
        var document = MarkdownDocumentRenderer.BuildReadOnlyDocument(text, 14);
        var previews = document.Blocks.OfType<BlockUIContainer>()
            .Select(block => (TranscriptCodeBlockPreview)block.Tag).ToArray();
        Assert.That(previews, Has.Length.EqualTo(2));
        foreach (var preview in previews)
            typeof(TranscriptCodeBlockPreview).GetMethod("Upgrade", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(preview, null);
        Assert.That(previews[0].Surface, Is.TypeOf<TextEditor>());
        Assert.That(((TextEditor)previews[0].Surface).SyntaxHighlighting.Name, Is.EqualTo("Razor"));
        Assert.That(previews[1].Surface, Is.TypeOf<TextBox>(), "unlabeled mixed/transcript fences must not auto-detect");
        Assert.That(previews.Select(p => p.GetCopyText()), Is.EqualTo(new[] { RazorComponent, RazorComponent }));
        Assert.That(MarkdownVisibleContent.GetText(document), Does.Contain("Before").And.Contain("Between").And.Contain("After"));
    }

    [Test]
    public void Razor_PromptPicker_CanonicalPlainAndAutoPersistThroughFenceRoundTrip()
    {
        var host = new RichTextBox();
        host.SetPlainText($"before\n```cshtml\n{RazorComponent}\n```\nafter");
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(host), Is.True);
        var region = RichTextBoxExtensions.EnumerateEmbeddedRegions(host).Single();
        foreach (var (token, grammar) in new (string, string?)[] { ("razor", "Razor"), ("text", null), ("", "Razor") })
        {
            var button = LogicalDescendants<Button>(region.LanguagePickerPopup.Child)
                .Single(b => b.Tag is RegionLanguageOption option && option.FenceToken == token);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(region.Language, Is.EqualTo(token));
            Assert.That(region.PickerMode, Is.EqualTo(RegionLanguagePicker.GetMode(token)));
            Assert.That(LogicalDescendants<TextEditor>(region).Single().SyntaxHighlighting?.Name, Is.EqualTo(grammar));
            var serialized = $"before\n```{token}\n{RazorComponent}\n```\nafter";
            Assert.That(host.GetPlainText(), Is.EqualTo(serialized));
            var restored = new RichTextBox();
            restored.SetPlainText(serialized);
            Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(restored), Is.True);
            var roundTrip = RichTextBoxExtensions.EnumerateEmbeddedRegions(restored).Single();
            Assert.That(roundTrip.Language, Is.EqualTo(token));
            Assert.That(roundTrip.Content, Is.EqualTo(RazorComponent));
            Assert.That(LogicalDescendants<TextEditor>(roundTrip).Single().SyntaxHighlighting?.Name, Is.EqualTo(grammar));
            Assert.That(restored.GetPlainText(), Is.EqualTo(serialized));
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("text")]
    [TestCase("unknown")]
    public void Razor_TranscriptUnlabeledPlainAndUnknown_RemainPlain(string? token)
    {
        Assert.That(TranscriptCodeBlockPreview.ResolveGrammar(token, RazorComponent), Is.Null);
        var block = (BlockUIContainer)CopyableCodeBlockBuilder.Build(RazorComponent, 14, transcript: true, language: token);
        var preview = (TranscriptCodeBlockPreview)block.Tag;
        typeof(TranscriptCodeBlockPreview).GetMethod("Upgrade", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(preview, null);
        Assert.That(preview.Surface, Is.TypeOf<TextBox>());
        Assert.That(preview.GetCopyText(), Is.EqualTo(RazorComponent));
    }

    [TestCase("\n")]
    [TestCase("\r\n")]
    [TestCase("\r")]
    public void Razor_AttachmentAndTranscriptLineBudgets_AreInclusiveAndLossless(string newline)
    {
        var atCap = "@code {}" + string.Concat(Enumerable.Repeat(newline, TranscriptCodeBlockPreview.MaximumLines - 1));
        AssertRazorBudget(atCap, atCap + newline);
    }

    [Test]
    public void Razor_AttachmentAndTranscriptCharacterBudgets_AreInclusiveAndLossless()
    {
        var atCap = "@code {}".PadRight(TranscriptCodeBlockPreview.MaximumCharacters, ' ');
        AssertRazorBudget(atCap, atCap + "x");
    }

    private static void AssertRazorBudget(string atCap, string overCap)
    {
        Assert.That(TranscriptCodeBlockPreview.ResolveGrammar("razor", atCap), Is.EqualTo("Razor"));
        Assert.That(TranscriptCodeBlockPreview.ResolveGrammar("razor", overCap), Is.Null);
        Assert.That(AttachmentTextClassifier.Classify(atCap).Grammar, Is.EqualTo("Razor"));
        Assert.That(AttachmentTextClassifier.Classify(overCap), Is.EqualTo(new AttachmentTextClassification(
            AttachmentTextRenderMode.PlainText, overCap, null, null)));
        var block = (BlockUIContainer)CopyableCodeBlockBuilder.Build(overCap, 14, transcript: true, language: "razor");
        var preview = (TranscriptCodeBlockPreview)block.Tag;
        Assert.That(preview.Surface, Is.TypeOf<TextBox>());
        Assert.That(preview.GetCopyText(), Is.EqualTo(overCap));
    }
}
