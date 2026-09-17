using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanHumanProofTitlePolicyTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("Does the inactive target update?")]
    public void ValidateAiAuthoredPlan_RejectsMissingOrQuestionShapedTitle(string? title)
    {
        var group = BuildGroup(title, "Does the inactive target update?");

        var diagnostics = PlanHumanProofTitlePolicy.ValidateAiAuthoredPlan(group);

        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void ValidateAiAuthoredPlan_AcceptsConciseDistinctScenarioTitle()
    {
        var group = BuildGroup("Inactive Razor target routing", "Does the inactive target update?");

        Assert.That(PlanHumanProofTitlePolicy.ValidateAiAuthoredPlan(group), Is.Empty);
    }

    [Test]
    public void ResolveDisplayTitle_LegacyRequirementMechanicallyHumanizesId()
    {
        Assert.That(
            PlanHumanReviewChecklist.ResolveDisplayTitle(null, "native-inactive-target"),
            Is.EqualTo("Native inactive target"));
    }

    private static DecomposedTaskGroup BuildGroup(string? title, string question) =>
        new(
            "PLAN-1",
            "Plan",
            "main",
            "Summary",
            [],
            ApprovalGates:
            [
                new DecomposedGate(
                    "GATE-1",
                    "Review",
                    [],
                    [],
                    [
                        new DecomposedTaskProofRequirement(
                            "native-inactive-target",
                            "live-ui-observation",
                            "Observe routing.",
                            question,
                            Title: title),
                    ],
                    "Do you approve?")
            ]);
}
