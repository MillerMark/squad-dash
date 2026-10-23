namespace SquadDash;

internal static class PlanTaskExecutionMode
{
    internal const string Implementation = "implementation";
    internal const string Verification = "verification";
}

internal static class PlanTaskVerificationLevel
{
    internal const string Lightweight = "lightweight";
    internal const string Standard = "standard";
    internal const string Enhanced = "enhanced";

    internal static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Lightweight => Lightweight,
        Standard => Standard,
        Enhanced => Enhanced,
        _ => Lightweight,
    };

    internal static bool IsValid(string? value) =>
        value is null || Normalize(value) == value.Trim().ToLowerInvariant();
}

/// <summary>
/// Distinguishes source-producing work from non-mutating proof work. Older plans did not
/// serialize executionMode, so a narrow title-based fallback keeps their final verification
/// steps usable without weakening commit requirements for ordinary implementation tasks.
/// </summary>
internal static class PlanTaskExecutionPolicy
{
    internal static bool IsVerificationOnly(DecomposedSubTask? task) =>
        task is not null && IsVerificationOnly(
            task.ExecutionMode,
            task.Title,
            task.Outputs,
            task.ProofRequirements);

    internal static bool IsVerificationOnly(PlanTask? task) =>
        task is not null && IsVerificationOnly(
            task.ExecutionMode,
            task.Title,
            task.Outputs,
            task.ProofRequirements);

    internal static bool RequiresIndependentVerification(DecomposedSubTask? task) =>
        task is not null && !IsVerificationOnly(task) &&
        PlanTaskVerificationLevel.Normalize(task.VerificationLevel) != PlanTaskVerificationLevel.Lightweight;

    internal static bool RequiresIndependentVerification(
        DecomposedSubTask? task,
        IReadOnlyList<string> changedFiles) =>
        task is not null && !IsVerificationOnly(task) &&
        (PlanTaskVerificationLevel.Normalize(task.VerificationLevel) != PlanTaskVerificationLevel.Lightweight ||
         ShouldUpgradeLightweightVerification(task, changedFiles));

    internal static bool RequiresIndependentVerification(PlanTask? task) =>
        task is not null && !IsVerificationOnly(task) &&
        PlanTaskVerificationLevel.Normalize(task.VerificationLevel) != PlanTaskVerificationLevel.Lightweight;

    internal static bool ShouldUpgradeLightweightVerification(
        DecomposedSubTask task,
        IReadOnlyList<string> changedFiles)
    {
        if (PlanTaskVerificationLevel.Normalize(task.VerificationLevel) != PlanTaskVerificationLevel.Lightweight)
            return false;
        if (string.Equals(task.Priority, "critical", StringComparison.OrdinalIgnoreCase))
            return true;

        return changedFiles.Any(path =>
            path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("migration", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("security", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("auth", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsVerificationOnly<TOutput, TProof>(
        string? executionMode,
        string? title,
        IReadOnlyList<TOutput>? outputs,
        IReadOnlyList<TProof>? proofRequirements)
    {
        if (string.Equals(executionMode, PlanTaskExecutionMode.Verification, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(executionMode))
            return false;

        // Legacy compatibility only. New plans must state executionMode explicitly.
        if (outputs is { Count: > 0 } || proofRequirements is not { Count: > 0 })
            return false;

        var normalizedTitle = title?.Trim() ?? string.Empty;
        return normalizedTitle.StartsWith("Verify ", StringComparison.OrdinalIgnoreCase) ||
               normalizedTitle.StartsWith("Validate ", StringComparison.OrdinalIgnoreCase) ||
               normalizedTitle.StartsWith("Audit ", StringComparison.OrdinalIgnoreCase) ||
               normalizedTitle.Contains("verification", StringComparison.OrdinalIgnoreCase) ||
               normalizedTitle.Contains("completion audit", StringComparison.OrdinalIgnoreCase);
    }
}
