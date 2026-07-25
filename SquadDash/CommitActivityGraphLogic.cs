using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Pure-logic helpers for the commit activity graph visualizer.
/// All methods are stateless and have no WPF, I/O, or threading dependencies —
/// they can be called from unit tests without a running WPF application.
/// Used by <see cref="CommitActivityGraphWindow"/> and <see cref="CommitActivityCanvas"/>.
/// </summary>
internal static class CommitActivityGraphLogic
{
    // ── Git log parsing ────────────────────────────────────────────────────────

    /// <summary>
    /// Parses output of <c>git log --format="%h %aI"</c>.
    /// Each line is an abbreviated SHA (≥ 7 chars) followed by a space and an ISO 8601 timestamp.
    /// Lines whose SHA portion is shorter than 7 characters are skipped.
    /// </summary>
    internal static List<(string sha, DateTimeOffset time)> ParseGitLogOutput(string output)
    {
        var results = new List<(string, DateTimeOffset)>();
        foreach (var line in output.AsSpan().EnumerateLines())
        {
            var s = line.ToString().Trim();
            var spaceIdx = s.IndexOf(' ');
            if (spaceIdx < 7) continue;
            var sha  = s[..spaceIdx];
            var rest = s[(spaceIdx + 1)..].Trim();
            if (DateTimeOffset.TryParse(rest, out var time))
                results.Add((sha, time));
        }
        return results;
    }

    // ── Duration formatting ────────────────────────────────────────────────────

    /// <summary>
    /// Formats a <see cref="TimeSpan"/> as a human-readable duration string:
    /// "Xh Ym" when ≥ 1 hour, "Xm" when ≥ 1 minute, "Xs" otherwise.
    /// </summary>
    internal static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1)
            return $"{(int)ts.TotalMinutes}m";
        return $"{(int)ts.TotalSeconds}s";
    }

    // ── Window placement key ───────────────────────────────────────────────────

    /// <summary>
    /// Builds the unique key used to persist window placement in
    /// <see cref="ApplicationSettingsStore"/>.
    /// Returns <c>"::CommitHistoryVisualizer"</c> when <paramref name="workspaceFolderPath"/>
    /// is <c>null</c>; otherwise prefixes with the normalized, trailing-separator-stripped path.
    /// </summary>
    internal static string BuildPlacementKey(string? workspaceFolderPath)
    {
        if (workspaceFolderPath is null) return "::CommitHistoryVisualizer";
        var normalized = Path.GetFullPath(workspaceFolderPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalized + "::CommitHistoryVisualizer";
    }

    // ── Day label priority ─────────────────────────────────────────────────────

    /// <summary>
    /// Priority order for day-of-week axis labels: Monday first (1), then Friday (2),
    /// Wednesday (3), Tuesday (4), Thursday (5), Saturday (6), Sunday (7).
    /// Lower value = higher priority = drawn first when space is limited.
    /// Ensures key business-week anchors survive thinning at low zoom.
    /// </summary>
    internal static int DayLabelPriority(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday    => 1,
        DayOfWeek.Friday    => 2,
        DayOfWeek.Wednesday => 3,
        DayOfWeek.Tuesday   => 4,
        DayOfWeek.Thursday  => 5,
        DayOfWeek.Saturday  => 6,
        DayOfWeek.Sunday    => 7,
        _                   => 8,
    };

    // ── Commit bar height ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the rendered height (in pixels) for a commit bar, scaled logarithmically
    /// by total lines changed.
    /// <list type="bullet">
    ///   <item>0 lines → 2 px</item>
    ///   <item>1 line  → ≥ 3 px (minimum for real commits)</item>
    ///   <item>≥ 1000 lines → <paramref name="rowHeight"/> × 1.75 (max)</item>
    /// </list>
    /// Bars may extend beyond the row boundary, which is intentional.
    /// </summary>
    internal static double CommitRectHeight(int insertions, int deletions, double rowHeight = 32.0)
    {
        const double ZeroHeight = 2.0;
        const double MinHeight  = 3.0;
        double maxHeight = rowHeight * 1.75;
        var totalLines = insertions + deletions;
        if (totalLines <= 0)    return ZeroHeight;
        if (totalLines >= 1000) return maxHeight;
        var t = Math.Log(totalLines + 1.0) / Math.Log(1001.0);
        return MinHeight + (maxHeight - MinHeight) * t;
    }

    // ── DateOnly clamping ──────────────────────────────────────────────────────

    /// <summary>
    /// Clamps <paramref name="value"/> to the inclusive range [<paramref name="min"/>, <paramref name="max"/>].
    /// </summary>
    internal static DateOnly ClampDate(DateOnly value, DateOnly min, DateOnly max)
        => value.DayNumber < min.DayNumber ? min
         : value.DayNumber > max.DayNumber ? max
         : value;

    // ── Day-number range clamping ──────────────────────────────────────────────

    /// <summary>
    /// Clamps an integer day-number range to [<paramref name="minDay"/>, <paramref name="maxDay"/>],
    /// preserving <paramref name="dayCount"/> by shifting the window rather than shrinking it.
    /// Both bounds are also individually clamped so neither escapes the allowed range even when
    /// the window is wider than the available track.
    /// </summary>
    internal static void ClampDayRange(
        ref int startDay, ref int endDay, int dayCount, int minDay, int maxDay)
    {
        if (endDay   > maxDay) { endDay   = maxDay;   startDay = endDay   - dayCount + 1; }
        if (startDay < minDay) { startDay = minDay;   endDay   = startDay + dayCount - 1; }
        startDay = Math.Max(startDay, minDay);
        endDay   = Math.Min(endDay,   maxDay);
    }

    // ── Feature row construction ───────────────────────────────────────────────

    /// <summary>
    /// Builds the ordered list of <see cref="CommitActivityRow"/> objects from
    /// <paramref name="items"/>:
    /// <list type="number">
    ///   <item>An "Uncategorized" row (FeatureGroup = null) when <paramref name="hasWorkspace"/>
    ///   is <c>true</c> or any item has a null FeatureGroup.</item>
    ///   <item>One row per distinct FeatureGroup, sorted alphabetically (case-insensitive),
    ///   with color indices cycling 1–6.</item>
    /// </list>
    /// </summary>
    internal static List<CommitActivityRow> BuildFeatureRows(
        List<CommitApprovalItem> items,
        bool                     hasWorkspace = false)
    {
        var rows = new List<CommitActivityRow>();

        var hasUncategorized = hasWorkspace || items.Any(i => i.FeatureGroup is null);
        if (hasUncategorized)
            rows.Add(new CommitActivityRow(null, "Uncategorized", 0));

        var named = items
            .Where(i => i.FeatureGroup is not null)
            .Select(i => i.FeatureGroup!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int i = 0; i < named.Count; i++)
            rows.Add(new CommitActivityRow(named[i], named[i], (i % 6) + 1));

        return rows;
    }

    // ── Commit request construction ────────────────────────────────────────────

    /// <summary>
    /// Builds one <see cref="CommitStatRequest"/> per distinct SHA in
    /// <paramref name="items"/> (case-insensitive deduplication).
    /// The first item in each SHA group provides the metadata.
    /// </summary>
    internal static List<CommitStatRequest> BuildRequests(List<CommitApprovalItem> items)
        => items
            .GroupBy(i => i.CommitSha, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new CommitStatRequest(
                    first.CommitSha,
                    first.FeatureGroup,
                    DateOnly.FromDateTime(first.TurnStartedAt.LocalDateTime),
                    TurnStartedAt: first.TurnStartedAt);
            })
            .ToList();

    // ── Zoom range calculation ─────────────────────────────────────────────────

    /// <summary>
    /// Computes the new (startDay, endDay) day-number pair after applying a horizontal zoom.
    /// The date under the mouse drifts 20% toward the viewport center per step.
    /// <para>
    /// The result is <b>not</b> clamped to [minDay, maxDay] — callers must apply
    /// <see cref="ClampDayRange"/> after this call.
    /// </para>
    /// </summary>
    /// <param name="startDay">Current start day number (DayNumber property of DateOnly).</param>
    /// <param name="endDay">Current end day number.</param>
    /// <param name="canvasWidth">Graph area width in pixels (excluding the label column).</param>
    /// <param name="mouseX">Mouse X position relative to the graph area (excluding the label column).
    /// Will be clamped internally to [0, <paramref name="canvasWidth"/>].</param>
    /// <param name="factor">Zoom factor. &gt;1 = zoom in (fewer days), &lt;1 = zoom out.</param>
    /// <param name="absRange">Maximum allowed day count (constrains zoom-out ceiling).</param>
    internal static (int newStartDay, int newEndDay) CalculateZoomedDayRange(
        int startDay, int endDay, double canvasWidth, double mouseX, double factor, int absRange)
    {
        if (canvasWidth <= 0 || absRange <= 0) return (startDay, endDay);

        int dayCount = Math.Max(1, endDay - startDay + 1);
        var ppd         = canvasWidth / dayCount;
        var clampedMouseX = Math.Clamp(mouseX, 0, canvasWidth);

        double mouseDateFrac  = startDay + clampedMouseX / ppd;
        double centerDateFrac = startDay + (canvasWidth / 2.0) / ppd;

        double newDayCount = Math.Clamp(dayCount / factor, 1, absRange);

        // Drift the date under the mouse 20% toward the viewport center.
        double newMouseDateFrac = centerDateFrac + (mouseDateFrac - centerDateFrac) * 0.8;

        double newPPD       = canvasWidth / newDayCount;
        double newStartFrac = newMouseDateFrac - clampedMouseX / newPPD;

        int newStartDay = (int)Math.Round(newStartFrac);
        int newEndDay   = newStartDay + (int)Math.Round(newDayCount) - 1;

        return (newStartDay, newEndDay);
    }

    // ── Off-hours segment computation ──────────────────────────────────────────

    /// <summary>
    /// Returns the off-hours time segments for a single calendar <paramref name="day"/>,
    /// as <c>(segStart, segEnd)</c> pairs expressed in the given UTC <paramref name="offset"/>:
    /// <list type="bullet">
    ///   <item>Non-work days: one segment covering midnight → next midnight (the full day).</item>
    ///   <item>Work days: two segments — midnight → WorkDayStartHour, and WorkDayEndHour → next midnight.</item>
    /// </list>
    /// </summary>
    internal static IEnumerable<(DateTimeOffset segStart, DateTimeOffset segEnd)> GetOffHoursSegments(
        DateOnly          day,
        TimeSpan          offset,
        WorkHoursSettings workHours)
    {
        var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue),            offset);
        var dayEnd   = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), offset);

        if (!workHours.IsWorkDay(day.DayOfWeek))
        {
            yield return (dayStart, dayEnd);
        }
        else
        {
            var workStart = new DateTimeOffset(
                day.ToDateTime(new TimeOnly(workHours.WorkDayStartHour, 0)), offset);
            var workEnd = new DateTimeOffset(
                day.ToDateTime(new TimeOnly(workHours.WorkDayEndHour, 0)), offset);
            yield return (dayStart, workStart);
            yield return (workEnd, dayEnd);
        }
    }
}
