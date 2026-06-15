using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

/// <summary>
/// Tracks per-task frequency state for maintenance mode.
/// State is persisted to <c>{stateDir}/maintenance-state.json</c>.
/// </summary>
internal sealed class MaintenanceStateStore {

    private readonly string _statePath;
    private readonly ITimeProvider _clock;
    private readonly Func<string, string, Task<int>> _commitCounter;
    private Dictionary<string, TaskState> _tasks = new(StringComparer.Ordinal);

    internal MaintenanceStateStore(string stateDir, ITimeProvider? clock = null,
        Func<string, string, Task<int>>? commitCounter = null) {
        _statePath = Path.Combine(stateDir, "maintenance-state.json");
        _clock = clock ?? SystemTimeProvider.Instance;
        _commitCounter = commitCounter ?? CountCommitsSinceAsync;
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
    public async Task<bool> IsEligibleAsync(string taskId, string frequency, string? commitSha, string? workspacePath = null) {
        var freqLower = frequency.ToLowerInvariant();
        
        // Handle "always"
        if (freqLower == "always")
            return true;

        // Handle "every-N-commits" (e.g., "every-5-commits", "every-10-commits")
        if (freqLower.StartsWith("every-") && freqLower.EndsWith("-commits")) {
            var nStr = freqLower["every-".Length..^"-commits".Length];
            if (!int.TryParse(nStr, out var n) || n < 1) goto HandleDaily;

            if (commitSha is null || workspacePath is null) goto HandleDaily;
            if (!_tasks.TryGetValue(taskId, out var everyNState)) return true;
            if (string.IsNullOrEmpty(everyNState.LastCommitSha)) return true;
            if (string.Equals(everyNState.LastCommitSha, commitSha, StringComparison.OrdinalIgnoreCase))
                return false;

            int count = await _commitCounter(everyNState.LastCommitSha, workspacePath).ConfigureAwait(false);
            return count >= n;
        }

        // Handle "after-commits" / "per-commit"
        if (freqLower == "after-commits" || freqLower == "per-commit") {
            if (commitSha is null) {
                SquadDashTrace.Write(TraceCategory.General,
                    $"MaintenanceStateStore: after-commits git fallback — commit SHA unavailable, treating task '{taskId}' as daily");
                goto HandleDaily;
            }
            if (!_tasks.TryGetValue(taskId, out var commitState))
                return true;
            return !string.Equals(commitState.LastCommitSha, commitSha,
                StringComparison.OrdinalIgnoreCase);
        }

        // Handle "weekly" (legacy format)
        if (freqLower == "weekly") {
            if (!_tasks.TryGetValue(taskId, out var weeklyState))
                return true;
            if (weeklyState.LastRunAt is null)
                return true;
            return weeklyState.LastRunAt.Value < StartOfCurrentWeekUtc(_clock.UtcNow.Date);
        }

        // Handle "weekly-Monday", "weekly-Tuesday", etc.
        if (freqLower.StartsWith("weekly-")) {
            var dayStr = freqLower.Substring(7); // Remove "weekly-" prefix
            if (TryParseDayOfWeek(dayStr, out var targetDay)) {
                if (!_tasks.TryGetValue(taskId, out var weeklyDayState))
                    return true;
                if (weeklyDayState.LastRunAt is null)
                    return true;
                
                var lastRun = weeklyDayState.LastRunAt.Value;
                var today = _clock.UtcNow.Date;
                var todayDow = today.DayOfWeek;
                
                // Check if today is the target day and last run was before today
                if (todayDow == targetDay && lastRun.Date < today)
                    return true;
                
                // Also check if we're past the target day this week and haven't run yet this week
                var startOfWeek = today.AddDays(-(int)todayDow);
                if (todayDow > targetDay && lastRun < startOfWeek)
                    return true;
                
                return false;
            }
            // If day parsing fails, treat as daily
            goto HandleDaily;
        }

        // Handle "monthly"
        if (freqLower == "monthly") {
            if (!_tasks.TryGetValue(taskId, out var monthlyState))
                return true;
            if (monthlyState.LastRunAt is null)
                return true;
            var now = _clock.UtcNow;
            var last = monthlyState.LastRunAt.Value;
            return last.Year < now.Year || (last.Year == now.Year && last.Month < now.Month);
        }

        // Handle "daily" and unknown frequencies
        HandleDaily:
        if (!_tasks.TryGetValue(taskId, out var dailyState))
            return true;
        if (dailyState.LastRunAt is null)
            return true;
        return dailyState.LastRunAt.Value.Date < _clock.UtcNow.Date;
    }

    /// <summary>Attempts to parse a day-of-week string (e.g., "Monday", "tuesday").</summary>
    private static bool TryParseDayOfWeek(string dayStr, out DayOfWeek result) {
        return Enum.TryParse<DayOfWeek>(dayStr, ignoreCase: true, out result);
    }

    /// <summary>Returns the UTC timestamp of the last recorded run for the task, or null if never run.</summary>
    public DateTime? GetLastRunAt(string taskId) {
        if (_tasks.TryGetValue(taskId, out var state))
            return state.LastRunAt;
        return null;
    }

    /// <summary>Returns the last recorded commit SHA for the task, or null if never run.</summary>
    public string? GetLastCommitSha(string taskId) {
        if (_tasks.TryGetValue(taskId, out var state))
            return state.LastCommitSha;
        return null;
    }

    /// <summary>
    /// Returns the number of commits between the task's last-run SHA and HEAD,
    /// using the injected commit counter. Returns 0 if the task has no recorded baseline.
    /// </summary>
    public Task<int> GetCommitCountSinceAsync(string taskId, string workspacePath) {
        if (!_tasks.TryGetValue(taskId, out var state) || string.IsNullOrEmpty(state.LastCommitSha))
            return Task.FromResult(0);
        return _commitCounter(state.LastCommitSha, workspacePath);
    }

    /// <summary>Records a completed run and persists state atomically.</summary>
    public void RecordRun(string taskId, string? commitSha) {
        var entry = new TaskState {
            LastRunAt    = _clock.UtcNow,
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

    /// <summary>Returns the UTC DateTime for the start of the current ISO week (Monday 00:00:00).</summary>
    private static DateTime StartOfCurrentWeekUtc(DateTime today)
    {
        // DayOfWeek: Sunday=0, Monday=1 … Saturday=6; we want Monday=0
        int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        return today.AddDays(-daysSinceMonday);
    }

    private static async Task<int> CountCommitsSinceAsync(string baseSha, string workspacePath) {
        try {
            var psi = new System.Diagnostics.ProcessStartInfo(
                "git", $"rev-list HEAD ^{baseSha} --count") {
                WorkingDirectory       = workspacePath,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return 0;
            var output = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return int.TryParse(output.Trim(), out var n) ? n : 0;
        }
        catch {
            return 0;
        }
    }

    private sealed class TaskState {
        public DateTime? LastRunAt    { get; set; }
        public string?   LastCommitSha { get; set; }
    }
}
