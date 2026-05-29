using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SquadDash;

/// <summary>Modeless pop-up window that displays a single <see cref="InboxMessage"/>.</summary>
internal sealed class InboxMessageWindow : ChromedWindow
{
    public string MessageId { get; }

    private readonly Func<string, TaskItem?>? _lookupTask;
    private readonly Action<string, InboxMessage>? _attachSelectedTextToChat;
    private readonly InboxMessage _message;
    private readonly FlowDocumentScrollViewer _bodyViewer;

    public InboxMessageWindow(
        InboxMessage message,
        Action<InboxAction, InboxMessage> onActionClicked,
        Func<string, TaskItem?>? lookupTask = null,
        Action<string, InboxMessage>? attachSelectedTextToChat = null)
        : base(captionHeight: 28, resizeMode: ResizeMode.CanResize)
    {
        _lookupTask             = lookupTask;
        _attachSelectedTextToChat = attachSelectedTextToChat;
        _message                = message;
        MessageId               = message.Id;
        Title                   = message.Subject;
        SizeToContent           = SizeToContent.Manual;
        Width                   = 750;
        Height                  = 550;
        MinWidth                = 400;
        MinHeight               = 300;
        Topmost                 = false;
        WindowStartupLocation   = WindowStartupLocation.CenterOwner;
        ShowInTaskbar           = true;

        SquadDashTrace.Write(TraceCategory.Inbox, $"InboxMessageWindow.ctor: msgId={message.Id} subject='{message.Subject}' attachments={message.Attachments.Count} actions={message.Actions.Count}");

        // Root grid: header / attachments / actions / body
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 0 header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 1 attachments
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 2 actions
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3 body
        var outerBorder = ApplyOuterBorder();
        outerBorder.Child = root;

        // ── Header ────────────────────────────────────────────────────────────
        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin      = new Thickness(12, 10, 12, 6),
        };
        Grid.SetRow(headerPanel, 0);
        root.Children.Add(headerPanel);

        var subjectLabel = new TextBlock
        {
            Text       = message.Subject,
            FontWeight = FontWeights.Bold,
            FontSize   = 14,
            TextWrapping = TextWrapping.Wrap,
        };
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
        var attachmentsPanel = new WrapPanel
        {
            Margin      = new Thickness(12, 4, 12, 0),
            Orientation = Orientation.Horizontal,
            Visibility  = message.Attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        Grid.SetRow(attachmentsPanel, 1);
        root.Children.Add(attachmentsPanel);

        foreach (var att in message.Attachments)
            attachmentsPanel.Children.Add(BuildAttachmentChip(att, this, _lookupTask));

        // ── Actions ───────────────────────────────────────────────────────────
        var actionsPanel = new WrapPanel
        {
            Margin      = new Thickness(12, 4, 12, 0),
            Orientation = Orientation.Horizontal,
            Visibility  = message.Actions is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed,
        };
        Grid.SetRow(actionsPanel, 2);
        root.Children.Add(actionsPanel);

        foreach (var action in message.Actions)
            actionsPanel.Children.Add(BuildActionButton(action, message, onActionClicked));

        // ── Body ──────────────────────────────────────────────────────────────
        var doc = MarkdownFlowDocumentBuilder.Build(message.Body ?? string.Empty);

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
        };

        // Fix for code block copying: FlowDocument's default copy handler can skip
        // Paragraph elements with backgrounds (code blocks). Intercept the copy event
        // to extract plain text from the selection, preserving all content.
        DataObject.AddCopyingHandler(_bodyViewer, OnFlowDocumentCopying);

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

            // Set up the context menu after OnApplyTemplate so our assignment wins
            // over any default ContextMenu the FlowDocumentScrollViewer installs.
            if (_attachSelectedTextToChat is not null)
            {
                var contextMenu = new ContextMenu();
                contextMenu.Style = (Style)Application.Current.Resources["ThemedContextMenuStyle"];

                var attachMenuItem = new MenuItem { Header = "Add to Chat" };
                attachMenuItem.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                attachMenuItem.Click += (_, _) =>
                {
                    var sel = _bodyViewer.Selection;
                    if (!sel.IsEmpty)
                        _attachSelectedTextToChat(sel.Text, _message);
                };
                contextMenu.Items.Add(attachMenuItem);

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
        };
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
        Action<InboxAction, InboxMessage> onActionClicked)
    {
        var btn = new Button
        {
            Content         = action.Label,
            Margin          = new Thickness(0, 0, 8, 8),
            Padding         = new Thickness(10, 4, 10, 4),
            BorderThickness = new Thickness(1),
            Cursor          = Cursors.Hand,
            MinHeight       = 28,
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
            btn.ToolTip = hint;

        bool alreadyUsed = msg.UsedActions.Contains(action.Label);
        if (alreadyUsed)
            btn.IsEnabled = false;

        btn.Click += (_, _) =>
        {
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

    private static UIElement BuildAttachmentChip(InboxAttachment att, Window? owner, Func<string, TaskItem?>? lookupTask = null)
    {
        var icon = att.Type switch
        {
            "url"      => "🔗",
            "file"     => "📄",
            "image"    => "🖼",
            "task-ref" => "✅",
            "text"     => "📝",
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
        };
        label.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
        label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        chip.Child = label;

        switch (att.Type)
        {
            case "url":
                if (att.Href is not null)
                    chip.MouseLeftButtonUp += (_, _) =>
                    {
                        try { Process.Start(new ProcessStartInfo(att.Href) { UseShellExecute = true }); }
                        catch { }
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
                    catch { }
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
                            MessageBox.Show($"Image not found:\n{imagePath}", att.Label, MessageBoxButton.OK, MessageBoxImage.Warning);
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
                        MessageBox.Show(ex.Message, att.Label, MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                break;
            }

            case "task-ref":
            {
                chip.ToolTip = $"Task: {att.TaskId}";
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
                        catch { }
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
                        try { inboxWin.SelectAndScrollToText(excerptText); }
                        catch (Exception ex) { SquadDashTrace.Write(TraceCategory.Inbox, $"InboxMessageWindow.AttachmentChip.Click: SelectAndScrollToText threw: {ex.Message}"); }
                    }
                    else
                    {
                        SquadDashTrace.Write(TraceCategory.Inbox, $"InboxMessageWindow.AttachmentChip.Click: fallback — owner is not InboxMessageWindow (type={owner?.GetType().Name ?? "null"}) or excerptText is empty — opening MarkdownDocumentWindow");
                        try { MarkdownDocumentWindow.ShowContent(owner, att.Label, excerptText ?? ""); }
                        catch { }
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
    public void SelectAndScrollToText(string excerptText)
    {
        SquadDashTrace.Write(TraceCategory.Inbox, $"InboxMessageWindow.SelectAndScrollToText: called for msgId={MessageId} excerptLen={excerptText.Length} excerpt='{excerptText[..Math.Min(80, excerptText.Length)]}'");

        if (string.IsNullOrWhiteSpace(excerptText))
        {
            SquadDashTrace.Write(TraceCategory.Inbox, "InboxMessageWindow.SelectAndScrollToText: EARLY EXIT — excerpt text is null or whitespace");
            return;
        }

        var doc = _bodyViewer.Document;
        if (doc is null)
        {
            SquadDashTrace.Write(TraceCategory.Inbox, "InboxMessageWindow.SelectAndScrollToText: EARLY EXIT — _bodyViewer.Document is null");
            return;
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
        }
        else
        {
            SquadDashTrace.Write(TraceCategory.Inbox, $"InboxMessageWindow.SelectAndScrollToText: text NOT found in document via FindTextInRange — excerptText='{excerptText}'");
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
}
