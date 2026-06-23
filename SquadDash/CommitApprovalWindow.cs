namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

/// <summary>Manages content in the inline Commit Approvals panel.</summary>
internal sealed class CommitApprovalPanel {
    private readonly Action<string>                               _navigateUrl;
    private readonly Action<CommitApprovalItem>                   _scrollToTurn;
    private readonly Action<CommitApprovalItem>                   _onItemChanged;
    private readonly Action<IReadOnlyList<CommitApprovalItem>>    _onItemsRemoved;
    private readonly Action<CommitApprovalItem>                   _onFollowUp;
    private readonly Action<CommitApprovalItem>?                _addToNewChat;
    private readonly Action<CommitApprovalItem>?                _addToNotes;
    private readonly Func<IReadOnlyList<string>>?               _getGroups;

    private readonly StackPanel _needsApprovalPanel;
    private readonly StackPanel _approvedPanel;
    private readonly StackPanel _rejectedPanel;
    private readonly UIElement  _rejectedSection;
    private readonly UIElement  _approvedSection;
    private readonly UIElement  _approvedScrollViewer;
    private readonly ScrollViewer _needsApprovalScrollViewer;

    private Border?    _selectedRow;
    private bool       _showRejected;
    private bool       _showApproved;
    private bool       _groupedView;
    private MenuItem?  _toggleRejectedItem;
    private MenuItem?  _toggleApprovedItem;
    private MenuItem?  _toggleGroupedViewItem;
    private readonly Action<bool>? _onShowApprovedChanged;
    private readonly Action<bool>? _onShowRejectedChanged;
    private readonly Action<bool>? _onGroupedViewChanged;
    private IReadOnlyList<CommitApprovalItem> _lastItems = [];
    private List<CommitApprovalItem> _mutableItems = new();

    public CommitApprovalPanel(
        StackPanel                               needsApprovalPanel,
        StackPanel                               approvedPanel,
        StackPanel                               rejectedPanel,
        UIElement                                rejectedSection,
        UIElement                                approvedSection,
        UIElement                                approvedScrollViewer,
        Border                                   outerBorder,
        ScrollViewer                             needsApprovalScrollViewer,
        Action<string>                            navigateUrl,
        Action<CommitApprovalItem>                scrollToTurn,
        Action<CommitApprovalItem>                onItemChanged,
        Action<IReadOnlyList<CommitApprovalItem>> onItemsRemoved,
        Action<CommitApprovalItem>                onFollowUp,
        Action<CommitApprovalItem>?               addToNewChat  = null,
        Action<CommitApprovalItem>?               addToNotes    = null,
        bool                                     initialShowApproved = true,
        Action<bool>?                            onShowApprovedChanged = null,
        bool                                     initialShowRejected = true,
        Action<bool>?                            onShowRejectedChanged = null,
        bool                                     initialGroupedView = false,
        Action<bool>?                            onGroupedViewChanged = null,
        Func<IReadOnlyList<string>>?             getGroups            = null) {
        _needsApprovalPanel        = needsApprovalPanel;
        _approvedPanel             = approvedPanel;
        _rejectedPanel             = rejectedPanel;
        _rejectedSection           = rejectedSection;
        _approvedSection           = approvedSection;
        _approvedScrollViewer      = approvedScrollViewer;
        _needsApprovalScrollViewer = needsApprovalScrollViewer;
        _navigateUrl               = navigateUrl;
        _scrollToTurn              = scrollToTurn;
        _onItemChanged             = onItemChanged;
        _onItemsRemoved            = onItemsRemoved;
        _onFollowUp                = onFollowUp;
        _addToNewChat              = addToNewChat;
        _addToNotes                = addToNotes;
        _showApproved              = initialShowApproved;
        _onShowApprovedChanged     = onShowApprovedChanged;
        _showRejected              = initialShowRejected;
        _onShowRejectedChanged     = onShowRejectedChanged;
        _groupedView               = initialGroupedView;
        _onGroupedViewChanged      = onGroupedViewChanged;
        _getGroups                 = getGroups;

        AttachPanelContextMenu(outerBorder);
        _rejectedSection.Visibility = _showRejected ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void AddItem(CommitApprovalItem item) {
        _mutableItems.Insert(0, item);
        if (_groupedView) {
            RebuildGroupedPanels();
            SyncApprovedSectionVisibility();
            return;
        }
        var row = BuildRow(item);
        row.Visibility = MatchesFilter(item) ? Visibility.Visible : Visibility.Collapsed;
        _needsApprovalPanel.Children.Insert(0, row);
    }

    public void ReplaceAllItems(IReadOnlyList<CommitApprovalItem> items) {
        _lastItems    = items;
        _mutableItems = items.ToList();
        _selectedRow  = null;
        _needsApprovalPanel.Children.Clear();
        _approvedPanel.Children.Clear();
        _rejectedPanel.Children.Clear();

        var ordered = items.OrderByDescending(i => i.TurnStartedAt).ToList();

        // Rejected always flat
        foreach (var item in ordered.Where(i => i.IsRejected))
            _rejectedPanel.Children.Add(BuildRejectedRow(item));

        var pending  = ordered.Where(i => !i.IsRejected && !i.IsApproved).ToList();
        var approved = ordered.Where(i => !i.IsRejected &&  i.IsApproved).ToList();

        if (_groupedView) {
            RenderGroupedPendingItems(pending);
            RenderGroupedApprovedItems(approved);
        } else {
            foreach (var item in pending)
                _needsApprovalPanel.Children.Add(BuildRow(item));
            foreach (var item in approved)
                _approvedPanel.Children.Add(BuildRow(item));
        }

        ApplyFilterToPanel(_needsApprovalPanel);
        ApplyFilterToPanel(_approvedPanel);
        ApplyFilterToPanel(_rejectedPanel);
        SyncApprovedSectionVisibility();
    }

    public void OnClearApprovedClicked() {
        var removed = new List<CommitApprovalItem>();
        foreach (var child in _approvedPanel.Children) {
            if (child is Border b && b.Tag is CommitApprovalItem item)
                removed.Add(item);
        }
        foreach (var r in removed)
            _mutableItems.RemoveAll(i => i.Id == r.Id);
        _approvedPanel.Children.Clear();
        SyncApprovedSectionVisibility();
        if (removed.Count > 0)
            _onItemsRemoved(removed);
    }

    private void RenderGroupedPendingItems(List<CommitApprovalItem> pending) {
        var grouped = pending
            .GroupBy(i => i.FeatureGroup ?? "Uncategorized")
            .OrderBy(g => g.Key == "Uncategorized" ? 0 : 1)
            .ThenBy(g => g.Key);

        foreach (var group in grouped) {
            var groupItems = group.OrderByDescending(i => i.TurnStartedAt).ToList();

            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 2) };
            var groupCheckBox = new CheckBox { IsThreeState = false, VerticalAlignment = VerticalAlignment.Center };
            var groupLabel = new TextBlock {
                Text              = group.Key,
                FontWeight        = FontWeights.SemiBold,
                Margin            = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            groupLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            groupLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            header.Children.Add(groupCheckBox);
            header.Children.Add(groupLabel);
            _needsApprovalPanel.Children.Add(header);

            foreach (var item in groupItems) {
                var row = BuildRow(item);
                row.Margin = new Thickness(12, 0, 0, 0);
                _needsApprovalPanel.Children.Add(row);
            }

            groupCheckBox.Checked += (_, _) => {
                foreach (var gi in groupItems) {
                    var idx2 = _mutableItems.FindIndex(x => x.Id == gi.Id);
                    if (idx2 >= 0) {
                        _mutableItems[idx2] = _mutableItems[idx2] with { IsApproved = true };
                        _onItemChanged(_mutableItems[idx2]);
                    }
                }
                RebuildGroupedPanels();
                SyncApprovedSectionVisibility();
            };
            groupCheckBox.Unchecked += (_, _) => {
                foreach (var gi in groupItems) {
                    var idx2 = _mutableItems.FindIndex(x => x.Id == gi.Id);
                    if (idx2 >= 0) {
                        _mutableItems[idx2] = _mutableItems[idx2] with { IsApproved = false };
                        _onItemChanged(_mutableItems[idx2]);
                    }
                }
                RebuildGroupedPanels();
                SyncApprovedSectionVisibility();
            };
        }
    }

    private void RenderGroupedApprovedItems(List<CommitApprovalItem> approved) {
        var grouped = approved
            .GroupBy(i => i.FeatureGroup ?? "Uncategorized")
            .OrderBy(g => g.Key == "Uncategorized" ? 0 : 1)
            .ThenBy(g => g.Key);

        foreach (var group in grouped) {
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 2) };
            var groupLabel = new TextBlock {
                Text              = group.Key,
                FontWeight        = FontWeights.SemiBold,
                Margin            = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            groupLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            groupLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            header.Children.Add(groupLabel);
            _approvedPanel.Children.Add(header);

            foreach (var item in group.OrderByDescending(i => i.TurnStartedAt)) {
                var row = BuildRow(item);
                row.Margin = new Thickness(12, 0, 0, 0);
                _approvedPanel.Children.Add(row);
            }
        }
    }

    // ── Panel context menu ────────────────────────────────────────────────────

    private static ContextMenu MakeMenu() {
        var m = new ContextMenu();
        m.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");
        return m;
    }

    private static MenuItem MakeItem(string header) {
        var i = new MenuItem { Header = header };
        i.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        return i;
    }

    private static Separator MakeSep() {
        var s = new Separator();
        s.SetResourceReference(Separator.StyleProperty, "ThemedMenuSeparatorStyle");
        return s;
    }

    private void AttachPanelContextMenu(Border outerBorder) {
        var menu = MakeMenu();

        _toggleRejectedItem    = MakeItem(string.Empty);
        _toggleApprovedItem    = MakeItem(string.Empty);
        _toggleGroupedViewItem = MakeItem(string.Empty);
        UpdateToggleHeaders();

        _toggleRejectedItem.Click += (_, _) => {
            _showRejected = !_showRejected;
            _rejectedSection.Visibility = _showRejected ? Visibility.Visible : Visibility.Collapsed;
            _onShowRejectedChanged?.Invoke(_showRejected);
            UpdateToggleHeaders();
        };

        _toggleApprovedItem.Click += (_, _) => {
            _showApproved = !_showApproved;
            _onShowApprovedChanged?.Invoke(_showApproved);
            SyncApprovedSectionVisibility();
            UpdateToggleHeaders();
        };

        _toggleGroupedViewItem.Click += (_, _) => {
            _groupedView = !_groupedView;
            _onGroupedViewChanged?.Invoke(_groupedView);
            UpdateToggleHeaders();
            ReplaceAllItems(_lastItems);
        };

        menu.Items.Add(MakeSep());
        menu.Items.Add(_toggleGroupedViewItem);
        menu.Items.Add(_toggleApprovedItem);
        menu.Items.Add(_toggleRejectedItem);
        outerBorder.ContextMenu = menu;
    }

    private void UpdateToggleHeaders() {
        if (_toggleRejectedItem is not null)
            _toggleRejectedItem.Header = _showRejected ? "Hide Rejected" : "Show Rejected";
        if (_toggleApprovedItem is not null)
            _toggleApprovedItem.Header = _showApproved ? "Hide Approved" : "Show Approved";
        if (_toggleGroupedViewItem is not null)
            _toggleGroupedViewItem.Header = _groupedView ? "Ungroup by Feature" : "Group by Feature";
    }

    // ── Row construction ─────────────────────────────────────────────────────

    private Border BuildRow(CommitApprovalItem item) {
        var row = new Border { Background = Brushes.Transparent, Tag = item };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverSurface");
        row.MouseLeave += (_, _) => {
            if (row == _selectedRow)
                row.SetResourceReference(Border.BackgroundProperty, "ApprovalSelectedSurface");
            else
                row.Background = Brushes.Transparent;
        };

        var menu = MakeMenu();
        var followUpItem = MakeItem("Add to Chat");
        followUpItem.Click += (_, _) => _onFollowUp(item);
        menu.Items.Add(followUpItem);
        if (_addToNewChat is not null)
        {
            var addToNewChatItem = MakeItem("Add to New Chat");
            addToNewChatItem.Click += (_, _) => _addToNewChat(item);
            menu.Items.Add(addToNewChatItem);
        }
        if (_addToNotes is not null)
        {
            var notesItem = MakeItem("Add to Notes");
            notesItem.Click += (_, _) => _addToNotes(item);
            menu.Items.Add(notesItem);
        }
        menu.Items.Add(MakeSep());
        if (_groupedView && _getGroups is not null) {
            var moveToCatItem = MakeItem("Move to category");
            moveToCatItem.Items.Add(new MenuItem { Header = "..." }); // placeholder so WPF treats as submenu parent
            moveToCatItem.SubmenuOpened += (_, _) => {
                moveToCatItem.Items.Clear();
                var groups = _getGroups.Invoke();
                foreach (var g in groups.OrderBy(x => x)) {
                    var groupMenuItem = MakeItem(g);
                    var capturedGroup = g;
                    groupMenuItem.Click += (_, _) => MoveItemToGroup(item, capturedGroup);
                    moveToCatItem.Items.Add(groupMenuItem);
                }
            };
            menu.Items.Add(moveToCatItem);
            menu.Items.Add(MakeSep());
        }
        var rejectItem = MakeItem($"Reject {DescriptionPreview(item.Description)}");
        rejectItem.Click += (_, _) => HandleRejectClicked(row, item);
        menu.Items.Add(rejectItem);
        menu.Items.Add(MakeSep());
        var rowToggleRejectedItem = MakeItem(string.Empty);
        var rowToggleApprovedItem = MakeItem(string.Empty);
        menu.Opened += (_, _) => {
            rowToggleRejectedItem.Header = _showRejected ? "Hide Rejected" : "Show Rejected";
            rowToggleApprovedItem.Header = _showApproved ? "Hide Approved" : "Show Approved";
        };
        rowToggleRejectedItem.Click += (_, _) => {
            _showRejected = !_showRejected;
            _rejectedSection.Visibility = _showRejected ? Visibility.Visible : Visibility.Collapsed;
            _onShowRejectedChanged?.Invoke(_showRejected);
            UpdateToggleHeaders();
        };
        rowToggleApprovedItem.Click += (_, _) => {
            _showApproved = !_showApproved;
            _onShowApprovedChanged?.Invoke(_showApproved);
            SyncApprovedSectionVisibility();
            UpdateToggleHeaders();
        };
        menu.Items.Add(rowToggleApprovedItem);
        menu.Items.Add(rowToggleRejectedItem);
        row.ContextMenu = menu;

        var grid = new Grid { Margin = new Thickness(4, 2, 4, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var checkBox = new CheckBox {
            IsChecked         = item.IsApproved,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(checkBox, 0);
        grid.Children.Add(checkBox);

        var descBlock = new TextBlock {
            Text              = CleanDescription(item.Description),
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(6, 0, 6, 0),
            Cursor            = Cursors.Hand,
            ToolTip           = BuildDescriptionTooltip(item),
        };
        if (item.TouchesDecisionsFile)
            descBlock.FontWeight = FontWeights.Bold;
        ToolTipService.SetShowDuration(descBlock, 30000);
        descBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        descBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        descBlock.MouseLeftButtonUp += (_, e) => {
            e.Handled = true;
            if (_selectedRow != null && _selectedRow != row)
                _selectedRow.Background = Brushes.Transparent;
            _selectedRow = row;
            _scrollToTurn(item);
        };
        Grid.SetColumn(descBlock, 1);
        grid.Children.Add(descBlock);

        if (!_groupedView && !string.IsNullOrEmpty(item.FeatureGroup)) {
            var badge = new Border {
                CornerRadius      = new CornerRadius(3),
                Padding           = new Thickness(4, 1, 4, 1),
                Margin            = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            badge.SetResourceReference(Border.BackgroundProperty, "SubtleBorder");
            var badgeText = new TextBlock {
                Text         = item.FeatureGroup,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth     = 80,
            };
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            badgeText.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeXSmall");
            badge.Child = badgeText;
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }

        if (item.CommitUrl is not null) {
            var sha = BuildShaBlock(item);
            Grid.SetColumn(sha, 3);
            grid.Children.Add(sha);
        }

        // Wire checkbox after IsChecked is set so construction doesn't fire handlers
        checkBox.Checked   += (_, _) => HandleCheckChanged(row, item, isApproved: true);
        checkBox.Unchecked += (_, _) => HandleCheckChanged(row, item, isApproved: false);

        row.Child = grid;
        return row;
    }

    private Border BuildRejectedRow(CommitApprovalItem item) {
        var row = new Border { Background = Brushes.Transparent, Tag = item };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverSurface");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

        var menu = MakeMenu();
        var unrejectItem = MakeItem("Unreject");
        unrejectItem.Click += (_, _) => HandleUnrejectClicked(row, item);
        menu.Items.Add(unrejectItem);
        row.ContextMenu = menu;

        var grid = new Grid { Margin = new Thickness(4, 2, 4, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(BuildRedX());

        var descBlock = new TextBlock {
            Text              = CleanDescription(item.Description),
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(6, 0, 6, 0),
            Opacity           = 0.6,
            ToolTip           = BuildDescriptionTooltip(item),
        };
        if (item.TouchesDecisionsFile)
            descBlock.FontWeight = FontWeights.Bold;
        ToolTipService.SetShowDuration(descBlock, 30000);
        descBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        descBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        Grid.SetColumn(descBlock, 1);
        grid.Children.Add(descBlock);

        if (item.CommitUrl is not null) {
            var sha = BuildShaBlock(item);
            Grid.SetColumn(sha, 2);
            grid.Children.Add(sha);
        }

        row.Child = grid;
        return row;
    }

    private TextBlock BuildShaBlock(CommitApprovalItem item) {
        var shaDisplay = item.CommitSha.Length >= 7 ? item.CommitSha[..7] : item.CommitSha;
        var shaBlock = new TextBlock {
            Cursor            = Cursors.Hand,
            Margin            = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        shaBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        var shaRun = new Run(shaDisplay) { TextDecorations = TextDecorations.Underline };
        shaRun.SetResourceReference(Run.ForegroundProperty, "SubtleText");
        shaBlock.Inlines.Add(shaRun);
        shaBlock.MouseLeftButtonUp += (_, e) => { e.Handled = true; _navigateUrl(item.CommitUrl!); };
        return shaBlock;
    }

    private static UIElement BuildRedX() {
        var canvas = new Canvas {
            Width             = 14,
            Height            = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var line1 = new Line { X1 = 2, Y1 = 2, X2 = 12, Y2 = 12, Stroke = Brushes.Red, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var line2 = new Line { X1 = 12, Y1 = 2, X2 = 2, Y2 = 12, Stroke = Brushes.Red, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        canvas.Children.Add(line1);
        canvas.Children.Add(line2);
        return canvas;
    }

    // ── State changes ─────────────────────────────────────────────────────────

    private void MoveItemToGroup(CommitApprovalItem item, string groupName) {
        var updated = item with { FeatureGroup = groupName };
        var idx = _mutableItems.FindIndex(x => x.Id == item.Id);
        if (idx >= 0) _mutableItems[idx] = updated;
        _onItemChanged(updated);
        RebuildGroupedPanels();
        SyncApprovedSectionVisibility();
        // After layout, scroll to and select the moved row
        _needsApprovalScrollViewer.Dispatcher.InvokeAsync(() => {
            foreach (var child in _needsApprovalPanel.Children) {
                if (child is Border b && b.Tag is CommitApprovalItem ci && ci.Id == item.Id) {
                    _selectedRow = b;
                    b.SetResourceReference(Border.BackgroundProperty, "ApprovalSelectedSurface");
                    b.BringIntoView();
                    break;
                }
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void HandleCheckChanged(Border row, CommitApprovalItem item, bool isApproved) {
        if (_selectedRow == row) _selectedRow = null;
        var updated = item with { IsApproved = isApproved };

        var idx = _mutableItems.FindIndex(i => i.Id == item.Id);
        if (idx >= 0) _mutableItems[idx] = updated;

        _onItemChanged(updated);

        if (_groupedView) {
            RebuildGroupedPanels();
            SyncApprovedSectionVisibility();
            return;
        }

        var sourcePanel = isApproved ? _needsApprovalPanel : _approvedPanel;
        var targetPanel = isApproved ? _approvedPanel      : _needsApprovalPanel;

        // When approving a near-bottom item while the list is already scrolled to the bottom,
        // the "Approved" header appearing shrinks the ScrollViewer and hides the remaining
        // bottom items. Detect this before modifying the panel, then re-scroll after layout.
        bool shouldScrollNeedsToBottom = false;
        if (isApproved) {
            int rowIdx = _needsApprovalPanel.Children.IndexOf(row);
            int count  = _needsApprovalPanel.Children.Count;
            bool isNearBottom = rowIdx >= 0 && rowIdx >= count - 3;
            bool wasAtBottom  = _needsApprovalScrollViewer.ScrollableHeight > 0 &&
                                _needsApprovalScrollViewer.VerticalOffset >=
                                    _needsApprovalScrollViewer.ScrollableHeight - 2.0;
            shouldScrollNeedsToBottom = isNearBottom && wasAtBottom;
        }

        sourcePanel.Children.Remove(row);
        InsertSorted(targetPanel, BuildRow(updated), updated);
        ApplyFilterToPanel(targetPanel);
        SyncApprovedSectionVisibility();

        if (shouldScrollNeedsToBottom) {
            _needsApprovalScrollViewer.Dispatcher.InvokeAsync(
                () => _needsApprovalScrollViewer.ScrollToBottom(),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void RebuildGroupedPanels() {
        _needsApprovalPanel.Children.Clear();
        _approvedPanel.Children.Clear();
        var ordered  = _mutableItems.OrderByDescending(i => i.TurnStartedAt).ToList();
        var pending  = ordered.Where(i => !i.IsRejected && !i.IsApproved).ToList();
        var approved = ordered.Where(i => !i.IsRejected &&  i.IsApproved).ToList();
        RenderGroupedPendingItems(pending);
        RenderGroupedApprovedItems(approved);
        ApplyFilterToPanel(_needsApprovalPanel);
        ApplyFilterToPanel(_approvedPanel);
    }

    private void HandleRejectClicked(Border row, CommitApprovalItem item) {
        if (_selectedRow == row) _selectedRow = null;
        var updated = item with { IsApproved = false, IsRejected = true };

        var idx = _mutableItems.FindIndex(i => i.Id == item.Id);
        if (idx >= 0) _mutableItems[idx] = updated;

        var sourcePanel = item.IsApproved ? _approvedPanel : _needsApprovalPanel;
        if (_groupedView) {
            // In grouped view, remove all children belonging to this item from the source panel,
            // then do a full rebuild to keep group headers clean.
            RebuildGroupedPanels();
        } else {
            sourcePanel.Children.Remove(row);
        }
        InsertSorted(_rejectedPanel, BuildRejectedRow(updated), updated);
        ApplyFilterToPanel(_rejectedPanel);
        SyncApprovedSectionVisibility();

        _onItemChanged(updated);
    }

    private void HandleUnrejectClicked(Border row, CommitApprovalItem item) {
        var updated = item with { IsRejected = false, IsApproved = false };

        var idx = _mutableItems.FindIndex(i => i.Id == item.Id);
        if (idx >= 0) _mutableItems[idx] = updated;

        _rejectedPanel.Children.Remove(row);

        if (_groupedView) {
            RebuildGroupedPanels();
            SyncApprovedSectionVisibility();
        } else {
            InsertSorted(_needsApprovalPanel, BuildRow(updated), updated);
            ApplyFilterToPanel(_needsApprovalPanel);
        }

        _onItemChanged(updated);
    }

    private void SyncApprovedSectionVisibility() {
        var vis = (_showApproved && _approvedPanel.Children.Count > 0)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _approvedSection.Visibility      = vis;
        _approvedScrollViewer.Visibility = vis;
    }

    /// <summary>Builds the tooltip string for an approval list row.
    /// Shows the full untruncated description, plus the original prompt or prompt hint if available.</summary>
    private ToolTip BuildDescriptionTooltip(CommitApprovalItem item) {
        var cleaned = CleanDescription(item.Description);

        var container = new StackPanel { Margin = new Thickness(2) };

        var summaryBlock = new TextBlock {
            Text        = cleaned,
            FontWeight  = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
        };
        summaryBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        summaryBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        container.Children.Add(summaryBlock);

        var relTime = StatusTimingPresentation.FormatRelativeTimestamp(item.TurnStartedAt);
        var relBlock = new TextBlock {
            Text         = relTime,
            Margin       = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        relBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        relBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        container.Children.Add(relBlock);

        string? rawPrompt = null;
        if (!string.IsNullOrWhiteSpace(item.OriginalPrompt))
            rawPrompt = item.OriginalPrompt.Trim();
        else if (!string.IsNullOrWhiteSpace(item.TurnPromptHint))
            rawPrompt = item.TurnPromptHint.Trim();

        if (rawPrompt is not null) {
            var promptText = DictationAnnotation.Replace(rawPrompt, string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(promptText) &&
                !promptText.Equals(cleaned, StringComparison.OrdinalIgnoreCase)) {
                var promptBlock = new TextBlock {
                    Text         = promptText,
                    TextWrapping = TextWrapping.Wrap,
                    Margin       = new Thickness(0, 6, 0, 0),
                };
                promptBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
                promptBlock.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
                container.Children.Add(promptBlock);
            }
        }

        var tooltip = new ToolTip { Content = container, Padding = new Thickness(8, 6, 8, 6) };
        tooltip.SetResourceReference(ToolTip.BackgroundProperty, "PopupSurface");
        tooltip.SetResourceReference(ToolTip.BorderBrushProperty, "ActivePanelBorder");
        tooltip.BorderThickness = new Thickness(1);
        tooltip.Opened += (_, _) => {
            container.MaxWidth = Math.Max(300, _needsApprovalPanel.ActualWidth * 1.5);
            relBlock.Text = StatusTimingPresentation.FormatRelativeTimestamp(item.TurnStartedAt);
        };
        return tooltip;
    }

    private static readonly Regex DictationAnnotation =
        new(@"\(some or all of this prompt was dictated by voice\)\s*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Strips the trailing " in commit &lt;ref&gt;" phrase and trims whitespace.</summary>
    private static string CleanDescription(string text)
        => CommitPhraseSuffix.Replace(text, string.Empty).Trim();

    /// <summary>Truncates <paramref name="text"/> to at most 35 characters.
    /// If the text exceeds 35 characters, returns the first 34 followed by "…".</summary>
    private static string TruncateDescription(string text) {
        text = CommitPhraseSuffix.Replace(text, string.Empty).Trim();
        return text.Length > 35 ? text[..34] + "\u2026" : text;
    }

    /// <summary>Matches a trailing " in commit &lt;ref&gt;" phrase (plus optional punctuation).</summary>
    private static readonly Regex CommitPhraseSuffix =
        new(@"\s+in commit \S+[.,;!?]*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Returns the first 3 words of <paramref name="text"/> followed by "…",
    /// capped at 35 characters total (including the ellipsis).</summary>
    private static string DescriptionPreview(string text) {
        const int maxLen = 35;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var preview = string.Join(" ", words.Take(3));
        if (preview.Length > maxLen - 1)
            preview = preview[..(maxLen - 1)];
        return "\u201c" + preview + "\u2026\u201d";
    }

    /// <summary>Inserts <paramref name="row"/> into <paramref name="panel"/> so that items remain
    /// ordered newest-first by <see cref="CommitApprovalItem.TurnStartedAt"/>.</summary>
    private static void InsertSorted(StackPanel panel, Border row, CommitApprovalItem item) {
        for (int i = 0; i < panel.Children.Count; i++) {
            if (panel.Children[i] is Border existing &&
                existing.Tag is CommitApprovalItem existingItem &&
                existingItem.TurnStartedAt < item.TurnStartedAt) {
                panel.Children.Insert(i, row);
                return;
            }
        }
        panel.Children.Add(row);
    }

    // ── Dynamic width measurement ─────────────────────────────────────────────

    /// <summary>
    /// Measures the first <paramref name="maxRows"/> rows across all three panels and
    /// returns the minimum panel width required to display the widest row without truncation.
    /// Returns null if no panel is loaded or no rows are present.
    /// </summary>
    public double? GetMaximumUsefulWidth(int maxRows = 50)
    {
        if (!_needsApprovalPanel.IsLoaded && !_approvedPanel.IsLoaded && !_rejectedPanel.IsLoaded)
            return null;

        var referenceElement = _needsApprovalPanel.IsLoaded ? _needsApprovalPanel
            : (_approvedPanel.IsLoaded ? _approvedPanel : _rejectedPanel);

        double maxRowContentWidth = 0;
        int rowsChecked = 0;

        foreach (var panel in new[] { _needsApprovalPanel, _approvedPanel, _rejectedPanel })
        {
            foreach (Border row in panel.Children)
            {
                if (rowsChecked >= maxRows) break;
                if (row.Tag is not CommitApprovalItem item) continue;

                var text = CleanDescription(item.Description);
                bool isBold = item.TouchesDecisionsFile;
                bool hasSha = item.CommitUrl is not null;

                var textWidth = MeasureTextWidth(text, referenceElement, isBold);
                double rowWidth = textWidth + (hasSha ? 100 : 40); // per-row chrome
                maxRowContentWidth = Math.Max(maxRowContentWidth, rowWidth);
                rowsChecked++;
            }
            if (rowsChecked >= maxRows) break;
        }

        if (maxRowContentWidth <= 0) return null;

        const double panelChrome = 43; // padding + border + scrollbar
        return maxRowContentWidth + panelChrome;
    }

    public double GetMaximumUsefulHeight()
    {
        const double titleRow      = 40;
        const double approvalRowH  = 40;
        const double cap           = 320;
        const double floor         = 120;

        int count = 0;
        foreach (var panel in new[] { _needsApprovalPanel, _approvedPanel, _rejectedPanel })
            foreach (var child in panel.Children)
                if (child is Border { Tag: CommitApprovalItem }) count++;

        double h = titleRow + count * approvalRowH + 24;
        return Math.Clamp(h, floor, cap);
    }

    private static double MeasureTextWidth(string text, FrameworkElement referenceElement, bool isBold)
    {
        var fontFamily = SystemFonts.MessageFontFamily;
        var fontSize = referenceElement.TryFindResource("FontSizeBody") is double fs ? fs : 13.0;
        var typeface = new Typeface(fontFamily, FontStyles.Normal, isBold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        var pixelsPerDip = VisualTreeHelper.GetDpi(referenceElement).PixelsPerDip;
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            pixelsPerDip);
        return ft.Width;
    }

    // ── Filter ────────────────────────────────────────────────────────────────

    private string _filterText = string.Empty;

    /// <summary>Applies a case-insensitive substring filter across all three sections.
    /// Pass an empty string to clear the filter and show everything.</summary>
    public void SetFilter(string filterText) {
        _filterText = filterText.Trim();
        ApplyFilterToPanel(_needsApprovalPanel);
        ApplyFilterToPanel(_approvedPanel);
        ApplyFilterToPanel(_rejectedPanel);
    }

    private bool MatchesFilter(CommitApprovalItem item) {
        if (string.IsNullOrEmpty(_filterText)) return true;
        return item.Description.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyFilterToPanel(StackPanel panel) {
        foreach (UIElement child in panel.Children) {
            if (child is Border row && row.Tag is CommitApprovalItem item)
                row.Visibility = MatchesFilter(item) ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
