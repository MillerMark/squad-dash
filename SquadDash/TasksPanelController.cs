namespace SquadDash;

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Threading.Tasks;

/// <summary>Manages content in the inline Tasks panel.</summary>
internal sealed class TasksPanelController {

    private readonly StackPanel      _activePanel;
    private readonly StackPanel      _completedPanel;
    private readonly UIElement       _completedSection;
    private readonly Func<string?>   _getTasksPath;
    private readonly Action          _reloadPanel;
    private readonly Action          _editTasksAction;
    private readonly Func<string, Brush> _priorityDotColor;

    private bool      _showCompleted;
    private MenuItem? _toggleCompletedItem;

    // ── Construction ─────────────────────────────────────────────────────────

    public TasksPanelController(
        StackPanel           activePanel,
        StackPanel           completedPanel,
        UIElement            completedSection,
        Border               outerBorder,
        Func<string?>        getTasksPath,
        Action               editTasksAction,
        Func<string, Brush>  priorityDotColor,
        Action               reloadPanel) {

        _activePanel      = activePanel;
        _completedPanel   = completedPanel;
        _completedSection = completedSection;
        _getTasksPath     = getTasksPath;
        _editTasksAction  = editTasksAction;
        _priorityDotColor = priorityDotColor;
        _reloadPanel      = reloadPanel;

        AttachPanelContextMenu(outerBorder);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Refresh(TaskParseResult result) {
        _activePanel.Children.Clear();
        _completedPanel.Children.Clear();

        var openGroups = result.OpenGroups;
        var hasOpen    = openGroups.Any(g => g.Items.Count > 0);

        if (!hasOpen) {
            ShowEmptyInPanel("No open tasks");
        } else {
            foreach (var group in openGroups) {
                if (group.Items.Count == 0) continue;

                // Priority heading: colored dot + label
                var headingRow = new StackPanel {
                    Orientation = Orientation.Horizontal,
                    Margin      = new Thickness(0, 8, 0, 3),
                };
                var dot = new Ellipse {
                    Width             = 9,
                    Height            = 9,
                    Fill              = _priorityDotColor(group.Emoji),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(0, 0, 5, 0),
                };
                var headingLabel = new TextBlock {
                    Text              = group.Label,
                    FontSize          = 11,
                    FontWeight        = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                headingLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
                headingRow.Children.Add(dot);
                headingRow.Children.Add(headingLabel);
                _activePanel.Children.Add(headingRow);

                foreach (var item in group.Items)
                    _activePanel.Children.Add(BuildRow(item));
            }
        }

        foreach (var item in result.CompletedItems)
            _completedPanel.Children.Add(BuildDoneRow(item));
    }

    public void ShowEmpty(string message) => ShowEmptyInPanel(message);

    // ── Panel context menu ────────────────────────────────────────────────────

    private void AttachPanelContextMenu(Border outerBorder) {
        var menu = new ContextMenu();

        var editItem = new MenuItem { Header = "Edit Tasks" };
        editItem.Click += (_, _) => _editTasksAction();
        menu.Items.Add(editItem);

        _toggleCompletedItem = new MenuItem();
        UpdateToggleHeader();
        _toggleCompletedItem.Click += (_, _) => {
            _showCompleted = !_showCompleted;
            _completedSection.Visibility = _showCompleted ? Visibility.Visible : Visibility.Collapsed;
            UpdateToggleHeader();
        };
        menu.Items.Add(_toggleCompletedItem);

        outerBorder.ContextMenu = menu;
    }

    private void UpdateToggleHeader() {
        if (_toggleCompletedItem is not null)
            _toggleCompletedItem.Header = _showCompleted ? "Hide Completed" : "Show Completed";
    }

    // ── Row construction — open tasks ─────────────────────────────────────────

    private Border BuildRow(TaskItem item) {
        var row = new Border { Background = Brushes.Transparent, Tag = item };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverSurface");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

        var menu           = new ContextMenu();
        var markCompleteItem = new MenuItem { Header = "Mark as Complete" };
        markCompleteItem.Click += (_, _) => _ = HandleMarkCompleteAsync(item, isDone: true);
        menu.Items.Add(markCompleteItem);
        row.ContextMenu = menu;

        var grid = new Grid { Margin = new Thickness(4, 3, 4, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var checkBox = new CheckBox {
            IsEnabled         = item.IsUserOwned,
            IsChecked         = false,
            VerticalAlignment = VerticalAlignment.Top,
            Margin            = new Thickness(0, 1, 6, 0),
        };
        Grid.SetColumn(checkBox, 0);
        grid.Children.Add(checkBox);

        var label = new TextBlock {
            Text         = item.Text,
            FontSize     = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth     = 220,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        // Wire after IsChecked is set so construction doesn't fire the handler
        checkBox.Checked += (_, _) => _ = HandleMarkCompleteAsync(item, isDone: true);

        row.Child = grid;
        return row;
    }

    // ── Row construction — done tasks ─────────────────────────────────────────

    private Border BuildDoneRow(TaskItem item) {
        var row = new Border { Background = Brushes.Transparent, Tag = item };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverSurface");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

        var menu             = new ContextMenu();
        var markIncompleteItem = new MenuItem { Header = "Mark as Incomplete" };
        markIncompleteItem.Click += (_, _) => _ = HandleMarkCompleteAsync(item, isDone: false);
        menu.Items.Add(markIncompleteItem);
        row.ContextMenu = menu;

        var label = new TextBlock {
            Text         = item.Text,
            FontSize     = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth     = 220,
            Opacity      = 0.6,
            TextDecorations = TextDecorations.Strikethrough,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");

        var wrapper = new Grid { Margin = new Thickness(4, 3, 4, 3) };
        wrapper.Children.Add(label);
        row.Child = wrapper;
        return row;
    }

    // ── Empty state ───────────────────────────────────────────────────────────

    private void ShowEmptyInPanel(string message) {
        _activePanel.Children.Clear();
        var empty = new TextBlock {
            Text       = message,
            FontSize   = 12,
            Margin     = new Thickness(0, 4, 0, 0),
            Opacity    = 0.6,
        };
        empty.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        _activePanel.Children.Add(empty);
    }

    // ── Write-back ────────────────────────────────────────────────────────────

    private async Task HandleMarkCompleteAsync(TaskItem item, bool isDone) {
        var path = _getTasksPath();
        if (path is null || !File.Exists(path)) return;

        var lines = await Task.Run(() => File.ReadAllLines(path));
        bool wrote = false;
        for (int i = 0; i < lines.Length; i++) {
            if (lines[i].TrimEnd() == item.RawLine) {
                if (isDone)
                    lines[i] = lines[i].Replace("- [ ]", "- [x]", StringComparison.Ordinal);
                else
                    lines[i] = lines[i].Replace("- [x]", "- [ ]", StringComparison.Ordinal);
                await Task.Run(() => File.WriteAllLines(path, lines));
                wrote = true;
                break;
            }
        }

        if (wrote)
            _reloadPanel();
    }
}
