using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SquadDash.GuidedTours;
using SquadDash.Hints;

namespace SquadDash;

/// <summary>
/// Developer-only dialog for editing a <see cref="GuidedTourStep"/> in-place
/// while a tour is running.  Only shown when <see cref="SquadDashEnvironment.IsDeveloperMode"/> is true.
/// </summary>
internal sealed class FrmGuidedTourStepEditor : ChromedWindow
{
    private GuidedTourStep             _step;
    private int                        _stepIndex;
    private GuidedTour                 _activeTour;
    private readonly List<GuidedTour>  _allTours;
    private readonly string?           _workspaceFolderPath;

    private readonly Stack<string>     _undoStack = new();
    private const int                  UndoStackMaxDepth = 50;
    private readonly Action?           _captureLayout;

    private readonly Action?           _livePreviewCallback;
    private readonly Action<int>?      _jumpToStepCallback;
    private readonly Action?           _addStepAfterCallback;
    private readonly Action?           _deleteStepCallback;
    private string                     _originalMarkdown;
    private string                     _originalPlacement;
    private double                     _originalTargetOffsetX;
    private double                     _originalTargetOffsetY;
    private readonly DispatcherTimer   _debounceTimer;
    private readonly DispatcherTimer   _autoSaveTimer;
    private bool                       _isLoadingStep;

    // Navigation state — snapshot-based dirty detection
    private string   _snapTitle           = string.Empty;
    private string   _snapMarkdown        = string.Empty;
    private string   _snapTargetControlId = string.Empty;
    private string   _snapPlacement       = string.Empty;
    private string   _snapAdvanceTrigger  = string.Empty;
    private string   _snapCommandsBefore  = string.Empty;
    private string   _snapCommandsAfter   = string.Empty;
    private Button                     _prevButton = null!;
    private Button                     _nextButton = null!;
    private TextBlock                  _stepCountLabel = null!;
    private ListBox                    _stepListBox = null!;
    private StackPanel                 _formPanel = null!;
    private TextBlock                  _multiSelectLabel = null!;

    // Drag-to-reorder state
    private Point  _listDragStart;
    private bool   _listDragInProgress;
    private int    _listDragSourceIndex = -1;

    // Drag insertion-line overlay
    private Grid      _stepListBoxHost  = null!;
    private Canvas    _dragInsertCanvas = null!;
    private Rectangle _dragInsertLine   = null!;

    // Clipboard copy/cut/paste
    private static readonly System.Text.Json.JsonSerializerOptions s_clipboardJsonOptions = new() { WriteIndented = false };
    private const string ClipboardFormatMarker = "SquadDashTourSteps/v1:";

    // Context menu items (populated in constructor, updated in ContextMenuOpening)
    private MenuItem _ctxCopy  = null!;
    private MenuItem _ctxCut   = null!;
    private MenuItem _ctxPaste = null!;

    // PTT voice dictation
    private readonly PttTextBoxAttachment _ptt;

    // Form controls
    private TextBox                _descriptionBox = null!;
    private readonly TextBox       _titleBox;
    private readonly TextBox       _markdownBox;
    private readonly RadioButton[] _placementRadios;
    private readonly TextBox       _targetControlBox;
    private readonly TextBlock     _statusLabel;
    private readonly ComboBox      _advanceTriggerBox;

    // Multi-line command TextBoxes
    private TextBox                    _commandBeforeBox = null!;
    private TextBox                    _commandAfterBox  = null!;
    private string[]                   _commandItems = Array.Empty<string>();

    // Extra windows to include in pick-mode (e.g. PreferencesWindow when open)
    private readonly Func<IReadOnlyList<Window>?>? _extraPickWindowsProvider;

    // Intellisense — context-sensitive autocomplete for command/trigger/target fields
    private readonly Func<IReadOnlyList<string>>? _elementNamesProvider;
    private readonly HashSet<string>              _parameterizedCommandNames = new(StringComparer.OrdinalIgnoreCase);
    private string[]                              _triggerItems = Array.Empty<string>();
    private readonly List<IDisposable>            _intelliSenseHelpers = new();

    private static readonly IReadOnlyList<string> s_preferencePageNames =
        PreferencesWindow.PageLabels;
    private static readonly HashSet<string> s_elementNameCommands =
        new(StringComparer.OrdinalIgnoreCase) { "HighlightElement", "HighlightMenuItem", "OpenMenu", "CloseMenu" };
    private static readonly HashSet<string> s_preferencePageCommands =
        new(StringComparer.OrdinalIgnoreCase) { "SelectPreferencesPage" };
    private static readonly HashSet<string> s_paramTriggerNames =
        new(StringComparer.OrdinalIgnoreCase) { "MenuOpened", "PreferencePageSelected" };

    // Crosshair picker
    private readonly Canvas        _crosshairCanvas;
    private readonly TextBlock     _crosshairCoordsLabel;
    private bool                   _crosshairDragging;

    // Pick-mode hover highlight elements (recreated each time pick mode opens)
    private Rectangle? _pickWhiteRect;
    private Rectangle? _pickBlackRect;
    private Border?    _pickLabel;

    // Target highlight overlay — transparent live-preview overlay pinned over the main window
    private Window?    _targetOverlay;
    private Canvas?    _targetOverlayCanvas;
    private Rectangle? _overlayBlackRect;
    private Rectangle? _overlayWhiteRect;
    private Border?    _overlayLabel;
    private Ellipse?   _overlayDot;

    /// <summary>True if the user clicked Save and the step was persisted.</summary>
    public bool WasSaved { get; private set; }

    private readonly Action<bool>? _onClosed;

    private ListBox                    _tourListBox = null!;
    private readonly Action<int>?      _switchTourCallback;
    private readonly Action?           _addTourCallback;
    private readonly Action?           _deleteTourCallback;
    private readonly Action<int, string>? _renameTourCallback;
    private readonly Action<int>?         _onStepChanged;

    public FrmGuidedTourStepEditor(
        GuidedTourStep   step,
        int              stepIndex,
        GuidedTour       activeTour,
        List<GuidedTour> allTours,
        string?          workspaceFolderPath,
        Window           owner,
        Action?          captureLayout        = null,
        Action?          livePreviewCallback  = null,
        Action<int>?     jumpToStepCallback   = null,
        GuidedTourCommandRegistry? commandRegistry = null,
        GuidedTourAdvanceTriggerRegistry? triggerRegistry = null,
        Action<bool>?    onClosed             = null,
        Action?          addStepAfterCallback = null,
        Action?          deleteStepCallback   = null,
        Action<int>?     switchTourCallback   = null,
        Action?          addTourCallback      = null,
        Action?          deleteTourCallback   = null,
        Action<int, string>? renameTourCallback = null,
        Func<IReadOnlyList<Window>?>? extraPickWindowsProvider = null,
        Func<IReadOnlyList<string>>? elementNamesProvider = null,
        Action<int>?     onStepChanged        = null)
        : base(captionHeight: 34, resizeMode: ResizeMode.NoResize, resizeBorderThickness: 0)
    {
        _onClosed            = onClosed;
        _originalMarkdown    = step.MarkdownText;
        _originalPlacement   = step.CalloutPlacement;
        _originalTargetOffsetX = step.TargetOffsetX;
        _originalTargetOffsetY = step.TargetOffsetY;
        _livePreviewCallback = livePreviewCallback;
        _jumpToStepCallback  = jumpToStepCallback;
        _addStepAfterCallback = addStepAfterCallback;
        _deleteStepCallback   = deleteStepCallback;
        _switchTourCallback   = switchTourCallback;
        _addTourCallback      = addTourCallback;
        _deleteTourCallback   = deleteTourCallback;
        _renameTourCallback   = renameTourCallback;
        _extraPickWindowsProvider = extraPickWindowsProvider;
        _elementNamesProvider     = elementNamesProvider;
        _onStepChanged            = onStepChanged;
        foreach (var n in commandRegistry?.ParameterizedCommandNames ?? Array.Empty<string>())
            _parameterizedCommandNames.Add(n);

        _step                = step;
        _stepIndex           = stepIndex;
        _activeTour          = activeTour;
        _allTours            = allTours;
        _workspaceFolderPath = workspaceFolderPath;
        _captureLayout       = captureLayout;

        Title                 = BuildEditorTitle(activeTour.Name, stepIndex, step.Title);
        Width                 = 1600;
        SizeToContent         = SizeToContent.Height;
        ShowInTaskbar         = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner                 = owner;

        var contentArea = ApplyOuterBorder("AppSurface", Title);

        // PTT voice dictation — uses the same settings store as the rest of the app
        _ptt = new PttTextBoxAttachment(() => new ApplicationSettingsStore().Load(), this, Dispatcher);
        Closed += (_, _) => _ptt.Dispose();
        Closed += (_, _) => { foreach (var h in _intelliSenseHelpers) h.Dispose(); };

        // ── Form fields ───────────────────────────────────────────────────────

        _titleBox = MakeTextBox(step.Title, multiLine: false);

        // Pre-fill with title when markdown is empty so the callout is never blank on first show.
        _markdownBox = MakeTextBox(
            string.IsNullOrWhiteSpace(step.MarkdownText) ? step.Title : step.MarkdownText,
            multiLine: true);
        _markdownBox.Height = 120;
        _markdownBox.FontFamily = new FontFamily("Consolas, Courier New, monospace");
        _markdownBox.AcceptsReturn = true;
        _markdownBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        var placementRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0) };
        _placementRadios = new[] { "Auto", "North", "South", "East", "West" }
            .Select(p =>
            {
                var rb = new RadioButton
                {
                    Content                  = p,
                    GroupName                = "Placement",
                    IsChecked                = string.Equals(step.CalloutPlacement, p, StringComparison.OrdinalIgnoreCase),
                    Margin                   = new Thickness(0, 0, 12, 0),
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                rb.SetResourceReference(RadioButton.ForegroundProperty, "LabelText");
                rb.SetResourceReference(RadioButton.FontSizeProperty,   "FontSizeBody");
                rb.Checked += (_, _) => { if (!_isLoadingStep) { PushLivePreview(); QueueAutoSave(); } };
                return rb;
            })
            .ToArray();

        if (_placementRadios.All(r => r.IsChecked != true))
            _placementRadios[0].IsChecked = true;

        foreach (var rb in _placementRadios)
            placementRow.Children.Add(rb);

        _targetControlBox = MakeTextBox(step.TargetControlId, multiLine: false);
        _targetControlBox.Loaded += (_, _) => AttachIntelliSenseToTargetBox();

        var browseButton = MakeButton("Target...");
        browseButton.Click += (_, _) => BrowseForControl();

        var pickButton = MakeButton("⌖");
        pickButton.FontSize = 14;
        pickButton.ToolTip  = "Click to pick a target element from the window";
        pickButton.Click   += (_, _) => StartPickMode();

        var targetRow = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_targetControlBox, 0);
        Grid.SetColumn(browseButton, 1);
        Grid.SetColumn(pickButton, 2);
        browseButton.Margin = new Thickness(6, 0, 0, 0);
        pickButton.Margin   = new Thickness(4, 0, 0, 0);
        targetRow.Children.Add(_targetControlBox);
        targetRow.Children.Add(browseButton);
        targetRow.Children.Add(pickButton);

        var commandNames = commandRegistry?.CommandNames ?? Array.Empty<string>();
        _commandItems = new[] { "" }.Concat(commandNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)).ToArray();

        double fontSizeBody = Application.Current.Resources.Contains("FontSizeBody")
            ? Convert.ToDouble(Application.Current.Resources["FontSizeBody"])
            : 13.0;
        double cmdBoxHeight = (fontSizeBody + 6) * 4 + 8;

        _commandBeforeBox = new TextBox
        {
            AcceptsReturn                = true,
            TextWrapping                 = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility  = ScrollBarVisibility.Auto,
            Height                       = cmdBoxHeight,
            Margin                       = new Thickness(0, 0, 0, 4),
            Text                         = string.Join("\n", step.EffectiveCommandsBefore),
        };
        _commandBeforeBox.SetResourceReference(TextBox.FontSizeProperty,   "FontSizeBody");
        _commandBeforeBox.SetResourceReference(TextBox.FontFamilyProperty, "MonoFont");
        _commandBeforeBox.SetResourceReference(TextBox.BackgroundProperty,  "TextBoxBackground");
        _commandBeforeBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _commandBeforeBox.SetResourceReference(TextBox.ForegroundProperty,  "LabelText");
        _commandBeforeBox.TextChanged += (_, _) => { if (!_isLoadingStep) QueueAutoSave(); };
        _commandBeforeBox.Loaded      += (_, _) => AttachIntelliSenseToCommandBox(_commandBeforeBox);

        _commandAfterBox = new TextBox
        {
            AcceptsReturn                = true,
            TextWrapping                 = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility  = ScrollBarVisibility.Auto,
            Height                       = cmdBoxHeight,
            Margin                       = new Thickness(0, 0, 0, 4),
            Text                         = string.Join("\n", step.EffectiveCommandsAfter),
        };
        _commandAfterBox.SetResourceReference(TextBox.FontSizeProperty,   "FontSizeBody");
        _commandAfterBox.SetResourceReference(TextBox.FontFamilyProperty, "MonoFont");
        _commandAfterBox.SetResourceReference(TextBox.BackgroundProperty,  "TextBoxBackground");
        _commandAfterBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _commandAfterBox.SetResourceReference(TextBox.ForegroundProperty,  "LabelText");
        _commandAfterBox.TextChanged += (_, _) => { if (!_isLoadingStep) QueueAutoSave(); };
        _commandAfterBox.Loaded      += (_, _) => AttachIntelliSenseToCommandBox(_commandAfterBox);

        var triggerNames = triggerRegistry?.TriggerNames ?? Array.Empty<string>();
        _triggerItems = new[] { "" }.Concat(triggerNames).ToArray();
        _advanceTriggerBox = MakeCommandCombo(_triggerItems, step.AdvanceTrigger);
        _advanceTriggerBox.Loaded += (_, _) =>
        {
            AttachIntelliSenseToComboBox(_advanceTriggerBox, isCommand: false);
            var innerTb = VisualTreeSearch.FindChild<TextBox>(_advanceTriggerBox);
            if (innerTb is not null)
                innerTb.TextChanged += (_, _) => { if (!_isLoadingStep) QueueAutoSave(); };
        };

        // ── Crosshair picker ──────────────────────────────────────────────────

        _crosshairCanvas = new Canvas { Height = 120, Margin = new Thickness(0, 4, 0, 0) };
        _crosshairCanvas.SetResourceReference(Canvas.BackgroundProperty, "InputSurface");
        _crosshairCanvas.Visibility = string.IsNullOrWhiteSpace(step.TargetControlId)
            ? Visibility.Collapsed
            : Visibility.Visible;
        _crosshairCanvas.MouseLeftButtonDown += (_, e) => { _crosshairDragging = true;  _crosshairCanvas.CaptureMouse(); UpdateCrosshairFromMouse(e.GetPosition(_crosshairCanvas)); };
        _crosshairCanvas.MouseMove           += (_, e) => { if (_crosshairDragging) UpdateCrosshairFromMouse(e.GetPosition(_crosshairCanvas)); };
        _crosshairCanvas.MouseLeftButtonUp   += (_, e) => { _crosshairDragging = false; _crosshairCanvas.ReleaseMouseCapture(); };
        _crosshairCanvas.SizeChanged         += (_, _) => RedrawCrosshair();
        _crosshairCanvas.MouseEnter          += (_, _) => ShowOrUpdateTargetOverlay();
        _crosshairCanvas.MouseLeave          += (_, _) => { if (!_crosshairDragging) CloseTargetOverlay(); };
        _crosshairCanvas.LostMouseCapture    += (_, _) => { _crosshairDragging = false; CloseTargetOverlay(); };

        _crosshairCoordsLabel = new TextBlock
        {
            Text   = FormatCrosshairCoords(step.TargetOffsetX, step.TargetOffsetY),
            Margin = new Thickness(0, 2, 0, 0),
        };
        _crosshairCoordsLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        _crosshairCoordsLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        _crosshairCoordsLabel.Visibility = _crosshairCanvas.Visibility;

        _targetControlBox.TextChanged += (_, _) =>
        {
            var hasTarget = !string.IsNullOrWhiteSpace(_targetControlBox.Text);
            _crosshairCanvas.Visibility      = hasTarget ? Visibility.Visible : Visibility.Collapsed;
            _crosshairCoordsLabel.Visibility = _crosshairCanvas.Visibility;
            if (hasTarget) RedrawCrosshair();
            // Target overlay is only shown on mouse-over of the crosshair canvas; close it
            // whenever the target text changes so stale highlights don't linger.
            CloseTargetOverlay();
            if (!_isLoadingStep) QueueAutoSave();
        };

        var captureButton = MakeButton("📷 Capture Current Layout for the Step");
        captureButton.HorizontalAlignment = HorizontalAlignment.Left;
        captureButton.Click += (_, _) => CaptureLayoutForStep();

        _statusLabel = new TextBlock
        {
            Margin   = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        _statusLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        _statusLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");

        // ── Layout ────────────────────────────────────────────────────────────

        _stepCountLabel = new TextBlock
        {
            Text              = $"Step {stepIndex + 1} of {activeTour.Steps.Count}",
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(4, 0, 0, 0),
        };
        _stepCountLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        _stepCountLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");

        _descriptionBox = new TextBox
        {
            Text                    = activeTour.Description,
            Padding                 = new Thickness(4, 3, 4, 3),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _descriptionBox.SetResourceReference(TextBox.BackgroundProperty,  "TextBoxBackground");
        _descriptionBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _descriptionBox.SetResourceReference(TextBox.ForegroundProperty,  "LabelText");
        _descriptionBox.SetResourceReference(TextBox.FontSizeProperty,    "FontSizeBody");
        _descriptionBox.TextChanged += (_, _) =>
        {
            if (_isLoadingStep) return;
            _activeTour.Description = _descriptionBox.Text;
            if (!string.IsNullOrWhiteSpace(_workspaceFolderPath))
            {
                try { GuidedTourSaver.Save(_allTours, _workspaceFolderPath); }
                catch { /* ignore auto-save errors */ }
            }
        };

        var formPanel = new StackPanel { Margin = new Thickness(14, 10, 14, 8) };
        _formPanel = formPanel;
        formPanel.Children.Add(MakeLabel("Title"));
        formPanel.Children.Add(_titleBox);
        formPanel.Children.Add(MakeLabel("Callout Text (Markdown)"));
        formPanel.Children.Add(_markdownBox);
        formPanel.Children.Add(MakeLabel("Callout Placement"));
        formPanel.Children.Add(placementRow);
        formPanel.Children.Add(MakeLabel("Target Control (x:Name)"));
        formPanel.Children.Add(targetRow);
        formPanel.Children.Add(MakeLabel("Target Offset (click to reposition arrow)"));
        formPanel.Children.Add(_crosshairCanvas);
        formPanel.Children.Add(_crosshairCoordsLabel);
        formPanel.Children.Add(MakeLabel("Commands Before"));
        formPanel.Children.Add(_commandBeforeBox);
        formPanel.Children.Add(MakeLabel("Commands After"));
        formPanel.Children.Add(_commandAfterBox);
        formPanel.Children.Add(MakeLabel("Advance Trigger"));
        formPanel.Children.Add(_advanceTriggerBox);
        formPanel.Children.Add(new Border { Height = 10 });
        formPanel.Children.Add(captureButton);
        formPanel.Children.Add(_statusLabel);

        // ── Button bar ────────────────────────────────────────────────────────

        _prevButton          = MakeButton("← Prev");
        _prevButton.IsEnabled = stepIndex > 0;
        _prevButton.Click    += (_, _) => TryNavigate(_stepIndex - 1);

        _nextButton          = MakeButton("Next →");
        _nextButton.IsEnabled = stepIndex < activeTour.Steps.Count - 1;
        _nextButton.Click    += (_, _) => TryNavigate(_stepIndex + 1);

        var closeButton = MakeButton("Close");
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => TryClose();

        var leftButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        leftButtons.Children.Add(_prevButton);
        leftButtons.Children.Add(_nextButton);

        var rightButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        rightButtons.Children.Add(closeButton);

        var buttonRow = new DockPanel
        {
            Margin        = new Thickness(14, 4, 14, 12),
            LastChildFill = false,
        };
        DockPanel.SetDock(leftButtons,  Dock.Left);
        DockPanel.SetDock(rightButtons, Dock.Right);
        buttonRow.Children.Add(leftButtons);
        buttonRow.Children.Add(rightButtons);

        // ── Step list (left sidebar) ──────────────────────────────────────────

        _stepListBox = new ListBox { Width = 320, SelectionMode = SelectionMode.Extended };
        ScrollViewer.SetHorizontalScrollBarVisibility(_stepListBox, ScrollBarVisibility.Disabled);
        _stepListBox.SetResourceReference(ListBox.BackgroundProperty,  "InputSurface");
        _stepListBox.SetResourceReference(ListBox.ForegroundProperty,  "LabelText");
        _stepListBox.SetResourceReference(ListBox.FontSizeProperty,    "FontSizeBody");

        for (int i = 0; i < activeTour.Steps.Count; i++)
            _stepListBox.Items.Add($"{i + 1}. {activeTour.Steps[i].Title}");

        _stepListBox.SelectedIndex = stepIndex;
        _stepListBox.SelectionChanged += OnStepListSelectionChanged;

        // ── Drag insertion-line overlay ──────────────────────────────────────

        _dragInsertLine = new Rectangle
        {
            Height           = 2,
            IsHitTestVisible = false,
        };
        _dragInsertLine.SetResourceReference(Rectangle.FillProperty, "QueueTabActiveBorder");

        _dragInsertCanvas = new Canvas
        {
            IsHitTestVisible = false,
            Visibility       = Visibility.Collapsed,
        };
        _dragInsertCanvas.Children.Add(_dragInsertLine);

        _stepListBoxHost = new Grid();
        _stepListBoxHost.Children.Add(_stepListBox);
        _stepListBoxHost.Children.Add(_dragInsertCanvas);

        // ── Drag-to-reorder ──────────────────────────────────────────────────

        // Reset drag state on every mouse-down in the window (tunnel fires root→target).
        // This ensures that a click in a text box outside _stepListBox clears stale drag
        // state; _stepListBox.PreviewMouseLeftButtonDown then re-sets it for list clicks.
        PreviewMouseLeftButtonDown += (_, _) =>
        {
            _listDragStart       = new Point(double.NaN, double.NaN);
            _listDragInProgress  = false;
            _listDragSourceIndex = -1;
        };

        _stepListBox.PreviewMouseLeftButtonDown += (_, e) =>
        {
            // Override the window-level reset: allow drag only when the press lands
            // directly on a ListBoxItem within this list.
            // IMPORTANT: use the hit-tested item's index, not SelectedIndex — at the time
            // PreviewMouseLeftButtonDown fires, selection hasn't changed yet, so SelectedIndex
            // still points to the previously selected item, causing accidental drags.
            var hit = e.OriginalSource as DependencyObject;
            var lbi = hit != null ? GetListBoxItemAncestor(_stepListBox, hit) : null;
            int hitIndex = lbi != null
                ? _stepListBox.ItemContainerGenerator.IndexFromContainer(lbi)
                : -1;
            _listDragStart       = hitIndex >= 0 ? e.GetPosition(_stepListBox) : new Point(double.NaN, double.NaN);
            _listDragInProgress  = false;
            _listDragSourceIndex = hitIndex;
        };

        _stepListBox.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _listDragInProgress) return;
            if (_listDragSourceIndex < 0) return;
            if (_stepListBox.SelectedItems.Count > 1) return;
            var pos  = e.GetPosition(_stepListBox);
            var diff = _listDragStart - pos;
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                // Guard: if the button was released before we reached the threshold
                // (fast click-release), skip the drag entirely.  DoDragDrop is a
                // blocking OLE call; starting it after mouse-up causes a freeze
                // because the nested message loop never sees the release event.
                if (Mouse.LeftButton != MouseButtonState.Pressed) {
                    _listDragSourceIndex = -1;
                    return;
                }
                _listDragInProgress = true;
                var item = _stepListBox.Items[_listDragSourceIndex];
                DragDrop.DoDragDrop(_stepListBox, item, DragDropEffects.Move);
                _listDragInProgress = false;
            }
        };

        _stepListBox.AllowDrop = true;
        _stepListBox.DragOver += (_, e) =>
        {
            if (_listDragSourceIndex < 0) { HideDragInsertLine(); return; }
            var pos = e.GetPosition(_stepListBox);
            int insertIndex = GetDropInsertIndex(_stepListBox, pos);
            ShowDragInsertLine(insertIndex);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        };
        _stepListBox.DragLeave += (_, _) => HideDragInsertLine();
        _stepListBox.Drop += (_, e) =>
        {
            HideDragInsertLine();
            if (_listDragSourceIndex < 0) return;
            var pos = e.GetPosition(_stepListBox);
            int destIndex = GetDropInsertIndex(_stepListBox, pos);
            destIndex = Math.Clamp(destIndex, 0, _activeTour.Steps.Count - 1);
            if (destIndex == _listDragSourceIndex) return;

            var step = _activeTour.Steps[_listDragSourceIndex];
            _activeTour.Steps.RemoveAt(_listDragSourceIndex);
            _activeTour.Steps.Insert(destIndex, step);

            if (!string.IsNullOrWhiteSpace(_workspaceFolderPath))
            {
                try { GuidedTourSaver.Save(_allTours, _workspaceFolderPath); }
                catch { /* ignore */ }
            }

            _stepListBox.Items.Clear();
            for (int i = 0; i < _activeTour.Steps.Count; i++)
                _stepListBox.Items.Add($"{i + 1}. {_activeTour.Steps[i].Title}");
            _stepListBox.SelectedIndex = destIndex;

            _stepIndex = destIndex;
            _jumpToStepCallback?.Invoke(destIndex);
            _livePreviewCallback?.Invoke();
        };

        // ── Context menu on the step list ────────────────────────────────────

        _ctxCopy  = new MenuItem { Header = "Copy Step" };
        _ctxCut   = new MenuItem { Header = "Cut Step" };
        _ctxPaste = new MenuItem { Header = "Paste Step" };

        _ctxCopy.Click  += (_, _) => CopySelectedSteps();
        _ctxCut.Click   += (_, _) => CutSelectedSteps();
        _ctxPaste.Click += (_, _) => PasteSteps();

        var stepContextMenu = new ContextMenu();
        stepContextMenu.Items.Add(_ctxCopy);
        stepContextMenu.Items.Add(_ctxCut);
        stepContextMenu.Items.Add(_ctxPaste);
        stepContextMenu.Opened += OnStepContextMenuOpening;
        _stepListBox.ContextMenu = stepContextMenu;

        // ── Sidebar buttons ──────────────────────────────────────────────────

        var listSidebarButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var addBtn    = MakeIconButton("+", new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF)), fontSize: 30, glyphVerticalOffset: -3);
        var deleteBtn = MakeIconButton("✕", new SolidColorBrush(Color.FromRgb(0xE0, 0x30, 0x30)));
        addBtn.Margin    = new Thickness(0, 0, 2, 0);
        deleteBtn.Margin = new Thickness(0);
        addBtn.Click    += (_, _) => _addStepAfterCallback?.Invoke();
        deleteBtn.Click += (_, _) => _deleteStepCallback?.Invoke();
        listSidebarButtons.Children.Add(addBtn);
        listSidebarButtons.Children.Add(deleteBtn);

        var stepListHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        DockPanel.SetDock(listSidebarButtons, Dock.Left);
        DockPanel.SetDock(_stepCountLabel,    Dock.Right);
        listSidebarButtons.Margin = new Thickness(0);
        stepListHeader.Children.Add(listSidebarButtons);
        stepListHeader.Children.Add(_stepCountLabel);

        var sidebarPanel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(stepListHeader, Dock.Top);
        sidebarPanel.Children.Add(stepListHeader);
        sidebarPanel.Children.Add(_stepListBoxHost);

        _multiSelectLabel = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Visibility          = Visibility.Collapsed,
        };
        _multiSelectLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        _multiSelectLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");

        var formContentGrid = new Grid();
        formContentGrid.Children.Add(formPanel);
        formContentGrid.Children.Add(_multiSelectLabel);

        var formScrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            Content                       = formContentGrid,
        };

        // ── Tour list (leftmost sidebar) ─────────────────────────────────────

        _tourListBox = new ListBox { Width = 300 };
        ScrollViewer.SetHorizontalScrollBarVisibility(_tourListBox, ScrollBarVisibility.Disabled);
        _tourListBox.SetResourceReference(ListBox.BackgroundProperty, "InputSurface");
        _tourListBox.SetResourceReference(ListBox.ForegroundProperty, "LabelText");
        _tourListBox.SetResourceReference(ListBox.FontSizeProperty,   "FontSizeBody");

        for (int i = 0; i < allTours.Count; i++)
            _tourListBox.Items.Add(BuildTourListItem(i, allTours[i].Name));

        _tourListBox.SelectedIndex = allTours.IndexOf(activeTour);
        _tourListBox.SelectionChanged += OnTourListSelectionChanged;

        var tourRenameMenuItem = new MenuItem { Header = "Rename" };
        tourRenameMenuItem.Click += (_, _) =>
        {
            var idx = _tourListBox.SelectedIndex;
            if (idx >= 0)
                BeginTourRename(idx);
        };
        var tourContextMenu = new ContextMenu();
        tourContextMenu.Items.Add(tourRenameMenuItem);
        _tourListBox.ContextMenu = tourContextMenu;

        var addTourBtn= MakeIconButton("+", new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF)), fontSize: 30, glyphVerticalOffset: -3);
        var deleteTourBtn = MakeIconButton("✕", new SolidColorBrush(Color.FromRgb(0xE0, 0x30, 0x30)));
        addTourBtn.Margin    = new Thickness(0, 0, 2, 0);
        deleteTourBtn.Margin = new Thickness(0);
        addTourBtn.Click    += (_, _) => _addTourCallback?.Invoke();
        deleteTourBtn.Click += (_, _) => _deleteTourCallback?.Invoke();

        var tourSidebarButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        tourSidebarButtons.Children.Add(addTourBtn);
        tourSidebarButtons.Children.Add(deleteTourBtn);

        var tourSidebarPanel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(tourSidebarButtons, Dock.Top);
        tourSidebarPanel.Children.Add(tourSidebarButtons);
        tourSidebarPanel.Children.Add(_tourListBox);

        var descLabel = new TextBlock
        {
            Text              = "Lesson description:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 6, 0),
        };
        descLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        descLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");

        var descriptionRow = new DockPanel { Margin = new Thickness(14, 4, 14, 4) };
        DockPanel.SetDock(descLabel, Dock.Left);
        descriptionRow.LastChildFill = true;
        descriptionRow.Children.Add(descLabel);
        descriptionRow.Children.Add(_descriptionBox);

        var formColumn = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(descriptionRow, Dock.Top);
        formColumn.Children.Add(descriptionRow);
        formColumn.Children.Add(formScrollViewer);

        var contentSplit = new Grid();
        contentSplit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300, GridUnitType.Pixel) });
        contentSplit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320, GridUnitType.Pixel) });
        contentSplit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,   GridUnitType.Star)  });
        Grid.SetColumn(tourSidebarPanel,  0);
        Grid.SetColumn(sidebarPanel,      1);
        Grid.SetColumn(formColumn,        2);
        contentSplit.Children.Add(tourSidebarPanel);
        contentSplit.Children.Add(sidebarPanel);
        contentSplit.Children.Add(formColumn);

        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        layout.Children.Add(buttonRow);
        layout.Children.Add(contentSplit);

        contentArea.Child = layout;

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); PushLivePreview(); };
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _autoSaveTimer.Tick += (_, _) => { _autoSaveTimer.Stop(); PerformAutoSave(); };
        _markdownBox.TextChanged += (_, _) => { if (_isLoadingStep) return; _debounceTimer.Stop(); _debounceTimer.Start(); QueueAutoSave(); };
        Closed += (_, _) =>
        {
            _debounceTimer.Stop();
            if (_autoSaveTimer.IsEnabled) { _autoSaveTimer.Stop(); PerformAutoSave(); }
            CloseTargetOverlay();
            if (!WasSaved) RestoreOriginals();
            _onClosed?.Invoke(WasSaved);
        };

        _titleBox.TextChanged += (_, _) =>
        {
            if (_isLoadingStep) return;
            var newLabel = $"{_stepIndex + 1}. {_titleBox.Text.Trim()}";
            // Use _stepIndex directly — assigning Items[i] causes WPF to briefly
            // set SelectedIndex to -1, which would skip updates on subsequent keystrokes.
            if (_stepIndex >= 0 && _stepIndex < _stepListBox.Items.Count)
                _stepListBox.Items[_stepIndex] = newLabel;
            Title = BuildEditorTitle(_activeTour.Name, _stepIndex, _titleBox.Text.Trim());
            QueueAutoSave();
        };

        // Push an undo snapshot the moment focus leaves either text box so the
        // snapshot reflects in-flight edits rather than waiting for the auto-save timer.
        _markdownBox.LostFocus += (_, _) => { if (!_isLoadingStep) { SaveCurrentFieldsToStep(); PushUndoSnapshot(); } };
        _titleBox.LostFocus    += (_, _) => { if (!_isLoadingStep) { SaveCurrentFieldsToStep(); PushUndoSnapshot(); } };

        SnapshotCurrentValues();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (IsTourRenameActive())
                {
                    CancelActiveTourRename();
                    e.Handled = true;
                    return;
                }
                e.Handled = true;
                TryClose();
                return;
            }

            // Ctrl+B / Ctrl+I: markdown bold/italic in the markdown text box
            if (_markdownBox.IsFocused && (Keyboard.Modifiers & ModifierKeys.Control) != 0
                && (Keyboard.Modifiers & ModifierKeys.Shift) == 0
                && (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
            {
                if (e.Key == Key.B)
                {
                    MarkdownEditorCommands.ApplyBold(_markdownBox);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.I)
                {
                    MarkdownEditorCommands.ApplyItalic(_markdownBox);
                    e.Handled = true;
                    return;
                }
            }

            // Ctrl+Z: undo last change — but yield to the focused TextBox so WPF's
            // native per-control undo runs first.  Only intercept when no TextBox
            // has keyboard focus.
            if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0
                && (Keyboard.Modifiers & ModifierKeys.Shift) == 0
                && (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
            {
                if (FocusManager.GetFocusedElement(this) is TextBox)
                    return; // let WPF route to TextBox natively
                UndoLastChange();
                e.Handled = true;
                return;
            }

            // Ctrl+S: manual save-now shortcut
            if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0
                && (Keyboard.Modifiers & ModifierKeys.Shift) == 0
                && (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
            {
                PerformAutoSave();
                e.Handled = true;
                return;
            }

            // Route double-tap Ctrl PTT to whichever text box has focus
            var focused = FocusManager.GetFocusedElement(this) as TextBox;
            if (focused is not null && _ptt.HandlePreviewKeyDown(e, focused))
                e.Handled = true;
        };

        PreviewKeyUp += (_, e) =>
        {
            if (_ptt.HandlePreviewKeyUp(e))
                e.Handled = true;
        };
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void CommitSave()
    {
        if (PerformSave())
            SnapshotCurrentValues();
    }

    private void SaveCurrentFieldsToStep()
    {
        _step.Title            = _titleBox.Text.Trim();
        _step.MarkdownText     = _markdownBox.Text;
        _step.CalloutPlacement = GetSelectedPlacement();
        _step.TargetControlId  = _targetControlBox.Text.Trim();
        _step.AdvanceTrigger   = GetSelectedCommand(_advanceTriggerBox);

        _step.CommandsBefore = _commandBeforeBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
        _step.CommandBefore = string.Empty;

        _step.CommandsAfter = _commandAfterBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
        _step.CommandAfter = string.Empty;
        _activeTour.Description = _descriptionBox.Text.Trim();
        // TargetOffsetX/Y are updated live via UpdateCrosshairFromMouse; no action needed here
    }

    private bool PerformSave()
    {
        try
        {
            SaveCurrentFieldsToStep();

            SquadDashTrace.Write(TraceCategory.Callouts,
                $"PerformSave: stepIndex={_stepIndex}, title=\"{_step.Title}\", target=\"{_step.TargetControlId}\", placement={_step.CalloutPlacement}, markdownLen={_step.MarkdownText.Length}, workspacePath={(string.IsNullOrWhiteSpace(_workspaceFolderPath) ? "(none)" : _workspaceFolderPath)}");

            if (!string.IsNullOrWhiteSpace(_workspaceFolderPath))
            {
                try
                {
                    GuidedTourSaver.Save(_allTours, _workspaceFolderPath);
                    SquadDashTrace.Write(TraceCategory.Callouts, "PerformSave: disk save succeeded");
                }
                catch (Exception ex)
                {
                    SquadDashTrace.Write(TraceCategory.Callouts, $"PerformSave: disk save FAILED — {ex.Message}");
                    MessageBox.Show(
                        $"Step updated in memory but could not be saved to disk:\n{ex.Message}",
                        "Save Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            WasSaved = true;
            SquadDashTrace.Write(TraceCategory.Callouts, $"PerformSave: WasSaved=true, tourStepCount={_activeTour.Steps.Count}");
            return true;
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write(TraceCategory.Callouts, $"PerformSave: EXCEPTION — {ex}");
            MessageBox.Show(
                $"An unexpected error occurred while saving the step:\n{ex}",
                "Save Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private void PerformAutoSave()
    {
        if (string.IsNullOrWhiteSpace(_workspaceFolderPath)) return;
        _isLoadingStep = true;
        try
        {
            SaveCurrentFieldsToStep();
            PushUndoSnapshot();

            GuidedTourSaver.Save(_allTours, _workspaceFolderPath);
            WasSaved = true;
            ShowStatus("✓ Saved");

            var clearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            clearTimer.Tick += (_, _) => { clearTimer.Stop(); _statusLabel.Visibility = Visibility.Collapsed; };
            clearTimer.Start();
        }
        catch (Exception ex)
        {
            ShowStatus($"⚠ Auto-save failed: {ex.Message}");
        }
        finally
        {
            _isLoadingStep = false;
        }
    }

    private void QueueAutoSave()
    {
        if (_isLoadingStep) return;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private string SnapshotTourJson() =>
        System.Text.Json.JsonSerializer.Serialize(_activeTour.Steps);

    private void PushUndoSnapshot()
    {
        _undoStack.Push(SnapshotTourJson());
        if (_undoStack.Count > UndoStackMaxDepth)
        {
            var items = _undoStack.ToArray(); // top-first order
            _undoStack.Clear();
            foreach (var item in items.Take(UndoStackMaxDepth).Reverse())
                _undoStack.Push(item);
        }
    }

    private void UndoLastChange()
    {
        if (_undoStack.Count == 0)
        {
            ShowStatus("Nothing to undo");
            return;
        }
        var json = _undoStack.Pop();
        try
        {
            var steps = System.Text.Json.JsonSerializer.Deserialize<List<GuidedTourStep>>(json);
            if (steps is null) return;
            _activeTour.Steps.Clear();
            foreach (var s in steps)
                _activeTour.Steps.Add(s);

            if (!string.IsNullOrWhiteSpace(_workspaceFolderPath))
                GuidedTourSaver.Save(_allTours, _workspaceFolderPath);

            _stepIndex = Math.Clamp(_stepIndex, 0, Math.Max(0, _activeTour.Steps.Count - 1));

            RefreshAfterBulkEdit(_stepIndex);
            ShowStatus("↩ Undone");
            var clearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            clearTimer.Tick += (_, _) => { clearTimer.Stop(); ShowStatus(string.Empty); };
            clearTimer.Start();
        }
        catch (Exception ex)
        {
            ShowStatus($"⚠ Undo failed: {ex.Message}");
        }
    }

    private void TryClose()
    {
        Close();
    }

    /// <summary>
    /// Syncs the editor selection to the tour and step that are currently active in the running tour.
    /// If the active tour is different from what the editor is showing, switches to it (rebuilding the step list).
    /// If it's the same tour, just moves the step selection without rebuilding anything.
    /// Called by GuidedTourController.ShowCurrentStep() as the user navigates.
    /// </summary>
    public void SyncToActiveTourStep(GuidedTour activeTour, int stepIndex)
    {
        if (!ReferenceEquals(_activeTour, activeTour))
        {
            // Tour changed — switch fully (also updates _tourListBox selection)
            var tourIdx = _allTours.IndexOf(activeTour);
            if (tourIdx >= 0)
            {
                _isLoadingStep = true;
                try { _tourListBox.SelectedIndex = tourIdx; }
                finally { _isLoadingStep = false; }
            }
            SwitchActiveTour(activeTour, stepIndex);
            return;
        }

        // Same tour — move the step selection and load the step detail fields.
        // We do NOT use OnStepListSelectionChanged here: that handler bails when
        // _isLoadingStep is true, and setting SelectedIndex inside a guard would
        // leave the right-side panel showing the previously-selected step's data.
        if (stepIndex < 0 || stepIndex >= _stepListBox.Items.Count) return;
        LoadStep(stepIndex);    // sets _stepListBox.SelectedIndex and scrolls into view internally
    }

    /// <summary>
    /// Rebuilds the step list, selects the given index, and loads that step's fields.
    /// Called by the controller after adding, deleting, or reordering steps from within the editor.
    /// </summary>
    public void RefreshStepList(int selectIndex)
    {
        _stepListBox.Items.Clear();
        for (int i = 0; i < _activeTour.Steps.Count; i++)
            _stepListBox.Items.Add($"{i + 1}. {_activeTour.Steps[i].Title}");
        LoadStep(selectIndex);
    }

    /// <summary>
    /// Rebuilds the tour list and selects the given index.
    /// Called by the controller after adding or deleting a tour.
    /// </summary>
    public void RefreshTourList(int selectTourIndex)
    {
        _isLoadingStep = true;
        try
        {
            _tourListBox.Items.Clear();
            for (int i = 0; i < _allTours.Count; i++)
                _tourListBox.Items.Add(BuildTourListItem(i, _allTours[i].Name));
            _tourListBox.SelectedIndex = Math.Clamp(selectTourIndex, 0, Math.Max(0, _allTours.Count - 1));
        }
        finally { _isLoadingStep = false; }
    }

    /// <summary>
    /// Switches the active tour shown by the editor and rebuilds the step list.
    /// Called by the controller when the user selects a different tour.
    /// </summary>
    public void SwitchActiveTour(GuidedTour newTour, int selectStepIndex)
    {
        _activeTour = newTour;
        _undoStack.Clear();
        _isLoadingStep = true;
        try { _descriptionBox.Text = newTour.Description; }
        finally { _isLoadingStep = false; }
        RefreshStepList(selectStepIndex);
    }

    private static string BuildEditorTitle(string tourName, int stepIndex, string stepTitle) =>
        $"Guided Tour Editor — {tourName} — Step {stepIndex + 1}: {stepTitle}";

    /// <summary>Updates the window title after a tour rename.</summary>
    public void UpdateWindowTitle()
    {
        Title = BuildEditorTitle(_activeTour.Name, _stepIndex, _titleBox.Text.Trim());
    }

    /// <summary>Builds a Grid-based ListBox item for a tour entry (TextBlock + inline edit TextBox).</summary>
    private Grid BuildTourListItem(int index, string name)
    {
        var grid = new Grid();

        var label = new TextBlock
        {
            Text                = name,
            VerticalAlignment   = VerticalAlignment.Center,
            TextTrimming        = TextTrimming.CharacterEllipsis,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        label.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");

        var editBox = new TextBox
        {
            Text              = name,
            Visibility        = Visibility.Collapsed,
            BorderThickness   = new Thickness(0),
            Padding           = new Thickness(0),
            Background        = System.Windows.Media.Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
        };
        editBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
        editBox.SetResourceReference(TextBox.FontSizeProperty,   "FontSizeBody");

        editBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                var trimmed = editBox.Text.Trim();
                if (!string.IsNullOrEmpty(trimmed) && trimmed != label.Text)
                {
                    label.Text = trimmed;
                    _renameTourCallback?.Invoke(index, trimmed);
                }
                SwitchTourItemToDisplay(grid);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                editBox.Text = label.Text;
                SwitchTourItemToDisplay(grid);
                e.Handled = true;
            }
        };

        editBox.LostFocus += (_, _) =>
        {
            editBox.Text = label.Text;
            SwitchTourItemToDisplay(grid);
        };

        grid.Children.Add(label);
        grid.Children.Add(editBox);

        return grid;
    }

    private static void SwitchTourItemToDisplay(Grid itemGrid)
    {
        foreach (var child in itemGrid.Children)
        {
            if (child is TextBlock tb) tb.Visibility = Visibility.Visible;
            if (child is TextBox   tx) tx.Visibility = Visibility.Collapsed;
        }
    }

    private static void SwitchTourItemToEdit(Grid itemGrid)
    {
        foreach (var child in itemGrid.Children)
        {
            if (child is TextBlock tb) tb.Visibility = Visibility.Collapsed;
            if (child is TextBox   tx) { tx.Visibility = Visibility.Visible; tx.SelectAll(); tx.Focus(); }
        }
    }

    /// <summary>Enters inline-rename edit mode for the tour at <paramref name="index"/>.</summary>
    public void BeginTourRename(int index)
    {
        if (index < 0 || index >= _tourListBox.Items.Count) return;
        if (_tourListBox.Items[index] is not Grid itemGrid) return;

        // Sync TextBox text with current label in case it drifted
        var label   = itemGrid.Children.OfType<TextBlock>().FirstOrDefault();
        var editBox = itemGrid.Children.OfType<TextBox>().FirstOrDefault();
        if (label is null || editBox is null) return;
        editBox.Text = label.Text;

        SwitchTourItemToEdit(itemGrid);
    }

    private bool IsTourRenameActive()
    {
        for (int i = 0; i < _tourListBox.Items.Count; i++)
        {
            if (_tourListBox.Items[i] is Grid g)
            {
                var tx = g.Children.OfType<TextBox>().FirstOrDefault();
                if (tx is { Visibility: Visibility.Visible })
                    return true;
            }
        }
        return false;
    }

    private void CancelActiveTourRename()
    {
        for (int i = 0; i < _tourListBox.Items.Count; i++)
        {
            if (_tourListBox.Items[i] is Grid g)
            {
                var tx = g.Children.OfType<TextBox>().FirstOrDefault();
                if (tx is { Visibility: Visibility.Visible })
                {
                    var lb = g.Children.OfType<TextBlock>().FirstOrDefault();
                    if (lb is not null) tx.Text = lb.Text;
                    SwitchTourItemToDisplay(g);
                    return;
                }
            }
        }
    }

    private static int GetListBoxItemIndexAtPoint(ListBox listBox, Point point)
    {
        for (int i = 0; i < listBox.Items.Count; i++)
        {
            if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
            {
                var itemPos = item.TranslatePoint(new Point(0, 0), listBox);
                if (point.Y >= itemPos.Y && point.Y < itemPos.Y + item.ActualHeight)
                    return i;
            }
        }
        return -1;
    }

    private static int GetDropInsertIndex(ListBox listBox, Point point)
    {
        for (int i = 0; i < listBox.Items.Count; i++)
        {
            if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item) continue;
            var itemPos = item.TranslatePoint(new Point(0, 0), listBox);
            var midY = itemPos.Y + item.ActualHeight / 2.0;
            if (point.Y < midY)
                return i;
        }
        return listBox.Items.Count;
    }

    private void ShowDragInsertLine(int insertIndex)
    {
        double y;
        if (insertIndex < _stepListBox.Items.Count &&
            _stepListBox.ItemContainerGenerator.ContainerFromIndex(insertIndex) is ListBoxItem itemAt)
        {
            var pos = itemAt.TranslatePoint(new Point(0, 0), _stepListBox);
            y = pos.Y;
        }
        else if (insertIndex > 0 &&
                 _stepListBox.ItemContainerGenerator.ContainerFromIndex(insertIndex - 1) is ListBoxItem itemBefore)
        {
            var pos = itemBefore.TranslatePoint(new Point(0, 0), _stepListBox);
            y = pos.Y + itemBefore.ActualHeight;
        }
        else
        {
            y = 0;
        }

        Canvas.SetTop(_dragInsertLine, y - 1);
        Canvas.SetLeft(_dragInsertLine, 0);
        _dragInsertLine.Width = _stepListBox.ActualWidth;

        _dragInsertCanvas.Visibility = Visibility.Visible;
    }

    private void HideDragInsertLine()
    {
        _dragInsertCanvas.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Walks up the visual tree from <paramref name="element"/> to find a <see cref="ListBoxItem"/>
    /// that is a direct container of <paramref name="listBox"/>. Returns null if the element is not
    /// inside any of the list's items — e.g. it originates from a control outside the list.
    /// </summary>
    private static ListBoxItem? GetListBoxItemAncestor(ListBox listBox, DependencyObject element)
    {
        var current = element;
        while (current is not null)
        {
            if (current is ListBoxItem lbi &&
                listBox.ItemContainerGenerator.IndexFromContainer(lbi) >= 0)
                return lbi;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current)
                   ?? (current is System.Windows.FrameworkContentElement fce ? fce.Parent : null);
        }
        return null;
    }

    private void TryNavigate(int newIndex)
    {
        if (newIndex < 0 || newIndex >= _activeTour.Steps.Count) return;
        if (_autoSaveTimer.IsEnabled) { _autoSaveTimer.Stop(); PerformAutoSave(); }
        LoadStep(newIndex);
        _jumpToStepCallback?.Invoke(newIndex);
    }

    private void LoadStep(int index)
    {
        _isLoadingStep = true;
        try
        {
        var step = _activeTour.Steps[index];

        _originalMarkdown      = step.MarkdownText;
        _originalPlacement     = step.CalloutPlacement;
        _originalTargetOffsetX = step.TargetOffsetX;
        _originalTargetOffsetY = step.TargetOffsetY;

        _step      = step;
        _stepIndex = index;
        _onStepChanged?.Invoke(index);

        _titleBox.Text         = step.Title;
        _markdownBox.Text      = step.MarkdownText;
        _targetControlBox.Text = step.TargetControlId;

        var placements = new[] { "Auto", "North", "South", "East", "West" };
        for (int i = 0; i < _placementRadios.Length; i++)
            _placementRadios[i].IsChecked = string.Equals(placements[i], step.CalloutPlacement, StringComparison.OrdinalIgnoreCase);
        if (_placementRadios.All(r => r.IsChecked != true))
            _placementRadios[0].IsChecked = true;

        _commandBeforeBox.Text = string.Join("\n", step.EffectiveCommandsBefore);
        _commandAfterBox.Text  = string.Join("\n", step.EffectiveCommandsAfter);
        _advanceTriggerBox.Text = string.IsNullOrEmpty(step.AdvanceTrigger) ? "(none)" : step.AdvanceTrigger;

        var hasTarget = !string.IsNullOrWhiteSpace(step.TargetControlId);
        _crosshairCanvas.Visibility      = hasTarget ? Visibility.Visible : Visibility.Collapsed;
        _crosshairCoordsLabel.Visibility = _crosshairCanvas.Visibility;
        _crosshairCoordsLabel.Text       = FormatCrosshairCoords(step.TargetOffsetX, step.TargetOffsetY);
        if (hasTarget) RedrawCrosshair();

        SquadDashTrace.Write(TraceCategory.Callouts,
            $"LoadStep: target={step.TargetControlId}, offsetX={step.TargetOffsetX:F3}, offsetY={step.TargetOffsetY:F3}");
        CloseTargetOverlay();

        _statusLabel.Visibility = Visibility.Collapsed;

        Title                = BuildEditorTitle(_activeTour.Name, index, _activeTour.Steps[index].Title);
        _stepCountLabel.Text = $"Step {index + 1} of {_activeTour.Steps.Count}";
        UpdateNavigationState();
        SnapshotCurrentValues();
        _stepListBox.SelectedIndex = index;
        _stepListBox.ScrollIntoView(_stepListBox.SelectedItem);
        }
        finally
        {
            _isLoadingStep = false;
            _debounceTimer.Stop();
            _autoSaveTimer.Stop();
        }
        // Move focus to the markdown box so the user can start editing immediately.
        Dispatcher.InvokeAsync(() => _markdownBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void UpdateNavigationState()
    {
        _prevButton.IsEnabled = _stepIndex > 0;
        _nextButton.IsEnabled = _stepIndex < _activeTour.Steps.Count - 1;
        if (_stepListBox.SelectedIndex != _stepIndex)
            _stepListBox.SelectedIndex = _stepIndex;
    }

    private void OnStepListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingStep) return;

        int count = _stepListBox.SelectedItems.Count;

        if (count == 0) return;

        if (count > 1)
        {
            EnterMultiSelectMode();
            return;
        }

        // count == 1 — single-select path
        ExitMultiSelectMode();
        int newIndex = _stepListBox.SelectedIndex;
        if (newIndex < 0 || newIndex == _stepIndex) return;
        if (_autoSaveTimer.IsEnabled) { _autoSaveTimer.Stop(); PerformAutoSave(); }
        LoadStep(newIndex);
        _jumpToStepCallback?.Invoke(newIndex);
    }

    private void EnterMultiSelectMode()
    {
        int count = _stepListBox.SelectedItems.Count;
        _multiSelectLabel.Text       = $"{count} steps selected — select a single step to edit";
        _multiSelectLabel.Visibility = Visibility.Visible;
        _formPanel.IsEnabled         = false;
    }

    private void ExitMultiSelectMode()
    {
        _multiSelectLabel.Visibility = Visibility.Collapsed;
        _formPanel.IsEnabled         = true;
    }

    private void OnTourListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingStep) return;
        int newIndex = _tourListBox.SelectedIndex;
        if (newIndex < 0 || newIndex >= _allTours.Count) return;
        if (ReferenceEquals(_allTours[newIndex], _activeTour)) return;
        if (_autoSaveTimer.IsEnabled) { _autoSaveTimer.Stop(); PerformAutoSave(); }
        _switchTourCallback?.Invoke(newIndex);
    }

    private void SnapshotCurrentValues()
    {
        _snapTitle           = _titleBox.Text;
        _snapMarkdown        = _markdownBox.Text;
        _snapTargetControlId = _targetControlBox.Text;
        _snapPlacement       = GetSelectedPlacement();
        _snapAdvanceTrigger  = _advanceTriggerBox.Text;
        _snapCommandsBefore  = _commandBeforeBox.Text;
        _snapCommandsAfter   = _commandAfterBox.Text;
        _originalTargetOffsetX = _step.TargetOffsetX;
        _originalTargetOffsetY = _step.TargetOffsetY;
    }

    private bool HasUnsavedChanges()
    {
        if (_titleBox.Text            != _snapTitle)            return true;
        if (_markdownBox.Text         != _snapMarkdown)         return true;
        if (_targetControlBox.Text    != _snapTargetControlId)  return true;
        if (GetSelectedPlacement()    != _snapPlacement)        return true;
        if (_advanceTriggerBox.Text   != _snapAdvanceTrigger)   return true;
        if (_step.TargetOffsetX       != _originalTargetOffsetX) return true;
        if (_step.TargetOffsetY       != _originalTargetOffsetY) return true;
        if (_commandBeforeBox.Text != _snapCommandsBefore) return true;
        if (_commandAfterBox.Text  != _snapCommandsAfter)  return true;
        return false;
    }

    private string GetSelectedPlacement() =>
        _placementRadios.FirstOrDefault(r => r.IsChecked == true)?.Content as string ?? "Auto";

    private void PushLivePreview()
    {
        _step.MarkdownText     = _markdownBox.Text;
        _step.CalloutPlacement = GetSelectedPlacement();
        _livePreviewCallback?.Invoke();
    }

    private void RestoreOriginals()
    {
        _step.MarkdownText     = _originalMarkdown;
        _step.CalloutPlacement = _originalPlacement;
        _step.TargetOffsetX    = _originalTargetOffsetX;
        _step.TargetOffsetY    = _originalTargetOffsetY;
        _livePreviewCallback?.Invoke();
    }

    private void CaptureLayoutForStep()
    {
        _captureLayout?.Invoke();

        var layoutName      = $"step-{_stepIndex}";
        _step.PreAction     = $"LoadLayout:{layoutName}";

        ShowStatus($"Layout captured \u2014 PreAction set to \"LoadLayout:{layoutName}\".");
    }

    private void StartPickMode()
    {
        var mainWindow = Owner;
        if (mainWindow is null) return;

        Visibility = Visibility.Hidden;

        // Reset highlight elements so EnsureHighlightElements re-creates them on the new canvas.
        _pickWhiteRect = null;
        _pickBlackRect = null;
        _pickLabel     = null;

        // Gather all windows whose visual trees should be searchable during pick mode.
        // Extra windows (e.g. PreferencesWindow) come FIRST because they are visually in
        // front of mainWindow.  VisualTreeHelper.HitTest is purely geometric within a single
        // window's tree and does not know about z-order across windows, so we must search
        // front-to-back manually: if we searched mainWindow first, its elements would be
        // found at any point that overlaps mainWindow even when PreferencesWindow is on top.
        var extraWindows = _extraPickWindowsProvider?.Invoke() ?? [];
        var allWindows   = new List<Window>(extraWindows) { mainWindow };

        // One full-virtual-screen overlay — eliminates z-order conflicts between windows.
        // Alpha=1 makes it nearly invisible while still receiving mouse events (WPF skips
        // hit-testing for fully transparent pixels when AllowsTransparency=true).
        var overlay = new Window
        {
            Owner                     = mainWindow,
            WindowStyle               = WindowStyle.None,
            AllowsTransparency        = true,
            Background                = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Topmost                   = true,
            ShowInTaskbar             = false,
            Cursor                    = Cursors.Cross,
            Left                      = SystemParameters.VirtualScreenLeft,
            Top                       = SystemParameters.VirtualScreenTop,
            Width                     = SystemParameters.VirtualScreenWidth,
            Height                    = SystemParameters.VirtualScreenHeight,
            WindowStartupLocation     = WindowStartupLocation.Manual,
        };

        var hint = new TextBlock
        {
            Text                = "Click any element to select it as the tour target · Esc to cancel",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Top,
            Margin              = new Thickness(0, 12, 0, 0),
            Padding             = new Thickness(12, 6, 12, 6),
            Background          = new SolidColorBrush(Color.FromArgb(0xCC, 30, 30, 30)),
            Foreground          = Brushes.White,
            FontSize            = 13,
        };

        // Canvas holds the hover-highlight layer; it never intercepts mouse input.
        var canvas = new Canvas { IsHitTestVisible = false };

        var grid = new Grid();
        grid.Children.Add(canvas);
        grid.Children.Add(hint);
        overlay.Content = grid;

        // Cache the last hit element to avoid redundant tree walks on every MouseMove.
        DependencyObject? lastHitObj = null;
        (FrameworkElement? element, string? name) lastResult = (null, null);
        Window? lastHitWindow = null;

        // Find the topmost named element under the screen-space cursor, searching all windows.
        (FrameworkElement? fe, string? name, Window? win) HitTestAllWindows(Point overlayPos)
        {
            var screenPos = overlay.PointToScreen(overlayPos);
            foreach (var win in allWindows)
            {
                if (!win.IsVisible) continue;
                var winPos = win.PointFromScreen(screenPos);
                var hit = VisualTreeHelper.HitTest(win, winPos);
                if (hit?.VisualHit is DependencyObject hitObj)
                {
                    var (fe, name) = FindFirstUniqueNamedAncestor(hitObj, win);
                    if (fe != null && name != null)
                        return (fe, name, win);
                }
            }
            return (null, null, null);
        }

        overlay.MouseMove += (_, e) =>
        {
            var overlayPos = e.GetPosition(overlay);
            var screenPos  = overlay.PointToScreen(overlayPos);

            // Try the window that had the hit last frame first (avoids full search on every move).
            // Default to allWindows[0] (the frontmost window) rather than mainWindow so that
            // extra windows (PreferencesWindow) are tried first on the initial frame.
            Window? candidateWin = lastHitWindow ?? allWindows[0];
            var candidatePos = candidateWin.PointFromScreen(screenPos);
            var quickHit = VisualTreeHelper.HitTest(candidateWin, candidatePos);
            DependencyObject? hitObj = quickHit?.VisualHit;
            Window? hitWin = hitObj != null ? candidateWin : null;

            // If quick hit failed, search all other windows.
            if (hitObj == null)
            {
                foreach (var win in allWindows)
                {
                    if (!win.IsVisible || ReferenceEquals(win, candidateWin)) continue;
                    var winPos = win.PointFromScreen(screenPos);
                    var h = VisualTreeHelper.HitTest(win, winPos);
                    if (h?.VisualHit is DependencyObject obj) { hitObj = obj; hitWin = win; break; }
                }
            }

            if (hitObj is DependencyObject finalHitObj && hitWin is not null)
            {
                lastHitWindow = hitWin;
                if (!ReferenceEquals(finalHitObj, lastHitObj))
                {
                    lastHitObj = finalHitObj;
                    lastResult = FindFirstUniqueNamedAncestor(finalHitObj, hitWin);
                }

                var (fe, name) = lastResult;
                if (fe != null && name != null)
                {
                    var topLeft = overlay.PointFromScreen(fe.PointToScreen(new Point(0, 0)));
                    const double stroke = 2;
                    const double pad    = 2;
                    UpdateHighlight(canvas, topLeft, fe.ActualWidth, fe.ActualHeight, stroke, pad, name);
                    return;
                }
            }
            else
            {
                lastHitObj    = null;
                lastHitWindow = null;
                lastResult    = (null, null);
            }
            ClearHighlight(canvas);
        };

        overlay.MouseLeftButtonUp += (_, e) =>
        {
            var overlayPos = e.GetPosition(overlay);
            // Convert to screen coords NOW, before Close() tears down the window's HwndSource.
            // HitTestAllWindows calls overlay.PointToScreen() — if overlay is already closed
            // that call throws InvalidOperationException.
            var screenPosAtClick = overlay.PointToScreen(overlayPos);
            ClearHighlight(canvas);
            overlay.Close();
            Visibility = Visibility.Visible;
            Activate();

            try
            {
                // Re-use pre-captured screen position instead of calling overlay.PointToScreen.
                (FrameworkElement? fe, string? name, Window? _) hitResult = (null, null, null);
                foreach (var win in allWindows)
                {
                    if (!win.IsVisible) continue;
                    var winPos = win.PointFromScreen(screenPosAtClick);
                    var hit = VisualTreeHelper.HitTest(win, winPos);
                    if (hit?.VisualHit is DependencyObject hitObj)
                    {
                        var (hfe, hname) = FindFirstUniqueNamedAncestor(hitObj, win);
                        if (hfe != null) { hitResult = (hfe, hname, win); break; }
                    }
                }

                var (rfe, rname, _) = hitResult;
                if (rname != null)
                {
                    _targetControlBox.Text = rname;
                    PushLivePreview();
                    QueueAutoSave();
                }
                else if (rfe != null)
                {
                    ShowStatus("⚠ The clicked element has no unique x:Name — cannot use as a target. " +
                               "Assign an x:Name to this element or select a different target.");
                }
                else
                {
                    ShowStatus("⚠ No element found at that position. Try clicking a different area.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"An error occurred while picking the target element:\n{ex}",
                    "Pick Target Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        };

        overlay.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                ClearHighlight(canvas);
                overlay.Close();
                Visibility = Visibility.Visible;
                Activate();
            }
        };

        overlay.Show();
        overlay.Focus();
        // AttachExtraPickOverlay is no longer needed — one full-screen overlay handles all windows.
    }

    /// <summary>
    /// Creates a transparent pick-mode overlay covering <paramref name="extraWin"/> so the user
    /// can click elements in it just like in the main window.  The overlay closes when the main
    /// overlay closes, and vice versa.
    /// </summary>
    private void AttachExtraPickOverlay(Window extraWin, Window mainOverlay)
    {
        var extraPickWhiteRect = default(Rectangle?);
        var extraPickBlackRect = default(Rectangle?);
        var extraPickLabel     = default(Border?);

        var extraCanvas = new Canvas { IsHitTestVisible = false };
        var extraGrid   = new Grid();
        extraGrid.Children.Add(extraCanvas);

        var extraOverlay = new Window
        {
            Owner                 = extraWin,
            WindowStyle           = WindowStyle.None,
            AllowsTransparency    = true,
            Background            = new SolidColorBrush(Color.FromArgb(0x10, 0, 0, 0)),
            Topmost               = true,
            ShowInTaskbar         = false,
            Cursor                = Cursors.Cross,
            Left                  = extraWin.Left,
            Top                   = extraWin.Top,
            Width                 = extraWin.ActualWidth,
            Height                = extraWin.ActualHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content               = extraGrid,
        };

        void ClearExtraHighlight()
        {
            if (extraPickWhiteRect != null) extraPickWhiteRect.Visibility = Visibility.Collapsed;
            if (extraPickBlackRect != null) extraPickBlackRect.Visibility = Visibility.Collapsed;
            if (extraPickLabel     != null) extraPickLabel.Visibility     = Visibility.Collapsed;
        }

        DependencyObject? lastHitObj2 = null;
        (FrameworkElement? element, string? name) lastResult2 = (null, null);

        extraOverlay.MouseMove += (_, e) =>
        {
            var pos = e.GetPosition(extraWin);
            var hit = VisualTreeHelper.HitTest(extraWin, pos);
            if (hit?.VisualHit is DependencyObject hitObj)
            {
                if (!ReferenceEquals(hitObj, lastHitObj2))
                {
                    lastHitObj2 = hitObj;
                    lastResult2 = FindFirstUniqueNamedAncestor(hitObj, extraWin);
                }
                var (fe, name) = lastResult2;
                if (fe != null && name != null)
                {
                    var topLeft = extraOverlay.PointFromScreen(fe.PointToScreen(new Point(0, 0)));
                    if (extraPickWhiteRect is null)
                    {
                        extraPickBlackRect = new Rectangle { Stroke = Brushes.Black, StrokeThickness = 2, Fill = Brushes.Transparent, IsHitTestVisible = false };
                        extraPickWhiteRect = new Rectangle { Stroke = Brushes.White, StrokeThickness = 2, Fill = Brushes.Transparent, IsHitTestVisible = false };
                        extraPickLabel = new Border
                        {
                            Background = new SolidColorBrush(Color.FromArgb(0xCC, 20, 20, 20)),
                            Padding    = new Thickness(6, 2, 6, 2),
                            Child      = new TextBlock { Foreground = Brushes.White, FontSize = 11 },
                            IsHitTestVisible = false,
                        };
                        extraCanvas.Children.Add(extraPickBlackRect);
                        extraCanvas.Children.Add(extraPickWhiteRect);
                        extraCanvas.Children.Add(extraPickLabel);
                    }
                    const double stroke = 2, pad = 2;
                    UpdateHighlight(extraCanvas, topLeft, fe.ActualWidth, fe.ActualHeight, stroke, pad, name);
                    return;
                }
            }
            else
            {
                lastHitObj2 = null;
                lastResult2 = (null, null);
            }
            ClearExtraHighlight();
        };

        extraOverlay.MouseLeftButtonUp += (_, e) =>
        {
            ClearExtraHighlight();
            extraOverlay.Close();
            mainOverlay.Close();
            Visibility = Visibility.Visible;
            Activate();
            try
            {
                var pos = e.GetPosition(extraWin);
                var hit = VisualTreeHelper.HitTest(extraWin, pos);
                if (hit?.VisualHit is DependencyObject hitObj)
                {
                    var (_, name) = FindFirstUniqueNamedAncestor(hitObj, extraWin);
                    if (name != null) { _targetControlBox.Text = name; PushLivePreview(); QueueAutoSave(); }
                    else ShowStatus("⚠ The clicked element has no unique x:Name — cannot use as a target.");
                }
                else ShowStatus("⚠ No element found at that position.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error picking target element:\n{ex}", "Pick Target Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        extraOverlay.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { extraOverlay.Close(); mainOverlay.Close(); Visibility = Visibility.Visible; Activate(); }
        };

        // Close extra overlay when main overlay closes (ESC or click on main window side).
        mainOverlay.Closed += (_, _) => { if (extraOverlay.IsLoaded) extraOverlay.Close(); };

        extraOverlay.Show();
    }

    // ── Pick-mode: unique-name helpers ───────────────────────────────────────

    /// <summary>
    /// Walks up the visual tree from <paramref name="hitObj"/> and returns the first
    /// ancestor (or self) whose name is unique in the entire visual tree — first unique name wins.
    /// Names from IHaveAgentName / INamedControl are assumed inherently unique.
    /// </summary>
    private static (FrameworkElement? element, string? name) FindFirstUniqueNamedAncestor(
        DependencyObject? hitObj, DependencyObject treeRoot)
    {
        var current = hitObj;
        while (current is not null)
        {
            if (current is FrameworkElement fe)
            {
                var name = fe.Name;
                // Skip WPF internal template parts (PART_xxx) — unreliable for targeting.
                // Accept only if the name appears exactly once in the visual tree.
                if (!string.IsNullOrEmpty(name)
                    && !name.StartsWith("PART_", StringComparison.Ordinal)
                    && IsNameUniqueInTree(treeRoot, name))
                    return (fe, name);

                // DataContext-sourced names are inherently unique (agent/control identity).
                if (fe.DataContext is IHaveAgentName agentNamed && !string.IsNullOrEmpty(agentNamed.AgentName))
                    return (fe, agentNamed.AgentName);
                if (fe.DataContext is INamedControl namedControl && !string.IsNullOrEmpty(namedControl.ControlName))
                    return (fe, namedControl.ControlName);
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return (null, null);
    }

    private static bool IsNameUniqueInTree(DependencyObject root, string name)
    {
        int count = 0;
        CountNamedDescendants(root, name, ref count);
        return count == 1;
    }

    private static void CountNamedDescendants(DependencyObject node, string name, ref int count)
    {
        if (count > 1) return; // early exit once we know it's non-unique
        if (node is FrameworkElement fe && fe.Name == name)
            count++;
        int childCount = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < childCount; i++)
            CountNamedDescendants(VisualTreeHelper.GetChild(node, i), name, ref count);
    }

    // ── Pick-mode: hover highlight drawing ───────────────────────────────────

    private void UpdateHighlight(Canvas canvas, Point topLeft, double w, double h,
                                 double stroke, double pad, string name)
    {
        EnsureHighlightElements(canvas);

        // White rectangle — tight around the element
        Canvas.SetLeft(_pickWhiteRect!, topLeft.X);
        Canvas.SetTop(_pickWhiteRect!, topLeft.Y);
        _pickWhiteRect!.Width  = w;
        _pickWhiteRect!.Height = h;

        // Black rectangle — 2px outside the white one (visible on both light and dark backgrounds)
        Canvas.SetLeft(_pickBlackRect!, topLeft.X - pad - stroke);
        Canvas.SetTop(_pickBlackRect!, topLeft.Y - pad - stroke);
        _pickBlackRect!.Width  = w + (pad + stroke) * 2;
        _pickBlackRect!.Height = h + (pad + stroke) * 2;

        // Name label: above the white rect if room (>24px), otherwise just below
        var labelTop = topLeft.Y - 26;
        if (labelTop < 0) labelTop = topLeft.Y + h + 4;
        Canvas.SetLeft(_pickLabel!, topLeft.X);
        Canvas.SetTop(_pickLabel!, labelTop);
        ((TextBlock)_pickLabel!.Child).Text = name;

        _pickWhiteRect.Visibility = Visibility.Visible;
        _pickBlackRect.Visibility = Visibility.Visible;
        _pickLabel.Visibility     = Visibility.Visible;
    }

    private void ClearHighlight(Canvas canvas)
    {
        if (_pickWhiteRect != null) _pickWhiteRect.Visibility = Visibility.Collapsed;
        if (_pickBlackRect != null) _pickBlackRect.Visibility = Visibility.Collapsed;
        if (_pickLabel     != null) _pickLabel.Visibility     = Visibility.Collapsed;
    }

    private void EnsureHighlightElements(Canvas canvas)
    {
        if (_pickWhiteRect != null) return;

        _pickBlackRect = new Rectangle
        {
            Stroke           = Brushes.Black,
            StrokeThickness  = 2,
            Fill             = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        _pickWhiteRect = new Rectangle
        {
            Stroke           = Brushes.White,
            StrokeThickness  = 2,
            Fill             = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        _pickLabel = new Border
        {
            Background       = new SolidColorBrush(Color.FromArgb(0xCC, 20, 20, 20)),
            Padding          = new Thickness(6, 2, 6, 2),
            CornerRadius     = new CornerRadius(3),
            IsHitTestVisible = false,
            Child            = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize   = 12,
            },
        };
        canvas.Children.Add(_pickBlackRect);
        canvas.Children.Add(_pickWhiteRect);
        canvas.Children.Add(_pickLabel);
    }

    // ── Target highlight overlay ──────────────────────────────────────────────

    /// <summary>
    /// Creates or updates a transparent topmost overlay that draws a highlight rectangle
    /// and an anchor-dot over the current target element, so you can see exactly where
    /// the callout will attach during step editing.
    /// </summary>
    private void ShowOrUpdateTargetOverlay()
    {
        var mainWindow = Owner;
        if (mainWindow is null) return;

        var targetId = _targetControlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(targetId))
        {
            CloseTargetOverlay();
            return;
        }

        var fe = FindElementByName(mainWindow, targetId);
        if (fe is null || !fe.IsVisible)
        {
            SquadDashTrace.Write(TraceCategory.Callouts,
                $"TargetOverlay: target={targetId} — element not found in visual tree");
            CloseTargetOverlay();
            return;
        }

        if (_targetOverlay is null)
        {
            _targetOverlayCanvas = new Canvas { IsHitTestVisible = false };

            _overlayBlackRect = new Rectangle
            {
                Stroke = Brushes.Black, StrokeThickness = 2,
                Fill = Brushes.Transparent, IsHitTestVisible = false,
            };
            _overlayWhiteRect = new Rectangle
            {
                Stroke = Brushes.White, StrokeThickness = 2,
                Fill = Brushes.Transparent, IsHitTestVisible = false,
            };
            _overlayLabel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 20, 20, 20)),
                Padding = new Thickness(6, 2, 6, 2),
                CornerRadius = new CornerRadius(3),
                IsHitTestVisible = false,
                Child = new TextBlock { Foreground = Brushes.White, FontSize = 12 },
            };
            _overlayDot = new Ellipse
            {
                Width = 10, Height = 10,
                Fill = Brushes.Yellow, IsHitTestVisible = false,
            };

            _targetOverlayCanvas.Children.Add(_overlayBlackRect);
            _targetOverlayCanvas.Children.Add(_overlayWhiteRect);
            _targetOverlayCanvas.Children.Add(_overlayLabel);
            _targetOverlayCanvas.Children.Add(_overlayDot);

            _targetOverlay = new Window
            {
                Owner                 = mainWindow,
                WindowStyle           = WindowStyle.None,
                AllowsTransparency    = true,
                Background            = Brushes.Transparent,
                Topmost               = true,
                ShowInTaskbar         = false,
                IsHitTestVisible      = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left                  = mainWindow.Left,
                Top                   = mainWindow.Top,
                Width                 = mainWindow.ActualWidth,
                Height                = mainWindow.ActualHeight,
                Content               = _targetOverlayCanvas,
            };
            _targetOverlay.Show();
        }
        else
        {
            _targetOverlay.Left   = mainWindow.Left;
            _targetOverlay.Top    = mainWindow.Top;
            _targetOverlay.Width  = mainWindow.ActualWidth;
            _targetOverlay.Height = mainWindow.ActualHeight;
            if (!_targetOverlay.IsVisible) _targetOverlay.Show();
        }

        var screenTL = fe.PointToScreen(new Point(0, 0));
        var topLeft  = _targetOverlay.PointFromScreen(screenTL);
        double w     = fe.ActualWidth;
        double h     = fe.ActualHeight;

        const double stroke = 2;
        const double pad    = 2;

        Canvas.SetLeft(_overlayBlackRect!, topLeft.X - pad - stroke);
        Canvas.SetTop(_overlayBlackRect!,  topLeft.Y - pad - stroke);
        _overlayBlackRect!.Width  = w + (pad + stroke) * 2;
        _overlayBlackRect!.Height = h + (pad + stroke) * 2;

        Canvas.SetLeft(_overlayWhiteRect!, topLeft.X);
        Canvas.SetTop(_overlayWhiteRect!,  topLeft.Y);
        _overlayWhiteRect!.Width  = w;
        _overlayWhiteRect!.Height = h;

        var labelTop = topLeft.Y - 26;
        if (labelTop < 0) labelTop = topLeft.Y + h + 4;
        Canvas.SetLeft(_overlayLabel!, topLeft.X);
        Canvas.SetTop(_overlayLabel!,  labelTop);
        ((TextBlock)_overlayLabel!.Child).Text = targetId;

        double dotX = topLeft.X + _step.TargetOffsetX * w;
        double dotY = topLeft.Y + _step.TargetOffsetY * h;
        Canvas.SetLeft(_overlayDot!, dotX - 5);
        Canvas.SetTop(_overlayDot!,  dotY - 5);

        SquadDashTrace.Write(TraceCategory.Callouts,
            $"TargetOverlay: target={targetId}, found=True, screenBounds=({screenTL.X:F1},{screenTL.Y:F1} {w:F1}×{h:F1}), dotAt=({dotX:F1},{dotY:F1})");
    }

    private void CloseTargetOverlay()
    {
        if (_targetOverlay is null) return;
        _targetOverlay.Close();
        _targetOverlay       = null;
        _targetOverlayCanvas = null;
        _overlayBlackRect    = null;
        _overlayWhiteRect    = null;
        _overlayLabel        = null;
        _overlayDot          = null;
    }

    private void BrowseForControl()
    {
        var picker = new FrmControlPicker(Application.Current.MainWindow, _targetControlBox.Text)
        {
            Owner = this,
        };
        picker.ShowDialog();
        if (picker.SelectedName is not null)
            _targetControlBox.Text = picker.SelectedName;
    }

    private void ShowStatus(string message)
    {
        _statusLabel.Text       = message;
        _statusLabel.Visibility = Visibility.Visible;
    }

    // ── Crosshair picker helpers ──────────────────────────────────────────────

    private const double CrosshairPadding = 8.0;

    private Rect GetCrosshairRectBounds()
    {
        double canvasW = _crosshairCanvas.ActualWidth;
        double canvasH = _crosshairCanvas.ActualHeight;
        if (canvasW <= CrosshairPadding * 2 || canvasH <= CrosshairPadding * 2)
            return Rect.Empty;

        double availW = canvasW - CrosshairPadding * 2;
        double availH = canvasH - CrosshairPadding * 2;

        // Determine aspect ratio from the target element, fall back to 4:3
        double aspectRatio = 4.0 / 3.0;
        var targetId = _targetControlBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(targetId) && Owner is Window ownerWin)
        {
            var el = VisualTreeHelper.HitTest(ownerWin, new Point(-9999, -9999))?.VisualHit; // dummy — use name search
            el = null;
            // Walk the visual tree to find the element by name
            el = FindElementByName(ownerWin, targetId);
            if (el is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
                aspectRatio = fe.ActualWidth / fe.ActualHeight;
        }

        // Scale rectangle to fit inside the available area while preserving aspect ratio
        double rectW, rectH;
        if (availW / availH > aspectRatio)
        {
            rectH = availH;
            rectW = rectH * aspectRatio;
        }
        else
        {
            rectW = availW;
            rectH = rectW / aspectRatio;
        }

        double left = CrosshairPadding + (availW - rectW) / 2;
        double top  = CrosshairPadding + (availH - rectH) / 2;
        return new Rect(left, top, rectW, rectH);
    }

    private static FrameworkElement? FindElementByName(DependencyObject root, string name)
    {
        if (root is FrameworkElement fe && fe.Name == name)
            return fe;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = FindElementByName(child, name);
            if (found is not null) return found;
        }
        return null;
    }

    private void RedrawCrosshair()
    {
        _crosshairCanvas.Children.Clear();

        var bounds = GetCrosshairRectBounds();
        if (bounds.IsEmpty) return;

        // Background rectangle
        var rect = new System.Windows.Shapes.Rectangle
        {
            Width           = bounds.Width,
            Height          = bounds.Height,
            Fill            = Brushes.Transparent,
            StrokeThickness = 1,
        };
        rect.SetResourceReference(System.Windows.Shapes.Rectangle.StrokeProperty, "InputBorder");
        Canvas.SetLeft(rect, bounds.Left);
        Canvas.SetTop(rect, bounds.Top);
        _crosshairCanvas.Children.Add(rect);

        // Crosshair lines
        double crossX = bounds.Left + _step.TargetOffsetX * bounds.Width;
        double crossY = bounds.Top  + _step.TargetOffsetY * bounds.Height;

        var hLine = new System.Windows.Shapes.Line
        {
            X1              = bounds.Left,
            Y1              = crossY,
            X2              = bounds.Right,
            Y2              = crossY,
            StrokeThickness = 1,
        };
        hLine.SetResourceReference(System.Windows.Shapes.Line.StrokeProperty, "LabelText");
        _crosshairCanvas.Children.Add(hLine);

        var vLine = new System.Windows.Shapes.Line
        {
            X1              = crossX,
            Y1              = bounds.Top,
            X2              = crossX,
            Y2              = bounds.Bottom,
            StrokeThickness = 1,
        };
        vLine.SetResourceReference(System.Windows.Shapes.Line.StrokeProperty, "LabelText");
        _crosshairCanvas.Children.Add(vLine);
    }

    private void UpdateCrosshairFromMouse(Point canvasPos)
    {
        var bounds = GetCrosshairRectBounds();
        if (bounds.IsEmpty) return;

        double offsetX = Math.Max(0, Math.Min(1, (canvasPos.X - bounds.Left) / bounds.Width));
        double offsetY = Math.Max(0, Math.Min(1, (canvasPos.Y - bounds.Top)  / bounds.Height));

        _step.TargetOffsetX = offsetX;
        _step.TargetOffsetY = offsetY;

        SquadDashTrace.Write(TraceCategory.Callouts,
            $"CrosshairMoved: target={_targetControlBox.Text.Trim()}, offsetX={offsetX:F3}, offsetY={offsetY:F3}, canvasPos=({canvasPos.X:F1},{canvasPos.Y:F1})");
        _crosshairCoordsLabel.Text = FormatCrosshairCoords(offsetX, offsetY);
        RedrawCrosshair();
        ShowOrUpdateTargetOverlay();
        _livePreviewCallback?.Invoke();
        QueueAutoSave();
    }

    private static string FormatCrosshairCoords(double x, double y) =>
        $"X: {x:F2}  Y: {y:F2}";

    // ── UI factory helpers ────────────────────────────────────────────────────

    private static TextBlock MakeLabel(string text)
    {
        var label = new TextBlock
        {
            Text   = text,
            Margin = new Thickness(0, 8, 0, 2),
        };
        label.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        return label;
    }

    private static TextBox MakeTextBox(string text, bool multiLine)
    {
        var box = new TextBox
        {
            Text    = text,
            Padding = new Thickness(5, 4, 5, 4),
        };
        if (multiLine)
            box.TextWrapping = TextWrapping.Wrap;
        box.SetResourceReference(TextBox.BackgroundProperty,   "InputSurface");
        box.SetResourceReference(TextBox.BorderBrushProperty,  "InputBorder");
        box.SetResourceReference(TextBox.ForegroundProperty,   "LabelText");
        box.SetResourceReference(TextBox.FontSizeProperty,     "FontSizeBody");
        return box;
    }

    private static Button MakeButton(string content)
    {
        var btn = new Button
        {
            Content = content,
            Height  = 26,
            Margin  = new Thickness(3, 0, 3, 0),
            Padding = new Thickness(10, 2, 10, 2),
        };
        btn.SetResourceReference(Button.StyleProperty,   "ThemedButtonStyle");
        btn.SetResourceReference(Button.FontSizeProperty, "FontSizeBody");
        return btn;
    }

    /// <summary>Creates a square icon button with a large, colored glyph filling the button face.</summary>
    private static Button MakeIconButton(string glyph, Brush iconBrush, double fontSize = 20, double glyphVerticalOffset = 0)
    {
        var label = new TextBlock
        {
            Text               = glyph,
            FontSize           = fontSize,
            FontWeight         = FontWeights.Bold,
            Foreground         = iconBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Margin             = new Thickness(0, glyphVerticalOffset, 0, -glyphVerticalOffset),
        };
        var btn = new Button
        {
            Content = label,
            Width   = 32,
            Height  = 32,
            Padding = new Thickness(0),
            Margin  = new Thickness(3, 0, 3, 0),
        };
        btn.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        return btn;
    }

    private static ComboBox MakeCommandCombo(IEnumerable<string> items, string currentValue)
    {
        var cb = new ComboBox { IsEditable = true, Height = 26 };
        cb.SetResourceReference(ComboBox.StyleProperty,    "ThemedEditableComboBoxStyle");
        cb.SetResourceReference(ComboBox.FontSizeProperty, "FontSizeBody");
        foreach (var item in items)
            cb.Items.Add(item == "" ? "(none)" : item);
        var displayValue = string.IsNullOrEmpty(currentValue) ? "(none)" : currentValue;
        cb.Text = displayValue; // use Text (not SelectedItem) since editable combo
        return cb;
    }

    private static string GetSelectedCommand(ComboBox cb) =>
        !string.IsNullOrWhiteSpace(cb.Text) && cb.Text != "(none)" ? cb.Text.Trim() : string.Empty;

    // ── Multi-line command TextBox helpers ────────────────────────────────────

    private void AttachIntelliSenseToCommandBox(TextBox tb)
    {
        var helper = new TourIntelliSenseHelper(
            placementTarget:     tb,
            textSource:          tb,
            suggestionsProvider: _ => GetCommandSuggestions(GetCurrentLine(tb)),
            acceptCallback:      accepted => AcceptCommandOnCurrentLine(tb, accepted));
        _intelliSenseHelpers.Add(helper);
    }

    private static string GetCurrentLine(TextBox tb)
    {
        var text  = tb.Text;
        var caret = Math.Clamp(tb.CaretIndex, 0, text.Length);
        var start = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
        var end   = text.IndexOf('\n', caret);
        if (end < 0) end = text.Length;
        return text[start..end];
    }

    private static void AcceptCommandOnCurrentLine(TextBox tb, string accepted)
    {
        var text  = tb.Text;
        var caret = Math.Clamp(tb.CaretIndex, 0, text.Length);
        var start = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
        var end   = text.IndexOf('\n', caret);
        if (end < 0) end = text.Length;
        tb.Text       = text[..start] + accepted + text[end..];
        tb.CaretIndex = start + accepted.Length;
    }

    // ── Intellisense helpers ──────────────────────────────────────────────────

    private void AttachIntelliSenseToTargetBox()
    {
        var helper = new TourIntelliSenseHelper(
            placementTarget:   _targetControlBox,
            textSource:        _targetControlBox,
            suggestionsProvider: text =>
            {
                var filter = text.Trim();
                var names  = _elementNamesProvider?.Invoke() ?? Array.Empty<string>();
                return names
                    .Where(n => filter.Length == 0
                                || n.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => filter.Length > 0 && n.StartsWith(filter, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            },
            acceptCallback: accepted =>
            {
                _targetControlBox.Text = accepted;
            });
        _intelliSenseHelpers.Add(helper);
    }

    private void AttachIntelliSenseToComboBox(ComboBox cb, bool isCommand)
    {
        var innerTb = VisualTreeSearch.FindChild<TextBox>(cb);
        if (innerTb is null) return;

        var helper = new TourIntelliSenseHelper(
            placementTarget:   cb,
            textSource:        innerTb,
            suggestionsProvider: text => isCommand
                ? GetCommandSuggestions(text)
                : GetTriggerSuggestions(text),
            acceptCallback: accepted =>
            {
                if (isCommand)
                    ApplyCommandAccepted(cb, accepted);
                else
                    ApplyTriggerAccepted(accepted);
            });
        _intelliSenseHelpers.Add(helper);
    }

    private IReadOnlyList<string> GetCommandSuggestions(string rawText)
    {
        var colonIdx = rawText.IndexOf(": ");
        if (colonIdx >= 0)
        {
            var cmdName   = rawText[..colonIdx].Trim();
            var paramText = rawText[(colonIdx + 2)..];
            return GetCommandParamSuggestions(cmdName, paramText);
        }

        var filter = rawText.Trim();
        if (string.Equals(filter, "(none)", StringComparison.OrdinalIgnoreCase)) filter = "";

        return _commandItems
            .Where(c => c != "" && c != "(none)")
            .Where(c => filter.Length == 0 || c.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => filter.Length > 0 && c.StartsWith(filter, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<string> GetCommandParamSuggestions(string cmdName, string paramText)
    {
        if (s_preferencePageCommands.Contains(cmdName))
            return s_preferencePageNames
                .Where(p => paramText.Length == 0
                            || p.Contains(paramText, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => paramText.Length > 0 && p.StartsWith(paramText, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (s_elementNameCommands.Contains(cmdName))
        {
            var names = _elementNamesProvider?.Invoke() ?? Array.Empty<string>();
            return names
                .Where(n => paramText.Length == 0
                            || n.Contains(paramText, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => paramText.Length > 0 && n.StartsWith(paramText, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return Array.Empty<string>();
    }

    private void ApplyCommandAccepted(ComboBox cb, string accepted)
    {
        var text      = cb.Text;
        var colonIdx  = text.IndexOf(": ");

        if (colonIdx >= 0)
        {
            // Accepting a parameter value — replace the text after ": "
            cb.Text = text[..(colonIdx + 2)] + accepted;
        }
        else
        {
            // Accepting a command name — append ": " if the command takes a parameter
            var needsParam = _parameterizedCommandNames.Contains(accepted);
            cb.Text = needsParam ? accepted + ": " : accepted;
        }
        PushLivePreview();
        QueueAutoSave();
    }

    private IReadOnlyList<string> GetTriggerSuggestions(string rawText)
    {
        var colonIdx = rawText.IndexOf(": ");
        if (colonIdx >= 0)
        {
            var triggerName = rawText[..colonIdx].Trim();
            var paramText   = rawText[(colonIdx + 2)..];
            return GetTriggerParamSuggestions(triggerName, paramText);
        }

        var filter = rawText.Trim();
        if (string.Equals(filter, "(none)", StringComparison.OrdinalIgnoreCase)) filter = "";

        return _triggerItems
            .Where(t => t != "" && t != "(none)")
            .Where(t => filter.Length == 0 || t.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => filter.Length > 0 && t.StartsWith(filter, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<string> GetTriggerParamSuggestions(string triggerName, string paramText)
    {
        if (string.Equals(triggerName, "PreferencePageSelected", StringComparison.OrdinalIgnoreCase))
            return s_preferencePageNames
                .Where(p => paramText.Length == 0
                            || p.Contains(paramText, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => paramText.Length > 0 && p.StartsWith(paramText, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (string.Equals(triggerName, "MenuOpened", StringComparison.OrdinalIgnoreCase))
        {
            var names = _elementNamesProvider?.Invoke() ?? Array.Empty<string>();
            return names
                .Where(n => paramText.Length == 0
                            || n.Contains(paramText, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => paramText.Length > 0 && n.StartsWith(paramText, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return Array.Empty<string>();
    }

    private void ApplyTriggerAccepted(string accepted)
    {
        var text     = _advanceTriggerBox.Text;
        var colonIdx = text.IndexOf(": ");

        if (colonIdx >= 0)
        {
            _advanceTriggerBox.Text = text[..(colonIdx + 2)] + accepted;
        }
        else
        {
            var needsParam = s_paramTriggerNames.Contains(accepted);
            _advanceTriggerBox.Text = needsParam ? accepted + ": " : accepted;
        }
        PushLivePreview();
        QueueAutoSave();
    }

    // ── Multi-select copy/cut/paste ───────────────────────────────────────────

    private void OnStepContextMenuOpening(object sender, System.Windows.RoutedEventArgs e)
    {
        int selectedCount = _stepListBox.SelectedItems.Count;
        bool hasSelection = selectedCount > 0;

        _ctxCopy.IsEnabled = hasSelection;
        _ctxCut.IsEnabled  = hasSelection;

        _ctxCopy.Header = selectedCount > 1
            ? $"Copy Steps ({selectedCount})"
            : "Copy Step";
        _ctxCut.Header = selectedCount > 1
            ? $"Cut Steps ({selectedCount})"
            : "Cut Step";

        _ctxPaste.IsEnabled = TryReadClipboardSteps(out _);
    }

    private void CopySelectedSteps()
    {
        var steps = GetSelectedStepsOrdered();
        if (steps.Count == 0) return;
        var json = ClipboardFormatMarker + System.Text.Json.JsonSerializer.Serialize(steps, s_clipboardJsonOptions);
        Clipboard.SetText(json);
    }

    private void CutSelectedSteps()
    {
        var steps = GetSelectedStepsOrdered();
        if (steps.Count == 0) return;
        var json = ClipboardFormatMarker + System.Text.Json.JsonSerializer.Serialize(steps, s_clipboardJsonOptions);
        Clipboard.SetText(json);

        var indicesToRemove = GetSelectedIndicesOrdered();
        foreach (int idx in indicesToRemove.OrderByDescending(i => i))
            _activeTour.Steps.RemoveAt(idx);

        if (!string.IsNullOrWhiteSpace(_workspaceFolderPath))
        {
            try { GuidedTourSaver.Save(_allTours, _workspaceFolderPath); }
            catch { /* ignore */ }
        }

        ExitMultiSelectMode();
        int selectAfter = indicesToRemove[0] < _activeTour.Steps.Count
            ? indicesToRemove[0]
            : Math.Max(0, _activeTour.Steps.Count - 1);
        RefreshAfterBulkEdit(selectAfter);
    }

    private void PasteSteps()
    {
        if (!TryReadClipboardSteps(out var steps) || steps is null)
        {
            ShowStatus("⚠ Clipboard does not contain valid tour step data.");
            return;
        }

        int insertAfter = _stepListBox.SelectedIndex >= 0
            ? _stepListBox.SelectedIndex
            : _activeTour.Steps.Count - 1;

        for (int i = 0; i < steps.Count; i++)
            _activeTour.Steps.Insert(insertAfter + 1 + i, steps[i]);

        if (!string.IsNullOrWhiteSpace(_workspaceFolderPath))
        {
            try { GuidedTourSaver.Save(_allTours, _workspaceFolderPath); }
            catch { /* ignore */ }
        }

        ExitMultiSelectMode();
        RefreshAfterBulkEdit(insertAfter + steps.Count);
    }

    private bool TryReadClipboardSteps(out List<GuidedTourStep>? steps)
    {
        steps = null;
        try
        {
            if (!Clipboard.ContainsText()) return false;
            var text = Clipboard.GetText();
            if (!text.StartsWith(ClipboardFormatMarker, StringComparison.Ordinal)) return false;
            var json = text.Substring(ClipboardFormatMarker.Length);
            var result = System.Text.Json.JsonSerializer.Deserialize<List<GuidedTourStep>>(json, s_clipboardJsonOptions);
            if (result is null || result.Count == 0) return false;
            steps = result;
            return true;
        }
        catch { return false; }
    }

    private List<GuidedTourStep> GetSelectedStepsOrdered()
    {
        return GetSelectedIndicesOrdered()
            .Select(i => _activeTour.Steps[i])
            .ToList();
    }

    private List<int> GetSelectedIndicesOrdered()
    {
        return _stepListBox.SelectedItems
            .Cast<object>()
            .Select(item => _stepListBox.Items.IndexOf(item))
            .Where(i => i >= 0)
            .OrderBy(i => i)
            .ToList();
    }

    private void RefreshAfterBulkEdit(int selectIndex = -1)
    {
        _stepListBox.Items.Clear();
        for (int i = 0; i < _activeTour.Steps.Count; i++)
            _stepListBox.Items.Add($"{i + 1}. {_activeTour.Steps[i].Title}");
        int idx = selectIndex >= 0
            ? Math.Clamp(selectIndex, 0, Math.Max(0, _activeTour.Steps.Count - 1))
            : Math.Min(_stepIndex, Math.Max(0, _activeTour.Steps.Count - 1));
        if (idx >= 0 && _activeTour.Steps.Count > 0)
        {
            _stepListBox.SelectedIndex = idx;
            _stepIndex = idx;
            _jumpToStepCallback?.Invoke(idx);
        }
        _livePreviewCallback?.Invoke();
    }
}

// ── Control picker ────────────────────────────────────────────────────────────

/// <summary>
/// Small modal window showing a searchable list of all named WPF elements
/// currently present in the visual tree of the main window.
/// </summary>
internal sealed class FrmControlPicker : ChromedWindow
{
    private readonly List<string> _allNames;
    private readonly ListBox      _list;
    private readonly TextBox      _filterBox;

    /// <summary>The element name chosen by the user, or <c>null</c> if cancelled.</summary>
    public string? SelectedName { get; private set; }

    public FrmControlPicker(DependencyObject root, string currentName)
        : base(captionHeight: 34, resizeMode: ResizeMode.NoResize, resizeBorderThickness: 0)
    {
        Title                 = "Pick Target Control";
        Width                 = 360;
        Height                = 460;
        ShowInTaskbar         = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _allNames = CollectNamedElements(root)
            .Distinct()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var contentArea = ApplyOuterBorder("AppSurface", "Pick Target Control");

        _filterBox = new TextBox
        {
            Height  = 26,
            Padding = new Thickness(5, 3, 5, 3),
            Margin  = new Thickness(10, 10, 10, 6),
        };
        _filterBox.SetResourceReference(TextBox.BackgroundProperty,  "InputSurface");
        _filterBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _filterBox.SetResourceReference(TextBox.ForegroundProperty,  "LabelText");
        _filterBox.SetResourceReference(TextBox.FontSizeProperty,    "FontSizeBody");
        _filterBox.TextChanged += (_, _) => ApplyFilter();

        _list = new ListBox { Margin = new Thickness(10, 0, 10, 8) };
        _list.SetResourceReference(ListBox.BackgroundProperty,  "AppSurface");
        _list.SetResourceReference(ListBox.BorderBrushProperty, "InputBorder");
        _list.SetResourceReference(ListBox.ForegroundProperty,  "LabelText");
        _list.MouseDoubleClick += (_, _) => CommitSelection();

        var selectButton = new Button
        {
            Content = "Select",
            Width   = 80,
            Height  = 26,
            Margin  = new Thickness(3, 0, 3, 0),
            Padding = new Thickness(10, 2, 10, 2),
        };
        selectButton.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        selectButton.Click += (_, _) => CommitSelection();

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width   = 70,
            Height  = 26,
            Margin  = new Thickness(3, 0, 3, 0),
            Padding = new Thickness(10, 2, 10, 2),
        };
        cancelButton.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        cancelButton.Click += (_, _) => Close();

        var buttonRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(10, 2, 10, 10),
        };
        buttonRow.Children.Add(selectButton);
        buttonRow.Children.Add(cancelButton);

        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_filterBox,  Dock.Top);
        layout.Children.Add(_filterBox);
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        layout.Children.Add(buttonRow);
        layout.Children.Add(_list);

        contentArea.Child = layout;

        PopulateList(_allNames, currentName);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            if (e.Key == Key.Enter) CommitSelection();
        };

        Loaded += (_, _) => _filterBox.Focus();
    }

    private void ApplyFilter()
    {
        var filter   = _filterBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allNames
            : _allNames.Where(n => n.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        PopulateList(filtered, null);
    }

    private void PopulateList(List<string> names, string? preselect)
    {
        _list.Items.Clear();
        foreach (var name in names)
            _list.Items.Add(name);

        if (preselect is not null && _list.Items.Contains(preselect))
            _list.SelectedItem = preselect;
        else if (_list.Items.Count > 0)
            _list.SelectedIndex = 0;
    }

    private void CommitSelection()
    {
        if (_list.SelectedItem is string name)
        {
            SelectedName = name;
            Close();
        }
    }

    private static IEnumerable<string> CollectNamedElements(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
                yield return fe.Name;
            foreach (var name in CollectNamedElements(child))
                yield return name;
        }
    }
}

// ── Command text editor ───────────────────────────────────────────────────────

/// <summary>
/// Small modal dialog for editing a command string that may contain literal
/// <c>\n</c> escape sequences.  Expands them to real newlines for comfortable
/// editing, then collapses them back on OK.
/// </summary>
internal sealed class FrmCommandTextEditor : ChromedWindow
{
    private readonly TextBox _textBox;

    /// <summary>
    /// The edited command text on successful OK, with newlines encoded back as
    /// literal <c>\n</c>.  <c>null</c> if the dialog was cancelled.
    /// </summary>
    public string? ResultText { get; private set; }

    public FrmCommandTextEditor(string initialText)
        : base(captionHeight: 34, resizeMode: ResizeMode.CanResizeWithGrip, resizeBorderThickness: 6)
    {
        Title                 = "Edit Command";
        Width                 = 520;
        Height                = 340;
        ShowInTaskbar         = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var contentArea = ApplyOuterBorder("AppSurface", "Edit Command");

        _textBox = new TextBox
        {
            AcceptsReturn               = true,
            TextWrapping                = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding                     = new Thickness(5, 4, 5, 4),
            Margin                      = new Thickness(10, 10, 10, 6),
            Text                        = initialText.Replace("\\n", Environment.NewLine),
        };
        _textBox.SetResourceReference(TextBox.BackgroundProperty,  "InputSurface");
        _textBox.SetResourceReference(TextBox.ForegroundProperty,  "LabelText");
        _textBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        _textBox.SetResourceReference(TextBox.FontSizeProperty,    "FontSizeBody");

        var okButton = new Button
        {
            Content   = "OK",
            IsDefault = true,
            Height    = 26,
            Margin    = new Thickness(3, 0, 3, 0),
            Padding   = new Thickness(10, 2, 10, 2),
        };
        okButton.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        okButton.Click += (_, _) => CommitOk();

        var cancelButton = new Button
        {
            Content  = "Cancel",
            IsCancel = true,
            Height   = 26,
            Margin   = new Thickness(3, 0, 3, 0),
            Padding  = new Thickness(10, 2, 10, 2),
        };
        cancelButton.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        cancelButton.Click += (_, _) => { DialogResult = false; };

        var buttonRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(10, 4, 10, 10),
        };
        buttonRow.Children.Add(okButton);
        buttonRow.Children.Add(cancelButton);

        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        layout.Children.Add(buttonRow);
        layout.Children.Add(_textBox);

        contentArea.Child = layout;

        Loaded += (_, _) =>
        {
            _textBox.Focus();
            _textBox.CaretIndex = _textBox.Text.Length;
        };
    }

    private void CommitOk()
    {
        ResultText   = _textBox.Text.Replace("\r\n", "\\n").Replace("\n", "\\n");
        DialogResult = true;
    }
}
