using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SquadDash;

internal enum DecomposeGroupExecutionState
{
    Eligible,
    Complete,
    Blocked,
    Missing,
    Unreadable,
}

/// <summary>
/// Orchestrates a decompose group lifecycle: validates the dependency DAG via Kahn's algorithm,
/// writes the group to tasks.md, tracks the in-flight step, and marks failed steps on stop.
/// </summary>
internal sealed class CodeHealthGroupRunner
{
    private readonly DecomposedTasksWriter _writer;
    private readonly string               _tasksFilePath;
    private string?                       _currentStepId;
    private string?                       _currentRevision;

    internal string? CurrentStepId => _currentStepId;
    internal string? CurrentRevision => _currentRevision;

    internal CodeHealthGroupRunner(DecomposedTasksWriter writer, string tasksFilePath)
    {
        _writer        = writer;
        _tasksFilePath = tasksFilePath;
    }

    /// <summary>
    /// Runs Kahn's cycle detection on the group's dependency DAG.
    /// <para>
    /// On success: writes the group (with <c>[ ]</c> markers) to tasks.md and returns <c>true</c>.
    /// </para>
    /// <para>
    /// On cycle: writes all tasks with <c>[!]</c> markers, populates <paramref name="inboxErrorJson"/>
    /// with a pre-formatted INBOX_MESSAGE_JSON string (ready for <c>TrySaveInboxMessageFromResponse</c>),
    /// and returns <c>false</c>.
    /// </para>
    /// </summary>
    internal bool TryStartGroup(
        DecomposedTaskGroup group,
        out string? inboxErrorJson,
        string? revision = null)
    {
        inboxErrorJson = null;

        if (!HasNoDependencyCycle(group, out var cycleIds))
        {
            _writer.WriteGroupFailed(_tasksFilePath, group);
            var cycleList  = string.Join(", ", cycleIds!);
            inboxErrorJson = BuildCycleInboxText(group.GroupId, cycleList);
            return false;
        }

        _writer.WriteGroup(_tasksFilePath, group, revision);
        return true;
    }

    /// <summary>Records which subtask is currently executing.</summary>
    internal void SetCurrentStep(string taskId) => _currentStepId = taskId;

    /// <summary>
    /// Reads the persisted group state and tracks the first dependency-eligible task.
    /// The loop prompt uses the same selection rule, so an interrupted iteration can
    /// reliably mark the task it was expected to execute as failed.
    /// </summary>
    internal DecomposeGroupExecutionState TrackFirstEligibleStep(string groupId)
    {
        _currentStepId = null;
        _currentRevision = null;
        try
        {
            if (!File.Exists(_tasksFilePath))
                return DecomposeGroupExecutionState.Missing;
            var parsed = TasksPanelParser.Parse(File.ReadAllLines(_tasksFilePath));
            var items = parsed.OpenGroups
                .SelectMany(group => group.Items)
                .Concat(parsed.CompletedItems)
                .Where(item => string.Equals(item.DecomposeGroupId, groupId, StringComparison.Ordinal))
                .ToList();
            if (items.Count == 0)
                return DecomposeGroupExecutionState.Missing;

            if (parsed.DecomposeGroups.TryGetValue(groupId, out var persistedGroup))
                _currentRevision = persistedGroup.HostRevision ?? PendingDecomposePlanStore.ComputeRevision(persistedGroup);

            var completedIds = items
                .Where(item => (item.IsChecked || item.IsSuperseded) && item.TaskId is not null)
                .Select(item => item.TaskId!)
                .ToHashSet(StringComparer.Ordinal);
            _currentStepId = items
                .Where(item => !item.IsChecked && !item.IsFailed && !item.IsPartial &&
                               !item.IsSuperseded && item.TaskId is not null)
                .FirstOrDefault(item => item.DependsOn is null || item.DependsOn.All(completedIds.Contains))
                ?.TaskId;
            if (_currentStepId is not null)
                return DecomposeGroupExecutionState.Eligible;
            if (items.All(item => item.IsChecked || item.IsSuperseded))
                return DecomposeGroupExecutionState.Complete;
            return DecomposeGroupExecutionState.Blocked;
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"CodeHealthGroupRunner: could not determine eligible step for '{groupId}': {ex.Message}");
            return DecomposeGroupExecutionState.Unreadable;
        }
    }

    /// <summary>
    /// Called when stop_loop is received while decompose mode is active.
    /// If the current step is still <c>[ ]</c> (i.e. the AI did not mark it <c>[x]</c>),
    /// marks it <c>[!]</c> in tasks.md.
    /// </summary>
    internal void MarkCurrentStepFailed()
    {
        if (_currentStepId is null) return;
        _writer.MarkTaskFailed(_tasksFilePath, _currentStepId);
    }

    internal bool ApplyStepResult(DecomposeStepResult result, out string? error)
    {
        error = null;
        if (_currentStepId is null || _currentRevision is null)
        {
            error = "SquadDash has no tracked plan step for this result.";
            return false;
        }
        if (!string.Equals(result.TaskId, _currentStepId, StringComparison.Ordinal))
        {
            error = $"The result was for {result.TaskId}, but SquadDash assigned {_currentStepId}.";
            return false;
        }
        if (!string.Equals(result.Revision, _currentRevision, StringComparison.Ordinal))
        {
            error = $"The result used stale revision {result.Revision}; expected {_currentRevision}.";
            return false;
        }

        var applied = result.Status switch
        {
            "complete" => _writer.MarkTaskComplete(
                _tasksFilePath, result.TaskId, result.Commit!, result.Summary),
            "partial" => _writer.MarkTaskPartial(
                _tasksFilePath,
                result.TaskId,
                result.Commit,
                result.Summary,
                result.RemainingWork ?? []),
            "failed" => MarkFailed(result.TaskId),
            _ => false,
        };
        if (!applied)
            error = $"SquadDash could not update task {result.TaskId} in tasks.md.";
        return applied;

        bool MarkFailed(string taskId)
        {
            _writer.MarkTaskFailed(_tasksFilePath, taskId);
            return File.ReadAllText(_tasksFilePath)
                .Contains($"- [!] **[{taskId}]**", StringComparison.Ordinal);
        }
    }

    internal bool ResetCurrentStep() =>
        _currentStepId is not null && _writer.ResetTaskPending(_tasksFilePath, _currentStepId);

    /// <summary>Clears step tracking when the loop has fully exited.</summary>
    internal void ClearCurrentStep()
    {
        _currentStepId = null;
        _currentRevision = null;
    }

    // ── Cycle detection (Kahn's algorithm) ─────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when no cycle is detected.
    /// When a cycle exists, <paramref name="cycleIds"/> contains the IDs with non-zero in-degree.
    /// </summary>
    internal static bool HasNoDependencyCycle(
        DecomposedTaskGroup     group,
        out List<string>?       cycleIds)
    {
        cycleIds = null;

        var inDegree  = new Dictionary<string, int>(StringComparer.Ordinal);
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var task in group.Tasks)
        {
            inDegree[task.Id]  = 0;
            adjacency[task.Id] = new List<string>();
        }

        foreach (var task in group.Tasks)
        {
            if (task.DependsOn is null) continue;
            foreach (var dep in task.DependsOn)
            {
                if (!adjacency.ContainsKey(dep)) continue;
                adjacency[dep].Add(task.Id);
                inDegree[task.Id]++;
            }
        }

        var queue = new Queue<string>();
        foreach (var kvp in inDegree)
            if (kvp.Value == 0)
                queue.Enqueue(kvp.Key);

        int processed = 0;
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            processed++;
            foreach (var neighbor in adjacency[node])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        if (processed == group.Tasks.Count)
            return true;

        cycleIds = new List<string>();
        foreach (var kvp in inDegree)
            if (kvp.Value > 0)
                cycleIds.Add(kvp.Key);

        return false;
    }

    private static string BuildCycleInboxText(string groupId, string cycleIds) =>
        "INBOX_MESSAGE_JSON:\n" +
        "{\n" +
        $"  \"subject\": \"Decompose group {groupId} — dependency cycle detected\",\n" +
        "  \"from\": \"argus-weld\",\n" +
        $"  \"body\": \"## Dependency Cycle Detected\\n\\nThe decompose group `{groupId}` contains a dependency cycle involving task IDs: {cycleIds}.\\n\\nAll tasks have been marked as failed in `.squad/tasks.md`. Please correct the `dependsOn` references and retry.\",\n" +
        "  \"attachments\": [],\n" +
        "  \"actions\": []\n" +
        "}\n";
}

