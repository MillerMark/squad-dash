using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SquadDash.GuidedTours;

namespace SquadDash;

/// <summary>
/// Developer-only dialog for editing a <see cref="GuidedTourStep"/> in-place
/// while a tour is running.  Only shown when <see cref="SquadDashEnvironment.IsDeveloperMode"/> is true.
/// </summary>
internal sealed class FrmGuidedTourStepEditor : ChromedWindow
{
    private readonly GuidedTourStep    _step;
    private readonly int               _stepIndex;
    private readonly GuidedTour        _activeTour;
    private readonly List<GuidedTour>  _allTours;
    private readonly string?           _workspaceFolderPath;
    private readonly Action?           _captureLayout;

    private readonly Action?           _livePreviewCallback;
    private readonly string            _originalMarkdown;
    private readonly string            _originalPlacement;
    private readonly double            _originalTargetOffsetX;
    private readonly double            _originalTargetOffsetY;
    private readonly DispatcherTimer   _debounceTimer;

    // PTT voice dictation
    private readonly PttTextBoxAttachment _ptt;

    // Form controls
    private readonly TextBox       _titleBox;
    private readonly TextBox       _markdownBox;
    private readonly RadioButton[] _placementRadios;
    private readonly TextBox       _targetControlBox;
    private readonly TextBlock     _statusLabel;
    private readonly ComboBox      _commandBeforeBox;
    private readonly ComboBox      _commandAfterBox;
    private readonly TextBox       _advanceTriggerBox;

    // Crosshair picker
    private readonly Canvas        _crosshairCanvas;
    private readonly TextBlock     _crosshairCoordsLabel;
    private bool                   _crosshairDragging;

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
        GuidedTourCommandRegistry? commandRegistry = null)
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
        var commandItems = new[] { "" }.Concat(commandNames).ToArray();

        _commandBeforeBox = MakeCommandCombo(commandItems, step.CommandBefore);
        _commandAfterBox  = MakeCommandCombo(commandItems, step.CommandAfter);

        _advanceTriggerBox = MakeTextBox(step.AdvanceTrigger, multiLine: false);

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

        var formPanel = new StackPanel { Margin = new Thickness(14, 10, 14, 8) };
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
        formPanel.Children.Add(_commandBeforeBox);
        formPanel.Children.Add(MakeLabel("Command After"));
        formPanel.Children.Add(_commandAfterBox);
        formPanel.Children.Add(MakeLabel("Advance Trigger"));
        formPanel.Children.Add(_advanceTriggerBox);
        formPanel.Children.Add(new Border { Height = 10 });
        formPanel.Children.Add(captureButton);
        formPanel.Children.Add(_statusLabel);

        // ── Button bar ────────────────────────────────────────────────────────

        var saveButton = MakeButton("Save");
        saveButton.IsDefault = true;
        saveButton.Click += (_, _) => CommitSave();

        var cancelButton = MakeButton("Cancel");
        cancelButton.IsCancel = true;
        cancelButton.Click += (_, _) => Close();

        var buttonRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(14, 4, 14, 12),
        };
        buttonRow.Children.Add(saveButton);
        buttonRow.Children.Add(cancelButton);

        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        layout.Children.Add(buttonRow);
        layout.Children.Add(formPanel);

        contentArea.Child = layout;

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); PushLivePreview(); };
        _markdownBox.TextChanged += (_, _) => { _debounceTimer.Stop(); _debounceTimer.Start(); };
        Closed += (_, _) => { _debounceTimer.Stop(); if (!WasSaved) RestoreOriginals(); };

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
        try
        {
            _step.Title            = _titleBox.Text.Trim();
            _step.MarkdownText     = _markdownBox.Text;
            _step.CalloutPlacement = GetSelectedPlacement();
            _step.TargetControlId  = _targetControlBox.Text.Trim();
            _step.CommandBefore    = GetSelectedCommand(_commandBeforeBox);
            _step.CommandAfter     = GetSelectedCommand(_commandAfterBox);
            _step.AdvanceTrigger   = _advanceTriggerBox.Text.Trim();
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
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"An unexpected error occurred while saving the step:\n{ex}",
                "Save Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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
        overlay.Content = hint;

        overlay.MouseLeftButtonUp += (_, e) =>
        {
            var pos = e.GetPosition(mainWindow);
            overlay.Close();
            Visibility = Visibility.Visible;
            Activate();

            try
            {
                var hit = VisualTreeHelper.HitTest(mainWindow, pos);
                if (hit?.VisualHit is DependencyObject hitObj)
                {
                    DependencyObject? current = hitObj;
                    bool found = false;
                    while (current is not null)
                    {
                        if (current is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
                        {
                            _targetControlBox.Text = fe.Name;
                            PushLivePreview();
                            found = true;
                            break;
                        }
                        current = VisualTreeHelper.GetParent(current);
                    }

                    if (!found)
                        ShowStatus("⚠ The clicked element has no x:Name — cannot use as a target. " +
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
                overlay.Close();
                Visibility = Visibility.Visible;
                Activate();
            }
        };

        overlay.Show();
        overlay.Focus();
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
