namespace SquadDash;

internal static class ProviderFailureContinuationPolicy
{
    internal static bool ShouldPauseRequiredPlanWork(
        ProviderFailureCategory category,
        bool coordinatorPromptRunning,
        bool planExecutionActive,
        string? assignedPlanTaskId,
        bool? rosterIdentityVerified) =>
        category == ProviderFailureCategory.RateLimitOrQuota &&
        IsVerifiedRequiredPlanWork(
            coordinatorPromptRunning,
            planExecutionActive,
            assignedPlanTaskId,
            rosterIdentityVerified);

    internal static bool ShouldInterruptRequiredPlanWork(
        ProviderFailureCategory category,
        bool coordinatorPromptRunning,
        bool planExecutionActive,
        string? assignedPlanTaskId,
        bool? rosterIdentityVerified)
    {
        if (!IsVerifiedRequiredPlanWork(
                coordinatorPromptRunning,
                planExecutionActive,
                assignedPlanTaskId,
                rosterIdentityVerified))
            return false;

        return category is
            ProviderFailureCategory.Authentication or
            ProviderFailureCategory.DeploymentOrModel or
            ProviderFailureCategory.EndpointOrProtocol;
    }

    private static bool IsVerifiedRequiredPlanWork(
        bool coordinatorPromptRunning,
        bool planExecutionActive,
        string? assignedPlanTaskId,
        bool? rosterIdentityVerified) =>
        coordinatorPromptRunning &&
        planExecutionActive &&
        !string.IsNullOrWhiteSpace(assignedPlanTaskId) &&
        rosterIdentityVerified != false;
}
