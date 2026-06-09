using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// Custom WPF editor for a single <see cref="MaintenanceTask"/>. Shows title, properties,
/// UI options YAML with live preview, and instructions with syntax highlighting.
/// Opened via "Edit Task" in the Maintenance panel context menu.
/// </summary>
internal sealed class MaintenanceTaskEditorWindow : ChromedWindow {

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly MaintenanceTask                         _task;
    private readonly Func<ApplicationSettingsSnapshot>       _settingsProvider;
    private readonly Action                                  _onSaved;
    private readonly Action<RichTextBox, string>?            _onReviseWithAi;
    private readonly Action<RichTextBox, string, string>?    _onDirectRevise;

    // Track if there are unsaved changes
    private bool _hasUnsavedChanges;

    // Track the selected day for weekly frequency
    private string _selectedWeeklyDay = "Monday";

    // ── Controls ──────────────────────────────────────────────────────────────

    private readonly TextBox     _titleBox;
    private readonly CheckBox    _enabledCheck;
    private readonly ComboBox    _frequencyCombo;
    private readonly ComboBox    _safetyCombo;
    private readonly TextBox     _optionsYamlBox;
    private readonly StackPanel  _optionsPreviewPanel;
    private readonly FlowDocumentScrollViewer _markdownPreview;
    private readonly MarkdownEditorPanel _instructionsPanel;
    private StackPanel? _weeklyDayPickerPanel;  // Day picker UI for weekly frequency

    // ── Voice ─────────────────────────────────────────────────────────────────

    private readonly PttTextBoxAttachment    _pttTitle;
    private readonly PttTextBoxAttachment    _pttOptions;
    private readonly PttRichTextBoxAttachment _pttInstructions;

    // ── Debounce timers ───────────────────────────────────────────────────────

    private DispatcherTimer? _optionsDebounce;

    // ── Preview→source hover highlight ────────────────────────────────────────

    private Grid                                          _instructionsHost       = null!;
    private Canvas?                                       _instructionsOverlay;
    private System.Windows.Shapes.Rectangle?              _instructionsHighlight;
    private DispatcherTimer?                              _instructionsHoverTimer;
    private readonly List<(int CharStart, int CharLength)> _previewBlockSrcRanges  = new();
    private readonly List<Block>                           _previewBlocks          = new();
    private int                                            _lastHoveredBlockIdx    = -1;

    // ── Re-entrance guard ─────────────────────────────────────────────────────

    // Tracks current option values for conditional preview resolution
    private readonly Dictionary<string, string> _optionValues = new();

    // Error indicator below the YAML editor
    private TextBlock _yamlErrorText = null!;

    // ── Highlight colours ─────────────────────────────────────────────────────

    // ── Constructor ───────────────────────────────────────────────────────────

    internal MaintenanceTaskEditorWindow(
        Window owner,
        MaintenanceTask task,
        Func<ApplicationSettingsSnapshot> settingsProvider,
        Action onSaved,
        Action<RichTextBox, string>?         onReviseWithAi = null,
        Action<RichTextBox, string, string>? onDirectRevise = null) : base(captionHeight: 56) {

        _task             = task;
        _settingsProvider = settingsProvider;
        _onSaved          = onSaved;
        _onReviseWithAi   = onReviseWithAi;
        _onDirectRevise   = onDirectRevise;

        Owner                 = owner;
        Title                 = "Edit Maintenance Task";
        Width                 = 800;
        Height                = 700;
        MinWidth              = 500;
        MinHeight             = 400;
        ResizeMode            = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _pttTitle        = new PttTextBoxAttachment(_settingsProvider, this, Dispatcher);
        _pttOptions      = new PttTextBoxAttachment(_settingsProvider, this, Dispatcher);
        _pttInstructions = new PttRichTextBoxAttachment(_settingsProvider, this, Dispatcher);

        _titleBox            = BuildTitleBox();
        _enabledCheck        = BuildEnabledCheck();
        _frequencyCombo      = BuildFrequencyCombo();
        _safetyCombo         = BuildSafetyCombo();
        _optionsYamlBox      = BuildOptionsYamlBox();
        _optionsPreviewPanel = new StackPanel { Margin = new Thickness(4) };
        _markdownPreview     = BuildMarkdownPreview();
        _instructionsPanel   = BuildInstructionsPanel();

        // Initialize the weekly day selection based on the task's current frequency
        if (_task.Frequency.StartsWith("weekly-", StringComparison.OrdinalIgnoreCase)) {
            var dayPart = _task.Frequency.Substring(7); // Remove "weekly-" prefix
            if (!string.IsNullOrWhiteSpace(dayPart)) {
                _selectedWeeklyDay = dayPart;
            }
        }

        // Build error indicator for YAML section
        _yamlErrorText = new TextBlock {
            Visibility   = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(2, 2, 2, 0),
        };
        _yamlErrorText.SetResourceReference(TextBlock.ForegroundProperty, "DangerText");
        _yamlErrorText.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");

        // Seed option values from task's current options
        if (_task.Options is not null)
            foreach (var opt in _task.Options)
                _optionValues[opt.Key] = opt.RawValue ?? string.Empty;

        ApplyOuterBorder().Child = BuildLayout();

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp   += OnPreviewKeyUp;
        Closed         += OnClosed;
        Closing        += OnClosing;

        // Track changes to the title
        _titleBox.TextChanged += (_, _) => _hasUnsavedChanges = true;
        // Track changes to the enabled state
        _enabledCheck.Checked += (_, _) => _hasUnsavedChanges = true;
        _enabledCheck.Unchecked += (_, _) => _hasUnsavedChanges = true;
        // Track changes to frequency
        _frequencyCombo.SelectionChanged += OnFrequencyComboChanged;
        // Track changes to options YAML
        _optionsYamlBox.TextChanged += (_, _) => _hasUnsavedChanges = true;
        // Track changes to instructions (TextChanged + PreviewUpdateRequested wired in BuildInstructionsPanel)

        // Use PreviewMouseMove on the viewer instead of Block.MouseEnter — FlowDocumentScrollViewer
        // does not route ContentElement mouse events to individual Block instances.
        _markdownPreview.PreviewMouseMove += OnMarkdownPreviewMouseMove;
        _markdownPreview.MouseLeave       += (_, _) => {
            _lastHoveredBlockIdx = -1;
            ClearInstructionsHoverHighlight();
        };

        // Initial renders
        Loaded += (_, _) => {
            UpdateMarkdownPreview();
            UpdateOptionsPreview();
        };
    }

    // ── Key / lifecycle wiring ────────────────────────────────────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e) {
        if (_titleBox.IsKeyboardFocusWithin)
            if (_pttTitle.HandlePreviewKeyDown(e, _titleBox)) e.Handled = true;
        if (_optionsYamlBox.IsKeyboardFocusWithin)
            if (_pttOptions.HandlePreviewKeyDown(e, _optionsYamlBox)) e.Handled = true;
        if (_instructionsPanel.IsKeyboardFocusWithin)
            if (_pttInstructions.HandlePreviewKeyDown(e, _instructionsPanel.EditorBox)) e.Handled = true;

        // Escape: close with unsaved changes confirmation
        if (e.Key == Key.Escape && !e.Handled) {
            if (HandleCloseRequest()) Close();
            e.Handled = true;
            return;
        }

        // Ctrl+Shift+A: Revise with AI
        if (_instructionsPanel.IsKeyboardFocusWithin
            && e.Key == Key.A
            && (Keyboard.Modifiers & ModifierKeys.Control) != 0
            && (Keyboard.Modifiers & ModifierKeys.Shift) != 0
            && _onReviseWithAi is not null
            && _instructionsPanel.EditorBox.GetSelectionLength() > 0)
        {
            _onReviseWithAi(_instructionsPanel.EditorBox, "");
            e.Handled = true;
            return;
        }

        // Ctrl+Shift+C: Quick Cleanup
        if (_instructionsPanel.IsKeyboardFocusWithin
            && e.Key == Key.C
            && (Keyboard.Modifiers & ModifierKeys.Control) != 0
            && (Keyboard.Modifiers & ModifierKeys.Shift) != 0
            && _onDirectRevise is not null
            && _instructionsPanel.EditorBox.GetSelectionLength() > 0)
        {
            var cleanupPrompt = _settingsProvider().CleanupPrompt;
            if (!string.IsNullOrWhiteSpace(cleanupPrompt))
            {
                _onDirectRevise(_instructionsPanel.EditorBox, "", cleanupPrompt);
                e.Handled = true;
                return;
            }
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e) {
        _pttTitle.HandlePreviewKeyUp(e);
        _pttOptions.HandlePreviewKeyUp(e);
        _pttInstructions.HandlePreviewKeyUp(e);
    }

    private void OnClosed(object? sender, EventArgs e) {
        if (_pttTitle.IsActive)        _ = _pttTitle.StopAsync();
        if (_pttOptions.IsActive)      _ = _pttOptions.StopAsync();
        if (_pttInstructions.IsActive) _ = _pttInstructions.StopAsync();
        _pttTitle.Dispose();
        _pttOptions.Dispose();
        _pttInstructions.Dispose();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!HandleCloseRequest())
            e.Cancel = true;
    }

    /// <summary>Returns <c>true</c> when the close should proceed, <c>false</c> to cancel it.</summary>
    private bool HandleCloseRequest()
    {
        if (!_hasUnsavedChanges) return true;

        var result = MessageBox.Show(
            "Save changes before closing?",
            "Save Changes?",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        if (result == MessageBoxResult.Yes)
        {
            OnSave(this, new RoutedEventArgs());
            return true;
        }
        if (result == MessageBoxResult.No)
        {
            _hasUnsavedChanges = false;
            return true;
        }

        // Cancel or dialog dismissed — stay open
        return false;
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private UIElement BuildLayout() {
        var root = new DockPanel { LastChildFill = true };

        // ── Window caption ────────────────────────────────────────────────────
        var captionLabel = new TextBlock {
            Text              = "Edit Maintenance Task",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight        = FontWeights.SemiBold,
            Margin            = new Thickness(12, 8, 50, 4),
        };
        captionLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        captionLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeMedium");
        DockPanel.SetDock(captionLabel, Dock.Top);
        root.Children.Add(captionLabel);

        // ── Title bar row ─────────────────────────────────────────────────────
        var titleRow = new DockPanel { Margin = new Thickness(8, 2, 50, 4), LastChildFill = true };
        var titleLabel = new TextBlock { Text = "Title:", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0) };
        titleLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        titleLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        DockPanel.SetDock(titleLabel, Dock.Left);
        titleRow.Children.Add(titleLabel);
        titleRow.Children.Add(_titleBox);
        DockPanel.SetDock(titleRow, Dock.Top);
        root.Children.Add(titleRow);

        // ── Properties row ────────────────────────────────────────────────────
        var propsRow = new StackPanel {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(8, 5, 8, 4),
        };
        propsRow.Children.Add(_enabledCheck);
        propsRow.Children.Add(BuildLabel("Frequency:"));
        propsRow.Children.Add(_frequencyCombo);
        
        // Build the day picker panel for weekly frequency
        _weeklyDayPickerPanel = BuildWeeklyDayPicker();
        _weeklyDayPickerPanel.Visibility = (_frequencyCombo.SelectedItem as string) == "weekly" 
            ? Visibility.Visible 
            : Visibility.Collapsed;
        propsRow.Children.Add(_weeklyDayPickerPanel);
        
        DockPanel.SetDock(propsRow, Dock.Top);
        root.Children.Add(propsRow);

        // ── Button row ────────────────────────────────────────────────────────
        var buttonRow = new StackPanel {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(8, 4, 8, 8),
        };
        var cancelBtn = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(16, 4, 16, 4) };
        cancelBtn.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        cancelBtn.Click += (_, _) => { if (HandleCloseRequest()) Close(); };

        var saveBtn = new Button { Content = "Save", Padding = new Thickness(16, 4, 16, 4) };
        saveBtn.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        saveBtn.Click += OnSave;

        buttonRow.Children.Add(cancelBtn);
        buttonRow.Children.Add(saveBtn);
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);

        // ── Separator ─────────────────────────────────────────────────────────
        var sep = new Separator { Margin = new Thickness(0) };
        sep.SetResourceReference(Separator.BackgroundProperty, "SubtleBorder");
        DockPanel.SetDock(sep, Dock.Bottom);
        root.Children.Add(sep);

        // ── Main split area ───────────────────────────────────────────────────
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        var optionsSection = BuildOptionsSection();
        Grid.SetRow(optionsSection, 0);
        mainGrid.Children.Add(optionsSection);

        var midSep = new GridSplitter {
            Name                = "TaskEditorOptionsSplitter",
            Height              = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        midSep.SetResourceReference(GridSplitter.BackgroundProperty, "SubtleBorder");
        Grid.SetRow(midSep, 1);
        mainGrid.Children.Add(midSep);

        var instrSection = BuildInstructionsSection();
        Grid.SetRow(instrSection, 2);
        mainGrid.Children.Add(instrSection);

        root.Children.Add(mainGrid);
        return root;
    }

    private UIElement BuildOptionsSection() {
        var container = new DockPanel { Margin = new Thickness(8, 4, 8, 2), LastChildFill = true };

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Header row: "UI preview" (Col 0) and "YAML" (Col 1)
        var uiPreviewLabel = new TextBlock { Text = "UI Preview", Margin = new Thickness(0, 0, 0, 2), TextAlignment = TextAlignment.Left };
        uiPreviewLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        uiPreviewLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        Grid.SetRow(uiPreviewLabel, 0);
        Grid.SetColumn(uiPreviewLabel, 0);
        grid.Children.Add(uiPreviewLabel);

        var yamlLabel = new TextBlock { Text = "YAML", Margin = new Thickness(4, 0, 0, 2), TextAlignment = TextAlignment.Left };
        yamlLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        yamlLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        Grid.SetRow(yamlLabel, 0);
        Grid.SetColumn(yamlLabel, 1);
        grid.Children.Add(yamlLabel);

        // Left: live preview (interactive)
        var leftPanel = new DockPanel { Margin = new Thickness(0, 0, 8, 0), LastChildFill = true };
        var previewScroll = new ScrollViewer {
            Content             = _optionsPreviewPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        previewScroll.SetResourceReference(ScrollViewer.BackgroundProperty, "RosterPanelSurface");
        leftPanel.Children.Add(previewScroll);
        Grid.SetRow(leftPanel, 1);
        Grid.SetColumn(leftPanel, 0);

        // Right: YAML editor with error line below
        var rightPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 0), LastChildFill = true };
        DockPanel.SetDock(_yamlErrorText, Dock.Bottom);
        rightPanel.Children.Add(_yamlErrorText);
        rightPanel.Children.Add(new ScrollViewer {
            Content             = _optionsYamlBox,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        Grid.SetRow(rightPanel, 1);
        Grid.SetColumn(rightPanel, 1);

        grid.Children.Add(leftPanel);
        grid.Children.Add(rightPanel);
        container.Children.Add(grid);
        return container;
    }

    private UIElement BuildInstructionsSection() {
        var panel = new DockPanel { Margin = new Thickness(8, 2, 8, 0), LastChildFill = true };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Header row: "Preview" (Col 0) and "Source" (Col 2)
        var leftLabel = new TextBlock { Text = "Instructions - Preview", Margin = new Thickness(0, 0, 0, 2) };
        leftLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        leftLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
        Grid.SetRow(leftLabel, 0);
        Grid.SetColumn(leftLabel, 0);

        var rightLabel = new TextBlock { Text = "Source", Margin = new Thickness(4, 0, 0, 2) };
        rightLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        rightLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
        Grid.SetRow(rightLabel, 0);
        Grid.SetColumn(rightLabel, 2);

        // Splitter (Row 1 only)
        var splitter = new GridSplitter {
            Name                = "TaskEditorInstructionsSplitter",
            Width               = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };
        splitter.SetResourceReference(GridSplitter.BackgroundProperty, "SubtleBorder");
        Grid.SetRow(splitter, 1);
        Grid.SetColumn(splitter, 1);

        // Left: Markdown preview (Row 1, Col 0)
        var leftStack = new DockPanel { LastChildFill = true };
        Grid.SetRow(leftStack, 1);
        Grid.SetColumn(leftStack, 0);
        leftStack.Children.Add(_markdownPreview);

        // Right: editable MarkdownEditorPanel (Row 1, Col 2)
        _instructionsHost = new Grid();
        _instructionsHost.Children.Add(_instructionsPanel);

        var rightStack = new DockPanel { Margin = new Thickness(4, 0, 0, 0), LastChildFill = true };
        Grid.SetRow(rightStack, 1);
        Grid.SetColumn(rightStack, 2);
        rightStack.Children.Add(_instructionsHost);

        grid.Children.Add(leftLabel);
        grid.Children.Add(rightLabel);
        grid.Children.Add(leftStack);
        grid.Children.Add(splitter);
        grid.Children.Add(rightStack);
        panel.Children.Add(grid);

        return panel;
    }

    // ── Control factories ─────────────────────────────────────────────────────

    private TextBox BuildTitleBox() {
        var tb = new TextBox {
            Text              = _task.Title,
            VerticalAlignment = VerticalAlignment.Center,
        };
        tb.SetResourceReference(TextBox.FontSizeProperty,   "FontSizeLarge");
        tb.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
        tb.SetResourceReference(TextBox.BackgroundProperty, "RosterPanelSurface");
        tb.SetResourceReference(TextBox.BorderBrushProperty,"SubtleBorder");
        WindowChrome.SetIsHitTestVisibleInChrome(tb, true);
        return tb;
    }

    private CheckBox BuildEnabledCheck() {
        var cb = new CheckBox {
            Content           = "Enabled",
            IsChecked         = _task.Enabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 12, 0),
        };
        cb.SetResourceReference(CheckBox.ForegroundProperty, "BodyText");
        cb.SetResourceReference(CheckBox.FontSizeProperty,   "FontSizeBody");
        return cb;
    }

    private ComboBox BuildFrequencyCombo() {
        var cb = new ComboBox { Margin = new Thickness(0, 0, 12, 0) };
        
        // Add base frequency options
        foreach (var item in new[] { "always", "daily", "weekly", "monthly", "after-commits" })
            cb.Items.Add(item);
        
        // Determine initial selection: extract base frequency from task's frequency
        // (e.g., "weekly-Monday" → select "weekly", "daily" → select "daily")
        string initialSelection = _task.Frequency;
        if (_task.Frequency.StartsWith("weekly-", StringComparison.OrdinalIgnoreCase)) {
            initialSelection = "weekly";
        }
        
        cb.SelectedItem = cb.Items.Contains(initialSelection) ? initialSelection : "daily";
        
        cb.SetResourceReference(ComboBox.ForegroundProperty,    "LabelText");
        cb.SetResourceReference(ComboBox.BackgroundProperty,    "RosterPanelSurface");
        cb.SetResourceReference(ComboBox.FontSizeProperty,      "FontSizeBody");
        cb.SetResourceReference(ComboBox.StyleProperty, "ThemedComboBoxStyle");
        
        return cb;
    }

    private ComboBox BuildSafetyCombo() {
        var cb = new ComboBox { Margin = new Thickness(0, 0, 8, 0) };
        foreach (var item in new[] { "report-only", "branch", "direct" })
            cb.Items.Add(item);
        cb.SelectedItem = cb.Items.Contains(_task.Safety) ? _task.Safety : "branch";
        cb.SetResourceReference(ComboBox.ForegroundProperty, "LabelText");
        cb.SetResourceReference(ComboBox.BackgroundProperty, "RosterPanelSurface");
        cb.SetResourceReference(ComboBox.FontSizeProperty,   "FontSizeBody");
        cb.SetResourceReference(ComboBox.StyleProperty, "ThemedComboBoxStyle");
        return cb;
    }

    /// <summary>Builds a horizontal panel with 7 day-of-week buttons for weekly frequency selection.</summary>
    private StackPanel BuildWeeklyDayPicker() {
        var panel = new StackPanel {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 0, 0, 0),
        };

        var days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        
        foreach (var day in days) {
            var btn = new Button {
                Content           = day.Substring(0, 3),  // Mon, Tue, Wed, etc.
                Padding           = new Thickness(6, 2, 6, 2),
                Margin            = new Thickness(2, 0, 2, 0),
                MinWidth          = 36,
                Cursor            = Cursors.Hand,
            };
            
            // Apply consistent styling
            btn.SetResourceReference(Button.StyleProperty,    "FlatButtonStyle");
            btn.SetResourceReference(Button.FontSizeProperty, "FontSizeSmall");
            btn.SetResourceReference(Button.ForegroundProperty, "LabelText");
            
            // Highlight the currently selected day
            if (string.Equals(day, _selectedWeeklyDay, StringComparison.OrdinalIgnoreCase)) {
                btn.SetResourceReference(Button.BackgroundProperty, "InputSurface");
                btn.SetResourceReference(Button.BorderBrushProperty, "InputBorder");
                btn.BorderThickness = new Thickness(1);
            }
            
            // Capture day in closure
            string capturedDay = day;
            btn.Click += (_, _) => {
                _selectedWeeklyDay = capturedDay;
                _hasUnsavedChanges = true;
                
                // Update UI to highlight the selected day
                foreach (UIElement child in panel.Children) {
                    if (child is Button dayBtn) {
                        dayBtn.BorderThickness = new Thickness(0);
                        dayBtn.Background = System.Windows.Media.Brushes.Transparent;
                    }
                }
                btn.BorderThickness = new Thickness(1);
                btn.SetResourceReference(Button.BackgroundProperty, "InputSurface");
                btn.SetResourceReference(Button.BorderBrushProperty, "InputBorder");
            };
            
            panel.Children.Add(btn);
        }
        
        return panel;
    }

    /// <summary>Handles frequency combo selection changes to show/hide the day picker.</summary>
    private void OnFrequencyComboChanged(object sender, SelectionChangedEventArgs e) {
        _hasUnsavedChanges = true;
        
        if (_weeklyDayPickerPanel != null) {
            var selectedFreq = _frequencyCombo.SelectedItem as string;
            _weeklyDayPickerPanel.Visibility = (selectedFreq == "weekly") 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }
    }

    private TextBox BuildOptionsYamlBox() {
        var yaml = SerializeOptionsToYaml(_task.Options);
        var tb = new TextBox {
            Text              = yaml,
            AcceptsReturn     = true,
            AcceptsTab        = true,
            TextWrapping      = TextWrapping.NoWrap,
            FontFamily        = new FontFamily("Consolas, Courier New"),
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        tb.SetResourceReference(TextBox.FontSizeProperty,    "FontSizeBody");
        tb.SetResourceReference(TextBox.ForegroundProperty,  "LabelText");
        tb.SetResourceReference(TextBox.BackgroundProperty,  "RosterPanelSurface");
        tb.SetResourceReference(TextBox.BorderBrushProperty, "SubtleBorder");
        tb.TextChanged += (_, _) => ScheduleOptionsDebounce();
        return tb;
    }

    private FlowDocumentScrollViewer BuildMarkdownPreview() {
        var viewer = new FlowDocumentScrollViewer {
            IsToolBarVisible     = false,
            HorizontalAlignment  = HorizontalAlignment.Stretch,
            VerticalAlignment    = VerticalAlignment.Stretch,
        };
        viewer.SetResourceReference(FlowDocumentScrollViewer.BackgroundProperty, "RosterPanelSurface");
        return viewer;
    }

    private MarkdownEditorPanel BuildInstructionsPanel() {
        var panel = new MarkdownEditorPanel(showImageButton: false, showHrButton: false);
        panel.SetText(_task.Instructions ?? "");
        panel.TextChanged            += (_, _)    => _hasUnsavedChanges = true;
        panel.PreviewUpdateRequested += (_, _)    => UpdateMarkdownPreview();
        return panel;
    }

    private static TextBlock BuildLabel(string text) {
        var tb = new TextBlock {
            Text              = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 4, 0),
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        tb.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        return tb;
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private void OnSave(object sender, RoutedEventArgs e) {
        var instructionsText = _instructionsPanel.GetText().TrimEnd('\r', '\n');

        // Build the frequency value: if "weekly" is selected, append the day of week
        var baseFrequency = (_frequencyCombo.SelectedItem as string) ?? _task.Frequency;
        var finalFrequency = baseFrequency;
        
        if (string.Equals(baseFrequency, "weekly", StringComparison.OrdinalIgnoreCase)) {
            finalFrequency = $"weekly-{_selectedWeeklyDay}";
        }

        var updatedTask = _task with {
            Title        = _titleBox.Text,
            Enabled      = _enabledCheck.IsChecked == true,
            Frequency    = finalFrequency,
            Safety       = (_safetyCombo.SelectedItem as string) ?? _task.Safety,
            Instructions = instructionsText,
            Options      = ParseOptionsFromYaml(_optionsYamlBox.Text) ?? _task.Options,
        };

        if (!string.IsNullOrEmpty(_task.SourceFilePath)) {
            try {
                MaintenanceMdParser.UpdateTask(_task.SourceFilePath, _task.Id, updatedTask);
            }
            catch (Exception ex) {
                SquadDashTrace.Write(TraceCategory.General,
                    $"MaintenanceTaskEditorWindow: failed to save task '{_task.Id}': {ex.Message}");
            }
        }

        _hasUnsavedChanges = false;
        _onSaved();
        Close();
    }

    // ── Markdown preview ──────────────────────────────────────────────────────

    private void UpdateMarkdownPreview() {
        var rawText = _instructionsPanel.GetText();

        var segments             = ResolveConditionalSegments(rawText);
        var conditionalBrush     = GetConditionalTextBrush();
        var conditionalBgBrush   = GetConditionalBackgroundBrush();

        var combined = new FlowDocument {
            FontFamily    = new FontFamily("Segoe UI, Segoe UI Emoji"),
            FontSize      = Application.Current.Resources["FontSizeMedium"] is double sz ? sz : 13.0,
            PagePadding   = new Thickness(18),
            TextAlignment = TextAlignment.Left,
        };
        combined.SetResourceReference(FlowDocument.BackgroundProperty, "RosterPanelSurface");
        combined.SetResourceReference(FlowDocument.ForegroundProperty, "LabelText");

        _previewBlockSrcRanges.Clear();
        _previewBlocks.Clear();
        int searchFrom = 0;

        foreach (var (segText, isConditional) in segments) {
            if (string.IsNullOrWhiteSpace(segText)) continue;

            // Locate this segment in rawText so we can compute block char offsets.
            var searchKey  = segText.TrimStart('\n');
            var segStart   = rawText.IndexOf(searchKey, searchFrom, StringComparison.Ordinal);
            if (segStart < 0) segStart = searchFrom;
            searchFrom = segStart + segText.Length;

            var segLines   = searchKey.Split('\n');
            var resolved   = SubstituteVars(segText);
            try {
                var segDoc = MarkdownFlowDocumentBuilder.BuildWithMap(resolved, out var blockLineRanges);
                var blocks = segDoc.Blocks.ToList();
                for (int i = 0; i < blocks.Count; i++) {
                    var block   = blocks[i];
                    var range   = i < blockLineRanges.Count ? blockLineRanges[i] : (StartLine: 0, EndLine: 0);
                    var srcRange = ComputeBlockCharRange(segLines, segStart, range);

                    segDoc.Blocks.Remove(block);
                    if (isConditional) {
                        ApplyForeground(block, conditionalBrush);
                        ApplyBackground(block, conditionalBgBrush);
                    }

                    _previewBlockSrcRanges.Add(srcRange);
                    _previewBlocks.Add(block);

                    combined.Blocks.Add(block);
                }
            }
            catch {
                var para = new Paragraph(new Run(resolved));
                if (isConditional) {
                    para.Foreground = conditionalBrush;
                    para.Background = conditionalBgBrush;
                }
                _previewBlockSrcRanges.Add((segStart, segText.Length));
                _previewBlocks.Add(para);
                combined.Blocks.Add(para);
            }
        }

        _markdownPreview.Document = combined;
        _lastHoveredBlockIdx = -1;
    }

    private void OnPreviewBlockMouseEnter(int blockIdx) {
        if (blockIdx >= _previewBlockSrcRanges.Count) return;
        var (charStart, charLength) = _previewBlockSrcRanges[blockIdx];
        if (charLength <= 0) return;
        HighlightInstructionsRange(charStart, charLength);
    }

    private void OnMarkdownPreviewMouseMove(object sender, MouseEventArgs e) {
        var mousePos = e.GetPosition(_markdownPreview);
        int idx = FindHoveredBlockIndex(mousePos);
        if (idx == _lastHoveredBlockIdx) return;
        _lastHoveredBlockIdx = idx;
        if (idx >= 0)
            OnPreviewBlockMouseEnter(idx);
        else
            ClearInstructionsHoverHighlight();
    }

    /// <summary>
    /// Finds the index of the block the mouse is currently over.
    /// <para>
    /// <see cref="TextPointer.GetCharacterRect"/> returns coordinates in the
    /// <c>DocumentPageView</c> visual's coordinate space.  Mouse position from
    /// <c>e.GetPosition(_markdownPreview)</c> is in the <see cref="FlowDocumentScrollViewer"/>
    /// viewport space.  These differ by however far the page visual has been scrolled.
    /// We use <c>TranslatePoint</c> from the <c>DocumentPageView</c> visual to
    /// <c>_markdownPreview</c> to compute the exact offset, then compare corrected Y values.
    /// </para>
    /// </summary>
    private int FindHoveredBlockIndex(Point mousePos) {
        if (_previewBlocks.Count == 0) return -1;

        // Locate the DocumentPageView visual that renders the FlowDocument content.
        // Its coordinate system is the same one GetCharacterRect reports in.
        var pageVisual = FindVisualDescendant(_markdownPreview,
            v => v.GetType().Name == "DocumentPageView") as FrameworkElement;
        if (pageVisual == null) return -1;

        // offset = where the page visual's (0,0) appears in _markdownPreview viewport coords.
        // Subtracting it from mousePos converts viewport coords to page-visual coords.
        var offset = pageVisual.TranslatePoint(new Point(0, 0), _markdownPreview);
        double docY = mousePos.Y - offset.Y;

        int best = -1;
        double bestTop = double.NegativeInfinity;
        for (int i = 0; i < _previewBlocks.Count; i++) {
            var block = _previewBlocks[i];
            var rect = block.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (rect == Rect.Empty) continue;
            if (rect.Top <= docY && rect.Top > bestTop) {
                bestTop = rect.Top;
                best = i;
            }
        }
        return best;
    }

    private static DependencyObject? FindVisualDescendant(
        DependencyObject parent, Func<DependencyObject, bool> predicate) {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++) {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (predicate(child)) return child;
            var found = FindVisualDescendant(child, predicate);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Converts a block's (StartLine, EndLine) range (0-based, relative to <paramref name="segLines"/>)
    /// into a (CharStart, CharLength) range relative to the start of <paramref name="rawText"/>.
    /// </summary>
    private static (int CharStart, int CharLength) ComputeBlockCharRange(
        string[] segLines, int segCharStart, (int StartLine, int EndLine) lineRange) {

        var (startLine, endLine) = lineRange;
        startLine = Math.Max(0, Math.Min(startLine, segLines.Length - 1));
        endLine   = Math.Max(startLine, Math.Min(endLine, segLines.Length - 1));

        int charStart = segCharStart;
        for (int i = 0; i < startLine; i++)
            charStart += segLines[i].Length + 1; // +1 for '\n'

        int charEnd = charStart;
        for (int i = startLine; i <= endLine; i++)
            charEnd += segLines[i].Length + 1;
        charEnd = Math.Max(charEnd - 1, charStart); // trim trailing '\n'

        return (charStart, charEnd - charStart);
    }

    private Canvas EnsureInstructionsOverlay() {
        if (_instructionsOverlay is not null) return _instructionsOverlay;
        _instructionsOverlay = new Canvas { IsHitTestVisible = false, Background = Brushes.Transparent };
        _instructionsHost.Children.Add(_instructionsOverlay);
        return _instructionsOverlay;
    }

    private void ClearInstructionsHoverHighlight() {
        _instructionsHoverTimer?.Stop();
        if (_instructionsHighlight is not null) {
            (_instructionsHighlight.Parent as Canvas)?.Children.Remove(_instructionsHighlight);
            _instructionsHighlight = null;
        }
    }

    private void HighlightInstructionsRange(int start, int length) {
        ClearInstructionsHoverHighlight();
        if (length <= 0) return;

        var editorBox = _instructionsPanel.EditorBox;
        var rect = editorBox.GetRectFromOffset(start);
        if (rect == Rect.Empty) return;

        var overlay    = EnsureInstructionsOverlay();
        var origin     = editorBox.TranslatePoint(new Point(0, 0), overlay);
        var charTopLeft = editorBox.TranslatePoint(rect.TopLeft, overlay);

        // Don't draw if the target line is scrolled outside the visible area.
        if (charTopLeft.Y < origin.Y || charTopLeft.Y >= origin.Y + editorBox.ActualHeight) return;

        var isDark = IsDark();
        var highlightColor = isDark
            ? Color.FromArgb(60, 255, 220, 80)
            : Color.FromArgb(50, 100, 180, 255);

        double highlightWidth = Math.Max(editorBox.ActualWidth - (charTopLeft.X - origin.X), 0);

        _instructionsHighlight = new System.Windows.Shapes.Rectangle {
            Width             = highlightWidth,
            Height            = Math.Max(rect.Height, 14),
            Fill              = new SolidColorBrush(highlightColor),
            IsHitTestVisible  = false,
        };
        Canvas.SetLeft(_instructionsHighlight, charTopLeft.X);
        Canvas.SetTop(_instructionsHighlight,  charTopLeft.Y);
        overlay.Children.Add(_instructionsHighlight);

        _instructionsHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _instructionsHoverTimer.Tick += (_, _) => {
            _instructionsHoverTimer?.Stop();
            ClearInstructionsHoverHighlight();
        };
        _instructionsHoverTimer.Start();
    }

    // ── Options YAML ──────────────────────────────────────────────────────────

    private void ScheduleOptionsDebounce() {
        _optionsDebounce?.Stop();
        _optionsDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _optionsDebounce.Tick += (_, _) => {
            _optionsDebounce?.Stop();
            _optionsDebounce = null;
            // Re-seed option values from the updated YAML so conditional preview stays in sync.
            var reparsed = ParseOptionsFromYaml(_optionsYamlBox.Text, out _);
            if (reparsed is not null) {
                _optionValues.Clear();
                foreach (var o in reparsed) _optionValues[o.Key] = o.RawValue ?? string.Empty;
            }
            UpdateOptionsPreview();
            UpdateMarkdownPreview();
        };
        _optionsDebounce.Start();
    }

    private void UpdateOptionsPreview() {
        _optionsPreviewPanel.Children.Clear();

        var opts = ParseOptionsFromYaml(_optionsYamlBox.Text, out var yamlError);

        // Show or hide the YAML error indicator
        if (!string.IsNullOrEmpty(yamlError)) {
            _yamlErrorText.Text       = "⚠ " + yamlError;
            _yamlErrorText.Visibility = Visibility.Visible;
        }
        else {
            _yamlErrorText.Text       = string.Empty;
            _yamlErrorText.Visibility = Visibility.Collapsed;
        }

        if (opts is null || opts.Count == 0) {
            var empty = new TextBlock { Text = "(no options)", FontStyle = FontStyles.Italic };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
            empty.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            _optionsPreviewPanel.Children.Add(empty);
            return;
        }

        foreach (var opt in opts) {
            if (!string.IsNullOrEmpty(opt.Label) && !string.Equals(opt.Type, "checkbox", StringComparison.OrdinalIgnoreCase)) {
                var label = new TextBlock {
                    Text         = opt.Label,
                    TextWrapping = TextWrapping.Wrap,
                    Margin       = new Thickness(0, 4, 0, 2),
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
                label.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
                _optionsPreviewPanel.Children.Add(label);
            }

            if (opt.Choices is { Count: > 0 }) {
                var optKey = opt.Key;
                int choiceIndex = 0;
                foreach (var choice in opt.Choices) {
                    var choiceValue = choice.Value;
                    var topMargin = choiceIndex == 0 ? 3 : 2; // First button gets more top margin for breathing room
                    var rb = new RadioButton {
                        Content   = choice.Value,
                        GroupName = $"preview-{opt.Key}",
                        IsChecked = string.Equals(choice.Value, _optionValues.GetValueOrDefault(opt.Key, opt.RawValue ?? string.Empty),
                            StringComparison.OrdinalIgnoreCase),
                        Margin    = new Thickness(8, topMargin, 0, 2),
                    };
                    rb.SetResourceReference(RadioButton.StyleProperty,    "ThemedRadioButtonStyle");
                    rb.SetResourceReference(RadioButton.ForegroundProperty, "ImportantText");
                    rb.SetResourceReference(RadioButton.FontSizeProperty,   "FontSizeSmall");
                    rb.Checked += (_, _) => {
                        _optionValues[optKey] = choiceValue;
                        UpdateMarkdownPreview();
                    };
                    _optionsPreviewPanel.Children.Add(rb);
                    choiceIndex++;
                }
            }
            else if (string.Equals(opt.Type, "checkbox", StringComparison.OrdinalIgnoreCase)) {
                var optKey = opt.Key;
                var currentValue = _optionValues.GetValueOrDefault(opt.Key, opt.RawValue ?? "false");
                var isChecked = IsStringTrueValue(currentValue);
                var cb = new CheckBox {
                    Content   = opt.Label ?? opt.Key,
                    IsChecked = isChecked,
                    Margin    = new Thickness(0, 2, 0, 2),
                };
                cb.SetResourceReference(CheckBox.ForegroundProperty, "ImportantText");
                cb.SetResourceReference(CheckBox.FontSizeProperty,   "FontSizeSmall");
                if (!string.IsNullOrEmpty(opt.Tooltip))
                    cb.ToolTip = opt.Tooltip;
                cb.Checked += (_, _) => {
                    _optionValues[optKey] = "true";
                    UpdateMarkdownPreview();
                };
                cb.Unchecked += (_, _) => {
                    _optionValues[optKey] = "false";
                    UpdateMarkdownPreview();
                };
                _optionsPreviewPanel.Children.Add(cb);
            }
            else {
                var optKey = opt.Key;
                var tb = new TextBox {
                    Text   = _optionValues.GetValueOrDefault(opt.Key, opt.RawValue ?? string.Empty),
                    Margin = new Thickness(0, 2, 0, 2),
                };
                tb.SetResourceReference(TextBox.ForegroundProperty,  "LabelText");
                tb.SetResourceReference(TextBox.BackgroundProperty,  "RosterPanelSurface");
                tb.SetResourceReference(TextBox.BorderBrushProperty, "SubtleBorder");
                tb.SetResourceReference(TextBox.FontSizeProperty,    "FontSizeSmall");
                tb.TextChanged += (_, _) => {
                    _optionValues[optKey] = tb.Text;
                    UpdateMarkdownPreview();
                };
                _optionsPreviewPanel.Children.Add(tb);
            }
        }
    }

    // ── YAML serialisation helpers ────────────────────────────────────────────

    private static string SerializeOptionsToYaml(IReadOnlyList<MaintenanceOption>? opts) {
        if (opts is null || opts.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var opt in opts) {
            sb.AppendLine($"{opt.Key}:");
            if (!string.IsNullOrEmpty(opt.Type))
                sb.AppendLine($"  type: {opt.Type}");
            if (!string.IsNullOrEmpty(opt.Label))
                sb.AppendLine($"  label: {opt.Label}");
            if (!string.IsNullOrEmpty(opt.Tooltip))
                sb.AppendLine($"  tooltip: {opt.Tooltip}");
            if (!string.IsNullOrEmpty(opt.RawValue))
                sb.AppendLine($"  value: {opt.RawValue}");
            if (opt.Choices is { Count: > 0 }) {
                sb.AppendLine("  choices:");
                foreach (var c in opt.Choices) {
                    sb.AppendLine($"    - value: {c.Value}");
                    if (!string.IsNullOrEmpty(c.Tooltip))
                        sb.AppendLine($"      tooltip: {c.Tooltip}");
                }
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static IReadOnlyList<MaintenanceOption>? ParseOptionsFromYaml(string yaml) =>
        ParseOptionsFromYaml(yaml, out _);

    /// <summary>
    /// Parses the options YAML. Sets <paramref name="error"/> to a user-facing
    /// error message on failure, or null on success.
    /// </summary>
    private static IReadOnlyList<MaintenanceOption>? ParseOptionsFromYaml(string yaml, out string? error) {
        error = null;
        if (string.IsNullOrWhiteSpace(yaml))
            return null;

        try {
            var lines   = yaml.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var options = new List<MaintenanceOption>();
            string? currentKey = null;
            string? type = null, label = null, tooltip = null, rawValue = null;
            bool inChoices = false;
            var choices = new List<MaintenanceOptionChoice>();
            MaintenanceOptionChoice? currentChoice = null;

            void CommitOption() {
                if (currentKey is null) return;
                if (currentChoice is not null) { choices.Add(currentChoice); currentChoice = null; }
                options.Add(new MaintenanceOption(
                    currentKey, rawValue ?? "", type ?? "string",
                    label, tooltip, choices.Count > 0 ? choices.ToList() : null));
                type = label = tooltip = rawValue = null;
                choices.Clear();
                inChoices = false;
            }

            foreach (var rawLine in lines) {
                if (string.IsNullOrWhiteSpace(rawLine)) continue;
                int indent  = 0;
                while (indent < rawLine.Length && rawLine[indent] == ' ') indent++;
                var trimmed = rawLine.TrimStart();

                if (indent == 0 && trimmed.EndsWith(':') && !trimmed.Contains(' ')) {
                    CommitOption();
                    currentKey = trimmed.TrimEnd(':');
                    continue;
                }

                if (currentKey is null) continue;

                if (inChoices) {
                    if (indent == 4 && trimmed.StartsWith("- ", StringComparison.Ordinal)) {
                        if (currentChoice is not null) choices.Add(currentChoice);
                        currentChoice = new MaintenanceOptionChoice();
                        var rest = trimmed[2..];
                        var ci = rest.IndexOf(':');
                        if (ci >= 0 && rest[..ci].Trim() == "value")
                            currentChoice.Value = rest[(ci + 1)..].Trim().Trim('"', '\'');
                        continue;
                    }
                    if (indent == 6 && currentChoice is not null) {
                        var ci = trimmed.IndexOf(':');
                        if (ci >= 0 && trimmed[..ci].Trim() == "tooltip")
                            currentChoice.Tooltip = trimmed[(ci + 1)..].Trim().Trim('"', '\'');
                        continue;
                    }
                    if (currentChoice is not null) { choices.Add(currentChoice); currentChoice = null; }
                    inChoices = false;
                }

                if (indent == 2) {
                    var ci = trimmed.IndexOf(':');
                    if (ci < 0) continue;
                    var k = trimmed[..ci].Trim();
                    var v = trimmed[(ci + 1)..].Trim().Trim('"', '\'');
                    switch (k) {
                        case "type":    type     = v; break;
                        case "label":   label    = v; break;
                        case "tooltip": tooltip  = v; break;
                        case "value":   rawValue = v; break;
                        case "default": rawValue = v; break;
                        case "choices":
                            if (v.Length == 0) { inChoices = true; }
                            break;
                    }
                }
            }

            CommitOption();
            return options.Count > 0 ? options : null;
        }
        catch (Exception ex) {
            error = ex.Message;
            return null;
        }
    }

    // ── Conditional preview helpers ──────────────────────────────────────────────

    private static bool IsOptionTruthy(string? value) =>
        LoopMdParser.IsOptionTruthy(value);

    private static bool IsStringTrueValue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1",    StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Delegates to <see cref="LoopMdParser.ResolveSegments"/> using the current
    /// <see cref="_optionValues"/> dictionary. Excluded conditional blocks are dropped;
    /// included ones are tagged <c>isConditional = true</c> for styled rendering.
    /// </summary>
    private List<(string Text, bool IsConditional)> ResolveConditionalSegments(string text) =>
        LoopMdParser.ResolveSegments(text, _optionValues);

    /// <summary>
    /// Replaces <c>{{varname}}</c> tokens (non-block tags) with the current
    /// option value from <see cref="_optionValues"/>.
    /// </summary>
    private string SubstituteVars(string text) =>
        Regex.Replace(text, @"\{\{([^}#/][^}]*)\}\}", m => {
            var key = m.Groups[1].Value.Trim();
            return _optionValues.TryGetValue(key, out var val) ? val : m.Value;
        });

    private bool IsDark() {
        // InputSurface is the actual preview background — reliably present in both theme files.
        if (TryFindResource("InputSurface") is SolidColorBrush b) {
            var lum = b.Color.R * 0.299 + b.Color.G * 0.587 + b.Color.B * 0.114;
            return lum < 128;
        }
        // Fallback: light label text implies dark theme.
        if (TryFindResource("LabelText") is SolidColorBrush lt)
            return lt.Color.R > 128;
        return false;
    }

    /// <summary>
    /// Returns a brush that is halfway between the current body text colour
    /// and the maximum contrast endpoint (white in dark theme, black in light).
    /// Conditional preview text is rendered with this brush to make it stand out.
    /// </summary>
    private Brush GetConditionalBackgroundBrush() {
        if (TryFindResource("ActivePanelSurface") is Brush b) return b;
        return IsDark()
            ? new SolidColorBrush(Color.FromArgb(0x50, 0x60, 0x80, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0x30, 0x20, 0x60, 0xC0));
    }

    private Brush GetConditionalTextBrush() {
        var baseColor = IsDark()
            ? Color.FromRgb(0xCC, 0xCC, 0xCC)   // fallback dark-theme body
            : Color.FromRgb(0x33, 0x33, 0x33);   // fallback light-theme body
        if (TryFindResource("LabelText") is SolidColorBrush lb)
            baseColor = lb.Color;
        var target = IsDark() ? Color.FromRgb(255, 255, 255) : Color.FromRgb(0, 0, 0);
        // 75% toward maximum contrast so conditional text stands out clearly
        return new SolidColorBrush(Color.FromRgb(
            (byte)((baseColor.R * 1 + target.R * 3) / 4),
            (byte)((baseColor.G * 1 + target.G * 3) / 4),
            (byte)((baseColor.B * 1 + target.B * 3) / 4)));
    }

    private static void ApplyBackground(Block block, Brush brush) {
        switch (block) {
            case Paragraph p:
                p.Background = brush;
                ApplyBackgroundToInlines(p.Inlines, brush);
                break;
            case Section s:
                s.Background = brush;
                foreach (var b in s.Blocks) ApplyBackground(b, brush);
                break;
            case List l:
                l.Background = brush;
                foreach (var li in l.ListItems)
                    foreach (var b in li.Blocks) ApplyBackground(b, brush);
                break;
            case BlockUIContainer buc:
                buc.SetValue(TextElement.BackgroundProperty, brush);
                break;
        }
    }

    // Override the CodeSurface background on inline-code Runs inside a conditional block
    // so they blend with the ActivePanelSurface rather than standing out as a different shade.
    private static void ApplyBackgroundToInlines(InlineCollection inlines, Brush brush) {
        foreach (var inline in inlines) {
            if (inline is Run run && IsInlineCodeRun(run))
                run.Background = brush;
            else if (inline is Span span)
                ApplyBackgroundToInlines(span.Inlines, brush);
        }
    }

    // Inline code runs from MarkdownFlowDocumentBuilder are identifiable by their Consolas font.
    private static bool IsInlineCodeRun(Run run)
        => run.FontFamily?.Source?.Contains("Consolas", StringComparison.OrdinalIgnoreCase) == true;

    private static void ApplyForeground(Block block, Brush brush) {
        switch (block) {
            case Paragraph p:
                p.Foreground = brush;
                foreach (var inline in p.Inlines) ApplyForegroundInline(inline, brush);
                break;
            case Section s:
                foreach (var b in s.Blocks) ApplyForeground(b, brush);
                break;
            case List l:
                foreach (var li in l.ListItems)
                    foreach (var b in li.Blocks) ApplyForeground(b, brush);
                break;
            case BlockUIContainer buc:
                buc.SetValue(TextElement.ForegroundProperty, brush);
                break;
        }
    }

    private static void ApplyForegroundInline(Inline inline, Brush brush) {
        inline.Foreground = brush;
        if (inline is Span span)
            foreach (var child in span.Inlines) ApplyForegroundInline(child, brush);
    }

    // ── Caret helpers ─────────────────────────────────────────────────────────
    // (Plain-text caret API provided by RichTextBoxExtensions)
}
