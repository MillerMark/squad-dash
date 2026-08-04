using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanProofContractParserTests
{
    private static DecomposedTaskGroup MakeGroup(bool includeAudit)
    {
        var task = new DecomposedSubTask(
            "PROOF-20260803-001",
            "Run a rendered UI scenario and observe the result.",
            [], "high", "Run live scenario",
            AgentRoutingMode: "generic",
            GenericAgentReason: "Test fixture.",
            ProofRequirements:
            [
                new DecomposedTaskProofRequirement(
                    "live-ui", "live-ui-observation", "Observe the rendered UI."),
            ]);
        var audits = includeAudit
            ? new[]
            {
                new DecomposedValidationNode(
                    "PROOF-20260803-VAL-001", "Audit completion", "Compare actual proof to the contract.",
                    [task.Id], [], ["The live UI was genuinely observed."], Mode: "audit"),
            }
            : null;
        return new DecomposedTaskGroup(
            "PROOF-20260803", "Proof Contract", "feature/proof-contract",
            "Require typed proof and a final audit.", [task], Validations: audits);
    }

    [Test]
    public void ProofBearingPlan_WithoutFinalAudit_IsRejected()
    {
        var text = "TASKS_JSON:\n" + JsonSerializer.Serialize(MakeGroup(false));
        Assert.That(TasksJsonParser.TryParse(text, out _), Is.False);
    }

    [Test]
    public void ProofBearingPlan_WithLeafCoveringAudit_IsAccepted()
    {
        var text = "TASKS_JSON:\n" + JsonSerializer.Serialize(MakeGroup(true));
        Assert.That(TasksJsonParser.TryParse(text, out var parsed), Is.True);
        Assert.That(parsed?.Validations?.Single().Mode, Is.EqualTo("audit"));
    }
}
