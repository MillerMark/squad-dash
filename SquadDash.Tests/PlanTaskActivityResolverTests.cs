using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanTaskActivityResolverTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PlanTask MakeTask(
        string taskId,
        string status = PlanTaskStatus.Pending,
        IReadOnlyList<string>? dependsOn = null) =>
        new(
            TaskId:      taskId,
            Title:       $"Task {taskId}",
            Description: $"Description for {taskId}",
            DependsOn:   dependsOn ?? [],
            Priority:    "high",
            Status:      status);

    private static Plan MakePlan(
        string lifecycleStatus = PlanLifecycleStatus.Executing,
        IReadOnlyList<PlanTask>? tasks = null,
        IReadOnlyList<PlanApprovalGate>? gates = null,
        int completedCount = 0,
        int totalCount = 5) =>
        new(
            PlanId:          "PLAN-TEST",
            Revision:        "rev1",
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: lifecycleStatus,
            Title:           "Test Plan",
            Branch:          "feature/test",
            Summary:         "A test plan",
            Tasks:           tasks ?? [],
            ApprovalGates:   gates ?? [],
            Progress:        new PlanProgress(completedCount, totalCount),
            Timestamps:      new PlanTimestamps(CreatedAt: DateTimeOffset.UtcNow));

    private static PlanApprovalGate MakeGate(
        string gateId,
        string status,
        IReadOnlyList<string> afterTaskIds,
        IReadOnlyList<string> beforeTaskIds) =>
        new(
            GateId:        gateId,
            Message:       $"Approve {gateId}",
            AfterTaskIds:  afterTaskIds,
            BeforeTaskIds: beforeTaskIds,
            Status:        status);

    // ── Per-task resolution tests ────────────────────────────────────────────

    [Test]
    public void ExecutingTask_ResolvesToExecuting()
    {
        var tasks = new[] { MakeTask("t1", PlanTaskStatus.Executing) };
        var plan = MakePlan(tasks: tasks);

        var result = PlanTaskActivityResolver.Resolve(plan);

        Assert.That(result["t1"], Is.EqualTo(PlanTaskActivityState.Executing));
    }

    [Test]
    public void VerificationPendingTask_ResolvesWithoutActiveSpinnerState()
    {
        var tasks = new[] { MakeTask("t1", PlanTaskStatus.VerificationPending) };
        var plan = MakePlan(tasks: tasks);

        var result = PlanTaskActivityResolver.Resolve(plan);

        Assert.That(result["t1"], Is.EqualTo(PlanTaskActivityState.VerificationPending));
    }

    [TestCase(PlanLifecycleStatus.Approved)]
    [TestCase(PlanLifecycleStatus.AwaitingApproval)]
    public void StaleVerifyingTask_WithoutExecutingLifecycle_ResolvesAsPending(string lifecycleStatus)
    {
        var plan = MakePlan(
            lifecycleStatus: lifecycleStatus,
            tasks: [MakeTask("t1", PlanTaskStatus.Verifying)]);

        var result = PlanTaskActivityResolver.Resolve(plan);

        Assert.That(result["t1"], Is.EqualTo(PlanTaskActivityState.VerificationPending));
    }

    [Test]
    public void PendingTaskNamedByExecutingProgress_ResolvesToExecuting()
    {
        var tasks = new[] { MakeTask("t1", PlanTaskStatus.Pending) };
        var plan = MakePlan(tasks: tasks) with
        {
            Progress = new PlanProgress(0, 1, ExecutingTaskId: "t1"),
        };

        var result = PlanTaskActivityResolver.Resolve(plan);

        Assert.That(result["t1"], Is.EqualTo(PlanTaskActivityState.Executing));
    }

    [Test]
    public void CompletedTask_ResolvesToCompleted()
    {
        var tasks = new[] { MakeTask("t1", PlanTaskStatus.Complete) };
        var plan = MakePlan(tasks: tasks);

        var result = PlanTaskActivityResolver.Resolve(plan);

        Assert.That(result["t1"], Is.EqualTo(PlanTaskActivityState.Completed));
    }

    [Test]
    public void SupersededTask_ResolvesToCompleted()
    {
        var tasks = new[] { MakeTask("t1", PlanTaskStatus.Superseded) };
        var plan = MakePlan(tasks: tasks);

        var result = PlanTaskActivityResolver.Resolve(plan);

        Assert.That(result["t1"], Is.EqualTo(PlanTaskActivityState.Completed));
    }

    [Test]
    public void FailedTask_ResolvesToBlocked()
    {
        var tasks = new[] { MakeTask("t1", PlanTaskStatus.Failed) };
        var plan = MakePlan(tasks: tasks);

        var result = PlanTaskActivityResolver.Resolve(plan);

        Assert.That(result["t1"], Is.EqualTo(PlanTaskActivityState.Blocked));
    }

    [Test]
    public void PartialTask_ResolvesToInterrupted()
    {
        var tasks = new[] { MakeTask("t1", PlanTaskStatus.Partial) };
        var plan = MakePlan(tasks: tasks);

        var result = PlanTaskActivityResolver.Resolve(plan);

        Assert.That(result["t1"], Is.EqualTo(PlanTaskActivityState.Interrupted));
    }

    [Test]
    public void PendingTask_DefaultResolvesToQueued()
    {
        var tasks = new[] { MakeTask("t1", PlanTaskStatus.Pending) };
        var plan = MakePlan(tasks: tasks);

        var result = PlanTaskActivityResolver.Resolve(plan);

        Assert.That(result["t1"], Is.EqualTo(PlanTaskActivityState.Queued));
    }

    // ── Parallel tasks ───────────────────────────────────────────────────────

    [Test]
    public void ParallelExecutingTasks_AllResolveToExecuting()
    {
        var tasks = new[]
        {
            MakeTask("t1", PlanTaskStatus.Executing),
            MakeTask("t2", PlanTaskStatus.Executing),
            MakeTask("t3", PlanTaskStatus.Pending),
        };
        var plan = MakePlan(tasks: tasks);

        var result = PlanTaskActivityResolver.Resolve(plan);

        Assert.That(result["t1"], Is.EqualTo(PlanTaskActivityState.Executing));
        Assert.That(result["t2"], Is.EqualTo(PlanTaskActivityState.Executing));
        Assert.That(result["t3"], Is.EqualTo(PlanTaskActivityState.Queued));
    }

    // ── Gate-blocked tasks ───────────────────────────────────────────────────

    [Test]
    public void TaskBehindPendingGate_ResolvesToAwaitingApproval()
    {
        var tasks = new[]
        {
            MakeTask("t1", PlanTaskStatus.Complete),
            MakeTask("t2", PlanTaskStatus.Pending),
        };
        var gates = new[]
        {
            MakeGate("g1", PlanGateStatus.AwaitingApproval,
                afterTaskIds: ["t1"], beforeTaskIds: ["t2"]),
        };
        var plan = MakePlan(tasks: tasks, gates: gates);

        var result = PlanTaskActivityResolver.Resolve(plan);

        Assert.That(result["t1"], Is.EqualTo(PlanTaskActivityState.Completed));
        Assert.That(result["t2"], Is.EqualTo(PlanTaskActivityState.AwaitingApproval));
    }

    [Test]
    public void TaskBehindApprovedGate_ResolvesToQueued()
    {
        var tasks = new[]
        {
            MakeTask("t1", PlanTaskStatus.Complete),
            MakeTask("t2", PlanTaskStatus.Pending),
        };
        var gates = new[]
        {
            MakeGate("g1", PlanGateStatus.Approved,
                afterTaskIds: ["t1"], beforeTaskIds: ["t2"]),
        };
        var plan = MakePlan(tasks: tasks, gates: gates);

        var result = PlanTaskActivityResolver.Resolve(plan);

        Assert.That(result["t2"], Is.EqualTo(PlanTaskActivityState.Queued));
    }

    // ── Failed-dependency blocking ───────────────────────────────────────────

    [Test]
    public void TaskWithFailedDependency_ResolvesToBlocked()
    {
        var tasks = new[]
        {
            MakeTask("t1", PlanTaskStatus.Failed),
            MakeTask("t2", PlanTaskStatus.Pending, dependsOn: ["t1"]),
        };
        var plan = MakePlan(tasks: tasks);

        var result = PlanTaskActivityResolver.Resolve(plan);

        Assert.That(result["t1"], Is.EqualTo(PlanTaskActivityState.Blocked));
        Assert.That(result["t2"], Is.EqualTo(PlanTaskActivityState.Blocked));
    }

    // ── Restart convergence ──────────────────────────────────────────────────

    [Test]
    public void RestartConvergence_AwaitingApprovalLifecycle_PendingTasksShowAwaitingApproval()
    {
        var tasks = new[]
        {
            MakeTask("t1", PlanTaskStatus.Complete),
            MakeTask("t2", PlanTaskStatus.Pending),
        };
        var plan = MakePlan(
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval,
            tasks: tasks);

        var result = PlanTaskActivityResolver.Resolve(plan);

        Assert.That(result["t1"], Is.EqualTo(PlanTaskActivityState.Completed));
        Assert.That(result["t2"], Is.EqualTo(PlanTaskActivityState.AwaitingApproval));
    }

    [Test]
    public void RestartConvergence_InterruptedLifecycle_PendingTasksShowInterrupted()
    {
        var tasks = new[]
        {
            MakeTask("t1", PlanTaskStatus.Complete),
            MakeTask("t2", PlanTaskStatus.Pending),
        };
        var plan = MakePlan(
            lifecycleStatus: PlanLifecycleStatus.Interrupted,
            tasks: tasks);

        var result = PlanTaskActivityResolver.Resolve(plan);

        Assert.That(result["t1"], Is.EqualTo(PlanTaskActivityState.Completed));
        Assert.That(result["t2"], Is.EqualTo(PlanTaskActivityState.Interrupted));
    }

    [TestCase(PlanTaskStatus.Executing)]
    [TestCase(PlanTaskStatus.VerificationPending)]
    [TestCase(PlanTaskStatus.Verifying)]
    [TestCase("scrutinizing")]
    [TestCase(PlanTaskStatus.Reworking)]
    public void RestartConvergence_InterruptedLifecycle_StaleActiveTaskDoesNotSpin(string taskStatus)
    {
        var tasks = new[] { MakeTask("t1", taskStatus) };
        var plan = MakePlan(
            lifecycleStatus: PlanLifecycleStatus.Interrupted,
            tasks: tasks) with
        {
            Progress = new PlanProgress(0, 1, ExecutingTaskId: "t1"),
        };

        var result = PlanTaskActivityResolver.Resolve(plan);

        Assert.That(result["t1"], Is.EqualTo(PlanTaskActivityState.Interrupted));
    }

    [TestCase("PLAN-TEST", "PLAN-TEST", "t1", "t1", true)]
    [TestCase("PLAN-TEST", "OTHER", "t1", "t1", false)]
    [TestCase("PLAN-TEST", "PLAN-TEST", "t2", "t1", false)]
    [TestCase("PLAN-TEST", null, null, "t1", false)]
    public void ActivityPulsePolicy_RequiresMatchingLiveRound(
        string persistedPlanId,
        string? livePlanId,
        string? liveTargetId,
        string targetId,
        bool expected)
    {
        Assert.That(
            PlanTaskActivityPulsePolicy.MatchesLiveTarget(
                persistedPlanId, livePlanId, liveTargetId, targetId),
            Is.EqualTo(expected));
    }

    [TestCase(PlanTaskActivityState.VerificationPending, "Step 2 - Verification pending")]
    [TestCase(PlanTaskActivityState.Verifying, "Step 2 - Verifying")]
    [TestCase(PlanTaskActivityState.Assessing, "Step 2 - Assessing")]
    [TestCase(PlanTaskActivityState.Queued, "Step 2")]
    public void StepLabel_DescribesVerificationPhase(
        PlanTaskActivityState activityState,
        string expected)
    {
        Assert.That(PlanTaskActivityPresentation.BuildStepLabel("2", activityState), Is.EqualTo(expected));
    }

    [TestCase(PlanTaskActivityState.Assessing, true)]
    [TestCase(PlanTaskActivityState.Verifying, false)]
    [TestCase(PlanTaskActivityState.Executing, false)]
    public void KeepsSpinnerContinuouslyActive_IsReservedForRecoveryAssessment(
        PlanTaskActivityState activityState,
        bool expected)
    {
        Assert.That(
            PlanTaskActivityPresentation.KeepsSpinnerContinuouslyActive(activityState),
            Is.EqualTo(expected));
    }

    [TestCase(PlanTaskActivityState.Verifying, false, PlanTaskActivityState.VerificationPending)]
    [TestCase(PlanTaskActivityState.Verifying, true, PlanTaskActivityState.Verifying)]
    [TestCase(PlanTaskActivityState.Executing, false, PlanTaskActivityState.Executing)]
    public void ResolveLiveState_VerificationRequiresMatchingRound(
        PlanTaskActivityState activityState,
        bool hasMatchingLiveRound,
        PlanTaskActivityState expected)
    {
        Assert.That(
            PlanTaskActivityPresentation.ResolveLiveState(activityState, hasMatchingLiveRound),
            Is.EqualTo(expected));
    }

    // ── Plan-level resolution ────────────────────────────────────────────────

    [Test]
    public void PlanLevel_Executing_ResolvesToExecuting()
    {
        var plan = MakePlan(lifecycleStatus: PlanLifecycleStatus.Executing);
        Assert.That(PlanTaskActivityResolver.ResolvePlanLevel(plan), Is.EqualTo(PlanTaskActivityState.Executing));
    }

    [Test]
    public void PlanLevel_AwaitingApproval_ResolvesToAwaitingApproval()
    {
        var plan = MakePlan(lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);
        Assert.That(PlanTaskActivityResolver.ResolvePlanLevel(plan), Is.EqualTo(PlanTaskActivityState.AwaitingApproval));
    }

    [Test]
    public void PlanLevel_Blocked_ResolvesToBlocked()
    {
        var plan = MakePlan(lifecycleStatus: PlanLifecycleStatus.Blocked);
        Assert.That(PlanTaskActivityResolver.ResolvePlanLevel(plan), Is.EqualTo(PlanTaskActivityState.Blocked));
    }

    [Test]
    public void PlanLevel_Interrupted_ResolvesToInterrupted()
    {
        var plan = MakePlan(lifecycleStatus: PlanLifecycleStatus.Interrupted);
        Assert.That(PlanTaskActivityResolver.ResolvePlanLevel(plan), Is.EqualTo(PlanTaskActivityState.Interrupted));
    }

    [Test]
    public void PlanLevel_Completed_ResolvesToCompleted()
    {
        var plan = MakePlan(lifecycleStatus: PlanLifecycleStatus.Completed);
        Assert.That(PlanTaskActivityResolver.ResolvePlanLevel(plan), Is.EqualTo(PlanTaskActivityState.Completed));
    }

    [Test]
    public void PlanLevel_Approved_ResolvesToQueued()
    {
        var plan = MakePlan(lifecycleStatus: PlanLifecycleStatus.Approved);
        Assert.That(PlanTaskActivityResolver.ResolvePlanLevel(plan), Is.EqualTo(PlanTaskActivityState.Queued));
    }

    // ── Stale event rejection (integration with live sync) ───────────────────

    [Test]
    public void StaleEventRejection_LiveSyncRejectsLowerCompletion()
    {
        var broker = new WeakEventBroker();
        var initial = MakePlan(
            lifecycleStatus: PlanLifecycleStatus.Executing,
            tasks: [MakeTask("t1", PlanTaskStatus.Complete), MakeTask("t2", PlanTaskStatus.Executing)],
            completedCount: 3, totalCount: 5);

        Plan? received = null;
        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-TEST", initial, broker,
            plan => received = plan);

        // Stale event with lower completion
        var stale = MakePlan(
            lifecycleStatus: PlanLifecycleStatus.Executing,
            tasks: [MakeTask("t1", PlanTaskStatus.Executing)],
            completedCount: 1, totalCount: 5);

        handler.HandleEventDirect(new PlanProgressEvent("PLAN-TEST", stale));

        Assert.That(received, Is.Null, "Stale event should be rejected");
        Assert.That(handler.RejectedCount, Is.EqualTo(1));

        handler.Detach();
    }

    [Test]
    public void StaleEventRejection_HigherCompletionAccepted()
    {
        var broker = new WeakEventBroker();
        var initial = MakePlan(
            lifecycleStatus: PlanLifecycleStatus.Executing,
            tasks: [MakeTask("t1", PlanTaskStatus.Complete)],
            completedCount: 1, totalCount: 5);

        Plan? received = null;
        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-TEST", initial, broker,
            plan => received = plan);

        var updated = MakePlan(
            lifecycleStatus: PlanLifecycleStatus.Executing,
            tasks: [MakeTask("t1", PlanTaskStatus.Complete), MakeTask("t2", PlanTaskStatus.Executing)],
            completedCount: 3, totalCount: 5);

        handler.HandleEventDirect(new PlanProgressEvent("PLAN-TEST", updated));

        Assert.That(received, Is.SameAs(updated));
        Assert.That(handler.AppliedCount, Is.EqualTo(1));

        handler.Detach();
    }

    [Test]
    public void LiveSync_NewHostRevisionWithSameProgress_IsAccepted()
    {
        var broker = new WeakEventBroker();
        var initial = MakePlan(
            lifecycleStatus: PlanLifecycleStatus.Interrupted,
            tasks: [MakeTask("t1", PlanTaskStatus.Executing)],
            completedCount: 1,
            totalCount: 2);
        Plan? received = null;
        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-TEST", initial, broker,
            plan => received = plan);
        var migrated = initial with
        {
            Revision = "rev2",
            HostRevision = "rev2",
            Tasks = [MakeTask("t1", PlanTaskStatus.Partial)],
        };

        handler.HandleEventDirect(new PlanProgressEvent("PLAN-TEST", migrated));

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.SameAs(migrated));
            Assert.That(handler.RejectedCount, Is.Zero);
        });
        handler.Detach();
    }

    // ── Event coalescence ────────────────────────────────────────────────────

    [Test]
    public void EventCoalescence_WithoutDispatcher_AllEventsApplyImmediately()
    {
        var broker = new WeakEventBroker();
        var initial = MakePlan(completedCount: 0);
        var updates = new List<Plan>();

        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-TEST", initial, broker,
            plan => updates.Add(plan),
            dispatcher: null);

        handler.HandleEventDirect(new PlanProgressEvent("PLAN-TEST", MakePlan(completedCount: 1)));
        handler.HandleEventDirect(new PlanProgressEvent("PLAN-TEST", MakePlan(completedCount: 2)));
        handler.HandleEventDirect(new PlanProgressEvent("PLAN-TEST", MakePlan(completedCount: 3)));

        Assert.That(updates, Has.Count.EqualTo(3));
        Assert.That(handler.CurrentPlan!.Progress.CompletedCount, Is.EqualTo(3));

        handler.Detach();
    }

    // ── Restart convergence with open viewer ─────────────────────────────────

    [Test]
    public void RestartConvergence_OpenViewer_LoadsDurableStateCorrectly()
    {
        // Simulates restart: durable state shows awaiting-approval, no live loop
        var tasks = new[]
        {
            MakeTask("t1", PlanTaskStatus.Complete),
            MakeTask("t2", PlanTaskStatus.Complete),
            MakeTask("t3", PlanTaskStatus.Pending),
        };
        var gates = new[]
        {
            MakeGate("g1", PlanGateStatus.AwaitingApproval,
                afterTaskIds: ["t1", "t2"], beforeTaskIds: ["t3"]),
        };
        var plan = MakePlan(
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval,
            tasks: tasks,
            gates: gates,
            completedCount: 2,
            totalCount: 3);

        // Viewer opens with durable state only (no live events needed)
        var broker = new WeakEventBroker();
        Plan? rendered = null;
        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-TEST", plan, broker,
            p => rendered = p);

        // Verify initial state resolves correctly without any events
        var states = PlanTaskActivityResolver.Resolve(plan);
        Assert.That(states["t1"], Is.EqualTo(PlanTaskActivityState.Completed));
        Assert.That(states["t2"], Is.EqualTo(PlanTaskActivityState.Completed));
        Assert.That(states["t3"], Is.EqualTo(PlanTaskActivityState.AwaitingApproval));

        // Plan-level indicator
        Assert.That(PlanTaskActivityResolver.ResolvePlanLevel(plan),
            Is.EqualTo(PlanTaskActivityState.AwaitingApproval));

        handler.Detach();
    }

    // ── Null plan guard ──────────────────────────────────────────────────────

    [Test]
    public void Resolve_NullPlan_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PlanTaskActivityResolver.Resolve(null!));
    }

    [Test]
    public void ResolvePlanLevel_NullPlan_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PlanTaskActivityResolver.ResolvePlanLevel(null!));
    }
}
