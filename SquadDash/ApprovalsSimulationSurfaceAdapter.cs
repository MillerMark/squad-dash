using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// <see cref="ISimulationSurfaceAdapter"/> for the Approvals panel.
/// Overlays static simulated commit approvals among real approvals via <see cref="CommitApprovalPanel"/>
/// without persisting to the approval store or enabling approve/reject actions.
/// </summary>
internal sealed class ApprovalsSimulationSurfaceAdapter : ISimulationSurfaceAdapter
{
    private readonly CommitApprovalPanel _panel;
    private readonly Dispatcher _dispatcher;

    // artifactId → (sessionId, CommitApprovalItem)
    private readonly Dictionary<string, (string SessionId, CommitApprovalItem Item)> _overlaid = new(StringComparer.Ordinal);

    public SimulationSurfaceKind SupportedSurface => SimulationSurfaceKind.Approvals;

    internal ApprovalsSimulationSurfaceAdapter(CommitApprovalPanel panel, Dispatcher dispatcher)
    {
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public Task OverlayAsync(SimulationArtifact artifact, object fixtureData)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (fixtureData is not CommitApprovalItem item)
            throw new ArgumentException(
                $"Expected fixtureData of type CommitApprovalItem but received {fixtureData?.GetType().Name ?? "null"}.",
                nameof(fixtureData));

        _overlaid[artifact.ArtifactId] = (artifact.SessionId, item);

        SquadDashTrace.Write("Simulation",
            $"Approval overlay: artifactId='{artifact.ArtifactId}', sha='{item.CommitSha}', desc='{item.Description}'.");

        _dispatcher.Invoke(() => _panel.AddItem(item));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(SimulationArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (_overlaid.TryGetValue(artifact.ArtifactId, out var entry))
        {
            _overlaid.Remove(artifact.ArtifactId);
            SquadDashTrace.Write("Simulation",
                $"Approval removed: artifactId='{artifact.ArtifactId}', id='{entry.Item.Id}'.");
            _dispatcher.Invoke(() => _panel.RemoveItemById(entry.Item.Id));
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
                    $"Approval removed (session cleanup): artifactId='{artifactId}', id='{entry.Item.Id}'.");
                _dispatcher.Invoke(() => _panel.RemoveItemById(entry.Item.Id));
            }
        }

        return Task.CompletedTask;
    }

    public bool Contains(string artifactId) => _overlaid.ContainsKey(artifactId);

    /// <summary>
    /// Checks whether the given approval item ID belongs to a simulated artifact,
    /// meaning production actions (approve, reject) must be blocked.
    /// </summary>
    internal bool IsSimulatedItem(string itemId)
        => _overlaid.Values.Any(entry => string.Equals(entry.Item.Id, itemId, StringComparison.Ordinal));
}
