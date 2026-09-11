using System;
using System.IO;
using System.Text;

namespace SquadDash;

/// <summary>
/// Keeps SquadDash's built-in Planning Architect available as a real named-agent route.
/// Rory is host-provided rather than a user-hired implementation specialist, so this service
/// deliberately leaves the workspace's editable team and routing documents unchanged.
/// </summary>
internal static class SquadPlanningWorkspaceRepairService
{
    internal const string AgentFolderName = "rory";

    internal static SquadPlanningWorkspaceRepairResult Repair(string workspaceFolder)
    {
        if (string.IsNullOrWhiteSpace(workspaceFolder))
            throw new ArgumentException("Workspace folder cannot be empty.", nameof(workspaceFolder));

        var normalizedWorkspace = Path.GetFullPath(workspaceFolder);
        var layout = SquadWorkspaceLayoutResolver.Resolve(normalizedWorkspace);
        var squadFolder = layout?.TeamSquadFolderPath ?? Path.Combine(normalizedWorkspace, ".squad");
        if (!Directory.Exists(squadFolder))
            return new SquadPlanningWorkspaceRepairResult(normalizedWorkspace, false, false, false);

        var agentFolder = Path.Combine(squadFolder, "agents", AgentFolderName);
        var createdDirectory = !Directory.Exists(agentFolder);
        Directory.CreateDirectory(agentFolder);

        var createdCharter = EnsureCharter(Path.Combine(agentFolder, "charter.md"));
        var createdHistory = EnsureHistory(Path.Combine(agentFolder, "history.md"), normalizedWorkspace);
        return new SquadPlanningWorkspaceRepairResult(
            normalizedWorkspace,
            createdDirectory || createdCharter || createdHistory,
            createdCharter,
            createdHistory);
    }

    private static bool EnsureCharter(string charterPath)
    {
        if (File.Exists(charterPath))
            return false;

        var content = $"""
                      # Rory — Planning Architect

                      - **Name:** Rory
                      - **Role:** Planning Architect
                      - **Mode:** Built-in named planning specialist

                      SquadDash supplies Rory's current immutable Planning Architect baseline and live
                      planning protocol at dispatch time. This workspace file is an optional customization
                      overlay only. Add project-specific planning context below; do not copy or replace the
                      shipped protocol here.

                      ## Workspace Customizations

                      <!-- Add durable project-specific planning guidance here. -->
                      """;
        File.WriteAllText(charterPath, NormalizeDocument(content), Encoding.UTF8);
        return true;
    }

    private static bool EnsureHistory(string historyPath, string workspaceFolder)
    {
        if (File.Exists(historyPath))
            return false;

        var projectName = Path.GetFileName(workspaceFolder.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(projectName))
            projectName = "Workspace";

        var content = $"""
                      # Rory — History

                      ## Core Context

                      - **Project:** {projectName}
                      - **Role:** Planning Architect
                      - **Joined:** {DateTimeOffset.UtcNow:O}

                      ## Learnings

                      <!-- Append planning learnings below -->
                      """;
        File.WriteAllText(historyPath, NormalizeDocument(content), Encoding.UTF8);
        return true;
    }

    private static string NormalizeDocument(string content)
    {
        var normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd();
        return normalized + Environment.NewLine;
    }
}

internal sealed record SquadPlanningWorkspaceRepairResult(
    string WorkspaceFolder,
    bool Repaired,
    bool CreatedCharter,
    bool CreatedHistory);
