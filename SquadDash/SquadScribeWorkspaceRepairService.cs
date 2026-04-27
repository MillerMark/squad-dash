using System;
using System.IO;
using System.Linq;
using System.Text;

namespace SquadDash;

internal static class SquadScribeWorkspaceRepairService {
    private const string SessionLoggingRow = "| Session logging | Scribe | Automatic — never needs routing |";

    public static SquadScribeWorkspaceRepairResult Repair(string workspaceFolder) {
        if (string.IsNullOrWhiteSpace(workspaceFolder))
            throw new ArgumentException("Workspace folder cannot be empty.", nameof(workspaceFolder));

        var normalizedWorkspace = Path.GetFullPath(workspaceFolder);
        var squadFolder = Path.Combine(normalizedWorkspace, ".squad");
        if (!Directory.Exists(squadFolder)) {
            return new SquadScribeWorkspaceRepairResult(
                normalizedWorkspace,
                Repaired: false,
                CreatedScribeCharter: false,
                CreatedScribeHistory: false,
                CreatedDecisionLog: false,
                CreatedDirectories: 0,
                RoutingRepaired: false,
                "No .squad folder was found.");
        }

        var createdDirectories = 0;
        createdDirectories += EnsureDirectory(Path.Combine(squadFolder, "log"));
        createdDirectories += EnsureDirectory(Path.Combine(squadFolder, "orchestration-log"));
        createdDirectories += EnsureDirectory(Path.Combine(squadFolder, "decisions"));
        createdDirectories += EnsureDirectory(Path.Combine(squadFolder, "decisions", "inbox"));
        createdDirectories += EnsureDirectory(Path.Combine(squadFolder, "agents"));
        var scribeDirectory = Path.Combine(squadFolder, "agents", "scribe");
        createdDirectories += EnsureDirectory(scribeDirectory);

        var createdDecisionLog = EnsureDecisionLog(Path.Combine(squadFolder, "decisions.md"));
        var createdScribeCharter = EnsureScribeCharter(
            Path.Combine(scribeDirectory, "charter.md"),
            Path.Combine(squadFolder, "templates", "scribe-charter.md"));
        var createdScribeHistory = EnsureScribeHistory(
            Path.Combine(scribeDirectory, "history.md"),
            BuildProjectName(normalizedWorkspace));
        var routingRepaired = EnsureSessionLoggingRoute(Path.Combine(squadFolder, "routing.md"));

        var repaired = createdDirectories > 0 ||
                       createdDecisionLog ||
                       createdScribeCharter ||
                       createdScribeHistory ||
                       routingRepaired;

        var summary = BuildSummary(
            createdDirectories,
            createdDecisionLog,
            createdScribeCharter,
            createdScribeHistory,
            routingRepaired,
            repaired);

        return new SquadScribeWorkspaceRepairResult(
            normalizedWorkspace,
            repaired,
            createdScribeCharter,
            createdScribeHistory,
            createdDecisionLog,
            createdDirectories,
            routingRepaired,
            summary);
    }

    private static int EnsureDirectory(string path) {
        if (Directory.Exists(path))
            return 0;

        Directory.CreateDirectory(path);
        return 1;
    }

    private static bool EnsureDecisionLog(string decisionsPath) {
        if (File.Exists(decisionsPath))
            return false;

        File.WriteAllText(
            decisionsPath,
            "# Team Decisions" + Environment.NewLine + Environment.NewLine,
            Encoding.UTF8);
        return true;
    }

    private static bool EnsureScribeCharter(string charterPath, string templatePath) {
        if (File.Exists(charterPath))
            return false;

        var content = File.Exists(templatePath)
            ? File.ReadAllText(templatePath)
            : """
              # Scribe

              > The team's memory. Silent, always present, never forgets.

              ## Identity

              - **Name:** Scribe
              - **Role:** Session Logger, Memory Manager & Decision Merger
              - **Style:** Silent. Never speaks to the user. Works in the background.
              - **Mode:** Always spawned as `mode: "background"`. Never blocks the conversation.
              """;

        File.WriteAllText(charterPath, NormalizeDocument(content), Encoding.UTF8);
        return true;
    }

    private static bool EnsureScribeHistory(string historyPath, string projectName) {
        if (File.Exists(historyPath))
            return false;

        var joinedAt = DateTimeOffset.UtcNow.ToString("O");
        var content = $"""
                       # Scribe — History

                       ## Core Context

                       - **Project:** {projectName}
                       - **Role:** Session Logger
                       - **Joined:** {joinedAt}

                       ## Learnings

                       <!-- Append learnings below -->
                       """;

        File.WriteAllText(historyPath, NormalizeDocument(content), Encoding.UTF8);
        return true;
    }

    private static bool EnsureSessionLoggingRoute(string routingPath) {
        if (!File.Exists(routingPath))
            return false;

        var original = File.ReadAllText(routingPath);
        var normalized = NormalizeDocument(original);
        var lines = normalized
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToList();

        for (var index = 0; index < lines.Count; index++) {
            if (!lines[index].TrimStart().StartsWith("| Session logging |", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(lines[index].Trim(), SessionLoggingRow, StringComparison.Ordinal))
                return false;

            lines[index] = SessionLoggingRow;
            File.WriteAllText(routingPath, NormalizeDocument(string.Join("\n", lines)), Encoding.UTF8);
            return true;
        }

        var routingTableHeaderIndex = lines.FindIndex(line =>
            line.Trim().Equals("## Routing Table", StringComparison.OrdinalIgnoreCase));
        if (routingTableHeaderIndex < 0)
            return false;

        var insertIndex = FindRoutingTableInsertIndex(lines, routingTableHeaderIndex);
        if (insertIndex < 0)
            return false;

        lines.Insert(insertIndex, SessionLoggingRow);
        File.WriteAllText(routingPath, NormalizeDocument(string.Join("\n", lines)), Encoding.UTF8);
        return true;
    }

    private static int FindRoutingTableInsertIndex(System.Collections.Generic.IReadOnlyList<string> lines, int routingTableHeaderIndex) {
        var headerRowFound = false;
        var separatorRowFound = false;

        for (var index = routingTableHeaderIndex + 1; index < lines.Count; index++) {
            var trimmed = lines[index].Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                return separatorRowFound ? index : -1;

            if (!headerRowFound) {
                if (trimmed.StartsWith("|", StringComparison.Ordinal) &&
                    trimmed.Contains("Work Type", StringComparison.OrdinalIgnoreCase)) {
                    headerRowFound = true;
                }

                continue;
            }

            if (!separatorRowFound) {
                if (trimmed.StartsWith("|", StringComparison.Ordinal) &&
                    trimmed.All(character => character is '|' or '-' or ':' or ' ')) {
                    separatorRowFound = true;
                }

                continue;
            }

            if (trimmed.StartsWith("|", StringComparison.Ordinal))
                continue;

            return index;
        }

        return separatorRowFound ? lines.Count : -1;
    }

    private static string BuildProjectName(string workspaceFolder) {
        var folderName = Path.GetFileName(workspaceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(folderName) ? "Workspace" : folderName;
    }

    private static string NormalizeDocument(string content) {
        var normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd();
        return normalized + Environment.NewLine;
    }

    private static string BuildSummary(
        int createdDirectories,
        bool createdDecisionLog,
        bool createdScribeCharter,
        bool createdScribeHistory,
        bool routingRepaired,
        bool repaired) {
        if (!repaired)
            return "Scribe support already looks healthy.";

        var updates = new System.Collections.Generic.List<string>();
        if (createdDirectories > 0)
            updates.Add($"created {createdDirectories} missing Scribe support director{(createdDirectories == 1 ? "y" : "ies")}");
        if (createdDecisionLog)
            updates.Add("seeded decisions.md");
        if (createdScribeCharter)
            updates.Add("seeded scribe/charter.md");
        if (createdScribeHistory)
            updates.Add("seeded scribe/history.md");
        if (routingRepaired)
            updates.Add("restored Session logging routing to Scribe");

        return "Repaired Scribe workspace support: " + string.Join(", ", updates) + ".";
    }
}

internal sealed record SquadScribeWorkspaceRepairResult(
    string WorkspaceFolder,
    bool Repaired,
    bool CreatedScribeCharter,
    bool CreatedScribeHistory,
    bool CreatedDecisionLog,
    int CreatedDirectories,
    bool RoutingRepaired,
    string Summary);
