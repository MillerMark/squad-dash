using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash.Tests;

/// <summary>
/// Verifies that <see cref="PendingDecomposePlanAdapter"/> preserves full compatibility
/// between the transient <see cref="PendingDecomposePlan"/> and the canonical
/// <see cref="Plan"/> domain model, and that revision hashes computed by
/// <see cref="PendingDecomposePlanStore.ComputeRevision"/> remain stable across the
/// round-trip conversion.
/// </summary>
[TestFixture]
internal sealed class PendingDecomposePlanAdapterTests
{
    // ─── ToPlan ──────────────────────────────────────────────────────────────

    [Test]
    public void ToPlan_PreservesGroupId_AsPlanId()
    {
        var pending = MakePending("PLANS-20260727");
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);
        Assert.That(plan.PlanId, Is.EqualTo("PLANS-20260727"));
    }

    [Test]
    public void ToPlan_PreservesRevision_Unchanged()
    {
        var pending = MakePending("PLANS-20260727");
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);
        Assert.That(plan.Revision, Is.EqualTo(pending.Revision));
    }

    [Test]
    public void ToPlan_SetsLifecycleStatus_ToStaged()
    {
        var pending = MakePending("PLANS-20260727");
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);
        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Staged));
    }

    [Test]
    public void ToPlan_SetsSource_ToTasksJson()
    {
        var pending = MakePending("PLANS-20260727");
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);
        Assert.That(plan.Source, Is.EqualTo(PlanSource.TasksJson));
    }

    [Test]
    public void ToPlan_PreservesTitle_BranchAndSummary()
    {
        var pending = MakePending("PLANS-20260727");
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Title,   Is.EqualTo(pending.Group.GroupTitle));
            Assert.That(plan.Branch,  Is.EqualTo(pending.Group.Branch));
            Assert.That(plan.Summary, Is.EqualTo(pending.Group.Summary));
        });
    }

    [Test]
    public void ToPlan_ConvertsAllTasks_WithPendingStatus()
    {
        var pending = MakePending("PLANS-20260727");
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks, Has.Count.EqualTo(pending.Group.Tasks.Count));
            Assert.That(plan.Tasks.All(t => t.Status == PlanTaskStatus.Pending), Is.True);
        });
    }

    [Test]
    public void ToPlan_PreservesTaskIds_DescriptionsAndDependencies()
    {
        var pending = MakePending("PLANS-20260727");
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);

        var first = plan.Tasks[0];
        var second = plan.Tasks[1];

        Assert.Multiple(() =>
        {
            Assert.That(first.TaskId,      Is.EqualTo("PLANS-20260727-001"));
            Assert.That(first.DependsOn,   Is.Empty);
            Assert.That(second.TaskId,     Is.EqualTo("PLANS-20260727-002"));
            Assert.That(second.DependsOn,  Is.EquivalentTo(new[] { "PLANS-20260727-001" }));
        });
    }

    [Test]
    public void ToPlan_SetsProgress_TotalEqualToNonSupersededTaskCount()
    {
        var pending = MakePending("PLANS-20260727");
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Progress.TotalCount,    Is.EqualTo(2));
            Assert.That(plan.Progress.CompletedCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void ToPlan_PreservesTimestamp_AsCreatedAt()
    {
        var timestamp = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        var pending = MakePending("PLANS-20260727");
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, timestamp);

        Assert.That(plan.Timestamps.CreatedAt, Is.EqualTo(timestamp));
    }

    [Test]
    public void ToPlan_SetsApprovalGates_ToEmpty()
    {
        var pending = MakePending("PLANS-20260727");
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);
        Assert.That(plan.ApprovalGates, Is.Empty);
    }

    // ─── FromPlan ────────────────────────────────────────────────────────────

    [Test]
    public void FromPlan_ReconstructsGroupId()
    {
        var plan = MakePlan("PLANS-20260727");
        var pending = PendingDecomposePlanAdapter.FromPlan(plan);
        Assert.That(pending.Group.GroupId, Is.EqualTo("PLANS-20260727"));
    }

    [Test]
    public void FromPlan_PreservesRevision()
    {
        var plan = MakePlan("PLANS-20260727");
        var pending = PendingDecomposePlanAdapter.FromPlan(plan);
        Assert.That(pending.Revision, Is.EqualTo(plan.Revision));
    }

    [Test]
    public void FromPlan_ReconstructsAllTasks()
    {
        var plan = MakePlan("PLANS-20260727");
        var pending = PendingDecomposePlanAdapter.FromPlan(plan);

        Assert.That(pending.Group.Tasks, Has.Count.EqualTo(plan.Tasks.Count));
    }

    [Test]
    public void FromPlan_ReconstructsTaskIds_AndDependencies()
    {
        var plan = MakePlan("PLANS-20260727");
        var pending = PendingDecomposePlanAdapter.FromPlan(plan);

        var first  = pending.Group.Tasks[0];
        var second = pending.Group.Tasks[1];

        Assert.Multiple(() =>
        {
            Assert.That(first.Id,       Is.EqualTo("PLANS-20260727-001"));
            Assert.That(first.DependsOn, Is.Empty);
            Assert.That(second.Id,      Is.EqualTo("PLANS-20260727-002"));
            Assert.That(second.DependsOn, Is.EquivalentTo(new[] { "PLANS-20260727-001" }));
        });
    }

    // ─── Round-trip revision compatibility ──────────────────────────────────

    [Test]
    public void RevisionIsValid_ForFreshPlan_ReturnsTrue()
    {
        var pending = MakePending("PLANS-20260727");
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);

        Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(plan), Is.True);
    }

    [Test]
    public void RevisionIsValid_AfterRoundTrip_ReturnsTrue()
    {
        var pending = MakePending("PLANS-20260727");
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);
        var reconstructed = PendingDecomposePlanAdapter.FromPlan(plan);

        // The revision computed from the reconstructed group must match the stored revision
        var recomputed = PendingDecomposePlanStore.ComputeRevision(reconstructed.Group);
        Assert.That(recomputed, Is.EqualTo(plan.Revision));
    }

    [Test]
    public void RevisionIsValid_AfterTaskModification_ReturnsFalse()
    {
        var pending = MakePending("PLANS-20260727");
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow);

        // Simulate tampered task description — revision should no longer match
        var tamperedTasks = plan.Tasks.ToList();
        tamperedTasks[0] = tamperedTasks[0] with { Description = "Tampered description!" };
        var tampered = plan with { Tasks = tamperedTasks };

        Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(tampered), Is.False);
    }

    [Test]
    public void RevisionIsValid_WhenSealedApprovalGateIsDropped_ReturnsFalse()
    {
        var basePending = MakePending("PLANS-20260727");
        var group = basePending.Group with
        {
            ApprovalGates =
            [
                new DecomposedGate(
                    "PLANS-20260727-G01",
                    "Review before continuing.",
                    ["PLANS-20260727-001"],
                    ["PLANS-20260727-002"]),
            ],
        };
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var plan = PendingDecomposePlanAdapter.ToPlan(
            new PendingDecomposePlan(revision, group),
            DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(plan.ApprovalGates, Has.Count.EqualTo(1));
            Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(plan), Is.True);
            Assert.That(
                PendingDecomposePlanAdapter.RevisionIsValid(plan with { ApprovalGates = [] }),
                Is.False,
                "Execution must reject a durable projection that drops an approved gate.");
        });
    }

    [Test]
    public void ToPlan_ThenFromPlan_RevisionMatchesPendingDecomposePlanStore()
    {
        // Full compatibility test: PendingDecomposePlanStore.ComputeRevision should agree
        // with the revision carried through ToPlan → FromPlan.
        var group = new DecomposedTaskGroup(
            GroupId:    "PLANS-20260727",
            GroupTitle: "Compatibility check plan",
            Branch:     "feature/compat",
            Summary:    "Tests adapter compatibility",
            Tasks:
            [
                new DecomposedSubTask(
                    Id:          "PLANS-20260727-001",
                    Description: "First task",
                    DependsOn:   [],
                    Priority:    "critical",
                    Title:       "Do something"),
                new DecomposedSubTask(
                    Id:          "PLANS-20260727-002",
                    Description: "Second task",
                    DependsOn:   ["PLANS-20260727-001"],
                    Priority:    "high",
                    Title:       "Do more"),
            ]);

        var originalRevision = PendingDecomposePlanStore.ComputeRevision(group);
        var originalPending  = new PendingDecomposePlan(originalRevision, group);

        var plan       = PendingDecomposePlanAdapter.ToPlan(originalPending, DateTimeOffset.UtcNow);
        var backPending = PendingDecomposePlanAdapter.FromPlan(plan);
        var recomputed = PendingDecomposePlanStore.ComputeRevision(backPending.Group);

        Assert.That(recomputed, Is.EqualTo(originalRevision));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static PendingDecomposePlan MakePending(string groupId)
    {
        var group = new DecomposedTaskGroup(
            GroupId:    groupId,
            GroupTitle: "Test plan for adapter",
            Branch:     "feature/test",
            Summary:    "Created for adapter tests",
            Tasks:
            [
                new DecomposedSubTask(
                    Id:          $"{groupId}-001",
                    Description: "First step description",
                    DependsOn:   [],
                    Priority:    "high",
                    Title:       "First step"),
                new DecomposedSubTask(
                    Id:          $"{groupId}-002",
                    Description: "Second step description",
                    DependsOn:   [$"{groupId}-001"],
                    Priority:    "high",
                    Title:       "Second step"),
            ]);

        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        return new PendingDecomposePlan(revision, group);
    }

    private static Plan MakePlan(string planId) =>
        PendingDecomposePlanAdapter.ToPlan(MakePending(planId), DateTimeOffset.UtcNow);
}
