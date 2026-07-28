using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash.Tests;

/// <summary>
/// Regression tests for PLANUX-20260728-001: atomic plan completion state and
/// load-time repair of impossible state combinations.
/// </summary>
[TestFixture]
internal sealed class PlanCompletionAtomicityTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static PlanTask MakePlanTask(string taskId, string status) =>
        new(TaskId:      taskId,
            Title:       $"Task {taskId}",
            Description: $"Description for {taskId}",
            DependsOn:   [],
            Priority:    "mid",
            Status:      status);

    private static Plan MakePlan(
        string                  lifecycle,
        IReadOnlyList<PlanTask> tasks,
        int?                    completedCountOverride = null,
        string?                 executingTaskId        = null)
    {
        var completedCount = completedCountOverride
            ?? tasks.Count(t => t.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded);
        return new Plan(
            PlanId:          "PLANS-20260727",
            Revision:        "rev1",
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: lifecycle,
            Title:           "Test Plan",
            Branch:          "feature/test",
            Summary:         "Atomicity regression tests",
            Tasks:           tasks,
            ApprovalGates:   [],
            Progress:        new PlanProgress(completedCount, tasks.Count, executingTaskId),
            Timestamps:      new PlanTimestamps(DateTimeOffset.UtcNow, StartedAt: DateTimeOffset.UtcNow));
    }

    private static TaskItem MakeTaskItem(string taskId, bool isChecked = false, bool isFailed = false,
        bool isPartial = false, bool isSuperseded = false) =>
        new(Text:             taskId,
            Owner:            null,
            IsUserOwned:      false,
            IsChecked:        isChecked,
            Emoji:            "🟡",
            RawLine:          $"- [{(isChecked ? "x" : " ")}] **[{taskId}]** description",
            DecomposeGroupId: "PLANS-20260727",
            TaskId:           taskId,
            IsFailed:         isFailed,
            IsPartial:        isPartial,
            IsSuperseded:     isSuperseded);

    // ── RepairInconsistentState ───────────────────────────────────────────────

    [Test]
    public void RepairInconsistentState_ConsistentPlan_ReturnsUnchanged()
    {
        // Executing plan: task-1 done, task-2 pending, progress count correct, no stale cursor.
        var tasks = new[]
        {
            MakePlanTask("PLANS-20260727-001", PlanTaskStatus.Complete),
            MakePlanTask("PLANS-20260727-002", PlanTaskStatus.Pending),
        };
        var plan = MakePlan(PlanLifecycleStatus.Executing, tasks, completedCountOverride: 1, executingTaskId: null);

        var repaired = PlanStoreUpdater.RepairInconsistentState(plan);

        Assert.That(ReferenceEquals(repaired, plan), Is.True, "Consistent plan must be returned unchanged.");
    }

    [Test]
    public void RepairInconsistentState_CompletedWithPendingTask_RepairsPendingToComplete()
    {
        // Case A: PlanStore says Completed but one task is still Pending (write was interrupted).
        var tasks = new[]
        {
            MakePlanTask("PLANS-20260727-001", PlanTaskStatus.Complete),
            MakePlanTask("PLANS-20260727-002", PlanTaskStatus.Pending),
        };
        var plan = MakePlan(PlanLifecycleStatus.Completed, tasks);

        var repaired = PlanStoreUpdater.RepairInconsistentState(plan);

        Assert.That(repaired.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
        Assert.That(repaired.Tasks.Any(t => t.Status == PlanTaskStatus.Pending), Is.False,
            "No tasks should remain Pending after repair.");
        Assert.That(repaired.Tasks.All(t => t.Status == PlanTaskStatus.Complete), Is.True);
    }

    [Test]
    public void RepairInconsistentState_CompletedWithExecutingTask_RepairsExecutingCursor()
    {
        // Case A: Completed but one task is still marked Executing and ExecutingTaskId is set.
        var tasks = new[]
        {
            MakePlanTask("PLANS-20260727-001", PlanTaskStatus.Complete),
            MakePlanTask("PLANS-20260727-002", PlanTaskStatus.Executing),
        };
        var plan = MakePlan(PlanLifecycleStatus.Completed, tasks, executingTaskId: "PLANS-20260727-002");

        var repaired = PlanStoreUpdater.RepairInconsistentState(plan);

        Assert.That(repaired.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
        Assert.That(repaired.Tasks.Any(t => t.Status == PlanTaskStatus.Executing), Is.False,
            "No tasks should remain Executing after repair.");
        Assert.That(repaired.Progress.ExecutingTaskId, Is.Null,
            "ExecutingTaskId must be cleared after repair.");
    }

    [Test]
    public void RepairInconsistentState_ExecutingAllTasksComplete_TransitionsToCompleted()
    {
        // Case B: tasks.md was updated for the final step but PlanStore write was interrupted.
        var tasks = new[]
        {
            MakePlanTask("PLANS-20260727-001", PlanTaskStatus.Complete),
            MakePlanTask("PLANS-20260727-002", PlanTaskStatus.Complete),
            MakePlanTask("PLANS-20260727-003", PlanTaskStatus.Complete),
        };
        var plan = MakePlan(PlanLifecycleStatus.Executing, tasks);

        var repaired = PlanStoreUpdater.RepairInconsistentState(plan);

        Assert.That(repaired.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
        Assert.That(repaired.Timestamps.CompletedAt, Is.Not.Null,
            "CompletedAt must be set after repair.");
        Assert.That(repaired.Progress.ExecutingTaskId, Is.Null);
    }

    [Test]
    public void RepairInconsistentState_ExecutingWithFailedTask_TransitionsToBlocked()
    {
        // Case B: all tasks terminal but one is Failed — plan should be Blocked, not Completed.
        var tasks = new[]
        {
            MakePlanTask("PLANS-20260727-001", PlanTaskStatus.Complete),
            MakePlanTask("PLANS-20260727-002", PlanTaskStatus.Complete),
            MakePlanTask("PLANS-20260727-003", PlanTaskStatus.Failed),
        };
        var plan = MakePlan(PlanLifecycleStatus.Executing, tasks);

        var repaired = PlanStoreUpdater.RepairInconsistentState(plan);

        Assert.That(repaired.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Blocked));
        Assert.That(repaired.Progress.ExecutingTaskId, Is.Null);
    }

    [Test]
    public void RepairInconsistentState_ProgressMismatch_RecomputesCount()
    {
        // Case C: stored CompletedCount is stale (1) but two tasks are actually Complete.
        var tasks = new[]
        {
            MakePlanTask("PLANS-20260727-001", PlanTaskStatus.Complete),
            MakePlanTask("PLANS-20260727-002", PlanTaskStatus.Complete),
            MakePlanTask("PLANS-20260727-003", PlanTaskStatus.Pending),
        };
        var plan = MakePlan(PlanLifecycleStatus.Executing, tasks, completedCountOverride: 1);

        var repaired = PlanStoreUpdater.RepairInconsistentState(plan);

        Assert.That(repaired.Progress.CompletedCount, Is.EqualTo(2),
            "CompletedCount must be recomputed from actual task statuses.");
        Assert.That(repaired.Progress.TotalCount, Is.EqualTo(3));
    }

    [Test]
    public void RepairInconsistentState_StaleExecutingTaskId_ClearsPointer()
    {
        // Case D: ExecutingTaskId still points to task-2 which is already Complete.
        var tasks = new[]
        {
            MakePlanTask("PLANS-20260727-001", PlanTaskStatus.Complete),
            MakePlanTask("PLANS-20260727-002", PlanTaskStatus.Complete),
            MakePlanTask("PLANS-20260727-003", PlanTaskStatus.Pending),
        };
        // Stale cursor: task-2 completed but cursor still points at it.
        var plan = MakePlan(PlanLifecycleStatus.Executing, tasks, executingTaskId: "PLANS-20260727-002");

        var repaired = PlanStoreUpdater.RepairInconsistentState(plan);

        Assert.That(repaired.Progress.ExecutingTaskId, Is.Null,
            "Stale ExecutingTaskId pointing to a Complete task must be cleared.");
    }

    [Test]
    public void RepairInconsistentState_InterruptedPlan_NotRepaired()
    {
        // Interrupted plans have their own recovery flow — must not be touched.
        var tasks = new[]
        {
            MakePlanTask("PLANS-20260727-001", PlanTaskStatus.Complete),
            MakePlanTask("PLANS-20260727-002", PlanTaskStatus.Pending),
        };
        var plan = MakePlan(PlanLifecycleStatus.Interrupted, tasks);

        var repaired = PlanStoreUpdater.RepairInconsistentState(plan);

        Assert.That(ReferenceEquals(repaired, plan), Is.True,
            "Interrupted plans must never be repaired by RepairInconsistentState.");
    }

    [Test]
    public void RepairInconsistentState_CompletedPlan_AllTasksComplete_NothingToRepair()
    {
        // A fully consistent Completed plan must be returned unchanged.
        var tasks = new[]
        {
            MakePlanTask("PLANS-20260727-001", PlanTaskStatus.Complete),
            MakePlanTask("PLANS-20260727-002", PlanTaskStatus.Complete),
        };
        var plan = MakePlan(PlanLifecycleStatus.Completed, tasks);

        var repaired = PlanStoreUpdater.RepairInconsistentState(plan);

        Assert.That(ReferenceEquals(repaired, plan), Is.True,
            "Consistent Completed plan must be returned unchanged.");
    }

    [Test]
    public void RepairInconsistentState_BlockedPlan_NotForced()
    {
        // Blocked plans with pending tasks (tasks that never ran) must not be touched.
        var tasks = new[]
        {
            MakePlanTask("PLANS-20260727-001", PlanTaskStatus.Complete),
            MakePlanTask("PLANS-20260727-002", PlanTaskStatus.Failed),
            MakePlanTask("PLANS-20260727-003", PlanTaskStatus.Pending),
        };
        var plan = MakePlan(PlanLifecycleStatus.Blocked, tasks);

        var repaired = PlanStoreUpdater.RepairInconsistentState(plan);

        Assert.That(ReferenceEquals(repaired, plan), Is.True,
            "Blocked plans must never be repaired by RepairInconsistentState.");
    }

    // ── Combined atomicity tests ──────────────────────────────────────────────

    [Test]
    public void ApplyStepAccepted_ThenApplyCompleted_SetsAllFieldsConsistently()
    {
        // Simulates the new atomic completion path: both task statuses and Completed lifecycle
        // are set in a single PublishPlanProgress call.
        var group = new DecomposedTaskGroup(
            GroupId:    "PLANS-20260727",
            GroupTitle: "Regression test plan",
            Branch:     "feature/test",
            Summary:    "Atomicity test",
            Tasks: new[]
            {
                new DecomposedSubTask("PLANS-20260727-001", "Task 1", [], "mid", "Task 1"),
                new DecomposedSubTask("PLANS-20260727-002", "Task 2", ["PLANS-20260727-001"], "mid", "Task 2"),
            });

        var startItems = new[]
        {
            MakeTaskItem("PLANS-20260727-001"),
            MakeTaskItem("PLANS-20260727-002"),
        };
        var executing = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", startItems, "PLANS-20260727-001");

        // Final step: both tasks now complete in tasks.md.
        var finalItems = new[]
        {
            MakeTaskItem("PLANS-20260727-001", isChecked: true),
            MakeTaskItem("PLANS-20260727-002", isChecked: true),
        };
        var withTasks = PlanStoreUpdater.ApplyStepAccepted(executing, finalItems, nextExecutingTaskId: null);
        var completed = PlanStoreUpdater.ApplyCompleted(withTasks);

        Assert.That(completed.LifecycleStatus,           Is.EqualTo(PlanLifecycleStatus.Completed));
        Assert.That(completed.Timestamps.CompletedAt,    Is.Not.Null);
        Assert.That(completed.Progress.ExecutingTaskId,  Is.Null);
        Assert.That(completed.Tasks[0].Status,           Is.EqualTo(PlanTaskStatus.Complete));
        Assert.That(completed.Tasks[1].Status,           Is.EqualTo(PlanTaskStatus.Complete));
        Assert.That(completed.Progress.CompletedCount,   Is.EqualTo(2));
    }

    [Test]
    public void ApplyCompleted_AlreadyCompleted_LifecycleRemainsCompletedAndTimestampUpdated()
    {
        // Calling ApplyCompleted on an already-Completed plan must keep LifecycleStatus = Completed
        // and set a non-null CompletedAt (implementation always writes UtcNow).
        var tasks = new[]
        {
            MakePlanTask("PLANS-20260727-001", PlanTaskStatus.Complete),
        };
        var plan = MakePlan(PlanLifecycleStatus.Completed, tasks);
        var originalCompletedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        plan = plan with
        {
            Timestamps = plan.Timestamps with { CompletedAt = originalCompletedAt },
        };

        var reapplied = PlanStoreUpdater.ApplyCompleted(plan);

        Assert.That(reapplied.LifecycleStatus,        Is.EqualTo(PlanLifecycleStatus.Completed),
            "LifecycleStatus must remain Completed.");
        Assert.That(reapplied.Timestamps.CompletedAt, Is.Not.Null,
            "CompletedAt must not be null after re-applying ApplyCompleted.");
        Assert.That(reapplied.Progress.ExecutingTaskId, Is.Null,
            "ExecutingTaskId must remain null.");
    }
}
