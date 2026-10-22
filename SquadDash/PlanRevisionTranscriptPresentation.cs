namespace SquadDash;

internal sealed record PlanRevisionApprovalPresentation(
    bool OwnsInterruptedRecovery,
    string Title,
    string? Context,
    string? RecordedReason,
    string? Recommendation,
    string Status);

internal static class PlanRevisionApprovalPresentationBuilder
{
    internal static PlanRevisionApprovalPresentation Build(
        Plan current,
        PendingPlanRevisionProposal pending)
    {
        var ownsInterruptedRecovery =
            string.Equals(current.PlanId, pending.Payload.PlanId, StringComparison.Ordinal) &&
            string.Equals(current.Revision, pending.Payload.BaseRevision, StringComparison.Ordinal) &&
            PlanRecoveryControlPolicy.HasActionableInterruption(current);
        if (!ownsInterruptedRecovery)
        {
            return new PlanRevisionApprovalPresentation(
                false,
                "Plan Revision - Approval Required",
                null,
                null,
                null,
                "The current plan remains authoritative until you approve this revision.");
        }

        var interruptedTask = current.Tasks.FirstOrDefault(task => string.Equals(
            task.TaskId,
            current.InterruptionData?.InterruptedTaskId,
            StringComparison.Ordinal));
        var stepLabel = PlanRecoveryPresentationBuilder.FormatStepLabel(
            interruptedTask?.DisplayStepLabel,
            interruptedTask?.Title ?? "the interrupted step");
        var interruptedTitle = interruptedTask?.Title;
        var stepReference = string.IsNullOrWhiteSpace(interruptedTitle) ||
                            stepLabel.Contains(interruptedTitle, StringComparison.OrdinalIgnoreCase)
            ? stepLabel
            : $"{stepLabel} ({interruptedTitle})";
        var recordedReason = current.InterruptionData?.AttentionSummary ??
                             current.InterruptionData?.Reason ??
                             "Automatic work stopped before the step could be completed.";
        recordedReason = PlanRecoveryPresentationBuilder.HumanizePlanText(current, recordedReason);
        var savedDate = pending.CreatedAt.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);

        return new PlanRevisionApprovalPresentation(
            true,
            "Plan stopped - saved revision ready",
            $"Automatic work stopped at {stepReference}. Existing work and the current plan are preserved.",
            recordedReason,
            $"This proposal was saved on {savedDate} for the current plan revision. Recommended: review the " +
            "proposed plan, then approve it to apply these changes and continue from the interruption. If the " +
            "proposal no longer matches your intent, revise it or keep the current plan instead.",
            "The saved proposal has not changed the plan. Approving it will apply the revision and continue from the preserved interruption.");
    }
}

internal static class PlanRevisionTranscriptPresentation
{
    internal static string BuildNoChanges(int preservedLockedTaskCount) =>
        preservedLockedTaskCount > 0
            ? $"No changes found in the plan revision. Preserved {preservedLockedTaskCount} completed or active tasks."
            : "No changes found in the plan revision.";

    internal static string BuildApplied(int revisionNumber, int appliedChangeCount, int preservedLockedTaskCount)
    {
        var summary = $"✅ Plan updated to revision {revisionNumber}. " +
                      $"Applied {appliedChangeCount} downstream changes.";
        return preservedLockedTaskCount > 0
            ? summary + $" Preserved {preservedLockedTaskCount} completed or active tasks."
            : summary;
    }
}
