using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

internal static class PlanAssignmentRepairPrompt
{
    internal static string Build(
        string groupId,
        string taskId,
        string revision,
        IReadOnlyList<DecomposedAgentAssignment> assignments,
        string routingContext,
        string reason)
    {
        var handles = string.Join(", ", assignments.Select(a => a.AgentHandle));
        return $$"""
            SquadDash rejected the previous task result because the required roster-agent delegation contract was not satisfied.
            Reason: {{reason}}

            Required primary assignments: {{handles}}

            Correct the delegation now. Launch the missing required primary assignment using the exact host-scoped envelope and complete charter supplied below. A prompt that merely says "you are" that agent is not sufficient. The assigned worker may inspect and validate preserved work, complete missing work, and create or adopt the single task commit. Wait for the worker, perform coordinator wrap-up, then return exactly one DECOMPOSE_STEP_RESULT_JSON for group {{groupId}}, task {{taskId}}, revision {{revision}}. Include the supplied `executionAttemptId` and one `agentExecutions` entry with `requestedAgent`, `actualPrimaryAgent`, `primaryToolCallId`, and direct child tool-call IDs in `children`.

            {{routingContext}}
            """;
    }
}
