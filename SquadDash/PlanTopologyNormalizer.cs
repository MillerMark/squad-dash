using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

internal sealed record PlanTopologyNormalizationChange(
    string Kind,
    string OwnerId,
    IReadOnlyList<string> RemovedIds);

internal sealed record PlanTopologyNormalizationResult(
    DecomposedTaskGroup Group,
    IReadOnlyList<PlanTopologyNormalizationChange> Changes)
{
    internal int RemovedDependencyCount => Changes
        .Where(change => change.Kind == "task-dependency")
        .Sum(change => change.RemovedIds.Count);

    internal int RemovedGateEndpointCount => Changes
        .Where(change => change.Kind is "gate-after" or "gate-before")
        .Sum(change => change.RemovedIds.Count);

    internal bool HasChanges => Changes.Count > 0;
}

/// <summary>
/// Computes the transitive reduction of a valid plan DAG and canonicalizes approval-gate
/// frontiers without changing task reachability or gate blocking behavior.
/// Invalid references and cycles are reported to the caller; they are never guessed at or repaired.
/// </summary>
internal static class PlanTopologyNormalizer
{
    internal static bool TryNormalize(
        DecomposedTaskGroup group,
        out PlanTopologyNormalizationResult? result,
        out string? error)
    {
        result = null;
        error = null;

        var tasksById = group.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        foreach (var task in group.Tasks)
        {
            var unknown = task.DependsOn.FirstOrDefault(id => !tasksById.ContainsKey(id));
            if (unknown is not null)
            {
                error = $"Task '{task.Id}' depends on unknown task ID '{unknown}'. " +
                        "The planning AI must correct the reference.";
                return false;
            }
        }

        foreach (var gate in group.ApprovalGates ?? [])
        {
            var unknownAfter = (gate.AfterTaskIds ?? []).FirstOrDefault(id => !tasksById.ContainsKey(id));
            if (unknownAfter is not null)
            {
                error = $"Approval gate '{gate.GateId}' references unknown after-task ID " +
                        $"'{unknownAfter}'. The planning AI must correct the reference.";
                return false;
            }
            var unknownBefore = (gate.BeforeTaskIds ?? []).FirstOrDefault(id => !tasksById.ContainsKey(id));
            if (unknownBefore is not null)
            {
                error = $"Approval gate '{gate.GateId}' references unknown before-task ID " +
                        $"'{unknownBefore}'. The planning AI must correct the reference.";
                return false;
            }
        }

        if (!CodeHealthGroupRunner.HasNoDependencyCycle(group, out var cycleIds))
        {
            error = "The task dependency graph contains a cycle involving: " +
                    string.Join(", ", cycleIds ?? []);
            return false;
        }

        bool IsAncestor(string ancestorId, string descendantId)
        {
            if (string.Equals(ancestorId, descendantId, StringComparison.Ordinal)) return false;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(descendantId);
            while (pending.TryPop(out var currentId))
            {
                if (!visited.Add(currentId) || !tasksById.TryGetValue(currentId, out var current))
                    continue;
                foreach (var dependencyId in current.DependsOn)
                {
                    if (string.Equals(dependencyId, ancestorId, StringComparison.Ordinal)) return true;
                    pending.Push(dependencyId);
                }
            }
            return false;
        }

        var changes = new List<PlanTopologyNormalizationChange>();
        var normalizedTasks = group.Tasks.Select(task =>
        {
            var distinct = task.DependsOn.Distinct(StringComparer.Ordinal).ToArray();
            var retained = distinct.Where(dependencyId => !distinct.Any(otherId =>
                    !string.Equals(otherId, dependencyId, StringComparison.Ordinal) &&
                    IsAncestor(dependencyId, otherId)))
                .ToArray();
            var removed = distinct.Except(retained, StringComparer.Ordinal)
                .Concat(task.DependsOn.GroupBy(id => id, StringComparer.Ordinal)
                    .Where(grouping => grouping.Count() > 1)
                    .SelectMany(grouping => Enumerable.Repeat(grouping.Key, grouping.Count() - 1)))
                .ToArray();
            if (removed.Length > 0)
                changes.Add(new PlanTopologyNormalizationChange(
                    "task-dependency", task.Id, removed));
            return removed.Length == 0 ? task : task with { DependsOn = retained };
        }).ToArray();

        // Reachability is unchanged by the task reduction, so the original graph can safely
        // canonicalize gate frontiers. For an after frontier, retain the latest descendants;
        // for a before frontier, retain the earliest ancestors.
        var normalizedGates = (group.ApprovalGates ?? []).Select(gate =>
        {
            var after = (gate.AfterTaskIds ?? []).Distinct(StringComparer.Ordinal).ToArray();
            var before = (gate.BeforeTaskIds ?? []).Distinct(StringComparer.Ordinal).ToArray();
            var retainedAfter = after.Where(id => !after.Any(other =>
                !string.Equals(id, other, StringComparison.Ordinal) && IsAncestor(id, other))).ToArray();
            var retainedBefore = before.Where(id => !before.Any(other =>
                !string.Equals(id, other, StringComparison.Ordinal) && IsAncestor(other, id))).ToArray();
            var removedAfter = after.Except(retainedAfter, StringComparer.Ordinal).ToArray();
            var removedBefore = before.Except(retainedBefore, StringComparer.Ordinal).ToArray();
            if (removedAfter.Length > 0)
                changes.Add(new PlanTopologyNormalizationChange("gate-after", gate.GateId, removedAfter));
            if (removedBefore.Length > 0)
                changes.Add(new PlanTopologyNormalizationChange("gate-before", gate.GateId, removedBefore));
            return removedAfter.Length == 0 && removedBefore.Length == 0
                ? gate
                : gate with { AfterTaskIds = retainedAfter, BeforeTaskIds = retainedBefore };
        }).ToArray();

        var normalized = group with
        {
            Tasks = normalizedTasks,
            ApprovalGates = normalizedGates.Length == 0 ? null : normalizedGates,
        };
        if (changes.Count > 0)
            normalized = DependencyEditRecomputer.SynchronizeApprovalPresentationAnchors(normalized);
        result = new PlanTopologyNormalizationResult(normalized, changes);
        return true;
    }
}
