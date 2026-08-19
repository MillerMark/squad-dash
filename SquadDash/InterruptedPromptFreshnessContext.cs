using System;

namespace SquadDash;

internal static class InterruptedPromptFreshnessContext
{
    internal static string BuildInstruction(
        DateTimeOffset stoppedAt,
        DateTimeOffset observedAt,
        string? stoppedHead,
        string? currentHead,
        int? commitsSinceStop)
    {
        var elapsed = observedAt - stoppedAt;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        var repositoryChange = commitsSinceStop is { } count
            ? $"Current repository HEAD is {ShortSha(currentHead)}: {count} commit{(count == 1 ? string.Empty : "s")} have been added since that stopped-run baseline."
            : !string.IsNullOrWhiteSpace(stoppedHead) && !string.IsNullOrWhiteSpace(currentHead)
                ? $"Repository HEAD changed from {ShortSha(stoppedHead)} to {ShortSha(currentHead)}, but the commit distance could not be established."
                : "The repository commit distance from that stopped run is unavailable.";

        return
            "[System note about interrupted-work freshness: A previous run was force-stopped by the user " +
            $"at {stoppedAt.ToUniversalTime():yyyy-MM-dd HH:mm:ss 'UTC'} ({FormatElapsed(elapsed)} ago), " +
            $"when repository HEAD was {ShortSha(stoppedHead)}. {repositoryChange} " +
            "Treat requests, assumptions, and unfinished work from that stopped run as historical and potentially stale context. " +
            "Revalidate them against the current repository and the user's current message. Do not resume or solve the stopped work merely because it remains in conversation history; only do so when the current message clearly asks for it. Newer user directions and repository state take precedence.]";
    }

    private static string ShortSha(string? sha)
    {
        var value = sha?.Trim();
        return string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value[..Math.Min(7, value.Length)];
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalDays >= 1)
            return $"{(int)elapsed.TotalDays}d {elapsed.Hours}h";
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
        if (elapsed.TotalMinutes >= 1)
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
        return $"{Math.Max(0, (int)elapsed.TotalSeconds)}s";
    }
}
