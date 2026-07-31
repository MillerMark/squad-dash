namespace SquadDash;

internal static class PlanFreshAttemptPrompt
{
    internal static string Build(
        string groupId,
        string taskId,
        string revision,
        string reason,
        string routingContext) => $$"""
            SquadDash closed the previous plan-task execution attempt because its immutable host-owned launch evidence was contaminated.
            Reason: {{reason}}

            Begin the fresh host-owned attempt supplied below. Do not try to repair or cite the closed attempt. Preserve all existing commits and valid repository work: inspect the current state, have the assigned primary worker validate what is already complete, finish only what remains, and create or adopt the one clean verified result required by the task. Wait for the assigned worker and perform coordinator wrap-up. Then return exactly one DECOMPOSE_STEP_RESULT_JSON for group {{groupId}}, task {{taskId}}, revision {{revision}} using the new `executionAttemptId`.

            {{routingContext}}
            """;
}
