using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SquadDash;

internal static class AgentImageCustomizationMigration {
    /// <summary>
    /// Identifies the legacy shared image key produced when every roster member was mistakenly
    /// assigned the workspace folder name. It is safe to remove only when that key is not the
    /// stable identity of any member in the repaired roster.
    /// </summary>
    public static string? FindObsoleteWorkspaceKey(
        string workspaceFolder,
        IReadOnlyCollection<SquadTeamMember> members,
        IReadOnlyDictionary<string, string> workspaceImages) {
        if (string.IsNullOrWhiteSpace(workspaceFolder) || members.Count == 0 || workspaceImages.Count == 0)
            return null;

        var normalizedWorkspace = workspaceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var workspaceKey = Path.GetFileName(normalizedWorkspace);
        if (string.IsNullOrWhiteSpace(workspaceKey) || !workspaceImages.ContainsKey(workspaceKey))
            return null;

        return members.Any(member => string.Equals(member.AccentKey, workspaceKey, StringComparison.OrdinalIgnoreCase))
            ? null
            : workspaceKey;
    }
}
