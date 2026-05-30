#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SquadDash.PanelDocking;

/// <summary>
/// Manages the current panel layout and moves panel controls between dock zones.
/// </summary>
internal sealed class PanelDockingService
{
    private string? _workspacePath;

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
                AddToZone(_leftZonePanel!, element, panelId, _leftZoneScrollViewer as FrameworkElement);
                ExpandZone(_leftZoneColumn!, _leftSplitterColumn!, _leftZoneScrollViewer!, _leftZoneSplitter!, element);
                break;

            case DockZone.Right:
                AddToZone(_rightZonePanel!, element, panelId, _rightZoneScrollViewer as FrameworkElement);
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
        if (LogicalTreeHelper.GetParent(element) is Panel parent)
            parent.Children.Remove(element);
    }

    private void AddToZone(StackPanel zone, FrameworkElement element, string panelId, FrameworkElement? scrollViewer)
    {
        // Save the original height binding before we replace it (only once per panel).
        if (!_savedHeightBindings.ContainsKey(panelId))
            _savedHeightBindings[panelId] = BindingOperations.GetMultiBinding(element, FrameworkElement.HeightProperty);

        element.ClearValue(Grid.ColumnProperty);
        element.ClearValue(FrameworkElement.MarginProperty);
        element.ClearValue(FrameworkElement.MaxWidthProperty);
        element.ClearValue(FrameworkElement.VerticalAlignmentProperty);

        // Bind height to the zone scroll viewer so the panel fills the full column height.
        // If no scroll viewer ref is available, just free the height constraint.
        if (scrollViewer is not null)
            element.SetBinding(FrameworkElement.HeightProperty,
                new Binding(nameof(FrameworkElement.ActualHeight)) { Source = scrollViewer });
        else
            element.ClearValue(FrameworkElement.HeightProperty);

        zone.Children.Add(element);
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
        _topZoneGrid.Children.Add(element);
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
        var filePath = LayoutFilePath(workspacePath);
        if (!File.Exists(filePath))
        {
            CurrentLayout = DockLayout.CreateDefault();
            return CurrentLayout;
        }

        var file = ReadLayoutsFile(filePath);
        var layout = file.Layouts.FirstOrDefault(l =>
            string.Equals(l.Name, file.ActiveLayout, StringComparison.OrdinalIgnoreCase));

        CurrentLayout = layout is not null
            ? new DockLayout { Name = layout.Name, Slots = layout.Slots.ToList(), LeftZoneWidth = layout.LeftZoneWidth, RightZoneWidth = layout.RightZoneWidth }
            : DockLayout.CreateDefault();

        return CurrentLayout;
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
