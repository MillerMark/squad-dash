using System.Threading;
using System.Windows;
using System.Windows.Documents;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class PlanCompletionSummaryFormatterTests
{
    [Test]
    public void CreateParagraph_LinksExampleReferencesAndPreservesAllText()
    {
        const string text = "Added CandidateSceneEvaluator.Evaluate(...) integrating the Step 4 expression AST, " +
            "the Step 2 Quantity system, and the Step 3 domain over a validated Step 5 document. " +
            "(regression-proven with the Step 3 normative examples).";
        var labels = new Dictionary<string, string>
        {
            ["expression-catalog"] = "4",
            ["quantities"] = "2",
            ["component-domain"] = "3",
            ["document"] = "5",
        };

        var paragraph = Format(text, labels);
        var links = paragraph.Inlines.OfType<Hyperlink>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(links.Select(link => link.Tag),
                Is.EqualTo(new[] { "expression-catalog", "quantities", "component-domain", "document", "component-domain" }));
            Assert.That(links.Select(LinkText), Is.EqualTo(new[] { "Step 4", "Step 2", "Step 3", "Step 5", "Step 3" }));
            Assert.That(ParagraphText(paragraph), Is.EqualTo(text));
        });
    }

    [Test]
    public void CreateParagraph_UsesExactDisplayedLabelsNotIdSuffixesOrDictionaryOrder()
    {
        var paragraph = Format("step 10, STEP 3 and Step 1.", new Dictionary<string, string>
        {
            ["PLAN-003"] = "10",
            ["PLAN-001"] = "3",
            ["PLAN-010"] = "1",
        });
        var links = paragraph.Inlines.OfType<Hyperlink>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(links.Select(link => link.Tag), Is.EqualTo(new[] { "PLAN-003", "PLAN-001", "PLAN-010" }));
            Assert.That(links.Select(LinkText), Is.EqualTo(new[] { "step 10", "STEP 3", "Step 1" }));
        });
    }

    [Test]
    public void CreateParagraph_LeavesAmbiguousAndUnknownReferencesPlain()
    {
        const string text = "Step 3 then Step 4, Step 999, and Step 3 again.";
        var paragraph = Format(text, new Dictionary<string, string>
        {
            ["first"] = "3",
            ["second"] = "3",
            ["unique"] = "4",
        });

        Assert.Multiple(() =>
        {
            Assert.That(paragraph.Inlines.OfType<Hyperlink>().Select(link => link.Tag), Is.EqualTo(new[] { "unique" }));
            Assert.That(paragraph.Inlines.OfType<Run>().Select(run => run.Text),
                Is.EqualTo(new[] { "Step 3 then ", ", Step 999, and Step 3 again." }));
            Assert.That(ParagraphText(paragraph), Is.EqualTo(text));
        });
    }

    [TestCase("Substep 3")]
    [TestCase("misstep 3")]
    [TestCase("Step 3a")]
    [TestCase("Step 3_thing")]
    [TestCase("Step 3.1")]
    [TestCase("Step 30")]
    [TestCase("Step 03")]
    [TestCase("Step3")]
    [TestCase("Step 999999999999999999999")]
    [TestCase("")]
    public void CreateParagraph_DoesNotLinkPartialOrUnknownNumbers(string text)
    {
        var paragraph = Format(text, new Dictionary<string, string> { ["task"] = "3" });

        Assert.Multiple(() =>
        {
            Assert.That(paragraph.Inlines.OfType<Hyperlink>(), Is.Empty);
            Assert.That(ParagraphText(paragraph), Is.EqualTo(text));
        });
    }

    [Test]
    public void CreateParagraph_WithoutDisplayedTasksLeavesTextPlain()
    {
        const string text = "Step 3 uses Step 10.";
        var paragraph = Format(text, new Dictionary<string, string>());
        Assert.That(paragraph.Inlines.Cast<Inline>().Single(), Is.TypeOf<Run>());
        Assert.That(ParagraphText(paragraph), Is.EqualTo(text));
    }

    [Test]
    public void CreateParagraph_PreservesWhitespaceAndInheritsEnvironmentFontSize()
    {
        const string text = "(Step\t3), Step 3.\nstep  3";
        var clicked = new List<string>();
        var paragraph = PlanCompletionSummaryFormatter.CreateParagraph(
            text,
            new Dictionary<string, string> { ["task"] = "3" },
            (label, taskId) =>
            {
                var link = new Hyperlink(new Run(label));
                link.Click += (_, _) => clicked.Add(taskId);
                return link;
            });
        var document = new FlowDocument(paragraph) { FontSize = 21 };
        var links = paragraph.Inlines.OfType<Hyperlink>().ToArray();
        foreach (var link in links)
            link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));

        Assert.Multiple(() =>
        {
            Assert.That(ParagraphText(paragraph), Is.EqualTo(text));
            Assert.That(links, Has.Length.EqualTo(3));
            Assert.That(clicked, Is.EqualTo(new[] { "task", "task", "task" }));
            Assert.That(links.Select(link => link.FontSize), Is.All.EqualTo(document.FontSize));
            Assert.That(links.Select(link => link.ReadLocalValue(TextElement.FontSizeProperty)),
                Is.All.EqualTo(DependencyProperty.UnsetValue));
        });
    }

    private static Paragraph Format(string text, IReadOnlyDictionary<string, string> labels) =>
        PlanCompletionSummaryFormatter.CreateParagraph(
            text, labels, (label, taskId) => new Hyperlink(new Run(label)) { Tag = taskId });

    private static string LinkText(Hyperlink link) => link.Inlines.OfType<Run>().Single().Text;

    private static string ParagraphText(Paragraph paragraph) =>
        string.Concat(paragraph.Inlines.Select(inline => inline is Hyperlink link ? LinkText(link) : ((Run)inline).Text));
}
