namespace SquadDash;

internal enum PlanTranscriptPhase
{
    Executing,
    VerifyingWork,
    ReworkingTask,
    ValidatingPlan,
}

internal static class PlanLoopTranscriptPresentation
{
    internal static string BuildPhaseKey(
        string planId,
        PlanTranscriptPhase phase,
        string? taskOrValidationId) =>
        $"{planId}|{phase}|{taskOrValidationId}";

    internal static bool ShouldEmitPhaseHeading(string? previousKey, string currentKey) =>
        !string.Equals(previousKey, currentKey, StringComparison.Ordinal);

    internal static bool ShouldShowLoopBookkeeping(bool isPlanLoop) => !isPlanLoop;

    internal static string BuildPlanCompleteMessage(Plan plan, string? detail = null)
    {
        var message = $"Plan complete · {plan.Progress.CompletedCount} of {plan.Progress.TotalCount}";
        return string.IsNullOrWhiteSpace(detail) ? message : $"{message} · {detail.Trim()}";
    }

    internal static string BuildPhasePrompt(
        Plan plan,
        string? taskId,
        string? taskTitle,
        string loopDetailsPath,
        PlanTranscriptPhase phase = PlanTranscriptPhase.Executing)
    {
        var task = ResolveTask(plan, taskId, taskTitle);
        var title = task?.Title?.Trim() is { Length: > 0 } resolvedTitle
            ? resolvedTitle
            : string.IsNullOrWhiteSpace(taskTitle)
                ? taskId ?? plan.Progress.ExecutingTaskId ?? "Current step"
                : taskTitle.Trim();
        var planTarget = Uri.EscapeDataString(plan.PlanId);
        var phaseLabel = phase switch
        {
            PlanTranscriptPhase.VerifyingWork => "Verifying work",
            PlanTranscriptPhase.ReworkingTask => "Reworking task",
            PlanTranscriptPhase.ValidatingPlan => "Validating plan",
            _ => "Executing plan",
        };

        var identity = phase == PlanTranscriptPhase.ValidatingPlan
            ? string.Empty
            : $" · {BuildProgressIdentity(plan, task)}";

        return $"{phaseLabel}{identity} · {title}  " +
               $"[View Plan](app://open-plan:{planTarget}) · " +
               $"[Loop details](app://open-loop-md:{loopDetailsPath})";
    }

    internal static string BuildVerifyingCompletedWorkMessage(Plan plan, string? taskId, string? taskTitle)
    {
        var task = ResolveTask(plan, taskId, taskTitle);
        var displayLabel = task?.DisplayStepLabel?.Trim();
        var subject = string.IsNullOrWhiteSpace(displayLabel)
            ? "work for the current step"
            : $"Step {displayLabel}";
        return $"Reviewing the completed {subject}. No code changes will occur during this review.";
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
