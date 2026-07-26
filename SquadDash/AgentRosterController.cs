using System;
using System.Collections.Generic;

namespace SquadDash;

/// <summary>
/// Routes <see cref="IAgentRosterView"/> calls from external callers
/// (e.g. <see cref="PromptExecutionController"/>) to the MainWindow agent-roster
/// state via injected delegates. MainWindow holds one instance and passes it
/// wherever an <see cref="IAgentRosterView"/> is required.
/// </summary>
internal sealed class AgentRosterController : IAgentRosterView
{
    private readonly Func<IReadOnlyList<AgentStatusCard>> _getAgents;
    private readonly Func<string?> _getCurrentSessionState;

    internal AgentRosterController(
        Func<IReadOnlyList<AgentStatusCard>> getAgents,
        Func<string?> getCurrentSessionState)
    {
        ArgumentNullException.ThrowIfNull(getAgents);
        ArgumentNullException.ThrowIfNull(getCurrentSessionState);

        _getAgents               = getAgents;
        _getCurrentSessionState  = getCurrentSessionState;
    }

    // ── IAgentRosterView ──────────────────────────────────────────────────
    IReadOnlyList<AgentStatusCard> IAgentRosterView.GetAgents()    => _getAgents();
    string? IAgentRosterView.CurrentSessionState                   => _getCurrentSessionState();
}
