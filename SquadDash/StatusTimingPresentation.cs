using System;

namespace SquadDash;

internal static class StatusTimingPresentation {
    public static string BuildStatus(
        string status,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt,
        DateTimeOffset now) {
        var normalizedStatus = string.IsNullOrWhiteSpace(status)
            ? completedAt is null ? "Running" : "Completed"
            : status.Trim();

        if (completedAt is { } finishedAt)
            return $"{normalizedStatus} ({FormatAgo(now - finishedAt)})";

        return $"{normalizedStatus} ({FormatElapsed(now - startedAt)})";
    }

    public static string AppendRunningSuffix(
        string label,
        DateTimeOffset startedAt,
        DateTimeOffset now) {
        if (string.IsNullOrWhiteSpace(label))
            return string.Empty;

        return $"{label} ({FormatDuration(now - startedAt)})";
    }

    public static string FormatAgo(TimeSpan elapsed) => $"{FormatCompletedDuration(elapsed)} ago";

    public static string FormatElapsed(TimeSpan elapsed) => FormatDuration(elapsed);

    public static string FormatDuration(TimeSpan elapsed) {
        var clamped = Clamp(elapsed);
        var totalHours = (int)clamped.TotalHours;
        var totalMinutes = (int)clamped.TotalMinutes;
        var totalSeconds = Math.Max(0, (int)clamped.TotalSeconds);

        if (clamped.TotalDays >= 1)
            return $"{(int)clamped.TotalDays}d {clamped.Hours}h {clamped.Minutes}m {clamped.Seconds:00}s";

        if (clamped.TotalHours >= 1)
            return $"{totalHours}h {clamped.Minutes}m {clamped.Seconds:00}s";

        if (clamped.TotalMinutes >= 1)
            return $"{totalMinutes}m {clamped.Seconds:00}s";

        return $"{totalSeconds}s";
    }

    private static string FormatCompletedDuration(TimeSpan elapsed) {
        var clamped = Clamp(elapsed);

        if (clamped.TotalDays >= 1)
            return $"{(int)clamped.TotalDays}d";

        if (clamped.TotalHours >= 1)
            return $"{(int)clamped.TotalHours}h";

        if (clamped.TotalMinutes >= 1)
            return $"{(int)clamped.TotalMinutes}m";

        return $"{Math.Max(0, (int)clamped.TotalSeconds)}s";
    }

    private static TimeSpan Clamp(TimeSpan elapsed) {
        return elapsed < TimeSpan.Zero
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(Math.Floor(elapsed.TotalSeconds));
    }
}
