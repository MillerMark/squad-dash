using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanStoreUpdaterTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static DecomposedTaskGroup MakeGroup(int taskCount = 3)
    {
        var tasks = Enumerable.Range(1, taskCount)
            .Select(i => new DecomposedSubTask(
                Id:          $"GROUP-001-00{i}",
                Description: $"Task {i} description",
                DependsOn:   i == 1 ? [] : [$"GROUP-001-00{i - 1}"],
                Priority:    "mid",
                Title:       $"Task {i}"))
            .ToList();

        return new DecomposedTaskGroup(
            GroupId:    "GROUP-001",
            GroupTitle: "Test Plan",
            Branch:     "feature/test",
            Summary:    "A test plan",
            Tasks:      tasks);
    }

    private static DecomposedTaskGroup MakeApprovalWindowGroup() => new(
        GroupId: "GROUP-001",
        GroupTitle: "Approval window plan",
        Branch: "feature/approval-window",
        Summary: "Continue independent work, then stop at the approval frontier.",
        Tasks:
        [
            new DecomposedSubTask(
                "GROUP-001-001", "Create baseline", [], "high", "Create baseline"),
            new DecomposedSubTask(
                "GROUP-001-002", "Run independent work", [], "high", "Run independent work"),
            new DecomposedSubTask(
                "GROUP-001-003", "Cross approved boundary", ["GROUP-001-001"], "high",
                "Cross approved boundary"),
            new DecomposedSubTask(
                "GROUP-001-004", "Summarize", ["GROUP-001-002", "GROUP-001-003"], "mid",
                "Summarize"),
        ],
        ApprovalGates:
        [
            new DecomposedGate(
                "GROUP-001-G01",
                "Review the baseline before crossing the boundary.",
                ["GROUP-001-001"],
                ["GROUP-001-003"]),
        ]);

    private static TaskItem MakeItem(string taskId, bool isChecked = false, bool isFailed = false,
        bool isPartial = false, bool isSuperseded = false)
    {
        return new TaskItem(
            Text:             taskId,
            Owner:            null,
            IsUserOwned:      false,
            IsChecked:        isChecked,
            Emoji:            "🟡",
            RawLine:          $"- [{(isChecked ? "x" : " ")}] **[{taskId}]** description",
            DecomposeGroupId: "GROUP-001",
            TaskId:           taskId,
            IsFailed:         isFailed,
            IsPartial:        isPartial,
            IsSuperseded:     isSuperseded);
    }

    private static Plan MakeExecutingPlan(int completed, int total, string? executingTaskId = null)
    {
        var progress   = new PlanProgress(completed, total, executingTaskId);
        var timestamps = new PlanTimestamps(DateTimeOffset.UtcNow, StartedAt: DateTimeOffset.UtcNow);
        return new Plan(
            PlanId:          "GROUP-001",
            Revision:        "rev1",
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Executing,
            Title:           "Test Plan",
            Branch:          "feature/test",
            Summary:         "A test plan",
            Tasks:           [],
            ApprovalGates:   [],
            Progress:        progress,
            Timestamps:      timestamps);
    }

    // ── ApplyExecutionStarted ─────────────────────────────────────────────────

    [Test]
    public void ApplyExecutionStarted_CreatesNewPlanWhenNoneExists()
    {
        var group = MakeGroup(3);
        var items = new List<TaskItem>
        {
            MakeItem("GROUP-001-001"),
            MakeItem("GROUP-001-002"),
            MakeItem("GROUP-001-003"),
        };

        var plan = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, "GROUP-001-001");

        Assert.That(plan.PlanId,          Is.EqualTo("GROUP-001"));
        Assert.That(plan.Revision,        Is.EqualTo("rev1"));
        Assert.That(plan.HostRevision,    Is.EqualTo("rev1"));
        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(plan.Title,           Is.EqualTo("Test Plan"));
        Assert.That(plan.Branch,          Is.EqualTo("feature/test"));
        Assert.That(plan.Source,          Is.EqualTo(PlanSource.DecomposeDecision));
    }

    [Test]
    public void ApplyExecutionStarted_FreshApprovalPlan_PreservesGateAndApprovedRevision()
    {
        var group = MakeApprovalWindowGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToArray();

        var plan = PlanStoreUpdater.ApplyExecutionStarted(
            existing: null,
            group,
            revision,
            items,
            "GROUP-001-001");

        Assert.Multiple(() =>
        {
            Assert.That(plan.ApprovalGates, Has.Count.EqualTo(1));
            Assert.That(plan.ApprovalGates[0].GateId, Is.EqualTo("GROUP-001-G01"));
            Assert.That(plan.ApprovalGates[0].AfterTaskIds, Is.EqualTo(new[] { "GROUP-001-001" }));
            Assert.That(plan.ApprovalGates[0].BeforeTaskIds, Is.EqualTo(new[] { "GROUP-001-003" }));
            Assert.That(plan.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.Pending));
            Assert.That(plan.ApprovalGates[0].PlanRevision, Is.EqualTo(revision));
            Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(plan), Is.True);
        });
    }

    [Test]
    public void ApplyExecutionStarted_ApprovalWindow_AllowsIndependentTaskThenBlocksFrontier()
    {
        var group = MakeApprovalWindowGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToList();
        var plan = PlanStoreUpdater.ApplyExecutionStarted(
            null, group, revision, items, "GROUP-001-001");

        items[0] = MakeItem("GROUP-001-001", isChecked: true);
        plan = PlanStoreUpdater.ApplyStepAccepted(plan, items, "GROUP-001-002");
        var afterBaseline = ApprovalGateReadinessEvaluator.EvaluateGates(plan);

        Assert.Multiple(() =>
        {
            Assert.That(afterBaseline.Single().IsReady, Is.True);
            Assert.That(
                ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan, afterBaseline),
                Is.EqualTo("GROUP-001-002"),
                "Independent work should continue while approval is available.");
            Assert.That(ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan, afterBaseline), Is.False);
        });

        items[1] = MakeItem("GROUP-001-002", isChecked: true);
        plan = PlanStoreUpdater.ApplyStepAccepted(plan, items, nextExecutingTaskId: null);
        var afterIndependent = ApprovalGateReadinessEvaluator.EvaluateGates(plan);

        Assert.Multiple(() =>
        {
            Assert.That(ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan, afterIndependent), Is.Null);
            Assert.That(ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan, afterIndependent), Is.True);
            Assert.That(
                PlanGateVisualizationPolicy.DownstreamTaskIds(plan.Tasks, plan.ApprovalGates),
                Does.Contain("GROUP-001-003"),
                "The durable gate must remain available to the plan viewer.");
        });
    }

    [Test]
    public void ApplyExecutionStarted_SameRevisionResume_PreservesGateRuntimeState()
    {
        var group = MakeApprovalWindowGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToArray();
        var existing = PlanStoreUpdater.ApplyExecutionStarted(
            null, group, revision, items, "GROUP-001-001");
        existing = existing with
        {
            ApprovalGates =
            [
                existing.ApprovalGates[0] with
                {
                    Status = PlanGateStatus.AwaitingApproval,
                    RequestedAt = new DateTimeOffset(2026, 7, 31, 18, 0, 0, TimeSpan.Zero),
                },
            ],
        };

        var resumed = PlanStoreUpdater.ApplyExecutionStarted(
            existing, group, revision, items, "GROUP-001-002");

        Assert.Multiple(() =>
        {
            Assert.That(resumed.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.AwaitingApproval));
            Assert.That(resumed.ApprovalGates[0].RequestedAt, Is.EqualTo(
                new DateTimeOffset(2026, 7, 31, 18, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public void ApplyExecutionStarted_NewRevision_ReplacesStaleStagedGateDefinition()
    {
        var originalGroup = MakeApprovalWindowGroup();
        var originalRevision = PendingDecomposePlanStore.ComputeRevision(originalGroup);
        var items = originalGroup.Tasks.Select(task => MakeItem(task.Id)).ToArray();
        var existing = PlanStoreUpdater.ApplyExecutionStarted(
            null, originalGroup, originalRevision, items, "GROUP-001-001");
        var revisedGroup = originalGroup with
        {
            ApprovalGates =
            [
                new DecomposedGate(
                    "GROUP-001-G02",
                    "Review later.",
                    ["GROUP-001-002"],
                    ["GROUP-001-004"]),
            ],
        };
        var revisedRevision = PendingDecomposePlanStore.ComputeRevision(revisedGroup);

        var updated = PlanStoreUpdater.ApplyExecutionStarted(
            existing, revisedGroup, revisedRevision, items, "GROUP-001-001");

        Assert.Multiple(() =>
        {
            Assert.That(updated.ApprovalGates, Has.Count.EqualTo(1));
            Assert.That(updated.ApprovalGates[0].GateId, Is.EqualTo("GROUP-001-G02"));
            Assert.That(updated.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.Pending));
            Assert.That(updated.ApprovalGates[0].PlanRevision, Is.EqualTo(revisedRevision));
            Assert.That(PendingDecomposePlanAdapter.RevisionIsValid(updated), Is.True);
        });
    }

    [Test]
    public void ApplyExecutionStarted_SetsCorrectProgress()
    {
        var group = MakeGroup(3);
        var items = new List<TaskItem>
        {
            MakeItem("GROUP-001-001", isChecked: true),
            MakeItem("GROUP-001-002"),
            MakeItem("GROUP-001-003"),
        };

        var plan = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, "GROUP-001-002");

        Assert.That(plan.Progress.CompletedCount,  Is.EqualTo(1));
        Assert.That(plan.Progress.TotalCount,      Is.EqualTo(3));
        Assert.That(plan.Progress.ExecutingTaskId, Is.EqualTo("GROUP-001-002"));
    }

    [Test]
    public void ApplyExecutionStarted_MapsTaskStatuses()
    {
        var group = MakeGroup(3);
        var items = new List<TaskItem>
        {
            MakeItem("GROUP-001-001", isChecked: true),
            MakeItem("GROUP-001-002", isFailed: true),
            MakeItem("GROUP-001-003"),
        };

        var plan = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, null);

        Assert.That(plan.Tasks[0].Status, Is.EqualTo(PlanTaskStatus.Complete));
        Assert.That(plan.Tasks[1].Status, Is.EqualTo(PlanTaskStatus.Failed));
        Assert.That(plan.Tasks[2].Status, Is.EqualTo(PlanTaskStatus.Pending));
    }

    [Test]
    public void ApplyExecutionStarted_UpdatesExistingPlanToExecuting()
    {
        var existing = MakeExecutingPlan(0, 3) with { LifecycleStatus = PlanLifecycleStatus.Blocked };
        var group    = MakeGroup(3);
        var items    = new List<TaskItem>
        {
            MakeItem("GROUP-001-001", isChecked: true),
            MakeItem("GROUP-001-002"),
            MakeItem("GROUP-001-003"),
        };

        var updated = PlanStoreUpdater.ApplyExecutionStarted(existing, group, "rev1", items, "GROUP-001-002");

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(updated.InterruptionData, Is.Null);
    }

    [Test]
    public void ApplyExecutionStarted_PreservesStartedAtOnResume()
    {
        var startTime = DateTimeOffset.UtcNow.AddHours(-1);
        var existing  = MakeExecutingPlan(1, 3) with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            Timestamps = new PlanTimestamps(
                CreatedAt: startTime,
                StartedAt: startTime),
        };
        var group = MakeGroup(3);
        var items = new List<TaskItem>
        {
            MakeItem("GROUP-001-001", isChecked: true),
            MakeItem("GROUP-001-002"),
            MakeItem("GROUP-001-003"),
        };

        var updated = PlanStoreUpdater.ApplyExecutionStarted(existing, group, "rev1", items, "GROUP-001-002");

        Assert.That(updated.Timestamps.StartedAt, Is.EqualTo(startTime),
            "StartedAt must not be reset on resume.");
    }

    [Test]
    public void ApplyExecutionStarted_PreservesAcceptedResultProvenanceOnResume()
    {
        var completedAt = new DateTimeOffset(2026, 7, 29, 15, 21, 42, TimeSpan.Zero);
        var durableTask = new PlanTask(
            TaskId: "GROUP-001-001",
            Title: "Old title",
            Description: "Old description",
            DependsOn: [],
            Priority: "low",
            Status: PlanTaskStatus.Complete,
            Commit: "774a047",
            CompletedAt: completedAt,
            CompletionSummary: "Verified and adopted.");
        var existing = MakeExecutingPlan(1, 1) with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            Tasks = [durableTask],
        };
        var assignment = new DecomposedAgentAssignment("vesper-knox", "test verifier", false);
        var group = new DecomposedTaskGroup(
            "GROUP-001",
            "Test Plan",
            "feature/test",
            "A test plan",
            [new DecomposedSubTask(
                "GROUP-001-001",
                "Current description",
                [],
                "high",
                "Current title",
                AgentAssignments: [assignment],
                ParallelEligible: true,
                AgentRoutingMode: "assigned")]);

        var resumed = PlanStoreUpdater.ApplyExecutionStarted(
            existing,
            group,
            "rev1",
            [MakeItem("GROUP-001-001", isChecked: true)],
            executingTaskId: null);

        Assert.Multiple(() =>
        {
            Assert.That(resumed.Tasks[0].Commit, Is.EqualTo("774a047"));
            Assert.That(resumed.Tasks[0].CompletedAt, Is.EqualTo(completedAt));
            Assert.That(resumed.Tasks[0].CompletionSummary, Is.EqualTo("Verified and adopted."));
            Assert.That(resumed.Tasks[0].Title, Is.EqualTo("Current title"));
            Assert.That(resumed.Tasks[0].AgentAssignments?.Single().AgentHandle,
                Is.EqualTo("vesper-knox"));
            Assert.That(resumed.Tasks[0].AgentRoutingMode, Is.EqualTo("assigned"));
            Assert.That(resumed.Tasks[0].ParallelEligible, Is.True);
        });
    }

    [Test]
    public void ApplyExecutionStarted_ReplacesStaleRoutingMetadataFromRevisedPlan()
    {
        var staleTask = new PlanTask(
            TaskId: "GROUP-001-001",
            Title: "Task",
            Description: "Assigned task",
            DependsOn: [],
            Priority: "high",
            Status: PlanTaskStatus.Pending,
            AgentAssignments: [new PlanAgentAssignment("old-agent", "old role", false)],
            AgentRoutingMode: "assigned");
        var existing = MakeExecutingPlan(0, 1) with { Tasks = [staleTask] };
        var revisedGroup = new DecomposedTaskGroup(
            "GROUP-001",
            "Revised Plan",
            "feature/revised",
            "A revised plan",
            [new DecomposedSubTask(
                "GROUP-001-001",
                "Explicit generic task",
                [],
                "high",
                "Task",
                AgentRoutingMode: "generic",
                GenericAgentReason: "No roster specialist is required.")]);

        var updated = PlanStoreUpdater.ApplyExecutionStarted(
            existing,
            revisedGroup,
            "rev2",
            [MakeItem("GROUP-001-001")],
            "GROUP-001-001");

        Assert.Multiple(() =>
        {
            Assert.That(updated.Revision, Is.EqualTo("rev2"));
            Assert.That(updated.HostRevision, Is.EqualTo("rev2"));
            Assert.That(updated.Title, Is.EqualTo("Revised Plan"));
            Assert.That(updated.Branch, Is.EqualTo("feature/revised"));
            Assert.That(updated.Summary, Is.EqualTo("A revised plan"));
            Assert.That(updated.Tasks[0].AgentAssignments, Is.Null);
            Assert.That(updated.Tasks[0].AgentRoutingMode, Is.EqualTo("generic"));
            Assert.That(updated.Tasks[0].GenericAgentReason,
                Is.EqualTo("No roster specialist is required."));
        });
    }

    // ── ApplyStepAccepted ─────────────────────────────────────────────────────

    [Test]
    public void ApplyStepAccepted_IncrementsCompletedCount()
    {
        var existing = MakeExecutingPlan(1, 3, "GROUP-001-002");
        var items    = new List<TaskItem>
        {
            MakeItem("GROUP-001-001", isChecked: true),
            MakeItem("GROUP-001-002", isChecked: true),
            MakeItem("GROUP-001-003"),
        };

        var updated = PlanStoreUpdater.ApplyStepAccepted(existing, items, "GROUP-001-003");

        Assert.That(updated.Progress.CompletedCount,  Is.EqualTo(2));
        Assert.That(updated.Progress.TotalCount,      Is.EqualTo(3));
        Assert.That(updated.Progress.ExecutingTaskId, Is.EqualTo("GROUP-001-003"));
    }

    [Test]
    public void ApplyStepAccepted_ClearsExecutingTaskIdWhenNull()
    {
        var existing = MakeExecutingPlan(2, 3, "GROUP-001-003");
        var items    = new List<TaskItem>
        {
            MakeItem("GROUP-001-001", isChecked: true),
            MakeItem("GROUP-001-002", isChecked: true),
            MakeItem("GROUP-001-003", isChecked: true),
        };

        var updated = PlanStoreUpdater.ApplyStepAccepted(existing, items, null);

        Assert.That(updated.Progress.CompletedCount,  Is.EqualTo(3));
        Assert.That(updated.Progress.ExecutingTaskId, Is.Null);
    }

    [Test]
    public void ApplyStepAccepted_PreservesLifecycleStatus()
    {
        var existing = MakeExecutingPlan(0, 3, "GROUP-001-001");
        var items    = new List<TaskItem>
        {
            MakeItem("GROUP-001-001", isChecked: true),
            MakeItem("GROUP-001-002"),
            MakeItem("GROUP-001-003"),
        };

        var updated = PlanStoreUpdater.ApplyStepAccepted(existing, items, "GROUP-001-002");

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
    }

    [Test]
    public void ApplyStepAccepted_RecordsAcceptedResultProvenance()
    {
        var group = MakeGroup(1);
        var started = PlanStoreUpdater.ApplyExecutionStarted(
            null,
            group,
            "rev1",
            [MakeItem("GROUP-001-001")],
            "GROUP-001-001");
        var result = new DecomposeStepResult(
            GroupId: "GROUP-001",
            TaskId: "GROUP-001-001",
            Revision: "rev1",
            Status: "complete",
            Commit: "8935e51",
            Summary: "Documented verified routing.",
            RemainingWork: [],
            Verification: new DecomposeStepVerification("passed", "path-check", "Paths verified."));
        var before = DateTimeOffset.UtcNow;

        var accepted = PlanStoreUpdater.ApplyStepAccepted(
            started,
            [MakeItem("GROUP-001-001", isChecked: true)],
            nextExecutingTaskId: null,
            acceptedResult: result);
        var completed = PlanStoreUpdater.ApplyCompleted(accepted);

        Assert.Multiple(() =>
        {
            Assert.That(completed.Tasks[0].Status, Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(completed.Tasks[0].Commit, Is.EqualTo("8935e51"));
            Assert.That(completed.Tasks[0].CompletionSummary,
                Is.EqualTo("Documented verified routing."));
            Assert.That(completed.Tasks[0].CompletedAt, Is.GreaterThanOrEqualTo(before));
            Assert.That(completed.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
        });
    }

    // ── ApplyBlocked ──────────────────────────────────────────────────────────

    [Test]
    public void ApplyBlocked_SetsStatusToBlocked()
    {
        var existing = MakeExecutingPlan(1, 3, "GROUP-001-002");
        var updated  = PlanStoreUpdater.ApplyBlocked(existing, "GROUP-001-002");

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Blocked));
    }

    [Test]
    public void ApplyBlocked_ClearsExecutingTaskId()
    {
        var existing = MakeExecutingPlan(1, 3, "GROUP-001-002");
        var updated  = PlanStoreUpdater.ApplyBlocked(existing, "GROUP-001-002");

        Assert.That(updated.Progress.ExecutingTaskId, Is.Null);
    }

    [Test]
    public void ApplyBlocked_SetsInterruptedAt()
    {
        var before   = DateTimeOffset.UtcNow;
        var existing = MakeExecutingPlan(1, 3, "GROUP-001-002");
        var updated  = PlanStoreUpdater.ApplyBlocked(existing, "GROUP-001-002");

        Assert.That(updated.Timestamps.InterruptedAt, Is.Not.Null);
        Assert.That(updated.Timestamps.InterruptedAt, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void ApplyBlocked_PreservesProgressCounts()
    {
        var existing = MakeExecutingPlan(2, 5, "GROUP-001-003");
        var updated  = PlanStoreUpdater.ApplyBlocked(existing, "GROUP-001-003");

        Assert.That(updated.Progress.CompletedCount, Is.EqualTo(2));
        Assert.That(updated.Progress.TotalCount,     Is.EqualTo(5));
    }

    // ── ApplyCompleted ────────────────────────────────────────────────────────

    [Test]
    public void ApplyCompleted_SetsStatusToCompleted()
    {
        var existing = MakeExecutingPlan(5, 5, null);
        var updated  = PlanStoreUpdater.ApplyCompleted(existing);

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
    }

    [Test]
    public void ApplyCompleted_SetsCompletedAt()
    {
        var before   = DateTimeOffset.UtcNow;
        var existing = MakeExecutingPlan(5, 5, null);
        var updated  = PlanStoreUpdater.ApplyCompleted(existing);

        Assert.That(updated.Timestamps.CompletedAt, Is.Not.Null);
        Assert.That(updated.Timestamps.CompletedAt, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void ApplyCompleted_ClearsExecutingTaskId()
    {
        var existing = MakeExecutingPlan(4, 5, "GROUP-001-005");
        var updated  = PlanStoreUpdater.ApplyCompleted(existing);

        Assert.That(updated.Progress.ExecutingTaskId, Is.Null);
    }

    [Test]
    public void ApplyCompleted_PreservesProgressCounts()
    {
        var existing = MakeExecutingPlan(5, 5, null);
        var updated  = PlanStoreUpdater.ApplyCompleted(existing);

        Assert.That(updated.Progress.CompletedCount, Is.EqualTo(5));
        Assert.That(updated.Progress.TotalCount,     Is.EqualTo(5));
    }

    // ── BuildProgress ─────────────────────────────────────────────────────────

    [Test]
    public void BuildProgress_CountsCheckedAndSuperseded()
    {
        var items = new List<TaskItem>
        {
            MakeItem("T1", isChecked: true),
            MakeItem("T2", isSuperseded: true),
            MakeItem("T3"),
            MakeItem("T4", isFailed: true),
        };

        var progress = PlanStoreUpdater.BuildProgress(items, "T3");

        Assert.That(progress.CompletedCount,  Is.EqualTo(2), "Only checked + superseded count as complete.");
        Assert.That(progress.TotalCount,      Is.EqualTo(4));
        Assert.That(progress.ExecutingTaskId, Is.EqualTo("T3"));
    }

    [Test]
    public void BuildProgress_ReturnsZeroForEmptyItems()
    {
        var progress = PlanStoreUpdater.BuildProgress(new List<TaskItem>(), "T1");

        Assert.That(progress.CompletedCount, Is.EqualTo(0));
        Assert.That(progress.TotalCount,     Is.EqualTo(0));
    }

    // ── Persisted vs live state consistency ───────────────────────────────────

    [Test]
    public void PlanLifecycle_StartedToCompletedTransitionIsConsistent()
    {
        var group      = MakeGroup(2);
        var allPending = new List<TaskItem>
        {
            MakeItem("GROUP-001-001"),
            MakeItem("GROUP-001-002"),
        };

        // 1. Start
        var started = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", allPending, "GROUP-001-001");
        Assert.That(started.LifecycleStatus,        Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(started.Progress.CompletedCount, Is.EqualTo(0));

        // 2. Step 1 accepted
        var afterStep1Items = new List<TaskItem>
        {
            MakeItem("GROUP-001-001", isChecked: true),
            MakeItem("GROUP-001-002"),
        };
        var afterStep1 = PlanStoreUpdater.ApplyStepAccepted(started, afterStep1Items, "GROUP-001-002");
        Assert.That(afterStep1.LifecycleStatus,          Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(afterStep1.Progress.CompletedCount,  Is.EqualTo(1));
        Assert.That(afterStep1.Progress.ExecutingTaskId, Is.EqualTo("GROUP-001-002"));

        // 3. Plan completes
        var completed = PlanStoreUpdater.ApplyCompleted(afterStep1);
        Assert.That(completed.LifecycleStatus,          Is.EqualTo(PlanLifecycleStatus.Completed));
        Assert.That(completed.Progress.ExecutingTaskId, Is.Null);
        Assert.That(completed.Timestamps.CompletedAt,   Is.Not.Null);
    }

    [Test]
    public void PlanLifecycle_StartedToBlockedTransitionIsConsistent()
    {
        var group = MakeGroup(3);
        var items = new List<TaskItem>
        {
            MakeItem("GROUP-001-001"),
            MakeItem("GROUP-001-002"),
            MakeItem("GROUP-001-003"),
        };

        var started = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, "GROUP-001-001");
        var blocked  = PlanStoreUpdater.ApplyBlocked(started, "GROUP-001-001");

        Assert.That(blocked.LifecycleStatus,          Is.EqualTo(PlanLifecycleStatus.Blocked));
        Assert.That(blocked.Progress.ExecutingTaskId, Is.Null,
            "Blocked plan must not show a current step.");
        Assert.That(PlanLifecycleStatus.IsTerminal(PlanLifecycleStatus.Blocked), Is.False,
            "Blocked is not terminal — plan can be recovered.");
    }

    [Test]
    public void PlanLifecycle_ResumedPlanPreservesStartedAt()
    {
        var originalStart = DateTimeOffset.UtcNow.AddDays(-1);
        var existing = new Plan(
            PlanId:          "GROUP-001",
            Revision:        "rev1",
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Interrupted,
            Title:           "Test Plan",
            Branch:          "feature/test",
            Summary:         "A test plan",
            Tasks:           [],
            ApprovalGates:   [],
            Progress:        new PlanProgress(2, 5, null),
            Timestamps:      new PlanTimestamps(
                CreatedAt: originalStart,
                StartedAt: originalStart));

        var group = MakeGroup(5);
        var items = Enumerable.Range(1, 5)
            .Select(i => MakeItem($"GROUP-001-00{i}", isChecked: i <= 2))
            .ToList<TaskItem>();

        var resumed = PlanStoreUpdater.ApplyExecutionStarted(existing, group, "rev1", items, "GROUP-001-003");

        Assert.That(resumed.Timestamps.StartedAt, Is.EqualTo(originalStart),
            "Resuming must preserve the original StartedAt.");
        Assert.That(resumed.LifecycleStatus,        Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(resumed.Progress.CompletedCount, Is.EqualTo(2));
    }

    [Test]
    public void ApplyGateReworkRequested_ReopensOnlyReviewedTaskAndArchivesAcceptedAttempt()
    {
        var group = MakeApprovalWindowGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToArray();
        var plan = PlanStoreUpdater.ApplyExecutionStarted(null, group, revision, items, "GROUP-001-001");
        plan = plan with
        {
            Tasks = plan.Tasks.Select(task => task.TaskId == "GROUP-001-001"
                ? task with
                {
                    Status = PlanTaskStatus.Complete,
                    Commit = "abc1234",
                    CompletedAt = DateTimeOffset.UtcNow,
                    CompletionSummary = "Original result",
                }
                : task).ToArray(),
            ApprovalGates =
            [
                plan.ApprovalGates[0] with { Status = PlanGateStatus.AwaitingApproval },
            ],
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            Progress = new PlanProgress(1, 4),
        };

        var updated = PlanStoreUpdater.ApplyGateReworkRequested(
            plan, "GROUP-001-G01", ["GROUP-001-001"], "Add the missing restart test.");
        var task = updated.Tasks.Single(candidate => candidate.TaskId == "GROUP-001-001");

        Assert.Multiple(() =>
        {
            Assert.That(task.Status, Is.EqualTo(PlanTaskStatus.Pending));
            Assert.That(task.Commit, Is.Null);
            Assert.That(task.AttemptHistory, Has.Count.EqualTo(1));
            Assert.That(task.AttemptHistory![0].Commit, Is.EqualTo("abc1234"));
            Assert.That(task.AttemptHistory[0].Disposition, Is.EqualTo("changes-requested"));
            Assert.That(updated.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.Pending));
            Assert.That(updated.ApprovalGates[0].ReworkCount, Is.EqualTo(1));
            Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(updated.Progress.CompletedCount, Is.Zero);
        });
    }

    [Test]
    public void ApplyGateReworkRequested_DoesNotReopenApprovedBoundary()
    {
        var group = MakeApprovalWindowGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        var items = group.Tasks.Select(task => MakeItem(task.Id)).ToArray();
        var plan = PlanStoreUpdater.ApplyExecutionStarted(null, group, revision, items, "GROUP-001-001");
        plan = plan with
        {
            Tasks = plan.Tasks.Select(task => task.TaskId == "GROUP-001-001"
                ? task with { Status = PlanTaskStatus.Complete, Commit = "abc1234" }
                : task).ToArray(),
            ApprovalGates =
            [
                plan.ApprovalGates[0] with { Status = PlanGateStatus.Approved },
            ],
        };

        var updated = PlanStoreUpdater.ApplyGateReworkRequested(
            plan, "GROUP-001-G01", ["GROUP-001-001"], "Try again.");

        Assert.That(updated, Is.SameAs(plan));
        Assert.That(updated.Tasks.Single(task => task.TaskId == "GROUP-001-001").Status,
            Is.EqualTo(PlanTaskStatus.Complete));
    }
}
