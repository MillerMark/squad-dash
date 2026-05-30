#nullable enable

using System.Windows;
using System.Windows.Controls;

namespace SquadDash.PanelDocking;

/// <summary>
/// Manages the current panel layout and moves panel controls between dock zones.
/// </summary>
internal sealed class PanelDockingService
{
    private readonly Dictionary<string, DockLayout> _savedLayouts = new(StringComparer.OrdinalIgnoreCase);

    // WPF context — null when running under unit tests.
    private readonly Dictionary<string, FrameworkElement>? _panelRegistry;
    private readonly StackPanel? _leftZonePanel;
    private readonly StackPanel? _rightZonePanel;
    private readonly Grid? _topZoneGrid;
    private readonly ColumnDefinition? _leftZoneColumn;
    private readonly ColumnDefinition? _rightZoneColumn;
    private readonly ColumnDefinition? _leftSplitterColumn;
    private readonly ColumnDefinition? _rightSplitterColumn;
    private readonly UIElement? _leftZoneScrollViewer;
    private readonly UIElement? _rightZoneScrollViewer;
    private readonly UIElement? _leftZoneSplitter;
    private readonly UIElement? _rightZoneSplitter;

    // Maps each dockable panel ID to its column index within TopZonePanelsGrid.
    private static readonly Dictionary<string, int> TopZoneColumnMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tasks"]       = 4,
        ["approvals"]   = 6,
        ["notes"]       = 7,
        ["maintenance"] = 8,
        ["inbox"]       = 9,
    };

    /// <summary>Data-model-only constructor for unit tests.</summary>
    public PanelDockingService() { }

    /// <summary>Full constructor with WPF context for production use.</summary>
    public PanelDockingService(
        Dictionary<string, FrameworkElement> panelRegistry,
        StackPanel leftZonePanel,
        StackPanel rightZonePanel,
        Grid topZoneGrid,
        ColumnDefinition leftZoneColumn,
        ColumnDefinition rightZoneColumn,
        ColumnDefinition leftSplitterColumn,
        ColumnDefinition rightSplitterColumn,
        UIElement leftZoneScrollViewer,
        UIElement rightZoneScrollViewer,
        UIElement leftZoneSplitter,
        UIElement rightZoneSplitter)
    {
        _panelRegistry = panelRegistry;
        _leftZonePanel = leftZonePanel;
        _rightZonePanel = rightZonePanel;
        _topZoneGrid = topZoneGrid;
        _leftZoneColumn = leftZoneColumn;
        _rightZoneColumn = rightZoneColumn;
        _leftSplitterColumn = leftSplitterColumn;
        _rightSplitterColumn = rightSplitterColumn;
        _leftZoneScrollViewer = leftZoneScrollViewer;
        _rightZoneScrollViewer = rightZoneScrollViewer;
        _leftZoneSplitter = leftZoneSplitter;
        _rightZoneSplitter = rightZoneSplitter;
    }

    /// <summary>The live panel layout for the current session.</summary>
    public DockLayout CurrentLayout { get; private set; } = DockLayout.CreateDefault();

    /// <summary>
    /// Moves <paramref name="panelId"/> to <paramref name="targetZone"/>, updating both
    /// the in-memory layout model and (when WPF context is present) the actual UI elements.
    /// </summary>
    public void MovePanel(string panelId, DockZone targetZone)
    {
        var existing = CurrentLayout.Slots.FirstOrDefault(s =>
            string.Equals(s.PanelId, panelId, StringComparison.OrdinalIgnoreCase));

        if (existing is not null && existing.Zone == targetZone)
            return;

        var sourceZone = existing?.Zone;

        // Update data model.
        var slots = CurrentLayout.Slots
            .Where(s => !string.Equals(s.PanelId, panelId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        int nextOrder = slots
            .Where(s => s.Zone == targetZone)
            .Select(s => s.Order)
            .DefaultIfEmpty(-1)
            .Max() + 1;

        slots.Add(new PanelSlot(panelId, targetZone, nextOrder));
        CurrentLayout.Slots = slots;

        // WPF reparenting (only when context is wired).
        if (_panelRegistry is null) return;
        if (!_panelRegistry.TryGetValue(panelId, out var element)) return;

        RemoveFromParent(element);

        switch (targetZone)
        {
            case DockZone.Left:
                AddToZone(_leftZonePanel!, element);
                ExpandZone(_leftZoneColumn!, _leftSplitterColumn!, _leftZoneScrollViewer!, _leftZoneSplitter!, element);
                break;

            case DockZone.Right:
                AddToZone(_rightZonePanel!, element);
                ExpandZone(_rightZoneColumn!, _rightSplitterColumn!, _rightZoneScrollViewer!, _rightZoneSplitter!, element);
                break;

            case DockZone.Top:
                AddToTopZone(panelId, element);
                break;
        }

        // Collapse source zone if it is now empty.
        if (sourceZone == DockZone.Left && !ZoneHasPanels(DockZone.Left))
            CollapseZone(_leftZoneColumn!, _leftSplitterColumn!, _leftZoneScrollViewer!, _leftZoneSplitter!);
        else if (sourceZone == DockZone.Right && !ZoneHasPanels(DockZone.Right))
            CollapseZone(_rightZoneColumn!, _rightSplitterColumn!, _rightZoneScrollViewer!, _rightZoneSplitter!);
    }

    private bool ZoneHasPanels(DockZone zone) =>
        CurrentLayout.Slots.Any(s => s.Zone == zone);

    private static void RemoveFromParent(FrameworkElement element)
    {
        switch (System.Windows.Media.VisualTreeHelper.GetParent(element))
        {
            case Grid g:
                g.Children.Remove(element);
                break;
            case StackPanel sp:
                sp.Children.Remove(element);
                break;
        }
    }

    private static void AddToZone(StackPanel zone, FrameworkElement element)
    {
        element.ClearValue(Grid.ColumnProperty);
        element.ClearValue(FrameworkElement.MarginProperty);
        element.ClearValue(FrameworkElement.MaxWidthProperty);
        zone.Children.Add(element);
    }

    private void AddToTopZone(string panelId, FrameworkElement element)
    {
        if (_topZoneGrid is null) return;
        if (!TopZoneColumnMap.TryGetValue(panelId, out int col)) return;

        element.Margin = new Thickness(14, 0, 0, 0);
        Grid.SetColumn(element, col);
        _topZoneGrid.Children.Add(element);
    }

    private static void ExpandZone(
        ColumnDefinition zoneCol,
        ColumnDefinition splitterCol,
        UIElement scrollViewer,
        UIElement splitter,
        FrameworkElement arrivedPanel)
    {
        if (zoneCol.Width.Value == 0)
        {
            double width = arrivedPanel.ActualWidth > 0 ? arrivedPanel.ActualWidth : 280;
            zoneCol.Width = new GridLength(width);
            splitterCol.Width = new GridLength(5);
            scrollViewer.Visibility = Visibility.Visible;
            splitter.Visibility = Visibility.Visible;
        }
    }

    private static void CollapseZone(
        ColumnDefinition zoneCol,
        ColumnDefinition splitterCol,
        UIElement scrollViewer,
        UIElement splitter)
    {
        zoneCol.Width = new GridLength(0);
        splitterCol.Width = new GridLength(0);
        scrollViewer.Visibility = Visibility.Collapsed;
        splitter.Visibility = Visibility.Collapsed;
    }

    /// <summary>Stores a snapshot of <see cref="CurrentLayout"/> under <paramref name="name"/>.</summary>
    public void SaveLayout(string name)
    {
        var snapshot = new DockLayout
        {
            Name = name,
            Slots = CurrentLayout.Slots.ToList()
        };
        _savedLayouts[name] = snapshot;
    }

    /// <summary>Restores a previously saved layout by name. Returns true if found.</summary>
    public bool LoadLayout(string name)
    {
        if (!_savedLayouts.TryGetValue(name, out var layout))
            return false;

        CurrentLayout = new DockLayout
        {
            Name = layout.Name,
            Slots = layout.Slots.ToList()
        };
        return true;
    }

    /// <summary>Returns the names of all layouts saved in this session.</summary>
    public IReadOnlyList<string> SavedLayoutNames =>
        _savedLayouts.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
}
