using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanHumanActionGuideReferenceTests
{
    private const string PlanId = "GUIDE-001";
    private const string Revision = "rev-1";
    private const string TaskId = "GUIDE-001-002";

    [Test]
    public void Resolve_HumanRequiredTaskBehindApprovedGate_ReturnsDurableGuideReference()
    {
        var plan = MakePlan();
        var approval = MakeApprovalMessage(includeGuide: true);

        var reference = PlanHumanActionGuideReferenceResolver.Resolve(plan, TaskId, approval);

        Assert.Multiple(() =>
        {
            Assert.That(reference, Is.Not.Null);
            Assert.That(reference!.Type, Is.EqualTo(DurableApprovalRequestManager.RunbookAttachmentType));
            Assert.That(reference.Label, Is.EqualTo(PlanHumanActionGuideReferenceResolver.LinkLabel));
            Assert.That(reference.InboxMessageId, Is.EqualTo(approval.Id));
            Assert.That(reference.PlanGroupId, Is.EqualTo(PlanId));
            Assert.That(reference.PlanRevision, Is.EqualTo(Revision));
        });
    }

    [TestCase(false, PlanGateStatus.Approved)]
    [TestCase(true, PlanGateStatus.Pending)]
    public void Resolve_MissingGuideOrUnapprovedGate_DoesNotInventReference(
        bool includeGuide,
        string gateStatus)
    {
        var reference = PlanHumanActionGuideReferenceResolver.Resolve(
            MakePlan(gateStatus),
            TaskId,
            MakeApprovalMessage(includeGuide));

        Assert.That(reference, Is.Null);
    }

    [Test]
    public void BuildRecoveryMessage_HumanRequired_UsesGuideAndPurposeBuiltActions()
    {
        var plan = MakePlan();
        var reference = PlanHumanActionGuideReferenceResolver.Resolve(
            plan,
            TaskId,
            MakeApprovalMessage(includeGuide: true));

        var message = DecomposePlanInbox.BuildRecoveryMessage(
            PendingDecomposePlanAdapter.FromPlan(plan),
            TaskId,
            plan.InterruptionData!.Reason,
            DateTimeOffset.Parse("2026-10-03T12:00:00Z"),
            durablePlan: plan,
            humanActionGuideReference: reference);

        Assert.Multiple(() =>
        {
            Assert.That(message.Attachments.Any(attachment =>
                attachment.Type == DurableApprovalRequestManager.RunbookAttachmentType &&
                attachment.InboxMessageId == DurableApprovalRequestManager.BuildMessageId(PlanId)), Is.True);
            Assert.That(message.Actions.Select(action => action.Label), Is.EqualTo(new[]
            {
                "Continue After Human Input",
                "✎ Revise Remaining Plan…",
            }));
            Assert.That(message.Body, Does.Contain("Open the required instructions attachment"));
            Assert.That(message.Body, Does.Not.Contain("does not have exact task-owned commit attribution"));
            Assert.That(message.Body, Does.Not.Contain("Choose an action below"));
        });
    }

    [Test]
    public void Reconcile_CurrentHumanRequiredTask_PreservesGuideReferenceAcrossRestart()
    {
        var plan = MakePlan();
        var reference = PlanHumanActionGuideReferenceResolver.Resolve(
            plan,
            TaskId,
            MakeApprovalMessage(includeGuide: true));
        var message = DecomposePlanInbox.BuildRecoveryMessage(
            PendingDecomposePlanAdapter.FromPlan(plan),
            TaskId,
            plan.InterruptionData!.Reason,
            DateTimeOffset.Parse("2026-10-03T12:00:00Z"),
            durablePlan: plan,
            humanActionGuideReference: reference);

        var reconciled = DecomposeRecoveryInboxReconciler.Reconcile(message, plan);

        Assert.That(reconciled.Message.Attachments.Any(attachment =>
            attachment.Type == DurableApprovalRequestManager.RunbookAttachmentType &&
            attachment.InboxMessageId == DurableApprovalRequestManager.BuildMessageId(PlanId)), Is.True);
    }

    private static Plan MakePlan(string gateStatus = PlanGateStatus.Approved)
    {
        var task = new PlanTask(
            TaskId,
            "Verify deployed release",
            "Verify deployed release",
            [],
            "high",
            PlanTaskStatus.Partial,
            DisplayStepLabel: "16",
            ExecutionMode: PlanTaskExecutionMode.Verification);
        var gate = new PlanApprovalGate(
            "GUIDE-001-GATE-001",
            "Authorize deployment",
            ["GUIDE-001-001"],
            [TaskId],
            gateStatus);
        return new Plan(
            PlanId,
            Revision,
            PlanSource.Inbox,
            PlanLifecycleStatus.Interrupted,
            "Guide plan",
            "feature/guide",
            "Verify a deployment.",
            [task],
            [gate],
            new PlanProgress(0, 1),
            new PlanTimestamps(DateTimeOffset.Parse("2026-10-03T11:00:00Z")),
            new PlanInterruptionData(
                "A human operator must complete the approved deployment.",
                PlanRecoveryState.PendingRecovery,
                1,
                TaskId,
                AttentionCategory: PlanAttentionCategory.HumanInputRequired,
                AttentionSummary: "Complete the approved deployment."));
    }

    private static InboxMessage MakeApprovalMessage(bool includeGuide)
    {
        var attachments = new List<InboxAttachment>();
        if (includeGuide)
        {
            attachments.Add(new InboxAttachment
            {
                Type = DurableApprovalRequestManager.OperatorGuideAttachmentType,
                Label = "Approval operator guide",
                Content = "Run the approved deployment procedure.",
                PlanGroupId = PlanId,
                PlanRevision = Revision,
            });
        }

        return new InboxMessage
        {
            Id = DurableApprovalRequestManager.BuildMessageId(PlanId),
            Subject = "Approval needed",
            Body = "All checkpoints resolved.",
            Attachments = attachments,
        };
    }
}
