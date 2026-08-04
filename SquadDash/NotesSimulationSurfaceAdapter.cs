using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// <see cref="ISimulationSurfaceAdapter"/> for the Notes panel.
/// Overlays static simulated notes among real notes via <see cref="NotesPanelController"/>
/// without persisting to disk or mutating the <see cref="NotesStore"/>.
/// </summary>
internal sealed class NotesSimulationSurfaceAdapter : ISimulationSurfaceAdapter
{
    private readonly NotesPanelController _controller;
    private readonly Dispatcher _dispatcher;

    // artifactId → (sessionId, NoteItem)
    private readonly Dictionary<string, (string SessionId, NoteItem Note)> _overlaid = new(StringComparer.Ordinal);

    public SimulationSurfaceKind SupportedSurface => SimulationSurfaceKind.Notes;

    internal NotesSimulationSurfaceAdapter(NotesPanelController controller, Dispatcher dispatcher)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public Task OverlayAsync(SimulationArtifact artifact, object fixtureData)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (fixtureData is not NoteItem note)
            throw new ArgumentException(
                $"Expected fixtureData of type NoteItem but received {fixtureData?.GetType().Name ?? "null"}.",
                nameof(fixtureData));

        _overlaid[artifact.ArtifactId] = (artifact.SessionId, note);

        SquadDashTrace.Write("Simulation",
            $"Note overlay: artifactId='{artifact.ArtifactId}', noteId='{note.Id}', title='{note.Title}'.");

        _dispatcher.Invoke(() => _controller.AddNote(note));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(SimulationArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (_overlaid.TryGetValue(artifact.ArtifactId, out var entry))
        {
            _overlaid.Remove(artifact.ArtifactId);
            SquadDashTrace.Write("Simulation",
                $"Note removed: artifactId='{artifact.ArtifactId}', noteId='{entry.Note.Id}'.");
            _dispatcher.Invoke(() => _controller.RemoveNote(entry.Note.Id));
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
                    $"Note removed (session cleanup): artifactId='{artifactId}', noteId='{entry.Note.Id}'.");
                _dispatcher.Invoke(() => _controller.RemoveNote(entry.Note.Id));
            }
        }

        return Task.CompletedTask;
    }

    public bool Contains(string artifactId) => _overlaid.ContainsKey(artifactId);
}
