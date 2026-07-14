using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private System.Windows.Controls.Image _mascotImage = null!;
    private CheckBox _showCompletedCheckBox = null!;
    private Action<GuidedTour>? _onTourSelected;

    /// <summary>
    /// The tour selected by the user, or <c>null</c> if the dialog was cancelled.
    /// </summary>
    public GuidedTour? SelectedTour { get; private set; }

    public FrmGuidedTourSelector(List<GuidedTour> tours, Func<string, bool>? isCompleted = null)
        : base(captionHeight: 36)
    {
        _allTours    = tours;
        _isCompleted = isCompleted ?? (_ => false);

        Title                 = "Select a Guided Tour";
        Width                 = 700;
        MinWidth              = 500;
        Height                = 560;
        MinHeight             = 420;
        ShowInTaskbar         = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;


        var contentArea = ApplyOuterBorder("AppSurface", "Select a Guided Tour");

        int completedCount = _allTours.Count(t => _isCompleted(t.Id));
        int mascotIndex = (completedCount % 8) + 1;
        string mascotPath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Assets", "GuidedTours", "Mascots", $"Mascot{mascotIndex}.png");

        var mascotImage = new System.Windows.Controls.Image
        {
            Height              = 265,
            Stretch             = System.Windows.Media.Stretch.Uniform,
            VerticalAlignment   = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin              = new Thickness(16, 16, 16, 16),
        };
        if (System.IO.File.Exists(mascotPath))
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.UriSource   = new Uri(mascotPath, UriKind.Absolute);
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();
            mascotImage.Source = bmp;
        }
        _mascotImage = mascotImage;

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

        // ── Context menu (Mark as complete / Mark as unseen) ─────────────────
        var markCompleteItem = new MenuItem { Header = "Mark as complete" };
        var markUnseenItem   = new MenuItem { Header = "Mark as unseen" };

        markCompleteItem.Click += (_, _) =>
        {
            if (GetSelectedTour() is GuidedTour tour)
            {
                GuidedTourStateStore.Shared.MarkCompleted(tour.Id);
                RepopulateCurrentFilter();
            }
        };
        markUnseenItem.Click += (_, _) =>
        {
            if (GetSelectedTour() is GuidedTour tour)
            {
                GuidedTourStateStore.Shared.MarkUncompleted(tour.Id);
                RepopulateCurrentFilter();
            }
        };

        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(markCompleteItem);
        contextMenu.Items.Add(markUnseenItem);
        contextMenu.ContextMenuOpening += (_, _) =>
        {
            var tour = GetSelectedTour();
            if (tour is null)
            {
                markCompleteItem.Visibility = Visibility.Collapsed;
                markUnseenItem.Visibility   = Visibility.Collapsed;
                return;
            }
            bool completed = GuidedTourStateStore.Shared.IsCompleted(tour.Id);
            markCompleteItem.Visibility = completed ? Visibility.Collapsed : Visibility.Visible;
            markUnseenItem.Visibility   = completed ? Visibility.Visible   : Visibility.Collapsed;
        };
        _tourList.ContextMenu = contextMenu;

        // ── Show Completed checkbox ──────────────────────────────────────────
        _showCompletedCheckBox = new CheckBox
        {
            Content           = "Show completed tours",
            IsChecked         = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(8, 0, 8, 0),
        };
        _showCompletedCheckBox.SetResourceReference(CheckBox.ForegroundProperty, "LabelText");
        _showCompletedCheckBox.SetResourceReference(CheckBox.FontSizeProperty,   "FontSizeBody");
        // Scale the entire checkbox (glyph + label) up to match FontSizeLarge (15) from FontSizeBody (13)
        _showCompletedCheckBox.LayoutTransform = new System.Windows.Media.ScaleTransform(15.0 / 13.0, 15.0 / 13.0);
        _showCompletedCheckBox.Checked   += (_, _) => RepopulateCurrentFilter();
        _showCompletedCheckBox.Unchecked += (_, _) => RepopulateCurrentFilter();

        // ── Buttons ──────────────────────────────────────────────────────────
        _startButton = new Button
        {
            Content   = "Start Tour",
            Width     = 90,
            Height    = 28,
            IsEnabled = false,
            Margin    = new Thickness(0, 0, 8, 0),
        };
        _startButton.SetResourceReference(Button.StyleProperty,    "ThemedButtonStyle");
        _startButton.SetResourceReference(Button.FontSizeProperty, "FontSizeBody");
        _startButton.Click += (_, _) => CommitSelection();

        var cancelButton = new Button { Content = "Cancel", Width = 70, Height = 28 };
        cancelButton.SetResourceReference(Button.StyleProperty,    "ThemedButtonStyle");
        cancelButton.SetResourceReference(Button.FontSizeProperty, "FontSizeBody");
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

        // Wrap the tour list in a two-column grid with the mascot on the left
        var outerGrid = new Grid();
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220, GridUnitType.Pixel) });
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left column: checkbox row (aligned with filter box) + mascot below
        var checkboxRow = new Border { Height = 28, Margin = new Thickness(0, 12, 0, 6) };
        checkboxRow.Child = _showCompletedCheckBox;
        var leftPanel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(checkboxRow, Dock.Top);
        leftPanel.Children.Add(checkboxRow);
        leftPanel.Children.Add(_mascotImage);

        Grid.SetColumn(leftPanel, 0);
        Grid.SetColumn(layout, 1);
        outerGrid.Children.Add(leftPanel);
        outerGrid.Children.Add(layout);
        contentArea.Child = outerGrid;

        ApplyFilter();
        if (_tourList.Items.Count == 1)
            _tourList.SelectedIndex = 0;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { SelectedTour = null; Close(); }
            if (e.Key == Key.Enter && _startButton.IsEnabled) CommitSelection();
        };
    }

    // ── Public factories ─────────────────────────────────────────────────────

    /// <summary>
    /// Shows the selector as a modal dialog. Returns the chosen tour, or null if cancelled.
    /// </summary>
    internal static GuidedTour? ShowForResult(Window owner, List<GuidedTour> tours, Func<string, bool>? isCompleted = null)
    {
        var dlg = new FrmGuidedTourSelector(tours, isCompleted) { Owner = owner };
        dlg.ShowDialog();
        return dlg.SelectedTour;
    }

    /// <summary>
    /// Shows the selector as a modeless window. Calls <paramref name="onTourSelected"/> when the
    /// user confirms a selection; does nothing if the window is closed without selecting.
    /// </summary>
    internal static void ShowModeless(Window owner, List<GuidedTour> tours,
        Func<string, bool>? isCompleted,
        Action<GuidedTour> onTourSelected)
    {
        var dlg = new FrmGuidedTourSelector(tours, isCompleted) { Owner = owner };
        dlg._onTourSelected = onTourSelected;
        dlg.Show();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        var filter        = _filterBox.Text.Trim();
        bool showComplete = _showCompletedCheckBox.IsChecked == true;
        var filtered = _allTours
            .Where(t => showComplete || !GuidedTourStateStore.Shared.IsCompleted(t.Id))
            .Where(t => string.IsNullOrEmpty(filter) ||
                t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _tourList.Items.Clear();
        foreach (var tour in filtered)
            _tourList.Items.Add(BuildTourItem(tour, GuidedTourStateStore.Shared.IsCompleted(tour.Id)));
        if (_tourList.Items.Count == 1)
            _tourList.SelectedIndex = 0;
        RefreshMascot();
        UpdateStartButton();
    }

    private void RepopulateCurrentFilter()
    {
        var selectedTour  = GetSelectedTour();
        var filter        = _filterBox.Text.Trim();
        bool showComplete = _showCompletedCheckBox.IsChecked == true;
        var filtered = _allTours
            .Where(t => showComplete || !GuidedTourStateStore.Shared.IsCompleted(t.Id))
            .Where(t => string.IsNullOrEmpty(filter) ||
                t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _tourList.Items.Clear();
        foreach (var tour in filtered)
            _tourList.Items.Add(BuildTourItem(tour, GuidedTourStateStore.Shared.IsCompleted(tour.Id)));
        // Restore selection (item disappears when marked complete with checkbox off — that's by design)
        if (selectedTour is not null)
        {
            for (int i = 0; i < _tourList.Items.Count; i++)
            {
                if (_tourList.Items[i] is System.Windows.Controls.StackPanel p && p.Tag is GuidedTour t && t.Id == selectedTour.Id)
                { _tourList.SelectedIndex = i; break; }
            }
        }
        RefreshMascot();
        UpdateStartButton();
    }

    private void RefreshMascot()
    {
        int completedCount = _allTours.Count(t => GuidedTourStateStore.Shared.IsCompleted(t.Id));
        int mascotIndex    = (completedCount % 8) + 1;
        string mascotPath  = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Assets", "GuidedTours", "Mascots", $"Mascot{mascotIndex}.png");
        if (!System.IO.File.Exists(mascotPath)) return;
        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.UriSource   = new Uri(mascotPath, UriKind.Absolute);
        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bmp.EndInit();
        _mascotImage.Source = bmp;
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
        const double badgeHeight = 48.0;
        const double badgeWidth  = badgeHeight * 256.0 / 191.0;

        var badgeArea = new Border
        {
            Width             = badgeWidth,
            Height            = badgeHeight,
            Margin            = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        string badgeFile = completed ? "CompletedBadge.png" : "EmptyBadge.png";
        string imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                        "Assets", "GuidedTours", badgeFile);
        if (File.Exists(imagePath))
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource    = new Uri(imagePath, UriKind.Absolute);
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();

            var image = new System.Windows.Controls.Image
            {
                Source  = bmp,
                Width   = badgeWidth,
                Height  = badgeHeight,
                Stretch = Stretch.Uniform,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            badgeArea.Child = image;
        }

        var nameBlock = new TextBlock
        {
            Text              = tour.Name,
            FontWeight        = FontWeights.SemiBold,
            TextWrapping      = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeLarge");
        nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        var descBlock = new TextBlock
        {
            Text         = tour.Description,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 2, 0, 0),
        };
        descBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeNormal");
        descBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        var textColumn = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textColumn.Children.Add(nameBlock);
        textColumn.Children.Add(descBlock);

        var row = new StackPanel
        {
            Orientation       = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(2),
            Tag               = tour,
        };
        row.Children.Add(badgeArea);
        row.Children.Add(textColumn);
        return row;
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

        var selectedFocusedTrigger = new MultiTrigger();
        selectedFocusedTrigger.Conditions.Add(new Condition(Selector.IsSelectedProperty, true));
        selectedFocusedTrigger.Conditions.Add(new Condition(UIElement.IsKeyboardFocusWithinProperty, true));
        selectedFocusedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
            Application.Current.TryFindResource("FocusedSelectedItem") ?? SystemColors.HighlightBrush, "Bd"));
        template.Triggers.Add(selectedFocusedTrigger);

        var selectedUnfocusedTrigger = new MultiTrigger();
        selectedUnfocusedTrigger.Conditions.Add(new Condition(Selector.IsSelectedProperty, true));
        selectedUnfocusedTrigger.Conditions.Add(new Condition(UIElement.IsKeyboardFocusWithinProperty, false));
        selectedUnfocusedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
            Application.Current.TryFindResource("UnfocusedSelectedItem") ?? SystemColors.ControlBrush, "Bd"));
        template.Triggers.Add(selectedUnfocusedTrigger);

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
        {
            Close();
            _onTourSelected?.Invoke(SelectedTour);
        }
    }
}
