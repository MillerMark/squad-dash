namespace SquadDash;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

/// <summary>Persists expand/collapse UI state for maintenance panel task rows.</summary>
internal sealed class MaintenancePanelUiState {

    private readonly string _statePath;
    private HashSet<string> _expandedTaskIds = new(StringComparer.Ordinal);

    internal MaintenancePanelUiState(string stateDir) {
        _statePath = Path.Combine(stateDir, "maintenance-panel-ui.json");
    }

    public bool IsExpanded(string taskId) => _expandedTaskIds.Contains(taskId);

    public void SetExpanded(string taskId, bool expanded) {
        if (expanded) _expandedTaskIds.Add(taskId);
        else          _expandedTaskIds.Remove(taskId);
        Save();
    }

    public void Load() {
        if (!File.Exists(_statePath)) return;
        try {
            var json = File.ReadAllText(_statePath);
            using var doc = JsonDocument.Parse(json);
            var loaded = new HashSet<string>(StringComparer.Ordinal);
            if (doc.RootElement.TryGetProperty("expandedTaskIds", out var arr) &&
                arr.ValueKind == JsonValueKind.Array) {
                foreach (var el in arr.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String && el.GetString() is { } id)
                        loaded.Add(id);
            }
            _expandedTaskIds = loaded;
        }
        catch { }
    }

    private void Save() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            var payload = new { expandedTaskIds = _expandedTaskIds };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            JsonFileStorage.AtomicWrite(_statePath, json);
        }
        catch (Exception ex) {
            SquadDashTrace.Write(TraceCategory.General,
                $"MaintenancePanelUiState: failed to save: {ex.Message}");
        }
    }
}
