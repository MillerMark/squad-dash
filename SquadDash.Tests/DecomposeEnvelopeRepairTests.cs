using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
public class DecomposeEnvelopeRepairTests
{
    // ── TryParse: valid input ──────────────────────────────────────────────────

    [Test]
    public void TryParse_ValidComplete_ReturnsTrue()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId": "g1",
              "taskId": "t1",
              "revision": "r1",
              "status": "complete",
              "commit": "abc1234",
              "summary": "did the work",
              "remainingWork": [],
              "verification": { "status": "passed", "command": "dotnet test", "summary": "all pass" }
            }
            """;
        Assert.That(DecomposeStepResultParser.TryParse(text, out var result, out var error), Is.True);
        Assert.That(error, Is.Null);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.GroupId, Is.EqualTo("g1"));
    }

    [Test]
    public void TryParse_BareObjectNormalizesNullCollectionsAndStatusCasing()
    {
        var text = """
            ```json
            {
              "groupId": "g1", "taskId": "t1", "revision": "r1",
              "status": " Complete ", "commit": "abc1234", "summary": "did the work",
              "remainingWork": null, "deferredWork": null,
              "verification": { "status": "Passed", "command": "dotnet test", "summary": "all pass" }
            }
            ```
            """;

        var parsed = DecomposeStepResultParser.TryParse(text, out var result, out var error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True, error);
            Assert.That(result!.Status, Is.EqualTo("complete"));
            Assert.That(result.RemainingWork, Is.Empty);
            Assert.That(result.DeferredWork, Is.Empty);
            Assert.That(result.Verification!.Status, Is.EqualTo("passed"));
        });
    }

    [Test]
    public void TryParse_DeferredWorkRequiresNamedOwner()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId": "g1", "taskId": "t1", "revision": "r1", "status": "complete",
              "commit": "abc1234", "summary": "did the work", "remainingWork": [],
              "deferredWork": [{"requirement":"Later wiring","reason":"Later task","ownerTaskIds":[]}],
              "verification": { "status": "passed", "command": "dotnet test", "summary": "all pass" }
            }
            """;

        Assert.That(DecomposeStepResultParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("at least one named owner task"));
    }

    [Test]
    public void TryParse_ProofEvidenceWithDetailInsteadOfSummary_ReportsSchemaError()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId": "g1", "taskId": "t1", "revision": "r1", "status": "complete",
              "commit": "abc1234", "summary": "did the work", "remainingWork": [],
              "verification": { "status": "passed", "command": "dotnet test", "summary": "all pass" },
              "proofEvidence": [
                { "requirementId": "build", "proofType": "build", "detail": "0 errors" }
              ]
            }
            """;

        Assert.That(DecomposeStepResultParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Is.EqualTo("Proof evidence requires requirementId, proofType, and summary."));
    }

    [Test]
    public void TryParse_MissingGroupId_ReturnsFalse()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "taskId": "t1",
              "revision": "r1",
              "status": "complete",
              "commit": "abc1234",
              "summary": "did the work",
              "remainingWork": [],
              "verification": { "status": "passed", "command": "dotnet test", "summary": "all pass" }
            }
            """;
        Assert.That(DecomposeStepResultParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public void TryParse_MissingSummary_ReturnsFalse()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId": "g1",
              "taskId": "t1",
              "revision": "r1",
              "status": "complete",
              "commit": "abc1234",
              "remainingWork": [],
              "verification": { "status": "passed", "command": "dotnet test", "summary": "all pass" }
            }
            """;
        Assert.That(DecomposeStepResultParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public void TryParse_CompleteStatusWithoutCommit_ReturnsFalse()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId": "g1",
              "taskId": "t1",
              "revision": "r1",
              "status": "complete",
              "summary": "did the work",
              "remainingWork": [],
              "verification": { "status": "passed", "command": "dotnet test", "summary": "all pass" }
            }
            """;
        Assert.That(DecomposeStepResultParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public void TryParse_CompleteStatusWithoutPassedVerification_ReturnsFalse()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId": "g1",
              "taskId": "t1",
              "revision": "r1",
              "status": "complete",
              "commit": "abc1234",
              "summary": "did the work",
              "remainingWork": [],
              "verification": { "status": "failed", "command": "dotnet test", "summary": "some fail" }
            }
            """;
        Assert.That(DecomposeStepResultParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public void TryParse_PartialStatusWithoutRemainingWork_ReturnsFalse()
    {
        var text = """
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId": "g1",
              "taskId": "t1",
              "revision": "r1",
              "status": "partial",
              "summary": "did some work",
              "remainingWork": []
            }
            """;
        Assert.That(DecomposeStepResultParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public void TryParse_MalformedJson_ReturnsFalse()
    {
        var text = "DECOMPOSE_STEP_RESULT_JSON:\n{ this is not valid json }";
        Assert.That(DecomposeStepResultParser.TryParse(text, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public void TryParse_NullInput_ReturnsFalse()
    {
        Assert.That(DecomposeStepResultParser.TryParse(null, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Null);
    }

    // ── RepairPrompt: content checks ──────────────────────────────────────────

    [Test]
    public void RepairPrompt_ContainsGroupId()
    {
        var prompt = DecomposeEnvelopeRepairPrompt.Build("my-group", "t1", "r1", "test reason");
        Assert.That(prompt, Does.Contain("my-group"));
    }

    [Test]
    public void RepairPrompt_ContainsTaskId()
    {
        var prompt = DecomposeEnvelopeRepairPrompt.Build("g1", "my-task", "r1", "test reason");
        Assert.That(prompt, Does.Contain("my-task"));
    }

    [Test]
    public void RepairPrompt_ContainsRevision()
    {
        var prompt = DecomposeEnvelopeRepairPrompt.Build("g1", "t1", "my-revision", "test reason");
        Assert.That(prompt, Does.Contain("my-revision"));
    }

    [Test]
    public void RepairPrompt_ContainsReason()
    {
        var prompt = DecomposeEnvelopeRepairPrompt.Build("g1", "t1", "r1", "the specific reason");
        Assert.That(prompt, Does.Contain("the specific reason"));
    }

    [Test]
    public void RepairPrompt_SchemaInvalid_ExplainsCorrectionWithoutRepeatingWork()
    {
        var prompt = DecomposeEnvelopeRepairPrompt.Build(
            "g1",
            "t1",
            "r1",
            "Proof evidence requires requirementId, proofType, and summary.",
            envelopeWasPresent: true);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("did not match the required schema"));
            Assert.That(prompt, Does.Contain("Proof evidence requires requirementId, proofType, and summary."));
            Assert.That(prompt, Does.Contain("Correct the validation error"));
            Assert.That(prompt, Does.Contain("Do not re-run any tools"));
        });
    }

    [Test]
    public void ProofEvidenceCorrection_ShowsActualResultSchemaWithRequiredSummary()
    {
        var prompt = DecomposeEnvelopeRepairPrompt.BuildProofEvidenceCorrection(
        [
            new DecomposedTaskProofRequirement(
                "modelprof-build-007",
                "build",
                "Project builds without errors."),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("\"requirementId\":\"modelprof-build-007\""));
            Assert.That(prompt, Does.Contain("\"proofType\":\"build\""));
            Assert.That(prompt, Does.Contain("\"summary\":"));
            Assert.That(prompt, Does.Contain("`summary` is required"));
            Assert.That(prompt, Does.Not.Contain("\"description\":"));
            Assert.That(prompt, Does.Not.Contain("\"detail\":"));
            Assert.That(prompt, Does.Not.Contain("\"passed\":"));
        });
    }
}
