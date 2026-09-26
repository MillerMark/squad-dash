using System.Text.Json;

namespace SquadDash;

internal static class DecomposePlanRevision
{
    internal static bool TryValidateAgainstPersisted(
        DecomposedTaskGroup proposal,
        DecomposedTaskGroup existing,
        IReadOnlySet<string> replaceableTaskIds,
        IReadOnlySet<string> completedTaskIds,
        IReadOnlySet<string> editablePendingTaskIds,
        out string? error)
    {
        error = null;
        var proposedById = proposal.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        foreach (var existingTask in existing.Tasks)
        {
            if (!proposedById.TryGetValue(existingTask.Id, out var proposedTask))
            {
                error = $"Revised plan omitted existing task {existingTask.Id}.";
                return false;
            }

            if (SameTaskContract(existingTask, proposedTask))
                continue;

            if (completedTaskIds.Contains(existingTask.Id))
            {
                error = $"Revised plan changed completed task {existingTask.Id}.";
                return false;
            }

            if (editablePendingTaskIds.Contains(existingTask.Id))
                continue;

            error = $"Revised plan changed task {existingTask.Id} after work or review had begun; keep its " +
                    "historical contract unchanged and add replacement or follow-up tasks instead.";
            return false;
        }

        foreach (var parentId in proposal.Tasks
                     .Where(task => !string.IsNullOrWhiteSpace(task.ParentTaskId))
                     .Select(task => task.ParentTaskId!)
                     .Distinct(StringComparer.Ordinal))
        {
            if (completedTaskIds.Contains(parentId))
            {
                error = $"Completed task {parentId} cannot be superseded; add a corrective follow-up task instead.";
                return false;
            }
            if (!replaceableTaskIds.Contains(parentId))
            {
                error = $"Task {parentId} cannot be superseded because it is already historical.";
                return false;
            }
            var existingParent = existing.Tasks.First(task => string.Equals(task.Id, parentId, StringComparison.Ordinal));
            if (!editablePendingTaskIds.Contains(parentId) &&
                !SameTaskContract(existingParent, proposedById[parentId]))
            {
                error = $"Revised plan changed started task {parentId}; keep its historical contract unchanged " +
                        "and put the new contract on its replacement tasks.";
                return false;
            }
        }
        return true;

        static bool SameTaskContract(DecomposedSubTask left, DecomposedSubTask right) =>
            string.Equals(
                JsonSerializer.Serialize(left),
                JsonSerializer.Serialize(right),
                StringComparison.Ordinal);
    }

    internal static bool TryNormalize(
        DecomposedTaskGroup proposal,
        out DecomposedTaskGroup normalized,
        out string? error)
    {
        normalized = proposal;
        error = null;
        var replacementGroups = proposal.Tasks
            .Where(task => !string.IsNullOrWhiteSpace(task.ParentTaskId))
            .GroupBy(task => task.ParentTaskId!, StringComparer.Ordinal)
            .ToArray();
        if (replacementGroups.Length == 0) return true;

        var ids = proposal.Tasks.Select(task => task.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var replacements in replacementGroups)
        {
            if (!ids.Contains(replacements.Key))
            {
                error = $"Replacement parent {replacements.Key} is not present in the revised plan.";
                return false;
            }
        }

        var rewritten = proposal.Tasks.ToDictionary(task => task.Id, task => task, StringComparer.Ordinal);
        foreach (var replacements in replacementGroups)
        {
            var replacementIds = replacements.Select(task => task.Id).ToHashSet(StringComparer.Ordinal);
            var dependedUpon = replacements
                .SelectMany(task => task.DependsOn ?? [])
                .Where(replacementIds.Contains)
                .ToHashSet(StringComparer.Ordinal);
            var terminalIds = replacementIds.Where(id => !dependedUpon.Contains(id)).ToArray();
            if (terminalIds.Length == 0)
            {
                error = $"Replacement tasks for {replacements.Key} do not have a terminal step.";
                return false;
            }

            foreach (var task in rewritten.Values.ToArray())
            {
                if (replacementIds.Contains(task.Id) ||
                    string.Equals(task.Id, replacements.Key, StringComparison.Ordinal) ||
                    task.DependsOn is null || !task.DependsOn.Contains(replacements.Key, StringComparer.Ordinal))
                    continue;
                rewritten[task.Id] = task with
                {
                    DependsOn = task.DependsOn
                        .Where(dependency => !string.Equals(dependency, replacements.Key, StringComparison.Ordinal))
                        .Concat(terminalIds)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                };
            }
        }

        normalized = proposal with
        {
            Tasks = proposal.Tasks.Select(task => rewritten[task.Id]).ToArray(),
        };
        return true;
    }
}
