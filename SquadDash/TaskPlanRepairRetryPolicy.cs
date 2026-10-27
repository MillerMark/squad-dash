namespace SquadDash;

internal sealed record TaskPlanRepairRetryDecision(
    bool ShouldRetry,
    string Reason);

internal static class TaskPlanRepairRetryPolicy
{
    internal const int MaximumRepairAttempts = 2;

    internal static TaskPlanRepairRetryDecision Evaluate(
        int repairAttemptsAlreadyQueued,
        IReadOnlyList<string> previousDiagnostics,
        IReadOnlyList<string> currentDiagnostics,
        string? previousCandidateHash,
        string? currentCandidateHash)
    {
        if (repairAttemptsAlreadyQueued == 0)
            return new TaskPlanRepairRetryDecision(true, "the first automatic repair is allowed");

        if (repairAttemptsAlreadyQueued >= MaximumRepairAttempts)
            return new TaskPlanRepairRetryDecision(false, "the two automatic repair attempts were exhausted");

        if (string.IsNullOrWhiteSpace(currentCandidateHash) ||
            string.Equals(previousCandidateHash, currentCandidateHash, StringComparison.Ordinal))
        {
            return new TaskPlanRepairRetryDecision(false, "the repair did not produce a different plan candidate");
        }

        var previous = Normalize(previousDiagnostics);
        var current = Normalize(currentDiagnostics);
        if (current.Count >= previous.Count)
        {
            return new TaskPlanRepairRetryDecision(
                false,
                current.Count > previous.Count
                    ? $"validation regressed from {previous.Count} issue(s) to {current.Count}"
                    : $"validation did not reduce the {previous.Count} remaining issue(s)");
        }

        return new TaskPlanRepairRetryDecision(
            true,
            $"validation advanced from {previous.Count} issue(s) to {current.Count}");
    }

    internal static IReadOnlyList<string> NormalizeForTracking(IEnumerable<string>? diagnostics) =>
        Normalize(diagnostics).OrderBy(value => value, StringComparer.Ordinal).ToArray();

    private static HashSet<string> Normalize(IEnumerable<string>? diagnostics) =>
        (diagnostics ?? [])
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
        .ToHashSet(StringComparer.Ordinal);
}
