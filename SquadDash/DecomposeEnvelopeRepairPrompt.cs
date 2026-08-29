using System.Text.Json;

namespace SquadDash;

internal static class DecomposeEnvelopeRepairPrompt
{
    internal static string Build(
        string groupId,
        string taskId,
        string revision,
        string reason,
        bool envelopeWasPresent = false)
    {
        if (IsMissingNewCommitFailure(reason))
            return BuildVerifiedNoOpCorrection(groupId, taskId, revision, reason, envelopeWasPresent);

        var correctionInstruction = IsCommitLookupFailure(reason)
            ? "The reported commit did not resolve. Run only `git rev-parse --short HEAD`, copy its output exactly into `commit`, and do not inspect or change any files."
            : "Do not re-run any tools, re-examine files, or repeat previous work.";

        return $$"""
            SquadDash detected that your previous response completed normally but its required DECOMPOSE_STEP_RESULT_JSON envelope {{(envelopeWasPresent ? "did not match the required schema" : "was missing or invalid")}}.
            Validation error: {{reason}}

            Correct the validation error and provide ONLY the result envelope now, based on the work you just completed. {{correctionInstruction}}

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
              "verification": {
                "status": "passed",
                "command": "<exact command used to verify>",
                "summary": "<what passed>"
              }
            }

            Preserve every deliberate deferral from the previous handoff. Each deferredWork entry must name the exact requirement, reason, and downstream ownerTaskIds. Do not invent a deferral while repairing the envelope.
            """;
    }

    private static string BuildVerifiedNoOpCorrection(
        string groupId,
        string taskId,
        string revision,
        string reason,
        bool envelopeWasPresent) => $$"""
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
            """;

    private static bool IsCommitLookupFailure(string reason) =>
        reason.Contains("Git validation failed", StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("reported commit was missing or malformed", StringComparison.OrdinalIgnoreCase);

    private static bool IsMissingNewCommitFailure(string reason) =>
        reason.Contains("without creating a new commit", StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("verified-no-op", StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("noOpEvidence", StringComparison.OrdinalIgnoreCase);

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
}
