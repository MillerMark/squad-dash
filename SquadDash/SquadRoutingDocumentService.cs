using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace SquadDash;

internal sealed class SquadRoutingDocumentService {
    private const string BackupFileName = "routing.pre-repair.backup.md";

    private readonly SquadTeamRosterLoader _rosterLoader;

    public SquadRoutingDocumentService()
        : this(new SquadTeamRosterLoader()) {
    }

    internal SquadRoutingDocumentService(SquadTeamRosterLoader rosterLoader) {
        _rosterLoader = rosterLoader ?? throw new ArgumentNullException(nameof(rosterLoader));
    }

    public SquadRoutingDocumentAssessment Assess(string workspaceFolder) {
        if (string.IsNullOrWhiteSpace(workspaceFolder))
            throw new ArgumentException("Workspace folder cannot be empty.", nameof(workspaceFolder));

        var normalizedWorkspace = NormalizePath(workspaceFolder);
        var squadFolderPath = Path.Combine(normalizedWorkspace, ".squad");
        var routingFilePath = Path.Combine(squadFolderPath, "routing.md");
        if (!Directory.Exists(squadFolderPath)) {
            return CreateAssessment(
                normalizedWorkspace,
                routingFilePath,
                SquadRoutingDocumentStatus.NotApplicable,
                Array.Empty<SquadTeamMember>(),
                null,
                "This workspace does not have a .squad directory yet.");
        }

        var members = _rosterLoader.Load(normalizedWorkspace);
        var routingMembers = members
            .Where(member => !member.IsUtilityAgent)
            .ToArray();
        if (routingMembers.Length == 0) {
            return CreateAssessment(
                normalizedWorkspace,
                routingFilePath,
                SquadRoutingDocumentStatus.NotApplicable,
                members,
                null,
                "Routing validation is deferred until the team roster has at least one real member.");
        }

        if (!File.Exists(routingFilePath)) {
            return CreateAssessment(
                normalizedWorkspace,
                routingFilePath,
                SquadRoutingDocumentStatus.Missing,
                members,
                null,
                "routing.md is missing.");
        }

        var existingContent = File.ReadAllText(routingFilePath);
        if (IsUntouchedSeed(existingContent)) {
            return CreateAssessment(
                normalizedWorkspace,
                routingFilePath,
                SquadRoutingDocumentStatus.UnfilledSeed,
                members,
                existingContent,
                "routing.md is still an untouched template or empty seed.");
        }

        var parsedRules = ParseRoutingRows(existingContent);
        if (parsedRules.Count == 0) {
            return CreateAssessment(
                normalizedWorkspace,
                routingFilePath,
                SquadRoutingDocumentStatus.InvalidCustom,
                members,
                existingContent,
                "routing.md does not contain a parseable routing table.");
        }

        return CreateAssessment(
            normalizedWorkspace,
            routingFilePath,
            SquadRoutingDocumentStatus.HealthyCustom,
            members,
            existingContent,
            null);
    }

    public string? BackupExistingRoutingFile(string workspaceFolder) {
        var assessment = Assess(workspaceFolder);
        if (string.IsNullOrWhiteSpace(assessment.ExistingContent))
            return null;

        var directory = Path.GetDirectoryName(assessment.RoutingFilePath);
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        Directory.CreateDirectory(directory);
        var backupPath = Path.Combine(directory, BackupFileName);
        File.WriteAllText(backupPath, NormalizeDocument(assessment.ExistingContent), Encoding.UTF8);
        return backupPath;
    }

    private static SquadRoutingDocumentAssessment CreateAssessment(
        string workspaceFolder,
        string routingFilePath,
        SquadRoutingDocumentStatus status,
        IReadOnlyList<SquadTeamMember> teamMembers,
        string? existingContent,
        string? diagnosticMessage = null) {
        return new SquadRoutingDocumentAssessment(
            workspaceFolder,
            routingFilePath,
            status,
            teamMembers,
            existingContent,
            BuildIssueFingerprint(workspaceFolder, routingFilePath, status, existingContent, teamMembers),
            diagnosticMessage);
    }

    private static string? BuildIssueFingerprint(
        string workspaceFolder,
        string routingFilePath,
        SquadRoutingDocumentStatus status,
        string? existingContent,
        IReadOnlyList<SquadTeamMember> teamMembers) {
        if (status is SquadRoutingDocumentStatus.NotApplicable or
            SquadRoutingDocumentStatus.HealthyCustom) {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine(status.ToString());
        builder.AppendLine();
        AppendFileSnapshot(builder, Path.Combine(workspaceFolder, ".squad", "team.md"));
        AppendFileSnapshot(builder, routingFilePath, existingContent);

        foreach (var charterPath in teamMembers
                     .Where(member => !member.IsUtilityAgent)
                     .Select(member => member.CharterPath)
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(path => NormalizePath(path!))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
            AppendFileSnapshot(builder, charterPath);
        }

        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private static void AppendFileSnapshot(StringBuilder builder, string filePath, string? knownContent = null) {
        builder.AppendLine($"[{NormalizePath(filePath)}]");
        var content = knownContent;
        if (content is null && File.Exists(filePath))
            content = File.ReadAllText(filePath);

        builder.AppendLine(content is null ? "(missing)" : NormalizeDocument(content));
    }

    private static bool IsUntouchedSeed(string content) {
        if (string.IsNullOrWhiteSpace(content))
            return true;

        var normalized = NormalizeDocument(content);
        return normalized.Contains("| {domain 1} | {Name} | {example tasks} |", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("| {domain 2} | {Name} | {example tasks} |", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("| {domain 3} | {Name} | {example tasks} |", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("| Code review | {Name} | Review PRs, check quality, suggest improvements |", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("| Testing | {Name} | Write tests, find edge cases, verify fixes |", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("| Scope & priorities | {Name} | What to build next, trade-offs, decisions |", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("| Work Type | Primary Agent | Fallback |", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("Update this file as team capabilities evolve", StringComparison.OrdinalIgnoreCase);
    }

    private static List<ParsedRoutingRule> ParseRoutingRows(string content) {
        var rules = new List<ParsedRoutingRule>();
        if (string.IsNullOrWhiteSpace(content))
            return rules;

        var lines = NormalizeDocument(content).Split('\n');
        var inRoutingTable = false;
        var headerPassed = false;
        foreach (var rawLine in lines) {
            var line = rawLine.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal)) {
                if (inRoutingTable &&
                    !line.Contains("Routing Table", StringComparison.OrdinalIgnoreCase) &&
                    !line.Contains("Work Type Rules", StringComparison.OrdinalIgnoreCase)) {
                    break;
                }

                inRoutingTable = line.Contains("Routing Table", StringComparison.OrdinalIgnoreCase) ||
                                 line.Contains("Work Type Rules", StringComparison.OrdinalIgnoreCase);
                headerPassed = false;
                continue;
            }

            if (!inRoutingTable || !line.StartsWith("|", StringComparison.Ordinal))
                continue;

            if (line.Contains("Work Type", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Pattern", StringComparison.OrdinalIgnoreCase)) {
                headerPassed = true;
                continue;
            }

            if (line.All(character => character is '|' or '-' or ':' or ' '))
                continue;

            if (!headerPassed)
                continue;

            var cells = line
                .Trim('|')
                .Split('|')
                .Select(cell => cell.Trim())
                .ToArray();
            if (cells.Length < 2)
                continue;

            var workType = cells[0];
            var routeTo = cells[1];
            if (string.IsNullOrWhiteSpace(workType) ||
                string.IsNullOrWhiteSpace(routeTo) ||
                workType.Contains('{') ||
                routeTo.Contains('{')) {
                continue;
            }

            rules.Add(new ParsedRoutingRule(workType, routeTo));
        }

        return rules;
    }

    private static string NormalizeDocument(string content) {
        var normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd();
        return normalized + Environment.NewLine;
    }

    private static string NormalizePath(string path) {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private sealed record ParsedRoutingRule(string WorkType, string RouteTo);
}

internal enum SquadRoutingDocumentStatus {
    NotApplicable,
    HealthyCustom,
    Missing,
    UnfilledSeed,
    InvalidCustom
}

internal sealed record SquadRoutingDocumentAssessment(
    string WorkspaceFolder,
    string RoutingFilePath,
    SquadRoutingDocumentStatus Status,
    IReadOnlyList<SquadTeamMember> TeamMembers,
    string? ExistingContent,
    string? IssueFingerprint,
    string? DiagnosticMessage) {
    public bool IsHealthy => Status is
        SquadRoutingDocumentStatus.NotApplicable or
        SquadRoutingDocumentStatus.HealthyCustom;

    public bool NeedsRepair => !IsHealthy;
}
