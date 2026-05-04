namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

/// <summary>Manages content in the inline Notes panel.</summary>
internal sealed class NotesPanelController {

    private readonly StackPanel          _listPanel;
    private readonly Action<NoteItem>    _openNote;
    private readonly Action<NoteItem, string> _renameNote;
    private readonly Action<NoteItem>    _deleteNote;
    private readonly Action              _newNote;

    private List<NoteItem> _notes = [];

    // ── Construction ─────────────────────────────────────────────────────────

    public NotesPanelController(
        StackPanel               listPanel,
        Action<NoteItem>         openNote,
        Action<NoteItem, string> renameNote,
        Action<NoteItem>         deleteNote,
        Action                   newNote) {

        _listPanel  = listPanel;
        _openNote   = openNote;
        _renameNote = renameNote;
        _deleteNote = deleteNote;
        _newNote    = newNote;

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

    // ── List construction ─────────────────────────────────────────────────────

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

        foreach (var note in _notes)
            _listPanel.Children.Add(BuildRow(note));
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

        // Single click → open note
        row.MouseLeftButtonUp += (_, e) => {
            if (e.Source is TextBox) return; // don't open during rename
            _openNote(note);
        };

        // Right-click context menu
        row.ContextMenu = BuildRowContextMenu(note, row, titleLabel);

        return row;
    }

    private ContextMenu BuildRowContextMenu(NoteItem note, Border row, TextBlock titleLabel) {
        var menu = MakeMenu();

        var newItem = MakeItem("New Note");
        newItem.Click += (_, _) => _newNote();
        menu.Items.Add(newItem);

        menu.Items.Add(MakeSep());

        var renameItem = MakeItem("Rename");
        renameItem.Click += (_, _) => BeginInlineRename(note, row, titleLabel);
        menu.Items.Add(renameItem);

        menu.Items.Add(MakeSep());

        var deleteItem = MakeItem("Delete");
        deleteItem.Click += (_, _) => ConfirmAndDelete(note);
        menu.Items.Add(deleteItem);

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
        _listPanel.ContextMenu = menu;
        // Also attach to the ScrollViewer (and its parent Grid) so right-clicking
        // anywhere in the panel — not just over existing note rows — shows the menu.
        if (_listPanel.Parent is FrameworkElement parent)
            parent.ContextMenu = menu;
        if (_listPanel.Parent is FrameworkElement { Parent: FrameworkElement grandParent })
            grandParent.ContextMenu = menu;
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
