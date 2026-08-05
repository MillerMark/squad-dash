using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    internal void WriteGroup(string tasksFilePath, DecomposedTaskGroup group, string? revision = null) =>
        PrependToTasksFile(
            tasksFilePath,
            group.GroupId,
            BuildGroupBlock(group with { HostRevision = revision ?? group.HostRevision }, failed: false));

    /// <summary>
    /// Prepends the group header and all subtasks with <c>[!]</c> failed markers
    /// and a failure note. Used when cycle detection rejects the group before tasks
    /// are ever written to the file.
    /// </summary>
    internal void WriteGroupFailed(string tasksFilePath, DecomposedTaskGroup group) =>
        PrependToTasksFile(tasksFilePath, group.GroupId, BuildGroupBlock(group, failed: true));

    internal bool ReplaceGroup(string tasksFilePath, DecomposedTaskGroup group, string revision)
    {
        if (!File.Exists(tasksFilePath)) return false;
        var lines = File.ReadAllLines(tasksFilePath).ToList();
        var headerPrefix = $"<!-- decompose-group: {group.GroupId} |";
        var start = lines.FindIndex(line => line.TrimStart().StartsWith(headerPrefix, StringComparison.Ordinal));
        if (start < 0) return false;
        var end = start + 1;
        while (end < lines.Count &&
               !lines[end].TrimStart().StartsWith("<!-- decompose-group:", StringComparison.Ordinal) &&
               !lines[end].StartsWith("# ", StringComparison.Ordinal))
            end++;

        var statuses = new Dictionary<string, char>(StringComparer.Ordinal);
        for (var index = start; index < end; index++)
        {
            var trimmed = lines[index].TrimStart();
            if (!trimmed.StartsWith("- [", StringComparison.Ordinal) || trimmed.Length < 5) continue;
            var idStart = trimmed.IndexOf("**[", StringComparison.Ordinal);
            var idEnd = idStart < 0 ? -1 : trimmed.IndexOf("]**", idStart, StringComparison.Ordinal);
            if (idStart >= 0 && idEnd > idStart)
                statuses[trimmed[(idStart + 3)..idEnd]] = trimmed[3];
        }

        foreach (var parent in group.Tasks
                     .Where(task => !string.IsNullOrWhiteSpace(task.ParentTaskId))
                     .GroupBy(task => task.ParentTaskId!, StringComparer.Ordinal))
            statuses[parent.Key] = '>';

        var replacement = BuildGroupBlock(
                group with { HostRevision = revision },
                failed: false,
                statuses)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .ToList();
        if (replacement.Count > 0 && replacement[^1].Length == 0) replacement.RemoveAt(replacement.Count - 1);
        lines.RemoveRange(start, end - start);
        lines.InsertRange(start, replacement);
        WriteAllLinesAtomically(tasksFilePath, lines);
        return true;
    }

    internal bool EnsureGroupRevision(string tasksFilePath, string groupId, string revision)
    {
        if (!File.Exists(tasksFilePath) || string.IsNullOrWhiteSpace(revision)) return false;
        var lines = File.ReadAllLines(tasksFilePath);
        var prefix = $"<!-- decompose-group: {groupId} |";
        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (trimmed.Contains("| revision:", StringComparison.Ordinal)) return true;
            var close = lines[index].LastIndexOf("-->", StringComparison.Ordinal);
            if (close < 0) return false;
            lines[index] = lines[index].Insert(close, $"| revision: {revision} ");
            WriteAllLinesAtomically(tasksFilePath, lines);
            return true;
        }
        return false;
    }

    /// <summary>Adds one host-authored amendment task without rewriting accepted task history.</summary>
    internal bool AppendTaskToGroup(
        string tasksFilePath,
        string groupId,
        string branch,
        DecomposedSubTask task,
        string revision)
    {
        if (!File.Exists(tasksFilePath) || string.IsNullOrWhiteSpace(revision)) return false;
        var lines = File.ReadAllLines(tasksFilePath).ToList();
        if (lines.Any(line => line.Contains($"**[{task.Id}]**", StringComparison.Ordinal)))
            return false;

        var headerPrefix = $"<!-- decompose-group: {groupId} |";
        var start = lines.FindIndex(line => line.TrimStart().StartsWith(headerPrefix, StringComparison.Ordinal));
        if (start < 0) return false;
        var close = lines[start].LastIndexOf("-->", StringComparison.Ordinal);
        if (close < 0) return false;
        var header = lines[start][..close].TrimEnd();
        var revisionIndex = header.IndexOf(" | revision:", StringComparison.Ordinal);
        if (revisionIndex >= 0) header = header[..revisionIndex].TrimEnd();
        lines[start] = $"{header} | revision: {revision} -->";

        var end = start + 1;
        while (end < lines.Count &&
               !lines[end].TrimStart().StartsWith("<!-- decompose-group:", StringComparison.Ordinal) &&
               !lines[end].StartsWith("# ", StringComparison.Ordinal))
            end++;

        var block = BuildTaskBlock(groupId, branch, task).ToList();
        if (end > 0 && !string.IsNullOrWhiteSpace(lines[end - 1])) block.Insert(0, string.Empty);
        lines.InsertRange(end, block);
        WriteAllLinesAtomically(tasksFilePath, lines);
        return true;
    }

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
            WriteAllLinesAtomically(tasksFilePath, lines);
            return;
        }

        // Insert failure note immediately after the task line.
        var result = new List<string>(lines.Length + 1);
        for (int i = 0; i <= foundIdx; i++)
            result.Add(lines[i]);
        result.Add("  (Failed — see inbox for details.)");
        for (int i = foundIdx + 1; i < lines.Length; i++)
            result.Add(lines[i]);

        WriteAllLinesAtomically(tasksFilePath, result);
    }

    internal bool MarkTaskComplete(
        string tasksFilePath,
        string taskId,
        string commit,
        string summary) =>
        SetTaskStatus(tasksFilePath, taskId, 'x', $"Completed by SquadDash — commit {commit}: {summary}");

    internal bool MarkTaskPartial(
        string tasksFilePath,
        string taskId,
        string? commit,
        string summary,
        IReadOnlyList<string> remainingWork)
    {
        var commitText = string.IsNullOrWhiteSpace(commit) ? string.Empty : $" — commit {commit}";
        return SetTaskStatus(
            tasksFilePath,
            taskId,
            '~',
            $"Partial{commitText}: {summary} Remaining: {string.Join("; ", remainingWork)}");
    }

    internal bool ResetTaskPending(string tasksFilePath, string taskId) =>
        SetTaskStatus(tasksFilePath, taskId, ' ', null);

    internal bool MarkTaskSuperseded(
        string tasksFilePath,
        string taskId,
        IReadOnlyList<string> replacementTaskIds) =>
        SetTaskStatus(
            tasksFilePath,
            taskId,
            '>',
            $"Superseded by: {string.Join(", ", replacementTaskIds)}");

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string BuildGroupBlock(
        DecomposedTaskGroup group,
        bool failed,
        IReadOnlyDictionary<string, char>? statuses = null)
    {
        var sb = new StringBuilder();
        var revision = string.IsNullOrWhiteSpace(group.HostRevision)
            ? string.Empty
            : $" | revision: {group.HostRevision}";
        sb.AppendLine(
            $"<!-- decompose-group: {group.GroupId} | branch: {group.Branch}{revision} -->");
        sb.AppendLine($"**[{group.GroupId}] {group.GroupTitle}**");
        sb.AppendLine($"> {group.Summary}");
        sb.AppendLine();

        foreach (var task in group.Tasks)
        {
            var status = failed ? '!' : statuses?.GetValueOrDefault(task.Id, ' ') ?? ' ';
            var marker = $"[{status}]";
            var depsDisplay = task.DependsOn is { Count: > 0 }
                ? string.Join(", ", task.DependsOn)
                : "(none)";

            // New groups keep the concise title on the scannable task line and preserve the
            // complete implementation brief as metadata. The fallback only supports groups
            // created programmatically by older builds; TASKS_JSON requires an explicit title.
            sb.AppendLine($"- {marker} **[{task.Id}]** {task.Title ?? task.Description}");
            sb.AppendLine(
                $"  Group: {group.GroupId} | Branch: {group.Branch} | Priority: {task.Priority}");
            sb.AppendLine($"  description: {task.Description}");
            sb.AppendLine($"  dependsOn: {depsDisplay}");
            if (task.AgentAssignments is { Count: > 0 })
                sb.AppendLine($"  agentAssignments: {System.Text.Json.JsonSerializer.Serialize(task.AgentAssignments)}");
            if (task.ParallelEligible is not null)
                sb.AppendLine($"  parallelEligible: {task.ParallelEligible.Value.ToString().ToLowerInvariant()}");
            if (!string.IsNullOrWhiteSpace(task.AgentRoutingMode))
                sb.AppendLine($"  agentRoutingMode: {task.AgentRoutingMode}");
            if (!string.IsNullOrWhiteSpace(task.GenericAgentReason))
                sb.AppendLine($"  genericAgentReason: {task.GenericAgentReason}");
            if (!string.IsNullOrWhiteSpace(task.ParentTaskId))
                sb.AppendLine($"  parentTaskId: {task.ParentTaskId}");
            if (!string.IsNullOrWhiteSpace(task.AmendmentGateId))
                sb.AppendLine($"  amendmentGateId: {task.AmendmentGateId}");
            if (failed)
                sb.AppendLine("  (Failed — see inbox for details.)");
            else if (status == '>')
            {
                var replacements = group.Tasks
                    .Where(candidate => string.Equals(candidate.ParentTaskId, task.Id, StringComparison.Ordinal))
                    .Select(candidate => candidate.Id);
                sb.AppendLine($"  (SquadDash status: Superseded by: {string.Join(", ", replacements)})");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static IEnumerable<string> BuildTaskBlock(string groupId, string branch, DecomposedSubTask task)
    {
        var depsDisplay = task.DependsOn is { Count: > 0 }
            ? string.Join(", ", task.DependsOn)
            : "(none)";
        yield return $"- [ ] **[{task.Id}]** {task.Title ?? task.Description}";
        yield return $"  Group: {groupId} | Branch: {branch} | Priority: {task.Priority}";
        yield return $"  description: {task.Description}";
        yield return $"  dependsOn: {depsDisplay}";
        if (task.AgentAssignments is { Count: > 0 })
            yield return $"  agentAssignments: {System.Text.Json.JsonSerializer.Serialize(task.AgentAssignments)}";
        if (task.ParallelEligible is not null)
            yield return $"  parallelEligible: {task.ParallelEligible.Value.ToString().ToLowerInvariant()}";
        if (!string.IsNullOrWhiteSpace(task.AgentRoutingMode))
            yield return $"  agentRoutingMode: {task.AgentRoutingMode}";
        if (!string.IsNullOrWhiteSpace(task.GenericAgentReason))
            yield return $"  genericAgentReason: {task.GenericAgentReason}";
        if (!string.IsNullOrWhiteSpace(task.ParentTaskId))
            yield return $"  parentTaskId: {task.ParentTaskId}";
        if (!string.IsNullOrWhiteSpace(task.AmendmentGateId))
            yield return $"  amendmentGateId: {task.AmendmentGateId}";
        yield return string.Empty;
    }

    private static void PrependToTasksFile(string tasksFilePath, string groupId, string content)
    {
        string existing = File.Exists(tasksFilePath)
            ? File.ReadAllText(tasksFilePath)
            : string.Empty;

        if (existing.Contains($"<!-- decompose-group: {groupId} |", StringComparison.Ordinal))
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"DecomposedTasksWriter: group '{groupId}' already exists; duplicate write skipped.");
            return;
        }

        var separator = existing.Length > 0 ? Environment.NewLine : string.Empty;
        var tempPath = tasksFilePath + ".tmp";
        File.WriteAllText(tempPath, content + separator + existing, Encoding.UTF8);
        File.Move(tempPath, tasksFilePath, overwrite: true);
    }

    private static void WriteAllLinesAtomically(string path, IEnumerable<string> lines)
    {
        var tempPath = path + ".tmp";
        File.WriteAllLines(tempPath, lines, Encoding.UTF8);
        File.Move(tempPath, path, overwrite: true);
    }

    private static bool SetTaskStatus(
        string tasksFilePath,
        string taskId,
        char status,
        string? hostNote)
    {
        if (!File.Exists(tasksFilePath)) return false;
        var lines = File.ReadAllLines(tasksFilePath).ToList();
        var marker = $"**[{taskId}]**";
        var index = lines.FindIndex(line =>
            line.TrimStart().StartsWith("- [", StringComparison.Ordinal) &&
            line.Contains(marker, StringComparison.Ordinal));
        if (index < 0) return false;

        var line = lines[index];
        var open = line.IndexOf("- [", StringComparison.Ordinal);
        if (open < 0 || open + 3 >= line.Length) return false;
        lines[index] = line[..(open + 3)] + status + line[(open + 4)..];

        const string notePrefix = "(SquadDash status:";
        var scan = index + 1;
        while (scan < lines.Count &&
               !lines[scan].TrimStart().StartsWith("- [", StringComparison.Ordinal) &&
               !lines[scan].TrimStart().StartsWith("<!-- decompose-group:", StringComparison.Ordinal))
        {
            var trimmed = lines[scan].Trim();
            if (trimmed.StartsWith(notePrefix, StringComparison.Ordinal) ||
                trimmed.Equals("(Failed — see inbox for details.)", StringComparison.Ordinal))
            {
                lines.RemoveAt(scan);
                continue;
            }
            scan++;
        }
        if (!string.IsNullOrWhiteSpace(hostNote))
            lines.Insert(index + 1, $"  {notePrefix} {hostNote})");
        WriteAllLinesAtomically(tasksFilePath, lines);
        return true;
    }
}
