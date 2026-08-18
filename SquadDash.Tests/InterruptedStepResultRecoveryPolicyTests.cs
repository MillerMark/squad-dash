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
}
