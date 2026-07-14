namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

internal sealed record FeatureGroupUsage(string Name, int CommitCount, bool IsStarter);

internal static class FeatureGroupPromptBuilder
{
    internal static IReadOnlyList<FeatureGroupUsage> BuildUsages(
        IEnumerable<string> configuredGroups,
        IEnumerable<CommitApprovalItem> items)
    {
        var counts = items
            .Where(item => !string.IsNullOrWhiteSpace(item.FeatureGroup))
            .GroupBy(item => item.FeatureGroup!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var names = configuredGroups
            .Concat(counts.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return names
            .Select(name => new FeatureGroupUsage(
                name,
                counts.GetValueOrDefault(name),
                FeatureGroupStore.Defaults.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(usage => usage.IsStarter)
            .ThenByDescending(usage => usage.CommitCount)
            .ThenBy(usage => usage.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static void AppendCategorizationGuidance(StringBuilder sb, IReadOnlyList<FeatureGroupUsage> groups)
    {
        var established = groups.Where(group => !group.IsStarter).ToList();
        var starters = groups.Where(group => group.IsStarter).ToList();

        sb.AppendLine("Category selection policy:");
        sb.AppendLine("- Strongly prefer an established workspace category when it reasonably fits the commit.");
        sb.AppendLine("- Commit counts show how established each workspace category is. When two categories fit equally well, prefer the more frequently used one; semantic fit remains more important than popularity.");
        sb.AppendLine("- Starter categories are broad scaffolding shipped with the product. They do not have the same authority as workspace-specific categories.");
        sb.AppendLine("- Create a new short category (2-4 words, Title Case) only for a genuinely distinct endeavor that is not covered by an established workspace category.");
        sb.AppendLine("- Do not create synonyms, spelling variants, or narrower restatements of an existing workspace category.");
        sb.AppendLine();

        if (established.Count > 0)
        {
            sb.AppendLine("Established workspace categories (strongly prefer these):");
            foreach (var group in established)
                sb.AppendLine($"- {group.Name} ({group.CommitCount} {CommitWord(group.CommitCount)})");
            sb.AppendLine();
        }

        if (starters.Count > 0)
        {
            sb.AppendLine("Generic starter categories (fallback scaffolding):");
            foreach (var group in starters)
                sb.AppendLine($"- {group.Name} ({group.CommitCount} {CommitWord(group.CommitCount)})");
            sb.AppendLine();
        }
    }

    internal static string BuildContext(IReadOnlyList<FeatureGroupUsage> groups)
    {
        var sb = new StringBuilder();
        AppendCategorizationGuidance(sb, groups);
        return sb.ToString().TrimEnd();
    }

    private static string CommitWord(int count) => count == 1 ? "commit" : "commits";
}
