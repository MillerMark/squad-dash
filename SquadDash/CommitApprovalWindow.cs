namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

/// <summary>Manages content in the inline Commit Approvals panel.</summary>
internal sealed class CommitApprovalPanel {
    private readonly Action<string>                               _navigateUrl;
    private readonly Action<CommitApprovalItem>                   _scrollToTurn;
    private readonly Action<CommitApprovalItem>                   _onItemChanged;
    private readonly Action<IReadOnlyList<CommitApprovalItem>>    _onItemsRemoved;

    private readonly StackPanel _needsApprovalPanel;
    private readonly StackPanel _approvedPanel;

    public CommitApprovalPanel(
        StackPanel                               needsApprovalPanel,
        StackPanel                               approvedPanel,
        Action<string>                            navigateUrl,
        Action<CommitApprovalItem>                scrollToTurn,
        Action<CommitApprovalItem>                onItemChanged,
        Action<IReadOnlyList<CommitApprovalItem>> onItemsRemoved) {
        _needsApprovalPanel = needsApprovalPanel;
        _approvedPanel      = approvedPanel;
        _navigateUrl        = navigateUrl;
        _scrollToTurn       = scrollToTurn;
        _onItemChanged      = onItemChanged;
        _onItemsRemoved     = onItemsRemoved;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void AddItem(CommitApprovalItem item) {
        _needsApprovalPanel.Children.Add(BuildRow(item));
    }

    public void ReplaceAllItems(IReadOnlyList<CommitApprovalItem> items) {
        _needsApprovalPanel.Children.Clear();
        _approvedPanel.Children.Clear();
        foreach (var item in items) {
            var target = item.IsApproved ? _approvedPanel : _needsApprovalPanel;
            target.Children.Add(BuildRow(item));
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

    // ── Row construction ─────────────────────────────────────────────────────

    private Border BuildRow(CommitApprovalItem item) {
        var row = new Border { Background = Brushes.Transparent, Tag = item };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverSurface");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

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
        descBlock.MouseLeftButtonUp += (_, e) => { e.Handled = true; _scrollToTurn(item); };
        Grid.SetColumn(descBlock, 1);
        grid.Children.Add(descBlock);

        if (item.CommitUrl is not null) {
            var shaDisplay = item.CommitSha.Length >= 7 ? item.CommitSha[..7] : item.CommitSha;
            var shaBlock = new TextBlock {
                Cursor            = Cursors.Hand,
                Margin            = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var shaRun = new Run(shaDisplay) { TextDecorations = TextDecorations.Underline };
            shaRun.SetResourceReference(Run.ForegroundProperty, "SubtleText");
            shaBlock.Inlines.Add(shaRun);
            shaBlock.MouseLeftButtonUp += (_, e) => { e.Handled = true; _navigateUrl(item.CommitUrl); };
            Grid.SetColumn(shaBlock, 2);
            grid.Children.Add(shaBlock);
        }

        // Wire checkbox after IsChecked is set so construction doesn't fire handlers
        checkBox.Checked   += (_, _) => HandleCheckChanged(row, item, isApproved: true);
        checkBox.Unchecked += (_, _) => HandleCheckChanged(row, item, isApproved: false);

        row.Child = grid;
        return row;
    }

    private void HandleCheckChanged(Border row, CommitApprovalItem item, bool isApproved) {
        var updated     = item with { IsApproved = isApproved };
        var sourcePanel = isApproved ? _needsApprovalPanel : _approvedPanel;
        var targetPanel = isApproved ? _approvedPanel      : _needsApprovalPanel;

        sourcePanel.Children.Remove(row);
        targetPanel.Children.Add(BuildRow(updated));

        _onItemChanged(updated);
    }
}
