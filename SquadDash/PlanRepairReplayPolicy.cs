namespace SquadDash;

/// <summary>
/// Prevents a repaired result envelope from causing the implementation prompt to run again.
/// A pending repair result is host-owned input for finalization, not a new plan iteration.
/// </summary>
internal static class PlanRepairReplayPolicy
{
    internal const string TaskResultRepairSourceTag = "plan-result-envelope-repair";

    internal static string BuildQueueDescription(string repairPrompt) =>
        repairPrompt.Contains(
            "each implementation step must produce exactly one atomic commit",
            StringComparison.OrdinalIgnoreCase)
            ? "The current step's implementation is finished. SquadDash found multiple commits and is confirming which commit belongs to this step. No implementation work will be repeated."
            : "The current step's implementation is finished. SquadDash is correcting its completion record. No implementation work will be repeated.";

    internal static bool ShouldCaptureTurnResponse(
        ActiveLoopExecutionState? execution,
        bool activePlanRuntime,
        bool hasDispatchedQueueItem,
        bool dispatchedItemIsSystemInjected,
        string? dispatchedItemSourceTag)
    {
        // Normal plan iterations are dispatched by the loop, not by the prompt queue.
        // Once a queue item is present, only the typed result-repair continuation owns
        // the plan envelope. An ordinary user prompt must remain ordinary even while a
        // paused plan retains its durable recovery state.
        if (!hasDispatchedQueueItem)
            return activePlanRuntime;

        return dispatchedItemIsSystemInjected &&
               string.Equals(
                   dispatchedItemSourceTag,
                   TaskResultRepairSourceTag,
                   StringComparison.Ordinal) &&
               ShouldPersistTaskRepairResponse(execution);
    }

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

    /// <summary>
    /// A repaired implementation result that was captured before restart is already completed
    /// work. Reclaiming it must reach host finalization before the clean-worktree preflight that
    /// protects new mutating work; otherwise unrelated dirty files can strand a valid handoff.
    /// The normal finalizer remains responsible for assignment, proof, branch, and commit checks.
    /// </summary>
    internal static bool ShouldReplayBeforeMutationPreflight(
        ActiveLoopExecutionState? execution,
        string planId,
        string? expectedRevision)
    {
        if (execution is null ||
            string.IsNullOrWhiteSpace(execution.DecomposeRevision) ||
            string.IsNullOrWhiteSpace(execution.RecoveryTaskId) ||
            !string.Equals(execution.DecomposeGroupId, planId, StringComparison.Ordinal) ||
            (!string.IsNullOrWhiteSpace(expectedRevision) &&
             !string.Equals(execution.DecomposeRevision, expectedRevision, StringComparison.Ordinal)))
            return false;

        return HasMatchingTerminalResult(
            execution,
            planId,
            execution.DecomposeRevision,
            execution.RecoveryTaskId,
            capturedResult: null);
    }

    /// <summary>
    /// A stop request means "finish this iteration, then stop". Once the coordinator has
    /// returned a valid terminal result for the active task, host finalization must still run;
    /// otherwise a simultaneous stop/restart can discard a successful handoff. Identity is
    /// checked here before any stop is allowed to bypass the normal finalization path. The
    /// finalizer still performs all proof, assignment, branch, and Git validation.
    /// </summary>
    internal static bool HasMatchingTerminalResult(
        ActiveLoopExecutionState? execution,
        string planId,
        string revision,
        string taskId,
        DecomposeStepResult? capturedResult)
    {
        if (execution is null ||
            !string.Equals(execution.DecomposeGroupId, planId, StringComparison.Ordinal) ||
            !string.Equals(execution.DecomposeRevision, revision, StringComparison.Ordinal) ||
            execution.ActiveVerificationTaskId is not null ||
            execution.ActiveValidationId is not null)
            return false;

        var attempt = execution.PlanExecutionAttempt;
        if (attempt is not null &&
            (!string.Equals(attempt.PlanId, planId, StringComparison.Ordinal) ||
             !string.Equals(attempt.Revision, revision, StringComparison.Ordinal) ||
             !string.Equals(attempt.TaskId, taskId, StringComparison.Ordinal)))
            return false;

        var result = capturedResult;
        if (result is null && execution.PendingRepairResult is { ResultJson: { } resultJson } pending &&
            pending.Matches(planId, revision, execution.PlanExecutionAttempt?.AttemptId) &&
            string.Equals(pending.TaskId, taskId, StringComparison.Ordinal))
        {
            try
            {
                result = System.Text.Json.JsonSerializer.Deserialize<DecomposeStepResult>(resultJson);
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }

        if (result is null || result.Status is not ("complete" or "partial" or "failed"))
            return false;

        var attemptId = execution?.PlanExecutionAttempt?.AttemptId;
        return string.Equals(result.GroupId, planId, StringComparison.Ordinal) &&
               string.Equals(result.Revision, revision, StringComparison.Ordinal) &&
               string.Equals(result.TaskId, taskId, StringComparison.Ordinal) &&
               (attemptId is null ||
                string.Equals(result.ExecutionAttemptId, attemptId, StringComparison.Ordinal));
    }

    internal static bool ShouldPersistTaskRepairResponse(ActiveLoopExecutionState? execution) =>
        execution?.RecoveryTaskId is not null &&
        execution.ActiveVerificationTaskId is null &&
        execution.ActiveValidationId is null;
}
