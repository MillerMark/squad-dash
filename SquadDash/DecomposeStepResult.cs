using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed record DecomposeStepVerification(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("command")] string? Command,
    [property: JsonPropertyName("summary")] string? Summary);

internal sealed record DecomposeAgentExecution(
    [property: JsonPropertyName("requestedAgent")] string RequestedAgent,
    [property: JsonPropertyName("actualPrimaryAgent")] string ActualPrimaryAgent,
    [property: JsonPropertyName("children")] IReadOnlyList<string>? Children,
    [property: JsonPropertyName("primaryToolCallId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? PrimaryToolCallId = null);

internal sealed record DecomposeStepProofEvidence(
    [property: JsonPropertyName("requirementId")] string RequirementId,
    [property: JsonPropertyName("proofType")] string ProofType,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("artifacts")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? Artifacts = null);

internal sealed record DecomposeStepResult(
    [property: JsonPropertyName("groupId")] string GroupId,
    [property: JsonPropertyName("taskId")] string TaskId,
    [property: JsonPropertyName("revision")] string Revision,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("commit")] string? Commit,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("remainingWork")] IReadOnlyList<string>? RemainingWork,
    [property: JsonPropertyName("verification")] DecomposeStepVerification? Verification,
    [property: JsonPropertyName("agentExecutions")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<DecomposeAgentExecution>? AgentExecutions = null,
    [property: JsonPropertyName("executionAttemptId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ExecutionAttemptId = null,
    [property: JsonPropertyName("proofEvidence")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<DecomposeStepProofEvidence>? ProofEvidence = null,
    [property: JsonPropertyName("deferredWork")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<PlanTaskDeferredWork>? DeferredWork = null,
    [property: JsonIgnore]
        IReadOnlyList<PlanEvidenceCommit>? Commits = null,
    [property: JsonIgnore]
        IReadOnlyList<PlanExecutionAnomaly>? ExecutionAnomalies = null,
    [property: JsonPropertyName("completedWork")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? CompletedWork = null,
    [property: JsonPropertyName("completionKind")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? CompletionKind = null,
    [property: JsonPropertyName("evaluatedCommit")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? EvaluatedCommit = null,
    [property: JsonPropertyName("noOpEvidence")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<PlanTaskNoOpEvidence>? NoOpEvidence = null)
{
    [JsonIgnore]
    internal bool IsVerifiedNoOp =>
        string.Equals(CompletionKind, DecomposeStepCompletionKind.VerifiedNoOp, StringComparison.Ordinal);

    [JsonIgnore]
    internal string? VerificationTargetCommit => IsVerifiedNoOp ? EvaluatedCommit : Commit;
}

internal static class DecomposeStepCompletionKind
{
    internal const string Changed = "changed";
    internal const string VerifiedNoOp = "verified-no-op";
}

internal static class DecomposeStepResultParser
{
    internal const string Marker = "DECOMPOSE_STEP_RESULT_JSON:";

    internal static bool TryParse(string? text, out DecomposeStepResult? result, out string? error)
    {
        result = null;
        error = null;
        if (!StructuredJsonBlockParser.TryExtractProtocolObject<DecomposeStepResult>(text, Marker, out var extraction) ||
            extraction is null)
        {
            error = $"The response did not contain a valid {Marker} payload.";
            return false;
        }
        result = extraction.Payload;

        result = result with
        {
            Status = result.Status?.Trim().ToLowerInvariant() ?? string.Empty,
            Commit = result.Commit?.Trim(),
            CompletionKind = string.IsNullOrWhiteSpace(result.CompletionKind)
                ? null
                : result.CompletionKind.Trim().ToLowerInvariant(),
            EvaluatedCommit = result.EvaluatedCommit?.Trim(),
            RemainingWork = (result.RemainingWork ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToArray(),
            Verification = result.Verification is null
                ? null
                : result.Verification with
                {
                    Status = result.Verification.Status?.Trim().ToLowerInvariant() ?? string.Empty,
                },
            DeferredWork = (result.DeferredWork ?? [])
                .Where(item => item is not null)
                .ToArray(),
            // Older result envelopes supplied only the prose summary. Preserve that response as
            // one complete item rather than guessing at sentence boundaries.
            CompletedWork = (result.CompletedWork ??
                    (string.IsNullOrWhiteSpace(result.Summary) ? [] : [result.Summary]))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToArray(),
            NoOpEvidence = (result.NoOpEvidence ?? [])
                .Where(item => item is not null)
                .Select(item => item with
                {
                    Requirement = item.Requirement?.Trim() ?? string.Empty,
                    Observation = item.Observation?.Trim() ?? string.Empty,
                    Locations = (item.Locations ?? [])
                        .Where(location => !string.IsNullOrWhiteSpace(location))
                        .Select(location => location.Trim())
                        .ToArray(),
                })
                .ToArray(),
        };

        if (result.Status == "complete" && string.IsNullOrWhiteSpace(result.CompletionKind))
            result = result with { CompletionKind = DecomposeStepCompletionKind.Changed };

        if (result is null || string.IsNullOrWhiteSpace(result.GroupId) ||
            string.IsNullOrWhiteSpace(result.TaskId) || string.IsNullOrWhiteSpace(result.Revision) ||
            string.IsNullOrWhiteSpace(result.Summary))
        {
            error = "The step result omitted groupId, taskId, revision, or summary.";
            return false;
        }

        if (result.Status is not ("complete" or "partial" or "failed"))
        {
            error = "The step-result status must be complete, partial, or failed.";
            return false;
        }

        if (result.Status == "complete" &&
            result.CompletionKind is not (DecomposeStepCompletionKind.Changed or DecomposeStepCompletionKind.VerifiedNoOp))
        {
            error = "A complete result must use completionKind changed or verified-no-op.";
            return false;
        }

        if (result.Status != "complete" && !string.IsNullOrWhiteSpace(result.CompletionKind))
        {
            error = "completionKind is only valid for a complete result.";
            return false;
        }

        if (result.Status == "complete" && result.Verification?.Status != "passed")
        {
            error = "A complete result requires passed verification evidence.";
            return false;
        }

        if (result.Status == "complete" && !result.IsVerifiedNoOp && string.IsNullOrWhiteSpace(result.Commit))
        {
            error = "A changed complete result requires a commit.";
            return false;
        }

        if (result.IsVerifiedNoOp && !string.IsNullOrWhiteSpace(result.Commit))
        {
            error = "A verified-no-op result must set commit to null; evaluatedCommit identifies the inspected snapshot.";
            return false;
        }

        if (result.IsVerifiedNoOp && string.IsNullOrWhiteSpace(result.EvaluatedCommit))
        {
            error = "A verified-no-op result requires evaluatedCommit.";
            return false;
        }

        if (result.IsVerifiedNoOp && result.NoOpEvidence is not { Count: > 0 })
        {
            error = "A verified-no-op result requires requirement-scoped noOpEvidence.";
            return false;
        }

        if (result.IsVerifiedNoOp && result.NoOpEvidence!.Any(item =>
                string.IsNullOrWhiteSpace(item.Requirement) ||
                string.IsNullOrWhiteSpace(item.Observation) ||
                item.Locations is not { Count: > 0 }))
        {
            error = "Each verified-no-op evidence item requires requirement, observation, and at least one repository location.";
            return false;
        }

        if (!result.IsVerifiedNoOp &&
            (!string.IsNullOrWhiteSpace(result.EvaluatedCommit) || result.NoOpEvidence is { Count: > 0 }))
        {
            error = "evaluatedCommit and noOpEvidence are only valid for a verified-no-op result.";
            return false;
        }

        if (result.Status == "partial" && (result.RemainingWork is null || result.RemainingWork.Count == 0))
        {
            error = "A partial result must describe its remaining work.";
            return false;
        }

        if (result.Status == "partial" && !string.IsNullOrWhiteSpace(result.Commit) &&
            result.Verification?.Status != "passed")
        {
            error = "A committed partial result requires passed verification evidence.";
            return false;
        }

        if ((result.ProofEvidence ?? []).Any(evidence =>
                string.IsNullOrWhiteSpace(evidence.RequirementId) ||
                string.IsNullOrWhiteSpace(evidence.ProofType) ||
                string.IsNullOrWhiteSpace(evidence.Summary)))
        {
            error = "Proof evidence requires requirementId, proofType, and summary.";
            return false;
        }

        if ((result.DeferredWork ?? []).Any(item =>
                string.IsNullOrWhiteSpace(item.Requirement) ||
                string.IsNullOrWhiteSpace(item.Reason) ||
                item.OwnerTaskIds is not { Count: > 0 } ||
                item.OwnerTaskIds.Any(string.IsNullOrWhiteSpace)))
        {
            error = "Deferred work requires a requirement, reason, and at least one named owner task.";
            return false;
        }

        return true;
    }
}

/// <summary>
/// Validates model-supplied result identity against the host-owned execution envelope. These
/// fields remain explicit protocol data: the host reports every mismatch for one bounded repair
/// instead of silently rewriting the response or revealing defects one at a time.
/// </summary>
internal static class DecomposeStepResultContractDiagnostics
{
    internal static IReadOnlyList<string> ValidateIdentity(
        DecomposeStepResult result,
        string expectedGroupId,
        string expectedTaskId,
        string expectedRevision)
    {
        var diagnostics = new List<string>();
        if (!string.Equals(result.GroupId, expectedGroupId, StringComparison.Ordinal))
            diagnostics.Add(
                $"The result used groupId '{result.GroupId}', but the active plan is '{expectedGroupId}'.");
        if (!string.Equals(result.TaskId, expectedTaskId, StringComparison.Ordinal))
            diagnostics.Add(
                $"The result used taskId '{result.TaskId}', but the assigned task is '{expectedTaskId}'.");
        if (!string.Equals(result.Revision, expectedRevision, StringComparison.Ordinal))
            diagnostics.Add(
                $"The result used revision '{result.Revision}', but the authoritative current plan revision is " +
                $"'{expectedRevision}'. Return \"revision\":\"{expectedRevision}\" for this execution attempt.");
        return diagnostics;
    }

    internal static string? Combine(IEnumerable<string?> diagnostics)
    {
        var all = diagnostics
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return all.Length switch
        {
            0 => null,
            1 => all[0],
            _ => $"The result envelope has {all.Length} independent validation problems:\n- " +
                 string.Join("\n- ", all),
        };
    }
}
