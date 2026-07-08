using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly string?                     _workspaceFolderPath;
    private bool                                 _isDark;
    private DateOnly                             _startDate;
    private DateOnly                             _endDate;
    private CancellationTokenSource?             _cts;
    private readonly DispatcherTimer             _debounceTimer;

    // ── UI ────────────────────────────────────────────────────────────────────
    private readonly CommitActivityCanvas _canvas;
    private readonly RangeSliderControl   _rangeSlider;

    public CommitActivityGraphWindow(
        ICommitStatService              statService,
        IEnumerable<CommitApprovalItem> items,
        bool                            isDark,
        string?                         workspaceFolderPath = null)
        : base(captionHeight: ChromedWindow.CloseButtonHeight)
    {
        _statService         = statService ?? throw new ArgumentNullException(nameof(statService));
        _allItems            = items.ToList();
        _isDark              = isDark;
        _workspaceFolderPath = workspaceFolderPath;

        _endDate   = DateOnly.FromDateTime(DateTime.Today);
        _startDate = _endDate.AddDays(-365);

        Title         = "Commit History";
        Width         = 1100;
        Height        = 600;
        MinWidth      = 1024;
        MinHeight     = 300;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost       = false;

        _debounceTimer       = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); StartLoadingData(); };

        // ── Canvas / scroll area ──────────────────────────────────────────────
        _canvas = new CommitActivityCanvas();
        ToolTipService.SetInitialShowDelay(_canvas, 400);
        ToolTipService.SetShowDuration(_canvas, 12_000);

        var scrollViewer = new ScrollViewer
        {
            // Horizontal scroll disabled — canvas auto-fits to window width.
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            Content                       = _canvas,
        };
        scrollViewer.SetResourceReference(ScrollViewer.BackgroundProperty, "AppSurface");

        // ── Range slider ──────────────────────────────────────────────────────
        var minDate = DateOnly.FromDateTime(DateTime.Today.AddYears(-5));
        var maxDate = DateOnly.FromDateTime(DateTime.Today);

        _rangeSlider = new RangeSliderControl(minDate, maxDate, _startDate, _endDate)
        {
            Margin = new Thickness(10, 6, 10, 4),
        };
        WindowChrome.SetIsHitTestVisibleInChrome(_rangeSlider, true);
        _rangeSlider.RangeChanged += OnRangeSliderChanged;

        var sliderPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        sliderPanel.Children.Add(_rangeSlider);

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

    // ── Range slider ──────────────────────────────────────────────────────────

    private void OnRangeSliderChanged(object? sender, EventArgs e)
    {
        _startDate = _rangeSlider.StartDate;
        _endDate   = _rangeSlider.EndDate;
        _debounceTimer.Stop();
        _debounceTimer.Start();
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
        var startDate = _startDate;
        var endDate   = _endDate;

        var filteredItems = _allItems
            .Where(i =>
            {
                var d = DateOnly.FromDateTime(i.TurnStartedAt.LocalDateTime);
                return d >= startDate && d <= endDate;
            })
            .ToList();

        var rows     = BuildFeatureRows(filteredItems, hasWorkspace: _workspaceFolderPath is not null);
        var requests = BuildRequests(filteredItems);

        var pendingShas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var req in requests)
        {
            if (_statService.TryGetCached(req.Sha) is null)
                pendingShas.Add(req.Sha);
        }

        RefreshCanvasData(rows, requests, pendingShas, startDate, endDate);

        // ── Git history scan (Change 5) ───────────────────────────────────────
        if (_workspaceFolderPath is not null)
        {
            try
            {
                var gitRequests = await BuildGitRequestsAsync(startDate, endDate, requests, ct);
                if (gitRequests.Count > 0 && !ct.IsCancellationRequested)
                {
                    requests.AddRange(gitRequests);
                    foreach (var req in gitRequests)
                    {
                        if (_statService.TryGetCached(req.Sha) is null)
                            pendingShas.Add(req.Sha);
                    }
                    RefreshCanvasData(rows, requests, pendingShas, startDate, endDate);
                }
            }
            catch (OperationCanceledException) { return; }
        }

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

    // ── Git history helpers ───────────────────────────────────────────────────

    private async Task<List<CommitStatRequest>> BuildGitRequestsAsync(
        DateOnly                startDate,
        DateOnly                endDate,
        List<CommitStatRequest> existingRequests,
        CancellationToken       ct)
    {
        var existingShas = new HashSet<string>(
            _allItems.Select(i => i.CommitSha)
                     .Concat(existingRequests.Select(r => r.Sha)),
            StringComparer.OrdinalIgnoreCase);

        var gitCommits = await RunGitLogAsync(startDate, endDate, ct).ConfigureAwait(false);

        return gitCommits
            .Where(c => !existingShas.Contains(c.sha))
            .Select(c => new CommitStatRequest(c.sha, null, c.date))
            .ToList();
    }

    private async Task<List<(string sha, DateOnly date)>> RunGitLogAsync(
        DateOnly          startDate,
        DateOnly          endDate,
        CancellationToken ct)
    {
        var since = startDate.AddDays(-1).ToString("yyyy-MM-dd");
        var until = endDate.AddDays(1).ToString("yyyy-MM-dd");
        var args  = $"log --format=\"%H %ad\" --date=short --after={since} --until={until}";

        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory       = _workspaceFolderPath!,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return [];

            var stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);

            return ParseGitLogOutput(stdout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Parses output of <c>git log --format="%H %ad" --date=short</c>.
    /// Each line is a full SHA followed by a space and an ISO date (yyyy-MM-dd).
    /// </summary>
    internal static List<(string sha, DateOnly date)> ParseGitLogOutput(string output)
    {
        var results = new List<(string, DateOnly)>();
        foreach (var line in output.AsSpan().EnumerateLines())
        {
            var s = line.ToString().Trim();
            var spaceIdx = s.IndexOf(' ');
            if (spaceIdx < 7) continue;
            var sha  = s[..spaceIdx];
            var rest = s[(spaceIdx + 1)..].Trim();
            if (DateOnly.TryParseExact(rest, "yyyy-MM-dd", null, DateTimeStyles.None, out var date))
                results.Add((sha, date));
        }
        return results;
    }

    // ── Canvas refresh ────────────────────────────────────────────────────────

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

    private static List<CommitActivityRow> BuildFeatureRows(
        List<CommitApprovalItem> items,
        bool                     hasWorkspace = false)
    {
        var rows = new List<CommitActivityRow>();

        // Always show Uncategorized when workspace is available (git history will populate it).
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
// Range slider control (Change 4)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A custom two-handle date range picker built entirely in code.
/// Exposes <see cref="StartDate"/> / <see cref="EndDate"/> and raises
/// <see cref="RangeChanged"/> when a handle is released after dragging.
/// </summary>
internal sealed class RangeSliderControl : FrameworkElement
{
    // ── Layout constants ───────────────────────────────────────────────────────
    private const double TrackY            = 14.0;
    private const double HandleRadius      = 5.0;
    private const double HandleHoverRadius = 6.0;
    private const double TrackThickness    = 2.0;
    private const double SelectedThickness = 3.0;
    private const double LabelY            = TrackY + HandleRadius + 5.0;
    private const double ControlHeight     = LabelY + 15.0;
    private const double TrackMargin       = 10.0;

    // ── Properties ─────────────────────────────────────────────────────────────
    public DateOnly StartDate    { get; private set; }
    public DateOnly EndDate      { get; private set; }
    public DateOnly MinDate      { get; set; }
    public DateOnly MaxDate      { get; set; }
    public int      MinRangeDays { get; set; } = 7;

    // ── Events ─────────────────────────────────────────────────────────────────
    public event EventHandler? RangeChanged;

    // ── Drag / hover state ─────────────────────────────────────────────────────
    private bool   _draggingLeft;
    private bool   _draggingRight;
    private bool   _hoverLeft;
    private bool   _hoverRight;
    private double _pixelsPerDip = 1.0;

    public RangeSliderControl(DateOnly minDate, DateOnly maxDate, DateOnly startDate, DateOnly endDate)
    {
        MinDate   = minDate;
        MaxDate   = maxDate;
        StartDate = startDate;
        EndDate   = endDate;
        Height    = ControlHeight;
        Cursor    = Cursors.Arrow;
    }

    // ── Measure ────────────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsFinite(availableSize.Width) && availableSize.Width > 0
            ? availableSize.Width
            : 400;
        return new Size(w, ControlHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
        => finalSize;

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
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var trackLeft  = TrackMargin;
        var trackRight = ActualWidth - TrackMargin;

        if (trackRight <= trackLeft) return;

        var leftX  = DateToX(StartDate);
        var rightX = DateToX(EndDate);

        // Full track (SubtleText at 40% opacity)
        var subtleBrush = TryFindBrush("SubtleText") ?? Brushes.Gray;
        var trackColor  = subtleBrush is SolidColorBrush scb
            ? Color.FromArgb(102, scb.Color.R, scb.Color.G, scb.Color.B)
            : Color.FromArgb(102, 160, 160, 160);
        dc.DrawLine(
            new Pen(new SolidColorBrush(trackColor), TrackThickness),
            new Point(trackLeft, TrackY),
            new Point(trackRight, TrackY));

        // Selected range fill (ActivePanelBorder)
        var fillBrush = TryFindBrush("ActivePanelBorder") ?? TryFindBrush("PanelBorder") ?? Brushes.CornflowerBlue;
        dc.DrawLine(
            new Pen(fillBrush, SelectedThickness),
            new Point(leftX, TrackY),
            new Point(rightX, TrackY));

        // Handles
        var handleFill   = TryFindBrush("LabelText")    ?? Brushes.White;
        var handleStroke = new Pen(TryFindBrush("PanelBorder") ?? Brushes.Gray, 1.0);
        var hoverFill    = TryFindBrush("CaptionButtonHover") ?? TryFindBrush("ActivePanelBorder") ?? Brushes.LightBlue;

        var leftR  = _hoverLeft  ? HandleHoverRadius : HandleRadius;
        var rightR = _hoverRight ? HandleHoverRadius : HandleRadius;

        dc.DrawEllipse(_hoverLeft  ? hoverFill : handleFill, handleStroke, new Point(leftX,  TrackY), leftR,  leftR);
        dc.DrawEllipse(_hoverRight ? hoverFill : handleFill, handleStroke, new Point(rightX, TrackY), rightR, rightR);

        // Date labels below handles
        var textBrush = TryFindBrush("SubtleText") ?? Brushes.Gray;
        var leftFt    = MakeText(StartDate.ToString("MMM d, yyyy"), textBrush, 10);
        var rightFt   = MakeText(EndDate.ToString("MMM d, yyyy"),   textBrush, 10);

        var leftLabelX  = Math.Max(0, Math.Min(leftX  - leftFt.Width  / 2, ActualWidth - leftFt.Width));
        var rightLabelX = Math.Max(0, Math.Min(rightX - rightFt.Width / 2, ActualWidth - rightFt.Width));

        // Prevent labels from overlapping
        if (rightLabelX < leftLabelX + leftFt.Width + 4)
            rightLabelX = leftLabelX + leftFt.Width + 4;

        dc.DrawText(leftFt,  new Point(leftLabelX,  LabelY));
        dc.DrawText(rightFt, new Point(rightLabelX, LabelY));
    }

    // ── Mouse events ───────────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pt        = e.GetPosition(this);
        var leftX     = DateToX(StartDate);
        var rightX    = DateToX(EndDate);
        var hitRadius = HandleRadius + 4;

        var nearLeft  = Math.Abs(pt.X - leftX)  <= hitRadius && Math.Abs(pt.Y - TrackY) <= hitRadius;
        var nearRight = Math.Abs(pt.X - rightX) <= hitRadius && Math.Abs(pt.Y - TrackY) <= hitRadius;

        // When both handles are very close, prefer the one the mouse is nearer to.
        if (nearLeft && (!nearRight || Math.Abs(pt.X - leftX) <= Math.Abs(pt.X - rightX)))
        {
            _draggingLeft = true;
            CaptureMouse();
            e.Handled = true;
        }
        else if (nearRight)
        {
            _draggingRight = true;
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pt = e.GetPosition(this);

        if (_draggingLeft)
        {
            var newDate = ClampDate(XToDate(pt.X), MinDate, EndDate.AddDays(-MinRangeDays));
            if (newDate != StartDate) { StartDate = newDate; InvalidateVisual(); }
            return;
        }

        if (_draggingRight)
        {
            var newDate = ClampDate(XToDate(pt.X), StartDate.AddDays(MinRangeDays), MaxDate);
            if (newDate != EndDate) { EndDate = newDate; InvalidateVisual(); }
            return;
        }

        // Hover detection
        var leftX     = DateToX(StartDate);
        var rightX    = DateToX(EndDate);
        var hitRadius = HandleRadius + 4;
        var newHoverLeft  = Math.Abs(pt.X - leftX)  <= hitRadius && Math.Abs(pt.Y - TrackY) <= hitRadius;
        var newHoverRight = Math.Abs(pt.X - rightX) <= hitRadius && Math.Abs(pt.Y - TrackY) <= hitRadius;

        if (newHoverLeft != _hoverLeft || newHoverRight != _hoverRight)
        {
            _hoverLeft  = newHoverLeft;
            _hoverRight = newHoverRight;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_draggingLeft || _draggingRight)
        {
            _draggingLeft  = false;
            _draggingRight = false;
            ReleaseMouseCapture();
            RangeChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverLeft || _hoverRight)
        {
            _hoverLeft = _hoverRight = false;
            InvalidateVisual();
        }
    }

    // ── Coordinate helpers ─────────────────────────────────────────────────────

    private double DateToX(DateOnly date)
    {
        var total      = MaxDate.DayNumber - MinDate.DayNumber;
        var trackWidth = ActualWidth - 2 * TrackMargin;
        if (total <= 0 || trackWidth <= 0) return TrackMargin;
        return TrackMargin + (date.DayNumber - MinDate.DayNumber) / (double)total * trackWidth;
    }

    private DateOnly XToDate(double x)
    {
        var trackWidth = ActualWidth - 2 * TrackMargin;
        if (trackWidth <= 0) return MinDate;
        var fraction = Math.Clamp((x - TrackMargin) / trackWidth, 0.0, 1.0);
        var total    = MaxDate.DayNumber - MinDate.DayNumber;
        return MinDate.AddDays((int)Math.Round(fraction * total));
    }

    private static DateOnly ClampDate(DateOnly value, DateOnly min, DateOnly max)
        => value.DayNumber < min.DayNumber ? min
         : value.DayNumber > max.DayNumber ? max
         : value;

    // ── Brush / text helpers ───────────────────────────────────────────────────

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
/// <para>
/// The timeline is auto-fitted to the available canvas width so no horizontal
/// scrolling is required: pixels-per-day = (canvasWidth − labelColumnWidth) / totalDays.
/// </para>
/// <para>
/// Circle radius formula: <c>radius(n) = BaseRadius × √n</c> (equal-area growth —
/// each additional commit adds the same visual area as the first).
/// </para>
/// </summary>
internal sealed class CommitActivityCanvas : FrameworkElement
{
    // ── Layout ─────────────────────────────────────────────────────────────────
    internal const double LabelColumnWidth    = 160;
    internal const double RowHeight           = 32;
    internal const double XAxisHeight         = 24;
    internal const double FallbackPixelsPerDay = 20; // used when ActualWidth is unavailable
    internal const double BaseRadius          = 5.0;

    // ── State ──────────────────────────────────────────────────────────────────
    private List<CommitActivityRow> _rows      = [];
    private DateOnly                _startDate;
    private DateOnly                _endDate;
    private bool                    _isDark;
    private int                     _dayCount;
    private double                  _pixelsPerDip        = 1.0;
    private double                  _effectivePixelsPerDay = FallbackPixelsPerDay;

    // Tooltip hit tracking
    private object? _lastHit;

    // ── Constructor ────────────────────────────────────────────────────────────

    public CommitActivityCanvas()
    {
        // Re-render whenever layout gives us a new size (window resize).
        SizeChanged += (_, _) => InvalidateVisual();
    }

    // Make the entire canvas surface hittable (not just rendered pixels) so
    // OnMouseMove fires everywhere and tooltips show over dots and lines.
    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        => new PointHitTestResult(this, hitTestParameters.HitPoint);

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

    // ── Measure / Arrange ──────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        var h = Math.Max(1, _rows.Count) * RowHeight + XAxisHeight;
        // Fill the available width so no horizontal scrollbar is needed.
        var w = double.IsFinite(availableSize.Width) && availableSize.Width > LabelColumnWidth
            ? availableSize.Width
            : LabelColumnWidth + Math.Max(1, _dayCount) * FallbackPixelsPerDay;
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
        => finalSize;

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

        // Compute pixels-per-day to fit the entire range in the canvas width.
        var canvasWidth = ActualWidth - LabelColumnWidth;
        _effectivePixelsPerDay = canvasWidth > 0 ? canvasWidth / _dayCount : FallbackPixelsPerDay;

        var palette     = _isDark ? CommitActivityGraphWindow.DarkPalette : CommitActivityGraphWindow.LightPalette;
        var textBrush   = TryFindBrush("LabelText")   ?? Brushes.Black;
        var subtleBrush = TryFindBrush("SubtleText")  ?? Brushes.Gray;
        var borderBrush = TryFindBrush("PanelBorder") ?? Brushes.LightGray;

        // Background
        dc.DrawRectangle(
            TryFindBrush("AppSurface") ?? Brushes.Transparent,
            null,
            new Rect(0, 0, ActualWidth, ActualHeight));

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
                dc.DrawLine(sepPen, new Point(0, i * RowHeight), new Point(ActualWidth, i * RowHeight));
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
            // Radius: BaseRadius × √n  (equal-area growth — each additional commit
            // adds the same visual area as the first, so r(n) = BaseRadius × √n).
            var fillColor = Color.FromArgb(128, color.R, color.G, color.B);
            var fillBrush = new SolidColorBrush(fillColor);
            var strokePen = new Pen(new SolidColorBrush(color), 1.0);
            foreach (var (date, commits) in row.CommitsByDay)
            {
                if (date < _startDate || date > _endDate) continue;
                var radius = BaseRadius * Math.Sqrt(commits.Count);
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
            new Point(ActualWidth, axisY));

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
        return offset * _effectivePixelsPerDay + _effectivePixelsPerDay / 2.0;
    }

    // ── Tooltip / hit testing ──────────────────────────────────────────────────

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pt  = e.GetPosition(this);
        var hit = HitTestPoint(pt);
        if (!Equals(hit, _lastHit))
        {
            _lastHit     = hit;
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
        var dayIdx = (int)(graphX / _effectivePixelsPerDay);
        if (dayIdx < 0 || dayIdx >= _dayCount) return null;

        var date  = _startDate.AddDays(dayIdx);
        var dotCx = DayToX(date); // offset from graph origin
        var dist  = Math.Abs(graphX - dotCx);

        const double hitTolerance = 4;

        // Check resolved dot — radius(n) = BaseRadius × √n
        if (row.CommitsByDay.TryGetValue(date, out var commits))
        {
            var radius = BaseRadius * Math.Sqrt(commits.Count);
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
            Text         = text,
            MaxWidth     = 340,
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
