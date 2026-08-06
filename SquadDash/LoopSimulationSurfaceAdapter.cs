using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// <see cref="ISimulationSurfaceAdapter"/> for the Loop panel.
/// Overlays a static "loop is running" display state without starting a real loop
/// or executing any prompts via <see cref="LoopController"/>.
/// </summary>
internal sealed class LoopSimulationSurfaceAdapter : ISimulationSurfaceAdapter
{
    private readonly Action<SimulationLoopState> _applyState;
    private readonly Action _clearState;
    private readonly Dispatcher _dispatcher;

    // artifactId → (sessionId, SimulationLoopState)
    private readonly Dictionary<string, (string SessionId, SimulationLoopState State)> _overlaid = new(StringComparer.Ordinal);

    public SimulationSurfaceKind SupportedSurface => SimulationSurfaceKind.Loop;

    /// <param name="applyState">
    /// Callback invoked on the UI thread to display simulated loop state in the panel.
    /// </param>
    /// <param name="clearState">
    /// Callback invoked on the UI thread to restore the loop panel to its real state.
    /// </param>
    internal LoopSimulationSurfaceAdapter(
        Action<SimulationLoopState> applyState,
        Action clearState,
        Dispatcher dispatcher)
    {
        _applyState = applyState ?? throw new ArgumentNullException(nameof(applyState));
        _clearState = clearState ?? throw new ArgumentNullException(nameof(clearState));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public Task OverlayAsync(SimulationArtifact artifact, object fixtureData)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (fixtureData is not SimulationLoopState state)
            throw new ArgumentException(
                $"Expected fixtureData of type SimulationLoopState but received {fixtureData?.GetType().Name ?? "null"}.",
                nameof(fixtureData));

        _overlaid[artifact.ArtifactId] = (artifact.SessionId, state);

        SquadDashTrace.Write("Simulation",
            $"Loop overlay: artifactId='{artifact.ArtifactId}', status='{state.StatusText}'.");

        _dispatcher.Invoke(() => _applyState(state));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(SimulationArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (_overlaid.TryGetValue(artifact.ArtifactId, out _))
        {
            _overlaid.Remove(artifact.ArtifactId);
            SquadDashTrace.Write("Simulation",
                $"Loop removed: artifactId='{artifact.ArtifactId}'.");

            // If no overlaid states remain, clear the simulated display
            if (_overlaid.Count == 0)
                _dispatcher.Invoke(() => _clearState());
        }

        return Task.CompletedTask;
    }

    public Task RemoveAllForSessionAsync(string sessionId)
    {
        var toRemove = _overlaid
            .Where(kvp => string.Equals(kvp.Value.SessionId, sessionId, StringComparison.Ordinal))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var artifactId in toRemove)
        {
            _overlaid.Remove(artifactId);
            SquadDashTrace.Write("Simulation",
                $"Loop removed (session cleanup): artifactId='{artifactId}'.");
        }

        if (toRemove.Count > 0 && _overlaid.Count == 0)
            _dispatcher.Invoke(() => _clearState());

        return Task.CompletedTask;
    }

    public bool Contains(string artifactId) => _overlaid.ContainsKey(artifactId);

    /// <summary>
    /// Checks whether any simulated loop state is currently being displayed.
    /// When true, real Stop/Abort buttons should be inert.
    /// </summary>
    internal bool IsSimulationActive => _overlaid.Count > 0;
}
