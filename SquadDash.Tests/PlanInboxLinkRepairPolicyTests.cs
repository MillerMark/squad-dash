namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanInboxLinkRepairPolicyTests
{
    [Test]
    public void FirstBrokenDelivery_RequiresRepair()
    {
        var decision = PlanInboxLinkRepairPolicy.Evaluate(
            pendingRepairError: null,
            hasInboxDelivery: true,
            currentValidationError: "broken target");

        Assert.Multiple(() =>
        {
            Assert.That(decision.ResultError, Is.EqualTo("broken target"));
            Assert.That(decision.PendingRepairError, Is.EqualTo("broken target"));
        });
    }

    [Test]
    public void CorrectedDelivery_ClearsDurableRepairRequirement()
    {
        var decision = PlanInboxLinkRepairPolicy.Evaluate(
            pendingRepairError: "broken target",
            hasInboxDelivery: true,
            currentValidationError: null);

        Assert.Multiple(() =>
        {
            Assert.That(decision.ResultError, Is.Null);
            Assert.That(decision.PendingRepairError, Is.Null);
        });
    }

    [Test]
    public void MissingDelivery_CannotBypassPendingRepair()
    {
        var decision = PlanInboxLinkRepairPolicy.Evaluate(
            pendingRepairError: "broken target",
            hasInboxDelivery: false,
            currentValidationError: null);

        Assert.Multiple(() =>
        {
            Assert.That(decision.ResultError, Does.Contain("did not re-emit"));
            Assert.That(decision.PendingRepairError, Is.EqualTo("broken target"));
        });
    }

    [Test]
    public void RepeatedBrokenDelivery_RemainsRejectedWithLatestDiagnostic()
    {
        var decision = PlanInboxLinkRepairPolicy.Evaluate(
            pendingRepairError: "first broken target",
            hasInboxDelivery: true,
            currentValidationError: "second broken target");

        Assert.Multiple(() =>
        {
            Assert.That(decision.ResultError, Is.EqualTo("second broken target"));
            Assert.That(decision.PendingRepairError, Is.EqualTo("second broken target"));
        });
    }
}
