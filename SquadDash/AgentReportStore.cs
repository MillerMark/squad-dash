using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SquadDash;

/// <summary>
/// Stores and manages per-workspace agent report files.
/// Each completed background agent report is written to
/// {workspace-state-dir}/reports/{sanitized-name}-{timestamp}.md
/// Reports older than <see cref="DefaultMaxAge"/> are pruned automatically.
/// </summary>
internal static class AgentReportStore
{
    internal const string ReportsDirName = "reports";
    internal static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(14);

    /// <summary>
    /// Writes an agent report to disk and returns the file path.
    /// </summary>
    internal static string Store(
        string reportsDir,
        string agentLabel,
        string header,
        string body,
        DateTimeOffset timestamp)
    {
        Directory.CreateDirectory(reportsDir);
        var sanitized = SanitizeForFileName(agentLabel);
        var ts        = timestamp.ToUnixTimeMilliseconds();
        var filePath  = Path.Combine(reportsDir, $"{sanitized}-{ts}.md");

        var sb = new StringBuilder();
        sb.AppendLine($"# {agentLabel}'s Report");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(header))
        {
            sb.AppendLine(header.Trim());
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }
        sb.Append(body.TrimEnd());
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }

    /// <summary>Deletes reports whose file write-time exceeds <paramref name="maxAge"/>.</summary>
    internal static void PruneOld(string reportsDir, TimeSpan? maxAge = null)
    {
        if (!Directory.Exists(reportsDir)) return;
        var cutoff = DateTimeOffset.UtcNow - (maxAge ?? DefaultMaxAge);
        foreach (var file in Directory.EnumerateFiles(reportsDir, "*.md"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime)
                    File.Delete(file);
            }
            catch { /* best effort */ }
        }
    }

    /// <summary>Deletes all report files in the directory (used on /clear).</summary>
    internal static void ClearAll(string reportsDir)
    {
        if (!Directory.Exists(reportsDir)) return;
        foreach (var file in Directory.EnumerateFiles(reportsDir, "*.md"))
        {
            try { File.Delete(file); } catch { }
        }
    }

    internal static string GetReportsDir(string workspaceStateDir) =>
        Path.Combine(workspaceStateDir, ReportsDirName);

    private static string SanitizeForFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars   = name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray();
        var result  = Regex.Replace(new string(chars), @"\s+", "-");
        return result.Length > 40 ? result[..40] : result;
    }
}
