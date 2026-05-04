using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SquadDash;

internal sealed class SquadTeamRosterLoader {
    private static readonly string[] UtilityAgentFolderNames = ["ralph", "scribe"];

    internal static bool HasNonUtilityMembers(IEnumerable<SquadTeamMember> members) =>
        members.Any(member => !member.IsUtilityAgent);

    internal static IReadOnlyList<string> GetMissingUtilityAgentNames(string workspaceFolder) {
        if (string.IsNullOrWhiteSpace(workspaceFolder))
            return UtilityAgentFolderNames.Select(TitleCase).ToArray();

        var normalizedWorkspace = Path.GetFullPath(workspaceFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var squadFolder = Path.Combine(normalizedWorkspace, ".squad");
        var agentsFolder = Path.Combine(normalizedWorkspace, ".squad", "agents");
        var listedMembers = LoadFromTeamFile(normalizedWorkspace, Path.Combine(squadFolder, "team.md"));
        var listedUtilityNames = new HashSet<string>(
            listedMembers
                .Where(member => member.IsUtilityAgent)
                .Select(member => member.Name),
            StringComparer.OrdinalIgnoreCase);

        return UtilityAgentFolderNames
            .Where(folderName =>
                !Directory.Exists(Path.Combine(agentsFolder, folderName)) &&
                !listedUtilityNames.Contains(TitleCase(folderName)))
            .Select(TitleCase)
            .ToArray();
    }

    public IReadOnlyList<SquadTeamMember> Load(string workspaceFolder) {
        if (string.IsNullOrWhiteSpace(workspaceFolder))
            throw new ArgumentException("Workspace folder cannot be empty.", nameof(workspaceFolder));

        var normalizedWorkspace = Path.GetFullPath(workspaceFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var squadFolder = Path.Combine(normalizedWorkspace, ".squad");
        var agentsFolder = Path.Combine(squadFolder, "agents");

        // Read registry.json to get authoritative retirement status (may differ from team.md Status column).
        var retiredSlugs = LoadRetiredSlugsFromRegistry(squadFolder);

        var members = LoadFromTeamFile(normalizedWorkspace, Path.Combine(squadFolder, "team.md"), retiredSlugs);
        if (members.Count == 0)
            return LoadFromAgentFolders(agentsFolder);

        var existingFolders = new HashSet<string>(
            members
                .Select(member => member.FolderPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => NormalizePath(path!)),
            StringComparer.OrdinalIgnoreCase);

        var existingNames = new HashSet<string>(
            members
                .Select(member => member.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var utilityFolderName in UtilityAgentFolderNames) {
            var folderPath = Path.Combine(agentsFolder, utilityFolderName);
            if (!Directory.Exists(folderPath))
                continue;

            var normalizedFolderPath = NormalizePath(folderPath);
            if (existingFolders.Contains(normalizedFolderPath))
                continue;

            var folderMember = BuildFromDirectory(folderPath, isUtility: true);
            if (existingNames.Contains(folderMember.Name))
                continue;

            members.Add(folderMember);
        }

        return members;
    }

    /// <summary>
    /// Reads .squad/casting/registry.json and returns the set of agent slugs (folder-name keys)
    /// whose status is "retired". Returns an empty set if the file is absent or unreadable.
    /// </summary>
    private static HashSet<string> LoadRetiredSlugsFromRegistry(string squadFolder) {
        var registryPath = Path.Combine(squadFolder, "casting", "registry.json");
        if (!File.Exists(registryPath))
            return [];

        try {
            using var stream = File.OpenRead(registryPath);
            using var doc    = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("agents", out var agents))
                return [];

            var retired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in agents.EnumerateObject()) {
                if (entry.Value.TryGetProperty("status", out var statusProp) &&
                    string.Equals(statusProp.GetString(), "retired", StringComparison.OrdinalIgnoreCase))
                    retired.Add(entry.Name);
            }
            return retired;
        }
        catch {
            return [];
        }
    }

    private static List<SquadTeamMember> LoadFromTeamFile(string workspaceFolder, string teamFilePath, HashSet<string>? retiredSlugs = null) {
        if (!File.Exists(teamFilePath))
            return [];

        var tableRows = ReadMembersTable(teamFilePath);
        if (tableRows.Count < 2)
            return [];

        var header = tableRows[0];
        var nameIndex = FindColumnIndex(header, "Name");
        var roleIndex = FindColumnIndex(header, "Role");
        var charterIndex = FindColumnIndex(header, "Charter");
        var statusIndex = FindColumnIndex(header, "Status");

        if (nameIndex < 0)
            return [];

        var members = new List<SquadTeamMember>();
        foreach (var row in tableRows.Skip(1)) {
            var name = GetCell(row, nameIndex);
            var role = GetCell(row, roleIndex);
            var charter = GetCell(row, charterIndex);
            var status = GetCell(row, statusIndex);

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(charter))
                continue;

            members.Add(BuildFromTeamRow(workspaceFolder, name, role, charter, status, retiredSlugs));
        }

        return members;
    }

    private static IReadOnlyList<SquadTeamMember> LoadFromAgentFolders(string agentsFolder) {
        if (!Directory.Exists(agentsFolder))
            return [];

        return Directory
            .GetDirectories(agentsFolder)
            .Select(directory => BuildFromDirectory(
                directory,
                IsUtilityDirectory(directory)))
            .OrderBy(member => member.IsUtilityAgent)
            .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SquadTeamMember BuildFromTeamRow(
        string workspaceFolder,
        string? name,
        string? role,
        string? charterPathText,
        string? status,
        HashSet<string>? retiredSlugs = null) {
        var charterPath = ResolvePath(workspaceFolder, charterPathText);
        var folderPath = !string.IsNullOrWhiteSpace(charterPath)
            ? Path.GetDirectoryName(charterPath)
            : null;
        var metadata = ReadCharterMetadata(charterPath);
        var normalizedFolderPath = NormalizeNullablePath(folderPath);
        var displayName = FirstNonEmpty(
                name,
                metadata.Name,
                normalizedFolderPath is null ? null : TitleCase(Path.GetFileName(normalizedFolderPath)))
            ?? "Agent";
        var displayRole = FirstNonEmpty(role, metadata.Role) ?? string.Empty;
        var isUtility = IsUtilityIdentity(displayName, normalizedFolderPath);

        // registry.json is the authoritative source for retirement; override team.md Status if needed.
        var folderSlug = normalizedFolderPath is not null ? Path.GetFileName(normalizedFolderPath) : null;
        if (retiredSlugs is not null && folderSlug is not null && retiredSlugs.Contains(folderSlug))
            status = "Retired";

        return new SquadTeamMember(
            displayName,
            displayRole,
            NormalizeStatus(status),
            charterPath,
            BuildHistoryPath(normalizedFolderPath),
            normalizedFolderPath,
            isUtility,
            GetAccentKey(normalizedFolderPath, displayName));
    }

    private static SquadTeamMember BuildFromDirectory(string directoryPath, bool isUtility) {
        var normalizedFolderPath = NormalizePath(directoryPath);
        var charterPath = Path.Combine(normalizedFolderPath, "charter.md");
        var metadata = ReadCharterMetadata(charterPath);
        var displayName = FirstNonEmpty(
                metadata.Name,
                TitleCase(Path.GetFileName(normalizedFolderPath)))
            ?? "Agent";
        var displayRole = metadata.Role ?? string.Empty;

        return new SquadTeamMember(
            displayName,
            displayRole,
            "Ready",
            File.Exists(charterPath) ? charterPath : null,
            BuildHistoryPath(normalizedFolderPath),
            normalizedFolderPath,
            isUtility,
            GetAccentKey(normalizedFolderPath, displayName));
    }

    private static List<string[]> ReadMembersTable(string teamFilePath) {
        var lines = File.ReadAllLines(teamFilePath);
        var rows = new List<string[]>();
        var inMembersSection = false;
        var inTable = false;

        foreach (var rawLine in lines) {
            var line = rawLine.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal)) {
                if (inTable)
                    break;

                inMembersSection = string.Equals(line, "## Members", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inMembersSection)
                continue;

            if (line.StartsWith("|", StringComparison.Ordinal)) {
                inTable = true;
                var cells = ParseMarkdownRow(line);
                if (cells.Length == 0 || IsSeparatorRow(cells))
                    continue;

                rows.Add(cells);
                continue;
            }

            if (inTable && line.Length > 0)
                break;
        }

        return rows;
    }

    private static string[] ParseMarkdownRow(string line) {
        return line
            .Trim()
            .Trim('|')
            .Split('|')
            .Select(cell => cell.Trim().Trim('`'))
            .ToArray();
    }

    private static bool IsSeparatorRow(IReadOnlyList<string> cells) {
        return cells.All(cell => cell.Length > 0 && cell.All(character => character is '-' or ':' or ' '));
    }

    private static int FindColumnIndex(IReadOnlyList<string> header, string columnName) {
        for (var index = 0; index < header.Count; index++) {
            if (string.Equals(header[index], columnName, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    private static string? GetCell(IReadOnlyList<string> row, int index) {
        return index >= 0 && index < row.Count
            ? row[index]
            : null;
    }

    private static string? ResolvePath(string workspaceFolder, string? pathText) {
        if (string.IsNullOrWhiteSpace(pathText))
            return null;

        var cleaned = pathText.Trim().Trim('`').Replace('/', Path.DirectorySeparatorChar);

        // Treat common placeholder values as absent
        if (cleaned is "—" or "-" or "–" or "N/A" or "n/a")
            return null;

        if (Path.IsPathRooted(cleaned))
            return NormalizePath(cleaned);

        // Try workspace-relative first, then .squad-relative as fallback
        var workspaceRelative = NormalizePath(Path.Combine(workspaceFolder, cleaned));
        if (File.Exists(workspaceRelative))
            return workspaceRelative;

        var squadRelative = NormalizePath(Path.Combine(workspaceFolder, ".squad", cleaned));
        if (File.Exists(squadRelative))
            return squadRelative;

        return workspaceRelative;
    }

    private static SquadAgentCharterMetadata ReadCharterMetadata(string? charterPath) {
        if (string.IsNullOrWhiteSpace(charterPath) || !File.Exists(charterPath))
            return SquadAgentCharterMetadata.Empty;

        string? headingName = null;
        string? headingRole = null;
        string? explicitName = null;
        string? explicitRole = null;

        foreach (var rawLine in File.ReadLines(charterPath).Take(32)) {
            var line = rawLine.Trim();
            if (headingName is null && line.StartsWith("#", StringComparison.Ordinal)) {
                var heading = line.TrimStart('#', ' ').Trim();
                if (!string.IsNullOrWhiteSpace(heading)) {
                    var separator = heading.IndexOf(" \u2014 ", StringComparison.Ordinal);
                    if (separator >= 0) {
                        headingName = heading[..separator].Trim();
                        headingRole = heading[(separator + 3)..].Trim();
                    }
                    else {
                        headingName = heading;
                    }
                }
            }

            if (line.StartsWith("- **Name:**", StringComparison.OrdinalIgnoreCase))
                explicitName = ExtractMarkdownFieldValue(line);

            if (line.StartsWith("- **Role:**", StringComparison.OrdinalIgnoreCase))
                explicitRole = ExtractMarkdownFieldValue(line);
        }

        return new SquadAgentCharterMetadata(
            FirstNonEmpty(explicitName, headingName),
            FirstNonEmpty(explicitRole, headingRole));
    }

    private static string? ExtractMarkdownFieldValue(string line) {
        var separator = line.IndexOf(':');
        if (separator < 0 || separator == line.Length - 1)
            return null;

        return line[(separator + 1)..].Trim().Trim('*').Trim();
    }

    private static string NormalizeStatus(string? status) {
        if (string.IsNullOrWhiteSpace(status))
            return "Ready";

        return status.Trim() switch {
            var value when value.Equals("Active", StringComparison.OrdinalIgnoreCase) => "Ready",
            var value when value.Equals("Watching", StringComparison.OrdinalIgnoreCase) => "Ready",
            var value when value.Equals("Observed", StringComparison.OrdinalIgnoreCase) => "Ready",
            _ => TitleCase(status)
        };
    }

    private static string GetAccentKey(string? folderPath, string displayName) {
        return !string.IsNullOrWhiteSpace(folderPath)
            ? Path.GetFileName(folderPath)
            : displayName;
    }

    private static string? BuildHistoryPath(string? folderPath) {
        if (string.IsNullOrWhiteSpace(folderPath))
            return null;

        var historyPath = Path.Combine(folderPath, "history.md");
        return File.Exists(historyPath) ? historyPath : null;
    }

    private static bool IsUtilityDirectory(string directoryPath) {
        var folderName = Path.GetFileName(directoryPath);
        return UtilityAgentFolderNames.Any(name =>
            string.Equals(name, folderName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUtilityIdentity(string displayName, string? folderPath) {
        if (!string.IsNullOrWhiteSpace(folderPath) && IsUtilityDirectory(folderPath))
            return true;

        return UtilityAgentFolderNames.Any(name =>
            string.Equals(TitleCase(name), displayName?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string? FirstNonEmpty(params string?[] values) {
        foreach (var value in values) {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static string? NormalizeNullablePath(string? path) {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : NormalizePath(path);
    }

    private static string NormalizePath(string path) {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string TitleCase(string value) {
        return string.Join(
            " ",
            value
                .Replace('-', ' ')
                .Replace('_', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(word => word.Length switch {
                    0 => word,
                    1 => char.ToUpperInvariant(word[0]).ToString(),
                    _ => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()
                }));
    }
}

internal sealed record SquadTeamMember(
    string Name,
    string Role,
    string Status,
    string? CharterPath,
    string? HistoryPath,
    string? FolderPath,
    bool IsUtilityAgent,
    string AccentKey);

internal sealed record SquadAgentCharterMetadata(string? Name, string? Role) {
    public static SquadAgentCharterMetadata Empty { get; } = new(null, null);
}
