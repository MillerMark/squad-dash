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
        return $$"""
            SquadDash detected that your previous response completed normally but its required DECOMPOSE_STEP_RESULT_JSON envelope {{(envelopeWasPresent ? "did not match the required schema" : "was missing or invalid")}}.
            Validation error: {{reason}}

            Correct the validation error and provide ONLY the result envelope now, based on the work you just completed. Do not re-run any tools, re-examine files, or repeat previous work.

            Required format:
            DECOMPOSE_STEP_RESULT_JSON:
            {
              "groupId": "{{groupId}}",
              "taskId": "{{taskId}}",
              "revision": "{{revision}}",
              "status": "complete",
              "commit": "<7-char SHA of the commit you made>",
              "summary": "concise description of what was done",
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
}
