namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanInboxLinkRepairPolicyTests
{
    [Test]
    public void ActivePlanDelivery_IsStagedUntilEnvelopeAcceptance()
    {
        const string response = "INBOX_MESSAGE_JSON:\n{\"subject\":\"Review\"}";

        Assert.Multiple(() =>
        {
            Assert.That(
                PlanInboxLinkRepairPolicy.ShouldStageDelivery(
                    isExecutingPlan: true,
                    envelopeAccepted: false,
                    response),
                Is.True);
            Assert.That(
                PlanInboxLinkRepairPolicy.ShouldStageDelivery(
                    isExecutingPlan: true,
                    envelopeAccepted: true,
                    response),
                Is.False);
        });
    }

    [Test]
    public void OrdinaryInboxDelivery_IsNotStaged()
    {
        const string response = "INBOX_MESSAGE_JSON_FILE:\n{\"path\":\".squad/tmp/agent-artifacts/review.json\"}";

        Assert.That(
            PlanInboxLinkRepairPolicy.ShouldStageDelivery(
                isExecutingPlan: false,
                envelopeAccepted: false,
                response),
            Is.False);
    }

    [Test]
    public void PlanResponseWithoutInboxIntent_IsNotStaged()
    {
        Assert.That(
            PlanInboxLinkRepairPolicy.ShouldStageDelivery(
                isExecutingPlan: true,
                envelopeAccepted: false,
                "DECOMPOSE_STEP_RESULT_JSON:\n{}"),
            Is.False);
    }

    [Test]
    public void TerminalBoundary_DiscardsStagedDeliveryEvenWithoutDeliveryError()
    {
        var execution = new ActiveLoopExecutionState(
            "D:/repo/.squad/loop-executing-plan.md",
            "PLAN-1",
            DecomposeGroupId: "PLAN-1",
            PendingInboxDeliveryRawResponse: "INBOX_MESSAGE_JSON:\n{\"subject\":\"stale\"}");

        var cleared = PlanInboxLinkRepairPolicy.ClearPendingDelivery(execution);

        Assert.Multiple(() =>
        {
            Assert.That(cleared.PendingInboxDeliveryRawResponse, Is.Null);
            Assert.That(cleared.PendingInboxLinkRepairError, Is.Null);
            Assert.That(cleared.DecomposeGroupId, Is.EqualTo("PLAN-1"));
        });
    }

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
