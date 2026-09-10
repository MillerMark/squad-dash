using SquadDash;
namespace SquadDash.Tests;

[TestFixture]
internal sealed class ToolCallElapsedTimePresenterTests
{
    [TestCase(-1, "0 milliseconds")]
    [TestCase(0, "0 milliseconds")]
    [TestCase(1, "1 second")]
    [TestCase(59, "59 seconds")]
    [TestCase(60, "1 minute 0 seconds")]
    [TestCase(61, "1 minute 1 second")]
    [TestCase(3599, "59 minutes 59 seconds")]
    [TestCase(3600, "1 hour 0 minutes 0 seconds")]
    [TestCase(3661, "1 hour 1 minute 1 second")]
    [TestCase(86399, "23 hours 59 minutes 59 seconds")]
    [TestCase(86400, "1 day 0 hours 0 minutes 0 seconds")]
    [TestCase(90061, "1 day 1 hour 1 minute 1 second")]
    public void FormatDuration_UsesHumanReadableUnitsAtThresholds(int elapsedSeconds, string expected)
    {
        var formatted = ToolCallElapsedTimePresenter.FormatDuration(TimeSpan.FromSeconds(elapsedSeconds));

        Assert.That(formatted, Is.EqualTo(expected));
    }

    [TestCase(1, "1 millisecond")]
    [TestCase(2, "2 milliseconds")]
    [TestCase(250, "250 milliseconds")]
    [TestCase(999, "999 milliseconds")]
    public void FormatDuration_UsesMillisecondsBelowOneSecond(int elapsedMilliseconds, string expected)
    {
        var formatted = ToolCallElapsedTimePresenter.FormatDuration(TimeSpan.FromMilliseconds(elapsedMilliseconds));

        Assert.That(formatted, Is.EqualTo(expected));
    }

    [Test]
    public void BuildRunningMessage_UsesRunningPhrasing()
    {
        var startedAt = new DateTimeOffset(2026, 9, 9, 13, 0, 0, TimeSpan.Zero);
        var now = startedAt.AddMinutes(1).AddSeconds(5);

        var message = ToolCallElapsedTimePresenter.BuildRunningMessage(startedAt, now);

        Assert.That(message, Is.EqualTo("Running for 1 minute 5 seconds."));
    }

    [Test]
    public void BuildCompletedMessage_UsesCompletedPhrasing()
    {
        var startedAt = new DateTimeOffset(2026, 9, 9, 13, 0, 0, TimeSpan.Zero);
        var finishedAt = startedAt.AddHours(2).AddMinutes(3).AddSeconds(4);

        var message = ToolCallElapsedTimePresenter.BuildCompletedMessage(startedAt, finishedAt, now: finishedAt);

        Assert.That(message, Is.EqualTo("Ran for 2 hours 3 minutes 4 seconds."));
    }
}
