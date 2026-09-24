using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class PlanGateReviewRendererTests
{
    [TestCase(1)]
    [TestCase(7)]
    public void ReadView_RendersOneRowPerRequirementAndKeepsCountSummarySeparate(int count)
    {
        var requirements = Enumerable.Range(1, count)
            .Select(index => new PlanTaskProofRequirement(
                $"proof-{index}", "human-observation", $"Description {index}",
                $"Did scenario {index} work?", Title: $"Scenario {index}"))
            .ToArray();
        var gate = Gate(requirements) with { Question = $"Review all {count} items." };
        var blocks = PlanGateReviewRenderer.CreateReviewBlocks(gate, [gate.Question], false);
        var list = blocks.OfType<System.Windows.Documents.List>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(list.ListItems.Count, Is.EqualTo(count));
            Assert.That(list.MarkerStyle, Is.EqualTo(TextMarkerStyle.Disc));
            Assert.That(Text(blocks.OfType<Paragraph>().Single()), Is.EqualTo(gate.Question));
            for (var index = 0; index < count; index++)
            {
                var paragraph = list.ListItems.ElementAt(index).Blocks.OfType<Paragraph>().Single();
                Assert.That(Text(paragraph), Does.Contain(requirements[index].Title)
                    .And.Contain(requirements[index].Description)
                    .And.Contain(requirements[index].Question));
                Assert.That(paragraph.Inlines.OfType<Run>().First().FontWeight, Is.EqualTo(FontWeights.Bold));
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReadView_NoRequirementsOrQuestionsReturnsNoEmptyList(bool nullRequirements)
    {
        var gate = Gate(nullRequirements ? null : []);
        Assert.That(PlanGateReviewRenderer.CreateReviewBlocks(gate, [], false), Is.Empty);
    }

    [Test]
    public void ReadView_LegacyQuestionStillRendersWithoutProofs()
    {
        var blocks = PlanGateReviewRenderer.CreateReviewBlocks(Gate(null), ["Does the feature work?"], false);
        var list = blocks.OfType<System.Windows.Documents.List>().Single();
        Assert.That(list.ListItems.Count, Is.EqualTo(1));
        Assert.That(list.ListItems.Single().Blocks.OfType<Paragraph>().Single()
            .Inlines.OfType<Run>().Single().Text, Is.EqualTo("Does the feature work?"));
    }

    [Test]
    public void ReadView_QuestionOnlyRequirementHasReadableFallbackTitleWithoutEmptyDescriptionLine()
    {
        var requirement = new PlanTaskProofRequirement("restart-visible", "human-observation", "",
            "Is the feature still visible?");
        var list = PlanGateReviewRenderer.CreateReviewBlocks(Gate([requirement]), [], false)
            .OfType<System.Windows.Documents.List>().Single();
        var paragraph = list.ListItems.Single().Blocks.OfType<Paragraph>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(Text(paragraph), Does.Contain(PlanHumanReviewChecklist.ResolveDisplayTitle(null, requirement.RequirementId))
                .And.Contain(requirement.Question));
            Assert.That(paragraph.Inlines.OfType<LineBreak>().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public void ReadView_DeduplicatesQuestionsWithoutDroppingProofRows()
    {
        var gate = Gate(
        [
            new("first", "human-observation", "Is the feature visible?", "Is the feature visible?", Title: "First"),
            new("second", "human-observation", "Other details.", "Is the feature visible?", Title: "Second"),
        ]);
        var blocks = PlanGateReviewRenderer.CreateReviewBlocks(gate, ["Is the feature visible?"], false);
        var list = blocks.OfType<System.Windows.Documents.List>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(list.ListItems.Count, Is.EqualTo(2));
            Assert.That(blocks.Count, Is.EqualTo(1));
            Assert.That(list.ListItems.SelectMany(item => item.Blocks.OfType<Paragraph>())
                .SelectMany(paragraph => paragraph.Inlines.OfType<Run>())
                .Count(run => run.Text == "Is the feature visible?"), Is.EqualTo(1));
        });
    }

    [Test]
    public void ReadView_ClarificationsReplaceOriginalQuestionsButKeepProofDescriptions()
    {
        var gate = Gate([new("proof", "human-observation", "Observe this scenario.", "Old question?", Title: "Scenario")]);
        var blocks = PlanGateReviewRenderer.CreateReviewBlocks(gate, ["Clarified question?"], true);
        var document = new FlowDocument();
        foreach (var block in blocks)
            document.Blocks.Add(block);
        var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
        Assert.That(text, Does.Contain("Observe this scenario.")
            .And.Contain("Clarified question?")
            .And.Not.Contain("Old question?"));
    }

    [Test]
    public void ReadView_ProofTextAndSupplementalQuestionFollowDynamicBodyFontSize()
    {
        var gate = Gate([new("proof", "human-observation", "Description", "Question?", Title: "Title")]);
        var document = new FlowDocument();
        document.Resources["FontSizeBody"] = 18.0;
        document.Resources["BodyText"] = Brushes.Black;
        document.Resources["ImportantText"] = Brushes.DarkBlue;
        foreach (var block in PlanGateReviewRenderer.CreateReviewBlocks(gate, ["Review the item."], false))
            document.Blocks.Add(block);
        var list = document.Blocks.OfType<System.Windows.Documents.List>().Single();
        var paragraph = list.ListItems.Single().Blocks.OfType<Paragraph>().Single();
        var summary = document.Blocks.OfType<Paragraph>().Single();

        foreach (var fontSize in new[] { 18.0, 26.0 })
        {
            document.Resources["FontSizeBody"] = fontSize;
            Assert.Multiple(() =>
            {
                Assert.That(list.FontSize, Is.EqualTo(fontSize));
                Assert.That(paragraph.FontSize, Is.EqualTo(fontSize));
                Assert.That(paragraph.Inlines.OfType<Run>().Select(run => run.FontSize), Is.All.EqualTo(fontSize));
                Assert.That(summary.FontSize, Is.EqualTo(fontSize));
            });
        }
    }

    [Test]
    public void PendingGateMapping_ReadViewRetainsAllSevenProofsInOrder()
    {
        var requirements = Enumerable.Range(1, 7).Select(index =>
            new DecomposedTaskProofRequirement($"proof-{index}", "human-observation",
                $"Description {index}", $"Question {index}?", Title: $"Scenario {index}")).ToArray();
        var group = new DecomposedTaskGroup("plan", "Plan", "branch", "Summary", [],
            ApprovalGates: [new DecomposedGate("gate", "Review 7 items.", [], [], requirements)]);
        var gate = PendingDecomposePlanAdapter.MapApprovalGates(group, "revision").Single();
        var list = PlanGateReviewRenderer.CreateReviewBlocks(gate, [], false)
            .OfType<System.Windows.Documents.List>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(list.ListItems.Count, Is.EqualTo(7));
            Assert.That(list.ListItems.Select(item => item.Blocks.OfType<Paragraph>().Single()
                .Inlines.OfType<Run>().First().Text), Is.EqualTo(requirements.Select(requirement => requirement.Title)));
        });
    }

    private static PlanApprovalGate Gate(IReadOnlyList<PlanTaskProofRequirement>? requirements) =>
        new("gate", "Review", [], [], PlanGateStatus.Pending, ProofRequirements: requirements);

    private static string Text(Paragraph paragraph) =>
        new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.TrimEnd('\r', '\n');
}
