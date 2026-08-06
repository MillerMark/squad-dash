using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanGateManagerTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Plan MakePlan(params (string Id, string[] DependsOn)[] taskSpecs)
    {
        var tasks = taskSpecs.Select(spec => new PlanTask(
            TaskId:      spec.Id,
            Title:       spec.Id,
            Description: "desc",
            DependsOn:   spec.DependsOn,
            Priority:    "mid",
            Status:      PlanTaskStatus.Pending)).ToArray();
        return new Plan(
            PlanId:          "TEST-20260101",
            Revision:        "rev1",
            Source:          PlanSource.TasksJson,
            LifecycleStatus: PlanLifecycleStatus.Approved,
            Title:           "Test Plan",
            Branch:          "feature/test",
            Summary:         "Test",
            Tasks:           tasks,
            ApprovalGates:   [],
            Progress:        new PlanProgress(0, tasks.Length),
            Timestamps:      new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    // ─── IsRootTask ──────────────────────────────────────────────────────────

    [Test]
    public void IsRootTask_ReturnsTrueForTaskWithNoDependsOn()
    {
        var plan = MakePlan(("TEST-20260101-001", []), ("TEST-20260101-002", ["TEST-20260101-001"]));
        Assert.That(PlanGateManager.IsRootTask(plan, "TEST-20260101-001"), Is.True);
    }

    [Test]
    public void IsRootTask_ReturnsFalseForTaskWithDependencies()
    {
        var plan = MakePlan(("TEST-20260101-001", []), ("TEST-20260101-002", ["TEST-20260101-001"]));
        Assert.That(PlanGateManager.IsRootTask(plan, "TEST-20260101-002"), Is.False);
    }

    [Test]
    public void IsRootTask_ReturnsFalseForUnknownTask()
    {
        var plan = MakePlan(("TEST-20260101-001", []));
        Assert.That(PlanGateManager.IsRootTask(plan, "UNKNOWN"), Is.False);
    }

    // ─── IsLeafTask ──────────────────────────────────────────────────────────

    [Test]
    public void IsLeafTask_ReturnsTrueForTaskWithNoDependants()
    {
        var plan = MakePlan(("TEST-20260101-001", []), ("TEST-20260101-002", ["TEST-20260101-001"]));
        Assert.That(PlanGateManager.IsLeafTask(plan, "TEST-20260101-002"), Is.True);
    }

    [Test]
    public void IsLeafTask_ReturnsFalseForTaskThatOthersDependOn()
    {
        var plan = MakePlan(("TEST-20260101-001", []), ("TEST-20260101-002", ["TEST-20260101-001"]));
        Assert.That(PlanGateManager.IsLeafTask(plan, "TEST-20260101-001"), Is.False);
    }

    // ─── NewGateId ───────────────────────────────────────────────────────────

    [Test]
    public void NewGateId_GeneratesGate001ForFirstGate()
    {
        var plan = MakePlan(("TEST-20260101-001", []), ("TEST-20260101-002", ["TEST-20260101-001"]));
        Assert.That(PlanGateManager.NewGateId(plan), Is.EqualTo("TEST-20260101-GATE-001"));
    }

    [Test]
    public void NewGateId_GeneratesGate002ForSecondGate()
    {
        var plan = MakePlan(("TEST-20260101-001", []), ("TEST-20260101-002", ["TEST-20260101-001"]), ("TEST-20260101-003", ["TEST-20260101-002"]));
        var existingGate = new PlanApprovalGate(
            GateId: "TEST-20260101-GATE-001",
            Message: "First gate",
            AfterTaskIds: ["TEST-20260101-001"],
            BeforeTaskIds: ["TEST-20260101-002"],
            Status: PlanGateStatus.Pending);
        plan = plan with { ApprovalGates = [existingGate] };
        Assert.That(PlanGateManager.NewGateId(plan), Is.EqualTo("TEST-20260101-GATE-002"));
    }

    // ─── AddGateBefore ───────────────────────────────────────────────────────

    [Test]
    public void AddGateBefore_OnRootTask_ReturnsPlanUnchanged()
    {
        var plan = MakePlan(("TEST-20260101-001", []), ("TEST-20260101-002", ["TEST-20260101-001"]));
        var result = PlanGateManager.AddGateBefore(plan, "TEST-20260101-001", "msg");
        Assert.That(ReferenceEquals(result, plan), Is.True);
    }

    [Test]
    public void AddGateBefore_OnNonRootTask_AddsGateWithCorrectBoundary()
    {
        var plan = MakePlan(("TEST-20260101-001", []), ("TEST-20260101-002", ["TEST-20260101-001"]));
        var result = PlanGateManager.AddGateBefore(plan, "TEST-20260101-002", "Review before 002");

        Assert.That(result.ApprovalGates, Has.Count.EqualTo(1));
        var gate = result.ApprovalGates[0];
        Assert.Multiple(() =>
        {
            Assert.That(gate.AfterTaskIds,  Is.EquivalentTo(new[] { "TEST-20260101-001" }));
            Assert.That(gate.BeforeTaskIds, Is.EquivalentTo(new[] { "TEST-20260101-002" }));
            Assert.That(gate.Message,       Is.EqualTo("Review before 002"));
            Assert.That(gate.Status,        Is.EqualTo(PlanGateStatus.Pending));
        });
    }

    [Test]
    public void AddGateBefore_WhenEquivalentGateExists_ReturnsPlanUnchanged()
    {
        var plan = MakePlan(("TEST-20260101-001", []), ("TEST-20260101-002", ["TEST-20260101-001"]));
        var first = PlanGateManager.AddGateBefore(plan, "TEST-20260101-002", "First gate");
        var second = PlanGateManager.AddGateBefore(first, "TEST-20260101-002", "Duplicate gate");

        Assert.That(second.ApprovalGates, Has.Count.EqualTo(1));
    }

    // ─── AddGateAfter ────────────────────────────────────────────────────────

    [Test]
    public void AddGateAfter_OnLeafTask_ReturnsPlanUnchanged()
    {
        var plan = MakePlan(("TEST-20260101-001", []), ("TEST-20260101-002", ["TEST-20260101-001"]));
        var result = PlanGateManager.AddGateAfter(plan, "TEST-20260101-002", "msg");
        Assert.That(ReferenceEquals(result, plan), Is.True);
    }

    [Test]
    public void AddGateAfter_OnNonLeafTask_AddsGateWithCorrectBoundary()
    {
        var plan = MakePlan(("TEST-20260101-001", []), ("TEST-20260101-002", ["TEST-20260101-001"]));
        var result = PlanGateManager.AddGateAfter(plan, "TEST-20260101-001", "Review after 001");

        Assert.That(result.ApprovalGates, Has.Count.EqualTo(1));
        var gate = result.ApprovalGates[0];
        Assert.Multiple(() =>
        {
            Assert.That(gate.AfterTaskIds,  Is.EquivalentTo(new[] { "TEST-20260101-001" }));
            Assert.That(gate.BeforeTaskIds, Is.EquivalentTo(new[] { "TEST-20260101-002" }));
            Assert.That(gate.Message,       Is.EqualTo("Review after 001"));
            Assert.That(gate.Status,        Is.EqualTo(PlanGateStatus.Pending));
        });
    }

    [Test]
    public void AddGateAfter_WhenEquivalentGateExists_ReturnsPlanUnchanged()
    {
        var plan = MakePlan(("TEST-20260101-001", []), ("TEST-20260101-002", ["TEST-20260101-001"]));
        var first = PlanGateManager.AddGateAfter(plan, "TEST-20260101-001", "First gate");
        var second = PlanGateManager.AddGateAfter(first, "TEST-20260101-001", "Duplicate gate");

        Assert.That(second.ApprovalGates, Has.Count.EqualTo(1));
    }

    // ─── Arbitrary graph boundaries ─────────────────────────────────────────

    [Test]
    public void AddBoundaryGate_CreatesSingleMultiTaskMilestone()
    {
        var plan = MakePlan(
            ("TEST-20260101-001", []),
            ("TEST-20260101-002", []),
            ("TEST-20260101-003", ["TEST-20260101-001", "TEST-20260101-002"]));

        var result = PlanGateManager.AddBoundaryGate(
            plan,
            ["TEST-20260101-001", "TEST-20260101-002"],
            ["TEST-20260101-003"],
            "Review the milestone");

        Assert.That(result.ApprovalGates, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result.ApprovalGates[0].AfterTaskIds,
                Is.EquivalentTo(new[] { "TEST-20260101-001", "TEST-20260101-002" }));
            Assert.That(result.ApprovalGates[0].BeforeTaskIds,
                Is.EquivalentTo(new[] { "TEST-20260101-003" }));
        });
    }

    [Test]
    public void AddBoundaryGate_RejectsUnknownOrOverlappingTaskSets()
    {
        var plan = MakePlan(
            ("TEST-20260101-001", []),
            ("TEST-20260101-002", ["TEST-20260101-001"]));

        var unknown = PlanGateManager.AddBoundaryGate(
            plan, ["TEST-20260101-001"], ["UNKNOWN"], "Unknown");
        var overlapping = PlanGateManager.AddBoundaryGate(
            plan, ["TEST-20260101-001"], ["TEST-20260101-001"], "Overlap");

        Assert.Multiple(() =>
        {
            Assert.That(ReferenceEquals(unknown, plan), Is.True);
            Assert.That(ReferenceEquals(overlapping, plan), Is.True);
        });
    }

    [Test]
    public void AddBoundaryGate_WithGroupNormalization_RemovesOnlyFullySubsumedTaskGate()
    {
        var plan = MakePlan(
            ("A", []), ("B", []),
            ("C", ["A", "B"]),
            ("D", ["A"]));
        plan = PlanGateManager.AddGateAfter(plan, "A", "Review A");
        plan = PlanGateManager.AddGateAfter(plan, "B", "Review B");

        var result = PlanGateManager.AddBoundaryGate(
            plan, ["A", "B"], ["C"], "Review ALL", "all:C",
            removeSubsumedTaskGates: true);

        Assert.That(result.ApprovalGates, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(result.ApprovalGates.Any(g => g.AfterTaskIds.SequenceEqual(new[] { "B" })), Is.False);
            Assert.That(result.ApprovalGates.Any(g => g.AfterTaskIds.SequenceEqual(new[] { "A" })), Is.True,
                "A's additional edge to D means its exit approval is not redundant.");
            Assert.That(result.ApprovalGates.Single(g => g.AfterTaskIds.Count == 2).PresentationAnchor,
                Is.EqualTo("all:C"));
        });
    }

    [Test]
    public void SetPresentationAnchor_TransfersEquivalentControlsWithoutChangingBoundary()
    {
        var plan = MakePlan(("A", []), ("B", ["A"]));
        plan = PlanGateManager.AddBoundaryGate(plan, ["A"], ["B"], "Review", "all:B");

        var result = PlanGateManager.SetPresentationAnchor(
            plan, plan.ApprovalGates[0].GateId, "task-before:B");

        Assert.Multiple(() =>
        {
            Assert.That(result.ApprovalGates, Has.Count.EqualTo(1));
            Assert.That(result.ApprovalGates[0].AfterTaskIds, Is.EquivalentTo(new[] { "A" }));
            Assert.That(result.ApprovalGates[0].BeforeTaskIds, Is.EquivalentTo(new[] { "B" }));
            Assert.That(result.ApprovalGates[0].PresentationAnchor, Is.EqualTo("task-before:B"));
        });
    }

    // ─── RemoveGate ──────────────────────────────────────────────────────────

    [Test]
    public void RemoveGate_RemovesCorrectGate()
    {
        var plan = MakePlan(("TEST-20260101-001", []), ("TEST-20260101-002", ["TEST-20260101-001"]), ("TEST-20260101-003", ["TEST-20260101-002"]));
        var withGate = PlanGateManager.AddGateAfter(plan, "TEST-20260101-001", "Gate A");
        var withGate2 = PlanGateManager.AddGateAfter(withGate, "TEST-20260101-002", "Gate B");
        Assert.That(withGate2.ApprovalGates, Has.Count.EqualTo(2));

        var gateId = withGate2.ApprovalGates[0].GateId;
        var result = PlanGateManager.RemoveGate(withGate2, gateId);

        Assert.That(result.ApprovalGates, Has.Count.EqualTo(1));
        Assert.That(result.ApprovalGates[0].Message, Is.EqualTo("Gate B"));
    }

    [Test]
    public void RemoveGate_OnMissingGateId_ReturnsPlanUnchanged()
    {
        var plan = MakePlan(("TEST-20260101-001", []), ("TEST-20260101-002", ["TEST-20260101-001"]));
        var result = PlanGateManager.RemoveGate(plan, "DOES-NOT-EXIST-GATE-001");
        Assert.That(ReferenceEquals(result, plan), Is.True);
    }

    [TestCase(PlanGateStatus.AwaitingApproval)]
    [TestCase(PlanGateStatus.Approved)]
    [TestCase(PlanGateStatus.Skipped)]
    public void RemoveGate_NonPendingGate_PreservesDurableDecision(string status)
    {
        var plan = MakePlan(("A", []), ("B", ["A"]));
        plan = PlanGateManager.AddGateAfter(plan, "A", "Review A");
        plan = plan with
        {
            ApprovalGates = [plan.ApprovalGates[0] with { Status = status }],
        };

        var result = PlanGateManager.RemoveGate(plan, plan.ApprovalGates[0].GateId);

        Assert.That(ReferenceEquals(result, plan), Is.True);
    }

    [Test]
    public void SetPresentationAnchor_ApprovedGate_PreservesAcceptedAnchor()
    {
        var plan = MakePlan(("A", []), ("B", ["A"]));
        plan = PlanGateManager.AddBoundaryGate(plan, ["A"], ["B"], "Review", "task-after:A");
        plan = plan with
        {
            ApprovalGates = [plan.ApprovalGates[0] with { Status = PlanGateStatus.Approved }],
        };

        var result = PlanGateManager.SetPresentationAnchor(
            plan, plan.ApprovalGates[0].GateId, "task-before:B");

        Assert.That(ReferenceEquals(result, plan), Is.True);
        Assert.That(result.ApprovalGates[0].PresentationAnchor, Is.EqualTo("task-after:A"));
    }

    [Test]
    public void ApplyEditableGateChanges_StaleViewerCannotDeleteApprovedGate()
    {
        var current = MakePlan(("A", []), ("B", ["A"]));
        current = PlanGateManager.AddBoundaryGate(current, ["A"], ["B"], "Review", "task-after:A");
        current = current with
        {
            ApprovalGates = [current.ApprovalGates[0] with
            {
                Status = PlanGateStatus.Approved,
                ResolvedBy = "Human",
                ResolutionNote = "Accepted",
            }],
        };
        var staleViewerProposal = current with { ApprovalGates = [] };

        var result = PlanGateManager.ApplyEditableGateChanges(current, staleViewerProposal);

        Assert.That(ReferenceEquals(result, current), Is.True);
        Assert.That(result.ApprovalGates.Single().Status, Is.EqualTo(PlanGateStatus.Approved));
        Assert.That(result.ApprovalGates.Single().ResolvedBy, Is.EqualTo("Human"));
    }
}
