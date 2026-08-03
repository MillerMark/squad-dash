namespace SquadDash;

/// <summary>
/// Prevents a repaired result envelope from causing the implementation prompt to run again.
/// A pending repair result is host-owned input for finalization, not a new plan iteration.
/// </summary>
internal static class PlanRepairReplayPolicy
{
    internal static bool ShouldFinalizeWithoutDispatch(
        ActiveLoopExecutionState? execution,
        string planId,
        string revision,
        string taskId)
    {
        var pending = execution?.PendingRepairResult;
        return pending is not null &&
               pending.Matches(planId, revision, execution?.PlanExecutionAttempt?.AttemptId) &&
               string.Equals(pending.TaskId, taskId, StringComparison.Ordinal);
    }
}
