using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// <see cref="ISimulationSurfaceAdapter"/> for the Plans panel.
/// Overlays static simulated plans among real plans via <see cref="PlansPanelController"/>
/// without triggering execution or mutating durable plan storage.
/// </summary>
internal sealed class PlanSimulationSurfaceAdapter : ISimulationSurfaceAdapter
{
    private readonly PlansPanelController _controller;
    private readonly Dispatcher _dispatcher;

    // artifactId → (sessionId, Plan)
    private readonly Dictionary<string, (string SessionId, Plan Plan)> _overlaid = new(StringComparer.Ordinal);

    public SimulationSurfaceKind SupportedSurface => SimulationSurfaceKind.Plan;

    internal PlanSimulationSurfaceAdapter(PlansPanelController controller, Dispatcher dispatcher)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public Task OverlayAsync(SimulationArtifact artifact, object fixtureData)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (fixtureData is not Plan plan)
            throw new ArgumentException(
                $"Expected fixtureData of type Plan but received {fixtureData?.GetType().Name ?? "null"}.",
                nameof(fixtureData));

        _overlaid[artifact.ArtifactId] = (artifact.SessionId, plan);

        SquadDashTrace.Write("Simulation",
            $"Plan overlay: artifactId='{artifact.ArtifactId}', planId='{plan.PlanId}'.");

        _dispatcher.Invoke(() => _controller.OnPlanChanged(plan));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(SimulationArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (_overlaid.TryGetValue(artifact.ArtifactId, out var entry))
        {
            _overlaid.Remove(artifact.ArtifactId);
            SquadDashTrace.Write("Simulation",
                $"Plan removed: artifactId='{artifact.ArtifactId}', planId='{entry.Plan.PlanId}'.");
            _dispatcher.Invoke(() => _controller.OnPlanRemoved(entry.Plan.PlanId));
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
            if (_overlaid.TryGetValue(artifactId, out var entry))
            {
                _overlaid.Remove(artifactId);
                SquadDashTrace.Write("Simulation",
                    $"Plan removed (session cleanup): artifactId='{artifactId}', planId='{entry.Plan.PlanId}'.");
                _dispatcher.Invoke(() => _controller.OnPlanRemoved(entry.Plan.PlanId));
            }
        }

        return Task.CompletedTask;
    }

    public bool Contains(string artifactId) => _overlaid.ContainsKey(artifactId);
}
