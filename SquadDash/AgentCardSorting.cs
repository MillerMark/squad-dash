using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Pure-logic helper that computes the sort key used to order agent cards in the roster panel.
/// Extracted from MainWindow so it can be unit-tested without WPF dependencies.
///
/// Sort order (ascending tuple comparison):
///   Group 0 — the lead/coordinator agent (always leftmost)
///   Group 1 — roster agents (non-utility), ordered by most-recently-activated first
///   Group 2 — Scribe (always last among named roster agents)
///   Group 3 — dynamic/temporary agents, ordered by most-recently-activated first
///
/// Within groups 1, 2 and 3:
///   SortTicks = long.MaxValue - max(thread.LastActivityAt.UtcTicks)
///   → larger last-activity tick  ⇒  smaller SortTicks  ⇒  sorts LEFT (leftmost = most recent)
///   Agents with no (non-placeholder) threads receive SortTicks = long.MaxValue (rightmost).
///
/// Callers must supply last-activity ticks computed via GetThreadLastActivityAt() and must
/// exclude placeholder threads — their StartedAt reflects UI interaction time, not agent work.
/// </summary>
internal static class AgentCardSorting {
    internal static (int Group, long SortTicks, string Name) ComputeSortKey(
        bool isLeadAgent,
        bool isDynamicAgent,
        IReadOnlyList<long> threadLastActivityAtUtcTicks,
        string name,
        bool isScribe = false) {
        if (isLeadAgent)
            return (0, 0, name);

        var sortTicks = threadLastActivityAtUtcTicks.Count == 0
            ? long.MaxValue
            : long.MaxValue - threadLastActivityAtUtcTicks.Max();

        if (isDynamicAgent)
            return (3, sortTicks, name);

        if (isScribe)
            return (2, sortTicks, name);

        return (1, sortTicks, name);
    }
}
