using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed record PlanHumanProofCoverageIssue(
    [property: JsonPropertyName("targetId")] string TargetId,
    [property: JsonPropertyName("observableOutcome")] string ObservableOutcome,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("recommendation")] string Recommendation);

internal sealed record PlanHumanProofCoverageReviewPayload(
    [property: JsonPropertyName("checkId")] string CheckId,
    [property: JsonPropertyName("candidateHash")] string CandidateHash,
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

    internal static string ComputeCandidateHash(string candidate) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidate)))
            .ToLowerInvariant()[..16];

    internal static string BuildPrompt(string checkId, string candidateHash) => $$"""
        Perform one focused plan-contract preflight. Do not implement, revise, or broadly critique
        the plan. Determine only whether every promised outcome that inherently requires a person
        to interact with or observe the live product has a matching human-only proof requirement
        in an approval gate at the correct boundary.

        Read the isolated revision-locked candidate definition from:
        .squad/tmp/decompose/preflight/{{checkId}}.txt

        The candidate may also contain structural errors. Ignore those—the host checks them
        independently. Still inspect every intelligible task and report all uncovered human-proof
        requirements in one issues array so the planner can repair everything in one response.

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
          "checkId": "{{checkId}}",
          "candidateHash": "{{candidateHash}}",
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
        if (string.IsNullOrWhiteSpace(payload.CheckId) ||
            string.IsNullOrWhiteSpace(payload.CandidateHash) ||
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
            CheckId = payload.CheckId.Trim(),
            CandidateHash = payload.CandidateHash.Trim(),
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

    internal void Save(string checkId, string candidate)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, checkId + ".txt");
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, candidate);
        File.Move(tempPath, path, overwrite: true);
    }

    internal void Delete(string checkId)
    {
        var path = Path.Combine(_folder, checkId + ".txt");
        if (File.Exists(path)) File.Delete(path);
    }
}
