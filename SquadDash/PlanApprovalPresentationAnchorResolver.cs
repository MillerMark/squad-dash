using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Chooses one deterministic visual/prose controller for approval gates created before
/// presentation anchors were persisted. Explicit user-selected anchors always win.
/// </summary>
internal static class PlanApprovalPresentationAnchorResolver
{
    internal static string? Resolve(
        PlanApprovalGate gate,
        IReadOnlyList<PlanTask> tasks,
        IReadOnlyDictionary<string, int> levels)
    {
        if (!string.IsNullOrWhiteSpace(gate.PresentationAnchor))
            return gate.PresentationAnchor;

        var stageCount = levels.Count == 0 ? 0 : levels.Values.Max() + 1;
        for (var leftLevel = 0; leftLevel < stageCount - 1; leftLevel++)
        {
            var immediateAfter = tasks.Where(task => levels.GetValueOrDefault(task.TaskId) == leftLevel)
                .Select(task => task.TaskId).ToArray();
            var immediateBefore = tasks.Where(task => levels.GetValueOrDefault(task.TaskId) == leftLevel + 1)
                .Select(task => task.TaskId).ToArray();
            var legacyAfter = tasks.Where(task => levels.GetValueOrDefault(task.TaskId) <= leftLevel)
                .Select(task => task.TaskId).ToArray();
            var legacyBefore = tasks.Where(task => levels.GetValueOrDefault(task.TaskId) > leftLevel)
                .Select(task => task.TaskId).ToArray();
            if (SameBoundary(gate, immediateAfter, immediateBefore) ||
                SameBoundary(gate, legacyAfter, legacyBefore))
                return $"stage:{leftLevel + 1}";
        }

        var allGroups = tasks.Where(task => task.DependsOn.Count > 1)
            .GroupBy(task => string.Join("\u001f", task.DependsOn.OrderBy(id => id, StringComparer.Ordinal)));
        foreach (var group in allGroups)
        {
            var targets = group.Select(task => task.TaskId).ToArray();
            var dependencies = group.First().DependsOn.ToArray();
            if (SameBoundary(gate, dependencies, targets))
                return "all:" + string.Join("|", targets.OrderBy(id => id, StringComparer.Ordinal));
        }

        if (gate.AfterTaskIds.Count == 1) return $"task-after:{gate.AfterTaskIds[0]}";
        if (gate.BeforeTaskIds.Count == 1) return $"task-before:{gate.BeforeTaskIds[0]}";
        return null;
    }

    private static bool SameBoundary(
        PlanApprovalGate gate,
        IReadOnlyList<string> afterIds,
        IReadOnlyList<string> beforeIds) =>
        gate.AfterTaskIds.ToHashSet(StringComparer.Ordinal).SetEquals(afterIds) &&
        gate.BeforeTaskIds.ToHashSet(StringComparer.Ordinal).SetEquals(beforeIds);
}
