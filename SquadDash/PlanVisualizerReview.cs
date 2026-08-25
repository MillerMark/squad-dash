using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed record PlanVisualizerReviewAnnotation(
    [property: JsonPropertyName("targetKind")] string TargetKind,
    [property: JsonPropertyName("targetId")] string TargetId,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("note")] string Note,
    [property: JsonPropertyName("actionLabel")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ActionLabel = null,
    [property: JsonPropertyName("change")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PlanRevisionOperation? Change = null);

internal sealed record PlanVisualizerReviewPayload(
    [property: JsonPropertyName("groupId")] string GroupId,
    [property: JsonPropertyName("baseRevision")] string BaseRevision,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("annotations")] IReadOnlyList<PlanVisualizerReviewAnnotation> Annotations,
    [property: JsonPropertyName("status")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Status = null,
    [property: JsonPropertyName("humanVerificationFindings")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<PlanHumanVerificationReviewFinding>? HumanVerificationFindings = null);

internal sealed record PlanHumanVerificationReviewFinding(
    [property: JsonPropertyName("gateId")] string GateId,
    [property: JsonPropertyName("requirementId")] string RequirementId,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("issue")] string Issue,
    [property: JsonPropertyName("recommendation")] string Recommendation);

internal static class PlanVisualizerReviewParser
{
    internal const string Marker = "PLAN_VISUALIZER_REVIEW_JSON:";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly HashSet<string> SupportedChangeOperations = new(StringComparer.Ordinal)
    {
        "updatePlan", "editPlanText", "updateTask", "editTaskText", "addTask", "removeTask",
        "updateApprovalGate", "editApprovalGateText", "addApprovalGate", "removeApprovalGate",
        "updateValidation", "editValidationText", "addValidation", "removeValidation",
    };

    internal static bool TryParse(
        string? response,
        out PlanVisualizerReviewPayload? payload,
        out string? error)
    {
        payload = null;
        error = null;
        if (string.IsNullOrWhiteSpace(response)) return false;
        var markerIndex = response.LastIndexOf(Marker, StringComparison.Ordinal);
        if (markerIndex < 0) return false;
        var jsonStart = response.IndexOf('{', markerIndex + Marker.Length);
        if (jsonStart < 0 || !TryFindObjectEnd(response, jsonStart, out var jsonEnd))
        {
            error = "The AI review marker was not followed by a complete JSON object.";
            return false;
        }
        try
        {
            payload = JsonSerializer.Deserialize<PlanVisualizerReviewPayload>(
                response[jsonStart..(jsonEnd + 1)], Options);
        }
        catch (JsonException exception)
        {
            error = "The AI review JSON was invalid: " + exception.Message;
            return false;
        }
        if (payload is null || string.IsNullOrWhiteSpace(payload.GroupId) ||
            string.IsNullOrWhiteSpace(payload.BaseRevision) || payload.Annotations is null)
        {
            error = "The AI review must include groupId, baseRevision, summary, and annotations.";
            payload = null;
            return false;
        }
        var invalid = payload.Annotations.FirstOrDefault(annotation =>
            annotation.TargetKind is not ("task" or "validation" or "gate" or "plan") ||
            string.IsNullOrWhiteSpace(annotation.TargetId) ||
            string.IsNullOrWhiteSpace(annotation.Title) ||
            string.IsNullOrWhiteSpace(annotation.Note));
        if (invalid is not null)
        {
            error = "Every AI review annotation needs a task, validation, gate, or plan target plus title and note.";
            payload = null;
            return false;
        }
        var invalidChange = payload.Annotations.FirstOrDefault(annotation =>
            annotation.Change is { } change &&
            (!SupportedChangeOperations.Contains(change.Op?.Trim() ?? string.Empty) ||
             !string.IsNullOrWhiteSpace(change.TargetRef)));
        if (invalidChange is not null)
        {
            error = "Each interactive review change must be one supported staged-plan operation using canonical IDs.";
            payload = null;
            return false;
        }
        var readinessStatus = string.IsNullOrWhiteSpace(payload.Status)
            ? "ready"
            : payload.Status.Trim().ToLowerInvariant();
        if (readinessStatus is not ("ready" or "ready-with-warnings" or "revision-required"))
        {
            error = "AI review status must be ready, ready-with-warnings, or revision-required.";
            payload = null;
            return false;
        }
        if ((payload.HumanVerificationFindings ?? []).Any(finding =>
                string.IsNullOrWhiteSpace(finding.GateId) ||
                string.IsNullOrWhiteSpace(finding.RequirementId) ||
                string.IsNullOrWhiteSpace(finding.Issue) ||
                string.IsNullOrWhiteSpace(finding.Recommendation)))
        {
            error = "Each human-verification finding requires gateId, requirementId, issue, and recommendation.";
            payload = null;
            return false;
        }
        payload = payload with
        {
            GroupId = payload.GroupId.Trim(),
            BaseRevision = payload.BaseRevision.Trim(),
            Summary = payload.Summary?.Trim() ?? string.Empty,
            Status = readinessStatus,
            HumanVerificationFindings = (payload.HumanVerificationFindings ?? []).Select(finding => finding with
            {
                GateId = finding.GateId.Trim(),
                RequirementId = finding.RequirementId.Trim(),
                Severity = string.IsNullOrWhiteSpace(finding.Severity) ? "warning" : finding.Severity.Trim().ToLowerInvariant(),
                Issue = finding.Issue.Trim(),
                Recommendation = finding.Recommendation.Trim(),
            }).ToArray(),
            Annotations = payload.Annotations.Select(annotation => annotation with
            {
                TargetKind = annotation.TargetKind.Trim().ToLowerInvariant(),
                TargetId = annotation.TargetId.Trim(),
                Severity = string.IsNullOrWhiteSpace(annotation.Severity)
                    ? "suggestion"
                    : annotation.Severity.Trim().ToLowerInvariant(),
                Title = annotation.Title.Trim(),
                Note = annotation.Note.Trim(),
                ActionLabel = string.IsNullOrWhiteSpace(annotation.ActionLabel)
                    ? null
                    : annotation.ActionLabel.Trim(),
                Change = annotation.Change is null
                    ? null
                    : annotation.Change with
                    {
                        Op = annotation.Change.Op.Trim(),
                        TargetId = string.IsNullOrWhiteSpace(annotation.Change.TargetId)
                            ? null
                            : annotation.Change.TargetId.Trim(),
                        Path = string.IsNullOrWhiteSpace(annotation.Change.Path)
                            ? null
                            : annotation.Change.Path.Trim(),
                    },
            }).ToArray(),
        };
        return true;
    }

    internal static string BuildPrompt(PendingDecomposePlan plan) => $$"""
        Review the currently staged plan for correctness, internal consistency, feasibility,
        missing validation coverage, stale duplicated facts, unsafe implementation assumptions,
        and whether every human-review item has a plausible, safe, understandable observation path.
        This is a review only. Do not revise or implement the plan.

        Read the revision-locked outline first:
        .squad/tmp/decompose/outlines/{{plan.Group.GroupId}}.json

        Retrieve only the task, validation, or gate entities needed to substantiate findings from:
        .squad/tmp/decompose/{{plan.Group.GroupId}}.json

        Return exactly one block using this schema. Use canonical target IDs from the selected
        entities, not T/G/V outline references. Use targetKind "plan" and targetId
        "{{plan.Group.GroupId}}" for findings that apply to the whole plan.

        PLAN_VISUALIZER_REVIEW_JSON:
        {
          "groupId": "{{plan.Group.GroupId}}",
          "baseRevision": "{{plan.Revision}}",
          "status": "ready | ready-with-warnings | revision-required",
          "summary": "one short overall assessment",
          "humanVerificationFindings": [
            {
              "gateId": "canonical gate ID",
              "requirementId": "canonical proof requirement ID",
              "severity": "warning | error",
              "issue": "specific access, setup, feasibility, or cleanup problem",
              "recommendation": "specific correction"
            }
          ],
          "annotations": [
            {
              "targetKind": "task | validation | gate | plan",
              "targetId": "canonical ID",
              "severity": "suggestion | warning | error",
              "title": "short finding title",
              "note": "specific actionable review note",
              "actionLabel": "Apply or a short specific verb; omit when advisory only",
              "change": {
                "op": "one supported STAGED_PLAN_REVISION_JSON operation",
                "targetId": "canonical ID when the operation targets an existing entity",
                "patch": { "onlyChangedProperty": "new value" }
              }
            }
          ]
        }

        Each annotation may contain at most one change operation. Omit actionLabel and change for
        advisory findings. Prefer property-level update or edit-text operations. Use add/remove only
        when necessary. A remove operation's actionLabel should be "Delete". The host validates every
        operation and lets the user apply suggestions individually; never combine several changes in
        one annotation.

        For human verification, assess whether the declared recipe plausibly reaches the requested
        state, depends on UI/data that exists by that boundary, and has safe setup and cleanup.
        Treat Setup, Open, Observe, and Cleanup as the four human-facing recipe fields.
        Never infer a fixture from prose: accept fixture ownership only when fixtureTaskId and
        fixtureOutputId explicitly reference an upstream task output. Use ready-with-warnings for
        incomplete but potentially executable review guidance. Use revision-required only for an
        impossible, unsafe, circular, or structurally invalid review path.
        """;

    private static bool TryFindObjectEnd(string text, int start, out int end)
    {
        end = -1;
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = start; index < text.Length; index++)
        {
            var value = text[index];
            if (escaped) { escaped = false; continue; }
            if (value == '\\' && inString) { escaped = true; continue; }
            if (value == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (value == '{') depth++;
            else if (value == '}' && --depth == 0) { end = index; return true; }
        }
        return false;
    }
}

internal sealed class PlanVisualizerReviewStore(string squadFolderPath)
{
    private readonly string _folder = Path.Combine(squadFolderPath, "tmp", "decompose", "reviews");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    internal void Save(PlanVisualizerReviewPayload payload)
    {
        Directory.CreateDirectory(_folder);
        JsonFileStorage.AtomicWrite(Path.Combine(_folder, payload.GroupId + ".json"), payload, Options);
    }

    internal PlanVisualizerReviewPayload? Load(string groupId, string revision)
    {
        var path = Path.Combine(_folder, groupId + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<PlanVisualizerReviewPayload>(File.ReadAllText(path), Options);
            return payload is not null && string.Equals(payload.BaseRevision, revision, StringComparison.Ordinal)
                ? payload
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            SquadDashTrace.Write("PlanReview", $"Could not load plan review for {groupId}: {exception.Message}");
            return null;
        }
    }

    internal void Delete(string groupId)
    {
        var path = Path.Combine(_folder, groupId + ".json");
        if (File.Exists(path)) File.Delete(path);
    }
}
