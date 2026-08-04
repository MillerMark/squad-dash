using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanStepProofPolicyTests
{
    private static DecomposedSubTask TaskWithProof(string proofType = "live-ui-observation") => new(
        Id: "PROOF-20260803-001",
        Description: "Exercise the running UI.",
        DependsOn: [],
        Priority: "high",
        Title: "Exercise UI",
        AgentRoutingMode: "generic",
        GenericAgentReason: "Test fixture.",
        ProofRequirements:
        [
            new DecomposedTaskProofRequirement("live-ui", proofType, "Observe the rendered UI."),
        ]);

    private static DecomposeStepResult Result(
        IReadOnlyList<DecomposeStepProofEvidence>? evidence) => new(
        "PROOF-20260803", "PROOF-20260803-001", "revision", "complete", "abc1234",
        "Finished", [], new DecomposeStepVerification("passed", "test", "passed"),
        ProofEvidence: evidence);

    [Test]
    public void CompleteResult_MissingDeclaredProof_IsRejected()
    {
        var error = PlanStepProofPolicy.Validate(TaskWithProof(), Result(null));
        Assert.That(error, Does.Contain("requires structured proof evidence"));
    }

    [Test]
    public void AutomatedTest_CannotSatisfyLiveObservationContract()
    {
        var evidence = new DecomposeStepProofEvidence(
            "live-ui", "automated-test", "Twenty headless tests passed.");
        var error = PlanStepProofPolicy.Validate(TaskWithProof(), Result([evidence]));
        Assert.That(error, Does.Contain("requires live-ui-observation"));
    }

    [Test]
    public void LiveObservation_WithoutDurableArtifact_IsRejected()
    {
        var evidence = new DecomposeStepProofEvidence(
            "live-ui", "live-ui-observation", "I saw the window update.");
        var error = PlanStepProofPolicy.Validate(TaskWithProof(), Result([evidence]));
        Assert.That(error, Does.Contain("requires a durable artifact"));
    }

    [Test]
    public void ExactStructuredProof_IsAcceptedAndRoundTrips()
    {
        var result = Result([
            new DecomposeStepProofEvidence(
                "live-ui", "live-ui-observation", "Observed the open viewer update without restart.",
                ["trace:plan-viewer-live-update"]),
        ]);

        Assert.That(PlanStepProofPolicy.Validate(TaskWithProof(), result), Is.Null);
        var roundTrip = JsonSerializer.Deserialize<DecomposeStepResult>(JsonSerializer.Serialize(result));
        Assert.That(roundTrip?.ProofEvidence?.Single().Artifacts,
            Is.EqualTo(new[] { "trace:plan-viewer-live-update" }));
    }
}
