using System;
using System.IO;
using System.Linq;

namespace SquadDash;

internal static class PlanAgentAssignmentCatalogValidator
{
    internal static bool TryValidate(
        DecomposedTaskGroup group,
        string squadFolderPath,
        out string? error,
        bool requireExplicitRouting = false,
        bool enforceAssignments = true)
    {
        error = null;
        var teamPath = Path.Combine(squadFolderPath, "team.md");
        var roster = PlanStepAgentResolver.ParseTeamMd(
            File.Exists(teamPath) ? File.ReadAllText(teamPath) : string.Empty);

        foreach (var task in group.Tasks)
        {
            if (!enforceAssignments)
                continue;

            if (task.AgentAssignments is { Count: > 1 })
            {
                error = $"Task {task.Id} assigns multiple primary agents. " +
                        "Parallel primary execution is unavailable until SquadDash can isolate each writer in its own worktree.";
                return false;
            }

            if (requireExplicitRouting && string.IsNullOrWhiteSpace(task.AgentRoutingMode))
            {
                error = $"Task {task.Id} must explicitly use agentRoutingMode 'assigned' or 'generic'.";
                return false;
            }
            if (string.Equals(task.AgentRoutingMode, "assigned", StringComparison.Ordinal) &&
                task.AgentAssignments is not { Count: 1 })
            {
                error = $"Task {task.Id} selects assigned routing but does not have exactly one primary assignment.";
                return false;
            }
            if (string.Equals(task.AgentRoutingMode, "generic", StringComparison.Ordinal) &&
                (task.AgentAssignments is { Count: > 0 } || string.IsNullOrWhiteSpace(task.GenericAgentReason)))
            {
                error = $"Task {task.Id} selects generic routing without a reason, or also declares an assignment.";
                return false;
            }

            foreach (var assignment in task.AgentAssignments ?? [])
            {
                if (assignment.AllowGenericChildren)
                {
                    error = $"Task {task.Id} allows generic child workers. " +
                            "Child workers are temporarily unavailable until SquadDash can enforce read-only isolation.";
                    return false;
                }
                var agent = roster.FirstOrDefault(candidate =>
                    candidate.IsActive &&
                    string.Equals(candidate.Handle, assignment.AgentHandle, StringComparison.OrdinalIgnoreCase));
                if (agent is null)
                {
                    error = $"Task {task.Id} assigns unavailable active roster agent '{assignment.AgentHandle}'.";
                    return false;
                }

                var charterPath = agent.CharterPath is { Length: > 0 }
                    ? Path.Combine(squadFolderPath, agent.CharterPath.Replace('/', Path.DirectorySeparatorChar))
                    : Path.Combine(squadFolderPath, "agents", agent.Handle, "charter.md");
                if (!File.Exists(charterPath))
                {
                    error = $"Task {task.Id} assigns '{assignment.AgentHandle}', but its charter is missing.";
                    return false;
                }
            }
        }

        return true;
    }
}
