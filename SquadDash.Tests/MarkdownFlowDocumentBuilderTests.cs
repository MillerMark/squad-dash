using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class MarkdownFlowDocumentBuilderTests {

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static FlowDocument Build(string markdown) =>
        MarkdownFlowDocumentBuilder.Build(markdown, baseFontSize: 13.0);

    private static List GetOrderedList(FlowDocument doc, int index = 0) {
        var lists = doc.Blocks.OfType<List>()
            .Where(l => l.MarkerStyle == TextMarkerStyle.Decimal)
            .ToList();
        Assert.That(lists, Has.Count.GreaterThan(index),
            $"Expected at least {index + 1} ordered list(s) in document");
        return lists[index];
    }

    private static int CountOrderedLists(FlowDocument doc) =>
        doc.Blocks.OfType<List>().Count(l => l.MarkerStyle == TextMarkerStyle.Decimal);

    private static string GetListItemText(List list, int itemIndex) {
        var item = list.ListItems.ElementAt(itemIndex);
        var para = item.Blocks.OfType<Paragraph>().First();
        return string.Concat(para.Inlines.OfType<Run>().Select(r => r.Text));
    }

    // ── Tight ordered list (no blank lines) ─────────────────────────────────

    [Test]
    public void TightOrderedList_ThreeItems_AllInOneList() {
        var doc = Build("1. Alpha\n2. Beta\n3. Gamma");

        Assert.That(CountOrderedLists(doc), Is.EqualTo(1));
        var list = GetOrderedList(doc);
        Assert.That(list.ListItems.Count, Is.EqualTo(3));
    }

    [Test]
    public void TightOrderedList_NumbersAreSequential() {
        var doc  = Build("1. First\n2. Second\n3. Third");
        var list = GetOrderedList(doc);

        Assert.That(GetListItemText(list, 0), Does.Contain("First"));
        Assert.That(GetListItemText(list, 1), Does.Contain("Second"));
        Assert.That(GetListItemText(list, 2), Does.Contain("Third"));
    }

    // ── Loose ordered list (blank lines between items) ───────────────────────

    [Test]
    public void LooseOrderedList_BlankLineBetweenTwoItems_SingleList() {
        var doc = Build("1. Alpha\n\n2. Beta");

        Assert.That(CountOrderedLists(doc), Is.EqualTo(1),
            "Blank line between ordered items should not split into separate lists");
        var list = GetOrderedList(doc);
        Assert.That(list.ListItems.Count, Is.EqualTo(2));
    }

    [Test]
    public void LooseOrderedList_BlankLineBetweenItems_TextPreserved() {
        var doc  = Build("1. First item\n\n2. Second item");
        var list = GetOrderedList(doc);

        Assert.That(GetListItemText(list, 0), Does.Contain("First item"));
        Assert.That(GetListItemText(list, 1), Does.Contain("Second item"));
    }

    [Test]
    public void LooseOrderedList_ThreeItemsWithBlanks_AllInOneList() {
        var doc = Build("1. One\n\n2. Two\n\n3. Three");

        Assert.That(CountOrderedLists(doc), Is.EqualTo(1));
        var list = GetOrderedList(doc);
        Assert.That(list.ListItems.Count, Is.EqualTo(3));
    }

    [Test]
    public void LooseOrderedList_MultiParagraphItem_ContentPreserved() {
        // The clipboard example: items have multi-line body paragraphs
        var markdown =
            "1. **Silent error suppression** — catch blocks that swallow failures.\n" +
            "\n" +
            "2. **Catch-and-return-sentinel** — functions that catch internally.";

        var doc = Build(markdown);

        Assert.That(CountOrderedLists(doc), Is.EqualTo(1));
        Assert.That(GetOrderedList(doc).ListItems.Count, Is.EqualTo(2));
    }

    [Test]
    public void LooseOrderedList_TrailingBlankAfterLastItem_DoesNotCreateExtraList() {
        var doc = Build("1. Only\n\n2. Two\n\n");

        Assert.That(CountOrderedLists(doc), Is.EqualTo(1));
    }

    // ── Regression: blank line followed by non-list content breaks list ──────

    [Test]
    public void OrderedList_BlankThenParagraph_ListEndsCorrectly() {
        var doc = Build("1. Item one\n\nThis is a paragraph.");

        // Only one ordered list, with one item — blank + paragraph terminates the list
        Assert.That(CountOrderedLists(doc), Is.EqualTo(1));
        Assert.That(GetOrderedList(doc).ListItems.Count, Is.EqualTo(1));

        // The paragraph should also appear in the document
        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        Assert.That(paragraphs.Any(p =>
            string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text))
                  .Contains("This is a paragraph")),
            Is.True, "Paragraph after list should still render");
    }

    [Test]
    public void BuildInbox_StylesCamelCaseAndPascalCaseFileAsInlineCode() {
        var doc = MarkdownFlowDocumentBuilder.BuildInbox(
            "Open PlanViewerWindow.cs and call selectElement while ordinary words remain prose.",
            baseFontSize: 13.0);
        var runs = doc.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines.OfType<Run>())
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(runs.Single(run => run.Text == "PlanViewerWindow.cs").FontFamily.Source,
                Does.Contain("Consolas"));
            Assert.That(runs.Single(run => run.Text == "selectElement").FontFamily.Source,
                Does.Contain("Consolas"));
            Assert.That(runs.Any(run => run.Text.Contains("ordinary") &&
                                        !run.FontFamily.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase)),
                Is.True);
        });
    }

    [Test]
    public void BuildInbox_RespectsExplicitBackticksInHeading() {
        var doc = MarkdownFlowDocumentBuilder.BuildInbox(
            "# Inspect `raw_value` in PlanViewerWindow.cs",
            baseFontSize: 13.0);
        var runs = doc.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines.OfType<Run>())
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(runs.Single(run => run.Text == "raw_value").FontFamily.Source,
                Does.Contain("Consolas"));
            Assert.That(runs.Single(run => run.Text == "PlanViewerWindow.cs").FontFamily.Source,
                Does.Contain("Consolas"));
        });
    }

    [Test]
    public void BuildInbox_FencedCopyReadyTextUsesTranscriptCodeSurface()
    {
        const string prompt = "In this component, make IncrementCount add 2.";
        var doc = MarkdownFlowDocumentBuilder.BuildInbox(
            $"Submit exactly:\n\n```text\n{prompt}\n```",
            baseFontSize: 13.0);

        var container = doc.Blocks.OfType<BlockUIContainer>().Single();
        var grid = (Grid)container.Child;
        var textBox = grid.Children.OfType<TextBox>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(textBox.Text, Is.EqualTo(prompt));
            Assert.That(textBox.Tag, Is.EqualTo("codeblock"));
            Assert.That(textBox.IsReadOnly, Is.True);
            Assert.That(textBox.ContextMenu?.Items.OfType<MenuItem>().Select(item => item.Header),
                Does.Contain("Copy"));
        });
    }

    [Test]
    public void BuildPlanDescription_RendersReadableMarkdownAndMarksHeadingLevels()
    {
        const string markdown =
            "Create the shell in PlanViewerWindow.cs.\n\n" +
            "## Observable outcome\n\n" +
            "The **published app** remains *usable* and calls `RenderScene`.\n\n" +
            "```csharp\nRenderScene();\n```";

        var doc = MarkdownFlowDocumentBuilder.BuildPlanDescription(markdown, baseFontSize: 13.0);
        var heading = doc.Blocks.OfType<Paragraph>()
            .Single(block => MarkdownFlowDocumentBuilder.TryGetHeadingLevel(block, out _));
        var proseRuns = doc.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines.OfType<Run>())
            .ToArray();
        var codeBlock = doc.Blocks.OfType<BlockUIContainer>().Single();
        var codeTextBox = ((Grid)codeBlock.Child).Children.OfType<TextBox>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(MarkdownFlowDocumentBuilder.TryGetHeadingLevel(heading, out var level), Is.True);
            Assert.That(level, Is.EqualTo(2));
            Assert.That(string.Concat(heading.Inlines.OfType<Run>().Select(run => run.Text)),
                Is.EqualTo("Observable outcome"));
            Assert.That(proseRuns.Single(run => run.Text == "published app").FontWeight,
                Is.EqualTo(FontWeights.Bold));
            Assert.That(proseRuns.Single(run => run.Text == "usable").FontStyle,
                Is.EqualTo(FontStyles.Italic));
            Assert.That(proseRuns.Single(run => run.Text == "PlanViewerWindow.cs").FontFamily.Source,
                Does.Contain("Consolas"));
            Assert.That(proseRuns.Single(run => run.Text == "RenderScene").FontFamily.Source,
                Does.Contain("Consolas"));
            Assert.That(codeTextBox.Text, Is.EqualTo("RenderScene();"));
            Assert.That(codeTextBox.Tag, Is.EqualTo("codeblock"));
        });
    }

    [Test]
    public void BuildPlanDescription_LegacyPlainTextRemainsOneReadableParagraph()
    {
        const string description = "Implement the existing behavior without Markdown.";

        var doc = MarkdownFlowDocumentBuilder.BuildPlanDescription(description, baseFontSize: 13.0);
        var paragraph = doc.Blocks.OfType<Paragraph>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(string.Concat(paragraph.Inlines.OfType<Run>().Select(run => run.Text)),
                Is.EqualTo(description));
            Assert.That(MarkdownFlowDocumentBuilder.TryGetHeadingLevel(paragraph, out _), Is.False);
        });
    }

    [Test]
    public void BuildPlanDescription_IncompleteMarkdownRemainsSafeAndVisible()
    {
        const string description = "Keep **unfinished emphasis visible.\n\n```text\npartial code";

        FlowDocument? doc = null;
        Assert.DoesNotThrow(() =>
            doc = MarkdownFlowDocumentBuilder.BuildPlanDescription(description, baseFontSize: 13.0));

        var visibleText = string.Join("\n", doc!.Blocks.Select(block => block switch
        {
            Paragraph paragraph => string.Concat(paragraph.Inlines.OfType<Run>().Select(run => run.Text)),
            BlockUIContainer { Child: Grid grid } => grid.Children.OfType<TextBox>().Single().Text,
            _ => string.Empty,
        }));
        Assert.Multiple(() =>
        {
            Assert.That(visibleText, Does.Contain("Keep **unfinished emphasis visible."));
            Assert.That(visibleText, Does.Contain("partial code"));
        });
    }

    [Test]
    public void BuildInbox_RendersContextualFileOrFolderLinkAndRoutesItsTarget()
    {
        string? clickedTarget = null;
        var doc = MarkdownFlowDocumentBuilder.BuildInbox(
            "2. Open [the sample solution](app://open-path:samples/manual-test/Sample.sln) and continue.",
            baseFontSize: 13.0,
            onLinkClicked: target => clickedTarget = target);
        var link = doc.Blocks.OfType<List>()
            .Single()
            .ListItems.Single()
            .Blocks.OfType<Paragraph>()
            .Single()
            .Inlines.OfType<Hyperlink>()
            .Single();

        Assert.That(string.Concat(link.Inlines.OfType<Run>().Select(run => run.Text)),
            Is.EqualTo("the sample solution"));
        Assert.That(link.Tag, Is.EqualTo("app://open-path:samples/manual-test/Sample.sln"));

        link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));

        Assert.That(clickedTarget, Is.EqualTo("app://open-path:samples/manual-test/Sample.sln"));
    }

    [Test]
    public void AddInboxInlineText_StylesIdentifiersOnTranscriptCards() {
        var paragraph = new Paragraph();

        MarkdownFlowDocumentBuilder.AddInboxInlineText(
            paragraph.Inlines,
            "Inspect ResponseTextBuilder in `MainWindow.xaml.cs`.");

        var runs = paragraph.Inlines.OfType<Run>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(runs.Single(run => run.Text == "ResponseTextBuilder").FontFamily.Source,
                Does.Contain("Consolas"));
            Assert.That(runs.Single(run => run.Text == "MainWindow.xaml.cs").FontFamily.Source,
                Does.Contain("Consolas"));
        });
    }

    [Test]
    public void AddInboxInlineText_StylesProtocolPayloadNameWithoutItalicizingUnderscores()
    {
        var paragraph = new Paragraph();

        MarkdownFlowDocumentBuilder.AddInboxInlineText(
            paragraph.Inlines,
            "The response did not contain a valid DECOMPOSE_STEP_RESULT_JSON payload.");

        var protocolRun = paragraph.Inlines.OfType<Run>()
            .Single(run => run.Text == "DECOMPOSE_STEP_RESULT_JSON");
        Assert.Multiple(() =>
        {
            Assert.That(protocolRun.FontFamily.Source, Does.Contain("Consolas"));
            Assert.That(protocolRun.FontStyle, Is.Not.EqualTo(FontStyles.Italic));
        });
    }

    [Test]
    public void Build_DefaultRenderer_DoesNotAutoStyleIdentifiers() {
        var doc = Build("PlanViewerWindow.cs remains ordinary outside Inbox.");
        var runs = doc.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines.OfType<Run>())
            .ToArray();

        Assert.That(runs, Has.None.Matches<Run>(run =>
            run.FontFamily.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase)));
    }
}
