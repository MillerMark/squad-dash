using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed record StagedPlanRevisionProposalPayload(
    [property: JsonPropertyName("groupId")] string GroupId,
    [property: JsonPropertyName("baseRevision")] string BaseRevision,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("operations")] IReadOnlyList<PlanRevisionOperation> Operations);

internal static class StagedPlanRevisionProposalParser
{
    internal const string Marker = "STAGED_PLAN_REVISION_JSON:";

    internal static bool TryParse(
        string? text,
        out StagedPlanRevisionProposalPayload? proposal,
        out string? error)
    {
        proposal = null;
        error = null;
        if (text?.Contains(Marker, StringComparison.Ordinal) != true)
            return false;
        if (CountOccurrences(text, Marker) != 1)
        {
            error = "Return exactly one STAGED_PLAN_REVISION_JSON object.";
            return false;
        }
        if (!StructuredJsonBlockParser.TryExtractObject<StagedPlanRevisionProposalPayload>(
                text, Marker, out var extraction) || extraction is null)
        {
            error = "The STAGED_PLAN_REVISION_JSON object was missing or was not valid JSON.";
            return false;
        }

        var payload = extraction.Payload;
        if (string.IsNullOrWhiteSpace(payload.GroupId) ||
            string.IsNullOrWhiteSpace(payload.BaseRevision) ||
            string.IsNullOrWhiteSpace(payload.Summary))
        {
            error = "groupId, baseRevision, and summary are required.";
            return false;
        }
        if (payload.Operations is not { Count: > 0 })
        {
            error = "operations must contain at least one delta operation.";
            return false;
        }
        if (payload.Operations.Any(operation => operation is null))
        {
            error = "operations cannot contain null entries.";
            return false;
        }
        if (payload.Operations.Any(operation =>
                string.Equals(operation.Op?.Trim(), "reopenTask", StringComparison.Ordinal)))
        {
            error = "reopenTask is not valid for an unapproved staged proposal because it has no execution history.";
            return false;
        }

        proposal = payload with
        {
            GroupId = payload.GroupId.Trim(),
            BaseRevision = payload.BaseRevision.Trim(),
            Summary = payload.Summary.Trim(),
            Operations = payload.Operations.Select(operation => operation with
            {
                Op = operation.Op?.Trim() ?? string.Empty,
                TargetId = string.IsNullOrWhiteSpace(operation.TargetId)
                    ? null
                    : operation.TargetId.Trim(),
                TargetRef = string.IsNullOrWhiteSpace(operation.TargetRef)
                    ? null
                    : operation.TargetRef.Trim().ToUpperInvariant(),
                Path = string.IsNullOrWhiteSpace(operation.Path)
                    ? null
                    : operation.Path.Trim(),
            }).ToArray(),
        };
        return true;
    }

    internal static string BuildRepairPrompt(
        string validationError,
        StagedPlanRevisionProposalPayload? proposal = null)
    {
        var identity = proposal is null
            ? "Use the exact groupId and baseRevision from the original staged-plan correction request."
            : $"Use groupId {proposal.GroupId} and baseRevision {proposal.BaseRevision}.";
        var intent = proposal is null
            ? "Preserve the user's requested correction."
            : $"Preserve this intended correction: {proposal.Summary}";
        return $$"""
            Your STAGED_PLAN_REVISION_JSON response did not satisfy SquadDash's required schema.

            Validation errors:
            {{validationError}}

            {{intent}}
            {{identity}}
            Return exactly one corrected STAGED_PLAN_REVISION_JSON block. Do not return TASKS_JSON,
            PLAN_REVISION_JSON, commentary, or source changes. The object must contain groupId,
            baseRevision, summary, and a non-empty operations array. Use only updatePlan, editPlanText,
            updateTask, editTaskText, addTask, removeTask, updateApprovalGate, editApprovalGateText,
            addApprovalGate, removeApprovalGate, updateValidation, editValidationText, addValidation,
            and removeValidation. Existing task, gate, and validation operations may use the current
            outline's short targetRef (T1, G1, or V1) instead of targetId; never supply both. Update
            operations require a target and a patch containing only changed fields. Text-edit operations
            require path, non-empty oldText that occurs exactly once, and
            newText; prefer them over replacing a long description or assertion array. Add operations require the complete task,
            approvalGate, or validation. Remove operations require targetId. Keep groupId and
            baseRevision unchanged and do not use reopenTask.
            """;
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
}

internal enum StagedPlanRevisionOutcome
{
    Applied,
    NoChanges,
    Stale,
    Invalid,
}

internal sealed record StagedPlanRevisionResult(
    StagedPlanRevisionOutcome Outcome,
    DecomposedTaskGroup? RevisedGroup = null,
    string? Revision = null,
    string? Error = null);

internal static class StagedPlanRevisionApplier
{
    internal static StagedPlanRevisionResult Apply(
        PendingDecomposePlan current,
        StagedPlanRevisionProposalPayload payload)
    {
        if (!string.Equals(current.Group.GroupId, payload.GroupId, StringComparison.Ordinal))
        {
            return new StagedPlanRevisionResult(
                StagedPlanRevisionOutcome.Invalid,
                Error: $"The staged proposal is {current.Group.GroupId}, not {payload.GroupId}.");
        }
        if (!string.Equals(current.Revision, payload.BaseRevision, StringComparison.Ordinal))
        {
            return new StagedPlanRevisionResult(
                StagedPlanRevisionOutcome.Stale,
                Error: $"The staged proposal changed from revision {payload.BaseRevision} to {current.Revision}.");
        }

        var preserveDisplayedStageOrder =
            DependencyEditRecomputer.IsInDisplayedStageOrder(current.Group);

        var syntheticPlan = PendingDecomposePlanAdapter.ToPlan(
            current,
            current.CreatedAt ?? DateTimeOffset.UtcNow);
        var resolvedOperations = new List<PlanRevisionOperation>(payload.Operations.Count);
        foreach (var operation in payload.Operations)
        {
            if (string.IsNullOrWhiteSpace(operation.TargetRef))
            {
                resolvedOperations.Add(operation);
                continue;
            }
            if (!string.IsNullOrWhiteSpace(operation.TargetId))
            {
                return new StagedPlanRevisionResult(
                    StagedPlanRevisionOutcome.Invalid,
                    Error: $"Operation '{operation.Op}' must use either targetId or targetRef, not both.");
            }
            if (!StagedPlanRevisionOutlineStore.TryResolveTargetRef(
                    current.Group, operation.TargetRef, operation.Op,
                    out var resolvedTargetId, out var referenceError))
            {
                return new StagedPlanRevisionResult(
                    StagedPlanRevisionOutcome.Invalid,
                    Error: referenceError);
            }
            resolvedOperations.Add(operation with { TargetId = resolvedTargetId, TargetRef = null });
        }

        var durablePayload = new PlanRevisionProposalPayload(
            payload.GroupId,
            payload.BaseRevision,
            payload.Summary,
            ReopenTaskIds: null,
            Operations: resolvedOperations);
        if (!PlanRevisionDeltaApplier.TryMaterialize(
                syntheticPlan,
                durablePayload,
                out var materialized,
                out var reopened,
                out var materializationError) ||
            materialized is null)
        {
            return new StagedPlanRevisionResult(
                StagedPlanRevisionOutcome.Invalid,
                Error: materializationError ?? "The staged-plan delta was invalid.");
        }
        if (reopened.Count > 0)
        {
            return new StagedPlanRevisionResult(
                StagedPlanRevisionOutcome.Invalid,
                Error: "A staged proposal cannot reopen tasks because it has no execution history.");
        }

        materialized = materialized with { Delivery = current.Group.Delivery };
        var parserInput = "TASKS_JSON:\n" + JsonSerializer.Serialize(materialized);
        if (!TasksJsonParser.TryParse(parserInput, out var parsed, out var parseDiagnostic) || parsed is null)
        {
            return new StagedPlanRevisionResult(
                StagedPlanRevisionOutcome.Invalid,
                Error: parseDiagnostic?.Message ?? "The materialized task graph failed TASKS_JSON validation.");
        }
        if (!DecomposePlanRevision.TryNormalize(parsed, out var normalized, out var normalizationError))
        {
            return new StagedPlanRevisionResult(
                StagedPlanRevisionOutcome.Invalid,
                Error: normalizationError ?? "The materialized task graph could not be normalized.");
        }
        if (!CodeHealthGroupRunner.HasNoDependencyCycle(normalized, out var cycleIds))
        {
            return new StagedPlanRevisionResult(
                StagedPlanRevisionOutcome.Invalid,
                Error: $"The materialized task graph contains a dependency cycle involving: {string.Join(", ", cycleIds!)}");
        }

        if (!PlanTopologyNormalizer.TryNormalize(
                normalized, out var topology, out var topologyError) || topology is null)
        {
            return new StagedPlanRevisionResult(
                StagedPlanRevisionOutcome.Invalid,
                Error: topologyError ?? "The materialized task graph has invalid topology.");
        }
        normalized = topology.Group;
        if (preserveDisplayedStageOrder)
        {
            normalized = normalized with
            {
                Tasks = DependencyEditRecomputer.OrderTasksByDisplayedStage(normalized),
            };
        }

        var revision = PendingDecomposePlanStore.ComputeRevision(normalized);
        return string.Equals(revision, current.Revision, StringComparison.Ordinal)
            ? new StagedPlanRevisionResult(StagedPlanRevisionOutcome.NoChanges, normalized, revision)
            : new StagedPlanRevisionResult(StagedPlanRevisionOutcome.Applied, normalized, revision);
    }
}
