using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SquadDash.GuidedTours;

/// <summary>
/// Per-machine state store for the Guided Tour system.
/// Persists to <c>%LocalAppData%\SquadDash\guided-tour-state.json</c>.
/// </summary>
internal sealed class GuidedTourStateStore
{
    private readonly string _filePath;
    private GuidedTourState _state;

    /// <summary>Application-wide shared instance.</summary>
    public static GuidedTourStateStore Shared { get; } = new GuidedTourStateStore();

    public GuidedTourStateStore() : this(SquadDashPaths.AppData) { }

    internal GuidedTourStateStore(string directory)
    {
        _filePath = Path.Combine(directory, "guided-tour-state.json");
        _state    = Load();
    }

    /// <summary>
    /// Whether the first-run tour offer has already been shown on this machine.
    /// Setting this to <c>true</c> persists immediately.
    /// </summary>
    public bool Offered
    {
        get => _state.Offered;
        set
        {
            if (_state.Offered == value) return;
            _state = _state with { Offered = value };
            Flush();
        }
    }

    /// <summary>Returns <c>true</c> if the tour with the given ID has been completed.</summary>
    public bool IsCompleted(string tourId) =>
        _state.CompletedTourIds.Contains(tourId);

    /// <summary>Marks the tour with the given ID as completed and persists the change.</summary>
    public void MarkCompleted(string tourId)
    {
        if (_state.CompletedTourIds.Add(tourId))
            Flush();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private GuidedTourState Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new GuidedTourState();
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<GuidedTourState>(json) ?? new GuidedTourState();
        }
        catch { return new GuidedTourState(); }
    }

    private void Flush()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath,
                JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal */ }
    }
}

internal sealed record GuidedTourState
{
    public bool Offered { get; set; }
    public HashSet<string> CompletedTourIds { get; set; } = new();
}
