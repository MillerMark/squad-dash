using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>Modeless pop-up window that displays a single <see cref="InboxMessage"/>.</summary>
internal sealed class InboxMessageWindow : ChromedWindow
{
    public string MessageId { get; }

    private readonly Func<string, TaskItem?>? _lookupTask;
    private readonly Action<string, InboxMessage>? _attachSelectedTextToChat;
    private readonly Action<string, InboxMessage>? _attachSelectedTextToNewChat;
    private readonly InboxMessage _message;
    private readonly FlowDocumentScrollViewer _bodyViewer;
    private readonly WrapPanel _attachmentsPanel;
    private readonly WrapPanel _actionsPanel;
    private readonly Grid _rootGrid;
    private readonly ContentControl _preflightRecoveryHost;
    private DispatcherTimer? _preflightPollTimer;
    private DispatcherTimer? _relativeTimeTimer;
    private readonly Action? _onMarkedRead;
    private readonly Action? _onMarkedUnread;
    private readonly Action? _onRepliedInChat;
    private readonly Action<double>? _onFontSizeChanged;
    private double _bodyFontSize;
    private bool _markedRead;

    public InboxMessageWindow(
        InboxMessage message,
        Action<InboxAction, InboxMessage> onActionClicked,
        Func<string, TaskItem?>? lookupTask = null,
        Action<string, InboxMessage>? attachSelectedTextToChat = null,
        Action<string, InboxMessage>? attachSelectedTextToNewChat = null,
        Action? onMarkedRead = null,
        Action? onMarkedUnread = null,
        Action? onRepliedInChat = null,
        double initialFontSize = 14,
        Action<double>? onFontSizeChanged = null,
        Action<InboxAttachment>? openDecomposePlan = null,
        Action<string>? openCommit = null)
        : base(captionHeight: 28, resizeMode: ResizeMode.CanResize)
    {
        _lookupTask             = lookupTask;
        _attachSelectedTextToChat = attachSelectedTextToChat;
        _attachSelectedTextToNewChat = attachSelectedTextToNewChat;
        _message                = message;
        _onMarkedRead           = onMarkedRead;
        _onMarkedUnread         = onMarkedUnread;
        _onRepliedInChat        = onRepliedInChat;
        _onFontSizeChanged      = onFontSizeChanged;
        _bodyFontSize           = initialFontSize > 0 ? initialFontSize : 14;
        MessageId               = message.Id;
        Title                   = message.Subject;
        SizeToContent           = SizeToContent.Manual;
        Width                   = 1312;
        Height                  = 825;
        MinWidth                = 400;
        MinHeight               = 300;
        Topmost                 = false;
        WindowStartupLocation   = WindowStartupLocation.CenterOwner;
        ShowInTaskbar           = true;

        SquadDashTrace.Write(TraceCategory.Inbox, $"InboxMessageWindow.ctor: msgId={message.Id} subject='{message.Subject}' attachments={message.Attachments.Count} actions={message.Actions.Count}");

        // Root grid: header / attachments / actions / body
        var root = new Grid();
        _rootGrid = root;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 0 header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 1 attachments
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 2 actions
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3 body
        var outerBorder = ApplyOuterBorder();
        outerBorder.Child = root;

        // ── Header ────────────────────────────────────────────────────────────
        var headerDock = new DockPanel
        {
            Margin          = new Thickness(12, 10, 12, 6),
            LastChildFill   = true,
        };
        Grid.SetRow(headerDock, 0);
        root.Children.Add(headerDock);

        // "Close as Unread" button — docked to the right so subject text fills the rest
        var closeUnreadBtn = new Button
        {
            Content           = "Close as Unread",
            Padding           = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Top,
            Margin            = new Thickness(8, 0, 44, 0),
            ToolTip           = "Mark as unread and close",
        };
        closeUnreadBtn.SetResourceReference(Button.StyleProperty,    "FlatButtonStyle");
        closeUnreadBtn.SetResourceReference(Button.FontSizeProperty, "FontSizeSmall");
        WindowChrome.SetIsHitTestVisibleInChrome(closeUnreadBtn, true);
        closeUnreadBtn.Click += (_, _) => { _onMarkedUnread?.Invoke(); Close(); };
        DockPanel.SetDock(closeUnreadBtn, Dock.Right);
        headerDock.Children.Add(closeUnreadBtn);

        var replyInChatBtn = new Button
        {
            Content           = "Reply in Chat",
            Padding           = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Top,
            Margin            = new Thickness(0, 0, 8, 0),
            ToolTip           = "Attach this message to your next chat prompt",
        };
        replyInChatBtn.SetResourceReference(Button.StyleProperty,    "FlatButtonStyle");
        replyInChatBtn.SetResourceReference(Button.FontSizeProperty, "FontSizeSmall");
        WindowChrome.SetIsHitTestVisibleInChrome(replyInChatBtn, true);
        replyInChatBtn.Click += (_, _) => { _onRepliedInChat?.Invoke(); Close(); };
        DockPanel.SetDock(replyInChatBtn, Dock.Right);
        headerDock.Children.Add(replyInChatBtn);

        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };
        headerDock.Children.Add(headerPanel);

        var subjectLabel = new TextBlock
        {
            Text       = message.Subject,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
        };
        subjectLabel.SetResourceReference(TextBlock.FontSizeProperty,  "FontSizeNormal");
        subjectLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        headerPanel.Children.Add(subjectLabel);

        var ts = StatusTimingPresentation.FormatRelativeTimestamp(message.Timestamp);
        var metaLabel = new TextBlock
        {
            Text   = $"{message.From} · {ts}",
            Margin = new Thickness(0, 2, 0, 0),
        };
        metaLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
        metaLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        headerPanel.Children.Add(metaLabel);

        // Separator
        var sep = new Separator { Margin = new Thickness(0, 4, 0, 0) };
        headerPanel.Children.Add(sep);

        // ── Attachments ───────────────────────────────────────────────────────
        var visibleAttachments = message.Attachments
            .Where(DurableApprovalRequestManager.IsPresentationAttachment)
            .ToArray();
        _attachmentsPanel = new WrapPanel
        {
            Margin      = new Thickness(12, 4, 12, 0),
            Orientation = Orientation.Horizontal,
            Visibility  = visibleAttachments.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        Grid.SetRow(_attachmentsPanel, 1);
        root.Children.Add(_attachmentsPanel);

        foreach (var att in visibleAttachments)
            _attachmentsPanel.Children.Add(BuildAttachmentChip(att, this, _bodyFontSize, _lookupTask, openDecomposePlan));

        // ── Actions ───────────────────────────────────────────────────────────
        var actionRegion = new StackPanel();
        Grid.SetRow(actionRegion, 2);
        root.Children.Add(actionRegion);

        _actionsPanel = new WrapPanel
        {
            Margin      = new Thickness(12, 4, 12, 0),
            Orientation = Orientation.Horizontal,
            Visibility  = message.Actions is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed,
        };
        actionRegion.Children.Add(_actionsPanel);

        foreach (var action in message.Actions)
            _actionsPanel.Children.Add(BuildActionButton(action, message, _bodyFontSize, onActionClicked));

        _preflightRecoveryHost = new ContentControl
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(12, 2, 12, 4),
        };
        actionRegion.Children.Add(_preflightRecoveryHost);

        // ── Body ──────────────────────────────────────────────────────────────
        var doc = MarkdownFlowDocumentBuilder.Build(message.Body ?? string.Empty, _bodyFontSize);
        _relativeTimeTimer = InboxRelativeTimePresenter.Attach(doc);
        InboxCommitLinkPresenter.Attach(doc, openCommit);

        _bodyViewer = new FlowDocumentScrollViewer
        {
            Margin                        = new Thickness(0),
            Padding                       = new Thickness(10, 8, 10, 8),
            VerticalAlignment             = VerticalAlignment.Stretch,
            HorizontalAlignment           = HorizontalAlignment.Stretch,
            // Auto shows a horizontal scrollbar only when content (e.g. a wide table)
            // genuinely overflows the viewport. Text paragraphs reflow normally.
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Document                      = doc,
            Focusable                     = true,
        };

        _bodyViewer.PreviewKeyDown += (_, e) =>
        {
            var sv = FindScrollViewer(_bodyViewer);
            if (sv is null) return;
            switch (e.Key)
            {
                case Key.PageDown: sv.PageDown(); e.Handled = true; break;
                case Key.PageUp:   sv.PageUp();   e.Handled = true; break;
                case Key.Home:     sv.ScrollToTop();    e.Handled = true; break;
                case Key.End:      sv.ScrollToBottom(); e.Handled = true; break;
                case Key.Down:     sv.LineDown(); e.Handled = true; break;
                case Key.Up:       sv.LineUp();   e.Handled = true; break;
            }
        };

        // Fix for code block copying: FlowDocument's default copy handler can skip
        // Paragraph elements with backgrounds (code blocks). Intercept the copy event
        // to extract plain text from the selection, preserving all content.
        DataObject.AddCopyingHandler(_bodyViewer, OnFlowDocumentCopying);

        _bodyViewer.PreviewMouseWheel += OnBodyViewerPreviewMouseWheel;
        Closed += (_, _) => _relativeTimeTimer?.Stop();

        var bodyBorder = new Border
        {
            Margin = new Thickness(8, 6, 8, 8),
            Child  = _bodyViewer,
        };
        bodyBorder.SetResourceReference(Border.BackgroundProperty, "InboxBodySurface");
        Grid.SetRow(bodyBorder, 3);
        root.Children.Add(bodyBorder);

        Loaded += (_, _) =>
        {
            SquadDashTrace.Write(TraceCategory.Inbox, $"InboxMessageWindow.Loaded: msgId={MessageId} ActualWidth={ActualWidth} ActualHeight={ActualHeight} bodyDocBlocks={_bodyViewer.Document?.Blocks.Count ?? -1}");

            // Deferred mark-as-read: fire after 3 s of viewing OR on any downward scroll.
            if (_onMarkedRead is not null)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                timer.Tick += (_, _) => { timer.Stop(); FireMarkRead(); };
                timer.Start();

                var sv = FindScrollViewer(_bodyViewer);
                if (sv is not null)
                    sv.ScrollChanged += (_, e) => { if (e.VerticalChange > 0) FireMarkRead(); };
            }

            // Set up the context menu after OnApplyTemplate so our assignment wins
            // over any default ContextMenu the FlowDocumentScrollViewer installs.
            if (_attachSelectedTextToChat is not null || _attachSelectedTextToNewChat is not null)
            {
                var contextMenu = new ContextMenu();
                contextMenu.Style = (Style)Application.Current.Resources["ThemedContextMenuStyle"];

                if (_attachSelectedTextToChat is not null)
                {
                    var attachMenuItem = new MenuItem { Header = "Add to Chat" };
                    attachMenuItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                    attachMenuItem.Click += (_, _) =>
                    {
                        var sel = _bodyViewer.Selection;
                        if (!sel.IsEmpty)
                            _attachSelectedTextToChat(sel.Text, _message);
                    };
                    contextMenu.Items.Add(attachMenuItem);
                }

                if (_attachSelectedTextToNewChat is not null)
                {
                    var addToNewChatMenuItem = new MenuItem { Header = "Add to New Chat" };
                    addToNewChatMenuItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                    addToNewChatMenuItem.Click += (_, _) =>
                    {
                        var sel = _bodyViewer.Selection;
                        if (!sel.IsEmpty)
                            _attachSelectedTextToNewChat(sel.Text, _message);
                    };
                    contextMenu.Items.Add(addToNewChatMenuItem);
                }

                var copyMenuItem = new MenuItem { Header = "Copy" };
                copyMenuItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                copyMenuItem.Click += (_, _) =>
                {
                    var sel = _bodyViewer.Selection;
                    if (!sel.IsEmpty)
                        Clipboard.SetText(sel.Text);
                };
                contextMenu.Items.Add(copyMenuItem);

                _bodyViewer.ContextMenu = contextMenu;
                _bodyViewer.ContextMenuOpening += (_, e) =>
                {
                    if (_bodyViewer.Selection.IsEmpty)
                        e.Handled = true;
                };
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _bodyViewer.Focus());
        };

        Closed += (_, _) =>
        {
            _preflightPollTimer?.Stop();
            _preflightPollTimer = null;
        };
    }

    /// <summary>Shows a persistent, contextual recovery card for a blocked plan action.</summary>
    internal void ShowPlanPreflightRecovery(
        PlanPreflightBlockedException exception,
        Action retry,
        Action viewChanges,
        Func<Task<bool>>? isWorkspaceClean = null)
    {
        _preflightPollTimer?.Stop();
        var content = PlanPreflightRecoveryContent.From(exception);
        _actionsPanel.IsEnabled = false;

        var stack = new StackPanel();
        var title = new TextBlock
        {
            Text = content.Title,
            FontWeight = FontWeights.SemiBold,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "PlanPreflightWarningText");
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeNormal");
        stack.Children.Add(title);

        var summary = new TextBlock
        {
            Text = content.Summary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 6),
        };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "PlanPreflightWarningText");
        summary.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        stack.Children.Add(summary);

        var files = new TextBlock
        {
            Text = content.ChangedFilesSummary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        files.SetResourceReference(TextBlock.ForegroundProperty, "PlanPreflightWarningText");
        files.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        stack.Children.Add(files);

        var details = new Expander
        {
            Header = "Technical details",
            Content = new TextBlock
            {
                Text = content.TechnicalDetails,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 4, 0, 6),
            },
            Margin = new Thickness(0, 0, 0, 6),
        };
        details.SetResourceReference(Expander.ForegroundProperty, "PlanPreflightWarningText");
        if (TryFindResource("ThemedExpanderStyle") is Style expanderStyle)
            details.Style = expanderStyle;
        stack.Children.Add(details);

        var readiness = new TextBlock
        {
            Text = content.RecoveryGuidance,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        readiness.SetResourceReference(TextBlock.ForegroundProperty, "PlanPreflightWarningText");
        readiness.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        stack.Children.Add(readiness);

        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        var viewButton = BuildRecoveryButton("View Changes");
        var copyButton = BuildRecoveryButton("Copy Details");
        var retryButton = BuildRecoveryButton("Retry");
        var dismissButton = BuildRecoveryButton("Keep Plan Pending");
        buttons.Children.Add(viewButton);
        buttons.Children.Add(copyButton);
        buttons.Children.Add(retryButton);
        buttons.Children.Add(dismissButton);
        stack.Children.Add(buttons);

        viewButton.Click += (_, _) => viewChanges();
        copyButton.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(content.ClipboardText);
                readiness.Text = "Details copied to the clipboard.";
            }
            catch
            {
                readiness.Text = "The clipboard is busy. Try Copy Details again.";
            }
        };
        retryButton.Click += (_, _) =>
        {
            buttons.IsEnabled = false;
            readiness.Text = "Checking the workspace and retrying…";
            retry();
        };
        dismissButton.Click += (_, _) => ClearPlanPreflightRecovery();

        var card = new Border
        {
            Child = stack,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
        };
        card.SetResourceReference(Border.BackgroundProperty, "PlanPreflightWarningSurface");
        card.SetResourceReference(Border.BorderBrushProperty, "PlanPreflightWarningBorder");
        _preflightRecoveryHost.Content = card;
        _preflightRecoveryHost.Visibility = Visibility.Visible;

        if (isWorkspaceClean is not null)
        {
            var pollInFlight = false;
            _preflightPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _preflightPollTimer.Tick += async (_, _) =>
            {
                if (pollInFlight) return;
                pollInFlight = true;
                try
                {
                    if (!await isWorkspaceClean()) return;
                    _preflightPollTimer?.Stop();
                    readiness.Text = "Workspace is clean. Retry is ready.";
                    retryButton.FontWeight = FontWeights.SemiBold;
                }
                catch { /* A failed readiness probe leaves the recovery card unchanged. */ }
                finally { pollInFlight = false; }
            };
            _preflightPollTimer.Start();
        }

        Button BuildRecoveryButton(string label)
        {
            var button = new Button
            {
                Content = label,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(10, 4, 10, 4),
                MinHeight = 28,
                Cursor = Cursors.Hand,
            };
            if (TryFindResource("QuickReplyButtonStyle") is Style style)
                button.Style = style;
            button.SetResourceReference(Button.BackgroundProperty, "QuickReplySurface");
            button.SetResourceReference(Button.ForegroundProperty, "QuickReplyText");
            button.SetResourceReference(Button.BorderBrushProperty, "QuickReplyBorder");
            return button;
        }
    }

    private void ClearPlanPreflightRecovery()
    {
        _preflightPollTimer?.Stop();
        _preflightPollTimer = null;
        _preflightRecoveryHost.Content = null;
        _preflightRecoveryHost.Visibility = Visibility.Collapsed;
        _actionsPanel.IsEnabled = true;
    }

    private void FireMarkRead()
    {
        if (_markedRead) return;
        _markedRead = true;
        _onMarkedRead?.Invoke();
    }

    private void OnBodyViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        e.Handled = true;

        const double step = 1.0;
        const double min  = 9.0;
        const double max  = 28.0;
        _bodyFontSize = Math.Clamp(_bodyFontSize + (e.Delta > 0 ? step : -step), min, max);

        if (_bodyViewer.Document is not null)
            RebuildDocument();
        ApplyInteractiveFontSize(_actionsPanel, _attachmentsPanel, _bodyFontSize);

        _onFontSizeChanged?.Invoke(_bodyFontSize);
    }

    private void RebuildDocument()
    {
        var doc = MarkdownFlowDocumentBuilder.Build(_message.Body ?? string.Empty, _bodyFontSize);
        _relativeTimeTimer?.Stop();
        _relativeTimeTimer = InboxRelativeTimePresenter.Attach(doc);
        _bodyViewer.Document = doc;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindScrollViewer(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static void OnFlowDocumentCopying(object sender, DataObjectCopyingEventArgs e)
    {
        if (sender is not FlowDocumentScrollViewer viewer)
            return;

        var selection = viewer.Selection;
        if (selection.IsEmpty)
            return;

        // Extract plain text from the selection. The default TextRange.Text property
        // properly handles all inlines within the range, including those in Paragraphs
        // with background colors (code blocks).
        try
        {
            var plainText = selection.Text;
            if (!string.IsNullOrEmpty(plainText))
            {
                Clipboard.SetText(plainText);
                e.CancelCommand(); // Prevent the default copy operation
            }
        }
        catch
        {
            // Clipboard contention — let default behavior proceed
        }
    }

    private static Button BuildActionButton(
        InboxAction action,
        InboxMessage msg,
        double fontSize,
        Action<InboxAction, InboxMessage> onActionClicked)
    {
        var isDraft = string.Equals(action.RouteMode, "draft", StringComparison.OrdinalIgnoreCase);
        var btn = new Button
        {
            Content         = isDraft ? $"✏️ {action.Label}" : action.Label,
            Margin          = new Thickness(0, 0, 8, 8),
            Padding         = new Thickness(10, 4, 10, 4),
            BorderThickness = new Thickness(1),
            Cursor          = Cursors.Hand,
            MinHeight       = 28,
            FontSize        = fontSize,
        };
        if (Application.Current.TryFindResource("QuickReplyButtonStyle") is Style qrStyle)
            btn.Style = qrStyle;
        btn.SetResourceReference(Button.BackgroundProperty,   "QuickReplySurface");
        btn.SetResourceReference(Button.ForegroundProperty,   "QuickReplyText");
        btn.SetResourceReference(Button.BorderBrushProperty,  "QuickReplyBorder");

        // Show hint as a tooltip. For routeMode "done" with no hint, use a sensible default.
        var hint = action.Hint;
        if (string.IsNullOrWhiteSpace(hint) &&
            string.Equals(action.RouteMode, "done", StringComparison.OrdinalIgnoreCase))
            hint = "Acknowledge — no action will be taken";
        if (!string.IsNullOrWhiteSpace(hint))
            btn.ToolTip = ToolTipHelper.MakeThemedToolTip(hint);

        bool alreadyUsed = msg.UsedActions.Contains(action.Label);
        if (alreadyUsed)
            btn.IsEnabled = false;

        btn.Click += (_, _) =>
        {
            // Host-owned plan actions close/archive the message on success. Keep them enabled
            // when validation or branch setup fails so the user can correct the issue and retry.
            if (!string.Equals(action.RouteMode, DecomposePlanInbox.ActionRouteMode, StringComparison.OrdinalIgnoreCase))
                btn.IsEnabled = false;
            onActionClicked(action, msg);
        };

        return btn;
    }

    private static string GetPriorityLabel(string emoji) => emoji switch {
        "🔴" => "High Priority",
        "🟡" => "Mid Priority",
        "🟢" => "Low Priority",
        _    => "Unknown Priority",
    };

    private static UIElement BuildAttachmentChip(
        InboxAttachment att,
        Window? owner,
        double fontSize,
        Func<string, TaskItem?>? lookupTask = null,
        Action<InboxAttachment>? openDecomposePlan = null)
    {
        var icon = att.Type switch
        {
            "url"      => "🔗",
            "file"     => "📄",
            "image"    => "🖼",
            "task-ref" => "✅",
            "text"     => "📝",
            "decompose-plan" => "🗺",
            _          => "📎",
        };

        var chip = new Border
        {
            Margin          = new Thickness(0, 0, 4, 4),
            Padding         = new Thickness(6, 2, 6, 2),
            CornerRadius    = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Cursor          = Cursors.Hand,
        };
        chip.SetResourceReference(Border.BackgroundProperty,  "InputSurface");
        chip.SetResourceReference(Border.BorderBrushProperty, "InputBorder");

        var label = new TextBlock
        {
            Text         = $"{icon} {att.Label}",
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth     = 160,
            FontSize     = fontSize,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        chip.Child = label;

        switch (att.Type)
        {
            case "decompose-plan":
                if (openDecomposePlan is not null)
                {
                    chip.ToolTip = ToolTipHelper.MakeThemedToolTip("Open the task dependency graph");
                    chip.MouseLeftButtonUp += (_, _) => openDecomposePlan(att);
                }
                break;

            case "url":
                if (att.Href is not null)
                    chip.MouseLeftButtonUp += (_, _) =>
                    {
                        try { Process.Start(new ProcessStartInfo(att.Href) { UseShellExecute = true }); }
                        catch (Exception ex)
                        {
                            SquadDashTrace.Write("Shell", $"Open failed: {ex.Message}");
                            UIErrorHelper.ShowWarning("Open Failed", $"Could not open:\n{ex.Message}");
                        }
                    };
                break;

            case "file":
            {
                var resolved = System.IO.Path.GetFullPath(att.Path!);
                chip.MouseLeftButtonUp += (_, _) =>
                {
                    try
                    {
                        if (resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                            MarkdownDocumentWindow.Show(owner, att.Label, resolved);
                        else
                            Process.Start(new ProcessStartInfo(resolved) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        SquadDashTrace.Write("Shell", $"Open failed: {ex.Message}");
                        UIErrorHelper.ShowWarning("Open Failed", $"Could not open:\n{ex.Message}");
                    }
                };
                break;
            }

            case "image":
            {
                string? imagePath = att.Path is not null ? System.IO.Path.GetFullPath(att.Path) : null;
                string? imageHref = att.Href;
                chip.MouseLeftButtonUp += (_, _) =>
                {
                    try
                    {
                        Uri? uri = imagePath is not null ? new Uri(imagePath) :
                                   imageHref is not null ? new Uri(imageHref) : null;
                        if (uri is null) return;

                        if (imagePath is not null && !File.Exists(imagePath))
                        {
                            UIErrorHelper.ShowWarning(att.Label, $"Image not found:\n{imagePath}");
                            return;
                        }

                        var bmp = new BitmapImage(uri);
                        var img = new System.Windows.Controls.Image
                        {
                            Source  = bmp,
                            Stretch = System.Windows.Media.Stretch.Uniform,
                            Margin  = new Thickness(8),
                        };
                        var win = new Window
                        {
                            Title         = att.Label,
                            Content       = img,
                            Width         = Math.Min(bmp.PixelWidth  > 0 ? bmp.PixelWidth  + 32 : 800, SystemParameters.PrimaryScreenWidth  * 0.9),
                            Height        = Math.Min(bmp.PixelHeight > 0 ? bmp.PixelHeight + 56 : 600, SystemParameters.PrimaryScreenHeight * 0.9),
                            Owner         = owner,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        };
                        win.Show();
                    }
                    catch (Exception ex)
                    {
                        UIErrorHelper.ShowError(att.Label, ex.Message);
                    }
                };
                break;
            }

            case "task-ref":
            {
                chip.ToolTip = ToolTipHelper.MakeThemedToolTip($"Task: {att.TaskId}");
                chip.Cursor  = Cursors.Hand;
                if (lookupTask is not null && att.TaskId is not null)
                {
                    chip.MouseLeftButtonUp += (_, _) =>
                    {
                        try
                        {
                            var task = lookupTask(att.TaskId);
                            if (task is null)
                            {
                                MessageBox.Show($"Task not found: {att.TaskId}", att.Label,
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }

                            var status   = task.IsChecked ? "✅ Done" : "⬜ Open";
                            var priority = $"{task.Emoji} {GetPriorityLabel(task.Emoji)}";
                            var owner    = task.Owner is not null ? $"\nOwner: {task.Owner}" : "";
                            var desc     = task.Description is not null ? $"\n\n{task.Description}" : "";
                            MessageBox.Show(
                                $"{status}  |  {priority}{owner}\n\n{task.Text}{desc}",
                                att.Label,
                                MessageBoxButton.OK,
                                MessageBoxImage.None);
                        }
                        catch (Exception ex)
                        {
                            SquadDashTrace.Write("Shell", $"Open failed: {ex.Message}");
                            UIErrorHelper.ShowWarning("Open Failed", $"Could not open:\n{ex.Message}");
                        }
                    };
                }
                break;
            }

            case "text":
                chip.MouseLeftButtonUp += (_, _) =>
                {
                    var excerptText = att.Content;
                    SquadDashTrace.Write(TraceCategory.Inbox, $"InboxMessageWindow.AttachmentChip.Click: type=text label='{att.Label}' contentLen={excerptText?.Length ?? 0} excerptPreview='{excerptText?[..Math.Min(80, excerptText?.Length ?? 0)]}'");
                    if (!string.IsNullOrWhiteSpace(excerptText) && owner is InboxMessageWindow inboxWin)
                    {
                        SquadDashTrace.Write(TraceCategory.Inbox, "InboxMessageWindow.AttachmentChip.Click: owner is InboxMessageWindow — calling SelectAndScrollToText");
                        try
                        {
                            if (inboxWin.SelectAndScrollToText(excerptText))
                                return;

                            SquadDashTrace.Write(TraceCategory.Inbox, "InboxMessageWindow.AttachmentChip.Click: text not found in body — opening inline content viewer");
                            MarkdownDocumentWindow.ShowContent(owner, att.Label, excerptText);
                        }
                        catch (Exception ex)
                        {
                            SquadDashTrace.Write(TraceCategory.Inbox, $"InboxMessageWindow.AttachmentChip.Click: text attachment open threw: {ex.Message}");
                            UIErrorHelper.ShowWarning("Open Failed", $"Could not open:\n{ex.Message}");
                        }
                    }
                    else
                    {
                        SquadDashTrace.Write(TraceCategory.Inbox, $"InboxMessageWindow.AttachmentChip.Click: fallback — owner is not InboxMessageWindow (type={owner?.GetType().Name ?? "null"}) or excerptText is empty — opening MarkdownDocumentWindow");
                        try { MarkdownDocumentWindow.ShowContent(owner, att.Label, excerptText ?? ""); }
                        catch (Exception ex)
                        {
                            SquadDashTrace.Write("Shell", $"Open failed: {ex.Message}");
                            UIErrorHelper.ShowWarning("Open Failed", $"Could not open:\n{ex.Message}");
                        }
                    }
                };
                break;
        }

        return chip;
    }

    /// <summary>
    /// Selects the specified text in the body viewer and scrolls it into view.
    /// Used when clicking on an inbox-excerpt attachment to highlight the referenced text.
    /// </summary>
    /// <summary>
    /// Rebuilds the FlowDocument body with fresh theme brushes.
    /// Call this whenever the application theme switches.
    /// </summary>
    public void NotifyThemeChanged()
    {
        RebuildDocument();
    }

    public bool SelectAndScrollToText(string excerptText)
    {
        SquadDashTrace.Write(TraceCategory.Inbox, $"InboxMessageWindow.SelectAndScrollToText: called for msgId={MessageId} excerptLen={excerptText.Length} excerpt='{excerptText[..Math.Min(80, excerptText.Length)]}'");

        if (string.IsNullOrWhiteSpace(excerptText))
        {
            SquadDashTrace.Write(TraceCategory.Inbox, "InboxMessageWindow.SelectAndScrollToText: EARLY EXIT — excerpt text is null or whitespace");
            return false;
        }

        var doc = _bodyViewer.Document;
        if (doc is null)
        {
            SquadDashTrace.Write(TraceCategory.Inbox, "InboxMessageWindow.SelectAndScrollToText: EARLY EXIT — _bodyViewer.Document is null");
            return false;
        }

        var debugRange = new TextRange(doc.ContentStart, doc.ContentEnd);
        var debugText  = debugRange.Text;
        var excerptFound = debugText.Contains(excerptText, StringComparison.Ordinal);
        SquadDashTrace.Write(TraceCategory.Inbox,
            $"InboxMessageWindow.SelectAndScrollToText: docTextLen={debugText.Length} excerptFoundInDoc={excerptFound} — first 200 chars of doc: '{debugText[..Math.Min(200, debugText.Length)]}'");

        var foundRange = FindTextInRange(doc.ContentStart, doc.ContentEnd, excerptText);
        if (foundRange is not null)
        {
            SquadDashTrace.Write(TraceCategory.Inbox, "InboxMessageWindow.SelectAndScrollToText: text found in document — setting selection");
            _bodyViewer.Selection.Select(foundRange.Start, foundRange.End);
            SquadDashTrace.Write(TraceCategory.Inbox, $"InboxMessageWindow.SelectAndScrollToText: selection applied — IsEmpty={_bodyViewer.Selection.IsEmpty} selText='{_bodyViewer.Selection.Text[..Math.Min(80, _bodyViewer.Selection.Text.Length)]}'");

            _bodyViewer.Focus();
            SquadDashTrace.Write(TraceCategory.Inbox, "InboxMessageWindow.SelectAndScrollToText: focus set on _bodyViewer — calling BringIntoView on paragraph");
            foundRange.Start.Paragraph?.BringIntoView();

            var rect = foundRange.Start.GetCharacterRect(LogicalDirection.Forward);
            if (!rect.IsEmpty)
            {
                SquadDashTrace.Write(TraceCategory.Inbox, $"InboxMessageWindow.SelectAndScrollToText: character rect found ({rect.X:F0},{rect.Y:F0}) — calling _bodyViewer.BringIntoView(rect)");
                _bodyViewer.BringIntoView(rect);
            }
            else
            {
                SquadDashTrace.Write(TraceCategory.Inbox, "InboxMessageWindow.SelectAndScrollToText: character rect is Empty — BringIntoView(rect) skipped");
            }
            SquadDashTrace.Write(TraceCategory.Inbox, "InboxMessageWindow.SelectAndScrollToText: scroll+select complete");
            return true;
        }
        else
        {
            SquadDashTrace.Write(TraceCategory.Inbox, $"InboxMessageWindow.SelectAndScrollToText: text NOT found in document via FindTextInRange — excerptText='{excerptText}'");
            return false;
        }
    }

    /// <summary>
    /// Searches for text within a FlowDocument range and returns the matching TextRange.
    /// Works across inline formatting boundaries (bold, italic, mixed runs) by building
    /// a flat character map over all text runs before searching.
    /// Returns null if the text is not found.
    /// </summary>
    private static TextRange? FindTextInRange(TextPointer start, TextPointer end, string searchText)
    {
        if (string.IsNullOrEmpty(searchText))
            return null;

        // Collect all text runs with their start pointers.
        var runs = new List<(TextPointer RunStart, string Text)>();
        var current = start;
        while (current is not null && current.CompareTo(end) < 0)
        {
            if (current.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var text = current.GetTextInRun(LogicalDirection.Forward);
                if (text.Length > 0)
                    runs.Add((current, text));
            }
            current = current.GetNextContextPosition(LogicalDirection.Forward);
        }

        // Build a flat string and a parallel map from string-index → (runIndex, charOffset).
        var sb     = new System.Text.StringBuilder();
        var posMap = new List<(int RunIndex, int CharOffset)>();
        for (int r = 0; r < runs.Count; r++)
        {
            var text = runs[r].Text;
            for (int c = 0; c < text.Length; c++)
            {
                posMap.Add((r, c));
                sb.Append(text[c]);
            }
        }

        var fullText = sb.ToString();

        // Try exact match first; fall back to case-insensitive.
        int idx = fullText.IndexOf(searchText, StringComparison.Ordinal);
        if (idx < 0)
            idx = fullText.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);

        if (idx < 0)
            return null;

        int endCharIdx = idx + searchText.Length - 1;
        if (idx >= posMap.Count || endCharIdx >= posMap.Count)
            return null;

        var (startRunIdx, startCharOff) = posMap[idx];
        var (endRunIdx,   endCharOff)   = posMap[endCharIdx];

        var matchStart = runs[startRunIdx].RunStart.GetPositionAtOffset(startCharOff);
        var matchEnd   = runs[endRunIdx].RunStart.GetPositionAtOffset(endCharOff + 1);

        if (matchStart is null || matchEnd is null)
            return null;

        return new TextRange(matchStart, matchEnd);
    }

    // ── Approval update flow ─────────────────────────────────────────────────

    private Border? _approvalUpdatingOverlay;
    private TextBlock? _approvalUpdatingSpinner;

    internal bool IsApprovalUpdateInProgress =>
        _approvalUpdatingOverlay?.Visibility == Visibility.Visible;

    /// <summary>
    /// Shows a compact spinner overlay and disables all action buttons.
    /// Call when the host begins updating the approval message content.
    /// </summary>
    internal void BeginApprovalUpdate()
    {
        foreach (var child in _actionsPanel.Children)
        {
            if (child is Button btn)
                btn.IsEnabled = false;
        }

        if (_approvalUpdatingOverlay is null)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 6, 8, 6),
            };
            var spinner = new TextBlock
            {
                Text = "⟳",
                FontSize = _bodyFontSize + 2,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(),
            };
            _approvalUpdatingSpinner = spinner;
            spinner.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            panel.Children.Add(spinner);
            var label = new TextBlock
            {
                Text = "Updating approval request…",
                FontSize = _bodyFontSize,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            panel.Children.Add(label);

            _approvalUpdatingOverlay = new Border
            {
                Child = panel,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(12, 4, 12, 4),
            };
            _approvalUpdatingOverlay.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        }

        _approvalUpdatingOverlay.Visibility = Visibility.Visible;
        AttachApprovalUpdatingOverlay(_rootGrid, _approvalUpdatingOverlay);
        if (_approvalUpdatingSpinner?.RenderTransform is RotateTransform rotation)
        {
            rotation.BeginAnimation(
                RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(850))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                });
        }
    }

    /// <summary>
    /// Places the approval-update overlay in the message layout's action row. The Window content
    /// is a chrome overlay Grid, so callers must use the retained message-layout root rather than
    /// infer its type by walking or casting <see cref="ContentControl.Content"/>.
    /// </summary>
    internal static void AttachApprovalUpdatingOverlay(Grid messageLayoutRoot, Border overlay)
    {
        ArgumentNullException.ThrowIfNull(messageLayoutRoot);
        ArgumentNullException.ThrowIfNull(overlay);
        if (messageLayoutRoot.Children.Contains(overlay))
            return;

        Grid.SetRow(overlay, 2);
        messageLayoutRoot.Children.Add(overlay);
    }

    /// <summary>Keeps Inbox actions and attachment labels aligned with the message body zoom.</summary>
    internal static void ApplyInteractiveFontSize(
        WrapPanel actionsPanel,
        WrapPanel attachmentsPanel,
        double fontSize)
    {
        foreach (var button in actionsPanel.Children.OfType<Button>())
            button.FontSize = fontSize;
        foreach (var label in attachmentsPanel.Children
                     .OfType<Border>()
                     .Select(border => border.Child)
                     .OfType<TextBlock>())
            label.FontSize = fontSize;
    }

    /// <summary>
    /// Replaces the body content and action buttons after an atomic update.
    /// Hides the spinner overlay and re-enables actions.
    /// </summary>
    internal void CompleteApprovalUpdate(
        InboxMessage updatedMessage,
        Action<InboxAction, InboxMessage> onActionClicked)
    {
        // Replace body document
        var doc = MarkdownFlowDocumentBuilder.Build(updatedMessage.Body ?? string.Empty, _bodyFontSize);
        _relativeTimeTimer?.Stop();
        _relativeTimeTimer = InboxRelativeTimePresenter.Attach(doc);
        _bodyViewer.Document = doc;

        // Replace action buttons
        _actionsPanel.Children.Clear();
        foreach (var action in updatedMessage.Actions)
            _actionsPanel.Children.Add(BuildActionButton(action, updatedMessage, _bodyFontSize, onActionClicked));
        _actionsPanel.Visibility = updatedMessage.Actions is { Count: > 0 }
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Hide overlay
        if (_approvalUpdatingOverlay is not null)
            _approvalUpdatingOverlay.Visibility = Visibility.Collapsed;
        if (_approvalUpdatingSpinner?.RenderTransform is RotateTransform rotation)
            rotation.BeginAnimation(RotateTransform.AngleProperty, null);

        Title = updatedMessage.Subject;
    }
}
