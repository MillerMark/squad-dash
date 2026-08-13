using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Pure-logic resolver that determines the <see cref="PlanTaskActivityState"/> for each task in a
/// <see cref="Plan"/>. Designed to be testable without WPF dependencies. Supports parallel tasks
/// (multiple executing simultaneously) and restart convergence (durable state without live events).
/// </summary>
internal static class PlanTaskActivityResolver
{
    /// <summary>
    /// Resolves the activity state for every task in the plan, keyed by TaskId.
    /// </summary>
    internal static IReadOnlyDictionary<string, PlanTaskActivityState> Resolve(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var result = new Dictionary<string, PlanTaskActivityState>(StringComparer.Ordinal);

        // Build a set of gate-blocked task IDs (tasks after a pending/awaiting gate)
        var gateBlockedTaskIds = GetGateBlockedTaskIds(plan);

        foreach (var task in plan.Tasks)
        {
            result[task.TaskId] = ResolveTaskState(task, plan, gateBlockedTaskIds);
        }

        return result;
    }

    /// <summary>
    /// Resolves the aggregate activity state for the entire plan (for the Plans panel indicator).
    /// </summary>
    internal static PlanTaskActivityState ResolvePlanLevel(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.LifecycleStatus switch
        {
            PlanLifecycleStatus.Executing        => PlanTaskActivityState.Executing,
            PlanLifecycleStatus.AwaitingApproval  => PlanTaskActivityState.AwaitingApproval,
            PlanLifecycleStatus.Blocked           => PlanTaskActivityState.Blocked,
            PlanLifecycleStatus.Interrupted       => PlanTaskActivityState.Interrupted,
            PlanLifecycleStatus.Completed         => PlanTaskActivityState.Completed,
            PlanLifecycleStatus.Stopped           => PlanTaskActivityState.Completed,
            PlanLifecycleStatus.Archived          => PlanTaskActivityState.Completed,
            _                                     => PlanTaskActivityState.Queued,
        };
    }

    private static PlanTaskActivityState ResolveTaskState(
        PlanTask task,
        Plan plan,
        IReadOnlySet<string> gateBlockedTaskIds)
    {
        // Terminal states first
        if (task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded)
            return PlanTaskActivityState.Completed;

        if (task.Status is PlanTaskStatus.Failed)
            return PlanTaskActivityState.Blocked;

        if (task.Status is PlanTaskStatus.HumanReviewRequired)
            return PlanTaskActivityState.AwaitingApproval;

        // A persisted task row can still say Executing/Verifying/Reworking after an
        // interruption. The plan lifecycle is authoritative for whether work is live; never
        // animate a task when there is no executing plan turn to drive it.
        if (plan.LifecycleStatus is PlanLifecycleStatus.Interrupted or PlanLifecycleStatus.Blocked &&
            (task.Status is PlanTaskStatus.Executing or PlanTaskStatus.Reworking or PlanTaskStatus.VerificationPending ||
             PlanTaskStatus.IsVerifying(task.Status) ||
             string.Equals(plan.Progress.ExecutingTaskId, task.TaskId, StringComparison.Ordinal)))
            return plan.LifecycleStatus == PlanLifecycleStatus.Blocked
                ? PlanTaskActivityState.Blocked
                : PlanTaskActivityState.Interrupted;

        if (task.Status is PlanTaskStatus.VerificationPending)
            return PlanTaskActivityState.VerificationPending;

        if (PlanTaskStatus.IsVerifying(task.Status))
            return plan.LifecycleStatus == PlanLifecycleStatus.Executing
                ? PlanTaskActivityState.Verifying
                : PlanTaskActivityState.VerificationPending;

        if (task.Status is PlanTaskStatus.Reworking)
            return PlanTaskActivityState.Reworking;

        // The progress projection is the authoritative single-task fallback during
        // restart convergence, when the task row can briefly remain Pending.
        if (plan.LifecycleStatus == PlanLifecycleStatus.Executing &&
            (task.Status is PlanTaskStatus.Executing ||
            string.Equals(plan.Progress.ExecutingTaskId, task.TaskId, StringComparison.Ordinal))
           )
            return PlanTaskActivityState.Executing;

        // Partial status: task was interrupted mid-execution
        if (task.Status is PlanTaskStatus.Partial)
            return PlanTaskActivityState.Interrupted;

        // Pending tasks: check if blocked by a gate
        if (gateBlockedTaskIds.Contains(task.TaskId))
            return PlanTaskActivityState.AwaitingApproval;

        // Check if blocked by plan-level interruption
        if (plan.LifecycleStatus is PlanLifecycleStatus.Interrupted)
            return PlanTaskActivityState.Interrupted;

        // Check if blocked by plan-level awaiting-approval
        if (plan.LifecycleStatus is PlanLifecycleStatus.AwaitingApproval)
            return PlanTaskActivityState.AwaitingApproval;

        // Check if blocked by a failed dependency
        if (HasFailedDependency(task, plan))
            return PlanTaskActivityState.Blocked;

        // Default: queued (waiting for dependencies or execution slot)
        return PlanTaskActivityState.Queued;
    }

    private static bool HasFailedDependency(PlanTask task, Plan plan)
    {
        if (task.DependsOn.Count == 0) return false;

        var taskLookup = plan.Tasks.ToDictionary(t => t.TaskId, StringComparer.Ordinal);
        foreach (var depId in task.DependsOn)
        {
            if (taskLookup.TryGetValue(depId, out var dep) && dep.Status is PlanTaskStatus.Failed)
                return true;
        }
        return false;
    }

    private static IReadOnlySet<string> GetGateBlockedTaskIds(Plan plan)
    {
        var blocked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var gate in plan.ApprovalGates)
        {
            if (gate.Status is PlanGateStatus.Pending or PlanGateStatus.AwaitingApproval)
            {
                foreach (var taskId in gate.BeforeTaskIds)
                {
                    blocked.Add(taskId);
                }
            }
        }

        return blocked;
    }
}
