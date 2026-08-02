using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

/// <summary>A structured gate-approval decision parsed from an AI or user free-text response.</summary>
internal sealed record PlanGateApproval(
    [property: JsonPropertyName("planId")]   string PlanId,
    [property: JsonPropertyName("gateId")]   string GateId,
    [property: JsonPropertyName("revision")] string Revision,
    [property: JsonPropertyName("requestVersion")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                             int? RequestVersion = null,
    [property: JsonPropertyName("note")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
                                             string? Note = null);

/// <summary>
/// Extracts a <see cref="PlanGateApproval"/> from a response containing a
/// <c>PLAN_GATE_APPROVAL_JSON:</c> block using the same brace-balanced technique as
/// <see cref="DecomposeDecisionParser"/>.
/// </summary>
internal static class PlanGateApprovalParser
{
    private const string Marker = "PLAN_GATE_APPROVAL_JSON:";

    internal static bool TryParse(string? text, out PlanGateApproval? approval)
    {
        approval = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var marker = text.LastIndexOf(Marker, StringComparison.Ordinal);
        if (marker < 0) return false;

        var start = text.IndexOf('{', marker + Marker.Length);
        if (start < 0) return false;

        int depth = 0, end = -1; bool quoted = false, escaped = false;
        for (int i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (escaped)         { escaped = false; continue; }
            if (quoted && c == '\\') { escaped = true;  continue; }
            if (c == '"')        { quoted = !quoted; continue; }
            if (quoted)          continue;
            if (c == '{')        depth++;
            else if (c == '}' && --depth == 0) { end = i; break; }
        }
        if (end < 0) return false;

        try
        {
            approval = JsonSerializer.Deserialize<PlanGateApproval>(
                text[start..(end + 1)],
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"PlanGateApprovalParser: JSON is invalid: {ex.Message}");
            return false;
        }

        return approval is not null
            && !string.IsNullOrWhiteSpace(approval.PlanId)
            && !string.IsNullOrWhiteSpace(approval.GateId)
            && !string.IsNullOrWhiteSpace(approval.Revision);
    }
}
