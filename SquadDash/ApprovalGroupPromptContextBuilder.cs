namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

internal static class ApprovalGroupPromptContextBuilder {
    public static string? Build(IReadOnlyList<string>? groups) {
        var canonicalGroups = (groups ?? [])
            .Select(static group => group?.Trim())
            .Where(static group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (canonicalGroups.Length == 0)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("SquadDash approval group context:");
        sb.AppendLine("If you make a git commit, append an APPROVAL_GROUP_JSON block at the very end of your response:");
        sb.AppendLine("APPROVAL_GROUP_JSON:");
        sb.AppendLine("{\"sha\":\"<7-char-hash>\",\"group\":\"<feature-group>\"}");
        sb.AppendLine("Use one of these canonical feature group names whenever it fits; preserve spelling and capitalization exactly:");

        foreach (var group in canonicalGroups)
            sb.AppendLine($"- {group}");

        sb.Append("Only create a new short Title Case group if none of the canonical groups fits.");
        return sb.ToString();
    }
}
