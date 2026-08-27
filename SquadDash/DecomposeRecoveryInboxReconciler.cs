namespace SquadDash;

/// <summary>
/// Reconciles a recovery Inbox request with the canonical durable plan. Recovery
/// actions are valid only while the referenced revision is still interrupted at
/// the same task. Everything else is retained as read-only archived history.
/// </summary>
internal static class DecomposeRecoveryInboxReconciler
{
    private const string RecoveryPrefix = "decompose-recovery-";
    private const string ResolutionMarker = "**Recovery request resolved.**";

    internal sealed record Result(InboxMessage Message, bool IsActionable, bool ShouldArchive);

    internal static bool IsRecoveryMessage(InboxMessage message) =>
        !string.IsNullOrWhiteSpace(message.Id) &&
        (message.Id.StartsWith(RecoveryPrefix, StringComparison.Ordinal) ||
         message.Actions?.Any(action => action is not null && string.Equals(
             action.RouteMode,
             DecomposePlanInbox.RecoveryRouteMode,
             StringComparison.OrdinalIgnoreCase)) == true);

    internal static string? GetPlanId(InboxMessage message) => message.ResolvePlanGroupId();

    internal static Result Reconcile(InboxMessage message, Plan? plan)
    {
        if (!IsRecoveryMessage(message))
            return new Result(message, IsActionable: true, ShouldArchive: false);

        var attachment = message.ResolvePlanAttachment();
        var planId = attachment?.PlanGroupId;
        var revision = attachment?.PlanRevision;
        var activeLifecycle = plan?.LifecycleStatus is PlanLifecycleStatus.Blocked or PlanLifecycleStatus.Interrupted;
        var currentTaskId = plan?.InterruptionData?.InterruptedTaskId;
        var expectedMessageId = plan is not null && !string.IsNullOrWhiteSpace(currentTaskId)
            ? $"{RecoveryPrefix}{plan.PlanId}-{currentTaskId}-{plan.Revision}"
            : null;
        var referencesCurrentPlan = plan is not null &&
            string.Equals(plan.PlanId, planId, StringComparison.Ordinal) &&
            string.Equals(plan.Revision, revision, StringComparison.Ordinal);
        // An interrupted lifecycle without an interrupted task is a valid transient state while
        // recovery acceptance advances to its next boundary. It cannot own an actionable task
        // recovery message. In particular, a missing task id must not behave like a wildcard.
        var referencesCurrentTask = expectedMessageId is not null &&
            string.Equals(message.Id, expectedMessageId, StringComparison.Ordinal);

        if (referencesCurrentPlan && activeLifecycle && referencesCurrentTask)
        {
            var active = DecomposePlanInbox.BuildRecoveryMessage(
                PendingDecomposePlanAdapter.FromPlan(plan!),
                currentTaskId!,
                plan!.InterruptionData?.Reason ?? "Plan execution stopped unexpectedly.",
                message.Timestamp,
                plan.InterruptionData?.TaskCommitEvidence,
                plan) with
            {
                Read = message.Read,
                Priority = "critical",
            };
            return new Result(active, IsActionable: true, ShouldArchive: false);
        }

        var explanation = GetResolutionExplanation(plan, planId, revision);
        var originalBody = message.Body ?? string.Empty;
        var body = originalBody.Contains(ResolutionMarker, StringComparison.Ordinal)
            ? originalBody
            : $"{originalBody}\n\n---\n\n{ResolutionMarker} {explanation}".TrimStart();
        var resolved = message with
        {
            Read = true,
            Body = body,
            Actions = [],
        };
        return new Result(resolved, IsActionable: false, ShouldArchive: true);
    }

    private static string GetResolutionExplanation(Plan? plan, string? planId, string? revision)
    {
        if (plan is null)
            return "The referenced plan is no longer available, so these recovery actions were disabled.";
        if (!string.Equals(plan.PlanId, planId, StringComparison.Ordinal) ||
            !string.Equals(plan.Revision, revision, StringComparison.Ordinal))
            return "The plan definition changed, so these recovery actions were disabled.";

        return plan.LifecycleStatus switch
        {
            PlanLifecycleStatus.Completed => "The plan completed successfully; no recovery action remains.",
            PlanLifecycleStatus.Executing => "The plan continued beyond this interruption; these earlier recovery actions are no longer valid.",
            PlanLifecycleStatus.AwaitingApproval => "The plan continued to an approval checkpoint; these earlier recovery actions are no longer valid.",
            PlanLifecycleStatus.Stopped => "The plan was ended, so these recovery actions are no longer valid.",
            PlanLifecycleStatus.Archived => "The plan was archived, so these recovery actions are no longer valid.",
            _ => "The plan moved beyond this interruption, so these recovery actions are no longer valid.",
        };
    }
}
