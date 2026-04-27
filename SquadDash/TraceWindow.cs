using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace SquadDash;



/// <summary>
/// Floating developer/debug tool that shows a running log of trace entries from
/// <see cref="TranscriptScrollController"/> and <see cref="SquadDashTrace"/>.
///
/// <para>Open via the <c>/trace</c> slash command; close with the × button or the Close button.
/// When closed, <see cref="TranscriptScrollController.TraceTarget"/> and
/// <see cref="SquadDashTrace.TraceTarget"/> are automatically cleared so tracing has zero
/// overhead while the window is not visible.</para>
/// </summary>
internal sealed class TraceWindow : Window, ILiveTraceTarget
{
    private readonly TextBox _logTextBox = null!;
    private readonly WrapPanel _checkboxPanel = null!;
    private readonly ApplicationSettingsStore _settingsStore;
    private HashSet<TraceCategory> _disabledCategories;

    public TraceWindow(ApplicationSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        var snapshot = settingsStore.Load();
        _disabledCategories = new HashSet<TraceCategory>(snapshot.DisabledTraceCategorySet);

        Title = "Trace";
        Width = 530;
        Height = 440;
        MinWidth = 380;
        MinHeight = 280;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;

        // WindowChrome removes the bright non-client WPF border that appears with
        // WindowStyle.None. CaptionHeight covers the header row so the entire top
        // strip is a drag zone — no MouseLeftButtonDown handler needed. Buttons are
        // marked IsHitTestVisibleInChrome=true so they still receive clicks.
        // ResizeBorderThickness keeps a thin resize hit-area at every edge.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight          = 36,
            ResizeBorderThickness  = new Thickness(4),
            GlassFrameThickness    = new Thickness(0),
            UseAeroCaptionButtons  = false,
        });

        // Disable Windows 11 DWM rounded corners — they expose the desktop
        // behind the window corners when using WindowStyle.None.
        SourceInitialized += (_, _) =>
            NativeMethods.DisableRoundedCorners(new WindowInteropHelper(this).Handle);

        // Outer border gives the window its background color and a 1.5px themed edge with
        // a slight corner radius so the corners render visibly at all DPI scales.
        var outerBorder = new Border { BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(4) };
        outerBorder.SetResourceReference(Border.BackgroundProperty, "AppSurface");
        outerBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // hint
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // checkboxes
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // log
        outerBorder.Child = root;
        Content = outerBorder;

        // ── Header ──────────────────────────────────────────────────────────────────────────

        // Transparent background makes empty header space hit-testable.
        var header = new DockPanel { LastChildFill = false, Background = Brushes.Transparent };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var closeButton = new Button
        {
            Content = "Close",
            MinWidth = 76,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        closeButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(closeButton, true);
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Right);
        header.Children.Add(closeButton);

        var copyButton = new Button
        {
            Content = "Copy",
            MinWidth = 76,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        copyButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(copyButton, true);
        copyButton.Click += (_, _) =>
        {
            var text = _logTextBox.Text;
            if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
        };
        DockPanel.SetDock(copyButton, Dock.Right);
        header.Children.Add(copyButton);

        var clearButton = new Button
        {
            Content = "Clear",
            MinWidth = 76,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        clearButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(clearButton, true);
        clearButton.Click += (_, _) => _logTextBox.Clear();
        DockPanel.SetDock(clearButton, Dock.Right);
        header.Children.Add(clearButton);

        var titleBlock = new TextBlock
        {
            Text = "Trace",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        header.Children.Add(titleBlock);

        // ── Hint ────────────────────────────────────────────────────────────────────────────

        var hintBlock = new TextBlock
        {
            Text = "Live trace entries from all sources. Use /trace again to bring to front. Uncheck categories to hide them.",
            Margin = new Thickness(0, 8, 0, 6),
            TextWrapping = TextWrapping.Wrap,
        };
        hintBlock.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        Grid.SetRow(hintBlock, 1);
        root.Children.Add(hintBlock);

        // ── Category checkboxes ──────────────────────────────────────────────────────────────

        var checkboxPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(checkboxPanel, 2);
        root.Children.Add(checkboxPanel);

        foreach (var cat in Enum.GetValues<TraceCategory>().OrderBy(c => c.ToString()))
        {
            var cb = new CheckBox
            {
                Content    = cat.ToString(),
                IsChecked  = !_disabledCategories.Contains(cat),
                Margin     = new Thickness(0, 0, 18, 4),
                Tag        = cat,
                ToolTip    = new ToolTip { Content = GetCategoryDescription(cat) },
                // Transparent background makes the gap between the box glyph and the
                // text label hit-testable — without it that strip has null background
                // and ignores mouse events entirely.
                Background = Brushes.Transparent,
                Cursor     = Cursors.Hand,
            };
            cb.Checked   += (_, _) => OnCategoryCheckboxChanged();
            cb.Unchecked += (_, _) => OnCategoryCheckboxChanged();
            checkboxPanel.Children.Add(cb);
        }

        _checkboxPanel = checkboxPanel;

        // ── Log area ────────────────────────────────────────────────────────────────────────

        var contentBorder = new Border
        {
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
        };
        contentBorder.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        contentBorder.SetResourceReference(Border.BorderBrushProperty, "LineColor");
        Grid.SetRow(contentBorder, 3);
        root.Children.Add(contentBorder);

        _logTextBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
        };
        _logTextBox.SetResourceReference(TextBox.BackgroundProperty, "CardSurface");
        _logTextBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
        // Remove the white dead-corner square where the two scrollbars meet by overriding
        // the SystemColors.ControlBrushKey that the default ScrollViewer template uses
        // for the corner rectangle fill.
        _logTextBox.Loaded += (_, _) =>
        {
            if (_logTextBox.TryFindResource("CardSurface") is Brush cardBrush)
                _logTextBox.Resources[SystemColors.ControlBrushKey] = cardBrush;
        };
        contentBorder.Child = _logTextBox;
    }

    // ────────────────────────────────────────────────────────────────────────────────────────
    // Checkbox handling
    // ────────────────────────────────────────────────────────────────────────────────────────

    private static string GetCategoryDescription(TraceCategory cat) => cat switch
    {
        TraceCategory.AgentCards   => "Agent card panel events — cards added, removed, updated, or synced with active threads.",
        TraceCategory.Bridge       => "TypeScript SDK ↔ C# bridge communication — agent stdin/stdout pipe messages and protocol events.",
        TraceCategory.General      => "Catch-all for trace messages that don't belong to a specific category.",
        TraceCategory.Load         => "Workspace and data loading — conversation store reads, thread restore, and workspace-open lifecycle.",
        TraceCategory.Performance  => "Startup and render timing — TURN_RENDER, LOAD UI, SCROLL_SETTLE, VIRTUAL_PREPEND, and other perf markers.",
        TraceCategory.PromptHealth => "Prompt text diagnostics — context size, token estimates, and prompt-health check events.",
        TraceCategory.Routing      => "Agent routing decisions — quick-reply routing, thread selection, and routing-issue publications.",
        TraceCategory.Scroll       => "Scroll position tracking — viewport anchoring, extent-change re-anchor, scroll-to-bottom button show/hide.",
        TraceCategory.Shutdown     => "App and workspace shutdown — window close, conversation save, and cleanup events.",
        TraceCategory.Startup      => "App initialization — window creation, workspace detection, and first-load sequencing.",
        TraceCategory.Threads           => "Agent thread lifecycle — thread creation, selection changes, and lazy on-demand render.",
        TraceCategory.TranscriptPanels  => "Secondary transcript panel operations — open, close, reconcile, title refresh, and grid rebuild events.",
        TraceCategory.UI                => "General UI events — layout updates, theme changes, and visual state transitions.",
        TraceCategory.Unhandled    => "Unhandled exceptions and unexpected error paths caught by the global exception handler.",
        TraceCategory.Workspace    => "Workspace management — open/close, conversation save/load, session ID tracking.",
        _                          => cat.ToString(),
    };

    private void OnCategoryCheckboxChanged()
    {
        var disabled = _checkboxPanel.Children
            .OfType<CheckBox>()
            .Where(cb => cb.IsChecked == false)
            .Select(cb => (TraceCategory)cb.Tag!)
            .ToList();

        _disabledCategories = new HashSet<TraceCategory>(disabled);
        _settingsStore.SaveDisabledTraceCategories(disabled);
    }

    // ────────────────────────────────────────────────────────────────────────────────────────
    // ILiveTraceTarget
    // ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends a formatted, timestamped trace entry to the log and auto-scrolls to the
    /// bottom so the latest event is always visible.  Entries whose category is currently
    /// disabled via the category checkboxes are silently dropped (window-display only;
    /// the file log is unaffected).
    ///
    /// <para>Thread-safe: marshals to the UI dispatcher if called from a background thread.
    /// In practice most trace events fire on the UI thread, so the fast-path is the
    /// synchronous path.</para>
    /// </summary>
    public void AddEntry(TraceCategory category, string detail)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AddEntry(category, detail));
            return;
        }

        if (_disabledCategories.Contains(category)) return;

        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var line = $"{timestamp}  {category,-24}  {detail}";

        if (_logTextBox.Text.Length > 0)
            _logTextBox.AppendText(Environment.NewLine);

        _logTextBox.AppendText(line);
        _logTextBox.ScrollToEnd();
    }
}
