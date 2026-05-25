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

            validIds.Add(task.Id);
        }

        // Validate all dependsOn IDs reference valid siblings.
        foreach (var task in parsed.Tasks)
        {
            if (task.DependsOn is null) continue;
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

        group = parsed;
        return true;
    }
}
