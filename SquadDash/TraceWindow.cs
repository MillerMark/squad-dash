using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace SquadDash;



/// <summary>
/// Floating developer/debug tool that shows a running log of trace entries from
/// <see cref="TranscriptScrollController"/> and <see cref="SquadDashTrace"/>.
///
/// <para>Open via the <c>/trace</c> slash command; close with the × button.
/// When closed, <see cref="TranscriptScrollController.TraceTarget"/> and
/// <see cref="SquadDashTrace.TraceTarget"/> are automatically cleared so tracing has zero
/// overhead while the window is not visible.</para>
/// </summary>
internal sealed class TraceWindow : ChromedWindow, ILiveTraceTarget
{
    private const int MaxFlushEntries = 200;
    private const int MaxLogTextChars = 250_000;
    private readonly TextBox _logTextBox = null!;
    private readonly WrapPanel _checkboxPanel = null!;
    private readonly ApplicationSettingsStore _settingsStore;
    private readonly Queue<(TraceCategory Category, string Timestamp, string Detail)> _pendingEntries = new();
    private HashSet<TraceCategory> _disabledCategories;
    private bool _flushPending;

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
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;

        var root = new Grid { Margin = new Thickness(12, 8, 12, 12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // hint
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // checkboxes
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // log
        ApplyOuterBorder().Child = root;

        // ── Header ──────────────────────────────────────────────────────────────────────────

        // Transparent background makes empty header space hit-testable.
        var header = new DockPanel { LastChildFill = false, Background = Brushes.Transparent };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // Buttons docked right; added before the title so DockPanel stacks them from the right.
        // Clear is rightmost — margin aligns its right edge with close button's left edge
        // (close = 38px wide, root right margin = 12px → offset = 26px).
        var clearButton = new Button
        {
            Content  = "Clear",
            MinWidth = 76,
            Height   = 30,
            Margin   = new Thickness(0, 0, 26, 0),
        };
        clearButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(clearButton, true);
        clearButton.Click += (_, _) => _logTextBox.Clear();
        DockPanel.SetDock(clearButton, Dock.Right);
        header.Children.Add(clearButton);

        var copyButton = new Button
        {
            Content  = "Copy",
            MinWidth = 76,
            Height   = 30,
            Margin   = new Thickness(0, 0, 8, 0),
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

        var titleBlock = new TextBlock
        {
            Text              = "Trace",
            FontSize          = (double)Application.Current.Resources["FontSizeSubtitle"],
            FontWeight        = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 8, 0),
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        DockPanel.SetDock(titleBlock, Dock.Left);
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
            FontSize = (double)Application.Current.Resources["FontSizeBody"],
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
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => EnqueueEntry(category, timestamp, detail));
            return;
        }

        EnqueueEntry(category, timestamp, detail);
    }

    private void EnqueueEntry(TraceCategory category, string timestamp, string detail)
    {
        if (_disabledCategories.Contains(category)) return;

        _pendingEntries.Enqueue((category, timestamp, detail));
        if (_flushPending)
            return;

        _flushPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, FlushPendingEntries);
    }

    private void FlushPendingEntries()
    {
        _flushPending = false;
        if (_pendingEntries.Count == 0)
            return;

        var builder = new StringBuilder();
        if (_logTextBox.Text.Length > 0)
            builder.AppendLine();

        var entryCount = 0;
        while (_pendingEntries.Count > 0 && entryCount < MaxFlushEntries)
        {
            var (category, timestamp, detail) = _pendingEntries.Dequeue();
            builder.Append($"{timestamp}  {category,-24}  {detail}");
            entryCount++;
            if (_pendingEntries.Count > 0 && entryCount < MaxFlushEntries)
                builder.AppendLine();
        }

        _logTextBox.AppendText(builder.ToString());
        TrimLogTextIfNeeded();
        _logTextBox.ScrollToEnd();

        if (_pendingEntries.Count > 0)
        {
            _flushPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, FlushPendingEntries);
        }
    }

    private void TrimLogTextIfNeeded()
    {
        var text = _logTextBox.Text;
        if (text.Length <= MaxLogTextChars)
            return;

        var trimTo = text.Length - MaxLogTextChars;
        var newlineIndex = text.IndexOf(Environment.NewLine, trimTo, StringComparison.Ordinal);
        var keepStart = newlineIndex >= 0
            ? newlineIndex + Environment.NewLine.Length
            : trimTo;

        _logTextBox.Text = text[keepStart..];
        _logTextBox.CaretIndex = _logTextBox.Text.Length;
    }
}
