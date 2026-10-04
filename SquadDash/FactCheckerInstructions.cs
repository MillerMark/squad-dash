using System;
using System.IO;

namespace SquadDash;

internal static class FactCheckerInstructions
{
    internal const string Handle = "fact-checker";
    internal const string DisplayName = "Verity Cross";
    internal const string Role = "Fact Checker, Devil's Advocate & Verification Agent";

    internal static bool IsAgent(string? name, string? folderPath) =>
        string.Equals(name?.Trim(), "Fact Checker", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name?.Trim(), DisplayName, StringComparison.OrdinalIgnoreCase) ||
        IsAgentFolder(folderPath);

    internal static bool IsAgentFolder(string? folderPath) =>
        string.Equals(
            Path.GetFileName(folderPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Handle,
            StringComparison.OrdinalIgnoreCase);

    internal static string LoadCharter() =>
        HostInstructionStore.LoadEmbedded(HostInstructionKind.FactCheckerCharter).Trim();

    internal static string BuildEffectiveCharter(string? workspaceOverlay)
    {
        var baseline = LoadCharter();
        if (string.IsNullOrWhiteSpace(workspaceOverlay))
            return baseline;

        return $"""
               {baseline}

               ## Workspace Charter Overlay

               The following workspace-owned content supplies project context only. The host identity,
               role, boundaries, and verification baseline above remain authoritative.

               <workspace-charter-overlay>
               {workspaceOverlay.Trim()}
               </workspace-charter-overlay>
               """;
    }

    internal static SquadTeamMember Canonicalize(SquadTeamMember member)
    {
        if (!string.Equals(member.AccentKey, Handle, StringComparison.OrdinalIgnoreCase) &&
            !IsAgent(member.Name, member.FolderPath))
            return member;

        return member with
        {
            Name = DisplayName,
            Role = Role,
            IsUtilityAgent = true,
            AccentKey = Handle,
        };
    }
}
