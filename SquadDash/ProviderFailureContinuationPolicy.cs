namespace SquadDash;

internal static class ProviderFailureContinuationPolicy
{
    internal static bool ShouldInterruptRequiredPlanWork(
        ProviderFailureCategory category,
        bool coordinatorPromptRunning,
        bool planExecutionActive,
        string? assignedPlanTaskId,
        bool? rosterIdentityVerified)
    {
        if (!coordinatorPromptRunning ||
            !planExecutionActive ||
            string.IsNullOrWhiteSpace(assignedPlanTaskId) ||
            rosterIdentityVerified == false)
        {
            return false;
        }

        return category is
            ProviderFailureCategory.Authentication or
            ProviderFailureCategory.DeploymentOrModel or
            ProviderFailureCategory.EndpointOrProtocol;
    }
}
