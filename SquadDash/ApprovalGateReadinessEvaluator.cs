using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Readiness state for a single approval gate.
/// Separates "gate prerequisites satisfied" from "plan must stop."
/// </summary>
internal sealed record GateReadinessState(
    string GateId,
    /// <summary>True when every AfterTaskId is complete — the gate can accept approval.</summary>
    bool IsReady,
    /// <summary>Task IDs that are downstream of this gate (directly or transitively blocked).</summary>
    IReadOnlySet<string> DownstreamFrontier);

/// <summary>
/// Pure-logic evaluator for approval-aware task scheduling.
/// Computes gate readiness, downstream frontiers, eligible ungated tasks,
/// and the plan-level stop condition — all without side effects.
/// </summary>
internal static class ApprovalGateReadinessEvaluator
{
    /// <summary>
    /// Evaluates readiness for every pending gate on the plan.
    /// A gate is ready when every task in <see cref="PlanApprovalGate.AfterTaskIds"/> has
    /// status <see cref="PlanTaskStatus.Complete"/> or <see cref="PlanTaskStatus.Superseded"/>.
    /// </summary>
    internal static IReadOnlyList<GateReadinessState> EvaluateGates(Plan plan)
    {
        var terminalIds = plan.Tasks
            .Where(t => t.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded)
            .Select(t => t.TaskId)
            .ToHashSet(StringComparer.Ordinal);

        var results = new List<GateReadinessState>();
        foreach (var gate in plan.ApprovalGates)
        {
            if (gate.Status is PlanGateStatus.Approved or PlanGateStatus.Skipped)
                continue;

            var isReady = gate.AfterTaskIds.All(id => terminalIds.Contains(id));
            var frontier = ComputeDownstreamFrontier(plan, gate);
            results.Add(new GateReadinessState(gate.GateId, isReady, frontier));
        }
        return results;
    }

    /// <summary>
    /// Returns the set of task IDs that are downstream of a gate — i.e. tasks in
    /// <see cref="PlanApprovalGate.BeforeTaskIds"/> plus every task that depends
    /// (directly or transitively) on those gated tasks.
    /// </summary>
    internal static IReadOnlySet<string> ComputeDownstreamFrontier(Plan plan, PlanApprovalGate gate)
    {
        var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var task in plan.Tasks)
        {
            foreach (var dep in task.DependsOn)
            {
                if (!dependents.TryGetValue(dep, out var list))
                {
                    list = new List<string>();
                    dependents[dep] = list;
                }
                list.Add(task.TaskId);
            }
        }

        var frontier = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(gate.BeforeTaskIds);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!frontier.Add(id)) continue;
            if (dependents.TryGetValue(id, out var children))
            {
                foreach (var child in children)
                    queue.Enqueue(child);
            }
        }
        return frontier;
    }

    /// <summary>
    /// Selects the next eligible task that is NOT behind any unapproved gate.
    /// Uses deterministic ordering: tasks appear in plan declaration order,
    /// filtered by dependency readiness and gate exclusion.
    /// </summary>
    internal static string? SelectNextUngatedTask(
        Plan plan,
        IReadOnlyList<GateReadinessState>? gateStates = null)
    {
        gateStates ??= EvaluateGates(plan);
        var blockedIds = ComputeAllBlockedTaskIds(plan, gateStates);
        var completedIds = GetTerminalTaskIds(plan);

        // Tasks in declaration order, filtered for eligibility
        foreach (var task in plan.Tasks)
        {
            if (task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded
                or PlanTaskStatus.Failed or PlanTaskStatus.Executing)
                continue;
            if (blockedIds.Contains(task.TaskId))
                continue;
            if (task.DependsOn.All(id => completedIds.Contains(id)))
                return task.TaskId;
        }
        return null;
    }

    /// <summary>
    /// Returns all task IDs that are blocked by unapproved gates — the union
    /// of downstream frontiers for all non-approved, non-skipped gates.
    /// </summary>
    internal static IReadOnlySet<string> ComputeAllBlockedTaskIds(
        Plan plan,
        IReadOnlyList<GateReadinessState>? gateStates = null)
    {
        gateStates ??= EvaluateGates(plan);
        var blocked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var gs in gateStates)
            blocked.UnionWith(gs.DownstreamFrontier);
        return blocked;
    }

    /// <summary>
    /// Determines whether the plan execution loop should stop because no ungated
    /// eligible work remains.
    /// Returns <c>true</c> when the only remaining ready tasks are behind unapproved gates.
    /// </summary>
    internal static bool ShouldStopForApproval(
        Plan plan,
        IReadOnlyList<GateReadinessState>? gateStates = null)
    {
        gateStates ??= EvaluateGates(plan);
        if (gateStates.Count == 0)
            return false;

        // If there's any ungated eligible task, don't stop.
        if (SelectNextUngatedTask(plan, gateStates) is not null)
            return false;

        // Check if there are any ready gates with pending downstream tasks
        var completedIds = GetTerminalTaskIds(plan);
        foreach (var gs in gateStates)
        {
            if (!gs.IsReady) continue;
            var gate = plan.ApprovalGates.FirstOrDefault(candidate =>
                string.Equals(candidate.GateId, gs.GateId, StringComparison.Ordinal));
            // A final human-proof checkpoint has no downstream task frontier; it gates plan
            // completion itself and must still stop for explicit human attestation.
            if (gate?.ProofRequirements is { Count: > 0 } && gs.DownstreamFrontier.Count == 0)
                return true;
            // Check if any task in the frontier has all non-gate dependencies satisfied
            foreach (var taskId in gs.DownstreamFrontier)
            {
                var task = plan.Tasks.FirstOrDefault(t =>
                    string.Equals(t.TaskId, taskId, StringComparison.Ordinal));
                if (task is null) continue;
                if (task.Status is PlanTaskStatus.Pending &&
                    task.DependsOn.All(id => completedIds.Contains(id) ||
                        gs.DownstreamFrontier.Contains(id)))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the set of ready gate IDs — gates whose AfterTaskIds are all complete
    /// and whose status is still Pending.
    /// </summary>
    internal static IReadOnlyList<string> GetReadyGateIds(
        IReadOnlyList<GateReadinessState> gateStates)
    {
        return gateStates
            .Where(gs => gs.IsReady)
            .Select(gs => gs.GateId)
            .ToList();
    }

    /// <summary>
    /// Returns task IDs that became eligible after a gate was approved.
    /// These are tasks in the gate's BeforeTaskIds whose non-gated dependencies
    /// are all complete.
    /// </summary>
    internal static IReadOnlyList<string> GetReleasedTaskIds(
        Plan plan,
        string approvedGateId)
    {
        var gate = plan.ApprovalGates.FirstOrDefault(g =>
            string.Equals(g.GateId, approvedGateId, StringComparison.Ordinal));
        if (gate is null) return [];

        var completedIds = GetTerminalTaskIds(plan);
        var remainingGateStates = EvaluateGates(plan);
        var stillBlockedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var gs in remainingGateStates)
        {
            if (string.Equals(gs.GateId, approvedGateId, StringComparison.Ordinal))
                continue;
            stillBlockedIds.UnionWith(gs.DownstreamFrontier);
        }

        return gate.BeforeTaskIds
            .Where(id => !stillBlockedIds.Contains(id))
            .Where(id =>
            {
                var task = plan.Tasks.FirstOrDefault(t =>
                    string.Equals(t.TaskId, id, StringComparison.Ordinal));
                return task is not null &&
                       task.Status == PlanTaskStatus.Pending &&
                       task.DependsOn.All(d => completedIds.Contains(d));
            })
            .ToList();
    }

    /// <summary>
    /// Returns IDs of tasks that are in a terminal state (complete or superseded).
    /// </summary>
    internal static IReadOnlySet<string> GetTerminalTaskIds(Plan plan) =>
        plan.Tasks
            .Where(t => t.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded)
            .Select(t => t.TaskId)
            .ToHashSet(StringComparer.Ordinal);

    internal static bool AllRequiredApproved(Plan plan) =>
        plan.ApprovalGates.All(gate =>
            gate.Status is PlanGateStatus.Approved or PlanGateStatus.Skipped);
}
