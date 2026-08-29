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

            Begin the fresh host-owned attempt supplied below. Its failure and retry history starts empty. Do not try to repair or cite the closed attempt, and do not carry any delegation, provider, quota, or runtime failure from it into this attempt. Preserve all existing commits and valid repository work: inspect the current state, have the assigned primary worker validate what is already complete without repeating completed implementation, and finish only what remains. If the current inspected snapshot already satisfies every task requirement, make no edits and return a `verified-no-op` result with requirement-scoped repository evidence and the current HEAD in `evaluatedCommit`. Do not attribute concurrent commits after the task baseline to this step. Otherwise create or adopt the one clean changed result required by the task. Do not interrupt the plan merely because this audit is needed. Wait for the assigned worker and perform coordinator wrap-up. Then return exactly one DECOMPOSE_STEP_RESULT_JSON for group {{groupId}}, task {{taskId}}, revision {{revision}} using the new `executionAttemptId`. Never claim an actual primary agent unless the current host-scoped delegation call completed.

            {{routingContext}}
            """;
}
