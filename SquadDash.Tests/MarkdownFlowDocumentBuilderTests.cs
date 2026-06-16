using System.Threading;
using System.Windows;
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
}
