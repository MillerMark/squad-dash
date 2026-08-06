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
        if (!StructuredJsonBlockParser.TryExtractObject<PlanGateApproval>(text, Marker, out var extraction) ||
            extraction is null)
            return false;

        approval = extraction.Payload;

        return approval is not null
            && !string.IsNullOrWhiteSpace(approval.PlanId)
            && !string.IsNullOrWhiteSpace(approval.GateId)
            && !string.IsNullOrWhiteSpace(approval.Revision);
    }
}
