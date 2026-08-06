using System.Text.Json.Serialization;

namespace SquadDash;

internal static class PlanTaskScrutinyVerdict
{
    internal const string Accepted = "accepted";
    internal const string ReworkRequired = "rework-required";
    internal const string HumanReviewRequired = "human-review-required";
}

internal enum PlanTaskScrutinyNextAction
{
    Accept,
    AutomaticRework,
    HumanReview,
}

internal static class PlanTaskScrutinyRecoveryPolicy
{
    internal static PlanTaskScrutinyNextAction Resolve(string verdict, int completedAutomaticReworks) =>
        verdict switch
        {
            PlanTaskScrutinyVerdict.Accepted => PlanTaskScrutinyNextAction.Accept,
            PlanTaskScrutinyVerdict.ReworkRequired when completedAutomaticReworks < 1 =>
                PlanTaskScrutinyNextAction.AutomaticRework,
            _ => PlanTaskScrutinyNextAction.HumanReview,
        };
}

internal sealed record PlanTaskScrutinyResult(
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("taskId")] string TaskId,
    [property: JsonPropertyName("revision")] string Revision,
    [property: JsonPropertyName("evaluatedCommit")] string EvaluatedCommit,
    [property: JsonPropertyName("verdict")] string Verdict,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("claimFindings")] IReadOnlyList<PlanTaskScrutinyFinding> ClaimFindings,
    [property: JsonPropertyName("missingOrOverstatedWork")] IReadOnlyList<string> MissingOrOverstatedWork,
    [property: JsonPropertyName("testAssessment")] string TestAssessment,
    [property: JsonPropertyName("reworkInstructions")] IReadOnlyList<string> ReworkInstructions);

internal static class PlanTaskScrutinyResultParser
{
    internal const string Marker = "PLAN_TASK_SCRUTINY_JSON:";

    internal static bool TryParse(string? text, out PlanTaskScrutinyResult? result, out string? error)
    {
        result = null;
        error = null;
        var extracted = StructuredJsonBlockParser.TryExtractObject<PlanTaskScrutinyResult>(
            text, Marker, out var extraction) ||
            StructuredJsonBlockParser.TryExtractSingleObject<PlanTaskScrutinyResult>(text, out extraction);
        if (!extracted || extraction?.Payload is not { } parsed)
        {
            error = $"The response did not contain a valid {Marker} payload.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(parsed.PlanId) || string.IsNullOrWhiteSpace(parsed.TaskId) ||
            string.IsNullOrWhiteSpace(parsed.Revision) || string.IsNullOrWhiteSpace(parsed.EvaluatedCommit) ||
            string.IsNullOrWhiteSpace(parsed.Summary) || string.IsNullOrWhiteSpace(parsed.TestAssessment))
        {
            error = "The scrutiny result omitted its scope, evaluated commit, summary, or test assessment.";
            return false;
        }

        if (parsed.Verdict is not (PlanTaskScrutinyVerdict.Accepted or
                                   PlanTaskScrutinyVerdict.ReworkRequired or
                                   PlanTaskScrutinyVerdict.HumanReviewRequired))
        {
            error = "The scrutiny verdict must be accepted, rework-required, or human-review-required.";
            return false;
        }

        if (parsed.ClaimFindings is null || parsed.MissingOrOverstatedWork is null ||
            parsed.ReworkInstructions is null)
        {
            error = "The scrutiny result must include claimFindings, missingOrOverstatedWork, and reworkInstructions arrays.";
            return false;
        }

        if (parsed.Verdict == PlanTaskScrutinyVerdict.Accepted && parsed.MissingOrOverstatedWork.Count > 0)
        {
            error = "An accepted scrutiny result cannot contain missing or overstated work.";
            return false;
        }

        if (parsed.Verdict == PlanTaskScrutinyVerdict.ReworkRequired && parsed.ReworkInstructions.Count == 0)
        {
            error = "A rework-required scrutiny result must include actionable rework instructions.";
            return false;
        }

        result = parsed;
        return true;
    }
}
