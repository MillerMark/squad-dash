using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Result of a <see cref="DependencyEditRecomputer.Recompute"/> call.
/// On failure (cycle detected), <see cref="Success"/> is <c>false</c> and
/// <see cref="CycleIds"/> lists the participating task IDs.
/// On success, all recomputed fields are populated.
/// </summary>
internal sealed record DependencyEditRecomputeResult
{
    internal bool   Success   { get; private init; }
    internal string? Error    { get; private init; }

    /// <summary>Task IDs involved in the dependency cycle; non-null only when <see cref="Success"/> is false.</summary>
    internal IReadOnlyList<string>? CycleIds { get; private init; }

    /// <summary>
    /// The recomputed group with tasks sorted in topological order (by stage level, then by
    /// their original list position within the same level). All other group properties are
    /// carried through unmodified.
    /// </summary>
    internal DecomposedTaskGroup? RecomputedGroup { get; private init; }

    /// <summary>
    /// Maps each task ID to its 0-based stage index (column in the plan visualizer).
    /// Stage 0 corresponds to "Stage 1" in the UI. A task whose all dependencies are
    /// removed lands at stage 0.
    /// </summary>
    internal IReadOnlyDictionary<string, int>? StageAssignments { get; private init; }

    /// <summary>
    /// Maps each task ID to its 1-based sequential display label within the recomputed
    /// topological order — matching the label that
    /// <see cref="PendingDecomposePlanAdapter.ToPlan"/> would assign if building the
    /// durable plan from scratch for the same graph.
    /// </summary>
    internal IReadOnlyDictionary<string, string>? DisplayLabels { get; private init; }

    /// <summary>
    /// Maps each approval gate ID to its resolved presentation anchor string (e.g.
    /// <c>"stage:1"</c>, <c>"all:T1|T2"</c>, <c>"task-after:T1"</c>) computed from
    /// the new stage assignments. Mirrors the anchor resolution used by the Plan
    /// Visualizer when rendering gates.
    /// </summary>
    internal IReadOnlyDictionary<string, string>? ApprovalBoundaryAnchors { get; private init; }

    /// <summary>
    /// IDs of validation nodes whose AfterTaskIds / BeforeTaskIds boundaries align with
    /// a structurally valid stage boundary or dependency join in the new layout — the
    /// same test the Plan Visualizer uses to classify a validation as stage-level.
    /// </summary>
    internal IReadOnlyList<string>? StructurallyEligibleValidationIds { get; private init; }

    internal static DependencyEditRecomputeResult Cycle(IReadOnlyList<string> cycleIds) =>
        new()
        {
            Success  = false,
            Error    = "The edited plan contains a dependency cycle involving: " +
                       string.Join(", ", cycleIds),
            CycleIds = cycleIds,
        };

    internal static DependencyEditRecomputeResult Ok(
        DecomposedTaskGroup recomputedGroup,
        IReadOnlyDictionary<string, int> stageAssignments,
        IReadOnlyDictionary<string, string> displayLabels,
        IReadOnlyDictionary<string, string> approvalBoundaryAnchors,
        IReadOnlyList<string> structurallyEligibleValidationIds) =>
        new()
        {
            Success                          = true,
            RecomputedGroup                  = recomputedGroup,
            StageAssignments                 = stageAssignments,
            DisplayLabels                    = displayLabels,
            ApprovalBoundaryAnchors          = approvalBoundaryAnchors,
            StructurallyEligibleValidationIds = structurallyEligibleValidationIds,
        };
}

/// <summary>
/// Safe DAG recomputation service for dependency edits. Accepts a
/// <see cref="DecomposedTaskGroup"/> with modified <c>DependsOn</c> arrays and
/// recomputes topological order, stage assignments, display labels, connector routing
/// eligibility, approval boundary anchors, and validation eligibility using the
/// same production layout engine (<see cref="ValidationShieldPresenter.ComputeValidationAwareLevels"/>)
/// that the Plan Visualizer invokes during rendering.
///
/// <para>
/// Cycle detection runs first; any edit that would introduce a circular dependency is
/// rejected before any mutation is applied. A task with all dependencies removed is
/// guaranteed to land at stage index 0 (Stage 1 in the UI).
/// </para>
/// </summary>
internal static class DependencyEditRecomputer
{
    /// <summary>
    /// Recomputes the full layout for <paramref name="group"/> after a dependency edit.
    /// Returns a failure result (no mutations) if the edited graph contains a cycle.
    /// </summary>
    internal static DependencyEditRecomputeResult Recompute(DecomposedTaskGroup group)
    {
        if (!CodeHealthGroupRunner.HasNoDependencyCycle(group, out var cycleIds))
            return DependencyEditRecomputeResult.Cycle(cycleIds ?? []);

        var validations = group.Validations ?? [];

        // Use the same production layout engine the Plan Visualizer uses.
        var levels = ValidationShieldPresenter.ComputeValidationAwareLevels(
            group.Tasks,
            validations);

        var stageAssignments = (IReadOnlyDictionary<string, int>)levels;

        // Sort tasks in topological order: stage level ascending, then original list
        // position within the same level. This matches the column-then-row traversal
        // order the Plan Visualizer uses and produces the same sequential display labels
        // that PendingDecomposePlanAdapter.ToPlan would assign from scratch.
        var indexedTasks = group.Tasks
            .Select((task, originalIndex) => (task, originalIndex))
            .ToArray();

        var sortedTasks = indexedTasks
            .OrderBy(item => levels.TryGetValue(item.task.Id, out var level) ? level : 0)
            .ThenBy(item => item.originalIndex)
            .Select(item => item.task)
            .ToArray();

        // Display labels: 1-based sequential in topological order.
        var displayLabels = sortedTasks
            .Select((task, index) => (task.Id, Label: (index + 1).ToString()))
            .ToDictionary(item => item.Id, item => item.Label, StringComparer.Ordinal);

        var recomputedGroup = group with { Tasks = StripOrphanedInputs(sortedTasks) };

        // Approval boundary anchors: resolve each gate using the new stage assignments.
        var approvalBoundaryAnchors = ResolveApprovalBoundaryAnchors(
            group.ApprovalGates ?? [],
            sortedTasks,
            levels);

        // Validation eligibility: a validation node is structurally eligible when its
        // AfterTaskIds / BeforeTaskIds boundaries align with a valid stage boundary or
        // dependency join in the new layout, mirroring the Plan Visualizer's classification.
        var eligibleValidationIds = ComputeEligibleValidationIds(
            validations,
            sortedTasks,
            levels);

        return DependencyEditRecomputeResult.Ok(
            recomputedGroup,
            stageAssignments,
            displayLabels,
            approvalBoundaryAnchors,
            eligibleValidationIds);
    }

    // ── Approval boundary anchor resolution ─────────────────────────────────────

    private static IReadOnlyDictionary<string, string> ResolveApprovalBoundaryAnchors(
        IReadOnlyList<DecomposedGate> gates,
        IReadOnlyList<DecomposedSubTask> tasks,
        IReadOnlyDictionary<string, int> levels)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (gates.Count == 0)
            return result;

        var stageCount = levels.Count == 0 ? 0 : levels.Values.Max() + 1;
        foreach (var gate in gates)
        {
            var anchor = ResolveGateAnchor(gate, tasks, levels, stageCount);
            if (anchor is not null)
                result[gate.GateId] = anchor;
        }
        return result;
    }

    private static string? ResolveGateAnchor(
        DecomposedGate gate,
        IReadOnlyList<DecomposedSubTask> tasks,
        IReadOnlyDictionary<string, int> levels,
        int stageCount)
    {
        var afterIds  = gate.AfterTaskIds  ?? [];
        var beforeIds = gate.BeforeTaskIds ?? [];

        // Stage milestone check: matches when AfterTaskIds / BeforeTaskIds exactly cover
        // an immediate or cumulative stage boundary (same logic as PlanViewerWindow).
        for (var leftLevel = 0; leftLevel < stageCount - 1; leftLevel++)
        {
            var immediateAfter = tasks
                .Where(task => GetLevel(levels, task.Id) == leftLevel)
                .Select(task => task.Id).ToArray();
            var immediateBefore = tasks
                .Where(task => GetLevel(levels, task.Id) == leftLevel + 1)
                .Select(task => task.Id).ToArray();
            var cumulativeAfter = tasks
                .Where(task => GetLevel(levels, task.Id) <= leftLevel)
                .Select(task => task.Id).ToArray();
            var cumulativeBefore = tasks
                .Where(task => GetLevel(levels, task.Id) > leftLevel)
                .Select(task => task.Id).ToArray();

            if (SameBoundary(afterIds, beforeIds, immediateAfter, immediateBefore) ||
                SameBoundary(afterIds, beforeIds, cumulativeAfter, cumulativeBefore))
                return $"stage:{leftLevel + 1}";
        }

        // ALL join check: gate guards an explicit multi-dependency fanin.
        var allGroups = tasks
            .Where(task => task.DependsOn.Count > 1)
            .GroupBy(task => string.Join(
                "\u001f",
                task.DependsOn.OrderBy(id => id, StringComparer.Ordinal)));
        foreach (var group in allGroups)
        {
            var targets      = group.Select(task => task.Id).ToArray();
            var dependencies = group.First().DependsOn.ToArray();
            if (SameBoundary(afterIds, beforeIds, dependencies, targets))
                return "all:" + string.Join("|", targets.OrderBy(id => id, StringComparer.Ordinal));
        }

        if (afterIds.Count == 1)  return $"task-after:{afterIds[0]}";
        if (beforeIds.Count == 1) return $"task-before:{beforeIds[0]}";
        return null;
    }

    // ── Validation eligibility ───────────────────────────────────────────────────

    private static IReadOnlyList<string> ComputeEligibleValidationIds(
        IReadOnlyList<DecomposedValidationNode> validations,
        IReadOnlyList<DecomposedSubTask> tasks,
        IReadOnlyDictionary<string, int> levels)
    {
        if (validations.Count == 0)
            return [];

        var stageCount = levels.Count == 0 ? 0 : levels.Values.Max() + 1;
        var eligible = new List<string>();

        foreach (var validation in validations)
        {
            if (IsValidationStructurallyEligible(validation, tasks, levels, stageCount))
                eligible.Add(validation.ValidationId);
        }

        return eligible;
    }

    private static bool IsValidationStructurallyEligible(
        DecomposedValidationNode validation,
        IReadOnlyList<DecomposedSubTask> tasks,
        IReadOnlyDictionary<string, int> levels,
        int stageCount)
    {
        var afterIds  = validation.AfterTaskIds  ?? [];
        var beforeIds = validation.BeforeTaskIds ?? [];
        if (afterIds.Count == 0 || beforeIds.Count == 0) return false;

        // Stage boundary: matches an immediate or cumulative stage boundary.
        for (var leftLevel = 0; leftLevel < stageCount - 1; leftLevel++)
        {
            var immediateAfter = tasks
                .Where(task => GetLevel(levels, task.Id) == leftLevel)
                .Select(task => task.Id).ToArray();
            var immediateBefore = tasks
                .Where(task => GetLevel(levels, task.Id) == leftLevel + 1)
                .Select(task => task.Id).ToArray();
            var cumulativeAfter = tasks
                .Where(task => GetLevel(levels, task.Id) <= leftLevel)
                .Select(task => task.Id).ToArray();
            var cumulativeBefore = tasks
                .Where(task => GetLevel(levels, task.Id) > leftLevel)
                .Select(task => task.Id).ToArray();

            if (SameBoundary(afterIds, beforeIds, immediateAfter, immediateBefore) ||
                SameBoundary(afterIds, beforeIds, cumulativeAfter, cumulativeBefore))
                return true;
        }

        // ALL join boundary: AfterTaskIds are the sources of a multi-dependency fanin.
        var allGroups = tasks
            .Where(task => task.DependsOn.Count > 1)
            .GroupBy(task => string.Join(
                "\u001f",
                task.DependsOn.OrderBy(id => id, StringComparer.Ordinal)));
        foreach (var group in allGroups)
        {
            var targets      = group.Select(task => task.Id).ToArray();
            var dependencies = group.First().DependsOn.ToArray();
            if (SameBoundary(afterIds, beforeIds, dependencies, targets))
                return true;
        }

        // Single task boundary: validation guards entry or exit of one task.
        if (afterIds.Count == 1 || beforeIds.Count == 1)
            return true;

        return false;
    }

    // ── Shared utilities ─────────────────────────────────────────────────────────

    /// <summary>
    /// Removes <see cref="DecomposedSubTask.Inputs"/> entries that reference outputs no longer
    /// reachable through the task's new dependency graph. Called after every recompute so that
    /// removing a dependency edge also cleans up dangling input references on the consumer.
    /// </summary>
    private static IReadOnlyList<DecomposedSubTask> StripOrphanedInputs(
        IReadOnlyList<DecomposedSubTask> tasks)
    {
        if (tasks.All(t => t.Inputs is null or { Count: 0 }))
            return tasks;

        // Build output-ID → owning task-ID map.
        var outputOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var t in tasks)
            foreach (var output in t.Outputs ?? [])
                outputOwners[output.OutputId] = t.Id;

        if (outputOwners.Count == 0)
        {
            // No outputs exist — all input references are stale.
            return tasks.Select(t => t.Inputs is { Count: > 0 } ? t with { Inputs = null } : t).ToArray();
        }

        // Build direct-dependency adjacency for transitive ancestor traversal.
        var dependsOn = tasks.ToDictionary(
            t => t.Id,
            t => (IReadOnlySet<string>)t.DependsOn.ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

        return tasks.Select(t =>
        {
            if (t.Inputs is null or { Count: 0 }) return t;
            var ancestors = ComputeAncestors(t.Id, dependsOn);
            var reachableOutputIds = outputOwners
                .Where(pair => ancestors.Contains(pair.Value))
                .Select(pair => pair.Key)
                .ToHashSet(StringComparer.Ordinal);
            var cleaned = t.Inputs.Where(reachableOutputIds.Contains).ToArray();
            if (cleaned.Length == t.Inputs.Count) return t;
            return t with { Inputs = cleaned.Length == 0 ? null : cleaned };
        }).ToArray();
    }

    private static HashSet<string> ComputeAncestors(
        string taskId,
        IReadOnlyDictionary<string, IReadOnlySet<string>> dependsOn)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        if (dependsOn.TryGetValue(taskId, out var directDeps))
            foreach (var dep in directDeps)
                queue.Enqueue(dep);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;
            if (dependsOn.TryGetValue(current, out var deps))
                foreach (var dep in deps)
                    queue.Enqueue(dep);
        }
        return visited;
    }

    // ── Original shared utilities ─────────────────────────────────────────────

    private static int GetLevel(IReadOnlyDictionary<string, int> levels, string taskId) =>
        levels.TryGetValue(taskId, out var level) ? level : 0;

    private static bool SameBoundary(
        IReadOnlyList<string> afterIds,
        IReadOnlyList<string> beforeIds,
        IReadOnlyList<string> expectedAfter,
        IReadOnlyList<string> expectedBefore) =>
        afterIds.ToHashSet(StringComparer.Ordinal).SetEquals(expectedAfter) &&
        beforeIds.ToHashSet(StringComparer.Ordinal).SetEquals(expectedBefore);
}
