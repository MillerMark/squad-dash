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
        var groups              = new List<TaskPriorityGroup>();
        var completedItems      = new List<TaskItem>();
        TaskPriorityGroup? current   = null;
        bool inCompletedSection      = false;

        for (int i = 0; i < lines.Length; i++) {
            var line    = lines[i].TrimEnd();
            var trimmed = line.TrimStart();

            if (line.StartsWith("## ", StringComparison.Ordinal)) {
                var m = PriorityHeadingRegex.Match(line);
                if (m.Success) {
                    if (line.Contains("✅", StringComparison.Ordinal)) {
                        // Enter the completed section — collect [x] items from here
                        current             = null;
                        inCompletedSection  = true;
                    } else {
                        inCompletedSection = false;
                        current = new TaskPriorityGroup(m.Groups[1].Value, m.Groups[2].Value.Trim());
                        groups.Add(current);
                    }
                } else if (line.Contains("✅", StringComparison.Ordinal)) {
                    current            = null;
                    inCompletedSection = true;
                } else {
                    current            = null;
                    inCompletedSection = false;
                }
                continue;
            }

            bool isOpen    = trimmed.StartsWith("- [ ]", StringComparison.Ordinal);
            bool isChecked = trimmed.StartsWith("- [x]", StringComparison.Ordinal);

            if (!isOpen && !isChecked) continue;

            // Items in the ✅ completed section
            if (inCompletedSection) {
                if (isChecked) {
                    var rawText = trimmed[5..].Trim();
                    // Strip "— ✅ Implemented/Decided/Verified …" annotation
                    var annot = rawText.IndexOf("— ✅", StringComparison.Ordinal);
                    if (annot < 0) annot = rawText.IndexOf("—✅", StringComparison.Ordinal);
                    if (annot > 0) rawText = rawText[..annot].Trim();
                    var text    = StripBoldAndOwner(rawText, out _);
                    completedItems.Add(new TaskItem(
                        Text:        text,
                        Owner:       null,
                        IsUserOwned: false,
                        IsChecked:   true,
                        Emoji:       "✅",
                        RawLine:     line,
                        Description: null));
                }
                continue;
            }

            if (current is not null) {
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

                // Collect description lines that follow the task item line
                var descLines = new List<string>();
                while (i + 1 < lines.Length) {
                    var next        = lines[i + 1].TrimEnd();
                    var nextTrimmed = next.TrimStart();
                    // Stop at a new list item, section heading, or horizontal rule
                    if (nextTrimmed.StartsWith("- [ ]", StringComparison.Ordinal) ||
                        nextTrimmed.StartsWith("- [x]", StringComparison.Ordinal) ||
                        next.StartsWith("## ",          StringComparison.Ordinal) ||
                        next.StartsWith("---",          StringComparison.Ordinal))
                        break;
                    i++;
                    descLines.Add(nextTrimmed);
                }
                var desc = descLines.Count > 0 ? string.Join("\n", descLines).Trim() : null;
                if (string.IsNullOrWhiteSpace(desc)) desc = null;

                var item = new TaskItem(
                    Text:        text,
                    Owner:       owner,
                    IsUserOwned: owner is not null &&
                                 owner.Contains("you", StringComparison.OrdinalIgnoreCase),
                    IsChecked:   isChecked,
                    Emoji:       current.Emoji,
                    RawLine:     line,
                    Description: desc
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

    /// <summary>
    /// Parses completed-tasks.md: extracts every <c>- [x]</c> line and returns the
    /// bold task title (text between the first pair of <c>**</c> markers) as a
    /// <see cref="TaskItem"/>.  Multi-line descriptions are ignored — only the header
    /// line is relevant.  File order is preserved (most-recent-first by convention).
    /// </summary>
    internal static IReadOnlyList<TaskItem> ParseCompletedFile(string[] lines) {
        var items = new List<TaskItem>();
        foreach (var rawLine in lines) {
            var trimmed = rawLine.TrimStart();
            if (!trimmed.StartsWith("- [x]", StringComparison.Ordinal)) continue;
            var rawText = trimmed[5..].Trim();
            var text    = StripBoldAndOwner(rawText, out _);
            if (string.IsNullOrWhiteSpace(text)) continue;
            items.Add(new TaskItem(
                Text:        text,
                Owner:       null,
                IsUserOwned: false,
                IsChecked:   true,
                Emoji:       "✅",
                RawLine:     rawLine.TrimEnd()));
        }
        return items;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Strips <c>**bold**</c> wrapper and trailing <c>*(Owner: …)*</c> suffix.</summary>
    private static string StripBoldAndOwner(string rawText, out string? owner) {
        owner = null;
        var text = rawText;

        // Strip owner suffix first (before bold so the marker is still visible)
        var ownerIdx = text.IndexOf(OwnerMarker, StringComparison.Ordinal);
        if (ownerIdx > 0) {
            var after    = text[(ownerIdx + OwnerMarker.Length)..];
            var closeIdx = after.IndexOf(')', StringComparison.Ordinal);
            if (closeIdx >= 0)
                owner = after[..closeIdx].Trim();
            text = text[..ownerIdx].Trim();
        }

        // Strip **bold** wrapper
        var boldEnd = text.IndexOf("**", 2, StringComparison.Ordinal);
        if (text.StartsWith("**", StringComparison.Ordinal) && boldEnd > 2)
            text = text[2..boldEnd].Trim();

        return text;
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
    string  RawLine,
    string? Description = null);

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
