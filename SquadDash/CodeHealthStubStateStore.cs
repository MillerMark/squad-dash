using System;
using System.IO;
using System.Text.Json;

namespace SquadDash;

/// <summary>
/// Tracks which code health stub sidecar file has been rendered into the coordinator
/// thread, so stubs from the most recent code health session can be re-rendered on
/// app restart. State is persisted to <c>{stateDir}/codehealth-stub-state.json</c>.
/// </summary>
internal sealed class CodeHealthStubStateStore {

    private readonly string _statePath;

    /// <summary>Absolute path of the last sidecar whose stubs were rendered.</summary>
    public string? LastRenderedSidecarPath { get; set; }

    internal CodeHealthStubStateStore(string stateDir) {
        _statePath = Path.Combine(stateDir, "codehealth-stub-state.json");
    }

    /// <summary>Loads state from disk. On any failure, leaves properties at defaults.</summary>
    public void Load() {
        // Migration: check for old maintenance-stub-state.json file if codehealth-stub-state.json doesn't exist
        var actualPath = _statePath;
        if (!File.Exists(actualPath)) {
            var oldPath = _statePath.Replace("codehealth-stub-state.json", "maintenance-stub-state.json");
            if (File.Exists(oldPath)) {
                actualPath = oldPath;
            }
        }
        
        if (!File.Exists(actualPath)) return;
        try {
            var json = File.ReadAllText(actualPath);
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
                $"CodeHealthStubStateStore: failed to save: {ex.Message}");
        }
    }
}

