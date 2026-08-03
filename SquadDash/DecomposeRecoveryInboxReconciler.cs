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
        message.Id.StartsWith(RecoveryPrefix, StringComparison.Ordinal) ||
        message.Actions.Any(action => string.Equals(
            action.RouteMode,
            DecomposePlanInbox.RecoveryRouteMode,
            StringComparison.OrdinalIgnoreCase));

    internal static string? GetPlanId(InboxMessage message) =>
        message.Attachments.FirstOrDefault(attachment =>
            string.Equals(attachment.Type, DecomposePlanInbox.AttachmentType, StringComparison.OrdinalIgnoreCase))
            ?.PlanGroupId;

    internal static Result Reconcile(InboxMessage message, Plan? plan)
    {
        if (!IsRecoveryMessage(message))
            return new Result(message, IsActionable: true, ShouldArchive: false);

        var attachment = message.Attachments.FirstOrDefault(candidate =>
            string.Equals(candidate.Type, DecomposePlanInbox.AttachmentType, StringComparison.OrdinalIgnoreCase));
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
        var referencesCurrentTask = expectedMessageId is null ||
            string.Equals(message.Id, expectedMessageId, StringComparison.Ordinal);

        if (referencesCurrentPlan && activeLifecycle && referencesCurrentTask)
        {
            var active = string.Equals(message.Priority, "critical", StringComparison.OrdinalIgnoreCase)
                ? message
                : message with { Priority = "critical" };
            return new Result(active, IsActionable: true, ShouldArchive: false);
        }

        var explanation = GetResolutionExplanation(plan, planId, revision);
        var body = message.Body.Contains(ResolutionMarker, StringComparison.Ordinal)
            ? message.Body
            : $"{message.Body}\n\n---\n\n{ResolutionMarker} {explanation}";
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
