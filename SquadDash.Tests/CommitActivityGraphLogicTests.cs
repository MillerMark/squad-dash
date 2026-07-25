using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class CommitActivityGraphLogicTests
{
    // ── ParseGitLogOutput ──────────────────────────────────────────────────────

    [Test]
    public void ParseGitLogOutput_ValidLine_ReturnsShaAndTime()
    {
        var output = "abc1234 2024-01-15T10:30:00+00:00\n";
        var results = CommitActivityGraphLogic.ParseGitLogOutput(output);
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].sha,  Is.EqualTo("abc1234"));
        Assert.That(results[0].time, Is.EqualTo(new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero)));
    }

    [Test]
    public void ParseGitLogOutput_EmptyOutput_ReturnsEmpty()
    {
        Assert.That(CommitActivityGraphLogic.ParseGitLogOutput(string.Empty), Is.Empty);
    }

    [Test]
    public void ParseGitLogOutput_SixCharSha_Skipped()
    {
        // Minimum required is 7 characters before the space.
        var output = "abc123 2024-01-01T00:00:00+00:00\n";
        Assert.That(CommitActivityGraphLogic.ParseGitLogOutput(output), Is.Empty);
    }

    [Test]
    public void ParseGitLogOutput_ExactlySevenCharSha_Accepted()
    {
        var output = "abc1234 2024-01-01T00:00:00+00:00\n";
        var results = CommitActivityGraphLogic.ParseGitLogOutput(output);
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].sha, Is.EqualTo("abc1234"));
    }

    [Test]
    public void ParseGitLogOutput_MultipleLines_ReturnsAll()
    {
        var output =
            "abc1234 2024-01-01T00:00:00+00:00\n" +
            "def5678 2024-02-01T12:00:00+00:00\n";
        var results = CommitActivityGraphLogic.ParseGitLogOutput(output);
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].sha, Is.EqualTo("abc1234"));
        Assert.That(results[1].sha, Is.EqualTo("def5678"));
    }

    [Test]
    public void ParseGitLogOutput_TimestampWithPositiveOffset_ParsedCorrectly()
    {
        var output = "aabbccd 2024-06-10T08:00:00+05:30\n";
        var results = CommitActivityGraphLogic.ParseGitLogOutput(output);
        Assert.That(results, Has.Count.EqualTo(1));
        var expected = new DateTimeOffset(2024, 6, 10, 8, 0, 0, new TimeSpan(5, 30, 0));
        Assert.That(results[0].time, Is.EqualTo(expected));
    }

    [Test]
    public void ParseGitLogOutput_MalformedTimestamp_LineSkipped()
    {
        var output = "abc1234 not-a-timestamp\n";
        Assert.That(CommitActivityGraphLogic.ParseGitLogOutput(output), Is.Empty);
    }

    // ── FormatDuration ─────────────────────────────────────────────────────────

    [Test]
    public void FormatDuration_LessThanOneMinute_ReturnsSeconds()
    {
        Assert.That(CommitActivityGraphLogic.FormatDuration(TimeSpan.FromSeconds(45)), Is.EqualTo("45s"));
    }

    [Test]
    public void FormatDuration_ExactlyOneMinute_ReturnsMinutes()
    {
        Assert.That(CommitActivityGraphLogic.FormatDuration(TimeSpan.FromMinutes(1)), Is.EqualTo("1m"));
    }

    [Test]
    public void FormatDuration_SeveralMinutes_ReturnsMinutesOnly()
    {
        Assert.That(CommitActivityGraphLogic.FormatDuration(TimeSpan.FromMinutes(5)), Is.EqualTo("5m"));
    }

    [Test]
    public void FormatDuration_ExactlyOneHour_ReturnsOneHourZeroMinutes()
    {
        Assert.That(CommitActivityGraphLogic.FormatDuration(TimeSpan.FromHours(1)), Is.EqualTo("1h 0m"));
    }

    [Test]
    public void FormatDuration_OneHourThirtyMinutes_ReturnsHoursAndMinutes()
    {
        Assert.That(CommitActivityGraphLogic.FormatDuration(TimeSpan.FromMinutes(90)), Is.EqualTo("1h 30m"));
    }

    [Test]
    public void FormatDuration_ZeroTimeSpan_ReturnsZeroSeconds()
    {
        Assert.That(CommitActivityGraphLogic.FormatDuration(TimeSpan.Zero), Is.EqualTo("0s"));
    }

    // ── BuildPlacementKey ──────────────────────────────────────────────────────

    [Test]
    public void BuildPlacementKey_NullPath_ReturnsDefaultKey()
    {
        Assert.That(CommitActivityGraphLogic.BuildPlacementKey(null),
            Is.EqualTo("::CommitHistoryVisualizer"));
    }

    [Test]
    public void BuildPlacementKey_WithPath_EndsWithSuffix()
    {
        var key = CommitActivityGraphLogic.BuildPlacementKey(@"C:\Projects\MyApp");
        Assert.That(key, Does.EndWith("::CommitHistoryVisualizer"));
    }

    [Test]
    public void BuildPlacementKey_PathWithTrailingSeparator_NormalizedToSameAsWithout()
    {
        var keyWith    = CommitActivityGraphLogic.BuildPlacementKey(@"C:\Projects\MyApp\");
        var keyWithout = CommitActivityGraphLogic.BuildPlacementKey(@"C:\Projects\MyApp");
        Assert.That(keyWith, Is.EqualTo(keyWithout));
    }

    [Test]
    public void BuildPlacementKey_PathNeverHasTrailingSeparatorBeforeSuffix()
    {
        var key = CommitActivityGraphLogic.BuildPlacementKey(@"C:\Projects\MyApp\");
        Assert.That(key, Does.Not.Contain(@"\::"));
        Assert.That(key, Does.Not.Contain("//::"));
    }

    // ── DayLabelPriority ───────────────────────────────────────────────────────

    [Test]
    public void DayLabelPriority_Monday_IsOne()
    {
        Assert.That(CommitActivityGraphLogic.DayLabelPriority(DayOfWeek.Monday), Is.EqualTo(1));
    }

    [Test]
    public void DayLabelPriority_Friday_IsTwo()
    {
        Assert.That(CommitActivityGraphLogic.DayLabelPriority(DayOfWeek.Friday), Is.EqualTo(2));
    }

    [Test]
    public void DayLabelPriority_Wednesday_IsThree()
    {
        Assert.That(CommitActivityGraphLogic.DayLabelPriority(DayOfWeek.Wednesday), Is.EqualTo(3));
    }

    [Test]
    public void DayLabelPriority_Monday_HasLowestValueOfAllDays()
    {
        var monPriority = CommitActivityGraphLogic.DayLabelPriority(DayOfWeek.Monday);
        foreach (var dow in Enum.GetValues<DayOfWeek>().Where(d => d != DayOfWeek.Monday))
            Assert.That(monPriority, Is.LessThan(CommitActivityGraphLogic.DayLabelPriority(dow)),
                $"Monday priority should be less than {dow}");
    }

    [Test]
    public void DayLabelPriority_SundayHigherThanSaturday()
    {
        Assert.That(
            CommitActivityGraphLogic.DayLabelPriority(DayOfWeek.Sunday),
            Is.GreaterThan(CommitActivityGraphLogic.DayLabelPriority(DayOfWeek.Saturday)));
    }

    [Test]
    public void DayLabelPriority_AllDaysHaveDistinctValues()
    {
        var values = Enum.GetValues<DayOfWeek>()
            .Select(d => CommitActivityGraphLogic.DayLabelPriority(d))
            .ToList();
        Assert.That(values.Distinct().Count(), Is.EqualTo(values.Count));
    }

    // ── CommitRectHeight ───────────────────────────────────────────────────────

    [Test]
    public void CommitRectHeight_ZeroLines_ReturnsTwoPixels()
    {
        Assert.That(CommitActivityGraphLogic.CommitRectHeight(0, 0), Is.EqualTo(2.0));
    }

    [Test]
    public void CommitRectHeight_ExactlyOneThousandLines_ReturnsMaxHeight()
    {
        double maxHeight = 32.0 * 1.75;
        Assert.That(CommitActivityGraphLogic.CommitRectHeight(1000, 0), Is.EqualTo(maxHeight));
    }

    [Test]
    public void CommitRectHeight_OverOneThousandLines_ReturnsMaxHeight()
    {
        double maxHeight = 32.0 * 1.75;
        Assert.That(CommitActivityGraphLogic.CommitRectHeight(500, 600), Is.EqualTo(maxHeight));
    }

    [Test]
    public void CommitRectHeight_IncreasesMonotonicallyWithLinesChanged()
    {
        var h1   = CommitActivityGraphLogic.CommitRectHeight(1, 0);
        var h10  = CommitActivityGraphLogic.CommitRectHeight(10, 0);
        var h100 = CommitActivityGraphLogic.CommitRectHeight(100, 0);
        var h500 = CommitActivityGraphLogic.CommitRectHeight(500, 0);
        Assert.That(h1,   Is.GreaterThan(2.0));
        Assert.That(h10,  Is.GreaterThan(h1));
        Assert.That(h100, Is.GreaterThan(h10));
        Assert.That(h500, Is.GreaterThan(h100));
    }

    [Test]
    public void CommitRectHeight_InsertionsAndDeletionsBothCount()
    {
        var insertionOnly = CommitActivityGraphLogic.CommitRectHeight(100, 0);
        var combined      = CommitActivityGraphLogic.CommitRectHeight(50, 50);
        Assert.That(insertionOnly, Is.EqualTo(combined).Within(0.001));
    }

    [Test]
    public void CommitRectHeight_CustomRowHeight_ScalesMax()
    {
        const double customRow = 40.0;
        double expectedMax = customRow * 1.75;
        Assert.That(CommitActivityGraphLogic.CommitRectHeight(1000, 0, customRow),
            Is.EqualTo(expectedMax));
    }

    // ── ClampDate ──────────────────────────────────────────────────────────────

    [Test]
    public void ClampDate_BelowMin_ReturnsMin()
    {
        var min = new DateOnly(2024, 1, 1);
        var max = new DateOnly(2024, 12, 31);
        Assert.That(CommitActivityGraphLogic.ClampDate(new DateOnly(2023, 6, 1), min, max), Is.EqualTo(min));
    }

    [Test]
    public void ClampDate_AboveMax_ReturnsMax()
    {
        var min = new DateOnly(2024, 1, 1);
        var max = new DateOnly(2024, 12, 31);
        Assert.That(CommitActivityGraphLogic.ClampDate(new DateOnly(2025, 3, 15), min, max), Is.EqualTo(max));
    }

    [Test]
    public void ClampDate_WithinRange_ReturnsValue()
    {
        var min = new DateOnly(2024, 1, 1);
        var max = new DateOnly(2024, 12, 31);
        var val = new DateOnly(2024, 6, 15);
        Assert.That(CommitActivityGraphLogic.ClampDate(val, min, max), Is.EqualTo(val));
    }

    [Test]
    public void ClampDate_EqualToMin_ReturnsMin()
    {
        var min = new DateOnly(2024, 1, 1);
        var max = new DateOnly(2024, 12, 31);
        Assert.That(CommitActivityGraphLogic.ClampDate(min, min, max), Is.EqualTo(min));
    }

    [Test]
    public void ClampDate_EqualToMax_ReturnsMax()
    {
        var min = new DateOnly(2024, 1, 1);
        var max = new DateOnly(2024, 12, 31);
        Assert.That(CommitActivityGraphLogic.ClampDate(max, min, max), Is.EqualTo(max));
    }

    // ── ClampDayRange ──────────────────────────────────────────────────────────

    [Test]
    public void ClampDayRange_EndBeyondMax_ShiftsWholeWindowLeft()
    {
        int start = 100, end = 150, dayCount = 51;
        CommitActivityGraphLogic.ClampDayRange(ref start, ref end, dayCount, minDay: 0, maxDay: 130);
        Assert.That(end,   Is.EqualTo(130));
        Assert.That(start, Is.EqualTo(130 - dayCount + 1));
    }

    [Test]
    public void ClampDayRange_StartBelowMin_ShiftsWholeWindowRight()
    {
        int start = -5, end = 45, dayCount = 51;
        CommitActivityGraphLogic.ClampDayRange(ref start, ref end, dayCount, minDay: 0, maxDay: 200);
        Assert.That(start, Is.EqualTo(0));
        Assert.That(end,   Is.EqualTo(dayCount - 1));
    }

    [Test]
    public void ClampDayRange_WithinRange_Unchanged()
    {
        int start = 50, end = 100, dayCount = 51;
        CommitActivityGraphLogic.ClampDayRange(ref start, ref end, dayCount, minDay: 0, maxDay: 200);
        Assert.That(start, Is.EqualTo(50));
        Assert.That(end,   Is.EqualTo(100));
    }

    [Test]
    public void ClampDayRange_BothBoundsViolated_ClampedToAvailableTrack()
    {
        // Window wider than the whole allowed track.
        int start = -10, end = 200, dayCount = 211;
        CommitActivityGraphLogic.ClampDayRange(ref start, ref end, dayCount, minDay: 0, maxDay: 100);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end,   Is.LessThanOrEqualTo(100));
    }

    // ── BuildFeatureRows ───────────────────────────────────────────────────────

    [Test]
    public void BuildFeatureRows_EmptyItems_NoWorkspace_ReturnsEmpty()
    {
        var rows = CommitActivityGraphLogic.BuildFeatureRows([], hasWorkspace: false);
        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void BuildFeatureRows_WithWorkspace_AlwaysIncludesUncategorized()
    {
        var rows = CommitActivityGraphLogic.BuildFeatureRows([], hasWorkspace: true);
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].FeatureGroup, Is.Null);
        Assert.That(rows[0].DisplayName,  Is.EqualTo("Uncategorized"));
        Assert.That(rows[0].ColorIndex,   Is.EqualTo(0));
    }

    [Test]
    public void BuildFeatureRows_ItemWithNullGroup_IncludesUncategorized()
    {
        var items = new List<CommitApprovalItem>
        {
            CommitApprovalItem.Create("sha1", null, "desc", DateTimeOffset.Now, null, null)
        };
        var rows = CommitActivityGraphLogic.BuildFeatureRows(items, hasWorkspace: false);
        Assert.That(rows.Any(r => r.FeatureGroup is null), Is.True);
    }

    [Test]
    public void BuildFeatureRows_NamedGroups_AreSortedAlphabetically()
    {
        var now = DateTimeOffset.Now;
        var items = new List<CommitApprovalItem>
        {
            CommitApprovalItem.Create("sha1", null, "d", now, null, null, featureGroup: "Zebra"),
            CommitApprovalItem.Create("sha2", null, "d", now, null, null, featureGroup: "Alpha"),
            CommitApprovalItem.Create("sha3", null, "d", now, null, null, featureGroup: "Mango"),
        };
        var rows = CommitActivityGraphLogic.BuildFeatureRows(items, hasWorkspace: false)
            .Where(r => r.FeatureGroup is not null).ToList();
        Assert.That(rows[0].DisplayName, Is.EqualTo("Alpha"));
        Assert.That(rows[1].DisplayName, Is.EqualTo("Mango"));
        Assert.That(rows[2].DisplayName, Is.EqualTo("Zebra"));
    }

    [Test]
    public void BuildFeatureRows_DuplicateGroupsCaseInsensitive_Deduplicated()
    {
        var now = DateTimeOffset.Now;
        var items = new List<CommitApprovalItem>
        {
            CommitApprovalItem.Create("sha1", null, "d", now, null, null, featureGroup: "Alpha"),
            CommitApprovalItem.Create("sha2", null, "d", now, null, null, featureGroup: "alpha"),
            CommitApprovalItem.Create("sha3", null, "d", now, null, null, featureGroup: "ALPHA"),
        };
        var rows = CommitActivityGraphLogic.BuildFeatureRows(items, hasWorkspace: false)
            .Where(r => r.FeatureGroup is not null).ToList();
        Assert.That(rows, Has.Count.EqualTo(1));
    }

    [Test]
    public void BuildFeatureRows_ColorIndexCyclesOneToSix()
    {
        var now = DateTimeOffset.Now;
        var items = Enumerable.Range(0, 7)
            .Select(i => CommitApprovalItem.Create($"sha{i}", null, "d", now, null, null,
                featureGroup: $"Group{i:D2}"))
            .ToList();
        var rows = CommitActivityGraphLogic.BuildFeatureRows(items, hasWorkspace: false)
            .Where(r => r.FeatureGroup is not null).ToList();
        Assert.That(rows[0].ColorIndex, Is.EqualTo(1));
        Assert.That(rows[5].ColorIndex, Is.EqualTo(6));
        Assert.That(rows[6].ColorIndex, Is.EqualTo(1)); // wraps back
    }

    // ── BuildRequests ──────────────────────────────────────────────────────────

    [Test]
    public void BuildRequests_EmptyList_ReturnsEmpty()
    {
        Assert.That(CommitActivityGraphLogic.BuildRequests([]), Is.Empty);
    }

    [Test]
    public void BuildRequests_DuplicateShasCaseInsensitive_Deduplicated()
    {
        var now = DateTimeOffset.Now;
        var items = new List<CommitApprovalItem>
        {
            CommitApprovalItem.Create("abc123", null, "d", now, null, null),
            CommitApprovalItem.Create("ABC123", null, "d", now.AddMinutes(1), null, null),
        };
        var requests = CommitActivityGraphLogic.BuildRequests(items);
        Assert.That(requests, Has.Count.EqualTo(1));
        Assert.That(requests[0].Sha, Is.EqualTo("abc123").IgnoreCase);
    }

    [Test]
    public void BuildRequests_FeatureGroupPreservedOnRequest()
    {
        var now = DateTimeOffset.Now;
        var items = new List<CommitApprovalItem>
        {
            CommitApprovalItem.Create("sha1", null, "d", now, null, null, featureGroup: "FeatureA"),
        };
        var requests = CommitActivityGraphLogic.BuildRequests(items);
        Assert.That(requests[0].FeatureGroupId, Is.EqualTo("FeatureA"));
    }

    [Test]
    public void BuildRequests_NullFeatureGroup_PreservedAsNull()
    {
        var now = DateTimeOffset.Now;
        var items = new List<CommitApprovalItem>
        {
            CommitApprovalItem.Create("sha1", null, "d", now, null, null),
        };
        Assert.That(CommitActivityGraphLogic.BuildRequests(items)[0].FeatureGroupId, Is.Null);
    }

    [Test]
    public void BuildRequests_TurnDateDerivedFromTurnStartedAt()
    {
        var now = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        var items = new List<CommitApprovalItem>
        {
            CommitApprovalItem.Create("sha1", null, "d", now, null, null),
        };
        var req = CommitActivityGraphLogic.BuildRequests(items)[0];
        Assert.That(req.TurnDate, Is.EqualTo(DateOnly.FromDateTime(now.LocalDateTime)));
    }

    // ── CalculateZoomedDayRange ────────────────────────────────────────────────

    [Test]
    public void CalculateZoomedDayRange_ZoomInFromCenter_NarrowerRange()
    {
        int startDay = 0, endDay = 29;  // 30-day range
        var (newStart, newEnd) = CommitActivityGraphLogic.CalculateZoomedDayRange(
            startDay, endDay, canvasWidth: 600, mouseX: 300, factor: 1.15, absRange: 3650);
        Assert.That(newEnd - newStart + 1, Is.LessThan(endDay - startDay + 1));
    }

    [Test]
    public void CalculateZoomedDayRange_ZoomOut_WiderRange()
    {
        int startDay = 0, endDay = 29;
        var (newStart, newEnd) = CommitActivityGraphLogic.CalculateZoomedDayRange(
            startDay, endDay, canvasWidth: 600, mouseX: 300, factor: 1.0 / 1.15, absRange: 3650);
        Assert.That(newEnd - newStart + 1, Is.GreaterThan(endDay - startDay + 1));
    }

    [Test]
    public void CalculateZoomedDayRange_ZeroCanvasWidth_ReturnsOriginalRange()
    {
        var (newStart, newEnd) = CommitActivityGraphLogic.CalculateZoomedDayRange(
            10, 39, canvasWidth: 0, mouseX: 0, factor: 1.15, absRange: 1000);
        Assert.That(newStart, Is.EqualTo(10));
        Assert.That(newEnd,   Is.EqualTo(39));
    }

    [Test]
    public void CalculateZoomedDayRange_ZeroAbsRange_ReturnsOriginalRange()
    {
        var (newStart, newEnd) = CommitActivityGraphLogic.CalculateZoomedDayRange(
            10, 39, canvasWidth: 600, mouseX: 300, factor: 1.15, absRange: 0);
        Assert.That(newStart, Is.EqualTo(10));
        Assert.That(newEnd,   Is.EqualTo(39));
    }

    [Test]
    public void CalculateZoomedDayRange_AbsRangeCapsZoomOut()
    {
        // Current range: 30 days. absRange: 10. Zooming out should not produce > 10 days.
        var (newStart, newEnd) = CommitActivityGraphLogic.CalculateZoomedDayRange(
            0, 29, canvasWidth: 600, mouseX: 0, factor: 0.5, absRange: 10);
        Assert.That(newEnd - newStart + 1, Is.LessThanOrEqualTo(10));
    }

    [Test]
    public void CalculateZoomedDayRange_MouseAtLeftEdge_DateRangeShiftsLeft()
    {
        // Zooming in with mouse at left edge should anchor the left side more than the right.
        int startDay = 0, endDay = 29;
        var (newStart, newEnd) = CommitActivityGraphLogic.CalculateZoomedDayRange(
            startDay, endDay, canvasWidth: 600, mouseX: 0, factor: 1.5, absRange: 3650);
        // The new start should be at or near 0 (left-anchored)
        Assert.That(newStart, Is.LessThanOrEqualTo(5));
    }

    // ── GetOffHoursSegments ────────────────────────────────────────────────────

    [Test]
    public void GetOffHoursSegments_Saturday_ReturnsFullDaySegment()
    {
        // Saturday is not a work day in default settings.
        var day      = new DateOnly(2024, 6, 1); // Saturday
        var offset   = TimeSpan.Zero;
        var settings = WorkHoursSettings.Default;

        var segments = CommitActivityGraphLogic.GetOffHoursSegments(day, offset, settings).ToList();

        Assert.That(segments, Has.Count.EqualTo(1));
        Assert.That(segments[0].segStart, Is.EqualTo(new DateTimeOffset(2024, 6, 1, 0, 0, 0, offset)));
        Assert.That(segments[0].segEnd,   Is.EqualTo(new DateTimeOffset(2024, 6, 2, 0, 0, 0, offset)));
    }

    [Test]
    public void GetOffHoursSegments_Sunday_ReturnsFullDaySegment()
    {
        var day      = new DateOnly(2024, 6, 2); // Sunday
        var offset   = TimeSpan.Zero;
        var settings = WorkHoursSettings.Default;

        var segments = CommitActivityGraphLogic.GetOffHoursSegments(day, offset, settings).ToList();

        Assert.That(segments, Has.Count.EqualTo(1));
    }

    [Test]
    public void GetOffHoursSegments_Monday_ReturnsTwoSegments()
    {
        var day      = new DateOnly(2024, 6, 3); // Monday
        var offset   = TimeSpan.Zero;
        var settings = WorkHoursSettings.Default; // 9am–5pm

        var segments = CommitActivityGraphLogic.GetOffHoursSegments(day, offset, settings).ToList();

        Assert.That(segments, Has.Count.EqualTo(2));

        // Pre-work: midnight → 9am
        Assert.That(segments[0].segStart, Is.EqualTo(new DateTimeOffset(2024, 6, 3, 0, 0, 0, offset)));
        Assert.That(segments[0].segEnd,   Is.EqualTo(new DateTimeOffset(2024, 6, 3, 9, 0, 0, offset)));

        // Post-work: 5pm → next midnight
        Assert.That(segments[1].segStart, Is.EqualTo(new DateTimeOffset(2024, 6, 3, 17, 0, 0, offset)));
        Assert.That(segments[1].segEnd,   Is.EqualTo(new DateTimeOffset(2024, 6, 4, 0, 0, 0, offset)));
    }

    [Test]
    public void GetOffHoursSegments_CustomWorkHours_RespectsStartAndEndHour()
    {
        var day      = new DateOnly(2024, 6, 3); // Monday
        var offset   = TimeSpan.Zero;
        var settings = new WorkHoursSettings(WorkDayStartHour: 8, WorkDayEndHour: 18);

        var segments = CommitActivityGraphLogic.GetOffHoursSegments(day, offset, settings).ToList();

        Assert.That(segments, Has.Count.EqualTo(2));
        Assert.That(segments[0].segEnd,   Is.EqualTo(new DateTimeOffset(2024, 6, 3, 8, 0, 0, offset)));
        Assert.That(segments[1].segStart, Is.EqualTo(new DateTimeOffset(2024, 6, 3, 18, 0, 0, offset)));
    }

    [Test]
    public void GetOffHoursSegments_NonUtcOffset_SegmentTimesCarryOffset()
    {
        var day      = new DateOnly(2024, 6, 3); // Monday
        var offset   = TimeSpan.FromHours(5);    // UTC+5
        var settings = WorkHoursSettings.Default;

        var segments = CommitActivityGraphLogic.GetOffHoursSegments(day, offset, settings).ToList();

        Assert.That(segments, Has.Count.EqualTo(2));
        Assert.That(segments[0].segStart.Offset, Is.EqualTo(offset));
        Assert.That(segments[0].segEnd.Offset,   Is.EqualTo(offset));
        Assert.That(segments[1].segStart.Offset, Is.EqualTo(offset));
        Assert.That(segments[1].segEnd.Offset,   Is.EqualTo(offset));
    }

    [Test]
    public void GetOffHoursSegments_SaturdaySetAsWorkDay_ReturnsTwoSegments()
    {
        var day      = new DateOnly(2024, 6, 1); // Saturday
        var offset   = TimeSpan.Zero;
        var settings = new WorkHoursSettings(SaturdayWork: true, WorkDayStartHour: 9, WorkDayEndHour: 13);

        var segments = CommitActivityGraphLogic.GetOffHoursSegments(day, offset, settings).ToList();

        Assert.That(segments, Has.Count.EqualTo(2));
        Assert.That(segments[0].segEnd,   Is.EqualTo(new DateTimeOffset(2024, 6, 1, 9, 0, 0, offset)));
        Assert.That(segments[1].segStart, Is.EqualTo(new DateTimeOffset(2024, 6, 1, 13, 0, 0, offset)));
    }
}
