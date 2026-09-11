namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanProposalTranscriptPolicyTests
{
    [TestCase("plan-visualizer-review")]
    [TestCase("plan-human-proof-coverage")]
    [TestCase("decompose-repair")]
    public void InternalPlanTurns_AreSuppressedFromConversation(string sourceTag)
    {
        Assert.That(
            PlanProposalTranscriptPolicy.ShouldSuppressInternalTurn(sourceTag),
            Is.True);
    }

    [Test]
    public void OrdinaryTurns_RemainVisible()
    {
        Assert.That(
            PlanProposalTranscriptPolicy.ShouldSuppressInternalTurn("user-follow-up"),
            Is.False);
    }

    [Test]
    public void ValidatedSummary_IsDerivedFromPersistedPlan()
    {
        var group = new DecomposedTaskGroup(
            "PLAN-20260911",
            "Validated plan title",
            "feature/validated-plan",
            "Authoritative summary from the parsed plan.",
            [
                new DecomposedSubTask("PLAN-20260911-001", "First", [], "high"),
                new DecomposedSubTask("PLAN-20260911-002", "Second", ["PLAN-20260911-001"], "high"),
            ],
            ApprovalGates: [new DecomposedGate("PLAN-20260911-GATE-001", "Review")],
            Validations:
            [
                new DecomposedValidationNode(
                    "PLAN-20260911-VAL-001",
                    "Validate",
                    "Validate the result.",
                    ["PLAN-20260911-002"],
                    [],
                    ["The result is correct."])
            ]);
        var plan = new PendingDecomposePlan("revision-123", group);

        var summary = PlanProposalTranscriptPolicy.BuildValidatedSummary(plan);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Heading, Is.EqualTo("✓ Plan “Validated plan title” is ready for approval."));
            Assert.That(summary.Summary, Is.EqualTo("Authoritative summary from the parsed plan."));
            Assert.That(summary.Counts, Is.EqualTo("2 tasks · 1 approval checkpoint · 1 validation"));
            Assert.That(summary.Branch, Is.EqualTo("feature/validated-plan"));
            Assert.That(summary.Identity, Is.EqualTo("Plan ID: PLAN-20260911 · Revision: revision-123"));
        });
    }
}
