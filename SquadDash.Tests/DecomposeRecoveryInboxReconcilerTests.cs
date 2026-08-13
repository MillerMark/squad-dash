using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class DecomposeRecoveryInboxReconcilerTests
{
    private const string PlanId = "RECOVERY-001";
    private const string Revision = "rev-1";
    private const string TaskId = "RECOVERY-001-002";

    [Test]
    public void Reconcile_CurrentInterruptedTask_KeepsActionsAndRaisesPriority()
    {
        var message = MakeMessage() with { Priority = "high" };
        var result = DecomposeRecoveryInboxReconciler.Reconcile(
            message,
            MakePlan(PlanLifecycleStatus.Interrupted, TaskId));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsActionable, Is.True);
            Assert.That(result.ShouldArchive, Is.False);
            Assert.That(result.Message.Priority, Is.EqualTo("critical"));
            Assert.That(result.Message.Actions, Has.Count.EqualTo(2));
            Assert.That(result.Message.Actions.Select(action => action.Label),
                Is.EqualTo(new[] { "Assess & Continue", "✎ Revise Remaining Plan…" }));
        });
    }

    [Test]
    public void Reconcile_CompletedPlan_DisablesAndArchivesRecoveryActions()
    {
        var result = DecomposeRecoveryInboxReconciler.Reconcile(
            MakeMessage(),
            MakePlan(PlanLifecycleStatus.Completed));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsActionable, Is.False);
            Assert.That(result.ShouldArchive, Is.True);
            Assert.That(result.Message.Read, Is.True);
            Assert.That(result.Message.Actions, Is.Empty);
            Assert.That(result.Message.Body, Does.Contain("completed successfully"));
        });
    }

    [Test]
    public void Reconcile_ExecutingPlan_ArchivesEarlierRecoveryRequest()
    {
        var result = DecomposeRecoveryInboxReconciler.Reconcile(
            MakeMessage(),
            MakePlan(PlanLifecycleStatus.Executing));

        Assert.That(result.ShouldArchive, Is.True);
        Assert.That(result.Message.Body, Does.Contain("continued beyond this interruption"));
    }

    [Test]
    public void Reconcile_DifferentInterruptedTask_ArchivesStaleRequest()
    {
        var result = DecomposeRecoveryInboxReconciler.Reconcile(
            MakeMessage(),
            MakePlan(PlanLifecycleStatus.Interrupted, "RECOVERY-001-003"));

        Assert.That(result.IsActionable, Is.False);
        Assert.That(result.ShouldArchive, Is.True);
    }

    [Test]
    public void Reconcile_DifferentRevision_ArchivesStaleRequest()
    {
        var plan = MakePlan(PlanLifecycleStatus.Interrupted, TaskId) with { Revision = "rev-2" };
        var result = DecomposeRecoveryInboxReconciler.Reconcile(MakeMessage(), plan);

        Assert.That(result.IsActionable, Is.False);
        Assert.That(result.Message.Body, Does.Contain("definition changed"));
    }

    [Test]
    public void Reconcile_LegacyRecoveryMessageWithNullCollections_DoesNotCrashStartup()
    {
        var legacyMessage = new InboxMessage
        {
            Id = "decompose-recovery-LEGACY-001-LEGACY-001-001-old-revision",
            Subject = "Blocked plan: Legacy",
            Body = null!,
            Actions = null!,
            Attachments = null!,
        };

        var result = DecomposeRecoveryInboxReconciler.Reconcile(legacyMessage, plan: null);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsActionable, Is.False);
            Assert.That(result.ShouldArchive, Is.True);
            Assert.That(result.Message.Actions, Is.Empty);
            Assert.That(result.Message.Body, Does.Contain("Recovery request resolved"));
        });
    }

    private static InboxMessage MakeMessage()
    {
        var pending = new PendingDecomposePlan(
            Revision,
            new DecomposedTaskGroup(
                PlanId,
                "Recovery plan",
                "feature/recovery",
                "Exercise recovery.",
                [new DecomposedSubTask(TaskId, "Blocked task", [], "high")]));
        return DecomposePlanInbox.BuildRecoveryMessage(
            pending,
            TaskId,
            "Worker stopped.",
            DateTimeOffset.Parse("2026-08-03T12:00:00Z"));
    }

    private static Plan MakePlan(string lifecycle, string? interruptedTaskId = null) =>
        new(
            PlanId,
            Revision,
            PlanSource.Inbox,
            lifecycle,
            "Recovery plan",
            "feature/recovery",
            "Exercise recovery.",
            [new PlanTask(TaskId, "Blocked task", "Blocked task", [], "high", PlanTaskStatus.Pending)],
            [],
            new PlanProgress(0, 1),
            new PlanTimestamps(DateTimeOffset.Parse("2026-08-03T11:00:00Z")),
            interruptedTaskId is null
                ? null
                : new PlanInterruptionData(
                    "Worker stopped.",
                    PlanRecoveryState.PendingRecovery,
                    1,
                    interruptedTaskId));
}
