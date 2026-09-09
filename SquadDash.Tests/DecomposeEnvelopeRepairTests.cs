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
        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("my-revision"));
            Assert.That(prompt, Does.Contain("authoritative current revision `my-revision`"));
            Assert.That(prompt, Does.Contain("Do not reuse a revision from earlier conversation or review context"));
        });
    }

    [Test]
    public void RepairPrompt_ContainsReason()
    {
        var prompt = DecomposeEnvelopeRepairPrompt.Build("g1", "t1", "r1", "the specific reason");
        Assert.That(prompt, Does.Contain("the specific reason"));
    }

    [Test]
    public void RepairPrompt_NoNewCommit_OffersEvidenceBackedVerifiedNoOp()
    {
        var prompt = DecomposeEnvelopeRepairPrompt.Build(
            "g1", "t1", "r1", "The step reported completion without creating a new commit.",
            envelopeWasPresent: true);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("\"completionKind\": \"verified-no-op\""));
            Assert.That(prompt, Does.Contain("\"commit\": null"));
            Assert.That(prompt, Does.Contain("\"evaluatedCommit\""));
            Assert.That(prompt, Does.Contain("\"noOpEvidence\""));
            Assert.That(prompt, Does.Contain("do not claim verified-no-op"));
            Assert.That(prompt, Does.Contain("independent Fact Checker"));
        });
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
            Assert.That(prompt, Does.Contain("`completionKind` is only valid when `status` is `complete`"));
            Assert.That(prompt, Does.Contain("For `partial` or `failed`, omit `completionKind`"));
        });
    }

    [Test]
    public void RepairPrompt_GenericSchemaRepair_PreservesExecutionAttemptWithoutAgentExecutions()
    {
        var prompt = DecomposeEnvelopeRepairPrompt.Build(
            "g1",
            "t1",
            "r1",
            "completionKind is only valid for a complete result.",
            envelopeWasPresent: true,
            executionAttemptId: "attempt-generic",
            previousStatus: "failed");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("\"executionAttemptId\": \"attempt-generic\""));
            Assert.That(prompt, Does.Not.Contain("\"agentExecutions\":"));
            Assert.That(prompt, Does.Contain("Preserve the `executionAttemptId` field shown in the corrected envelope"));
            Assert.That(prompt, Does.Contain("Do not add `agentExecutions` unless the corrected envelope schema above includes it"));
            Assert.That(prompt, Does.Contain("\"status\": \"failed\""));
            Assert.That(prompt, Does.Not.Contain("\"completionKind\":"));
            Assert.That(prompt, Does.Contain("Do not re-run tools"));
        });
    }

    [Test]
    public void RepairPrompt_VerificationOnlySchemaRepair_DoesNotSuggestChangedCommit()
    {
        var prompt = DecomposeEnvelopeRepairPrompt.Build(
            "g1",
            "t1",
            "r1",
            "Proof evidence requires requirementId, proofType, and summary.",
            envelopeWasPresent: true,
            executionAttemptId: "attempt-verification",
            verificationOnly: true);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("\"completionKind\": \"verified-no-op\""));
            Assert.That(prompt, Does.Contain("\"commit\": null"));
            Assert.That(prompt, Does.Contain("\"executionAttemptId\": \"attempt-verification\""));
            Assert.That(prompt, Does.Not.Contain("\"completionKind\": \"changed\""));
        });
    }

    [Test]
    public void RepairPrompt_AssignedSchemaRepair_PreservesExecutionAttemptWrapUp()
    {
        var prompt = DecomposeEnvelopeRepairPrompt.Build(
            "g1",
            "t1",
            "r1",
            "Proof evidence requires requirementId, proofType, and summary.",
            envelopeWasPresent: true,
            executionAttemptId: "attempt-123",
            assignments: [new DecomposedAgentAssignment("brucebanner", "QA")]);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("\"executionAttemptId\": \"attempt-123\""));
            Assert.That(prompt, Does.Contain("\"agentExecutions\":"));
            Assert.That(prompt, Does.Contain("\"requestedAgent\":\"brucebanner\""));
            Assert.That(prompt, Does.Contain("\"actualPrimaryAgent\":\"brucebanner\""));
            Assert.That(prompt, Does.Contain("already has host-observed completion for brucebanner"));
            Assert.That(prompt, Does.Contain("will reject the repair if they are omitted or changed"));
        });
    }

    [Test]
    public void RepairPrompt_AssignedVerifiedNoOpRepair_PreservesExecutionAttemptWrapUp()
    {
        var prompt = DecomposeEnvelopeRepairPrompt.Build(
            "g1",
            "t1",
            "r1",
            "The step reported completion without creating a new commit.",
            envelopeWasPresent: true,
            executionAttemptId: "attempt-456",
            assignments:
            [
                new DecomposedAgentAssignment("brucebanner", "QA"),
                new DecomposedAgentAssignment("shuri", "AiGen"),
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("\"completionKind\": \"verified-no-op\""));
            Assert.That(prompt, Does.Contain("\"executionAttemptId\": \"attempt-456\""));
            Assert.That(prompt, Does.Contain("\"requestedAgent\":\"brucebanner\""));
            Assert.That(prompt, Does.Contain("\"requestedAgent\":\"shuri\""));
            Assert.That(prompt, Does.Contain("Preserve the `executionAttemptId` and `agentExecutions` fields"));
        });
    }

    [Test]
    public void RepairPrompt_GenericVerifiedNoOpRepair_PreservesExecutionAttemptWithoutAgentExecutions()
    {
        var prompt = DecomposeEnvelopeRepairPrompt.Build(
            "g1",
            "t1",
            "r1",
            "The step reported completion without creating a new commit.",
            envelopeWasPresent: true,
            executionAttemptId: "attempt-generic");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("\"completionKind\": \"verified-no-op\""));
            Assert.That(prompt, Does.Contain("\"executionAttemptId\": \"attempt-generic\""));
            Assert.That(prompt, Does.Not.Contain("\"agentExecutions\":"));
            Assert.That(prompt, Does.Contain("Preserve the `executionAttemptId` field shown in the corrected envelope"));
        });
    }

    [Test]
    public void RepairPrompt_UnresolvableCommit_AllowsOnlyReadOnlyHeadLookup()
    {
        var prompt = DecomposeEnvelopeRepairPrompt.Build(
            "g1",
            "t1",
            "r1",
            "Git validation failed: fatal: Needed a single revision.",
            envelopeWasPresent: true);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("The reported commit did not resolve"));
            Assert.That(prompt, Does.Contain("git rev-parse --short HEAD"));
            Assert.That(prompt, Does.Contain("do not inspect or change any files"));
            Assert.That(prompt, Does.Not.Contain("Do not re-run any tools"));
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

    [Test]
    public void InboxLinkCorrection_ReEmitsDeliveryWithoutRepeatingImplementation()
    {
        var prompt = DecomposeEnvelopeRepairPrompt.BuildInboxLinkCorrection(
            "Resolved path did not exist: C:\\repo\\Package\\Plugins\\Sample.sln");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("INBOX_MESSAGE_JSON or INBOX_MESSAGE_JSON_FILE"));
            Assert.That(prompt, Does.Contain("active SquadDash workspace directory"));
            Assert.That(prompt, Does.Contain("not from the Git repository root"));
            Assert.That(prompt, Does.Contain("use its absolute path"));
            Assert.That(prompt, Does.Contain("confirm each resolved file or folder exists"));
            Assert.That(prompt, Does.Contain("Do not repeat implementation work"));
        });
    }
}
