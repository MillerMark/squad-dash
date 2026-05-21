using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SquadDash;

/// <summary>
/// Tracks per-task frequency state for maintenance mode.
/// State is persisted to <c>{stateDir}/maintenance-state.json</c>.
/// </summary>
internal sealed class MaintenanceStateStore {

    private readonly string _statePath;
    private Dictionary<string, TaskState> _tasks = new(StringComparer.Ordinal);

    internal MaintenanceStateStore(string stateDir) {
        _statePath = Path.Combine(stateDir, "maintenance-state.json");
    }

    /// <summary>Reloads state from disk. On any failure, starts with empty state.</summary>
    public void Reload() {
        if (!File.Exists(_statePath)) {
            _tasks = new Dictionary<string, TaskState>(StringComparer.Ordinal);
            return;
        }

        try {
            var json = File.ReadAllText(_statePath);
            using var doc = JsonDocument.Parse(json);
            var loaded = new Dictionary<string, TaskState>(StringComparer.Ordinal);

            if (doc.RootElement.TryGetProperty("tasks", out var tasksEl)) {
                foreach (var prop in tasksEl.EnumerateObject()) {
                    var entry = ParseEntry(prop.Value);
                    if (entry is not null)
                        loaded[prop.Name] = entry;
                }
            }
            _tasks = loaded;
        }
        catch {
            _tasks = new Dictionary<string, TaskState>(StringComparer.Ordinal);
        }
    }

    /// <summary>Returns true if this task is eligible to run based on its frequency.</summary>
    public bool IsEligible(string taskId, string frequency, string? commitSha) {
        switch (frequency.ToLowerInvariant()) {
            case "always":
                return true;

            case "per-commit":
                if (commitSha is null) {
                    SquadDashTrace.Write(TraceCategory.General,
                        $"MaintenanceStateStore: per-commit git fallback — commit SHA unavailable, treating task '{taskId}' as daily");
                    goto case "daily";
                }
                if (!_tasks.TryGetValue(taskId, out var commitState))
                    return true;
                return !string.Equals(commitState.LastCommitSha, commitSha,
                    StringComparison.OrdinalIgnoreCase);

            case "weekly":
                if (!_tasks.TryGetValue(taskId, out var weeklyState))
                    return true;
                if (weeklyState.LastRunAt is null)
                    return true;
                return weeklyState.LastRunAt.Value < DateTime.UtcNow.AddDays(-7);

            case "monthly":
                if (!_tasks.TryGetValue(taskId, out var monthlyState))
                    return true;
                if (monthlyState.LastRunAt is null)
                    return true;
                var now = DateTime.UtcNow;
                var last = monthlyState.LastRunAt.Value;
                return last.Year < now.Year || (last.Year == now.Year && last.Month < now.Month);

            case "daily":
            default:
                if (!_tasks.TryGetValue(taskId, out var dailyState))
                    return true;
                if (dailyState.LastRunAt is null)
                    return true;
                return dailyState.LastRunAt.Value.Date < DateTime.UtcNow.Date;
        }
    }

    /// <summary>Returns the UTC timestamp of the last recorded run for the task, or null if never run.</summary>
    public DateTime? GetLastRunAt(string taskId) {
        if (_tasks.TryGetValue(taskId, out var state))
            return state.LastRunAt;
        return null;
    }

    /// <summary>Records a completed run and persists state atomically.</summary>
    public void RecordRun(string taskId, string? commitSha) {
        var entry = new TaskState {
            LastRunAt    = DateTime.UtcNow,
            LastCommitSha = commitSha,
        };
        _tasks[taskId] = entry;
        Persist();
    }

    // ── Persistence ────────────────────────────────────────────────────────────

    private void Persist() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);

            using var ms = new System.IO.MemoryStream();
            using var w  = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });
            w.WriteStartObject();
            w.WritePropertyName("tasks");
            w.WriteStartObject();
            foreach (var (id, state) in _tasks) {
                w.WritePropertyName(id);
                w.WriteStartObject();
                w.WriteString("lastRunAt",    state.LastRunAt?.ToString("O") ?? "");
                w.WriteString("lastCommitSha", state.LastCommitSha ?? "");
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
            w.Flush();

            var json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            JsonFileStorage.AtomicWrite(_statePath, json);
        }
        catch (Exception ex) {
            SquadDashTrace.Write(TraceCategory.General,
                $"MaintenanceStateStore: failed to persist state: {ex.Message}");
        }
    }

    private static TaskState? ParseEntry(JsonElement el) {
        if (el.ValueKind != JsonValueKind.Object) return null;
        var s = new TaskState();
        if (el.TryGetProperty("lastRunAt", out var runAt) && runAt.ValueKind == JsonValueKind.String) {
            if (DateTime.TryParse(runAt.GetString(), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                s.LastRunAt = dt;
        }
        if (el.TryGetProperty("lastCommitSha", out var sha) && sha.ValueKind == JsonValueKind.String)
            s.LastCommitSha = sha.GetString();
        return s;
    }

    private sealed class TaskState {
        public DateTime? LastRunAt    { get; set; }
        public string?   LastCommitSha { get; set; }
    }
}
