namespace SquadDash;

internal static class PlanLoopTranscriptPresentation
{
    internal static string BuildExecutingPrompt(
        Plan plan,
        string? taskId,
        string? taskTitle,
        string loopDetailsPath)
    {
        var task = ResolveTask(plan, taskId, taskTitle);
        var title = task?.Title?.Trim() is { Length: > 0 } resolvedTitle
            ? resolvedTitle
            : string.IsNullOrWhiteSpace(taskTitle)
                ? taskId ?? plan.Progress.ExecutingTaskId ?? "Current step"
                : taskTitle.Trim();
        var planTarget = Uri.EscapeDataString(plan.PlanId);

        return $"Executing plan · {BuildProgressIdentity(plan, task)} · {title}  " +
               $"[View Plan](app://open-plan:{planTarget}) · " +
               $"[Loop details](app://open-loop-md:{loopDetailsPath})";
    }

    internal static string BuildValidatingMessage(Plan plan, string? taskId, string? taskTitle)
    {
        var task = ResolveTask(plan, taskId, taskTitle);
        var identity = BuildProgressIdentity(plan, task);
        return $"Validating completed work for {identity}. The implementation will not be run again.";
    }

    internal static string BuildTaskIdentity(Plan plan, string? taskId, string? taskTitle)
    {
        var task = ResolveTask(plan, taskId, taskTitle);
        return BuildProgressIdentity(plan, task);
    }

    private static PlanTask? ResolveTask(Plan plan, string? taskId, string? taskTitle)
    {
        var resolvedId = string.IsNullOrWhiteSpace(taskId)
            ? plan.Progress.ExecutingTaskId
            : taskId;
        var byId = plan.Tasks.FirstOrDefault(candidate =>
            string.Equals(candidate.TaskId, resolvedId, StringComparison.Ordinal));
        if (byId is not null) return byId;
        if (string.IsNullOrWhiteSpace(taskTitle)) return null;
        return plan.Tasks.FirstOrDefault(candidate =>
            string.Equals(candidate.Title, taskTitle.Trim(), StringComparison.Ordinal));
    }

    private static string BuildProgressIdentity(Plan plan, PlanTask? task)
    {
        var expectedOrdinal = Math.Min(
            plan.Progress.TotalCount,
            Math.Max(1, plan.Progress.CompletedCount + 1));
        var displayLabel = task?.DisplayStepLabel?.Trim();
        if (string.IsNullOrWhiteSpace(displayLabel))
            return $"{plan.Progress.CompletedCount} of {plan.Progress.TotalCount} complete";
        if (string.Equals(displayLabel, expectedOrdinal.ToString(), StringComparison.Ordinal))
            return $"Step {displayLabel} of {plan.Progress.TotalCount}";
        return $"{plan.Progress.CompletedCount} of {plan.Progress.TotalCount} complete (Step \"{displayLabel}\")";
    }
}
