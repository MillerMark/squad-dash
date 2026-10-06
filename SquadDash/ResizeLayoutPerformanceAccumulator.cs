using System;
using System.Diagnostics;

namespace SquadDash;

internal readonly record struct ResizeLayoutPerformanceSnapshot(
    int SizeChangedCount,
    int WidthChangedCount,
    int HeightOnlyCount,
    int LayoutPassCallbackCount,
    int TitleFitCount,
    double TotalTitleFitMs,
    double MaxTitleFitMs,
    double AverageDispatcherDelayMs,
    double MaxDispatcherDelayMs,
    double TotalSizeHandlerMs,
    double MaxSizeHandlerMs,
    double MaxProbeDelayMs,
    double WindowMs);

internal sealed class ResizeLayoutPerformanceAccumulator
{
    private readonly long _reportIntervalTicks;
    private readonly long _idleIntervalTicks;
    private long _windowStartTicks;
    private long _lastActivityTicks;
    private int _sizeChangedCount;
    private int _widthChangedCount;
    private int _heightOnlyCount;
    private int _layoutPassCallbackCount;
    private int _titleFitCount;
    private double _totalTitleFitMs;
    private double _maxTitleFitMs;
    private double _totalDispatcherDelayMs;
    private double _maxDispatcherDelayMs;
    private double _totalSizeHandlerMs;
    private double _maxSizeHandlerMs;
    private double _maxProbeDelayMs;

    internal ResizeLayoutPerformanceAccumulator(
        TimeSpan reportInterval,
        TimeSpan idleInterval)
    {
        _reportIntervalTicks = (long)(reportInterval.TotalSeconds * Stopwatch.Frequency);
        _idleIntervalTicks = (long)(idleInterval.TotalSeconds * Stopwatch.Frequency);
    }

    internal bool HasPendingData =>
        _sizeChangedCount > 0 ||
        _layoutPassCallbackCount > 0 ||
        _titleFitCount > 0;

    internal void RecordSizeChanged(
        double widthDelta,
        double heightDelta,
        long nowTicks,
        double handlerMs = 0)
    {
        EnsureWindowStarted(nowTicks);
        _lastActivityTicks = nowTicks;
        _sizeChangedCount++;
        _totalSizeHandlerMs += handlerMs;
        _maxSizeHandlerMs = Math.Max(_maxSizeHandlerMs, handlerMs);
        if (widthDelta > 0)
            _widthChangedCount++;
        else if (heightDelta > 0)
            _heightOnlyCount++;
    }

    internal void RecordLayoutPassCallback(long nowTicks)
    {
        EnsureWindowStarted(nowTicks);
        // Other windows and animations also raise LayoutUpdated. They must not keep
        // resize tracking alive after the last size change/title fit.
        _layoutPassCallbackCount++;
    }

    internal void RecordTitleFit(
        double elapsedMs,
        double dispatcherDelayMs,
        long nowTicks)
    {
        EnsureWindowStarted(nowTicks);
        _lastActivityTicks = nowTicks;
        _titleFitCount++;
        _totalTitleFitMs += elapsedMs;
        _maxTitleFitMs = Math.Max(_maxTitleFitMs, elapsedMs);
        _totalDispatcherDelayMs += dispatcherDelayMs;
        _maxDispatcherDelayMs = Math.Max(_maxDispatcherDelayMs, dispatcherDelayMs);
    }

    internal bool IsIdle(long nowTicks) =>
        _lastActivityTicks > 0 && nowTicks - _lastActivityTicks >= _idleIntervalTicks;

    internal void RecordProbeDelay(double delayMs) =>
        _maxProbeDelayMs = Math.Max(_maxProbeDelayMs, delayMs);

    internal bool TryCreateSnapshot(
        long nowTicks,
        bool force,
        out ResizeLayoutPerformanceSnapshot snapshot)
    {
        EnsureWindowStarted(nowTicks);
        var elapsedTicks = nowTicks - _windowStartTicks;
        var intervalElapsed = elapsedTicks >= _reportIntervalTicks;
        if (!HasPendingData || (!force && !intervalElapsed))
        {
            snapshot = default;
            return false;
        }

        var avgDispatcherDelay = _titleFitCount > 0
            ? _totalDispatcherDelayMs / _titleFitCount
            : 0;
        snapshot = new ResizeLayoutPerformanceSnapshot(
            _sizeChangedCount,
            _widthChangedCount,
            _heightOnlyCount,
            _layoutPassCallbackCount,
            _titleFitCount,
            _totalTitleFitMs,
            _maxTitleFitMs,
            avgDispatcherDelay,
            _maxDispatcherDelayMs,
            _totalSizeHandlerMs,
            _maxSizeHandlerMs,
            _maxProbeDelayMs,
            StopwatchTicksToMs(elapsedTicks));

        Reset(nowTicks);
        return true;
    }

    private void EnsureWindowStarted(long nowTicks)
    {
        if (_windowStartTicks == 0 || (!HasPendingData && IsIdle(nowTicks)))
            _windowStartTicks = nowTicks;
    }

    private void Reset(long nowTicks)
    {
        _windowStartTicks = nowTicks;
        _sizeChangedCount = 0;
        _widthChangedCount = 0;
        _heightOnlyCount = 0;
        _layoutPassCallbackCount = 0;
        _titleFitCount = 0;
        _totalTitleFitMs = 0;
        _maxTitleFitMs = 0;
        _totalDispatcherDelayMs = 0;
        _maxDispatcherDelayMs = 0;
        _totalSizeHandlerMs = 0;
        _maxSizeHandlerMs = 0;
        _maxProbeDelayMs = 0;
    }

    private static double StopwatchTicksToMs(long ticks) =>
        ticks * 1000.0 / Stopwatch.Frequency;
}
