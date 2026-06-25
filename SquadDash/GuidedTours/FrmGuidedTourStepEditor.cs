using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    // Form controls
    private readonly TextBox    _titleBox;
    private readonly TextBox    _markdownBox;
    private readonly ComboBox   _placementCombo;
    private readonly TextBox    _targetControlBox;
    private readonly TextBlock  _statusLabel;

    /// <summary>True if the user clicked Save and the step was persisted.</summary>
    public bool WasSaved { get; private set; }

    public FrmGuidedTourStepEditor(
        GuidedTourStep   step,
        int              stepIndex,
        GuidedTour       activeTour,
        List<GuidedTour> allTours,
        string?          workspaceFolderPath,
        Window           owner,
        Action?          captureLayout = null)
        : base(captionHeight: 34, resizeMode: ResizeMode.NoResize, resizeBorderThickness: 0)
    {
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

        // ── Form fields ───────────────────────────────────────────────────────

        _titleBox = MakeTextBox(step.Title, multiLine: false);

        _markdownBox = MakeTextBox(step.MarkdownText, multiLine: true);
        _markdownBox.Height = 120;
        _markdownBox.FontFamily = new FontFamily("Consolas, Courier New, monospace");
        _markdownBox.AcceptsReturn = true;
        _markdownBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        _placementCombo = new ComboBox { Height = 26, Margin = new Thickness(0, 0, 0, 0) };
        _placementCombo.SetResourceReference(ComboBox.BackgroundProperty,  "InputSurface");
        _placementCombo.SetResourceReference(ComboBox.BorderBrushProperty, "InputBorder");
        _placementCombo.SetResourceReference(ComboBox.ForegroundProperty,  "LabelText");
        _placementCombo.SetResourceReference(ComboBox.FontSizeProperty,    "FontSizeBody");
        foreach (var p in new[] { "Auto", "North", "South", "East", "West" })
            _placementCombo.Items.Add(p);
        _placementCombo.SelectedItem = step.CalloutPlacement;
        if (_placementCombo.SelectedItem is null) _placementCombo.SelectedIndex = 0;

        _targetControlBox = MakeTextBox(step.TargetControlId, multiLine: false);

        var browseButton = MakeButton("Browse…");
        browseButton.Click += (_, _) => BrowseForControl();

        var targetRow = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_targetControlBox, 0);
        Grid.SetColumn(browseButton, 1);
        browseButton.Margin = new Thickness(6, 0, 0, 0);
        targetRow.Children.Add(_targetControlBox);
        targetRow.Children.Add(browseButton);

        var captureButton = MakeButton("📷 Capture Layout for This Step");
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
        formPanel.Children.Add(_placementCombo);
        formPanel.Children.Add(MakeLabel("Target Control (x:Name)"));
        formPanel.Children.Add(targetRow);
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

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void CommitSave()
    {
        _step.Title            = _titleBox.Text.Trim();
        _step.MarkdownText     = _markdownBox.Text;
        _step.CalloutPlacement = (_placementCombo.SelectedItem as string) ?? "Auto";
        _step.TargetControlId  = _targetControlBox.Text.Trim();

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

    private void CaptureLayoutForStep()
    {
        _captureLayout?.Invoke();

        var layoutName      = $"step-{_stepIndex}";
        _step.PreAction     = $"LoadLayout:{layoutName}";

        ShowStatus($"Layout captured \u2014 PreAction set to \"LoadLayout:{layoutName}\".");
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
