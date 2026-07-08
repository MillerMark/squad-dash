using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
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
        Width                 = 700;
        Height                = 480;
        ShowInTaskbar         = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost               = true;

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
        var badgeArea = new Border
        {
            Width             = 48,
            Height            = 48,
            Margin            = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (completed)
            badgeArea.Child = BuildCompletionBadge();

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

    private static UIElement BuildCompletionBadge()
    {
        const string xaml = """
            <Viewbox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Canvas Width="2378" Height="2377">
                    <Canvas>
                        <Canvas.Clip>
                            <PathGeometry Figures="M1189,2158.5C654.375,2158.5,219.5,1723.625,219.5,1189C219.5,654.375,654.375,219.5,1189,219.5C1723.625,219.5,2158.5,654.5,2158.5,1189C2158.5,1723.5,1723.625,2158.5,1189,2158.5z M1189,247.25C669.75,247.25,247.25,669.75,247.25,1189.125C247.25,1708.375,669.75,2130.875,1189,2130.875C1708.25,2130.875,2130.875,1708.375,2130.875,1189.125C2130.875,669.75,1708.375,247.25,1189,247.25z" />
                        </Canvas.Clip>
                        <Rectangle Width="1940" Height="1940" Canvas.Left="219" Canvas.Top="219">
                            <Rectangle.Fill>
                                <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
                                    <LinearGradientBrush.GradientStops>
                                        <GradientStop Color="#FF8B5E3C" Offset="0" />
                                        <GradientStop Color="#FFFBB040" Offset="0.250258531540848" />
                                        <GradientStop Color="#FFF4EEC2" Offset="0.54110651499482942" />
                                        <GradientStop Color="#FFFBB142" Offset="0.79860392967942084" />
                                        <GradientStop Color="#FFBD833E" Offset="0.9123578076525336" />
                                        <GradientStop Color="#FF8B5E3C" Offset="1" />
                                    </LinearGradientBrush.GradientStops>
                                </LinearGradientBrush>
                            </Rectangle.Fill>
                        </Rectangle>
                    </Canvas>
                    <Path Fill="#FF0D253B">
                        <Path.Data>
                            <PathGeometry Figures="M2184.5,1188.5C2184.5,1738.625,1738.875,2184.5,1189,2184.5C639.25,2184.5,193.5,1738.625,193.5,1188.5C193.5,638.5,639.25,192.5,1189,192.5C1738.875,192.5,2184.5,638.5,2184.5,1188.5" />
                        </Path.Data>
                    </Path>
                    <Path Fill="#FF9FCBFD">
                        <Path.Data>
                            <PathGeometry Figures="M405.1875,1330.6875L550.5625,1185.25C570.6875,1165.1875,603.3125,1165.3125,623.5625,1185.5L916.125,1478.0625 1705.75,688.4375C1725.8125,668.375,1758.5,668.4375,1778.6875,688.6875L1925.125,835.125C1945.375,855.3125,1945.5,888,1925.375,908.0625L1099.4375,1734.0625 1099.375,1734.0625 954,1879.5C933.9375,1899.5625,901.25,1899.4375,881.0625,1879.1875L405.4375,1403.625C385.25,1383.375,385.125,1350.75,405.1875,1330.6875z" />
                        </Path.Data>
                    </Path>
                    <Path Fill="#FF3794FB">
                        <Path.Data>
                            <PathGeometry Figures="M1924,852.375L1934.9375,868.875C1942.5,887.4375,1938.75,909.5625,1923.6875,924.625L1097.6875,1750.625 1097.6875,1750.625 952.3125,1896C932.25,1916.0625,899.625,1916,879.5,1895.875L405.875,1422.3125C390.8125,1407.1875,387,1385.0625,394.5,1366.5L405,1350.625 915.4375,1861z" />
                        </Path.Data>
                    </Path>
                    <Canvas>
                        <Canvas.Clip>
                            <PathGeometry Figures="M2093.25,1066.75L2097.875,1097.125C2101,1127.875,2102.5,1159,2102.5,1190.5C2102.5,1695.375,1693.5,2104.5,1188.875,2104.5C1157.375,2104.5,1126.125,2103,1095.5,2099.875L1021.5,2088.5 1107.625,2002.375 1188.875,2006.5C1639.375,2006.5,2004.5,1641.125,2004.5,1190.5L2002.875,1157.125z M1188.875,276.5C1346.625,276.5,1495,316.5,1624.375,386.875L1655.5,405.75 1584.25,477.125 1577.625,473.125C1462.125,410.25,1329.625,374.625,1188.875,374.625C794.75,374.625,465.875,654.375,389.75,1026.125L382.125,1076.75 275.5,1183.25 279.875,1097.125C326.75,636.25,715.75,276.5,1188.875,276.5z" />
                        </Canvas.Clip>
                        <Rectangle Width="1828" Height="1829" Canvas.Left="275" Canvas.Top="276">
                            <Rectangle.Fill>
                                <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
                                    <LinearGradientBrush.GradientStops>
                                        <GradientStop Color="#FF8B5E3C" Offset="0" />
                                        <GradientStop Color="#FFFBB040" Offset="0.25027442371020858" />
                                        <GradientStop Color="#FFF4EEC2" Offset="0.54116355653128434" />
                                        <GradientStop Color="#FFFBB142" Offset="0.79884742041712409" />
                                        <GradientStop Color="#FF8B5E3C" Offset="1" />
                                    </LinearGradientBrush.GradientStops>
                                </LinearGradientBrush>
                            </Rectangle.Fill>
                        </Rectangle>
                    </Canvas>
                    <Path Fill="#FFDEEDFE">
                        <Path.Data>
                            <PathGeometry Figures="M1749.5,666.1875C1762.6875,666.25,1775.9375,671.3125,1786,681.4375L1932.4375,827.875C1942.5625,838,1947.6875,851.1875,1947.6875,864.4375L1946.9375,868.375 1909.3125,868.375 1751.1875,710.3125 923.9375,1537.5625 588.3125,1201.9375 425.3125,1365 398.5625,1365 397.5625,1359.8125C397.5,1346.625,402.5,1333.4375,412.5,1323.4375L557.9375,1178C578,1157.9375,610.625,1158.0625,630.875,1178.25L923.4375,1470.8125 1713.0625,681.1875C1723.125,671.125,1736.3125,666.125,1749.5,666.1875z" />
                        </Path.Data>
                    </Path>
                </Canvas>
            </Viewbox>
            """;

        try
        {
            return (UIElement)XamlReader.Parse(xaml);
        }
        catch
        {
            // Fallback: plain green checkmark if XAML parse fails for any reason
            var fallback = new TextBlock
            {
                Text                = "✓",
                FontSize            = 20,
                FontWeight          = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };
            fallback.SetResourceReference(TextBlock.ForegroundProperty, "DiffAddedText");
            return fallback;
        }
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
            Close();
    }
}
