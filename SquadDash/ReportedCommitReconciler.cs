namespace SquadDash;

/// <summary>
/// Recognizes the narrow case where an agent copied a resolvable-length commit prefix with one
/// substituted hexadecimal character. Repository ancestry and atomic-commit validation remain
/// host responsibilities; this helper only identifies the transcription error.
/// </summary>
internal static class ReportedCommitReconciler
{
    internal const int MinimumCommitPrefixLength = 7;

    internal static bool MatchesObservedHead(string? candidateCommit, string? observedHead)
    {
        if (string.IsNullOrWhiteSpace(candidateCommit) || string.IsNullOrWhiteSpace(observedHead))
            return false;

        var candidate = candidateCommit.Trim();
        var head = observedHead.Trim();
        return candidate.Length >= MinimumCommitPrefixLength &&
               candidate.Length <= head.Length &&
               candidate.All(Uri.IsHexDigit) &&
               head.All(Uri.IsHexDigit) &&
               head.StartsWith(candidate, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsSingleCharacterSubstitution(string? reportedCommit, string? observedHead)
    {
        if (string.IsNullOrWhiteSpace(reportedCommit) || string.IsNullOrWhiteSpace(observedHead))
            return false;

        var reported = reportedCommit.Trim();
        var head = observedHead.Trim();
        if (reported.Length < MinimumCommitPrefixLength || reported.Length > head.Length ||
            !reported.All(Uri.IsHexDigit) || !head.All(Uri.IsHexDigit))
            return false;

        var differences = 0;
        for (var i = 0; i < reported.Length; i++)
        {
            if (char.ToUpperInvariant(reported[i]) == char.ToUpperInvariant(head[i]))
                continue;

            differences++;
            if (differences > 1)
                return false;
        }

        return differences == 1;
    }
}
