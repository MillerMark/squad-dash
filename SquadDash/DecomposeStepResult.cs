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
        IReadOnlyList<PlanTaskDeferredWork>? DeferredWork = null);

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
        };

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
            (string.IsNullOrWhiteSpace(result.Commit) || result.Verification?.Status != "passed"))
        {
            error = "A complete result requires a commit and passed verification evidence.";
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
