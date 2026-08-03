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
        if (!executionLocked)
            return PlanApprovalControlVisualState.EditableOctagon;

        if (controllingGateStatus == PlanGateStatus.Approved && isPrimaryAnchor)
            return PlanApprovalControlVisualState.ApprovedCheck;

        if (controllingGateStatus is PlanGateStatus.Pending or PlanGateStatus.AwaitingApproval ||
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
