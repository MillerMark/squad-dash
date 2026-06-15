using System;
using System.IO;
using System.Text.Json;

namespace SquadDash;

/// <summary>
/// Tracks which maintenance stub sidecar file has been rendered into the coordinator
/// thread, so stubs from the most recent maintenance session can be re-rendered on
/// app restart. State is persisted to <c>{stateDir}/maintenance-stub-state.json</c>.
/// </summary>
internal sealed class MaintenanceStubStateStore {

    private readonly string _statePath;

    /// <summary>Absolute path of the last sidecar whose stubs were rendered.</summary>
    public string? LastRenderedSidecarPath { get; set; }

    internal MaintenanceStubStateStore(string stateDir) {
        _statePath = Path.Combine(stateDir, "maintenance-stub-state.json");
    }

    /// <summary>Loads state from disk. On any failure, leaves properties at defaults.</summary>
    public void Load() {
        if (!File.Exists(_statePath)) return;
        try {
            var json = File.ReadAllText(_statePath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("lastRenderedSidecarPath", out var el) &&
                el.ValueKind == JsonValueKind.String)
                LastRenderedSidecarPath = el.GetString();
        }
        catch { }
    }

    /// <summary>Persists state atomically to disk.</summary>
    public void Save() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            var json = JsonSerializer.Serialize(
                new { lastRenderedSidecarPath = LastRenderedSidecarPath },
                new JsonSerializerOptions { WriteIndented = true });
            JsonFileStorage.AtomicWrite(_statePath, json);
        }
        catch (Exception ex) {
            SquadDashTrace.Write(TraceCategory.General,
                $"MaintenanceStubStateStore: failed to save: {ex.Message}");
        }
    }
}
