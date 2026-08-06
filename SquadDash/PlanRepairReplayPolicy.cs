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
        // PendingRepairResult belongs only to the implementation-result phase. A stale task
        // repair must never consume a verification-envelope or validation iteration.
        if (execution?.ActiveVerificationTaskId is not null || execution?.ActiveValidationId is not null)
            return false;

        var pending = execution?.PendingRepairResult;
        return pending is not null &&
               pending.Matches(planId, revision, execution?.PlanExecutionAttempt?.AttemptId) &&
               string.Equals(pending.TaskId, taskId, StringComparison.Ordinal);
    }

    internal static bool ShouldPersistTaskRepairResponse(ActiveLoopExecutionState? execution) =>
        execution?.RecoveryTaskId is not null &&
        execution.ActiveVerificationTaskId is null &&
        execution.ActiveValidationId is null;
}
