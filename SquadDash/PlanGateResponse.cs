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

        var wire = extraction.Payload with
        {
            Disposition = extraction.Payload.Disposition?.Trim().ToLowerInvariant() ?? string.Empty,
            TaskIds = extraction.Payload.TaskIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
        };
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
            : "The user requested a change at the approval checkpoint below. Their next free-form prompt may request plan rework, add bounded follow-up work, be unrelated work, or require clarification.";
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

internal static class PlanReviewResponseAction
{
    internal const string Discuss = "discuss";
    internal const string RecordCorrection = "record-correction";
    internal const string RequestRework = "request-rework";
    internal const string AddAmendment = "add-amendment";
    internal const string Clarify = "clarify";
    internal const string Unrelated = "unrelated";
}

/// <summary>
/// One correlated response contract for every legitimate outcome of an editable human-review
/// prompt. The explicit action is semantic model output; host dispatch never infers intent from
/// keywords in visible prose.
/// </summary>
internal sealed record PlanReviewResponse(
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("gateId")] string GateId,
    [property: JsonPropertyName("revision")] string Revision,
    [property: JsonPropertyName("requestVersion")] int RequestVersion,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("taskIds")] IReadOnlyList<string>? TaskIds = null,
    [property: JsonPropertyName("summary")] string? Summary = null,
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("instructions")] string? Instructions = null);

internal static class PlanReviewResponseParser
{
    internal const string Marker = "PLAN_REVIEW_RESPONSE_JSON:";

    internal static bool TryParse(
        string? text,
        out PlanReviewResponse? response,
        out string error)
    {
        response = null;
        error = string.Empty;
        if (!StructuredJsonBlockParser.TryExtractObject<PlanReviewResponse>(text, Marker, out var extraction) ||
            extraction is null)
        {
            error = $"missing or unreadable {Marker} object";
            return false;
        }

        var payload = extraction.Payload;
        var action = payload.Action?.Trim().ToLowerInvariant() ?? string.Empty;
        var taskIds = payload.TaskIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(payload.PlanId)) errors.Add("planId is required");
        if (string.IsNullOrWhiteSpace(payload.GateId)) errors.Add("gateId is required");
        if (string.IsNullOrWhiteSpace(payload.Revision)) errors.Add("revision is required");
        if (payload.RequestVersion <= 0) errors.Add("requestVersion must be positive");
        if (action is not (
                PlanReviewResponseAction.Discuss or
                PlanReviewResponseAction.RecordCorrection or
                PlanReviewResponseAction.RequestRework or
                PlanReviewResponseAction.AddAmendment or
                PlanReviewResponseAction.Clarify or
                PlanReviewResponseAction.Unrelated))
            errors.Add("action is not supported");
        if (action is PlanReviewResponseAction.Discuss or PlanReviewResponseAction.RecordCorrection)
        {
            if (taskIds is not { Length: > 0 }) errors.Add("taskIds are required for review activity");
            if (string.IsNullOrWhiteSpace(payload.Summary)) errors.Add("summary is required for review activity");
        }
        if (action == PlanReviewResponseAction.RequestRework)
        {
            if (taskIds is not { Length: > 0 }) errors.Add("taskIds are required for rework");
            if (string.IsNullOrWhiteSpace(payload.Instructions)) errors.Add("instructions are required for rework");
        }
        if (action == PlanReviewResponseAction.AddAmendment)
        {
            if (string.IsNullOrWhiteSpace(payload.Title)) errors.Add("title is required for an amendment");
            if (string.IsNullOrWhiteSpace(payload.Instructions))
                errors.Add("instructions are required for an amendment");
        }

        response = payload with
        {
            Action = action,
            TaskIds = taskIds,
            Summary = payload.Summary?.Trim(),
            Title = payload.Title?.Trim(),
            Instructions = payload.Instructions?.Trim(),
        };

        if (errors.Count > 0)
        {
            error = string.Join("; ", errors);
            return false;
        }
        return true;
    }

    internal static string BuildInstruction(
        Plan plan,
        PlanApprovalGate gate,
        ApprovalClickToken token,
        string taskId,
        bool repair = false,
        string? diagnostic = null)
    {
        var prefix = repair
            ? $"Repair only the previous response metadata. The prior payload was invalid: {diagnostic ?? "unknown schema error"}. Do not repeat work."
            : "Respond to the user's editable human-review request using one correlated outcome contract.";
        return $$"""
            ## Correlated human-review response
            {{prefix}}

            Exact correlation: planId={{plan.PlanId}}; gateId={{gate.GateId}}; revision={{plan.Revision}}; requestVersion={{token.RequestVersion}}; reviewedTask={{taskId}}.
            Choose exactly one action: `discuss`, `record-correction`, `request-rework`, `add-amendment`, `clarify`, or `unrelated`.
            The host dispatches only the explicit action; do not rely on keywords in prose.
            - `discuss` and `record-correction`: include taskIds and summary.
            - `request-rework`: include taskIds and independently executable instructions.
            - `add-amendment`: include taskIds, title, and independently executable instructions.
            - `clarify` and `unrelated`: correlation fields and action are sufficient.
            End with exactly one object:
            {{Marker}}
            {"planId":"{{plan.PlanId}}","gateId":"{{gate.GateId}}","revision":"{{plan.Revision}}","requestVersion":{{token.RequestVersion}},"action":"add-amendment","taskIds":["{{taskId}}"],"title":"Add the requested review fixture","instructions":"Describe the bounded additional work precisely."}
            """;
    }
}

internal sealed record PlanReviewResponseCorrelationResult(bool Success, string Diagnostic);

internal static class PlanReviewResponseCorrelation
{
    internal static PlanReviewResponseCorrelationResult Validate(
        PlanReviewResponse response,
        Plan? plan,
        PlanApprovalGate? gate,
        ApprovalClickToken? token)
    {
        if (plan is null) return new(false, $"plan not found: {response.PlanId}");
        if (gate is null) return new(false, $"gate not found: {response.GateId}");
        if (token is null) return new(false, $"approval token not found: {response.PlanId}");

        var errors = new List<string>();
        if (!string.Equals(response.PlanId, plan.PlanId, StringComparison.Ordinal))
            errors.Add($"planId mismatch expected={plan.PlanId} actual={response.PlanId}");
        if (!string.Equals(response.GateId, gate.GateId, StringComparison.Ordinal))
            errors.Add($"gateId mismatch expected={gate.GateId} actual={response.GateId}");
        if (gate.Status != PlanGateStatus.AwaitingApproval)
            errors.Add($"gate is not awaiting approval: {gate.Status}");
        if (!string.Equals(plan.Revision, response.Revision, StringComparison.Ordinal))
            errors.Add($"revision mismatch expected={plan.Revision} actual={response.Revision}");
        if (response.RequestVersion != token.RequestVersion)
            errors.Add($"request version mismatch expected={token.RequestVersion} actual={response.RequestVersion}");
        if (!token.GateIds.Contains(response.GateId, StringComparer.Ordinal))
            errors.Add($"gate is absent from approval token: {response.GateId}");
        var missingTaskIds = response.TaskIds?
            .Where(taskId => !plan.Tasks.Any(task =>
                string.Equals(task.TaskId, taskId, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        if (missingTaskIds.Length > 0)
            errors.Add($"taskIds do not exist in the plan: [{string.Join(", ", missingTaskIds)}]");
        return errors.Count == 0
            ? new(true, "correlation accepted")
            : new(false, string.Join("; ", errors));
    }
}

internal static class PlanReviewResponseRepairDiagnostics
{
    internal static string Build(
        string? schemaDiagnostic,
        PlanReviewResponse? candidate,
        Plan plan,
        PlanApprovalGate gate,
        ApprovalClickToken token)
    {
        var problems = new List<string>();
        if (!string.IsNullOrWhiteSpace(schemaDiagnostic))
            problems.Add(schemaDiagnostic.Trim());
        if (candidate is not null)
        {
            var correlation = PlanReviewResponseCorrelation.Validate(candidate, plan, gate, token);
            if (!correlation.Success)
                problems.Add(correlation.Diagnostic);
            if (candidate.TaskIds is { Count: > 0 })
            {
                var invalidTargets = PlanApprovalBoundaryTargetPolicy.FindInvalidTargets(
                    plan, gate, candidate.TaskIds);
                if (invalidTargets.Length > 0)
                    problems.Add(PlanApprovalBoundaryTargetPolicy.BuildRepairDiagnostic(
                        plan, gate, invalidTargets));
            }
        }
        return string.Join("; ", problems.Where(problem => !string.IsNullOrWhiteSpace(problem)));
    }
}

/// <summary>
/// Defines which task results can anchor rework or an amendment at an active approval gate.
/// Human-review-required means implementation and automated verification have finished; only
/// the human observation is outstanding, so it is a valid boundary anchor.
/// </summary>
internal static class PlanApprovalBoundaryTargetPolicy
{
    internal static bool IsEligibleStatus(string? status) =>
        status is PlanTaskStatus.Complete or PlanTaskStatus.Partial or
            PlanTaskStatus.HumanReviewRequired;

    internal static string[] FindInvalidTargets(
        Plan plan,
        PlanApprovalGate gate,
        IEnumerable<string> taskIds) =>
        taskIds
            .Distinct(StringComparer.Ordinal)
            .Where(id =>
                !gate.AfterTaskIds.Contains(id, StringComparer.Ordinal) ||
                !plan.Tasks.Any(task =>
                    string.Equals(task.TaskId, id, StringComparison.Ordinal) &&
                    IsEligibleStatus(task.Status)))
            .ToArray();

    internal static string BuildRepairDiagnostic(
        Plan plan,
        PlanApprovalGate gate,
        IReadOnlyCollection<string> invalidTargets)
    {
        var allowed = gate.AfterTaskIds
            .Where(id => plan.Tasks.Any(task =>
                string.Equals(task.TaskId, id, StringComparison.Ordinal) &&
                IsEligibleStatus(task.Status)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return "taskIds must name eligible tasks on the current approval boundary. " +
               $"Invalid taskIds: [{string.Join(", ", invalidTargets)}]. " +
               $"Allowed taskIds: [{string.Join(", ", allowed)}].";
    }
}
