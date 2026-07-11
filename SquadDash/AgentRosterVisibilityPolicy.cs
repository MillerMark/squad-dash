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
        IsFactChecker(name) ||
        IsFactCheckerFolder(folderPath);

    private static bool IsScribe(string? value) =>
        string.Equals(value?.Trim(), "Scribe", StringComparison.OrdinalIgnoreCase);

    private static bool IsFactChecker(string? value) =>
        string.Equals(value?.Trim(), "Fact Checker", StringComparison.OrdinalIgnoreCase);

    private static bool IsScribeFolder(string? folderPath) {
        return IsUtilityFolder(folderPath, IsScribe);
    }

    private static bool IsFactCheckerFolder(string? folderPath) {
        return IsUtilityFolder(folderPath, IsFactCheckerFolderName);
    }

    private static bool IsFactCheckerFolderName(string? value) =>
        string.Equals(value?.Trim(), "fact-checker", StringComparison.OrdinalIgnoreCase);

    private static bool IsUtilityFolder(string? folderPath, Func<string?, bool> matches) {
        if (string.IsNullOrWhiteSpace(folderPath))
            return false;

        var normalized = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folderName = Path.GetFileName(normalized);
        return matches(folderName);
    }
}
