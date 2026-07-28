using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Pure-logic helpers for adding, removing, and querying human approval gates on a <see cref="Plan"/>.
/// No WPF or IO dependencies — fully unit-testable.
/// </summary>
internal static class PlanGateManager
{
    /// <summary>Returns true when <paramref name="taskId"/> has no DependsOn (root task).</summary>
    internal static bool IsRootTask(Plan plan, string taskId)
    {
        var task = plan.Tasks.FirstOrDefault(t => t.TaskId == taskId);
        return task is not null && (task.DependsOn is null || task.DependsOn.Count == 0);
    }

    /// <summary>Returns true when no task depends on <paramref name="taskId"/> (leaf task).</summary>
    internal static bool IsLeafTask(Plan plan, string taskId)
    {
        return !plan.Tasks.Any(t => t.DependsOn.Contains(taskId, StringComparer.Ordinal));
    }

    /// <summary>Returns true when a gate already exists with the same boundary.</summary>
    internal static bool HasEquivalentGate(Plan plan,
        IReadOnlyList<string> afterIds, IReadOnlyList<string> beforeIds)
    {
        return plan.ApprovalGates.Any(g =>
            g.AfterTaskIds.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(afterIds.OrderBy(x => x, StringComparer.Ordinal)) &&
            g.BeforeTaskIds.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(beforeIds.OrderBy(x => x, StringComparer.Ordinal)));
    }

    /// <summary>Generates next stable gate ID: "{planId}-GATE-001", "...GATE-002", etc.</summary>
    internal static string NewGateId(Plan plan)
    {
        var prefix = $"{plan.PlanId}-GATE-";
        var existingNumbers = plan.ApprovalGates
            .Select(g => g.GateId)
            .Where(id => id.StartsWith(prefix, StringComparison.Ordinal))
            .Select(id => id[prefix.Length..])
            .Where(suffix => int.TryParse(suffix, out _))
            .Select(int.Parse)
            .ToHashSet();

        var next = 1;
        while (existingNumbers.Contains(next)) next++;
        return $"{prefix}{next:D3}";
    }

    /// <summary>
    /// Adds a gate that blocks <paramref name="taskId"/> until approved.
    /// Gate: AfterTaskIds = task.DependsOn, BeforeTaskIds = [taskId].
    /// Returns plan unchanged when taskId is a root task or an equivalent gate already exists.
    /// </summary>
    internal static Plan AddGateBefore(Plan plan, string taskId, string message)
    {
        if (IsRootTask(plan, taskId)) return plan;

        var task = plan.Tasks.First(t => t.TaskId == taskId);
        IReadOnlyList<string> afterIds  = task.DependsOn ?? [];
        IReadOnlyList<string> beforeIds = [taskId];

        if (HasEquivalentGate(plan, afterIds, beforeIds)) return plan;

        var gate = new PlanApprovalGate(
            GateId:        NewGateId(plan),
            Message:       message,
            AfterTaskIds:  afterIds,
            BeforeTaskIds: beforeIds,
            Status:        PlanGateStatus.Pending,
            PlanRevision:  plan.Revision);

        return plan with { ApprovalGates = [..plan.ApprovalGates, gate] };
    }

    /// <summary>
    /// Adds a gate after <paramref name="taskId"/>, blocking tasks that directly depend on it.
    /// Gate: AfterTaskIds = [taskId], BeforeTaskIds = tasks where DependsOn.Contains(taskId).
    /// Returns plan unchanged when taskId is a leaf task or an equivalent gate already exists.
    /// </summary>
    internal static Plan AddGateAfter(Plan plan, string taskId, string message)
    {
        if (IsLeafTask(plan, taskId)) return plan;

        IReadOnlyList<string> afterIds  = [taskId];
        IReadOnlyList<string> beforeIds = plan.Tasks
            .Where(t => t.DependsOn.Contains(taskId, StringComparer.Ordinal))
            .Select(t => t.TaskId)
            .ToArray();

        if (HasEquivalentGate(plan, afterIds, beforeIds)) return plan;

        var gate = new PlanApprovalGate(
            GateId:        NewGateId(plan),
            Message:       message,
            AfterTaskIds:  afterIds,
            BeforeTaskIds: beforeIds,
            Status:        PlanGateStatus.Pending,
            PlanRevision:  plan.Revision);

        return plan with { ApprovalGates = [..plan.ApprovalGates, gate] };
    }

    /// <summary>Removes the gate with the given gateId. Returns plan unchanged if not found.</summary>
    internal static Plan RemoveGate(Plan plan, string gateId)
    {
        var remaining = plan.ApprovalGates
            .Where(g => !string.Equals(g.GateId, gateId, StringComparison.Ordinal))
            .ToArray();
        if (remaining.Length == plan.ApprovalGates.Count) return plan;
        return plan with { ApprovalGates = remaining };
    }

    /// <summary>
    /// Returns true when the gate should trigger a notification — i.e. it has never been notified before.
    /// Guard is based on <see cref="PlanApprovalGate.NotifiedAt"/> being null.
    /// </summary>
    internal static bool ShouldNotifyGateActivation(PlanApprovalGate gate) => gate.NotifiedAt is null;
}
