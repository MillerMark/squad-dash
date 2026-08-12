using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed record PlanRevisionProposalPayload(
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("baseRevision")] string BaseRevision,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("reopenTaskIds")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? ReopenTaskIds,
    // Retained only so approval proposals created by older SquadDash builds remain recoverable.
    // New model responses must use Operations and never regenerate the complete plan.
    [property: JsonPropertyName("revisedPlan")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DecomposedTaskGroup? RevisedPlan = null,
    [property: JsonPropertyName("operations")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<PlanRevisionOperation>? Operations = null);

internal sealed record PlanRevisionOperation(
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("targetId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TargetId = null,
    [property: JsonPropertyName("patch")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    JsonElement? Patch = null,
    [property: JsonPropertyName("task")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DecomposedSubTask? Task = null,
    [property: JsonPropertyName("approvalGate")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DecomposedGate? ApprovalGate = null,
    [property: JsonPropertyName("validation")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DecomposedValidationNode? Validation = null);

internal sealed record PendingPlanRevisionProposal(
    [property: JsonPropertyName("proposalId")] string ProposalId,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("payload")] PlanRevisionProposalPayload Payload);

internal static class PlanRevisionProposalParser
{
    internal const string Marker = "PLAN_REVISION_JSON:";

    internal static bool TryParse(
        string? text,
        out PlanRevisionProposalPayload? proposal,
        out string? error)
    {
        proposal = null;
        error = null;
        if (text?.Contains(Marker, StringComparison.Ordinal) != true)
            return false;
        if (CountOccurrences(text, Marker) != 1)
        {
            error = "Return exactly one PLAN_REVISION_JSON object.";
            return false;
        }

        if (!StructuredJsonBlockParser.TryExtractObject<PlanRevisionProposalPayload>(
                text, Marker, out var extraction) || extraction is null)
        {
            error = "The PLAN_REVISION_JSON object was missing or was not valid JSON.";
            return false;
        }

        var payload = extraction.Payload;
        if (string.IsNullOrWhiteSpace(payload.PlanId) ||
            string.IsNullOrWhiteSpace(payload.BaseRevision) ||
            string.IsNullOrWhiteSpace(payload.Summary))
        {
            error = "planId, baseRevision, and summary are required.";
            return false;
        }
        if (payload.Operations is not { Count: > 0 })
        {
            error = "operations must contain at least one delta operation. Do not return revisedPlan.";
            return false;
        }

        proposal = payload with
        {
            PlanId = payload.PlanId.Trim(),
            BaseRevision = payload.BaseRevision.Trim(),
            Summary = payload.Summary.Trim(),
            ReopenTaskIds = null,
            RevisedPlan = null,
            Operations = payload.Operations.Select(operation => operation with
            {
                Op = operation.Op?.Trim() ?? string.Empty,
                TargetId = string.IsNullOrWhiteSpace(operation.TargetId) ? null : operation.TargetId.Trim(),
            }).ToArray(),
        };
        return true;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    internal static string BuildRepairPrompt(string validationError, PlanRevisionProposalPayload? proposal = null)
    {
        var intent = proposal is null
            ? "Preserve the user's requested plan change."
            : $"Preserve this intended revision: {proposal.Summary}";
        var identity = proposal is null
            ? "Use the exact planId and baseRevision from the original revision request."
            : $"Use planId {proposal.PlanId} and baseRevision {proposal.BaseRevision}.";
        return $$"""
            Your PLAN_REVISION_JSON response did not satisfy SquadDash's required schema.

            Validation errors:
            {{validationError}}

            {{intent}}
            {{identity}}
            Return exactly one corrected PLAN_REVISION_JSON block. Do not add commentary, return a complete plan, or make source changes.
            The object must contain planId, baseRevision, summary, and a non-empty operations array.
            Use only these delta operations: updatePlan, reopenTask, updateTask, addTask, removeTask,
            updateApprovalGate, addApprovalGate, removeApprovalGate, updateValidation, addValidation,
            and removeValidation. Update operations require targetId and a patch containing only changed fields.
            Add operations require the complete task, approvalGate, or validation being added. Remove and reopen
            operations require targetId. Keep planId and baseRevision unchanged.
            """;
    }
}

internal sealed class PendingPlanRevisionProposalStore(string squadFolderPath)
{
    private readonly string _folder = Path.Combine(squadFolderPath, "plans", "pending-revisions");
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal PendingPlanRevisionProposal Save(PlanRevisionProposalPayload payload)
    {
        Directory.CreateDirectory(_folder);
        var proposal = new PendingPlanRevisionProposal(
            "plan-revision-" + Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            payload);
        JsonFileStorage.AtomicWrite(GetPath(payload.PlanId), proposal, Options);
        return proposal;
    }

    internal PendingPlanRevisionProposal? Load(string planId)
    {
        var path = GetPath(planId);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<PendingPlanRevisionProposal>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal IReadOnlyList<PendingPlanRevisionProposal> LoadAll()
    {
        if (!Directory.Exists(_folder)) return [];
        var proposals = new List<PendingPlanRevisionProposal>();
        foreach (var path in Directory.EnumerateFiles(_folder, "*.json"))
        {
            try
            {
                var proposal = JsonSerializer.Deserialize<PendingPlanRevisionProposal>(
                    File.ReadAllText(path), Options);
                if (proposal is not null) proposals.Add(proposal);
            }
            catch (JsonException) { }
        }
        return proposals.OrderBy(proposal => proposal.CreatedAt).ToArray();
    }

    internal void Delete(string planId)
    {
        var path = GetPath(planId);
        if (File.Exists(path)) File.Delete(path);
    }

    private string GetPath(string planId) => Path.Combine(_folder, Sanitize(planId) + ".json");

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}

internal static class PlanRevisionPromptInjection
{
    internal static string Build(string squadFolderPath, bool includeExecutingPlan)
    {
        var eligible = new PlanStore(squadFolderPath).LoadAll()
            .Where(plan => IsEligible(plan, includeExecutingPlan))
            .OrderBy(plan => plan.Timestamps.CreatedAt)
            .ToArray();
        if (eligible.Length == 0) return string.Empty;

        var lines = eligible.Select(plan =>
        {
            var path = $".squad/plans/{plan.PlanId}.json";
            var reopenable = plan.Tasks
                .Where(task => task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Partial or
                    PlanTaskStatus.Failed or PlanTaskStatus.HumanReviewRequired)
                .Select(task => task.TaskId)
                .ToArray();
            return $"- planId={plan.PlanId}; baseRevision={plan.Revision}; status={plan.LifecycleStatus}; " +
                   $"definition={path}; reopenableTaskIds=[{string.Join(", ", reopenable)}]";
        });

        return $$"""
            ## Optional revision of an unfinished plan

            The plan executor is currently at a durable boundary. If, and only if, the user's request asks to change one of the unfinished plans below, do not edit source files in this turn. Read the exact durable plan file, then propose only the requested changes with exactly one `PLAN_REVISION_JSON:` object.

            {{string.Join("\n", lines)}}

            Schema:
            ```json
            {
              "planId": "exact eligible plan ID",
              "baseRevision": "exact current revision",
              "summary": "short user-facing description of the proposed change",
              "operations": [
                { "op": "reopenTask", "targetId": "completed task ID whose specification must change" },
                { "op": "updateTask", "targetId": "task ID", "patch": { "description": "only changed fields" } },
                { "op": "updateValidation", "targetId": "validation ID", "patch": { "assertions": ["replacement assertions"] } }
              ]
            }
            ```

            Allowed operations are updatePlan, reopenTask, updateTask, addTask, removeTask, updateApprovalGate,
            addApprovalGate, removeApprovalGate, updateValidation, addValidation, and removeValidation. Update operations
            require `targetId` and `patch`; include only fields that change. Add operations require one complete `task`,
            `approvalGate`, or `validation`. Remove and reopen operations require `targetId`. Never return `revisedPlan`
            or copy unaffected definitions. A completed task can change only when preceded by a reopenTask operation;
            its accepted execution history remains preserved. Update downstream pending tasks and validations when the new
            contract affects them. The response creates a proposal only: a human must approve it before SquadDash changes
            the durable plan or resumes execution. If several plans are listed and the user did not identify one
            unambiguously, ask one concise question and do not emit PLAN_REVISION_JSON.
            """;
    }

    private static bool IsEligible(Plan plan, bool includeExecutingPlan) =>
        plan.Tasks.Any(task => task.Status is not (PlanTaskStatus.Complete or PlanTaskStatus.Superseded)) &&
        (plan.LifecycleStatus is PlanLifecycleStatus.AwaitingApproval or
             PlanLifecycleStatus.Interrupted or PlanLifecycleStatus.Blocked ||
         includeExecutingPlan && plan.LifecycleStatus == PlanLifecycleStatus.Executing);
}
