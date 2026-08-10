namespace SquadDash;

/// <summary>
/// Prevents a repaired result envelope from causing the implementation prompt to run again.
/// A pending repair result is host-owned input for finalization, not a new plan iteration.
/// </summary>
internal static class PlanRepairReplayPolicy
{
    internal static bool TryCreatePendingResult(
        ActiveLoopExecutionState? execution,
        DecomposeStepResult? result,
        string? parseError,
        out PendingRepairResult? pending,
        out string? rejection)
    {
        pending = null;
        rejection = null;
        if (!ShouldPersistTaskRepairResponse(execution) ||
            string.IsNullOrWhiteSpace(execution!.DecomposeGroupId) ||
            string.IsNullOrWhiteSpace(execution.DecomposeRevision) ||
            string.IsNullOrWhiteSpace(execution.RecoveryTaskId))
        {
            rejection = "the durable execution is not awaiting an implementation-result repair";
            return false;
        }

        var groupId = execution.DecomposeGroupId!;
        var revision = execution.DecomposeRevision!;
        var taskId = execution.RecoveryTaskId!;
        var attemptId = execution.PlanExecutionAttempt?.AttemptId;
        if (result is not null)
        {
            if (!string.Equals(result.GroupId, groupId, StringComparison.Ordinal) ||
                !string.Equals(result.TaskId, taskId, StringComparison.Ordinal) ||
                !string.Equals(result.Revision, revision, StringComparison.Ordinal))
            {
                rejection = "the repaired result does not match the durable plan, task, and revision";
                return false;
            }
            if (result.ExecutionAttemptId is not null && attemptId is not null &&
                !string.Equals(result.ExecutionAttemptId, attemptId, StringComparison.Ordinal))
            {
                rejection = "the repaired result belongs to a different execution attempt";
                return false;
            }
        }
        else if (string.IsNullOrWhiteSpace(parseError))
        {
            rejection = "the repair response produced neither a result nor a parse error";
            return false;
        }

        pending = new PendingRepairResult(
            groupId,
            revision,
            taskId,
            attemptId,
            result is not null ? System.Text.Json.JsonSerializer.Serialize(result) : null,
            parseError);
        return true;
    }

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
