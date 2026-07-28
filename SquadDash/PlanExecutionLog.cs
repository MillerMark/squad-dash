using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

/// <summary>One recorded event in the plan execution log.</summary>
internal sealed record PlanExecutionLogEntry(
    [property: JsonPropertyName("kind")]       string Kind,
    [property: JsonPropertyName("ts")]         string Timestamp,
    [property: JsonPropertyName("planId")]     string? PlanId,
    [property: JsonPropertyName("revision")]   string? Revision,
    [property: JsonPropertyName("round")]      int? Round,
    [property: JsonPropertyName("taskId")]     string? TaskId,
    [property: JsonPropertyName("taskTitle")]  string? TaskTitle,
    [property: JsonPropertyName("message")]    string? Message,
    [property: JsonPropertyName("outcome")]    string? Outcome);

/// <summary>
/// Append-only, workspace-scoped loop execution log.
/// Each plan run appends NDJSON lines to a single per-workspace file.
/// Retention: keep last 500 entries (trim on load).
/// </summary>
internal sealed class PlanExecutionLog
{
    internal const int MaxEntries = 500;
    private readonly string _logPath;

    internal PlanExecutionLog(string workspaceFolderPath)
    {
        var dir = Path.Combine(workspaceFolderPath, ".squad", "logs");
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, "plan-execution.ndjson");
    }

    internal string LogPath => _logPath;

    /// <summary>Appends one entry. NDJSON single-line append is safe for concurrent readers.</summary>
    internal void Append(PlanExecutionLogEntry entry)
    {
        var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
        File.AppendAllText(_logPath, line);
    }

    /// <summary>Loads all entries, trimming to <see cref="MaxEntries"/> most recent.</summary>
    internal IReadOnlyList<PlanExecutionLogEntry> Load()
    {
        if (!File.Exists(_logPath))
            return [];

        var entries = new List<PlanExecutionLogEntry>();
        try
        {
            foreach (var line in File.ReadLines(_logPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<PlanExecutionLogEntry>(line);
                    if (entry is not null)
                        entries.Add(entry);
                }
                catch { /* skip malformed lines */ }
            }
        }
        catch { return []; }

        if (entries.Count > MaxEntries)
        {
            var trimmed = entries.GetRange(entries.Count - MaxEntries, MaxEntries);
            TrimFile(trimmed);
            return trimmed;
        }

        return entries;
    }

    private void TrimFile(List<PlanExecutionLogEntry> entries)
    {
        try
        {
            var lines = entries.Select(e => JsonSerializer.Serialize(e));
            File.WriteAllLines(_logPath, lines);
        }
        catch { /* best-effort trim */ }
    }
}
