namespace SquadDash;

/// <summary>
/// Retires every transient or actionable artifact that belongs to an archived plan.
/// Archiving applies to the plan identity, not merely to its currently accepted revision.
/// </summary>
internal static class PlanFamilyArtifactArchiver
{
    internal static IReadOnlyList<string> Archive(
        string planId,
        PendingDecomposePlanStore pendingStore,
        InboxStore inboxStore) =>
        ArchiveMany([planId], pendingStore, inboxStore);

    internal static IReadOnlyList<string> ArchiveMany(
        IEnumerable<string> planIds,
        PendingDecomposePlanStore pendingStore,
        InboxStore inboxStore)
    {
        var archivedPlanIds = planIds
            .Where(planId => !string.IsNullOrWhiteSpace(planId))
            .ToHashSet(StringComparer.Ordinal);
        if (archivedPlanIds.Count == 0)
            return [];

        foreach (var planId in archivedPlanIds)
            pendingStore.Archive(planId);

        var archivedMessageIds = inboxStore.LoadAll()
            .Where(message => archivedPlanIds.Any(planId => ReferencesPlan(message, planId)))
            .Select(message => message.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var messageId in archivedMessageIds)
            inboxStore.Archive(messageId);

        return archivedMessageIds;
    }

    internal static bool ReferencesPlan(InboxMessage message, string planId)
    {
        if (message is null || string.IsNullOrWhiteSpace(planId))
            return false;

        if (string.Equals(
                message.Id,
                DurableApprovalRequestManager.BuildMessageId(planId),
                StringComparison.Ordinal))
            return true;

        return message.Attachments?.Any(attachment => attachment is not null &&
            string.Equals(attachment.PlanGroupId, planId, StringComparison.Ordinal)) == true;
    }
}
