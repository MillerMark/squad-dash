using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SquadDash;

/// <summary>
/// Writes "While You Were Away" maintenance reports to
/// <c>.squad/code-health-reports/YYYYMMDD-HHmmss.md</c>.
/// Auto-prunes to the 30 most recent reports on every write.
/// </summary>
internal sealed class CodeHealthReportWriter {

    private const int MaxReports = 30;
    private readonly string _reportsDir;

    internal CodeHealthReportWriter(string workspacePath) {
        _reportsDir = Path.Combine(workspacePath, ".squad", "code-health-reports");
    }

    /// <summary>Writes a report and returns the file path.</summary>
    public string WriteReport(CodeHealthReport report) {
        Directory.CreateDirectory(_reportsDir);

        var fileName = report.StartedAt.LocalDateTime.ToString("yyyyMMdd-HHmmss") + ".md";
        var filePath = Path.Combine(_reportsDir, fileName);

        var content = BuildReportMarkdown(report);
        File.WriteAllText(filePath, content, Encoding.UTF8);

        Prune();
        return filePath;
    }

    /// <summary>
    /// Writes a stub sidecar JSON file alongside the report at
    /// <paramref name="reportFilePath"/> (same path with <c>.json</c> extension).
    /// </summary>
    public void WriteStubSidecar(string reportFilePath, IReadOnlyList<CodeHealthStubRecord> stubs) {
        var sidecarPath = Path.ChangeExtension(reportFilePath, ".json");
        try {
            var json = JsonSerializer.Serialize(stubs, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sidecarPath, json, Encoding.UTF8);
        }
        catch (Exception ex) {
            SquadDashTrace.Write(TraceCategory.General,
                $"CodeHealthReportWriter: failed to write stub sidecar: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads the stub sidecar at <paramref name="reportFilePath"/> (same path with
    /// <c>.json</c> extension). Returns null if the file does not exist or cannot be parsed.
    /// </summary>
    public IReadOnlyList<CodeHealthStubRecord>? TryReadStubSidecar(string reportFilePath) {
        var sidecarPath = Path.ChangeExtension(reportFilePath, ".json");
        if (!File.Exists(sidecarPath)) return null;
        try {
            var json = File.ReadAllText(sidecarPath);
            return JsonSerializer.Deserialize<List<CodeHealthStubRecord>>(json) ?? [];
        }
        catch { return null; }
    }

    /// <summary>
    /// Returns the path of the most recently written stub sidecar (<c>.json</c>), or null
    /// if none exists in the reports directory.
    /// </summary>
    public string? GetMostRecentSidecarPath() {
        if (!Directory.Exists(_reportsDir)) return null;
        return Directory.GetFiles(_reportsDir, "*.json")
            .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>Returns report file paths sorted newest-first.</summary>
    public IReadOnlyList<string> GetReportPaths() {
        if (!Directory.Exists(_reportsDir))
            return [];

        return Directory.GetFiles(_reportsDir, "*.md")
            .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildReportMarkdown(CodeHealthReport report) {
        var sb = new StringBuilder();

        var dateStr = report.StartedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        sb.AppendLine($"# Code Health Report — {dateStr}");
        sb.AppendLine();

        var duration = FormatDuration(report.Duration);
        sb.AppendLine($"**Session duration:** {duration}");
        sb.AppendLine();

        // Tasks Run section
        sb.AppendLine("## Tasks Run");
        sb.AppendLine();
        if (report.TaskResults.Count == 0) {
            sb.AppendLine("No tasks were run this session.");
        }
        else {
            foreach (var t in report.TaskResults) {
                var icon = t.Outcome switch {
                    CodeHealthTaskOutcome.Completed  => "✅",
                    CodeHealthTaskOutcome.Skipped    => "⏭",
                    CodeHealthTaskOutcome.Error      => "❌",
                    CodeHealthTaskOutcome.Interrupted => "⏸",
                    _                                  => "•",
                };
                var suffix = t.Outcome switch {
                    CodeHealthTaskOutcome.Completed  => $" — {FormatDuration(t.Duration)}",
                    CodeHealthTaskOutcome.Skipped    => " — skipped (already run today)",
                    CodeHealthTaskOutcome.Interrupted => " — interrupted by user activity",
                    CodeHealthTaskOutcome.Error      => " — error during execution",
                    _                                  => "",
                };
                sb.AppendLine($"- {icon} {t.Title} ({t.Id}){suffix}");
                if (!string.IsNullOrWhiteSpace(t.SafetyOverrideNote))
                    sb.AppendLine($"  ⚠ {t.SafetyOverrideNote}");
            }
        }
        sb.AppendLine();

        // Summary
        if (!string.IsNullOrWhiteSpace(report.Summary)) {
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine(report.Summary.Trim());
            sb.AppendLine();
        }

        // Branches Created
        var branches = report.TaskResults
            .Where(t => !string.IsNullOrWhiteSpace(t.BranchCreated))
            .Select(t => t.BranchCreated!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (branches.Count > 0) {
            sb.AppendLine("## Branches Created");
            sb.AppendLine();
            foreach (var b in branches)
                sb.AppendLine($"- {b}");
            sb.AppendLine();
        }

        // Files Changed
        var files = report.TaskResults
            .SelectMany(t => t.FilesChanged ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count > 0) {
            sb.AppendLine("## Files Changed");
            sb.AppendLine();
            foreach (var f in files)
                sb.AppendLine($"- {f}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private void Prune() {
        try {
            var reports = Directory.GetFiles(_reportsDir, "*.md")
                .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var old in reports.Skip(MaxReports)) {
                try { File.Delete(old); }
                catch (Exception ex) {
                    SquadDashTrace.Write(TraceCategory.General,
                        $"CodeHealthReportWriter: failed to prune {old}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) {
            SquadDashTrace.Write(TraceCategory.General,
                $"CodeHealthReportWriter: prune failed: {ex.Message}");
        }
    }

    private static string FormatDuration(TimeSpan ts) {
        if (ts.TotalSeconds < 60)
            return $"{(int)ts.TotalSeconds}s";
        if (ts.TotalSeconds < 3600)
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        return $"{(int)ts.TotalHours}h {ts.Minutes}m";
    }
}


