using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SquadDash;

/// <summary>One AI-proposed recovery action backed by evidence.</summary>
internal sealed record PlanRecoveryOption(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("label")]       string Label,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("action")]      string Action,
    [property: JsonPropertyName("viable")]      bool Viable,
    [property: JsonPropertyName("evidence")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                string? Evidence = null);

/// <summary>Full AI recovery analysis response.</summary>
internal sealed record PlanRecoveryOptionsResponse(
    [property: JsonPropertyName("groupId")]         string GroupId,
    [property: JsonPropertyName("taskId")]          string TaskId,
    [property: JsonPropertyName("revision")]        string Revision,
    [property: JsonPropertyName("options")]         IReadOnlyList<PlanRecoveryOption> Options,
    [property: JsonPropertyName("recommendation")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                    string? Recommendation = null,
    [property: JsonPropertyName("summary")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                                    string? Summary = null);

internal static class PlanRecoveryOptionsParser
{
    internal const string Marker = "PLAN_RECOVERY_OPTIONS_JSON:";

    internal static bool TryParse(string? text, out PlanRecoveryOptionsResponse? response)
    {
        response = null;
        if (!StructuredJsonBlockParser.TryExtractProtocolObject<PlanRecoveryOptionsResponse>(text, Marker, out var extraction)
            || extraction is null)
            return false;
        response = extraction.Payload with
        {
            Options = (extraction.Payload.Options ?? [])
                .Where(option => option is not null)
                .Select(option => option with
                {
                    Action = option.Action?.Trim().ToLowerInvariant() ?? string.Empty,
                })
                .ToArray(),
        };
        return response is not null
            && !string.IsNullOrWhiteSpace(response.GroupId)
            && !string.IsNullOrWhiteSpace(response.TaskId)
            && !string.IsNullOrWhiteSpace(response.Revision)
            && response.Options is { Count: > 0 }
            && response.Options.All(o =>
                !string.IsNullOrWhiteSpace(o.Id)
                && !string.IsNullOrWhiteSpace(o.Label)
                && o.Action is "adopt-commit" or "partial-adopt" or "revert-and-retry" or "clean-retry" or "replan");
    }

    /// <summary>
    /// Validates each option's mechanical feasibility and sets Viable accordingly.
    /// - "adopt-commit": viable if a candidate unrecorded commit exists between baseline and HEAD
    /// - "partial-adopt": viable if candidate commit exists OR uncommitted tracked files changed
    /// - "revert-and-retry": always viable (git revert is always possible)
    /// - "clean-retry": always viable
    /// - "replan": always viable
    /// </summary>
    internal static IReadOnlyList<PlanRecoveryOption> ValidateRecoveryViability(
        IReadOnlyList<PlanRecoveryOption> options,
        bool hasCandidateCommit,
        bool hasUncommittedWork)
    {
        return options
            .Select(o => o with
            {
                Viable = o.Action switch
                {
                    "adopt-commit"    => hasCandidateCommit,
                    "partial-adopt"   => hasCandidateCommit || hasUncommittedWork,
                    "revert-and-retry" => true,
                    "clean-retry"     => true,
                    "replan"          => true,
                    _                 => false,
                }
            })
            .ToList();
    }
}
