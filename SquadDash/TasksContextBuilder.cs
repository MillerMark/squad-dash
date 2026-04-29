using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SquadDash;

/// <summary>
/// Parses .squad/tasks.md content and builds a compact prompt-injection block
/// listing only unchecked (open) items, capped at <see cref="MaxInjectedItems"/>
/// to keep the context brief.
/// </summary>
internal static class TasksContextBuilder {

    /// <summary>Maximum number of task items shown in a prompt injection block.</summary>
    internal const int MaxInjectedItems = 7;

    /// <summary>
    /// Parses <paramref name="lines"/> from tasks.md and returns a prompt-injection
    /// string, or <c>null</c> if there are no open items.
    /// Items are shown in file order (high-priority sections first), capped at
    /// <see cref="MaxInjectedItems"/> total.
    /// </summary>
    internal static string? Build(string[] lines) {
        var groups = ParseOpenGroups(lines);
        var openGroups = groups.Where(g => g.Items.Count > 0).ToList();
        if (openGroups.Count == 0)
            return null;

        var totalOpen = openGroups.Sum(g => g.Items.Count);
        var sb = new StringBuilder();
        sb.AppendLine("## Open Tasks (from .squad/tasks.md)");
        sb.AppendLine("The following items are outstanding. Keep them in mind when making recommendations.");
        sb.AppendLine();

        int shown = 0;
        foreach (var group in openGroups) {
            if (shown >= MaxInjectedItems)
                break;

            var visibleItems = group.Items.Take(MaxInjectedItems - shown).ToList();
            sb.AppendLine(group.Heading);
            foreach (var item in visibleItems)
                sb.AppendLine($"- [ ] {item}");
            sb.AppendLine();
            shown += visibleItems.Count;
        }

        if (totalOpen > MaxInjectedItems)
            sb.AppendLine($"(showing {shown} of {totalOpen} — see .squad/tasks.md for full details)");
        else
            sb.AppendLine($"({totalOpen} open item{(totalOpen == 1 ? "" : "s")} — see .squad/tasks.md for full details)");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Parses task groups from raw lines. Exposed internally for unit testing.
    /// Stops accumulating items when the "✅" done section is reached.
    /// </summary>
    internal static List<TaskGroup> ParseOpenGroups(string[] lines) {
        var groups = new List<TaskGroup>();
        TaskGroup? current = null;

        foreach (var rawLine in lines) {
            var line = rawLine.TrimEnd();

            if (line.StartsWith("## ", StringComparison.Ordinal)) {
                var heading = line[3..].Trim();
                if (heading.StartsWith("✅", StringComparison.Ordinal))
                    break;
                current = new TaskGroup(heading);
                groups.Add(current);
                continue;
            }

            if (current is not null && line.TrimStart().StartsWith("- [ ]", StringComparison.Ordinal)) {
                var itemText = line.TrimStart()[5..].Trim();
                // Strip markdown bold wrapper: **text** → text
                var boldEnd = itemText.IndexOf("**", 2, StringComparison.Ordinal);
                if (itemText.StartsWith("**", StringComparison.Ordinal) && boldEnd > 2)
                    itemText = itemText[2..boldEnd].Trim();
                // Strip owner suffix: trim from " *(Owner:" onward
                var ownerIdx = itemText.IndexOf(" *(Owner:", StringComparison.Ordinal);
                if (ownerIdx > 0)
                    itemText = itemText[..ownerIdx].Trim();
                current.Items.Add(itemText);
            }
        }

        return groups;
    }

    internal sealed class TaskGroup(string heading) {
        internal string Heading { get; } = heading;
        internal List<string> Items { get; } = [];
    }
}
