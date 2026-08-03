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
        string? ValidatedCommit = null);

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
        if (!StructuredJsonBlockParser.TryExtractObject<PlanValidationResultPayload>(
                text, Marker, out var extraction) || extraction is null)
        {
            error = $"The response did not contain a valid {Marker} payload.";
            return false;
        }
        result = extraction.Payload;

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
}
