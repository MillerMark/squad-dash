using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// <see cref="ISimulationSurfaceAdapter"/> for the Tasks panel.
/// Overlays static simulated tasks among real tasks via <see cref="TasksPanelController"/>
/// without writing to <c>tasks.md</c> or triggering any plan execution.
/// </summary>
internal sealed class TasksSimulationSurfaceAdapter : ISimulationSurfaceAdapter
{
    private readonly TasksPanelController _controller;
    private readonly Dispatcher _dispatcher;

    // artifactId → (sessionId, TaskItem)
    private readonly Dictionary<string, (string SessionId, TaskItem Task)> _overlaid = new(StringComparer.Ordinal);

    public SimulationSurfaceKind SupportedSurface => SimulationSurfaceKind.Tasks;

    internal TasksSimulationSurfaceAdapter(TasksPanelController controller, Dispatcher dispatcher)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public Task OverlayAsync(SimulationArtifact artifact, object fixtureData)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (fixtureData is not TaskItem task)
            throw new ArgumentException(
                $"Expected fixtureData of type TaskItem but received {fixtureData?.GetType().Name ?? "null"}.",
                nameof(fixtureData));

        _overlaid[artifact.ArtifactId] = (artifact.SessionId, task);

        SquadDashTrace.Write("Simulation",
            $"Task overlay: artifactId='{artifact.ArtifactId}', text='{task.Text}'.");

        _dispatcher.Invoke(() => _controller.AddSimulatedTask(artifact.ArtifactId, task));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(SimulationArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (_overlaid.TryGetValue(artifact.ArtifactId, out var entry))
        {
            _overlaid.Remove(artifact.ArtifactId);
            SquadDashTrace.Write("Simulation",
                $"Task removed: artifactId='{artifact.ArtifactId}', text='{entry.Task.Text}'.");
            _dispatcher.Invoke(() => _controller.RemoveSimulatedTask(artifact.ArtifactId));
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
                    $"Task removed (session cleanup): artifactId='{artifactId}', text='{entry.Task.Text}'.");
                _dispatcher.Invoke(() => _controller.RemoveSimulatedTask(artifactId));
            }
        }

        return Task.CompletedTask;
    }

    public bool Contains(string artifactId) => _overlaid.ContainsKey(artifactId);
}
