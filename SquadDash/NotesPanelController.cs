namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

/// <summary>Manages content in the inline Notes panel.</summary>
internal sealed class NotesPanelController {

    private readonly StackPanel          _listPanel;
    private readonly FrameworkElement    _scrollContainer;
    private readonly Action<NoteItem>         _openNote;
    private readonly Action<NoteItem>         _editNote;
    private readonly Action<NoteItem, string> _renameNote;
    private readonly Action<NoteItem>         _deleteNote;
    private readonly Action                   _newNote;
    private readonly Action<NoteItem>?        _attachFollowUp;
    private readonly Func<NoteItem, string>?  _loadPreview;

    private List<NoteItem> _notes = [];
    private string _filterText = string.Empty;
    private NotesSortOrder _sortOrder = NotesSortOrder.MostRecentOnTop;
    private Action<NotesSortOrder>? _onSortOrderChanged;

    // ── Construction ─────────────────────────────────────────────────────────

    public NotesPanelController(
        StackPanel               listPanel,
        FrameworkElement         scrollContainer,
        Action<NoteItem>         openNote,
        Action<NoteItem>         editNote,
        Action<NoteItem, string> renameNote,
        Action<NoteItem>         deleteNote,
        Action                   newNote,
        Action<NoteItem>?        attachFollowUp      = null,
        Func<NoteItem, string>?  loadPreview         = null,
        NotesSortOrder           initialSortOrder    = NotesSortOrder.MostRecentOnTop,
        Action<NotesSortOrder>?  onSortOrderChanged  = null) {

        _listPanel            = listPanel;
        _scrollContainer      = scrollContainer;
        _openNote             = openNote;
        _editNote             = editNote;
        _renameNote           = renameNote;
        _deleteNote           = deleteNote;
        _newNote              = newNote;
        _attachFollowUp       = attachFollowUp;
        _loadPreview          = loadPreview;
        _sortOrder            = initialSortOrder;
        _onSortOrderChanged   = onSortOrderChanged;

        AttachPanelContextMenu();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Refresh(IReadOnlyList<NoteItem> notes) {
        _notes = [.. notes];
        RebuildList();
    }

    public void AddNote(NoteItem note) {
        _notes.Insert(0, note);
        RebuildList();
    }

    public void SetFilter(string text) {
        _filterText = text.Trim();
        ApplyFilterToList();
    }

    // ── List construction─────────────────────────────────────────────────────

    private void RebuildList() {
        _listPanel.Children.Clear();

        if (_notes.Count == 0) {
            var empty = new TextBlock {
                Text         = "No notes yet",
                FontSize     = 12,
                FontStyle    = FontStyles.Italic,
                Margin       = new Thickness(4, 6, 4, 4),
                TextWrapping = TextWrapping.Wrap,
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            _listPanel.Children.Add(empty);
            return;
        }

        var sorted = _sortOrder == NotesSortOrder.Alphabetical
            ? _notes.OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase).ToList()
            : _notes.OrderByDescending(n => n.CreatedAt).ToList();

        foreach (var note in sorted)
            _listPanel.Children.Add(BuildRow(note));

        ApplyFilterToList();
    }

    private void ApplyFilterToList() {
        foreach (UIElement child in _listPanel.Children) {
            if (child is Border { Tag: NoteItem note })
                child.Visibility = PanelFilterHelper.Matches(note.Title, _filterText)
                    ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private Border BuildRow(NoteItem note) {
        var row = new Border {
            Background = Brushes.Transparent,
            Tag        = note,
            Cursor     = Cursors.Hand,
        };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverSurface");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

        var titleLabel = new TextBlock {
            Text         = note.Title,
            FontSize     = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth     = 240,
            Margin       = new Thickness(4, 4, 4, 4),
            Cursor       = Cursors.Hand,
        };
        titleLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        row.Child = titleLabel;

        // ── Hover tooltip ─────────────────────────────────────────────────────
        if (_loadPreview is not null)
        {
            var tooltip = new ToolTip { MaxWidth = 190, Placement = System.Windows.Controls.Primitives.PlacementMode.Right };
            tooltip.SetResourceReference(ToolTip.BackgroundProperty, "PopupSurface");

            tooltip.Opened += (_, _) =>
            {
                tooltip.Content = BuildTooltipContent(note);
            };
            row.ToolTip = tooltip;
            ToolTipService.SetInitialShowDelay(row, 600);
        }

        // Single click → open note
        row.MouseLeftButtonUp += (_, e) => {
            if (e.Source is TextBox) return; // don't open during rename
            _openNote(note);
        };

        // Right-click context menu
        row.ContextMenu = BuildRowContextMenu(note, row, titleLabel);

        return row;
    }

    private object BuildTooltipContent(NoteItem note)
    {
        var panel = new StackPanel { Margin = new Thickness(4, 4, 4, 6), MaxWidth = 178 };

        // Header: title — relative time
        var ts = DateTimeOffset.FromUnixTimeSeconds(note.CreatedAt);
        var relTime = StatusTimingPresentation.FormatRelativeTimestamp(ts);

        var headerPanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var titleBlock = new TextBlock
        {
            Text       = note.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize   = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth   = 110,
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        var separatorBlock = new TextBlock { Text = " — ", FontSize = 12 };
        separatorBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        var timeBlock = new TextBlock { Text = relTime, FontSize = 11 };
        timeBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        headerPanel.Children.Add(titleBlock);
        headerPanel.Children.Add(separatorBlock);
        headerPanel.Children.Add(timeBlock);
        panel.Children.Add(headerPanel);

        // Content preview
        var content = _loadPreview!(note);
        var preview = string.IsNullOrWhiteSpace(content) ? "(empty)" : TruncatePreview(content, 300);
        var bodyBlock = new TextBlock
        {
            Text         = preview,
            FontSize     = 11,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth     = 178,
        };
        bodyBlock.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        panel.Children.Add(bodyBlock);

        return panel;
    }

    private static string TruncatePreview(string text, int maxChars)
    {
        var trimmed = text.Trim();
        if (trimmed.Length <= maxChars) return trimmed;
        var cut = trimmed.LastIndexOf(' ', maxChars);
        return cut > 0 ? trimmed[..cut] + "…" : trimmed[..maxChars] + "…";
    }

    private ContextMenu BuildRowContextMenu(NoteItem note, Border row, TextBlock titleLabel) {
        var menu = MakeMenu();

        if (_attachFollowUp is not null)
        {
            var followUpItem = MakeItem("Add to chat");
            followUpItem.Click += (_, _) => _attachFollowUp(note);
            menu.Items.Add(followUpItem);
            menu.Items.Add(MakeSep());
        }

        var newItem = MakeItem("New Note");
        newItem.Click += (_, _) => _newNote();
        menu.Items.Add(newItem);

        menu.Items.Add(MakeSep());

        var renameItem = MakeItem("Rename");
        renameItem.Click += (_, _) => BeginInlineRename(note, row, titleLabel);
        menu.Items.Add(renameItem);

        var editItem = MakeItem("View/Edit\u2026");
        editItem.Click += (_, _) => _editNote(note);
        menu.Items.Add(editItem);

        var deleteItem = MakeItem("Delete\u2026");
        deleteItem.Click += (_, _) => ConfirmAndDelete(note);
        menu.Items.Add(deleteItem);

        menu.Items.Add(MakeSep());
        menu.Items.Add(BuildSortSubmenu());

        return menu;
    }

    // ── Rename ────────────────────────────────────────────────────────────────

    private void BeginInlineRename(NoteItem note, Border row, TextBlock titleLabel) {
        var textBox = new TextBox {
            Text        = note.Title,
            FontSize    = 12,
            BorderThickness = new Thickness(0),
            Padding     = new Thickness(4, 3, 4, 3),
            MaxWidth    = 240,
        };
        textBox.SetResourceReference(TextBox.BackgroundProperty, "InputSurface");
        textBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");

        row.Child = textBox;
        row.Cursor = Cursors.IBeam;
        textBox.SelectAll();
        textBox.Focus();

        void Commit() {
            var newTitle = textBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(newTitle))
                newTitle = note.Title;

            titleLabel.Text = newTitle;
            row.Child  = titleLabel;
            row.Cursor = Cursors.Hand;

            if (!string.Equals(newTitle, note.Title, StringComparison.Ordinal))
                _renameNote(note, newTitle);
        }

        void Cancel() {
            row.Child  = titleLabel;
            row.Cursor = Cursors.Hand;
        }

        textBox.LostFocus  += (_, _) => Commit();
        textBox.KeyDown    += (_, e) => {
            if (e.Key == Key.Enter)  { Commit(); e.Handled = true; }
            if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
        };
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    private void ConfirmAndDelete(NoteItem note) {
        var result = MessageBox.Show(
            Application.Current.MainWindow,
            $"Delete note \"{note.Title}\"?",
            "Delete Note",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            _deleteNote(note);
    }

    // ── Panel-level context menu ──────────────────────────────────────────────

    private void AttachPanelContextMenu() {
        var menu = MakeMenu();
        var newItem = MakeItem("New Note");
        newItem.Click += (_, _) => _newNote();
        menu.Items.Add(newItem);
        menu.Items.Add(MakeSep());
        menu.Items.Add(BuildSortSubmenu());
        _listPanel.ContextMenu = menu;
        // Also attach to the ScrollViewer (and its parent Grid) so right-clicking
        // anywhere in the panel — not just over existing note rows — shows the menu.
        if (_listPanel.Parent is FrameworkElement parent)
            parent.ContextMenu = menu;
        if (_listPanel.Parent is FrameworkElement { Parent: FrameworkElement grandParent })
            grandParent.ContextMenu = menu;
    }

    private MenuItem BuildSortSubmenu() {
        var sortMenu = MakeItem("Sort");

        var alphabetItem = MakeItem("Alphabetically");
        alphabetItem.IsCheckable = true;
        alphabetItem.IsChecked   = _sortOrder == NotesSortOrder.Alphabetical;

        var recentItem = MakeItem("Most Recent on Top");
        recentItem.IsCheckable = true;
        recentItem.IsChecked   = _sortOrder == NotesSortOrder.MostRecentOnTop;

        alphabetItem.Click += (_, _) => ApplySortOrder(NotesSortOrder.Alphabetical,   alphabetItem, recentItem);
        recentItem.Click   += (_, _) => ApplySortOrder(NotesSortOrder.MostRecentOnTop, alphabetItem, recentItem);

        sortMenu.Items.Add(alphabetItem);
        sortMenu.Items.Add(recentItem);
        return sortMenu;
    }

    private void ApplySortOrder(NotesSortOrder order, MenuItem alphabetItem, MenuItem recentItem) {
        _sortOrder = order;
        alphabetItem.IsChecked = order == NotesSortOrder.Alphabetical;
        recentItem.IsChecked   = order == NotesSortOrder.MostRecentOnTop;
        RebuildList();
        _onSortOrderChanged?.Invoke(order);
    }

    // ── Menu helpers ──────────────────────────────────────────────────────────

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
}
