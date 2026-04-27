using System;
using System.IO;

namespace SquadDash;

internal static class AgentRosterVisibilityPolicy {
    public static bool ShouldShow(AgentStatusCard agent) {
        ArgumentNullException.ThrowIfNull(agent);

        if (!agent.IsUtilityAgent)
            return true;

        return IsScribe(agent.Name) || IsScribeFolder(agent.FolderPath);
    }

    internal static bool IsScribeAgent(string? name, string? folderPath) =>
        IsScribe(name) || IsScribeFolder(folderPath);

    private static bool IsScribe(string? value) =>
        string.Equals(value?.Trim(), "Scribe", StringComparison.OrdinalIgnoreCase);

    private static bool IsScribeFolder(string? folderPath) {
        if (string.IsNullOrWhiteSpace(folderPath))
            return false;

        var normalized = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folderName = Path.GetFileName(normalized);
        return IsScribe(folderName);
    }
}
