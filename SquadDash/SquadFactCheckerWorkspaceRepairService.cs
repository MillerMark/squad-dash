using System;
using System.IO;
using System.Text;

namespace SquadDash;

/// <summary>
/// Keeps the built-in Fact Checker route present in every installed Squad workspace. The workspace
/// charter is a customization overlay; the shipped host charter remains the runtime baseline.
/// </summary>
internal static class SquadFactCheckerWorkspaceRepairService
{
    internal static SquadFactCheckerWorkspaceRepairResult Repair(string workspaceFolder)
    {
        if (string.IsNullOrWhiteSpace(workspaceFolder))
            throw new ArgumentException("Workspace folder cannot be empty.", nameof(workspaceFolder));

        var normalizedWorkspace = Path.GetFullPath(workspaceFolder);
        var layout = SquadWorkspaceLayoutResolver.Resolve(normalizedWorkspace);
        var squadFolder = layout?.TeamSquadFolderPath ?? Path.Combine(normalizedWorkspace, ".squad");
        if (!Directory.Exists(squadFolder))
            return new(normalizedWorkspace, false, false, false, false);

        var agentFolder = Path.Combine(squadFolder, "agents", FactCheckerInstructions.Handle);
        var createdDirectory = !Directory.Exists(agentFolder);
        Directory.CreateDirectory(agentFolder);

        var charterPath = Path.Combine(agentFolder, "charter.md");
        var charterResult = EnsureCharter(charterPath);
        var createdHistory = EnsureHistory(
            Path.Combine(agentFolder, "history.md"),
            normalizedWorkspace);

        return new(
            normalizedWorkspace,
            createdDirectory || charterResult.Changed || createdHistory,
            charterResult.Created,
            charterResult.MigratedLegacy,
            createdHistory);
    }

    private static (bool Changed, bool Created, bool MigratedLegacy) EnsureCharter(string path)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, BuildWorkspaceOverlay(), Encoding.UTF8);
            return (true, true, false);
        }

        var existing = File.ReadAllText(path);
        if (!IsLegacyGenericScaffold(existing))
            return (false, false, false);

        File.WriteAllText(path, BuildWorkspaceOverlay(), Encoding.UTF8);
        return (true, false, true);
    }

    internal static bool IsLegacyGenericScaffold(string content) =>
        content.Contains("# Fact Checker", StringComparison.OrdinalIgnoreCase) &&
        content.Contains("**Name:** Fact Checker", StringComparison.OrdinalIgnoreCase) &&
        content.Contains("**Casting:** Gets a universe name like any other agent", StringComparison.OrdinalIgnoreCase) &&
        !content.Contains("Verity Cross", StringComparison.OrdinalIgnoreCase);

    private static string BuildWorkspaceOverlay() => NormalizeDocument("""
        # Verity Cross — Fact Checker

        - **Name:** Verity Cross
        - **Role:** Fact Checker, Devil's Advocate & Verification Agent
        - **Mode:** Built-in named verification specialist

        SquadDash supplies Verity Cross's immutable Fact Checker identity and verification baseline
        at dispatch time. This workspace file is an optional customization overlay only.

        ## Workspace Customizations

        <!-- Add durable project-specific verification guidance here. -->
        """);

    private static bool EnsureHistory(string path, string workspaceFolder)
    {
        if (File.Exists(path))
            return false;

        var projectName = Path.GetFileName(workspaceFolder.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(projectName))
            projectName = "Workspace";

        File.WriteAllText(path, NormalizeDocument($"""
            # Verity Cross — History

            ## Core Context

            - **Project:** {projectName}
            - **Role:** Fact Checker
            - **Joined:** {DateTimeOffset.UtcNow:O}

            ## Learnings

            <!-- Append verification learnings below. -->
            """), Encoding.UTF8);
        return true;
    }

    private static string NormalizeDocument(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd() + Environment.NewLine;
}

internal sealed record SquadFactCheckerWorkspaceRepairResult(
    string WorkspaceFolder,
    bool Repaired,
    bool CreatedCharter,
    bool MigratedLegacyCharter,
    bool CreatedHistory);
