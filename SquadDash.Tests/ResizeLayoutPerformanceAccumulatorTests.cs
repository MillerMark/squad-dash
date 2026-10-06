using System;
using System.Diagnostics;
using System.Threading;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ResizeLayoutPerformanceAccumulatorTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void TryCreateSnapshot_HoldsUntilReportIntervalThenResets()
    {
        var accumulator = new ResizeLayoutPerformanceAccumulator(
            reportInterval: TimeSpan.FromMilliseconds(500),
            idleInterval: TimeSpan.FromMilliseconds(700));
        var start = Stopwatch.GetTimestamp();
        accumulator.RecordSizeChanged(widthDelta: 24, heightDelta: 0, start, handlerMs: 2.5);
        accumulator.RecordProbeDelay(15);
        accumulator.RecordLayoutPassCallback(start);
        accumulator.RecordTitleFit(elapsedMs: 4.2, dispatcherDelayMs: 11.5, start);

        var beforeThreshold = start + MsToTicks(490);
        Assert.That(
            accumulator.TryCreateSnapshot(beforeThreshold, force: false, out _),
            Is.False);

        var atThreshold = start + MsToTicks(510);
        Assert.That(
            accumulator.TryCreateSnapshot(atThreshold, force: false, out var snapshot),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.SizeChangedCount, Is.EqualTo(1));
            Assert.That(snapshot.WidthChangedCount, Is.EqualTo(1));
            Assert.That(snapshot.LayoutPassCallbackCount, Is.EqualTo(1));
            Assert.That(snapshot.TitleFitCount, Is.EqualTo(1));
            Assert.That(snapshot.TotalTitleFitMs, Is.EqualTo(4.2).Within(0.001));
            Assert.That(snapshot.AverageDispatcherDelayMs, Is.EqualTo(11.5).Within(0.001));
            Assert.That(snapshot.TotalSizeHandlerMs, Is.EqualTo(2.5));
            Assert.That(snapshot.MaxSizeHandlerMs, Is.EqualTo(2.5));
            Assert.That(snapshot.MaxProbeDelayMs, Is.EqualTo(15));
            Assert.That(snapshot.WindowMs, Is.GreaterThanOrEqualTo(500));
        });

        Assert.That(
            accumulator.TryCreateSnapshot(atThreshold + MsToTicks(600), force: false, out _),
            Is.False);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void TryCreateSnapshot_ForceFlushesPendingDataBeforeInterval()
    {
        var accumulator = new ResizeLayoutPerformanceAccumulator(
            reportInterval: TimeSpan.FromMilliseconds(500),
            idleInterval: TimeSpan.FromMilliseconds(700));
        var start = Stopwatch.GetTimestamp();
        accumulator.RecordSizeChanged(widthDelta: 0, heightDelta: 32, start);

        Assert.That(
            accumulator.TryCreateSnapshot(start + MsToTicks(30), force: true, out var snapshot),
            Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.SizeChangedCount, Is.EqualTo(1));
            Assert.That(snapshot.WidthChangedCount, Is.EqualTo(0));
            Assert.That(snapshot.HeightOnlyCount, Is.EqualTo(1));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void IsIdle_UsesConfiguredIdleInterval()
    {
        var accumulator = new ResizeLayoutPerformanceAccumulator(
            reportInterval: TimeSpan.FromMilliseconds(500),
            idleInterval: TimeSpan.FromMilliseconds(700));
        var start = Stopwatch.GetTimestamp();
        accumulator.RecordSizeChanged(widthDelta: 5, heightDelta: 0, start);

        Assert.That(accumulator.IsIdle(start + MsToTicks(650)), Is.False);
        Assert.That(accumulator.IsIdle(start + MsToTicks(710)), Is.True);
    }

    [Test]
    public void UnrelatedLayoutCallbacks_DoNotKeepResizeTrackingAlive()
    {
        var accumulator = new ResizeLayoutPerformanceAccumulator(
            TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(700));
        var start = Stopwatch.GetTimestamp();
        accumulator.RecordSizeChanged(5, 0, start);
        for (var ms = 50; ms <= 700; ms += 50)
            accumulator.RecordLayoutPassCallback(start + MsToTicks(ms));
        Assert.That(accumulator.IsIdle(start + MsToTicks(710)), Is.True);
    }

    [Test]
    public void NewResizeAfterIdle_DoesNotIncludeIdleTimeInReportWindow()
    {
        var accumulator = new ResizeLayoutPerformanceAccumulator(
            TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(700));
        var start = Stopwatch.GetTimestamp();
        accumulator.RecordSizeChanged(5, 0, start);
        accumulator.TryCreateSnapshot(start + MsToTicks(500), false, out _);
        accumulator.RecordSizeChanged(5, 0, start + MsToTicks(10000));
        Assert.That(accumulator.TryCreateSnapshot(start + MsToTicks(10100), false, out _), Is.False);
        Assert.That(accumulator.TryCreateSnapshot(start + MsToTicks(10500), false, out var report), Is.True);
        Assert.That(report.WindowMs, Is.EqualTo(500).Within(0.01));
    }

    private static long MsToTicks(double milliseconds) =>
        (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000.0);
}
