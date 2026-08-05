using System.Text;

namespace SquadDash;

/// <summary>Builds the bounded, task-specific context injected for every plan task.</summary>
internal static class PlanExecutionContextBuilder
{
    internal static string Build(Plan plan, PlanTask currentTask)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Plan intent and connected execution context");
        builder.AppendLine();
        builder.AppendLine($"Plan: **{plan.Title}** (`{plan.PlanId}`)");
        builder.AppendLine($"Guiding intent: {plan.Summary}");
        var ancestors = GetAncestors(plan, currentTask);
        if (ancestors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("### Accepted upstream handoffs");
            builder.AppendLine("The following first-person record is the connected work already accepted on the direct path to this task:");
            foreach (var ancestor in ancestors)
            {
                builder.AppendLine();
                builder.AppendLine($"- **{ancestor.Title ?? ancestor.TaskId}** (`{ancestor.TaskId}`)");
                if (ancestor.Handoff is { } handoff)
                {
                    builder.AppendLine($"  - Commit: `{handoff.Commit}`");
                    builder.AppendLine($"  - I previously completed this by: {handoff.Summary}");
                    if (handoff.ChangedFiles.Count > 0)
                        builder.AppendLine($"  - Changed files: {string.Join(", ", handoff.ChangedFiles.Select(path => $"`{path}`"))}");
                    if (!string.IsNullOrWhiteSpace(handoff.Verification?.Summary))
                        builder.AppendLine($"  - Verification: {handoff.Verification.Summary}");
                }
                else if (!string.IsNullOrWhiteSpace(ancestor.CompletionSummary))
                    builder.AppendLine($"  - Earlier accepted summary: {ancestor.CompletionSummary}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("### Current task contract");
        builder.AppendLine($"Current task: **{currentTask.Title ?? currentTask.TaskId}** (`{currentTask.TaskId}`)");
        builder.AppendLine(currentTask.Description);
        builder.AppendLine();
        builder.AppendLine("Implement this task as part of that larger intent. Preserve accepted upstream contracts, " +
                           "integrate with their actual production surfaces, and do not claim work that the repository evidence does not support.");
        return builder.ToString().TrimEnd();
    }

    internal static IReadOnlyList<PlanTask> GetAncestors(Plan plan, PlanTask task)
    {
        var byId = plan.Tasks.ToDictionary(candidate => candidate.TaskId, StringComparer.Ordinal);
        var distance = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<(string Id, int Distance)>();
        foreach (var dependency in task.DependsOn)
            queue.Enqueue((dependency, 1));

        while (queue.Count > 0)
        {
            var (id, currentDistance) = queue.Dequeue();
            if (!byId.TryGetValue(id, out var ancestor)) continue;
            if (distance.TryGetValue(id, out var prior) && prior <= currentDistance) continue;
            distance[id] = currentDistance;
            foreach (var dependency in ancestor.DependsOn)
                queue.Enqueue((dependency, currentDistance + 1));
        }

        var ordinalById = plan.Tasks
            .Select((candidate, index) => (candidate.TaskId, index))
            .ToDictionary(item => item.TaskId, item => item.index, StringComparer.Ordinal);
        return plan.Tasks
            .Where(candidate => distance.ContainsKey(candidate.TaskId))
            .OrderBy(candidate => ordinalById[candidate.TaskId])
            .ToArray();
    }
}
