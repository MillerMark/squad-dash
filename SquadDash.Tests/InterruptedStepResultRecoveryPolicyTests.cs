namespace SquadDash.Tests;

[TestFixture]
internal sealed class InterruptedStepResultRecoveryPolicyTests
{
    private static Plan MakePlan(string status = PlanLifecycleStatus.Interrupted) => new(
        "PLAN-1",
        "rev-1",
        "tasks_json",
        status,
        "Plan",
        "feature/plan",
        "Summary",
        [new PlanTask("PLAN-1-001", "Task", "Work", [], "high", PlanTaskStatus.Failed)],
        [],
        new PlanProgress(0, 1),
        new PlanTimestamps(DateTimeOffset.UtcNow),
        new PlanInterruptionData(
            "Stopped",
            PlanRecoveryState.PendingRecovery,
            1,
            InterruptedTaskId: "PLAN-1-001"));

    private static DecomposeStepResult MakeResult(string taskId = "PLAN-1-001") => new(
        "PLAN-1",
        taskId,
        "rev-1",
        "complete",
        "abcdef1",
        "Done",
        [],
        new DecomposeStepVerification("passed", "dotnet test", "Passed"));

    [Test]
    public void MatchingLateCompleteResult_RoutesToHostCommitReview()
    {
        Assert.That(
            InterruptedStepResultRecoveryPolicy.CanRouteToCommitReview(MakePlan(), MakeResult()),
            Is.True);
    }

    [TestCase(PlanLifecycleStatus.Executing)]
    [TestCase(PlanLifecycleStatus.Completed)]
    public void NonInterruptedPlan_DoesNotRoute(string status)
    {
        Assert.That(
            InterruptedStepResultRecoveryPolicy.CanRouteToCommitReview(MakePlan(status), MakeResult()),
            Is.False);
    }

    [Test]
    public void DifferentInterruptedTask_DoesNotRoute()
    {
        Assert.That(
            InterruptedStepResultRecoveryPolicy.CanRouteToCommitReview(MakePlan(), MakeResult("PLAN-1-002")),
            Is.False);
    }

    [Test]
    public void NewerDescendantCommitRefreshesTaskEvidence()
    {
        var existingEvidence = new PlanTaskCommitEvidence(
            "PLAN-1-001",
            "attempt-1",
            "base",
            "ddc9136669",
            "Original run summary.",
            new DecomposeStepVerification("passed", "dotnet test", "93/93 tests passed"));
        var candidate = new PlanTaskCommitEvidence(
            "PLAN-1-001",
            "attempt-2",
            "base",
            "f8f3dd89b9",
            "Retry run summary.",
            new DecomposeStepVerification("passed", "dotnet test", "98/98 tests passed"));

        var replacement = global::SquadDash.InterruptedPlanEvidenceRefresher.SelectReplacement(
            existingEvidence,
            candidate,
            (ancestor, descendant) =>
                string.Equals(ancestor, "ddc9136669", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(descendant, "f8f3dd89b9", StringComparison.OrdinalIgnoreCase));
        var nonDescendant = global::SquadDash.InterruptedPlanEvidenceRefresher.SelectReplacement(
            existingEvidence,
            candidate,
            (_, _) => false);
        var whenMissingExisting = global::SquadDash.InterruptedPlanEvidenceRefresher.SelectReplacement(
            null,
            candidate,
            (_, _) => false);
        var sameCommit = global::SquadDash.InterruptedPlanEvidenceRefresher.SelectReplacement(
            existingEvidence,
            candidate with { Commit = "ddc9136669" },
            (_, _) => true);

        Assert.Multiple(() =>
        {
            Assert.That(replacement, Is.EqualTo(candidate));
            Assert.That(replacement!.Verification!.Summary, Does.Contain("98/98"));
            Assert.That(nonDescendant, Is.Null);
            Assert.That(whenMissingExisting, Is.EqualTo(candidate));
            Assert.That(sameCommit, Is.Null);
        });
    }
}
