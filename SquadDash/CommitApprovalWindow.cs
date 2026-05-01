namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Linq;
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

    private readonly StackPanel _needsApprovalPanel;
    private readonly StackPanel _approvedPanel;
    private readonly StackPanel _rejectedPanel;
    private readonly UIElement  _rejectedSection;

    private Border?    _selectedRow;
    private bool       _showRejected;
    private MenuItem?  _toggleRejectedItem;

    public CommitApprovalPanel(
        StackPanel                               needsApprovalPanel,
        StackPanel                               approvedPanel,
        StackPanel                               rejectedPanel,
        UIElement                                rejectedSection,
        Border                                   outerBorder,
        Action<string>                            navigateUrl,
        Action<CommitApprovalItem>                scrollToTurn,
        Action<CommitApprovalItem>                onItemChanged,
        Action<IReadOnlyList<CommitApprovalItem>> onItemsRemoved) {
        _needsApprovalPanel = needsApprovalPanel;
        _approvedPanel      = approvedPanel;
        _rejectedPanel      = rejectedPanel;
        _rejectedSection    = rejectedSection;
        _navigateUrl        = navigateUrl;
        _scrollToTurn       = scrollToTurn;
        _onItemChanged      = onItemChanged;
        _onItemsRemoved     = onItemsRemoved;

        AttachPanelContextMenu(outerBorder);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void AddItem(CommitApprovalItem item) {
        // Newest items go to the top
        _needsApprovalPanel.Children.Insert(0, BuildRow(item));
    }

    public void ReplaceAllItems(IReadOnlyList<CommitApprovalItem> items) {
        _selectedRow = null;
        _needsApprovalPanel.Children.Clear();
        _approvedPanel.Children.Clear();
        _rejectedPanel.Children.Clear();
        // Newest first in every section
        foreach (var item in items.OrderByDescending(i => i.TurnStartedAt)) {
            if (item.IsRejected)
                _rejectedPanel.Children.Add(BuildRejectedRow(item));
            else if (item.IsApproved)
                _approvedPanel.Children.Add(BuildRow(item));
            else
                _needsApprovalPanel.Children.Add(BuildRow(item));
        }
    }

    public void OnClearApprovedClicked() {
        var removed = new List<CommitApprovalItem>(_approvedPanel.Children.Count);
        foreach (Border row in _approvedPanel.Children) {
            if (row.Tag is CommitApprovalItem item)
                removed.Add(item);
        }
        _approvedPanel.Children.Clear();
        if (removed.Count > 0)
            _onItemsRemoved(removed);
    }

    // ── Panel context menu ────────────────────────────────────────────────────

    private void AttachPanelContextMenu(Border outerBorder) {
        var menu = new ContextMenu();
        _toggleRejectedItem = new MenuItem();
        UpdateToggleHeader();
        _toggleRejectedItem.Click += (_, _) => {
            _showRejected = !_showRejected;
            _rejectedSection.Visibility = _showRejected ? Visibility.Visible : Visibility.Collapsed;
            UpdateToggleHeader();
        };
        menu.Items.Add(_toggleRejectedItem);
        outerBorder.ContextMenu = menu;
    }

    private void UpdateToggleHeader() {
        if (_toggleRejectedItem is not null)
            _toggleRejectedItem.Header = _showRejected ? "Hide Rejected" : "Show Rejected";
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

        var menu = new ContextMenu();
        var rejectItem = new MenuItem { Header = $"Reject {DescriptionPreview(item.Description)}" };
        rejectItem.Click += (_, _) => HandleRejectClicked(row, item);
        menu.Items.Add(rejectItem);
        row.ContextMenu = menu;

        var grid = new Grid { Margin = new Thickness(4, 2, 4, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var checkBox = new CheckBox {
            IsChecked         = item.IsApproved,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(checkBox, 0);
        grid.Children.Add(checkBox);

        var descBlock = new TextBlock {
            Text              = item.Description,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(6, 0, 0, 0),
            Cursor            = Cursors.Hand,
        };
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

        if (item.CommitUrl is not null) {
            var sha = BuildShaBlock(item);
            Grid.SetColumn(sha, 2);
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

        var menu = new ContextMenu();
        var unrejectItem = new MenuItem { Header = "Unreject" };
        unrejectItem.Click += (_, _) => HandleUnrejectClicked(row, item);
        menu.Items.Add(unrejectItem);
        row.ContextMenu = menu;

        var grid = new Grid { Margin = new Thickness(4, 2, 4, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(BuildRedX());

        var descBlock = new TextBlock {
            Text              = item.Description,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(6, 0, 0, 0),
            Opacity           = 0.6,
        };
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

    private void HandleCheckChanged(Border row, CommitApprovalItem item, bool isApproved) {
        if (_selectedRow == row) _selectedRow = null;
        var updated     = item with { IsApproved = isApproved };
        var sourcePanel = isApproved ? _needsApprovalPanel : _approvedPanel;
        var targetPanel = isApproved ? _approvedPanel      : _needsApprovalPanel;

        sourcePanel.Children.Remove(row);
        InsertSorted(targetPanel, BuildRow(updated), updated);

        _onItemChanged(updated);
    }

    private void HandleRejectClicked(Border row, CommitApprovalItem item) {
        if (_selectedRow == row) _selectedRow = null;
        var updated     = item with { IsApproved = false, IsRejected = true };
        var sourcePanel = item.IsApproved ? _approvedPanel : _needsApprovalPanel;

        sourcePanel.Children.Remove(row);
        InsertSorted(_rejectedPanel, BuildRejectedRow(updated), updated);

        _onItemChanged(updated);
    }

    private void HandleUnrejectClicked(Border row, CommitApprovalItem item) {
        var updated = item with { IsRejected = false, IsApproved = false };
        _rejectedPanel.Children.Remove(row);
        InsertSorted(_needsApprovalPanel, BuildRow(updated), updated);
        _onItemChanged(updated);
    }

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
}
