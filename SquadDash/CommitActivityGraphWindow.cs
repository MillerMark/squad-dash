using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// Floating ChromedWindow that renders a horizontal Gantt-style commit activity graph
/// grouped by feature group.  Opens via View → Commit History.
/// </summary>
internal sealed class CommitActivityGraphWindow : ChromedWindow
{
    // ── Color palette ─────────────────────────────────────────────────────────
    internal static readonly Color[] DarkPalette =
    [
        Color.FromRgb(0xFF, 0x6B, 0x6B), // 0 Uncategorized
        Color.FromRgb(0x4E, 0xCD, 0xC4), // 1
        Color.FromRgb(0xFF, 0xD9, 0x3D), // 2
        Color.FromRgb(0xA2, 0x9B, 0xFE), // 3
        Color.FromRgb(0x6B, 0xCB, 0x77), // 4
        Color.FromRgb(0xFF, 0xA0, 0x7A), // 5
        Color.FromRgb(0x74, 0xB9, 0xFF), // 6
    ];

    internal static readonly Color[] LightPalette =
    [
        Color.FromRgb(0xC0, 0x39, 0x2B), // 0 Uncategorized
        Color.FromRgb(0x14, 0x8A, 0x82), // 1
        Color.FromRgb(0xB8, 0x86, 0x0B), // 2
        Color.FromRgb(0x5E, 0x35, 0xB1), // 3
        Color.FromRgb(0x2E, 0x7D, 0x32), // 4
        Color.FromRgb(0xBF, 0x57, 0x22), // 5
        Color.FromRgb(0x15, 0x65, 0xC0), // 6
    ];

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly ICommitStatService          _statService;
    private readonly List<CommitApprovalItem>    _allItems;
    private bool                                 _isDark;
    private int                                  _visibleDays = 30;
    private CancellationTokenSource?             _cts;
    private readonly DispatcherTimer             _debounceTimer;

    // ── UI ────────────────────────────────────────────────────────────────────
    private readonly CommitActivityCanvas _canvas;
    private readonly TextBlock            _dateRangeLabel;

    public CommitActivityGraphWindow(
        ICommitStatService              statService,
        IEnumerable<CommitApprovalItem> items,
        bool                            isDark)
        : base(captionHeight: ChromedWindow.CloseButtonHeight)
    {
        _statService = statService ?? throw new ArgumentNullException(nameof(statService));
        _allItems    = items.ToList();
        _isDark      = isDark;

        Title         = "Commit History";
        Width         = 900;
        Height        = 600;
        MinWidth      = 500;
        MinHeight     = 300;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost       = false;

        _debounceTimer          = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounceTimer.Tick    += (_, _) => { _debounceTimer.Stop(); StartLoadingData(); };

        // ── Canvas / scroll area ──────────────────────────────────────────────
        _canvas = new CommitActivityCanvas();
        ToolTipService.SetInitialShowDelay(_canvas, 400);
        ToolTipService.SetShowDuration(_canvas, 12_000);

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            Content                       = _canvas,
        };
        scrollViewer.SetResourceReference(ScrollViewer.BackgroundProperty, "AppSurface");

        // ── Slider ────────────────────────────────────────────────────────────
        var slider = new Slider
        {
            Minimum       = 30,
            Maximum       = 365,
            Value         = 30,
            LargeChange   = 30,
            SmallChange   = 1,
            TickFrequency = 30,
            TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
            Margin        = new Thickness(10, 6, 10, 4),
        };
        WindowChrome.SetIsHitTestVisibleInChrome(slider, true);
        slider.ValueChanged += (_, _) => OnSliderValueChanged((int)slider.Value);

        _dateRangeLabel = new TextBlock { Margin = new Thickness(10, 0, 10, 6), FontSize = 11 };
        _dateRangeLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        UpdateDateRangeLabel();

        var sliderPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        sliderPanel.Children.Add(slider);
        sliderPanel.Children.Add(_dateRangeLabel);

        // ── Main layout ───────────────────────────────────────────────────────
        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(sliderPanel, Dock.Top);
        layout.Children.Add(sliderPanel);
        layout.Children.Add(scrollViewer);

        var contentBorder   = ApplyOuterBorder(titleText: "Commit History");
        contentBorder.Child = layout;

        Loaded += (_, _) => StartLoadingData();
        Closed += (_, _) => { _cts?.Cancel(); _debounceTimer.Stop(); };
    }

    // ── Theme ─────────────────────────────────────────────────────────────────

    public void NotifyThemeChanged(bool isDark)
    {
        _isDark = isDark;
        _canvas.SetTheme(isDark);
    }

    // ── Slider ────────────────────────────────────────────────────────────────

    private void OnSliderValueChanged(int days)
    {
        _visibleDays = days;
        UpdateDateRangeLabel();
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void UpdateDateRangeLabel()
    {
        var end   = DateTime.Today;
        var start = end.AddDays(-(_visibleDays - 1));
        _dateRangeLabel.Text = $"{start:MMM d, yyyy}  –  {end:MMM d, yyyy}  ({_visibleDays} days)";
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private void StartLoadingData()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = LoadDataAsync(_cts.Token);
    }

    private async Task LoadDataAsync(CancellationToken ct)
    {
        var endDate   = DateOnly.FromDateTime(DateTime.Today);
        var startDate = endDate.AddDays(-(_visibleDays - 1));

        var filteredItems = _allItems
            .Where(i =>
            {
                var d = DateOnly.FromDateTime(i.TurnStartedAt.LocalDateTime);
                return d >= startDate && d <= endDate;
            })
            .ToList();

        var rows     = BuildFeatureRows(filteredItems);
        var requests = BuildRequests(filteredItems);

        var pendingShas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var req in requests)
        {
            if (_statService.TryGetCached(req.Sha) is null)
                pendingShas.Add(req.Sha);
        }

        RefreshCanvasData(rows, requests, pendingShas, startDate, endDate);

        if (requests.Count == 0) return;

        var progress = new Progress<IReadOnlyList<CommitStatResult>>(batch =>
        {
            if (ct.IsCancellationRequested) return;
            foreach (var r in batch)
                pendingShas.Remove(r.Sha);
            RefreshCanvasData(rows, requests, pendingShas, startDate, endDate);
        });

        try
        {
            await _statService.GetStatsAsync(requests, progress, ct);
        }
        catch (OperationCanceledException) { /* expected */ }
    }

    private void RefreshCanvasData(
        List<CommitActivityRow>   rows,
        List<CommitStatRequest>   requests,
        HashSet<string>           pendingShas,
        DateOnly                  startDate,
        DateOnly                  endDate)
    {
        foreach (var row in rows)
        {
            row.CommitsByDay.Clear();
            row.PendingDays.Clear();
        }

        var rowByGroup = rows.ToDictionary(
            r => r.FeatureGroup ?? "",
            r => r,
            StringComparer.Ordinal);

        foreach (var req in requests)
        {
            var key = req.FeatureGroupId ?? "";
            if (!rowByGroup.TryGetValue(key, out var row)) continue;

            if (pendingShas.Contains(req.Sha))
            {
                row.PendingDays.Add(req.TurnDate);
                continue;
            }

            var result = _statService.TryGetCached(req.Sha);
            if (result is null || !result.IsFound) continue;

            if (!row.CommitsByDay.TryGetValue(req.TurnDate, out var list))
                row.CommitsByDay[req.TurnDate] = list = [];

            if (!list.Any(r => string.Equals(r.Sha, result.Sha, StringComparison.OrdinalIgnoreCase)))
                list.Add(result);
        }

        _canvas.SetData(rows, startDate, endDate, _isDark);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<CommitActivityRow> BuildFeatureRows(List<CommitApprovalItem> items)
    {
        var rows = new List<CommitActivityRow>();

        var hasUncategorized = items.Any(i => i.FeatureGroup is null);
        if (hasUncategorized)
            rows.Add(new CommitActivityRow(null, "Uncategorized", 0));

        var named = items
            .Where(i => i.FeatureGroup is not null)
            .Select(i => i.FeatureGroup!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int i = 0; i < named.Count; i++)
            rows.Add(new CommitActivityRow(named[i], named[i], (i + 1) % 7));

        return rows;
    }

    private static List<CommitStatRequest> BuildRequests(List<CommitApprovalItem> items)
        => items
            .GroupBy(i => i.CommitSha, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new CommitStatRequest(
                    first.CommitSha,
                    first.FeatureGroup,
                    DateOnly.FromDateTime(first.TurnStartedAt.LocalDateTime));
            })
            .ToList();
}

// ─────────────────────────────────────────────────────────────────────────────
// Row model
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One row in the commit activity graph — one feature group (or Uncategorized).</summary>
internal sealed class CommitActivityRow
{
    public string?                                       FeatureGroup  { get; }
    public string                                        DisplayName   { get; }
    public int                                           ColorIndex    { get; }
    public Dictionary<DateOnly, List<CommitStatResult>> CommitsByDay  { get; } = new();
    public HashSet<DateOnly>                             PendingDays   { get; } = new();

    public CommitActivityRow(string? featureGroup, string displayName, int colorIndex)
    {
        FeatureGroup = featureGroup;
        DisplayName  = displayName;
        ColorIndex   = colorIndex;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Hit-test result types
// ─────────────────────────────────────────────────────────────────────────────

internal sealed record CommitDotHit(
    CommitActivityRow           Row,
    DateOnly                    Date,
    bool                        IsPending,
    List<CommitStatResult>?     Commits);

internal sealed record CommitLineHit(
    CommitActivityRow Row,
    DateOnly          FirstDate,
    DateOnly          LastDate);

// ─────────────────────────────────────────────────────────────────────────────
// Canvas renderer
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Custom <see cref="FrameworkElement"/> that renders the commit activity graph
/// (feature-name column, connecting lines, dots, and x-axis date labels) via
/// <see cref="DrawingContext"/>.
/// </summary>
internal sealed class CommitActivityCanvas : FrameworkElement
{
    // ── Layout ─────────────────────────────────────────────────────────────────
    internal const double LabelColumnWidth = 160;
    internal const double RowHeight        = 32;
    internal const double XAxisHeight      = 24;
    internal const double PixelsPerDay     = 20;
    internal const double BaseRadius       = 5.0;

    // ── State ──────────────────────────────────────────────────────────────────
    private List<CommitActivityRow> _rows      = [];
    private DateOnly                _startDate;
    private DateOnly                _endDate;
    private bool                    _isDark;
    private int                     _dayCount;
    private double                  _pixelsPerDip = 1.0;

    // Tooltip hit tracking
    private object? _lastHit;

    // ── Public API ─────────────────────────────────────────────────────────────

    public void SetData(
        List<CommitActivityRow> rows,
        DateOnly                startDate,
        DateOnly                endDate,
        bool                    isDark)
    {
        _rows      = rows;
        _startDate = startDate;
        _endDate   = endDate;
        _isDark    = isDark;
        _dayCount  = Math.Max(1, endDate.DayNumber - startDate.DayNumber + 1);

        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetTheme(bool isDark)
    {
        _isDark = isDark;
        InvalidateVisual();
    }

    // ── Measure ────────────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_dayCount == 0) return new Size(400, 100);
        var w = LabelColumnWidth + _dayCount * PixelsPerDay;
        var h = Math.Max(1, _rows.Count) * RowHeight + XAxisHeight;
        return new Size(w, h);
    }

    // ── DPI ────────────────────────────────────────────────────────────────────

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _pixelsPerDip = newDpi.PixelsPerDip;
        InvalidateVisual();
    }

    // ── Rendering ──────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        if (_dayCount == 0) return;

        var palette    = _isDark ? CommitActivityGraphWindow.DarkPalette : CommitActivityGraphWindow.LightPalette;
        var textBrush  = TryFindBrush("LabelText")  ?? Brushes.Black;
        var subtleBrush = TryFindBrush("SubtleText") ?? Brushes.Gray;
        var borderBrush = TryFindBrush("PanelBorder") ?? Brushes.LightGray;

        // Background
        dc.DrawRectangle(
            TryFindBrush("AppSurface") ?? Brushes.Transparent,
            null,
            new Rect(0, 0, Math.Max(ActualWidth, DesiredSize.Width), Math.Max(ActualHeight, DesiredSize.Height)));

        // Vertical separator between label column and graph area
        dc.DrawLine(
            new Pen(borderBrush, 1),
            new Point(LabelColumnWidth - 0.5, 0),
            new Point(LabelColumnWidth - 0.5, _rows.Count * RowHeight));

        for (int i = 0; i < _rows.Count; i++)
        {
            var row   = _rows[i];
            var cy    = i * RowHeight + RowHeight / 2.0;
            var color = palette[row.ColorIndex % 7];

            // Row separator (subtle dashed line, skip first row)
            if (i > 0)
            {
                var sepPen = new Pen(borderBrush, 0.5) { DashStyle = DashStyles.Dot };
                dc.DrawLine(sepPen, new Point(0, i * RowHeight), new Point(DesiredSize.Width, i * RowHeight));
            }

            // ── Feature label ─────────────────────────────────────────────────
            var labelFt = MakeText(row.DisplayName, textBrush, 12);
            dc.PushClip(new RectangleGeometry(new Rect(4, i * RowHeight + 2, LabelColumnWidth - 8, RowHeight - 4)));
            dc.DrawText(labelFt, new Point(8, cy - labelFt.Height / 2.0));
            dc.Pop();

            // Collect all dates (to determine line span)
            var allDates = row.CommitsByDay.Keys.Concat(row.PendingDays).ToList();
            if (allDates.Count == 0) continue;

            var firstDate = allDates.Min();
            var lastDate  = allDates.Max();
            var x1        = DayToX(firstDate);
            var x2        = DayToX(lastDate);

            // ── Connecting line ───────────────────────────────────────────────
            var linePen = new Pen(new SolidColorBrush(color), 1.0);
            dc.DrawLine(linePen,
                new Point(LabelColumnWidth + x1, cy),
                new Point(LabelColumnWidth + x2, cy));

            // ── Pending (hollow) dots ──────────────────────────────────────────
            var pendingColor = Color.FromArgb(128, color.R, color.G, color.B);
            var pendingPen   = new Pen(new SolidColorBrush(pendingColor), 1.5);
            foreach (var date in row.PendingDays)
            {
                if (date < _startDate || date > _endDate) continue;
                if (row.CommitsByDay.ContainsKey(date)) continue; // solid dot takes priority
                var cx = LabelColumnWidth + DayToX(date);
                dc.DrawEllipse(null, pendingPen, new Point(cx, cy), BaseRadius, BaseRadius);
            }

            // ── Resolved (solid) dots ─────────────────────────────────────────
            var fillColor  = Color.FromArgb(128, color.R, color.G, color.B);
            var fillBrush  = new SolidColorBrush(fillColor);
            var strokePen  = new Pen(new SolidColorBrush(color), 1.0);
            foreach (var (date, commits) in row.CommitsByDay)
            {
                if (date < _startDate || date > _endDate) continue;
                var count  = commits.Count;
                var radius = Math.Min(BaseRadius * Math.Pow(1.4, count - 1), BaseRadius * 8);
                var cx     = LabelColumnWidth + DayToX(date);
                dc.DrawEllipse(fillBrush, strokePen, new Point(cx, cy), radius, radius);
            }
        }

        // ── X-axis ─────────────────────────────────────────────────────────────
        RenderXAxis(dc, subtleBrush, borderBrush);
    }

    private void RenderXAxis(DrawingContext dc, Brush textBrush, Brush tickBrush)
    {
        var axisY        = _rows.Count * RowHeight;
        var intervalDays = _dayCount <= 90 ? 7 : 30;

        // Axis line
        dc.DrawLine(
            new Pen(tickBrush, 1),
            new Point(LabelColumnWidth, axisY),
            new Point(LabelColumnWidth + _dayCount * PixelsPerDay, axisY));

        // Tick marks and labels — align to multiples of intervalDays from DayNumber epoch
        var cursor = _startDate;
        while (cursor.DayNumber % intervalDays != 0)
            cursor = cursor.AddDays(1);

        while (cursor <= _endDate)
        {
            var x     = LabelColumnWidth + DayToX(cursor);
            var tickY = axisY + 4;
            dc.DrawLine(new Pen(tickBrush, 1), new Point(x, axisY), new Point(x, tickY));

            var label = cursor.ToString("MMM d");
            var ft    = MakeText(label, textBrush, 10);
            dc.DrawText(ft, new Point(x - ft.Width / 2.0, tickY + 2));

            cursor = cursor.AddDays(intervalDays);
        }
    }

    // ── Coordinate helpers ─────────────────────────────────────────────────────

    /// <summary>Returns the X offset (relative to the graph area, i.e. after LabelColumnWidth) for a date.</summary>
    private double DayToX(DateOnly date)
    {
        var offset = date.DayNumber - _startDate.DayNumber;
        return offset * PixelsPerDay + PixelsPerDay / 2.0;
    }

    // ── Tooltip / hit testing ──────────────────────────────────────────────────

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pt  = e.GetPosition(this);
        var hit = HitTestPoint(pt);
        if (!Equals(hit, _lastHit))
        {
            _lastHit   = hit;
            this.ToolTip = hit is null ? null : (object)BuildTooltipElement(hit);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _lastHit     = null;
        this.ToolTip = null;
    }

    private object? HitTestPoint(Point pt)
    {
        if (_rows.Count == 0 || _dayCount == 0) return null;
        if (pt.X < LabelColumnWidth) return null;
        if (pt.Y > _rows.Count * RowHeight) return null;

        var rowIndex = (int)(pt.Y / RowHeight);
        if (rowIndex < 0 || rowIndex >= _rows.Count) return null;

        var row    = _rows[rowIndex];
        var graphX = pt.X - LabelColumnWidth;
        var dayIdx = (int)(graphX / PixelsPerDay);
        if (dayIdx < 0 || dayIdx >= _dayCount) return null;

        var date  = _startDate.AddDays(dayIdx);
        var dotCx = DayToX(date); // offset from graph origin
        var dist  = Math.Abs(graphX - dotCx);

        const double hitTolerance = 4;

        // Check resolved dot
        if (row.CommitsByDay.TryGetValue(date, out var commits))
        {
            var radius = Math.Min(BaseRadius * Math.Pow(1.4, commits.Count - 1), BaseRadius * 8);
            if (dist <= radius + hitTolerance)
                return new CommitDotHit(row, date, false, commits);
        }

        // Check pending dot
        if (row.PendingDays.Contains(date) && !row.CommitsByDay.ContainsKey(date))
        {
            if (dist <= BaseRadius + hitTolerance)
                return new CommitDotHit(row, date, true, null);
        }

        // Check line segment
        var allDates = row.CommitsByDay.Keys.Concat(row.PendingDays).ToList();
        if (allDates.Count >= 2)
        {
            var firstDate = allDates.Min();
            var lastDate  = allDates.Max();
            var lineX1    = DayToX(firstDate);
            var lineX2    = DayToX(lastDate);
            var cy        = rowIndex * RowHeight + RowHeight / 2.0;
            if (graphX >= lineX1 && graphX <= lineX2 && Math.Abs(pt.Y - cy) <= 5)
                return new CommitLineHit(row, firstDate, lastDate);
        }

        return null;
    }

    private static TextBlock BuildTooltipElement(object hit)
    {
        var text = hit switch
        {
            CommitDotHit { IsPending: true  } d =>
                $"Feature: {d.Row.DisplayName}\nDate: {d.Date:MMM d, yyyy}\nStatus: Loading commit data\u2026",
            CommitDotHit { IsPending: false } d =>
                $"Feature: {d.Row.DisplayName}\nDate: {d.Date:MMM d, yyyy}\n" +
                $"Commits: {d.Commits!.Count}  " +
                $"(files: {d.Commits.Sum(c => c.FilesChanged)}, " +
                $"+{d.Commits.Sum(c => c.Insertions)} / " +
                $"-{d.Commits.Sum(c => c.Deletions)})",
            CommitLineHit l =>
                $"Feature: {l.Row.DisplayName}\nActive: {l.FirstDate:MMM d, yyyy} \u2192 {l.LastDate:MMM d, yyyy}",
            _ => ""
        };
        return new TextBlock
        {
            Text        = text,
            MaxWidth    = 340,
            TextWrapping = TextWrapping.Wrap,
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Brush? TryFindBrush(string key)
    {
        try   { return FindResource(key) as Brush; }
        catch { return null; }
    }

    private FormattedText MakeText(string text, Brush foreground, double fontSize)
        => new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            foreground,
            _pixelsPerDip == 0 ? 1.0 : _pixelsPerDip);
}
