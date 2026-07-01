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
    private readonly GuidedTour        _activeTour;
    private readonly List<GuidedTour>  _allTours;
    private readonly string?           _workspaceFolderPath;
    private readonly Action?           _captureLayout;

    private readonly Action?           _livePreviewCallback;
    private string                     _originalMarkdown;
    private string                     _originalPlacement;
    private double                     _originalTargetOffsetX;
    private double                     _originalTargetOffsetY;
    private readonly DispatcherTimer   _debounceTimer;

    // Navigation state
    private bool                       _isDirty;
    private bool                       _suppressDirty;
    private Button                     _prevButton = null!;
    private Button                     _nextButton = null!;
    private TextBlock                  _stepCountLabel = null!;

    // PTT voice dictation
    private readonly PttTextBoxAttachment _ptt;

    // Form controls
    private readonly TextBox       _titleBox;
    private readonly TextBox       _markdownBox;
    private readonly RadioButton[] _placementRadios;
    private readonly TextBox       _targetControlBox;
    private readonly TextBlock     _statusLabel;
    private readonly ComboBox      _advanceTriggerBox;

    // Multi-command rows
    private readonly List<CommandRow>  _commandBeforeRows = new();
    private readonly List<CommandRow>  _commandAfterRows  = new();
    private StackPanel                 _commandBeforePanel = null!;
    private StackPanel                 _commandAfterPanel  = null!;
    private string[]                   _commandItems = Array.Empty<string>();

    // Crosshair picker
    private readonly Canvas        _crosshairCanvas;
    private readonly TextBlock     _crosshairCoordsLabel;
    private bool                   _crosshairDragging;

    // Pick-mode hover highlight elements (recreated each time pick mode opens)
    private Rectangle? _pickWhiteRect;
    private Rectangle? _pickBlackRect;
    private Border?    _pickLabel;

    /// <summary>True if the user clicked Save and the step was persisted.</summary>
    public bool WasSaved { get; private set; }

    public FrmGuidedTourStepEditor(
        GuidedTourStep   step,
        int              stepIndex,
        GuidedTour       activeTour,
        List<GuidedTour> allTours,
        string?          workspaceFolderPath,
        Window           owner,
        Action?          captureLayout        = null,
        Action?          livePreviewCallback  = null,
        GuidedTourCommandRegistry? commandRegistry = null,
        GuidedTourAdvanceTriggerRegistry? triggerRegistry = null)
        : base(captionHeight: 34, resizeMode: ResizeMode.NoResize, resizeBorderThickness: 0)
    {
        _originalMarkdown    = step.MarkdownText;
        _originalPlacement   = step.CalloutPlacement;
        _originalTargetOffsetX = step.TargetOffsetX;
        _originalTargetOffsetY = step.TargetOffsetY;
        _livePreviewCallback = livePreviewCallback;

        _step                = step;
        _stepIndex           = stepIndex;
        _activeTour          = activeTour;
        _allTours            = allTours;
        _workspaceFolderPath = workspaceFolderPath;
        _captureLayout       = captureLayout;

        Title                 = $"Edit Step {stepIndex + 1} — {activeTour.Name}";
        Width                 = 560;
        SizeToContent         = SizeToContent.Height;
        ShowInTaskbar         = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner                 = owner;

        var contentArea = ApplyOuterBorder("AppSurface", Title);

        // PTT voice dictation — uses the same settings store as the rest of the app
        _ptt = new PttTextBoxAttachment(() => new ApplicationSettingsStore().Load(), this, Dispatcher);
        Closed += (_, _) => _ptt.Dispose();

        // ── Form fields ───────────────────────────────────────────────────────

        _titleBox = MakeTextBox(step.Title, multiLine: false);

        _markdownBox = MakeTextBox(step.MarkdownText, multiLine: true);
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
                rb.Checked += (_, _) => PushLivePreview();
                return rb;
            })
            .ToArray();

        if (_placementRadios.All(r => r.IsChecked != true))
            _placementRadios[0].IsChecked = true;

        foreach (var rb in _placementRadios)
            placementRow.Children.Add(rb);

        _targetControlBox = MakeTextBox(step.TargetControlId, multiLine: false);

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
        _commandItems = new[] { "" }.Concat(commandNames).ToArray();

        _commandBeforePanel = new StackPanel();
        _commandAfterPanel  = new StackPanel();

        // Populate initial rows from effective command lists (handles legacy single-string migration)
        var initialBefore = step.EffectiveCommandsBefore;
        var initialAfter  = step.EffectiveCommandsAfter;
        foreach (var cmd in initialBefore.Count > 0 ? initialBefore : (IReadOnlyList<string>)[string.Empty])
            AddCommandRowToPanel(_commandBeforeRows, _commandBeforePanel, cmd);
        foreach (var cmd in initialAfter.Count > 0 ? initialAfter : (IReadOnlyList<string>)[string.Empty])
            AddCommandRowToPanel(_commandAfterRows, _commandAfterPanel, cmd);

        var addBeforeButton = MakeAddRowButton(_commandBeforeRows, _commandBeforePanel);
        var addAfterButton  = MakeAddRowButton(_commandAfterRows,  _commandAfterPanel);

        var triggerNames = triggerRegistry?.TriggerNames ?? Array.Empty<string>();
        var triggerItems = new[] { "" }.Concat(triggerNames).ToArray();
        _advanceTriggerBox = MakeCommandCombo(triggerItems, step.AdvanceTrigger);

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
            Text                = $"Step {stepIndex + 1} of {activeTour.Steps.Count}",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(0, 4, 0, 0),
        };
        _stepCountLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        _stepCountLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");

        var formPanel = new StackPanel { Margin = new Thickness(14, 10, 14, 8) };
        formPanel.Children.Add(_stepCountLabel);
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
        formPanel.Children.Add(MakeLabel("Command Before"));
        formPanel.Children.Add(_commandBeforePanel);
        formPanel.Children.Add(addBeforeButton);
        formPanel.Children.Add(MakeLabel("Command After"));
        formPanel.Children.Add(_commandAfterPanel);
        formPanel.Children.Add(addAfterButton);
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

        var saveButton = MakeButton("Save");
        saveButton.IsDefault = true;
        saveButton.Click += (_, _) => CommitSave();

        var cancelButton = MakeButton("Cancel");
        cancelButton.IsCancel = true;
        cancelButton.Click += (_, _) => Close();

        var leftButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        leftButtons.Children.Add(_prevButton);
        leftButtons.Children.Add(_nextButton);

        var rightButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        rightButtons.Children.Add(saveButton);
        rightButtons.Children.Add(cancelButton);

        var buttonRow = new DockPanel
        {
            Margin        = new Thickness(14, 4, 14, 12),
            LastChildFill = false,
        };
        DockPanel.SetDock(leftButtons,  Dock.Left);
        DockPanel.SetDock(rightButtons, Dock.Right);
        buttonRow.Children.Add(leftButtons);
        buttonRow.Children.Add(rightButtons);

        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        layout.Children.Add(buttonRow);
        layout.Children.Add(formPanel);

        contentArea.Child = layout;

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); PushLivePreview(); };
        _markdownBox.TextChanged += (_, _) => { _debounceTimer.Stop(); _debounceTimer.Start(); };
        Closed += (_, _) => { _debounceTimer.Stop(); if (!WasSaved) RestoreOriginals(); };

        // ── Dirty tracking ────────────────────────────────────────────────────

        void MarkDirty() { if (!_suppressDirty) _isDirty = true; }
        _titleBox.TextChanged       += (_, _) => MarkDirty();
        _markdownBox.TextChanged    += (_, _) => MarkDirty();
        _targetControlBox.TextChanged += (_, _) => MarkDirty();
        _advanceTriggerBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => MarkDirty()));
        foreach (var rb in _placementRadios)
            rb.Checked += (_, _) => MarkDirty();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); return; }

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
            Close();
    }

    private bool PerformSave()
    {
        try
        {
            _step.Title            = _titleBox.Text.Trim();
            _step.MarkdownText     = _markdownBox.Text;
            _step.CalloutPlacement = GetSelectedPlacement();
            _step.TargetControlId  = _targetControlBox.Text.Trim();
            _step.AdvanceTrigger   = GetSelectedCommand(_advanceTriggerBox);

            _step.CommandsBefore = _commandBeforeRows
                .Select(r => GetSelectedCommand(r.Box))
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
            _step.CommandBefore = string.Empty;

            _step.CommandsAfter = _commandAfterRows
                .Select(r => GetSelectedCommand(r.Box))
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
            _step.CommandAfter = string.Empty;
            // TargetOffsetX/Y are updated live via UpdateCrosshairFromMouse; no action needed here

            if (!string.IsNullOrWhiteSpace(_workspaceFolderPath))
            {
                try
                {
                    GuidedTourSaver.Save(_allTours, _workspaceFolderPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Step updated in memory but could not be saved to disk:\n{ex.Message}",
                        "Save Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            WasSaved = true;
            _isDirty = false;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"An unexpected error occurred while saving the step:\n{ex}",
                "Save Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private void TryNavigate(int newIndex)
    {
        if (newIndex < 0 || newIndex >= _activeTour.Steps.Count) return;

        if (_isDirty)
        {
            var result = MessageBox.Show(
                "Save changes to this step before moving?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);

            if (result == MessageBoxResult.Cancel) return;
            if (result == MessageBoxResult.Yes)
            {
                if (!PerformSave()) return;
            }
            else
            {
                RestoreOriginals();
            }
        }

        LoadStep(newIndex);
        PushLivePreview();
    }

    private void LoadStep(int index)
    {
        var step = _activeTour.Steps[index];

        _originalMarkdown      = step.MarkdownText;
        _originalPlacement     = step.CalloutPlacement;
        _originalTargetOffsetX = step.TargetOffsetX;
        _originalTargetOffsetY = step.TargetOffsetY;

        _suppressDirty = true;
        try
        {
            _step      = step;
            _stepIndex = index;

            _titleBox.Text         = step.Title;
            _markdownBox.Text      = step.MarkdownText;
            _targetControlBox.Text = step.TargetControlId;

            var placements = new[] { "Auto", "North", "South", "East", "West" };
            for (int i = 0; i < _placementRadios.Length; i++)
                _placementRadios[i].IsChecked = string.Equals(placements[i], step.CalloutPlacement, StringComparison.OrdinalIgnoreCase);
            if (_placementRadios.All(r => r.IsChecked != true))
                _placementRadios[0].IsChecked = true;

            ReloadCommandRows(_commandBeforeRows, _commandBeforePanel, step.EffectiveCommandsBefore);
            ReloadCommandRows(_commandAfterRows,  _commandAfterPanel,  step.EffectiveCommandsAfter);
            _advanceTriggerBox.Text = string.IsNullOrEmpty(step.AdvanceTrigger) ? "(none)" : step.AdvanceTrigger;

            var hasTarget = !string.IsNullOrWhiteSpace(step.TargetControlId);
            _crosshairCanvas.Visibility      = hasTarget ? Visibility.Visible : Visibility.Collapsed;
            _crosshairCoordsLabel.Visibility = _crosshairCanvas.Visibility;
            _crosshairCoordsLabel.Text       = FormatCrosshairCoords(step.TargetOffsetX, step.TargetOffsetY);
            if (hasTarget) RedrawCrosshair();

            _statusLabel.Visibility = Visibility.Collapsed;
            _isDirty = false;
        }
        finally
        {
            _suppressDirty = false;
        }

        Title                = $"Edit Step {index + 1} — {_activeTour.Name}";
        _stepCountLabel.Text = $"Step {index + 1} of {_activeTour.Steps.Count}";
        UpdateNavigationState();
    }

    private void UpdateNavigationState()
    {
        _prevButton.IsEnabled = _stepIndex > 0;
        _nextButton.IsEnabled = _stepIndex < _activeTour.Steps.Count - 1;
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

        var overlay = new Window
        {
            WindowStyle               = WindowStyle.None,
            AllowsTransparency        = true,
            Background                = new SolidColorBrush(Color.FromArgb(0x10, 0, 0, 0)),
            Topmost                   = true,
            ShowInTaskbar             = false,
            Cursor                    = Cursors.Cross,
            Left                      = mainWindow.Left,
            Top                       = mainWindow.Top,
            Width                     = mainWindow.ActualWidth,
            Height                    = mainWindow.ActualHeight,
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

        overlay.MouseMove += (_, e) =>
        {
            var pos = e.GetPosition(mainWindow);
            var hit = VisualTreeHelper.HitTest(mainWindow, pos);
            if (hit?.VisualHit is DependencyObject hitObj)
            {
                // Throttle: reuse result if the same leaf element is still under the cursor.
                if (!ReferenceEquals(hitObj, lastHitObj))
                {
                    lastHitObj  = hitObj;
                    lastResult  = FindFirstUniqueNamedAncestor(hitObj, mainWindow);
                }

                var (fe, name) = lastResult;
                if (fe != null && name != null)
                {
                    var topLeft = overlay.PointFromScreen(fe.PointToScreen(new Point(0, 0)));
                    const double stroke = 2;
                    const double pad    = 2; // gap between white rect and black rect
                    UpdateHighlight(canvas, topLeft, fe.ActualWidth, fe.ActualHeight, stroke, pad, name);
                    return;
                }
            }
            else
            {
                lastHitObj = null;
                lastResult = (null, null);
            }
            ClearHighlight(canvas);
        };

        overlay.MouseLeftButtonUp += (_, e) =>
        {
            var pos = e.GetPosition(mainWindow);
            ClearHighlight(canvas);
            overlay.Close();
            Visibility = Visibility.Visible;
            Activate();

            try
            {
                var hit = VisualTreeHelper.HitTest(mainWindow, pos);
                if (hit?.VisualHit is DependencyObject hitObj)
                {
                    // Walk up the visual tree; first unique name wins.
                    var (_, name) = FindFirstUniqueNamedAncestor(hitObj, mainWindow);
                    if (name != null)
                    {
                        _targetControlBox.Text = name;
                        PushLivePreview();
                    }
                    else
                    {
                        ShowStatus("⚠ The clicked element has no unique x:Name — cannot use as a target. " +
                                   "Assign an x:Name to this element or select a different target.");
                    }
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

        _crosshairCoordsLabel.Text = FormatCrosshairCoords(offsetX, offsetY);
        RedrawCrosshair();
        _livePreviewCallback?.Invoke();
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
        btn.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        return btn;
    }

    private static ComboBox MakeCommandCombo(IEnumerable<string> items, string currentValue)
    {
        var cb = new ComboBox { IsEditable = true, Height = 26 };
        cb.SetResourceReference(ComboBox.StyleProperty, "ThemedEditableComboBoxStyle");
        foreach (var item in items)
            cb.Items.Add(item == "" ? "(none)" : item);
        var displayValue = string.IsNullOrEmpty(currentValue) ? "(none)" : currentValue;
        cb.Text = displayValue; // use Text (not SelectedItem) since editable combo
        return cb;
    }

    private static string GetSelectedCommand(ComboBox cb) =>
        !string.IsNullOrWhiteSpace(cb.Text) && cb.Text != "(none)" ? cb.Text.Trim() : string.Empty;

    // ── Multi-command row helpers ──────────────────────────────────────────────

    /// <summary>Tracks a single command combo row with its delete button.</summary>
    private sealed record CommandRow(ComboBox Box, Button DeleteButton);

    private void AddCommandRowToPanel(List<CommandRow> rows, StackPanel panel, string value)
    {
        var cb = MakeCommandCombo(_commandItems, value);

        var expandBtn = MakeButton("…");
        expandBtn.Width   = 32;
        expandBtn.Margin  = new Thickness(4, 0, 0, 0);
        expandBtn.Padding = new Thickness(0);
        expandBtn.ToolTip = "Edit as multi-line text (\\n = new line)";
        expandBtn.Click  += (_, _) => OpenCommandEditor(cb);

        var deleteBtn = MakeButton("×");
        deleteBtn.Width   = 26;
        deleteBtn.Margin  = new Thickness(4, 0, 0, 0);
        deleteBtn.Padding = new Thickness(0);
        deleteBtn.ToolTip = "Remove this command";

        var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(cb,        0);
        Grid.SetColumn(expandBtn, 1);
        Grid.SetColumn(deleteBtn, 2);
        rowGrid.Children.Add(cb);
        rowGrid.Children.Add(expandBtn);
        rowGrid.Children.Add(deleteBtn);

        var commandRow = new CommandRow(cb, deleteBtn);
        rows.Add(commandRow);
        panel.Children.Add(rowGrid);

        deleteBtn.Click += (_, _) =>
        {
            rows.Remove(commandRow);
            panel.Children.Remove(rowGrid);
            UpdateDeleteButtonVisibility(rows);
            if (!_suppressDirty) _isDirty = true;
        };

        cb.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => { if (!_suppressDirty) _isDirty = true; }));
        cb.SelectionChanged += (_, _) => { if (!_suppressDirty) _isDirty = true; };

        UpdateDeleteButtonVisibility(rows);
    }

    private Button MakeAddRowButton(List<CommandRow> rows, StackPanel panel)
    {
        var btn = MakeButton("+ Add Command");
        btn.HorizontalAlignment = HorizontalAlignment.Left;
        btn.Margin = new Thickness(0, 2, 0, 0);
        btn.Click += (_, _) =>
        {
            AddCommandRowToPanel(rows, panel, string.Empty);
            if (!_suppressDirty) _isDirty = true;
        };
        return btn;
    }

    private static void UpdateDeleteButtonVisibility(List<CommandRow> rows)
    {
        bool canDelete = rows.Count > 1;
        foreach (var r in rows)
            r.DeleteButton.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReloadCommandRows(List<CommandRow> rows, StackPanel panel, IReadOnlyList<string> values)
    {
        rows.Clear();
        panel.Children.Clear();
        var list = values.Count > 0 ? values : (IReadOnlyList<string>)[string.Empty];
        foreach (var v in list)
            AddCommandRowToPanel(rows, panel, v);
    }

    private void OpenCommandEditor(ComboBox comboBox)
    {
        var editor = new FrmCommandTextEditor(GetSelectedCommand(comboBox)) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            comboBox.Text = string.IsNullOrEmpty(editor.ResultText) ? "(none)" : editor.ResultText;
            PushLivePreview();
        }
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
