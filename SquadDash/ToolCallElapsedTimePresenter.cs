using System;

namespace SquadDash;

internal static class ToolCallElapsedTimePresenter
{
    public static string BuildRunningMessage(DateTimeOffset startedAt, DateTimeOffset now)
    {
        var elapsed = now - startedAt;
        return $"This tool has been running for {FormatDuration(elapsed)}.";
    }

    public static string BuildCompletedMessage(DateTimeOffset startedAt, DateTimeOffset? finishedAt, DateTimeOffset now)
    {
        var completedAt = finishedAt ?? now;
        var elapsed = completedAt - startedAt;
        return $"A tool ran for {FormatDuration(elapsed)}.";
    }

    public static string FormatDuration(TimeSpan elapsed)
    {
        var clamped = Clamp(elapsed);
        var totalSeconds = (long)clamped.TotalSeconds;

        if (totalSeconds < 60)
            return BuildUnit(totalSeconds, "second");

        var days = totalSeconds / 86_400;
        var remainingAfterDays = totalSeconds % 86_400;
        var hours = remainingAfterDays / 3_600;
        var remainingAfterHours = remainingAfterDays % 3_600;
        var minutes = remainingAfterHours / 60;
        var seconds = remainingAfterHours % 60;

        if (totalSeconds < 3_600)
            return $"{BuildUnit(minutes, "minute")} {BuildUnit(seconds, "second")}";

        if (totalSeconds < 86_400)
            return $"{BuildUnit(hours, "hour")} {BuildUnit(minutes, "minute")} {BuildUnit(seconds, "second")}";

        return $"{BuildUnit(days, "day")} {BuildUnit(hours, "hour")} {BuildUnit(minutes, "minute")} {BuildUnit(seconds, "second")}";
    }

    private static string BuildUnit(long value, string unit)
    {
        return value == 1
            ? $"1 {unit}"
            : $"{value} {unit}s";
    }

    private static TimeSpan Clamp(TimeSpan elapsed)
    {
        return elapsed < TimeSpan.Zero
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(Math.Floor(elapsed.TotalSeconds));
    }
}
