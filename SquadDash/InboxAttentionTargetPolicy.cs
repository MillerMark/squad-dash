namespace SquadDash;

internal enum InboxAttentionTargetKind
{
    Menu,
    MessageRow,
    FilteredPanel,
}

/// <summary>
/// Chooses the truthful Inbox attention surface without changing a user's filters.
/// Persisted visibility counts as visible while startup is still restoring panels.
/// </summary>
internal static class InboxAttentionTargetPolicy
{
    internal static bool ShouldUseInboxPanel(bool panelVisible, bool persistedPanelVisible) =>
        panelVisible || persistedPanelVisible;

    internal static InboxAttentionTargetKind Select(bool panelVisible, bool messageRowVisible)
    {
        if (!panelVisible)
            return InboxAttentionTargetKind.Menu;

        return messageRowVisible
            ? InboxAttentionTargetKind.MessageRow
            : InboxAttentionTargetKind.FilteredPanel;
    }
}
