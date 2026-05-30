#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SquadDash.PanelDocking;

/// <summary>
/// Manages the current panel layout and moves panel controls between dock zones.
/// </summary>
internal sealed class PanelDockingService
{
    private string? _workspacePath;

    // WPF context — null when running under unit tests.
    private readonly Dictionary<string, FrameworkElement>? _panelRegistry;
    private readonly Grid? _leftZonePanel;
    private readonly Grid? _rightZonePanel;
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
        ["loop"]        = 3,
        ["tasks"]       = 4,
        ["approvals"]   = 6,
        ["notes"]       = 7,
        ["maintenance"] = 8,
        ["inbox"]       = 9,
    };

    // Saves each panel's original XAML Height binding so it can be restored when the
    // panel moves back to the Top zone after having been in a Left/Right zone.
    private readonly Dictionary<string, MultiBinding?> _savedHeightBindings =
        new(StringComparer.OrdinalIgnoreCase);

    // Ordered lists of panels currently in each side zone (used to rebuild row layout).
    private readonly List<FrameworkElement> _leftZonePanels  = new();
    private readonly List<FrameworkElement> _rightZonePanels = new();

    /// <summary>Data-model-only constructor for unit tests.</summary>
    public PanelDockingService() { }

    /// <summary>Full constructor with WPF context for production use.</summary>
    public PanelDockingService(
        Dictionary<string, FrameworkElement> panelRegistry,
        Grid leftZonePanel,
        Grid rightZonePanel,
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

        // Zone-aware removal — side zones need Grid row cleanup; top zone is a plain Grid.
        if (sourceZone == DockZone.Left)
            RemoveFromZone(_leftZonePanel!, _leftZonePanels, element, _leftZoneScrollViewer as FrameworkElement);
        else if (sourceZone == DockZone.Right)
            RemoveFromZone(_rightZonePanel!, _rightZonePanels, element, _rightZoneScrollViewer as FrameworkElement);
        else
            RemoveFromTopZone(element);

        switch (targetZone)
        {
            case DockZone.Left:
                AddToZone(_leftZonePanel!, _leftZonePanels, element, panelId, _leftZoneScrollViewer as FrameworkElement);
                ExpandZone(_leftZoneColumn!, _leftSplitterColumn!, _leftZoneScrollViewer!, _leftZoneSplitter!, element);
                break;

            case DockZone.Right:
                AddToZone(_rightZonePanel!, _rightZonePanels, element, panelId, _rightZoneScrollViewer as FrameworkElement);
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

    private static void RemoveFromTopZone(FrameworkElement element)
    {
        if (LogicalTreeHelper.GetParent(element) is Panel parent)
            parent.Children.Remove(element);
    }

    private static void RemoveFromZone(Grid zone, List<FrameworkElement> zoneList, FrameworkElement element, FrameworkElement? scrollViewer)
    {
        zoneList.Remove(element);
        RebuildZoneGrid(zone, zoneList, scrollViewer);
    }

    private void AddToZone(Grid zone, List<FrameworkElement> zoneList, FrameworkElement element, string panelId, FrameworkElement? scrollViewer)
    {
        // Save the original height binding before we replace it (only once per panel).
        if (!_savedHeightBindings.ContainsKey(panelId))
            _savedHeightBindings[panelId] = BindingOperations.GetMultiBinding(element, FrameworkElement.HeightProperty);

        DetachFromCurrentPanelParent(element);

        element.ClearValue(Grid.ColumnProperty);
        element.ClearValue(FrameworkElement.MarginProperty);
        element.ClearValue(FrameworkElement.MaxWidthProperty);
        element.ClearValue(FrameworkElement.VerticalAlignmentProperty);
        element.ClearValue(FrameworkElement.HeightProperty); // row sizing handles height

        zoneList.Add(element);
        RebuildZoneGrid(zone, zoneList, scrollViewer);
    }

    /// <summary>
    /// Clears and rebuilds the side-zone Grid with one star row per panel and a
    /// 5 px <see cref="GridSplitter"/> row between consecutive panels.
    /// The Grid height is bound to <paramref name="scrollViewer"/> so star rows have
    /// a finite space to divide.
    /// </summary>
    private static void RebuildZoneGrid(Grid zone, List<FrameworkElement> panels, FrameworkElement? scrollViewer)
    {
        zone.Children.Clear();
        zone.RowDefinitions.Clear();

        for (int i = 0; i < panels.Count; i++)
        {
            if (i > 0)
            {
                zone.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
                var splitter = new GridSplitter
                {
                    Height = 5,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment   = VerticalAlignment.Center,
                    ResizeDirection     = GridResizeDirection.Rows,
                    ResizeBehavior      = GridResizeBehavior.PreviousAndNext,
                };
                splitter.SetResourceReference(Control.BackgroundProperty, "AppSurface");
                splitter.Opacity = 0.25;
                Grid.SetRow(splitter, zone.RowDefinitions.Count - 1);
                zone.Children.Add(splitter);
            }

            zone.RowDefinitions.Add(new RowDefinition
            {
                Height    = new GridLength(1, GridUnitType.Star),
                MinHeight = 100,
            });
            Grid.SetRow(panels[i], zone.RowDefinitions.Count - 1);
            zone.Children.Add(panels[i]);
        }

        // Bind the zone Grid height to the scroll viewer so star rows fill the column.
        BindingOperations.ClearBinding(zone, FrameworkElement.HeightProperty);
        if (scrollViewer is not null && panels.Count > 0)
            zone.SetBinding(FrameworkElement.HeightProperty,
                new Binding(nameof(FrameworkElement.ActualHeight)) { Source = scrollViewer });
    }

    private void AddToTopZone(string panelId, FrameworkElement element)
    {
        if (_topZoneGrid is null) return;
        if (!TopZoneColumnMap.TryGetValue(panelId, out int col)) return;

        // Remove any zone-height binding and restore the original Top-zone height constraint.
        element.ClearValue(FrameworkElement.HeightProperty);
        if (_savedHeightBindings.TryGetValue(panelId, out var saved) && saved is not null)
            BindingOperations.SetBinding(element, FrameworkElement.HeightProperty, saved);

        element.VerticalAlignment = VerticalAlignment.Top;
        element.Margin = new Thickness(14, 0, 0, 0);
        Grid.SetColumn(element, col);
        DetachFromCurrentPanelParent(element);
        _topZoneGrid.Children.Add(element);
    }

    private static void DetachFromCurrentPanelParent(FrameworkElement element)
    {
        if (LogicalTreeHelper.GetParent(element) is Panel logicalParent)
        {
            logicalParent.Children.Remove(element);
            return;
        }

        if (VisualTreeHelper.GetParent(element) is Panel visualParent)
            visualParent.Children.Remove(element);
    }

    private static void ExpandZone(
        ColumnDefinition zoneCol,
        ColumnDefinition splitterCol,
        UIElement scrollViewer,
        UIElement splitter,
        FrameworkElement arrivedPanel)
    {
        if (zoneCol.Width.IsAbsolute && zoneCol.Width.Value == 0)
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

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string LayoutFilePath(string workspacePath) =>
        Path.Combine(workspacePath, ".squad", "panel-layouts.json");

    /// <summary>
    /// Persists <see cref="CurrentLayout"/> (upserted by name) to
    /// <c>&lt;workspacePath&gt;/.squad/panel-layouts.json</c>.
    /// Creates the <c>.squad</c> directory if it does not yet exist.
    /// </summary>
    public void SaveLayout(string workspacePath)
    {
        _workspacePath = workspacePath;
        var filePath = LayoutFilePath(workspacePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var file = ReadLayoutsFile(filePath);
        file.ActiveLayout = CurrentLayout.Name;

        var idx = file.Layouts.FindIndex(l =>
            string.Equals(l.Name, CurrentLayout.Name, StringComparison.OrdinalIgnoreCase));

        var snapshot = new DockLayout
        {
            Name = CurrentLayout.Name,
            Slots = CurrentLayout.Slots.ToList(),
            LeftZoneWidth  = (_leftZoneColumn  is { } lc && lc.Width.IsAbsolute && lc.Width.Value > 0)
                             ? lc.Width.Value : (double?)null,
            RightZoneWidth = (_rightZoneColumn is { } rc && rc.Width.IsAbsolute && rc.Width.Value > 0)
                             ? rc.Width.Value : (double?)null,
        };

        if (idx >= 0)
            file.Layouts[idx] = snapshot;
        else
            file.Layouts.Add(snapshot);

        File.WriteAllText(filePath, JsonSerializer.Serialize(file, _jsonOptions));
    }

    /// <summary>
    /// Reads <c>&lt;workspacePath&gt;/.squad/panel-layouts.json</c>, sets
    /// <see cref="CurrentLayout"/> to the active layout, and returns it.
    /// Falls back to <see cref="DockLayout.CreateDefault"/> if the file is
    /// missing or the active layout name is not found.
    /// </summary>
    public DockLayout LoadLayout(string workspacePath)
    {
        _workspacePath = workspacePath;
        CurrentLayout = ReadActiveLayout(workspacePath);
        return CurrentLayout;
    }

    /// <summary>
    /// Reads and applies the active workspace layout, moving the live WPF panels from
    /// their current layout before replacing <see cref="CurrentLayout"/>.
    /// </summary>
    public DockLayout LoadAndApplyLayout(string workspacePath)
    {
        _workspacePath = workspacePath;
        var targetLayout = ReadActiveLayout(workspacePath);
        ApplyLayout(targetLayout);
        return CurrentLayout;
    }

    private void ApplyLayout(DockLayout targetLayout)
    {
        var targetSnapshot = CloneLayout(targetLayout);
        var currentSlots = CurrentLayout.Slots.ToList();

        foreach (var slot in currentSlots.Where(s => s.Zone != DockZone.Top))
            MovePanel(slot.PanelId, DockZone.Top);

        foreach (var slot in targetSnapshot.Slots.Where(s => s.Zone != DockZone.Top).OrderBy(s => s.Order))
            MovePanel(slot.PanelId, slot.Zone);

        CurrentLayout = targetSnapshot;
    }

    private static DockLayout CloneLayout(DockLayout layout) => new()
    {
        Name = layout.Name,
        Slots = layout.Slots.ToList(),
        LeftZoneWidth = layout.LeftZoneWidth,
        RightZoneWidth = layout.RightZoneWidth,
    };

    private static DockLayout ReadActiveLayout(string workspacePath)
    {
        var filePath = LayoutFilePath(workspacePath);
        if (!File.Exists(filePath))
        {
            return DockLayout.CreateDefault();
        }

        var file = ReadLayoutsFile(filePath);
        var layout = file.Layouts.FirstOrDefault(l =>
            string.Equals(l.Name, file.ActiveLayout, StringComparison.OrdinalIgnoreCase));

        return layout is not null
            ? CloneLayout(layout)
            : DockLayout.CreateDefault();
    }

    /// <summary>
    /// Renames <see cref="CurrentLayout"/> without saving to disk.
    /// Call <see cref="SaveLayout"/> afterwards to persist the new name.
    /// </summary>
    public void RenameCurrentLayout(string newName) =>
        CurrentLayout.Name = newName;

    private static PanelLayoutsFile ReadLayoutsFile(string filePath)
    {
        if (!File.Exists(filePath))
            return new PanelLayoutsFile();

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<PanelLayoutsFile>(json, _jsonOptions)
                   ?? new PanelLayoutsFile();
        }
        catch
        {
            return new PanelLayoutsFile();
        }
    }

    /// <summary>Returns the names of all layouts saved for the current workspace.</summary>
    public IReadOnlyList<string> SavedLayoutNames =>
        _workspacePath is null
            ? []
            : ReadLayoutsFile(LayoutFilePath(_workspacePath)).Layouts.Select(l => l.Name).ToList();

    /// <summary>Returns the zone that <paramref name="panelId"/> currently occupies.</summary>
    public DockZone GetCurrentZone(string panelId)
        => CurrentLayout.Slots.FirstOrDefault(s =>
               string.Equals(s.PanelId, panelId, StringComparison.OrdinalIgnoreCase))?.Zone
           ?? DockZone.Top;

    /// <summary>
    /// Registers a panel element at runtime so it can be moved by <see cref="MovePanel"/>.
    /// Adds a <see cref="PanelSlot"/> in <see cref="DockZone.Top"/> if the panel is not
    /// already tracked.
    /// </summary>
    public void RegisterPanel(string panelId, FrameworkElement panel)
    {
        _panelRegistry?.TryAdd(panelId, panel);

        if (!CurrentLayout.Slots.Any(s =>
                string.Equals(s.PanelId, panelId, StringComparison.OrdinalIgnoreCase)))
        {
            int nextOrder = CurrentLayout.Slots
                .Where(s => s.Zone == DockZone.Top)
                .Select(s => s.Order)
                .DefaultIfEmpty(-1)
                .Max() + 1;

            CurrentLayout.Slots = [.. CurrentLayout.Slots, new PanelSlot(panelId, DockZone.Top, nextOrder)];
        }
    }
}
