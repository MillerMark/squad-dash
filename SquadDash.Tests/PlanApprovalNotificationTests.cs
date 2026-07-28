using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanApprovalNotificationTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static Plan MakePlanWithGate(
        string gateStatus = PlanGateStatus.Pending,
        DateTimeOffset? notifiedAt = null)
    {
        var tasks = new[]
        {
            new PlanTask("TEST-20260101-001", "Task 1", "desc", [], "mid", PlanTaskStatus.Complete),
            new PlanTask("TEST-20260101-002", "Task 2", "desc", ["TEST-20260101-001"], "mid", PlanTaskStatus.Pending),
        };
        var gate = new PlanApprovalGate(
            GateId:        "TEST-20260101-GATE-001",
            Message:       "Review before continuing",
            AfterTaskIds:  ["TEST-20260101-001"],
            BeforeTaskIds: ["TEST-20260101-002"],
            Status:        gateStatus,
            NotifiedAt:    notifiedAt);
        return new Plan(
            PlanId:          "TEST-20260101",
            Revision:        "rev1",
            Source:          PlanSource.TasksJson,
            LifecycleStatus: PlanLifecycleStatus.Executing,
            Title:           "Test Plan",
            Branch:          "feature/test",
            Summary:         "test",
            Tasks:           tasks,
            ApprovalGates:   [gate],
            Progress:        new PlanProgress(1, 2, "TEST-20260101-001"),
            Timestamps:      new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    private const string KnownGateId   = "TEST-20260101-GATE-001";
    private const string UnknownGateId = "TEST-20260101-GATE-999";

    // ── ApplyGateActivated — NotifiedAt tracking ───────────────────────────

    [Test]
    public void ApplyGateActivated_SetsNotifiedAtOnFirstActivation()
    {
        var before  = DateTimeOffset.UtcNow;
        var plan    = MakePlanWithGate();                 // NotifiedAt is null
        var updated = PlanStoreUpdater.ApplyGateActivated(plan, KnownGateId);
        var gate    = updated.ApprovalGates[0];

        Assert.That(gate.NotifiedAt, Is.Not.Null);
        Assert.That(gate.NotifiedAt!.Value, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void ApplyGateActivated_PreservesNotifiedAtOnReactivation()
    {
        var originalNotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        // Simulate a gate that was already notified (NotifiedAt already set).
        var plan    = MakePlanWithGate(notifiedAt: originalNotifiedAt);
        var updated = PlanStoreUpdater.ApplyGateActivated(plan, KnownGateId);
        var gate    = updated.ApprovalGates[0];

        Assert.That(gate.NotifiedAt, Is.EqualTo(originalNotifiedAt),
            "NotifiedAt should not be overwritten when already set");
    }

    [Test]
    public void ApplyGateActivated_UnknownGate_ReturnsUnchanged()
    {
        var plan    = MakePlanWithGate();
        var updated = PlanStoreUpdater.ApplyGateActivated(plan, UnknownGateId);

        Assert.That(ReferenceEquals(plan, updated), Is.True,
            "Unknown gateId should return the same plan instance");
    }

    // ── ShouldNotifyGateActivation guard logic ─────────────────────────────

    [Test]
    public void ShouldNotifyGateActivation_NewGate_ReturnsTrue()
    {
        var gate = new PlanApprovalGate(
            GateId:        "G1",
            Message:       "Gate",
            AfterTaskIds:  [],
            BeforeTaskIds: [],
            Status:        PlanGateStatus.Pending,
            NotifiedAt:    null);

        Assert.That(PlanGateManager.ShouldNotifyGateActivation(gate), Is.True);
    }

    [Test]
    public void ShouldNotifyGateActivation_AlreadyNotified_ReturnsFalse()
    {
        var gate = new PlanApprovalGate(
            GateId:        "G1",
            Message:       "Gate",
            AfterTaskIds:  [],
            BeforeTaskIds: [],
            Status:        PlanGateStatus.AwaitingApproval,
            NotifiedAt:    DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.That(PlanGateManager.ShouldNotifyGateActivation(gate), Is.False);
    }
}
