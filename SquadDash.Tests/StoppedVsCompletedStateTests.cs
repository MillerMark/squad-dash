using NUnit.Framework;
using System;

namespace SquadDash.Tests;

/// <summary>
/// Tests the semantic distinction between Stopped and Completed and covers
/// <see cref="PlanLifecycleStatus.IsTerminal"/> for statuses not exercised in PlanModelTests.cs
/// (which already covers Stopped, Completed, Archived, Executing, Staged, Interrupted).
/// </summary>
[TestFixture]
internal sealed class StoppedVsCompletedStateTests
{
    // ── IsTerminal boundary cases not covered by PlanModelTests ──────────────

    [Test]
    public void AwaitingApproval_isNotTerminal()
    {
        Assert.That(PlanLifecycleStatus.IsTerminal(PlanLifecycleStatus.AwaitingApproval), Is.False,
            "AwaitingApproval is a pause state; it can resume execution after gate approval.");
    }

    [Test]
    public void Blocked_isNotTerminal()
    {
        Assert.That(PlanLifecycleStatus.IsTerminal(PlanLifecycleStatus.Blocked), Is.False,
            "Blocked is recoverable — the plan can be retried or replanned.");
    }

    // ── Semantic difference between Stopped and Completed ────────────────────

    [Test]
    public void Stopped_preservesInterruptionDataWithEndedRecoveryState()
    {
        var plan = new Plan(
            PlanId:          "SEM-001",
            Revision:        "rev1",
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Interrupted,
            Title:           "Semantic Test",
            Branch:          "feature/test",
            Summary:         "test",
            Tasks:           [],
            ApprovalGates:   [],
            Progress:        new PlanProgress(1, 3, null),
            Timestamps:      new PlanTimestamps(DateTimeOffset.UtcNow),
            InterruptionData: new PlanInterruptionData(
                Reason:        "user request",
                RecoveryState: PlanRecoveryState.PendingRecovery,
                LoopIteration: 2));

        var stopped = PlanStoreUpdater.ApplyStopped(plan);

        Assert.That(stopped.LifecycleStatus,                    Is.EqualTo(PlanLifecycleStatus.Stopped));
        Assert.That(stopped.InterruptionData,                   Is.Not.Null,
            "Stopped preserves interruption history for audit purposes.");
        Assert.That(stopped.InterruptionData!.RecoveryState,    Is.EqualTo(PlanRecoveryState.Ended),
            "RecoveryState must be Ended so no recovery reminders are shown.");
        Assert.That(stopped.Timestamps.StoppedAt,               Is.Not.Null);
        Assert.That(stopped.Timestamps.CompletedAt,             Is.Null,
            "Stopped does not set CompletedAt — only Completed does.");
    }

    [Test]
    public void Completed_setsCompletedAt_andClearsExecutingTask()
    {
        var plan = new Plan(
            PlanId:          "SEM-001",
            Revision:        "rev1",
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Executing,
            Title:           "Semantic Test",
            Branch:          "feature/test",
            Summary:         "test",
            Tasks:           [],
            ApprovalGates:   [],
            Progress:        new PlanProgress(3, 3, "SEM-001-003"),
            Timestamps:      new PlanTimestamps(DateTimeOffset.UtcNow));

        var before    = DateTimeOffset.UtcNow;
        var completed = PlanStoreUpdater.ApplyCompleted(plan);

        Assert.That(completed.LifecycleStatus,          Is.EqualTo(PlanLifecycleStatus.Completed));
        Assert.That(completed.Timestamps.CompletedAt,   Is.Not.Null);
        Assert.That(completed.Timestamps.CompletedAt,   Is.GreaterThanOrEqualTo(before));
        Assert.That(completed.Timestamps.StoppedAt,     Is.Null,
            "Completed does not set StoppedAt — only Stopped does.");
        Assert.That(completed.Progress.ExecutingTaskId, Is.Null);
        Assert.That(completed.InterruptionData,         Is.Null);
    }

    [Test]
    public void Stopped_and_Completed_areBothTerminal_butDistinct()
    {
        Assert.That(PlanLifecycleStatus.IsTerminal(PlanLifecycleStatus.Stopped),   Is.True);
        Assert.That(PlanLifecycleStatus.IsTerminal(PlanLifecycleStatus.Completed), Is.True);
        Assert.That(PlanLifecycleStatus.Stopped,   Is.Not.EqualTo(PlanLifecycleStatus.Completed),
            "Stopped and Completed are distinct terminal states with different semantics.");
    }
}
