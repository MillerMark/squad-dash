namespace SquadDash;

/// <summary>
/// Resolves the already-durable approval operator guide for a human-required interruption.
/// This is deliberately structural: it follows an approved gate whose before-task boundary
/// contains the interrupted task. It never parses AI prose to guess which guide is relevant.
/// </summary>
internal static class PlanHumanActionGuideReferenceResolver
{
    internal const string LinkLabel = "Open required instructions";

    internal static InboxAttachment? Resolve(
        Plan? plan,
        string taskId,
        InboxMessage? approvalMessage)
    {
        if (plan?.InterruptionData?.AttentionCategory != PlanAttentionCategory.HumanInputRequired ||
            !string.Equals(plan.InterruptionData.InterruptedTaskId, taskId, StringComparison.Ordinal) ||
            approvalMessage is null ||
            !string.Equals(
                approvalMessage.Id,
                DurableApprovalRequestManager.BuildMessageId(plan.PlanId),
                StringComparison.Ordinal))
        {
            return null;
        }

        var hasRelevantApprovedGate = plan.ApprovalGates.Any(gate =>
            gate.Status == PlanGateStatus.Approved &&
            gate.BeforeTaskIds.Contains(taskId, StringComparer.Ordinal));
        if (!hasRelevantApprovedGate)
            return null;

        var hasDurableGuide = approvalMessage.Attachments?.Any(attachment =>
            attachment is not null &&
            string.Equals(
                attachment.Type,
                DurableApprovalRequestManager.OperatorGuideAttachmentType,
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(attachment.Content)) == true;
        if (!hasDurableGuide)
            return null;

        return new InboxAttachment
        {
            Type = DurableApprovalRequestManager.RunbookAttachmentType,
            Label = LinkLabel,
            InboxMessageId = approvalMessage.Id,
            PlanGroupId = plan.PlanId,
            PlanRevision = plan.Revision,
        };
    }

    internal static InboxAttachment? FindPreservedReference(InboxMessage message) =>
        message.Attachments?.FirstOrDefault(attachment =>
            attachment is not null &&
            string.Equals(
                attachment.Type,
                DurableApprovalRequestManager.RunbookAttachmentType,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(attachment.Label, LinkLabel, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(attachment.InboxMessageId));
}
