using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

internal static class PlanAssignmentWrapUpRepairPrompt
{
    internal static string Build(
        string groupId,
        string taskId,
        string revision,
        string attemptId,
        IReadOnlyList<DecomposedAgentAssignment> assignments,
        string reason)
    {
        var reports = string.Join(", ", assignments.Select(assignment =>
            $"{{\"requestedAgent\":\"{assignment.AgentHandle}\",\"actualPrimaryAgent\":\"{assignment.AgentHandle}\"}}"));
        return $$"""
            SquadDash verified the host-observed assigned workers, but the coordinator result omitted or misstated its structured wrap-up.
            Reason: {{reason}}

            Do not launch another worker and do not run more tools. Return only the corrected DECOMPOSE_STEP_RESULT_JSON for group {{groupId}}, task {{taskId}}, revision {{revision}}. Set `executionAttemptId` to `{{attemptId}}` and set `agentExecutions` to [{{reports}}]. Preserve the commit, summary, remaining work, and verification facts from your previous response.
            """;
    }
}
