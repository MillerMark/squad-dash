using System;
using System.Collections.Generic;
using System.Linq;
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

    private const string OwnerMarker = " *(Owner:";

    internal static TaskParseResult Parse(string[] lines) {
        var groups         = new List<TaskPriorityGroup>();
        var completedItems = new List<TaskItem>();
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
                var trimmed   = line.TrimStart();
                bool isOpen    = trimmed.StartsWith("- [ ]", StringComparison.Ordinal);
                bool isChecked = trimmed.StartsWith("- [x]", StringComparison.Ordinal);

                if (!isOpen && !isChecked) continue;

                // Raw text after the checkbox marker
                var rawText = trimmed[5..].Trim();

                // Extract owner BEFORE bold-stripping so the suffix is still present
                string? owner       = null;
                var     displayText = rawText;
                var     ownerIdx    = displayText.IndexOf(OwnerMarker, StringComparison.Ordinal);
                if (ownerIdx > 0) {
                    var after    = displayText[(ownerIdx + OwnerMarker.Length)..];
                    var closeIdx = after.IndexOf(')', StringComparison.Ordinal);
                    if (closeIdx >= 0)
                        owner = after[..closeIdx].Trim();
                    displayText = displayText[..ownerIdx].Trim();
                }

                // Strip **bold** wrapper from display text
                var text    = displayText;
                var boldEnd = text.IndexOf("**", 2, StringComparison.Ordinal);
                if (text.StartsWith("**", StringComparison.Ordinal) && boldEnd > 2)
                    text = text[2..boldEnd].Trim();

                var item = new TaskItem(
                    Text:        text,
                    Owner:       owner,
                    IsUserOwned: owner is not null &&
                                 owner.Contains("you", StringComparison.OrdinalIgnoreCase),
                    IsChecked:   isChecked,
                    Emoji:       current.Emoji,
                    RawLine:     line
                );

                if (isChecked)
                    completedItems.Add(item);
                else
                    current.Items.Add(item);
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

        return new TaskParseResult(merged, completedItems);
    }

    private static int PriorityOrder(string emoji) => emoji switch {
        "🔴" => 0,
        "🟡" => 1,
        "🟢" => 2,
        _    => 3
    };
}

/// <summary>A single task item parsed from tasks.md.</summary>
internal sealed record TaskItem(
    string  Text,
    string? Owner,
    bool    IsUserOwned,
    bool    IsChecked,
    string  Emoji,
    string  RawLine);

/// <summary>Result of parsing tasks.md: open priority groups and completed items.</summary>
internal sealed class TaskParseResult(
    IReadOnlyList<TaskPriorityGroup> openGroups,
    IReadOnlyList<TaskItem>          completedItems) {
    internal IReadOnlyList<TaskPriorityGroup> OpenGroups     { get; } = openGroups;
    internal IReadOnlyList<TaskItem>          CompletedItems { get; } = completedItems;
}

internal sealed class TaskPriorityGroup(string emoji, string label) {
    internal string         Emoji { get; } = emoji;
    internal string         Label { get; } = label;
    internal List<TaskItem> Items { get; } = [];
}
