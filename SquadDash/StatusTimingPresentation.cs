using System;

namespace SquadDash;

internal static class StatusTimingPresentation {
    public static string FormatRelativeTimestamp(DateTimeOffset timestamp) =>
        FormatRelativeTimestamp(timestamp, DateTimeOffset.Now);

    public static string FormatRelativeTimestamp(DateTimeOffset timestamp, DateTimeOffset now) {
        var elapsed = now - timestamp;
        var localTs = timestamp.LocalDateTime;

        if (elapsed.TotalMinutes < 1)
            return $"just now ({localTs:h:mm tt})";

        if (elapsed.TotalHours < 1) {
            var mins = (int)elapsed.TotalMinutes;
            var ago = mins == 1 ? "1 minute ago" : $"{mins} minutes ago";
            return $"{ago} ({localTs:h:mm tt})";
        }

        if (elapsed.TotalHours < 24) {
            var hours = (int)elapsed.TotalHours;
            var mins = (int)(elapsed.TotalMinutes % 60);
            var hourPart = hours == 1 ? "1 hour" : $"{hours} hours";
            var ago = mins == 0 ? $"{hourPart} ago" : $"{hourPart} {mins} minutes ago";
            return $"{ago} ({localTs:h:mm tt})";
        }

        var localNow = now.LocalDateTime;

        if (localNow.Date == localTs.Date.AddDays(1))
            return $"Yesterday at {localTs:h:mm tt} ({localTs:MMM d})";

        if ((localNow.Date - localTs.Date).TotalDays < 7)
            return $"{localTs:dddd} at {localTs:h:mm tt} ({localTs:MMM d})";

        if ((localNow.Date - localTs.Date).TotalDays < 14)
            return $"Last week ({localTs:MMM d})";

        var weeks = (int)((localNow.Date - localTs.Date).TotalDays / 7);
        return weeks == 1 ? $"1 week ago ({localTs:MMM d})" : $"{weeks} weeks ago ({localTs:MMM d})";
    }

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
