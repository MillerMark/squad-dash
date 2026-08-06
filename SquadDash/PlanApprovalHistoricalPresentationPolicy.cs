namespace SquadDash;

/// <summary>
/// Chooses the visual rendered for an approval boundary after execution has begun.
/// Historical, non-gated boundaries disappear; a resolved primary boundary becomes a
/// blue approval check; unresolved boundaries remain visible but read-only.
/// </summary>
internal static class PlanApprovalHistoricalPresentationPolicy
{
    internal static PlanApprovalControlVisualState Resolve(
        bool executionLocked,
        string? controllingGateStatus,
        bool isPrimaryAnchor,
        bool hasUnresolvedEquivalent = false)
    {
        // Resolution is authoritative even when the individual task boundary has not yet
        // crossed its execution frontier. A single approved gate can be projected at several
        // equivalent anchors; only its chosen presentation anchor survives as the blue check.
        // Leaving the other projections editable allows a stale viewer to erase the approval.
        if (controllingGateStatus == PlanGateStatus.Approved)
            return isPrimaryAnchor
                ? PlanApprovalControlVisualState.ApprovedCheck
                : PlanApprovalControlVisualState.Hidden;

        if (controllingGateStatus == PlanGateStatus.Skipped)
            return PlanApprovalControlVisualState.Hidden;

        // A gate that is actively awaiting the human is already part of the execution record.
        // It remains visible, but cannot be retargeted or removed from the graph.
        if (controllingGateStatus == PlanGateStatus.AwaitingApproval)
            return PlanApprovalControlVisualState.LockedOctagon;

        if (!executionLocked)
            return PlanApprovalControlVisualState.EditableOctagon;

        if (controllingGateStatus == PlanGateStatus.Pending ||
            hasUnresolvedEquivalent)
            return PlanApprovalControlVisualState.LockedOctagon;

        return PlanApprovalControlVisualState.Hidden;
    }
}

internal enum PlanApprovalControlVisualState
{
    EditableOctagon,
    LockedOctagon,
    ApprovedCheck,
    Hidden,
}
