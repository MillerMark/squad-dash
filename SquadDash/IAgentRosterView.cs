using System.Collections.Generic;

namespace SquadDash;

internal interface IAgentRosterView {
    IReadOnlyList<AgentStatusCard> GetAgents();
    string? CurrentSessionState { get; }
}
