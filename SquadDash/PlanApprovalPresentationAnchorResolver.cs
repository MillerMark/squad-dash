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
        var candidates = new List<string>();

        void AddCandidate(string anchor)
        {
            if (!candidates.Contains(anchor, StringComparer.Ordinal))
                candidates.Add(anchor);
        }

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
                AddCandidate($"stage:{leftLevel + 1}");
        }

        var allGroups = tasks.Where(task => task.DependsOn.Count > 1)
            .GroupBy(task => string.Join("\u001f", task.DependsOn.OrderBy(id => id, StringComparer.Ordinal)));
        var joins = allGroups.Select(group => (
            Targets: group.Select(task => task.TaskId).ToArray(),
            Dependencies: group.First().DependsOn.ToArray())).ToArray();
        foreach (var join in joins)
        {
            if (SameBoundary(gate, join.Dependencies, join.Targets))
                AddCandidate(AllAnchor(join.Targets));
        }

        if (gate.AfterTaskIds.Count == 1 &&
            tasks.Any(task => string.Equals(task.TaskId, gate.AfterTaskIds[0], StringComparison.Ordinal)))
        {
            var taskId = gate.AfterTaskIds[0];
            var directDependents = tasks
                .Where(task => task.DependsOn.Contains(taskId, StringComparer.Ordinal))
                .Select(task => task.TaskId)
                .ToArray();
            if (PlanGateVisualizationPolicy.GraphEquivalent(
                    tasks,
                    gate.AfterTaskIds, gate.BeforeTaskIds,
                    [taskId], directDependents))
                AddCandidate($"task-after:{taskId}");
        }
        if (gate.BeforeTaskIds.Count == 1 &&
            tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, gate.BeforeTaskIds[0], StringComparison.Ordinal)) is { } beforeTask &&
            beforeTask.DependsOn.Count > 0 &&
            PlanGateVisualizationPolicy.GraphEquivalent(
                tasks,
                gate.AfterTaskIds, gate.BeforeTaskIds,
                beforeTask.DependsOn, [beforeTask.TaskId]))
            AddCandidate($"task-before:{beforeTask.TaskId}");

        // A broad approval boundary is also projected onto the ALL joins and task edges it
        // completely covers. Those projections are real controls, so one of them must become
        // the primary when a dependency edit makes the previously stored anchor disappear.
        foreach (var join in joins)
            if (PlanGateVisualizationPolicy.CompletelyCovers(
                    tasks, gate, join.Dependencies, join.Targets))
                AddCandidate(AllAnchor(join.Targets));

        foreach (var task in tasks)
        {
            var directDependents = tasks
                .Where(candidate => candidate.DependsOn.Contains(task.TaskId, StringComparer.Ordinal))
                .Select(candidate => candidate.TaskId)
                .ToArray();
            if (directDependents.Length > 0 &&
                PlanGateVisualizationPolicy.CompletelyCovers(
                    tasks, gate, [task.TaskId], directDependents))
                AddCandidate($"task-after:{task.TaskId}");
        }

        foreach (var task in tasks.Where(task => task.DependsOn.Count > 0))
            if (PlanGateVisualizationPolicy.CompletelyCovers(
                    tasks, gate, task.DependsOn, [task.TaskId]))
                AddCandidate($"task-before:{task.TaskId}");

        if (!string.IsNullOrWhiteSpace(gate.PresentationAnchor) &&
            candidates.Contains(gate.PresentationAnchor, StringComparer.Ordinal))
            return gate.PresentationAnchor;

        return candidates.FirstOrDefault();
    }

    private static string AllAnchor(IEnumerable<string> targetIds) =>
        "all:" + string.Join("|", targetIds.OrderBy(id => id, StringComparer.Ordinal));

    private static bool SameBoundary(
        PlanApprovalGate gate,
        IReadOnlyList<string> afterIds,
        IReadOnlyList<string> beforeIds) =>
        gate.AfterTaskIds.ToHashSet(StringComparer.Ordinal).SetEquals(afterIds) &&
        gate.BeforeTaskIds.ToHashSet(StringComparer.Ordinal).SetEquals(beforeIds);
}
