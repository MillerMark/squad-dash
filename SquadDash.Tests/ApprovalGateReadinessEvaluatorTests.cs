using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ApprovalGateReadinessEvaluatorTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a plan with two parallel lanes meeting at a gate:
    ///   T1 ─┐
    ///        ├── [GATE] ──► T3 ──► T4
    ///   T2 ─┘
    /// Plus an ungated branch: T5 (depends on T1 only, NOT behind the gate).
    /// </summary>
    private static Plan MakeTwoLanePlanWithGateAndUngated(
        string t1Status = PlanTaskStatus.Pending,
        string t2Status = PlanTaskStatus.Pending,
        string gateStatus = PlanGateStatus.Pending)
    {
        var tasks = new[]
        {
            new PlanTask("T1", "Task 1", "desc", [], "mid", t1Status),
            new PlanTask("T2", "Task 2", "desc", [], "mid", t2Status),
            new PlanTask("T3", "Task 3", "desc", ["T1", "T2"], "mid", PlanTaskStatus.Pending),
            new PlanTask("T4", "Task 4", "desc", ["T3"], "mid", PlanTaskStatus.Pending),
            new PlanTask("T5", "Task 5", "desc", ["T1"], "mid", PlanTaskStatus.Pending),
        };
        var gate = new PlanApprovalGate(
            GateId: "GATE-001",
            Message: "Review before T3",
            AfterTaskIds: ["T1", "T2"],
            BeforeTaskIds: ["T3"],
            Status: gateStatus);
        return new Plan(
            PlanId: "PLAN-001",
            Revision: "rev1",
            Source: PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Executing,
            Title: "Two-Lane Plan",
            Branch: "feature/test",
            Summary: "test",
            Tasks: tasks,
            ApprovalGates: [gate],
            Progress: new PlanProgress(0, 5),
            Timestamps: new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Builds a plan with two sequential gates:
    ///   T1 ──[GATE-A]──► T2 ──[GATE-B]──► T3
    /// </summary>
    private static Plan MakeNestedGatePlan(
        string t1Status = PlanTaskStatus.Complete,
        string t2Status = PlanTaskStatus.Pending,
        string gateAStatus = PlanGateStatus.Pending,
        string gateBStatus = PlanGateStatus.Pending)
    {
        var tasks = new[]
        {
            new PlanTask("T1", "Task 1", "desc", [], "mid", t1Status),
            new PlanTask("T2", "Task 2", "desc", ["T1"], "mid", t2Status),
            new PlanTask("T3", "Task 3", "desc", ["T2"], "mid", PlanTaskStatus.Pending),
        };
        var gateA = new PlanApprovalGate(
            GateId: "GATE-A", Message: "First gate",
            AfterTaskIds: ["T1"], BeforeTaskIds: ["T2"], Status: gateAStatus);
        var gateB = new PlanApprovalGate(
            GateId: "GATE-B", Message: "Second gate",
            AfterTaskIds: ["T2"], BeforeTaskIds: ["T3"], Status: gateBStatus);
        return new Plan(
            PlanId: "PLAN-002", Revision: "rev1",
            Source: PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Executing,
            Title: "Nested Gate Plan", Branch: "feature/test", Summary: "test",
            Tasks: tasks, ApprovalGates: [gateA, gateB],
            Progress: new PlanProgress(1, 3),
            Timestamps: new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Builds a plan with two independent gates (parallel lanes):
    ///   T1 ──[GATE-X]──► T3
    ///   T2 ──[GATE-Y]──► T4
    ///   T5 (no gate, depends on nothing)
    /// </summary>
    private static Plan MakeParallelGatePlan(
        string t1Status = PlanTaskStatus.Complete,
        string t2Status = PlanTaskStatus.Complete)
    {
        var tasks = new[]
        {
            new PlanTask("T1", "Task 1", "desc", [], "mid", t1Status),
            new PlanTask("T2", "Task 2", "desc", [], "mid", t2Status),
            new PlanTask("T3", "Task 3", "desc", ["T1"], "mid", PlanTaskStatus.Pending),
            new PlanTask("T4", "Task 4", "desc", ["T2"], "mid", PlanTaskStatus.Pending),
            new PlanTask("T5", "Task 5", "desc", [], "mid", PlanTaskStatus.Pending),
        };
        var gateX = new PlanApprovalGate(
            GateId: "GATE-X", Message: "Gate X",
            AfterTaskIds: ["T1"], BeforeTaskIds: ["T3"], Status: PlanGateStatus.Pending);
        var gateY = new PlanApprovalGate(
            GateId: "GATE-Y", Message: "Gate Y",
            AfterTaskIds: ["T2"], BeforeTaskIds: ["T4"], Status: PlanGateStatus.Pending);
        return new Plan(
            PlanId: "PLAN-003", Revision: "rev1",
            Source: PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Executing,
            Title: "Parallel Gate Plan", Branch: "feature/test", Summary: "test",
            Tasks: tasks, ApprovalGates: [gateX, gateY],
            Progress: new PlanProgress(2, 5),
            Timestamps: new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    // ── EvaluateGates ─────────────────────────────────────────────────────────

    [Test]
    public void EvaluateGates_WhenAfterTasksNotComplete_GateIsNotReady()
    {
        var plan = MakeTwoLanePlanWithGateAndUngated(t1Status: PlanTaskStatus.Complete);
        var states = ApprovalGateReadinessEvaluator.EvaluateGates(plan);

        Assert.That(states, Has.Count.EqualTo(1));
        Assert.That(states[0].IsReady, Is.False);
        Assert.That(states[0].GateId, Is.EqualTo("GATE-001"));
    }

    [Test]
    public void EvaluateGates_WhenAllAfterTasksComplete_GateIsReady()
    {
        var plan = MakeTwoLanePlanWithGateAndUngated(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete);
        var states = ApprovalGateReadinessEvaluator.EvaluateGates(plan);

        Assert.That(states, Has.Count.EqualTo(1));
        Assert.That(states[0].IsReady, Is.True);
    }

    [Test]
    public void EvaluateGates_SkipsApprovedGates()
    {
        var plan = MakeTwoLanePlanWithGateAndUngated(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateStatus: PlanGateStatus.Approved);
        var states = ApprovalGateReadinessEvaluator.EvaluateGates(plan);

        Assert.That(states, Is.Empty);
    }

    [Test]
    public void EvaluateGates_SkipsSkippedGates()
    {
        var plan = MakeTwoLanePlanWithGateAndUngated(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateStatus: PlanGateStatus.Skipped);
        var states = ApprovalGateReadinessEvaluator.EvaluateGates(plan);

        Assert.That(states, Is.Empty);
    }

    // ── ComputeDownstreamFrontier ─────────────────────────────────────────────

    [Test]
    public void ComputeDownstreamFrontier_IncludesDirectAndTransitiveDependents()
    {
        var plan = MakeTwoLanePlanWithGateAndUngated();
        var gate = plan.ApprovalGates[0];
        var frontier = ApprovalGateReadinessEvaluator.ComputeDownstreamFrontier(plan, gate);

        // T3 is in BeforeTaskIds, T4 depends on T3 transitively
        Assert.That(frontier, Does.Contain("T3"));
        Assert.That(frontier, Does.Contain("T4"));
        // T5 depends on T1 but is NOT in the gate's BeforeTaskIds
        Assert.That(frontier, Does.Not.Contain("T5"));
        Assert.That(frontier, Does.Not.Contain("T1"));
        Assert.That(frontier, Does.Not.Contain("T2"));
    }

    [Test]
    public void ComputeDownstreamFrontier_NestedGates_InnerGateFrontierContainsOnlyItsDownstream()
    {
        var plan = MakeNestedGatePlan();
        var gateA = plan.ApprovalGates[0];
        var gateB = plan.ApprovalGates[1];

        var frontierA = ApprovalGateReadinessEvaluator.ComputeDownstreamFrontier(plan, gateA);
        var frontierB = ApprovalGateReadinessEvaluator.ComputeDownstreamFrontier(plan, gateB);

        Assert.That(frontierA, Does.Contain("T2"));
        Assert.That(frontierA, Does.Contain("T3")); // T3 is transitively downstream of gate A
        Assert.That(frontierB, Does.Contain("T3"));
        Assert.That(frontierB, Does.Not.Contain("T2"));
    }

    // ── SelectNextUngatedTask ─────────────────────────────────────────────────

    [Test]
    public void SelectNextUngatedTask_WhenUngatedWorkExists_ReturnsIt()
    {
        var plan = MakeTwoLanePlanWithGateAndUngated(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete);
        var next = ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan);

        // T5 depends only on T1 (complete) and is not behind the gate
        Assert.That(next, Is.EqualTo("T5"));
    }

    [Test]
    public void SelectNextUngatedTask_WhenAllWorkIsGated_ReturnsNull()
    {
        // Remove T5 so only gated T3/T4 remain
        var plan = MakeTwoLanePlanWithGateAndUngated(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete);
        var tasksWithoutT5 = plan.Tasks.Where(t => t.TaskId != "T5").ToList();
        plan = plan with { Tasks = tasksWithoutT5 };

        var next = ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan);
        Assert.That(next, Is.Null);
    }

    [Test]
    public void SelectNextUngatedTask_DeclarationOrder_IsDeterministic()
    {
        // Two ungated pending tasks; T1 and T2 (both roots)
        var plan = MakeTwoLanePlanWithGateAndUngated();
        var next = ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan);

        Assert.That(next, Is.EqualTo("T1")); // First in declaration order
    }

    [Test]
    public void SelectNextUngatedTask_SkipsCompletedAndFailed()
    {
        var tasks = new[]
        {
            new PlanTask("A", "A", "desc", [], "mid", PlanTaskStatus.Complete),
            new PlanTask("B", "B", "desc", [], "mid", PlanTaskStatus.Failed),
            new PlanTask("C", "C", "desc", [], "mid", PlanTaskStatus.Pending),
        };
        var plan = new Plan("P", "r", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "T", "b", "s", tasks, [],
            new PlanProgress(1, 3), new PlanTimestamps(DateTimeOffset.UtcNow));

        Assert.That(ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan), Is.EqualTo("C"));
    }

    // ── ShouldStopForApproval ─────────────────────────────────────────────────

    [Test]
    public void ShouldStopForApproval_WhenUngatedWorkExists_ReturnsFalse()
    {
        var plan = MakeTwoLanePlanWithGateAndUngated(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete);

        Assert.That(ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan), Is.False);
    }

    [Test]
    public void ShouldStopForApproval_WhenOnlyGatedWorkRemains_ReturnsTrue()
    {
        var plan = MakeTwoLanePlanWithGateAndUngated(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete);
        // Complete T5 so only gated work remains
        var updatedTasks = plan.Tasks.Select(t =>
            t.TaskId == "T5" ? t with { Status = PlanTaskStatus.Complete } : t).ToList();
        plan = plan with { Tasks = updatedTasks };

        Assert.That(ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan), Is.True);
    }

    [Test]
    public void ShouldStopForApproval_NoGates_ReturnsFalse()
    {
        var tasks = new[]
        {
            new PlanTask("A", "A", "desc", [], "mid", PlanTaskStatus.Pending),
        };
        var plan = new Plan("P", "r", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "T", "b", "s", tasks, [],
            new PlanProgress(0, 1), new PlanTimestamps(DateTimeOffset.UtcNow));

        Assert.That(ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan), Is.False);
    }

    // ── Multiple simultaneous gates ───────────────────────────────────────────

    [Test]
    public void EvaluateGates_ParallelGates_BothBecomeReady()
    {
        var plan = MakeParallelGatePlan();
        var states = ApprovalGateReadinessEvaluator.EvaluateGates(plan);

        Assert.That(states, Has.Count.EqualTo(2));
        Assert.That(states.All(s => s.IsReady), Is.True);
    }

    [Test]
    public void SelectNextUngatedTask_ParallelGates_ReturnsUngatedTask()
    {
        var plan = MakeParallelGatePlan();
        var next = ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan);

        Assert.That(next, Is.EqualTo("T5")); // Only ungated task
    }

    [Test]
    public void ShouldStopForApproval_ParallelGates_WhenUngatedTaskComplete_ReturnsTrue()
    {
        var plan = MakeParallelGatePlan();
        var updatedTasks = plan.Tasks.Select(t =>
            t.TaskId == "T5" ? t with { Status = PlanTaskStatus.Complete } : t).ToList();
        plan = plan with { Tasks = updatedTasks };

        Assert.That(ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan), Is.True);
    }

    // ── GetReadyGateIds ───────────────────────────────────────────────────────

    [Test]
    public void GetReadyGateIds_ReturnsOnlyReadyGates()
    {
        var plan = MakeTwoLanePlanWithGateAndUngated(
            t1Status: PlanTaskStatus.Complete);
        var states = ApprovalGateReadinessEvaluator.EvaluateGates(plan);
        var readyIds = ApprovalGateReadinessEvaluator.GetReadyGateIds(states);

        Assert.That(readyIds, Is.Empty); // T2 not complete yet
    }

    [Test]
    public void GetReadyGateIds_WhenReady_ReturnsGateId()
    {
        var plan = MakeTwoLanePlanWithGateAndUngated(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete);
        var states = ApprovalGateReadinessEvaluator.EvaluateGates(plan);
        var readyIds = ApprovalGateReadinessEvaluator.GetReadyGateIds(states);

        Assert.That(readyIds, Has.Count.EqualTo(1));
        Assert.That(readyIds[0], Is.EqualTo("GATE-001"));
    }

    // ── GetReleasedTaskIds ────────────────────────────────────────────────────

    [Test]
    public void GetReleasedTaskIds_AfterApproval_ReturnsBeforeTasks()
    {
        var plan = MakeTwoLanePlanWithGateAndUngated(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateStatus: PlanGateStatus.Approved);
        var released = ApprovalGateReadinessEvaluator.GetReleasedTaskIds(plan, "GATE-001");

        Assert.That(released, Does.Contain("T3"));
    }

    [Test]
    public void GetReleasedTaskIds_PartialApproval_StillBlockedByOtherGate()
    {
        var plan = MakeParallelGatePlan();
        // Approve gate X only
        var updatedGates = plan.ApprovalGates.Select(g =>
            g.GateId == "GATE-X" ? g with { Status = PlanGateStatus.Approved } : g).ToList();
        plan = plan with { ApprovalGates = updatedGates };

        var releasedX = ApprovalGateReadinessEvaluator.GetReleasedTaskIds(plan, "GATE-X");
        Assert.That(releasedX, Does.Contain("T3")); // T3 is released from GATE-X

        // T4 is behind GATE-Y which is pending — it stays blocked
        var blockedIds = ApprovalGateReadinessEvaluator.ComputeAllBlockedTaskIds(plan);
        Assert.That(blockedIds, Does.Contain("T4"));
        Assert.That(blockedIds, Does.Not.Contain("T3")); // T3 is no longer blocked
    }

    // ── PlanStoreUpdater.ApplyGateReady ───────────────────────────────────────

    [Test]
    public void ApplyGateReady_SetsGateToAwaitingApproval_KeepsPlanExecuting()
    {
        var plan = MakeTwoLanePlanWithGateAndUngated(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete);

        var updated = PlanStoreUpdater.ApplyGateReady(plan, "GATE-001");

        Assert.That(updated.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.AwaitingApproval));
        Assert.That(updated.ApprovalGates[0].RequestedAt, Is.Not.Null);
        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
    }

    [Test]
    public void ApplyGateReady_AlreadyApproved_ReturnsUnchanged()
    {
        var plan = MakeTwoLanePlanWithGateAndUngated(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateStatus: PlanGateStatus.Approved);

        var updated = PlanStoreUpdater.ApplyGateReady(plan, "GATE-001");
        Assert.That(updated, Is.SameAs(plan));
    }

    [Test]
    public void ApplyGateReady_UnknownGate_ReturnsUnchanged()
    {
        var plan = MakeTwoLanePlanWithGateAndUngated();
        var updated = PlanStoreUpdater.ApplyGateReady(plan, "UNKNOWN");
        Assert.That(updated, Is.SameAs(plan));
    }

    // ── PlanStoreUpdater.ApplyFullStopAtGates ─────────────────────────────────

    [Test]
    public void ApplyFullStopAtGates_TransitionsToPlanAwaitingApproval()
    {
        var plan = MakeParallelGatePlan();
        var updated = PlanStoreUpdater.ApplyFullStopAtGates(plan, ["GATE-X", "GATE-Y"]);

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));
        Assert.That(updated.Progress.ExecutingTaskId, Is.Null);
        Assert.That(updated.ApprovalGates.All(g => g.Status == PlanGateStatus.AwaitingApproval), Is.True);
    }

    [Test]
    public void ApplyFullStopAtGates_EmptyList_ReturnsUnchanged()
    {
        var plan = MakeParallelGatePlan();
        var updated = PlanStoreUpdater.ApplyFullStopAtGates(plan, []);
        Assert.That(updated, Is.SameAs(plan));
    }

    [Test]
    public void ApplyFullStopAtGates_OnlyAffectsPendingGates()
    {
        var plan = MakeParallelGatePlan();
        // Pre-approve GATE-X
        var gatesWithApproved = plan.ApprovalGates.Select(g =>
            g.GateId == "GATE-X" ? g with { Status = PlanGateStatus.Approved } : g).ToList();
        plan = plan with { ApprovalGates = gatesWithApproved };

        var updated = PlanStoreUpdater.ApplyFullStopAtGates(plan, ["GATE-X", "GATE-Y"]);

        // GATE-X stays Approved, GATE-Y moves to AwaitingApproval
        Assert.That(updated.ApprovalGates.First(g => g.GateId == "GATE-X").Status,
            Is.EqualTo(PlanGateStatus.Approved));
        Assert.That(updated.ApprovalGates.First(g => g.GateId == "GATE-Y").Status,
            Is.EqualTo(PlanGateStatus.AwaitingApproval));
    }

    // ── Nested gate scheduling ────────────────────────────────────────────────

    [Test]
    public void NestedGates_OnlyInnerGateIsReady_OuterGateStaysPending()
    {
        var plan = MakeNestedGatePlan();
        var states = ApprovalGateReadinessEvaluator.EvaluateGates(plan);

        var gateAState = states.First(s => s.GateId == "GATE-A");
        var gateBState = states.First(s => s.GateId == "GATE-B");

        Assert.That(gateAState.IsReady, Is.True);  // T1 is complete
        Assert.That(gateBState.IsReady, Is.False); // T2 is not complete
    }

    [Test]
    public void NestedGates_SelectNextUngatedTask_ReturnsNull_WhenAllWorkIsBehindGates()
    {
        var plan = MakeNestedGatePlan();
        var next = ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan);

        Assert.That(next, Is.Null); // T2 and T3 are both downstream of gates
    }

    // ── Stage boundaries ──────────────────────────────────────────────────────

    [Test]
    public void EvaluateGates_SupersededTasksCountAsComplete()
    {
        var tasks = new[]
        {
            new PlanTask("S1", "S1", "desc", [], "mid", PlanTaskStatus.Superseded),
            new PlanTask("S2", "S2", "desc", ["S1"], "mid", PlanTaskStatus.Pending),
        };
        var gate = new PlanApprovalGate(
            GateId: "GATE-S", Message: "Stage gate",
            AfterTaskIds: ["S1"], BeforeTaskIds: ["S2"], Status: PlanGateStatus.Pending);
        var plan = new Plan("PS", "r", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "T", "b", "s", tasks, [gate],
            new PlanProgress(1, 2), new PlanTimestamps(DateTimeOffset.UtcNow));

        var states = ApprovalGateReadinessEvaluator.EvaluateGates(plan);
        Assert.That(states[0].IsReady, Is.True);
    }

    // ── ComputeAllBlockedTaskIds ──────────────────────────────────────────────

    [Test]
    public void ComputeAllBlockedTaskIds_UnionOfAllFrontiers()
    {
        var plan = MakeParallelGatePlan();
        var blocked = ApprovalGateReadinessEvaluator.ComputeAllBlockedTaskIds(plan);

        Assert.That(blocked, Does.Contain("T3"));
        Assert.That(blocked, Does.Contain("T4"));
        Assert.That(blocked, Does.Not.Contain("T5"));
    }

    // ── Restart safety via PlanStoreUpdater ───────────────────────────────────

    [Test]
    public void ApplyGateReady_ThenApplyGateApproved_RestoresCycle()
    {
        var plan = MakeTwoLanePlanWithGateAndUngated(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete);

        // Gate becomes ready (plan stays executing)
        var withReady = PlanStoreUpdater.ApplyGateReady(plan, "GATE-001");
        Assert.That(withReady.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));

        // Human approves
        var withApproved = PlanStoreUpdater.ApplyGateApproved(withReady, "GATE-001", "Looks good");
        Assert.That(withApproved.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(withApproved.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.Approved));
        Assert.That(withApproved.ApprovalGates[0].ResolutionNote, Is.EqualTo("Looks good"));
    }

    [Test]
    public void ApplyFullStopAtGates_ThenApproveOne_PartialApprovalStaysAwaiting()
    {
        var plan = MakeParallelGatePlan();
        var updatedTasks = plan.Tasks.Select(t =>
            t.TaskId == "T5" ? t with { Status = PlanTaskStatus.Complete } : t).ToList();
        plan = plan with { Tasks = updatedTasks };

        var stopped = PlanStoreUpdater.ApplyFullStopAtGates(plan, ["GATE-X", "GATE-Y"]);
        Assert.That(stopped.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));

        // Approve only GATE-X
        var partialApprove = PlanStoreUpdater.ApplyGateApproved(stopped, "GATE-X", null);
        Assert.That(partialApprove.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));

        // Approve GATE-Y too
        var fullyApproved = PlanStoreUpdater.ApplyGateApproved(partialApprove, "GATE-Y", null);
        Assert.That(fullyApproved.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Approved));
    }

    // ── GetTerminalTaskIds ────────────────────────────────────────────────────

    [Test]
    public void GetTerminalTaskIds_IncludesCompleteAndSuperseded()
    {
        var tasks = new[]
        {
            new PlanTask("A", "A", "desc", [], "mid", PlanTaskStatus.Complete),
            new PlanTask("B", "B", "desc", [], "mid", PlanTaskStatus.Superseded),
            new PlanTask("C", "C", "desc", [], "mid", PlanTaskStatus.Pending),
            new PlanTask("D", "D", "desc", [], "mid", PlanTaskStatus.Failed),
        };
        var plan = new Plan("P", "r", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "T", "b", "s", tasks, [],
            new PlanProgress(2, 4), new PlanTimestamps(DateTimeOffset.UtcNow));

        var terminal = ApprovalGateReadinessEvaluator.GetTerminalTaskIds(plan);

        Assert.That(terminal, Does.Contain("A"));
        Assert.That(terminal, Does.Contain("B"));
        Assert.That(terminal, Does.Not.Contain("C"));
        Assert.That(terminal, Does.Not.Contain("D"));
    }
}
