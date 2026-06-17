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
using System.Globalization;
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
    private readonly Action<InboxMessage, Action?> _openMessageWindow;
    private readonly Action<InboxMessage>?    _addToChat;
    private readonly Action<InboxMessage>?    _addToNewChat;
    private Func<string, TaskItem?>?          _lookupTask;

    private readonly HashSet<string> _selectedIds = new();
    private string? _anchorId;
    private string? _lastSingleClickId;
    private bool _listHasFocus;

    private readonly InboxPanelViewModel _viewModel = new();
    internal InboxPanelViewModel ViewModel => _viewModel;

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
        Action<InboxMessage, Action?> openMessageWindow,
        Func<string, TaskItem?>? lookupTask = null,
        Action<InboxMessage>?    addToChat  = null,
        Action<InboxMessage>?    addToNewChat = null)
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
        _addToNewChat           = addToNewChat;
        _lookupTask             = lookupTask;

        _listScrollContainer.IsKeyboardFocusWithinChanged += (_, _) =>
        {
            _listHasFocus = _listScrollContainer.IsKeyboardFocusWithin;
            RefreshRowHighlights();
        };
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Refresh(IReadOnlyList<InboxMessage> messages)
    {
        _viewModel.Messages = [.. messages];
        _viewModel.SelectedMessage = null;
        _selectedIds.Clear();
        _anchorId = null;
        _viewerBorder.Visibility = Visibility.Collapsed;
        RebuildList();
    }

    public void SetFilter(string text)
    {
        _viewModel.FilterText = text.Trim();
        ApplyFilter();
    }

    public void SetUnreadOnly(bool unreadOnly)
    {
        _viewModel.UnreadOnly = unreadOnly;
        ApplyFilter();
    }

    // ── List construction ────────────────────────────────────────────────────

    private void RebuildList()
    {
        _listPanel.Children.Clear();

        var sorted = _viewModel.Messages.OrderByDescending(m => m.Timestamp).ToList();

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
        if (string.IsNullOrEmpty(_viewModel.FilterText))
            return true;

        // Parse a leading @handle token from the filter text.
        if (_viewModel.FilterText.StartsWith('@'))
        {
            var spaceIdx = _viewModel.FilterText.IndexOf(' ', 1);
            string handle  = spaceIdx > 0 ? _viewModel.FilterText[1..spaceIdx] : _viewModel.FilterText[1..];
            string remaining = spaceIdx > 0 ? _viewModel.FilterText[(spaceIdx + 1)..].Trim() : string.Empty;

            if (string.IsNullOrEmpty(handle))
                return PanelFilterHelper.Matches(msg.Subject, remaining);

            bool agentMatch = msg.From.Contains(handle, StringComparison.OrdinalIgnoreCase)
                           || (msg.Body ?? string.Empty).Contains("@" + handle, StringComparison.OrdinalIgnoreCase);

            return agentMatch && (string.IsNullOrEmpty(remaining) || PanelFilterHelper.Matches(msg.Subject, remaining));
        }

        return PanelFilterHelper.Matches(msg.Subject, _viewModel.FilterText);
    }

    private void ApplyFilter()
    {
        bool anyVisible = false;

        // First pass: show/hide rows based on filter.
        foreach (UIElement child in _listPanel.Children)
        {
            if (child is Border { Tag: InboxMessage msg })
            {
                bool visible = MatchesFilter(msg) && (!_viewModel.UnreadOnly || !msg.Read);
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
                    tb.Text = _viewModel.UnreadOnly ? "No unread messages" : "No messages";
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
            _listPanel.Children.Add(BuildEmptyLabel(_viewModel.UnreadOnly ? "No unread messages" : "No messages"));
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
            Opacity    = 1.0,
        };

        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverSurface");
        row.MouseLeave += (_, _) => RefreshRowHighlight(row, msg.Id);

        var rowStack = new StackPanel { Orientation = Orientation.Vertical };

        // ── Subject row: unread dot + subject text ────────────────────────────
        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var indicator = BuildPriorityIndicator(msg);

        var subjectLabel = new TextBlock
        {
            Text              = msg.Subject,
            FontWeight        = msg.Read ? FontWeights.Normal : FontWeights.SemiBold,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            // MaxWidth removed — panel width now controls truncation via column/splitter
            VerticalAlignment = VerticalAlignment.Center,
        };
        subjectLabel.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        subjectLabel.SetResourceReference(TextBlock.ForegroundProperty, msg.Read ? "SubtleText" : "LabelText");

        headerRow.Children.Add(indicator);
        headerRow.Children.Add(subjectLabel);
        rowStack.Children.Add(headerRow);

        row.Child = rowStack;

        // ── Hover preview: shows sender, time, and body as a popup ────────────
        if (!string.IsNullOrWhiteSpace(msg.Body))
            MarkdownHoverPopup.Attach(
                row,
                buildHeader: () => {
                    var metaText = new TextBlock {
                        Text   = $"{msg.From} · {FormatShortRelativeTimestamp(msg.Timestamp)}",
                        Margin = new Thickness(0, 0, 0, 4),
                    };
                    metaText.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
                    metaText.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                    return metaText;
                },
                getMarkdown: () => msg.Body,
                placement:   System.Windows.Controls.Primitives.PlacementMode.Left,
                maxWidth:    560);

        row.MouseLeftButtonUp += (_, _) => {
            var modifiers = Keyboard.Modifiers;
            if ((modifiers & ModifierKeys.Control) != 0)
                ToggleSelectMessage(msg);
            else if ((modifiers & ModifierKeys.Shift) != 0)
                RangeSelectMessage(msg);
            else
                SelectMessage(msg, row, indicator, subjectLabel);
        };
        row.ContextMenu         = BuildRowContextMenu(msg, row, indicator, subjectLabel);
        // Rebuild the context menu each time it opens so the read/unread item reflects current state.
        row.ContextMenuOpening += (_, _) => row.ContextMenu = _selectedIds.Count >= 2
            ? BuildMultiSelectContextMenu()
            : BuildRowContextMenu(msg, row, indicator, subjectLabel);

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

    private void SelectMessage(InboxMessage msg, Border row, UIElement indicator, TextBlock subjectLabel)
    {
        _selectedIds.Clear();
        _selectedIds.Add(msg.Id);
        _anchorId = msg.Id;
        _lastSingleClickId = msg.Id;
        RefreshRowHighlights();

        SquadDashTrace.Write(TraceCategory.Inbox, $"InboxPanelController.SelectMessage: msgId={msg.Id} subject='{msg.Subject}' read={msg.Read}");
        _viewModel.SelectedMessage = msg;

        // Defer mark-as-read: the window fires the callback after 3 s of viewing
        // or on any scroll, whichever comes first.
        Action? markReadCallback = msg.Read ? null : () => MarkRowRead(msg, row, indicator, subjectLabel);

        SquadDashTrace.Write(TraceCategory.Inbox, $"InboxPanelController.SelectMessage: calling _openMessageWindow (wired to OpenOrFocusInboxMessage) for msgId={msg.Id}");
        _openMessageWindow(msg, markReadCallback);
    }

    private void ToggleSelectMessage(InboxMessage msg)
    {
        if (!_selectedIds.Remove(msg.Id))
            _selectedIds.Add(msg.Id);
        _anchorId = msg.Id;
        RefreshRowHighlights();
    }

    private void RangeSelectMessage(InboxMessage msg)
    {
        if (_anchorId is null)
        {
            _selectedIds.Add(msg.Id);
            RefreshRowHighlights();
            return;
        }

        var orderedIds = _listPanel.Children
            .OfType<Border>()
            .Where(b => b.Tag is InboxMessage && b.Visibility == Visibility.Visible)
            .Select(b => ((InboxMessage)b.Tag!).Id)
            .ToList();

        int anchorIdx = orderedIds.IndexOf(_anchorId);
        int clickIdx  = orderedIds.IndexOf(msg.Id);

        if (anchorIdx < 0 || clickIdx < 0)
        {
            _selectedIds.Add(msg.Id);
            RefreshRowHighlights();
            return;
        }

        int from = Math.Min(anchorIdx, clickIdx);
        int to   = Math.Max(anchorIdx, clickIdx);
        for (int i = from; i <= to; i++)
            _selectedIds.Add(orderedIds[i]);

        RefreshRowHighlights();
    }

    /// <summary>Selects a single message by ID (used for back-selection when an InboxMessageWindow gains focus).</summary>
    public void SelectMessageById(string id)
    {
        _selectedIds.Clear();
        _selectedIds.Add(id);
        _anchorId = id;
        RefreshRowHighlights();
    }

    private void RefreshRowHighlights()
    {
        foreach (UIElement child in _listPanel.Children)
        {
            if (child is Border { Tag: InboxMessage rowMsg } rowBorder)
                RefreshRowHighlight(rowBorder, rowMsg.Id);
        }
    }

    private void RefreshRowHighlight(Border row, string msgId)
    {
        if (_selectedIds.Contains(msgId))
            row.SetResourceReference(Border.BackgroundProperty,
                _listHasFocus ? "FocusedSelectedItem" : "UnfocusedSelectedItem");
        else
            row.Background = Brushes.Transparent;
    }

    private void MarkRowRead(InboxMessage msg, Border row, UIElement indicator, TextBlock subjectLabel)
    {
        msg.Read = true;
        _markRead(msg.Id);
        row.Opacity             = 1.0;
        indicator.Opacity       = 0.4;
        subjectLabel.FontWeight = FontWeights.Normal;
        subjectLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
    }

    private void MarkRowUnread(InboxMessage msg, Border row, UIElement indicator, TextBlock subjectLabel)
    {
        msg.Read = false;
        _markUnread(msg.Id);
        row.Opacity             = 1.0;
        indicator.Opacity       = 1.0;
        subjectLabel.FontWeight = FontWeights.SemiBold;
        subjectLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
    }

    private double MeasureTextWidth(string text, FontWeight weight)
    {
        var fontSize = _listPanel.TryFindResource("FontSizeBody") is double fs ? fs : 13.0;
        var typeface = new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, weight, FontStretches.Normal);
        var pixelsPerDip = VisualTreeHelper.GetDpi(_listPanel).PixelsPerDip;
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            pixelsPerDip);
        return ft.Width;
    }

    public double? GetMaximumUsefulWidth(int maxRows = 50)
    {
        double maxRowWidth = 0;
        int count = 0;

        if (_listPanel.IsLoaded)
        {
            foreach (var child in _listPanel.Children)
            {
                if (count >= maxRows) break;
                if (child is not Border { Tag: InboxMessage msg }) continue;
                var weight = msg.Read ? FontWeights.Normal : FontWeights.SemiBold;
                var textWidth = MeasureTextWidth(msg.Subject, weight);
                const double perRowChrome = 20; // row padding + dot
                maxRowWidth = Math.Max(maxRowWidth, textWidth + perRowChrome);
                count++;
            }
        }

        const double panelChrome = 43; // padding + border + scrollbar
        // If panel not yet loaded or has no measurable rows, return a reasonable cap
        // rather than null so the drag engine doesn't treat it as uncapped.
        if (maxRowWidth <= 0)
            return 420 + panelChrome;

        return maxRowWidth + panelChrome;
    }

    public double GetMaximumUsefulHeight()
    {
        const double titleRow      = 40;
        const double filterRow     = 32;
        const double msgRowHeight  = 44;
        const double cap           = 600;
        const double floor         = 150;

        int count = 0;
        foreach (var child in _listPanel.Children)
            if (child is Border { Tag: InboxMessage }) count++;

        double h = titleRow + filterRow + count * msgRowHeight;
        if (_viewerBorder.Visibility == Visibility.Visible)
            h += 120; // minimum viewer height when open
        h += 24; // bottom padding
        return Math.Clamp(h, floor, cap);
    }

    // ── Message viewer ────────────────────────────────────────────────────────

    private void ShowViewer(InboxMessage msg)
    {
        // NOTE: This method is currently DEAD CODE — it is never called.
        // SelectMessage calls _openMessageWindow (pop-out InboxMessageWindow) instead.
        // If the inline viewer path is ever wired up, traces here will fire.
        SquadDashTrace.Write(TraceCategory.Inbox, $"InboxPanelController.ShowViewer: [DEAD CODE PATH] msgId={msg.Id} subject='{msg.Subject}'");
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
                        catch { }
                    };
                }
                break;
            }

            case "text":
                chip.MouseLeftButtonUp += (_, _) =>
                {
                    SquadDashTrace.Write(TraceCategory.Inbox, $"InboxPanelController.AttachmentChip.Click: type=text label='{att.Label}' contentLen={att.Content?.Length ?? 0} — opening MarkdownDocumentWindow, NOT calling SelectAndScrollToText");
                    try { MarkdownDocumentWindow.ShowContent(owner, att.Label, att.Content ?? ""); }
                    catch { }
                };
                break;
        }

        return chip;
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    private ContextMenu BuildRowContextMenu(InboxMessage msg, Border row, UIElement indicator, TextBlock subjectLabel)
    {
        var menu = MakeMenu();

        if (_addToChat is not null)
        {
            var addToChatItem = MakeItem("Add to Chat");
            addToChatItem.Click += (_, _) => _addToChat(msg);
            menu.Items.Add(addToChatItem);
        }

        if (_addToNewChat is not null)
        {
            var addToNewChatItem = MakeItem("Add to New Chat");
            addToNewChatItem.Click += (_, _) => _addToNewChat(msg);
            menu.Items.Add(addToNewChatItem);
        }

        if (_addToChat is not null || _addToNewChat is not null)
            menu.Items.Add(MakeSep());

        if (msg.Read)
        {
            var markUnreadItem = MakeItem("Mark as unread");
            markUnreadItem.Click += (_, _) =>
            {
                MarkRowUnread(msg, row, indicator, subjectLabel);
            };
            menu.Items.Add(markUnreadItem);
        }
        else
        {
            var markReadItem = MakeItem("Mark as read");
            markReadItem.Click += (_, _) =>
            {
                MarkRowRead(msg, row, indicator, subjectLabel);
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

    private ContextMenu BuildMultiSelectContextMenu()
    {
        var menu  = MakeMenu();
        var count = _selectedIds.Count;

        var header = MakeItem($"{count} messages selected");
        header.IsEnabled = false;
        menu.Items.Add(header);
        menu.Items.Add(MakeSep());

        // Determine the read state of selected messages so we can show appropriate items.
        bool anyUnread = false;
        bool anyRead   = false;
        foreach (UIElement child in _listPanel.Children)
        {
            if (child is Border { Tag: InboxMessage rowMsg } && _selectedIds.Contains(rowMsg.Id))
            {
                if (rowMsg.Read) anyRead   = true;
                else             anyUnread = true;
            }
        }

        // Show "Mark all as read" when any selected message is unread.
        if (anyUnread)
        {
            var markReadItem = MakeItem("Mark all as read");
            markReadItem.Click += (_, _) =>
            {
                foreach (UIElement child in _listPanel.Children)
                {
                    if (child is Border { Tag: InboxMessage rowMsg } rowBorder
                        && _selectedIds.Contains(rowMsg.Id) && !rowMsg.Read)
                    {
                        var dot   = FindIndicatorInRow(rowBorder);
                        var label = FindSubjectLabelInRow(rowBorder);
                        if (dot is not null && label is not null)
                            MarkRowRead(rowMsg, rowBorder, dot, label);
                    }
                }
            };
            menu.Items.Add(markReadItem);
        }

        // Show "Mark all as unread" when any selected message is read.
        if (anyRead)
        {
            var markUnreadItem = MakeItem("Mark all as unread");
            markUnreadItem.Click += (_, _) =>
            {
                foreach (UIElement child in _listPanel.Children)
                {
                    if (child is Border { Tag: InboxMessage rowMsg } rowBorder
                        && _selectedIds.Contains(rowMsg.Id) && rowMsg.Read)
                    {
                        var dot   = FindIndicatorInRow(rowBorder);
                        var label = FindSubjectLabelInRow(rowBorder);
                        if (dot is not null && label is not null)
                            MarkRowUnread(rowMsg, rowBorder, dot, label);
                    }
                }
            };
            menu.Items.Add(markUnreadItem);
        }

        menu.Items.Add(MakeSep());

        var deleteItem = MakeItem("Delete all");
        deleteItem.Click += (_, _) =>
        {
            var result = MessageBox.Show(
                $"Delete {count} selected messages?",
                "Delete Messages",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var toRemove = _listPanel.Children
                .OfType<Border>()
                .Where(b => b.Tag is InboxMessage rowMsg && _selectedIds.Contains(rowMsg.Id))
                .ToList();

            foreach (var rowBorder in toRemove)
            {
                if (rowBorder.Tag is InboxMessage rowMsg)
                    _delete(rowMsg.Id);
                _listPanel.Children.Remove(rowBorder);
            }

            _selectedIds.Clear();

            bool anyVisible = _listPanel.Children
                .OfType<Border>()
                .Any(b => b.Visibility == Visibility.Visible && b.Tag is InboxMessage);

            if (!anyVisible)
            {
                bool hasEmpty = _listPanel.Children
                    .OfType<TextBlock>()
                    .Any(tb => tb.Tag is string t && t == "empty");
                if (!hasEmpty)
                    _listPanel.Children.Add(BuildEmptyLabel("No messages"));
            }
        };
        menu.Items.Add(deleteItem);

        return menu;
    }

    private static UIElement? FindIndicatorInRow(Border row)
    {
        if (row.Child is StackPanel outer
            && outer.Children.Count > 0
            && outer.Children[0] is StackPanel header
            && header.Children.Count > 0)
            return header.Children[0] as UIElement;
        return null;
    }

    private static UIElement BuildPriorityIndicator(InboxMessage msg)
    {
        var priority = (msg.Priority ?? "mid").ToLowerInvariant();
        // All indicators use the same 8px width + 4px right margin = 12px footprint
        // so that every subject line starts at the same horizontal position.
        // When read, opacity is dimmed rather than hidden so the shape stays visible.
        double opacity = msg.Read ? 0.4 : 1.0;

        switch (priority)
        {
            case "low":
            {
                var path = new System.Windows.Shapes.Path
                {
                    Data            = Geometry.Parse("M18.3333333333333,17.6666666666667C30.6666666666666,5.66666666666666,30.6666666666666,5.66666666666666,30.6666666666666,5.66666666666666C32.6666666666666,3.66666666666666,31.3333333333333,0.333333333333329,28.6666666666666,0.333333333333329C3.66666666666663,0.333333333333329,3.66666666666663,0.333333333333329,3.66666666666663,0.333333333333329C0.666666666666629,0.333333333333329,-0.666666666666686,3.66666666666666,1.33333333333331,5.66666666666666C14,17.6666666666667,14,17.6666666666667,14,17.6666666666667C15,19,17,19,18.3333333333333,17.6666666666667z"),
                    StrokeThickness = 1.333,
                };
                path.SetResourceReference(System.Windows.Shapes.Path.FillProperty, "TaskPriorityLow");
                var canvas = new Canvas { Width = 32, Height = 19 };
                canvas.Children.Add(path);
                var viewbox = new Viewbox
                {
                    Stretch           = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(0, 0, 4, 0),
                    Opacity           = opacity,
                    Child             = canvas,
                };
                viewbox.SetResourceReference(FrameworkElement.WidthProperty,  "FontSizeBody");
                viewbox.SetResourceReference(FrameworkElement.HeightProperty, "FontSizeBody");
                return viewbox;
            }
            case "high":

            {
                var path = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M13.6666666666666,1.66666666666666C0,25,0,25,0,25C-1.66666666666669,27.6666666666667,0.666666666666629,31.3333333333333,4,31.3333333333333C30.6666666666666,31.3333333333333,30.6666666666666,31.3333333333333,30.6666666666666,31.3333333333333C34,31.3333333333333,36,27.6666666666667,34.3333333333333,25C21,1.66666666666666,21,1.66666666666666,21,1.66666666666666C19.3333333333333,-1,15.3333333333333,-1,13.6666666666666,1.66666666666666z"),
                };
                path.SetResourceReference(System.Windows.Shapes.Path.FillProperty, "TaskPriorityMid");
                var canvas = new Canvas { Width = 35, Height = 31 };
                canvas.Children.Add(path);
                var viewbox = new Viewbox
                {
                    Stretch           = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(0, 0, 4, 0),
                    Opacity           = opacity,
                    Child             = canvas,
                };
                viewbox.SetResourceReference(FrameworkElement.WidthProperty,  "FontSizeBody");
                viewbox.SetResourceReference(FrameworkElement.HeightProperty, "FontSizeBody");
                return viewbox;
            }
            case "critical":
            {
                var path = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M 17.5,0 L 35,17.5 L 17.5,35 L 0,17.5 Z"),
                };
                path.SetResourceReference(System.Windows.Shapes.Path.FillProperty, "TaskPriorityHigh");
                var canvas = new Canvas { Width = 35, Height = 35 };
                canvas.Children.Add(path);
                var viewbox = new Viewbox
                {
                    Stretch           = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(0, 0, 4, 0),
                    Opacity           = opacity,
                    Child             = canvas,
                };
                viewbox.LayoutTransform = new System.Windows.Media.ScaleTransform(1.15, 1.15);
                viewbox.SetResourceReference(FrameworkElement.WidthProperty,  "FontSizeBody");
                viewbox.SetResourceReference(FrameworkElement.HeightProperty, "FontSizeBody");
                return viewbox;
            }
            default: // "mid" and anything unrecognised
            {
                var dot = new Ellipse
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    LayoutTransform   = new System.Windows.Media.ScaleTransform(0.8, 0.8),
                    Margin            = new Thickness(0, 0, 4, 0),
                    Opacity           = opacity,
                };
                dot.SetResourceReference(Ellipse.FillProperty, "TaskPriorityLow");
                dot.SetResourceReference(FrameworkElement.WidthProperty,  "FontSizeBody");
                dot.SetResourceReference(FrameworkElement.HeightProperty, "FontSizeBody");
                return dot;
            }
        }
    }

    private static TextBlock? FindSubjectLabelInRow(Border row)
    {
        if (row.Child is StackPanel outer
            && outer.Children.Count > 0
            && outer.Children[0] is StackPanel header
            && header.Children.Count > 1
            && header.Children[1] is TextBlock label)
            return label;
        return null;
    }

    private void RemoveRow(Border row)
    {
        if (row.Tag is InboxMessage removed)
        {
            _selectedIds.Remove(removed.Id);
            if (_viewModel.SelectedMessage is not null
                && _viewModel.SelectedMessage.Id == removed.Id)
            {
                _viewerBorder.Visibility = Visibility.Collapsed;
                _viewModel.SelectedMessage = null;
            }
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
