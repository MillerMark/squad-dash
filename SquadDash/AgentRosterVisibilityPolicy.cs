using System;
using System.IO;

namespace SquadDash;

internal static class AgentRosterVisibilityPolicy {
    public static bool ShouldShow(AgentStatusCard agent) {
        ArgumentNullException.ThrowIfNull(agent);

        if (!agent.IsUtilityAgent)
            return true;

        return IsVisibleUtilityAgent(agent.Name, agent.FolderPath);
    }

    internal static bool IsScribeAgent(string? name, string? folderPath) =>
        IsScribe(name) || IsScribeFolder(folderPath);

    internal static bool IsVisibleUtilityAgent(string? name, string? folderPath) =>
        IsScribe(name) ||
        IsScribeFolder(folderPath) ||
        IsRory(name) ||
        IsRoryFolder(folderPath) ||
        FactCheckerInstructions.IsAgent(name, folderPath);

    private static bool IsScribe(string? value) =>
        string.Equals(value?.Trim(), "Scribe", StringComparison.OrdinalIgnoreCase);

    private static bool IsRory(string? value) =>
        string.Equals(value?.Trim(), "Rory", StringComparison.OrdinalIgnoreCase);

    private static bool IsScribeFolder(string? folderPath) {
        return IsUtilityFolder(folderPath, IsScribe);
    }

    private static bool IsRoryFolder(string? folderPath) {
        return IsUtilityFolder(folderPath, IsRoryFolderName);
    }

    private static bool IsRoryFolderName(string? value) =>
        string.Equals(value?.Trim(), SquadPlanningWorkspaceRepairService.AgentFolderName,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsUtilityFolder(string? folderPath, Func<string?, bool> matches) {
        if (string.IsNullOrWhiteSpace(folderPath))
            return false;

        var normalized = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folderName = Path.GetFileName(normalized);
        return matches(folderName);
    }
}
