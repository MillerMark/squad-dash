namespace SquadDash;

internal static class InterruptedPlanEvidenceRefresher
{
    internal static PlanTaskCommitEvidence? SelectReplacement(
        PlanTaskCommitEvidence? existingEvidence,
        PlanTaskCommitEvidence candidate,
        Func<string, string, bool> isAncestor)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(isAncestor);

        if (existingEvidence is null)
            return candidate;
        if (!string.Equals(existingEvidence.TaskId, candidate.TaskId, StringComparison.Ordinal))
            return null;
        if (string.IsNullOrWhiteSpace(existingEvidence.Commit) ||
            string.IsNullOrWhiteSpace(candidate.Commit))
            return null;
        if (string.Equals(existingEvidence.Commit, candidate.Commit, StringComparison.OrdinalIgnoreCase))
            return null;
        return isAncestor(existingEvidence.Commit, candidate.Commit)
            ? candidate
            : null;
    }
}
