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

    [Test]
    public void CreateContainer_MakesEmbeddedCardResponsiveToTranscriptWidth()
    {
        var card = new Border
        {
            Width = 400,
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var container = TranscriptQuickReplyFactory.CreateContainer(card);

        Assert.Multiple(() =>
        {
            Assert.That(container.Child, Is.SameAs(card));
            Assert.That(double.IsNaN(card.Width), Is.True);
            Assert.That(double.IsPositiveInfinity(card.MaxWidth), Is.True);
            Assert.That(card.HorizontalAlignment, Is.EqualTo(HorizontalAlignment.Stretch));
        });
    }

    [Test]
    public void CreateContainer_CardRegistersCopyableWithoutReplacingIdentityTag()
    {
        var identity = new PlanPreflightRecoveryTag("PLAN-1", "revision");
        var label = new TextBlock { Text = "Plan recovery details" };
        var card = new Border { Child = label };

        var container = TranscriptQuickReplyFactory.CreateContainer(card, identity);
        var copyable = TranscriptCardLayout.GetCopyable(container);

        Assert.Multiple(() =>
        {
            Assert.That(container.Tag, Is.SameAs(identity));
            Assert.That(copyable, Is.Not.Null);
            Assert.That(copyable!.GetCopyText(), Is.EqualTo("Plan recovery details"));
            Assert.That(TranscriptCardLayout.FindContainer(label), Is.SameAs(container));
        });
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
    public void ReplacePendingDecomposeApprovalSurface_PlanTurnThenEmptySystemTurn_RestoresOneActionCardAtEnd()
    {
        var document = new FlowDocument();
        var planTurn = new Section();
        planTurn.Blocks.Add(new Paragraph(new Run("Historical plan receipt"))
        {
            Tag = new PendingDecomposePlanLinkTag("PLAN-RESTART", "revision-1"),
        });
        document.Blocks.Add(planTurn);
        document.Blocks.Add(new Section(new Paragraph(new Run(string.Empty)))
        {
            Tag = "empty-system-turn",
        });

        var restoredLink = new Paragraph(new Run("Restored plan receipt"))
        {
            Tag = new PendingDecomposePlanLinkTag("PLAN-RESTART", "revision-1"),
        };
        var restoredActions = TranscriptQuickReplyFactory.CreateContainer(
            new WrapPanel(),
            new PendingDecomposeApprovalTag("PLAN-RESTART", "revision-1"));

        TranscriptQuickReplyFactory.ReplacePendingDecomposeApprovalSurface(
            document.Blocks,
            "PLAN-RESTART",
            restoredLink,
            restoredActions);

        var allBlocks = EnumerateBlocks(document.Blocks).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(allBlocks.Count(block => block.Tag is PendingDecomposePlanLinkTag), Is.EqualTo(1));
            Assert.That(allBlocks.Count(block => block.Tag is PendingDecomposeApprovalTag), Is.EqualTo(1));
            Assert.That(document.Blocks.LastBlock, Is.SameAs(restoredActions));
            Assert.That(restoredActions.PreviousBlock, Is.SameAs(restoredLink));
            Assert.That(document.Blocks.OfType<Section>().Any(section => Equals(section.Tag, "empty-system-turn")), Is.True);
            Assert.That(
                TranscriptQuickReplyFactory.ContainsPendingDecomposeApprovalSurface(
                    document.Blocks, "PLAN-RESTART", "revision-1"),
                Is.True);
            Assert.That(
                TranscriptQuickReplyFactory.ContainsPendingDecomposeApprovalSurface(
                    document.Blocks, "PLAN-RESTART", "obsolete-revision"),
                Is.False);
        });
    }

    [Test]
    public void ReplacePendingDecomposeApprovalSurfaceWithInboxBanner_PreservesOriginalPlanTurnPosition()
    {
        var document = new FlowDocument();
        var planTurn = new Section();
        var historicalLink = new Paragraph(new Run("Historical plan receipt"))
        {
            Tag = new PendingDecomposePlanLinkTag("PLAN-INBOX", "revision-1"),
        };
        planTurn.Blocks.Add(historicalLink);
        document.Blocks.Add(planTurn);
        var actions = TranscriptQuickReplyFactory.CreateContainer(
            new WrapPanel(),
            new PendingDecomposeApprovalTag("PLAN-INBOX", "revision-1"));
        document.Blocks.Add(actions);
        var laterTurn = new Section(new Paragraph(new Run("A later unrelated turn")));
        document.Blocks.Add(laterTurn);
        var banner = new Paragraph(new Run("Plan moved to Inbox"))
        {
            Tag = new PendingDecomposeInboxBannerTag("PLAN-INBOX", "revision-1"),
        };

        var replaced = TranscriptQuickReplyFactory.ReplacePendingDecomposeApprovalSurfaceWithInboxBanner(
            document.Blocks,
            "PLAN-INBOX",
            banner);

        var allBlocks = EnumerateBlocks(document.Blocks).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.True);
            Assert.That(planTurn.Blocks.Single(), Is.SameAs(banner));
            Assert.That(document.Blocks.LastBlock, Is.SameAs(laterTurn));
            Assert.That(allBlocks.Any(block => block.Tag is PendingDecomposePlanLinkTag), Is.False);
            Assert.That(allBlocks.Any(block => block.Tag is PendingDecomposeApprovalTag), Is.False);
            Assert.That(allBlocks.Count(block => block.Tag is PendingDecomposeInboxBannerTag), Is.EqualTo(1));
        });
    }

    [Test]
    public void ReplacePendingDecomposeApprovalSurfaceWithInboxBanner_NoAnchorLeavesRecoveryFallbackToCaller()
    {
        var document = new FlowDocument(new Paragraph(new Run("Unrelated transcript")));
        var banner = new Paragraph(new Run("Plan moved to Inbox"))
        {
            Tag = new PendingDecomposeInboxBannerTag("PLAN-MISSING", "revision-1"),
        };

        var replaced = TranscriptQuickReplyFactory.ReplacePendingDecomposeApprovalSurfaceWithInboxBanner(
            document.Blocks,
            "PLAN-MISSING",
            banner);

        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.False);
            Assert.That(document.Blocks.Contains(banner), Is.False);
        });
    }

    [Test]
    public void RemoveDecomposeRecoveryContainers_RemovesEntireTaggedSurfaceRecursively()
    {
        var document = new FlowDocument();
        var historySection = new Section();
        var recoverySurface = new Section
        {
            Tag = new DecomposeRecoveryTag("PLAN-RECOVERY", "revision-2", "TASK-005"),
        };
        recoverySurface.Blocks.Add(new Paragraph(new Run("Recovery explanation")));
        recoverySurface.Blocks.Add(TranscriptQuickReplyFactory.CreateContainer(
            new WrapPanel(),
            new DecomposeRecoveryTag("PLAN-RECOVERY", "revision-2", "TASK-005")));
        historySection.Blocks.Add(recoverySurface);
        historySection.Blocks.Add(new Paragraph(new Run("Historical narrative")));
        document.Blocks.Add(historySection);

        TranscriptQuickReplyFactory.RemoveDecomposeRecoveryContainers(document.Blocks);

        Assert.That(historySection.Blocks.Contains(recoverySurface), Is.False);
        Assert.That(historySection.Blocks.OfType<Paragraph>().Single().Inlines
            .OfType<Run>().Single().Text, Is.EqualTo("Historical narrative"));
    }

    [Test]
    public void RemoveDecomposeRecoveryContainers_ForPlan_ReplacesOnlyThatPlansWholeCard()
    {
        var document = new FlowDocument();
        var planA = new Section
        {
            Tag = new DecomposeRecoveryTag("PLAN-A", "revision-a", "TASK-A"),
        };
        planA.Blocks.Add(TranscriptQuickReplyFactory.CreateContainer(
            new Border(),
            new DecomposeRecoveryCardTag(
                new DecomposeRecoveryTag("PLAN-A", "revision-a", "TASK-A"),
                new WrapPanel())));
        var planB = new Section
        {
            Tag = new DecomposeRecoveryTag("PLAN-B", "revision-b", "TASK-B"),
        };
        document.Blocks.Add(planA);
        document.Blocks.Add(planB);

        TranscriptQuickReplyFactory.RemoveDecomposeRecoveryContainers(document.Blocks, "PLAN-A");

        Assert.Multiple(() =>
        {
            Assert.That(document.Blocks.Contains(planA), Is.False);
            Assert.That(document.Blocks.Contains(planB), Is.True);
        });
    }

    [Test]
    public void RemoveDecomposeRecoveryActions_PreservesNarrativeAndUnrelatedPlanActions()
    {
        var document = new FlowDocument();
        var recoverySurface = new Section
        {
            Tag = new DecomposeRecoveryTag("PLAN-A", "revision-a", "TASK-A"),
        };
        var explanation = new Paragraph(new Run("Why Plan A stopped"));
        var planAActions = TranscriptQuickReplyFactory.CreateContainer(
            new WrapPanel(),
            new DecomposeRecoveryTag("PLAN-A", "revision-a", "TASK-A"));
        var planBActions = TranscriptQuickReplyFactory.CreateContainer(
            new WrapPanel(),
            new DecomposeRecoveryTag("PLAN-B", "revision-b", "TASK-B"));
        recoverySurface.Blocks.Add(explanation);
        recoverySurface.Blocks.Add(planAActions);
        document.Blocks.Add(recoverySurface);
        document.Blocks.Add(planBActions);

        TranscriptQuickReplyFactory.RemoveDecomposeRecoveryActions(document.Blocks, "PLAN-A");

        Assert.Multiple(() =>
        {
            Assert.That(document.Blocks.Contains(recoverySurface), Is.True);
            Assert.That(recoverySurface.Blocks.Contains(explanation), Is.True);
            Assert.That(recoverySurface.Blocks.Contains(planAActions), Is.False);
            Assert.That(document.Blocks.Contains(planBActions), Is.True);
        });
    }

    [Test]
    public void RemoveDecomposeRecoveryActions_HidesButtonsButPreservesRecoveryCard()
    {
        var document = new FlowDocument();
        var section = new Section
        {
            Tag = new DecomposeRecoveryTag("PLAN-A", "revision-a", "TASK-A"),
        };
        var actions = new WrapPanel();
        actions.Children.Add(new Button { Content = "Assess & Continue" });
        var reassessmentActions = new WrapPanel();
        reassessmentActions.Children.Add(new Button { Content = "Reassess with AI" });
        var tag = new DecomposeRecoveryCardTag(
            new DecomposeRecoveryTag("PLAN-A", "revision-a", "TASK-A"),
            actions)
        {
            CopyText = "Human review required",
        };
        tag.RegisterActionSurface(reassessmentActions);
        var card = TranscriptQuickReplyFactory.CreateContainer(new Border { Child = actions }, tag);
        section.Blocks.Add(card);
        document.Blocks.Add(section);

        TranscriptQuickReplyFactory.RemoveDecomposeRecoveryActions(document.Blocks, "PLAN-A");

        Assert.Multiple(() =>
        {
            Assert.That(section.Blocks.Contains(card), Is.True);
            Assert.That(actions.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(reassessmentActions.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(tag.GetCopyText(), Is.EqualTo("Human review required"));
        });
    }

    [Test]
    public void RecoveryCardActionState_LocksPrimaryAndReassessmentButtonsTogether()
    {
        var primaryActions = new WrapPanel();
        var reassessmentActions = new WrapPanel();
        var tag = new DecomposeRecoveryCardTag(
            new DecomposeRecoveryTag("PLAN-A", "revision-a", "TASK-A"),
            primaryActions);
        tag.RegisterActionSurface(reassessmentActions);

        tag.SetActionsEnabled(false);

        Assert.Multiple(() =>
        {
            Assert.That(primaryActions.IsEnabled, Is.False);
            Assert.That(reassessmentActions.IsEnabled, Is.False);
        });

        tag.SetActionsEnabled(true);

        Assert.Multiple(() =>
        {
            Assert.That(primaryActions.IsEnabled, Is.True);
            Assert.That(reassessmentActions.IsEnabled, Is.True);
        });
    }

    [Test]
    public void FindLatestActionButtons_DiscoversEnabledButtonsInsideNewestCard()
    {
        _ = Application.Current ?? new Application();
        var document = new FlowDocument();
        var oldPanel = new WrapPanel();
        oldPanel.Children.Add(TranscriptQuickReplyFactory.CreateButton("Old action", 14));
        document.Blocks.Add(TranscriptQuickReplyFactory.CreateContainer(oldPanel));
        var section = new Section();
        var cardPanel = new WrapPanel();
        var disabled = TranscriptQuickReplyFactory.CreateButton("Step 7 Is Complete", 14);
        disabled.IsEnabled = false;
        var address = TranscriptQuickReplyFactory.CreateButton("✎ Address Unchecked Items…", 14);
        cardPanel.Children.Add(disabled);
        cardPanel.Children.Add(address);
        section.Blocks.Add(TranscriptQuickReplyFactory.CreateContainer(
            new Border { Child = cardPanel },
            new DecomposeRecoveryTag("PLAN", "revision", "PLAN-007")));
        document.Blocks.Add(section);

        var buttons = TranscriptQuickReplyFactory.FindLatestActionButtons(document.Blocks);

        Assert.That(buttons.Select(button => button.Content),
            Is.EqualTo(new object[] { "✎ Address Unchecked Items…" }));
    }

    [Test]
    public void RemoveProtocolJsonIndicators_RemovesNestedDiagnosticButtonsOnly()
    {
        var document = new FlowDocument();
        var section = new Section();
        var narrative = new Paragraph(new Run("Recovery summary"));
        var jsonIndicator = new Paragraph
        {
            Tag = new TranscriptProtocolJsonIndicatorTag("DECOMPOSE_STEP_RESULT_JSON"),
        };
        jsonIndicator.Inlines.Add(new InlineUIContainer(new Button { Content = "{ } JSON received" }));
        section.Blocks.Add(narrative);
        section.Blocks.Add(jsonIndicator);
        document.Blocks.Add(section);

        TranscriptQuickReplyFactory.RemoveProtocolJsonIndicators(document.Blocks);

        Assert.Multiple(() =>
        {
            Assert.That(section.Blocks.Contains(narrative), Is.True);
            Assert.That(section.Blocks.Contains(jsonIndicator), Is.False);
        });
    }

    private static IEnumerable<Block> EnumerateBlocks(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;
            if (block is not Section section)
                continue;
            foreach (var nested in EnumerateBlocks(section.Blocks))
                yield return nested;
        }
    }
}
