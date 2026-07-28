using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanStoreUpdaterGateTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static Plan MakePlanWithGate(string gateStatus = PlanGateStatus.Pending)
    {
        var tasks = new[]
        {
            new PlanTask("TEST-20260101-001", "Task 1", "desc", [], "mid", PlanTaskStatus.Complete),
            new PlanTask("TEST-20260101-002", "Task 2", "desc", ["TEST-20260101-001"], "mid", PlanTaskStatus.Pending),
        };
        var gate = new PlanApprovalGate(
            GateId:       "TEST-20260101-GATE-001",
            Message:      "Review before continuing",
            AfterTaskIds: ["TEST-20260101-001"],
            BeforeTaskIds: ["TEST-20260101-002"],
            Status:       gateStatus);
        return new Plan(
            PlanId:          "TEST-20260101",
            Revision:        "rev1",
            Source:          PlanSource.TasksJson,
            LifecycleStatus: PlanLifecycleStatus.Executing,
            Title:           "Test",
            Branch:          "feature/test",
            Summary:         "test",
            Tasks:           tasks,
            ApprovalGates:   [gate],
            Progress:        new PlanProgress(1, 2, "TEST-20260101-001"),
            Timestamps:      new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    private const string KnownGateId    = "TEST-20260101-GATE-001";
    private const string UnknownGateId  = "TEST-20260101-GATE-999";

    // ── ApplyGateActivated ────────────────────────────────────────────────────

    [Test]
    public void ApplyGateActivated_SetsStatusAndRequestedAt()
    {
        var before  = DateTimeOffset.UtcNow;
        var plan    = MakePlanWithGate();
        var updated = PlanStoreUpdater.ApplyGateActivated(plan, KnownGateId);

        var gate = updated.ApprovalGates[0];
        Assert.That(gate.Status,      Is.EqualTo(PlanGateStatus.AwaitingApproval));
        Assert.That(gate.RequestedAt, Is.Not.Null);
        Assert.That(gate.RequestedAt!.Value, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void ApplyGateActivated_SetsPlanLifecycleToAwaitingApproval()
    {
        var plan    = MakePlanWithGate();
        var updated = PlanStoreUpdater.ApplyGateActivated(plan, KnownGateId);

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));
    }

    [Test]
    public void ApplyGateActivated_ClearsExecutingTaskId()
    {
        var plan    = MakePlanWithGate();
        var updated = PlanStoreUpdater.ApplyGateActivated(plan, KnownGateId);

        Assert.That(updated.Progress.ExecutingTaskId, Is.Null);
    }

    [Test]
    public void ApplyGateActivated_UnknownGate_ReturnsUnchanged()
    {
        var plan    = MakePlanWithGate();
        var updated = PlanStoreUpdater.ApplyGateActivated(plan, UnknownGateId);

        Assert.That(ReferenceEquals(updated, plan), Is.True,
            "Plan must be returned unchanged when gateId is not found.");
    }

    // ── ApplyGateApproved ─────────────────────────────────────────────────────

    [Test]
    public void ApplyGateApproved_SetsStatusAndTimestamps()
    {
        var before  = DateTimeOffset.UtcNow;
        var plan    = MakePlanWithGate(PlanGateStatus.AwaitingApproval);
        var updated = PlanStoreUpdater.ApplyGateApproved(plan, KnownGateId, note: null);

        var gate = updated.ApprovalGates[0];
        Assert.That(gate.Status,     Is.EqualTo(PlanGateStatus.Approved));
        Assert.That(gate.ResolvedAt, Is.Not.Null);
        Assert.That(gate.ResolvedAt!.Value, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void ApplyGateApproved_SetsResolutionNote()
    {
        var plan    = MakePlanWithGate(PlanGateStatus.AwaitingApproval);
        var updated = PlanStoreUpdater.ApplyGateApproved(plan, KnownGateId, note: "LGTM");

        Assert.That(updated.ApprovalGates[0].ResolutionNote, Is.EqualTo("LGTM"));
    }

    [Test]
    public void ApplyGateApproved_WhenNoOtherGatesAwaitingApproval_TransitionsPlanToExecuting()
    {
        var plan    = MakePlanWithGate(PlanGateStatus.AwaitingApproval) with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
        };
        var updated = PlanStoreUpdater.ApplyGateApproved(plan, KnownGateId, note: null);

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
    }

    [Test]
    public void ApplyGateApproved_WhenAnotherGateStillAwaiting_KeepsPlanAwaitingApproval()
    {
        var tasks = new[]
        {
            new PlanTask("TEST-20260101-001", "Task 1", "desc", [], "mid", PlanTaskStatus.Complete),
            new PlanTask("TEST-20260101-002", "Task 2", "desc", ["TEST-20260101-001"], "mid", PlanTaskStatus.Pending),
            new PlanTask("TEST-20260101-003", "Task 3", "desc", ["TEST-20260101-002"], "mid", PlanTaskStatus.Pending),
        };
        var gate1 = new PlanApprovalGate("G1", "Gate 1", ["TEST-20260101-001"], ["TEST-20260101-002"], PlanGateStatus.AwaitingApproval);
        var gate2 = new PlanApprovalGate("G2", "Gate 2", ["TEST-20260101-002"], ["TEST-20260101-003"], PlanGateStatus.AwaitingApproval);
        var plan = new Plan(
            PlanId: "TEST-20260101", Revision: "rev1", Source: PlanSource.TasksJson,
            LifecycleStatus: PlanLifecycleStatus.AwaitingApproval,
            Title: "Test", Branch: "feature/test", Summary: "test",
            Tasks: tasks, ApprovalGates: [gate1, gate2],
            Progress: new PlanProgress(1, 3, null),
            Timestamps: new PlanTimestamps(DateTimeOffset.UtcNow));

        var updated = PlanStoreUpdater.ApplyGateApproved(plan, "G1", note: null);

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval),
            "Plan must remain AwaitingApproval while gate G2 is still pending.");
    }

    [Test]
    public void ApplyGateApproved_UnknownGate_ReturnsUnchanged()
    {
        var plan    = MakePlanWithGate(PlanGateStatus.AwaitingApproval);
        var updated = PlanStoreUpdater.ApplyGateApproved(plan, UnknownGateId, note: null);

        Assert.That(ReferenceEquals(updated, plan), Is.True,
            "Plan must be returned unchanged when gateId is not found.");
    }

    [Test]
    public void ApplyGateApproved_GateNotAwaitingApproval_ReturnsUnchanged()
    {
        var plan    = MakePlanWithGate(PlanGateStatus.Pending);
        var updated = PlanStoreUpdater.ApplyGateApproved(plan, KnownGateId, note: null);

        Assert.That(ReferenceEquals(updated, plan), Is.True,
            "Plan must be returned unchanged when gate is not in AwaitingApproval status.");
    }
}
