using System.Text.Json;

namespace SquadDash;

internal static class DecomposeEnvelopeRepairPrompt
{
    internal static string Build(
        string groupId,
        string taskId,
        string revision,
        string reason,
        bool envelopeWasPresent = false,
        string? executionAttemptId = null,
        IReadOnlyList<DecomposedAgentAssignment>? assignments = null,
        string? previousStatus = null,
        bool verificationOnly = false,
        IReadOnlyList<DecomposeStepResultViolation>? violations = null,
        DecomposeStepResult? previousResult = null)
    {
        if (HasViolation(violations, DecomposeStepResultViolationCode.CompletionKindOnNonCompleteResult) ||
            IsCompletionKindOnNonCompleteResult(reason, previousStatus))
            return BuildNonCompleteCorrection(
                groupId,
                taskId,
                revision,
                reason,
                previousStatus!,
                executionAttemptId,
                assignments);

        if (verificationOnly || RequiresVerifiedNoOpCorrection(violations) || IsMissingNewCommitFailure(reason))
            return BuildVerifiedNoOpCorrection(
                groupId,
                taskId,
                revision,
                reason,
                envelopeWasPresent,
                executionAttemptId,
                assignments);

        var correctionInstruction = IsCommitLookupFailure(reason)
            ? "The reported commit did not resolve. Run only `git rev-parse --short HEAD`, copy its output exactly into `commit`, and do not inspect or change any files."
            : "Do not re-run any tools, re-examine files, or repeat previous work.";
        var executionAttemptJsonFields = BuildExecutionAttemptJsonFields(executionAttemptId, assignments);
        var executionAttemptInstruction = BuildExecutionAttemptInstruction(executionAttemptId, assignments);
        var fieldCorrections = BuildFieldCorrections(violations, previousResult);

        return $$"""
            SquadDash detected that your previous response completed normally but its required DECOMPOSE_STEP_RESULT_JSON envelope {{(envelopeWasPresent ? "did not match the required schema" : "was missing or invalid")}}.
            Validation error: {{reason}}

            Correct the validation error and provide ONLY the result envelope now, based on the work you just completed. {{correctionInstruction}}

            {{fieldCorrections}}

            Authoritative identity for this execution attempt: groupId `{{groupId}}`, taskId `{{taskId}}`, revision `{{revision}}`.
            If your previous response used another revision, did you mean the authoritative current revision `{{revision}}` for the work just completed? If so, use that exact value below. Do not reuse a revision from earlier conversation or review context, and do not change other valid result content merely because this identity metadata is being corrected.

            Required format:
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId": "{{groupId}}",
              "taskId": "{{taskId}}",
              "revision": "{{revision}}",
              "status": "complete",
              "completionKind": "changed",
              "commit": "<exact abbreviated SHA previously returned by git rev-parse --short HEAD>",
              "summary": "one-sentence outcome retained for compatibility",
              "completedWork": ["one concrete completed outcome per array item"],
              "remainingWork": [],
              "deferredWork": [],
            {{executionAttemptJsonFields}}
              "verification": {
                "status": "passed",
                "command": "<exact command used to verify>",
                "summary": "<what passed>"
              }
            }

            Schema reminders:
            - Preserve every valid field from the previous handoff; change only the invalid field(s) named by the validation error and any field required by this schema.
            - Treat `completionKind` as the discriminator: a `changed` object uses `commit` and MUST omit `evaluatedCommit` and `noOpEvidence`; a `verified-no-op` object uses `evaluatedCommit` plus `noOpEvidence` and MUST omit `commit`.
            - `completionKind` is only valid when `status` is `complete`.
            - For `partial` or `failed`, omit `completionKind`, `evaluatedCommit`, and `noOpEvidence`; a `partial` result must include non-empty `remainingWork`.
            - For a `complete` changed result, keep `completionKind: "changed"`, include the exact commit, and keep passed verification evidence.

            Preserve every deliberate deferral from the previous handoff. Each deferredWork entry must name the exact requirement, reason, and downstream ownerTaskIds. Do not invent a deferral while repairing the envelope.
            {{executionAttemptInstruction}}
            """;
    }

    private static string BuildNonCompleteCorrection(
        string groupId,
        string taskId,
        string revision,
        string reason,
        string previousStatus,
        string? executionAttemptId,
        IReadOnlyList<DecomposedAgentAssignment>? assignments)
    {
        var status = string.Equals(previousStatus, "failed", StringComparison.Ordinal)
            ? "failed"
            : "partial";
        var executionAttemptJsonFields = BuildExecutionAttemptJsonFields(executionAttemptId, assignments);
        var executionAttemptInstruction = BuildExecutionAttemptInstruction(executionAttemptId, assignments);

        return $$"""
            SquadDash detected one completion-only field in a non-complete task result.
            Validation error: {{reason}}

            Correct only the result envelope. Do not re-run tools, re-examine files, repeat work, or
            turn an incomplete result into a completion claim. Preserve the previous `{{status}}`
            status and every truthful work, deferral, commit, and verification fact. Include the
            optional `commit` field only when the previous result safely created one. Remove
            `completionKind`, `evaluatedCommit`, and `noOpEvidence`; those fields are forbidden for
            `partial` and `failed` results.

            Authoritative identity for this execution attempt: groupId `{{groupId}}`, taskId
            `{{taskId}}`, revision `{{revision}}`.

            Required {{status}} variant:
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId": "{{groupId}}",
              "taskId": "{{taskId}}",
              "revision": "{{revision}}",
              "status": "{{status}}",
              "summary": "<preserve the truthful prior summary>",
              "completedWork": ["<preserve each concrete completed outcome>"],
              "remainingWork": ["<concrete work or human evidence still required>"],
              "deferredWork": [],
            {{executionAttemptJsonFields}}
              "verification": {
                "status": "<passed or failed, matching what actually occurred>",
                "command": "<exact command or inspection actually performed>",
                "summary": "<truthful verification outcome>"
              }
            }

            Return only the corrected envelope. Do not add any completion-only field.
            {{executionAttemptInstruction}}
            """;
    }

    private static string BuildVerifiedNoOpCorrection(
        string groupId,
        string taskId,
        string revision,
        string reason,
        bool envelopeWasPresent,
        string? executionAttemptId,
        IReadOnlyList<DecomposedAgentAssignment>? assignments)
    {
        var executionAttemptJsonFields = BuildExecutionAttemptJsonFields(executionAttemptId, assignments);
        var executionAttemptInstruction = BuildExecutionAttemptInstruction(executionAttemptId, assignments);

        return $$"""
            SquadDash detected that your previous response completed normally but its required DECOMPOSE_STEP_RESULT_JSON envelope {{(envelopeWasPresent ? "did not describe a valid completion" : "was missing or invalid")}}.
            Validation error: {{reason}}

            A normal implementation completion must create a new commit. If your completed inspection established that every task requirement was already satisfied at the exact repository snapshot you inspected, correct the envelope to the verified-no-op form below. This is a factual claim, not a way to bypass unfinished work: preserve concrete repository evidence from the inspection you already performed. Do not run tools, modify files, create an empty commit, or invent evidence.

            If the task was not already fully satisfied, do not claim verified-no-op. Return a truthful `partial` or `failed` result with concrete `remainingWork`; SquadDash can then route the task through its normal correction policy.

            Authoritative identity for this execution attempt: groupId `{{groupId}}`, taskId `{{taskId}}`, revision `{{revision}}`.

            Verified-no-op format:
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId": "{{groupId}}",
              "taskId": "{{taskId}}",
              "revision": "{{revision}}",
              "status": "complete",
              "completionKind": "verified-no-op",
              "commit": null,
              "evaluatedCommit": "<the exact HEAD SHA already inspected and previously reported>",
              "summary": "one-sentence explanation that the task was already satisfied",
              "completedWork": ["one already-satisfied outcome per array item"],
              "remainingWork": [],
              "deferredWork": [],
            {{executionAttemptJsonFields}}
              "noOpEvidence": [
                {
                  "requirement": "one exact current-task requirement",
                  "observation": "what existing implementation proves the requirement is already satisfied",
                  "locations": ["repository/path/File.cs:member-or-line"]
                }
              ],
              "verification": {
                "status": "passed",
                "command": "exact read-only command or inspection used to verify the existing behavior",
                "summary": "what the existing repository state proved"
              }
            }

            Return only one corrected result envelope. SquadDash will require `evaluatedCommit` to identify the current HEAD and will require an independent Fact Checker review of the no-op claim before accepting it. Concurrent commits are not attributed to this task.
            {{executionAttemptInstruction}}
            """;
    }

    private static string BuildExecutionAttemptJsonFields(
        string? executionAttemptId,
        IReadOnlyList<DecomposedAgentAssignment>? assignments)
    {
        if (string.IsNullOrWhiteSpace(executionAttemptId))
            return string.Empty;

        var attemptId = executionAttemptId.Trim();
        var reports = (assignments ?? [])
            .Where(assignment => !string.IsNullOrWhiteSpace(assignment.AgentHandle))
            .Select(assignment => new
            {
                requestedAgent = assignment.AgentHandle.Trim(),
                actualPrimaryAgent = assignment.AgentHandle.Trim(),
            })
            .ToArray();
        if (reports.Length == 0)
        {
            return $$"""
              "executionAttemptId": {{JsonSerializer.Serialize(attemptId)}},
""";
        }

        return $$"""
              "executionAttemptId": {{JsonSerializer.Serialize(attemptId)}},
              "agentExecutions": {{JsonSerializer.Serialize(reports)}},
""";
    }

    private static string BuildExecutionAttemptInstruction(
        string? executionAttemptId,
        IReadOnlyList<DecomposedAgentAssignment>? assignments)
    {
        if (string.IsNullOrWhiteSpace(executionAttemptId))
            return string.Empty;

        var attemptId = executionAttemptId.Trim();
        var handles = (assignments ?? [])
            .Select(assignment => assignment.AgentHandle?.Trim())
            .Where(handle => !string.IsNullOrWhiteSpace(handle))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (handles.Length == 0)
        {
            return $$"""

            This task already has host-owned execution attempt `{{attemptId}}`. Preserve the `executionAttemptId` field shown in the corrected envelope; SquadDash validates it against host-owned evidence and will reject the repair if it is omitted or changed. Do not add `agentExecutions` unless the corrected envelope schema above includes it.
            """;
        }

        return $$"""

            This assigned-agent task already has host-observed completion for {{string.Join(", ", handles)}}. Preserve the `executionAttemptId` and `agentExecutions` fields shown in the corrected envelope; SquadDash validates them against host-owned evidence and will reject the repair if they are omitted or changed.
            """;
    }

    private static bool IsCommitLookupFailure(string reason) =>
        reason.Contains("Git validation failed", StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("reported commit was missing or malformed", StringComparison.OrdinalIgnoreCase);

    private static bool IsMissingNewCommitFailure(string reason) =>
        reason.Contains("without creating a new commit", StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("A verified-no-op result requires", StringComparison.OrdinalIgnoreCase);

    private static bool HasViolation(
        IReadOnlyList<DecomposeStepResultViolation>? violations,
        DecomposeStepResultViolationCode code) =>
        (violations ?? []).Any(violation => violation.Code == code);

    private static bool RequiresVerifiedNoOpCorrection(
        IReadOnlyList<DecomposeStepResultViolation>? violations) =>
        (violations ?? []).Any(violation => violation.Code is
            DecomposeStepResultViolationCode.VerifiedNoOpEvaluatedCommitMissing or
            DecomposeStepResultViolationCode.VerifiedNoOpEvidenceMissing or
            DecomposeStepResultViolationCode.VerifiedNoOpEvidenceInvalid);

    private static string BuildFieldCorrections(
        IReadOnlyList<DecomposeStepResultViolation>? violations,
        DecomposeStepResult? previousResult)
    {
        var operations = (violations ?? [])
            .Where(violation => !string.IsNullOrWhiteSpace(violation.RepairOperation))
            .Select(violation => $"- `{violation.JsonPath ?? "/"}`: {violation.RepairOperation}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (operations.Length == 0)
            return "Change only the field or fields named by the validation error.";

        var prior = previousResult is null
            ? string.Empty
            : "\nThe host retained the prior candidate object; preserve all fields not listed above.";
        return "Apply these field-specific corrections (JSON-Patch semantics):\n" +
               string.Join("\n", operations) + prior;
    }

    private static bool IsCompletionKindOnNonCompleteResult(string reason, string? previousStatus) =>
        previousStatus is "partial" or "failed" &&
        reason.Contains("completionKind is only valid for a complete result", StringComparison.OrdinalIgnoreCase);

    internal static string BuildProofEvidenceCorrection(
        IReadOnlyList<DecomposedTaskProofRequirement> requirements)
    {
        var evidenceTemplate = requirements.Select(requirement => new
        {
            requirementId = requirement.RequirementId,
            proofType = requirement.ProofType,
            summary = $"<what was actually observed for: {requirement.Description}>",
            artifacts = Array.Empty<string>(),
        });

        return "The approved task requires `proofEvidence`. Add the following array to the result " +
               "envelope. Return one object per requirement, preserve each requirementId and proofType " +
               "exactly, and replace each summary placeholder with the evidence actually observed. " +
               "`summary` is required; do not substitute `description`, `detail`, or `passed`.\n" +
               "\"proofEvidence\": " + JsonSerializer.Serialize(evidenceTemplate);
    }

    internal static string BuildInboxLinkCorrection(string validationError) =>
        $$"""


        The task's required Inbox delivery was withheld because its operator package did not validate.
        Correct every error below before re-emitting it:
        {{validationError}}

        This delivery correction is the sole exception to the instruction above to return only the
        result envelope. After the corrected DECOMPOSE_STEP_RESULT_JSON, re-emit exactly one complete
        INBOX_MESSAGE_JSON or INBOX_MESSAGE_JSON_FILE block containing the corrected concise manual-test
        procedure. Do not repeat implementation work. You may inspect only the information needed to
        correct the delivery. The approval message itself must state every required operator action;
        do not tell the reviewer to follow a runbook, another Inbox message, kit instructions, linked
        steps, or an unspecified file/location. A direct artifact link supports the inline action but
        never replaces it. Do not create a separate operator runbook. Compose the approval procedure as
        a whole; group requirements with shared setup, actions, and cleanup, while keeping every expected
        observation and approval marker independent. Put exact text the reviewer must copy, type, paste,
        or submit alone in a fenced `text` or appropriate-language block immediately after its introducing
        step so SquadDash can render its standard copy button. Use each required `{{"{{approval-check:requirement-id}}"}}` marker exactly
        once with its real stable ID, immediately after that check's expected result. Resolve every relative `app://open-path:`
        target from the active SquadDash workspace directory, not from the Git repository root or the
        shell's current directory, and confirm each resolved file or folder exists before returning it.
        If a target is outside the active workspace, use its absolute path. Preserve the rest of the
        valid Inbox content. Return all corrections together; do not make the human reconcile partial attempts.
        """;
}
