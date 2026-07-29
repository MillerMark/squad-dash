using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Validates that the mutable tasks.md projection still describes the exact durable plan before
/// execution progress is accepted. This prevents missing, malformed, or stale task data from
/// being converted into authoritative plan state.
/// </summary>
internal static class PlanTaskProjectionValidator
{
    internal static bool TryGetValidatedItems(
        Plan plan,
        TaskParseResult? parsed,
        string groupId,
        bool requireAllComplete,
        out IReadOnlyList<TaskItem> items,
        out string? error)
    {
        items = [];
        error = null;

        if (parsed is null)
        {
            error = "tasks.md is missing or could not be read.";
            return false;
        }
        if (parsed.Errors.Count > 0)
        {
            error = "tasks.md contains invalid plan metadata: " + string.Join("; ", parsed.Errors);
            return false;
        }
        if (!string.Equals(plan.PlanId, groupId, StringComparison.Ordinal))
        {
            error = $"Durable plan '{plan.PlanId}' does not match requested plan '{groupId}'.";
            return false;
        }
        if (!parsed.DecomposeGroups.TryGetValue(groupId, out var group))
        {
            error = $"Plan {groupId} is missing from tasks.md.";
            return false;
        }

        var projectionRevision = group.HostRevision ?? PendingDecomposePlanStore.ComputeRevision(group);
        if (!string.Equals(plan.Revision, projectionRevision, StringComparison.Ordinal))
        {
            error = $"Plan {groupId} revision changed from {plan.Revision} to {projectionRevision}.";
            return false;
        }

        var expectedIds = plan.Tasks.Select(task => task.TaskId).ToArray();
        var definitionIds = group.Tasks.Select(task => task.Id).ToArray();
        if (!HasExactUniqueIds(expectedIds, definitionIds))
        {
            error = $"Plan {groupId} task definitions do not match its durable task graph.";
            return false;
        }

        var projected = parsed.OpenGroups
            .SelectMany(priorityGroup => priorityGroup.Items)
            .Concat(parsed.CompletedItems)
            .Where(item => string.Equals(item.DecomposeGroupId, groupId, StringComparison.Ordinal))
            .ToArray();
        if (projected.Any(item => string.IsNullOrWhiteSpace(item.TaskId)) ||
            !HasExactUniqueIds(expectedIds, projected.Select(item => item.TaskId!).ToArray()))
        {
            error = $"Plan {groupId} task statuses do not match its durable task graph.";
            return false;
        }
        if (requireAllComplete && projected.Any(item => !item.IsChecked && !item.IsSuperseded))
        {
            error = $"Plan {groupId} cannot complete because one or more tasks remain unfinished.";
            return false;
        }

        items = projected;
        return true;
    }

    private static bool HasExactUniqueIds(
        IReadOnlyCollection<string> expected,
        IReadOnlyCollection<string> actual)
    {
        if (expected.Count != actual.Count)
            return false;
        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        var actualSet = actual.ToHashSet(StringComparer.Ordinal);
        return expectedSet.Count == expected.Count &&
               actualSet.Count == actual.Count &&
               expectedSet.SetEquals(actualSet);
    }
}
