namespace SquadDash;

internal static class PlanLoopTranscriptPresentation
{
    internal static string BuildExecutingPrompt(
        Plan plan,
        string? taskTitle,
        string loopDetailsPath)
    {
        var stepNumber = Math.Min(
            plan.Progress.TotalCount,
            Math.Max(1, plan.Progress.CompletedCount + 1));
        var title = string.IsNullOrWhiteSpace(taskTitle)
            ? plan.Progress.ExecutingTaskId ?? "Current step"
            : taskTitle.Trim();
        var planTarget = Uri.EscapeDataString(plan.PlanId);

        return $"Executing plan · Step {stepNumber} of {plan.Progress.TotalCount} · {title}  " +
               $"[View Plan](app://open-plan:{planTarget}) · " +
               $"[Loop details](app://open-loop-md:{loopDetailsPath})";
    }
}
