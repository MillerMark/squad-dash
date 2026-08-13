using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash.Tests;

/// <summary>
/// Combined lifecycle-transition sequence tests for <see cref="PlanStoreUpdater"/>.
/// Individual operation tests live in PlanStoreUpdaterTests, PlanStoreUpdaterGateTests,
/// and PlanStoreUpdaterInterruptedTests; this fixture focuses on multi-step contracts.
/// </summary>
[TestFixture]
internal sealed class PlanLifecycleContractTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static DecomposedTaskGroup MakeGroup(int taskCount = 3)
    {
        var tasks = Enumerable.Range(1, taskCount)
            .Select(i => new DecomposedSubTask(
                Id:          $"CTR-001-00{i}",
                Description: $"Task {i}",
                DependsOn:   i == 1 ? [] : [$"CTR-001-00{i - 1}"],
                Priority:    "mid",
                Title:       $"Task {i}"))
            .ToList();

        return new DecomposedTaskGroup(
            GroupId:    "CTR-001",
            GroupTitle: "Contract Test Plan",
            Branch:     "feature/contract",
            Summary:    "lifecycle contract tests",
            Tasks:      tasks);
    }

    private static TaskItem MakeItem(string taskId, bool isChecked = false, bool isFailed = false,
        bool isPartial = false, bool isSuperseded = false) =>
        new(Text:             taskId,
            Owner:            null,
            IsUserOwned:      false,
            IsChecked:        isChecked,
            Emoji:            "🟡",
            RawLine:          $"- [{(isChecked ? "x" : " ")}] **[{taskId}]** description",
            DecomposeGroupId: "CTR-001",
            TaskId:           taskId,
            IsFailed:         isFailed,
            IsPartial:        isPartial,
            IsSuperseded:     isSuperseded);

    private static Plan MakeExecutingPlanWithGate()
    {
        var tasks = new[]
        {
            new PlanTask("CTR-001-001", "Task 1", "desc", [], "mid", PlanTaskStatus.Complete),
            new PlanTask("CTR-001-002", "Task 2", "desc", ["CTR-001-001"], "mid", PlanTaskStatus.Pending),
        };
        var gate = new PlanApprovalGate(
            GateId:        "CTR-001-GATE-001",
            Message:       "Review before continuing",
            AfterTaskIds:  ["CTR-001-001"],
            BeforeTaskIds: ["CTR-001-002"],
            Status:        PlanGateStatus.Pending);

        return new Plan(
            PlanId:          "CTR-001",
            Revision:        "rev1",
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Executing,
            Title:           "Contract Test Plan",
            Branch:          "feature/contract",
            Summary:         "lifecycle contract tests",
            Tasks:           tasks,
            ApprovalGates:   [gate],
            Progress:        new PlanProgress(1, 2, "CTR-001-001"),
            Timestamps:      new PlanTimestamps(DateTimeOffset.UtcNow, StartedAt: DateTimeOffset.UtcNow));
    }

    // ── 1. Happy path: start → accept steps → complete ────────────────────────

    [Test]
    public void ExecutionStarted_to_StepAccepted_to_Completed_happyPath()
    {
        var group     = MakeGroup(2);
        var items0    = new List<TaskItem> { MakeItem("CTR-001-001"), MakeItem("CTR-001-002") };
        var started   = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items0, "CTR-001-001");

        Assert.That(started.LifecycleStatus,         Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(started.Progress.CompletedCount, Is.EqualTo(0));

        // Accept first step
        var afterStep1Items = new List<TaskItem>
        {
            MakeItem("CTR-001-001", isChecked: true),
            MakeItem("CTR-001-002"),
        };
        var afterStep1 = PlanStoreUpdater.ApplyStepAccepted(started, afterStep1Items, "CTR-001-002");

        Assert.That(afterStep1.LifecycleStatus,          Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(afterStep1.Tasks[0].Status,          Is.EqualTo(PlanTaskStatus.Complete));
        Assert.That(afterStep1.Progress.CompletedCount,  Is.EqualTo(1));
        Assert.That(afterStep1.Progress.ExecutingTaskId, Is.EqualTo("CTR-001-002"));

        // Accept second (final) step
        var afterStep2Items = new List<TaskItem>
        {
            MakeItem("CTR-001-001", isChecked: true),
            MakeItem("CTR-001-002", isChecked: true),
        };
        var afterStep2 = PlanStoreUpdater.ApplyStepAccepted(afterStep1, afterStep2Items, null);

        Assert.That(afterStep2.LifecycleStatus,         Is.EqualTo(PlanLifecycleStatus.Executing),
            "Completing the final step via ApplyStepAccepted alone must NOT auto-complete the plan.");
        Assert.That(afterStep2.Tasks[1].Status,         Is.EqualTo(PlanTaskStatus.Complete));
        Assert.That(afterStep2.Progress.CompletedCount, Is.EqualTo(2));

        // Explicit ApplyCompleted
        var before    = DateTimeOffset.UtcNow;
        var completed = PlanStoreUpdater.ApplyCompleted(afterStep2);

        Assert.That(completed.LifecycleStatus,          Is.EqualTo(PlanLifecycleStatus.Completed));
        Assert.That(completed.Progress.ExecutingTaskId, Is.Null);
        Assert.That(completed.Timestamps.CompletedAt,   Is.Not.Null);
        Assert.That(completed.Timestamps.CompletedAt,   Is.GreaterThanOrEqualTo(before));
    }

    // ── 2. Gate pause/resume sequence ─────────────────────────────────────────

    [Test]
    public void Executing_to_AwaitingApproval_preservesActiveTasks()
    {
        var plan    = MakeExecutingPlanWithGate();
        var updated = PlanStoreUpdater.ApplyGateActivated(plan, "CTR-001-GATE-001");

        Assert.That(updated.LifecycleStatus,                  Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));
        Assert.That(updated.ApprovalGates[0].Status,          Is.EqualTo(PlanGateStatus.AwaitingApproval));
        Assert.That(updated.Progress.ExecutingTaskId,         Is.Null, "Executing task must be cleared at gate.");
        Assert.That(updated.Tasks, Has.Count.EqualTo(plan.Tasks.Count), "Tasks must be preserved.");
        Assert.That(updated.Tasks[0].Status,                  Is.EqualTo(PlanTaskStatus.Complete));
        Assert.That(updated.Tasks[1].Status,                  Is.EqualTo(PlanTaskStatus.Pending));
    }

    [Test]
    public void AwaitingApproval_to_Approved_viaGateApproval()
    {
        var plan      = MakeExecutingPlanWithGate();
        var paused    = PlanStoreUpdater.ApplyGateActivated(plan, "CTR-001-GATE-001");

        Assert.That(paused.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));

        var resumed = PlanStoreUpdater.ApplyGateApproved(paused, "CTR-001-GATE-001", note: "LGTM");

        Assert.That(resumed.LifecycleStatus,               Is.EqualTo(PlanLifecycleStatus.Approved));
        Assert.That(resumed.ApprovalGates[0].Status,       Is.EqualTo(PlanGateStatus.Approved));
        Assert.That(resumed.ApprovalGates[0].ResolutionNote, Is.EqualTo("LGTM"));
    }

    [Test]
    public void GateApproval_whenGateNotInAwaitingApproval_isIgnored()
    {
        // Gate starts Pending, not AwaitingApproval — approve must be a no-op
        var plan    = MakeExecutingPlanWithGate();
        var updated = PlanStoreUpdater.ApplyGateApproved(plan, "CTR-001-GATE-001", note: null);

        Assert.That(ReferenceEquals(updated, plan), Is.True,
            "ApplyGateApproved must return the plan unchanged when gate is not AwaitingApproval.");
    }

    // ── 3. Interrupted → Stopped ──────────────────────────────────────────────

    [Test]
    public void Executing_to_Interrupted_to_Stopped_setsRecoveryStateEnded()
    {
        var group    = MakeGroup(1);
        var items    = new List<TaskItem> { MakeItem("CTR-001-001") };
        var started  = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, "CTR-001-001");

        var interrupted = PlanStoreUpdater.ApplyInterrupted(
            started, reason: "process restart", loopIteration: 1, interruptedTaskId: "CTR-001-001");

        Assert.That(interrupted.LifecycleStatus,                Is.EqualTo(PlanLifecycleStatus.Interrupted));
        Assert.That(interrupted.InterruptionData,               Is.Not.Null);
        Assert.That(interrupted.InterruptionData!.RecoveryState, Is.EqualTo(PlanRecoveryState.PendingRecovery));

        var stopped = PlanStoreUpdater.ApplyStopped(interrupted);

        Assert.That(stopped.LifecycleStatus,                    Is.EqualTo(PlanLifecycleStatus.Stopped));
        Assert.That(stopped.InterruptionData!.RecoveryState,    Is.EqualTo(PlanRecoveryState.Ended),
            "Stopping a plan must seal RecoveryState to Ended.");
        Assert.That(stopped.Timestamps.StoppedAt,               Is.Not.Null);
        Assert.That(stopped.Progress.ExecutingTaskId,           Is.Null);
    }

    // ── 4. Interrupted → resume via ApplyExecutionStarted ────────────────────

    [Test]
    public void Interrupted_to_Executing_viaResume_preservesStartedAt()
    {
        var originalStart = DateTimeOffset.UtcNow.AddHours(-2);
        var interrupted   = new Plan(
            PlanId:          "CTR-001",
            Revision:        "rev1",
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Interrupted,
            Title:           "Contract Test Plan",
            Branch:          "feature/contract",
            Summary:         "lifecycle contract tests",
            Tasks:           [],
            ApprovalGates:   [],
            Progress:        new PlanProgress(1, 2, null),
            Timestamps:      new PlanTimestamps(
                CreatedAt: originalStart,
                StartedAt: originalStart),
            InterruptionData: new PlanInterruptionData("crash", PlanRecoveryState.PendingRecovery, 1));

        var group  = MakeGroup(2);
        var items  = new List<TaskItem>
        {
            MakeItem("CTR-001-001", isChecked: true),
            MakeItem("CTR-001-002"),
        };
        var resumed = PlanStoreUpdater.ApplyExecutionStarted(interrupted, group, "rev2", items, "CTR-001-002");

        Assert.That(resumed.LifecycleStatus,         Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(resumed.Timestamps.StartedAt,    Is.EqualTo(originalStart),
            "Resume must not reset StartedAt.");
        Assert.That(resumed.InterruptionData,        Is.Null, "Resume must clear InterruptionData.");
        Assert.That(resumed.Progress.CompletedCount, Is.EqualTo(1));
    }

    // ── 5. Final step accepted does NOT auto-complete ─────────────────────────

    [Test]
    public void AllTasksAccepted_doesNotAutoComplete_requiresExplicitApplyCompleted()
    {
        var group = MakeGroup(2);
        var items = new List<TaskItem> { MakeItem("CTR-001-001"), MakeItem("CTR-001-002") };
        var plan  = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, "CTR-001-001");

        var allDoneItems = new List<TaskItem>
        {
            MakeItem("CTR-001-001", isChecked: true),
            MakeItem("CTR-001-002", isChecked: true),
        };
        var afterFinalStep = PlanStoreUpdater.ApplyStepAccepted(plan, allDoneItems, null);

        Assert.That(afterFinalStep.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing),
            "Accepting the last task must not implicitly set status to Completed.");
        Assert.That(afterFinalStep.Timestamps.CompletedAt, Is.Null,
            "CompletedAt must remain null until ApplyCompleted is called explicitly.");
    }

    // ── 6. ApplyCompleted sets CompletedAt ───────────────────────────────────

    [Test]
    public void ApplyCompleted_whenAllTasksDone_setsTimestampAndStatus()
    {
        var group    = MakeGroup(1);
        var items    = new List<TaskItem> { MakeItem("CTR-001-001", isChecked: true) };
        var plan     = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, null);
        var before   = DateTimeOffset.UtcNow;
        var finished = PlanStoreUpdater.ApplyCompleted(plan);

        Assert.That(finished.LifecycleStatus,    Is.EqualTo(PlanLifecycleStatus.Completed));
        Assert.That(finished.Timestamps.CompletedAt, Is.Not.Null);
        Assert.That(finished.Timestamps.CompletedAt, Is.GreaterThanOrEqualTo(before));
    }

    // ── 7. Task status Partial is mapped correctly ────────────────────────────

    [Test]
    public void ApplyStepAccepted_mapsPartialTaskStatus()
    {
        var group = MakeGroup(2);
        var items = new List<TaskItem> { MakeItem("CTR-001-001"), MakeItem("CTR-001-002") };
        var plan  = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, "CTR-001-001");

        var partialItems = new List<TaskItem>
        {
            MakeItem("CTR-001-001", isPartial: true),
            MakeItem("CTR-001-002"),
        };
        var updated = PlanStoreUpdater.ApplyStepAccepted(plan, partialItems, "CTR-001-001");

        Assert.That(updated.Tasks[0].Status,    Is.EqualTo(PlanTaskStatus.Partial));
        Assert.That(updated.LifecycleStatus,    Is.EqualTo(PlanLifecycleStatus.Executing),
            "A partial task alone does not block the plan.");
    }

    // ── 8. ApplyBlocked transitions plan status ───────────────────────────────

    [Test]
    public void ApplyBlocked_transitionsPlanToBlocked_andClearsExecutingTask()
    {
        var group   = MakeGroup(2);
        var items   = new List<TaskItem> { MakeItem("CTR-001-001"), MakeItem("CTR-001-002") };
        var plan    = PlanStoreUpdater.ApplyExecutionStarted(null, group, "rev1", items, "CTR-001-001");
        var blocked = PlanStoreUpdater.ApplyBlocked(plan, "CTR-001-001");

        Assert.That(blocked.LifecycleStatus,          Is.EqualTo(PlanLifecycleStatus.Blocked));
        Assert.That(blocked.Progress.ExecutingTaskId, Is.Null);
        Assert.That(blocked.Timestamps.InterruptedAt, Is.Not.Null);
    }

    // ── 9 & 10. Terminal status enforcement ───────────────────────────────────

    [Test]
    public void StoppedPlan_isTerminal()
    {
        Assert.That(PlanLifecycleStatus.IsTerminal(PlanLifecycleStatus.Stopped), Is.True);
    }

    [Test]
    public void CompletedPlan_isTerminal()
    {
        Assert.That(PlanLifecycleStatus.IsTerminal(PlanLifecycleStatus.Completed), Is.True);
    }
}
