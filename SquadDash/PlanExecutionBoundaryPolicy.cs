namespace SquadDash;

/// <summary>
/// Classifies the next host-owned operation at an executing-plan boundary. Validation
/// nodes take precedence over task-frontier and approval-stop classification because a
/// ready validation is executable plan work, not a human approval request.
/// </summary>
internal static class PlanExecutionBoundaryPolicy
{
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

        return PlanValidationScheduler.GetInProgressValidation(plan)
            ?? PlanValidationScheduler.SelectNextSchedulable(plan);
    }

    internal static bool ShouldStopForHumanApproval(Plan plan) =>
        SelectValidation(plan) is null &&
        ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan);

    internal static bool HasFailedValidation(Plan plan) =>
        (plan.Validations ?? []).Any(validation =>
            validation.Status == PlanValidationStatus.Failed);
}
