namespace SquadDash.Hints;

/// <summary>
/// Implemented by data-context objects that represent a named agent.
/// Used by <see cref="HintControlResolver"/> to derive a stable control identity
/// for hint targeting.
/// </summary>
public interface IHaveAgentName {
    /// <summary>The agent's stable name (e.g. "orion-vale").</summary>
    string AgentName { get; }
}
