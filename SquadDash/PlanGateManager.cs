using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Pure-logic helpers for adding, removing, and querying human approval gates on a <see cref="Plan"/>.
/// No WPF or IO dependencies — fully unit-testable.
/// </summary>
internal static class PlanGateManager
{
    /// <summary>
    /// Only a pending gate is still a plan-design choice. Once execution has requested,
    /// approved, or skipped a gate, its boundary and presentation anchor are durable history.
    /// </summary>
    internal static bool CanEditGate(PlanApprovalGate? gate) =>
        gate?.Status == PlanGateStatus.Pending;

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

    /// <summary>
    /// Returns true when a task feeds a terminal validation. Such a validation is a meaningful
    /// exit boundary even though no plan task depends on the task.
    /// </summary>
    internal static bool HasFinalValidationAfterTask(Plan plan, string taskId) =>
        (plan.Validations ?? []).Any(validation =>
            validation.BeforeTaskIds.Count == 0 &&
            validation.AfterTaskIds.Contains(taskId, StringComparer.Ordinal));

    /// <summary>Returns true when a gate already exists with the same boundary.</summary>
    internal static bool HasEquivalentGate(Plan plan,
        IReadOnlyList<string> afterIds, IReadOnlyList<string> beforeIds)
    {
        return FindEquivalentGate(plan, afterIds, beforeIds) is not null;
    }

    /// <summary>Returns the gate with the same boundary, or <c>null</c> when none exists.</summary>
    internal static PlanApprovalGate? FindEquivalentGate(Plan plan,
        IReadOnlyList<string> afterIds, IReadOnlyList<string> beforeIds)
    {
        return plan.ApprovalGates.FirstOrDefault(g =>
            g.AfterTaskIds.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(afterIds.OrderBy(x => x, StringComparer.Ordinal)) &&
            g.BeforeTaskIds.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(beforeIds.OrderBy(x => x, StringComparer.Ordinal)));
    }

    /// <summary>
    /// Adds one approval boundary between arbitrary non-overlapping task sets. Used by graph
    /// affordances such as an ALL join or a milestone between two displayed stages.
    /// </summary>
    internal static Plan AddBoundaryGate(Plan plan,
        IReadOnlyList<string> afterIds, IReadOnlyList<string> beforeIds, string message,
        string? presentationAnchor = null, bool removeSubsumedTaskGates = false)
    {
        var knownIds = plan.Tasks.Select(task => task.TaskId).ToHashSet(StringComparer.Ordinal);
        var normalizedAfter = afterIds.Distinct(StringComparer.Ordinal).ToArray();
        var normalizedBefore = beforeIds.Distinct(StringComparer.Ordinal).ToArray();
        if (normalizedAfter.Length == 0 || normalizedBefore.Length == 0 ||
            normalizedAfter.Any(id => !knownIds.Contains(id)) ||
            normalizedBefore.Any(id => !knownIds.Contains(id)) ||
            normalizedAfter.Intersect(normalizedBefore, StringComparer.Ordinal).Any() ||
            HasEquivalentGate(plan, normalizedAfter, normalizedBefore))
            return plan;

        var gate = new PlanApprovalGate(
            GateId:        NewGateId(plan),
            Message:       message,
            AfterTaskIds:  normalizedAfter,
            BeforeTaskIds: normalizedBefore,
            Status:        PlanGateStatus.Pending,
            PlanRevision:  plan.Revision,
            PresentationAnchor: presentationAnchor);

        var retained = removeSubsumedTaskGates
            ? plan.ApprovalGates.Where(existing => !IsSubsumedTaskGate(
                plan, existing, normalizedAfter, normalizedBefore)).ToArray()
            : plan.ApprovalGates;
        return plan with { ApprovalGates = [..retained, gate] };
    }

    private static bool IsSubsumedTaskGate(Plan plan, PlanApprovalGate existing,
        IReadOnlyCollection<string> groupAfter, IReadOnlyCollection<string> groupBefore)
    {
        // A task exit/before gate is redundant only when the larger boundary covers every
        // endpoint it controls. A task with an additional edge therefore keeps its own gate.
        var taskExit = existing.AfterTaskIds.Count == 1 &&
            plan.Tasks.Where(task => task.DependsOn.Contains(existing.AfterTaskIds[0], StringComparer.Ordinal))
                .Select(task => task.TaskId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(existing.BeforeTaskIds);
        var taskEntry = existing.BeforeTaskIds.Count == 1 &&
            plan.Tasks.FirstOrDefault(task => task.TaskId == existing.BeforeTaskIds[0]) is { } entryTask &&
            entryTask.DependsOn.ToHashSet(StringComparer.Ordinal).SetEquals(existing.AfterTaskIds);
        var taskScoped = taskExit || taskEntry;
        if (!taskScoped) return false;
        var groupGate = new PlanApprovalGate(
            "coverage", "coverage", groupAfter.ToArray(), groupBefore.ToArray(), PlanGateStatus.Pending);
        return PlanGateVisualizationPolicy.CompletelyCovers(
            plan.Tasks, groupGate, existing.AfterTaskIds, existing.BeforeTaskIds);
    }

    internal static Plan SetPresentationAnchor(Plan plan, string gateId, string presentationAnchor)
    {
        var changed = false;
        var gates = plan.ApprovalGates.Select(gate =>
        {
            if (!string.Equals(gate.GateId, gateId, StringComparison.Ordinal) ||
                !CanEditGate(gate) ||
                string.Equals(gate.PresentationAnchor, presentationAnchor, StringComparison.Ordinal))
                return gate;
            changed = true;
            return gate with { PresentationAnchor = presentationAnchor };
        }).ToArray();
        return changed ? plan with { ApprovalGates = gates } : plan;
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

        return AddBoundaryGate(plan, afterIds, beforeIds, message, $"task-before:{taskId}");
    }

    /// <summary>
    /// Adds a gate after <paramref name="taskId"/>, blocking tasks that directly depend on it.
    /// Gate: AfterTaskIds = [taskId], BeforeTaskIds = tasks where DependsOn.Contains(taskId).
    /// A leaf task may still receive a gate when it feeds a final validation. In that case the
    /// gate has an empty task frontier and guards the validation at the same boundary.
    /// Returns plan unchanged for a leaf with no final validation or an equivalent gate.
    /// </summary>
    internal static Plan AddGateAfter(Plan plan, string taskId, string message)
    {
        IReadOnlyList<string> afterIds  = [taskId];
        IReadOnlyList<string> beforeIds = plan.Tasks
            .Where(t => t.DependsOn.Contains(taskId, StringComparer.Ordinal))
            .Select(t => t.TaskId)
            .ToArray();

        if (beforeIds.Count == 0)
        {
            if (!HasFinalValidationAfterTask(plan, taskId) ||
                HasEquivalentGate(plan, afterIds, beforeIds))
                return plan;

            var terminalGate = new PlanApprovalGate(
                GateId: NewGateId(plan),
                Message: message,
                AfterTaskIds: afterIds,
                BeforeTaskIds: beforeIds,
                Status: PlanGateStatus.Pending,
                PlanRevision: plan.Revision,
                PresentationAnchor: $"task-after:{taskId}");
            return plan with { ApprovalGates = [..plan.ApprovalGates, terminalGate] };
        }

        return AddBoundaryGate(plan, afterIds, beforeIds, message, $"task-after:{taskId}");
    }

    /// <summary>Removes the gate with the given gateId. Returns plan unchanged if not found.</summary>
    internal static Plan RemoveGate(Plan plan, string gateId)
    {
        var remaining = plan.ApprovalGates
            .Where(g => !string.Equals(g.GateId, gateId, StringComparison.Ordinal) ||
                        !CanEditGate(g))
            .ToArray();
        if (remaining.Length == plan.ApprovalGates.Count) return plan;
        return plan with { ApprovalGates = remaining };
    }

    /// <summary>
    /// Applies gate-design changes from a viewer snapshot to the latest durable plan while
    /// preserving every gate that has already entered execution history. This is the final
    /// defense against a stale open viewer replacing an approved gate with an older snapshot.
    /// </summary>
    internal static Plan ApplyEditableGateChanges(Plan current, Plan proposed)
    {
        var currentById = current.ApprovalGates.ToDictionary(gate => gate.GateId, StringComparer.Ordinal);
        var proposedById = proposed.ApprovalGates.ToDictionary(gate => gate.GateId, StringComparer.Ordinal);
        var merged = new List<PlanApprovalGate>();

        foreach (var currentGate in current.ApprovalGates)
        {
            if (!CanEditGate(currentGate))
            {
                merged.Add(currentGate);
                continue;
            }

            // Omitting a still-pending gate is the viewer's supported remove operation.
            if (proposedById.TryGetValue(currentGate.GateId, out var proposedGate) &&
                proposedGate.Status == PlanGateStatus.Pending)
                merged.Add(proposedGate);
        }

        foreach (var proposedGate in proposed.ApprovalGates)
        {
            if (currentById.ContainsKey(proposedGate.GateId) ||
                proposedGate.Status != PlanGateStatus.Pending)
                continue;
            merged.Add(proposedGate);
        }

        if (current.ApprovalGates.SequenceEqual(merged)) return current;
        return current with { ApprovalGates = merged };
    }

    /// <summary>
    /// Returns true when the gate should trigger a notification — i.e. it has never been notified before.
    /// Guard is based on <see cref="PlanApprovalGate.NotifiedAt"/> being null.
    /// </summary>
    internal static bool ShouldNotifyGateActivation(PlanApprovalGate gate) => gate.NotifiedAt is null;
}
