using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanBranchLeasePolicyTests
{
    private static readonly PlanBranchLease Lease = new(
        "PLAN-1",
        "revision-1",
        "feature/canonical",
        "1111111111111111111111111111111111111111",
        DateTimeOffset.UtcNow);

    [Test]
    public void CurrentCanonicalBranch_NeedsNoRepair()
    {
        var result = PlanBranchLeasePolicy.Assess(
            Lease,
            "feature/canonical",
            Lease.BaselineCommit,
            Lease.BaselineCommit,
            null,
            canonicalIsAncestorOfCandidate: false,
            candidateCommitCount: -1,
            worktreeIsSafe: true,
            mutationAllowed: true);

        Assert.That(result.Action, Is.EqualTo(PlanBranchRepairAction.None));
    }

    [Test]
    public void RenamedBranchAtSameHead_IsRestoredWithoutCommitMovement()
    {
        var result = PlanBranchLeasePolicy.Assess(
            Lease,
            "feature/wrong-name",
            Lease.BaselineCommit,
            Lease.BaselineCommit,
            null,
            canonicalIsAncestorOfCandidate: false,
            candidateCommitCount: -1,
            worktreeIsSafe: true,
            mutationAllowed: true);

        Assert.That(result.Action, Is.EqualTo(PlanBranchRepairAction.RestoreCanonicalBranch));
    }

    [Test]
    public void SingleReportedTaskCommit_OnWrongBranch_IsSafeToAdopt()
    {
        const string candidate = "2222222222222222222222222222222222222222";
        var result = PlanBranchLeasePolicy.Assess(
            Lease,
            "feature/wrong-name",
            Lease.BaselineCommit,
            candidate,
            candidate,
            canonicalIsAncestorOfCandidate: true,
            candidateCommitCount: 1,
            worktreeIsSafe: true,
            mutationAllowed: true);

        Assert.That(result.Action, Is.EqualTo(PlanBranchRepairAction.AdoptCandidateCommit));
    }

    [TestCase(false, 1, true, "does not descend")]
    [TestCase(true, 2, true, "exactly one")]
    [TestCase(true, 1, false, "outside SquadDash-owned")]
    public void AmbiguousHistory_PausesWithoutGuessing(
        bool isAncestor,
        int commitCount,
        bool worktreeSafe,
        string expectedReason)
    {
        const string candidate = "2222222222222222222222222222222222222222";
        var result = PlanBranchLeasePolicy.Assess(
            Lease,
            "feature/wrong-name",
            Lease.BaselineCommit,
            candidate,
            candidate,
            isAncestor,
            commitCount,
            worktreeSafe,
            mutationAllowed: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.Action, Is.EqualTo(PlanBranchRepairAction.PauseForReview));
            Assert.That(result.Explanation, Does.Contain(expectedReason));
        });
    }

    [Test]
    public void VerificationTurn_NeverAdoptsAMutatingCommit()
    {
        const string candidate = "2222222222222222222222222222222222222222";
        var result = PlanBranchLeasePolicy.Assess(
            Lease,
            "feature/wrong-name",
            Lease.BaselineCommit,
            candidate,
            candidate,
            canonicalIsAncestorOfCandidate: true,
            candidateCommitCount: 1,
            worktreeIsSafe: true,
            mutationAllowed: false);

        Assert.That(result.Action, Is.EqualTo(PlanBranchRepairAction.PauseForReview));
        Assert.That(result.Explanation, Does.Contain("non-mutating"));
    }

    [TestCase("SquadDash detected that plan work left the host-owned plan branch 'feature/right'.", true)]
    [TestCase("Loop crashed unexpectedly: Plan P requires branch 'feature/right', but 'feature/wrong' is active.", true)]
    [TestCase("Uncommitted changes must be committed first.", false)]
    [TestCase(null, false)]
    public void RecoveryPolicy_RecognizesCurrentAndLegacyBranchMismatchReasons(
        string? reason,
        bool expected)
    {
        Assert.That(PlanBranchLeaseRecoveryPolicy.IsBranchMismatchReason(reason), Is.EqualTo(expected));
    }
}
