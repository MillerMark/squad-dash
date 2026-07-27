using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;

namespace SquadDash;

internal sealed class TasksStatusWindow : ChromedWindow {
    private readonly RichTextBox _contentRichBox;
    private string _rawContent = string.Empty;

    private static readonly Regex s_emojiSplitter =
        new(@"(🔴|🟡|🟢)", RegexOptions.Compiled);

    // ── Watch Health section ──────────────────────────────────────────────────

    private SquadWatchHealthService?  _watchHealthService;
    private Func<string?>?            _watchHealthGetPath;
    private Action<bool>?             _watchHealthPersist;
    private DispatcherTimer?          _watchHealthAutoRefreshTimer;
    private SquadWatchHealthResult?   _watchHealthResult;
    private bool                      _watchHealthCommandInFlight;
    private bool                      _watchHealthSectionExpanded;

    private TextBlock?  _watchChevron;
    private Ellipse?    _watchStatusDot;
    private TextBlock?  _watchLastCheckLabel;
    private UIElement?  _watchBodyPanel;
    private StackPanel? _watchOutputStack;
    private Button?     _watchRefreshButton;
    private Button?     _watchStartButton;
    private Button?     _watchStopButton;
    private TextBox?    _watchIntervalBox;
    private CheckBox?   _watchExecuteCheckBox;
    private ComboBox?   _watchNotifyLevelCombo;

    private UIElement?  _watchHealthSection;

    public TasksStatusWindow() {
        Title = "Live Tasks";
        Width = 560;
        Height = 420;
        MinWidth = 420;
        MinHeight = 260;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;

        var root = new Grid {
            Margin = new Thickness(12, 8, 12, 12)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ApplyOuterBorder().Child = root;

        var header = new DockPanel {
            LastChildFill = false,
            Background    = Brushes.Transparent,
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // Copy button docked right, with margin to align its right edge with the close button's left edge.
        // Close button = 38px wide; root right margin = 12px → offset = 38 - 12 = 26px.
        var copyButton = new Button {
            Content  = "Copy",
            MinWidth = 76,
            Height   = 30,
            Margin   = new Thickness(0, 0, 26, 0),
        };
        copyButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(copyButton, true);
        copyButton.Click += (_, _) => {
            if (!string.IsNullOrEmpty(_rawContent))
                Clipboard.SetText(_rawContent);
        };
        DockPanel.SetDock(copyButton, Dock.Right);
        header.Children.Add(copyButton);

        var titleBlock = new TextBlock {
            Text              = "Live Tasks",
            FontSize          = (double)Application.Current.Resources["FontSizeSubtitle"],
            FontWeight        = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 8, 0),
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        DockPanel.SetDock(titleBlock, Dock.Left);
        header.Children.Add(titleBlock);

        var hintBlock = new TextBlock {
            Text = "Use /dropTasks to hide this window.",
            Margin = new Thickness(0, 8, 0, 10),
            TextWrapping = TextWrapping.Wrap
        };
        hintBlock.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        Grid.SetRow(hintBlock, 1);
        root.Children.Add(hintBlock);

        var contentBorder = new Border {
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };
        contentBorder.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        contentBorder.SetResourceReference(Border.BorderBrushProperty, "LineColor");
        Grid.SetRow(contentBorder, 2);
        root.Children.Add(contentBorder);

        _contentRichBox = new RichTextBox {
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Consolas"),
            FontSize = (double)Application.Current.Resources["FontSizeNormal"],
            Padding = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _contentRichBox.SetResourceReference(RichTextBox.BackgroundProperty, "CardSurface");
        _contentRichBox.SetResourceReference(RichTextBox.ForegroundProperty, "LabelText");
        contentBorder.Child = _contentRichBox;

        _watchHealthSection = BuildWatchHealthSection();
        Grid.SetRow(_watchHealthSection, 3);
        _watchHealthSection.Visibility = Visibility.Collapsed;
        root.Children.Add(_watchHealthSection);
    }

    // ── Watch Health section ──────────────────────────────────────────────────

    /// <summary>
    /// Attaches the Watch Health section to this window.
    /// Idempotent — subsequent calls update the workspace-path getter only.
    /// </summary>
    internal void AttachWatchHealth(
        SquadWatchHealthService service,
        Func<string?>           getWorkspacePath,
        Action<bool>            persistExpandedState,
        bool                    initiallyExpanded = false) {

        if (_watchHealthService is not null) {
            _watchHealthGetPath = getWorkspacePath;
            return;
        }

        _watchHealthService         = service;
        _watchHealthGetPath         = getWorkspacePath;
        _watchHealthPersist         = persistExpandedState;
        _watchHealthSectionExpanded = initiallyExpanded;

        if (_watchChevron is not null)
            _watchChevron.Text = _watchHealthSectionExpanded ? "▼" : "▶";
        if (_watchBodyPanel is not null)
            _watchBodyPanel.Visibility = _watchHealthSectionExpanded ? Visibility.Visible : Visibility.Collapsed;

        if (_watchHealthSection is not null)
            _watchHealthSection.Visibility = Visibility.Visible;

        _watchHealthAutoRefreshTimer = new DispatcherTimer {
            Interval = UiTimingConstants.WatchHealthAutoRefreshInterval
        };
        _watchHealthAutoRefreshTimer.Tick += async (_, _) => await WatchHealthAutoRefreshTickAsync();
    }

    private UIElement BuildWatchHealthSection() {
        var outerStack = new StackPanel();

        // Separator
        var sep = new Separator { Margin = new Thickness(0, 8, 0, 2) };
        sep.SetResourceReference(Separator.StyleProperty, "ThemedMenuSeparatorStyle");
        outerStack.Children.Add(sep);

        // Header row (always visible)
        var headerRow = new Border {
            Background = Brushes.Transparent,
            Cursor     = Cursors.Hand,
        };
        headerRow.MouseEnter += (_, _) => headerRow.SetResourceReference(Border.BackgroundProperty, "HoverSurface");
        headerRow.MouseLeave += (_, _) => headerRow.Background = Brushes.Transparent;

        var headerStack = new StackPanel {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(4, 3, 4, 3),
        };

        _watchChevron = new TextBlock {
            Text              = _watchHealthSectionExpanded ? "▼" : "▶",
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 4, 0),
        };
        _watchChevron.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeXSmall");
        _watchChevron.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        var headerLabel = new TextBlock {
            Text              = "Watch Health",
            FontWeight        = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 6, 0),
        };
        headerLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
        headerLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        _watchStatusDot = new Ellipse {
            Width             = 8,
            Height            = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 4, 0),
        };
        _watchStatusDot.SetResourceReference(Ellipse.FillProperty, "SubtleText");

        _watchLastCheckLabel = new TextBlock {
            VerticalAlignment = VerticalAlignment.Center,
            Opacity           = 0.7,
        };
        _watchLastCheckLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeXSmall");
        _watchLastCheckLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        headerStack.Children.Add(_watchChevron);
        headerStack.Children.Add(headerLabel);
        headerStack.Children.Add(_watchStatusDot);
        headerStack.Children.Add(_watchLastCheckLabel);
        headerRow.Child = headerStack;

        // Body (collapsible)
        var bodyStack = new StackPanel {
            Margin     = new Thickness(4, 2, 4, 6),
            Visibility = _watchHealthSectionExpanded ? Visibility.Visible : Visibility.Collapsed,
        };
        _watchBodyPanel = bodyStack;

        // Controls row
        var controlsRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };

        _watchRefreshButton = MakeWatchButton("Refresh");
        _watchRefreshButton.Click += async (_, _) => await WatchHealthRefreshAsync();

        var copyButton = MakeWatchButton("Copy");
        copyButton.Click += (_, _) => WatchHealthCopy();

        _watchStartButton = MakeWatchButton("Start");
        _watchStartButton.Click += async (_, _) => await WatchHealthStartAsync();

        _watchStopButton = MakeWatchButton("Stop");
        _watchStopButton.Click += async (_, _) => await WatchHealthStopAsync();

        controlsRow.Children.Add(_watchRefreshButton);
        controlsRow.Children.Add(copyButton);
        controlsRow.Children.Add(_watchStartButton);
        controlsRow.Children.Add(_watchStopButton);
        bodyStack.Children.Add(controlsRow);

        // Options row
        var optionsRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };

        var intervalLabel = new TextBlock {
            Text              = "Interval:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 3, 0),
        };
        intervalLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeXSmall");
        intervalLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        _watchIntervalBox = new TextBox {
            Text                      = "5",
            Width                     = 30,
            Height                    = 19,
            VerticalContentAlignment  = VerticalAlignment.Center,
            Margin                    = new Thickness(0, 0, 6, 0),
            BorderThickness           = new Thickness(1),
        };
        _watchIntervalBox.SetResourceReference(TextBox.FontSizeProperty,    "FontSizeXSmall");
        _watchIntervalBox.SetResourceReference(TextBox.BackgroundProperty,  "InputSurface");
        _watchIntervalBox.SetResourceReference(TextBox.ForegroundProperty,  "LabelText");
        _watchIntervalBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");

        _watchExecuteCheckBox = new CheckBox {
            Content           = "Execute",
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 6, 0),
        };
        _watchExecuteCheckBox.SetResourceReference(CheckBox.FontSizeProperty,   "FontSizeXSmall");
        _watchExecuteCheckBox.SetResourceReference(CheckBox.ForegroundProperty, "SubtleText");

        var notifyLabel = new TextBlock {
            Text              = "Notify:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 3, 0),
        };
        notifyLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeXSmall");
        notifyLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        _watchNotifyLevelCombo = new ComboBox {
            Height                   = 19,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness          = new Thickness(1),
        };
        _watchNotifyLevelCombo.SetResourceReference(ComboBox.FontSizeProperty,    "FontSizeXSmall");
        _watchNotifyLevelCombo.SetResourceReference(ComboBox.BackgroundProperty,  "InputSurface");
        _watchNotifyLevelCombo.SetResourceReference(ComboBox.ForegroundProperty,  "LabelText");
        _watchNotifyLevelCombo.SetResourceReference(ComboBox.BorderBrushProperty, "InputBorder");
        _watchNotifyLevelCombo.Items.Add(new ComboBoxItem { Content = "all" });
        _watchNotifyLevelCombo.Items.Add(new ComboBoxItem { Content = "important", IsSelected = true });
        _watchNotifyLevelCombo.Items.Add(new ComboBoxItem { Content = "none" });
        _watchNotifyLevelCombo.SelectedIndex = 1;

        optionsRow.Children.Add(intervalLabel);
        optionsRow.Children.Add(_watchIntervalBox);
        optionsRow.Children.Add(_watchExecuteCheckBox);
        optionsRow.Children.Add(notifyLabel);
        optionsRow.Children.Add(_watchNotifyLevelCombo);
        bodyStack.Children.Add(optionsRow);

        // Output area
        _watchOutputStack = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        var outputScroll = new ScrollViewer {
            MaxHeight                     = 110,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        outputScroll.Content = _watchOutputStack;
        bodyStack.Children.Add(outputScroll);

        outerStack.Children.Add(headerRow);
        outerStack.Children.Add(bodyStack);

        // Toggle collapse on header click
        headerRow.MouseLeftButtonUp += (_, _) => {
            _watchHealthSectionExpanded = !_watchHealthSectionExpanded;
            if (_watchChevron is not null)
                _watchChevron.Text = _watchHealthSectionExpanded ? "▼" : "▶";
            bodyStack.Visibility = _watchHealthSectionExpanded ? Visibility.Visible : Visibility.Collapsed;
            _watchHealthPersist?.Invoke(_watchHealthSectionExpanded);
        };

        SyncWatchHealthSection();
        return outerStack;
    }

    private static Button MakeWatchButton(string text) {
        var btn = new Button {
            Content = text,
            Height  = 20,
            Padding = new Thickness(6, 0, 6, 0),
            Margin  = new Thickness(0, 0, 4, 4),
        };
        btn.SetResourceReference(Button.FontSizeProperty, "FontSizeXSmall");
        btn.SetResourceReference(Button.StyleProperty,    "ThemedButtonStyle");
        return btn;
    }

    private void SyncWatchHealthSection() {
        if (_watchStatusDot is null) return;

        var result = _watchHealthResult;

        // Status dot — green when running, error-red on failure, subtle-gray otherwise.
        if (result?.Success == false)
            _watchStatusDot.SetResourceReference(Ellipse.FillProperty, "SystemErrorText");
        else if (result?.IsRunning == true)
            _watchStatusDot.SetResourceReference(Ellipse.FillProperty, "ActivePanelTitle");
        else
            _watchStatusDot.SetResourceReference(Ellipse.FillProperty, "SubtleText");

        if (_watchLastCheckLabel is not null)
            _watchLastCheckLabel.Text = result is not null ? DateTime.Now.ToString("HH:mm:ss") : string.Empty;

        if (_watchOutputStack is not null) {
            _watchOutputStack.Children.Clear();
            if (result is not null) {
                _watchOutputStack.Children.Add(MakeOutputRow(result.Summary, result.Success && result.IsRunning));
                foreach (var line in result.Lines.Where(l => !string.Equals(l, result.Summary, StringComparison.Ordinal)))
                    _watchOutputStack.Children.Add(MakeOutputRow(line, false));
            }
        }

        SyncWatchHealthControls();
    }

    private static TextBlock MakeOutputRow(string text, bool isAccent) {
        var tb = new TextBlock {
            Text         = text,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 1, 0, 1),
        };
        tb.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeXSmall");
        tb.SetResourceReference(TextBlock.ForegroundProperty, isAccent ? "ActivePanelTitle" : "SubtleText");
        return tb;
    }

    private void SyncWatchHealthControls() {
        if (_watchRefreshButton is null || _watchStartButton is null || _watchStopButton is null) return;

        var running      = _watchHealthResult?.IsRunning == true;
        var hasProcessId = _watchHealthResult?.ProcessId is not null;
        var canAct       = _watchHealthGetPath?.Invoke() is not null && !_watchHealthCommandInFlight;

        _watchRefreshButton.IsEnabled = canAct;
        _watchStartButton.IsEnabled   = canAct && !running;
        _watchStopButton.IsEnabled    = canAct && running && hasProcessId;
        if (_watchIntervalBox      is not null) _watchIntervalBox.IsEnabled      = canAct && !running;
        if (_watchExecuteCheckBox  is not null) _watchExecuteCheckBox.IsEnabled  = canAct && !running;
        if (_watchNotifyLevelCombo is not null) _watchNotifyLevelCombo.IsEnabled = canAct && !running;
    }

    private void SyncWatchHealthAutoRefresh() {
        var shouldRun = _watchHealthResult?.IsRunning == true;
        if (shouldRun && _watchHealthAutoRefreshTimer?.IsEnabled == false)
            _watchHealthAutoRefreshTimer.Start();
        else if (!shouldRun && _watchHealthAutoRefreshTimer?.IsEnabled == true)
            _watchHealthAutoRefreshTimer.Stop();
    }

    private async Task WatchHealthRefreshAsync() {
        var path = _watchHealthGetPath?.Invoke();
        if (_watchHealthService is null || path is null || _watchHealthCommandInFlight) return;
        _watchHealthCommandInFlight = true;
        _watchHealthResult = SquadWatchHealthResult.Checking;
        SyncWatchHealthSection();
        try {
            _watchHealthResult = await _watchHealthService.GetHealthAsync(path);
        } finally {
            _watchHealthCommandInFlight = false;
            SyncWatchHealthSection();
            SyncWatchHealthAutoRefresh();
        }
    }

    private void WatchHealthCopy() {
        if (_watchHealthResult is null) return;
        try { Clipboard.SetText(string.Join(Environment.NewLine, _watchHealthResult.Lines)); }
        catch { /* clipboard may be unavailable */ }
    }

    private async Task WatchHealthStartAsync() {
        var path = _watchHealthGetPath?.Invoke();
        if (_watchHealthService is null || path is null || _watchHealthCommandInFlight) return;

        var interval    = ReadWatchHealthInterval();
        var execute     = _watchExecuteCheckBox?.IsChecked == true;
        var notifyLevel = ReadWatchHealthNotifyLevel();

        _watchHealthCommandInFlight = true;
        _watchHealthResult = new SquadWatchHealthResult(
            true, false, "Starting Squad Watch...",
            [$"Starting: squad watch{(execute ? " --execute" : string.Empty)} --interval {interval}"]);
        SyncWatchHealthSection();

        try {
            var startResult = await _watchHealthService.StartWatchAsync(path, interval, execute, false, notifyLevel);
            if (!startResult.Success) {
                _watchHealthResult = SquadWatchHealthResult.FromCommandResult(startResult);
                return;
            }
            await Task.Delay(UiTimingConstants.WatchHealthStartSettleMs);
            _watchHealthResult = await _watchHealthService.GetHealthAsync(path);
        } finally {
            _watchHealthCommandInFlight = false;
            SyncWatchHealthSection();
            SyncWatchHealthAutoRefresh();
        }
    }

    private async Task WatchHealthStopAsync() {
        var path = _watchHealthGetPath?.Invoke();
        if (_watchHealthService is null || path is null || _watchHealthCommandInFlight) return;

        var processId = _watchHealthResult?.ProcessId;
        if (processId is null) return;

        _watchHealthCommandInFlight = true;
        _watchHealthResult = new SquadWatchHealthResult(
            true, false, "Stopping Squad Watch...", ["Stopping watch..."]);
        SyncWatchHealthSection();

        try {
            var stopResult = await _watchHealthService.StopWatchAsync(processId.Value);
            if (!stopResult.Success) {
                _watchHealthResult = SquadWatchHealthResult.FromCommandResult(stopResult);
                return;
            }
            await Task.Delay(UiTimingConstants.WatchHealthStopSettleMs);
            _watchHealthResult = await _watchHealthService.GetHealthAsync(path);
        } finally {
            _watchHealthCommandInFlight = false;
            SyncWatchHealthSection();
            SyncWatchHealthAutoRefresh();
        }
    }

    private async Task WatchHealthAutoRefreshTickAsync() {
        var path = _watchHealthGetPath?.Invoke();
        if (_watchHealthService is null || path is null || _watchHealthCommandInFlight) return;
        _watchHealthCommandInFlight = true;
        SyncWatchHealthControls();
        try {
            _watchHealthResult = await _watchHealthService.GetHealthAsync(path);
        } catch { /* ignore transient errors */ }
        finally {
            _watchHealthCommandInFlight = false;
            SyncWatchHealthSection();
            SyncWatchHealthAutoRefresh();
        }
    }

    private int ReadWatchHealthInterval() {
        var text = _watchIntervalBox?.Text;
        if (int.TryParse(text, out var interval) && interval > 0) return interval;
        if (_watchIntervalBox is not null) _watchIntervalBox.Text = "5";
        return 5;
    }

    private string? ReadWatchHealthNotifyLevel() {
        return _watchNotifyLevelCombo?.SelectedItem is ComboBoxItem item
            ? item.Content as string
            : null;
    }

    public void UpdateContent(string content) {
        _rawContent = content ?? string.Empty;

        var doc = new FlowDocument {
            FontFamily = new FontFamily("Consolas"),
            FontSize = (double)Application.Current.Resources["FontSizeNormal"],
            PagePadding = new Thickness(0),
        };
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "LabelText");

        foreach (var rawLine in _rawContent.Split('\n')) {
            var line = rawLine.TrimEnd('\r');
            var para = new Paragraph {
                Margin = new Thickness(0),
                LineHeight = 20,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
            AppendColoredInlines(para.Inlines, line);
            doc.Blocks.Add(para);
        }

        _contentRichBox.Document = doc;
        _contentRichBox.ScrollToHome();
    }

    private void AppendColoredInlines(InlineCollection inlines, string text) {
        var parts = s_emojiSplitter.Split(text);
        foreach (var part in parts) {
            var key = EmojiResourceKey(part);
            if (key is not null) {
                // Colored emoji glyphs ignore Run.Foreground — use a real Ellipse instead.
                var ellipse = new System.Windows.Shapes.Ellipse {
                    Width  = 11,
                    Height = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 1, -1),
                };
                ellipse.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, key);
                inlines.Add(new InlineUIContainer(ellipse));
            } else {
                var run = new Run(part);
                run.SetResourceReference(Run.ForegroundProperty, "LabelText");
                inlines.Add(run);
            }
        }
    }

    internal static string? EmojiResourceKey(string segment) => segment switch {
        "⚫" => "PriorityCritical",
        "🔴" => "PriorityHigh",
        "🟡" => "PriorityMid",
        "🟢" => "PriorityLow",
        _    => null
    };
}
