namespace SquadDash;

internal enum AssessedPlanContinuationAction
{
    RemainStopped,
    Complete,
    StartExecution,
}

/// <summary>
/// Selects the boundary that follows acceptance of AI-assessed interrupted work.
/// Approval advancement runs first because a newly-ready gate may either pause the plan or
/// remain non-blocking while an independent task continues.
/// </summary>
internal static class AssessedPlanContinuationPolicy
{
    internal static AssessedPlanContinuationAction Resolve(
        Plan plan,
        string? nextTaskId,
        PlanValidationNode? nextValidation)
    {
        if (plan.LifecycleStatus != PlanLifecycleStatus.Executing)
            return AssessedPlanContinuationAction.RemainStopped;

        if (nextTaskId is null &&
            nextValidation is null &&
            PlanValidationReadinessEvaluator.AllRequiredPassed(plan) &&
            ApprovalGateReadinessEvaluator.AllRequiredApproved(plan))
        {
            return AssessedPlanContinuationAction.Complete;
        }

        return AssessedPlanContinuationAction.StartExecution;
    }
}
