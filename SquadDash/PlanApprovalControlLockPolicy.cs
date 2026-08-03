using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Pure-logic policy that determines whether approval controls in the Plan Viewer
/// should be locked (read-only) based on plan execution progress.
/// Once a task is completed or a gate has been traversed, its controls become immutable.
/// </summary>
internal static class PlanApprovalControlLockPolicy
{
    /// <summary>Task statuses that represent completed work — the task has already executed.</summary>
    private static bool IsTaskCompleted(string? status) =>
        status is PlanTaskStatus.Complete or PlanTaskStatus.Partial
            or PlanTaskStatus.Failed or PlanTaskStatus.Superseded;

    /// <summary>Task statuses that represent started or completed work.</summary>
    private static bool IsTaskStartedOrCompleted(string? status) =>
        status is PlanTaskStatus.Executing or PlanTaskStatus.Complete
            or PlanTaskStatus.Partial or PlanTaskStatus.Failed
            or PlanTaskStatus.Superseded;

    /// <summary>Gate statuses that represent a traversed (already resolved) gate.</summary>
    private static bool IsGateTraversed(string? status) =>
        status is PlanGateStatus.Approved or PlanGateStatus.Skipped;

    /// <summary>
    /// Determines whether a task-entry approval control (gate before this task) is execution-locked.
    /// Locked when the task has already started or completed — cannot retroactively add a gate.
    /// </summary>
    internal static bool IsTaskEntryLocked(Plan plan, string taskId)
    {
        var task = plan.Tasks.FirstOrDefault(t =>
            string.Equals(t.TaskId, taskId, StringComparison.Ordinal));
        return task is not null && IsTaskStartedOrCompleted(task.Status);
    }

    /// <summary>
    /// Determines whether a task-exit approval control (gate after this task) is execution-locked.
    /// Locked when the task has completed — cannot retroactively add a gate after finished work.
    /// </summary>
    internal static bool IsTaskExitLocked(Plan plan, string taskId)
    {
        var task = plan.Tasks.FirstOrDefault(t =>
            string.Equals(t.TaskId, taskId, StringComparison.Ordinal));
        return task is not null && IsTaskCompleted(task.Status);
    }

    /// <summary>
    /// Determines whether a stage milestone boundary is execution-locked.
    /// Locked when the gate has been traversed, or when all prerequisite tasks (afterTaskIds)
    /// have completed and any of the downstream tasks have started.
    /// </summary>
    internal static bool IsStageMilestoneLocked(Plan plan,
        IReadOnlyList<string> afterTaskIds, IReadOnlyList<string> beforeTaskIds)
    {
        // Check for an existing traversed gate at this boundary
        var existingGate = plan.ApprovalGates.FirstOrDefault(g =>
            g.AfterTaskIds.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(afterTaskIds.OrderBy(x => x, StringComparer.Ordinal)) &&
            g.BeforeTaskIds.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(beforeTaskIds.OrderBy(x => x, StringComparer.Ordinal)));
        if (existingGate is not null && IsGateTraversed(existingGate.Status))
            return true;

        // Also locked if all upstream tasks have completed and downstream work has begun
        var allUpstreamComplete = afterTaskIds.All(id =>
        {
            var t = plan.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, id, StringComparison.Ordinal));
            return t is not null && IsTaskCompleted(t.Status);
        });
        if (!allUpstreamComplete) return false;

        var anyDownstreamStarted = beforeTaskIds.Any(id =>
        {
            var t = plan.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, id, StringComparison.Ordinal));
            return t is not null && IsTaskStartedOrCompleted(t.Status);
        });
        return anyDownstreamStarted;
    }

    /// <summary>
    /// Determines whether an ALL-join approval control is execution-locked.
    /// Locked only after ALL inbound paths (afterTaskIds) have completed.
    /// </summary>
    internal static bool IsAllJoinLocked(Plan plan,
        IReadOnlyList<string> afterTaskIds, IReadOnlyList<string> beforeTaskIds)
    {
        // Check for an existing traversed gate at this boundary
        var existingGate = plan.ApprovalGates.FirstOrDefault(g =>
            g.AfterTaskIds.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(afterTaskIds.OrderBy(x => x, StringComparer.Ordinal)) &&
            g.BeforeTaskIds.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(beforeTaskIds.OrderBy(x => x, StringComparer.Ordinal)));
        if (existingGate is not null && IsGateTraversed(existingGate.Status))
            return true;

        // ALL joins are locked only when every inbound task has completed
        return afterTaskIds.All(id =>
        {
            var t = plan.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, id, StringComparison.Ordinal));
            return t is not null && IsTaskCompleted(t.Status);
        });
    }

    /// <summary>
    /// Returns the tooltip explanation for a locked control.
    /// </summary>
    internal static string LockedTooltip(string controlDescription) =>
        $"{controlDescription} — completed work cannot be modified.";

    /// <summary>
    /// Determines whether the plan is in a state where execution locking applies at all.
    /// Locking only applies when the plan is executing, awaiting approval, interrupted, or blocked.
    /// Plans that are staged/approved (not yet started) have no execution locks.
    /// </summary>
    internal static bool PlanHasExecutionContext(Plan? plan) =>
        plan is not null && plan.LifecycleStatus is
            PlanLifecycleStatus.Executing or
            PlanLifecycleStatus.AwaitingApproval or
            PlanLifecycleStatus.Interrupted or
            PlanLifecycleStatus.Blocked or
            PlanLifecycleStatus.Completed or
            PlanLifecycleStatus.Stopped;
}
