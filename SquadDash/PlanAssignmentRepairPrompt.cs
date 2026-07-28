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

            Correct the delegation now. Launch every missing required primary assignment using the exact machine-readable envelope and charter supplied below. A prompt that merely says "you are" that agent is not sufficient. The assigned worker may inspect and validate preserved work, complete missing work, and create or adopt the single task commit. Wait for all required workers, perform coordinator wrap-up, then return exactly one DECOMPOSE_STEP_RESULT_JSON for group {{groupId}}, task {{taskId}}, revision {{revision}}. Include `agentExecutions`, one entry per required assignment, with `requestedAgent`, `actualPrimaryAgent`, and `children`.

            {{routingContext}}
            """;
    }
}
