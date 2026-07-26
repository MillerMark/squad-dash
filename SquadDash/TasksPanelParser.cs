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

    // Matches: ## 🔴 High Priority, ## 🟡 Mid Priority, ## 🟢/🔵 Low Priority, ## ⚫ Critical, etc.
    private static readonly Regex PriorityHeadingRegex =
        new(@"^##\s+(🔴|🟡|🟢|🔵|⚫)\s+(.+)$", RegexOptions.Compiled);

    private static readonly Regex DecomposeHeaderRegex = new(
        @"^<!--\s*decompose-group:\s*(?<id>[^|]+?)\s*\|\s*branch:\s*(?<branch>[^|]+?)(?:\s*\|\s*revision:\s*(?<revision>[^|]+?))?\s*-->$",
        RegexOptions.Compiled);

    private static readonly Regex DecomposeTaskRegex = new(
        @"^-\s*\[(?<status>[ x!~>])\]\s*\*\*\[(?<id>[^\]]+)\]\*\*\s*(?<description>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DecomposeMetadataRegex = new(
        @"^Group:\s*(?<group>[^|]+?)\s*\|\s*Branch:\s*(?<branch>[^|]+?)\s*\|\s*Priority:\s*(?<priority>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const string OwnerMarker = " *(Owner:";

    internal static TaskParseResult Parse(string[] lines) {
        var groups              = new List<TaskPriorityGroup>();
        var completedItems      = new List<TaskItem>();
        var decomposeGroups     = new Dictionary<string, DecomposedTaskGroup>(StringComparer.Ordinal);
        var consumed            = ParseDecomposeGroups(lines, groups, completedItems, decomposeGroups);
        TaskPriorityGroup? current   = null;
        bool inCompletedSection      = false;

        for (int i = 0; i < lines.Length; i++) {
            if (consumed[i]) continue;
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
                        // Normalize 🔵 → 🟢 so blue-circle low-priority sections
                        // merge with green-circle ones into the same panel group.
                        var rawEmoji = m.Groups[1].Value;
                        var emoji    = rawEmoji == "🔵" ? "🟢" : rawEmoji;
                        current = new TaskPriorityGroup(emoji, m.Groups[2].Value.Trim());
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
            var existing = g.DecomposeGroupId is null
                ? merged.FirstOrDefault(m => m.DecomposeGroupId is null && m.Emoji == g.Emoji)
                : null;
            if (existing is not null)
                existing.Items.AddRange(g.Items);
            else
                merged.Add(g);
        }

        // Sort: High (🔴) → Mid (🟡) → Low (🟢), items within each group keep file order.
        merged = merged
            .Select((group, index) => (group, index))
            .OrderBy(pair => PriorityOrder(pair.group.Emoji))
            .ThenBy(pair => pair.index)
            .Select(pair => pair.group)
            .ToList();

        return new TaskParseResult(merged, completedItems, decomposeGroups);
    }

    private static bool[] ParseDecomposeGroups(
        string[] lines,
        List<TaskPriorityGroup> openGroups,
        List<TaskItem> completedItems,
        Dictionary<string, DecomposedTaskGroup> groupsById) {

        var consumed = new bool[lines.Length];
        int index = 0;
        while (index < lines.Length) {
            // StreamReader normally consumes a UTF-8 BOM, but callers can also provide
            // already-decoded strings containing U+FEFF. Never let that turn a plan into
            // an unstructured backlog block.
            var headerLine = lines[index].Trim().TrimStart('\uFEFF');
            var header = DecomposeHeaderRegex.Match(headerLine);
            if (!header.Success) {
                index++;
                continue;
            }

            int start = index;
            int end = index + 1;
            while (end < lines.Length &&
                   !DecomposeHeaderRegex.IsMatch(lines[end].Trim()) &&
                   !lines[end].StartsWith("## ", StringComparison.Ordinal))
                end++;

            for (int i = start; i < end; i++) consumed[i] = true;

            var groupId = header.Groups["id"].Value.Trim();
            var branch  = header.Groups["branch"].Value.Trim();
            var revision = header.Groups["revision"].Value.Trim();
            var title   = groupId;
            var summary = string.Empty;
            var tasks   = new List<DecomposedSubTask>();
            var items   = new List<TaskItem>();

            for (int i = start + 1; i < end; i++) {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("<!-- decompose-revision:", StringComparison.Ordinal) &&
                    trimmed.EndsWith("-->", StringComparison.Ordinal)) {
                    revision = trimmed["<!-- decompose-revision:".Length..^3].Trim();
                    continue;
                }
                if (trimmed.StartsWith($"**[{groupId}] ", StringComparison.Ordinal) &&
                    trimmed.EndsWith("**", StringComparison.Ordinal)) {
                    title = trimmed[(groupId.Length + 4)..^2].Trim();
                    continue;
                }
                if (trimmed.StartsWith("> ", StringComparison.Ordinal)) {
                    summary = trimmed[2..].Trim();
                    continue;
                }

                var taskMatch = DecomposeTaskRegex.Match(trimmed);
                if (!taskMatch.Success) continue;

                var status      = taskMatch.Groups["status"].Value.ToLowerInvariant();
                var taskId      = taskMatch.Groups["id"].Value.Trim();
                var taskTitle   = taskMatch.Groups["description"].Value.Trim();
                var description = taskTitle;
                var priority    = "medium";
                IReadOnlyList<string> dependsOn = [];
                string? parentTaskId = null;

                int metadataEnd = i + 1;
                while (metadataEnd < end &&
                       !DecomposeTaskRegex.IsMatch(lines[metadataEnd].Trim()) &&
                       !DecomposeHeaderRegex.IsMatch(lines[metadataEnd].Trim())) {
                    var metadata = lines[metadataEnd].Trim();
                    var metadataMatch = DecomposeMetadataRegex.Match(metadata);
                    if (metadataMatch.Success)
                        priority = metadataMatch.Groups["priority"].Value.Trim();
                    else if (metadata.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                        description = metadata["description:".Length..].Trim();
                    else if (metadata.StartsWith("dependsOn:", StringComparison.OrdinalIgnoreCase)) {
                        var rawDependencies = metadata["dependsOn:".Length..].Trim();
                        dependsOn = rawDependencies.Equals("(none)", StringComparison.OrdinalIgnoreCase) ||
                                    rawDependencies.Length == 0
                            ? []
                            : rawDependencies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    }
                    else if (metadata.StartsWith("parentTaskId:", StringComparison.OrdinalIgnoreCase))
                        parentTaskId = metadata["parentTaskId:".Length..].Trim();
                    metadataEnd++;
                }

                description = StripOwner(description, out var owner);

                var emoji = PriorityToEmoji(priority);
                var hover = BuildDecomposeHover(
                    groupId,
                    title,
                    branch,
                    summary,
                    taskId,
                    taskTitle,
                    description,
                    priority,
                    dependsOn,
                    status);
                var item = new TaskItem(
                    // Structured plans provide a compact, human-readable title separately
                    // from the implementation brief. Keep the Tasks panel scannable and
                    // expose the full brief through the existing hover/details surface.
                    Text: taskTitle,
                    Owner: owner,
                    IsUserOwned: owner?.Contains("you", StringComparison.OrdinalIgnoreCase) == true,
                    IsChecked: status == "x",
                    Emoji: emoji,
                    RawLine: lines[i].TrimEnd(),
                    Description: hover,
                    DecomposeGroupId: groupId,
                    DecomposeGroupTitle: title,
                    DecomposeBranch: branch,
                    TaskId: taskId,
                    DependsOn: dependsOn,
                    IsFailed: status == "!",
                    IsPartial: status == "~",
                    IsSuperseded: status == ">");

                tasks.Add(new DecomposedSubTask(taskId, description, dependsOn, priority, taskTitle, parentTaskId));
                items.Add(item);
                i = metadataEnd - 1;
            }

            if (tasks.Count > 0) {
                var group = new DecomposedTaskGroup(
                    groupId,
                    title,
                    branch,
                    summary,
                    tasks,
                    HostRevision: string.IsNullOrWhiteSpace(revision) ? null : revision);
                groupsById[groupId] = group;

                var openItems = items.Where(item => !item.IsChecked && !item.IsSuperseded).ToList();
                if (openItems.Count > 0) {
                    var groupEmoji = openItems
                        .OrderBy(item => PriorityOrder(item.Emoji))
                        .Select(item => item.Emoji)
                        .First();
                    var panelGroup = new TaskPriorityGroup(
                        groupEmoji,
                        $"Plan · {title}",
                        groupId,
                        title,
                        branch);
                    panelGroup.Items.AddRange(openItems);
                    openGroups.Add(panelGroup);
                }

                completedItems.AddRange(items.Where(item => item.IsChecked || item.IsSuperseded));
            }

            index = end;
        }

        return consumed;
    }

    private static string StripOwner(string text, out string? owner) {
        owner = null;
        var ownerIdx = text.IndexOf(OwnerMarker, StringComparison.Ordinal);
        if (ownerIdx <= 0) return text;
        var after = text[(ownerIdx + OwnerMarker.Length)..];
        var closeIdx = after.IndexOf(')', StringComparison.Ordinal);
        if (closeIdx >= 0) owner = after[..closeIdx].Trim();
        return text[..ownerIdx].Trim();
    }

    private static string BuildDecomposeHover(
        string groupId,
        string title,
        string branch,
        string summary,
        string taskId,
        string taskTitle,
        string description,
        string priority,
        IReadOnlyList<string> dependsOn,
        string status) {

        var dependencies = dependsOn.Count == 0 ? "None" : string.Join(", ", dependsOn);
        var statusText = status switch {
            "!" => "\n\n**Status:** Failed — use the plan recovery controls in the transcript or Inbox.",
            "~" => "\n\n**Status:** Partial — use the plan recovery controls in the transcript or Inbox.",
            _ => string.Empty,
        };
        var summaryText = string.IsNullOrWhiteSpace(summary) ? string.Empty : $"\n\n{summary}";
        return $"**Plan:** {title} (`{groupId}`)  \n" +
               $"**Task:** {taskTitle} (`{taskId}`)  \n" +
               $"**Branch:** `{branch}`  \n" +
               $"**Priority:** {priority}  \n" +
               $"**Depends on:** {dependencies}\n\n" +
               description + summaryText + statusText;
    }

    private static string PriorityToEmoji(string priority) => priority.Trim().ToLowerInvariant() switch {
        "critical" => "⚫",
        "high"     => "🔴",
        "low"      => "🟢",
        _          => "🟡",
    };

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
        "⚫" => -1,   // Critical sorts first
        "🔴" => 0,
        "🟡" => 1,
        "🟢" => 2,
        "🔵" => 2,
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
    string? Description = null,
    string? DecomposeGroupId = null,
    string? DecomposeGroupTitle = null,
    string? DecomposeBranch = null,
    string? TaskId = null,
    IReadOnlyList<string>? DependsOn = null,
    bool IsFailed = false,
    bool IsPartial = false,
    bool IsSuperseded = false);

/// <summary>Result of parsing tasks.md: open priority groups and completed items.</summary>
internal sealed class TaskParseResult(
    IReadOnlyList<TaskPriorityGroup> openGroups,
    IReadOnlyList<TaskItem>          completedItems,
    IReadOnlyDictionary<string, DecomposedTaskGroup>? decomposeGroups = null) {
    internal IReadOnlyList<TaskPriorityGroup> OpenGroups     { get; } = openGroups;
    internal IReadOnlyList<TaskItem>          CompletedItems { get; } = completedItems;
    internal IReadOnlyDictionary<string, DecomposedTaskGroup> DecomposeGroups { get; } =
        decomposeGroups ?? new Dictionary<string, DecomposedTaskGroup>(StringComparer.Ordinal);

    internal TaskParseResult WithCompletedItems(IReadOnlyList<TaskItem> completedItems) =>
        new(OpenGroups, completedItems, DecomposeGroups);
}

internal sealed class TaskPriorityGroup(
    string emoji,
    string label,
    string? decomposeGroupId = null,
    string? decomposeGroupTitle = null,
    string? decomposeBranch = null) {
    internal string         Emoji { get; } = emoji;
    internal string         Label { get; } = label;
    internal string? DecomposeGroupId { get; } = decomposeGroupId;
    internal string? DecomposeGroupTitle { get; } = decomposeGroupTitle;
    internal string? DecomposeBranch { get; } = decomposeBranch;
    internal List<TaskItem> Items { get; } = [];
}
