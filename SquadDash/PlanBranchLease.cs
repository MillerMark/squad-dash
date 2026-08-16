namespace SquadDash;

/// <summary>
/// Host-owned Git branch identity captured for one plan task dispatch. Workers may read this
/// lease, but only SquadDash may create, switch, or advance the canonical branch.
/// </summary>
internal sealed record PlanBranchLease(
    string PlanId,
    string Revision,
    string CanonicalBranch,
    string BaselineCommit,
    DateTimeOffset AcquiredAt);

internal enum PlanBranchRepairAction
{
    None,
    RestoreCanonicalBranch,
    AdoptCandidateCommit,
    PauseForReview,
}

internal sealed record PlanBranchRepairAssessment(
    PlanBranchRepairAction Action,
    string Explanation)
{
    internal bool CanContinue => Action != PlanBranchRepairAction.PauseForReview;
}

internal sealed record PlanBranchEnforcementResult(
    bool CanContinue,
    bool Repaired,
    bool AdoptedCommit,
    string Message);

internal sealed class PlanBranchLeaseViolationException(string message) : InvalidOperationException(message);

internal static class PlanBranchLeaseRecoveryPolicy
{
    internal static bool IsBranchMismatchReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return false;
        return reason.Contains("host-owned plan branch", StringComparison.OrdinalIgnoreCase) ||
               (reason.Contains("requires branch", StringComparison.OrdinalIgnoreCase) &&
                reason.Contains("is active", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Pure policy for deciding whether a branch divergence can be repaired without guessing about
/// commit ownership. Git inspection and mutations remain host-owned in MainWindow.
/// </summary>
internal static class PlanBranchLeasePolicy
{
    internal static PlanBranchRepairAssessment Assess(
        PlanBranchLease lease,
        string activeBranch,
        string canonicalHead,
        string activeHead,
        string? candidateCommit,
        bool canonicalIsAncestorOfCandidate,
        int candidateCommitCount,
        bool worktreeIsSafe,
        bool mutationAllowed)
    {
        if (string.Equals(activeBranch, lease.CanonicalBranch, StringComparison.Ordinal))
            return new(PlanBranchRepairAction.None, "The canonical plan branch is active.");

        if (string.Equals(canonicalHead, activeHead, StringComparison.OrdinalIgnoreCase))
            return new(
                PlanBranchRepairAction.RestoreCanonicalBranch,
                $"The branch name changed to '{activeBranch}', but both branches still point to {activeHead}.");

        if (!mutationAllowed)
            return Pause("This verification or validation turn changed Git history; non-mutating plan work cannot be adopted automatically.");
        if (!worktreeIsSafe)
            return Pause("The worktree contains changes outside SquadDash-owned plan metadata.");
        if (string.IsNullOrWhiteSpace(candidateCommit) ||
            !string.Equals(candidateCommit, activeHead, StringComparison.OrdinalIgnoreCase))
            return Pause("The worker's reported commit is not the active branch HEAD.");
        if (!string.Equals(canonicalHead, lease.BaselineCommit, StringComparison.OrdinalIgnoreCase))
            return Pause("The canonical branch advanced after this task was dispatched.");
        if (!canonicalIsAncestorOfCandidate)
            return Pause("The worker commit does not descend from the canonical task baseline.");
        if (candidateCommitCount != 1)
            return Pause($"The worker branch contains {candidateCommitCount} commits after the task baseline; exactly one task commit is required.");

        return new(
            PlanBranchRepairAction.AdoptCandidateCommit,
            $"The single task commit {candidateCommit} can be adopted from '{activeBranch}' onto '{lease.CanonicalBranch}'.");
    }

    private static PlanBranchRepairAssessment Pause(string explanation) =>
        new(PlanBranchRepairAction.PauseForReview, explanation);
}
