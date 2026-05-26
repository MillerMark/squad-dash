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
        Width                   = 640;
        Height                  = 520;
        MinWidth                = 400;
        MinHeight               = 300;
        Topmost                 = false;
        WindowStartupLocation   = WindowStartupLocation.CenterOwner;
        ShowInTaskbar           = true;

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

        // Add context menu for "Add to Chat" on text selection
        if (_attachSelectedTextToChat is not null)
        {
            var contextMenu = new ContextMenu();
            
            // Apply themed context menu style
            contextMenu.Style = (Style)Application.Current.Resources["ThemedContextMenuStyle"];
            
            var attachMenuItem = new MenuItem { Header = "Add to Chat" };
            attachMenuItem.Click += (_, _) =>
            {
                var selection = _bodyViewer.Selection;
                if (!selection.IsEmpty)
                {
                    var selectedText = selection.Text;
                    _attachSelectedTextToChat(selectedText, _message);
                }
            };
            contextMenu.Items.Add(attachMenuItem);

            // Explicitly set the custom context menu and suppress default behavior
            _bodyViewer.ContextMenu = contextMenu;
            _bodyViewer.ContextMenuOpening += (_, e) =>
            {
                // If no text is selected, cancel the context menu entirely
                if (_bodyViewer.Selection.IsEmpty)
                {
                    e.Handled = true;
                }
                // Otherwise, ensure our custom menu is shown
            };
        }

        var bodyBorder = new Border
        {
            Margin = new Thickness(8, 6, 8, 8),
            Child  = _bodyViewer,
        };
        bodyBorder.SetResourceReference(Border.BackgroundProperty, "InboxBodySurface");
        Grid.SetRow(bodyBorder, 3);
        root.Children.Add(bodyBorder);
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
                    try { MarkdownDocumentWindow.ShowContent(owner, att.Label, att.Content ?? ""); }
                    catch { }
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
        var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "squaddash-excerpt-debug.log");
        void Log(string msg)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logMsg = $"{timestamp} {msg}\n";
            System.IO.File.AppendAllText(logPath, logMsg);
            System.Diagnostics.Trace.WriteLine(msg);
        }
        
        Log($"[SelectAndScroll] Called with text: '{excerptText}'");
        
        if (string.IsNullOrWhiteSpace(excerptText))
        {
            Log($"[SelectAndScroll] Excerpt text is null or whitespace, exiting");
            return;
        }

        var doc = _bodyViewer.Document;
        if (doc is null)
        {
            Log($"[SelectAndScroll] Document is null, exiting");
            return;
        }

        Log($"[SelectAndScroll] Document found, searching for text");
        
        // DEBUG: Dump the first 500 chars of the document text
        var debugRange = new TextRange(doc.ContentStart, doc.ContentEnd);
        var debugText = debugRange.Text;
        Log($"[SelectAndScroll] Document text length: {debugText.Length}");
        Log($"[SelectAndScroll] First 500 chars: '{debugText[..Math.Min(500, debugText.Length)]}'");
        Log($"[SelectAndScroll] Looking for excerpt: '{excerptText}'");
        Log($"[SelectAndScroll] Excerpt exists in doc: {debugText.Contains(excerptText)}");
        
        // Search the document for the excerpt text
        var docStart = doc.ContentStart;
        var docEnd = doc.ContentEnd;

        // Find the text in the document
        var foundRange = FindTextInRange(docStart, docEnd, excerptText);
        if (foundRange is not null)
        {
            Log($"[SelectAndScroll] Text found! Setting selection and scrolling");
            
            // Select the found text
            _bodyViewer.Selection.Select(foundRange.Start, foundRange.End);
            
            Log($"[SelectAndScroll] Selection set, checking if selection is visible");
            Log($"[SelectAndScroll] Selection.IsEmpty: {_bodyViewer.Selection.IsEmpty}");
            Log($"[SelectAndScroll] Selection text: '{_bodyViewer.Selection.Text}'");

            // Force focus to the viewer to ensure selection is visible
            _bodyViewer.Focus();
            
            // Try multiple scrolling approaches to ensure visibility
            // Approach 1: BringIntoView on the paragraph
            foundRange.Start.Paragraph?.BringIntoView();
            
            // Approach 2: Also call BringIntoView on the actual TextPointer's rect
            var rect = foundRange.Start.GetCharacterRect(LogicalDirection.Forward);
            if (!rect.IsEmpty)
            {
                Log($"[SelectAndScroll] Character rect found, calling BringIntoView on rect");
                _bodyViewer.BringIntoView(rect);
            }
            
            Log($"[SelectAndScroll] Scrolling complete");
        }
        else
        {
            Log($"[SelectAndScroll] Text NOT found in document");
        }
    }

    /// <summary>
    /// Searches for text within a TextRange and returns the matching TextPointer range.
    /// Returns null if the text is not found.
    /// </summary>
    private static TextRange? FindTextInRange(TextPointer start, TextPointer end, string searchText)
    {
        if (string.IsNullOrEmpty(searchText))
            return null;

        var current = start;
        
        while (current is not null && current.CompareTo(end) < 0)
        {
            if (current.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var textRun = current.GetTextInRun(LogicalDirection.Forward);
                
                // Check if the search text appears in this text run
                var index = textRun.IndexOf(searchText, StringComparison.Ordinal);
                if (index >= 0)
                {
                    // Found it! Create a TextRange for the match
                    var matchStart = current.GetPositionAtOffset(index);
                    var matchEnd = matchStart?.GetPositionAtOffset(searchText.Length);
                    
                    if (matchStart is not null && matchEnd is not null)
                        return new TextRange(matchStart, matchEnd);
                }
            }
            
            current = current.GetNextContextPosition(LogicalDirection.Forward);
        }

        return null;
    }
}
