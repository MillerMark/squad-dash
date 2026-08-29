namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanValidationEvidencePolicyTests
{
    private static readonly PlanValidationNode Validation = new(
        "PROOF-20260803-VAL-001",
        "Completion audit",
        "Audit every approved assertion.",
        ["PROOF-20260803-001"],
        [],
        ["The live viewer updated.", "The restart preserved the result."],
        null,
        "audit",
        null,
        true,
        PlanValidationStatus.Validating);

    [Test]
    public void MissingApprovedAssertion_IsRejected()
    {
        var result = Result([
            new PlanAssertionEvidence("The live viewer updated.", true, "Observed."),
        ]);

        Assert.That(PlanValidationEvidencePolicy.Validate(Validation, result),
            Does.Contain("exactly one evidence item"));
    }

    [Test]
    public void OverallPass_MustAgreeWithEveryAssertion()
    {
        var result = Result([
            new PlanAssertionEvidence("The live viewer updated.", true, "Observed."),
            new PlanAssertionEvidence("The restart preserved the result.", false, "Not observed."),
        ]);

        Assert.That(PlanValidationEvidencePolicy.Validate(Validation, result),
            Does.Contain("overall status"));
    }

    [Test]
    public void ExactCompleteEvidence_IsAccepted()
    {
        var result = Result([
            new PlanAssertionEvidence("The live viewer updated.", true, "Observed."),
            new PlanAssertionEvidence("The restart preserved the result.", true, "Observed after restart."),
        ]);

        Assert.That(PlanValidationEvidencePolicy.Validate(Validation, result), Is.Null);
    }

    [Test]
    public void NeedsHumanProof_WithAtomicLiveObservation_IsAccepted()
    {
        var result = new PlanValidationResultPayload(
            Validation.ValidationId,
            "PROOF-20260803",
            false,
            "Automated proof passed; live restart confirmation remains.",
            [
                new PlanAssertionEvidence("The live viewer updated.", true, "Automated contract passed."),
                new PlanAssertionEvidence("The restart preserved the result.", false,
                    "Requires direct observation after restart."),
            ],
            "0123456789abcdef",
            PlanValidationResultPayload.NeedsHumanProofOutcome,
            [new PlanTaskProofRequirement(
                "restart-retained",
                "restart-observation",
                "The selected value remains after restart.",
                "After restarting, does the selected value remain visible?",
                HumanVerificationAccessMode.Recipe,
                new HumanVerificationRecipe(
                    "Select a non-default value and save.",
                    "Restart SquadDash and reopen Options.",
                    "Confirm the same value remains selected.",
                    "Restore the original value."))]);

        Assert.That(PlanValidationEvidencePolicy.Validate(Validation, result), Is.Null);
    }

    [Test]
    public void NeedsHumanProof_RejectsMissingChecklist()
    {
        var result = new PlanValidationResultPayload(
            Validation.ValidationId,
            "PROOF-20260803",
            false,
            "Human observation remains.",
            [
                new PlanAssertionEvidence("The live viewer updated.", true, "Automated contract passed."),
                new PlanAssertionEvidence("The restart preserved the result.", false, "Not observed."),
            ],
            "0123456789abcdef",
            PlanValidationResultPayload.NeedsHumanProofOutcome,
            []);

        Assert.That(PlanValidationEvidencePolicy.Validate(Validation, result),
            Does.Contain("no review checklist"));
    }

    [Test]
    public void LegacyHumanCheckpointFailure_IsRecognizedForChecklistRecovery()
    {
        var failed = Validation with
        {
            Status = PlanValidationStatus.Failed,
            Summary = "The global assertion fails because live UI outcomes are not backed by approved human-proof checkpoint evidence.",
        };

        Assert.That(PlanValidationHumanProofRecoveryPolicy.IsLegacyHumanProofOnlyFailure(failed), Is.True);
    }

    private static PlanValidationResultPayload Result(IReadOnlyList<PlanAssertionEvidence> evidence) =>
        new(
            Validation.ValidationId,
            "PROOF-20260803",
            true,
            "Audit complete.",
            evidence,
            "0123456789abcdef");
}
