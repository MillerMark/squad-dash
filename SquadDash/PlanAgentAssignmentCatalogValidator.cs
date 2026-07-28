using System;
using System.IO;
using System.Linq;

namespace SquadDash;

internal static class PlanAgentAssignmentCatalogValidator
{
    internal static bool TryValidate(
        DecomposedTaskGroup group,
        string squadFolderPath,
        out string? error)
    {
        error = null;
        var teamPath = Path.Combine(squadFolderPath, "team.md");
        var roster = PlanStepAgentResolver.ParseTeamMd(
            File.Exists(teamPath) ? File.ReadAllText(teamPath) : string.Empty);

        foreach (var task in group.Tasks)
        foreach (var assignment in task.AgentAssignments ?? [])
        {
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

        return true;
    }
}
