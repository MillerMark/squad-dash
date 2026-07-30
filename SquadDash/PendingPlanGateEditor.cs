namespace SquadDash;

/// <summary>
/// Persists approval-gate edits on a pending (not-yet-executing) inbox plan, recomputes
/// the content revision, and atomically replaces the corresponding Inbox message attachment
/// and action payloads so that execution actions always reference the latest gate definition.
/// </summary>
internal static class PendingPlanGateEditor
{
    /// <summary>Result of applying a gate edit to a pending plan.</summary>
    internal sealed record EditResult(
        PendingDecomposePlan UpdatedPlan,
        Plan SyntheticDurablePlan,
        string? NewInboxMessageId);

    /// <summary>
    /// Applies the gate configuration from <paramref name="gatedPlan"/> to the pending plan
    /// store, recomputes the draft revision, and atomically replaces the host-owned inbox
    /// message so that its attachment snapshot and decompose action payloads reflect the new
    /// gate definition.
    /// </summary>
    /// <param name="gatedPlan">
    /// The <see cref="Plan"/> after gate add/remove/anchor edits by the viewer.
    /// </param>
    /// <param name="oldInboxMessageId">
    /// The current inbox message ID (based on the pre-edit revision). Null when no inbox
    /// message exists for this plan.
    /// </param>
    /// <param name="pendingStore">Store for transient pending plans.</param>
    /// <param name="inboxStore">
    /// Inbox store, or null when the workspace has no inbox. When non-null the old message
    /// is deleted and a replacement with the new revision-based ID is saved.
    /// </param>
    /// <param name="activeBranch">Current git branch, for action button filtering.</param>
    internal static EditResult Apply(
        Plan gatedPlan,
        string? oldInboxMessageId,
        PendingDecomposePlanStore pendingStore,
        InboxStore? inboxStore,
        string? activeBranch)
    {
        var updatedGroup = PendingDecomposePlanAdapter.FromPlan(gatedPlan).Group;
        var savedPending = pendingStore.Save(updatedGroup);

        var syntheticPlan = PendingDecomposePlanAdapter.ToPlan(
            savedPending, gatedPlan.Timestamps.CreatedAt);

        string? newInboxMessageId = null;
        if (inboxStore is not null && oldInboxMessageId is not null)
        {
            var existing = inboxStore.GetById(oldInboxMessageId);
            if (existing is not null)
            {
                // Delete old message (its ID embeds the old revision).
                inboxStore.Delete(oldInboxMessageId);

                // Build a replacement message with the new revision-based ID, preserving
                // the original timestamp and read state.
                var replacement = DecomposePlanInbox.BuildMessage(
                    savedPending,
                    existing.Timestamp,
                    explicitlyRequested: true,
                    activeBranch) with
                {
                    Read = existing.Read,
                };
                inboxStore.Save(replacement);
                newInboxMessageId = replacement.Id;
            }
        }

        return new EditResult(savedPending, syntheticPlan, newInboxMessageId);
    }
}
