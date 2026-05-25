using System.Collections.Generic;

namespace SquadDash;

/// <summary>
/// Orchestrates a decompose group lifecycle: validates the dependency DAG via Kahn's algorithm,
/// writes the group to tasks.md, tracks the in-flight step, and marks failed steps on stop.
/// </summary>
internal sealed class MaintenanceGroupRunner
{
    private readonly DecomposedTasksWriter _writer;
    private readonly string               _tasksFilePath;
    private string?                       _currentStepId;

    internal MaintenanceGroupRunner(DecomposedTasksWriter writer, string tasksFilePath)
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
    internal bool TryStartGroup(DecomposedTaskGroup group, out string? inboxErrorJson)
    {
        inboxErrorJson = null;

        if (!TryDetectCycle(group, out var cycleIds))
        {
            _writer.WriteGroupFailed(_tasksFilePath, group);
            var cycleList  = string.Join(", ", cycleIds!);
            inboxErrorJson = BuildCycleInboxText(group.GroupId, cycleList);
            return false;
        }

        _writer.WriteGroup(_tasksFilePath, group);
        return true;
    }

    /// <summary>Records which subtask is currently executing.</summary>
    internal void SetCurrentStep(string taskId) => _currentStepId = taskId;

    /// <summary>
    /// Called when stop_loop is received while decompose mode is active.
    /// If the current step is still <c>[ ]</c> (i.e. the AI did not mark it <c>[x]</c>),
    /// marks it <c>[!]</c> in tasks.md.
    /// </summary>
    internal void OnStopRequested()
    {
        if (_currentStepId is null) return;
        _writer.MarkTaskFailed(_tasksFilePath, _currentStepId);
    }

    /// <summary>Clears step tracking when the loop has fully exited.</summary>
    internal void ClearCurrentStep() => _currentStepId = null;

    // ── Cycle detection (Kahn's algorithm) ─────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when no cycle is detected.
    /// When a cycle exists, <paramref name="cycleIds"/> contains the IDs with non-zero in-degree.
    /// </summary>
    private static bool TryDetectCycle(
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
        "  \"actions\": [\n" +
        "    { \"label\": \"Dismiss\", \"routeMode\": \"done\", \"hint\": \"Acknowledge — no action will be taken\" }\n" +
        "  ]\n" +
        "}\n";
}
