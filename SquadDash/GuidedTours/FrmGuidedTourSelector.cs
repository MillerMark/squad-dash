using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SquadDash.GuidedTours;

namespace SquadDash;

/// <summary>
/// Floating themed dialog that lets the user pick a guided tour to start.
/// </summary>
internal sealed class FrmGuidedTourSelector : ChromedWindow
{
    private readonly List<GuidedTour>   _allTours;
    private readonly Func<string, bool> _isCompleted;
    private readonly ListBox            _tourList;
    private readonly TextBox            _filterBox;
    private readonly Button             _startButton;

    /// <summary>
    /// The tour selected by the user, or <c>null</c> if the dialog was cancelled.
    /// </summary>
    public GuidedTour? SelectedTour { get; private set; }

    public FrmGuidedTourSelector(List<GuidedTour> tours, Func<string, bool>? isCompleted = null)
        : base(captionHeight: 36, resizeMode: ResizeMode.NoResize, resizeBorderThickness: 0)
    {
        _allTours    = tours;
        _isCompleted = isCompleted ?? (_ => false);

        Title                 = "Select a Guided Tour";
        Width                 = 420;
        Height                = 480;
        ShowInTaskbar         = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost               = true;

        var contentArea = ApplyOuterBorder("AppSurface", "Select a Guided Tour");

        // ── Filter box (with inline placeholder) ─────────────────────────────
        _filterBox = new TextBox
        {
            Height  = 28,
            Padding = new Thickness(6, 4, 6, 4),
        };
        _filterBox.SetResourceReference(TextBox.BackgroundProperty,   "TextBoxBackground");
        _filterBox.SetResourceReference(TextBox.BorderBrushProperty,  "InputBorder");
        _filterBox.SetResourceReference(TextBox.ForegroundProperty,   "LabelText");
        _filterBox.SetResourceReference(TextBox.FontSizeProperty,     "FontSizeBody");
        _filterBox.TextChanged += (_, _) => ApplyFilter();

        var placeholderBlock = new TextBlock
        {
            Text              = "Filter tours...",
            IsHitTestVisible  = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(8, 0, 0, 0),
        };
        placeholderBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        placeholderBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");

        // Hide placeholder when filter has text
        _filterBox.TextChanged += (_, _) =>
            placeholderBlock.Visibility = string.IsNullOrEmpty(_filterBox.Text)
                ? Visibility.Visible : Visibility.Hidden;

        var filterGrid = new Grid { Margin = new Thickness(12, 12, 12, 6) };
        filterGrid.Children.Add(_filterBox);
        filterGrid.Children.Add(placeholderBlock);

        // ── Tour list ────────────────────────────────────────────────────────
        _tourList = new ListBox { Margin = new Thickness(12, 0, 12, 8) };
        ScrollViewer.SetHorizontalScrollBarVisibility(_tourList, ScrollBarVisibility.Disabled);
        _tourList.SetResourceReference(ListBox.BackgroundProperty,   "AppSurface");
        _tourList.SetResourceReference(ListBox.BorderBrushProperty,  "InputBorder");
        _tourList.SetResourceReference(ListBox.ForegroundProperty,   "LabelText");
        _tourList.SelectionChanged  += (_, _) => UpdateStartButton();
        _tourList.MouseDoubleClick  += (_, _) => CommitSelection();
        _tourList.ItemContainerStyle = BuildListItemStyle();

        // ── Buttons ──────────────────────────────────────────────────────────
        _startButton = new Button
        {
            Content   = "Start Tour",
            Width     = 90,
            Height    = 28,
            IsEnabled = false,
            Margin    = new Thickness(0, 0, 8, 0),
        };
        _startButton.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        _startButton.Click += (_, _) => CommitSelection();

        var cancelButton = new Button { Content = "Cancel", Width = 70, Height = 28 };
        cancelButton.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        cancelButton.Click += (_, _) => { SelectedTour = null; Close(); };

        var buttonRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(12, 0, 12, 12),
        };
        buttonRow.Children.Add(_startButton);
        buttonRow.Children.Add(cancelButton);

        // ── Layout ───────────────────────────────────────────────────────────
        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(filterGrid,  Dock.Top);
        layout.Children.Add(filterGrid);
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        layout.Children.Add(buttonRow);
        layout.Children.Add(_tourList);

        contentArea.Child = layout;

        PopulateList(_allTours);
        if (_tourList.Items.Count == 1)
            _tourList.SelectedIndex = 0;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { SelectedTour = null; Close(); }
            if (e.Key == Key.Enter && _startButton.IsEnabled) CommitSelection();
        };
    }

    // ── Public factory ───────────────────────────────────────────────────────

    /// <summary>
    /// Shows the selector as a modal dialog. Returns the chosen tour, or null if cancelled.
    /// </summary>
    internal static GuidedTour? ShowForResult(Window owner, List<GuidedTour> tours, Func<string, bool>? isCompleted = null)
    {
        var dlg = new FrmGuidedTourSelector(tours, isCompleted) { Owner = owner };
        dlg.ShowDialog();
        return dlg.SelectedTour;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        var filter = _filterBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allTours
            : _allTours.Where(t =>
                t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        PopulateList(filtered);
    }

    private void PopulateList(List<GuidedTour> tours)
    {
        _tourList.Items.Clear();
        foreach (var tour in tours)
            _tourList.Items.Add(BuildTourItem(tour, _isCompleted(tour.Id)));

        if (_tourList.Items.Count == 1)
            _tourList.SelectedIndex = 0;

        UpdateStartButton();
    }

    private static UIElement BuildTourItem(GuidedTour tour, bool completed)
    {
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };

        if (completed)
        {
            var checkMark = new TextBlock
            {
                Text              = "✓ ",
                FontWeight        = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            checkMark.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
            checkMark.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            nameRow.Children.Add(checkMark);
        }

        var nameBlock = new TextBlock
        {
            Text         = tour.Name,
            FontWeight   = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        nameBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        nameRow.Children.Add(nameBlock);

        var descBlock = new TextBlock
        {
            Text         = tour.Description,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 2, 0, 0),
        };
        descBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
        descBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        var panel = new StackPanel { Margin = new Thickness(2), Tag = tour };
        panel.Children.Add(nameRow);
        panel.Children.Add(descBlock);
        return panel;
    }

    private static Style BuildListItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(ListBoxItem.PaddingProperty,          new Thickness(6, 5, 6, 5)));
        style.Setters.Add(new Setter(ListBoxItem.MarginProperty,           new Thickness(0, 1, 0, 1)));
        style.Setters.Add(new Setter(ListBoxItem.CursorProperty,           Cursors.Hand));
        style.Setters.Add(new Setter(ListBoxItem.FocusVisualStyleProperty, null));

        // Use a simple rounded border template
        var template  = new ControlTemplate(typeof(ListBoxItem));
        var borderFef = new FrameworkElementFactory(typeof(Border));
        borderFef.Name = "Bd";
        borderFef.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        borderFef.SetValue(Border.PaddingProperty, new Thickness(4, 3, 4, 3));
        borderFef.SetResourceReference(Border.BackgroundProperty, "AppSurface");

        var cpFef = new FrameworkElementFactory(typeof(ContentPresenter));
        borderFef.AppendChild(cpFef);
        template.VisualTree = borderFef;

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
            Application.Current.TryFindResource("HoverSurface") ?? SystemColors.ControlBrush, "Bd"));
        template.Triggers.Add(hoverTrigger);

        var selectedTrigger = new Trigger { Property = Selector.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
            Application.Current.TryFindResource("InputSurface") ?? SystemColors.HighlightBrush, "Bd"));
        template.Triggers.Add(selectedTrigger);

        style.Setters.Add(new Setter(ListBoxItem.TemplateProperty, template));
        return style;
    }

    private GuidedTour? GetSelectedTour()
    {
        if (_tourList.SelectedItem is StackPanel panel && panel.Tag is GuidedTour tour)
            return tour;
        return null;
    }

    private void UpdateStartButton() =>
        _startButton.IsEnabled = GetSelectedTour() is not null;

    private void CommitSelection()
    {
        SelectedTour = GetSelectedTour();
        if (SelectedTour is not null)
            Close();
    }
}
