using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

internal sealed record PendingPlanHumanProofCoverageReview(
    string CheckId,
    string CandidateHash,
    DecomposedTaskGroup? Group,
    string RejectedCandidate,
    IReadOnlyList<string> AdditionalDiagnostics,
    int Attempt,
    string? Correction = null);

/// <summary>
/// Focused semantic preflight for plan constraints a JSON schema cannot infer: whether prose
/// promises a human-observable outcome without placing human proof at the right boundary, leaves
/// an AI executor with a task whose only meaningful outcome is that human observation, or attributes
/// required execution to a named agent other than the task's single assigned primary.
/// </summary>
internal static class PlanHumanProofCoverageReview
{
    internal const string Marker = "PLAN_HUMAN_PROOF_COVERAGE_JSON:";

    internal static string ComputeCandidateHash(string candidate) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidate)))
            .ToLowerInvariant()[..16];

    internal static string BuildPrompt(string checkId, string candidateHash) => $$"""
        Perform one focused plan-contract preflight. Do not implement, revise, or broadly critique
        the plan. Determine whether every promised outcome that inherently requires a person to
        interact with or observe the live product has a matching human-only proof requirement in an
        approval gate at the correct boundary, whether every preceding task still contains meaningful
        work or evidence its assigned executor can actually produce, and whether every required task
        output and worker-owned proof belongs to that task's single assigned primary.

        Read the isolated revision-locked candidate definition from:
        .squad/tmp/decompose/preflight/{{checkId}}.txt

        The candidate may also contain structural errors. Ignore those—the host checks them
        independently. Still inspect every intelligible task and report all uncovered human-proof and
        cross-agent ownership requirements in one issues array so the planner can repair everything in
        one response alongside the host's aggregated structural diagnostics.

        Each task has exactly one accountable primary agent. Inspect its description, outputs, and
        worker-owned proof requirements for required execution attributed to another named agent. If
        another named agent must modify files, produce an output, create or run tests, perform a build,
        or satisfy a worker-owned proof, require a separate dependent task assigned to that agent with
        explicit output/input handoff. Required execution must not be hidden in prose such as
        “collaborates,” “owns testing,” “supplies validation,” or equivalent wording under a different
        primary. Do not flag a mere subject-matter reference, historical attribution, or optional
        read-only consultation that leaves all required work and accountability with the assigned
        primary. Generic children are subordinate read-only research helpers and never satisfy a named
        agent assignment or worker proof.

        Inspect task descriptions, observable outcomes, validation assertions, and proof contracts.
        Live UI behavior, restart behavior, focus, window state, notification appearance, timing as
        perceived by a person, and interactive progress affordances require human proof unless the
        plan declares a truthful automated substitute for the exact outcome. Code review, AI review,
        source inspection, and headless tests do not establish direct human observation. Do not ask
        for human proof when an outcome is completely and truthfully automated. A matching checkpoint
        must occur after the work that creates the behavior and before a final audit that claims it.

        A task whose only meaningful outcome is one or more human observations is an invalid
        human-only task shell even when those observations are correctly routed to a gate. Require
        the planner to replace it with an AI-executable preparation task followed by the human gate.
        The preparation task should produce any required fixture, sample project, sample files, test
        data, or diagnostics and author one concise, self-contained approval procedure for SquadDash
        to absorb into the durable approval Inbox message. It must not create a separate operator
        runbook. A screenshot is sufficient only for a purely static visual claim.

        Do not classify a task as a human-only shell merely because it contains human proof, uses
        verification execution mode, has no declared output, or has a title such as smoke test. Read
        its complete execution contract. Use revision-required for this issue only when the task
        explicitly asks AI to perform or claim the human interaction and contains no meaningful
        AI-accessible command, inspection, preparation, artifact, fixture, or evidence work. If such
        work exists, the task is executable and the separate human gate is valid.

        Judge capability from the executor's actual environment. Repository inspection, commands,
        deterministic tests, and genuinely available browser/service automation may be AI-reachable.
        Native or host-embedded application interaction, inaccessible build/install/restart flows,
        physical keyboard or mouse input, voice, cameras, microphones, speakers, and live human sight,
        hearing, timing, or focus are human-only. For mixed work, require AI to prepare the state,
        instructions, fixtures, expected observations, cleanup, and failure-capture guidance so the
        human performs only the irreducible interaction or perception. The plan must say how that
        package reaches the reviewer through the SquadDash Inbox.

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
              "observableOutcome": "the exact uncovered human-observable promise or conflicting cross-agent ownership statement",
              "reason": "why proof is uncovered, why the task is not executable by its assigned worker, or why required work belongs to another primary",
              "recommendation": "the checkpoint or preparation task to add, or the dependent assigned task and output/input handoff to create"
            }
          ]
        }

        Use revision-required when at least one human-observable promise is uncovered, at least one
        human-only task shell remains, or required execution is attributed to a named agent other than
        the task's assigned primary. Use ready with an empty issues array otherwise. Do not report
        implementation-quality, architecture, naming, style, or ordinary automated-test findings in
        this focused preflight.
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
            error = "The plan-contract review omitted its identity, status, summary, or issues.";
            payload = null;
            return false;
        }

        if (payload.Issues.Any(issue =>
                string.IsNullOrWhiteSpace(issue.TargetId) ||
                string.IsNullOrWhiteSpace(issue.ObservableOutcome) ||
                string.IsNullOrWhiteSpace(issue.Reason) ||
                string.IsNullOrWhiteSpace(issue.Recommendation)))
        {
            error = "Every plan-contract issue needs a target, relevant contract statement, reason, and recommendation.";
            payload = null;
            return false;
        }
        if ((status == "ready") != (payload.Issues.Count == 0))
        {
            error = "Plan-contract status must be ready only when the issue list is empty.";
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
        var details = BuildIssueRepairDiagnostics(review)
            .Select((detail, index) => $"{index + 1}. {detail}");
        return "Semantic plan-contract validation failed. " + review.Summary + Environment.NewLine +
               string.Join(Environment.NewLine, details);
    }

    internal static IReadOnlyList<string> BuildIssueRepairDiagnostics(
        PlanHumanProofCoverageReviewPayload review) =>
        review.Issues.Select(issue =>
            $"{issue.TargetId}: '{issue.ObservableOutcome}'. {issue.Reason} " +
            $"Required correction: {issue.Recommendation}").ToArray();
}

internal sealed class PlanHumanProofCoverageCandidateStore(string squadFolderPath)
{
    private readonly string _folder = Path.Combine(squadFolderPath, "tmp", "decompose", "preflight");
    private readonly string _pendingPath = Path.Combine(
        squadFolderPath, "tmp", "decompose", "preflight", "pending.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    internal void Save(PendingPlanHumanProofCoverageReview pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        Validate(pending);
        Directory.CreateDirectory(_folder);
        WriteAtomic(GetCandidatePath(pending.CheckId), pending.RejectedCandidate);
        WriteAtomic(_pendingPath, JsonSerializer.Serialize(pending, Options));
    }

    internal void Delete(string checkId)
    {
        if (File.Exists(_pendingPath))
        {
            try
            {
                var current = JsonSerializer.Deserialize<PendingPlanHumanProofCoverageReview>(
                    File.ReadAllText(_pendingPath), Options);
                if (current is not null &&
                    string.Equals(current.CheckId, checkId, StringComparison.Ordinal))
                {
                    File.Delete(_pendingPath);
                }
            }
            catch (JsonException)
            {
                // Preserve unreadable state for diagnosis rather than deleting a potentially
                // newer transaction merely because an older completion arrived late.
            }
        }

        var candidatePath = GetCandidatePath(checkId);
        if (File.Exists(candidatePath)) File.Delete(candidatePath);
    }

    internal PendingPlanHumanProofCoverageReview? Load(out string? error)
    {
        error = null;
        if (!File.Exists(_pendingPath)) return null;

        try
        {
            var pending = JsonSerializer.Deserialize<PendingPlanHumanProofCoverageReview>(
                File.ReadAllText(_pendingPath), Options);
            if (pending is null)
            {
                error = "The persisted plan-contract preflight state was empty.";
                return null;
            }

            Validate(pending);
            var candidatePath = GetCandidatePath(pending.CheckId);
            if (!File.Exists(candidatePath))
            {
                error = $"The persisted plan-contract candidate '{pending.CheckId}' was missing.";
                return null;
            }

            var candidate = File.ReadAllText(candidatePath);
            if (!string.Equals(candidate, pending.RejectedCandidate, StringComparison.Ordinal))
            {
                error = "The persisted plan-contract candidate did not match its transaction state.";
                return null;
            }

            return pending;
        }
        catch (JsonException ex)
        {
            error = $"The persisted plan-contract preflight state contained invalid JSON: {ex.Message}";
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                      or ArgumentException)
        {
            error = $"The persisted plan-contract preflight state was invalid: {ex.Message}";
            return null;
        }
    }

    private string GetCandidatePath(string checkId) => Path.Combine(_folder, checkId + ".txt");

    private static void Validate(PendingPlanHumanProofCoverageReview pending)
    {
        if (string.IsNullOrWhiteSpace(pending.CheckId) ||
            !string.Equals(Path.GetFileName(pending.CheckId), pending.CheckId, StringComparison.Ordinal) ||
            pending.CheckId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException("The plan-contract preflight check identifier was invalid.");
        }
        if (string.IsNullOrWhiteSpace(pending.RejectedCandidate))
            throw new InvalidDataException("The plan-contract candidate was empty.");
        var computedHash = PlanHumanProofCoverageReview.ComputeCandidateHash(pending.RejectedCandidate);
        if (!string.Equals(computedHash, pending.CandidateHash, StringComparison.Ordinal))
            throw new InvalidDataException("The plan-contract candidate hash did not match its transaction state.");
        if (pending.AdditionalDiagnostics is null || pending.Attempt < 0)
            throw new InvalidDataException("The plan-contract preflight retry state was invalid.");
    }

    private static void WriteAtomic(string path, string content)
    {
        var tempPath = path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, content);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
