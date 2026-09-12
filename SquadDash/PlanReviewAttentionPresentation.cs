namespace SquadDash;

internal sealed record PlanReviewAttentionPresentation(
    PlanTask Task,
    PlanApprovalGate Gate,
    IReadOnlyList<PlanHumanReviewChecklistItem> Checklist,
    string Title,
    string Summary);

internal enum PlanCheckpointPrimaryActionKind
{
    ApproveCheckpoint,
    CompleteHumanReviewTasks,
}

internal sealed record PlanCheckpointPrimaryActionPresentation(
    PlanCheckpointPrimaryActionKind Kind,
    string Label,
    string Hint,
    IReadOnlyList<PlanReviewAttentionPresentation> ReviewAttentions)
{
    internal PlanReviewAttentionPresentation? ReviewAttention =>
        ReviewAttentions.Count == 1 ? ReviewAttentions[0] : null;
}

/// <summary>
/// Defines the single primary action represented by an approval boundary. UI surfaces must use
/// this presentation instead of inferring task completion from a gate's AfterTaskIds. A gate can
/// review an already-complete task without owning a task-completion transition.
/// </summary>
internal static class PlanCheckpointPrimaryActionPresentationResolver
{
    internal static PlanCheckpointPrimaryActionPresentation Resolve(
        Plan plan,
        IReadOnlyList<string> representedGateIds)
    {
        var representedGates = plan.ApprovalGates
            .Where(gate => representedGateIds.Contains(gate.GateId, StringComparer.Ordinal) &&
                           gate.Status == PlanGateStatus.AwaitingApproval)
            .ToArray();
        var reviewAttentions = representedGates.Select(gate =>
        {
            var taskId = gate.AfterTaskIds.Count == 1 ? gate.AfterTaskIds[0] : null;
            var reviewAttention = PlanReviewAttentionPresentationResolver.Resolve(plan, taskId);
            return reviewAttention is not null &&
                   string.Equals(reviewAttention.Gate.GateId, gate.GateId, StringComparison.Ordinal)
                ? reviewAttention
                : null;
        }).OfType<PlanReviewAttentionPresentation>().ToArray();
        if (reviewAttentions.Length > 0)
        {
            var singleReview = reviewAttentions.Length == 1 ? reviewAttentions[0] : null;
            var stepLabel = singleReview is null
                ? "Reviewed Steps"
                : PlanRecoveryPresentationBuilder.FormatStepLabel(
                    singleReview.Task.DisplayStepLabel,
                    singleReview.Task.Title ?? singleReview.Task.TaskId);
            var label = representedGates.Length == 1
                ? $"{stepLabel} Is Complete"
                : singleReview is null
                    ? "Complete Reviewed Steps & Approve Checkpoints"
                    : $"Complete {stepLabel} & Approve Checkpoints";
            if (!plan.IsPaused) label += " & Continue";
            return new PlanCheckpointPrimaryActionPresentation(
                PlanCheckpointPrimaryActionKind.CompleteHumanReviewTasks,
                label,
                plan.IsPaused
                    ? "Accept every represented human-reviewed step, approve all represented checkpoints, and keep the plan paused in one durable transition."
                    : "Accept every represented human-reviewed step, approve all represented checkpoints, and continue in one durable transition.",
                reviewAttentions);
        }

        var scope = representedGates.Length switch
        {
            2 => "Both Checkpoints",
            > 2 => "All Checkpoints",
            _ => "Checkpoint",
        };
        return new PlanCheckpointPrimaryActionPresentation(
            PlanCheckpointPrimaryActionKind.ApproveCheckpoint,
            plan.IsPaused ? $"Approve {scope}" : $"Approve {scope} & Continue",
            plan.IsPaused
                ? "Approve the represented checkpoint without changing task completion and keep the plan paused."
                : "Approve the represented checkpoint without changing task completion, then continue the plan.",
            []);
    }
}

internal static class PlanHumanReviewStepAcceptance
{
    internal static Plan ApplyTasks(
        Plan plan,
        IReadOnlyList<PlanReviewAttentionPresentation> reviewAttentions,
        string? currentHead = null)
    {
        var updated = plan;
        var recordedCommit = plan.InterruptionData?.TaskCommitEvidence?.Commit;
        foreach (var reviewAttention in reviewAttentions)
        {
            var handoff = reviewAttention.Task.Handoff ??
                throw new InvalidOperationException("The reviewed task has no durable implementation handoff.");
            var isVerifiedNoOp = string.Equals(
                handoff.CompletionKind,
                DecomposeStepCompletionKind.VerifiedNoOp,
                StringComparison.Ordinal);
            if (!isVerifiedNoOp && string.IsNullOrWhiteSpace(handoff.Commit))
                throw new InvalidOperationException("The reviewed task handoff has no commit.");
            if (!isVerifiedNoOp &&
                !string.IsNullOrWhiteSpace(currentHead) &&
                (string.IsNullOrWhiteSpace(recordedCommit) ||
                 !string.Equals(handoff.Commit, recordedCommit, StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(handoff.Commit, currentHead, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "Reviewed evidence is stale: task tip has advanced past the reviewed commit. Refresh the review card.");
            }
            var commits = isVerifiedNoOp
                ? Array.Empty<PlanEvidenceCommit>()
                : reviewAttention.Task.Commits ??
                [new PlanEvidenceCommit(
                    handoff.Commit!,
                    PlanRecoveryCommitRelation.Task,
                    "Host-validated implementation handoff reviewed at the human checkpoint.")];
            var result = new DecomposeStepResult(
                updated.PlanId,
                reviewAttention.Task.TaskId,
                updated.Revision,
                "complete",
                isVerifiedNoOp ? null : handoff.Commit,
                handoff.Summary,
                [],
                handoff.Verification,
                CompletedWork: handoff.CompletedWork,
                CompletionKind: isVerifiedNoOp
                    ? DecomposeStepCompletionKind.VerifiedNoOp
                    : DecomposeStepCompletionKind.Changed,
                EvaluatedCommit: isVerifiedNoOp ? handoff.EvaluatedCommit : null,
                NoOpEvidence: isVerifiedNoOp ? handoff.NoOpEvidence : null)
            {
                Commits = commits,
            };
            updated = PlanStoreUpdater.ApplyStepAccepted(updated, null, result);
        }
        return updated with
        {
            InterruptionData = null,
            Progress = updated.Progress with { ExecutingTaskId = null },
        };
    }
}

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
