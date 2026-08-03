using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed record DecomposeRecoveryDecision(
    [property: JsonPropertyName("groupId")] string GroupId,
    [property: JsonPropertyName("revision")] string Revision,
    [property: JsonPropertyName("action")] string Action);

internal static class DecomposeRecoveryDecisionParser
{
    internal const string Marker = "DECOMPOSE_RECOVERY_JSON:";

    internal static bool TryParse(string? text, out DecomposeRecoveryDecision? decision)
    {
        decision = null;
        if (!StructuredJsonBlockParser.TryExtractObject<DecomposeRecoveryDecision>(text, Marker, out var extraction) ||
            extraction is null)
            return false;
        decision = extraction.Payload;

        return decision is not null &&
               !string.IsNullOrWhiteSpace(decision.GroupId) &&
               !string.IsNullOrWhiteSpace(decision.Revision) &&
               decision.Action is "assess-and-continue" or "retry-as-written" or
                   "replan-failed-task" or "review-completed-work";
    }
}
