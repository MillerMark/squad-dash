using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

/// <summary>
/// Structured validation result returned by a validation turn. Validation work never produces
/// a production commit; the <see cref="ValidatedCommit"/> field records the HEAD observed at
/// validation time for provenance, not a new commit.
/// </summary>
internal sealed record PlanValidationResultPayload(
    [property: JsonPropertyName("validationId")] string ValidationId,
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("assertionEvidence")] IReadOnlyList<PlanAssertionEvidence> AssertionEvidence,
    [property: JsonPropertyName("validatedCommit")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ValidatedCommit = null,
    [property: JsonPropertyName("outcome")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Outcome = null,
    [property: JsonPropertyName("humanProofRequirements")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<PlanTaskProofRequirement>? HumanProofRequirements = null)
{
    internal const string PassedOutcome = "passed";
    internal const string FailedOutcome = "failed";
    internal const string NeedsHumanProofOutcome = "needs-human-proof";

    internal string NormalizedOutcome => string.IsNullOrWhiteSpace(Outcome)
        ? Passed ? PassedOutcome : FailedOutcome
        : Outcome.Trim().ToLowerInvariant();

    internal bool NeedsHumanProof =>
        string.Equals(NormalizedOutcome, NeedsHumanProofOutcome, StringComparison.Ordinal);
}

/// <summary>Per-assertion structured evidence.</summary>
internal sealed record PlanAssertionEvidence(
    [property: JsonPropertyName("assertion")] string Assertion,
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("evidence")] string Evidence);

internal static class PlanValidationResultParser
{
    internal const string Marker = "PLAN_VALIDATION_RESULT_JSON:";

    internal static bool TryParse(
        string? text,
        out PlanValidationResultPayload? result,
        out string? error)
    {
        result = null;
        error = null;
        if (!StructuredJsonBlockParser.TryExtractProtocolObject<PlanValidationResultPayload>(
                text, Marker, out var extraction) || extraction is null)
        {
            error = $"The response did not contain a valid {Marker} payload.";
            return false;
        }
        result = NormalizeHumanProofRequirements(extraction.Payload);

        if (result is null || string.IsNullOrWhiteSpace(result.ValidationId) ||
            string.IsNullOrWhiteSpace(result.PlanId) || string.IsNullOrWhiteSpace(result.Summary))
        {
            error = "The validation result omitted validationId, planId, or summary.";
            return false;
        }

        if (result.AssertionEvidence is null || result.AssertionEvidence.Count == 0)
        {
            error = "The validation result must include assertion evidence.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Keeps checklist results compatible with the first published validation schema, which
    /// required an atomic question but did not show the separate display description field.
    /// The question is already the complete human-observation contract, so it is safe to use it
    /// as the display description rather than stopping an otherwise valid plan review.
    /// </summary>
    private static PlanValidationResultPayload? NormalizeHumanProofRequirements(
        PlanValidationResultPayload? payload)
    {
        if (payload?.HumanProofRequirements is not { Count: > 0 } requirements ||
            !requirements.Any(requirement =>
                string.IsNullOrWhiteSpace(requirement.Description) &&
                !string.IsNullOrWhiteSpace(requirement.Question)))
            return payload;

        return payload with
        {
            HumanProofRequirements = requirements.Select(requirement =>
                string.IsNullOrWhiteSpace(requirement.Description) &&
                !string.IsNullOrWhiteSpace(requirement.Question)
                    ? requirement with { Description = requirement.Question!.Trim() }
                    : requirement).ToArray(),
        };
    }
}

/// <summary>
/// Validates the structured result against the approved validation contract. This is exact
/// contract matching rather than a heuristic judgment about filenames or implementation shape.
/// </summary>
internal static class PlanValidationEvidencePolicy
{
    internal static string? Validate(
        PlanValidationNode validation,
        PlanValidationResultPayload result)
    {
        var expected = validation.Assertions.ToHashSet(StringComparer.Ordinal);
        var returned = result.AssertionEvidence ?? [];
        var duplicate = returned
            .GroupBy(item => item.Assertion, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            return $"Validation {validation.ValidationId} returned duplicate evidence for assertion '{duplicate.Key}'.";

        if (returned.Count != expected.Count ||
            returned.Any(item => !expected.Contains(item.Assertion)) ||
            expected.Any(assertion => !returned.Any(item =>
                string.Equals(item.Assertion, assertion, StringComparison.Ordinal))))
            return $"Validation {validation.ValidationId} must return exactly one evidence item for every approved assertion.";

        if (returned.Any(item => string.IsNullOrWhiteSpace(item.Evidence)))
            return $"Validation {validation.ValidationId} returned empty assertion evidence.";

        var allAssertionsPassed = returned.All(item => item.Passed);
        var outcome = result.NormalizedOutcome;
        if (outcome is not (PlanValidationResultPayload.PassedOutcome or
                            PlanValidationResultPayload.FailedOutcome or
                            PlanValidationResultPayload.NeedsHumanProofOutcome))
            return $"Validation {validation.ValidationId} returned unsupported outcome '{result.Outcome}'.";

        if (result.Passed != (outcome == PlanValidationResultPayload.PassedOutcome) ||
            (outcome == PlanValidationResultPayload.PassedOutcome) != allAssertionsPassed)
            return $"Validation {validation.ValidationId} overall status does not match its assertion results.";

        var humanRequirements = result.HumanProofRequirements ?? [];
        if (outcome == PlanValidationResultPayload.NeedsHumanProofOutcome)
        {
            var humanProofError = ValidateHumanProofRequirements(validation, humanRequirements);
            if (humanProofError is not null)
                return humanProofError;
        }
        else if (humanRequirements.Count > 0)
        {
            return $"Validation {validation.ValidationId} returned human-proof requirements without the needs-human-proof outcome.";
        }

        if (string.IsNullOrWhiteSpace(result.ValidatedCommit))
            return $"Validation {validation.ValidationId} did not identify the commit it evaluated.";

        return null;
    }

    private static string? ValidateHumanProofRequirements(
        PlanValidationNode validation,
        IReadOnlyList<PlanTaskProofRequirement> requirements)
    {
        if (requirements.Count == 0)
            return $"Validation {validation.ValidationId} needs human proof but returned no review checklist.";
        if (requirements.Count > 7)
            return $"Validation {validation.ValidationId} returned more than seven human review checks.";
        if (requirements.Any(requirement =>
                string.IsNullOrWhiteSpace(requirement.RequirementId) ||
                string.IsNullOrWhiteSpace(requirement.Description) ||
                string.IsNullOrWhiteSpace(requirement.Question)))
            return $"Validation {validation.ValidationId} returned an incomplete human review check.";
        if (requirements.GroupBy(requirement => requirement.RequirementId, StringComparer.Ordinal)
                .Any(group => group.Count() > 1))
            return $"Validation {validation.ValidationId} returned duplicate human review requirement IDs.";
        if (requirements.Any(requirement =>
                !PlanProofCapabilityPolicy.IsHumanOnly(requirement.ProofType)))
            return $"Validation {validation.ValidationId} may request only human-observation proof at a review checkpoint.";
        if (requirements.Any(requirement =>
                PlanHumanReviewChecklist.SplitLegacyQuestions(requirement.Question!).Count != 1))
            return $"Validation {validation.ValidationId} must return one atomic question per human review check.";

        foreach (var requirement in requirements)
        {
            var mode = requirement.AccessMode?.Trim().ToLowerInvariant();
            if (mode is not (HumanVerificationAccessMode.DirectlyReachable or
                             HumanVerificationAccessMode.Recipe))
                return $"Validation {validation.ValidationId} human review checks must be directly reachable or include a recipe.";
            if (mode == HumanVerificationAccessMode.Recipe &&
                (requirement.VerificationRecipe is null ||
                 string.IsNullOrWhiteSpace(requirement.VerificationRecipe.Setup) ||
                 string.IsNullOrWhiteSpace(requirement.VerificationRecipe.Open) ||
                 string.IsNullOrWhiteSpace(requirement.VerificationRecipe.Observe) ||
                 string.IsNullOrWhiteSpace(requirement.VerificationRecipe.Cleanup)))
                return $"Validation {validation.ValidationId} recipe checks require Setup, Open, Observe, and Cleanup instructions.";
        }

        return null;
    }
}

/// <summary>Recognizes pre-outcome audit results that explicitly stopped only for missing human proof.</summary>
internal static class PlanValidationHumanProofRecoveryPolicy
{
    internal static bool IsLegacyHumanProofOnlyFailure(PlanValidationNode? validation)
    {
        if (validation?.Status != PlanValidationStatus.Failed) return false;
        var text = string.Join(" ", new[] { validation.Summary }
            .Concat(validation.Evidence ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return text.Contains("human-proof checkpoint", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("human approval checkpoint", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("human checkpoint evidence", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("human review check", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("review checklist", StringComparison.OrdinalIgnoreCase);
    }
}
