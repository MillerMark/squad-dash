using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>Pure graph rules used by the plan viewer to project approval boundaries.</summary>
internal static class PlanGateVisualizationPolicy
{
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

    internal static bool CompletelyCovers(
        PlanApprovalGate larger,
        IReadOnlyList<string> afterIds,
        IReadOnlyList<string> beforeIds) =>
        afterIds.All(id => larger.AfterTaskIds.Contains(id, StringComparer.Ordinal)) &&
        beforeIds.All(id => larger.BeforeTaskIds.Contains(id, StringComparer.Ordinal));
}
