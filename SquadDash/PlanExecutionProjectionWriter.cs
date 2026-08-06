using System.IO;
using System.Text;

namespace SquadDash;

/// <summary>
/// Writes an inspectable Markdown projection of one plan.  The durable Plan JSON remains the
/// authority; this file is regenerated atomically and is never merged back into plan state.
/// </summary>
internal static class PlanExecutionProjectionWriter
{
    internal static string Write(string workspaceStateDirectory, Plan plan)
    {
        // Execution projections are mutable host-owned context. Keep them outside the Git
        // worktree so regenerating one can never dirty or block the plan it describes.
        var directory = Path.Combine(workspaceStateDirectory, "plan-execution-context");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{plan.PlanId}.execution.md");
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, Build(plan));
        File.Move(tempPath, path, overwrite: true);
        return path;
    }

    internal static string Build(Plan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {plan.Title}");
        builder.AppendLine();
        builder.AppendLine($"> Generated execution projection for `{plan.PlanId}` revision `{plan.Revision}`. Do not edit.");
        builder.AppendLine();
        builder.AppendLine("## Guiding intent");
        builder.AppendLine();
        builder.AppendLine(plan.Summary);
        builder.AppendLine();
        builder.AppendLine("## Tasks and accepted handoffs");
        foreach (var task in plan.Tasks)
        {
            builder.AppendLine();
            builder.AppendLine($"### {task.Title ?? task.TaskId}");
            builder.AppendLine();
            builder.AppendLine($"- ID: `{task.TaskId}`");
            builder.AppendLine($"- Status: `{task.Status}`");
            if (task.DependsOn.Count > 0)
                builder.AppendLine($"- Depends on: {string.Join(", ", task.DependsOn.Select(id => $"`{id}`"))}");
            builder.AppendLine($"- Contract: {task.Description}");
            if (task.Handoff is { } handoff)
            {
                builder.AppendLine($"- Handoff commit: `{handoff.Commit}`");
                builder.AppendLine($"- Handoff: {handoff.Summary}");
                if (handoff.ChangedFiles.Count > 0)
                    builder.AppendLine($"- Changed files: {string.Join(", ", handoff.ChangedFiles.Select(file => $"`{file}`"))}");
            }
            if (task.VerificationHistory is { Count: > 0 })
            {
                var latest = task.VerificationHistory[^1];
                builder.AppendLine($"- Latest verification: `{latest.Verdict}` — {latest.Summary}");
            }
        }
        return builder.ToString();
    }
}
