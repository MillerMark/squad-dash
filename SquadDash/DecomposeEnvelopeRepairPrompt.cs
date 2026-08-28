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

    private static bool IsCommitLookupFailure(string reason) =>
        reason.Contains("Git validation failed", StringComparison.OrdinalIgnoreCase) ||
        reason.Contains("reported commit was missing or malformed", StringComparison.OrdinalIgnoreCase);

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
