using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal static class PlanGateResponseDisposition
{
    internal const string RequestRework = "request-rework";
    internal const string AddAmendment = "add-amendment";
    internal const string Unrelated = "unrelated";
    internal const string Clarification = "clarification";
}

/// <summary>
/// Structured classification of a free-form user response while a plan approval request is active.
/// The host changes plan state only for <see cref="PlanGateResponseDisposition.RequestRework"/>
/// and <see cref="PlanGateResponseDisposition.AddAmendment"/>.
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
    [property: JsonPropertyName("title")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Title = null,
    [property: JsonPropertyName("instructions")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Instructions = null);

/// <summary>
/// Compatibility shape for an early AI-authored response that grouped each task and its
/// instructions under <c>reworkTasks</c>. The host normalizes this at the protocol boundary;
/// durable plan state and the rest of the runtime continue to use <see cref="PlanGateResponse"/>.
/// </summary>
internal sealed record PlanGateReworkTask(
    [property: JsonPropertyName("taskId")] string TaskId,
    [property: JsonPropertyName("instructions")] string Instructions);

internal sealed record PlanGateResponseWire(
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("gateId")] string GateId,
    [property: JsonPropertyName("revision")] string Revision,
    [property: JsonPropertyName("requestVersion")] int RequestVersion,
    [property: JsonPropertyName("disposition")] string Disposition,
    [property: JsonPropertyName("taskIds")] IReadOnlyList<string>? TaskIds = null,
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("instructions")] string? Instructions = null,
    [property: JsonPropertyName("reworkTasks")] IReadOnlyList<PlanGateReworkTask>? ReworkTasks = null);

internal static class PlanGateResponseParser
{
    internal const string Marker = "PLAN_GATE_RESPONSE_JSON:";

    internal static bool TryParse(string? text, out PlanGateResponse? response)
    {
        response = null;
        if (!StructuredJsonBlockParser.TryExtractObject<PlanGateResponseWire>(text, Marker, out var extraction) ||
            extraction is null)
            return false;

        var wire = extraction.Payload;
        if (wire is null ||
            string.IsNullOrWhiteSpace(wire.PlanId) ||
            string.IsNullOrWhiteSpace(wire.GateId) ||
            string.IsNullOrWhiteSpace(wire.Revision) ||
            wire.RequestVersion <= 0 ||
            wire.Disposition is not (
                PlanGateResponseDisposition.RequestRework or
                PlanGateResponseDisposition.AddAmendment or
                PlanGateResponseDisposition.Unrelated or
                PlanGateResponseDisposition.Clarification))
            return false;

        if (wire.Disposition is PlanGateResponseDisposition.Unrelated or PlanGateResponseDisposition.Clarification)
        {
            response = new PlanGateResponse(
                wire.PlanId, wire.GateId, wire.Revision, wire.RequestVersion, wire.Disposition);
            return true;
        }

        var taskIds = wire.TaskIds;
        var title = wire.Title;
        var instructions = wire.Instructions;
        if (wire.Disposition == PlanGateResponseDisposition.RequestRework &&
            (taskIds is not { Count: > 0 } || string.IsNullOrWhiteSpace(instructions)) &&
            wire.ReworkTasks is { Count: > 0 } legacyTasks &&
            legacyTasks.All(task =>
                !string.IsNullOrWhiteSpace(task.TaskId) &&
                !string.IsNullOrWhiteSpace(task.Instructions)))
        {
            taskIds = legacyTasks.Select(task => task.TaskId).Distinct(StringComparer.Ordinal).ToArray();
            instructions = legacyTasks.Count == 1
                ? legacyTasks[0].Instructions
                : string.Join("\n", legacyTasks.Select(task => $"{task.TaskId}: {task.Instructions.Trim()}"));
        }

        if (string.IsNullOrWhiteSpace(instructions) ||
            taskIds?.Any(string.IsNullOrWhiteSpace) == true ||
            (wire.Disposition == PlanGateResponseDisposition.RequestRework && taskIds is not { Count: > 0 }))
            return false;

        if (wire.Disposition == PlanGateResponseDisposition.AddAmendment && string.IsNullOrWhiteSpace(title))
            title = "Apply requested changes before approval";

        response = new PlanGateResponse(
            wire.PlanId,
            wire.GateId,
            wire.Revision,
            wire.RequestVersion,
            wire.Disposition,
            taskIds,
            title,
            instructions);
        return true;
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
        var exampleTaskId = plan.Tasks
            .FirstOrDefault(task => gate.AfterTaskIds.Contains(task.TaskId, StringComparer.Ordinal))?.TaskId
            ?? "EXACT-REVIEWED-TASK-ID";
        var requestReworkExample = JsonSerializer.Serialize(new
        {
            planId = plan.PlanId,
            gateId = gate.GateId,
            revision = plan.Revision,
            requestVersion = token.RequestVersion,
            disposition = PlanGateResponseDisposition.RequestRework,
            taskIds = new[] { exampleTaskId },
            instructions = "Describe the requested correction precisely.",
        });
        var addAmendmentExample = JsonSerializer.Serialize(new
        {
            planId = plan.PlanId,
            gateId = gate.GateId,
            revision = plan.Revision,
            requestVersion = token.RequestVersion,
            disposition = PlanGateResponseDisposition.AddAmendment,
            taskIds = gate.AfterTaskIds,
            title = "Add the requested integration correction",
            instructions = "Describe the bounded additional work precisely.",
        });

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
            - Use `request-rework` only when the user wants to withdraw acceptance of an existing completed task's result and replace or correct that task's own deliverable. Name the exact completed task IDs whose accepted results must be reopened. Do not edit files in this classification turn.
            - Use `add-amendment` when the completed reviewed tasks should remain complete and the user wants bounded additional work before approving their accumulated result. This includes a new integration, cleanup, hardening, compatibility correction, or guided-tour adjustment discovered during review; a change spanning more than one reviewed task; or work the user performed during the approval pause that now needs to be incorporated and verified. An amendment becomes a new plan task after the reviewed tasks and returns to this same approval gate when complete.
            - Do not choose `request-rework` merely because the user says "change", "fix", or names code originally created by a completed task. Choose it only when that completed task's accepted result itself must be replaced. If its result remains valid and the requested work can be layered onto the joined result, choose `add-amendment`.
            - For `add-amendment`, include the reviewed `taskIds` whose accumulated output the amendment affects. Omit `taskIds` only when it applies to the entire reviewed boundary. Supply a concise imperative `title` and exact, independently executable `instructions`.
            - If it requests separate work, handle that work normally and emit disposition `unrelated`; the approval remains pending.
            - If intent or target is ambiguous, ask one concise question and emit disposition `clarification`; the approval remains pending.

            For `request-rework`, copy these property names and structure exactly (change only the task IDs and instructions):
            {{Marker}}
            {{requestReworkExample}}

            For `add-amendment`, copy this structure exactly:
            {{Marker}}
            {{addAmendmentExample}}

            Use `taskIds` plus one top-level `instructions` string. `add-amendment` also uses `title`. Never emit `reworkTasks`.
            End the response with exactly one {{Marker}} JSON object using the exact plan, gate, revision, and request version above.
            """;
    }
}

internal sealed record PendingGateResponseContext(
    ApprovalClickToken Token,
    string GateId,
    int RepairAttempts = 0);
