namespace SquadDash;

internal static class PlanValidationProviderFailurePresentation
{
    internal static string BuildAttemptMessage(
        string validationTitle,
        string? rawError,
        bool willRetry,
        ProviderFailureContext? context = null)
    {
        var failure = ProviderFailurePresentation.Analyze(rawError, context);
        var nextAction = failure.IsQuotaEntitlementFailure
            ? failure.Guidance
            : willRetry
                ? "SquadDash will retry this non-mutating validation once."
                : "SquadDash preserved the completed implementation work and paused this validation with a retry action.";
        return $"{failure.Title} · {validationTitle}. {failure.Explanation} {nextAction} " +
               "Completed implementation work will not be repeated.";
    }

    internal static string BuildRecoveryReason(
        string? rawError,
        int attemptCount,
        ProviderFailureContext? context = null)
    {
        var failure = ProviderFailurePresentation.Analyze(rawError, context);
        var attempts = Math.Max(1, attemptCount);
        return $"{failure.Title}. {failure.Explanation} " +
               $"The validation provider stopped {attempts} times before returning evidence. " +
               failure.Guidance;
    }
}
