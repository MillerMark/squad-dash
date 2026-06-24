namespace SquadDash.Hints;

/// <summary>
/// Implemented by data-context objects that want to expose a stable name for hint targeting.
/// Lower priority than <see cref="IHaveAgentName"/> in the resolution chain.
/// </summary>
public interface INamedControl {
    /// <summary>A stable, non-empty identifier for the control.</summary>
    string ControlName { get; }
}
