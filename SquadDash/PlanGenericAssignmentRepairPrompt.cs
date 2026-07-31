namespace SquadDash;

internal static class PlanGenericAssignmentRepairPrompt
{
    internal static string Build(
        string groupId,
        string taskId,
        string revision,
        string reason,
        string routingContext) => $$"""
            SquadDash rejected the previous task result because the explicitly authorized generic primary worker was not launched.
            Reason: {{reason}}

            Launch exactly one generic primary worker using the current host-owned attempt below. Do not create any additional coordinator-owned primary workers or child workers. Wait for it to finish, perform coordinator wrap-up, and return exactly one DECOMPOSE_STEP_RESULT_JSON for group {{groupId}}, task {{taskId}}, revision {{revision}}. Include the supplied `executionAttemptId` and omit `agentExecutions`.

            {{routingContext}}
            """;
}
