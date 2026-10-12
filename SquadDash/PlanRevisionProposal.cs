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
    DecomposedValidationNode? Validation = null,
    [property: JsonPropertyName("path")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Path = null,
    [property: JsonPropertyName("oldText")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? OldText = null,
    [property: JsonPropertyName("newText")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? NewText = null,
    [property: JsonPropertyName("targetRef")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TargetRef = null);

internal sealed record PendingPlanRevisionProposal(
    [property: JsonPropertyName("proposalId")] string ProposalId,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("payload")] PlanRevisionProposalPayload Payload);

internal sealed record RejectedPlanRevisionRecoveryState(
    PlanRevisionProposalPayload Payload,
    string LastError,
    int RepairAttempts,
    DateTimeOffset RejectedAt);

internal sealed record PlanRevisionRecoveryPresentation(
    bool CanRetry,
    string Summary,
    string Explanation,
    string Recommendation,
    string TechnicalDetails);

internal static class PlanRevisionRepairRetryPolicy
{
    internal const int MaxAutomaticAttempts = 2;

    internal static bool CanQueue(int attemptsAlreadyQueued) =>
        attemptsAlreadyQueued >= 0 && attemptsAlreadyQueued < MaxAutomaticAttempts;
}

internal static class PlanRevisionRecoveryPresentationBuilder
{
    internal static PlanRevisionRecoveryPresentation Build(
        Plan? current,
        RejectedPlanRevisionRecoveryState recovery)
    {
        var payload = recovery.Payload;
        var baseIsCurrent = current is not null &&
                            string.Equals(current.PlanId, payload.PlanId, StringComparison.Ordinal) &&
                            string.Equals(current.Revision, payload.BaseRevision, StringComparison.Ordinal);
        var isCurrent = PlanRevisionRecoveryPolicy.CanRetry(current, payload);
        var changedValidationIds = (payload.Operations ?? [])
            .Where(operation => string.Equals(operation.Op, "updateValidation", StringComparison.Ordinal))
            .Select(operation => operation.TargetId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var relatedFailedValidation = current?.Validations?.FirstOrDefault(validation =>
            validation.Status == PlanValidationStatus.Failed &&
            changedValidationIds.Contains(validation.ValidationId));

        var explanation =
            $"A later revision was proposed but rejected before it changed plan {payload.PlanId}. " +
            $"The rejected revision reported: {recovery.LastError}";
        if (relatedFailedValidation is not null)
            explanation += $" The earlier validation “{relatedFailedValidation.Title}” remains failed because " +
                           "the revision that changes it was never applied.";

        var recommendation = isCurrent
            ? " Recommended: repair and resubmit this saved revision because the durable plan is still at its " +
              "original base revision, so the intended proposal can be preserved without recreating the plan."
            : baseIsCurrent
                ? "The durable plan is still at the proposal's base revision, but it is not currently at a safe " +
                  "revision boundary. Finish or stop the current transition before retrying."
                : "The durable plan has changed since this proposal was authored, so automatic resubmission is unsafe; " +
                  "review the current plan before creating a new revision.";

        var operationSummary = string.Join(
            ", ",
            (payload.Operations ?? []).Select(operation =>
            {
                var target = operation.TargetId ?? operation.TargetRef;
                return string.IsNullOrWhiteSpace(target)
                    ? operation.Op
                    : $"{operation.Op} {target}";
            }));
        var details =
            $"Plan: {payload.PlanId}{Environment.NewLine}" +
            $"Base revision: {payload.BaseRevision}{Environment.NewLine}" +
            $"Proposed change: {payload.Summary}{Environment.NewLine}" +
            $"Operations preserved: {payload.Operations?.Count ?? 0}{Environment.NewLine}" +
            $"Operations: {(operationSummary.Length == 0 ? "none" : operationSummary)}{Environment.NewLine}" +
            $"Repair attempts: {recovery.RepairAttempts}{Environment.NewLine}" +
            $"Rejected at: {recovery.RejectedAt.LocalDateTime:g}{Environment.NewLine}" +
            $"Last schema error: {recovery.LastError}";

        return new PlanRevisionRecoveryPresentation(
            isCurrent,
            payload.Summary,
            explanation,
            recommendation.TrimStart(),
            details);
    }
}

internal static class PlanRevisionRecoveryPolicy
{
    internal static bool CanRetry(Plan? current, PlanRevisionProposalPayload payload) =>
        current is not null &&
        string.Equals(current.PlanId, payload.PlanId, StringComparison.Ordinal) &&
        string.Equals(current.Revision, payload.BaseRevision, StringComparison.Ordinal) &&
        IsSafeRevisionBoundary(current);

    internal static bool IsSafeRevisionBoundary(Plan plan) =>
        plan.Tasks.Any(task => task.Status is not (PlanTaskStatus.Complete or PlanTaskStatus.Superseded)) &&
        plan.LifecycleStatus is PlanLifecycleStatus.AwaitingApproval or
            PlanLifecycleStatus.Interrupted or PlanLifecycleStatus.Blocked or PlanLifecycleStatus.Executing;
}

internal static class PlanRevisionProposalParser
{
    internal const string Marker = "PLAN_REVISION_JSON:";
    internal const string PatchFieldGuidance = """
        Allowed update patch fields:
        - updatePlan: groupTitle, branch, summary
        - updateTask: title, description, dependsOn, priority, parentTaskId, agentAssignments, parallelEligible, agentRoutingMode, genericAgentReason, outputs, inputs, proofRequirements, amendmentGateId, executionMode, verificationLevel, contractExamples
        - updateApprovalGate: message, afterTaskIds, beforeTaskIds, proofRequirements, question
        - updateValidation: title, description, afterTaskIds, beforeTaskIds, assertions, outputIds, mode, commands, revalidateAtCompletion
        Runtime fields such as status, timestamps, resolution metadata, evidence, commits, and human selections are host-owned. Never include them in an update patch.
        Do not set status to pending to request another approval or validation run. After an approved definition change, SquadDash resets affected gates and validations itself when reevaluation is required.
        """;

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
                text, Marker, out var extraction, out var parseError) || extraction is null)
        {
            error = string.IsNullOrWhiteSpace(parseError)
                ? "The PLAN_REVISION_JSON object was missing or was not valid JSON."
                : $"The PLAN_REVISION_JSON object was invalid: {parseError}";
            return false;
        }

        var payload = extraction.Payload;
        var schemaErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(payload.PlanId)) schemaErrors.Add("planId is required.");
        if (string.IsNullOrWhiteSpace(payload.BaseRevision)) schemaErrors.Add("baseRevision is required.");
        if (string.IsNullOrWhiteSpace(payload.Summary)) schemaErrors.Add("summary is required.");
        if (payload.Operations is not { Count: > 0 })
            schemaErrors.Add("operations must contain at least one delta operation. Do not return revisedPlan.");
        else
        {
            foreach (var entry in payload.Operations.Select((operation, index) => (operation, index)))
            {
                if (entry.operation is null)
                {
                    continue;
                }
                if (string.Equals(entry.operation.Op?.Trim(), "addTask", StringComparison.Ordinal) &&
                    entry.operation.Task is not null && string.IsNullOrWhiteSpace(entry.operation.Task.Id))
                    schemaErrors.Add($"operations[{entry.index}].addTask.task.taskId is required. " +
                                     "Use taskId for new tasks; legacy id is accepted only for compatibility.");
            }
            schemaErrors.AddRange(PlanRevisionDeltaApplier.GetOperationSchemaErrors(payload.Operations));
        }
        if (schemaErrors.Count > 0)
        {
            error = "Plan revision schema errors:" + Environment.NewLine +
                    string.Join(Environment.NewLine, schemaErrors.Select(item => "- " + item));
            return false;
        }

        proposal = payload with
        {
            PlanId = payload.PlanId.Trim(),
            BaseRevision = payload.BaseRevision.Trim(),
            Summary = payload.Summary.Trim(),
            ReopenTaskIds = null,
            RevisedPlan = null,
            Operations = payload.Operations!.Select(operation => operation with
            {
                Op = operation.Op?.Trim() ?? string.Empty,
                TargetId = string.IsNullOrWhiteSpace(operation.TargetId) ? null : operation.TargetId.Trim(),
                TargetRef = string.IsNullOrWhiteSpace(operation.TargetRef) ? null : operation.TargetRef.Trim(),
                Path = string.IsNullOrWhiteSpace(operation.Path) ? null : operation.Path.Trim(),
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

    internal static string BuildRepairPrompt(
        string validationError,
        PlanRevisionProposalPayload? proposal = null,
        bool includeSavedCandidate = false)
    {
        var intent = proposal is null
            ? "Preserve the user's requested plan change."
            : $"Preserve this intended revision: {proposal.Summary}";
        var identity = proposal is null
            ? "Use the exact planId and baseRevision from the original revision request."
            : $"Use planId {proposal.PlanId} and baseRevision {proposal.BaseRevision}.";
        var savedCandidate = includeSavedCandidate && proposal is not null
            ? $$"""

                Repair this saved candidate (it may no longer be present in recent conversation context):
                {{Marker}}
                {{JsonSerializer.Serialize(proposal, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                })}}
                """
            : string.Empty;
        return $$"""
            Your PLAN_REVISION_JSON response did not satisfy SquadDash's required schema.

            Validation errors:
            {{validationError}}

            {{intent}}
            {{identity}}
            {{savedCandidate}}
            Correct only the listed validation errors. Preserve every unaffected operation, field, and value from
            the saved or immediately preceding PLAN_REVISION_JSON. Do not introduce new plan changes. If an error requires
            combining duplicate operations, preserve their complete combined intent.
            Return exactly one corrected PLAN_REVISION_JSON block. Do not add commentary, return a complete plan, or make source changes.
            The object must contain planId, baseRevision, summary, and a non-empty operations array.
            Use only these delta operations: updatePlan, editPlanText, reopenTask, updateTask,
            editTaskText, addTask, removeTask,
            updateApprovalGate, addApprovalGate, removeApprovalGate, updateValidation, addValidation,
            removeValidation, editApprovalGateText, and editValidationText. Update operations require
            targetId and a patch containing only changed fields. Text-edit operations require a JSON
            pointer path, non-empty oldText that occurs exactly once, and newText; prefer them over
            replacing a long description or assertion array.
            Add operations require the complete task, approvalGate, or validation being added. New tasks must
            use taskId as their identifier; do not emit id. Remove and reopen operations require targetId.
            Keep planId and baseRevision unchanged.

            {{PlanRevisionProposalParser.PatchFieldGuidance}}
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

    internal void Replace(PendingPlanRevisionProposal proposal)
    {
        Directory.CreateDirectory(_folder);
        JsonFileStorage.AtomicWrite(GetPath(proposal.Payload.PlanId), proposal, Options);
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

            The plan executor is currently at a durable boundary. If, and only if, the user's request asks to change the specification or graph of one of the unfinished plans below, do not edit source files in this turn. Read the exact durable plan file, then propose only the requested changes with exactly one `PLAN_REVISION_JSON:` object. A conversational correction to implementation while the approved task specification remains unchanged is not a plan revision; follow the injected human-review activity protocol instead.

            {{string.Join("\n", lines)}}

            Schema:
            ```json
            {
              "planId": "exact eligible plan ID",
              "baseRevision": "exact current revision",
              "summary": "short user-facing description of the proposed change",
              "operations": [
                { "op": "reopenTask", "targetId": "completed task ID whose specification must change" },
                { "op": "updateTask", "targetId": "task ID", "patch": { "priority": "high" } },
                { "op": "addTask", "task": { "taskId": "PLAN-YYYYMMDD-010", "title": "New step", "description": "Complete task contract", "dependsOn": [], "priority": "high" } },
                { "op": "editTaskText", "targetId": "task ID", "path": "/description", "oldText": "exact existing clause", "newText": "replacement clause" },
                { "op": "editValidationText", "targetId": "validation ID", "path": "/assertions/0", "oldText": "exact existing clause", "newText": "replacement clause" }
              ]
            }
            ```

            Allowed operations are updatePlan, editPlanText, reopenTask, updateTask, editTaskText, addTask,
            removeTask, updateApprovalGate, editApprovalGateText, addApprovalGate, removeApprovalGate,
            updateValidation, editValidationText, addValidation, and removeValidation. Update operations
            require `targetId` and `patch`; include only fields that change. Text-edit operations require a
            JSON Pointer `path`, non-empty `oldText` that occurs exactly once at that path, and `newText`.
            Prefer a text edit for a clause-level correction within a long string or string array instead of
            replacing the complete property. Add operations require one complete `task`,
            `approvalGate`, or `validation`. A new task uses `taskId` as its identifier; do not emit `id`.
            {{PlanRevisionProposalParser.PatchFieldGuidance}}
            Remove and reopen operations require `targetId`. Never return `revisedPlan`
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
             PlanLifecycleStatus.WaitingForCleanWorktree or
             PlanLifecycleStatus.Interrupted or PlanLifecycleStatus.Blocked ||
         includeExecutingPlan && plan.LifecycleStatus == PlanLifecycleStatus.Executing);
}
