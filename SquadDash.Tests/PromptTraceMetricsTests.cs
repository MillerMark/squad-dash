using System;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PromptTraceMetricsTests {

    // ── FormatCharsPerSecond ──────────────────────────────────────────────────

    [Test]
    public void FormatCharsPerSecond_ZeroCharCount_ReturnsNotApplicable() {
        var first = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var last  = first.AddSeconds(5);
        Assert.That(PromptTraceMetrics.FormatCharsPerSecond(0, first, last), Is.EqualTo("n/a"));
    }

    [Test]
    public void FormatCharsPerSecond_NegativeCharCount_ReturnsNotApplicable() {
        var first = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var last  = first.AddSeconds(5);
        Assert.That(PromptTraceMetrics.FormatCharsPerSecond(-1, first, last), Is.EqualTo("n/a"));
    }

    [Test]
    public void FormatCharsPerSecond_NullFirstAt_ReturnsNotApplicable() {
        var last = new DateTimeOffset(2026, 1, 1, 12, 0, 5, TimeSpan.Zero);
        Assert.That(PromptTraceMetrics.FormatCharsPerSecond(100, null, last), Is.EqualTo("n/a"));
    }

    [Test]
    public void FormatCharsPerSecond_NullLastAt_ReturnsNotApplicable() {
        var first = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.That(PromptTraceMetrics.FormatCharsPerSecond(100, first, null), Is.EqualTo("n/a"));
    }

    [Test]
    public void FormatCharsPerSecond_BothTimestampsNull_ReturnsNotApplicable() {
        Assert.That(PromptTraceMetrics.FormatCharsPerSecond(100, null, null), Is.EqualTo("n/a"));
    }

    [Test]
    public void FormatCharsPerSecond_SameTimestamps_ZeroDuration_ReturnsNotApplicable() {
        var ts = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.That(PromptTraceMetrics.FormatCharsPerSecond(100, ts, ts), Is.EqualTo("n/a"));
    }

    [Test]
    public void FormatCharsPerSecond_LastBeforeFirst_NegativeDuration_ReturnsNotApplicable() {
        var first = new DateTimeOffset(2026, 1, 1, 12, 0, 10, TimeSpan.Zero);
        var last  = new DateTimeOffset(2026, 1, 1, 12, 0,  0, TimeSpan.Zero);
        Assert.That(PromptTraceMetrics.FormatCharsPerSecond(100, first, last), Is.EqualTo("n/a"));
    }

    [Test]
    public void FormatCharsPerSecond_WholeNumberRate_ReturnsFormattedWithOneDecimal() {
        var first = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var last  = first.AddSeconds(10);
        // 100 chars / 10 s = 10.0
        Assert.That(PromptTraceMetrics.FormatCharsPerSecond(100, first, last), Is.EqualTo("10.0"));
    }

    [Test]
    public void FormatCharsPerSecond_FractionalRate_RoundsToOneDecimalPlace() {
        var first = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var last  = first.AddSeconds(3);
        // 10 chars / 3 s ≈ 3.333… → "3.3"
        Assert.That(PromptTraceMetrics.FormatCharsPerSecond(10, first, last), Is.EqualTo("3.3"));
    }

    [Test]
    public void FormatCharsPerSecond_LargeCharCount_ReturnsFormattedRate() {
        var first = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var last  = first.AddSeconds(1);
        // 10 000 chars / 1 s = 10000.0
        Assert.That(PromptTraceMetrics.FormatCharsPerSecond(10_000, first, last), Is.EqualTo("10000.0"));
    }

    [Test]
    public void FormatCharsPerSecond_OneChar_SubSecondDuration_ReturnsFormattedRate() {
        var first = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var last  = first.AddMilliseconds(500); // 0.5 s
        // 1 / 0.5 = 2.0
        Assert.That(PromptTraceMetrics.FormatCharsPerSecond(1, first, last), Is.EqualTo("2.0"));
    }

    // ── FormatAverageChunkSize ────────────────────────────────────────────────

    [Test]
    public void FormatAverageChunkSize_ZeroCharCount_ReturnsNotApplicable() {
        Assert.That(PromptTraceMetrics.FormatAverageChunkSize(0, 5), Is.EqualTo("n/a"));
    }

    [Test]
    public void FormatAverageChunkSize_ZeroChunkCount_ReturnsNotApplicable() {
        Assert.That(PromptTraceMetrics.FormatAverageChunkSize(100, 0), Is.EqualTo("n/a"));
    }

    [Test]
    public void FormatAverageChunkSize_BothZero_ReturnsNotApplicable() {
        Assert.That(PromptTraceMetrics.FormatAverageChunkSize(0, 0), Is.EqualTo("n/a"));
    }

    [Test]
    public void FormatAverageChunkSize_NegativeCharCount_ReturnsNotApplicable() {
        Assert.That(PromptTraceMetrics.FormatAverageChunkSize(-10, 5), Is.EqualTo("n/a"));
    }

    [Test]
    public void FormatAverageChunkSize_NegativeChunkCount_ReturnsNotApplicable() {
        Assert.That(PromptTraceMetrics.FormatAverageChunkSize(100, -1), Is.EqualTo("n/a"));
    }

    [TestCase(10,        10, "1.0")]
    [TestCase(100,        4, "25.0")]
    [TestCase(10,         3, "3.3")]
    [TestCase(1,          1, "1.0")]
    [TestCase(1_000_000, 100, "10000.0")]
    public void FormatAverageChunkSize_NormalValues_ReturnsFormattedAverage(
        int chars, int chunks, string expected) {
        Assert.That(PromptTraceMetrics.FormatAverageChunkSize(chars, chunks), Is.EqualTo(expected));
    }

    [Test]
    public void FormatAverageChunkSize_SingleCharSingleChunk_ReturnsOnePointZero() {
        Assert.That(PromptTraceMetrics.FormatAverageChunkSize(1, 1), Is.EqualTo("1.0"));
    }
}
