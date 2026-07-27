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
        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(plan.Title,           Is.EqualTo("Test Plan"));
        Assert.That(plan.Branch,          Is.EqualTo("feature/test"));
        Assert.That(plan.Source,          Is.EqualTo(PlanSource.DecomposeDecision));
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
        var progress = PlanStoreUpdater.BuildProgress([], "T1");

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
}
