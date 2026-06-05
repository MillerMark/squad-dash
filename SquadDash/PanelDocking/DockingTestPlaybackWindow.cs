#nullable enable

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SquadDash.PanelDocking;

/// <summary>
/// Developer tool window for stepping through recorded docking test cases interactively.
/// Phases per test: (auto) Apply Layout + Open Map → Execute Move → Next Test.
/// </summary>
internal sealed class DockingTestPlaybackWindow : ChromedWindow
{
    private enum PlaybackPhase { MapOpen, MoveExecuted }

    private sealed record TestCaseEntry(string FilePath, string DisplayName);

    private sealed record ParsedTestCase(
        string SourcePanelId,
        Dictionary<string, List<string>> InitialLayout,
        Dictionary<string, List<string>> ExpectedLayout,
        string TargetZoneDisplay,
        int TargetOrder,
        bool TargetIsInsert,
        string FilePath,
        List<SlotButtonViewModel>? ExpectedDockingMapSlots = null,
        DockingMapViewModel? DockingMapViewModel = null);

    private readonly string _testCaseFolder;
    private readonly Action<Dictionary<string, List<string>>> _applyLayout;
    private readonly Action<string, (DockZone Zone, int Order, bool IsInsert)> _openDockingMap;
    private readonly Action<string, string> _addFileToChat;
    private readonly Action<string, DockZone, int> _executeMove;
    private readonly Func<Dictionary<string, List<string>>> _getCurrentLayout;
    private readonly Action<string, DockZone, int>? _showDropPreview;
    private readonly Action? _hideDropPreview;
    private readonly Action<string>? _showSourceHighlight;
    private readonly Action? _hideSourceHighlight;
    private readonly Action? _closeMapWindow;
    private readonly Func<string, (DockZone Zone, int Order), IReadOnlyList<string>>? _getMapViolations;

    private ListBox _testList = null!;
    private TextBlock _detailBlock = null!;
    private Canvas _dockingMapCanvas = null!;
    private Button _stepButton = null!;
    private TextBlock _statusLabel = null!;
    private ScrollViewer _detailScroll = null!;

    private PlaybackPhase _phase = PlaybackPhase.MapOpen;
    private bool _testLoaded = false;
    private ParsedTestCase? _currentTest;
    private readonly List<TestCaseEntry> _entries = [];
    private SlotButtonViewModel? _selectedSlot = null;
    private readonly Dictionary<SlotButtonViewModel, Border> _slotBorders = new();
    private FileSystemWatcher? _watcher;

    public DockingTestPlaybackWindow(
        string testCaseFolder,
        Action<Dictionary<string, List<string>>> applyLayout,
        Action<string, (DockZone Zone, int Order, bool IsInsert)> openDockingMap,
        Action<string, string> addFileToChat,
        Action<string, DockZone, int> executeMove,
        Func<Dictionary<string, List<string>>> getCurrentLayout,
        ResourceDictionary appResources,
        Action<string, DockZone, int>? showDropPreview = null,
        Action? hideDropPreview = null,
        Action<string>? showSourceHighlight = null,
        Action? hideSourceHighlight = null,
        Action? closeMapWindow = null,
        Func<string, (DockZone Zone, int Order), IReadOnlyList<string>>? getMapViolations = null)
        : base(captionHeight: ChromedWindow.CloseButtonHeight, resizeMode: ResizeMode.CanResize)
    {
        _testCaseFolder    = testCaseFolder;
        _applyLayout       = applyLayout;
        _openDockingMap    = openDockingMap;
        _addFileToChat     = addFileToChat;
        _executeMove       = executeMove;
        _getCurrentLayout  = getCurrentLayout;
        _showDropPreview   = showDropPreview;
        _hideDropPreview   = hideDropPreview;
        _showSourceHighlight = showSourceHighlight;
        _hideSourceHighlight = hideSourceHighlight;
        _closeMapWindow      = closeMapWindow;
        _getMapViolations    = getMapViolations;

        Title      = "Docking Test Playback";
        Width      = 720;
        Height     = 500;

        var contentHolder = ApplyOuterBorder("AppSurface", "Docking Test Playback");
        BuildUI(appResources, contentHolder);
        LoadTestCases();

        // Route Up/Down arrow keys to the test list from anywhere in this window.
        // This is a safety net for when focus lands on chrome or the Step button —
        // the map window is shown with ShowActivated=false so focus should normally
        // stay on the list, but this ensures navigation always works.
        PreviewKeyDown += OnWindowPreviewKeyDown;

        if (Directory.Exists(_testCaseFolder))
        {
            _watcher = new FileSystemWatcher(_testCaseFolder, "*.json")
            {
                NotifyFilter        = NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler refresh = (_, _) =>
            {
                if (!Dispatcher.HasShutdownStarted)
                    Dispatcher.InvokeAsync(LoadTestCases, System.Windows.Threading.DispatcherPriority.Background);
            };
            _watcher.Created += refresh;
            _watcher.Deleted += refresh;
            _watcher.Renamed += (_, _) =>
            {
                if (!Dispatcher.HasShutdownStarted)
                    Dispatcher.InvokeAsync(LoadTestCases, System.Windows.Threading.DispatcherPriority.Background);
            };
            Closed += (_, _) => { _watcher?.Dispose(); _watcher = null; };
        }
    }

    private void BuildUI(ResourceDictionary appResources, Border contentHolder)
    {
        var outer = new Grid { Margin = new Thickness(4) };
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── Main content (list + detail + map canvas) ────────────────────────────────
        var content = new Grid { Margin = new Thickness(8, 8, 8, 4) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(455) });
        Grid.SetRow(content, 0);
        outer.Children.Add(content);

        // Test list
        _testList = new ListBox { BorderThickness = new Thickness(1) };
        _testList.SetResourceReference(ListBox.BackgroundProperty,  "AppSurface");
        _testList.SetResourceReference(ListBox.ForegroundProperty,  "LabelText");
        _testList.SetResourceReference(ListBox.BorderBrushProperty, "PanelBorder");
        _testList.SelectionChanged += OnTestSelected;

        // Re-apply layout when clicking an already-selected item (SelectionChanged won't fire).
        // Deferred to Loaded priority so the full mouse-event cycle finishes first — otherwise
        // the ListBoxItem focus-grab after MouseLeftButtonDown deactivates the map window,
        // which auto-closes on Deactivated.
        _testList.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject src)
            {
                var item = FindAncestorOrSelf<ListBoxItem>(src);
                if (item is not null && item.IsSelected)
                    Dispatcher.InvokeAsync(() => OnTestSelected(_testList, null!),
                        System.Windows.Threading.DispatcherPriority.Loaded);
            }
        };

        var contextMenu      = new ContextMenu();
        var addToChatItem    = new MenuItem { Header = "Add to Chat" };
        addToChatItem.Click += OnAddToChat;
        contextMenu.Items.Add(addToChatItem);
        _testList.ContextMenu = contextMenu;

        Grid.SetColumn(_testList, 0);
        content.Children.Add(_testList);

        // Detail panel
        _detailScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _detailScroll.SetResourceReference(ScrollViewer.BackgroundProperty, "AppSurface");

        _detailBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Padding      = new Thickness(4),
            FontFamily   = new FontFamily("Consolas"),
            FontSize     = 11,
        };
        _detailBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        _detailScroll.Content = _detailBlock;
        Grid.SetColumn(_detailScroll, 2);
        content.Children.Add(_detailScroll);

        // Docking map canvas (scrollable)
        var mapScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(1),
        };
        mapScroll.SetResourceReference(ScrollViewer.BackgroundProperty, "AppSurface");
        mapScroll.SetResourceReference(ScrollViewer.BorderBrushProperty, "PanelBorder");

        _dockingMapCanvas = new Canvas
        {
            Background = new SolidColorBrush(Colors.Transparent),
        };
        _dockingMapCanvas.MouseLeftButtonDown += OnCanvasMouseDown;
        _dockingMapCanvas.KeyDown += OnCanvasKeyDown;
        _dockingMapCanvas.Focusable = true;

        mapScroll.Content = _dockingMapCanvas;
        Grid.SetColumn(mapScroll, 4);
        content.Children.Add(mapScroll);

        // ── Bottom bar (status + clear-overlay + step button) ────────────────
        var bottomBar = new Grid { Margin = new Thickness(8, 4, 8, 8) };
        bottomBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottomBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(bottomBar, 1);
        outer.Children.Add(bottomBar);

        _statusLabel = new TextBlock
        {
            Text              = "Select a test case to begin.",
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping      = TextWrapping.Wrap,
        };
        _statusLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        var copyStatusItem = new MenuItem { Header = "Copy" };
        copyStatusItem.Click += (_, _) => System.Windows.Clipboard.SetText(_statusLabel.Text);
        var statusMenu = new ContextMenu();
        statusMenu.Items.Add(copyStatusItem);
        _statusLabel.ContextMenu = statusMenu;

        Grid.SetColumn(_statusLabel, 0);
        bottomBar.Children.Add(_statusLabel);

        var clearOverlayButton = new Button
        {
            Content   = "Clear Overlay",
            Width     = 110,
            Height    = 28,
            Margin    = new Thickness(8, 0, 0, 0),
        };
        if (appResources["FlatButtonStyle"] is Style clearStyle)
            clearOverlayButton.Style = clearStyle;
        clearOverlayButton.Click += (_, _) => { _hideDropPreview?.Invoke(); _hideSourceHighlight?.Invoke(); _closeMapWindow?.Invoke(); };
        Grid.SetColumn(clearOverlayButton, 1);
        bottomBar.Children.Add(clearOverlayButton);

        _stepButton = new Button
        {
            Content   = "Step →",
            Width     = 130,
            Height    = 28,
            IsEnabled = false,
            Margin    = new Thickness(8, 0, 0, 0),
        };
        if (appResources["FlatButtonStyle"] is Style flatStyle)
            _stepButton.Style = flatStyle;
        _stepButton.Click += OnStep;
        Grid.SetColumn(_stepButton, 2);
        bottomBar.Children.Add(_stepButton);

        contentHolder.Child = outer;
    }

    private void LoadTestCases()
    {
        if (!IsLoaded && _entries.Count > 0) return; // guard against stale watcher calls after close

        // Remember the currently-selected test name so we can restore it after reload.
        var selectedName = _testList.SelectedIndex >= 0 && _testList.SelectedIndex < _entries.Count
            ? _entries[_testList.SelectedIndex].DisplayName
            : null;

        // Suppress SelectionChanged during list rebuild to avoid re-running the test while reloading.
        _testList.SelectionChanged -= OnTestSelected;
        _entries.Clear();
        _testList.Items.Clear();

        if (Directory.Exists(_testCaseFolder))
        {
            foreach (var file in Directory.GetFiles(_testCaseFolder, "*.json").OrderBy(f => f))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                _entries.Add(new TestCaseEntry(file, name));
                _testList.Items.Add(name);
            }
        }

        _testList.SelectionChanged += OnTestSelected;

        if (selectedName is not null)
        {
            int idx = _entries.FindIndex(e => e.DisplayName == selectedName);
            if (idx >= 0)
            {
                _testList.SelectedIndex = idx; // SelectionChanged not wired — restores quietly
            }
            else
            {
                // Previously-selected file was removed; reset playback state.
                _currentTest = null;
                _testLoaded  = false;
                _detailBlock.Text     = string.Empty;
                _stepButton.IsEnabled = false;
                SetStatus("Selected test file was removed. Choose another.", StatusKind.Subtle);
            }
        }
    }

    private void OnTestSelected(object sender, SelectionChangedEventArgs e)
    {
        SquadDashTrace.Write("Docking", "[PREVIEW-RENDER] OnTestSelected called");
        _phase      = PlaybackPhase.MapOpen;
        _testLoaded = false;
        _currentTest = null;

        int idx = _testList.SelectedIndex;
        if (idx < 0 || idx >= _entries.Count)
        {
            _detailBlock.Text     = string.Empty;
            _stepButton.IsEnabled = false;
            SetStatus("Select a test case to begin.", StatusKind.Subtle);
            SquadDashTrace.Write("Docking", "[PREVIEW-RENDER] No selection");
            return;
        }

        var entry = _entries[idx];
        try
        {
            var json = File.ReadAllText(entry.FilePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var sourcePanelId  = root.GetProperty("sourcePanelId").GetString() ?? "";
            var initialLayout  = ParseZoneMap(root.GetProperty("initialLayout"));
            var action         = root.GetProperty("action");
            var targetZone     = action.GetProperty("targetZone").GetString() ?? "";
            var targetOrder    = action.GetProperty("targetOrder").GetInt32();
            var targetIsInsert = action.TryGetProperty("insertKind", out var ikProp)
                              && ikProp.GetString() is { Length: > 0 };
            var expectedLayout = root.TryGetProperty("expectedLayout", out var elProp)
                ? ParseZoneMap(elProp)
                : new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            
            var expectedDockingMapSlots = root.TryGetProperty("expectedDockingMap", out var edmProp)
                ? ParseExpectedDockingMap(edmProp, sourcePanelId)
                : null;

            // Build the DockingMapViewModel from the expected layout
            SquadDashTrace.Write("Docking", $"[OnTestSelected] ========== BUILDING VIEWMODEL ==========");
            SquadDashTrace.Write("Docking", $"[OnTestSelected] initialLayout zones: {string.Join(", ", initialLayout.Where(kv => kv.Value.Count > 0).Select(kv => $"{kv.Key}=[{string.Join(",", kv.Value)}]"))}");
            SquadDashTrace.Write("Docking", $"[OnTestSelected] expectedLayout zones: {string.Join(", ", expectedLayout.Where(kv => kv.Value.Count > 0).Select(kv => $"{kv.Key}=[{string.Join(",", kv.Value)}]"))}");
            SquadDashTrace.Write("Docking", $"[OnTestSelected] expectedLayout zone count: {expectedLayout.Count}, non-empty zones: {expectedLayout.Count(kv => kv.Value.Count > 0)}");
            
            var dockLayout = BuildDockLayoutFromZoneMap(sourcePanelId, initialLayout);
            SquadDashTrace.Write("Docking", $"[OnTestSelected] Built DockLayout with {dockLayout.Slots.Count} slots: {string.Join(", ", dockLayout.Slots.Select(s => $"{s.PanelId}@{s.Zone}:{s.Order}"))}");
            
            var dockingMapViewModel = DockingMapBuilder.BuildDockingMap(sourcePanelId, dockLayout, null);
            SquadDashTrace.Write("Docking", $"[OnTestSelected] Built ViewModel: PopupWidth={dockingMapViewModel.PopupWidth}, PopupHeight={dockingMapViewModel.PopupHeight}, SlotCount={dockingMapViewModel.Slots.Count}");
            SquadDashTrace.Write("Docking", $"[OnTestSelected] ========== STORING VIEWMODEL ==========");

            _currentTest = new ParsedTestCase(sourcePanelId, initialLayout, expectedLayout, targetZone, targetOrder, targetIsInsert, entry.FilePath, expectedDockingMapSlots, dockingMapViewModel);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Source  : {sourcePanelId}");
            sb.AppendLine($"Action  : move → {targetZone} @ order {targetOrder}");
            sb.AppendLine();

            sb.AppendLine("Initial layout:");
            foreach (var kv in initialLayout)
                if (kv.Value.Count > 0)
                    sb.AppendLine($"  {kv.Key,-8}: {string.Join(", ", kv.Value)}");

            sb.AppendLine();
            sb.AppendLine("Expected layout:");
            foreach (var kv in expectedLayout)
                if (kv.Value.Count > 0)
                    sb.AppendLine($"  {kv.Key,-8}: {string.Join(", ", kv.Value)}");

            _detailBlock.Text = sb.ToString();

            // Render the docking map preview based on the ViewModel
            SquadDashTrace.Write("Docking", $"[PREVIEW-RENDER] Calling RenderDockingMapPreview with {_currentTest.DockingMapViewModel?.Slots.Count ?? 0} slots");
            RenderDockingMapPreview(_currentTest.DockingMapViewModel);
            SquadDashTrace.Write("Docking", $"[PREVIEW-RENDER] RenderDockingMapPreview done");
        }
        catch (Exception ex)
        {
            _detailBlock.Text = $"Error reading test case:\n{ex.Message}";
            _stepButton.IsEnabled = false;
            SetStatus($"Error: {ex.Message}", StatusKind.Subtle);
            return;
        }

        // Only apply layout if it differs from the current state — avoids a visible flash
        // when clicking a test whose initial layout is already in place.
        var actualLayout = _getCurrentLayout();
        var (layoutOk, layoutDetails) = CompareZoneMaps(_currentTest.InitialLayout, actualLayout);
        if (!layoutOk)
        {
            _applyLayout(_currentTest.InitialLayout);
            actualLayout  = _getCurrentLayout();
            (layoutOk, layoutDetails) = CompareZoneMaps(_currentTest.InitialLayout, actualLayout);
        }

        var zone = DockingLayoutEngine.ParseZoneDisplayName(_currentTest.TargetZoneDisplay);

        // Show the source-panel highlight first (Render priority, queued before map window Show)
        // so it appears behind the docking map window in Z-order.
        var capturedTest = _currentTest;
        var capturedZone = zone;
        Dispatcher.InvokeAsync(() => _showSourceHighlight?.Invoke(capturedTest.SourcePanelId),
            System.Windows.Threading.DispatcherPriority.Render);

        _openDockingMap(_currentTest.SourcePanelId, (zone, _currentTest.TargetOrder, _currentTest.TargetIsInsert));

        // Defer the drop-preview overlay to Render priority so WPF has completed its
        // layout/arrange pass after _applyLayout moved panels — GetDropPreviewRect reads
        // panel screen positions via PointToScreen and returns stale coords if called
        // before the render pass. _openDockingMap already defers internally for the same reason.
        Dispatcher.InvokeAsync(() => _showDropPreview?.Invoke(capturedTest.SourcePanelId, capturedZone, capturedTest.TargetOrder),
            System.Windows.Threading.DispatcherPriority.Render);

        // Check for adjacent-thin violations in the generated docking map.
        IReadOnlyList<string>? mapViolations = null;
        if (_getMapViolations is not null)
            mapViolations = _getMapViolations(_currentTest.SourcePanelId, (zone, _currentTest.TargetOrder));

        _phase      = PlaybackPhase.MapOpen;
        _testLoaded = true;

        if (mapViolations is { Count: > 0 })
        {
            var violationText = string.Join("; ", mapViolations);
            SetStatus(
                (layoutOk ? "✓ Layout OK — " : $"✗ Layout mismatch ({layoutDetails}) — ")
                + $"⚠ Map violation: {violationText}",
                StatusKind.Error);
        }
        else
        {
            SetStatus(layoutOk
                ? "✓ Layout confirmed, map open — click Execute Move."
                : $"✗ Layout mismatch — {layoutDetails}",
                layoutOk ? StatusKind.Normal : StatusKind.Error);
        }

        UpdateStepButton();
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (System.Windows.Input.Key.Up or System.Windows.Input.Key.Down)) return;
        if (_testList.Items.Count == 0) return;

        int dir    = e.Key == System.Windows.Input.Key.Down ? 1 : -1;
        int newIdx = Math.Clamp(_testList.SelectedIndex + dir, 0, _testList.Items.Count - 1);
        if (newIdx == _testList.SelectedIndex) { e.Handled = true; return; }

        _testList.SelectedIndex = newIdx;
        _testList.ScrollIntoView(_testList.SelectedItem);
        e.Handled = true;
    }

    private void OnStep(object sender, RoutedEventArgs e)
    {
        if (_currentTest is null) return;

        switch (_phase)
        {
            case PlaybackPhase.MapOpen:
            {
                _hideDropPreview?.Invoke();
                _hideSourceHighlight?.Invoke();
                _closeMapWindow?.Invoke();
                var zone = DockingLayoutEngine.ParseZoneDisplayName(_currentTest.TargetZoneDisplay);
                _executeMove(_currentTest.SourcePanelId, zone, _currentTest.TargetOrder);
                _phase = PlaybackPhase.MoveExecuted;
                var actual = _getCurrentLayout();
                var (ok, details) = CompareZoneMaps(_currentTest.ExpectedLayout, actual);
                SetStatus(ok
                    ? $"✓ Test passed: {_currentTest.SourcePanelId} → {_currentTest.TargetZoneDisplay} @ {_currentTest.TargetOrder} — click Next Test."
                    : $"✗ Test FAILED — {details}",
                    ok ? StatusKind.Normal : StatusKind.Error);
                break;
            }

            case PlaybackPhase.MoveExecuted:
            {
                // Advancing selection triggers OnTestSelected which auto-applies the next layout and opens the map.
                int nextIdx = _testList.SelectedIndex + 1;
                if (nextIdx < _testList.Items.Count)
                    _testList.SelectedIndex = nextIdx;
                else
                {
                    // Last test complete — clear all overlays so nothing lingers.
                    _hideDropPreview?.Invoke();
                    _hideSourceHighlight?.Invoke();
                    SetStatus("All tests complete.", StatusKind.Subtle);
                    _stepButton.IsEnabled = false;
                }
                return;
            }
        }

        UpdateStepButton();
    }

    private void OnAddToChat(object sender, RoutedEventArgs e)
    {
        int idx = _testList.SelectedIndex;
        if (idx < 0 || idx >= _entries.Count) return;
        var entry = _entries[idx];
        _addFileToChat(entry.FilePath, entry.DisplayName);
    }

    private void UpdateStepButton()
    {
        _stepButton.IsEnabled = _testLoaded && _currentTest is not null;
        _stepButton.Content   = _phase switch
        {
            PlaybackPhase.MapOpen      => "Execute Move →",
            PlaybackPhase.MoveExecuted => "Next Test →",
            _                          => "Step →",
        };
    }

    private enum StatusKind { Subtle, Normal, Error }

    private void SetStatus(string text, StatusKind kind)
    {
        _statusLabel.Text = text;
        // Pass/neutral messages use high-contrast label text; only failures get red.
        if (kind == StatusKind.Error)
            _statusLabel.SetValue(TextBlock.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(210, 60, 60)));
        else
            _statusLabel.SetResourceReference(TextBlock.ForegroundProperty,
                kind == StatusKind.Subtle ? "SubtleText" : "LabelText");
    }

    /// <summary>
    /// Compares <paramref name="expected"/> zone map against <paramref name="actual"/>.
    /// Only checks zones/panels listed in expected; extra panels in actual are ignored.
    /// This handles the common case where the test JSON was recorded with a subset of
    /// visible panels — unrecorded panels (approvals, notes, etc.) may still be in Top
    /// in the live layout and should not cause a false failure.
    /// </summary>
    private static (bool Match, string Details) CompareZoneMaps(
        Dictionary<string, List<string>> expected,
        Dictionary<string, List<string>> actual)
    {
        // Build the full set of panels mentioned in the expected layout so we can filter
        // actual panels down to only those we care about.
        var expectedPanelSet = new HashSet<string>(
            expected.Values.SelectMany(v => v),
            StringComparer.OrdinalIgnoreCase);

        var mismatches = new List<string>();
        foreach (var (zone, expectedPanels) in expected)
        {
            actual.TryGetValue(zone, out var actualPanels);
            actualPanels ??= [];

            // Only compare panels that appear in the expected layout; ignore extras.
            var exp = expectedPanels.Select(p => p.ToLowerInvariant()).ToList();
            var act = actualPanels
                .Where(p => expectedPanelSet.Contains(p))
                .Select(p => p.ToLowerInvariant())
                .ToList();

            if (!exp.SequenceEqual(act))
                mismatches.Add($"{zone}: expected [{string.Join(",", exp)}] got [{string.Join(",", act)}]");
        }
        return mismatches.Count == 0
            ? (true, "All zones match")
            : (false, string.Join("; ", mismatches));
    }

    private static Dictionary<string, List<string>> ParseZoneMap(JsonElement element)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in element.EnumerateObject())
        {
            var panels = new List<string>();
            foreach (var item in prop.Value.EnumerateArray())
            {
                var s = item.GetString();
                if (s is not null) panels.Add(s);
            }
            result[prop.Name] = panels;
        }
        return result;
    }

    private static T? FindAncestorOrSelf<T>(DependencyObject obj) where T : DependencyObject
    {
        var current = obj;
        while (current is not null)
        {
            if (current is T match) return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void RenderDockingMapPreview(DockingMapViewModel? viewModel)
    {
        _dockingMapCanvas.Children.Clear();
        _slotBorders.Clear();
        _selectedSlot = null;

        SquadDashTrace.Write("Docking", $"[PREVIEW-RENDER-START] ViewModel: {(viewModel is null ? "NULL" : $"PopupWidth={viewModel.PopupWidth}, PopupHeight={viewModel.PopupHeight}, SlotCount={viewModel.Slots.Count}")}");

        if (viewModel is null || viewModel.Slots.Count == 0)
        {
            _dockingMapCanvas.Width = 0;
            _dockingMapCanvas.Height = 0;
            SquadDashTrace.Write("Docking", $"[PREVIEW-RENDER] ViewModel is null or has no slots");
            return;
        }

        bool isDark = AgentStatusCard.IsDarkTheme;
        Color groundingColor = isDark ? Colors.Black : Colors.White;
        Color polarColor = isDark ? Colors.White : Colors.Black;

        // Set canvas size from the ViewModel (already calculated correctly by DockingMapBuilder)
        _dockingMapCanvas.Width = viewModel.PopupWidth;
        _dockingMapCanvas.Height = viewModel.PopupHeight;

        var scrollParent = VisualTreeHelper.GetParent(_dockingMapCanvas) as ScrollViewer;
        double parentWidth = scrollParent?.ActualWidth ?? double.NaN;
        double parentHeight = scrollParent?.ActualHeight ?? double.NaN;

        SquadDashTrace.Write("Docking", $"[PREVIEW-RENDER] Canvas size: {viewModel.PopupWidth}x{viewModel.PopupHeight}, Parent: {parentWidth}x{parentHeight}");
        SquadDashTrace.Write("Docking", $"[PREVIEW-RENDER] Total slots: {viewModel.Slots.Count}");

        // Get target zone from the current test to highlight the target slot
        DockZone? targetZone = null;
        int? targetOrder = null;
        SlotButtonViewModel? targetSlot = null;
        if (_currentTest is not null)
        {
            targetZone = DockingLayoutEngine.ParseZoneDisplayName(_currentTest.TargetZoneDisplay);
            targetOrder = _currentTest.TargetOrder;
            SquadDashTrace.Write("Docking", $"[PREVIEW-RENDER] Target: {targetZone}@{targetOrder}");
            
            // Find the best matching slot: prefer regular panels over synthetic inserts
            if (targetZone.HasValue && targetOrder.HasValue)
            {
                var candidates = viewModel.Slots.Where(s => s.TargetZone == targetZone.Value && s.TargetOrder == targetOrder.Value).ToList();
                // Prefer non-synthetic slots (regular panels) over synthetic inserts
                targetSlot = candidates.FirstOrDefault(s => !s.IsSyntheticInsert) ?? candidates.FirstOrDefault();
                if (targetSlot is not null)
                {
                    SquadDashTrace.Write("Docking", $"[PREVIEW-RENDER] Found {candidates.Count} slots matching {targetZone}@{targetOrder}; highlighting {(targetSlot.IsSyntheticInsert ? "synthetic" : "regular")} at ({targetSlot.X}, {targetSlot.Y})");
                }
            }
        }

        // Render each slot at its actual coordinates (no scaling) — same logic as DockingMapWindow
        int slotIndex = 0;
        foreach (var slot in viewModel.Slots)
        {
            var border = BuildMapSlotElement(slot, groundingColor, polarColor, isDark, targetSlot);
            Canvas.SetLeft(border, slot.X);
            Canvas.SetTop(border,  slot.Y);
            _dockingMapCanvas.Children.Add(border);
            _slotBorders[slot] = border;
            
            SquadDashTrace.Write("Docking", $"[PREVIEW-RENDER] Slot {slotIndex}: {slot.Label} @({slot.X},{slot.Y}) {slot.Width}x{slot.Height}");
            slotIndex++;
        }
        
        SquadDashTrace.Write("Docking", $"[PREVIEW-RENDER] Done: {slotIndex} slots rendered, canvas has {_dockingMapCanvas.Children.Count} children");
    }

    private List<SlotButtonViewModel>? ParseExpectedDockingMap(JsonElement arrayElement, string sourcePanelId)
    {
        var slots = new List<SlotButtonViewModel>();
        try
        {
            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                var label = item.TryGetProperty("label", out var lp) ? lp.GetString() : "";
                var panelId = item.TryGetProperty("panelId", out var pid) ? pid.GetString() : label;
                var isSource = string.Equals(panelId, sourcePanelId, StringComparison.OrdinalIgnoreCase);  // Case-insensitive match
                var isExpansion = item.TryGetProperty("isExpansionButton", out var ieb) && ieb.GetBoolean();
                var x = item.TryGetProperty("x", out var xp) ? xp.GetDouble() : 0;
                var y = item.TryGetProperty("y", out var yp) ? yp.GetDouble() : 0;
                var width = item.TryGetProperty("width", out var wp) ? wp.GetDouble() : 48;
                var height = item.TryGetProperty("height", out var hp) ? hp.GetDouble() : 48;
                var zoneStr = item.TryGetProperty("targetZone", out var zp) ? zp.GetString() : "Top";
                var order = item.TryGetProperty("targetOrder", out var op) ? op.GetInt32() : 0;
                var insertKindStr = item.TryGetProperty("insertKind", out var ikp) ? ikp.GetString() : "";
                var isSeparator = item.TryGetProperty("isSeparator", out var isp2) && isp2.GetBoolean();

                if (!Enum.TryParse<DockZone>(zoneStr, ignoreCase: true, out var zone))
                    zone = DockZone.Top;

                var insertKind = insertKindStr switch
                {
                    "InsertBefore" => SyntheticInsertKind.InsertBefore,
                    "InsertAfter" => SyntheticInsertKind.InsertAfter,
                    _ => SyntheticInsertKind.None
                };

                var slot = new SlotButtonViewModel(
                    label ?? "",
                    isSource,
                    isExpansion,
                    x,
                    y,
                    width,
                    height,
                    zone,
                    order,
                    sourcePanelId)
                {
                    IsSeparator = isSeparator,
                    InsertKind = insertKind
                };
                slots.Add(slot);
            }
        }
        catch { /* ignore parse errors */ }

        return slots.Count > 0 ? slots : null;
    }

    /// <summary>
    /// Converts a zone map (Dictionary&lt;string, List&lt;string&gt;&gt;) to a DockLayout object.
    /// </summary>
    private static DockLayout BuildDockLayoutFromZoneMap(string sourcePanelId, Dictionary<string, List<string>> zoneMap)
    {
        var layout = new DockLayout { Name = "Preview" };
        var slots = new List<PanelSlot>();

        SquadDashTrace.Write("Docking", $"[BuildDockLayoutFromZoneMap] Processing {zoneMap.Count} zone entries");
        foreach (var (zoneName, panelIds) in zoneMap)
        {
            var zone = ParseZoneDisplayName(zoneName);
            SquadDashTrace.Write("Docking", $"[BuildDockLayoutFromZoneMap] Zone '{zoneName}' -> {(zone.HasValue ? zone.Value.ToString() : "NULL")} ({panelIds.Count} panels)");
            
            if (!zone.HasValue)
                continue;

            int order = 0;
            foreach (var panelId in panelIds)
            {
                slots.Add(new PanelSlot(panelId, zone.Value, order));
                SquadDashTrace.Write("Docking", $"[BuildDockLayoutFromZoneMap]   Added: {panelId}@{zone.Value}:{order}");
                order++;
            }
        }

        SquadDashTrace.Write("Docking", $"[BuildDockLayoutFromZoneMap] Final DockLayout: {slots.Count} slots");
        layout.Slots = slots;
        return layout;
    }

    /// <summary>
    /// Converts zone display names (e.g., "Right 1", "Left 2") to DockZone enums.
    /// Handles variations in spacing and casing.
    /// </summary>
    private static DockZone? ParseZoneDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            SquadDashTrace.Write("Docking", $"[ParseZoneDisplayName] Input: null or empty");
            return null;
        }

        var normalized = displayName.Trim().ToLowerInvariant();
        SquadDashTrace.Write("Docking", $"[ParseZoneDisplayName] Input: '{displayName}' → normalized: '{normalized}'");
        
        var result = normalized switch
        {
            "top"     => (DockZone?)DockZone.Top,
            "left 1"  => (DockZone?)DockZone.Left,
            "left 2"  => (DockZone?)DockZone.Left2,
            "left 3"  => (DockZone?)DockZone.Left3,
            "left 4"  => (DockZone?)DockZone.Left4,
            "right 1" => (DockZone?)DockZone.Right,
            "right 2" => (DockZone?)DockZone.Right2,
            "right 3" => (DockZone?)DockZone.Right3,
            "right 4" => (DockZone?)DockZone.Right4,
            _         => null,
        };
        SquadDashTrace.Write("Docking", $"[ParseZoneDisplayName] Output: {(result.HasValue ? result.Value.ToString() : "NULL")}");
        return result;
    }

    private Border BuildMapSlotElement(SlotButtonViewModel slot, Color groundingColor, Color polarColor, bool isDark, SlotButtonViewModel? targetSlot = null)
    {
        // Separator (decorative)
        if (slot.IsSeparator)
        {
            return new Border
            {
                Width = slot.Width,
                Height = slot.Height,
                Background = MakeBrush(polarColor, isDark ? 0.15 : 0.30),
                CornerRadius = new CornerRadius(slot.Width / 2.0),
            };
        }

        // Source panel — highlight with green border and thicker border for clear visibility
        if (slot.IsSourcePanel)
        {
            return new Border
            {
                Width = slot.Width,
                Height = slot.Height,
                Background = MakeBrush(groundingColor, 0.20),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 200, 100)),  // Bright green
                BorderThickness = new Thickness(3),  // Thicker for emphasis
                CornerRadius = new CornerRadius(3),
                Tag = slot,
            };
        }

        // Target zone slot — highlight with yellow/gold for destination visibility
        // Only highlight if this is the specific target slot (prefer regular panels over synthetics)
        bool isTargetSlot = targetSlot is not null && slot == targetSlot;

        var normalBg = MakeBrush(groundingColor, 0.70);
        var normalBorder = MakeBrush(polarColor, 0.10);

        var border = new Border
        {
            Width = slot.Width,
            Height = slot.Height,
            Background = isTargetSlot ? MakeBrush(Color.FromRgb(255, 200, 0), 0.25) : normalBg,
            BorderBrush = isTargetSlot ? new SolidColorBrush(Color.FromRgb(255, 200, 0)) : normalBorder,
            BorderThickness = isTargetSlot ? new Thickness(2) : new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Cursor = Cursors.Hand,
            Tag = slot,
        };

        // Add context menu for debugging
        var contextMenu = new ContextMenu();
        var markCorrectMenuItem = new MenuItem { Header = "Mark as correct target" };
        markCorrectMenuItem.Click += (_, _) =>
        {
            SquadDashTrace.Write("Docking", "=== MARKED CORRECT TARGET (RIGHT-CLICK) ===");
            SquadDashTrace.Write("Docking", $"Label: {slot.Label}");
            SquadDashTrace.Write("Docking", $"Position: ({slot.X}, {slot.Y})");
            SquadDashTrace.Write("Docking", $"Size: {slot.Width}x{slot.Height}");
            SquadDashTrace.Write("Docking", $"TargetZone: {slot.TargetZone}");
            SquadDashTrace.Write("Docking", $"TargetOrder: {slot.TargetOrder}");
            SquadDashTrace.Write("Docking", $"IsSyntheticInsert: {slot.IsSyntheticInsert}");
            SquadDashTrace.Write("Docking", $"InsertKind: {slot.InsertKind}");
            SquadDashTrace.Write("Docking", $"IsSourcePanel: {slot.IsSourcePanel}");
            SquadDashTrace.Write("Docking", "=============================================");
        };
        contextMenu.Items.Add(markCorrectMenuItem);
        
        // Add menu item for synthetic inserts to mark as unwanted
        if (slot.IsSyntheticInsert)
        {
            contextMenu.Items.Add(new Separator());
            var markUnwantedMenuItem = new MenuItem { Header = "Mark as should NOT be shown (remove from test)" };
            markUnwantedMenuItem.Click += (_, _) =>
            {
                SquadDashTrace.Write("Docking", "=== MARK AS UNWANTED SYNTHETIC (SHOULD REMOVE) ===");
                SquadDashTrace.Write("Docking", $"Position: ({slot.X}, {slot.Y})");
                SquadDashTrace.Write("Docking", $"Size: {slot.Width}x{slot.Height}");
                SquadDashTrace.Write("Docking", $"TargetZone: {slot.TargetZone}");
                SquadDashTrace.Write("Docking", $"TargetOrder: {slot.TargetOrder}");
                SquadDashTrace.Write("Docking", $"InsertKind: {slot.InsertKind}");
                SquadDashTrace.Write("Docking", "");
                SquadDashTrace.Write("Docking", "ACTION: Remove from expectedDockingMap:");
                SquadDashTrace.Write("Docking", $"  Delete the entry with zone='{slot.TargetZone}' and order={slot.TargetOrder}");
                SquadDashTrace.Write("Docking", "==================================================");
            };
            contextMenu.Items.Add(markUnwantedMenuItem);
        }
        
        border.ContextMenu = contextMenu;

        border.MouseEnter += (_, _) =>
        {
            border.Background = MakeBrush(groundingColor, 0.90);
            border.BorderBrush = MakeBrush(polarColor, 0.50);
        };

        border.MouseLeave += (_, _) =>
        {
            if (_selectedSlot == slot)
            {
                border.Background = MakeBrush(Colors.Orange, 0.25);
                border.BorderBrush = MakeBrush(Colors.Orange, 0.85);
            }
            else if (isTargetSlot)
            {
                border.Background = MakeBrush(Color.FromRgb(255, 200, 0), 0.25);
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 200, 0));
                border.BorderThickness = new Thickness(2);
            }
            else
            {
                border.Background = normalBg;
                border.BorderBrush = normalBorder;
            }
        };

        return border;
    }

    private static SolidColorBrush MakeBrush(Color color, double opacity) =>
        new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), color.R, color.G, color.B));

    private void OnCanvasMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(_dockingMapCanvas);

        // Find which slot was clicked
        SlotButtonViewModel? clickedSlot = null;
        foreach (var kvp in _slotBorders)
        {
            var slot = kvp.Key;
            if (pos.X >= slot.X && pos.X < slot.X + slot.Width &&
                pos.Y >= slot.Y && pos.Y < slot.Y + slot.Height)
            {
                clickedSlot = slot;
                break;
            }
        }

        // Update selection
        if (_selectedSlot != null && _slotBorders.TryGetValue(_selectedSlot, out var oldBorder))
        {
            oldBorder.BorderThickness = new Thickness(1);
            bool isDark = AgentStatusCard.IsDarkTheme;
            Color polarColor = isDark ? Colors.White : Colors.Black;
            Color groundingColor = isDark ? Colors.Black : Colors.White;
            oldBorder.BorderBrush = MakeBrush(polarColor, 0.10);
            oldBorder.Background = MakeBrush(groundingColor, 0.70);
        }

        _selectedSlot = clickedSlot;
        if (_selectedSlot != null && _slotBorders.TryGetValue(_selectedSlot, out var newBorder))
        {
            newBorder.BorderThickness = new Thickness(3);
            newBorder.BorderBrush = new SolidColorBrush(Colors.Orange);
            newBorder.Background = MakeBrush(Colors.Orange, 0.25);
        }

        _dockingMapCanvas.Focus();
        e.Handled = true;
    }

    private void OnCanvasKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Delete && _selectedSlot != null)
        {
            DeleteSelectedSlot();
            e.Handled = true;
        }
    }

    private void DeleteSelectedSlot()
    {
        if (_selectedSlot == null || _currentTest == null)
            return;

        var slotsToDelete = _currentTest.ExpectedDockingMapSlots
            ?.Where(s => s.Label == _selectedSlot.Label && 
                         s.X == _selectedSlot.X && 
                         s.Y == _selectedSlot.Y && 
                         s.TargetZone == _selectedSlot.TargetZone &&
                         s.TargetOrder == _selectedSlot.TargetOrder)
            .ToList();

        if (slotsToDelete is not null && slotsToDelete.Count > 0)
        {
            foreach (var slot in slotsToDelete)
                _currentTest.ExpectedDockingMapSlots?.Remove(slot);

            // Serialize and persist
            SaveExpectedDockingMapToJson();

            // Reload to show updated state
            LoadTestCases();
            _testList.SelectedIndex = _entries.FindIndex(e => e.FilePath == _currentTest.FilePath);
        }
    }

    private void SaveExpectedDockingMapToJson()
    {
        if (_currentTest == null || _currentTest.ExpectedDockingMapSlots == null)
            return;

        try
        {
            var json = File.ReadAllText(_currentTest.FilePath);
            
            // Parse as JSON and rebuild
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            // Create a new JSON object with all properties
            var options = new JsonSerializerOptions { WriteIndented = true };
            var dict = new System.Collections.Generic.Dictionary<string, object?>();
            
            // Copy all existing properties except expectedDockingMap
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name != "expectedDockingMap")
                {
                    // For nested objects and arrays, we need to preserve them as JsonElement
                    dict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                }
            }
            
            // Build the new docking map array
            var slotsArray = new System.Collections.Generic.List<object>();
            foreach (var slot in _currentTest.ExpectedDockingMapSlots)
            {
                slotsArray.Add(new
                {
                    label = slot.Label,
                    isSourcePanel = slot.IsSourcePanel,
                    isExpansionButton = slot.IsExpansionButton,
                    x = slot.X,
                    y = slot.Y,
                    width = slot.Width,
                    height = slot.Height,
                    targetZone = slot.TargetZone.ToString(),
                    targetOrder = slot.TargetOrder,
                    insertKind = slot.InsertKind.ToString(),
                    isSeparator = slot.IsSeparator
                });
            }
            
            dict["expectedDockingMap"] = slotsArray;
            
            var updatedJson = JsonSerializer.Serialize(dict, options);
            File.WriteAllText(_currentTest.FilePath, updatedJson);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving expectedDockingMap: {ex.Message}");
        }
    }
}
