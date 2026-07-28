using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SquadDash;

/// <summary>
/// Parses a <c>TASKS_JSON:</c> block from AI response text into a <see cref="DecomposedTaskGroup"/>.
/// Uses the same brace-balanced JSON extraction technique as <see cref="InboxMessageParser"/>.
/// </summary>
internal static class TasksJsonParser
{
    private const string Marker = "TASKS_JSON:";

    private static readonly Regex GroupIdPattern =
        new(@"^[A-Z]+-\d{8}$", RegexOptions.Compiled);

    private static readonly Regex TaskIdPattern =
        new(@"^([A-Z]+-\d{8})-\d{3}$", RegexOptions.Compiled);

    private static readonly Regex AgentHandlePattern =
        new(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Attempts to extract and validate a TASKS_JSON block from <paramref name="text"/>.
    /// Returns <c>true</c> when a valid, well-formed group is found; <c>false</c> otherwise.
    /// Validation errors are written to trace.
    /// </summary>
    internal static bool TryParse(string text, out DecomposedTaskGroup? group)
    {
        group = null;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // Use the last occurrence so that multiple blocks resolve to the final one.
        int markerIdx = normalized.LastIndexOf(Marker, StringComparison.Ordinal);
        if (markerIdx < 0)
            return false;

        int braceStart = normalized.IndexOf('{', markerIdx + Marker.Length);
        if (braceStart < 0)
            return false;

        // Walk brace depth to find the closing brace, ignoring braces inside strings.
        int  depth    = 0;
        int  braceEnd = -1;
        bool inString = false;
        bool escaped  = false;
        for (int i = braceStart; i < normalized.Length; i++)
        {
            char c = normalized[i];
            if (escaped)           { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if      (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) { braceEnd = i; break; }
            }
        }

        if (braceEnd < 0)
            return false;

        var jsonText = normalized[braceStart..(braceEnd + 1)];

        DecomposedTaskGroup? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<DecomposedTaskGroup>(jsonText, ParseOptions);
        }
        catch (JsonException ex)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"TasksJsonParser: JSON parse error — {ex.Message}");
            return false;
        }

        if (parsed is null)
            return false;

        if (string.IsNullOrWhiteSpace(parsed.GroupTitle) ||
            string.IsNullOrWhiteSpace(parsed.Branch) ||
            string.IsNullOrWhiteSpace(parsed.Summary))
        {
            SquadDashTrace.Write(TraceCategory.General,
                "TasksJsonParser: groupTitle, branch, and summary must be non-empty");
            return false;
        }

        // Validate groupId format.
        if (!GroupIdPattern.IsMatch(parsed.GroupId ?? string.Empty))
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"TasksJsonParser: invalid groupId '{parsed.GroupId}' — must match [A-Z]+-\\d{{8}}");
            return false;
        }

        // Validate task count.
        if (parsed.Tasks is null || parsed.Tasks.Count == 0)
        {
            SquadDashTrace.Write(TraceCategory.General,
                "TasksJsonParser: tasks array is null or empty");
            return false;
        }

        if (parsed.Tasks.Count > 25)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"TasksJsonParser: {parsed.Tasks.Count} tasks exceeds maximum of 25");
            return false;
        }

        // Build a set of valid task IDs and validate each ID format.
        var validIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in parsed.Tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Id))
            {
                SquadDashTrace.Write(TraceCategory.General,
                    "TasksJsonParser: a task has a null or empty id");
                return false;
            }

            var m = TaskIdPattern.Match(task.Id);
            if (!m.Success || m.Groups[1].Value != parsed.GroupId)
            {
                SquadDashTrace.Write(TraceCategory.General,
                    $"TasksJsonParser: task id '{task.Id}' does not match {{groupId}}-NNN pattern");
                return false;
            }

            if (!validIds.Add(task.Id))
            {
                SquadDashTrace.Write(TraceCategory.General,
                    $"TasksJsonParser: duplicate task id '{task.Id}'");
                return false;
            }

            if (string.IsNullOrWhiteSpace(task.Title))
            {
                SquadDashTrace.Write(TraceCategory.General,
                    $"TasksJsonParser: task '{task.Id}' has an empty title");
                return false;
            }

            if (string.IsNullOrWhiteSpace(task.Description))
            {
                SquadDashTrace.Write(TraceCategory.General,
                    $"TasksJsonParser: task '{task.Id}' has an empty description");
                return false;
            }

            if (task.AgentAssignments is { Count: > 4 })
            {
                SquadDashTrace.Write(TraceCategory.General,
                    $"TasksJsonParser: task '{task.Id}' has more than four primary agent assignments");
                return false;
            }

            var assignedHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assignment in task.AgentAssignments ?? [])
            {
                var handle = assignment.AgentHandle ?? string.Empty;
                if (!AgentHandlePattern.IsMatch(handle) ||
                    string.IsNullOrWhiteSpace(assignment.Role) ||
                    !assignedHandles.Add(handle))
                {
                    SquadDashTrace.Write(TraceCategory.General,
                        $"TasksJsonParser: task '{task.Id}' has an invalid or duplicate agent assignment");
                    return false;
                }
            }
        }

        // Validate all dependsOn IDs reference valid siblings.
        foreach (var task in parsed.Tasks)
        {
            if (task.DependsOn is not null)
            {
                foreach (var dep in task.DependsOn)
                {
                    if (!validIds.Contains(dep))
                    {
                        SquadDashTrace.Write(TraceCategory.General,
                            $"TasksJsonParser: task '{task.Id}' depends on unknown id '{dep}'");
                        return false;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(task.ParentTaskId) &&
                (!validIds.Contains(task.ParentTaskId) ||
                 string.Equals(task.ParentTaskId, task.Id, StringComparison.Ordinal)))
            {
                SquadDashTrace.Write(TraceCategory.General,
                    $"TasksJsonParser: task '{task.Id}' has invalid parentTaskId '{task.ParentTaskId}'");
                return false;
            }
        }

        // Validate approval gates if present.
        if (parsed.ApprovalGates is { Count: > 0 })
        {
            // Build leaf-task set: tasks that no other task depends on.
            var dependedUpon = new HashSet<string>(StringComparer.Ordinal);
            foreach (var task in parsed.Tasks)
                foreach (var dep in task.DependsOn ?? [])
                    dependedUpon.Add(dep);
            var leafIds = validIds.Where(id => !dependedUpon.Contains(id)).ToHashSet(StringComparer.Ordinal);

            // Build root-task set: tasks with no DependsOn.
            var rootIds = parsed.Tasks
                .Where(t => t.DependsOn is null || t.DependsOn.Count == 0)
                .Select(t => t.Id)
                .ToHashSet(StringComparer.Ordinal);

            var gateIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var gate in parsed.ApprovalGates)
            {
                if (string.IsNullOrWhiteSpace(gate.GateId))
                {
                    SquadDashTrace.Write(TraceCategory.General,
                        "TasksJsonParser: a gate has a null or empty gateId");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(gate.Message))
                {
                    SquadDashTrace.Write(TraceCategory.General,
                        $"TasksJsonParser: gate '{gate.GateId}' has an empty message");
                    return false;
                }

                if (!gateIds.Add(gate.GateId))
                {
                    SquadDashTrace.Write(TraceCategory.General,
                        $"TasksJsonParser: duplicate gate id '{gate.GateId}'");
                    return false;
                }

                foreach (var id in gate.AfterTaskIds ?? [])
                {
                    if (!validIds.Contains(id))
                    {
                        SquadDashTrace.Write(TraceCategory.General,
                            $"TasksJsonParser: gate '{gate.GateId}' afterTaskIds references unknown task '{id}'");
                        return false;
                    }
                }

                foreach (var id in gate.BeforeTaskIds ?? [])
                {
                    if (!validIds.Contains(id))
                    {
                        SquadDashTrace.Write(TraceCategory.General,
                            $"TasksJsonParser: gate '{gate.GateId}' beforeTaskIds references unknown task '{id}'");
                        return false;
                    }
                }

                // Reject before-first-step: AfterTaskIds empty/null AND BeforeTaskIds contain only root tasks.
                var hasAfter  = gate.AfterTaskIds  is { Count: > 0 };
                var hasBefore = gate.BeforeTaskIds is { Count: > 0 };
                if (!hasAfter && hasBefore && gate.BeforeTaskIds!.All(id => rootIds.Contains(id)))
                {
                    SquadDashTrace.Write(TraceCategory.General,
                        $"TasksJsonParser: gate '{gate.GateId}' is a before-first-step gate; use plan-level execution approval instead");
                    return false;
                }

                // Reject after-final-step: BeforeTaskIds empty/null AND AfterTaskIds contain only leaf tasks.
                if (!hasBefore && hasAfter && gate.AfterTaskIds!.All(id => leafIds.Contains(id)))
                {
                    SquadDashTrace.Write(TraceCategory.General,
                        $"TasksJsonParser: gate '{gate.GateId}' is an after-final-step gate; it would never block any task");
                    return false;
                }
            }
        }

        group = parsed;
        return true;
    }
}
