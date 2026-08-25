namespace SquadDash;

internal sealed record PlanReviewAttentionPresentation(
    PlanTask Task,
    PlanApprovalGate Gate,
    IReadOnlyList<PlanHumanReviewChecklistItem> Checklist,
    string Title,
    string Summary);

internal static class PlanReviewAttentionPresentationResolver
{
    internal static PlanReviewAttentionPresentation? Resolve(Plan plan, string? taskId = null)
    {
        taskId ??= plan.InterruptionData?.InterruptedTaskId ?? plan.Progress.ExecutingTaskId;
        if (string.IsNullOrWhiteSpace(taskId))
            taskId = plan.Tasks.FirstOrDefault(task =>
                task.Status == PlanTaskStatus.HumanReviewRequired)?.TaskId;
        if (string.IsNullOrWhiteSpace(taskId)) return null;

        var task = plan.Tasks.FirstOrDefault(candidate =>
            string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal));
        if (task is null || task.Status != PlanTaskStatus.HumanReviewRequired) return null;
        var gate = PlanHumanReviewGatePolicy.FindCombinableGate(plan, task.TaskId);
        if (gate is null) return null;

        var stepLabel = PlanRecoveryPresentationBuilder.FormatStepLabel(
            task.DisplayStepLabel,
            task.Title ?? task.TaskId);
        return new PlanReviewAttentionPresentation(
            task,
            gate,
            PlanHumanReviewChecklist.Build(plan, [gate]),
            "Review Required",
            $"Implementation and automated verification for {stepLabel} are complete. Human observation remains.");
    }

    internal static PlanTaskProofRequirement? FindRequirement(
        PlanReviewAttentionPresentation presentation,
        PlanHumanReviewChecklistItem item) =>
        presentation.Gate.ProofRequirements?.FirstOrDefault(requirement =>
            string.Equals(requirement.RequirementId, item.RequirementId, StringComparison.Ordinal));
}
