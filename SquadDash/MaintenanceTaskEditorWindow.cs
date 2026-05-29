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

    private readonly MaintenanceTask                   _task;
    private readonly Func<ApplicationSettingsSnapshot> _settingsProvider;
    private readonly Action                            _onSaved;

    // ── Controls ──────────────────────────────────────────────────────────────

    private readonly TextBox     _titleBox;
    private readonly CheckBox    _enabledCheck;
    private readonly ComboBox    _frequencyCombo;
    private readonly ComboBox    _safetyCombo;
    private readonly TextBox     _optionsYamlBox;
    private readonly StackPanel  _optionsPreviewPanel;
    private readonly FlowDocumentScrollViewer _markdownPreview;
    private readonly RichTextBox _instructionsBox;

    // ── Voice ─────────────────────────────────────────────────────────────────

    private readonly PttTextBoxAttachment _pttTitle;
    private readonly PttTextBoxAttachment _pttOptions;

    // ── Debounce timers ───────────────────────────────────────────────────────

    private DispatcherTimer? _optionsDebounce;
    private DispatcherTimer? _instructionsDebounce;

    // ── Preview→source hover highlight ────────────────────────────────────────

    private Grid                                          _instructionsHost       = null!;
    private Canvas?                                       _instructionsOverlay;
    private System.Windows.Shapes.Rectangle?              _instructionsHighlight;
    private DispatcherTimer?                              _instructionsHoverTimer;
    private readonly List<(int CharStart, int CharLength)> _previewBlockSrcRanges  = new();
    private readonly List<Block>                           _previewBlocks          = new();
    private int                                            _lastHoveredBlockIdx    = -1;

    // ── Re-entrance guard ─────────────────────────────────────────────────────

    private bool _updatingHighlight;

    // Tracks current option values for conditional preview resolution
    private readonly Dictionary<string, string> _optionValues = new();

    // Error indicator below the YAML editor
    private TextBlock _yamlErrorText = null!;

    // ── Highlight colours ─────────────────────────────────────────────────────

    private static readonly Brush BlockTagBrush    = new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B)); // amber (light)
    private static readonly Brush BlockTagBrushDk  = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)); // gold (dark)
    private static readonly Brush VarBrush         = new SolidColorBrush(Color.FromRgb(0x2A, 0x8A, 0x8A)); // teal
    private static readonly Brush VarBrushDk       = new SolidColorBrush(Color.FromRgb(0x4D, 0xC4, 0xC4)); // lighter teal

    // ── Constructor ───────────────────────────────────────────────────────────

    internal MaintenanceTaskEditorWindow(
        Window owner,
        MaintenanceTask task,
        Func<ApplicationSettingsSnapshot> settingsProvider,
        Action onSaved) : base(captionHeight: 56) {

        _task             = task;
        _settingsProvider = settingsProvider;
        _onSaved          = onSaved;

        Owner                 = owner;
        Title                 = "Edit Maintenance Task";
        Width                 = 800;
        Height                = 700;
        MinWidth              = 500;
        MinHeight             = 400;
        ResizeMode            = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _pttTitle   = new PttTextBoxAttachment(_settingsProvider, this, Dispatcher);
        _pttOptions = new PttTextBoxAttachment(_settingsProvider, this, Dispatcher);

        _titleBox            = BuildTitleBox();
        _enabledCheck        = BuildEnabledCheck();
        _frequencyCombo      = BuildFrequencyCombo();
        _safetyCombo         = BuildSafetyCombo();
        _optionsYamlBox      = BuildOptionsYamlBox();
        _optionsPreviewPanel = new StackPanel { Margin = new Thickness(4) };
        _markdownPreview     = BuildMarkdownPreview();
        _instructionsBox     = BuildInstructionsBox();

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

        // Use PreviewMouseMove on the viewer instead of Block.MouseEnter — FlowDocumentScrollViewer
        // does not route ContentElement mouse events to individual Block instances.
        _markdownPreview.PreviewMouseMove += OnMarkdownPreviewMouseMove;
        _markdownPreview.MouseLeave       += (_, _) => {
            _lastHoveredBlockIdx = -1;
            ClearInstructionsHoverHighlight();
        };

        // Initial renders
        Loaded += (_, _) => {
            UpdateInstructionsHighlight();
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
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e) {
        _pttTitle.HandlePreviewKeyUp(e);
        _pttOptions.HandlePreviewKeyUp(e);
    }

    private void OnClosed(object? sender, EventArgs e) {
        if (_pttTitle.IsActive)   _ = _pttTitle.StopAsync();
        if (_pttOptions.IsActive) _ = _pttOptions.StopAsync();
        _pttTitle.Dispose();
        _pttOptions.Dispose();
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
        captionLabel.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        captionLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeMedium");
        DockPanel.SetDock(captionLabel, Dock.Top);
        root.Children.Add(captionLabel);

        // ── Title bar row ─────────────────────────────────────────────────────
        var titleRow = new DockPanel { Margin = new Thickness(8, 2, 50, 4), LastChildFill = true };
        var titleLabel = new TextBlock { Text = "Title:", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0) };
        titleLabel.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
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
        propsRow.Children.Add(BuildLabel("Safety:"));
        propsRow.Children.Add(_safetyCombo);
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
        cancelBtn.SetResourceReference(Button.StyleProperty, "FlatButtonStyle");
        cancelBtn.Click += (_, _) => Close();

        var saveBtn = new Button { Content = "Save", Padding = new Thickness(16, 4, 16, 4) };
        saveBtn.SetResourceReference(Button.StyleProperty, "FlatButtonStyle");
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
        var expander = new Expander { IsExpanded = true, Margin = new Thickness(8, 4, 8, 2) };
        expander.SetResourceReference(Expander.StyleProperty, "ThemedExpanderStyle");

        var headerTb = new TextBlock { Text = "UI Options" };
        headerTb.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        headerTb.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        expander.Header = headerTb;

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left: live preview (interactive)
        var leftPanel = new DockPanel { Margin = new Thickness(0, 0, 4, 0), LastChildFill = true };
        var previewLabel = new TextBlock { Text = "Preview", Margin = new Thickness(0, 0, 0, 2) };
        previewLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        previewLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
        DockPanel.SetDock(previewLabel, Dock.Top);
        leftPanel.Children.Add(previewLabel);
        var previewScroll = new ScrollViewer {
            Content             = _optionsPreviewPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        previewScroll.SetResourceReference(ScrollViewer.BackgroundProperty, "InputSurface");
        leftPanel.Children.Add(previewScroll);
        Grid.SetColumn(leftPanel, 0);

        // Right: YAML editor with error line below
        var rightPanel = new DockPanel { Margin = new Thickness(4, 0, 0, 0), LastChildFill = true };
        var yamlLabel = new TextBlock { Text = "Options YAML", Margin = new Thickness(0, 0, 0, 2) };
        yamlLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        yamlLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
        DockPanel.SetDock(yamlLabel, Dock.Top);
        rightPanel.Children.Add(yamlLabel);
        DockPanel.SetDock(_yamlErrorText, Dock.Bottom);
        rightPanel.Children.Add(_yamlErrorText);
        rightPanel.Children.Add(new ScrollViewer {
            Content             = _optionsYamlBox,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        Grid.SetColumn(rightPanel, 1);

        grid.Children.Add(leftPanel);
        grid.Children.Add(rightPanel);
        expander.Content = grid;
        return expander;
    }

    private UIElement BuildInstructionsSection() {
        var panel = new DockPanel { Margin = new Thickness(8, 2, 8, 0), LastChildFill = true };

        var headerTb = new TextBlock { Text = "Instructions", Margin = new Thickness(0, 0, 0, 2) };
        headerTb.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        headerTb.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        DockPanel.SetDock(headerTb, Dock.Top);
        panel.Children.Add(headerTb);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left: Markdown preview
        var leftLabel = new TextBlock { Text = "Preview", Margin = new Thickness(0, 0, 0, 2) };
        leftLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        leftLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");

        var leftStack = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(leftLabel, Dock.Top);
        leftStack.Children.Add(leftLabel);
        leftStack.Children.Add(_markdownPreview);
        Grid.SetColumn(leftStack, 0);

        // Splitter
        var splitter = new GridSplitter {
            Width               = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };
        splitter.SetResourceReference(GridSplitter.BackgroundProperty, "SubtleBorder");
        Grid.SetColumn(splitter, 1);

        // Right: editable RichTextBox
        var rightLabel = new TextBlock { Text = "Edit", Margin = new Thickness(4, 0, 0, 2) };
        rightLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        rightLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");

        _instructionsHost = new Grid();
        _instructionsHost.Children.Add(_instructionsBox);

        var rightStack = new DockPanel { Margin = new Thickness(4, 0, 0, 0), LastChildFill = true };
        DockPanel.SetDock(rightLabel, Dock.Top);
        rightStack.Children.Add(rightLabel);
        rightStack.Children.Add(_instructionsHost);
        Grid.SetColumn(rightStack, 2);

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
        tb.SetResourceReference(TextBox.ForegroundProperty, "BodyText");
        tb.SetResourceReference(TextBox.BackgroundProperty, "InputSurface");
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
        foreach (var item in new[] { "always", "daily", "weekly", "monthly", "after-commits" })
            cb.Items.Add(item);
        cb.SelectedItem = cb.Items.Contains(_task.Frequency) ? _task.Frequency : "daily";
        cb.SetResourceReference(ComboBox.ForegroundProperty,    "BodyText");
        cb.SetResourceReference(ComboBox.BackgroundProperty,    "InputSurface");
        cb.SetResourceReference(ComboBox.FontSizeProperty,      "FontSizeBody");
        cb.SetResourceReference(ComboBox.StyleProperty, "ThemedComboBoxStyle");
        return cb;
    }

    private ComboBox BuildSafetyCombo() {
        var cb = new ComboBox { Margin = new Thickness(0, 0, 8, 0) };
        foreach (var item in new[] { "report-only", "branch", "direct" })
            cb.Items.Add(item);
        cb.SelectedItem = cb.Items.Contains(_task.Safety) ? _task.Safety : "branch";
        cb.SetResourceReference(ComboBox.ForegroundProperty, "BodyText");
        cb.SetResourceReference(ComboBox.BackgroundProperty, "InputSurface");
        cb.SetResourceReference(ComboBox.FontSizeProperty,   "FontSizeBody");
        cb.SetResourceReference(ComboBox.StyleProperty, "ThemedComboBoxStyle");
        return cb;
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
        tb.SetResourceReference(TextBox.ForegroundProperty,  "BodyText");
        tb.SetResourceReference(TextBox.BackgroundProperty,  "InputSurface");
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
        viewer.SetResourceReference(FlowDocumentScrollViewer.BackgroundProperty, "PanelBackground");
        return viewer;
    }

    private RichTextBox BuildInstructionsBox() {
        var rtb = new RichTextBox {
            AcceptsReturn       = true,
            AcceptsTab          = true,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontFamily          = new FontFamily("Consolas, Courier New"),
        };
        rtb.SetResourceReference(RichTextBox.FontSizeProperty,   "FontSizeBody");
        rtb.SetResourceReference(RichTextBox.ForegroundProperty, "BodyText");
        rtb.SetResourceReference(RichTextBox.BackgroundProperty, "InputSurface");
        rtb.SetResourceReference(RichTextBox.BorderBrushProperty,"SubtleBorder");

        // Populate with initial text (plain)
        rtb.Document.Blocks.Clear();
        rtb.AppendText(_task.Instructions ?? "");

        rtb.TextChanged     += OnInstructionsTextChanged;
        rtb.MouseMove       += OnInstructionsMouseMove;

        return rtb;
    }

    private static TextBlock BuildLabel(string text) {
        var tb = new TextBlock {
            Text              = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 4, 0),
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        tb.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        return tb;
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private void OnSave(object sender, RoutedEventArgs e) {
        var instructionsText = new TextRange(
            _instructionsBox.Document.ContentStart,
            _instructionsBox.Document.ContentEnd).Text.TrimEnd('\r', '\n');

        var updatedTask = _task with {
            Title        = _titleBox.Text,
            Enabled      = _enabledCheck.IsChecked == true,
            Frequency    = (_frequencyCombo.SelectedItem as string) ?? _task.Frequency,
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

        _onSaved();
        Close();
    }

    // ── Instructions syntax highlighting ─────────────────────────────────────

    private void OnInstructionsTextChanged(object sender, TextChangedEventArgs e) {
        if (_updatingHighlight) return;
        ScheduleInstructionsDebounce();
    }

    private void ScheduleInstructionsDebounce() {
        _instructionsDebounce?.Stop();
        _instructionsDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _instructionsDebounce.Tick += (_, _) => {
            _instructionsDebounce?.Stop();
            _instructionsDebounce = null;
            UpdateInstructionsHighlight();
            UpdateMarkdownPreview();
        };
        _instructionsDebounce.Start();
    }

    private void UpdateInstructionsHighlight() {
        if (_updatingHighlight) return;
        _updatingHighlight = true;
        try {
            var doc      = _instructionsBox.Document;
            var fullText = new TextRange(doc.ContentStart, doc.ContentEnd).Text;

            // Preserve caret
            var caretOffset = GetCaretOffset(_instructionsBox);

            // Clear and rebuild with highlighted runs
            doc.Blocks.Clear();
            var para = new Paragraph { Margin = new Thickness(0) };
            AppendHighlightedRuns(para.Inlines, fullText);
            doc.Blocks.Add(para);

            // Restore caret
            RestoreCaretOffset(_instructionsBox, caretOffset);
        }
        finally {
            _updatingHighlight = false;
        }
    }

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

    private void AppendHighlightedRuns(InlineCollection inlines, string text) {
        var blockTagBrush = IsDark() ? BlockTagBrushDk : BlockTagBrush;
        var varBrush      = IsDark() ? VarBrushDk      : VarBrush;

        // Pattern for {{ ... }}
        var pattern = @"\{\{[^}]*\}\}";
        int pos = 0;
        foreach (Match m in Regex.Matches(text, pattern)) {
            if (m.Index > pos) {
                inlines.Add(new Run(text[pos..m.Index]));
            }

            var tag        = m.Value;
            bool isBlock   = tag.StartsWith("{{#", StringComparison.Ordinal)
                          || tag.StartsWith("{{/", StringComparison.Ordinal);
            var run = new Run(tag) { Foreground = isBlock ? blockTagBrush : varBrush };

            if (!isBlock) {
                var tip = MakeToolTip("System variable — will be replaced when the task runs.");
                run.ToolTip = tip;
            }

            inlines.Add(run);
            pos = m.Index + m.Length;
        }

        if (pos < text.Length)
            inlines.Add(new Run(text[pos..]));
    }

    private static ToolTip MakeToolTip(string text) {
        var tb = new TextBlock { Text = text };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        var tip = new ToolTip {
            Content         = tb,
            Padding         = new Thickness(6, 4, 6, 4),
            BorderThickness = new Thickness(1),
        };
        tip.SetResourceReference(Control.BackgroundProperty,    "InputSurface");
        tip.SetResourceReference(Control.BorderBrushProperty,  "InputBorder");
        return tip;
    }

    private void OnInstructionsMouseMove(object sender, MouseEventArgs e) {
        // Tooltip is on the Run inlines themselves — no extra hit-test needed.
    }

    // ── Markdown preview ──────────────────────────────────────────────────────

    private void UpdateMarkdownPreview() {
        var rawText = _instructionsBox.GetPlainText();

        var segments         = ResolveConditionalSegments(rawText);
        var conditionalBrush = GetConditionalTextBrush();

        var combined = new FlowDocument {
            FontFamily    = new FontFamily("Segoe UI, Segoe UI Emoji"),
            FontSize      = Application.Current.Resources["FontSizeMedium"] is double sz ? sz : 13.0,
            Background    = Brushes.Transparent,
            PagePadding   = new Thickness(18),
            TextAlignment = TextAlignment.Left,
        };
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
                    if (isConditional) ApplyForeground(block, conditionalBrush);

                    _previewBlockSrcRanges.Add(srcRange);
                    _previewBlocks.Add(block);

                    combined.Blocks.Add(block);
                }
            }
            catch {
                var para = new Paragraph(new Run(resolved));
                if (isConditional) para.Foreground = conditionalBrush;
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
        var mouseY = e.GetPosition(_markdownPreview).Y;
        var idx = FindBlockIndexAtY(mouseY);
        if (idx == _lastHoveredBlockIdx) return;
        _lastHoveredBlockIdx = idx;
        if (idx >= 0)
            OnPreviewBlockMouseEnter(idx);
        else
            ClearInstructionsHoverHighlight();
    }

    /// <summary>
    /// Finds the index of the block whose rendered top edge is the highest position
    /// still at or above <paramref name="mouseY"/> (in <see cref="_markdownPreview"/> coords).
    /// Returns -1 if no suitable block is found.
    /// </summary>
    private int FindBlockIndexAtY(double mouseY) {
        var doc = _markdownPreview.Document;
        if (doc == null || _previewBlocks.Count == 0) return -1;

        int best = -1;
        double bestTop = double.NegativeInfinity;

        for (int i = 0; i < _previewBlocks.Count; i++) {
            var block = _previewBlocks[i];
            if (block.Parent != doc) continue;
            var rect = block.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (rect == Rect.Empty) continue;
            if (rect.Top <= mouseY && rect.Top > bestTop) {
                bestTop = rect.Top;
                best = i;
            }
        }
        return best;
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

        var rect = _instructionsBox.GetRectFromOffset(start);
        if (rect == Rect.Empty) return;

        var overlay    = EnsureInstructionsOverlay();
        var origin     = _instructionsBox.TranslatePoint(new Point(0, 0), overlay);
        var charTopLeft = _instructionsBox.TranslatePoint(rect.TopLeft, overlay);

        // Don't draw if the target line is scrolled outside the visible area.
        if (charTopLeft.Y < origin.Y || charTopLeft.Y >= origin.Y + _instructionsBox.ActualHeight) return;

        var isDark = IsDark();
        var highlightColor = isDark
            ? Color.FromArgb(60, 255, 220, 80)
            : Color.FromArgb(50, 100, 180, 255);

        double highlightWidth = Math.Max(_instructionsBox.ActualWidth - (charTopLeft.X - origin.X), 0);

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
            empty.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            empty.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            _optionsPreviewPanel.Children.Add(empty);
            return;
        }

        foreach (var opt in opts) {
            if (!string.IsNullOrEmpty(opt.Label)) {
                var label = new TextBlock {
                    Text         = opt.Label,
                    TextWrapping = TextWrapping.Wrap,
                    Margin       = new Thickness(0, 4, 0, 2),
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                label.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
                _optionsPreviewPanel.Children.Add(label);
            }

            if (opt.Choices is { Count: > 0 }) {
                var optKey = opt.Key;
                foreach (var choice in opt.Choices) {
                    var choiceValue = choice.Value;
                    var rb = new RadioButton {
                        Content   = choice.Value,
                        GroupName = $"preview-{opt.Key}",
                        IsChecked = string.Equals(choice.Value, _optionValues.GetValueOrDefault(opt.Key, opt.RawValue),
                            StringComparison.OrdinalIgnoreCase),
                        Margin    = new Thickness(8, 1, 0, 1),
                    };
                    rb.SetResourceReference(RadioButton.StyleProperty,    "ThemedRadioButtonStyle");
                    rb.SetResourceReference(RadioButton.ForegroundProperty, "BodyText");
                    rb.SetResourceReference(RadioButton.FontSizeProperty,   "FontSizeSmall");
                    rb.Checked += (_, _) => {
                        _optionValues[optKey] = choiceValue;
                        UpdateMarkdownPreview();
                    };
                    _optionsPreviewPanel.Children.Add(rb);
                }
            }
            else {
                var optKey = opt.Key;
                var tb = new TextBox {
                    Text   = _optionValues.GetValueOrDefault(opt.Key, opt.RawValue ?? string.Empty),
                    Margin = new Thickness(0, 2, 0, 2),
                };
                tb.SetResourceReference(TextBox.ForegroundProperty,  "BodyText");
                tb.SetResourceReference(TextBox.BackgroundProperty,  "InputSurface");
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
        !string.IsNullOrEmpty(value) &&
        !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(value, "0",     StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// Returns a brush that is halfway between the current body text colour
    /// and the maximum contrast endpoint (white in dark theme, black in light).
    /// Conditional preview text is rendered with this brush to make it stand out.
    /// </summary>
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

    private static int GetCaretOffset(RichTextBox rtb) {
        try {
            var start = rtb.Document.ContentStart;
            var caret = rtb.CaretPosition;
            return start.GetOffsetToPosition(caret);
        }
        catch { return 0; }
    }

    private static void RestoreCaretOffset(RichTextBox rtb, int offset) {
        try {
            var start    = rtb.Document.ContentStart;
            var restored = start.GetPositionAtOffset(Math.Max(0, offset));
            if (restored is not null)
                rtb.CaretPosition = restored;
        }
        catch { /* ignore */ }
    }
}
