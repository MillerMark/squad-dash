namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly FlowDocumentScrollViewer  _viewerBody;
    private readonly Action<string>            _markRead;
    private readonly Action<string>            _archive;
    private readonly Action<string>            _delete;

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
        Action<string>           archive,
        Action<string>           delete)
    {
        _listPanel              = listPanel;
        _listScrollContainer    = listScrollContainer;
        _viewerBorder           = viewerBorder;
        _viewerSubjectLabel     = viewerSubjectLabel;
        _viewerMetaLabel        = viewerMetaLabel;
        _viewerAttachmentsPanel = viewerAttachmentsPanel;
        _viewerBody             = viewerBody;
        _markRead               = markRead;
        _archive                = archive;
        _delete                 = delete;
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

    private void ApplyFilter()
    {
        bool anyVisible = false;

        // First pass: show/hide rows based on filter.
        foreach (UIElement child in _listPanel.Children)
        {
            if (child is Border { Tag: InboxMessage msg })
            {
                bool visible = PanelFilterHelper.Matches(msg.Subject, _filterText)
                            && (!_unreadOnly || !msg.Read);
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

        ShowViewer(msg);
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

    // ── Message viewer ────────────────────────────────────────────────────────

    private void ShowViewer(InboxMessage msg)
    {
        _viewerSubjectLabel.Text = msg.Subject;

        var ts = StatusTimingPresentation.FormatRelativeTimestamp(msg.Timestamp);
        _viewerMetaLabel.Text = $"{msg.From} · {ts}";

        // Attachments
        _viewerAttachmentsPanel.Children.Clear();
        foreach (var att in msg.Attachments)
            _viewerAttachmentsPanel.Children.Add(BuildAttachmentChip(att));
        _viewerAttachmentsPanel.Visibility = msg.Attachments.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;

        // Markdown body
        _viewerBody.Document = MarkdownFlowDocumentBuilder.Build(msg.Body ?? string.Empty);

        _viewerBorder.Visibility = Visibility.Visible;
    }

    private static UIElement BuildAttachmentChip(InboxAttachment att)
    {
        var icon = att.Type switch
        {
            "file"  => "📄",
            "link"  => "🔗",
            "task"  => "✅",
            "image" => "🖼",
            _       => "📎",
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

        // Open file or link on click.
        var target = att.Href ?? att.Path;
        if (target is not null)
        {
            chip.MouseLeftButtonUp += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
                catch { }
            };
        }

        return chip;
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    private ContextMenu BuildRowContextMenu(InboxMessage msg, Border row, Ellipse dot, TextBlock subjectLabel)
    {
        var menu = MakeMenu();

        var markReadItem = MakeItem("Mark as read");
        markReadItem.IsEnabled = !msg.Read;
        markReadItem.Click += (_, _) =>
        {
            if (!msg.Read)
            {
                MarkRowRead(msg, row, dot, subjectLabel);
                markReadItem.IsEnabled = false;
            }
        };
        menu.Items.Add(markReadItem);

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
