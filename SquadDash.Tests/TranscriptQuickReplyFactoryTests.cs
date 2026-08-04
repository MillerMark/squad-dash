using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class TranscriptQuickReplyFactoryTests
{
    [Test]
    public void CreateButton_UsesTranscriptFontAndSharedGeometry()
    {
        _ = Application.Current ?? new Application();

        var button = TranscriptQuickReplyFactory.CreateButton("Run plan", 19, tag: "payload");

        Assert.Multiple(() =>
        {
            Assert.That(button.FontSize, Is.EqualTo(19));
            Assert.That(button.Padding, Is.EqualTo(new Thickness(10, 4, 10, 4)));
            Assert.That(button.Margin, Is.EqualTo(new Thickness(0, 0, 8, 8)));
            Assert.That(button.MinHeight, Is.EqualTo(28));
            Assert.That(button.Tag, Is.EqualTo("payload"));
        });
    }

    [Test]
    public void CreateContainer_MarksPlanButtonsForLaterTranscriptZoomUpdates()
    {
        _ = Application.Current ?? new Application();
        var panel = new WrapPanel();
        var button = TranscriptQuickReplyFactory.CreateButton("Run plan", 14);
        panel.Children.Add(button);

        var container = TranscriptQuickReplyFactory.CreateContainer(panel);
        var buttons = TranscriptQuickReplyFactory.EnumerateButtons(container.Child).ToArray();

        Assert.That(TranscriptQuickReplyFactory.IsQuickReplyContainer(container), Is.True);
        Assert.That(buttons, Is.EqualTo(new[] { button }));
    }

    [TestCase("warning", QuickReplyTone.Warning)]
    [TestCase(" WARNING ", QuickReplyTone.Warning)]
    [TestCase("destructive", QuickReplyTone.Destructive)]
    [TestCase("unknown", QuickReplyTone.Default)]
    [TestCase(null, QuickReplyTone.Default)]
    public void ParseTone_RecognizesSupportedValues(string? value, QuickReplyTone expected)
    {
        Assert.That(TranscriptQuickReplyFactory.ParseTone(value), Is.EqualTo(expected));
    }

    [Test]
    public void RemovePendingDecomposeApprovalContainers_RemovesOnlyPlanActionsRecursively()
    {
        var document = new FlowDocument();
        var section = new Section();
        var planActions = TranscriptQuickReplyFactory.CreateContainer(
            new WrapPanel(),
            new PendingDecomposeApprovalTag("PLAN-20260725", "revision"));
        var ordinaryReplies = TranscriptQuickReplyFactory.CreateContainer(
            new WrapPanel(),
            new QuickReplyCopyData(["Continue"], null));
        section.Blocks.Add(new Paragraph(new Run("View task plan and dependencies"))
        {
            Tag = new PendingDecomposePlanLinkTag("PLAN-20260725", "revision"),
        });
        section.Blocks.Add(planActions);
        section.Blocks.Add(ordinaryReplies);
        document.Blocks.Add(section);

        TranscriptQuickReplyFactory.RemovePendingDecomposeApprovalContainers(document.Blocks);

        Assert.Multiple(() =>
        {
            Assert.That(section.Blocks.Contains(planActions), Is.False);
            Assert.That(section.Blocks.Contains(ordinaryReplies), Is.True);
            Assert.That(section.Blocks.OfType<Paragraph>().Single().Inlines.OfType<Run>().Single().Text,
                Is.EqualTo("View task plan and dependencies"));
        });
    }

    [Test]
    public void RemovePendingDecomposeApprovalContainers_RepairsMissingPlanLinkBeforeRemovingActions()
    {
        var document = new FlowDocument();
        var section = new Section();
        var planActions = TranscriptQuickReplyFactory.CreateContainer(
            new WrapPanel(),
            new PendingDecomposeApprovalTag("PLAN-20260725", "revision"));
        section.Blocks.Add(planActions);
        document.Blocks.Add(section);

        TranscriptQuickReplyFactory.RemovePendingDecomposeApprovalContainers(
            document.Blocks,
            tag => new Paragraph(new Run("View task plan and dependencies"))
            {
                Tag = new PendingDecomposePlanLinkTag(tag.GroupId, tag.Revision),
            });

        var link = section.Blocks.OfType<Paragraph>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(section.Blocks.Contains(planActions), Is.False);
            Assert.That(link.Tag, Is.EqualTo(new PendingDecomposePlanLinkTag("PLAN-20260725", "revision")));
            Assert.That(link.Inlines.OfType<Run>().Single().Text,
                Is.EqualTo("View task plan and dependencies"));
        });
    }

    [Test]
    public void ContainsDecomposeRecoveryContainer_FindsMatchingActionsRecursively()
    {
        var document = new FlowDocument();
        var section = new Section();
        section.Blocks.Add(TranscriptQuickReplyFactory.CreateContainer(
            new WrapPanel(),
            new DecomposeRecoveryTag("PLAN-RECOVERY", "revision-2", "TASK-005")));
        document.Blocks.Add(section);

        Assert.Multiple(() =>
        {
            Assert.That(TranscriptQuickReplyFactory.ContainsDecomposeRecoveryContainer(
                document.Blocks, "PLAN-RECOVERY", "revision-2", "TASK-005"), Is.True);
            Assert.That(TranscriptQuickReplyFactory.ContainsDecomposeRecoveryContainer(
                document.Blocks, "PLAN-RECOVERY", "revision-2", "TASK-006"), Is.False);
        });
    }
}
