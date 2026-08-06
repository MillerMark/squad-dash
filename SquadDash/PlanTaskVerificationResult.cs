using System.Text.Json.Serialization;

namespace SquadDash;

internal static class PlanTaskVerificationVerdict
{
    internal const string Accepted = "accepted";
    internal const string ReworkRequired = "rework-required";
    internal const string HumanReviewRequired = "human-review-required";
}

internal enum PlanTaskVerificationNextAction
{
    Accept,
    AutomaticRework,
    HumanReview,
}

internal static class PlanTaskVerificationRecoveryPolicy
{
    internal static PlanTaskVerificationNextAction Resolve(string verdict, int completedAutomaticReworks) =>
        verdict switch
        {
            PlanTaskVerificationVerdict.Accepted => PlanTaskVerificationNextAction.Accept,
            PlanTaskVerificationVerdict.ReworkRequired when completedAutomaticReworks < 1 =>
                PlanTaskVerificationNextAction.AutomaticRework,
            _ => PlanTaskVerificationNextAction.HumanReview,
        };
}

internal sealed record PlanTaskVerificationResult(
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("taskId")] string TaskId,
    [property: JsonPropertyName("revision")] string Revision,
    [property: JsonPropertyName("evaluatedCommit")] string EvaluatedCommit,
    [property: JsonPropertyName("verdict")] string Verdict,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("claimFindings")] IReadOnlyList<PlanTaskVerificationFinding> ClaimFindings,
    [property: JsonPropertyName("missingOrOverstatedWork")] IReadOnlyList<string> MissingOrOverstatedWork,
    [property: JsonPropertyName("testAssessment")] string TestAssessment,
    [property: JsonPropertyName("reworkInstructions")] IReadOnlyList<string> ReworkInstructions);

internal static class PlanTaskVerificationResultParser
{
    internal const string Marker = "PLAN_TASK_VERIFICATION_JSON:";
    internal const string LegacyMarker = "PLAN_TASK_SCRUTINY_JSON:";

    internal static bool TryParse(string? text, out PlanTaskVerificationResult? result, out string? error)
    {
        result = null;
        error = null;
        var extracted = StructuredJsonBlockParser.TryExtractObject<PlanTaskVerificationResult>(
            text, Marker, out var extraction) ||
            StructuredJsonBlockParser.TryExtractObject<PlanTaskVerificationResult>(
                text, LegacyMarker, out extraction) ||
            StructuredJsonBlockParser.TryExtractSingleObject<PlanTaskVerificationResult>(text, out extraction);
        if (!extracted || extraction?.Payload is not { } parsed)
        {
            error = $"The response did not contain a valid {Marker} payload.";
            return false;
        }

        // Normalize representation-only model variations at the protocol boundary. The verdict
        // consistency checks below still reject missing decision-critical evidence.
        parsed = parsed with
        {
            Verdict = parsed.Verdict?.Trim().ToLowerInvariant() ?? string.Empty,
            ClaimFindings = (parsed.ClaimFindings ?? [])
                .Where(finding => finding is not null)
                .ToArray(),
            MissingOrOverstatedWork = (parsed.MissingOrOverstatedWork ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToArray(),
            ReworkInstructions = (parsed.ReworkInstructions ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToArray(),
        };

        if (string.IsNullOrWhiteSpace(parsed.PlanId) || string.IsNullOrWhiteSpace(parsed.TaskId) ||
            string.IsNullOrWhiteSpace(parsed.Revision) || string.IsNullOrWhiteSpace(parsed.EvaluatedCommit) ||
            string.IsNullOrWhiteSpace(parsed.Summary) || string.IsNullOrWhiteSpace(parsed.TestAssessment))
        {
            error = "The verification result omitted its scope, evaluated commit, summary, or test assessment.";
            return false;
        }

        if (parsed.Verdict is not (PlanTaskVerificationVerdict.Accepted or
                                   PlanTaskVerificationVerdict.ReworkRequired or
                                   PlanTaskVerificationVerdict.HumanReviewRequired))
        {
            error = "The verification verdict must be accepted, rework-required, or human-review-required.";
            return false;
        }

        if (parsed.Verdict == PlanTaskVerificationVerdict.Accepted && parsed.MissingOrOverstatedWork.Count > 0)
        {
            error = "An accepted verification result cannot contain missing or overstated work.";
            return false;
        }

        if (parsed.Verdict == PlanTaskVerificationVerdict.ReworkRequired && parsed.ReworkInstructions.Count == 0)
        {
            error = "A rework-required verification result must include actionable rework instructions.";
            return false;
        }

        result = parsed;
        return true;
    }
}
