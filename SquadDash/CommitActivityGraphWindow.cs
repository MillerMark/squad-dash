using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
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
        Color.FromRgb(0x90, 0x90, 0x90), // 0 Uncategorized
        Color.FromRgb(0x4E, 0xCD, 0xC4), // 1
        Color.FromRgb(0xFF, 0xD9, 0x3D), // 2
        Color.FromRgb(0xA2, 0x9B, 0xFE), // 3
        Color.FromRgb(0x6B, 0xCB, 0x77), // 4
        Color.FromRgb(0xFF, 0xA0, 0x7A), // 5
        Color.FromRgb(0x74, 0xB9, 0xFF), // 6
    ];

    internal static readonly Color[] LightPalette =
    [
        Color.FromRgb(0x70, 0x70, 0x70), // 0 Uncategorized
        Color.FromRgb(0x14, 0x8A, 0x82), // 1
        Color.FromRgb(0xB8, 0x86, 0x0B), // 2
        Color.FromRgb(0x5E, 0x35, 0xB1), // 3
        Color.FromRgb(0x2E, 0x7D, 0x32), // 4
        Color.FromRgb(0xBF, 0x57, 0x22), // 5
        Color.FromRgb(0x15, 0x65, 0xC0), // 6
    ];

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly ICommitStatService          _statService;
    private List<CommitApprovalItem>             _allItems;
    private readonly string?                     _workspaceFolderPath;
    private bool                                 _isDark;
    private DateOnly                             _startDate;
    private DateOnly                             _endDate;
    private TimeOnly                             _startTime = TimeOnly.MinValue;
    private TimeOnly                             _endTime   = new TimeOnly(23, 59);
    private CancellationTokenSource?             _cts;
    private readonly DispatcherTimer             _debounceTimer;

    // ── Cached data for filter-only refreshes ─────────────────────────────────
    private List<CommitActivityRow>?  _cachedRows;
    private List<CommitStatRequest>?  _cachedRequests;
    private HashSet<string>           _cachedPendingShas = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset            _cachedStartDate;
    private DateTimeOffset            _cachedEndDate;

    // ── Sub-day viewport helpers ──────────────────────────────────────────────
    private DateTimeOffset EffectiveStart =>
        new DateTimeOffset(_startDate.ToDateTime(_startTime), DateTimeOffset.Now.Offset);
    private DateTimeOffset EffectiveEnd =>
        new DateTimeOffset(_endDate.ToDateTime(_endTime), DateTimeOffset.Now.Offset);

    // ── UI ────────────────────────────────────────────────────────────────────
    private readonly CommitActivityCanvas _canvas;
    private readonly RangeSliderControl   _rangeSlider;
    private readonly CheckBox             _showUncategorizedCheckBox;
    private TextBox?                      _featureFilterBox;
    private Button?                       _featureFilterClear;
    private string[]                      _featureFilters = [];

    // ── Zoom / pan ────────────────────────────────────────────────────────────
    private bool   _isPanMode;
    private bool   _isPanning;
    private Point  _panStartMouse;
    private DateOnly _panStartDate;
    private ScrollViewer _scrollViewer = null!;

    // ── Selection ─────────────────────────────────────────────────────────────
    private bool   _isSelecting;
    private double _selectionDragStartX;

    // ── Selection analysis panel ───────────────────────────────────────────────
    private Border?         _selectionPanel;
    private GridSplitter?   _selectionSplitter;
    private RowDefinition?  _selectionPanelRow;
    private TextBlock?      _selectionEarliestValue;
    private TextBlock?      _selectionLatestValue;
    private TextBlock?      _selectionDurationValue;
    private TextBlock?      _selectionCommitCountValue;
    private TextBlock?      _selectionFilesValue;
    private TextBlock?      _selectionLinesAddedValue;
    private TextBlock?      _selectionLinesRemovedValue;
    private TextBlock?      _selectionAiTimeValue;
    private StackPanel?     _selectionCommitListPanel;

    // ── AI categorization ─────────────────────────────────────────────────────
    private SquadSdkCategorizationService? _categorizationService;
    private CommitCategoryCache?           _categoryCache;
    private bool                           _categorizationInFlight;
    internal bool IsCategorizationInFlight => _categorizationInFlight;
    private TextBlock?                     _categorizeStatusText;
    private Button?                        _categorizeButton;
    private DispatcherTimer?               _spinnerTimer;
    private int                            _spinnerFrame;
    private string                         _spinnerBaseText  = string.Empty;
    private static readonly string[]       SpinnerFrames     = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
    private Action<IReadOnlyList<(string Sha, string Group)>>? _onCategoriesAssigned;
    private readonly Func<IReadOnlyList<string>>? _getFeatureGroups;

    public CommitActivityGraphWindow(
        ICommitStatService              statService,
        IEnumerable<CommitApprovalItem> items,
        bool                            isDark,
        string?                         workspaceFolderPath = null,
        IWorkspacePaths?                workspacePaths      = null,
        string?                         workspaceStateDirectory = null,
        Func<IReadOnlyList<string>>?    getFeatureGroups    = null,
        Action<IReadOnlyList<(string Sha, string Group)>>? onCategoriesAssigned = null)
        : base(captionHeight: ChromedWindow.CloseButtonHeight)
    {
        _statService         = statService ?? throw new ArgumentNullException(nameof(statService));
        _allItems            = items.ToList();
        _isDark              = isDark;
        _workspaceFolderPath = workspaceFolderPath;
        _onCategoriesAssigned = onCategoriesAssigned;
        _getFeatureGroups     = getFeatureGroups;
        if (workspacePaths is not null)
        {
            _categorizationService = new SquadSdkCategorizationService(workspacePaths);
            _categoryCache         = new CommitCategoryCache(workspaceStateDirectory ?? workspacePaths.ApplicationRoot);
            ApplyCacheTo(_allItems);
        }

        _endDate   = DateOnly.FromDateTime(DateTime.Today);
        _startDate = _endDate.AddDays(-30);

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
        _canvas.RowSelectionChanged += (_, _) => UpdateSelectionPanel();

        var canvasWrapper = new Border
        {
            Child = _canvas,
        };
        _scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            Content                       = canvasWrapper,
        };
        _scrollViewer.SetResourceReference(ScrollViewer.BackgroundProperty, "AppSurface");

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

        // ── Show Uncategorized checkbox ───────────────────────────────────────
        _showUncategorizedCheckBox = new CheckBox
        {
            IsChecked         = true,
            Content           = "Show Uncategorized",
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 8, 0),
        };
        _showUncategorizedCheckBox.SetResourceReference(CheckBox.ForegroundProperty, "LabelText");
        _showUncategorizedCheckBox.SetResourceReference(CheckBox.FontSizeProperty,   "FontSizeBody");
        _showUncategorizedCheckBox.Checked   += OnShowUncategorizedChanged;
        _showUncategorizedCheckBox.Unchecked += OnShowUncategorizedChanged;
        WindowChrome.SetIsHitTestVisibleInChrome(_showUncategorizedCheckBox, true);

        // ── Quick-range buttons ───────────────────────────────────────────────
        var controlsBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(10, 4, 10, 0),
        };
        controlsBar.Children.Add(_showUncategorizedCheckBox);
        foreach (var btn in CreateQuickRangeButtons())
            controlsBar.Children.Add(btn);

        var zoomOutBtn = CreateZoomOutButton();
        controlsBar.Children.Add(zoomOutBtn);

        if (_categorizationService is not null)
        {
            _categorizeButton = new Button
            {
                Content           = "Categorize",
                Padding           = new Thickness(8, 2, 8, 2),
                Margin            = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            _categorizeButton.SetResourceReference(Button.StyleProperty,      "ThemedButtonStyle");
            _categorizeButton.SetResourceReference(Button.FontSizeProperty,   "FontSizeBody");
            _categorizeButton.Click += OnCategorizeButtonClick;
            WindowChrome.SetIsHitTestVisibleInChrome(_categorizeButton, true);
            controlsBar.Children.Add(_categorizeButton);

            _categorizeStatusText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(8, 0, 0, 0),
                Visibility        = Visibility.Collapsed,
            };
            _categorizeStatusText.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            _categorizeStatusText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
            WindowChrome.SetIsHitTestVisibleInChrome(_categorizeStatusText, true);
            controlsBar.Children.Add(_categorizeStatusText);
        }

        // ── Top bar (controls bar + slider) ──────────────────────────────────
        var topBar = new StackPanel();
        topBar.Children.Add(controlsBar);
        topBar.Children.Add(sliderPanel);

        // ── Selection analysis panel ──────────────────────────────────────────
        _selectionPanel            = BuildSelectionPanel();
        _selectionPanel.Visibility = Visibility.Collapsed;

        // ── Feature filter widget ─────────────────────────────────────────────
        var filterWidget = BuildFeatureFilterWidget();

        // ── Main layout ───────────────────────────────────────────────────────
        _selectionSplitter = new GridSplitter
        {
            Height              = 5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Center,
            ResizeDirection     = GridResizeDirection.Rows,
            ResizeBehavior      = GridResizeBehavior.PreviousAndNext,
            Visibility          = Visibility.Collapsed,
            Cursor              = Cursors.SizeNS,
        };
        _selectionSplitter.SetResourceReference(GridSplitter.BackgroundProperty, "PanelBorder");
        WindowChrome.SetIsHitTestVisibleInChrome(_selectionSplitter, true);

        _selectionPanelRow        = new RowDefinition { MinHeight = 80 };
        _selectionPanelRow.Height = new GridLength(180);

        var canvasGrid = new Grid();
        canvasGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        canvasGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        canvasGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        canvasGrid.RowDefinitions.Add(_selectionPanelRow);
        Grid.SetRow(filterWidget,       0);
        Grid.SetRow(_scrollViewer,      1);
        Grid.SetRow(_selectionSplitter, 2);
        Grid.SetRow(_selectionPanel,    3);
        canvasGrid.Children.Add(filterWidget);
        canvasGrid.Children.Add(_scrollViewer);
        canvasGrid.Children.Add(_selectionSplitter);
        canvasGrid.Children.Add(_selectionPanel);

        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(topBar, Dock.Top);
        layout.Children.Add(topBar);
        layout.Children.Add(canvasGrid);

        var contentBorder   = ApplyOuterBorder(titleText: "Commit History");
        contentBorder.Child = layout;

        Loaded += (_, _) => StartLoadingData();
        Closed += (_, _) => { _cts?.Cancel(); _debounceTimer.Stop(); };

        // ── Ctrl+scroll zoom (date-range narrowing) ───────────────────────────
        // Zooming in = fewer days visible; the canvas auto-adjusts pixels-per-day.
        // The date under the mouse drifts 20% toward the viewport center per step.
        PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            var mouseInCanvas = e.GetPosition(_canvas);
            double factor     = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
            ApplyDateRangeZoom(mouseInCanvas.X, factor);
            e.Handled = true;
        };

        // ── Spacebar pan mode + selection keyboard shortcuts ──────────────────
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space && !_isPanMode)
            {
                _isPanMode = true;
                _scrollViewer.Cursor = AnnotationCursors.OpenHand;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _canvas.HasSelection)
            {
                _canvas.ClearSelection();
                UpdateSelectionPanel();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _canvas.SelectedRowIndices.Count > 0)
            {
                _canvas.ClearRowSelection();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _canvas.SelectedRowIndices.Count > 0 && !_canvas.HasSelection)
            {
                var names = _canvas.SelectedRowIndices
                    .Select(idx => _canvas.GetRowDisplayName(idx))
                    .Where(n => !string.IsNullOrEmpty(n));
                if (_featureFilterBox is not null)
                    _featureFilterBox.Text = string.Join(";", names);
                _canvas.ClearRowSelection();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _canvas.HasSelection)
            {
                ApplySelectionAsDateRange();
                e.Handled = true;
            }
        };

        PreviewKeyUp += (_, e) =>
        {
            if (e.Key == Key.Space && _isPanMode)
            {
                _isPanMode = false;
                if (_isPanning)
                {
                    _isPanning = false;
                    _scrollViewer.ReleaseMouseCapture();
                }
                _scrollViewer.Cursor = null;
                e.Handled = true;
            }
        };

        // ── Pan drag (shifts the visible date range) ──────────────────────────
        _scrollViewer.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!_isPanMode) return;
            _isPanning     = true;
            _panStartMouse = e.GetPosition(_canvas);
            _panStartDate  = _startDate;
            _scrollViewer.CaptureMouse();
            _scrollViewer.Cursor = AnnotationCursors.ClosedHand;
            e.Handled = true;
        };

        _scrollViewer.PreviewMouseMove += (_, e) =>
        {
            if (!_isPanning) return;
            var pos              = e.GetPosition(_canvas);
            var canvasGraphWidth = _canvas.ActualWidth - CommitActivityCanvas.LabelColumnWidth;
            if (canvasGraphWidth <= 0) return;
            int dayCount = Math.Max(1, _endDate.DayNumber - _startDate.DayNumber + 1);
            var ppd      = canvasGraphWidth / dayCount;
            var dayDelta = (int)Math.Round((_panStartMouse.X - pos.X) / ppd);
            ShiftDateRange(_panStartDate, dayDelta);
            e.Handled = true;
        };

        _scrollViewer.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!_isPanning) return;
            _isPanning = false;
            _scrollViewer.ReleaseMouseCapture();
            _scrollViewer.Cursor = _isPanMode ? AnnotationCursors.OpenHand : null;
            e.Handled = true;
        };

        // ── Selection click-drag ──────────────────────────────────────────────
        _scrollViewer.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_isPanMode) return;
            var pos = e.GetPosition(_canvas);
            if (pos.X < CommitActivityCanvas.LabelColumnWidth) return;
            _selectionDragStartX = pos.X;
            _canvas.SetSelection(pos.X, pos.X);
            _scrollViewer.CaptureMouse();
            _isSelecting = true;
            e.Handled = true;
        };

        _scrollViewer.PreviewMouseMove += (_, e) =>
        {
            if (_isSelecting)
            {
                var pos = e.GetPosition(_canvas);
                var dt1 = CanvasXToDateTime(_selectionDragStartX);
                var dt2 = CanvasXToDateTime(pos.X);
                _canvas.SetSelectionWithTimes(_selectionDragStartX, pos.X, dt1, dt2);
                UpdateSelectionPanel();
            }
            else if (!_isPanMode && !_isPanning)
            {
                var pos = e.GetPosition(_canvas);
                _scrollViewer.Cursor = pos.X > CommitActivityCanvas.LabelColumnWidth ? Cursors.Cross : null;
            }
        };

        _scrollViewer.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!_isSelecting) return;
            _isSelecting = false;
            _scrollViewer.ReleaseMouseCapture();
            UpdateSelectionPanel();
        };

        // ── Right-click on canvas row: context menu for AI categorization ─────
        _canvas.MouseRightButtonDown += (_, e) =>
        {
            var pos    = e.GetPosition(_canvas);
            var hitRow = _canvas.HitTestRow(pos);

            if (hitRow?.FeatureGroup is null) // null FeatureGroup = Uncategorized row
            {
                if (_categorizationService is null) return;
                var menu     = new ContextMenu();
                var menuItem = new MenuItem { Header = "Categorize with AI" };
                menuItem.Click += (_, _) => OnCategorizeButtonClick(null, null!);
                menu.Items.Add(menuItem);
                menu.IsOpen = true;
                e.Handled = true;
            }
            else // named feature row
            {
                var sourceGroup = hitRow.FeatureGroup;
                var otherGroups = _cachedRows?
                    .Where(r => r.FeatureGroup is not null &&
                                !string.Equals(r.FeatureGroup, sourceGroup, StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.FeatureGroup!)
                    .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (otherGroups is null || otherGroups.Count == 0) return;

                var menu      = new ContextMenu();
                var mergeItem = new MenuItem { Header = "Merge with ▶" };
                foreach (var targetGroup in otherGroups)
                {
                    var target    = targetGroup; // capture for closure
                    var subItem   = new MenuItem { Header = target };
                    subItem.Click += (_, _) =>
                    {
                        var result = MessageBox.Show(
                            $"Merge \"{sourceGroup}\" into \"{target}\"?\n\nAll commits in \"{sourceGroup}\" will be reassigned to \"{target}\". This cannot be undone.",
                            "Merge Categories",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);
                        if (result != MessageBoxResult.Yes) return;
                        MergeFeatureCategories(sourceGroup, target);
                    };
                    mergeItem.Items.Add(subItem);
                }
                menu.Items.Add(mergeItem);
                menu.IsOpen = true;
                e.Handled = true;
            }
        };
    }

    private void MergeFeatureCategories(string sourceGroup, string targetGroup)
    {
        // Reassign all _allItems from sourceGroup → targetGroup
        for (int i = 0; i < _allItems.Count; i++)
        {
            if (string.Equals(_allItems[i].FeatureGroup, sourceGroup, StringComparison.OrdinalIgnoreCase))
                _allItems[i] = _allItems[i] with { FeatureGroup = targetGroup };
        }

        // Update the category cache
        if (_categoryCache is not null)
        {
            bool changed = false;
            foreach (var item in _allItems.Where(i =>
                string.Equals(i.FeatureGroup, targetGroup, StringComparison.OrdinalIgnoreCase)))
            {
                _categoryCache.SetGroup(item.CommitSha, targetGroup);
                changed = true;
            }
            if (changed)
                _categoryCache.Save();
        }

        // Notify the host so Approvals panel and canonical category list update
        var reassigned = _allItems
            .Where(i => string.Equals(i.FeatureGroup, targetGroup, StringComparison.OrdinalIgnoreCase))
            .Select(i => (i.CommitSha, targetGroup))
            .ToList();
        _onCategoriesAssigned?.Invoke(reassigned);

        StartLoadingData();
    }

    // ── Zoom / pan helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Narrows or widens the visible date range (horizontal zoom only).
    /// <paramref name="mouseCanvasX"/> is the canvas-relative X of the mouse cursor.
    /// Each step drifts the date under the mouse 20% toward the viewport center.
    /// </summary>
    private void ApplyDateRangeZoom(double mouseCanvasX, double factor)
    {
        var canvasGraphWidth = _canvas.ActualWidth - CommitActivityCanvas.LabelColumnWidth;
        if (canvasGraphWidth <= 0) return;
        int dayCount = Math.Max(1, _endDate.DayNumber - _startDate.DayNumber + 1);
        var ppd         = canvasGraphWidth / dayCount;
        var mouseGraphX = Math.Clamp(mouseCanvasX - CommitActivityCanvas.LabelColumnWidth, 0, canvasGraphWidth);

        double mouseDateFrac  = _startDate.DayNumber + mouseGraphX / ppd;
        double centerDateFrac = _startDate.DayNumber + (canvasGraphWidth / 2.0) / ppd;

        int absRange     = _rangeSlider.MaxDate.DayNumber - _rangeSlider.MinDate.DayNumber + 1;
        double newDayCount = Math.Clamp(dayCount / factor, 1, absRange);

        double newMouseDateFrac = centerDateFrac + (mouseDateFrac - centerDateFrac) * 0.8;

        double newPPD       = canvasGraphWidth / newDayCount;
        double newStartFrac = newMouseDateFrac - mouseGraphX / newPPD;

        int newStartDay = (int)Math.Round(newStartFrac);
        int newEndDay   = newStartDay + (int)Math.Round(newDayCount) - 1;

        ClampDateRange(ref newStartDay, ref newEndDay, (int)Math.Round(newDayCount));

        _startDate = DateOnly.FromDayNumber(newStartDay);
        _endDate   = DateOnly.FromDayNumber(newEndDay);
        _startTime = TimeOnly.MinValue;
        _endTime   = new TimeOnly(23, 59);
        _rangeSlider.SetRange(_startDate, _endDate);
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    /// <summary>Shifts the visible date range by <paramref name="dayDelta"/> days from <paramref name="baseStart"/>.</summary>
    private void ShiftDateRange(DateOnly baseStart, int dayDelta)
    {
        if (dayDelta == 0) return;
        int rangeLen    = Math.Max(1, _endDate.DayNumber - _startDate.DayNumber + 1);
        int newStartDay = baseStart.DayNumber + dayDelta;
        int newEndDay   = newStartDay + rangeLen - 1;
        ClampDateRange(ref newStartDay, ref newEndDay, rangeLen);
        _startDate = DateOnly.FromDayNumber(newStartDay);
        _endDate   = DateOnly.FromDayNumber(newEndDay);
        _startTime = TimeOnly.MinValue;
        _endTime   = new TimeOnly(23, 59);
        _rangeSlider.SetRange(_startDate, _endDate);
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void ClampDateRange(ref int startDay, ref int endDay, int dayCount)
    {
        int minDay = _rangeSlider.MinDate.DayNumber;
        int maxDay = _rangeSlider.MaxDate.DayNumber;
        if (endDay   > maxDay) { endDay   = maxDay;   startDay = endDay   - dayCount + 1; }
        if (startDay < minDay) { startDay = minDay;   endDay   = startDay + dayCount - 1; }
        startDay = Math.Max(startDay, minDay);
        endDay   = Math.Min(endDay,   maxDay);
    }

    private void ApplySelectionAsDateRange()
    {
        if (!_canvas.HasSelection) return;
        var ppd = _canvas.PixelsPerDay;
        if (ppd <= 0) return;

        var minGraphX = _canvas.SelectionXMin - CommitActivityCanvas.LabelColumnWidth;
        var maxGraphX = _canvas.SelectionXMax - CommitActivityCanvas.LabelColumnWidth;

        // Fractional day offsets from EffectiveStart
        var newStart = EffectiveStart.AddDays(minGraphX / ppd);
        var newEnd   = EffectiveStart.AddDays(maxGraphX / ppd);

        // Minimum 30-minute range
        if ((newEnd - newStart).TotalMinutes < 30)
            newEnd = newStart.AddMinutes(30);

        // Extract DateOnly + TimeOnly from results
        _startDate = DateOnly.FromDateTime(newStart.LocalDateTime);
        _startTime = TimeOnly.FromDateTime(newStart.LocalDateTime);
        _endDate   = DateOnly.FromDateTime(newEnd.LocalDateTime);
        _endTime   = TimeOnly.FromDateTime(newEnd.LocalDateTime);

        // Update slider to show the containing day range (slider is day-granular)
        var prevMin = _rangeSlider.MinRangeDays;
        _rangeSlider.MinRangeDays = 0;
        _rangeSlider.SetRange(_startDate, _endDate);
        _rangeSlider.MinRangeDays = prevMin;

        _debounceTimer.Stop();
        _debounceTimer.Start();
        _canvas.ClearSelection();
        UpdateSelectionPanel();
    }

    // ── Selection analysis panel ───────────────────────────────────────────────

    private Border BuildSelectionPanel()
    {
        _selectionEarliestValue     = MakeStatValueBlock();
        _selectionLatestValue       = MakeStatValueBlock();
        _selectionDurationValue     = MakeStatValueBlock();
        _selectionCommitCountValue  = MakeStatValueBlock();
        _selectionFilesValue        = MakeStatValueBlock();
        _selectionLinesAddedValue   = MakeStatValueBlock();
        _selectionLinesRemovedValue = MakeStatValueBlock();

        var aiTimeValue = MakeStatValueBlock();
        _selectionAiTimeValue = aiTimeValue;

        var statsWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 4, 4, 0) };
        statsWrap.Children.Add(MakeStatChip("🕐 Earliest",      _selectionEarliestValue));
        statsWrap.Children.Add(MakeStatChip("🕐 Latest",        _selectionLatestValue));
        statsWrap.Children.Add(MakeStatChip("⏱ Duration",      _selectionDurationValue));
        statsWrap.Children.Add(MakeStatChip("📄 Commits",       _selectionCommitCountValue));
        statsWrap.Children.Add(MakeStatChip("📝 Files",         _selectionFilesValue));
        statsWrap.Children.Add(MakeStatChip("＋ Lines added",   _selectionLinesAddedValue));
        statsWrap.Children.Add(MakeStatChip("－ Lines removed", _selectionLinesRemovedValue));
        statsWrap.Children.Add(MakeStatChip("🤖 Est. AI time",  aiTimeValue));

        var statsScroller = new ScrollViewer
        {
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content                       = statsWrap,
        };
        var statsContainer = new Border
        {
            Child           = statsScroller,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding         = new Thickness(4, 0, 4, 0),
        };
        statsContainer.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");

        _selectionCommitListPanel = new StackPanel { Orientation = Orientation.Vertical };
        var commitScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content                       = _selectionCommitListPanel,
            Padding                       = new Thickness(6, 4, 4, 4),
        };

        var innerGrid = new Grid();
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(statsContainer, 0);
        Grid.SetColumn(commitScroll, 1);
        innerGrid.Children.Add(statsContainer);
        innerGrid.Children.Add(commitScroll);

        var panel = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child           = innerGrid,
        };
        panel.SetResourceReference(Border.BackgroundProperty,  "AppSurface");
        panel.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        return panel;
    }

    private static TextBlock MakeStatValueBlock()
    {
        var tb = new TextBlock { FontSize = 11 };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        return tb;
    }

    private static FrameworkElement MakeStatChip(string label, TextBlock valueBlock)
    {
        var keyLabel = new TextBlock
        {
            Text              = label,
            FontSize          = 10,
            Margin            = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        keyLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        valueBlock.VerticalAlignment = VerticalAlignment.Center;

        var chip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 0, 16, 6),
        };
        chip.Children.Add(keyLabel);
        chip.Children.Add(valueBlock);
        return chip;
    }

    private void UpdateSelectionPanel()
    {
        if (_selectionPanel is null) return;

        if (!_canvas.HasSelection)
        {
            _selectionPanel.Visibility   = Visibility.Collapsed;
            _selectionSplitter!.Visibility = Visibility.Collapsed;
            return;
        }

        var rangeStartDt = CanvasXToDateTime(_canvas.SelectionXMin);
        var rangeEndDt   = CanvasXToDateTime(_canvas.SelectionXMax);
        if (rangeStartDt is null || rangeEndDt is null)
        {
            _selectionPanel.Visibility   = Visibility.Collapsed;
            _selectionSplitter!.Visibility = Visibility.Collapsed;
            return;
        }

        var selStart = rangeStartDt.Value < rangeEndDt.Value ? rangeStartDt.Value : rangeEndDt.Value;
        var selEnd   = rangeStartDt.Value < rangeEndDt.Value ? rangeEndDt.Value   : rangeStartDt.Value;

        // Determine which rows to analyse based on selection/filter scope
        IEnumerable<CommitActivityRow> analysisRows;
        var selectedIndices = _canvas.SelectedRowIndices;
        if (selectedIndices.Count > 0)
        {
            var selectedNames = selectedIndices
                .Select(i => _canvas.GetRowDisplayName(i))
                .Where(n => n is not null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            analysisRows = _cachedRows?.Where(r => selectedNames.Contains(r.DisplayName)) ?? [];
        }
        else if (_featureFilters.Length > 0)
        {
            analysisRows = _cachedRows?.Where(r =>
                _featureFilters.Any(f => r.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase))) ?? [];
        }
        else
        {
            analysisRows = _cachedRows ?? [];
        }

        var commits = new List<(CommitStatResult Result, string DisplayName)>();
        foreach (var row in analysisRows)
        {
            foreach (var (_, dayCommits) in row.CommitsByDay)
            {
                foreach (var commit in dayCommits)
                {
                    var dt = commit.CommitTime ?? commit.TurnStartedAt;
                    if (dt.HasValue && dt.Value >= selStart && dt.Value <= selEnd)
                        commits.Add((commit, row.DisplayName));
                }
            }
        }

        var times = commits
            .Select(c => c.Result.CommitTime ?? c.Result.TurnStartedAt)
            .Where(dt => dt.HasValue)
            .Select(dt => dt!.Value)
            .ToList();

        var earliestTime = times.Count > 0 ? (DateTimeOffset?)times.Min() : null;
        var latestTime   = times.Count > 0 ? (DateTimeOffset?)times.Max() : null;
        var duration     = earliestTime.HasValue && latestTime.HasValue
            ? latestTime.Value - earliestTime.Value
            : TimeSpan.Zero;

        int totalFiles   = commits.Sum(c => c.Result.FilesChanged);
        int totalAdded   = commits.Sum(c => c.Result.Insertions);
        int totalRemoved = commits.Sum(c => c.Result.Deletions);

        var totalAiTime = commits
            .Select(c => c.Result)
            .Where(r => r.CommitTime.HasValue && r.TurnStartedAt.HasValue && r.CommitTime > r.TurnStartedAt)
            .Aggregate(TimeSpan.Zero, (acc, r) => acc + (r.CommitTime!.Value - r.TurnStartedAt!.Value));

        if (_selectionEarliestValue     is not null)
            _selectionEarliestValue.Text     = earliestTime.HasValue ? earliestTime.Value.LocalDateTime.ToString("MMM d  h:mm tt") : "—";
        if (_selectionLatestValue       is not null)
            _selectionLatestValue.Text       = latestTime.HasValue   ? latestTime.Value.LocalDateTime.ToString("MMM d  h:mm tt")   : "—";
        if (_selectionDurationValue     is not null)
            _selectionDurationValue.Text     = times.Count > 1 ? FormatSelectionDuration(duration) : "—";
        if (_selectionCommitCountValue  is not null)
            _selectionCommitCountValue.Text  = commits.Count.ToString();
        if (_selectionFilesValue        is not null)
            _selectionFilesValue.Text        = totalFiles.ToString("N0");
        if (_selectionLinesAddedValue   is not null)
            _selectionLinesAddedValue.Text   = totalAdded.ToString("N0");
        if (_selectionLinesRemovedValue is not null)
            _selectionLinesRemovedValue.Text = totalRemoved.ToString("N0");
        if (_selectionAiTimeValue is not null)
            _selectionAiTimeValue.Text = totalAiTime > TimeSpan.Zero ? FormatSelectionDuration(totalAiTime) : "—";

        if (_selectionCommitListPanel is not null)
        {
            _selectionCommitListPanel.Children.Clear();
            var sorted = commits
                .OrderBy(c => c.Result.CommitTime ?? c.Result.TurnStartedAt ?? DateTimeOffset.MaxValue)
                .ToList();
            foreach (var (commit, displayName) in sorted)
                _selectionCommitListPanel.Children.Add(BuildSelectionCommitRow(commit, displayName));
        }

        _selectionPanel.Visibility   = Visibility.Visible;
        _selectionSplitter!.Visibility = Visibility.Visible;
    }

    private static string FormatSelectionDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1)
            return $"{(int)ts.TotalMinutes}m";
        return $"{(int)ts.TotalSeconds}s";
    }

    private static FrameworkElement BuildSelectionCommitRow(CommitStatResult commit, string displayName)
    {
        const int MaxMsgChars = 50;
        var sha     = commit.Sha.Length >= 7 ? commit.Sha[..7] : commit.Sha;
        var msg     = string.IsNullOrWhiteSpace(commit.Message) ? "(no message)" : commit.Message;
        if (msg.Length > MaxMsgChars) msg = msg[..MaxMsgChars] + "…";
        var timeStr = commit.CommitTime.HasValue
            ? commit.CommitTime.Value.LocalDateTime.ToString("h:mm tt")
            : "—";

        var tb = new TextBlock
        {
            FontSize     = 10,
            Padding      = new Thickness(0, 1, 0, 1),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var shaRun = new Run(sha + "  ");
        shaRun.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");

        var featureRun = new Run(displayName + "  ");
        featureRun.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");

        var msgRun = new Run(msg + "  ");
        msgRun.SetResourceReference(TextElement.ForegroundProperty, "LabelText");

        var timeRun = new Run(timeStr);
        timeRun.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");

        tb.Inlines.Add(shaRun);
        tb.Inlines.Add(featureRun);
        tb.Inlines.Add(msgRun);
        tb.Inlines.Add(timeRun);
        return tb;
    }

    // ── Theme ─────────────────────────────────────────────────────────────────

    private DateTimeOffset? CanvasXToDateTime(double canvasX)
    {
        var ppd = _canvas.PixelsPerDay;
        if (ppd <= 0) return null;
        var graphX = Math.Max(0, canvasX - CommitActivityCanvas.LabelColumnWidth);
        return EffectiveStart.AddDays(graphX / ppd);
    }

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
        _startTime = TimeOnly.MinValue;
        _endTime   = new TimeOnly(23, 59);
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnShowUncategorizedChanged(object sender, RoutedEventArgs e)
    {
        if (_cachedRows is null) return;
        RefreshCanvasData(_cachedRows, _cachedRequests!, _cachedPendingShas,
                          _cachedStartDate, _cachedEndDate);
    }

    private IEnumerable<Button> CreateQuickRangeButtons()
    {
        // "Today" button — single-day range
        var todayBtn = new Button
        {
            Content = "Today",
            Padding = new Thickness(8, 3, 8, 3),
            Margin  = new Thickness(4, 0, 0, 0),
        };
        todayBtn.SetResourceReference(Button.StyleProperty,    "ThemedButtonStyle");
        todayBtn.SetResourceReference(Button.FontSizeProperty, "FontSizeBody");
        WindowChrome.SetIsHitTestVisibleInChrome(todayBtn, true);
        todayBtn.Click += (_, _) =>
        {
            var today  = DateOnly.FromDateTime(DateTime.Today);
            _startDate = today;
            _endDate   = today;
            _startTime = TimeOnly.MinValue;
            _endTime   = new TimeOnly(23, 59);
            _rangeSlider.MinRangeDays = 0;
            _rangeSlider.SetRange(_startDate, _endDate);
            _rangeSlider.MinRangeDays = 1;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        };
        yield return todayBtn;

        (string Label, int Days)[] ranges =
        [
            ("Last Week",    7),
            ("Last Month",   30),
            ("Last Quarter", 91),
            ("Last Year",    365),
        ];

        foreach (var (label, days) in ranges)
        {
            var btn = new Button
            {
                Content = label,
                Padding = new Thickness(8, 3, 8, 3),
                Margin  = new Thickness(4, 0, 0, 0),
            };
            btn.SetResourceReference(Button.StyleProperty,     "ThemedButtonStyle");
            btn.SetResourceReference(Button.FontSizeProperty,  "FontSizeBody");
            WindowChrome.SetIsHitTestVisibleInChrome(btn, true);
            var d = days;
            btn.Click += (_, _) =>
            {
                var today  = DateOnly.FromDateTime(DateTime.Today);
                _startDate = today.AddDays(-d);
                _endDate   = today;
                _startTime = TimeOnly.MinValue;
                _endTime   = new TimeOnly(23, 59);
                _rangeSlider.SetRange(_startDate, _endDate);
                _debounceTimer.Stop();
                _debounceTimer.Start();
            };
            yield return btn;
        }
    }

    private Button CreateZoomOutButton()
    {
        var btn = new Button
        {
            Content = "Zoom Out",
            Padding = new Thickness(8, 3, 8, 3),
            Margin  = new Thickness(12, 0, 0, 0),
            ToolTip = "Expand the date range by 50% on each side.",
        };
        btn.SetResourceReference(Button.StyleProperty,    "ThemedButtonStyle");
        btn.SetResourceReference(Button.FontSizeProperty, "FontSizeBody");
        WindowChrome.SetIsHitTestVisibleInChrome(btn, true);
        btn.Click += (_, _) => ZoomOut();
        return btn;
    }

    private void ZoomOut()
    {
        int currentDays = Math.Max(1, _endDate.DayNumber - _startDate.DayNumber + 1);
        int expand      = (int)Math.Round(currentDays * 0.5);

        var today       = DateOnly.FromDateTime(DateTime.Today);
        var oldestDate  = today.AddDays(-365);

        int newStartDay = Math.Max(_startDate.DayNumber - expand, oldestDate.DayNumber);
        int newEndDay   = Math.Min(_endDate.DayNumber   + expand, today.DayNumber);

        _startDate = DateOnly.FromDayNumber(newStartDay);
        _endDate   = DateOnly.FromDayNumber(newEndDay);
        _startTime = TimeOnly.MinValue;
        _endTime   = new TimeOnly(23, 59);
        _rangeSlider.SetRange(_startDate, _endDate);
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

    internal void ReplaceItems(IEnumerable<CommitApprovalItem> items)
    {
        _allItems = items.ToList();
        ApplyCacheTo(_allItems);
        StartLoadingData();
    }

    private async Task LoadDataAsync(CancellationToken ct)
    {
        var startDate      = _startDate;    // DateOnly, for git log range
        var endDate        = _endDate;      // DateOnly, for git log range
        var effectiveStart = EffectiveStart;
        var effectiveEnd   = EffectiveEnd;

        var filteredItems = _allItems
            .Where(i => i.TurnStartedAt >= effectiveStart && i.TurnStartedAt <= effectiveEnd)
            .ToList();

        // Build rows from ALL items so every known feature group always has a row,
        // even when its TurnStartedAt falls outside the current date window.
        // The date filter only controls which commit requests (and git-log range) are queried.
        var rows     = BuildFeatureRows(_allItems, hasWorkspace: _workspaceFolderPath is not null);
        var requests = BuildRequests(filteredItems);

        var pendingShas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var req in requests)
        {
            if (_statService.TryGetCached(req.Sha) is null)
                pendingShas.Add(req.Sha);
        }

        RefreshCanvasData(rows, requests, pendingShas, effectiveStart, effectiveEnd);

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
                    RefreshCanvasData(rows, requests, pendingShas, effectiveStart, effectiveEnd);
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
            RefreshCanvasData(rows, requests, pendingShas, effectiveStart, effectiveEnd);
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
            .Select(c =>
            {
                string? cachedGroup = null;
                _categoryCache?.TryGetGroup(c.sha, out cachedGroup);
                return new CommitStatRequest(
                    c.sha, cachedGroup,
                    DateOnly.FromDateTime(c.time.LocalDateTime),
                    CommitTime: c.time);
            })
            .ToList();
    }

    private async Task<List<(string sha, DateTimeOffset time)>> RunGitLogAsync(
        DateOnly          startDate,
        DateOnly          endDate,
        CancellationToken ct)
    {
        var since = startDate.AddDays(-1).ToString("yyyy-MM-dd");
        var until = endDate.AddDays(1).ToString("yyyy-MM-dd");
        var args  = $"log --format=\"%h %aI\" --after={since} --until={until}";

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
    /// Parses output of <c>git log --format="%h %aI"</c>.
    /// Each line is an abbreviated SHA followed by a space and an ISO 8601 timestamp.
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

    // ── Canvas refresh ────────────────────────────────────────────────────────

    private void RefreshCanvasData(
        List<CommitActivityRow>   rows,
        List<CommitStatRequest>   requests,
        HashSet<string>           pendingShas,
        DateTimeOffset            startDt,
        DateTimeOffset            endDt)
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

        var displayRows = _showUncategorizedCheckBox?.IsChecked == false
            ? rows.Where(r => r.FeatureGroup is not null).ToList()
            : rows;

        if (_featureFilters.Length > 0)
        {
            displayRows = displayRows.Where(r =>
                _featureFilters.Any(f =>
                    (r.DisplayName ?? "").Contains(f, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        _canvas.SetData(displayRows, startDt, endDt, _isDark);

        // Set slider MinDate to the global oldest date across the full dataset so
        // the track is stable regardless of which filter is active (e.g. "Last Week"
        // must not shrink the left boundary and lock the handles).
        var todayMinus5Years = DateOnly.FromDateTime(DateTime.Today.AddYears(-5));
        var allKnownDates = _allItems
            .Select(i => DateOnly.FromDateTime(i.TurnStartedAt.LocalDateTime))
            .Concat(requests.Select(r => r.TurnDate))
            .ToList();
        if (allKnownDates.Count > 0)
        {
            var oldest = allKnownDates.Min();
            _rangeSlider.MinDate = oldest > todayMinus5Years ? oldest : todayMinus5Years;
        }
        else
        {
            _rangeSlider.MinDate = todayMinus5Years;
        }
        _rangeSlider.InvalidateVisual();

        // Snapshot for filter-only refreshes (e.g. toggling Show Uncategorized).
        _cachedRows        = rows;
        _cachedRequests    = requests;
        _cachedPendingShas = new HashSet<string>(pendingShas, StringComparer.OrdinalIgnoreCase);
        _cachedStartDate   = startDt;
        _cachedEndDate     = endDt;

        // Update filter box max width to match the widest feature name.
        if (_featureFilterBox is not null)
        {
            var maxNameWidth = _cachedRows
                .Select(r => MeasureTextWidth(r.DisplayName, 12.0))
                .DefaultIfEmpty(CommitActivityCanvas.LabelColumnWidth)
                .Max();
            _featureFilterBox.MaxWidth = Math.Max(80, Math.Min(maxNameWidth + 16, CommitActivityCanvas.LabelColumnWidth));
        }
    }

    private Panel BuildFeatureFilterWidget()
    {
        _featureFilterBox = new TextBox
        {
            BorderThickness   = new Thickness(0),
            Padding           = new Thickness(2, 1, 2, 1),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth          = CommitActivityCanvas.LabelColumnWidth,
            MinWidth          = 40,
        };
        _featureFilterBox.SetResourceReference(TextBox.ForegroundProperty,  "LabelText");
        _featureFilterBox.SetResourceReference(TextBox.FontSizeProperty,    "FontSizeBody");
        _featureFilterBox.Background = Brushes.Transparent;
        WindowChrome.SetIsHitTestVisibleInChrome(_featureFilterBox, true);

        var placeholder = new TextBlock
        {
            Text              = "(filter)",
            IsHitTestVisible  = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(4, 1, 2, 1),
        };
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        placeholder.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");

        var filterGrid = new Grid { MaxWidth = CommitActivityCanvas.LabelColumnWidth };
        filterGrid.Children.Add(placeholder);
        filterGrid.Children.Add(_featureFilterBox);

        _featureFilterClear = new Button
        {
            Content           = "×",
            Padding           = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility        = Visibility.Collapsed,
        };
        _featureFilterClear.SetResourceReference(Button.StyleProperty,    "PanelFilterClearButtonStyle");
        _featureFilterClear.SetResourceReference(Button.FontSizeProperty, "FontSizeBody");
        WindowChrome.SetIsHitTestVisibleInChrome(_featureFilterClear, true);
        _featureFilterClear.Click += (_, _) => { _featureFilterBox.Text = ""; _canvas.ClearRowSelection(); };

        var container = new StackPanel
        {
            Orientation       = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin            = new Thickness(2, 2, 0, 2),
        };
        WindowChrome.SetIsHitTestVisibleInChrome(container, true);
        container.Children.Add(filterGrid);
        container.Children.Add(_featureFilterClear);

        _featureFilterBox.TextChanged += (_, _) =>
        {
            var text = _featureFilterBox.Text;
            placeholder.Visibility       = string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
            _featureFilterClear.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            _featureFilters = text
                .Split(';')
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToArray();
            ApplyFeatureFilter();
        };

        return container;
    }

    private void ApplyFeatureFilter()
    {
        if (_cachedRows is null || _cachedRequests is null) return;
        RefreshCanvasData(_cachedRows, _cachedRequests, _cachedPendingShas,
                          _cachedStartDate, _cachedEndDate);
    }

    private static double MeasureTextWidth(string? text, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);
        return ft.Width;
    }

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
            rows.Add(new CommitActivityRow(named[i], named[i], (i % 6) + 1));

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
                    DateOnly.FromDateTime(first.TurnStartedAt.LocalDateTime),
                    TurnStartedAt: first.TurnStartedAt);
            })
            .ToList();

    // ── AI categorization ─────────────────────────────────────────────────────

    private void ApplyCacheTo(List<CommitApprovalItem> items)
    {
        if (_categoryCache is null) return;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].FeatureGroup is not null) continue;
            if (_categoryCache.TryGetGroup(items[i].CommitSha, out var group) && group is not null)
                items[i] = items[i] with { FeatureGroup = group };
        }
    }

    private void StartSpinner(string baseText)
    {
        _spinnerBaseText = baseText;
        _spinnerFrame    = 0;
        if (_categorizeStatusText is not null)
            _categorizeStatusText.Text = $"{SpinnerFrames[0]} {baseText}";
        if (_spinnerTimer is null)
        {
            _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _spinnerTimer.Tick += (_, _) =>
            {
                _spinnerFrame++;
                if (_categorizeStatusText is not null)
                    _categorizeStatusText.Text = $"{SpinnerFrames[_spinnerFrame % SpinnerFrames.Length]} {_spinnerBaseText}";
            };
        }
        _spinnerTimer.Start();
    }

    private void StopSpinner() => _spinnerTimer?.Stop();

    private async void OnCategorizeButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_categorizationService is null || _categorizationInFlight) return;
        _categorizationInFlight = true;
        if (_categorizeButton is not null)   _categorizeButton.IsEnabled = false;
        if (_categorizeStatusText is not null)
            _categorizeStatusText.Visibility = Visibility.Visible;
        StartSpinner("Categorizing\u2026");

        try
        {
            // Uncategorized items from the approvals list
            var approvalUncategorized = _allItems
                .Where(i => i.FeatureGroup is null)
                .OrderByDescending(i => i.TurnStartedAt)
                .Take(100)
                .Select(i => (i.CommitSha, i.Description))
                .ToList();

            // Uncategorized commits from git history (loaded but not in _allItems)
            var allItemsShas = new HashSet<string>(_allItems.Select(i => i.CommitSha), StringComparer.OrdinalIgnoreCase);
            var gitUncategorized = (_cachedRequests ?? [])
                .Where(r => r.FeatureGroupId is null && !allItemsShas.Contains(r.Sha))
                .Select(r =>
                {
                    var result = _statService.TryGetCached(r.Sha);
                    var desc = result?.Message ?? r.Sha;
                    return (r.Sha, desc);
                })
                .Where(t => t.desc is not null)
                .Take(100 - approvalUncategorized.Count)
                .ToList();

            var uncategorized = approvalUncategorized.Concat(gitUncategorized).ToList();

            if (uncategorized.Count == 0)
            {
                StopSpinner();
                if (_categorizeStatusText is not null)
                {
                    _categorizeStatusText.Text = "Nothing to categorize.";
                    _ = Task.Delay(2000).ContinueWith(_ =>
                        Dispatcher.Invoke(() => _categorizeStatusText.Visibility = Visibility.Collapsed));
                }
                return;
            }

            var configuredGroups = _getFeatureGroups?.Invoke()
                ?? FeatureGroupStore.Defaults;
            var groupUsages = FeatureGroupPromptBuilder.BuildUsages(configuredGroups, _allItems);

            if (_categorizeStatusText is not null)
                _spinnerBaseText = $"Categorizing {uncategorized.Count} commits\u2026";

            var results = await Task.Run(() =>
                _categorizationService.CategorizeAsync(uncategorized, groupUsages))
                .ConfigureAwait(true); // back on UI thread

            if (results.Count == 0)
            {
                StopSpinner();
                if (_categorizeStatusText is not null)
                    _categorizeStatusText.Text = "No categories returned.";
            }
            else
            {
                var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (sha, group) in results)
                    lookup[sha] = group;

                for (int i = 0; i < _allItems.Count; i++)
                {
                    if (_allItems[i].FeatureGroup is not null) continue;
                    string? group = null;
                    if (!lookup.TryGetValue(_allItems[i].CommitSha, out group))
                    {
                        foreach (var kv in lookup)
                        {
                            if (_allItems[i].CommitSha.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase) ||
                                kv.Key.StartsWith(_allItems[i].CommitSha, StringComparison.OrdinalIgnoreCase))
                            {
                                group = kv.Value;
                                break;
                            }
                        }
                    }
                    if (group is not null)
                        _allItems[i] = _allItems[i] with { FeatureGroup = group };
                }

                if (_categoryCache is not null)
                {
                    foreach (var (sha, group) in results)
                        _categoryCache.SetGroup(sha, group);
                    _categoryCache.Save();
                }

                _onCategoriesAssigned?.Invoke(results);

                StartLoadingData();

                StopSpinner();
                if (_categorizeStatusText is not null)
                    _categorizeStatusText.Text = $"Categorized {results.Count} commits.";
            }

            _ = Task.Delay(3000).ContinueWith(_ =>
                Dispatcher.Invoke(() =>
                {
                    if (_categorizeStatusText is not null)
                        _categorizeStatusText.Visibility = Visibility.Collapsed;
                }));
        }
        catch (Exception ex)
        {
            StopSpinner();
            SquadDashTrace.Write("CommitViewer", $"Categorization failed: {ex.Message}");
            if (_categorizeStatusText is not null)
                _categorizeStatusText.Text = "Categorization failed.";
        }
        finally
        {
            _categorizationInFlight = false;
            if (_categorizeButton is not null) _categorizeButton.IsEnabled = true;
        }
    }
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
    public int      MinRangeDays { get; set; } = 1;

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

    /// <summary>
    /// Programmatically sets both handles, clamping to [MinDate, MaxDate] and
    /// enforcing <see cref="MinRangeDays"/>.  Triggers an immediate repaint.
    /// </summary>
    public void SetRange(DateOnly start, DateOnly end)
    {
        start = ClampDate(start, MinDate, MaxDate);
        end   = ClampDate(end,   MinDate, MaxDate);
        if (end.DayNumber - start.DayNumber < MinRangeDays)
            end = ClampDate(start.AddDays(MinRangeDays), MinDate, MaxDate);
        StartDate = start;
        EndDate   = end;
        InvalidateVisual();
    }

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
    CommitActivityRow       Row,
    DateOnly                Date,
    bool                    IsPending,
    CommitStatResult?       Commit);   // null when IsPending

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
/// Commit markers are rendered as rounded rectangles whose width spans from the turn-start
/// time to the commit time on the timeline, giving a visual indication of session duration.
/// A fixed-width fallback is used when precise timestamps are unavailable.
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
    internal const double RectHeight          = 10.0;
    internal const double MinRectWidth        = 3.0;
    internal const double CornerRadius        = 2.0;
    internal const double MaxBarDurationHours = 8.0 / 60.0; // clamp against stale/corrupt TurnStartedAt data (~8 min)
    // Extra canvas space reserved above and below the row band so tall commit bars
    // on the first/last feature row are not clipped at the canvas layout boundary.
    private const double VerticalPadding      = RowHeight;

    // ── State ──────────────────────────────────────────────────────────────────
    private List<CommitActivityRow> _rows      = [];
    private DateOnly                _startDate;
    private DateOnly                _endDate;
    private DateTimeOffset          _viewStart;
    private DateTimeOffset          _viewEnd;
    private bool                    _isDark;
    private double                  _dayCount;
    private double                  _pixelsPerDip        = 1.0;
    private double                  _effectivePixelsPerDay = FallbackPixelsPerDay;

    // ── Selection overlay ──────────────────────────────────────────────────────
    private double?         _selectionStartX;
    private double?         _selectionEndX;
    private DateTimeOffset? _selectionStartDateTime;
    private DateTimeOffset? _selectionEndDateTime;

    // ── Row (label-column) multi-select ────────────────────────────────────────
    private HashSet<int> _selectedRowIndices = new();
    private int          _anchorRowIndex     = -1;
    private int          _focusRowIndex      = -1;

    // ── Hover popup ────────────────────────────────────────────────────────────
    private Popup?     _hoverPopup;
    private TextBlock? _hoverContent;

    // ── Constructor ────────────────────────────────────────────────────────────

    public CommitActivityCanvas()
    {
        SizeChanged += (_, _) => InvalidateVisual();
    }

    // Make the entire canvas surface hittable (not just rendered pixels) so
    // OnMouseMove fires everywhere and the popup shows over dots and lines.
    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        => new PointHitTestResult(this, hitTestParameters.HitPoint);

    // ── Hover popup ────────────────────────────────────────────────────────────

    private void EnsurePopup()
    {
        if (_hoverPopup is not null) return;
        _hoverContent = new TextBlock
        {
            MaxWidth     = 340,
            TextWrapping = TextWrapping.Wrap,
            Padding      = new Thickness(6, 4, 6, 4),
        };
        _hoverContent.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        _hoverContent.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        var border = new Border
        {
            Child          = _hoverContent,
            CornerRadius   = new CornerRadius(4),
            Padding        = new Thickness(0),
            BorderThickness = new Thickness(1),
        };
        border.SetResourceReference(Border.BackgroundProperty,  "InputSurface");
        border.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");

        _hoverPopup = new Popup
        {
            Child              = border,
            AllowsTransparency = true,
            Placement          = PlacementMode.Mouse,
            HorizontalOffset   = 12,
            VerticalOffset     = 12,
            StaysOpen          = true,   // We control open/close manually
            IsHitTestVisible   = false,
            PlacementTarget    = this,
        };
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        EnsurePopup();
        var hit = HitTestPoint(e.GetPosition(this));
        if (hit is null)
        {
            _hoverPopup!.IsOpen = false;
        }
        else
        {
            PopulateTooltipInlines(_hoverContent!, hit);
            _hoverPopup!.IsOpen = true;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverPopup is not null) _hoverPopup.IsOpen = false;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pt = e.GetPosition(this);

        if (pt.X < LabelColumnWidth)
        {
            var rowY     = pt.Y - VerticalPadding;
            var rowIndex = (int)(rowY / RowHeight);

            if (rowY < 0 || rowIndex < 0 || rowIndex >= _rows.Count)
            {
                _selectedRowIndices.Clear();
                _anchorRowIndex = -1;
                _focusRowIndex  = -1;
                InvalidateVisual();
                RowSelectionChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            var ctrl  = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift)   != 0;

            if (ctrl && shift)
            {
                if (_anchorRowIndex < 0) _anchorRowIndex = rowIndex;
                int lo = Math.Min(_anchorRowIndex, rowIndex);
                int hi = Math.Max(_anchorRowIndex, rowIndex);
                for (int r = lo; r <= hi; r++)
                    _selectedRowIndices.Remove(r);
                _focusRowIndex = rowIndex;
            }
            else if (shift)
            {
                if (_anchorRowIndex < 0) _anchorRowIndex = rowIndex;
                int lo = Math.Min(_anchorRowIndex, rowIndex);
                int hi = Math.Max(_anchorRowIndex, rowIndex);
                for (int r = lo; r <= hi; r++)
                    _selectedRowIndices.Add(r);
                _focusRowIndex = rowIndex;
            }
            else if (ctrl)
            {
                if (_selectedRowIndices.Contains(rowIndex))
                {
                    _selectedRowIndices.Remove(rowIndex);
                }
                else
                {
                    _selectedRowIndices.Add(rowIndex);
                    _anchorRowIndex = rowIndex;
                }
                _focusRowIndex = rowIndex;
            }
            else
            {
                _selectedRowIndices = [rowIndex];
                _anchorRowIndex = rowIndex;
                _focusRowIndex  = rowIndex;
            }

            InvalidateVisual();
            RowSelectionChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public void SetData(
        List<CommitActivityRow> rows,
        DateTimeOffset          startDt,
        DateTimeOffset          endDt,
        bool                    isDark)
    {
        _rows      = rows;
        _viewStart = startDt;
        _viewEnd   = endDt;
        _startDate = DateOnly.FromDateTime(startDt.LocalDateTime);
        _endDate   = DateOnly.FromDateTime(endDt.LocalDateTime);
        _isDark    = isDark;
        _dayCount  = Math.Max(1.0 / 48.0, (endDt - startDt).TotalDays);

        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetTheme(bool isDark)
    {
        _isDark = isDark;
        InvalidateVisual();
    }

    public void SetSelection(double? x1, double? x2) { _selectionStartX = x1; _selectionEndX = x2; _selectionStartDateTime = null; _selectionEndDateTime = null; InvalidateVisual(); }
    public void SetSelectionWithTimes(double? x1, double? x2, DateTimeOffset? dt1, DateTimeOffset? dt2) { _selectionStartX = x1; _selectionEndX = x2; _selectionStartDateTime = dt1; _selectionEndDateTime = dt2; InvalidateVisual(); }
    public void ClearSelection() { _selectionStartX = null; _selectionEndX = null; _selectionStartDateTime = null; _selectionEndDateTime = null; InvalidateVisual(); }
    public bool   HasSelection   => _selectionStartX.HasValue && _selectionEndX.HasValue;
    public double SelectionXMin  => Math.Min(_selectionStartX!.Value, _selectionEndX!.Value);
    public double SelectionXMax  => Math.Max(_selectionStartX!.Value, _selectionEndX!.Value);
    public double PixelsPerDay   => _effectivePixelsPerDay;

    // ── Row selection public API ────────────────────────────────────────────────
    public event EventHandler? RowSelectionChanged;
    public IReadOnlySet<int> SelectedRowIndices => _selectedRowIndices;
    public void ClearRowSelection()
    {
        _selectedRowIndices.Clear();
        _anchorRowIndex = -1;
        _focusRowIndex  = -1;
        InvalidateVisual();
    }
    public string? GetRowDisplayName(int index) =>
        index >= 0 && index < _rows.Count ? _rows[index].DisplayName : null;

    // ── Measure / Arrange ──────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        var h = Math.Max(1, _rows.Count) * RowHeight + XAxisHeight + VerticalPadding;
        // Fill the available width so no horizontal scrollbar is needed.
        var w = double.IsFinite(availableSize.Width) && availableSize.Width > LabelColumnWidth
            ? availableSize.Width
            : LabelColumnWidth + Math.Max(1.0, _dayCount) * FallbackPixelsPerDay;
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
        if (_dayCount <= 0) return;

        // Compute pixels-per-day to fit the entire range in the canvas width.
        var canvasWidth = ActualWidth - LabelColumnWidth;
        _effectivePixelsPerDay = canvasWidth > 0 ? canvasWidth / _dayCount : FallbackPixelsPerDay;

        var palette     = _isDark ? CommitActivityGraphWindow.DarkPalette : CommitActivityGraphWindow.LightPalette;
        var textBrush          = TryFindBrush("LabelText")     ?? Brushes.Black;
        var subtleBrush        = TryFindBrush("SubtleText")    ?? Brushes.Gray;
        var importantTextBrush = TryFindBrush("ImportantText") ?? Brushes.White;
        var borderBrush        = TryFindBrush("PanelBorder")   ?? Brushes.LightGray;

        // Background
        dc.DrawRectangle(
            TryFindBrush("AppSurface") ?? Brushes.Transparent,
            null,
            new Rect(0, 0, ActualWidth, ActualHeight));

        // Shift all row content down by VerticalPadding so tall commit bars on
        // row 0 have room to draw above without hitting the canvas layout boundary.
        dc.PushTransform(new TranslateTransform(0, VerticalPadding));

        // Vertical separator between label column and graph area
        dc.DrawLine(
            new Pen(borderBrush, 1),
            new Point(LabelColumnWidth - 0.5, 0),
            new Point(LabelColumnWidth - 0.5, _rows.Count * RowHeight));

        // Pass 1: draw row separators and feature labels (outside graph-area clip so
        // labels at x < LabelColumnWidth are not hidden).
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var cy  = i * RowHeight + RowHeight / 2.0;

            if (i > 0)
            {
                var sepPen = new Pen(borderBrush, 0.5) { DashStyle = DashStyles.Dot };
                dc.DrawLine(sepPen, new Point(0, i * RowHeight), new Point(ActualWidth, i * RowHeight));
            }

            // Selection background
            if (_selectedRowIndices.Contains(i))
            {
                var selBrush = TryFindBrush("DocEditorSelectionBrush");
                if (selBrush is SolidColorBrush scbSel)
                {
                    var halfOpacity = new SolidColorBrush(Color.FromArgb(128, scbSel.Color.R, scbSel.Color.G, scbSel.Color.B));
                    dc.DrawRectangle(halfOpacity, null, new Rect(0, i * RowHeight, LabelColumnWidth, RowHeight));
                }
                else
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(128, 0x29, 0x96, 0xFF)), null,
                        new Rect(0, i * RowHeight, LabelColumnWidth, RowHeight));
                }
            }

            // Focus dotted border (last-clicked row)
            if (i == _focusRowIndex && _selectedRowIndices.Count > 0)
            {
                var focusColor = _isDark ? Color.FromArgb(64, 255, 255, 255) : Color.FromArgb(64, 0, 0, 0);
                var focusPen = new Pen(new SolidColorBrush(focusColor), 1.0)
                {
                    DashStyle = new DashStyle(new double[] { 3, 3 }, 0)
                };
                dc.DrawRectangle(null, focusPen, new Rect(1, i * RowHeight + 1, LabelColumnWidth - 2, RowHeight - 2));
            }

            var labelFt = MakeText(row.DisplayName, textBrush, 12);
            dc.PushClip(new RectangleGeometry(new Rect(4, i * RowHeight + 2, LabelColumnWidth - 8, RowHeight - 4)));
            dc.DrawText(labelFt, new Point(8, cy - labelFt.Height / 2.0));
            dc.Pop();
        }

        // Clip horizontally so markers never bleed into the label column.
        // The vertical range is generous: VerticalPadding above row 0 and one row
        // below the last row so tall bars are never cropped.
        dc.PushClip(new RectangleGeometry(new Rect(
            LabelColumnWidth, -VerticalPadding,
            Math.Max(0, ActualWidth - LabelColumnWidth),
            (_rows.Count + 1) * RowHeight + VerticalPadding)));

        // ── Vertical grid lines (day/week/month/quarter/year) ─────────────────
        // All lines are 1px wide. A type is suppressed when spacing < 50px.
        // Opacity steps up from finest visible unit: 20% / 40% / 60% / 80%+.
        // Each date is drawn at its coarsest matching boundary level.
        const double MinGridSpacing = 50.0;
        // Approximate spacings for visibility pre-check (fine for threshold math).
        bool dayVisible     = _effectivePixelsPerDay          >= MinGridSpacing;
        bool weekVisible    = 7.0   * _effectivePixelsPerDay  >= MinGridSpacing;
        bool monthVisible   = 30.4  * _effectivePixelsPerDay  >= MinGridSpacing;
        bool quarterVisible = 91.3  * _effectivePixelsPerDay  >= MinGridSpacing;
        bool yearVisible    = 365.25 * _effectivePixelsPerDay >= MinGridSpacing;

        // Assign opacity per level: level 0=day … 4=year, finest visible → rank 0 → 20%.
        bool[] levelVisible = { dayVisible, weekVisible, monthVisible, quarterVisible, yearVisible };
        // 20/40/60/80% expressed as byte (0-255)
        byte[] rankAlpha = { 51, 102, 153, 204 };
        var levelAlpha = new byte[5];
        int opRank = 0;
        for (int li = 0; li < 5; li++)
        {
            if (!levelVisible[li]) continue;
            levelAlpha[li] = rankAlpha[Math.Min(opRank, rankAlpha.Length - 1)];
            opRank++;
        }

        var gridBase   = _isDark ? Colors.White : Colors.Black;
        var gridHeight = _rows.Count * RowHeight;
        for (var d = _startDate; d <= _endDate; d = d.AddDays(1))
        {
            // Classify at coarsest matching boundary.
            int level;
            if      (d.Day == 1 && d.Month == 1)                                        level = 4; // year
            else if (d.Day == 1 && (d.Month == 4 || d.Month == 7 || d.Month == 10))     level = 3; // quarter
            else if (d.Day == 1)                                                         level = 2; // month
            else if (d.DayOfWeek == DayOfWeek.Monday)                                   level = 1; // week
            else                                                                         level = 0; // day

            var alpha = levelAlpha[level];
            if (alpha == 0) continue;

            var gridColor = Color.FromArgb(alpha, gridBase.R, gridBase.G, gridBase.B);
            var gx = LabelColumnWidth + DayToX(d);
            dc.DrawLine(new Pen(new SolidColorBrush(gridColor), 1.0),
                new Point(gx, 0),
                new Point(gx, gridHeight));

            // Draw a date label just to the right of the line at the top of the graph.
            // Month: always (e.g. "Aug 1"). Week: when spacing ≥ 80px (e.g. "Jun 16").
            // Day: when spacing ≥ 60px (e.g. "Mon Jun 16").
            string? labelText = level switch
            {
                4 => null,  // year lines: the month label on Jan 1 is enough
                3 => null,  // quarter lines: month label covers it
                2 => $"{d:MMM d}",                                                // "Aug 1"
                1 when 7.0 * _effectivePixelsPerDay >= 80.0  => $"{d:MMM d}",    // "Jun 16"
                0 when _effectivePixelsPerDay         >= 60.0 => $"{d:ddd MMM d}", // "Mon Jun 16"
                _ => null
            };
            if (labelText is not null)
            {
                var labelBrush = new SolidColorBrush(Color.FromArgb(alpha, gridBase.R, gridBase.G, gridBase.B));
                var ft = MakeText(labelText, labelBrush, 9.5);
                dc.DrawText(ft, new Point(gx + 2, 2));
            }
        }

        // ── Sub-day grid lines (when viewing 1 day or less) ───────────────────
        if (_dayCount <= 1.0)
        {
            bool halfHourGrid = _effectivePixelsPerDay >= 2000.0;
            var  gridMinutes  = halfHourGrid ? 30 : 60;
            var  subDayAlpha  = (byte)153;
            var  subDayColor  = Color.FromArgb(subDayAlpha, gridBase.R, gridBase.G, gridBase.B);
            var  subDayPen    = new Pen(new SolidColorBrush(subDayColor), 1.0);
            var  subDayBrush  = new SolidColorBrush(subDayColor);

            // First aligned tick at or after _viewStart
            var startMins    = _viewStart.Hour * 60 + _viewStart.Minute;
            var firstAligned = (int)Math.Ceiling((double)startMins / gridMinutes) * gridMinutes;
            var firstGrid    = new DateTimeOffset(
                _viewStart.Year, _viewStart.Month, _viewStart.Day,
                0, 0, 0, _viewStart.Offset).AddMinutes(firstAligned);

            for (var cur = firstGrid; cur <= _viewEnd; cur = cur.AddMinutes(gridMinutes))
            {
                var gx = LabelColumnWidth + (cur - _viewStart).TotalDays * _effectivePixelsPerDay;
                dc.DrawLine(subDayPen, new Point(gx, 0), new Point(gx, gridHeight));
                var ft = MakeText(cur.LocalDateTime.ToString("h:mm tt"), subDayBrush, 9.5);
                dc.DrawText(ft, new Point(gx + 2, 2));
            }
        }

        // ── Selection overlay (behind commit bars) ────────────────────────────
        if (HasSelection)
        {
            var selXMin     = SelectionXMin;
            var selXMax     = SelectionXMax;
            var graphHeight = _rows.Count * RowHeight;
            // Leave a gap at the top so the datetime labels sit above the selection fill.
            const double LabelAreaHeight = 20.0;
            var selectionTop = -VerticalPadding + LabelAreaHeight;
            var selectionH   = graphHeight + VerticalPadding - LabelAreaHeight;
            var fillBrush   = new SolidColorBrush(Color.FromArgb(128, 0x29, 0x96, 0xFF));
            var linePen     = new Pen(new SolidColorBrush(Color.FromArgb(255, 0x29, 0x96, 0xFF)), 1.5);
            dc.DrawRectangle(fillBrush, null,
                new Rect(selXMin, selectionTop, selXMax - selXMin, selectionH));
            dc.DrawLine(linePen, new Point(selXMin, selectionTop), new Point(selXMin, graphHeight));
            dc.DrawLine(linePen, new Point(selXMax, selectionTop), new Point(selXMax, graphHeight));

            // ── DateTime labels above selection boundaries ────────────────────
            if (_selectionStartDateTime.HasValue || _selectionEndDateTime.HasValue)
            {
                const double labelY = -VerticalPadding + 2;
                var startIsMin = (_selectionStartX ?? 0) <= (_selectionEndX ?? 0);
                var minDt = startIsMin ? _selectionStartDateTime : _selectionEndDateTime;
                var maxDt = startIsMin ? _selectionEndDateTime   : _selectionStartDateTime;

                if (selXMax - selXMin < 80)
                {
                    // Lines are close — show one merged label at the midpoint
                    var dt = minDt ?? maxDt;
                    if (dt.HasValue)
                    {
                        var ft = MakeText(dt.Value.ToString("MMM d  h:mm tt"), importantTextBrush, 9.5);
                        var lx = (selXMin + selXMax) / 2.0 - ft.Width / 2.0;
                        lx = Math.Max(LabelColumnWidth + 2, Math.Min(lx, ActualWidth - ft.Width - 2));
                        dc.DrawText(ft, new Point(lx, labelY));
                    }
                }
                else
                {
                    if (minDt.HasValue)
                    {
                        var ft = MakeText(minDt.Value.ToString("MMM d  h:mm tt"), importantTextBrush, 9.5);
                        var lx = selXMin - ft.Width / 2.0;
                        lx = Math.Max(LabelColumnWidth + 2, Math.Min(lx, ActualWidth - ft.Width - 2));
                        dc.DrawText(ft, new Point(lx, labelY));
                    }
                    if (maxDt.HasValue)
                    {
                        var ft = MakeText(maxDt.Value.ToString("MMM d  h:mm tt"), importantTextBrush, 9.5);
                        var lx = selXMax - ft.Width / 2.0;
                        lx = Math.Max(LabelColumnWidth + 2, Math.Min(lx, ActualWidth - ft.Width - 2));
                        dc.DrawText(ft, new Point(lx, labelY));
                    }
                }
            }
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            var row   = _rows[i];
            var cy    = i * RowHeight + RowHeight / 2.0;
            var color = palette[row.ColorIndex % 7];

            // ── Full-width guide line (always drawn) ──────────────────────────
            byte guideAlpha = _selectedRowIndices.Contains(i) ? (byte)255 : (byte)128;
            var guideColor = Color.FromArgb(guideAlpha, color.R, color.G, color.B);
            dc.DrawLine(new Pen(new SolidColorBrush(guideColor), 1.0),
                new Point(LabelColumnWidth, cy),
                new Point(ActualWidth, cy));

            // No commits in this row at all — skip activity rendering
            var allDates = row.CommitsByDay.Keys.Concat(row.PendingDays).ToList();
            if (allDates.Count == 0) continue;

            var firstDate = allDates.Min();
            var lastDate  = allDates.Max();
            var x1        = DayToX(firstDate);
            var x2        = DayToX(lastDate);

            // ── Day-span lines (2px, 100% opacity) — drawn when 2+ commits in a day ──────
            var spanPen = new Pen(new SolidColorBrush(color), 2.0);
            foreach (var (date, commits) in row.CommitsByDay)
            {
                if (date < _startDate || date > _endDate) continue;
                if (commits.Count < 2) continue;

                double minX = double.MaxValue;
                double maxX = double.MinValue;
                var dayCx   = LabelColumnWidth + DayToX(date);

                foreach (var commit in commits)
                {
                    double left, right;
                    if (commit.TurnStartedAt.HasValue && commit.CommitTime.HasValue)
                    {
                        left  = LabelColumnWidth + DateTimeToX(commit.TurnStartedAt.Value);
                        right = LabelColumnWidth + DateTimeToX(commit.CommitTime.Value);
                        if (right < left) (left, right) = (right, left);
                        var maxBarPx = _effectivePixelsPerDay * MaxBarDurationHours / 24.0;
                        if (right - left > maxBarPx) left = right - maxBarPx;
                        if (right - left < MinRectWidth)
                        {
                            var mid = (left + right) / 2.0;
                            left  = mid - MinRectWidth / 2.0;
                            right = mid + MinRectWidth / 2.0;
                        }
                    }
                    else if (commit.CommitTime.HasValue)
                    {
                        right = LabelColumnWidth + DateTimeToX(commit.CommitTime.Value);
                        left  = right - _effectivePixelsPerDay * MaxBarDurationHours / 24.0;
                    }
                    else
                    {
                        left  = dayCx - BaseRadius;
                        right = dayCx + BaseRadius;
                    }
                    if (left  < minX) minX = left;
                    if (right > maxX) maxX = right;
                }

                if (maxX > minX)
                    dc.DrawLine(spanPen, new Point(minX, cy), new Point(maxX, cy));
            }

            // ── Pending (hollow) rounded rectangles ───────────────────────────
            var pendingColor = Color.FromArgb(128, color.R, color.G, color.B);
            var pendingPen   = new Pen(new SolidColorBrush(pendingColor), 1.5);
            foreach (var date in row.PendingDays)
            {
                if (date < _startDate || date > _endDate) continue;
                if (row.CommitsByDay.ContainsKey(date)) continue; // solid rect takes priority
                var cx      = LabelColumnWidth + DayToX(date);
                var rectTop = cy - RectHeight / 2.0;
                dc.DrawRoundedRectangle(null, pendingPen,
                    new Rect(cx - BaseRadius, rectTop, BaseRadius * 2, RectHeight),
                    CornerRadius, CornerRadius);
            }

            // ── Resolved (solid) rounded rectangles ───────────────────────────
            // Each commit gets a rounded rect whose width spans turn-start → commit-time;
            // falls back to a fixed-width rect centered on the day when timestamps are absent.
            // Height is scaled logarithmically by lines changed (2px min → 48px max at ≥1000 lines).
            // Opacity is scaled linearly by files changed: 1 file → 20%, ≥6 files → 100%.
            foreach (var (date, commits) in row.CommitsByDay)
            {
                if (date < _startDate || date > _endDate) continue;
                var dayCx = LabelColumnWidth + DayToX(date);
                foreach (var commit in commits)
                {
                    var fileAlpha = (byte)(int)Math.Round(
                        51 + Math.Clamp((commit.FilesChanged - 1) / 5.0, 0.0, 1.0) * 204);
                    var fillBrush = new SolidColorBrush(Color.FromArgb(fileAlpha, color.R, color.G, color.B));
                    var strokePen = new Pen(new SolidColorBrush(Color.FromArgb(fileAlpha, color.R, color.G, color.B)), 1.0);
                    double left, right;
                    double minWidth;
                    if (commit.TurnStartedAt.HasValue && commit.CommitTime.HasValue)
                    {
                        left  = LabelColumnWidth + DateTimeToX(commit.TurnStartedAt.Value);
                        right = LabelColumnWidth + DateTimeToX(commit.CommitTime.Value);
                        if (right < left) (left, right) = (right, left);
                        var maxBarPx = _effectivePixelsPerDay * MaxBarDurationHours / 24.0;
                        if (right - left > maxBarPx) left = right - maxBarPx;
                        minWidth = MinRectWidth;
                    }
                    else if (commit.CommitTime.HasValue)
                    {
                        // No turn-start: infer 8-minute duration ending at commit time
                        right    = LabelColumnWidth + DateTimeToX(commit.CommitTime.Value);
                        left     = right - _effectivePixelsPerDay * MaxBarDurationHours / 24.0;
                        minWidth = 1.0;
                    }
                    else
                    {
                        left     = dayCx - BaseRadius;
                        right    = dayCx + BaseRadius;
                        minWidth = MinRectWidth;
                    }
                    if (right - left < minWidth)
                    {
                        var mid = (left + right) / 2.0;
                        left  = mid - minWidth / 2.0;
                        right = mid + minWidth / 2.0;
                    }
                    var rectH     = CommitRectHeight(commit);
                    var rectTop   = cy - rectH / 2.0;
                    var rectWidth = right - left;
                    dc.DrawRoundedRectangle(
                        fillBrush, strokePen,
                        new Rect(left, rectTop, rectWidth, rectH),
                        CornerRadius, CornerRadius);
                }
            }
        }

        dc.Pop(); // end graph-area clip
        dc.Pop(); // end VerticalPadding translate
        RenderXAxis(dc, subtleBrush, borderBrush);
    }

    private void RenderXAxis(DrawingContext dc, Brush textBrush, Brush tickBrush)
    {
        var axisY = VerticalPadding + _rows.Count * RowHeight;

        // Axis line
        dc.DrawLine(
            new Pen(tickBrush, 1),
            new Point(LabelColumnWidth, axisY),
            new Point(ActualWidth, axisY));

        if (_dayCount < 2.0)
        {
            // Sub-day axis: hour or half-hour ticks with time labels
            bool halfHourTicks  = (_effectivePixelsPerDay / 48.0) >= 50.0;
            var  intervalMinutes = halfHourTicks ? 30 : 60;

            var startMins    = _viewStart.Hour * 60 + _viewStart.Minute;
            var firstAligned = (int)Math.Ceiling((double)startMins / intervalMinutes) * intervalMinutes;
            var firstTick    = new DateTimeOffset(
                _viewStart.Year, _viewStart.Month, _viewStart.Day,
                0, 0, 0, _viewStart.Offset).AddMinutes(firstAligned);

            for (var cur = firstTick; cur <= _viewEnd; cur = cur.AddMinutes(intervalMinutes))
            {
                var x     = LabelColumnWidth + (cur - _viewStart).TotalDays * _effectivePixelsPerDay;
                var tickY = axisY + 4;
                dc.DrawLine(new Pen(tickBrush, 1), new Point(x, axisY), new Point(x, tickY));
                var label = cur.LocalDateTime.ToString("h:mm tt");
                var ft    = MakeText(label, textBrush, 10);
                dc.DrawText(ft, new Point(x - ft.Width / 2.0, tickY + 2));
            }
        }
        else
        {
            // Day/week/month axis — align to multiples of intervalDays from DayNumber epoch
            var intervalDays = _dayCount <= 90 ? 7 : 30;
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
    }

    // ── Coordinate helpers ─────────────────────────────────────────────────────

    /// <summary>Returns the X offset (relative to the graph area, i.e. after LabelColumnWidth) for a date.</summary>
    private double DayToX(DateOnly date)
    {
        var dt = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), _viewStart.Offset);
        return (dt - _viewStart).TotalDays * _effectivePixelsPerDay;
    }

    /// <summary>Returns the X offset (relative to the graph area) for a precise timestamp.</summary>
    private double DateTimeToX(DateTimeOffset dt)
    {
        return (dt - _viewStart).TotalDays * _effectivePixelsPerDay;
    }

    /// <summary>
    /// Returns the rect height for a commit, scaled logarithmically by lines changed.
    /// 0 lines → 2px; 1 line → 3px (minimum for real commits); ≥1000 lines → RowHeight × 1.75 (56px).
    /// Rects may extend 75% beyond the row boundary, which is intentional.
    /// </summary>
    private static double CommitRectHeight(CommitStatResult commit)
    {
        const double ZeroHeight = 2.0;
        const double MinHeight  = 3.0;
        const double MaxHeight  = RowHeight * 1.75;
        var totalLines = commit.Insertions + commit.Deletions;
        if (totalLines <= 0)    return ZeroHeight;
        if (totalLines >= 1000) return MaxHeight;
        var t = Math.Log(totalLines + 1.0) / Math.Log(1001.0);
        return MinHeight + (MaxHeight - MinHeight) * t;
    }

    // ── Tooltip / hit testing ──────────────────────────────────────────────────

    private object? HitTestPoint(Point pt)
    {
        if (_rows.Count == 0 || _dayCount <= 0) return null;
        if (pt.X < LabelColumnWidth) return null;
        var rowY = pt.Y - VerticalPadding;
        if (rowY < 0 || rowY > _rows.Count * RowHeight) return null;

        var rowIndex = (int)(rowY / RowHeight);
        if (rowIndex < 0 || rowIndex >= _rows.Count) return null;

        var row            = _rows[rowIndex];
        var graphX         = pt.X - LabelColumnWidth;
        var fractionalDays = graphX / _effectivePixelsPerDay;
        if (fractionalDays < 0 || fractionalDays > _dayCount) return null;

        var hoverDt = _viewStart.AddDays(fractionalDays);
        var date    = DateOnly.FromDateTime(hoverDt.LocalDateTime);

        const double hitTolerance = 4;

        // Check resolved commits — rectangle-based hit testing
        if (row.CommitsByDay.TryGetValue(date, out var commits))
        {
            var dayCx = DayToX(date);
            foreach (var commit in commits)
            {
                double left, right;
                if (commit.TurnStartedAt.HasValue && commit.CommitTime.HasValue)
                {
                    left  = DateTimeToX(commit.TurnStartedAt.Value);
                    right = DateTimeToX(commit.CommitTime.Value);
                    if (right < left) (left, right) = (right, left);
                    var maxBarPx = _effectivePixelsPerDay * MaxBarDurationHours / 24.0;
                    if (right - left > maxBarPx) left = right - maxBarPx;
                    if (right - left < MinRectWidth)
                    {
                        var mid = (left + right) / 2.0;
                        left  = mid - MinRectWidth / 2.0;
                        right = mid + MinRectWidth / 2.0;
                    }
                }
                else if (commit.CommitTime.HasValue)
                {
                    right = DateTimeToX(commit.CommitTime.Value);
                    left  = right - _effectivePixelsPerDay * MaxBarDurationHours / 24.0;
                }
                else
                {
                    left  = dayCx - BaseRadius - 4;
                    right = dayCx + BaseRadius + 4;
                }
                if (graphX >= left - 2 && graphX <= right + 2)
                    return new CommitDotHit(row, date, false, commit);
            }
        }

        // For pending dot and line hit, use the simple day-center distance
        var dotCx = DayToX(date); // offset from graph origin
        var dist  = Math.Abs(graphX - dotCx);

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
            if (graphX >= lineX1 && graphX <= lineX2 && Math.Abs(rowY - cy) <= 5)
                return new CommitLineHit(row, firstDate, lastDate);
        }

        return null;
    }

    /// <summary>
    /// Returns the <see cref="CommitActivityRow"/> at the given canvas-local Y position,
    /// or null if the point is outside all rows.
    /// </summary>
    internal CommitActivityRow? HitTestRow(Point canvasPoint)
    {
        if (_rows.Count == 0) return null;
        if (canvasPoint.Y > VerticalPadding + _rows.Count * RowHeight) return null;
        var rowIndex = (int)((canvasPoint.Y - VerticalPadding) / RowHeight);
        if (rowIndex < 0 || rowIndex >= _rows.Count) return null;
        return _rows[rowIndex];
    }

    private static void PopulateTooltipInlines(TextBlock tb, object hit)
    {
        tb.Inlines.Clear();
        switch (hit)
        {
            case CommitDotHit { IsPending: true } d:
                tb.Inlines.Add(new Run(
                    $"Feature: {d.Row.DisplayName}\nDate: {d.Date:MMM d, yyyy}\nStatus: Loading commit data\u2026"));
                break;
            case CommitDotHit { IsPending: false } d when d.Commit is { } c:
                PopulateCommitInlines(tb, d.Row.DisplayName, c);
                break;
            case CommitLineHit l:
                tb.Inlines.Add(new Run(
                    $"Feature: {l.Row.DisplayName}\nActive: {l.FirstDate:MMM d, yyyy} \u2192 {l.LastDate:MMM d, yyyy}"));
                break;
        }
    }

    private static void PopulateCommitInlines(TextBlock tb, string featureName, CommitStatResult c)
    {
        // Subject line: bold, capped at ~22 chars (≈150px at body font)
        const int MaxSubjectChars = 50;
        if (!string.IsNullOrEmpty(c.Message))
        {
            var subject = c.Message.Length > MaxSubjectChars
                ? c.Message[..MaxSubjectChars] + "\u2026"
                : c.Message;
            tb.Inlines.Add(new Run(subject) { FontWeight = FontWeights.Bold });
            tb.Inlines.Add(new LineBreak());
        }

        var sha  = c.Sha.Length >= 7 ? c.Sha[..7] : c.Sha;
        var body = new System.Text.StringBuilder();
        body.Append($"Feature: {featureName}");
        body.Append($"\nCommit:  {sha}");
        if (c.CommitTime.HasValue)
            body.Append($"\nTime:    {c.CommitTime.Value.LocalDateTime:MMM d, yyyy  h:mm tt}");
        else
            body.Append($"\nDate:    {c.TurnDate:MMM d, yyyy}");
        if (c.TurnStartedAt.HasValue && c.CommitTime.HasValue)
        {
            var elapsed = c.CommitTime.Value - c.TurnStartedAt.Value;
            if (elapsed.TotalSeconds > 0)
            {
                var responseStr = elapsed.TotalHours >= 1
                    ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m"
                    : elapsed.TotalMinutes >= 1
                        ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s"
                        : $"{(int)elapsed.TotalSeconds}s";
                body.Append($"\nResponse: {responseStr}");
            }
        }
        body.Append($"\nFiles:   {c.FilesChanged}");
        body.Append($"\nLines:   +{c.Insertions} / -{c.Deletions}");
        tb.Inlines.Add(new Run(body.ToString()));
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
