using System;
using System.Text;
using System.Text.Json;

namespace SquadDash;

internal sealed record QueuedLoopJob(
    ActiveLoopExecutionState Execution,
    string DisplayLabel,
    int TaskCount)
{
    private const string Prefix = "SQUADDASH_QUEUED_LOOP_V1:";

    internal string Encode() => Prefix + Convert.ToBase64String(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)));

    internal static bool TryDecode(string? value, out QueuedLoopJob? job)
    {
        job = null;
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(value[Prefix.Length..]));
            var parsed = JsonSerializer.Deserialize<QueuedLoopJob>(json);
            if (parsed?.Execution is null ||
                ActiveLoopExecutionState.Normalize(parsed.Execution) is not { } normalized ||
                string.IsNullOrWhiteSpace(parsed.DisplayLabel) || parsed.TaskCount <= 0)
                return false;
            job = parsed with { Execution = normalized, DisplayLabel = parsed.DisplayLabel.Trim() };
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
