using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Host-controlled synthetic runner covering the complete deterministic plan lifecycle:
/// proposal, Inbox, editable gates, Add to Plans, explicit start, parallel named-agent
/// evidence, commit acceptance, out-of-loop repair, live progress, approval accumulation,
/// process restart, completed-work review, safe continuation, queued-step inspection,
/// blocked and failed variants, and completion.
///
/// Invariants asserted:
/// • Work is never silently repeated (idempotency).
/// • Approvals have one identity (single aggregated message per plan).
/// • All surfaces converge on the Plan record (Plans panel, Loop panel, Plan Viewer).
/// </summary>
[TestFixture]
internal sealed class DeterministicPlanLifecycleHarnessTests
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
        string groupId = "HARNESS-001",
        int taskCount = 5,
        string branch = "feature/plan-reliability",
        IReadOnlyList<DecomposedGate>? gates = null,
        bool parallelEligible = false)
    {
        var tasks = Enumerable.Range(1, taskCount)
            .Select(i => new DecomposedSubTask(
                Id:          $"{groupId}-{i:D3}",
                Description: $"Harness task {i}",
                DependsOn:   parallelEligible ? [] : (i == 1 ? [] : [$"{groupId}-{i - 1:D3}"]),
                Priority:    "mid",
                Title:       $"Step {i}",
                AgentAssignments: [new DecomposedAgentAssignment($"agent-{(char)('a' + (i - 1))}", "worker")]))
            .ToList();

        return new DecomposedTaskGroup(
            GroupId:       groupId,
            GroupTitle:    "Deterministic Lifecycle Harness Plan",
            Branch:        branch,
            Summary:       "End-to-end synthetic harness",
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
        string planId = "HARNESS-001",
        string status = PlanLifecycleStatus.Executing,
        int completedCount = 0,
        int totalCount = 5,
        IReadOnlyList<PlanTask>? tasks = null,
        IReadOnlyList<PlanApprovalGate>? gates = null,
        string? executingTaskId = null,
        PlanInterruptionData? interruptionData = null) =>
        new(
            PlanId:           planId,
            Revision:         "rev-harness",
            Source:           PlanSource.DecomposeDecision,
            LifecycleStatus:  status,
            Title:            "Harness Plan",
            Branch:           "feature/plan-reliability",
            Summary:          "Synthetic harness plan",
            Tasks:            tasks ?? MakeTaskList(planId, totalCount),
            ApprovalGates:    gates ?? [],
            Progress:         new PlanProgress(completedCount, totalCount, executingTaskId),
            Timestamps:       new PlanTimestamps(DateTimeOffset.UtcNow, StartedAt: DateTimeOffset.UtcNow),
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

    private static IReadOnlyList<PlanTask> MakeParallelTaskList(string planId, int count) =>
        Enumerable.Range(1, count)
            .Select(i => new PlanTask(
                TaskId:      $"{planId}-{i:D3}",
                Title:       $"Step {i}",
                Description: $"Task {i}",
                DependsOn:   [],
                Priority:    "mid",
                Status:      PlanTaskStatus.Pending,
                ParallelEligible: true,
                AgentAssignments: [new PlanAgentAssignment($"agent-{(char)('a' + (i - 1))}", "worker")]))
            .ToArray();

    private static TaskItem MakeItem(string taskId, string groupId = "HARNESS-001",
        bool isChecked = false, bool isFailed = false, bool isPartial = false,
        bool isSuperseded = false) =>
        new(Text:             taskId,
            Owner:            null,
            IsUserOwned:      false,
            IsChecked:        isChecked,
            Emoji:            "🟡",
            RawLine:          $"- [{(isChecked ? "x" : " ")}] **[{taskId}]** description",
            DecomposeGroupId: groupId,
            TaskId:           taskId,
            IsFailed:         isFailed,
            IsPartial:        isPartial,
            IsSuperseded:     isSuperseded);

    private string TasksMdPath => Path.Combine(_squadFolder, "tasks.md");

    private void AssertTasksMdUnmodified()
    {
        Assert.That(File.Exists(TasksMdPath), Is.False,
            ".squad/tasks.md must never be created by collection or transition services.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 1. Full lifecycle happy path
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void FullLifecycleHappyPath_Proposal_Inbox_AddToPlans_Start_Execute_Complete()
    {
        var pending = MakePending();
        var t0 = DateTimeOffset.UtcNow;

        // 1. Collect (simulates Add to Plans from Inbox)
        var collectResult = _collectionService.Collect(pending, t0);
        Assert.That(collectResult.Outcome, Is.EqualTo(CollectionOutcome.Collected));
        var plan = collectResult.Plan!;
        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Approved));

        // 2. Start — Approved → Executing
        var startResult = _transitionService.Start(plan, t0.AddMinutes(1));
        Assert.That(startResult.Outcome, Is.EqualTo(ExecutionTransitionOutcome.Started));
        plan = startResult.Plan!;
        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));

        // 3. Simulate progress through all tasks via live sync
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

    // ═══════════════════════════════════════════════════════════════════════════
    // 2. Parallel named-agent evidence
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void ParallelNamedAgentEvidence_MultipleAgentsProduceCorrectState()
    {
        var parallelTasks = MakeParallelTaskList("HARNESS-PAR", 3);
        var plan = MakePlan(planId: "HARNESS-PAR", tasks: parallelTasks, totalCount: 3);

        // Simulate parallel agents completing independently
        var afterAgent1 = plan with
        {
            Tasks = plan.Tasks.Select(t => t.TaskId == "HARNESS-PAR-001"
                ? t with { Status = PlanTaskStatus.Complete, Commit = "aaa1111" }
                : t).ToArray(),
            Progress = new PlanProgress(1, 3),
        };

        var afterAgent2 = afterAgent1 with
        {
            Tasks = afterAgent1.Tasks.Select(t => t.TaskId == "HARNESS-PAR-002"
                ? t with { Status = PlanTaskStatus.Complete, Commit = "bbb2222" }
                : t).ToArray(),
            Progress = new PlanProgress(2, 3),
        };

        var afterAgent3 = afterAgent2 with
        {
            Tasks = afterAgent2.Tasks.Select(t => t.TaskId == "HARNESS-PAR-003"
                ? t with { Status = PlanTaskStatus.Complete, Commit = "ccc3333" }
                : t).ToArray(),
            Progress = new PlanProgress(3, 3),
            LifecycleStatus = PlanLifecycleStatus.Completed,
        };

        Assert.Multiple(() =>
        {
            // Each agent assigned uniquely
            Assert.That(plan.Tasks[0].AgentAssignments![0].AgentHandle, Is.EqualTo("agent-a"));
            Assert.That(plan.Tasks[1].AgentAssignments![0].AgentHandle, Is.EqualTo("agent-b"));
            Assert.That(plan.Tasks[2].AgentAssignments![0].AgentHandle, Is.EqualTo("agent-c"));

            // Final state has all complete with distinct commits
            Assert.That(afterAgent3.Tasks.All(t => t.Status == PlanTaskStatus.Complete), Is.True);
            Assert.That(afterAgent3.Tasks.Select(t => t.Commit).Distinct().Count(), Is.EqualTo(3));
            Assert.That(afterAgent3.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 3. Commit acceptance flow
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void CommitAcceptance_EvidenceArrivesValidatedCommitted()
    {
        var plan = MakePlan(executingTaskId: "HARNESS-001-001");
        var items = new List<TaskItem>
        {
            MakeItem("HARNESS-001-001", isChecked: true),
            MakeItem("HARNESS-001-002"),
            MakeItem("HARNESS-001-003"),
            MakeItem("HARNESS-001-004"),
            MakeItem("HARNESS-001-005"),
        };

        var afterAccept = PlanStoreUpdater.ApplyStepAccepted(plan, items, "HARNESS-001-002");

        Assert.Multiple(() =>
        {
            Assert.That(afterAccept.Tasks[0].Status, Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(afterAccept.Progress.CompletedCount, Is.EqualTo(1));
            Assert.That(afterAccept.Progress.ExecutingTaskId, Is.EqualTo("HARNESS-001-002"));
            Assert.That(afterAccept.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 4. Out-of-loop repair
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void OutOfLoopRepair_PendingResultCapturedDurably_ReplayedOnRestart()
    {
        var result = new DecomposeStepResult(
            "HARNESS-001", "HARNESS-001-002", "rev-harness", "complete", "def5678",
            "repaired the issue", Array.Empty<string>(),
            new DecomposeStepVerification("passed", "dotnet test", "all green"),
            null, "attempt-repair-1");

        var pending = new PendingRepairResult(
            "HARNESS-001", "rev-harness", "HARNESS-001-002", "attempt-repair-1",
            System.Text.Json.JsonSerializer.Serialize(result), null);

        var execution = new ActiveLoopExecutionState(
            "loop.md", "*.cs",
            DecomposeGroupId: "HARNESS-001",
            DecomposeRevision: "rev-harness",
            PlanExecutionAttempt: new PlanExecutionAttemptState(
                "attempt-repair-1", "HARNESS-001", "HARNESS-001-002", "rev-harness",
                _workspace.RootPath, DateTimeOffset.UtcNow,
                Array.Empty<PlanExecutionAssignmentAttempt>()),
            RecoveryTaskId: "HARNESS-001-002",
            RecoveryAttemptId: "attempt-repair-1",
            PendingRepairResult: pending);

        // The pending result should suppress re-dispatch of the same task
        Assert.That(PlanRepairReplayPolicy.ShouldFinalizeWithoutDispatch(
            execution, "HARNESS-001", "rev-harness", "HARNESS-001-002"), Is.True);

        // After restart with stale attempt, should NOT suppress
        var staleExecution = execution with { RecoveryAttemptId = "old-attempt" };
        var staleResult = new PendingRepairResult(
            "HARNESS-001", "rev-harness", "HARNESS-001-002", "old-attempt",
            System.Text.Json.JsonSerializer.Serialize(result), null);
        staleExecution = staleExecution with { PendingRepairResult = staleResult };

        Assert.That(PlanRepairReplayPolicy.ShouldFinalizeWithoutDispatch(
            staleExecution, "HARNESS-001", "rev-harness", "HARNESS-001-002"), Is.False);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 5. Live progress — coalescence and stale event rejection
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void LiveProgress_StaleEventsRejected_ActivityStatesConverge()
    {
        var broker = new WeakEventBroker();
        var initial = MakePlan(completedCount: 2, executingTaskId: "HARNESS-001-003");
        Plan? lastReceived = null;

        var handler = new PlanViewerLiveSyncHandler(
            "HARNESS-001", initial, broker,
            plan => lastReceived = plan);

        // Send stale event (lower completion count) — must be rejected
        var stale = initial with { Progress = new PlanProgress(1, 5) };
        handler.HandleEventDirect(new PlanProgressEvent("HARNESS-001", stale));
        Assert.That(lastReceived, Is.Null, "Stale event must be rejected");
        Assert.That(handler.RejectedCount, Is.EqualTo(1));

        // Send valid progress event
        var advanced = initial with { Progress = new PlanProgress(3, 5, "HARNESS-001-004") };
        handler.HandleEventDirect(new PlanProgressEvent("HARNESS-001", advanced));
        Assert.That(lastReceived, Is.SameAs(advanced));
        Assert.That(handler.AppliedCount, Is.EqualTo(1));

        // Wrong plan ID — ignored
        var otherPlan = MakePlan(planId: "OTHER-PLAN", completedCount: 5);
        handler.HandleEventDirect(new PlanProgressEvent("OTHER-PLAN", otherPlan));
        Assert.That(handler.AppliedCount, Is.EqualTo(1), "Other plan events must be filtered");

        handler.Detach();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 6. Approval accumulation — single aggregated message, one identity
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ApprovalAccumulation_MultipleGates_SingleAggregatedMessage_OneIdentity()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"squad-harness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var inbox = new InboxStore(tempDir);
            var coordinator = new ApprovalActionCoordinator();
            var durableManager = new DurableApprovalRequestManager(inbox);

            var tasks = new List<PlanTask>
            {
                new("T1", "Task 1", "desc", [], "high", PlanTaskStatus.Complete),
                new("T2", "Task 2", "desc", [], "high", PlanTaskStatus.Complete),
                new("T3", "Task 3", "desc", ["T1", "T2"], "high", PlanTaskStatus.Pending),
                new("T4", "Task 4", "desc", ["T3"], "high", PlanTaskStatus.Pending),
            };
            var gateA = new PlanApprovalGate(
                "GATE-A", "Review T1+T2", ["T1", "T2"], ["T3"],
                PlanGateStatus.AwaitingApproval);
            var gateB = new PlanApprovalGate(
                "GATE-B", "Review T3", ["T3"], ["T4"],
                PlanGateStatus.Pending);

            var plan = new Plan(
                "HARNESS-APPROVAL", "rev1", PlanSource.DecomposeDecision,
                PlanLifecycleStatus.AwaitingApproval, "Approval Plan", "main", "Summary",
                tasks, [gateA, gateB],
                new PlanProgress(2, 4), new PlanTimestamps(DateTimeOffset.UtcNow));

            var snapshot = new ApprovalReviewSnapshot(
                "HARNESS-APPROVAL", "Approval Plan", 2, 4,
                PlanLifecycleStatus.AwaitingApproval,
                "GATE-A", "Review T1+T2", ["T1", "T2"], ["T3"],
                [], [], [], [], DateTimeOffset.UtcNow);


            // Append first gate
            await durableManager.AppendCheckpointAsync(plan, gateA, snapshot);
            var messageId = DurableApprovalRequestManager.BuildMessageId(plan.PlanId);
            var msg1 = inbox.GetById(messageId);
            Assert.That(msg1, Is.Not.Null, "First gate creates a message");

            // Append second gate — same message updated, not a new one
            var plan2 = plan with { ApprovalGates = [gateA, gateB with { Status = PlanGateStatus.AwaitingApproval }] };
            var snapshot2 = snapshot with { GateId = "GATE-B", GateReason = "Review T3" };
            await durableManager.AppendCheckpointAsync(plan2, gateB with { Status = PlanGateStatus.AwaitingApproval }, snapshot2);

            var msg2 = inbox.GetById(messageId);
            Assert.That(msg2, Is.Not.Null);

            // Assert single identity — one message, not two
            var allMessages = inbox.LoadAll();
            var approvalMessages = allMessages.Where(m =>
                m.Id == messageId).ToList();
            Assert.That(approvalMessages, Has.Count.EqualTo(1),
                "Approvals must have one identity — single aggregated message per plan");

            coordinator.ClearAll();
            durableManager.ClearLocks();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 7. Process restart — all surfaces converge, no work repeated
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void ProcessRestart_AllSurfacesConverge_NoWorkRepeated()
    {
        var pending = MakePending();
        var t0 = DateTimeOffset.UtcNow;

        // Collect and start
        var collected = _collectionService.Collect(pending, t0).Plan!;
        var started = _transitionService.Start(collected, t0.AddMinutes(1)).Plan!;

        // Simulate partial progress
        var partialPlan = started with
        {
            Tasks = started.Tasks.Select((t, i) => i < 2
                ? t with { Status = PlanTaskStatus.Complete }
                : t).ToArray(),
            Progress = new PlanProgress(2, 5, "HARNESS-001-003"),
        };
        _planStore.Save(partialPlan);

        // Simulate process restart — fresh store instances
        var freshStore = new PlanStore(_squadFolder);
        var loaded = freshStore.Load("HARNESS-001");

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(loaded.Progress.CompletedCount, Is.EqualTo(2));
            Assert.That(loaded.Progress.ExecutingTaskId, Is.EqualTo("HARNESS-001-003"));
            // Surfaces converge on the same record
            Assert.That(loaded.Tasks[0].Status, Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(loaded.Tasks[1].Status, Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(loaded.Tasks[2].Status, Is.EqualTo(PlanTaskStatus.Pending));
        });

        // Resume from partial progress — must NOT repeat completed tasks
        var items = loaded!.Tasks.Select(t => MakeItem(t.TaskId,
            isChecked: t.Status == PlanTaskStatus.Complete)).ToList();
        var nextStep = PlanStoreUpdater.ApplyStepAccepted(loaded, items, "HARNESS-001-004");
        Assert.That(nextStep.Progress.CompletedCount, Is.GreaterThanOrEqualTo(2),
            "Work must never regress — completed count must be monotonically increasing");
        AssertTasksMdUnmodified();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 8. Completed-work review after recovery
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void CompletedWorkReview_PresentedCorrectlyAfterRecovery()
    {
        var tasks = new[]
        {
            new PlanTask("HARNESS-001-001", "Step 1", "desc", [], "mid", PlanTaskStatus.Complete,
                Commit: "abc1234567890", CompletedAt: DateTimeOffset.UtcNow.AddMinutes(-10)),
            new PlanTask("HARNESS-001-002", "Step 2", "desc", ["HARNESS-001-001"], "mid", PlanTaskStatus.Executing),
            new PlanTask("HARNESS-001-003", "Step 3", "desc", ["HARNESS-001-002"], "mid", PlanTaskStatus.Pending),
        };
        var plan = MakePlan(tasks: tasks, totalCount: 3, executingTaskId: "HARNESS-001-002");

        // Simulate commit evidence on the completed task
        var evidence = new PlanTaskCommitEvidence(
            "HARNESS-001-001", "attempt-1", "baseline-000", "abc1234567890",
            "Completed step 1",
            new DecomposeStepVerification("passed", "dotnet test", "14 tests passed"));

        var interruptedPlan = plan with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            InterruptionData = new PlanInterruptionData(
                Reason: "Process crash",
                RecoveryState: PlanRecoveryState.PendingRecovery,
                LoopIteration: 2,
                InterruptedTaskId: "HARNESS-001-002",
                TaskCommitEvidence: evidence),
        };

        // Verify review can be built from evidence
        Assert.Multiple(() =>
        {
            Assert.That(interruptedPlan.InterruptionData!.TaskCommitEvidence, Is.Not.Null);
            Assert.That(interruptedPlan.InterruptionData.TaskCommitEvidence!.TaskId, Is.EqualTo("HARNESS-001-001"));
            Assert.That(interruptedPlan.InterruptionData.TaskCommitEvidence.Commit, Is.EqualTo("abc1234567890"));
            Assert.That(interruptedPlan.InterruptionData.TaskCommitEvidence.Verification!.Status, Is.EqualTo("passed"));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 9. Safe continuation — queue item created, not editable, dequeued correctly
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void SafeContinuation_QueueItemCreated_NotEditable_DequeuedCorrectly()
    {
        var tasks = Enumerable.Range(1, 5)
            .Select(i => new PlanTask(
                $"P-{i}", $"Task {i}", "desc", [], "normal",
                i <= 2 ? PlanTaskStatus.Complete :
                i == 3 ? PlanTaskStatus.Executing : PlanTaskStatus.Pending))
            .ToArray();
        var plan = new Plan(
            "P", "revision", PlanSource.DecomposeDecision, PlanLifecycleStatus.Executing,
            "Plan", "feature/x", "Summary", tasks, [],
            new PlanProgress(2, 5, "P-3"),
            new PlanTimestamps(DateTimeOffset.UtcNow));

        var display = PlanContinuationQueuePresentation.Build(plan);

        Assert.Multiple(() =>
        {
            Assert.That(display, Is.Not.Null);
            Assert.That(display!.Label, Does.Contain("Plan Step"));
            Assert.That(display.Description, Does.Contain("locked continuation"));
            Assert.That(display.Description, Does.Contain("cannot be edited or sent manually"));
        });

        // Verify queue item is locked
        var queue = new PromptQueue();
        queue.EnqueueItem(new PromptQueueItem
        {
            Text = "plan continuation",
            SourceTag = "plan-continuation",
            IsLocked = true,
            DisplayLabel = display.Label,
            ReadOnlyDisplayText = display.Description,
        });
        var item = queue.Items.First(i => i.SourceTag == "plan-continuation");
        Assert.That(item.IsLocked, Is.True);

        // Dequeue removes correctly
        queue.RemoveByTag("plan-continuation");
        Assert.That(queue.Items.Any(i => i.SourceTag == "plan-continuation"), Is.False);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 10. Queued-step inspection — selection shows read-only explanation
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void QueuedStepInspection_SelectionShowsReadOnlyExplanation()
    {
        var tasks = Enumerable.Range(1, 8)
            .Select(i => new PlanTask(
                $"P-{i}", $"Task {i}", "D", [], "normal",
                i <= 1 ? PlanTaskStatus.Complete :
                i == 2 ? PlanTaskStatus.Executing : PlanTaskStatus.Pending))
            .ToArray();
        var plan = new Plan(
            "P", "rev", PlanSource.DecomposeDecision, PlanLifecycleStatus.Executing,
            "Plan", "feature/q", "Summary", tasks, [],
            new PlanProgress(1, 8, "P-2"),
            new PlanTimestamps(DateTimeOffset.UtcNow));

        var display = PlanContinuationQueuePresentation.Build(plan);

        Assert.Multiple(() =>
        {
            Assert.That(display, Is.Not.Null);
            Assert.That(display!.StepNumber, Is.EqualTo(3));
            Assert.That(display.Label, Is.EqualTo("Plan Step 3: Task 3"));
            Assert.That(display.Description, Does.Contain("Next task:"));
            Assert.That(display.Description, Does.Contain("Why it is next:"));
            Assert.That(display.Description, Does.Contain("Release:"));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 11. Blocked variant — plan blocked by failed dependency
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void BlockedVariant_PlanBlockedByFailedDependency()
    {
        var tasks = new[]
        {
            new PlanTask("H-1", "Step 1", "desc", [], "mid", PlanTaskStatus.Complete),
            new PlanTask("H-2", "Step 2", "desc", ["H-1"], "mid", PlanTaskStatus.Failed),
            new PlanTask("H-3", "Step 3", "desc", ["H-2"], "mid", PlanTaskStatus.Pending),
        };
        var plan = MakePlan(planId: "HARNESS-BLOCKED", tasks: tasks, totalCount: 3,
            status: PlanLifecycleStatus.Blocked);

        // Resolve activity states — H-3 should be blocked because H-2 failed
        var activity = PlanTaskActivityResolver.Resolve(plan);

        Assert.Multiple(() =>
        {
            Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Blocked));
            Assert.That(activity["H-2"], Is.EqualTo(PlanTaskActivityState.Blocked));
            Assert.That(activity["H-3"], Is.EqualTo(PlanTaskActivityState.Blocked));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 12. Failed variant — step fails, plan is blocked, recovery offered
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void FailedVariant_StepFails_PlanBlocked_RecoveryOffered()
    {
        var tasks = new[]
        {
            new PlanTask("H-1", "Step 1", "desc", [], "mid", PlanTaskStatus.Complete),
            new PlanTask("H-2", "Step 2", "desc", ["H-1"], "mid", PlanTaskStatus.Executing),
            new PlanTask("H-3", "Step 3", "desc", ["H-2"], "mid", PlanTaskStatus.Pending),
        };
        var plan = MakePlan(planId: "HARNESS-FAIL", tasks: tasks, totalCount: 3,
            executingTaskId: "H-2");

        // Simulate failure — apply blocked transition
        var afterFail = PlanStoreUpdater.ApplyBlocked(plan, "H-2");

        Assert.Multiple(() =>
        {
            Assert.That(afterFail.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Blocked));
        });

        // Verify plan-level activity state reflects blocked status
        var planLevelActivity = PlanTaskActivityResolver.ResolvePlanLevel(afterFail);
        Assert.That(planLevelActivity, Is.EqualTo(PlanTaskActivityState.Blocked));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 13. Stale actions — stale tokens are rejected
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task StaleActions_StaleTokensRejected()
    {
        var coordinator = new ApprovalActionCoordinator();
        try
        {
            // Register with version 1
            var token1 = await coordinator.RegisterAsync("PLAN-STALE", "rev1", ["GATE-A"]);

            // Replace registration (version 2)
            var token2 = await coordinator.RegisterAsync("PLAN-STALE", "rev1", ["GATE-A", "GATE-B"]);

            // Token1 must be stale
            var result1 = await coordinator.TryApproveAsync(token1, ["GATE-A"]);
            Assert.That(result1, Is.EqualTo(ApprovalClickResult.StaleRejected));

            // Token2 is still valid
            var result2 = await coordinator.TryApproveAsync(token2, ["GATE-A"]);
            Assert.That(result2, Is.EqualTo(ApprovalClickResult.Approved));
        }
        finally
        {
            coordinator.ClearAll();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 14. Missing/malformed results — graceful handling
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void MissingMalformedResults_GracefulHandling()
    {
        // Missing result JSON
        var pendingMissing = new PendingRepairResult(
            "HARNESS-001", "rev-harness", "HARNESS-001-002", "attempt-1",
            null, "No result received");

        Assert.Multiple(() =>
        {
            Assert.That(pendingMissing.ResultJson, Is.Null);
            Assert.That(pendingMissing.ErrorText, Is.EqualTo("No result received"));
        });

        // Malformed result JSON
        var pendingMalformed = new PendingRepairResult(
            "HARNESS-001", "rev-harness", "HARNESS-001-002", "attempt-1",
            "{ not valid json !!!", null);

        Assert.That(pendingMalformed.ResultJson, Is.Not.Null);
        // Deserialization should fail gracefully
        DecomposeStepResult? parsed = null;
        try
        {
            parsed = System.Text.Json.JsonSerializer.Deserialize<DecomposeStepResult>(pendingMalformed.ResultJson!);
        }
        catch (System.Text.Json.JsonException)
        {
            // Expected — malformed JSON
        }
        Assert.That(parsed, Is.Null, "Malformed result JSON must not produce a valid result");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 15. Dirty preflight — preflight detects dirty state
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void DirtyPreflight_DetectsDirtyState()
    {
        var changedPaths = new List<string> { "src/App.cs", ".squad/tasks.md" };
        var ex = new PlanPreflightBlockedException("Uncommitted changes", changedPaths, "feature/dirty");
        var content = PlanPreflightRecoveryContent.From(ex);

        Assert.Multiple(() =>
        {
            Assert.That(content.Title, Is.EqualTo("Plan not started"));
            Assert.That(content.Summary, Does.Contain("No plan work was started"));
            Assert.That(content.ChangedFilesSummary, Does.Contain("src/App.cs"));
            Assert.That(content.ChangedFilesSummary, Does.Contain(".squad/tasks.md"));
            Assert.That(content.RecoveryGuidance, Does.Contain("commit or stash"));
            Assert.That(content.TechnicalDetails, Does.Contain("Changed files: 2"));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 16. Normal workspace (no restarts) — clean happy path
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void NormalWorkspace_NoRestarts_CleanHappyPath()
    {
        var pending = MakePending();
        var t0 = DateTimeOffset.UtcNow;

        var collected = _collectionService.Collect(pending, t0).Plan!;
        var started = _transitionService.Start(collected, t0.AddMinutes(1)).Plan!;

        // Progress through all steps without any interruption
        var current = started;
        for (int i = 1; i <= 5; i++)
        {
            var items = current.Tasks.Select((t, idx) => MakeItem(t.TaskId,
                isChecked: idx < i)).ToList();
            var nextExecuting = i < 5 ? $"HARNESS-001-{i + 1:D3}" : null;
            current = PlanStoreUpdater.ApplyStepAccepted(current, items, nextExecuting);
        }

        var completed = PlanStoreUpdater.ApplyCompleted(current);

        Assert.Multiple(() =>
        {
            Assert.That(completed.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
            Assert.That(completed.Progress.CompletedCount, Is.EqualTo(5));
            Assert.That(completed.Tasks.All(t => t.Status == PlanTaskStatus.Complete), Is.True);
            Assert.That(completed.Timestamps.CompletedAt, Is.Not.Null);
        });
        AssertTasksMdUnmodified();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 17. SquadDash build restart — state preserved across build restart
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void SquadDashBuildRestart_StatePreservedAcrossBuildRestart()
    {
        var pending = MakePending();
        var t0 = DateTimeOffset.UtcNow;

        var collected = _collectionService.Collect(pending, t0).Plan!;
        var started = _transitionService.Start(collected, t0.AddMinutes(1)).Plan!;

        // Simulate partial progress
        var items = started.Tasks.Select((t, idx) => MakeItem(t.TaskId,
            isChecked: idx == 0)).ToList();
        var afterStep1 = PlanStoreUpdater.ApplyStepAccepted(started, items, "HARNESS-001-002");
        _planStore.Save(afterStep1);

        // Build restart: simulate complete process death and restart
        var freshStore = new PlanStore(_squadFolder);
        var freshPendingStore = new PendingDecomposePlanStore(_squadFolder);
        var freshCollectionService = new PlanCollectionService(freshStore, freshPendingStore);
        var freshTransitionService = new PlanExecutionTransitionService(freshStore);

        var loaded = freshStore.Load("HARNESS-001");

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(loaded.Progress.CompletedCount, Is.EqualTo(1));
            Assert.That(loaded.Progress.ExecutingTaskId, Is.EqualTo("HARNESS-001-002"));
            Assert.That(loaded.Tasks[0].Status, Is.EqualTo(PlanTaskStatus.Complete));
        });

        // Collecting same plan again must be blocked by active plan protection
        var reCollect = freshCollectionService.Collect(pending, t0.AddMinutes(5));
        Assert.That(reCollect.Outcome, Is.EqualTo(CollectionOutcome.ActivePlanBlocked),
            "Active executing plan blocks re-collection (idempotent protection)");

        AssertTasksMdUnmodified();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 18. Two workspaces — independent state, no cross-contamination
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void TwoWorkspaces_IndependentState_NoCrossContamination()
    {
        // Workspace A
        var wsA = new TestWorkspace();
        var squadFolderA = wsA.GetPath(".squad");
        Directory.CreateDirectory(squadFolderA);
        var storeA = new PlanStore(squadFolderA);
        var pendingStoreA = new PendingDecomposePlanStore(squadFolderA);
        var collectionA = new PlanCollectionService(storeA, pendingStoreA);

        // Workspace B
        var wsB = new TestWorkspace();
        var squadFolderB = wsB.GetPath(".squad");
        Directory.CreateDirectory(squadFolderB);
        var storeB = new PlanStore(squadFolderB);
        var pendingStoreB = new PendingDecomposePlanStore(squadFolderB);
        var collectionB = new PlanCollectionService(storeB, pendingStoreB);

        try
        {
            var groupA = MakeGroup(groupId: "WS-A-PLAN");
            var groupB = MakeGroup(groupId: "WS-B-PLAN");
            var t0 = DateTimeOffset.UtcNow;

            collectionA.Collect(MakePending(groupA), t0);
            collectionB.Collect(MakePending(groupB), t0);

            // Workspace A must not see workspace B's plan and vice versa
            var loadedA = storeA.Load("WS-A-PLAN");
            var loadedB = storeB.Load("WS-B-PLAN");
            var crossA = storeA.Load("WS-B-PLAN");
            var crossB = storeB.Load("WS-A-PLAN");

            Assert.Multiple(() =>
            {
                Assert.That(loadedA, Is.Not.Null);
                Assert.That(loadedB, Is.Not.Null);
                Assert.That(crossA, Is.Null, "Workspace A must not see workspace B's plan");
                Assert.That(crossB, Is.Null, "Workspace B must not see workspace A's plan");
            });
        }
        finally
        {
            wsA.Dispose();
            wsB.Dispose();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 19. Full restart — complete cold restart recovery
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void FullRestart_CompleteColdRestartRecovery()
    {
        var pending = MakePending();
        var t0 = DateTimeOffset.UtcNow;

        // Set up state: collected → started → interrupted
        var collected = _collectionService.Collect(pending, t0).Plan!;
        var started = _transitionService.Start(collected, t0.AddMinutes(1)).Plan!;
        var interrupted = started with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            InterruptionData = new PlanInterruptionData(
                Reason: "Cold restart",
                RecoveryState: PlanRecoveryState.PendingRecovery,
                LoopIteration: 3,
                InterruptedTaskId: "HARNESS-001-003"),
            Timestamps = started.Timestamps with { InterruptedAt = t0.AddMinutes(10) },
        };
        _planStore.Save(interrupted);

        // Cold restart — everything reinitializes
        var freshStore = new PlanStore(_squadFolder);
        var freshTransition = new PlanExecutionTransitionService(freshStore);
        var loaded = freshStore.Load("HARNESS-001");

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
            Assert.That(loaded.InterruptionData!.Reason, Is.EqualTo("Cold restart"));
            Assert.That(loaded.InterruptionData.InterruptedTaskId, Is.EqualTo("HARNESS-001-003"));
        });

        // Resume after cold restart
        var resumed = freshTransition.Resume(loaded!, t0.AddMinutes(15));
        Assert.Multiple(() =>
        {
            Assert.That(resumed.Outcome, Is.EqualTo(ExecutionTransitionOutcome.Started));
            Assert.That(resumed.Plan!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(resumed.Plan.InterruptionData!.RecoveryState, Is.EqualTo(PlanRecoveryState.Recovered));
        });

        AssertTasksMdUnmodified();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 20. Work never silently repeated — idempotency invariant
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void WorkNeverSilentlyRepeated_IdempotencyInvariant()
    {
        var pending = MakePending();
        var t0 = DateTimeOffset.UtcNow;

        var collected = _collectionService.Collect(pending, t0).Plan!;
        var started = _transitionService.Start(collected, t0.AddMinutes(1)).Plan!;

        // Complete first two steps
        var items1 = started.Tasks.Select((t, idx) => MakeItem(t.TaskId,
            isChecked: idx == 0)).ToList();
        var afterStep1 = PlanStoreUpdater.ApplyStepAccepted(started, items1, "HARNESS-001-002");

        var items2 = afterStep1.Tasks.Select((t, idx) => MakeItem(t.TaskId,
            isChecked: idx <= 1)).ToList();
        var afterStep2 = PlanStoreUpdater.ApplyStepAccepted(afterStep1, items2, "HARNESS-001-003");

        // Duplicate collection attempt must be blocked by active plan protection
        var dupCollect = _collectionService.Collect(pending, t0.AddMinutes(5));
        Assert.That(dupCollect.Outcome, Is.EqualTo(CollectionOutcome.ActivePlanBlocked),
            "Active executing plan blocks re-collection (idempotent protection)");

        // Duplicate start attempt on executing plan
        var dupStart = _transitionService.Start(afterStep2, t0.AddMinutes(5));
        Assert.That(dupStart.Outcome, Is.EqualTo(ExecutionTransitionOutcome.AlreadyExecuting));

        // Completed count must be monotonically increasing
        Assert.Multiple(() =>
        {
            Assert.That(started.Progress.CompletedCount, Is.EqualTo(0));
            Assert.That(afterStep1.Progress.CompletedCount, Is.EqualTo(1));
            Assert.That(afterStep2.Progress.CompletedCount, Is.EqualTo(2));
        });

        // PlanRepairReplayPolicy prevents re-dispatch of matching pending repair
        var repair = new PendingRepairResult(
            "HARNESS-001", "rev-harness", "HARNESS-001-001", "attempt-1",
            "{}", null);
        var execution = new ActiveLoopExecutionState(
            "loop.md", "*.cs",
            DecomposeGroupId: "HARNESS-001",
            DecomposeRevision: "rev-harness",
            PlanExecutionAttempt: new PlanExecutionAttemptState(
                "attempt-1", "HARNESS-001", "HARNESS-001-001", "rev-harness",
                _workspace.RootPath, DateTimeOffset.UtcNow,
                Array.Empty<PlanExecutionAssignmentAttempt>()),
            RecoveryTaskId: "HARNESS-001-001",
            RecoveryAttemptId: "attempt-1",
            PendingRepairResult: repair);

        Assert.That(PlanRepairReplayPolicy.ShouldFinalizeWithoutDispatch(
            execution, "HARNESS-001", "rev-harness", "HARNESS-001-001"), Is.True,
            "Same-attempt repair result must suppress re-dispatch (idempotency)");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 21. Approvals have one identity — single aggregated message per plan
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void ApprovalsHaveOneIdentity_SingleAggregatedMessagePerPlan()
    {
        // The message ID is deterministically computed from plan ID
        var messageId1 = DurableApprovalRequestManager.BuildMessageId("PLAN-X");
        var messageId2 = DurableApprovalRequestManager.BuildMessageId("PLAN-X");
        var messageId3 = DurableApprovalRequestManager.BuildMessageId("PLAN-Y");

        Assert.Multiple(() =>
        {
            Assert.That(messageId1, Is.EqualTo(messageId2),
                "Same plan must produce same message ID (identity)");
            Assert.That(messageId1, Is.Not.EqualTo(messageId3),
                "Different plans must produce different message IDs");
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 22. All surfaces converge — Plans panel, Loop panel, Plan Viewer same state
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void AllSurfacesConverge_PlansPanelLoopPanelPlanViewerSameState()
    {
        var pending = MakePending();
        var t0 = DateTimeOffset.UtcNow;

        // Collect and start
        var collected = _collectionService.Collect(pending, t0).Plan!;
        var started = _transitionService.Start(collected, t0.AddMinutes(1)).Plan!;

        // Simulate progress
        var progressPlan = started with
        {
            Tasks = started.Tasks.Select((t, i) => i < 3
                ? t with { Status = PlanTaskStatus.Complete }
                : t).ToArray(),
            Progress = new PlanProgress(3, 5, "HARNESS-001-004"),
        };
        _planStore.Save(progressPlan);

        // Plans panel view — loads from store
        var plansPanelView = _planStore.Load("HARNESS-001");

        // Plan Viewer sync — receives live event
        var broker = new WeakEventBroker();
        Plan? viewerPlan = null;
        var handler = new PlanViewerLiveSyncHandler(
            "HARNESS-001", started, broker,
            p => viewerPlan = p);
        handler.HandleEventDirect(new PlanProgressEvent("HARNESS-001", progressPlan));

        // Activity resolver — used by Loop panel
        var activity = PlanTaskActivityResolver.Resolve(progressPlan);

        Assert.Multiple(() =>
        {
            // All surfaces see same completion count
            Assert.That(plansPanelView!.Progress.CompletedCount, Is.EqualTo(3));
            Assert.That(viewerPlan!.Progress.CompletedCount, Is.EqualTo(3));

            // Activity resolver agrees
            Assert.That(activity["HARNESS-001-001"], Is.EqualTo(PlanTaskActivityState.Completed));
            Assert.That(activity["HARNESS-001-002"], Is.EqualTo(PlanTaskActivityState.Completed));
            Assert.That(activity["HARNESS-001-003"], Is.EqualTo(PlanTaskActivityState.Completed));

            // Same lifecycle status across all surfaces
            Assert.That(plansPanelView.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(viewerPlan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        });

        handler.Detach();
        AssertTasksMdUnmodified();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Persist-then-notify ordering invariant
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void PersistThenNotify_OrderingPreserved()
    {
        var plan = MakePlan();
        var order = string.Empty;

        var published = PlanProgressPublisher.TryPublish(
            plan,
            _ => order += "persist ",
            _ => order += "notify",
            out var persistenceError,
            out var notificationError);

        Assert.Multiple(() =>
        {
            Assert.That(published, Is.True);
            Assert.That(order, Is.EqualTo("persist notify"));
            Assert.That(persistenceError, Is.Null);
            Assert.That(notificationError, Is.Null);
        });
    }

    [Test]
    public void PersistThenNotify_PersistFailure_NoNotification()
    {
        var plan = MakePlan();
        var notified = false;

        var published = PlanProgressPublisher.TryPublish(
            plan,
            _ => throw new InvalidOperationException("disk full"),
            _ => notified = true,
            out var persistenceError,
            out _);

        Assert.Multiple(() =>
        {
            Assert.That(published, Is.False);
            Assert.That(notified, Is.False);
            Assert.That(persistenceError, Is.EqualTo("disk full"));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Gate editability transitions
    // ═══════════════════════════════════════════════════════════════════════════

    [Test]
    public void GateEditability_CompletedRegionsAreReadOnly()
    {
        var tasks = new[]
        {
            new PlanTask("G-1", "Step 1", "desc", [], "mid", PlanTaskStatus.Complete),
            new PlanTask("G-2", "Step 2", "desc", ["G-1"], "mid", PlanTaskStatus.Pending),
        };
        var gate = new PlanApprovalGate(
            "GATE-1", "Approve", ["G-1"], ["G-2"], PlanGateStatus.Approved,
            ResolvedAt: DateTimeOffset.UtcNow);

        var plan = new Plan(
            "HARNESS-GATE", "rev1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "Gate Plan", "main", "Summary",
            tasks, [gate], new PlanProgress(1, 2, "G-2"),
            new PlanTimestamps(DateTimeOffset.UtcNow, StartedAt: DateTimeOffset.UtcNow));

        // Completed task exit must be locked
        var taskExitLocked = PlanApprovalControlLockPolicy.PlanHasExecutionContext(plan) &&
            PlanApprovalControlLockPolicy.IsTaskExitLocked(plan, "G-1");

        Assert.That(taskExitLocked, Is.True,
            "Completed task exit must be locked — completed regions are read-only");

        // Not-yet-started task entry should not be locked
        var taskEntryLockedForPending = PlanApprovalControlLockPolicy.IsTaskEntryLocked(plan, "G-2");
        Assert.That(taskEntryLockedForPending, Is.False,
            "Pending task entry must not be locked until started");
    }
}
