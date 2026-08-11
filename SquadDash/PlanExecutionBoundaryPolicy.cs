namespace SquadDash;

/// <summary>
/// Classifies the next host-owned operation at an executing-plan boundary. Validation
/// nodes take precedence over task-frontier and approval-stop classification because a
/// ready validation is executable plan work, not a human approval request.
/// </summary>
internal static class PlanExecutionBoundaryPolicy
{
    internal static PlanPreIterationBoundaryAction ResolvePreIteration(
        Plan? plan,
        DecomposeGroupExecutionState groupState)
    {
        if (groupState == DecomposeGroupExecutionState.AwaitingApproval && plan is not null)
            return ShouldStopForHumanApproval(plan)
                ? PlanPreIterationBoundaryAction.ActivateApproval
                : PlanPreIterationBoundaryAction.Continue;

        return groupState is DecomposeGroupExecutionState.Blocked or
            DecomposeGroupExecutionState.AwaitingApproval or
            DecomposeGroupExecutionState.Complete or
            DecomposeGroupExecutionState.Missing or
            DecomposeGroupExecutionState.Unreadable
                ? PlanPreIterationBoundaryAction.Stop
                : PlanPreIterationBoundaryAction.Continue;
    }

    internal static PlanValidationNode? SelectValidation(
        Plan plan,
        string? activeValidationId = null)
    {
        var validations = plan.Validations ?? [];
        if (!string.IsNullOrWhiteSpace(activeValidationId))
        {
            var active = validations.FirstOrDefault(validation =>
                string.Equals(validation.ValidationId, activeValidationId, StringComparison.Ordinal) &&
                validation.Status is PlanValidationStatus.Ready or PlanValidationStatus.Validating);
            if (active is not null)
                return active;
        }

        var inProgress = PlanValidationScheduler.GetInProgressValidation(plan);
        if (inProgress is not null) return inProgress;

        var readyIds = PlanValidationReadinessEvaluator.Evaluate(plan)
            .Where(state => state.IsReady)
            .Select(state => state.ValidationId)
            .ToHashSet(StringComparer.Ordinal);
        var ordered = validations
            .OrderByDescending(validation =>
                string.Equals(validation.ValidationId, activeValidationId, StringComparison.Ordinal));
        return ordered.FirstOrDefault(validation =>
            readyIds.Contains(validation.ValidationId) &&
            !ApprovalGateReadinessEvaluator.IsValidationBlockedByApproval(plan, validation));
    }

    internal static bool ShouldStopForHumanApproval(Plan plan) =>
        SelectValidation(plan) is null &&
        ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan);

    internal static bool HasFailedValidation(Plan plan) =>
        (plan.Validations ?? []).Any(validation =>
            validation.Status == PlanValidationStatus.Failed);

    /// <summary>
    /// Identifies plans written by the pre-fix validation boundary path: the validation passed,
    /// a pending human gate became the only remaining frontier, and shutdown recorded a generic
    /// interruption before the approval runtime could activate that gate.
    /// </summary>
    internal static bool ShouldRecoverInterruptedApprovalBoundary(Plan plan) =>
        plan.LifecycleStatus == PlanLifecycleStatus.Interrupted &&
        plan.Progress.ExecutingTaskId is null &&
        plan.InterruptionData is
        {
            InterruptedTaskId: null,
            Reason: "Plan execution stopped before the current task was accepted."
        } &&
        SelectValidation(plan) is null &&
        plan.ApprovalGates.Any(gate => gate.Status == PlanGateStatus.Pending) &&
        ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan);
}

internal enum PlanPreIterationBoundaryAction
{
    Continue,
    ActivateApproval,
    Stop,
}
