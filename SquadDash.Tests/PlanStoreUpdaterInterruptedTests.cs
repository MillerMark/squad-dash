using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanStoreUpdaterInterruptedTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static Plan MakePlan(string status = PlanLifecycleStatus.Executing)
    {
        var tasks = new[] { new PlanTask("GRP-001", "T1", "desc", [], "mid", PlanTaskStatus.Pending) };
        return new Plan(
            PlanId:          "GRP-20260101",
            Revision:        "rev1",
            Source:          PlanSource.TasksJson,
            LifecycleStatus: status,
            Title:           "Test Plan",
            Branch:          "feature/test",
            Summary:         "test",
            Tasks:           tasks,
            ApprovalGates:   [],
            Progress:        new PlanProgress(0, 1, "GRP-001"),
            Timestamps:      new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    // ── ApplyInterrupted ──────────────────────────────────────────────────────

    [Test]
    public void ApplyInterrupted_SetsLifecycleStatus()
    {
        var plan    = MakePlan();
        var updated = PlanStoreUpdater.ApplyInterrupted(plan, "test reason", loopIteration: 3);

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
    }

    [Test]
    public void ApplyInterrupted_PopulatesInterruptionData()
    {
        var plan    = MakePlan();
        var updated = PlanStoreUpdater.ApplyInterrupted(plan, "unexpected stop", loopIteration: 2);

        Assert.That(updated.InterruptionData,                Is.Not.Null);
        Assert.That(updated.InterruptionData!.Reason,        Is.EqualTo("unexpected stop"));
        Assert.That(updated.InterruptionData.LoopIteration,  Is.EqualTo(2));
    }

    [Test]
    public void ApplyInterrupted_SetsRecoveryStatePendingRecovery()
    {
        var plan    = MakePlan();
        var updated = PlanStoreUpdater.ApplyInterrupted(plan, "reason", loopIteration: 0);

        Assert.That(updated.InterruptionData!.RecoveryState, Is.EqualTo(PlanRecoveryState.PendingRecovery));
    }

    [Test]
    public void ApplyInterrupted_ClearsExecutingTaskId()
    {
        var plan    = MakePlan();
        Assert.That(plan.Progress.ExecutingTaskId, Is.Not.Null, "Precondition: plan has an executing task.");

        var updated = PlanStoreUpdater.ApplyInterrupted(plan, "reason", loopIteration: 1);

        Assert.That(updated.Progress.ExecutingTaskId, Is.Null);
    }

    [Test]
    public void ApplyInterrupted_SetsInterruptedAtTimestamp()
    {
        var before  = DateTimeOffset.UtcNow;
        var plan    = MakePlan();
        var updated = PlanStoreUpdater.ApplyInterrupted(plan, "reason", loopIteration: 0);

        Assert.That(updated.Timestamps.InterruptedAt, Is.Not.Null);
        Assert.That(updated.Timestamps.InterruptedAt!.Value, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void ApplyInterrupted_WithOptionalFields_AllPresent()
    {
        var plan    = MakePlan();
        var paths   = (IReadOnlyList<string>)["src/foo.cs", "src/bar.cs"];
        var updated = PlanStoreUpdater.ApplyInterrupted(
            plan,
            reason:              "network failure",
            loopIteration:       5,
            interruptedTaskId:   "GRP-001",
            lastCompletedTaskId: "GRP-000",
            lastCommit:          "abc1234",
            affectedPaths:       paths,
            partialWorkEvidence: "partial evidence");

        var data = updated.InterruptionData!;
        Assert.That(data.InterruptedTaskId,   Is.EqualTo("GRP-001"));
        Assert.That(data.LastCompletedTaskId, Is.EqualTo("GRP-000"));
        Assert.That(data.LastCommit,          Is.EqualTo("abc1234"));
        Assert.That(data.AffectedPaths,       Is.EqualTo(paths));
        Assert.That(data.PartialWorkEvidence, Is.EqualTo("partial evidence"));
    }

    // ── ApplyStopped ──────────────────────────────────────────────────────────

    [Test]
    public void ApplyStopped_SetsLifecycleStatusStopped()
    {
        var plan    = MakePlan(PlanLifecycleStatus.Interrupted);
        var updated = PlanStoreUpdater.ApplyStopped(plan);

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Stopped));
    }

    [Test]
    public void ApplyStopped_SetsStoppedAtTimestamp()
    {
        var before  = DateTimeOffset.UtcNow;
        var plan    = MakePlan(PlanLifecycleStatus.Interrupted);
        var updated = PlanStoreUpdater.ApplyStopped(plan);

        Assert.That(updated.Timestamps.StoppedAt, Is.Not.Null);
        Assert.That(updated.Timestamps.StoppedAt!.Value, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void ApplyStopped_SetsRecoveryStateEnded_WhenInterruptionDataPresent()
    {
        var plan = MakePlan(PlanLifecycleStatus.Interrupted) with
        {
            InterruptionData = new PlanInterruptionData(
                Reason:        "some reason",
                RecoveryState: PlanRecoveryState.PendingRecovery,
                LoopIteration: 1),
        };
        var updated = PlanStoreUpdater.ApplyStopped(plan);

        Assert.That(updated.InterruptionData!.RecoveryState, Is.EqualTo(PlanRecoveryState.Ended));
    }

    [Test]
    public void ApplyStopped_PreservesInterruptionData_ExceptRecoveryState()
    {
        var plan = MakePlan(PlanLifecycleStatus.Interrupted) with
        {
            InterruptionData = new PlanInterruptionData(
                Reason:              "prior reason",
                RecoveryState:       PlanRecoveryState.PendingRecovery,
                LoopIteration:       7,
                InterruptedTaskId:   "T-42",
                LastCompletedTaskId: "T-41",
                LastCommit:          "deadbeef"),
        };
        var updated = PlanStoreUpdater.ApplyStopped(plan);

        var data = updated.InterruptionData!;
        Assert.That(data.Reason,              Is.EqualTo("prior reason"));
        Assert.That(data.LoopIteration,       Is.EqualTo(7));
        Assert.That(data.InterruptedTaskId,   Is.EqualTo("T-42"));
        Assert.That(data.LastCompletedTaskId, Is.EqualTo("T-41"));
        Assert.That(data.LastCommit,          Is.EqualTo("deadbeef"));
    }

    [Test]
    public void ApplyStopped_ClearsExecutingTaskId()
    {
        var plan    = MakePlan(PlanLifecycleStatus.Executing);
        Assert.That(plan.Progress.ExecutingTaskId, Is.Not.Null, "Precondition: plan has an executing task.");

        var updated = PlanStoreUpdater.ApplyStopped(plan);

        Assert.That(updated.Progress.ExecutingTaskId, Is.Null);
    }

    [Test]
    public void ApplyStopped_WhenNoInterruptionData_ReturnsCorrectly()
    {
        var plan = MakePlan(PlanLifecycleStatus.Executing);
        Assert.That(plan.InterruptionData, Is.Null, "Precondition: no interruption data.");

        var updated = PlanStoreUpdater.ApplyStopped(plan);

        Assert.That(updated.LifecycleStatus,  Is.EqualTo(PlanLifecycleStatus.Stopped));
        Assert.That(updated.InterruptionData, Is.Null);
    }
}
