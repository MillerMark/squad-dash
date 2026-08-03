using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SquadDash.Tests;

/// <summary>
/// Deterministic host-controlled integration coverage exercising the full collected-plan
/// lifecycle: proposal → collection → panel reveal → explicit start → live progress →
/// interruption → resume → completion. Covers duplicate Add-to-Plans clicks, stale Inbox
/// rows, watcher refresh, restart in every collected or active state, gate editability
/// transitions, event ordering, and surface convergence.
/// </summary>
[TestFixture]
internal sealed class CollectedPlanLifecycleIntegrationTests
{
    private TestWorkspace _workspace = null!;
    private string _squadFolder = null!;
    private PlanStore _planStore = null!;
    private PendingDecomposePlanStore _pendingStore = null!;
    private PlanCollectionService _collectionService = null!;
    private PlanExecutionTransitionService _transitionService = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TestWorkspace();
        _squadFolder = _workspace.GetPath(".squad");
        Directory.CreateDirectory(_squadFolder);
        _planStore = new PlanStore(_squadFolder);
        _pendingStore = new PendingDecomposePlanStore(_squadFolder);
        _collectionService = new PlanCollectionService(_planStore, _pendingStore);
        _transitionService = new PlanExecutionTransitionService(_planStore);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DecomposedTaskGroup MakeGroup(
        string groupId = "LIFECYCLE-001",
        int taskCount = 4,
        string branch = "feature/lifecycle",
        IReadOnlyList<DecomposedGate>? gates = null)
    {
        var tasks = Enumerable.Range(1, taskCount)
            .Select(i => new DecomposedSubTask(
                Id:          $"{groupId}-{i:D3}",
                Description: $"Lifecycle task {i}",
                DependsOn:   i == 1 ? [] : [$"{groupId}-{i - 1:D3}"],
                Priority:    "mid",
                Title:       $"Step {i}",
                AgentAssignments: [new DecomposedAgentAssignment("orion-vale", "architect")]))
            .ToList();

        return new DecomposedTaskGroup(
            GroupId:       groupId,
            GroupTitle:    "Lifecycle Integration Plan",
            Branch:        branch,
            Summary:       "End-to-end lifecycle test",
            Tasks:         tasks,
            ApprovalGates: gates);
    }

    private static PendingDecomposePlan MakePending(DecomposedTaskGroup? group = null)
    {
        group ??= MakeGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        return new PendingDecomposePlan(revision, group, DateTimeOffset.UtcNow);
    }

    private static Plan MakePlan(
        string planId,
        string status,
        int completedCount = 0,
        int totalCount = 4,
        IReadOnlyList<PlanTask>? tasks = null,
        IReadOnlyList<PlanApprovalGate>? gates = null,
        PlanInterruptionData? interruptionData = null,
        string? executingTaskId = null) =>
        new(
            PlanId:          planId,
            Revision:        "rev-integration",
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: status,
            Title:           "Lifecycle Test Plan",
            Branch:          "feature/lifecycle",
            Summary:         "Integration test plan",
            Tasks:           tasks ?? MakeTaskList(planId, totalCount),
            ApprovalGates:   gates ?? [],
            Progress:        new PlanProgress(completedCount, totalCount, executingTaskId),
            Timestamps:      new PlanTimestamps(CreatedAt: DateTimeOffset.UtcNow),
            InterruptionData: interruptionData);

    private static IReadOnlyList<PlanTask> MakeTaskList(string planId, int count) =>
        Enumerable.Range(1, count)
            .Select(i => new PlanTask(
                TaskId:      $"{planId}-{i:D3}",
                Title:       $"Step {i}",
                Description: $"Task {i}",
                DependsOn:   i == 1 ? [] : [$"{planId}-{i - 1:D3}"],
                Priority:    "mid",
                Status:      PlanTaskStatus.Pending))
            .ToArray();

    private string TasksMdPath => Path.Combine(_squadFolder, "tasks.md");

    private void AssertTasksMdUnmodified()
    {
        Assert.That(File.Exists(TasksMdPath), Is.False,
            ".squad/tasks.md must never be created by collection or transition services.");
    }

    // ── 1. Happy path: Proposal → Collection → Start → Progress → Completion ─

    [Test]
    public void HappyPath_Proposal_To_Collection_To_Start_To_Progress_To_Completion()
    {
        var pending = MakePending();
        var t0 = DateTimeOffset.UtcNow;

        // 1. Collect
        var collectResult = _collectionService.Collect(pending, t0);
        Assert.That(collectResult.Outcome, Is.EqualTo(CollectionOutcome.Collected));
        var plan = collectResult.Plan!;
        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Approved));

        // 2. Start — transition Approved → Executing
        var startResult = _transitionService.Start(plan, t0.AddMinutes(1));
        Assert.That(startResult.Outcome, Is.EqualTo(ExecutionTransitionOutcome.Started));
        plan = startResult.Plan!;
        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));

        // 3. Simulate progress events via broker
        var broker = new WeakEventBroker();
        var appliedPlans = new List<Plan>();
        var handler = new PlanViewerLiveSyncHandler(
            plan.PlanId, plan, broker,
            p => appliedPlans.Add(p));

        for (int i = 1; i <= plan.Progress.TotalCount; i++)
        {
            var updatedTasks = plan.Tasks.Select((t, idx) => idx < i
                ? t with { Status = PlanTaskStatus.Complete }
                : t).ToArray();
            var progressPlan = plan with
            {
                Progress = new PlanProgress(i, plan.Progress.TotalCount),
                Tasks = updatedTasks,
                LifecycleStatus = i == plan.Progress.TotalCount
                    ? PlanLifecycleStatus.Completed
                    : PlanLifecycleStatus.Executing,
            };
            handler.HandleEventDirect(new PlanProgressEvent(plan.PlanId, progressPlan));
        }

        Assert.Multiple(() =>
        {
            Assert.That(appliedPlans, Has.Count.EqualTo(plan.Progress.TotalCount));
            Assert.That(handler.CurrentPlan!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
            Assert.That(handler.CurrentPlan.Progress.CompletedCount, Is.EqualTo(plan.Progress.TotalCount));
        });

        handler.Detach();
        AssertTasksMdUnmodified();
    }

    // ── 2. Happy path: Collection → Start → Interruption → Resume → Completion ─

    [Test]
    public void HappyPath_Collection_Start_Interrupt_Resume_Completion()
    {
        var pending = MakePending();
        var t0 = DateTimeOffset.UtcNow;

        // Collect & start
        var collected = _collectionService.Collect(pending, t0).Plan!;
        var started = _transitionService.Start(collected, t0.AddMinutes(1)).Plan!;
        Assert.That(started.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));

        // Simulate interruption — persist interrupted state
        var interrupted = started with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            InterruptionData = new PlanInterruptionData(
                Reason: "Process crash",
                RecoveryState: PlanRecoveryState.PendingRecovery,
                LoopIteration: 2,
                InterruptedTaskId: $"{started.PlanId}-002"),
            Timestamps = started.Timestamps with { InterruptedAt = t0.AddMinutes(5) },
        };
        _planStore.Save(interrupted);

        // Resume
        var resumeResult = _transitionService.Resume(interrupted, t0.AddMinutes(10));
        Assert.Multiple(() =>
        {
            Assert.That(resumeResult.Outcome, Is.EqualTo(ExecutionTransitionOutcome.Started));
            Assert.That(resumeResult.Plan!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(resumeResult.Plan.InterruptionData!.RecoveryState,
                Is.EqualTo(PlanRecoveryState.Recovered));
        });

        // Simulate completion after resume
        var completedPlan = resumeResult.Plan! with
        {
            LifecycleStatus = PlanLifecycleStatus.Completed,
            Progress = new PlanProgress(4, 4),
            Timestamps = resumeResult.Plan.Timestamps with { CompletedAt = t0.AddMinutes(20) },
        };
        _planStore.Save(completedPlan);

        var reloaded = _planStore.Load(pending.Group.GroupId);
        Assert.That(reloaded!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
        AssertTasksMdUnmodified();
    }

    // ── 3. Duplicate Add to Plans clicks — idempotent, no duplicate plan rows ─

    [Test]
    public void DuplicateAddToPlans_SecondClickIsIdempotent_NoDuplicateRows()
    {
        var pending = MakePending();
        var t0 = DateTimeOffset.UtcNow;

        var first = _collectionService.Collect(pending, t0);
        var second = _collectionService.Collect(pending, t0.AddSeconds(30));

        Assert.Multiple(() =>
        {
            Assert.That(first.Outcome, Is.EqualTo(CollectionOutcome.Collected));
            Assert.That(second.Outcome, Is.EqualTo(CollectionOutcome.AlreadyCollected));
            Assert.That(second.Plan!.PlanId, Is.EqualTo(first.Plan!.PlanId));
        });

        // LoadAll returns exactly one plan — no duplicates
        var allPlans = _planStore.LoadAll();
        Assert.That(allPlans.Count(p =>
            string.Equals(p.PlanId, pending.Group.GroupId, StringComparison.Ordinal)),
            Is.EqualTo(1), "Panel must never show duplicate plan rows.");
        AssertTasksMdUnmodified();
    }

    // ── 4. Stale Inbox rows — collecting from outdated revision is rejected ──

    [Test]
    public void StaleInboxRow_OutdatedRevision_IsRejected()
    {
        // Collect current version
        var group1 = MakeGroup();
        var pending1 = MakePending(group1);
        _collectionService.Collect(pending1, DateTimeOffset.UtcNow);

        // Attempt to collect from stale Inbox row (different task count = different revision)
        var staleGroup = MakeGroup(taskCount: 6);
        var stalePending = MakePending(staleGroup);
        var result = _collectionService.Collect(stalePending, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(CollectionOutcome.StaleRevisionRejected));
            Assert.That(result.Plan, Is.Null);
        });
        AssertTasksMdUnmodified();
    }

    // ── 5. Collection never launches execution ──────────────────────────────

    [Test]
    public void CollectionNeverLaunchesExecution_StatusIsApprovedNotExecuting()
    {
        var pending = MakePending();
        var result = _collectionService.Collect(pending, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Plan!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Approved));
            Assert.That(result.Plan.LifecycleStatus, Is.Not.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(result.Plan.Timestamps.StartedAt, Is.Null,
                "Collection must not stamp StartedAt — execution is a separate transition.");
        });
        AssertTasksMdUnmodified();
    }

    // ── 6. Application restart in collected (Approved) state ─────────────────

    [Test]
    public void Restart_InCollectedState_PlanPersistsAndLoads()
    {
        var pending = MakePending();
        var t0 = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        _collectionService.Collect(pending, t0);

        // Simulate restart: fresh store instances over the same folder
        var freshStore = new PlanStore(_squadFolder);
        var loaded = freshStore.Load(pending.Group.GroupId);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Approved));
            Assert.That(loaded.Revision, Is.EqualTo(pending.Revision));
            Assert.That(loaded.Tasks, Has.Count.EqualTo(4));
            Assert.That(loaded.Timestamps.AcceptedAt, Is.EqualTo(t0));
            Assert.That(loaded.Timestamps.StartedAt, Is.Null);
        });
        AssertTasksMdUnmodified();
    }

    // ── 7. Application restart in active/executing state ─────────────────────

    [Test]
    public void Restart_InExecutingState_PlanPersistsCorrectly()
    {
        var pending = MakePending();
        var t0 = DateTimeOffset.UtcNow;

        var collected = _collectionService.Collect(pending, t0).Plan!;
        var started = _transitionService.Start(collected, t0.AddMinutes(1)).Plan!;
        Assert.That(started.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));

        // Simulate restart
        var freshStore = new PlanStore(_squadFolder);
        var loaded = freshStore.Load(pending.Group.GroupId);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(loaded.Timestamps.StartedAt, Is.Not.Null);
        });
        AssertTasksMdUnmodified();
    }

    [Test]
    public void Restart_InInterruptedState_PlanPreservesInterruptionData()
    {
        var pending = MakePending();
        var t0 = DateTimeOffset.UtcNow;

        var collected = _collectionService.Collect(pending, t0).Plan!;
        var started = _transitionService.Start(collected, t0.AddMinutes(1)).Plan!;

        var interrupted = started with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            InterruptionData = new PlanInterruptionData(
                Reason: "User cancelled",
                RecoveryState: PlanRecoveryState.PendingRecovery,
                LoopIteration: 1),
        };
        _planStore.Save(interrupted);

        // Simulate restart
        var freshStore = new PlanStore(_squadFolder);
        var loaded = freshStore.Load(pending.Group.GroupId);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
            Assert.That(loaded.InterruptionData, Is.Not.Null);
            Assert.That(loaded.InterruptionData!.Reason, Is.EqualTo("User cancelled"));
            Assert.That(loaded.InterruptionData.RecoveryState,
                Is.EqualTo(PlanRecoveryState.PendingRecovery));
        });

        // Verify resume still works after restart
        var freshTransitionService = new PlanExecutionTransitionService(freshStore);
        var resumeResult = freshTransitionService.Resume(loaded!, t0.AddMinutes(10));
        Assert.That(resumeResult.Outcome, Is.EqualTo(ExecutionTransitionOutcome.Started));
        AssertTasksMdUnmodified();
    }

    // ── 8. No plan row duplicates in panel ───────────────────────────────────

    [Test]
    public void NoPlanRowDuplicates_LoadAllReturnsExactlyOnePlanPerGroup()
    {
        // Collect two different plans
        var groupA = MakeGroup(groupId: "PLAN-A");
        var groupB = MakeGroup(groupId: "PLAN-B");
        _collectionService.Collect(MakePending(groupA), DateTimeOffset.UtcNow);
        _collectionService.Collect(MakePending(groupB), DateTimeOffset.UtcNow);

        // Duplicate clicks for both
        _collectionService.Collect(MakePending(groupA), DateTimeOffset.UtcNow.AddSeconds(1));
        _collectionService.Collect(MakePending(groupB), DateTimeOffset.UtcNow.AddSeconds(1));

        var allPlans = _planStore.LoadAll();
        var grouped = allPlans.GroupBy(p => p.PlanId).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(allPlans, Has.Count.EqualTo(2));
            Assert.That(grouped.All(g => g.Count() == 1), Is.True,
                "No plan ID should appear more than once in the store.");
        });
    }

    // ── 9. Progress events arrive in order — completed count monotonically increases ─

    [Test]
    public void ProgressEvents_CompletedCountMonotonicallyIncreases()
    {
        var broker = new WeakEventBroker();
        var plan = MakePlan("PROGRESS-001", PlanLifecycleStatus.Executing, completedCount: 0);
        var completedCounts = new List<int>();

        var handler = new PlanViewerLiveSyncHandler(
            plan.PlanId, plan, broker,
            p => completedCounts.Add(p.Progress.CompletedCount));

        // Send events in order
        for (int i = 1; i <= 4; i++)
        {
            var updated = plan with { Progress = new PlanProgress(i, 4) };
            handler.HandleEventDirect(new PlanProgressEvent(plan.PlanId, updated));
        }

        Assert.Multiple(() =>
        {
            Assert.That(completedCounts, Has.Count.EqualTo(4));
            for (int i = 1; i < completedCounts.Count; i++)
            {
                Assert.That(completedCounts[i], Is.GreaterThan(completedCounts[i - 1]),
                    $"CompletedCount must monotonically increase at index {i}.");
            }
        });

        handler.Detach();
    }

    // ── 10. Stale events rejected — lower completed count ignored ────────────

    [Test]
    public void StaleEvents_LowerCompletedCount_AreRejected()
    {
        var broker = new WeakEventBroker();
        var plan = MakePlan("STALE-001", PlanLifecycleStatus.Executing, completedCount: 3);
        var appliedPlans = new List<Plan>();

        var handler = new PlanViewerLiveSyncHandler(
            plan.PlanId, plan, broker,
            p => appliedPlans.Add(p));

        // Send stale event (lower count)
        var stale1 = plan with { Progress = new PlanProgress(1, 4) };
        handler.HandleEventDirect(new PlanProgressEvent(plan.PlanId, stale1));

        var stale2 = plan with { Progress = new PlanProgress(2, 4) };
        handler.HandleEventDirect(new PlanProgressEvent(plan.PlanId, stale2));

        // Send valid event (higher count)
        var valid = plan with { Progress = new PlanProgress(4, 4) };
        handler.HandleEventDirect(new PlanProgressEvent(plan.PlanId, valid));

        Assert.Multiple(() =>
        {
            Assert.That(handler.RejectedCount, Is.EqualTo(2));
            Assert.That(appliedPlans, Has.Count.EqualTo(1));
            Assert.That(handler.CurrentPlan!.Progress.CompletedCount, Is.EqualTo(4));
        });

        handler.Detach();
    }

    // ── 11. Rapid events coalesced (no dispatcher) — all apply directly ─────

    [Test]
    public void RapidEvents_WithoutDispatcher_LatestStateApplied()
    {
        var broker = new WeakEventBroker();
        var plan = MakePlan("COALESCE-001", PlanLifecycleStatus.Executing, completedCount: 0);
        var appliedPlans = new List<Plan>();

        var handler = new PlanViewerLiveSyncHandler(
            plan.PlanId, plan, broker,
            p => appliedPlans.Add(p),
            dispatcher: null);

        // Rapid-fire 10 events
        for (int i = 1; i <= 4; i++)
        {
            var updated = plan with { Progress = new PlanProgress(i, 4) };
            handler.HandleEventDirect(new PlanProgressEvent(plan.PlanId, updated));
        }

        // Without dispatcher, all events apply directly (coalescence bypassed)
        Assert.That(handler.CurrentPlan!.Progress.CompletedCount, Is.EqualTo(4));
        Assert.That(handler.AppliedCount, Is.EqualTo(4));

        handler.Detach();
    }

    // ── 12. Future gate edits allowed ────────────────────────────────────────

    [Test]
    public void FutureGateEdits_AheadOfExecutionFrontier_AreEditable()
    {
        var tasks = new PlanTask[]
        {
            new("A", "Step A", "First", [], "mid", PlanTaskStatus.Executing),
            new("B", "Step B", "Second", ["A"], "mid", PlanTaskStatus.Pending),
            new("C", "Step C", "Third", ["B"], "mid", PlanTaskStatus.Pending),
        };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "Review before C", ["B"], ["C"], PlanGateStatus.Pending),
        };
        var plan = MakePlan("GATE-001", PlanLifecycleStatus.Executing, tasks: tasks, gates: gates);

        Assert.Multiple(() =>
        {
            // B is pending — its entry is still editable
            Assert.That(PlanApprovalControlLockPolicy.IsTaskEntryLocked(plan, "B"), Is.False);
            // Gate G1 is ahead of execution — editable
            Assert.That(PlanApprovalControlLockPolicy.IsStageMilestoneLocked(plan, ["B"], ["C"]),
                Is.False);
            // C is pending — its entry is editable
            Assert.That(PlanApprovalControlLockPolicy.IsTaskEntryLocked(plan, "C"), Is.False);
        });
    }

    // ── 13. Historical gate edits forbidden ──────────────────────────────────

    [Test]
    public void HistoricalGateEdits_BehindExecutionFrontier_AreLocked()
    {
        var tasks = new PlanTask[]
        {
            new("A", "Step A", "First", [], "mid", PlanTaskStatus.Complete),
            new("B", "Step B", "Second", ["A"], "mid", PlanTaskStatus.Complete),
            new("C", "Step C", "Third", ["B"], "mid", PlanTaskStatus.Executing),
        };
        var gates = new PlanApprovalGate[]
        {
            new("G1", "Review before B", ["A"], ["B"], PlanGateStatus.Approved),
        };
        var plan = MakePlan("GATE-002", PlanLifecycleStatus.Executing, tasks: tasks, gates: gates);

        Assert.Multiple(() =>
        {
            // A completed — exit locked
            Assert.That(PlanApprovalControlLockPolicy.IsTaskExitLocked(plan, "A"), Is.True);
            // B completed — entry locked
            Assert.That(PlanApprovalControlLockPolicy.IsTaskEntryLocked(plan, "B"), Is.True);
            // Historical gate (traversed) — locked
            Assert.That(PlanApprovalControlLockPolicy.IsStageMilestoneLocked(plan, ["A"], ["B"]),
                Is.True);
        });
    }

    // ── 14. Live transition from editable to locked ─────────────────────────

    [Test]
    public void LiveTransition_GateBecomesLockedWhenExecutionPassesIt()
    {
        // Phase 1: B is pending, gate before B is editable
        var tasksBefore = new PlanTask[]
        {
            new("A", "Step A", "First", [], "mid", PlanTaskStatus.Executing),
            new("B", "Step B", "Second", ["A"], "mid", PlanTaskStatus.Pending),
        };
        var planBefore = MakePlan("TRANSITION-001", PlanLifecycleStatus.Executing,
            tasks: tasksBefore);

        Assert.That(PlanApprovalControlLockPolicy.IsTaskEntryLocked(planBefore, "B"), Is.False,
            "Before A completes, B entry should be editable.");

        // Phase 2: A completed, B is now executing — gate is locked
        var tasksAfter = new PlanTask[]
        {
            new("A", "Step A", "First", [], "mid", PlanTaskStatus.Complete),
            new("B", "Step B", "Second", ["A"], "mid", PlanTaskStatus.Executing),
        };
        var planAfter = MakePlan("TRANSITION-001", PlanLifecycleStatus.Executing,
            tasks: tasksAfter);

        Assert.That(PlanApprovalControlLockPolicy.IsTaskEntryLocked(planAfter, "B"), Is.True,
            "Once B starts executing, its entry gate is locked.");
    }

    // ── 15. Every surface converges ─────────────────────────────────────────

    [Test]
    public void SurfaceConvergence_PanelAndViewerSeeConsistentStateAfterEachTransition()
    {
        var pending = MakePending();
        var t0 = DateTimeOffset.UtcNow;

        // Collect
        var collected = _collectionService.Collect(pending, t0).Plan!;

        // Verify panel (store) and viewer (in-memory) converge on Approved
        var storeState = _planStore.Load(pending.Group.GroupId)!;
        Assert.That(storeState.LifecycleStatus, Is.EqualTo(collected.LifecycleStatus),
            "Store and collection result must agree on status after collection.");

        // Start
        var started = _transitionService.Start(collected, t0.AddMinutes(1)).Plan!;
        storeState = _planStore.Load(pending.Group.GroupId)!;
        Assert.That(storeState.LifecycleStatus, Is.EqualTo(started.LifecycleStatus),
            "Store and transition result must agree on status after start.");

        // Simulate viewer live sync
        var broker = new WeakEventBroker();
        Plan? viewerPlan = null;
        var handler = new PlanViewerLiveSyncHandler(
            started.PlanId, started, broker,
            p => viewerPlan = p);

        // Publish progress event
        var progressed = started with
        {
            Progress = new PlanProgress(2, 4),
            Tasks = started.Tasks.Select((t, i) => i < 2
                ? t with { Status = PlanTaskStatus.Complete }
                : t).ToArray(),
        };
        _planStore.Save(progressed);
        handler.HandleEventDirect(new PlanProgressEvent(started.PlanId, progressed));

        storeState = _planStore.Load(pending.Group.GroupId)!;

        Assert.Multiple(() =>
        {
            Assert.That(viewerPlan, Is.Not.Null);
            Assert.That(viewerPlan!.Progress.CompletedCount,
                Is.EqualTo(storeState.Progress.CompletedCount),
                "Viewer and store must show same completed count.");
            Assert.That(viewerPlan.LifecycleStatus,
                Is.EqualTo(storeState.LifecycleStatus),
                "Viewer and store must show same lifecycle status.");
        });

        handler.Detach();
        AssertTasksMdUnmodified();
    }

    // ── 16. PlanProgressPublisher: persist-then-notify ordering ──────────────

    [Test]
    public void PlanProgressPublisher_PersistSucceeds_NotifyFailure_StillReturnsTrue()
    {
        var plan = MakePlan("PUB-001", PlanLifecycleStatus.Executing, completedCount: 1);
        var persisted = false;
        var notified = false;

        var result = PlanProgressPublisher.TryPublish(
            plan,
            p => { persisted = true; },
            p => { notified = true; throw new InvalidOperationException("Notify failure"); },
            out var persistErr,
            out var notifyErr);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True, "Must return true when persist succeeds.");
            Assert.That(persisted, Is.True);
            Assert.That(notified, Is.True);
            Assert.That(persistErr, Is.Null);
            Assert.That(notifyErr, Is.Not.Null);
        });
    }

    [Test]
    public void PlanProgressPublisher_PersistFails_NotifyNeverCalled()
    {
        var plan = MakePlan("PUB-002", PlanLifecycleStatus.Executing, completedCount: 1);
        var notifyCalled = false;

        var result = PlanProgressPublisher.TryPublish(
            plan,
            p => throw new IOException("Disk full"),
            p => notifyCalled = true,
            out var persistErr,
            out var notifyErr);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False, "Must return false when persist fails.");
            Assert.That(notifyCalled, Is.False, "Notify must never be called if persist fails.");
            Assert.That(persistErr, Is.Not.Null);
            Assert.That(notifyErr, Is.Null);
        });
    }

    // ── 17. Watcher refresh — LoadAll returns consistent state ───────────────

    [Test]
    public void WatcherRefresh_LoadAllAfterMultipleTransitions_ReturnsLatestState()
    {
        var pending = MakePending();
        var t0 = DateTimeOffset.UtcNow;

        // Collect, start, progress
        var collected = _collectionService.Collect(pending, t0).Plan!;
        var started = _transitionService.Start(collected, t0.AddMinutes(1)).Plan!;
        var progressed = started with
        {
            Progress = new PlanProgress(2, 4),
            Tasks = started.Tasks.Select((t, i) => i < 2
                ? t with { Status = PlanTaskStatus.Complete }
                : t).ToArray(),
        };
        _planStore.Save(progressed);

        // Simulate watcher file-change: fresh store reads latest state
        var freshStore = new PlanStore(_squadFolder);
        var allPlans = freshStore.LoadAll();

        Assert.Multiple(() =>
        {
            Assert.That(allPlans, Has.Count.EqualTo(1));
            Assert.That(allPlans[0].Progress.CompletedCount, Is.EqualTo(2));
            Assert.That(allPlans[0].LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        });
    }

    // ── 18. Active plan protection — collection blocked during execution ─────

    [Test]
    [TestCase(PlanLifecycleStatus.Executing)]
    [TestCase(PlanLifecycleStatus.Interrupted)]
    [TestCase(PlanLifecycleStatus.Blocked)]
    [TestCase(PlanLifecycleStatus.AwaitingApproval)]
    public void ActivePlanProtection_CollectionBlockedDuringActiveState(string activeStatus)
    {
        var pending = MakePending();
        // Pre-populate an active plan with matching revision
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow) with
        {
            LifecycleStatus = activeStatus,
        };
        _planStore.Save(plan);

        var result = _collectionService.Collect(pending, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(CollectionOutcome.ActivePlanBlocked));
            Assert.That(result.Plan, Is.Null);
        });
        AssertTasksMdUnmodified();
    }

    // ── 19. DecomposePlanInbox.BuildActionDefinitions includes "Add to Plans" ─

    [Test]
    public void InboxActionDefinitions_IncludeAddToPlansCollectAction()
    {
        var pending = MakePending();
        var actions = DecomposePlanInbox.BuildActionDefinitions(pending, activeBranch: "main");

        var collectAction = actions.FirstOrDefault(a =>
            string.Equals(a.Action, "collect", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(collectAction, Is.Not.Null,
                "Action definitions must include 'collect' action.");
            Assert.That(collectAction!.Label, Is.EqualTo("Add to Plans"));
        });
    }

    // ── 20. Start requires Approved — Staged plans cannot start directly ─────

    [Test]
    public void Start_StagedPlan_IsRejected_RequiresCollectionFirst()
    {
        var plan = MakePlan("STAGED-001", PlanLifecycleStatus.Staged);
        var result = _transitionService.Start(plan, DateTimeOffset.UtcNow);

        Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.InvalidStatus),
            "Staged plans must first be collected (Approved) before they can start.");
    }

    // ── 21. Resume requires Interrupted — Approved plans cannot resume ───────

    [Test]
    public void Resume_ApprovedPlan_IsRejected()
    {
        var plan = MakePlan("RESUME-001", PlanLifecycleStatus.Approved);
        var result = _transitionService.Resume(plan, DateTimeOffset.UtcNow);

        Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.InvalidStatus));
    }

    // ── 22. Idempotent start on already-executing plan ───────────────────────

    [Test]
    public void Start_AlreadyExecuting_IdempotentGuard()
    {
        var plan = MakePlan("IDEMP-001", PlanLifecycleStatus.Executing);
        var result = _transitionService.Start(plan, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.AlreadyExecuting));
            Assert.That(result.Plan!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        });
    }

    // ── 23. Idempotent resume on already-executing plan ─────────────────────

    [Test]
    public void Resume_AlreadyExecuting_IdempotentGuard()
    {
        var plan = MakePlan("IDEMP-002", PlanLifecycleStatus.Executing);
        var result = _transitionService.Resume(plan, DateTimeOffset.UtcNow);

        Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.AlreadyExecuting));
    }

    // ── 24. PlanHasExecutionContext respects lifecycle ───────────────────────

    [Test]
    [TestCase(PlanLifecycleStatus.Staged, false)]
    [TestCase(PlanLifecycleStatus.Approved, false)]
    [TestCase(PlanLifecycleStatus.Executing, true)]
    [TestCase(PlanLifecycleStatus.Interrupted, true)]
    [TestCase(PlanLifecycleStatus.Completed, true)]
    public void PlanHasExecutionContext_CorrectForLifecycleStages(string status, bool expected)
    {
        var plan = MakePlan("CTX-001", status);
        Assert.That(PlanApprovalControlLockPolicy.PlanHasExecutionContext(plan), Is.EqualTo(expected));
    }

    // ── 25. Full lifecycle end-to-end with gates ────────────────────────────

    [Test]
    public void FullLifecycle_WithGates_GateEditabilityTransitionsCorrectly()
    {
        var gates = new[]
        {
            new DecomposedGate("GATE-1", "Phase 1 review",
                AfterTaskIds: ["LIFECYCLE-001-002"],
                BeforeTaskIds: ["LIFECYCLE-001-003"]),
        };
        var group = MakeGroup(gates: gates);
        var pending = MakePending(group);
        var t0 = DateTimeOffset.UtcNow;

        // Collect
        var collected = _collectionService.Collect(pending, t0).Plan!;
        Assert.That(collected.ApprovalGates, Has.Count.EqualTo(1));

        // No execution context yet — no locks
        Assert.That(PlanApprovalControlLockPolicy.PlanHasExecutionContext(collected), Is.False);

        // Start
        var started = _transitionService.Start(collected, t0.AddMinutes(1)).Plan!;
        Assert.That(PlanApprovalControlLockPolicy.PlanHasExecutionContext(started), Is.True);

        // Task 1 executing, task 2 pending — gate is editable
        var phase1 = started with
        {
            Tasks = started.Tasks.Select(t =>
                t.TaskId == "LIFECYCLE-001-001" ? t with { Status = PlanTaskStatus.Executing } : t)
                .ToArray(),
        };
        Assert.That(PlanApprovalControlLockPolicy.IsStageMilestoneLocked(
            phase1, ["LIFECYCLE-001-002"], ["LIFECYCLE-001-003"]), Is.False);

        // Tasks 1-2 complete, task 3 executing — gate becomes locked
        var phase2 = started with
        {
            Tasks = started.Tasks.Select(t => t.TaskId switch
            {
                "LIFECYCLE-001-001" => t with { Status = PlanTaskStatus.Complete },
                "LIFECYCLE-001-002" => t with { Status = PlanTaskStatus.Complete },
                "LIFECYCLE-001-003" => t with { Status = PlanTaskStatus.Executing },
                _ => t,
            }).ToArray(),
        };
        Assert.That(PlanApprovalControlLockPolicy.IsStageMilestoneLocked(
            phase2, ["LIFECYCLE-001-002"], ["LIFECYCLE-001-003"]), Is.True,
            "Gate must lock when upstream completes and downstream starts.");

        AssertTasksMdUnmodified();
    }

    // ── 26. Detached handler ignores events ─────────────────────────────────

    [Test]
    public void DetachedHandler_EventsAfterDetach_AreIgnored()
    {
        var broker = new WeakEventBroker();
        var plan = MakePlan("DETACH-001", PlanLifecycleStatus.Executing, completedCount: 0);
        var received = new List<Plan>();

        var handler = new PlanViewerLiveSyncHandler(
            plan.PlanId, plan, broker,
            p => received.Add(p));

        // First event applies
        var evt1 = plan with { Progress = new PlanProgress(1, 4) };
        handler.HandleEventDirect(new PlanProgressEvent(plan.PlanId, evt1));
        Assert.That(received, Has.Count.EqualTo(1));

        // Detach
        handler.Detach();

        // Post-detach events ignored
        var evt2 = plan with { Progress = new PlanProgress(2, 4) };
        handler.HandleEventDirect(new PlanProgressEvent(plan.PlanId, evt2));
        Assert.That(received, Has.Count.EqualTo(1),
            "No events should be received after detach.");
    }

    // ── 27. Simultaneous UI surfaces ────────────────────────────────────────

    [Test]
    public void SimultaneousUiSurfaces_TwoHandlersReceiveSameEvents()
    {
        var broker = new WeakEventBroker();
        var plan = MakePlan("MULTI-001", PlanLifecycleStatus.Executing, completedCount: 0);
        Plan? panelPlan = null;
        Plan? viewerPlan = null;

        var panelHandler = new PlanViewerLiveSyncHandler(
            plan.PlanId, plan, broker,
            p => panelPlan = p);
        var viewerHandler = new PlanViewerLiveSyncHandler(
            plan.PlanId, plan, broker,
            p => viewerPlan = p);

        var updated = plan with { Progress = new PlanProgress(3, 4) };
        broker.Publish(new PlanProgressEvent(plan.PlanId, updated));

        Assert.Multiple(() =>
        {
            Assert.That(panelPlan, Is.Not.Null);
            Assert.That(viewerPlan, Is.Not.Null);
            Assert.That(panelPlan!.Progress.CompletedCount,
                Is.EqualTo(viewerPlan!.Progress.CompletedCount),
                "Both surfaces must converge on the same completed count.");
        });

        panelHandler.Detach();
        viewerHandler.Detach();
    }

    // ── 28. Three rapid duplicate clicks followed by start ───────────────────

    [Test]
    public void ThreeRapidDuplicateClicks_ThenStart_NoConflict()
    {
        var pending = MakePending();
        var t0 = DateTimeOffset.UtcNow;

        var r1 = _collectionService.Collect(pending, t0);
        var r2 = _collectionService.Collect(pending, t0.AddMilliseconds(100));
        var r3 = _collectionService.Collect(pending, t0.AddMilliseconds(200));

        Assert.Multiple(() =>
        {
            Assert.That(r1.Outcome, Is.EqualTo(CollectionOutcome.Collected));
            Assert.That(r2.Outcome, Is.EqualTo(CollectionOutcome.AlreadyCollected));
            Assert.That(r3.Outcome, Is.EqualTo(CollectionOutcome.AlreadyCollected));
        });

        // Start still works
        var plan = _planStore.Load(pending.Group.GroupId)!;
        var startResult = _transitionService.Start(plan, t0.AddMinutes(1));
        Assert.That(startResult.Outcome, Is.EqualTo(ExecutionTransitionOutcome.Started));
        AssertTasksMdUnmodified();
    }

    // ── 29. Terminal plans cannot start or resume ────────────────────────────

    [Test]
    [TestCase(PlanLifecycleStatus.Completed)]
    [TestCase(PlanLifecycleStatus.Stopped)]
    [TestCase(PlanLifecycleStatus.Archived)]
    public void TerminalPlan_CannotStartOrResume(string terminalStatus)
    {
        var plan = MakePlan("TERMINAL-001", terminalStatus);

        var startResult = _transitionService.Start(plan, DateTimeOffset.UtcNow);
        var resumeResult = _transitionService.Resume(plan, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(startResult.Outcome, Is.EqualTo(ExecutionTransitionOutcome.TerminalPlan));
            Assert.That(resumeResult.Outcome, Is.EqualTo(ExecutionTransitionOutcome.TerminalPlan));
        });
    }

    // ── 30. Mixed stale and valid events interleaved ────────────────────────

    [Test]
    public void MixedStaleAndValidEvents_OnlyValidEventsApply()
    {
        var broker = new WeakEventBroker();
        var plan = MakePlan("MIX-001", PlanLifecycleStatus.Executing, completedCount: 0);
        var applied = new List<int>();

        var handler = new PlanViewerLiveSyncHandler(
            plan.PlanId, plan, broker,
            p => applied.Add(p.Progress.CompletedCount));

        // Interleaved valid and stale events
        handler.HandleEventDirect(new PlanProgressEvent(plan.PlanId,
            plan with { Progress = new PlanProgress(1, 4) }));
        handler.HandleEventDirect(new PlanProgressEvent(plan.PlanId,
            plan with { Progress = new PlanProgress(0, 4) })); // stale
        handler.HandleEventDirect(new PlanProgressEvent(plan.PlanId,
            plan with { Progress = new PlanProgress(2, 4) }));
        handler.HandleEventDirect(new PlanProgressEvent(plan.PlanId,
            plan with { Progress = new PlanProgress(1, 4) })); // stale
        handler.HandleEventDirect(new PlanProgressEvent(plan.PlanId,
            plan with { Progress = new PlanProgress(3, 4) }));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.EqualTo(new[] { 1, 2, 3 }),
                "Only forward-progress events should apply.");
            Assert.That(handler.RejectedCount, Is.EqualTo(2));
        });

        handler.Detach();
    }
}
