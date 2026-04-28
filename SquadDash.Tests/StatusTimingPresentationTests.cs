using System;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class StatusTimingPresentationTests {
    [TestCase(13, "13s")]
    [TestCase(301, "5m 01s")]
    [TestCase(11101, "3h 5m 01s")]
    [TestCase(93845, "1d 2h 4m 05s")]
    public void FormatDuration_UsesFullElapsedDisplayForRunningItems(int elapsedSeconds, string expected) {
        var formatted = StatusTimingPresentation.FormatDuration(TimeSpan.FromSeconds(elapsedSeconds));

        Assert.That(formatted, Is.EqualTo(expected));
    }

    [Test]
    public void BuildStatus_ShowsElapsedTimeWhileRunning() {
        var startedAt = new DateTimeOffset(2026, 4, 16, 13, 0, 0, TimeSpan.FromHours(-4));
        var now = startedAt.AddMinutes(5).AddSeconds(1);

        var status = StatusTimingPresentation.BuildStatus("Tooling", startedAt, completedAt: null, now);

        Assert.That(status, Is.EqualTo("Tooling (5m 01s)"));
    }

    [Test]
    public void BuildStatus_ShowsAgeWhenCompleted() {
        var startedAt = new DateTimeOffset(2026, 4, 16, 13, 0, 0, TimeSpan.FromHours(-4));
        var completedAt = startedAt.AddMinutes(2);
        var now = completedAt.AddMinutes(13);

        var status = StatusTimingPresentation.BuildStatus("Completed", startedAt, completedAt, now);

        Assert.That(status, Is.EqualTo("Completed (13m ago)"));
    }

    [Test]
    public void AppendRunningSuffix_AddsElapsedTimeToLabel() {
        var startedAt = new DateTimeOffset(2026, 4, 16, 13, 0, 0, TimeSpan.FromHours(-4));
        var now = startedAt.AddMinutes(1).AddSeconds(7);

        var label = StatusTimingPresentation.AppendRunningSuffix("Lyra Morn", startedAt, now);

        Assert.That(label, Is.EqualTo("Lyra Morn (1m 07s)"));
    }

    [TestCase(13, "13s ago")]
    [TestCase(780, "13m ago")]
    [TestCase(11160, "3h ago")]
    [TestCase(93600, "1d ago")]
    public void FormatAgo_UsesLargestNonZeroUnitForCompletedItems(int elapsedSeconds, string expected) {
        var formatted = StatusTimingPresentation.FormatAgo(TimeSpan.FromSeconds(elapsedSeconds));

        Assert.That(formatted, Is.EqualTo(expected));
    }

    [Test]
    public void FormatRelativeTimestamp_ShowsJustNowWithClockTime() {
        var timestamp = new DateTimeOffset(2026, 4, 28, 14, 33, 0, TimeSpan.FromHours(-4));
        var now = timestamp.AddSeconds(30);

        var formatted = StatusTimingPresentation.FormatRelativeTimestamp(timestamp, now);

        Assert.That(formatted, Is.EqualTo("just now (2:33 PM)"));
    }

    [Test]
    public void FormatRelativeTimestamp_ShowsMinutesAndClockTime() {
        var timestamp = new DateTimeOffset(2026, 4, 28, 14, 30, 0, TimeSpan.FromHours(-4));
        var now = timestamp.AddMinutes(3);

        var formatted = StatusTimingPresentation.FormatRelativeTimestamp(timestamp, now);

        Assert.That(formatted, Is.EqualTo("3 minutes ago (2:30 PM)"));
    }
}
