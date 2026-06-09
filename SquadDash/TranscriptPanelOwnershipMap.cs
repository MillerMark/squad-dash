namespace SquadDash;

/// <summary>
/// Explicitly stores which <see cref="AgentStatusCard"/> owns the main transcript panel
/// and each secondary transcript panel.  The owner is recorded when a panel is created
/// or reassigned, so hover-glow handlers can look it up directly instead of re-deriving
/// it from <c>IsTranscriptTargetSelected</c> at interaction time.
///
/// Panel identity is represented by an opaque <c>object</c> token so the map remains
/// testable without a WPF STA thread.  In production, callers pass the <c>Border</c>
/// that wraps the panel.
/// </summary>
internal sealed class TranscriptPanelOwnershipMap
{
    private AgentStatusCard? _mainPanelOwner;
    private readonly Dictionary<object, AgentStatusCard> _panelToAgent =
        new(ReferenceEqualityComparer.Instance);

    // ── Main panel ────────────────────────────────────────────────────────────

    /// <summary>The agent whose transcript is currently showing in the main panel, or
    /// <c>null</c> when the main panel is hidden.</summary>
    public AgentStatusCard? MainPanelOwner => _mainPanelOwner;

    /// <summary>Records <paramref name="agent"/> as the owner of the main panel.
    /// Pass <c>null</c> when the main panel is hidden.</summary>
    public void SetMainPanelOwner(AgentStatusCard? agent)
    {
        _mainPanelOwner = agent;
    }

    /// <summary>Returns <c>true</c> when <paramref name="agent"/> is the recorded owner
    /// of the main transcript panel (reference equality; never true for null).</summary>
    public bool IsMainPanelOwner(AgentStatusCard agent)
    {
        return _mainPanelOwner is not null &&
               ReferenceEquals(_mainPanelOwner, agent);
    }

    // ── Secondary panels ──────────────────────────────────────────────────────

    /// <summary>Associates <paramref name="panelToken"/> (typically a <c>Border</c>)
    /// with <paramref name="owner"/>.  Overwrites any previous entry for the same token.</summary>
    public void RegisterSecondaryPanel(object panelToken, AgentStatusCard owner)
    {
        _panelToAgent[panelToken] = owner;
    }

    /// <summary>Removes the entry for <paramref name="panelToken"/> if present.</summary>
    public void UnregisterSecondaryPanel(object panelToken)
    {
        _panelToAgent.Remove(panelToken);
    }

    /// <summary>Returns the agent that owns <paramref name="panelToken"/>, or <c>null</c>
    /// if the token has not been registered.</summary>
    public AgentStatusCard? GetOwnerForPanel(object panelToken)
    {
        return _panelToAgent.TryGetValue(panelToken, out var owner) ? owner : null;
    }

    /// <summary>Returns the panel token registered for <paramref name="agent"/>, or
    /// <c>null</c> if the agent has no secondary panel open.  When multiple tokens share
    /// the same owner, the first found is returned.</summary>
    public object? GetSecondaryPanelForAgent(AgentStatusCard agent)
    {
        foreach (var kvp in _panelToAgent)
            if (ReferenceEquals(kvp.Value, agent))
                return kvp.Key;

        return null;
    }

    /// <summary>Returns <c>true</c> when at least one secondary panel is registered
    /// for <paramref name="agent"/>.</summary>
    public bool HasSecondaryPanel(AgentStatusCard agent)
    {
        foreach (var owner in _panelToAgent.Values)
            if (ReferenceEquals(owner, agent))
                return true;

        return false;
    }
}
