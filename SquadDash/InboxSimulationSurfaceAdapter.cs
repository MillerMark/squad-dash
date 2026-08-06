using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// <see cref="ISimulationSurfaceAdapter"/> for the Inbox panel.
/// Overlays static simulated messages among real messages via <see cref="InboxPanelController"/>
/// without persisting to <see cref="InboxStore"/> or enabling archive/delete actions.
/// </summary>
internal sealed class InboxSimulationSurfaceAdapter : ISimulationSurfaceAdapter
{
    private readonly InboxPanelController _controller;
    private readonly Dispatcher _dispatcher;

    // artifactId → (sessionId, InboxMessage)
    private readonly Dictionary<string, (string SessionId, InboxMessage Message)> _overlaid = new(StringComparer.Ordinal);

    public SimulationSurfaceKind SupportedSurface => SimulationSurfaceKind.Inbox;

    internal InboxSimulationSurfaceAdapter(InboxPanelController controller, Dispatcher dispatcher)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public Task OverlayAsync(SimulationArtifact artifact, object fixtureData)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (fixtureData is not InboxMessage message)
            throw new ArgumentException(
                $"Expected fixtureData of type InboxMessage but received {fixtureData?.GetType().Name ?? "null"}.",
                nameof(fixtureData));

        _overlaid[artifact.ArtifactId] = (artifact.SessionId, message);

        SquadDashTrace.Write("Simulation",
            $"Inbox overlay: artifactId='{artifact.ArtifactId}', subject='{message.Subject}'.");

        _dispatcher.Invoke(() => _controller.AddSimulatedMessage(artifact.ArtifactId, message));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(SimulationArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (_overlaid.TryGetValue(artifact.ArtifactId, out var entry))
        {
            _overlaid.Remove(artifact.ArtifactId);
            SquadDashTrace.Write("Simulation",
                $"Inbox removed: artifactId='{artifact.ArtifactId}', subject='{entry.Message.Subject}'.");
            _dispatcher.Invoke(() => _controller.RemoveSimulatedMessage(artifact.ArtifactId));
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
                    $"Inbox removed (session cleanup): artifactId='{artifactId}', subject='{entry.Message.Subject}'.");
                _dispatcher.Invoke(() => _controller.RemoveSimulatedMessage(artifactId));
            }
        }

        return Task.CompletedTask;
    }

    public bool Contains(string artifactId) => _overlaid.ContainsKey(artifactId);

    /// <summary>
    /// Checks whether the given message ID belongs to a simulated artifact,
    /// meaning production actions (archive, delete, mark read) must be blocked.
    /// </summary>
    internal bool IsSimulatedMessage(string messageId)
        => _overlaid.Values.Any(entry => string.Equals(entry.Message.Id, messageId, StringComparison.Ordinal));
}
