using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SquadDash;

/// <summary>
/// Parses .squad/tasks.md and returns structured priority groups
/// for display in the Tasks sidebar panel.
/// Priority sections are identified by ## headings containing emoji
/// priority indicators (🔴/🟡/🟢) followed by a priority label.
/// </summary>
internal static class TasksPanelParser {

    // Matches: ## 🔴 High Priority, ## 🟡 Mid Priority, ## 🟢 Low Priority, etc.
    private static readonly Regex PriorityHeadingRegex =
        new(@"^##\s+(🔴|🟡|🟢)\s+(.+)$", RegexOptions.Compiled);

    internal static IReadOnlyList<TaskPriorityGroup> Parse(string[] lines) {
        var groups = new List<TaskPriorityGroup>();
        TaskPriorityGroup? current = null;

        foreach (var rawLine in lines) {
            var line = rawLine.TrimEnd();

            if (line.StartsWith("## ", StringComparison.Ordinal)) {
                var m = PriorityHeadingRegex.Match(line);
                if (m.Success) {
                    // Stop at the ✅ Done section
                    if (line.Contains("✅", StringComparison.Ordinal))
                        break;
                    current = new TaskPriorityGroup(m.Groups[1].Value, m.Groups[2].Value.Trim());
                    groups.Add(current);
                } else if (line.Contains("✅", StringComparison.Ordinal)) {
                    break;
                } else {
                    current = null; // non-priority heading resets group
                }
                continue;
            }

            if (current is not null) {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("- [ ]", StringComparison.Ordinal)) {
                    var text = trimmed[5..].Trim();
                    // Strip **bold** wrapper
                    var boldEnd = text.IndexOf("**", 2, StringComparison.Ordinal);
                    if (text.StartsWith("**", StringComparison.Ordinal) && boldEnd > 2)
                        text = text[2..boldEnd].Trim();
                    // Strip owner suffix
                    var ownerIdx = text.IndexOf(" *(Owner:", StringComparison.Ordinal);
                    if (ownerIdx > 0)
                        text = text[..ownerIdx].Trim();
                    current.Items.Add(text);
                }
            }
        }

        // Merge groups with the same emoji so duplicate priority sections
        // in the file collapse into a single group in the panel.
        var merged = new List<TaskPriorityGroup>();
        foreach (var g in groups) {
            var existing = merged.FirstOrDefault(m => m.Emoji == g.Emoji);
            if (existing is not null)
                existing.Items.AddRange(g.Items);
            else
                merged.Add(g);
        }

        // Sort: High (🔴) → Mid (🟡) → Low (🟢), items within each group keep file order.
        merged.Sort((a, b) => PriorityOrder(a.Emoji).CompareTo(PriorityOrder(b.Emoji)));

        return merged;
    }

    private static int PriorityOrder(string emoji) => emoji switch {
        "🔴" => 0,
        "🟡" => 1,
        "🟢" => 2,
        _    => 3
    };
}

internal sealed class TaskPriorityGroup(string emoji, string label) {
    internal string Emoji { get; } = emoji;
    internal string Label { get; } = label;
    internal List<string> Items { get; } = [];
}
