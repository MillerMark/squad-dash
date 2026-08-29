using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed record PlanHumanProofCoverageIssue(
    [property: JsonPropertyName("targetId")] string TargetId,
    [property: JsonPropertyName("observableOutcome")] string ObservableOutcome,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("recommendation")] string Recommendation);

internal sealed record PlanHumanProofCoverageReviewPayload(
    [property: JsonPropertyName("groupId")] string GroupId,
    [property: JsonPropertyName("baseRevision")] string BaseRevision,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("issues")] IReadOnlyList<PlanHumanProofCoverageIssue> Issues);

/// <summary>
/// Focused semantic preflight for the one plan constraint a JSON schema cannot infer: whether
/// prose promises a human-observable outcome without placing human proof at the right boundary.
/// </summary>
internal static class PlanHumanProofCoverageReview
{
    internal const string Marker = "PLAN_HUMAN_PROOF_COVERAGE_JSON:";

    internal static string BuildPrompt(PendingDecomposePlan plan) => $$"""
        Perform one focused plan-contract preflight. Do not implement, revise, or broadly critique
        the plan. Determine only whether every promised outcome that inherently requires a person
        to interact with or observe the live product has a matching human-only proof requirement
        in an approval gate at the correct boundary.

        Read the isolated revision-locked candidate definition from:
        .squad/tmp/decompose/preflight/{{plan.Group.GroupId}}.json

        Inspect task descriptions, observable outcomes, validation assertions, and proof contracts.
        Live UI behavior, restart behavior, focus, window state, notification appearance, timing as
        perceived by a person, and interactive progress affordances require human proof unless the
        plan declares a truthful automated substitute for the exact outcome. Code review, AI review,
        source inspection, and headless tests do not establish direct human observation. Do not ask
        for human proof when an outcome is completely and truthfully automated. A matching checkpoint
        must occur after the work that creates the behavior and before a final audit that claims it.

        Return exactly one block:

        {{Marker}}
        {
          "groupId": "{{plan.Group.GroupId}}",
          "baseRevision": "{{plan.Revision}}",
          "status": "ready | revision-required",
          "summary": "one short focused assessment",
          "issues": [
            {
              "targetId": "canonical task, validation, or plan ID",
              "observableOutcome": "the exact uncovered human-observable promise",
              "reason": "why existing approved proof cannot establish it",
              "recommendation": "the checkpoint and atomic observation the planner must add"
            }
          ]
        }

        Use revision-required only when at least one human-observable promise is uncovered. Use ready
        with an empty issues array otherwise. Do not report implementation-quality, architecture,
        naming, style, or ordinary automated-test findings in this focused preflight.
        """;

    internal static bool TryParse(
        string? response,
        out PlanHumanProofCoverageReviewPayload? payload,
        out string? error)
    {
        payload = null;
        error = null;
        if (!StructuredJsonBlockParser.TryExtractProtocolObject<PlanHumanProofCoverageReviewPayload>(
                response, Marker, out var extraction) || extraction?.Payload is null)
        {
            error = $"The response did not contain a valid {Marker} payload.";
            return false;
        }

        payload = extraction.Payload;
        var status = payload.Status?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(payload.GroupId) ||
            string.IsNullOrWhiteSpace(payload.BaseRevision) ||
            string.IsNullOrWhiteSpace(payload.Summary) ||
            status is not ("ready" or "revision-required") ||
            payload.Issues is null)
        {
            error = "The human-proof coverage review omitted its identity, status, summary, or issues.";
            payload = null;
            return false;
        }

        if (payload.Issues.Any(issue =>
                string.IsNullOrWhiteSpace(issue.TargetId) ||
                string.IsNullOrWhiteSpace(issue.ObservableOutcome) ||
                string.IsNullOrWhiteSpace(issue.Reason) ||
                string.IsNullOrWhiteSpace(issue.Recommendation)))
        {
            error = "Every human-proof coverage issue needs a target, observable outcome, reason, and recommendation.";
            payload = null;
            return false;
        }
        if ((status == "ready") != (payload.Issues.Count == 0))
        {
            error = "Human-proof coverage status must be ready only when the issue list is empty.";
            payload = null;
            return false;
        }

        payload = payload with
        {
            GroupId = payload.GroupId.Trim(),
            BaseRevision = payload.BaseRevision.Trim(),
            Status = status,
            Summary = payload.Summary.Trim(),
            Issues = payload.Issues.Select(issue => issue with
            {
                TargetId = issue.TargetId.Trim(),
                ObservableOutcome = issue.ObservableOutcome.Trim(),
                Reason = issue.Reason.Trim(),
                Recommendation = issue.Recommendation.Trim(),
            }).ToArray(),
        };
        return true;
    }

    internal static string BuildRepairDiagnostic(PlanHumanProofCoverageReviewPayload review)
    {
        var details = review.Issues.Select((issue, index) =>
            $"{index + 1}. {issue.TargetId}: '{issue.ObservableOutcome}'. {issue.Reason} " +
            $"Required correction: {issue.Recommendation}");
        return "Semantic human-proof coverage validation failed. " + review.Summary + Environment.NewLine +
               string.Join(Environment.NewLine, details);
    }
}

internal sealed class PlanHumanProofCoverageCandidateStore(string squadFolderPath)
{
    private readonly string _folder = Path.Combine(squadFolderPath, "tmp", "decompose", "preflight");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    internal void Save(PendingDecomposePlan plan)
    {
        Directory.CreateDirectory(_folder);
        JsonFileStorage.AtomicWrite(
            Path.Combine(_folder, plan.Group.GroupId + ".json"), plan, Options);
    }

    internal void Delete(string groupId)
    {
        var path = Path.Combine(_folder, groupId + ".json");
        if (File.Exists(path)) File.Delete(path);
    }
}
