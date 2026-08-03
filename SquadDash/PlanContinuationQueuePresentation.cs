namespace SquadDash;

internal sealed record PlanContinuationQueueDisplay(
    int StepNumber,
    string Label,
    string Description);

internal static class PlanContinuationQueuePresentation
{
    internal static PlanContinuationQueueDisplay? Build(Plan plan)
    {
        var nextStepNumber = plan.Progress.CompletedCount + 2;
        if (nextStepNumber > plan.Progress.TotalCount)
            return null;

        var nextTask = plan.Tasks.ElementAtOrDefault(nextStepNumber - 1);
        var nextTaskName = nextTask?.Title ?? nextTask?.TaskId ?? $"Step {nextStepNumber}";
        var dependencyNames = (nextTask?.DependsOn ?? [])
            .Select(dependencyId => plan.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, dependencyId, StringComparison.Ordinal))?.Title ?? dependencyId)
            .ToArray();
        var dependencyReason = dependencyNames.Length == 0
            ? "It is the next dependency-ready task."
            : "It becomes eligible after: " + string.Join(", ", dependencyNames) + ".";

        return new PlanContinuationQueueDisplay(
            nextStepNumber,
            $"Plan Step {nextStepNumber}",
            $"This is a locked continuation of the currently executing plan.\n\n" +
            $"Plan: {plan.Title}\n" +
            $"Next task: {nextTaskName}\n" +
            $"Why it is next: {dependencyReason}\n\n" +
            $"Release: after the current step is accepted and any approval boundary is resolved, " +
            $"SquadDash will continue with Plan Step {nextStepNumber} of {plan.Progress.TotalCount}. " +
            "This item is managed by SquadDash and cannot be edited or sent manually. You may move it " +
            "in the queue to schedule user prompts before or after that step.");
    }
}
