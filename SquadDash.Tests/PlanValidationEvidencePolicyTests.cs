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

    private static PlanValidationResultPayload Result(IReadOnlyList<PlanAssertionEvidence> evidence) =>
        new(
            Validation.ValidationId,
            "PROOF-20260803",
            true,
            "Audit complete.",
            evidence,
            "0123456789abcdef");
}
