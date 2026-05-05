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
    private MenuItem?  _toggleRejectedItem;
    private MenuItem?  _toggleApprovedItem;
    private readonly Action<bool>? _onShowApprovedChanged;

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
        bool                                     initialShowApproved = true,
        Action<bool>?                            onShowApprovedChanged = null) {
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
        _showApproved              = initialShowApproved;
        _onShowApprovedChanged     = onShowApprovedChanged;

        AttachPanelContextMenu(outerBorder);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void AddItem(CommitApprovalItem item) {
        // Newest items go to the top
        var row = BuildRow(item);
        row.Visibility = MatchesFilter(item) ? Visibility.Visible : Visibility.Collapsed;
        _needsApprovalPanel.Children.Insert(0, row);
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
        ApplyFilterToPanel(_needsApprovalPanel);
        ApplyFilterToPanel(_approvedPanel);
        ApplyFilterToPanel(_rejectedPanel);
        SyncApprovedSectionVisibility();
    }

    public void OnClearApprovedClicked() {
        var removed = new List<CommitApprovalItem>(_approvedPanel.Children.Count);
        foreach (Border row in _approvedPanel.Children) {
            if (row.Tag is CommitApprovalItem item)
                removed.Add(item);
        }
        _approvedPanel.Children.Clear();
        SyncApprovedSectionVisibility();
        if (removed.Count > 0)
            _onItemsRemoved(removed);
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

    private void AttachPanelContextMenu(Border outerBorder) {
        var menu = MakeMenu();

        _toggleRejectedItem = MakeItem(string.Empty);
        _toggleApprovedItem = MakeItem(string.Empty);
        UpdateToggleHeaders();

        _toggleRejectedItem.Click += (_, _) => {
            _showRejected = !_showRejected;
            _rejectedSection.Visibility = _showRejected ? Visibility.Visible : Visibility.Collapsed;
            UpdateToggleHeaders();
        };

        _toggleApprovedItem.Click += (_, _) => {
            _showApproved = !_showApproved;
            _onShowApprovedChanged?.Invoke(_showApproved);
            SyncApprovedSectionVisibility();
            UpdateToggleHeaders();
        };

        menu.Items.Add(_toggleRejectedItem);
        menu.Items.Add(_toggleApprovedItem);
        outerBorder.ContextMenu = menu;
    }

    private void UpdateToggleHeaders() {
        if (_toggleRejectedItem is not null)
            _toggleRejectedItem.Header = _showRejected ? "Hide Rejected" : "Show Rejected";
        if (_toggleApprovedItem is not null)
            _toggleApprovedItem.Header = _showApproved ? "Hide Approved" : "Show Approved";
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
        var followUpItem = MakeItem("Follow up…");
        followUpItem.Click += (_, _) => _onFollowUp(item);
        menu.Items.Add(followUpItem);
        menu.Items.Add(new Separator());
        var rejectItem = MakeItem($"Reject {DescriptionPreview(item.Description)}");
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
            Text              = TruncateDescription(item.Description),
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(6, 0, 0, 0),
            Cursor            = Cursors.Hand,
            ToolTip           = BuildDescriptionTooltip(item),
        };
        ToolTipService.SetShowDuration(descBlock, 30000);
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
            Text              = TruncateDescription(item.Description),
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(6, 0, 0, 0),
            Opacity           = 0.6,
            ToolTip           = BuildDescriptionTooltip(item),
        };
        ToolTipService.SetShowDuration(descBlock, 30000);
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

        // When approving a near-bottom item while the list is already scrolled to the bottom,
        // the "Approved" header appearing shrinks the ScrollViewer and hides the remaining
        // bottom items. Detect this before modifying the panel, then re-scroll after layout.
        bool shouldScrollNeedsToBottom = false;
        if (isApproved) {
            int idx   = _needsApprovalPanel.Children.IndexOf(row);
            int count = _needsApprovalPanel.Children.Count;
            bool isNearBottom = idx >= 0 && idx >= count - 3;
            bool wasAtBottom  = _needsApprovalScrollViewer.ScrollableHeight > 0 &&
                                _needsApprovalScrollViewer.VerticalOffset >=
                                    _needsApprovalScrollViewer.ScrollableHeight - 2.0;
            shouldScrollNeedsToBottom = isNearBottom && wasAtBottom;
        }

        sourcePanel.Children.Remove(row);
        InsertSorted(targetPanel, BuildRow(updated), updated);
        ApplyFilterToPanel(targetPanel);
        SyncApprovedSectionVisibility();

        _onItemChanged(updated);

        if (shouldScrollNeedsToBottom) {
            _needsApprovalScrollViewer.Dispatcher.InvokeAsync(
                () => _needsApprovalScrollViewer.ScrollToBottom(),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void HandleRejectClicked(Border row, CommitApprovalItem item) {
        if (_selectedRow == row) _selectedRow = null;
        var updated     = item with { IsApproved = false, IsRejected = true };
        var sourcePanel = item.IsApproved ? _approvedPanel : _needsApprovalPanel;

        sourcePanel.Children.Remove(row);
        InsertSorted(_rejectedPanel, BuildRejectedRow(updated), updated);
        ApplyFilterToPanel(_rejectedPanel);
        SyncApprovedSectionVisibility();

        _onItemChanged(updated);
    }

    private void HandleUnrejectClicked(Border row, CommitApprovalItem item) {
        var updated = item with { IsRejected = false, IsApproved = false };
        _rejectedPanel.Children.Remove(row);
        InsertSorted(_needsApprovalPanel, BuildRow(updated), updated);
        ApplyFilterToPanel(_needsApprovalPanel);
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
        var cleaned = CommitPhraseSuffix.Replace(item.Description, string.Empty).Trim();

        var container = new StackPanel();

        var summaryBlock = new TextBlock {
            Text        = cleaned,
            FontWeight  = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
        };
        container.Children.Add(summaryBlock);

        string? rawPrompt = null;
        if (!string.IsNullOrWhiteSpace(item.OriginalPrompt))
            rawPrompt = item.OriginalPrompt.Trim();
        else if (!string.IsNullOrWhiteSpace(item.TurnPromptHint))
            rawPrompt = item.TurnPromptHint.Trim();

        if (rawPrompt is not null) {
            var promptText = DictationAnnotation.Replace(rawPrompt, string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(promptText) &&
                !promptText.Equals(cleaned, StringComparison.OrdinalIgnoreCase)) {
                container.Children.Add(new TextBlock {
                    Text         = promptText,
                    TextWrapping = TextWrapping.Wrap,
                    Margin       = new Thickness(0, 6, 0, 0),
                });
            }
        }

        var tooltip = new ToolTip { Content = container };
        tooltip.Opened += (_, _) =>
            container.MaxWidth = Math.Max(300, _needsApprovalPanel.ActualWidth * 3);
        return tooltip;
    }

    private static readonly Regex DictationAnnotation =
        new(@"\(some or all of this prompt was dictated by voice\)\s*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
