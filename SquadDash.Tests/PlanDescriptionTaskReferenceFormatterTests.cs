using System.Threading;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class PlanDescriptionTaskReferenceFormatterTests
{
    [Test]
    public void Apply_PreservesMarkdownLinksAndCodeWhileLinkingProseTaskReferences()
    {
        var doc = MarkdownFlowDocumentBuilder.BuildPlanDescription(
            "Use **task 002** after `task 001`. Keep [task 001](https://example.test) explicit.\n\n" +
            "```text\ntask 002\n```",
            baseFontSize: 13.0,
            onLinkClicked: _ => { });
        var tasks = new[]
        {
            MakeTask("PLAN-20260923-001", "1"),
            MakeTask("PLAN-20260923-002", "2"),
        };

        PlanDescriptionTaskReferenceFormatter.Apply(
            doc,
            tasks,
            (label, taskId) => new Hyperlink(new Run(label)) { Tag = taskId });

        var hyperlinks = doc.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines.OfType<Hyperlink>())
            .ToArray();
        var inlineCode = doc.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines.OfType<Run>())
            .Single(run => run.Text == "task 001");
        var codeTextBox = ((Grid)doc.Blocks.OfType<BlockUIContainer>().Single().Child)
            .Children.OfType<TextBox>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(hyperlinks.Select(link => link.Tag),
                Is.EquivalentTo(new object[] { "PLAN-20260923-002", "https://example.test" }));
            Assert.That(hyperlinks.Single(link => Equals(link.Tag, "PLAN-20260923-002"))
                    .Inlines.OfType<Run>().Single().Text,
                Is.EqualTo("Step 2"));
            Assert.That(inlineCode.FontFamily.Source, Does.Contain("Consolas"));
            Assert.That(codeTextBox.Text, Is.EqualTo("task 002"));
        });
    }

    private static PlanTask MakeTask(string taskId, string displayStepLabel) => new(
        TaskId: taskId,
        Title: "Task " + displayStepLabel,
        Description: "Description",
        DependsOn: [],
        Priority: "mid",
        Status: PlanTaskStatus.Pending,
        DisplayStepLabel: displayStepLabel);
}
