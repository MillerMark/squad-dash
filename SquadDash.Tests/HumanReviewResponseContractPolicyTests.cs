using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class HumanReviewResponseContractPolicyTests
{
    [TestCase(HumanReviewResponseContractPolicy.ClarificationDraftSourceTag)]
    [TestCase(HumanReviewResponseContractPolicy.WordingDraftSourceTag)]
    [TestCase(HumanReviewResponseContractPolicy.RepairSourceTag)]
    public void SpecializedHumanReviewTurn_RequiresClarificationContract(string sourceTag)
    {
        Assert.That(
            HumanReviewResponseContractPolicy.RequiresClarificationProtocol(sourceTag),
            Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("plan-review-correction-draft")]
    [TestCase("plan-continuation")]
    public void OtherTurn_DoesNotRequireClarificationContract(string? sourceTag)
    {
        Assert.That(
            HumanReviewResponseContractPolicy.RequiresClarificationProtocol(sourceTag),
            Is.False);
    }

    [Test]
    public void SpecializedHumanReviewPointer_OmitsCompetingReviewActivityProtocol()
    {
        var pointer = DecomposePlanningInstructions.BuildOrdinaryPromptPointer(
            ".squad/instructions/decompose-planning.md",
            includePlanReviewActivityProtocol: false);

        Assert.That(pointer, Does.Not.Contain(PlanReviewActivityResponseParser.Marker));
        Assert.That(pointer, Does.Contain("PLAN_GATE_RESPONSE_JSON"));
    }
}
