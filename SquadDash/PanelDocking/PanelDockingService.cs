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
    private readonly Grid? _left2ZonePanel;
    private readonly Grid? _right2ZonePanel;
    private readonly Grid? _topZoneGrid;
    private readonly ColumnDefinition? _leftZoneColumn;
    private readonly ColumnDefinition? _rightZoneColumn;
    private readonly ColumnDefinition? _left2ZoneColumn;
    private readonly ColumnDefinition? _right2ZoneColumn;
    private readonly ColumnDefinition? _leftSplitterColumn;
    private readonly ColumnDefinition? _rightSplitterColumn;
    private readonly ColumnDefinition? _left2SplitterColumn;
    private readonly ColumnDefinition? _right2SplitterColumn;
    private readonly UIElement? _leftZoneScrollViewer;
    private readonly UIElement? _rightZoneScrollViewer;
    private readonly UIElement? _left2ZoneScrollViewer;
    private readonly UIElement? _right2ZoneScrollViewer;
    private readonly UIElement? _leftZoneSplitter;
    private readonly UIElement? _rightZoneSplitter;
    private readonly UIElement? _left2ZoneSplitter;
    private readonly UIElement? _right2ZoneSplitter;

    // Maps each dockable panel ID to its column index within TopZonePanelsGrid.
    // Used only to validate which panels may live in the top zone.
    private static readonly Dictionary<string, int> TopZoneColumnMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["loop"]        = 3,
        ["tasks"]       = 4,
        ["approvals"]   = 6,
        ["notes"]       = 7,
        ["maintenance"] = 8,
        ["inbox"]       = 9,
    };

    // Physical grid columns available for dockable top-zone panels, in left-to-right order.
    // Column 5 is occupied by WatchPanelBorder (non-dockable) so it is skipped.
    private static readonly int[] TopZonePhysicalColumns = [3, 4, 6, 7, 8, 9];

    // Saves each panel's original XAML Height binding so it can be restored when the
    // panel moves back to the Top zone after having been in a Left/Right zone.
    private readonly Dictionary<string, MultiBinding?> _savedHeightBindings =
        new(StringComparer.OrdinalIgnoreCase);

    // Ordered lists of panels currently in each side zone (used to rebuild row layout).
    private readonly List<FrameworkElement> _leftZonePanels   = new();
    private readonly List<FrameworkElement> _rightZonePanels  = new();
    private readonly List<FrameworkElement> _left2ZonePanels  = new();
    private readonly List<FrameworkElement> _right2ZonePanels = new();

    /// <summary>Data-model-only constructor for unit tests.</summary>
    public PanelDockingService() { }

    /// <summary>Active test recorder; null when not recording.</summary>
    internal IDockingMoveRecorder? TestRecorder { get; set; }

    /// <summary>
    /// Builds a <see cref="PanelLayoutData"/> snapshot from the current layout.
    /// Uses <paramref name="visiblePanelIds"/> when provided; otherwise infers
    /// visibility from the WPF panel registry (non-collapsed = visible).
    /// </summary>
    public PanelLayoutData GetCurrentLayoutData(IReadOnlySet<string>? visiblePanelIds = null)
    {
        IReadOnlySet<string> visible;
        if (visiblePanelIds is not null)
            visible = visiblePanelIds;
        else if (_panelRegistry is not null)
            visible = CurrentLayout.Slots
                .Select(s => s.PanelId)
                .Where(id => _panelRegistry.TryGetValue(id, out var el) && el.Visibility != Visibility.Collapsed)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        else
            visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new PanelLayoutData
        {
            Slots          = CurrentLayout.Slots.ToList(),
            VisiblePanelIds = visible,
        };
    }

    /// <summary>Full constructor with WPF context for production use.</summary>
    public PanelDockingService(
        Dictionary<string, FrameworkElement> panelRegistry,
        Grid leftZonePanel,
        Grid rightZonePanel,
        Grid left2ZonePanel,
        Grid right2ZonePanel,
        Grid topZoneGrid,
        ColumnDefinition leftZoneColumn,
        ColumnDefinition rightZoneColumn,
        ColumnDefinition left2ZoneColumn,
        ColumnDefinition right2ZoneColumn,
        ColumnDefinition leftSplitterColumn,
        ColumnDefinition rightSplitterColumn,
        ColumnDefinition left2SplitterColumn,
        ColumnDefinition right2SplitterColumn,
        UIElement leftZoneScrollViewer,
        UIElement rightZoneScrollViewer,
        UIElement left2ZoneScrollViewer,
        UIElement right2ZoneScrollViewer,
        UIElement leftZoneSplitter,
        UIElement rightZoneSplitter,
        UIElement left2ZoneSplitter,
        UIElement right2ZoneSplitter)
    {
        _panelRegistry = panelRegistry;
        _leftZonePanel = leftZonePanel;
        _rightZonePanel = rightZonePanel;
        _left2ZonePanel = left2ZonePanel;
        _right2ZonePanel = right2ZonePanel;
        _topZoneGrid = topZoneGrid;
        _leftZoneColumn = leftZoneColumn;
        _rightZoneColumn = rightZoneColumn;
        _left2ZoneColumn = left2ZoneColumn;
        _right2ZoneColumn = right2ZoneColumn;
        _leftSplitterColumn = leftSplitterColumn;
        _rightSplitterColumn = rightSplitterColumn;
        _left2SplitterColumn = left2SplitterColumn;
        _right2SplitterColumn = right2SplitterColumn;
        _leftZoneScrollViewer = leftZoneScrollViewer;
        _rightZoneScrollViewer = rightZoneScrollViewer;
        _left2ZoneScrollViewer = left2ZoneScrollViewer;
        _right2ZoneScrollViewer = right2ZoneScrollViewer;
        _leftZoneSplitter = leftZoneSplitter;
        _rightZoneSplitter = rightZoneSplitter;
        _left2ZoneSplitter = left2ZoneSplitter;
        _right2ZoneSplitter = right2ZoneSplitter;
    }

    /// <summary>The live panel layout for the current session.</summary>
    public DockLayout CurrentLayout { get; private set; } = DockLayout.CreateDefault();

    /// <summary>
    /// Moves <paramref name="panelId"/> to <paramref name="targetZone"/> at position
    /// <paramref name="targetOrder"/>, updating both the in-memory layout model and (when
    /// WPF context is present) the actual UI elements.  When <paramref name="targetOrder"/>
    /// is negative the panel is appended at the end of the zone.
    /// Same-zone reordering is supported.
    /// </summary>
    public void MovePanel(string panelId, DockZone targetZone, int targetOrder = -1)
    {
        var existing = CurrentLayout.Slots.FirstOrDefault(s =>
            string.Equals(s.PanelId, panelId, StringComparison.OrdinalIgnoreCase));

        var sourceZone = existing?.Zone;
        bool sameZone  = existing is not null && existing.Zone == targetZone;

        // Reorder within the same zone — skip if no explicit order requested or already correct.
        if (sameZone)
        {
            if (targetOrder < 0) return; // no-op: same zone, no explicit reorder requested

            var zoneSlots = CurrentLayout.Slots
                .Where(s => s.Zone == targetZone)
                .OrderBy(s => s.Order)
                .Select(s => s.PanelId)
                .ToList();
            int currentIdx = zoneSlots.IndexOf(
                zoneSlots.FirstOrDefault(id => string.Equals(id, panelId, StringComparison.OrdinalIgnoreCase)) ?? "");
            int clampedTarget = targetOrder < 0 ? zoneSlots.Count - 1 : Math.Clamp(targetOrder, 0, zoneSlots.Count - 1);
            if (currentIdx == clampedTarget) return;

            // Reorder the list and reassign Order values using immutable record updates.
            zoneSlots.Remove(panelId);
            zoneSlots.Insert(Math.Clamp(clampedTarget, 0, zoneSlots.Count), panelId);
            CurrentLayout.Slots = CurrentLayout.Slots
                .Where(s => s.Zone != targetZone ||
                            !zoneSlots.Contains(s.PanelId, StringComparer.OrdinalIgnoreCase))
                .Concat(zoneSlots.Select((id, i) => new PanelSlot(id, targetZone, i)))
                .ToList();

            TestRecorder?.OnMoveCompleted(panelId, targetZone, targetOrder, GetCurrentLayoutData());

            // WPF: reorder the panel within the zone list and rebuild the grid.
            // For the Top zone, reassign physical columns; for side zones, rebuild the row grid.
            if (_panelRegistry is not null)
            {
                if (targetZone == DockZone.Top)
                {
                    RebuildTopZoneLayout();
                }
                else if (_panelRegistry.TryGetValue(panelId, out var el))
                {
                    var (zoneList, zoneGrid, scrollViewer) = GetZoneContext(targetZone);
                    if (zoneList is not null && zoneGrid is not null)
                    {
                        zoneList.Remove(el);
                        zoneList.Insert(Math.Clamp(clampedTarget, 0, zoneList.Count), el);
                        RebuildZoneGrid(zoneGrid, zoneList, scrollViewer as FrameworkElement);
                    }
                }
            }
            return;
        }

        // Cross-zone move — update data model.
        SquadDashTrace.Write(TraceCategory.Docking,
            $"MovePanel: {panelId} {sourceZone} → {targetZone} (requestedOrder={targetOrder})");

        var slots = CurrentLayout.Slots
            .Where(s => !string.Equals(s.PanelId, panelId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Determine insertion index within target zone.
        var targetZoneSlots = slots
            .Where(s => s.Zone == targetZone)
            .OrderBy(s => s.Order)
            .ToList();

        int insertAt = targetOrder < 0 ? targetZoneSlots.Count : Math.Clamp(targetOrder, 0, targetZoneSlots.Count);

        // Build ordered list of target zone panel IDs with the new panel inserted, then renumber.
        var targetIds = targetZoneSlots.Select(s => s.PanelId).ToList();
        targetIds.Insert(insertAt, panelId);

        // Replace all target-zone slots with freshly ordered records.
        slots = slots.Where(s => s.Zone != targetZone).ToList();
        slots.AddRange(targetIds.Select((id, i) => new PanelSlot(id, targetZone, i)));
        CurrentLayout.Slots = slots;

        TestRecorder?.OnMoveCompleted(panelId, targetZone, targetOrder, GetCurrentLayoutData());

        // WPF reparenting (only when context is wired).
        if (_panelRegistry is null) return;
        if (!_panelRegistry.TryGetValue(panelId, out var element)) return;

        // Zone-aware removal — side zones need Grid row cleanup; top zone is a plain Grid.
        if (sourceZone == DockZone.Left)
            RemoveFromZone(_leftZonePanel!, _leftZonePanels, element, _leftZoneScrollViewer as FrameworkElement);
        else if (sourceZone == DockZone.Right)
            RemoveFromZone(_rightZonePanel!, _rightZonePanels, element, _rightZoneScrollViewer as FrameworkElement);
        else if (sourceZone == DockZone.Left2)
            RemoveFromZone(_left2ZonePanel!, _left2ZonePanels, element, _left2ZoneScrollViewer as FrameworkElement);
        else if (sourceZone == DockZone.Right2)
            RemoveFromZone(_right2ZonePanel!, _right2ZonePanels, element, _right2ZoneScrollViewer as FrameworkElement);
        else
            RemoveFromTopZone(element);

        switch (targetZone)
        {
            case DockZone.Left:
                AddToZone(_leftZonePanel!, _leftZonePanels, element, panelId, _leftZoneScrollViewer as FrameworkElement, insertAt);
                if (element.Visibility != Visibility.Collapsed)
                    ExpandZone(_leftZoneColumn!, _leftSplitterColumn!, _leftZoneScrollViewer!, _leftZoneSplitter!, element);
                break;

            case DockZone.Right:
                AddToZone(_rightZonePanel!, _rightZonePanels, element, panelId, _rightZoneScrollViewer as FrameworkElement, insertAt);
                if (element.Visibility != Visibility.Collapsed)
                    ExpandZone(_rightZoneColumn!, _rightSplitterColumn!, _rightZoneScrollViewer!, _rightZoneSplitter!, element);
                break;

            case DockZone.Left2:
                AddToZone(_left2ZonePanel!, _left2ZonePanels, element, panelId, _left2ZoneScrollViewer as FrameworkElement, insertAt);
                if (element.Visibility != Visibility.Collapsed)
                    ExpandZone(_left2ZoneColumn!, _left2SplitterColumn!, _left2ZoneScrollViewer!, _left2ZoneSplitter!, element);
                break;

            case DockZone.Right2:
                AddToZone(_right2ZonePanel!, _right2ZonePanels, element, panelId, _right2ZoneScrollViewer as FrameworkElement, insertAt);
                if (element.Visibility != Visibility.Collapsed)
                    ExpandZone(_right2ZoneColumn!, _right2SplitterColumn!, _right2ZoneScrollViewer!, _right2ZoneSplitter!, element);
                break;

            case DockZone.Top:
                AddToTopZone(panelId, element);
                break;
        }

        // Collapse source zone if it is now empty.
        if (sourceZone == DockZone.Left && !ZoneHasPanels(DockZone.Left))
        {
            SquadDashTrace.Write(TraceCategory.Docking, $"MovePanel: Left zone now empty after moving {panelId} out — collapsing");
            CollapseZone(_leftZoneColumn!, _leftSplitterColumn!, _leftZoneScrollViewer!, _leftZoneSplitter!);
        }
        else if (sourceZone == DockZone.Right && !ZoneHasPanels(DockZone.Right))
        {
            SquadDashTrace.Write(TraceCategory.Docking, $"MovePanel: Right zone now empty after moving {panelId} out — collapsing");
            CollapseZone(_rightZoneColumn!, _rightSplitterColumn!, _rightZoneScrollViewer!, _rightZoneSplitter!);
        }
        else if (sourceZone == DockZone.Left2 && !ZoneHasPanels(DockZone.Left2))
        {
            SquadDashTrace.Write(TraceCategory.Docking, $"MovePanel: Left2 zone now empty after moving {panelId} out — collapsing");
            CollapseZone(_left2ZoneColumn!, _left2SplitterColumn!, _left2ZoneScrollViewer!, _left2ZoneSplitter!);
        }
        else if (sourceZone == DockZone.Right2 && !ZoneHasPanels(DockZone.Right2))
        {
            SquadDashTrace.Write(TraceCategory.Docking, $"MovePanel: Right2 zone now empty after moving {panelId} out — collapsing");
            CollapseZone(_right2ZoneColumn!, _right2SplitterColumn!, _right2ZoneScrollViewer!, _right2ZoneSplitter!);
        }
        else if (sourceZone is DockZone.Left or DockZone.Right or DockZone.Left2 or DockZone.Right2)
        {
            int remaining = CurrentLayout.Slots.Count(s => s.Zone == sourceZone);
            SquadDashTrace.Write(TraceCategory.Docking,
                $"MovePanel: {sourceZone} still has {remaining} panel(s) after moving {panelId} — not collapsing");
        }
    }

    private bool ZoneHasPanels(DockZone zone) =>
        CurrentLayout.Slots.Any(s =>
            s.Zone == zone &&
            _panelRegistry!.TryGetValue(s.PanelId, out var el) &&
            el.Visibility != Visibility.Collapsed);

    /// <summary>
    /// Called when a panel's visibility is toggled (shown or hidden) without moving it to a
    /// different zone.  Handles two behaviours for side zones (Left/Right/Left2/Right2):
    /// <list type="bullet">
    ///   <item><b>Hidden:</b> Rebuilds the zone grid with only the remaining visible panels,
    ///   redistributing the closed panel's star-height share proportionally.  Collapses the
    ///   zone column entirely when no visible panels remain.</item>
    ///   <item><b>Shown:</b> Re-expands a collapsed zone if needed and rebuilds the grid to
    ///   include the newly visible panel.</item>
    /// </list>
    /// Top-zone panels are silently ignored — their layout is managed separately.
    /// </summary>
    public void OnPanelVisibilityChanged(string panelId, bool visible)
    {
        if (_panelRegistry is null) return;

        var slot = CurrentLayout.Slots.FirstOrDefault(s =>
            string.Equals(s.PanelId, panelId, StringComparison.OrdinalIgnoreCase));
        if (slot is null || slot.Zone == DockZone.Top) return;

        var (zoneList, zoneGrid, scrollViewer) = GetZoneContext(slot.Zone);

        SquadDashTrace.Write(TraceCategory.Docking,
            $"OnPanelVisibilityChanged: {panelId} visible={visible} zone={slot.Zone} " +
            $"zoneList=[{(zoneList is null ? "null" : string.Join(",", zoneList.Select(e =>
            {
                var id = _panelRegistry.FirstOrDefault(kv => kv.Value == e).Key ?? "?";
                return $"{id}({(e.Visibility == Visibility.Collapsed ? "hidden" : "vis")})";
            })))}]");

        if (zoneList is null || zoneGrid is null || zoneList.Count == 0)
        {
            SquadDashTrace.Write(TraceCategory.Docking,
                $"OnPanelVisibilityChanged: early-exit — zoneList null/empty for {panelId} zone={slot.Zone}");
            return;
        }

        var (col, splCol, sv, spl) = GetZoneColumnContext(slot.Zone);

        if (!visible)
        {
            // Capture star weights from RowDefinitions *before* rebuilding.
            // The panel is already Collapsed at call time but RowDefinitions still reflect
            // the pre-collapse proportions.
            var weights = CaptureStarWeights(zoneGrid, zoneList);

            var remaining = zoneList
                .Where(el => el.Visibility != Visibility.Collapsed)
                .ToList();

            SquadDashTrace.Write(TraceCategory.Docking,
                $"OnPanelVisibilityChanged: hiding {panelId} — remaining visible={remaining.Count}/{zoneList.Count} " +
                $"colWidth={col?.Width.Value:F0} willCollapse={remaining.Count == 0}");

            if (remaining.Count == 0)
            {
                if (col is not null)
                    CollapseZone(col, splCol!, sv!, spl!);
                RebuildZoneGrid(zoneGrid, remaining, scrollViewer as FrameworkElement);
            }
            else
            {
                // Distribute the hidden panel's star share proportionally to the survivors.
                if (_panelRegistry.TryGetValue(panelId, out var hiddenEl) &&
                    weights.TryGetValue(hiddenEl, out double hiddenWeight) &&
                    hiddenWeight > 0)
                {
                    double remainingTotal = remaining.Sum(p => weights.GetValueOrDefault(p, 1.0));
                    if (remainingTotal > 0)
                    {
                        foreach (var p in remaining)
                        {
                            double cur = weights.GetValueOrDefault(p, 1.0);
                            weights[p] = cur + hiddenWeight * (cur / remainingTotal);
                        }
                    }
                    SquadDashTrace.Write(TraceCategory.Docking,
                        $"OnPanelVisibilityChanged: redistributed hiddenWeight={hiddenWeight:F2} among {remaining.Count} survivor(s)");
                }
                RebuildZoneGrid(zoneGrid, remaining, scrollViewer as FrameworkElement, weights);
            }
        }
        else
        {
            // Panel is being shown.  Re-expand zone column if it was collapsed.
            bool wasCollapsed = col is not null && col.Width.IsAbsolute && col.Width.Value == 0;
            SquadDashTrace.Write(TraceCategory.Docking,
                $"OnPanelVisibilityChanged: showing {panelId} zone={slot.Zone} colWidth={col?.Width.Value:F0} wasCollapsed={wasCollapsed}");

            if (wasCollapsed)
            {
                double width = _panelRegistry.TryGetValue(panelId, out var shownEl) && shownEl.ActualWidth > 0
                    ? shownEl.ActualWidth : 280;
                col!.Width    = new GridLength(width);
                splCol!.Width = new GridLength(5);
                sv!.Visibility  = Visibility.Visible;
                spl!.Visibility = Visibility.Visible;
            }

            // Rebuild including the now-visible panel, preserving zone order.
            var orderedVisible = zoneList
                .Where(el => el.Visibility != Visibility.Collapsed)
                .ToList();
            RebuildZoneGrid(zoneGrid, orderedVisible, scrollViewer as FrameworkElement);
        }
    }

    /// <summary>
    /// Returns the zone column, splitter-column, scroll-viewer, and splitter UI elements for
    /// a given side zone.  Returns all-nulls for <see cref="DockZone.Top"/>.
    /// </summary>
    private (ColumnDefinition? col, ColumnDefinition? splCol, UIElement? sv, UIElement? spl)
        GetZoneColumnContext(DockZone zone) =>
        zone switch
        {
            DockZone.Left   => (_leftZoneColumn,   _leftSplitterColumn,   _leftZoneScrollViewer,   _leftZoneSplitter),
            DockZone.Right  => (_rightZoneColumn,  _rightSplitterColumn,  _rightZoneScrollViewer,  _rightZoneSplitter),
            DockZone.Left2  => (_left2ZoneColumn,  _left2SplitterColumn,  _left2ZoneScrollViewer,  _left2ZoneSplitter),
            DockZone.Right2 => (_right2ZoneColumn, _right2SplitterColumn, _right2ZoneScrollViewer, _right2ZoneSplitter),
            _               => (null, null, null, null),
        };

    /// <summary>
    /// Reads the current star-height values from <paramref name="zoneGrid"/>'s RowDefinitions
    /// and returns a mapping from each panel element to its star weight.
    /// Panel <c>i</c> occupies row <c>2*i</c> (odd rows are 5 px splitters).
    /// Falls back to 1.0 when a row is missing or not a star row.
    /// </summary>
    private static Dictionary<FrameworkElement, double> CaptureStarWeights(
        Grid zoneGrid, List<FrameworkElement> zoneList)
    {
        var weights = new Dictionary<FrameworkElement, double>(zoneList.Count);
        for (int i = 0; i < zoneList.Count; i++)
        {
            int rowIdx = 2 * i;
            double star = 1.0;
            if (rowIdx < zoneGrid.RowDefinitions.Count)
            {
                var h = zoneGrid.RowDefinitions[rowIdx].Height;
                if (h.IsStar) star = h.Value;
            }
            weights[zoneList[i]] = star;
        }
        return weights;
    }

    private void RemoveFromTopZone(FrameworkElement element)
    {
        if (LogicalTreeHelper.GetParent(element) is Panel parent)
            parent.Children.Remove(element);
        RebuildTopZoneLayout();
    }

    private static void RemoveFromZone(Grid zone, List<FrameworkElement> zoneList, FrameworkElement element, FrameworkElement? scrollViewer)
    {
        zoneList.Remove(element);
        RebuildZoneGrid(zone, zoneList, scrollViewer);
    }

    private void AddToZone(Grid zone, List<FrameworkElement> zoneList, FrameworkElement element, string panelId, FrameworkElement? scrollViewer, int insertAt = -1)
    {
        // Save the original height binding before we replace it (only once per panel).
        if (!_savedHeightBindings.ContainsKey(panelId))
            _savedHeightBindings[panelId] = BindingOperations.GetMultiBinding(element, FrameworkElement.HeightProperty);

        DetachFromCurrentPanelParent(element);

        element.ClearValue(Grid.ColumnProperty);
        element.ClearValue(FrameworkElement.MarginProperty);
        element.ClearValue(FrameworkElement.MaxWidthProperty);
        element.ClearValue(FrameworkElement.MaxHeightProperty);
        element.ClearValue(FrameworkElement.VerticalAlignmentProperty);
        element.ClearValue(FrameworkElement.HeightProperty); // row sizing handles height

        if (insertAt < 0 || insertAt >= zoneList.Count)
            zoneList.Add(element);
        else
            zoneList.Insert(insertAt, element);
        RebuildZoneGrid(zone, zoneList, scrollViewer);
    }

    /// <summary>Returns the zone list, grid, and scroll viewer for a given side zone.</summary>
    private (List<FrameworkElement>? zoneList, Grid? zoneGrid, UIElement? scrollViewer) GetZoneContext(DockZone zone) =>
        zone switch
        {
            DockZone.Left   => (_leftZonePanels,   _leftZonePanel,   _leftZoneScrollViewer),
            DockZone.Right  => (_rightZonePanels,  _rightZonePanel,  _rightZoneScrollViewer),
            DockZone.Left2  => (_left2ZonePanels,  _left2ZonePanel,  _left2ZoneScrollViewer),
            DockZone.Right2 => (_right2ZonePanels, _right2ZonePanel, _right2ZoneScrollViewer),
            _               => (null, null, null),
        };

    /// <summary>
    /// Clears and rebuilds the side-zone Grid with one star row per panel and a
    /// 5 px <see cref="GridSplitter"/> row between consecutive panels.
    /// The Grid height is bound to <paramref name="scrollViewer"/> so star rows have
    /// a finite space to divide.
    /// </summary>
    private static void RebuildZoneGrid(Grid zone, List<FrameworkElement> panels, FrameworkElement? scrollViewer,
        IReadOnlyDictionary<FrameworkElement, double>? weights = null)
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
                    Name = $"{zone.Name}RowSplitter{i - 1}",
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

            double starValue = weights is not null && weights.TryGetValue(panels[i], out double w) ? w : 1.0;
            // MinHeight is only meaningful when a GridSplitter is present (2+ panels) so the user
            // cannot drag a panel to zero.  A solo panel has no splitter and must be allowed to
            // start at zero height (NaN zone) without creating a spurious visible stub.
            zone.RowDefinitions.Add(new RowDefinition
            {
                Height    = new GridLength(starValue, GridUnitType.Star),
                MinHeight = panels.Count > 1 ? 100 : 0,
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
        if (!TopZoneColumnMap.ContainsKey(panelId)) return; // not a valid top-zone panel

        // Remove any zone-height binding and restore the original Top-zone height constraint.
        element.ClearValue(FrameworkElement.HeightProperty);
        if (_savedHeightBindings.TryGetValue(panelId, out var saved) && saved is not null)
            BindingOperations.SetBinding(element, FrameworkElement.HeightProperty, saved);

        element.VerticalAlignment = VerticalAlignment.Top;
        element.Margin = new Thickness(14, 0, 0, 0);
        DetachFromCurrentPanelParent(element);
        _topZoneGrid.Children.Add(element);

        // Assign columns for ALL top-zone panels based on their current Order in the layout.
        RebuildTopZoneLayout();
    }

    /// <summary>
    /// Assigns each top-zone panel element to the physical grid column that corresponds
    /// to its rank (0-based) in <see cref="CurrentLayout"/>.  Call after any change to
    /// the top-zone order so that the visual left-to-right order tracks the data model.
    /// </summary>
    private void RebuildTopZoneLayout()
    {
        if (_topZoneGrid is null || _panelRegistry is null) return;

        var topSlots = CurrentLayout.Slots
            .Where(s => s.Zone == DockZone.Top)
            .OrderBy(s => s.Order)
            .ToList();

        for (int rank = 0; rank < topSlots.Count && rank < TopZonePhysicalColumns.Length; rank++)
        {
            if (_panelRegistry.TryGetValue(topSlots[rank].PanelId, out var element))
                Grid.SetColumn(element, TopZonePhysicalColumns[rank]);
        }
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
        bool wasCollapsed = zoneCol.Width.IsAbsolute && zoneCol.Width.Value == 0;
        if (wasCollapsed)
        {
            double width = arrivedPanel.ActualWidth > 0 ? arrivedPanel.ActualWidth : 280;
            zoneCol.Width = new GridLength(width);
            splitterCol.Width = new GridLength(5);
            scrollViewer.Visibility = Visibility.Visible;
            splitter.Visibility = Visibility.Visible;
            SquadDashTrace.Write(TraceCategory.Docking,
                $"ExpandZone: was collapsed → expanded to w={width:F0}");
        }
        else
        {
            SquadDashTrace.Write(TraceCategory.Docking,
                $"ExpandZone: already expanded (w={zoneCol.Width.Value:F0}) — no change");
        }
    }

    private static void CollapseZone(
        ColumnDefinition zoneCol,
        ColumnDefinition splitterCol,
        UIElement scrollViewer,
        UIElement splitter)
    {
        SquadDashTrace.Write(TraceCategory.Docking,
            $"CollapseZone: collapsing (was w={zoneCol.Width.Value:F0})");
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
            LeftZoneWidth   = (_leftZoneColumn   is { } lc  && lc.Width.IsAbsolute  && lc.Width.Value  > 0) ? lc.Width.Value  : (double?)null,
            RightZoneWidth  = (_rightZoneColumn  is { } rc  && rc.Width.IsAbsolute  && rc.Width.Value  > 0) ? rc.Width.Value  : (double?)null,
            Left2ZoneWidth  = (_left2ZoneColumn  is { } l2c && l2c.Width.IsAbsolute && l2c.Width.Value > 0) ? l2c.Width.Value : (double?)null,
            Right2ZoneWidth = (_right2ZoneColumn is { } r2c && r2c.Width.IsAbsolute && r2c.Width.Value > 0) ? r2c.Width.Value : (double?)null,
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

        // Reassign physical top-zone columns to match the restored layout order.
        RebuildTopZoneLayout();
    }

    private static DockLayout CloneLayout(DockLayout layout) => new()
    {
        Name = layout.Name,
        Slots = layout.Slots.ToList(),
        LeftZoneWidth   = layout.LeftZoneWidth,
        RightZoneWidth  = layout.RightZoneWidth,
        Left2ZoneWidth  = layout.Left2ZoneWidth,
        Right2ZoneWidth = layout.Right2ZoneWidth,
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

    /// <summary>
    /// Returns the screen rectangle (in physical pixels) that should be highlighted when the
    /// user hovers over <paramref name="slot"/> in the docking map popup.
    /// <para>
    /// • Column slot (N existing panels) → column's left/right bounds, height divided into
    ///   (N+1) equal bands; targetOrder selects the band (append slot = last band).<br/>
    /// • Empty column zone              → 64px wide strip at the column's near edge.<br/>
    /// • Top zone slot, source in top   → full panel screen rect (replace/reorder).<br/>
    /// • Top zone slot, source elsewhere → thin vertical insertion strip.<br/>
    /// • Empty top zone                 → thin horizontal strip across the top-zone grid.
    /// </para>
    /// Returns <see cref="Rect.Empty"/> when the location cannot be determined.
    /// </summary>
    public Rect GetSlotScreenRect(SlotButtonViewModel slot)
    {
        if (_panelRegistry is null)
        {
            SquadDashTrace.Write(TraceCategory.Docking, $"GetSlotScreenRect: _panelRegistry is null — returning Empty. slot={slot.TargetZone}[{slot.TargetOrder}] src={slot.SourcePanelId}");
            return Rect.Empty;
        }

        var panelsInZone = CurrentLayout.Slots
            .Where(s => s.Zone == slot.TargetZone)
            .OrderBy(s => s.Order)
            .Select(s => s.PanelId)
            // Filter to only registered AND currently visible panels.
            // Panels whose border is Collapsed are in the layout but not rendered — treat
            // them as absent so the zone is correctly seen as empty for preview purposes.
            // Always include the source panel (it may be mid-drag and invisible itself).
            .Where(id =>
            {
                if (string.Equals(id, slot.SourcePanelId, StringComparison.OrdinalIgnoreCase))
                    return true;
                return _panelRegistry!.TryGetValue(id, out var el) && el.IsVisible;
            })
            .ToList();

        var allInZone = CurrentLayout.Slots
            .Where(s => s.Zone == slot.TargetZone)
            .OrderBy(s => s.Order)
            .Select(s => s.PanelId)
            .Where(id => _panelRegistry!.ContainsKey(id) ||
                         string.Equals(id, slot.SourcePanelId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        SquadDashTrace.Write(TraceCategory.Docking,
            $"GetSlotScreenRect: zone={slot.TargetZone} order={slot.TargetOrder} src={slot.SourcePanelId} " +
            $"inZone=[{string.Join(",", allInZone.Select(id => {
                if (!_panelRegistry!.TryGetValue(id, out var el)) return id + "(unregistered)";
                return id + (el.IsVisible ? "" : "(hidden)");
            }))}] " +
            $"visible=[{string.Join(",", panelsInZone)}]");

        if (slot.TargetZone == DockZone.Top)
        {
            // If the dragged panel is already in the top zone, hovering another top slot
            // means a reorder/replace — show the full target panel rect, not an insertion strip.
            bool sourceIsInTop = CurrentLayout.Slots.Any(s =>
                s.Zone == DockZone.Top &&
                string.Equals(s.PanelId, slot.SourcePanelId, StringComparison.OrdinalIgnoreCase));

            return sourceIsInTop
                ? GetTopReorderRect(slot.TargetOrder, panelsInZone)
                : GetTopInsertionRect(slot.TargetOrder, panelsInZone);
        }

        return GetColumnSlotRect(slot.TargetZone, slot.TargetOrder, panelsInZone);
    }

    /// <summary>
    /// Source is already in top zone → show the full rect of the panel at targetOrder
    /// (or the last panel's rect for the append "+" slot).
    /// </summary>
    private Rect GetTopReorderRect(int targetOrder, List<string> panelsInZone)
    {
        if (panelsInZone.Count == 0) return Rect.Empty;

        var index = Math.Clamp(targetOrder, 0, panelsInZone.Count - 1);
        if (_panelRegistry!.TryGetValue(panelsInZone[index], out var el))
            return GetScreenRect(el);

        return Rect.Empty;
    }

    private Rect GetColumnSlotRect(DockZone zone, int targetOrder, List<string> panelsInZone)
    {
        UIElement? container = zone switch
        {
            DockZone.Left   => _leftZoneScrollViewer,
            DockZone.Right  => _rightZoneScrollViewer,
            DockZone.Left2  => _left2ZoneScrollViewer,
            DockZone.Right2 => _right2ZoneScrollViewer,
            _               => null,
        };

        // When the scroll-viewer is collapsed (empty column), fall back to the zone's Grid panel.
        Grid? zoneGrid = zone switch
        {
            DockZone.Left   => _leftZonePanel,
            DockZone.Right  => _rightZonePanel,
            DockZone.Left2  => _left2ZonePanel,
            DockZone.Right2 => _right2ZonePanel,
            _               => null,
        };

        bool isRightSide = zone == DockZone.Right || zone == DockZone.Right2;

        if (panelsInZone.Count == 0)
        {
            // Empty zone — 64px wide strip just outside the inner neighbor.
            // "Inner neighbor" = the zone that sits between this one and the center content.
            // Left  → neighbor is the center (TopZonePanelsGrid)
            // Left2 → neighbor is the Left zone scroll-viewer (if visible) else center
            // Right  → neighbor is the center
            // Right2 → neighbor is the Right zone scroll-viewer (if visible) else center
            const double StripWidth = 64;

            Rect neighborRect = GetInnerNeighborRect(zone);
            SquadDashTrace.Write(TraceCategory.Docking,
                $"GetColumnSlotRect(empty): zone={zone} neighborRect={neighborRect} " +
                $"topZoneGrid={(_topZoneGrid is FrameworkElement tg ? $"vis={tg.IsVisible} w={tg.ActualWidth:F0}" : "null")} " +
                $"leftSV={(_leftZoneScrollViewer is FrameworkElement lsv ? $"vis={lsv.IsVisible} w={lsv.ActualWidth:F0}" : "null")} " +
                $"rightSV={(_rightZoneScrollViewer is FrameworkElement rsv ? $"vis={rsv.IsVisible} w={rsv.ActualWidth:F0}" : "null")}");

            if (!neighborRect.IsEmpty)
            {
                // For Left/Right: when the column is collapsed (empty), the center grid extends
                // to the window edge, so neighborRect.Left ≈ 0 and Left - 64 is off-screen.
                // Overlap the center grid's near edge by 64px so the strip is always visible.
                // For Left2/Right2: neighbor is the Left/Right scroll-viewer whose far edge is
                // already at the window boundary — same overlap treatment.
                double x = zone switch
                {
                    DockZone.Left   => neighborRect.Left,
                    DockZone.Left2  => neighborRect.Left,
                    DockZone.Right  => neighborRect.Right - StripWidth,
                    DockZone.Right2 => neighborRect.Right - StripWidth,
                    _               => isRightSide ? neighborRect.Right : neighborRect.Left - StripWidth,
                };

                // The neighborRect height is only the top-zone panel height (~330px).
                // The preview strip for an empty side zone should span the full window height.
                // Walk up from _topZoneGrid to find the host Window for full bounds.
                double stripTop    = neighborRect.Top;
                double stripHeight = neighborRect.Height;
                if (_topZoneGrid is DependencyObject tgDep)
                {
                    var win = System.Windows.Window.GetWindow(tgDep);
                    if (win is FrameworkElement winFe)
                    {
                        var winRect = GetScreenRect(winFe);
                        if (!winRect.IsEmpty)
                        {
                            stripTop    = winRect.Top;
                            stripHeight = winRect.Height;
                        }
                    }
                }

                var result = new Rect(x, stripTop, StripWidth, stripHeight);
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"GetColumnSlotRect(empty): zone={zone} x={x:F0} neighborRect={neighborRect} " +
                    $"stripTop={stripTop:F0} stripH={stripHeight:F0} → strip={result}");
                return result;
            }
            SquadDashTrace.Write(TraceCategory.Docking,
                $"GetColumnSlotRect(empty): zone={zone} neighborRect is Empty — returning Rect.Empty");
            return Rect.Empty;
        }

        // Get the column's bounding rect (horizontal bounds + total height).
        // Prefer the container scroll-viewer; fall back to zone grid then to first panel's rect.
        Rect colRect = Rect.Empty;
        if (container is FrameworkElement cfe2 && cfe2.IsVisible)
            colRect = GetScreenRect(cfe2);
        if (colRect.IsEmpty && zoneGrid is FrameworkElement cfeGrid && cfeGrid.IsVisible)
            colRect = GetScreenRect(cfeGrid);
        if (colRect.IsEmpty && _panelRegistry!.TryGetValue(panelsInZone[0], out var firstFallback))
            colRect = GetScreenRect(firstFallback);
        if (colRect.IsEmpty) return Rect.Empty;

        // Divide the column height into (N+1) equal bands — one slot per existing panel
        // plus one for the incoming panel.  targetOrder selects the band.
        // The append "+" slot (targetOrder == panelsInZone.Count) naturally becomes the last band.
        int sections = panelsInZone.Count + 1;
        int index    = Math.Clamp(targetOrder, 0, sections - 1);
        double bandH = colRect.Height / sections;
        double top   = colRect.Top + index * bandH;
        return new Rect(colRect.Left, top, colRect.Width, bandH);
    }

    /// <summary>
    /// Returns the screen rect of the element immediately "inside" (toward center) of the
    /// given zone — used to anchor the 64px empty-zone strip at the correct outer edge.
    /// </summary>
    private Rect GetInnerNeighborRect(DockZone zone)
    {
        // For Left2/Right2 try the adjacent Left/Right scroll-viewer first (non-empty check).
        UIElement? adjacent = zone switch
        {
            DockZone.Left2  => _leftZoneScrollViewer,
            DockZone.Right2 => _rightZoneScrollViewer,
            _               => null,
        };
        if (adjacent is FrameworkElement adjFe && adjFe.IsVisible && adjFe.ActualWidth > 0)
        {
            var r = GetScreenRect(adjFe);
            SquadDashTrace.Write(TraceCategory.Docking,
                $"GetInnerNeighborRect: zone={zone} using adjacent SV IsVisible={adjFe.IsVisible} w={adjFe.ActualWidth:F0} → {r}");
            if (!r.IsEmpty) return r;
        }

        // Fall back to the always-visible center top-zone grid.
        var topRect = _topZoneGrid is not null ? GetScreenRect(_topZoneGrid) : Rect.Empty;
        SquadDashTrace.Write(TraceCategory.Docking,
            $"GetInnerNeighborRect: zone={zone} using topZoneGrid={(_topZoneGrid is FrameworkElement tg2 ? $"IsVisible={tg2.IsVisible} w={tg2.ActualWidth:F0}" : "null")} → {topRect}");
        return topRect;
    }

    private Rect GetTopInsertionRect(int targetOrder, List<string> panelsInZone)
    {
        if (_topZoneGrid is null) return Rect.Empty;

        var gridRect = GetScreenRect(_topZoneGrid);
        if (gridRect.IsEmpty) return Rect.Empty;

        const double StripW = 8;

        if (panelsInZone.Count == 0)
        {
            // Empty top zone — vertical insertion strip at the left edge of the grid
            // (to the right of the inactive-agents panel), spanning roughly the panel height cap.
            double stripHeight = 320;
            if (_topZoneGrid is DependencyObject tgd2)
            {
                var win2 = System.Windows.Window.GetWindow(tgd2);
                if (win2 is FrameworkElement winFe2)
                {
                    var winRect2 = GetScreenRect(winFe2);
                    if (!winRect2.IsEmpty)
                        stripHeight = Math.Max(240, winRect2.Height / 3);
                }
            }
            return new Rect(gridRect.Right, gridRect.Top, StripW, stripHeight);
        }

        if (targetOrder < panelsInZone.Count)
        {
            // Thin vertical strip to the LEFT of the panel at targetOrder.
            if (_panelRegistry!.TryGetValue(panelsInZone[targetOrder], out var el))
            {
                var r = GetScreenRect(el);
                if (!r.IsEmpty)
                    return new Rect(r.Left - StripW - 2, gridRect.Top, StripW, gridRect.Height);
            }
        }
        else
        {
            // "+" append slot — thin vertical strip to the RIGHT of the last panel.
            // Fall back to the grid's right edge if the last panel isn't in the registry.
            double rightX = gridRect.Right;
            if (_panelRegistry!.TryGetValue(panelsInZone[^1], out var el))
            {
                var r = GetScreenRect(el);
                if (!r.IsEmpty) rightX = r.Right + 2;
            }
            return new Rect(rightX, gridRect.Top, StripW, gridRect.Height);
        }

        return Rect.Empty;
    }

    private static Rect GetScreenRect(FrameworkElement el)
    {
        if (!el.IsVisible || el.ActualWidth <= 0) return Rect.Empty;
        try
        {
            var pt = el.PointToScreen(new Point(0, 0));
            return new Rect(pt.X, pt.Y, el.ActualWidth, el.ActualHeight);
        }
        catch { return Rect.Empty; }
    }

    /// <summary>Returns the zone that <paramref name="panelId"/> currently occupies.</summary>
    public DockZone GetCurrentZone(string panelId)
        => CurrentLayout.Slots.FirstOrDefault(s =>
               string.Equals(s.PanelId, panelId, StringComparison.OrdinalIgnoreCase))?.Zone
           ?? DockZone.Top;

    /// <summary>
    /// Writes a one-shot Docking trace snapshot of every panel's zone, order, and
    /// visibility.  Called when the docking map window opens so the full layout state
    /// is in the trace before any hover events fire.
    /// </summary>
    public void LogLayoutSnapshot(string sourcePanelId)
    {
        if (_panelRegistry is null) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== DockingMap opened  src={sourcePanelId}  layout={CurrentLayout.Name} ===");
        sb.AppendLine($"  {"PanelId",-14} {"Zone",-8} {"Order",-6} {"Registered",-12} {"Visible",-9} ScreenRect");

        var zones = Enum.GetValues<DockZone>();
        foreach (var zone in zones)
        {
            var slotsInZone = CurrentLayout.Slots
                .Where(s => s.Zone == zone)
                .OrderBy(s => s.Order);
            foreach (var slot in slotsInZone)
            {
                bool registered = _panelRegistry.TryGetValue(slot.PanelId, out var el);
                bool visible    = registered && el!.IsVisible;
                string rectStr  = registered && el!.IsVisible
                    ? GetScreenRect(el) is { IsEmpty: false } r
                        ? $"{r.Left:F0},{r.Top:F0} {r.Width:F0}×{r.Height:F0}"
                        : "empty/zero"
                    : "—";
                sb.AppendLine(
                    $"  {slot.PanelId,-14} {zone,-8} {slot.Order,-6} {registered,-12} {visible,-9} {rectStr}");
            }
        }

        // Also list any registered panels NOT in the layout at all.
        var inLayout = CurrentLayout.Slots.Select(s => s.PanelId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _panelRegistry)
        {
            if (!inLayout.Contains(kvp.Key))
                sb.AppendLine($"  {kvp.Key,-14} {"(none)",-8} {"—",-6} {"true",-12} {kvp.Value.IsVisible,-9} (not in layout)");
        }

        SquadDashTrace.Write(TraceCategory.Docking, sb.ToString().TrimEnd());
    }

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
