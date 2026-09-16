using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class CodeHealthTaskEditorPreviewTests {
    private const string Example = "- AND the message is older than {{minAgeDays}} days";
    private readonly List<(int CharStart, int CharLength)> _blocks = new();
    private readonly List<(Run Run, int CharStart, int CharLength)> _inlines = new();

    private FlowDocument Build(string source, string value = "7") {
        _blocks.Clear();
        _inlines.Clear();
        return CodeHealthTaskEditorWindow.BuildMarkdownPreview(source,
            new Dictionary<string, string> { ["minAgeDays"] = value, ["enabled"] = "true" },
            Brushes.DarkBlue, Brushes.LightBlue, _blocks, _inlines);
    }

    private static string Text(TextElement element) =>
        new TextRange(element.ContentStart, element.ContentEnd).Text.TrimEnd('\r', '\n').TrimStart('\u2022', '\t');

    [Test]
    public void InlineVariable_PreservesSingleBulletedParagraphAndBothSourceMaps() {
        var document = Build(Example);
        var list = (List)document.Blocks.Single();
        var paragraph = (Paragraph)list.ListItems.Single().Blocks.Single();
        var (run, start, length) = _inlines.Single();

        Assert.Multiple(() => {
            Assert.That(list.MarkerStyle, Is.EqualTo(TextMarkerStyle.Disc));
            Assert.That(Text(paragraph), Is.EqualTo("AND the message is older than 7 days"));
            Assert.That(run.Text, Is.EqualTo("7"));
            Assert.That(run.FontFamily.Source, Is.EqualTo("Consolas"));
            Assert.That(_blocks, Is.EqualTo(new[] { (0, Example.Length) }));
            Assert.That(start, Is.EqualTo(Example.IndexOf("{{", StringComparison.Ordinal)));
            Assert.That(length, Is.EqualTo("{{minAgeDays}}".Length));
            Assert.That(document.Blocks.Count, Is.EqualTo(_blocks.Count));
        });
    }

    [TestCase("# Age {{minAgeDays}} days", "Age 7 days")]
    [TestCase("> Age {{minAgeDays}} days", "Age 7 days")]
    [TestCase("**Age {{minAgeDays}} days**", "Age 7 days")]
    [TestCase("`Age {{minAgeDays}} days`", "Age 7 days")]
    [TestCase("{{minAgeDays}} days", "7 days")]
    [TestCase("Age {{minAgeDays}}", "Age 7")]
    public void InlineVariable_PreservesSurroundingBlock(string source, string expected) {
        var document = Build(source);
        var paragraph = (Paragraph)document.Blocks.Single();
        Assert.That(Text(paragraph), Is.EqualTo(expected));
        Assert.That(_inlines, Has.Count.EqualTo(1));
        if (source.StartsWith('#'))
            Assert.That(_inlines[0].Run.FontSize, Is.GreaterThan(document.FontSize));
        if (source.StartsWith("**", StringComparison.Ordinal))
            Assert.That(_inlines[0].Run.FontWeight, Is.EqualTo(FontWeights.Bold));
    }

    [TestCase("`**7**` | {{unknown}}")]
    [TestCase("")]
    [TestCase("   ")]
    public void InlineVariable_ValueIsLiteralRatherThanMarkdown(string value) {
        var document = Build(Example, value);
        Assert.That(Text(document.Blocks.Single()), Does.Contain("older than " + value + " days"));
        Assert.That(_inlines.Single().Run.Text, Is.EqualTo(value));
    }

    [Test]
    public void InlineVariables_PreserveListSiblingsContinuationAndRepeatedTokenOffsets() {
        var source = "- First {{minAgeDays}} days\n  continued\n- Second {{minAgeDays}} days";
        var document = Build(source);
        var list = (List)document.Blocks.Single();
        Assert.Multiple(() => {
            Assert.That(list.ListItems.Count, Is.EqualTo(2));
            Assert.That(Text(list.ListItems.First()), Is.EqualTo("First 7 days continued"));
            Assert.That(Text(list.ListItems.Last()), Is.EqualTo("Second 7 days"));
            Assert.That(_blocks.Single(), Is.EqualTo((0, source.Length)));
            Assert.That(_inlines.Select(range => range.CharStart),
                Is.EqualTo(new[] { source.IndexOf("{{", StringComparison.Ordinal), source.LastIndexOf("{{", StringComparison.Ordinal) }));
        });
    }

    [Test]
    public void InlineVariables_AdjacentTokensAndMultilineParagraphKeepOneBlock() {
        var source = "Age {{minAgeDays}}{{minAgeDays}} days\nand more text";
        var document = Build(source);
        Assert.That(Text(document.Blocks.Single()), Is.EqualTo("Age 77 days and more text"));
        Assert.That(_inlines, Has.Count.EqualTo(2));
        Assert.That(_blocks.Single(), Is.EqualTo((0, source.Length)));
    }

    [Test]
    public void InlineVariable_AfterLeadingBlankLinesKeepsSourceOffsets() {
        var source = "\n\n" + Example;
        var document = Build(source);
        var list = document.Blocks.OfType<List>().Single();
        Assert.That(_blocks[document.Blocks.ToList().IndexOf(list)], Is.EqualTo((2, Example.Length)));
        Assert.That(_inlines.Single().CharStart, Is.EqualTo(source.IndexOf("{{", StringComparison.Ordinal)));
    }

    [Test]
    public void InlineVariable_DoesNotChangeNeighboringHeadingsOrCodeBlocks() {
        var document = Build("# Heading\n\n" + Example + "\n\n```\nliteral code\n```");
        Assert.Multiple(() => {
            Assert.That(document.Blocks.OfType<List>().Count(), Is.EqualTo(1));
            Assert.That(Text(document.Blocks.First()), Is.EqualTo("Heading"));
            Assert.That(Text(document.Blocks.Last()), Is.EqualTo("literal code"));
            Assert.That(document.Blocks.Last().FontFamily.Source, Is.EqualTo("Consolas"));
            Assert.That(_blocks.Count, Is.EqualTo(document.Blocks.Count));
        });
    }

    [Test]
    public void UnknownVariable_RemainsLiteral() {
        var document = Build("{{unknown}}");
        Assert.That(Text(document.Blocks.Single()), Is.EqualTo("{{unknown}}"));
        Assert.That(_inlines, Is.Empty);
    }

    [Test]
    public void StandaloneVariable_RetainsSeparateHighlightedBlockAndTokenMap() {
        const string source = "Before\n{{minAgeDays}}\nAfter";
        var document = Build(source);
        Assert.That(_inlines, Is.Empty);
        var substitution = document.Blocks.Single(block => Text(block) == "7");
        var index = document.Blocks.ToList().IndexOf(substitution);
        Assert.Multiple(() => {
            Assert.That(substitution.Foreground, Is.EqualTo(Brushes.DarkBlue));
            Assert.That(substitution.Background, Is.EqualTo(Brushes.LightBlue));
            Assert.That(_blocks[index], Is.EqualTo((7, "{{minAgeDays}}".Length)));
            Assert.That(document.Blocks.Count, Is.EqualTo(_blocks.Count));
        });
    }

    [Test]
    public void MultilineValue_RetainsBlockRendering() {
        var document = Build(Example, "7\n\n8");
        Assert.That(_inlines, Is.Empty);
        Assert.That(document.Blocks.Count, Is.GreaterThan(1));
        Assert.That(_blocks.Count, Is.EqualTo(document.Blocks.Count));
    }

    [Test]
    public void CodeFence_DoesNotUseInlineSubstitutionPath() {
        var document = Build("```\nAge {{minAgeDays}} days\n```");
        Assert.That(_inlines, Is.Empty);
        Assert.That(document.Blocks.First().FontFamily.Source, Is.EqualTo("Consolas"));
        Assert.That(_blocks.Count, Is.EqualTo(document.Blocks.Count));
    }

    [Test]
    public void ConditionalVariable_KeepsSurroundingHighlightAndExactSourceOffsets() {
        var source = "Intro\n\n{{#if enabled == \"true\"}}\n" + Example + "\n{{/if}}\n\nTail";
        var document = Build(source);
        var list = document.Blocks.OfType<List>().Single();
        var paragraph = (Paragraph)list.ListItems.Single().Blocks.Single();
        var (run, start, length) = _inlines.Single();
        Assert.Multiple(() => {
            Assert.That(paragraph.Foreground, Is.EqualTo(Brushes.DarkBlue));
            Assert.That(paragraph.Background, Is.EqualTo(Brushes.LightBlue));
            Assert.That(DependencyPropertyHelper.GetValueSource(run, TextElement.ForegroundProperty).IsExpression, Is.True);
            Assert.That(DependencyPropertyHelper.GetValueSource(run, TextElement.BackgroundProperty).IsExpression, Is.True);
            Assert.That(source.Substring(start, length), Is.EqualTo("{{minAgeDays}}"));
            Assert.That(_blocks[document.Blocks.ToList().IndexOf(list)],
                Is.EqualTo((source.IndexOf(Example, StringComparison.Ordinal), Example.Length)));
            Assert.That(_blocks.Last(), Is.EqualTo((source.IndexOf("Tail", StringComparison.Ordinal), 4)));
        });
    }

    [Test]
    public void InlineVariable_CodeBrushesFollowThemeResources() {
        var document = Build(Example);
        var run = _inlines.Single().Run;
        foreach (var (surface, text) in new[] {
            (Color.FromRgb(0x25, 0x20, 0x18), Color.FromRgb(0xD8, 0xCC, 0xBA)),
            (Color.FromRgb(0xF6, 0xF1, 0xE9), Color.FromRgb(0x2A, 0x1E, 0x12)),
        }) {
            document.Resources["CodeSurface"] = new SolidColorBrush(surface);
            document.Resources["CodeText"] = new SolidColorBrush(text);
            Assert.That(((SolidColorBrush)run.Background).Color, Is.EqualTo(surface));
            Assert.That(((SolidColorBrush)run.Foreground).Color, Is.EqualTo(text));
        }
    }

    [Test]
    public void InlineVariable_LayoutKeepsSentenceOnOneLineAndSupportsTokenHitTesting() {
        var document = Build(Example);
        var viewer = new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false };
        viewer.Measure(new Size(800, 300));
        viewer.Arrange(new Rect(0, 0, 800, 300));
        viewer.UpdateLayout();
        var paragraph = (Paragraph)((List)document.Blocks.Single()).ListItems.Single().Blocks.Single();
        var run = _inlines.Single().Run;
        var first = paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        var tokenStart = run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        var tokenEnd = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
        var last = paragraph.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
        var tokenBounds = Rect.Union(tokenStart, tokenEnd);

        Assert.Multiple(() => {
            Assert.That(CodeHealthTaskEditorWindow.FindPreviewDocumentVisual(viewer), Is.Not.Null);
            Assert.That(first, Is.Not.EqualTo(Rect.Empty));
            Assert.That(tokenStart.Top, Is.LessThan(first.Bottom));
            Assert.That(tokenStart.Bottom, Is.GreaterThan(first.Top));
            Assert.That(last.Top, Is.EqualTo(first.Top));
            Assert.That(CodeHealthTaskEditorWindow.FindInlineAtPoint(new[] { run },
                new Point(tokenBounds.Left + tokenBounds.Width / 2, tokenBounds.Top + tokenBounds.Height / 2)),
                Is.SameAs(run));
            Assert.That(CodeHealthTaskEditorWindow.FindInlineAtPoint(new[] { run },
                new Point(first.Left, first.Top + first.Height / 2)), Is.Null);
        });
    }
}
