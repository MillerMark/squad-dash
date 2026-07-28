namespace SquadDash;

internal static class DecomposeEnvelopeRepairPrompt
{
    internal static string Build(string groupId, string taskId, string revision, string reason)
    {
        return $$"""
            SquadDash detected that your previous response completed normally but was missing the required DECOMPOSE_STEP_RESULT_JSON envelope.
            Reason: {{reason}}

            Please provide ONLY the result envelope now, based on the work you just completed. Do not re-run any tools, re-examine files, or repeat previous work.

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
              "verification": {
                "status": "passed",
                "command": "<exact command used to verify>",
                "summary": "<what passed>"
              }
            }
            """;
    }
}
