using System.Text;
using System.Text.Json;
using System.IO;

namespace SquadDash;

/// <summary>
/// Appends the exact host-generated task context, candidate handoff, verification assignment, verdict,
/// and bounded rework instructions to an inspectable plan-only journal outside the Git worktree.
/// The durable Plan remains authoritative; this is diagnostic presentation data.
/// </summary>
internal static class PlanExecutionJournal
{
    private static readonly object WriteGate = new();

    internal static string Append(
        string workspaceStateDirectory,
        string planId,
        string taskId,
        string phase,
        string content,
        DateTimeOffset? timestamp = null)
    {
        var directory = Path.Combine(workspaceStateDirectory, "plan-execution-context");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{planId}.journal.md");
        lock (WriteGate)
        {
            var builder = new StringBuilder();
            if (!File.Exists(path))
            {
                builder.AppendLine($"# Plan execution journal — {planId}");
                builder.AppendLine();
                builder.AppendLine("> Host-generated diagnostic record. Durable plan JSON remains authoritative.");
                builder.AppendLine();
            }
            builder.AppendLine($"## {(timestamp ?? DateTimeOffset.UtcNow):O} · {taskId} · {phase}");
            builder.AppendLine();
            builder.AppendLine("```text");
            builder.AppendLine(content.Replace("```", "` ` `", StringComparison.Ordinal));
            builder.AppendLine("```");
            builder.AppendLine();
            File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
        }
        return path;
    }

    internal static string Serialize(object value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
}
