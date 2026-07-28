using System.Collections.Generic;
using System.IO;

namespace SquadDash;

/// <summary>
/// Routing decision for the current plan step — computed once per loop iteration
/// and injected into the loop system prompt context.
/// </summary>
internal sealed record PlanStepRoutingContext(
    string                 StepId,
    string                 StepTitle,
    string                 StepDescription,
    AgentRoutingResolution Resolution,
    string?                CharterContent)
{
    internal static PlanStepRoutingContext Resolve(
        string                      stepId,
        string                      stepTitle,
        string                      stepDescription,
        string                      squadFolderPath,
        IReadOnlyList<RoutingRule>  rules,
        IReadOnlyList<RosterAgent>  agents)
    {
        var resolver   = new PlanStepAgentResolver(rules, agents);
        var resolution = resolver.Resolve(stepTitle, stepDescription);

        string? charter = null;
        if (!resolution.IsGenericFallback && resolution.AgentHandle is not null)
        {
            var charterPath = Path.Combine(squadFolderPath, "agents", resolution.AgentHandle, "charter.md");
            if (File.Exists(charterPath))
                charter = File.ReadAllText(charterPath);
        }

        return new PlanStepRoutingContext(stepId, stepTitle, stepDescription, resolution, charter);
    }
}
