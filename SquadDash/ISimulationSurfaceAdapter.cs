using System.Threading.Tasks;

namespace SquadDash;

/// <summary>
/// Contract for a panel surface that can overlay and remove simulation artifacts.
/// Each surface (Plan, Notes, Tasks, etc.) implements this interface to participate
/// in the static simulation runtime.
/// </summary>
internal interface ISimulationSurfaceAdapter
{
    /// <summary>Which surface this adapter manages.</summary>
    SimulationSurfaceKind SupportedSurface { get; }

    /// <summary>Overlay a fixture among real data for the given artifact.</summary>
    Task OverlayAsync(SimulationArtifact artifact, object fixtureData);

    /// <summary>Remove exactly one session-owned artifact.</summary>
    Task RemoveAsync(SimulationArtifact artifact);

    /// <summary>Remove all artifacts belonging to the specified session.</summary>
    Task RemoveAllForSessionAsync(string sessionId);

    /// <summary>Check whether the specified artifact is currently overlaid.</summary>
    bool Contains(string artifactId);
}
