namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanDelegationRequestContextTests
{
    [Test]
    public void ImplementationRequest_ContainsEveryHostScopedIdentity()
    {
        var context = PlanDelegationRequestContext.Build(
            "PLAN-1", "revision-1", "PLAN-1-005", "implementation", "attempt-1", "feature/plan");

        Assert.That(context, Does.Contain("`plan_execution_request`")
            .And.Contain("\"planId\":\"PLAN-1\"")
            .And.Contain("\"workItemId\":\"PLAN-1-005\"")
            .And.Contain("\"attemptId\":\"attempt-1\"")
            .And.Contain("\"branch\":\"feature/plan\""));
    }

    [Test]
    public void ValidationRequest_OmitsRatherThanNullsAttemptId()
    {
        var context = PlanDelegationRequestContext.Build(
            "PLAN-1", "revision-1", "VAL-1", "validation", null, "feature/plan");

        Assert.That(context, Does.Not.Contain("attemptId"));
    }
}
