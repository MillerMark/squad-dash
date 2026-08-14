using System;

namespace SquadDash;

/// <summary>
/// Persists any host-validated staged-plan edit and keeps the revision-addressed Inbox
/// message synchronized with the newly materialized proposal.
/// </summary>
internal static class PendingPlanEditor
{
    internal sealed record EditResult(
        PendingDecomposePlan UpdatedPlan,
        Plan SyntheticDurablePlan,
        string? NewInboxMessageId);

    internal static EditResult Apply(
        DecomposedTaskGroup editedGroup,
        DateTimeOffset createdAt,
        string? oldInboxMessageId,
        PendingDecomposePlanStore pendingStore,
        InboxStore? inboxStore,
        string? activeBranch)
    {
        if (!StagedPlanEditor.TryValidate(editedGroup, out var normalized, out var error) || normalized is null)
            throw new InvalidOperationException(error ?? "The edited plan is invalid.");

        var savedPending = pendingStore.Save(normalized);
        var syntheticPlan = PendingDecomposePlanAdapter.ToPlan(savedPending, createdAt);
        string? newInboxMessageId = null;

        if (inboxStore is not null && oldInboxMessageId is not null)
        {
            var existing = inboxStore.GetById(oldInboxMessageId);
            if (existing is not null)
            {
                var replacement = DecomposePlanInbox.BuildMessage(
                    savedPending,
                    existing.Timestamp,
                    explicitlyRequested: true,
                    activeBranch) with { Read = existing.Read };
                inboxStore.Replace(oldInboxMessageId, replacement);
                newInboxMessageId = replacement.Id;
            }
        }

        return new EditResult(savedPending, syntheticPlan, newInboxMessageId);
    }
}
