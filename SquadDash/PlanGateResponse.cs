using System.Text.Json.Serialization;

namespace SquadDash;

internal static class PlanGateResponseDisposition
{
    internal const string RequestRework = "request-rework";
    internal const string Unrelated = "unrelated";
    internal const string Clarification = "clarification";
}

/// <summary>
/// Structured classification of a free-form user response while a plan approval request is active.
/// The host changes plan state only for <see cref="PlanGateResponseDisposition.RequestRework"/>.
/// </summary>
internal sealed record PlanGateResponse(
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("gateId")] string GateId,
    [property: JsonPropertyName("revision")] string Revision,
    [property: JsonPropertyName("requestVersion")] int RequestVersion,
    [property: JsonPropertyName("disposition")] string Disposition,
    [property: JsonPropertyName("taskIds")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? TaskIds = null,
    [property: JsonPropertyName("instructions")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Instructions = null);

internal static class PlanGateResponseParser
{
    internal const string Marker = "PLAN_GATE_RESPONSE_JSON:";

    internal static bool TryParse(string? text, out PlanGateResponse? response)
    {
        response = null;
        if (!StructuredJsonBlockParser.TryExtractObject<PlanGateResponse>(text, Marker, out var extraction) ||
            extraction is null)
            return false;

        response = extraction.Payload;
        if (response is null ||
            string.IsNullOrWhiteSpace(response.PlanId) ||
            string.IsNullOrWhiteSpace(response.GateId) ||
            string.IsNullOrWhiteSpace(response.Revision) ||
            response.RequestVersion <= 0 ||
            response.Disposition is not (
                PlanGateResponseDisposition.RequestRework or
                PlanGateResponseDisposition.Unrelated or
                PlanGateResponseDisposition.Clarification))
            return false;

        if (response.Disposition != PlanGateResponseDisposition.RequestRework)
            return true;

        return response.TaskIds is { Count: > 0 } &&
               response.TaskIds.All(id => !string.IsNullOrWhiteSpace(id)) &&
               !string.IsNullOrWhiteSpace(response.Instructions);
    }

    internal static string BuildClassificationInstruction(
        Plan plan,
        PlanApprovalGate gate,
        ApprovalClickToken token,
        bool repair = false)
    {
        var reviewedTasks = plan.Tasks
            .Where(task => gate.AfterTaskIds.Contains(task.TaskId, StringComparer.Ordinal))
            .Select(task => $"- taskId={task.TaskId}; title={task.Title ?? task.TaskId}; commit={task.Commit ?? "none"}")
            .ToArray();
        var prefix = repair
            ? "The previous response did not include a valid approval-response classification. Classify that same user response now. Do not perform more work."
            : "The user clicked Request changes for the approval checkpoint below. Their next free-form prompt may request plan rework, may be unrelated work, or may require clarification.";

        return $$"""
            ## Active plan approval response
            {{prefix}}

            planId={{plan.PlanId}}
            revision={{plan.Revision}}
            gateId={{gate.GateId}}
            requestVersion={{token.RequestVersion}}
            reviewed tasks:
            {{string.Join("\n", reviewedTasks)}}

            Make a semantic judgment. Do not assume every prompt is rework.
            - If it explicitly asks to revise reviewed work, emit disposition `request-rework`, exact task IDs, and actionable instructions. Do not edit files in this classification turn.
            - If it requests separate work, handle that work normally and emit disposition `unrelated`; the approval remains pending.
            - If intent or target is ambiguous, ask one concise question and emit disposition `clarification`; the approval remains pending.

            End the response with exactly one {{Marker}} JSON object using the exact plan, gate, revision, and request version above.
            """;
    }
}

internal sealed record PendingGateResponseContext(
    ApprovalClickToken Token,
    string GateId,
    int RepairAttempts = 0);
