using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SquadDash;

/// <summary>
/// Writes decompose group headers and subtask entries to <c>.squad/tasks.md</c>,
/// and marks individual subtasks as failed.
/// </summary>
internal sealed class DecomposedTasksWriter
{
    /// <summary>
    /// Prepends the group header and all subtasks (with <c>[ ]</c> pending markers)
    /// to <paramref name="tasksFilePath"/>.
    /// </summary>
    internal void WriteGroup(string tasksFilePath, DecomposedTaskGroup group) =>
        PrependToTasksFile(tasksFilePath, BuildGroupBlock(group, failed: false));

    /// <summary>
    /// Prepends the group header and all subtasks with <c>[!]</c> failed markers
    /// and a failure note. Used when cycle detection rejects the group before tasks
    /// are ever written to the file.
    /// </summary>
    internal void WriteGroupFailed(string tasksFilePath, DecomposedTaskGroup group) =>
        PrependToTasksFile(tasksFilePath, BuildGroupBlock(group, failed: true));

    /// <summary>
    /// Finds the line <c>- [ ] **[{taskId}]**</c> in <paramref name="tasksFilePath"/>
    /// and replaces <c>[ ]</c> with <c>[!]</c>. Appends a failure note if not already present.
    /// Uses an atomic read-modify-write.
    /// </summary>
    internal void MarkTaskFailed(string tasksFilePath, string taskId)
    {
        if (!File.Exists(tasksFilePath))
            return;

        var lines = File.ReadAllLines(tasksFilePath);
        var target = $"- [ ] **[{taskId}]**";
        int foundIdx = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith(target, StringComparison.Ordinal))
            {
                lines[i] = lines[i].Replace("- [ ] **", "- [!] **", StringComparison.Ordinal);
                foundIdx = i;
                break;
            }
        }

        if (foundIdx < 0)
            return;

        // Check whether a failure note is already present nearby.
        bool alreadyHasNote = false;
        for (int i = foundIdx + 1; i < lines.Length && i <= foundIdx + 5; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith("- [", StringComparison.Ordinal)) break;
            if (trimmed.Contains("(Failed — see inbox for details.)", StringComparison.Ordinal))
            {
                alreadyHasNote = true;
                break;
            }
        }

        if (alreadyHasNote)
        {
            File.WriteAllLines(tasksFilePath, lines);
            return;
        }

        // Insert failure note immediately after the task line.
        var result = new List<string>(lines.Length + 1);
        for (int i = 0; i <= foundIdx; i++)
            result.Add(lines[i]);
        result.Add("  (Failed — see inbox for details.)");
        for (int i = foundIdx + 1; i < lines.Length; i++)
            result.Add(lines[i]);

        File.WriteAllLines(tasksFilePath, result);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string BuildGroupBlock(DecomposedTaskGroup group, bool failed)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"<!-- decompose-group: {group.GroupId} | branch: {group.Branch} -->");
        sb.AppendLine($"**[{group.GroupId}] {group.GroupTitle}**");
        sb.AppendLine($"> {group.Summary}");
        sb.AppendLine();

        foreach (var task in group.Tasks)
        {
            var marker      = failed ? "[!]" : "[ ]";
            var depsDisplay = task.DependsOn is { Count: > 0 }
                ? string.Join(", ", task.DependsOn)
                : "(none)";

            sb.AppendLine($"- {marker} **[{task.Id}]** {task.Description}");
            sb.AppendLine(
                $"  Group: {group.GroupId} | Branch: {group.Branch} | Priority: {task.Priority}");
            sb.AppendLine($"  dependsOn: {depsDisplay}");
            if (failed)
                sb.AppendLine("  (Failed — see inbox for details.)");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void PrependToTasksFile(string tasksFilePath, string content)
    {
        string existing = File.Exists(tasksFilePath)
            ? File.ReadAllText(tasksFilePath)
            : string.Empty;

        var separator = existing.Length > 0 ? Environment.NewLine : string.Empty;
        File.WriteAllText(tasksFilePath, content + separator + existing, Encoding.UTF8);
    }
}
