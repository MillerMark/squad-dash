using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace SquadDash;

public partial class MainWindow
{
    private static readonly TimeSpan ResizePerfReportInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ResizePerfIdleInterval = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan MainTitleResizeFitInterval = TimeSpan.FromMilliseconds(90);

    private readonly ResizeLayoutPerformanceAccumulator _resizeLayoutPerformanceAccumulator =
        new(ResizePerfReportInterval, ResizePerfIdleInterval);
    private readonly HashSet<SecondaryTranscriptEntry> _pendingSecondaryTitleFits = new();

    private DispatcherTimer? _resizeLayoutPerformanceFlushTimer;
    private DispatcherTimer? _mainTitleResizeFitTimer;
    private bool _resizeLayoutPassTrackingAttached;
    private bool _mainTitleResizeFitPending;
    private long _mainTitleResizeFitQueuedAt;
    private long _resizeProbeQueuedAt;
    private TranscriptThreadState? _cachedMainTitleThread;
    private string _cachedMainTitleBaseText = string.Empty;
    private string _cachedMainTitleRelativeTime = string.Empty;

    private void OnWindowSizeChangedForResizeTrace(SizeChangedEventArgs e, double handlerMs)
    {
        var nowTicks = Stopwatch.GetTimestamp();
        var widthDelta = Math.Abs(e.NewSize.Width - e.PreviousSize.Width);
        var heightDelta = Math.Abs(e.NewSize.Height - e.PreviousSize.Height);
        _resizeLayoutPerformanceAccumulator.RecordSizeChanged(widthDelta, heightDelta, nowTicks, handlerMs);
        EnsureResizeLayoutPerformanceTracking();
    }

    private void OnTranscriptTitleDockSizeChanged(SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
            return;
        QueueMainTranscriptTitleFit();
    }

    private void TranscriptTitleSpinner_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        QueueMainTranscriptTitleFit();

    private void QueueMainTranscriptTitleFit()
    {
        var activeThread = _selectedTranscriptThread ?? CoordinatorThread;
        if (_isClosing || activeThread.Kind == TranscriptThreadKind.Coordinator)
            return;

        if (!_mainTitleResizeFitPending && _pendingSecondaryTitleFits.Count == 0)
            _mainTitleResizeFitQueuedAt = Stopwatch.GetTimestamp();
        _mainTitleResizeFitPending = true;
        _mainTitleResizeFitTimer ??= CreateMainTitleResizeFitTimer();
        if (!_mainTitleResizeFitTimer.IsEnabled)
            _mainTitleResizeFitTimer.Start();
    }

    private void QueueSecondaryTranscriptTitleFit(SecondaryTranscriptEntry entry)
    {
        if (_isClosing || !_secondaryTranscripts.Contains(entry))
            return;
        if (!_mainTitleResizeFitPending && _pendingSecondaryTitleFits.Count == 0)
            _mainTitleResizeFitQueuedAt = Stopwatch.GetTimestamp();
        _pendingSecondaryTitleFits.Add(entry);
        _mainTitleResizeFitTimer ??= CreateMainTitleResizeFitTimer();
        if (!_mainTitleResizeFitTimer.IsEnabled)
            _mainTitleResizeFitTimer.Start();
    }

    private DispatcherTimer CreateMainTitleResizeFitTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = MainTitleResizeFitInterval
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_mainTitleResizeFitPending && _pendingSecondaryTitleFits.Count == 0)
                return;

            var fitMainTitle = _mainTitleResizeFitPending;
            _mainTitleResizeFitPending = false;
            var secondaryTitles = new List<SecondaryTranscriptEntry>(_pendingSecondaryTitleFits);
            _pendingSecondaryTitleFits.Clear();
            try
            {
                var now = Stopwatch.GetTimestamp();
                var dispatchDelayMs = StopwatchTicksToMs(now - _mainTitleResizeFitQueuedAt);
                var fitSw = Stopwatch.StartNew();
                if (fitMainTitle)
                {
                    var appliedFromCache = TryApplyCachedMainTranscriptTitleFit();
                    if (!appliedFromCache)
                        UpdateTranscriptThreadBadge();
                }
                foreach (var entry in secondaryTitles)
                    ApplyCachedSecondaryTranscriptTitleFit(entry);
                fitSw.Stop();
                _resizeLayoutPerformanceAccumulator.RecordTitleFit(
                    fitSw.Elapsed.TotalMilliseconds,
                    dispatchDelayMs,
                    Stopwatch.GetTimestamp());
                EnsureResizeLayoutPerformanceTracking();
            }
            catch (Exception ex)
            {
                HandleUiCallbackException("TranscriptTitle.ResizeFit", ex);
            }
        };
        return timer;
    }

    private bool TryApplyCachedMainTranscriptTitleFit()
    {
        var activeThread = _selectedTranscriptThread ?? CoordinatorThread;
        if (activeThread.Kind == TranscriptThreadKind.Coordinator || _cachedMainTitleThread is null)
            return false;
        if (!ReferenceEquals(activeThread, _cachedMainTitleThread))
            return false;

        var fullText = string.IsNullOrWhiteSpace(_cachedMainTitleRelativeTime)
            ? _cachedMainTitleBaseText
            : $"{_cachedMainTitleBaseText} - {_cachedMainTitleRelativeTime}";
        var availableWidth = TranscriptTitleDockPanel.ActualWidth
            - PromptNavButtonsPanel.ActualWidth
            - TranscriptTitleTextBlock.Margin.Left
            - TranscriptTitleTextBlock.Margin.Right;
        if (availableWidth <= 0)
        {
            TranscriptTitleTextBlock.Text = _cachedMainTitleBaseText;
            TranscriptTitleTextBlock.TextTrimming = System.Windows.TextTrimming.None;
            return true;
        }

        ApplyMainTranscriptTitleFit(fullText, _cachedMainTitleBaseText, availableWidth);
        return true;
    }

    private void CacheMainTranscriptTitleFitInputs(
        TranscriptThreadState thread,
        string baseText,
        string relativeTime)
    {
        _cachedMainTitleThread = thread;
        _cachedMainTitleBaseText = baseText;
        _cachedMainTitleRelativeTime = relativeTime;
    }

    private void ApplyMainTranscriptTitleFit(
        string fullText,
        string baseText,
        double availableWidth)
    {
        var nominalSize = TranscriptTitleTextBlock.TryFindResource("FontSizeSubtitle") is double d ? d : 13.0;
        var decision = TranscriptTitleFitPolicy.Resolve(
            fullText,
            baseText,
            nominalSize,
            availableWidth,
            // Budget the candidate font's final spinner slot, not its currently animated
            // width. Otherwise font-fit changes resize the spinner and feed back into fit.
            (text, fontSize) => TranscriptTitleSpinnerLayout.MeasureText(TranscriptTitleTextBlock, text, fontSize)
                + (TranscriptTitleSpinner.Visibility == Visibility.Visible
                    ? TranscriptTitleSpinnerLayout.GetExpandedWidth(TranscriptTitleTextBlock, fontSize) : 0));
        TranscriptTitleTextBlock.Text = decision.DisplayText;
        TranscriptTitleTextBlock.FontSize = decision.FontSize;
        TranscriptTitleTextBlock.TextTrimming = decision.Trimming;
    }

    private void EnsureResizeLayoutPerformanceTracking()
    {
        if (_isClosing)
            return;
        if (!_resizeLayoutPassTrackingAttached)
        {
            LayoutUpdated += OnResizeLayoutPassTracked;
            _resizeLayoutPassTrackingAttached = true;
        }

        if (_resizeLayoutPerformanceFlushTimer is null)
        {
            _resizeLayoutPerformanceFlushTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = ResizePerfReportInterval
            };
            _resizeLayoutPerformanceFlushTimer.Tick += (_, _) =>
            {
                var nowTicks = Stopwatch.GetTimestamp();
                _resizeLayoutPerformanceAccumulator.RecordProbeDelay(
                    Math.Max(0, StopwatchTicksToMs(nowTicks - _resizeProbeQueuedAt)
                        - ResizePerfReportInterval.TotalMilliseconds));
                FlushResizeLayoutPerformanceIfDue(force: false, nowTicks);
                _resizeProbeQueuedAt = Stopwatch.GetTimestamp();
                if (_resizeLayoutPerformanceAccumulator.IsIdle(nowTicks))
                {
                    FlushResizeLayoutPerformanceIfDue(force: true, nowTicks);
                    DetachResizeLayoutPassTracking();
                    _resizeLayoutPerformanceFlushTimer?.Stop();
                }
            };
        }

        if (!_resizeLayoutPerformanceFlushTimer.IsEnabled)
        {
            _resizeProbeQueuedAt = Stopwatch.GetTimestamp();
            _resizeLayoutPerformanceFlushTimer.Start();
        }
    }

    private void OnResizeLayoutPassTracked(object? sender, EventArgs e)
    {
        _resizeLayoutPerformanceAccumulator.RecordLayoutPassCallback(Stopwatch.GetTimestamp());
    }

    private void FlushResizeLayoutPerformanceIfDue(bool force, long nowTicks)
    {
        if (!_resizeLayoutPerformanceAccumulator.TryCreateSnapshot(nowTicks, force, out var snapshot))
            return;

        SquadDashTrace.Write(
            TraceCategory.Performance,
            $"RESIZE sizeChanged={snapshot.SizeChangedCount} widthChanged={snapshot.WidthChangedCount} " +
            $"heightOnly={snapshot.HeightOnlyCount} titleFitCount={snapshot.TitleFitCount} " +
            $"sizeHandlerMs={snapshot.TotalSizeHandlerMs:0.0} maxSizeHandlerMs={snapshot.MaxSizeHandlerMs:0.0} " +
            $"titleFitMs={snapshot.TotalTitleFitMs:0.0} maxTitleFitMs={snapshot.MaxTitleFitMs:0.0} " +
            $"titleQueueAvgMs={snapshot.AverageDispatcherDelayMs:0.0} titleQueueMaxMs={snapshot.MaxDispatcherDelayMs:0.0} " +
            $"probeLateMs={snapshot.MaxProbeDelayMs:0.0} " +
            $"windowMs={snapshot.WindowMs:0.0} width={ActualWidth:0.0} height={ActualHeight:0.0}");
        SquadDashTrace.Write(
            TraceCategory.Performance,
            $"LAYOUT_PASS callbacks={snapshot.LayoutPassCallbackCount} windowMs={snapshot.WindowMs:0.0} " +
            "(LayoutUpdated callback count; not CPU time)");
    }

    private void DetachResizeLayoutPassTracking()
    {
        if (!_resizeLayoutPassTrackingAttached)
            return;
        LayoutUpdated -= OnResizeLayoutPassTracked;
        _resizeLayoutPassTrackingAttached = false;
    }

    private void DisposeResizeAndTitlePerfTracking()
    {
        _mainTitleResizeFitPending = false;
        _pendingSecondaryTitleFits.Clear();
        var nowTicks = Stopwatch.GetTimestamp();
        FlushResizeLayoutPerformanceIfDue(force: true, nowTicks);
        DetachResizeLayoutPassTracking();
        if (_resizeLayoutPerformanceFlushTimer is not null)
        {
            _resizeLayoutPerformanceFlushTimer.Stop();
            _resizeLayoutPerformanceFlushTimer = null;
        }

        if (_mainTitleResizeFitTimer is not null)
        {
            _mainTitleResizeFitTimer.Stop();
            _mainTitleResizeFitTimer = null;
        }
    }

    private static double StopwatchTicksToMs(long ticks) =>
        ticks * 1000.0 / Stopwatch.Frequency;
}
