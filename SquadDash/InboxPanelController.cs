namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

/// <summary>Manages content in the inline Inbox panel.</summary>
internal sealed class InboxPanelController
{
    private readonly StackPanel                _listPanel;
    private readonly FrameworkElement          _listScrollContainer;
    private readonly Border                    _viewerBorder;
    private readonly TextBlock                 _viewerSubjectLabel;
    private readonly TextBlock                 _viewerMetaLabel;
    private readonly WrapPanel                 _viewerAttachmentsPanel;
    private readonly WrapPanel                 _viewerActionsPanel;
    private readonly FlowDocumentScrollViewer  _viewerBody;
    private readonly Action<string>            _markRead;
    private readonly Action<string>            _markUnread;
    private readonly Action<string>            _archive;
    private readonly Action<string>            _delete;
    private readonly Action<InboxAction, InboxMessage> _onActionClicked;
    private readonly Action<InboxMessage>      _openMessageWindow;
    private readonly Action<InboxMessage>?    _addToChat;
    private Func<string, TaskItem?>?          _lookupTask;

    private List<InboxMessage> _messages      = [];
    private string             _filterText    = string.Empty;
    private bool               _unreadOnly    = false;
    private InboxMessage?      _selectedMessage;

    // ── Construction ─────────────────────────────────────────────────────────

    public InboxPanelController(
        StackPanel               listPanel,
        FrameworkElement         listScrollContainer,
        Border                   viewerBorder,
        TextBlock                viewerSubjectLabel,
        TextBlock                viewerMetaLabel,
        WrapPanel                viewerAttachmentsPanel,
        FlowDocumentScrollViewer viewerBody,
        Action<string>           markRead,
        Action<string>           markUnread,
        Action<string>           archive,
        Action<string>           delete,
        WrapPanel                viewerActionsPanel,
        Action<InboxAction, InboxMessage> onActionClicked,
        Action<InboxMessage>     openMessageWindow,
        Func<string, TaskItem?>? lookupTask = null,
        Action<InboxMessage>?    addToChat  = null)
    {
        _listPanel              = listPanel;
        _listScrollContainer    = listScrollContainer;
        _viewerBorder           = viewerBorder;
        _viewerSubjectLabel     = viewerSubjectLabel;
        _viewerMetaLabel        = viewerMetaLabel;
        _viewerAttachmentsPanel = viewerAttachmentsPanel;
        _viewerBody             = viewerBody;
        _markRead               = markRead;
        _markUnread             = markUnread;
        _archive                = archive;
        _delete                 = delete;
        _viewerActionsPanel     = viewerActionsPanel;
        _onActionClicked        = onActionClicked;
        _openMessageWindow      = openMessageWindow;
        _addToChat              = addToChat;
        _lookupTask             = lookupTask;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Refresh(IReadOnlyList<InboxMessage> messages)
    {
        _messages = [.. messages];
        _selectedMessage = null;
        _viewerBorder.Visibility = Visibility.Collapsed;
        RebuildList();
    }

    public void SetFilter(string text)
    {
        _filterText = text.Trim();
        ApplyFilter();
    }

    public void SetUnreadOnly(bool unreadOnly)
    {
        _unreadOnly = unreadOnly;
        ApplyFilter();
    }

    // ── List construction ────────────────────────────────────────────────────

    private void RebuildList()
    {
        _listPanel.Children.Clear();

        var sorted = _messages.OrderByDescending(m => m.Timestamp).ToList();

        if (sorted.Count == 0)
        {
            _listPanel.Children.Add(BuildEmptyLabel("No messages"));
            return;
        }

        foreach (var msg in sorted)
            _listPanel.Children.Add(BuildRow(msg));

        ApplyFilter();
    }

    private bool MatchesFilter(InboxMessage msg)
    {
        if (string.IsNullOrEmpty(_filterText))
            return true;

        // Parse a leading @handle token from the filter text.
        if (_filterText.StartsWith('@'))
        {
            var spaceIdx = _filterText.IndexOf(' ', 1);
            string handle  = spaceIdx > 0 ? _filterText[1..spaceIdx] : _filterText[1..];
            string remaining = spaceIdx > 0 ? _filterText[(spaceIdx + 1)..].Trim() : string.Empty;

            if (string.IsNullOrEmpty(handle))
                return PanelFilterHelper.Matches(msg.Subject, remaining);

            bool agentMatch = msg.From.Contains(handle, StringComparison.OrdinalIgnoreCase)
                           || (msg.Body ?? string.Empty).Contains("@" + handle, StringComparison.OrdinalIgnoreCase);

            return agentMatch && (string.IsNullOrEmpty(remaining) || PanelFilterHelper.Matches(msg.Subject, remaining));
        }

        return PanelFilterHelper.Matches(msg.Subject, _filterText);
    }

    private void ApplyFilter()
    {
        bool anyVisible = false;

        // First pass: show/hide rows based on filter.
        foreach (UIElement child in _listPanel.Children)
        {
            if (child is Border { Tag: InboxMessage msg })
            {
                bool visible = MatchesFilter(msg) && (!_unreadOnly || !msg.Read);
                child.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (visible) anyVisible = true;
            }
        }

        // Show or hide the empty state label.
        bool emptyLabelPresent = false;
        foreach (UIElement child in _listPanel.Children)
        {
            if (child is TextBlock { Tag: string tag } tb && tag == "empty")
            {
                if (!anyVisible)
                {
                    tb.Text = _unreadOnly ? "No unread messages" : "No messages";
                    tb.Visibility = Visibility.Visible;
                }
                else
                {
                    tb.Visibility = Visibility.Collapsed;
                }
                emptyLabelPresent = true;
            }
        }

        if (!anyVisible && !emptyLabelPresent)
            _listPanel.Children.Add(BuildEmptyLabel(_unreadOnly ? "No unread messages" : "No messages"));
    }

    private UIElement BuildEmptyLabel(string text)
    {
        var tb = new TextBlock
        {
            Text         = text,
            Tag          = "empty",
            FontStyle    = FontStyles.Italic,
            Margin       = new Thickness(4, 6, 4, 4),
            TextWrapping = TextWrapping.Wrap,
        };
        tb.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        tb.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        return tb;
    }

    private Border BuildRow(InboxMessage msg)
    {
        var row = new Border
        {
            Background = Brushes.Transparent,
            Tag        = msg,
            Cursor     = Cursors.Hand,
            Padding    = new Thickness(4, 5, 4, 5),
            Opacity    = msg.Read ? 0.6 : 1.0,
        };

        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverSurface");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

        var rowStack = new StackPanel { Orientation = Orientation.Vertical };

        // ── Subject row: unread dot + subject text ────────────────────────────
        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var dot = new Ellipse
        {
            Width             = 7,
            Height            = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 5, 0),
            Visibility        = msg.Read ? Visibility.Hidden : Visibility.Visible,
        };
        dot.SetResourceReference(Ellipse.FillProperty, "ActionLinkText");

        var subjectLabel = new TextBlock
        {
            Text             = msg.Subject,
            FontWeight       = msg.Read ? FontWeights.Normal : FontWeights.SemiBold,
            TextTrimming     = TextTrimming.CharacterEllipsis,
            MaxWidth         = 220,
            VerticalAlignment = VerticalAlignment.Center,
        };
        subjectLabel.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        subjectLabel.SetResourceReference(TextBlock.ForegroundProperty, msg.Read ? "SubtleText" : "LabelText");

        headerRow.Children.Add(dot);
        headerRow.Children.Add(subjectLabel);
        rowStack.Children.Add(headerRow);

        // ── Meta row: from · relative timestamp ───────────────────────────────
        var shortTime = FormatShortRelativeTimestamp(msg.Timestamp);
        var metaLabel = new TextBlock
        {
            Text         = $"{msg.From} · {shortTime}",
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth     = 230,
            Margin       = new Thickness(12, 1, 0, 0),
        };
        metaLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
        metaLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        rowStack.Children.Add(metaLabel);

        row.Child = rowStack;

        row.MouseLeftButtonUp += (_, _) => SelectMessage(msg, row, dot, subjectLabel);
        row.ContextMenu        = BuildRowContextMenu(msg, row, dot, subjectLabel);

        return row;
    }

    private static string FormatShortRelativeTimestamp(DateTimeOffset ts)
    {
        var elapsed = DateTimeOffset.Now - ts;
        if (elapsed.TotalMinutes < 1)  return "just now";
        if (elapsed.TotalHours   < 1)  return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalDays    < 1)  return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays    < 7)  return $"{(int)elapsed.TotalDays}d ago";
        return ts.LocalDateTime.ToString("MMM d");
    }

    // ── Message selection ─────────────────────────────────────────────────────

    private void SelectMessage(InboxMessage msg, Border row, Ellipse dot, TextBlock subjectLabel)
    {
        _selectedMessage = msg;

        if (!msg.Read)
            MarkRowRead(msg, row, dot, subjectLabel);

        _openMessageWindow(msg);
    }

    private void MarkRowRead(InboxMessage msg, Border row, Ellipse dot, TextBlock subjectLabel)
    {
        msg.Read = true;
        _markRead(msg.Id);
        row.Opacity            = 0.6;
        dot.Visibility         = Visibility.Hidden;
        subjectLabel.FontWeight = FontWeights.Normal;
        subjectLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
    }

    private void MarkRowUnread(InboxMessage msg, Border row, Ellipse dot, TextBlock subjectLabel)
    {
        msg.Read = false;
        _markUnread(msg.Id);
        row.Opacity            = 1.0;
        dot.Visibility         = Visibility.Visible;
        subjectLabel.FontWeight = FontWeights.SemiBold;
        subjectLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
    }

    // ── Message viewer ────────────────────────────────────────────────────────

    private void ShowViewer(InboxMessage msg)
    {
        _viewerSubjectLabel.Text = msg.Subject;

        var ts = StatusTimingPresentation.FormatRelativeTimestamp(msg.Timestamp);
        _viewerMetaLabel.Text = $"{msg.From} · {ts}";

        // Attachments
        _viewerAttachmentsPanel.Children.Clear();
        foreach (var att in msg.Attachments)
            _viewerAttachmentsPanel.Children.Add(BuildAttachmentChip(att, Application.Current?.MainWindow, _lookupTask));
        _viewerAttachmentsPanel.Visibility = msg.Attachments.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;

        // Actions (deferred quick-reply buttons)
        _viewerActionsPanel.Children.Clear();
        bool hasActions = msg.Actions is { Count: > 0 };
        _viewerActionsPanel.Visibility = hasActions ? Visibility.Visible : Visibility.Collapsed;
        if (hasActions)
        {
            foreach (var action in msg.Actions)
                _viewerActionsPanel.Children.Add(BuildActionButton(action, msg));
        }

        // Markdown body
        _viewerBody.Document = MarkdownFlowDocumentBuilder.Build(msg.Body ?? string.Empty);

        _viewerBorder.Visibility = Visibility.Visible;
    }

    private Button BuildActionButton(InboxAction action, InboxMessage msg)
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

        bool alreadyUsed = msg.UsedActions.Contains(action.Label);
        if (alreadyUsed)
            btn.IsEnabled = false;

        btn.Click += (_, _) =>
        {
            btn.IsEnabled = false;
            _onActionClicked(action, msg);
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
        chip.SetResourceReference(Border.BackgroundProperty,   "InputSurface");
        chip.SetResourceReference(Border.BorderBrushProperty,  "InputBorder");

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

    // ── Context menu ──────────────────────────────────────────────────────────

    private ContextMenu BuildRowContextMenu(InboxMessage msg, Border row, Ellipse dot, TextBlock subjectLabel)
    {
        var menu = MakeMenu();

        if (_addToChat is not null)
        {
            var addToChatItem = MakeItem("Add to chat");
            addToChatItem.Click += (_, _) => _addToChat(msg);
            menu.Items.Add(addToChatItem);
            menu.Items.Add(MakeSep());
        }

        if (msg.Read)
        {
            var markUnreadItem = MakeItem("Mark as unread");
            markUnreadItem.Click += (_, _) =>
            {
                MarkRowUnread(msg, row, dot, subjectLabel);
            };
            menu.Items.Add(markUnreadItem);
        }
        else
        {
            var markReadItem = MakeItem("Mark as read");
            markReadItem.Click += (_, _) =>
            {
                MarkRowRead(msg, row, dot, subjectLabel);
            };
            menu.Items.Add(markReadItem);
        }

        menu.Items.Add(MakeSep());

        var archiveItem = MakeItem("Archive");
        archiveItem.Click += (_, _) =>
        {
            _archive(msg.Id);
            RemoveRow(row);
        };
        menu.Items.Add(archiveItem);

        var deleteItem = MakeItem("Delete");
        deleteItem.Click += (_, _) =>
        {
            _delete(msg.Id);
            RemoveRow(row);
        };
        menu.Items.Add(deleteItem);

        return menu;
    }

    private void RemoveRow(Border row)
    {
        if (row.Tag is InboxMessage removed
            && _selectedMessage is not null
            && _selectedMessage.Id == removed.Id)
        {
            _viewerBorder.Visibility = Visibility.Collapsed;
            _selectedMessage = null;
        }

        _listPanel.Children.Remove(row);

        // Check whether any message row is still visible.
        bool anyVisible = false;
        foreach (UIElement child in _listPanel.Children)
        {
            if (child is Border { Visibility: Visibility.Visible, Tag: InboxMessage })
            {
                anyVisible = true;
                break;
            }
        }

        if (!anyVisible)
        {
            bool hasEmpty = false;
            foreach (UIElement child in _listPanel.Children)
            {
                if (child is TextBlock { Tag: string t } && t == "empty")
                {
                    hasEmpty = true;
                    break;
                }
            }
            if (!hasEmpty)
                _listPanel.Children.Add(BuildEmptyLabel("No messages"));
        }
    }

    // ── Menu helpers ──────────────────────────────────────────────────────────

    private static ContextMenu MakeMenu()
    {
        var m = new ContextMenu();
        m.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");
        return m;
    }

    private static MenuItem MakeItem(string header)
    {
        var i = new MenuItem { Header = header };
        i.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        return i;
    }

    private static Separator MakeSep()
    {
        var s = new Separator();
        s.SetResourceReference(Separator.StyleProperty, "ThemedMenuSeparatorStyle");
        return s;
    }
}
