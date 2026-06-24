using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SquadDash;

internal static class SquadTeamRootRepairService {
    // Squad-specific folder names that are safe to remove when duplicated at the workspace root.
    private static readonly string[] SquadOwnedFolderNames = [
        "agents", "casting", "code-health-reports", "orchestration-log"
    ];

    // Squad-specific file names that are safe to remove when duplicated at the workspace root.
    private static readonly string[] SquadOwnedFileNames = [
        "code-health.md"
    ];

    public static SquadTeamRootAssessment Assess(string workspaceFolder) {
        var normalized = Path.GetFullPath(workspaceFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var squadDir = Path.Combine(normalized, ".squad");
        if (!Directory.Exists(squadDir))
            return SquadTeamRootAssessment.Clean(normalized);

        var configPath = Path.Combine(squadDir, "config.json");
        var configNeedsRepair = File.Exists(configPath) && IsTeamRootMisconfigured(configPath, normalized);
        var pollution = FindRootPollution(normalized, squadDir);

        return new SquadTeamRootAssessment(normalized, configNeedsRepair, pollution);
    }

    public static bool RepairConfig(string workspaceFolder) {
        var normalized = Path.GetFullPath(workspaceFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configPath = Path.Combine(normalized, ".squad", "config.json");

        if (!File.Exists(configPath))
            return false;

        try {
            var json = File.ReadAllText(configPath);
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj)
                return false;

            obj["teamRoot"] = ".squad";
            var updated = obj.ToJsonString(JsonFileStorage.PrettyPrint);
            File.WriteAllText(configPath, updated + Environment.NewLine, Encoding.UTF8);
            return true;
        }
        catch {
            return false;
        }
    }

    public static SquadTeamRootCleanupResult CleanRootPollution(string workspaceFolder) {
        var normalized = Path.GetFullPath(workspaceFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var squadDir = Path.Combine(normalized, ".squad");
        var cleaned = new List<string>();
        var failed = new List<string>();

        foreach (var name in SquadOwnedFolderNames) {
            var rootPath = Path.Combine(normalized, name);
            if (!Directory.Exists(rootPath) || !Directory.Exists(Path.Combine(squadDir, name)))
                continue;

            try {
                Directory.Delete(rootPath, recursive: true);
                cleaned.Add(name + "/");
            }
            catch (Exception ex) {
                failed.Add($"{name}/: {ex.Message}");
            }
        }

        foreach (var name in SquadOwnedFileNames) {
            var rootPath = Path.Combine(normalized, name);
            if (!File.Exists(rootPath) || !File.Exists(Path.Combine(squadDir, name)))
                continue;

            try {
                File.Delete(rootPath);
                cleaned.Add(name);
            }
            catch (Exception ex) {
                failed.Add($"{name}: {ex.Message}");
            }
        }

        foreach (var file in EnumerateLoopFiles(normalized)) {
            var name = Path.GetFileName(file);
            try {
                File.Delete(file);
                cleaned.Add(name);
            }
            catch (Exception ex) {
                failed.Add($"{name}: {ex.Message}");
            }
        }

        return new SquadTeamRootCleanupResult(cleaned, failed);
    }

    private static bool IsTeamRootMisconfigured(string configPath, string workspaceFolder) {
        try {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = doc.RootElement;
            if (!root.TryGetProperty("teamRoot", out var prop) || prop.ValueKind != JsonValueKind.String)
                return false;

            var teamRoot = prop.GetString();
            if (string.IsNullOrWhiteSpace(teamRoot))
                return false;

            var trimmed = teamRoot.Trim();

            if (trimmed == ".")
                return true;

            if (Path.IsPathRooted(trimmed))
                return PathsEqual(Path.GetFullPath(trimmed), workspaceFolder);

            // Relative path from the workspace root: check if it resolves back to workspace root.
            var resolved = Path.GetFullPath(Path.Combine(workspaceFolder, trimmed));
            return PathsEqual(resolved, workspaceFolder);
        }
        catch {
            return false;
        }
    }

    private static IReadOnlyList<string> FindRootPollution(string workspaceFolder, string squadDir) {
        var items = new List<string>();

        foreach (var name in SquadOwnedFolderNames) {
            if (Directory.Exists(Path.Combine(workspaceFolder, name)) &&
                Directory.Exists(Path.Combine(squadDir, name)))
                items.Add(name + "/");
        }

        foreach (var name in SquadOwnedFileNames) {
            if (File.Exists(Path.Combine(workspaceFolder, name)) &&
                File.Exists(Path.Combine(squadDir, name)))
                items.Add(name);
        }

        items.AddRange(EnumerateLoopFiles(workspaceFolder).Select(Path.GetFileName).OfType<string>());

        return items;
    }

    private static IEnumerable<string> EnumerateLoopFiles(string folder) {
        try {
            return Directory.EnumerateFiles(folder, "loop-*.md", SearchOption.TopDirectoryOnly);
        }
        catch {
            return [];
        }
    }

    private static bool PathsEqual(string left, string right) {
        var nl = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var nr = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(nl, nr, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record SquadTeamRootAssessment(
    string WorkspaceFolder,
    bool ConfigNeedsRepair,
    IReadOnlyList<string> PollutionItems) {

    public bool HasPollution => PollutionItems.Count > 0;

    public static SquadTeamRootAssessment Clean(string workspaceFolder) =>
        new(workspaceFolder, ConfigNeedsRepair: false, PollutionItems: []);
}

internal sealed record SquadTeamRootCleanupResult(
    IReadOnlyList<string> CleanedItems,
    IReadOnlyList<string> FailedItems) {

    public bool AnySuccess => CleanedItems.Count > 0;
    public bool AnyFailure => FailedItems.Count > 0;
}
