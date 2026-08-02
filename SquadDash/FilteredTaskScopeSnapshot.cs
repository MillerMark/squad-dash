using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SquadDash;

internal sealed record FilteredTaskScopeItem(
    string Identity,
    string TaskLine,
    string? TaskText = null);

internal sealed record FilteredTaskScopeSnapshot(
    string OriginalFilter,
    IReadOnlyList<FilteredTaskScopeItem> Tasks)
{
    private const string Prefix = "SQUADDASH_TASK_SCOPE_V1:";

    internal static FilteredTaskScopeSnapshot Capture(string? originalFilter, IReadOnlyList<TaskItem> tasks)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var captured = tasks.Select(task =>
        {
            var line = task.RawLine.Trim();
            occurrences.TryGetValue(line, out var occurrence);
            occurrence++;
            occurrences[line] = occurrence;
            var identity = !string.IsNullOrWhiteSpace(task.TaskId)
                ? task.TaskId!
                : $"line-{StableHash(line)}-{occurrence}";
            return new FilteredTaskScopeItem(identity, line, task.Text.Trim());
        }).ToArray();
        return new FilteredTaskScopeSnapshot(originalFilter?.Trim() ?? string.Empty, captured);
    }

    internal string Encode() => Prefix + Convert.ToBase64String(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)));

    internal static bool TryDecode(string? value, out FilteredTaskScopeSnapshot? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(value[Prefix.Length..]));
            var parsed = JsonSerializer.Deserialize<FilteredTaskScopeSnapshot>(json);
            if (parsed?.Tasks is not { Count: > 0 } ||
                parsed.Tasks.Any(task => string.IsNullOrWhiteSpace(task.Identity) || string.IsNullOrWhiteSpace(task.TaskLine)))
                return false;
            snapshot = parsed with { Tasks = parsed.Tasks.ToArray() };
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string StableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }
        return hash.ToString("x8");
    }
}
