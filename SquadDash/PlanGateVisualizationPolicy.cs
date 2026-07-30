using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>Pure graph rules used by the plan viewer to project approval boundaries.</summary>
internal static class PlanGateVisualizationPolicy
{
    internal static bool GraphEquivalent(
        IReadOnlyList<PlanTask> tasks,
        IReadOnlyList<string> firstAfter,
        IReadOnlyList<string> firstBefore,
        IReadOnlyList<string> secondAfter,
        IReadOnlyList<string> secondBefore)
    {
        var byId = tasks.ToDictionary(task => task.TaskId, StringComparer.Ordinal);

        bool IsStrictAncestor(string ancestor, string descendant)
        {
            if (string.Equals(ancestor, descendant, StringComparison.Ordinal)) return false;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(descendant);
            while (pending.TryPop(out var current))
            {
                if (!visited.Add(current) || !byId.TryGetValue(current, out var task)) continue;
                if (task.DependsOn.Contains(ancestor, StringComparer.Ordinal)) return true;
                foreach (var dependency in task.DependsOn) pending.Push(dependency);
            }
            return false;
        }

        HashSet<string> CanonicalAfter(IReadOnlyList<string> ids) => ids
            .Where(id => !ids.Any(other => IsStrictAncestor(id, other)))
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string> CanonicalBefore(IReadOnlyList<string> ids) => ids
            .Where(id => !ids.Any(other => IsStrictAncestor(other, id)))
            .ToHashSet(StringComparer.Ordinal);

        return CanonicalAfter(firstAfter).SetEquals(CanonicalAfter(secondAfter)) &&
               CanonicalBefore(firstBefore).SetEquals(CanonicalBefore(secondBefore));
    }

    internal static HashSet<string> DownstreamTaskIds(
        IReadOnlyList<PlanTask> tasks, IReadOnlyList<PlanApprovalGate> gates)
    {
        var downstream = gates.SelectMany(gate => gate.BeforeTaskIds)
            .ToHashSet(StringComparer.Ordinal);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var task in tasks)
                if (!downstream.Contains(task.TaskId) &&
                    task.DependsOn.Any(downstream.Contains))
                    changed |= downstream.Add(task.TaskId);
        }
        return downstream;
    }

    internal static HashSet<(string From, string To)> DashedEdges(
        IReadOnlyList<PlanTask> tasks,
        IReadOnlyList<PlanApprovalGate> gates,
        bool requireEveryIncomingAtConvergence)
    {
        var edges = tasks.SelectMany(task => task.DependsOn.Select(dependency =>
            (From: dependency, To: task.TaskId))).ToArray();
        var dashed = new HashSet<(string From, string To)>();

        // Seed the actual graph cut for every approval gate. A target beyond the gate is
        // downstream; an edge entering that territory is the first dashed segment.
        foreach (var gate in gates)
        {
            var downstream = gate.BeforeTaskIds.ToHashSet(StringComparer.Ordinal);
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var task in tasks)
                    if (!downstream.Contains(task.TaskId) && task.DependsOn.Any(downstream.Contains))
                        changed |= downstream.Add(task.TaskId);
            }
            foreach (var edge in edges)
                if (gate.AfterTaskIds.Contains(edge.From, StringComparer.Ordinal) &&
                    downstream.Contains(edge.To))
                    dashed.Add(edge);
        }

        var byLevel = TopologicalLevels(tasks);
        foreach (var task in tasks.OrderBy(task => byLevel.GetValueOrDefault(task.TaskId)))
        {
            if (task.DependsOn.Count == 0) continue;
            var incoming = task.DependsOn.Select(dependency =>
                dashed.Contains((dependency, task.TaskId))).ToArray();
            var propagate = requireEveryIncomingAtConvergence
                ? incoming.All(value => value)
                : incoming.Any(value => value);
            if (!propagate) continue;
            foreach (var edge in edges.Where(edge => edge.From == task.TaskId)) dashed.Add(edge);
        }
        return dashed;
    }

    private static Dictionary<string, int> TopologicalLevels(IReadOnlyList<PlanTask> tasks)
    {
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        var unresolved = tasks.ToDictionary(task => task.TaskId, StringComparer.Ordinal);
        while (unresolved.Count > 0)
        {
            var progressed = false;
            foreach (var task in unresolved.Values.ToArray())
            {
                if (task.DependsOn.Any(id => unresolved.ContainsKey(id))) continue;
                levels[task.TaskId] = task.DependsOn.Count == 0
                    ? 0
                    : task.DependsOn.Select(id => levels.GetValueOrDefault(id)).Max() + 1;
                unresolved.Remove(task.TaskId);
                progressed = true;
            }
            if (progressed) continue;
            foreach (var task in unresolved.Values) levels[task.TaskId] = 0;
            break;
        }
        return levels;
    }

    internal static bool CompletelyCovers(
        IReadOnlyList<PlanTask> tasks,
        PlanApprovalGate larger,
        IReadOnlyList<string> afterIds,
        IReadOnlyList<string> beforeIds)
    {
        var byId = tasks.ToDictionary(task => task.TaskId, StringComparer.Ordinal);

        bool IsAncestorOrSelfOfAny(string candidate, IReadOnlyList<string> descendants)
        {
            if (descendants.Contains(candidate, StringComparer.Ordinal)) return true;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(descendants);
            while (pending.TryPop(out var current))
            {
                if (!visited.Add(current) || !byId.TryGetValue(current, out var task)) continue;
                if (task.DependsOn.Contains(candidate, StringComparer.Ordinal)) return true;
                foreach (var dependency in task.DependsOn) pending.Push(dependency);
            }
            return false;
        }

        bool IsDescendantOrSelfOfAny(string candidate, IReadOnlyList<string> ancestors)
        {
            if (ancestors.Contains(candidate, StringComparer.Ordinal)) return true;
            var visited = ancestors.ToHashSet(StringComparer.Ordinal);
            var pending = new Queue<string>(ancestors);
            while (pending.TryDequeue(out var current))
            {
                foreach (var dependent in tasks.Where(task =>
                             task.DependsOn.Contains(current, StringComparer.Ordinal)))
                {
                    if (!visited.Add(dependent.TaskId)) continue;
                    if (string.Equals(dependent.TaskId, candidate, StringComparison.Ordinal)) return true;
                    pending.Enqueue(dependent.TaskId);
                }
            }
            return false;
        }

        // Completing the larger boundary must imply all candidate prerequisites, and every
        // candidate target must be at or downstream of the larger boundary's blocked frontier.
        return afterIds.All(id => IsAncestorOrSelfOfAny(id, larger.AfterTaskIds)) &&
               beforeIds.All(id => IsDescendantOrSelfOfAny(id, larger.BeforeTaskIds));
    }
}
