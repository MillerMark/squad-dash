using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed record DecomposeStepVerification(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("command")] string? Command,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("tests")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DecomposeStepTestResults? Tests = null);

internal sealed record DecomposeStepTestResults(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("passed")] int Passed,
    [property: JsonPropertyName("failed")] int Failed,
    [property: JsonPropertyName("skipped")] int Skipped);

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

internal enum DecomposeStepResultViolationCode
{
    MissingOrInvalidEnvelope,
    MissingIdentityOrSummary,
    InvalidStatus,
    InvalidCompleteVariant,
    CompletionKindOnNonCompleteResult,
    CompleteVerificationNotPassed,
    ChangedCommitMissing,
    VerifiedNoOpEvaluatedCommitMissing,
    VerifiedNoOpEvidenceMissing,
    VerifiedNoOpEvidenceInvalid,
    NoOpFieldsOnChangedResult,
    PartialRemainingWorkMissing,
    PartialCommitNotVerified,
    ProofEvidenceInvalid,
    DeferredWorkInvalid,
    IdentityMismatch,
    InvalidTestCounts,
}

internal sealed record DecomposeStepResultViolation(
    DecomposeStepResultViolationCode Code,
    string Message,
    string? JsonPath = null,
    string? RepairOperation = null);

internal sealed record DecomposeStepResultParseOutcome(
    DecomposeStepResult? Candidate,
    IReadOnlyList<DecomposeStepResultViolation> Violations,
    IReadOnlyList<string> Canonicalizations)
{
    internal bool IsValid => Candidate is not null && Violations.Count == 0;
    internal string? Error => DecomposeStepResultContractDiagnostics.Combine(
        Violations.Select(violation => violation.Message));
}

internal static class DecomposeStepResultParser
{
    internal const string Marker = "DECOMPOSE_STEP_RESULT_JSON:";

    internal static bool TryParse(string? text, out DecomposeStepResult? result, out string? error)
    {
        var outcome = ParseDetailed(text);
        result = outcome.IsValid ? outcome.Candidate : null;
        error = outcome.Error;
        return outcome.IsValid;
    }

    internal static DecomposeStepResultParseOutcome ParseDetailed(
        string? text,
        string? authoritativeGroupId = null,
        string? authoritativeTaskId = null,
        string? authoritativeRevision = null,
        string? authoritativeExecutionAttemptId = null)
    {
        if (!StructuredJsonBlockParser.TryExtractProtocolObject<DecomposeStepResult>(text, Marker, out var extraction) ||
            extraction is null)
        {
            return new DecomposeStepResultParseOutcome(
                null,
                [new(
                    DecomposeStepResultViolationCode.MissingOrInvalidEnvelope,
                    $"The response did not contain a valid {Marker} payload.",
                    RepairOperation: "return the required result object")],
                []);
        }
        var result = extraction.Payload;
        var canonicalizations = new List<string>();

        result = result with
        {
            GroupId = CanonicalizeMissingHostField(result.GroupId, authoritativeGroupId, "groupId", canonicalizations),
            TaskId = CanonicalizeMissingHostField(result.TaskId, authoritativeTaskId, "taskId", canonicalizations),
            Revision = CanonicalizeMissingHostField(result.Revision, authoritativeRevision, "revision", canonicalizations),
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
            ExecutionAttemptId = CanonicalizeMissingHostField(
                result.ExecutionAttemptId,
                authoritativeExecutionAttemptId,
                "executionAttemptId",
                canonicalizations),
        };

        if (result.Status == "complete" && string.IsNullOrWhiteSpace(result.CompletionKind))
            result = result with { CompletionKind = DecomposeStepCompletionKind.Changed };

        // Some models mechanically copy the inspected baseline into both fields even after
        // explicitly selecting verified-no-op. The commit field has no authority for this
        // completion kind: host validation below binds evaluatedCommit to an unchanged HEAD and
        // task baseline. Ignore the redundant field instead of spending the one repair attempt
        // on a schema-only correction or interrupting an otherwise valid plan.
        if (result.IsVerifiedNoOp && !string.IsNullOrWhiteSpace(result.Commit))
        {
            result = result with { Commit = null };
            canonicalizations.Add("removed redundant commit from verified-no-op result");
        }

        // evaluatedCommit and noOpEvidence are host-validated no-op metadata. Models sometimes
        // mirror the inspected snapshot and its evidence into a changed result even though
        // completionKind already discriminates the variant. Neither field can alter the meaning
        // of a changed result, so discard both at the trust boundary instead of interrupting an
        // otherwise valid step for a reporting-only mistake.
        if (string.Equals(result.CompletionKind, DecomposeStepCompletionKind.Changed, StringComparison.Ordinal) &&
            (!string.IsNullOrWhiteSpace(result.EvaluatedCommit) || result.NoOpEvidence is { Count: > 0 }))
        {
            result = result with { EvaluatedCommit = null, NoOpEvidence = null };
            canonicalizations.Add("removed irrelevant verified-no-op metadata from changed result");
        }

        var violations = new List<DecomposeStepResultViolation>();

        if (string.IsNullOrWhiteSpace(result.GroupId) ||
            string.IsNullOrWhiteSpace(result.TaskId) || string.IsNullOrWhiteSpace(result.Revision) ||
            string.IsNullOrWhiteSpace(result.Summary))
        {
            violations.Add(new(
                DecomposeStepResultViolationCode.MissingIdentityOrSummary,
                "The step result omitted groupId, taskId, revision, or summary.",
                RepairOperation: "add each missing required field"));
        }

        if (result.Status is not ("complete" or "partial" or "failed"))
        {
            violations.Add(new(
                DecomposeStepResultViolationCode.InvalidStatus,
                "The step-result status must be complete, partial, or failed.",
                "/status",
                "replace with complete, partial, or failed"));
        }

        if (result.Status == "complete" &&
            result.CompletionKind is not (DecomposeStepCompletionKind.Changed or DecomposeStepCompletionKind.VerifiedNoOp))
        {
            violations.Add(new(
                DecomposeStepResultViolationCode.InvalidCompleteVariant,
                "A complete result must use completionKind changed or verified-no-op.",
                "/completionKind",
                "replace with changed or verified-no-op"));
        }

        if (result.Status != "complete" && !string.IsNullOrWhiteSpace(result.CompletionKind))
        {
            violations.Add(new(
                DecomposeStepResultViolationCode.CompletionKindOnNonCompleteResult,
                "completionKind is only valid for a complete result.",
                "/completionKind",
                "remove"));
        }

        if (result.Status == "complete" && result.Verification?.Status != "passed")
        {
            violations.Add(new(
                DecomposeStepResultViolationCode.CompleteVerificationNotPassed,
                "A complete result requires passed verification evidence.",
                "/verification/status",
                "replace only if verification actually passed"));
        }

        if (result.Status == "complete" && !result.IsVerifiedNoOp && string.IsNullOrWhiteSpace(result.Commit))
        {
            violations.Add(new(
                DecomposeStepResultViolationCode.ChangedCommitMissing,
                "A changed complete result requires a commit.",
                "/commit",
                "add the exact commit already created"));
        }

        if (result.IsVerifiedNoOp && string.IsNullOrWhiteSpace(result.EvaluatedCommit))
        {
            violations.Add(new(
                DecomposeStepResultViolationCode.VerifiedNoOpEvaluatedCommitMissing,
                "A verified-no-op result requires evaluatedCommit.",
                "/evaluatedCommit",
                "add the exact inspected HEAD"));
        }

        if (result.IsVerifiedNoOp && result.NoOpEvidence is not { Count: > 0 })
        {
            violations.Add(new(
                DecomposeStepResultViolationCode.VerifiedNoOpEvidenceMissing,
                "A verified-no-op result requires requirement-scoped noOpEvidence.",
                "/noOpEvidence",
                "add requirement-scoped evidence from the completed inspection"));
        }

        if (result.IsVerifiedNoOp && result.NoOpEvidence!.Any(item =>
                string.IsNullOrWhiteSpace(item.Requirement) ||
                string.IsNullOrWhiteSpace(item.Observation) ||
                item.Locations is not { Count: > 0 }))
        {
            violations.Add(new(
                DecomposeStepResultViolationCode.VerifiedNoOpEvidenceInvalid,
                "Each verified-no-op evidence item requires requirement, observation, and at least one repository location.",
                "/noOpEvidence",
                "replace invalid evidence items"));
        }

        if (!result.IsVerifiedNoOp &&
            (!string.IsNullOrWhiteSpace(result.EvaluatedCommit) || result.NoOpEvidence is { Count: > 0 }))
        {
            violations.Add(new(
                DecomposeStepResultViolationCode.NoOpFieldsOnChangedResult,
                "noOpEvidence is only valid for a verified-no-op result.",
                "/noOpEvidence",
                "remove from the changed result"));
        }

        if (result.Status == "partial" && (result.RemainingWork is null || result.RemainingWork.Count == 0))
        {
            violations.Add(new(
                DecomposeStepResultViolationCode.PartialRemainingWorkMissing,
                "A partial result must describe its remaining work.",
                "/remainingWork",
                "add concrete remaining work"));
        }

        if (result.Status == "partial" && !string.IsNullOrWhiteSpace(result.Commit) &&
            result.Verification?.Status != "passed")
        {
            violations.Add(new(
                DecomposeStepResultViolationCode.PartialCommitNotVerified,
                "A committed partial result requires passed verification evidence.",
                "/verification/status",
                "record the truthful verification outcome"));
        }

        if ((result.ProofEvidence ?? []).Any(evidence =>
                string.IsNullOrWhiteSpace(evidence.RequirementId) ||
                string.IsNullOrWhiteSpace(evidence.ProofType) ||
                string.IsNullOrWhiteSpace(evidence.Summary)))
        {
            violations.Add(new(
                DecomposeStepResultViolationCode.ProofEvidenceInvalid,
                "Proof evidence requires requirementId, proofType, and summary.",
                "/proofEvidence",
                "replace invalid proof evidence items"));
        }

        if ((result.DeferredWork ?? []).Any(item =>
                string.IsNullOrWhiteSpace(item.Requirement) ||
                string.IsNullOrWhiteSpace(item.Reason) ||
                item.OwnerTaskIds is not { Count: > 0 } ||
                item.OwnerTaskIds.Any(string.IsNullOrWhiteSpace)))
        {
            violations.Add(new(
                DecomposeStepResultViolationCode.DeferredWorkInvalid,
                "Deferred work requires a requirement, reason, and at least one named owner task.",
                "/deferredWork",
                "replace invalid deferred work items"));
        }

        if (result.Verification?.Tests is { } tests &&
            (tests.Total < 0 || tests.Passed < 0 || tests.Failed < 0 || tests.Skipped < 0 ||
             tests.Total != tests.Passed + tests.Failed + tests.Skipped))
        {
            violations.Add(new(
                DecomposeStepResultViolationCode.InvalidTestCounts,
                "Structured test counts must be non-negative and total must equal passed plus failed plus skipped.",
                "/verification/tests",
                "replace with exact aggregate counts from the reported test runs, or remove tests when counts are unavailable"));
        }

        return new DecomposeStepResultParseOutcome(result, violations, canonicalizations);
    }

    private static string CanonicalizeMissingHostField(
        string? supplied,
        string? authoritative,
        string fieldName,
        ICollection<string> canonicalizations)
    {
        if (!string.IsNullOrWhiteSpace(supplied))
            return supplied.Trim();
        if (string.IsNullOrWhiteSpace(authoritative))
            return supplied?.Trim() ?? string.Empty;
        canonicalizations.Add($"filled missing host-owned {fieldName}");
        return authoritative.Trim();
    }
}

/// <summary>
/// Validates model-supplied result identity against the host-owned execution envelope. These
/// fields remain explicit protocol data: the host reports every mismatch as a typed violation
/// instead of silently rewriting conflicting identity or revealing defects one at a time.
/// </summary>
internal static class DecomposeStepResultContractDiagnostics
{
    internal static IReadOnlyList<DecomposeStepResultViolation> ValidateIdentityViolations(
        DecomposeStepResult result,
        string expectedGroupId,
        string expectedTaskId,
        string expectedRevision)
    {
        var diagnostics = new List<DecomposeStepResultViolation>();
        if (!string.Equals(result.GroupId, expectedGroupId, StringComparison.Ordinal))
            diagnostics.Add(new(
                DecomposeStepResultViolationCode.IdentityMismatch,
                $"The result used groupId '{result.GroupId}', but the active plan is '{expectedGroupId}'.",
                "/groupId",
                $"replace with {expectedGroupId}"));
        if (!string.Equals(result.TaskId, expectedTaskId, StringComparison.Ordinal))
            diagnostics.Add(new(
                DecomposeStepResultViolationCode.IdentityMismatch,
                $"The result used taskId '{result.TaskId}', but the assigned task is '{expectedTaskId}'.",
                "/taskId",
                $"replace with {expectedTaskId}"));
        if (!string.Equals(result.Revision, expectedRevision, StringComparison.Ordinal))
            diagnostics.Add(new(
                DecomposeStepResultViolationCode.IdentityMismatch,
                $"The result used revision '{result.Revision}', but the authoritative current plan revision is " +
                $"'{expectedRevision}'. Return \"revision\":\"{expectedRevision}\" for this execution attempt.",
                "/revision",
                $"replace with {expectedRevision}"));
        return diagnostics;
    }

    internal static IReadOnlyList<string> ValidateIdentity(
        DecomposeStepResult result,
        string expectedGroupId,
        string expectedTaskId,
        string expectedRevision)
        => ValidateIdentityViolations(result, expectedGroupId, expectedTaskId, expectedRevision)
            .Select(violation => violation.Message)
            .ToArray();

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
