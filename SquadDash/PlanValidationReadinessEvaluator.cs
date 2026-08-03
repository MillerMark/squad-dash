namespace SquadDash;

internal sealed record PlanValidationReadiness(
    string ValidationId,
    bool IsReady,
    IReadOnlySet<string> DownstreamFrontier);

/// <summary>
/// Pure scheduler semantics for first-class validation nodes. This deliberately does not execute
/// validation work; it supplies deterministic readiness and blocking decisions to the runtime.
/// </summary>
internal static class PlanValidationReadinessEvaluator
{
    internal static IReadOnlyList<PlanValidationReadiness> Evaluate(Plan plan)
    {
        var terminalTasks = plan.Tasks
            .Where(task => task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded)
            .Select(task => task.TaskId)
            .ToHashSet(StringComparer.Ordinal);

        return (plan.Validations ?? []).Select(validation => new PlanValidationReadiness(
            validation.ValidationId,
            (validation.Status is PlanValidationStatus.Pending or PlanValidationStatus.Ready or PlanValidationStatus.Stale) &&
                validation.AfterTaskIds.All(terminalTasks.Contains),
            ComputeDownstreamFrontier(plan, validation))).ToArray();
    }

    internal static PlanValidationNode? SelectNextReady(Plan plan)
    {
        var readyIds = Evaluate(plan)
            .Where(state => state.IsReady)
            .Select(state => state.ValidationId)
            .ToHashSet(StringComparer.Ordinal);
        return (plan.Validations ?? []).FirstOrDefault(validation =>
            readyIds.Contains(validation.ValidationId));
    }

    internal static IReadOnlySet<string> ComputeAllBlockedTaskIds(Plan plan)
    {
        var blocked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in Evaluate(plan))
        {
            var validation = (plan.Validations ?? []).First(candidate =>
                string.Equals(candidate.ValidationId, state.ValidationId, StringComparison.Ordinal));
            if (validation.Status != PlanValidationStatus.Passed)
                blocked.UnionWith(state.DownstreamFrontier);
        }
        return blocked;
    }

    internal static IReadOnlySet<string> ComputeDownstreamFrontier(
        Plan plan,
        PlanValidationNode validation)
    {
        var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var task in plan.Tasks)
        {
            foreach (var dependency in task.DependsOn)
            {
                if (!dependents.TryGetValue(dependency, out var values))
                    dependents[dependency] = values = [];
                values.Add(task.TaskId);
            }
        }

        var frontier = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(validation.BeforeTaskIds);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!frontier.Add(current)) continue;
            if (!dependents.TryGetValue(current, out var children)) continue;
            foreach (var child in children)
                pending.Enqueue(child);
        }
        return frontier;
    }

    internal static bool AllRequiredPassed(Plan plan) =>
        (plan.Validations ?? []).All(validation => validation.Status == PlanValidationStatus.Passed);
}
