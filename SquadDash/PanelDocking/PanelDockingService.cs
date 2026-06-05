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
    private readonly Grid? _left3ZonePanel;
    private readonly Grid? _right3ZonePanel;
    private readonly Grid? _left4ZonePanel;
    private readonly Grid? _right4ZonePanel;
    private readonly Grid? _left5ZonePanel;
    private readonly Grid? _right5ZonePanel;
    private readonly Grid? _left6ZonePanel;
    private readonly Grid? _right6ZonePanel;
    private readonly Grid? _topZoneGrid;
    private readonly ColumnDefinition? _leftZoneColumn;
    private readonly ColumnDefinition? _rightZoneColumn;
    private readonly ColumnDefinition? _left2ZoneColumn;
    private readonly ColumnDefinition? _right2ZoneColumn;
    private readonly ColumnDefinition? _left3ZoneColumn;
    private readonly ColumnDefinition? _right3ZoneColumn;
    private readonly ColumnDefinition? _left4ZoneColumn;
    private readonly ColumnDefinition? _right4ZoneColumn;
    private readonly ColumnDefinition? _left5ZoneColumn;
    private readonly ColumnDefinition? _right5ZoneColumn;
    private readonly ColumnDefinition? _left6ZoneColumn;
    private readonly ColumnDefinition? _right6ZoneColumn;
    private readonly ColumnDefinition? _leftSplitterColumn;
    private readonly ColumnDefinition? _rightSplitterColumn;
    private readonly ColumnDefinition? _left2SplitterColumn;
    private readonly ColumnDefinition? _right2SplitterColumn;
    private readonly ColumnDefinition? _left3SplitterColumn;
    private readonly ColumnDefinition? _right3SplitterColumn;
    private readonly ColumnDefinition? _left4SplitterColumn;
    private readonly ColumnDefinition? _right4SplitterColumn;
    private readonly ColumnDefinition? _left5SplitterColumn;
    private readonly ColumnDefinition? _right5SplitterColumn;
    private readonly ColumnDefinition? _left6SplitterColumn;
    private readonly ColumnDefinition? _right6SplitterColumn;
    private readonly UIElement? _leftZoneScrollViewer;
    private readonly UIElement? _rightZoneScrollViewer;
    private readonly UIElement? _left2ZoneScrollViewer;
    private readonly UIElement? _right2ZoneScrollViewer;
    private readonly UIElement? _left3ZoneScrollViewer;
    private readonly UIElement? _right3ZoneScrollViewer;
    private readonly UIElement? _left4ZoneScrollViewer;
    private readonly UIElement? _right4ZoneScrollViewer;
    private readonly UIElement? _left5ZoneScrollViewer;
    private readonly UIElement? _right5ZoneScrollViewer;
    private readonly UIElement? _left6ZoneScrollViewer;
    private readonly UIElement? _right6ZoneScrollViewer;
    private readonly UIElement? _leftZoneSplitter;
    private readonly UIElement? _rightZoneSplitter;
    private readonly UIElement? _left2ZoneSplitter;
    private readonly UIElement? _right2ZoneSplitter;
    private readonly UIElement? _left3ZoneSplitter;
    private readonly UIElement? _right3ZoneSplitter;
    private readonly UIElement? _left4ZoneSplitter;
    private readonly UIElement? _right4ZoneSplitter;
    private readonly UIElement? _left5ZoneSplitter;
    private readonly UIElement? _right5ZoneSplitter;
    private readonly UIElement? _left6ZoneSplitter;
    private readonly UIElement? _right6ZoneSplitter;

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

    // Maximum width for panels placed in the Top zone, preventing side-zone panels from
    // expanding too wide when moved here (consistent with MaxWidth="320" in MainWindow.xaml).
    private const double TopZonePanelMaxWidth = 320;

    // Width of the thin insertion-indicator strip used for both top-zone and empty side-zone
    // docking previews, keeping the visual language consistent across all drop targets.
    private const double InsertionIndicatorWidth = 8;

    // Saves each panel's original XAML Height binding so it can be restored when the
    // panel moves back to the Top zone after having been in a Left/Right zone.
    private readonly Dictionary<string, MultiBinding?> _savedHeightBindings =
        new(StringComparer.OrdinalIgnoreCase);

    // Ordered lists of panels currently in each side zone (used to rebuild row layout).
    private readonly List<FrameworkElement> _leftZonePanels   = new();
    private readonly List<FrameworkElement> _rightZonePanels  = new();
    private readonly List<FrameworkElement> _left2ZonePanels  = new();
    private readonly List<FrameworkElement> _right2ZonePanels = new();
    private readonly List<FrameworkElement> _left3ZonePanels  = new();
    private readonly List<FrameworkElement> _right3ZonePanels = new();
    private readonly List<FrameworkElement> _left4ZonePanels  = new();
    private readonly List<FrameworkElement> _right4ZonePanels = new();
    private readonly List<FrameworkElement> _left5ZonePanels  = new();
    private readonly List<FrameworkElement> _right5ZonePanels = new();
    private readonly List<FrameworkElement> _left6ZonePanels  = new();
    private readonly List<FrameworkElement> _right6ZonePanels = new();

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
        Grid left3ZonePanel,
        Grid right3ZonePanel,
        Grid left4ZonePanel,
        Grid right4ZonePanel,
        Grid left5ZonePanel,
        Grid right5ZonePanel,
        Grid left6ZonePanel,
        Grid right6ZonePanel,
        Grid topZoneGrid,
        ColumnDefinition leftZoneColumn,
        ColumnDefinition rightZoneColumn,
        ColumnDefinition left2ZoneColumn,
        ColumnDefinition right2ZoneColumn,
        ColumnDefinition left3ZoneColumn,
        ColumnDefinition right3ZoneColumn,
        ColumnDefinition left4ZoneColumn,
        ColumnDefinition right4ZoneColumn,
        ColumnDefinition left5ZoneColumn,
        ColumnDefinition right5ZoneColumn,
        ColumnDefinition left6ZoneColumn,
        ColumnDefinition right6ZoneColumn,
        ColumnDefinition leftSplitterColumn,
        ColumnDefinition rightSplitterColumn,
        ColumnDefinition left2SplitterColumn,
        ColumnDefinition right2SplitterColumn,
        ColumnDefinition left3SplitterColumn,
        ColumnDefinition right3SplitterColumn,
        ColumnDefinition left4SplitterColumn,
        ColumnDefinition right4SplitterColumn,
        ColumnDefinition left5SplitterColumn,
        ColumnDefinition right5SplitterColumn,
        ColumnDefinition left6SplitterColumn,
        ColumnDefinition right6SplitterColumn,
        UIElement leftZoneScrollViewer,
        UIElement rightZoneScrollViewer,
        UIElement left2ZoneScrollViewer,
        UIElement right2ZoneScrollViewer,
        UIElement left3ZoneScrollViewer,
        UIElement right3ZoneScrollViewer,
        UIElement left4ZoneScrollViewer,
        UIElement right4ZoneScrollViewer,
        UIElement left5ZoneScrollViewer,
        UIElement right5ZoneScrollViewer,
        UIElement left6ZoneScrollViewer,
        UIElement right6ZoneScrollViewer,
        UIElement leftZoneSplitter,
        UIElement rightZoneSplitter,
        UIElement left2ZoneSplitter,
        UIElement right2ZoneSplitter,
        UIElement left3ZoneSplitter,
        UIElement right3ZoneSplitter,
        UIElement left4ZoneSplitter,
        UIElement right4ZoneSplitter,
        UIElement left5ZoneSplitter,
        UIElement right5ZoneSplitter,
        UIElement left6ZoneSplitter,
        UIElement right6ZoneSplitter)
    {
        _panelRegistry = panelRegistry;
        _leftZonePanel = leftZonePanel;
        _rightZonePanel = rightZonePanel;
        _left2ZonePanel = left2ZonePanel;
        _right2ZonePanel = right2ZonePanel;
        _left3ZonePanel = left3ZonePanel;
        _right3ZonePanel = right3ZonePanel;
        _left4ZonePanel = left4ZonePanel;
        _right4ZonePanel = right4ZonePanel;
        _left5ZonePanel = left5ZonePanel;
        _right5ZonePanel = right5ZonePanel;
        _left6ZonePanel = left6ZonePanel;
        _right6ZonePanel = right6ZonePanel;
        _topZoneGrid = topZoneGrid;
        _leftZoneColumn = leftZoneColumn;
        _rightZoneColumn = rightZoneColumn;
        _left2ZoneColumn = left2ZoneColumn;
        _right2ZoneColumn = right2ZoneColumn;
        _left3ZoneColumn = left3ZoneColumn;
        _right3ZoneColumn = right3ZoneColumn;
        _left4ZoneColumn = left4ZoneColumn;
        _right4ZoneColumn = right4ZoneColumn;
        _left5ZoneColumn = left5ZoneColumn;
        _right5ZoneColumn = right5ZoneColumn;
        _left6ZoneColumn = left6ZoneColumn;
        _right6ZoneColumn = right6ZoneColumn;
        _leftSplitterColumn = leftSplitterColumn;
        _rightSplitterColumn = rightSplitterColumn;
        _left2SplitterColumn = left2SplitterColumn;
        _right2SplitterColumn = right2SplitterColumn;
        _left3SplitterColumn = left3SplitterColumn;
        _right3SplitterColumn = right3SplitterColumn;
        _left4SplitterColumn = left4SplitterColumn;
        _right4SplitterColumn = right4SplitterColumn;
        _left5SplitterColumn = left5SplitterColumn;
        _right5SplitterColumn = right5SplitterColumn;
        _left6SplitterColumn = left6SplitterColumn;
        _right6SplitterColumn = right6SplitterColumn;
        _leftZoneScrollViewer = leftZoneScrollViewer;
        _rightZoneScrollViewer = rightZoneScrollViewer;
        _left2ZoneScrollViewer = left2ZoneScrollViewer;
        _right2ZoneScrollViewer = right2ZoneScrollViewer;
        _left3ZoneScrollViewer = left3ZoneScrollViewer;
        _right3ZoneScrollViewer = right3ZoneScrollViewer;
        _left4ZoneScrollViewer = left4ZoneScrollViewer;
        _right4ZoneScrollViewer = right4ZoneScrollViewer;
        _left5ZoneScrollViewer = left5ZoneScrollViewer;
        _right5ZoneScrollViewer = right5ZoneScrollViewer;
        _left6ZoneScrollViewer = left6ZoneScrollViewer;
        _right6ZoneScrollViewer = right6ZoneScrollViewer;
        _leftZoneSplitter = leftZoneSplitter;
        _rightZoneSplitter = rightZoneSplitter;
        _left2ZoneSplitter = left2ZoneSplitter;
        _right2ZoneSplitter = right2ZoneSplitter;
        _left3ZoneSplitter = left3ZoneSplitter;
        _right3ZoneSplitter = right3ZoneSplitter;
        _left4ZoneSplitter = left4ZoneSplitter;
        _right4ZoneSplitter = right4ZoneSplitter;
        _left5ZoneSplitter = left5ZoneSplitter;
        _right5ZoneSplitter = right5ZoneSplitter;
        _left6ZoneSplitter = left6ZoneSplitter;
        _right6ZoneSplitter = right6ZoneSplitter;
    }

    /// <summary>The live panel layout for the current session.</summary>
    public DockLayout CurrentLayout { get; private set; } = DockLayout.CreateDefault();

    /// <summary>
    /// Moves <paramref name="panelId"/> to <paramref name="targetZone"/> at position
    /// <paramref name="targetOrder"/>, updating both the in-memory layout model and (when
    /// WPF context is present) the actual UI elements.  When <paramref name="targetOrder"/>
    /// is negative the panel is appended at the end of the zone.
    /// Same-zone reordering is supported.
    /// <para>
    /// When <paramref name="insertKind"/> is <see cref="SyntheticInsertKind.InsertBefore"/>
    /// and the target zone has adjacent occupied outer zones that can be shifted outward
    /// to make room for a new standalone column, the shift is performed automatically
    /// and the target zone is redirected accordingly.
    /// </para>
    /// </summary>
    public void MovePanel(string panelId, DockZone targetZone, int targetOrder = -1,
        SyntheticInsertKind insertKind = SyntheticInsertKind.None)
    {
        bool isTopLevel = !_isMovingPanel;
        _isMovingPanel = true;
        var requestedTargetZone = targetZone;
        var requestedTargetOrder = targetOrder;
        // Capture source zone before try so the finally block can check it.
        DockZone? sourceZoneCapture = CurrentLayout.Slots
            .FirstOrDefault(s => string.Equals(s.PanelId, panelId, StringComparison.OrdinalIgnoreCase))?.Zone;
        
        // Declare variables for test recording at function scope so they're accessible in finally
        DockingMapViewModel? preMoveDockingMapForRecording = null;
        bool syntheticColumnShiftChanged = false;
        
        try
        {
        string LayoutSignature() => string.Join("|", CurrentLayout.Slots
            .OrderBy(s => s.Zone)
            .ThenBy(s => s.Order)
            .ThenBy(s => s.PanelId, StringComparer.OrdinalIgnoreCase)
            .Select(s => $"{s.PanelId}@{s.Zone}:{s.Order}"));

        if (insertKind != SyntheticInsertKind.None)
        {
            // Capture the real pre-move docking map before resolving synthetic column shifts.
            // ResolveInsert* may shift neighboring columns as part of preparing the move.
            preMoveDockingMapForRecording = (TestRecorder is not null) ?
                DockingMapBuilder.BuildDockingMap(panelId, CurrentLayout, GetCurrentLayoutData().VisiblePanelIds) :
                null;

            if (TestRecorder is not null)
            {
                var slotCount = preMoveDockingMapForRecording?.Slots.Count ?? -1;
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[docking-recorder] Synthetic move: captured preMove docking map with {slotCount} slots");
            }

            var beforeSyntheticResolve = LayoutSignature();
            if (insertKind == SyntheticInsertKind.InsertBefore)
                (targetZone, targetOrder) = ResolveInsertBeforeColumnShift(panelId, targetZone, targetOrder);
            else if (insertKind == SyntheticInsertKind.InsertAfter)
                (targetZone, targetOrder) = ResolveInsertAfterColumnShift(panelId, targetZone, targetOrder);
            syntheticColumnShiftChanged = !string.Equals(
                beforeSyntheticResolve,
                LayoutSignature(),
                StringComparison.Ordinal);
        }

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
            if (currentIdx == clampedTarget && !syntheticColumnShiftChanged) return;

            // Capture docking map with PRE-MOVE layout for test recording
            preMoveDockingMapForRecording = (TestRecorder is not null) ?
                DockingMapBuilder.BuildDockingMap(panelId, CurrentLayout, GetCurrentLayoutData().VisiblePanelIds) :
                null;
            
            if (TestRecorder is not null)
            {
                var slotCount = preMoveDockingMapForRecording?.Slots.Count ?? -1;
                SquadDashTrace.Write(TraceCategory.Docking, 
                    $"[docking-recorder] Same-zone move: captured preMove docking map with {slotCount} slots");
            }

            // Reorder the list and reassign Order values using immutable record updates.
            zoneSlots.Remove(panelId);
            zoneSlots.Insert(Math.Clamp(clampedTarget, 0, zoneSlots.Count), panelId);
            CurrentLayout.Slots = CurrentLayout.Slots
                .Where(s => s.Zone != targetZone ||
                            !zoneSlots.Contains(s.PanelId, StringComparer.OrdinalIgnoreCase))
                .Concat(zoneSlots.Select((id, i) => new PanelSlot(id, targetZone, i)))
                .ToList();

            // For test recording: set the pre-move docking map BEFORE calling OnMoveCompleted
            // (which writes the JSON file and needs _dockingMapSlots to be populated)
            if (preMoveDockingMapForRecording is not null && TestRecorder is not null)
            {
                TestRecorder.OnDockingMapBuilt(preMoveDockingMapForRecording.Slots);
            }
            
            if (!syntheticColumnShiftChanged)
                TestRecorder?.OnMoveCompleted(panelId, requestedTargetZone, requestedTargetOrder, insertKind, GetCurrentLayoutData());

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

        preMoveDockingMapForRecording ??= (TestRecorder is not null) ?
            DockingMapBuilder.BuildDockingMap(panelId, CurrentLayout, GetCurrentLayoutData().VisiblePanelIds) :
            null;
        
        if (TestRecorder is not null && insertKind == SyntheticInsertKind.None)
        {
            var slotCount = preMoveDockingMapForRecording?.Slots.Count ?? -1;
            SquadDashTrace.Write(TraceCategory.Docking, 
                $"[docking-recorder] Cross-zone move: captured preMove docking map with {slotCount} slots");
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
        else if (sourceZone == DockZone.Left3)
            RemoveFromZone(_left3ZonePanel!, _left3ZonePanels, element, _left3ZoneScrollViewer as FrameworkElement);
        else if (sourceZone == DockZone.Right3)
            RemoveFromZone(_right3ZonePanel!, _right3ZonePanels, element, _right3ZoneScrollViewer as FrameworkElement);
        else if (sourceZone == DockZone.Left4)
            RemoveFromZone(_left4ZonePanel!, _left4ZonePanels, element, _left4ZoneScrollViewer as FrameworkElement);
        else if (sourceZone == DockZone.Right4)
            RemoveFromZone(_right4ZonePanel!, _right4ZonePanels, element, _right4ZoneScrollViewer as FrameworkElement);
        else if (sourceZone == DockZone.Left5)
            RemoveFromZone(_left5ZonePanel!, _left5ZonePanels, element, _left5ZoneScrollViewer as FrameworkElement);
        else if (sourceZone == DockZone.Right5)
            RemoveFromZone(_right5ZonePanel!, _right5ZonePanels, element, _right5ZoneScrollViewer as FrameworkElement);
        else if (sourceZone == DockZone.Left6)
            RemoveFromZone(_left6ZonePanel!, _left6ZonePanels, element, _left6ZoneScrollViewer as FrameworkElement);
        else if (sourceZone == DockZone.Right6)
            RemoveFromZone(_right6ZonePanel!, _right6ZonePanels, element, _right6ZoneScrollViewer as FrameworkElement);
        else
            RemoveFromTopZone(element);

        switch (targetZone)
        {
            case DockZone.Left:
                AddToZone(_leftZonePanel!, _leftZonePanels, element, panelId, _leftZoneScrollViewer as FrameworkElement, insertAt);
                if (element.Visibility != Visibility.Collapsed)
                {
                    if (ExpandZone(_leftZoneColumn!, _leftSplitterColumn!, _leftZoneScrollViewer!, _leftZoneSplitter!, element))
                        ScheduleZoneHeightRefresh(_leftZonePanel!, _leftZoneScrollViewer as FrameworkElement);
                }
                break;

            case DockZone.Right:
                AddToZone(_rightZonePanel!, _rightZonePanels, element, panelId, _rightZoneScrollViewer as FrameworkElement, insertAt);
                if (element.Visibility != Visibility.Collapsed)
                {
                    if (ExpandZone(_rightZoneColumn!, _rightSplitterColumn!, _rightZoneScrollViewer!, _rightZoneSplitter!, element))
                        ScheduleZoneHeightRefresh(_rightZonePanel!, _rightZoneScrollViewer as FrameworkElement);
                }
                break;

            case DockZone.Left2:
                AddToZone(_left2ZonePanel!, _left2ZonePanels, element, panelId, _left2ZoneScrollViewer as FrameworkElement, insertAt);
                if (element.Visibility != Visibility.Collapsed)
                {
                    if (ExpandZone(_left2ZoneColumn!, _left2SplitterColumn!, _left2ZoneScrollViewer!, _left2ZoneSplitter!, element))
                        ScheduleZoneHeightRefresh(_left2ZonePanel!, _left2ZoneScrollViewer as FrameworkElement);
                }
                break;

            case DockZone.Right2:
                AddToZone(_right2ZonePanel!, _right2ZonePanels, element, panelId, _right2ZoneScrollViewer as FrameworkElement, insertAt);
                if (element.Visibility != Visibility.Collapsed)
                {
                    if (ExpandZone(_right2ZoneColumn!, _right2SplitterColumn!, _right2ZoneScrollViewer!, _right2ZoneSplitter!, element))
                        ScheduleZoneHeightRefresh(_right2ZonePanel!, _right2ZoneScrollViewer as FrameworkElement);
                }
                break;

            case DockZone.Left3:
                AddToZone(_left3ZonePanel!, _left3ZonePanels, element, panelId, _left3ZoneScrollViewer as FrameworkElement, insertAt);
                if (element.Visibility != Visibility.Collapsed)
                {
                    if (ExpandZone(_left3ZoneColumn!, _left3SplitterColumn!, _left3ZoneScrollViewer!, _left3ZoneSplitter!, element))
                        ScheduleZoneHeightRefresh(_left3ZonePanel!, _left3ZoneScrollViewer as FrameworkElement);
                }
                break;

            case DockZone.Right3:
                AddToZone(_right3ZonePanel!, _right3ZonePanels, element, panelId, _right3ZoneScrollViewer as FrameworkElement, insertAt);
                if (element.Visibility != Visibility.Collapsed)
                {
                    if (ExpandZone(_right3ZoneColumn!, _right3SplitterColumn!, _right3ZoneScrollViewer!, _right3ZoneSplitter!, element))
                        ScheduleZoneHeightRefresh(_right3ZonePanel!, _right3ZoneScrollViewer as FrameworkElement);
                }
                break;

            case DockZone.Left4:
                AddToZone(_left4ZonePanel!, _left4ZonePanels, element, panelId, _left4ZoneScrollViewer as FrameworkElement, insertAt);
                if (element.Visibility != Visibility.Collapsed)
                {
                    if (ExpandZone(_left4ZoneColumn!, _left4SplitterColumn!, _left4ZoneScrollViewer!, _left4ZoneSplitter!, element))
                        ScheduleZoneHeightRefresh(_left4ZonePanel!, _left4ZoneScrollViewer as FrameworkElement);
                }
                break;

            case DockZone.Right4:
                AddToZone(_right4ZonePanel!, _right4ZonePanels, element, panelId, _right4ZoneScrollViewer as FrameworkElement, insertAt);
                if (element.Visibility != Visibility.Collapsed)
                {
                    if (ExpandZone(_right4ZoneColumn!, _right4SplitterColumn!, _right4ZoneScrollViewer!, _right4ZoneSplitter!, element))
                        ScheduleZoneHeightRefresh(_right4ZonePanel!, _right4ZoneScrollViewer as FrameworkElement);
                }
                break;

            case DockZone.Left5:
                AddToZone(_left5ZonePanel!, _left5ZonePanels, element, panelId, _left5ZoneScrollViewer as FrameworkElement, insertAt);
                if (element.Visibility != Visibility.Collapsed)
                {
                    if (ExpandZone(_left5ZoneColumn!, _left5SplitterColumn!, _left5ZoneScrollViewer!, _left5ZoneSplitter!, element))
                        ScheduleZoneHeightRefresh(_left5ZonePanel!, _left5ZoneScrollViewer as FrameworkElement);
                }
                break;

            case DockZone.Right5:
                AddToZone(_right5ZonePanel!, _right5ZonePanels, element, panelId, _right5ZoneScrollViewer as FrameworkElement, insertAt);
                if (element.Visibility != Visibility.Collapsed)
                {
                    if (ExpandZone(_right5ZoneColumn!, _right5SplitterColumn!, _right5ZoneScrollViewer!, _right5ZoneSplitter!, element))
                        ScheduleZoneHeightRefresh(_right5ZonePanel!, _right5ZoneScrollViewer as FrameworkElement);
                }
                break;

            case DockZone.Left6:
                AddToZone(_left6ZonePanel!, _left6ZonePanels, element, panelId, _left6ZoneScrollViewer as FrameworkElement, insertAt);
                if (element.Visibility != Visibility.Collapsed)
                {
                    if (ExpandZone(_left6ZoneColumn!, _left6SplitterColumn!, _left6ZoneScrollViewer!, _left6ZoneSplitter!, element))
                        ScheduleZoneHeightRefresh(_left6ZonePanel!, _left6ZoneScrollViewer as FrameworkElement);
                }
                break;

            case DockZone.Right6:
                AddToZone(_right6ZonePanel!, _right6ZonePanels, element, panelId, _right6ZoneScrollViewer as FrameworkElement, insertAt);
                if (element.Visibility != Visibility.Collapsed)
                {
                    if (ExpandZone(_right6ZoneColumn!, _right6SplitterColumn!, _right6ZoneScrollViewer!, _right6ZoneSplitter!, element))
                        ScheduleZoneHeightRefresh(_right6ZonePanel!, _right6ZoneScrollViewer as FrameworkElement);
                }
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
        else if (sourceZone == DockZone.Left3 && !ZoneHasPanels(DockZone.Left3))
        {
            SquadDashTrace.Write(TraceCategory.Docking, $"MovePanel: Left3 zone now empty after moving {panelId} out — collapsing");
            CollapseZone(_left3ZoneColumn!, _left3SplitterColumn!, _left3ZoneScrollViewer!, _left3ZoneSplitter!);
        }
        else if (sourceZone == DockZone.Right3 && !ZoneHasPanels(DockZone.Right3))
        {
            SquadDashTrace.Write(TraceCategory.Docking, $"MovePanel: Right3 zone now empty after moving {panelId} out — collapsing");
            CollapseZone(_right3ZoneColumn!, _right3SplitterColumn!, _right3ZoneScrollViewer!, _right3ZoneSplitter!);
        }
        else if (sourceZone == DockZone.Left4 && !ZoneHasPanels(DockZone.Left4))
        {
            SquadDashTrace.Write(TraceCategory.Docking, $"MovePanel: Left4 zone now empty after moving {panelId} out — collapsing");
            CollapseZone(_left4ZoneColumn!, _left4SplitterColumn!, _left4ZoneScrollViewer!, _left4ZoneSplitter!);
        }
        else if (sourceZone == DockZone.Right4 && !ZoneHasPanels(DockZone.Right4))
        {
            SquadDashTrace.Write(TraceCategory.Docking, $"MovePanel: Right4 zone now empty after moving {panelId} out — collapsing");
            CollapseZone(_right4ZoneColumn!, _right4SplitterColumn!, _right4ZoneScrollViewer!, _right4ZoneSplitter!);
        }
        else if (sourceZone == DockZone.Left5 && !ZoneHasPanels(DockZone.Left5))
        {
            SquadDashTrace.Write(TraceCategory.Docking, $"MovePanel: Left5 zone now empty after moving {panelId} out — collapsing");
            CollapseZone(_left5ZoneColumn!, _left5SplitterColumn!, _left5ZoneScrollViewer!, _left5ZoneSplitter!);
        }
        else if (sourceZone == DockZone.Right5 && !ZoneHasPanels(DockZone.Right5))
        {
            SquadDashTrace.Write(TraceCategory.Docking, $"MovePanel: Right5 zone now empty after moving {panelId} out — collapsing");
            CollapseZone(_right5ZoneColumn!, _right5SplitterColumn!, _right5ZoneScrollViewer!, _right5ZoneSplitter!);
        }
        else if (sourceZone == DockZone.Left6 && !ZoneHasPanels(DockZone.Left6))
        {
            SquadDashTrace.Write(TraceCategory.Docking, $"MovePanel: Left6 zone now empty after moving {panelId} out — collapsing");
            CollapseZone(_left6ZoneColumn!, _left6SplitterColumn!, _left6ZoneScrollViewer!, _left6ZoneSplitter!);
        }
        else if (sourceZone == DockZone.Right6 && !ZoneHasPanels(DockZone.Right6))
        {
            SquadDashTrace.Write(TraceCategory.Docking, $"MovePanel: Right6 zone now empty after moving {panelId} out — collapsing");
            CollapseZone(_right6ZoneColumn!, _right6SplitterColumn!, _right6ZoneScrollViewer!, _right6ZoneSplitter!);
        }
        else if (sourceZone is DockZone.Left or DockZone.Right or DockZone.Left2 or DockZone.Right2 or DockZone.Left3 or DockZone.Right3 or DockZone.Left4 or DockZone.Right4 or DockZone.Left5 or DockZone.Right5 or DockZone.Left6 or DockZone.Right6)
        {
            int remaining = CurrentLayout.Slots.Count(s => s.Zone == sourceZone);
            SquadDashTrace.Write(TraceCategory.Docking,
                $"MovePanel: {sourceZone} still has {remaining} panel(s) after moving {panelId} — not collapsing");
        }

        } // end try
        finally
        {
            if (isTopLevel)
            {
                // Only normalize when a side zone was vacated — that's the only way gaps can arise.
                // Skip when source is Top/null (fresh placement), which can't create zone gaps.
                bool sourceIsSideZone = sourceZoneCapture is
                    DockZone.Left or DockZone.Left2 or DockZone.Left3 or DockZone.Left4 or DockZone.Left5 or DockZone.Left6 or
                    DockZone.Right or DockZone.Right2 or DockZone.Right3 or DockZone.Right4 or DockZone.Right5 or DockZone.Right6;
                if (sourceIsSideZone) NormalizeZoneOrder();
                _isMovingPanel = false;
                // Notify recorder after normalization so it captures the true final layout,
                // not an intermediate state that normalization will immediately undo.
                // Same-zone reorders already fired at the call site above; skip them here.
                if (sourceZoneCapture != targetZone || syntheticColumnShiftChanged)
                {
                    // For test recording: set the pre-move docking map BEFORE calling OnMoveCompleted
                    // (which writes the JSON file and needs _dockingMapSlots to be populated)
                    if (preMoveDockingMapForRecording is not null && TestRecorder is not null)
                    {
                        TestRecorder.OnDockingMapBuilt(preMoveDockingMapForRecording.Slots);
                    }
                    
                    TestRecorder?.OnMoveCompleted(panelId, requestedTargetZone, requestedTargetOrder, insertKind, GetCurrentLayoutData());
                }
            }
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
            if (wasCollapsed)
                ScheduleZoneHeightRefresh(zoneGrid, scrollViewer as FrameworkElement);
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
            DockZone.Left3  => (_left3ZoneColumn,  _left3SplitterColumn,  _left3ZoneScrollViewer,  _left3ZoneSplitter),
            DockZone.Right3 => (_right3ZoneColumn, _right3SplitterColumn, _right3ZoneScrollViewer, _right3ZoneSplitter),
            DockZone.Left4  => (_left4ZoneColumn,  _left4SplitterColumn,  _left4ZoneScrollViewer,  _left4ZoneSplitter),
            DockZone.Right4 => (_right4ZoneColumn, _right4SplitterColumn, _right4ZoneScrollViewer, _right4ZoneSplitter),
            DockZone.Left5  => (_left5ZoneColumn,  _left5SplitterColumn,  _left5ZoneScrollViewer,  _left5ZoneSplitter),
            DockZone.Right5 => (_right5ZoneColumn, _right5SplitterColumn, _right5ZoneScrollViewer, _right5ZoneSplitter),
            DockZone.Left6  => (_left6ZoneColumn,  _left6SplitterColumn,  _left6ZoneScrollViewer,  _left6ZoneSplitter),
            DockZone.Right6 => (_right6ZoneColumn, _right6SplitterColumn, _right6ZoneScrollViewer, _right6ZoneSplitter),
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
            DockZone.Left3  => (_left3ZonePanels,  _left3ZonePanel,  _left3ZoneScrollViewer),
            DockZone.Right3 => (_right3ZonePanels, _right3ZonePanel, _right3ZoneScrollViewer),
            DockZone.Left4  => (_left4ZonePanels,  _left4ZonePanel,  _left4ZoneScrollViewer),
            DockZone.Right4 => (_right4ZonePanels, _right4ZonePanel, _right4ZoneScrollViewer),
            DockZone.Left5  => (_left5ZonePanels,  _left5ZonePanel,  _left5ZoneScrollViewer),
            DockZone.Right5 => (_right5ZonePanels, _right5ZonePanel, _right5ZoneScrollViewer),
            DockZone.Left6  => (_left6ZonePanels,  _left6ZonePanel,  _left6ZoneScrollViewer),
            DockZone.Right6 => (_right6ZonePanels, _right6ZonePanel, _right6ZoneScrollViewer),
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

        // Collapsed panels are still tracked in the zone list (so their order is preserved for
        // when they are shown again) but must not receive a star row — a Collapsed element in a
        // Height="*" row still occupies its proportional share of the grid height, leaving a
        // visible empty gap between the panels that are actually rendered.
        var visible = panels.Where(p => p.Visibility != Visibility.Collapsed).ToList();

        for (int i = 0; i < visible.Count; i++)
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

            double starValue = weights is not null && weights.TryGetValue(visible[i], out double w) ? w : 1.0;
            // MinHeight is only meaningful when a GridSplitter is present (2+ panels) so the user
            // cannot drag a panel to zero.  A solo panel has no splitter and must be allowed to
            // start at zero height (NaN zone) without creating a spurious visible stub.
            zone.RowDefinitions.Add(new RowDefinition
            {
                Height    = new GridLength(starValue, GridUnitType.Star),
                MinHeight = visible.Count > 1 ? 100 : 0,
            });
            Grid.SetRow(visible[i], zone.RowDefinitions.Count - 1);
            zone.Children.Add(visible[i]);
        }

        // Bind the zone Grid height to the scroll viewer so star rows fill the column.
        BindingOperations.ClearBinding(zone, FrameworkElement.HeightProperty);
        if (scrollViewer is not null && visible.Count > 0)
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
        element.MaxWidth = TopZonePanelMaxWidth;
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

        var assignments = new System.Text.StringBuilder();
        for (int rank = 0; rank < topSlots.Count && rank < TopZonePhysicalColumns.Length; rank++)
        {
            var col = TopZonePhysicalColumns[rank];
            assignments.Append($" {topSlots[rank].PanelId}→col{col}");
            if (_panelRegistry.TryGetValue(topSlots[rank].PanelId, out var element))
                Grid.SetColumn(element, col);
        }
        SquadDashTrace.Write(TraceCategory.Docking, $"RebuildTopZoneLayout:{assignments}");
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

    /// <returns><c>true</c> when the zone was collapsed and has just been expanded;
    /// <c>false</c> when it was already visible (no change made).</returns>
    private static bool ExpandZone(
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
        return wasCollapsed;
    }

    /// <summary>
    /// When a synthetic <see cref="SyntheticInsertKind.InsertBefore"/> thin-slot drop targets a
    /// zone whose adjacent outer zone is occupied and an even-more-outer zone is empty, shift
    /// the adjacent outer zone's panels one step further out to make room for a new standalone
    /// column.  Returns the (possibly redirected) zone and order to use for the main move.
    /// </summary>
    /// <remarks>
    /// Examples:
    /// <list type="bullet">
    /// <item>InsertBefore Left@0, Left2 occupied, Left3 empty → shift Left2→Left3, target Left2.</item>
    /// <item>InsertBefore Right2@0, Right2 occupied, Right3 empty → shift Right2→Right3, target Right2.</item>
    /// <item>InsertBefore Right@0, Right occupied, Right2 empty → shift Right→Right2, target Right.</item>
    /// </list>
    /// Falls back to the original zone/order when the shift is not possible (outer zone already occupied).
    /// </remarks>
    private (DockZone zone, int order) ResolveInsertBeforeColumnShift(
        string movingPanelId, DockZone requestedZone, int requestedOrder)
    {
        // InsertBefore Left@0: "new column between Left2 and Left"
        // Case A: Left2 occupied, Left3 empty → shift Left2→Left3, redirect target to Left2.
        // Case B: Left2 empty,    Left occupied → shift Left→Left2, target Left.
        // Case C: Left2+Left3 occupied, Left4 empty → Left3→Left4, Left2→Left3, target Left2.
        if (requestedZone == DockZone.Left && requestedOrder == 0)
        {
            var left2 = GetZoneSlotsExcluding(DockZone.Left2, movingPanelId);
            var left3 = GetZoneSlotsExcluding(DockZone.Left3, movingPanelId);
            var left4 = GetZoneSlotsExcluding(DockZone.Left4, movingPanelId);
            var left5 = GetZoneSlotsExcluding(DockZone.Left5, movingPanelId);
            var left6 = GetZoneSlotsExcluding(DockZone.Left6, movingPanelId);
            if (left2.Count > 0 && left3.Count > 0 && left4.Count > 0 && left5.Count > 0 && left6.Count == 0)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertBefore Left@0 — cascade5: Left5→Left6, Left4→Left5, Left3→Left4, Left2→Left3, targeting Left2");
                foreach (var s in left5) MovePanel(s.PanelId, DockZone.Left6, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left4, movingPanelId)) MovePanel(s.PanelId, DockZone.Left5, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left3, movingPanelId)) MovePanel(s.PanelId, DockZone.Left4, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left2, movingPanelId)) MovePanel(s.PanelId, DockZone.Left3, s.Order);
                return (DockZone.Left2, 0);
            }
            if (left2.Count > 0 && left3.Count > 0 && left4.Count > 0 && left5.Count == 0)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertBefore Left@0 — cascade4: Left4→Left5, Left3→Left4, Left2→Left3, targeting Left2");
                foreach (var s in left4) MovePanel(s.PanelId, DockZone.Left5, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left3, movingPanelId)) MovePanel(s.PanelId, DockZone.Left4, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left2, movingPanelId)) MovePanel(s.PanelId, DockZone.Left3, s.Order);
                return (DockZone.Left2, 0);
            }
            if (left2.Count > 0 && left3.Count > 0 && left4.Count == 0)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertBefore Left@0 — cascade: Left3({left3.Count})→Left4, Left2({left2.Count})→Left3, targeting Left2");
                foreach (var s in left3)
                    MovePanel(s.PanelId, DockZone.Left4, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left2, movingPanelId))
                    MovePanel(s.PanelId, DockZone.Left3, s.Order);
                return (DockZone.Left2, 0);
            }
            if (left2.Count > 0 && left3.Count == 0)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertBefore Left@0 — shifting Left2({left2.Count}) → Left3, redirecting target to Left2");
                foreach (var s in left2)
                    MovePanel(s.PanelId, DockZone.Left3, s.Order);
                return (DockZone.Left2, 0);
            }
            var left = GetZoneSlotsExcluding(DockZone.Left, movingPanelId);
            if (left.Count > 0 && left2.Count == 0)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertBefore Left@0 — shifting Left({left.Count}) → Left2, targeting Left");
                foreach (var s in left)
                    MovePanel(s.PanelId, DockZone.Left2, s.Order);
                return (DockZone.Left, 0);
            }
        }

        // InsertBefore Left2@0: "new column between Left and Left2"
        if (requestedZone == DockZone.Left2 && requestedOrder == 0)
        {
            var left2 = GetZoneSlotsExcluding(DockZone.Left2, movingPanelId);
            var left3 = GetZoneSlotsExcluding(DockZone.Left3, movingPanelId);
            var left4 = GetZoneSlotsExcluding(DockZone.Left4, movingPanelId);
            var left5 = GetZoneSlotsExcluding(DockZone.Left5, movingPanelId);
            var left6 = GetZoneSlotsExcluding(DockZone.Left6, movingPanelId);
            if (left2.Count > 0 && left3.Count > 0 && left4.Count > 0 && left5.Count > 0 && left6.Count == 0)
            {
                foreach (var s in left5) MovePanel(s.PanelId, DockZone.Left6, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left4, movingPanelId)) MovePanel(s.PanelId, DockZone.Left5, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left3, movingPanelId)) MovePanel(s.PanelId, DockZone.Left4, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left2, movingPanelId)) MovePanel(s.PanelId, DockZone.Left3, s.Order);
                return (DockZone.Left2, 0);
            }
            if (left2.Count > 0 && left3.Count > 0 && left4.Count > 0 && left5.Count == 0)
            {
                foreach (var s in left4) MovePanel(s.PanelId, DockZone.Left5, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left3, movingPanelId)) MovePanel(s.PanelId, DockZone.Left4, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left2, movingPanelId)) MovePanel(s.PanelId, DockZone.Left3, s.Order);
                return (DockZone.Left2, 0);
            }
            if (left2.Count > 0 && left3.Count > 0 && left4.Count == 0)
            {
                foreach (var s in left3) MovePanel(s.PanelId, DockZone.Left4, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left2, movingPanelId)) MovePanel(s.PanelId, DockZone.Left3, s.Order);
                return (DockZone.Left2, 0);
            }
            if (left2.Count > 0 && left3.Count == 0)
            {
                foreach (var s in left2) MovePanel(s.PanelId, DockZone.Left3, s.Order);
                return (DockZone.Left2, 0);
            }
        }

        // InsertBefore Left3@0
        if (requestedZone == DockZone.Left3 && requestedOrder == 0)
        {
            var left3 = GetZoneSlotsExcluding(DockZone.Left3, movingPanelId);
            var left4 = GetZoneSlotsExcluding(DockZone.Left4, movingPanelId);
            var left5 = GetZoneSlotsExcluding(DockZone.Left5, movingPanelId);
            var left6 = GetZoneSlotsExcluding(DockZone.Left6, movingPanelId);
            if (left3.Count > 0 && left4.Count > 0 && left5.Count > 0 && left6.Count == 0)
            {
                foreach (var s in left5) MovePanel(s.PanelId, DockZone.Left6, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left4, movingPanelId)) MovePanel(s.PanelId, DockZone.Left5, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left3, movingPanelId)) MovePanel(s.PanelId, DockZone.Left4, s.Order);
                return (DockZone.Left3, 0);
            }
            if (left3.Count > 0 && left4.Count > 0 && left5.Count == 0)
            {
                foreach (var s in left4) MovePanel(s.PanelId, DockZone.Left5, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left3, movingPanelId)) MovePanel(s.PanelId, DockZone.Left4, s.Order);
                return (DockZone.Left3, 0);
            }
            if (left3.Count > 0 && left4.Count == 0)
            {
                foreach (var s in left3) MovePanel(s.PanelId, DockZone.Left4, s.Order);
                return (DockZone.Left3, 0);
            }
        }

        // InsertBefore Left4@0
        if (requestedZone == DockZone.Left4 && requestedOrder == 0)
        {
            var left4 = GetZoneSlotsExcluding(DockZone.Left4, movingPanelId);
            var left5 = GetZoneSlotsExcluding(DockZone.Left5, movingPanelId);
            var left6 = GetZoneSlotsExcluding(DockZone.Left6, movingPanelId);
            if (left4.Count > 0 && left5.Count > 0 && left6.Count == 0)
            {
                foreach (var s in left5) MovePanel(s.PanelId, DockZone.Left6, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left4, movingPanelId)) MovePanel(s.PanelId, DockZone.Left5, s.Order);
                return (DockZone.Left4, 0);
            }
            if (left4.Count > 0 && left5.Count == 0)
            {
                foreach (var s in left4) MovePanel(s.PanelId, DockZone.Left5, s.Order);
                return (DockZone.Left4, 0);
            }
        }

        // InsertBefore Left5@0
        if (requestedZone == DockZone.Left5 && requestedOrder == 0)
        {
            var left5 = GetZoneSlotsExcluding(DockZone.Left5, movingPanelId);
            if (left5.Count > 0)
            {
                foreach (var s in left5) MovePanel(s.PanelId, DockZone.Left6, s.Order);
                return (DockZone.Left5, 0);
            }
        }

        // InsertBefore Right2@0: "new column between Right and Right2"
        // Single: Right2 occupied, Right3 empty → shift Right2→Right3, target Right2.
        // Cascade: Right2+Right3 occupied, Right4 empty → Right3→Right4, Right2→Right3, target Right2.
        if (requestedZone == DockZone.Right2 && requestedOrder == 0)
        {
            var right2 = GetZoneSlotsExcluding(DockZone.Right2, movingPanelId);
            var right3 = GetZoneSlotsExcluding(DockZone.Right3, movingPanelId);
            var right4 = GetZoneSlotsExcluding(DockZone.Right4, movingPanelId);
            var right5 = GetZoneSlotsExcluding(DockZone.Right5, movingPanelId);
            var right6 = GetZoneSlotsExcluding(DockZone.Right6, movingPanelId);
            if (right2.Count > 0 && right3.Count > 0 && right4.Count > 0 && right5.Count > 0 && right6.Count == 0)
            {
                foreach (var s in right5) MovePanel(s.PanelId, DockZone.Right6, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right4, movingPanelId)) MovePanel(s.PanelId, DockZone.Right5, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right3, movingPanelId)) MovePanel(s.PanelId, DockZone.Right4, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right2, movingPanelId)) MovePanel(s.PanelId, DockZone.Right3, s.Order);
                return (DockZone.Right2, 0);
            }
            if (right2.Count > 0 && right3.Count > 0 && right4.Count > 0 && right5.Count == 0)
            {
                foreach (var s in right4) MovePanel(s.PanelId, DockZone.Right5, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right3, movingPanelId)) MovePanel(s.PanelId, DockZone.Right4, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right2, movingPanelId)) MovePanel(s.PanelId, DockZone.Right3, s.Order);
                return (DockZone.Right2, 0);
            }
            if (right2.Count > 0 && right3.Count > 0 && right4.Count == 0)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertBefore Right2@0 — cascade: Right3({right3.Count})→Right4, Right2({right2.Count})→Right3, targeting Right2");
                foreach (var s in right3)
                    MovePanel(s.PanelId, DockZone.Right4, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right2, movingPanelId))
                    MovePanel(s.PanelId, DockZone.Right3, s.Order);
                return (DockZone.Right2, 0);
            }
            if (right2.Count > 0 && right3.Count == 0)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertBefore Right2@0 — shifting Right2({right2.Count}) → Right3, targeting Right2");
                foreach (var s in right2)
                    MovePanel(s.PanelId, DockZone.Right3, s.Order);
                return (DockZone.Right2, 0);
            }
        }

        // InsertBefore Right3@0
        if (requestedZone == DockZone.Right3 && requestedOrder == 0)
        {
            var right3 = GetZoneSlotsExcluding(DockZone.Right3, movingPanelId);
            var right4 = GetZoneSlotsExcluding(DockZone.Right4, movingPanelId);
            var right5 = GetZoneSlotsExcluding(DockZone.Right5, movingPanelId);
            var right6 = GetZoneSlotsExcluding(DockZone.Right6, movingPanelId);
            if (right3.Count > 0 && right4.Count > 0 && right5.Count > 0 && right6.Count == 0)
            {
                foreach (var s in right5) MovePanel(s.PanelId, DockZone.Right6, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right4, movingPanelId)) MovePanel(s.PanelId, DockZone.Right5, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right3, movingPanelId)) MovePanel(s.PanelId, DockZone.Right4, s.Order);
                return (DockZone.Right3, 0);
            }
            if (right3.Count > 0 && right4.Count > 0 && right5.Count == 0)
            {
                foreach (var s in right4) MovePanel(s.PanelId, DockZone.Right5, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right3, movingPanelId)) MovePanel(s.PanelId, DockZone.Right4, s.Order);
                return (DockZone.Right3, 0);
            }
            if (right3.Count > 0 && right4.Count == 0)
            {
                foreach (var s in right3) MovePanel(s.PanelId, DockZone.Right4, s.Order);
                return (DockZone.Right3, 0);
            }
        }

        // InsertBefore Right4@0
        if (requestedZone == DockZone.Right4 && requestedOrder == 0)
        {
            var right4 = GetZoneSlotsExcluding(DockZone.Right4, movingPanelId);
            var right5 = GetZoneSlotsExcluding(DockZone.Right5, movingPanelId);
            var right6 = GetZoneSlotsExcluding(DockZone.Right6, movingPanelId);
            if (right4.Count > 0 && right5.Count > 0 && right6.Count == 0)
            {
                foreach (var s in right5) MovePanel(s.PanelId, DockZone.Right6, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right4, movingPanelId)) MovePanel(s.PanelId, DockZone.Right5, s.Order);
                return (DockZone.Right4, 0);
            }
            if (right4.Count > 0 && right5.Count == 0)
            {
                foreach (var s in right4) MovePanel(s.PanelId, DockZone.Right5, s.Order);
                return (DockZone.Right4, 0);
            }
        }

        // InsertBefore Right5@0
        if (requestedZone == DockZone.Right5 && requestedOrder == 0)
        {
            var right5 = GetZoneSlotsExcluding(DockZone.Right5, movingPanelId);
            if (right5.Count > 0)
            {
                foreach (var s in right5) MovePanel(s.PanelId, DockZone.Right6, s.Order);
                return (DockZone.Right5, 0);
            }
        }

        // InsertBefore Right@0: "new column between center and Right"
        // Single-step:    Right→Right2 (if Right2 empty)
        // Cascade 2-step: Right2→Right3, Right→Right2 (if Right2+Right occupied, Right3 empty)
        // Cascade 3-step: Right3→Right4, Right2→Right3, Right→Right2 (all three occupied, Right4 empty)
        if (requestedZone == DockZone.Right && requestedOrder == 0)
        {
            var right  = GetZoneSlotsExcluding(DockZone.Right,  movingPanelId);
            var right2 = GetZoneSlotsExcluding(DockZone.Right2, movingPanelId);
            var right3 = GetZoneSlotsExcluding(DockZone.Right3, movingPanelId);
            var right4 = GetZoneSlotsExcluding(DockZone.Right4, movingPanelId);
            var right5 = GetZoneSlotsExcluding(DockZone.Right5, movingPanelId);
            var right6 = GetZoneSlotsExcluding(DockZone.Right6, movingPanelId);
            if (right.Count > 0 && right2.Count > 0 && right3.Count > 0 && right4.Count > 0 && right5.Count > 0 && right6.Count == 0)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertBefore Right@0 — cascade5: Right5→Right6, Right4→Right5, Right3→Right4, Right2→Right3, Right→Right2, targeting Right");
                foreach (var s in right5) MovePanel(s.PanelId, DockZone.Right6, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right4, movingPanelId)) MovePanel(s.PanelId, DockZone.Right5, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right3, movingPanelId)) MovePanel(s.PanelId, DockZone.Right4, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right2, movingPanelId)) MovePanel(s.PanelId, DockZone.Right3, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right, movingPanelId)) MovePanel(s.PanelId, DockZone.Right2, s.Order);
                return (DockZone.Right, 0);
            }
            if (right.Count > 0 && right2.Count > 0 && right3.Count > 0 && right4.Count > 0 && right5.Count == 0)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertBefore Right@0 — cascade4: Right4→Right5, Right3→Right4, Right2→Right3, Right→Right2, targeting Right");
                foreach (var s in right4) MovePanel(s.PanelId, DockZone.Right5, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right3, movingPanelId)) MovePanel(s.PanelId, DockZone.Right4, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right2, movingPanelId)) MovePanel(s.PanelId, DockZone.Right3, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right, movingPanelId)) MovePanel(s.PanelId, DockZone.Right2, s.Order);
                return (DockZone.Right, 0);
            }
            if (right.Count > 0 && right2.Count > 0 && right3.Count > 0 && right4.Count == 0)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertBefore Right@0 — cascade3: Right3({right3.Count})→Right4, Right2({right2.Count})→Right3, Right({right.Count})→Right2, targeting Right");
                foreach (var s in right3)
                    MovePanel(s.PanelId, DockZone.Right4, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right2, movingPanelId))
                    MovePanel(s.PanelId, DockZone.Right3, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right, movingPanelId))
                    MovePanel(s.PanelId, DockZone.Right2, s.Order);
                return (DockZone.Right, 0);
            }
            if (right.Count > 0 && right2.Count == 0)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertBefore Right@0 — shifting Right({right.Count}) → Right2, targeting Right");
                foreach (var s in right)
                    MovePanel(s.PanelId, DockZone.Right2, s.Order);
                return (DockZone.Right, 0);
            }
            if (right.Count > 0 && right2.Count > 0 && right3.Count == 0)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertBefore Right@0 — cascade: Right2({right2.Count})→Right3, Right({right.Count})→Right2, targeting Right");
                foreach (var s in right2)
                    MovePanel(s.PanelId, DockZone.Right3, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Right, movingPanelId))
                    MovePanel(s.PanelId, DockZone.Right2, s.Order);
                return (DockZone.Right, 0);
            }
        }

        return (requestedZone, requestedOrder); // fallback: stack in zone as usual
    }

    /// <summary>
    /// Resolves an <c>InsertAfter</c> thin-slot drop on a left-side zone into a column-shift operation.
    /// "InsertAfter Left@N" means the user dropped on the inner-edge thin (right side of the Left column),
    /// intending to create a new innermost column.  The existing Left occupants shift outward.
    /// </summary>
    private (DockZone zone, int order) ResolveInsertAfterColumnShift(
        string movingPanelId, DockZone requestedZone, int requestedOrder)
    {
        // InsertAfter Left@N: inner-edge thin on the left side.
        // New panel should land at Left (innermost); existing Left shifts to Left2, Left2 shifts to Left3, Left3 shifts to Left4.
        if (requestedZone == DockZone.Left)
        {
            var left  = GetZoneSlotsExcluding(DockZone.Left,  movingPanelId);
            var left2 = GetZoneSlotsExcluding(DockZone.Left2, movingPanelId);
            var left3 = GetZoneSlotsExcluding(DockZone.Left3, movingPanelId);
            var left4 = GetZoneSlotsExcluding(DockZone.Left4, movingPanelId);
            var left5 = GetZoneSlotsExcluding(DockZone.Left5, movingPanelId);
            var left6 = GetZoneSlotsExcluding(DockZone.Left6, movingPanelId);

            if (left.Count > 0 && left2.Count > 0 && left3.Count > 0 && left4.Count > 0 && left5.Count > 0 && left6.Count == 0)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertAfter Left — cascade5: Left5→Left6, Left4→Left5, Left3→Left4, Left2→Left3, Left→Left2, targeting Left");
                foreach (var s in left5) MovePanel(s.PanelId, DockZone.Left6, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left4, movingPanelId)) MovePanel(s.PanelId, DockZone.Left5, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left3, movingPanelId)) MovePanel(s.PanelId, DockZone.Left4, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left2, movingPanelId)) MovePanel(s.PanelId, DockZone.Left3, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left, movingPanelId)) MovePanel(s.PanelId, DockZone.Left2, s.Order);
                return (DockZone.Left, 0);
            }
            if (left.Count > 0 && left2.Count > 0 && left3.Count > 0 && left4.Count > 0 && left5.Count == 0)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertAfter Left — cascade4: Left4→Left5, Left3→Left4, Left2→Left3, Left→Left2, targeting Left");
                foreach (var s in left4) MovePanel(s.PanelId, DockZone.Left5, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left3, movingPanelId)) MovePanel(s.PanelId, DockZone.Left4, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left2, movingPanelId)) MovePanel(s.PanelId, DockZone.Left3, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left, movingPanelId)) MovePanel(s.PanelId, DockZone.Left2, s.Order);
                return (DockZone.Left, 0);
            }
            if (left.Count > 0 && left2.Count > 0 && left3.Count > 0 && left4.Count == 0)
            {
                // Cascade 3-step: L3→L4, L2→L3, L→L2, new panel→L@0
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertAfter Left — cascade3: Left3({left3.Count})→Left4, Left2({left2.Count})→Left3, Left({left.Count})→Left2, targeting Left");
                foreach (var s in left3)
                    MovePanel(s.PanelId, DockZone.Left4, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left2, movingPanelId))
                    MovePanel(s.PanelId, DockZone.Left3, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left, movingPanelId))
                    MovePanel(s.PanelId, DockZone.Left2, s.Order);
                return (DockZone.Left, 0);
            }
            if (left.Count > 0 && left2.Count > 0 && left3.Count == 0)
            {
                // Cascade: L2→L3, L→L2, new panel→L@0
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertAfter Left — cascade: Left2({left2.Count})→Left3, Left({left.Count})→Left2, targeting Left");
                foreach (var s in left2)
                    MovePanel(s.PanelId, DockZone.Left3, s.Order);
                foreach (var s in GetZoneSlotsExcluding(DockZone.Left, movingPanelId))
                    MovePanel(s.PanelId, DockZone.Left2, s.Order);
                return (DockZone.Left, 0);
            }
            if (left.Count > 0 && left2.Count == 0)
            {
                // Single step: L→L2, new panel→L@0
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"MovePanel: InsertAfter Left — shifting Left({left.Count}) → Left2, targeting Left");
                foreach (var s in left)
                    MovePanel(s.PanelId, DockZone.Left2, s.Order);
                return (DockZone.Left, 0);
            }
        }

        return (requestedZone, requestedOrder); // fallback: stack in zone as usual
    }

    /// <summary>Returns all slots in <paramref name="zone"/>, ordered by <c>Order</c>, excluding the specified panel.</summary>
    private List<PanelSlot> GetZoneSlotsExcluding(DockZone zone, string excludePanelId) =>
        CurrentLayout.Slots
            .Where(s => s.Zone == zone && !string.Equals(s.PanelId, excludePanelId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Order)
            .ToList();

    /// <summary>Returns all slots in <paramref name="zone"/>, ordered by <c>Order</c>.</summary>
    private List<PanelSlot> GetZoneSlots(DockZone zone) =>
        CurrentLayout.Slots.Where(s => s.Zone == zone).OrderBy(s => s.Order).ToList();

    private bool _isNormalizingZones = false;
    private bool _isMovingPanel      = false;

    /// <summary>
    /// Eliminates zone gaps by cascading panels inward.  A gap exists when an inner zone is
    /// empty but an outer zone is occupied (e.g. Left3=Tasks, Left2=empty, Left=Approvals).
    /// Guards against re-entry so recursive <see cref="MovePanel"/> calls triggered during
    /// normalization do not start another normalization pass.
    /// </summary>
    private void NormalizeZoneOrder()
    {
        if (_isNormalizingZones) return;
        _isNormalizingZones = true;
        try
        {
            NormalizeSide(DockZone.Left, DockZone.Left2, DockZone.Left3, DockZone.Left4, DockZone.Left5, DockZone.Left6);
            NormalizeSide(DockZone.Right, DockZone.Right2, DockZone.Right3, DockZone.Right4, DockZone.Right5, DockZone.Right6);
        }
        finally
        {
            _isNormalizingZones = false;
        }
    }

    /// <summary>
    /// Slides panels inward until no gaps remain for the six-zone column family.
    /// Repeats until stable because filling mid may expose a gap between inner and mid.
    /// </summary>
    private void NormalizeSide(DockZone inner, DockZone mid, DockZone outer, DockZone outermost, DockZone outer5, DockZone outer6)
    {
        bool changed;
        do
        {
            changed = false;
            // If outer5 is empty but outer6 is occupied → slide outer6 → outer5.
            if (!GetZoneSlots(outer5).Any() && GetZoneSlots(outer6).Any())
            {
                var outer6Slots = GetZoneSlots(outer6);
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"NormalizeZoneOrder: {outer5} empty — sliding {outer6}({outer6Slots.Count}) → {outer5}");
                foreach (var s in outer6Slots)
                    MovePanel(s.PanelId, outer5, s.Order);
                changed = true;
            }
            // If outermost is empty but outer5 is occupied → slide outer5 → outermost.
            if (!GetZoneSlots(outermost).Any() && GetZoneSlots(outer5).Any())
            {
                var outer5Slots = GetZoneSlots(outer5);
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"NormalizeZoneOrder: {outermost} empty — sliding {outer5}({outer5Slots.Count}) → {outermost}");
                foreach (var s in outer5Slots)
                    MovePanel(s.PanelId, outermost, s.Order);
                changed = true;
            }
            // If outer is empty but outermost is occupied → slide outermost → outer.
            if (!GetZoneSlots(outer).Any() && GetZoneSlots(outermost).Any())
            {
                var outermostSlots = GetZoneSlots(outermost);
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"NormalizeZoneOrder: {outer} empty — sliding {outermost}({outermostSlots.Count}) → {outer}");
                foreach (var s in outermostSlots)
                    MovePanel(s.PanelId, outer, s.Order);
                changed = true;
            }
            // If mid is empty but outer is occupied → slide outer → mid.
            if (!GetZoneSlots(mid).Any() && GetZoneSlots(outer).Any())
            {
                var outerSlots = GetZoneSlots(outer);
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"NormalizeZoneOrder: {mid} empty — sliding {outer}({outerSlots.Count}) → {mid}");
                foreach (var s in outerSlots)
                    MovePanel(s.PanelId, mid, s.Order);
                changed = true;
            }
            // If inner is empty but mid is occupied → slide mid → inner.
            if (!GetZoneSlots(inner).Any() && GetZoneSlots(mid).Any())
            {
                var midSlots = GetZoneSlots(mid);
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"NormalizeZoneOrder: {inner} empty — sliding {mid}({midSlots.Count}) → {inner}");
                foreach (var s in midSlots)
                    MovePanel(s.PanelId, inner, s.Order);
                changed = true;
            }
        } while (changed);
    }

    /// <summary>
    /// Schedules a deferred refresh of <paramref name="zonePanel"/>'s height binding at
    /// <see cref="System.Windows.Threading.DispatcherPriority.Loaded"/> priority.
    /// Call this after expanding a previously-collapsed zone: the layout pass triggered by
    /// <see cref="ExpandZone"/> must complete before the scroll viewer's
    /// <c>ActualHeight</c> is non-zero, and the deferred call re-applies the binding at
    /// that point so the zone panel gets the correct height.
    /// </summary>
    private static void ScheduleZoneHeightRefresh(Grid zonePanel, FrameworkElement? scrollViewer)
    {
        if (scrollViewer is null) return;
        Application.Current.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (scrollViewer.ActualHeight > 0)
                    zonePanel.SetBinding(
                        FrameworkElement.HeightProperty,
                        new Binding(nameof(FrameworkElement.ActualHeight)) { Source = scrollViewer });
            }));
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
            Left3ZoneWidth  = (_left3ZoneColumn  is { } l3c && l3c.Width.IsAbsolute && l3c.Width.Value > 0) ? l3c.Width.Value : (double?)null,
            Right3ZoneWidth = (_right3ZoneColumn is { } r3c && r3c.Width.IsAbsolute && r3c.Width.Value > 0) ? r3c.Width.Value : (double?)null,
            Left4ZoneWidth  = (_left4ZoneColumn  is { } l4c && l4c.Width.IsAbsolute && l4c.Width.Value > 0) ? l4c.Width.Value : (double?)null,
            Right4ZoneWidth = (_right4ZoneColumn is { } r4c && r4c.Width.IsAbsolute && r4c.Width.Value > 0) ? r4c.Width.Value : (double?)null,
            Left5ZoneWidth  = (_left5ZoneColumn  is { } l5c && l5c.Width.IsAbsolute && l5c.Width.Value > 0) ? l5c.Width.Value : (double?)null,
            Right5ZoneWidth = (_right5ZoneColumn is { } r5c && r5c.Width.IsAbsolute && r5c.Width.Value > 0) ? r5c.Width.Value : (double?)null,
            Left6ZoneWidth  = (_left6ZoneColumn  is { } l6c && l6c.Width.IsAbsolute && l6c.Width.Value > 0) ? l6c.Width.Value : (double?)null,
            Right6ZoneWidth = (_right6ZoneColumn is { } r6c && r6c.Width.IsAbsolute && r6c.Width.Value > 0) ? r6c.Width.Value : (double?)null,
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

    /// <summary>
    /// Applies a layout described by the zone-name → panel-ids map from a recorded test case.
    /// Zone widths are preserved from the current layout.
    /// </summary>
    public void ApplyTestLayout(Dictionary<string, List<string>> zoneMap)
    {
        var layoutData = DockingLayoutEngine.ParseLayoutFromJson(zoneMap);
        var testPanelIds = new HashSet<string>(
            layoutData.Slots.Select(s => s.PanelId), StringComparer.OrdinalIgnoreCase);

        // Build a set of all panels we're supplementing so we don't double-add.
        var accounted = new HashSet<string>(testPanelIds, StringComparer.OrdinalIgnoreCase);
        var supplementary = new List<PanelSlot>();
        int extraOrder = layoutData.Slots.Count(t => t.Zone == DockZone.Top);

        // Pass 1 — panels tracked in CurrentLayout.Slots but absent from the test JSON.
        // These may be in a side zone; they need to be sent to Top so the layout is
        // self-consistent and RebuildTopZoneLayout can assign them a real column.
        foreach (var slot in CurrentLayout.Slots)
        {
            if (accounted.Contains(slot.PanelId)) continue;
            supplementary.Add(new PanelSlot(slot.PanelId, DockZone.Top, extraOrder++));
            accounted.Add(slot.PanelId);
        }

        // Pass 2 — panels that exist in the registry but are NOT in CurrentLayout.Slots.
        // This catches panels whose slots were wiped by a subsequent LoadAndApplyLayout call
        // (they are still physically present in _topZoneGrid at their natural column, so they
        // will overlap whichever test panel is assigned that same column by RebuildTopZoneLayout).
        if (_panelRegistry is not null)
        {
            foreach (var panelId in _panelRegistry.Keys)
            {
                if (accounted.Contains(panelId)) continue;
                supplementary.Add(new PanelSlot(panelId, DockZone.Top, extraOrder++));
                accounted.Add(panelId);
            }
        }

        var target = new DockLayout
        {
            Name            = CurrentLayout.Name,
            Slots           = layoutData.Slots.Concat(supplementary).ToList(),
            LeftZoneWidth   = CurrentLayout.LeftZoneWidth,
            RightZoneWidth  = CurrentLayout.RightZoneWidth,
            Left2ZoneWidth  = CurrentLayout.Left2ZoneWidth,
            Right2ZoneWidth = CurrentLayout.Right2ZoneWidth,
            Left3ZoneWidth  = CurrentLayout.Left3ZoneWidth,
            Right3ZoneWidth = CurrentLayout.Right3ZoneWidth,
            Left4ZoneWidth  = CurrentLayout.Left4ZoneWidth,
            Right4ZoneWidth = CurrentLayout.Right4ZoneWidth,
            Left5ZoneWidth  = CurrentLayout.Left5ZoneWidth,
            Right5ZoneWidth = CurrentLayout.Right5ZoneWidth,
            Left6ZoneWidth  = CurrentLayout.Left6ZoneWidth,
            Right6ZoneWidth = CurrentLayout.Right6ZoneWidth,
        };
        ApplyLayout(target);
    }

    public void ApplyLayout(DockLayout targetLayout)
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
        Left3ZoneWidth  = layout.Left3ZoneWidth,
        Right3ZoneWidth = layout.Right3ZoneWidth,
        Left4ZoneWidth  = layout.Left4ZoneWidth,
        Right4ZoneWidth = layout.Right4ZoneWidth,
        Left5ZoneWidth  = layout.Left5ZoneWidth,
        Right5ZoneWidth = layout.Right5ZoneWidth,
        Left6ZoneWidth  = layout.Left6ZoneWidth,
        Right6ZoneWidth = layout.Right6ZoneWidth,
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
    /// • Empty column zone              → 8px wide strip (same width as the top-zone insertion indicator) at the column's near edge.<br/>
    /// • Top zone slot, source in top   → full panel screen rect (replace/reorder).<br/>
    /// • Top zone slot, source elsewhere → thin vertical insertion strip.<br/>
    /// • Empty top zone                 → thin horizontal strip across the top-zone grid.
    /// </para>
    /// Returns <see cref="Rect.Empty"/> when the location cannot be determined.
    /// </summary>
    /// <summary>
    /// Returns the screen rect that would be highlighted as the drop preview for
    /// <paramref name="targetZone"/>/<paramref name="targetOrder"/> when the given
    /// <paramref name="sourcePanelId"/> is being dragged. Used by the docking test
    /// playback window to show the preview without requiring a live docking-map hover.
    /// </summary>
    public Rect GetDropPreviewRect(
        string sourcePanelId,
        DockZone targetZone,
        int targetOrder,
        SyntheticInsertKind insertKind = SyntheticInsertKind.None)
    {
        var slot = new SlotButtonViewModel(
            Label:            string.Empty,
            IsSourcePanel:    false,
            IsExpansionButton: false,
            X:                0,
            Y:                0,
            Width:            0,
            Height:           0,
            TargetZone:       targetZone,
            TargetOrder:      targetOrder,
            SourcePanelId:    sourcePanelId)
        {
            InsertKind = insertKind
        };
        return GetSlotScreenRect(slot);
    }

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

        // Synthetic thin slots ("insert before" targets between occupied zone-columns) use the
        // same band-division preview as regular column slots: show band 0 of the target zone's
        // existing column so the user sees exactly where the dropped panel would land.
        if (slot.IsSyntheticInsert)
        {
            SquadDashTrace.Write(TraceCategory.Docking,
                $"GetSlotScreenRect: [synthetic-insert] kind={slot.InsertKind} zone={slot.TargetZone} order={slot.TargetOrder} inZone=[{string.Join(",", panelsInZone)}]");
            return GetSyntheticInsertScreenRect(slot.TargetZone, slot.InsertKind, panelsInZone);
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

    private UIElement? GetZoneScrollViewer(DockZone zone) => zone switch
    {
        DockZone.Left   => _leftZoneScrollViewer,
        DockZone.Right  => _rightZoneScrollViewer,
        DockZone.Left2  => _left2ZoneScrollViewer,
        DockZone.Right2 => _right2ZoneScrollViewer,
        DockZone.Left3  => _left3ZoneScrollViewer,
        DockZone.Right3 => _right3ZoneScrollViewer,
        DockZone.Left4  => _left4ZoneScrollViewer,
        DockZone.Right4 => _right4ZoneScrollViewer,
        DockZone.Left5  => _left5ZoneScrollViewer,
        DockZone.Right5 => _right5ZoneScrollViewer,
        DockZone.Left6  => _left6ZoneScrollViewer,
        DockZone.Right6 => _right6ZoneScrollViewer,
        _               => null,
    };

    private Grid? GetZoneGrid(DockZone zone) => zone switch
    {
        DockZone.Left   => _leftZonePanel,
        DockZone.Right  => _rightZonePanel,
        DockZone.Left2  => _left2ZonePanel,
        DockZone.Right2 => _right2ZonePanel,
        DockZone.Left3  => _left3ZonePanel,
        DockZone.Right3 => _right3ZonePanel,
        DockZone.Left4  => _left4ZonePanel,
        DockZone.Right4 => _right4ZonePanel,
        DockZone.Left5  => _left5ZonePanel,
        DockZone.Right5 => _right5ZonePanel,
        DockZone.Left6  => _left6ZonePanel,
        DockZone.Right6 => _right6ZonePanel,
        _               => null,
    };

    private Rect GetColumnSlotRect(DockZone zone, int targetOrder, List<string> panelsInZone)
    {
        UIElement? container = GetZoneScrollViewer(zone);
        Grid? zoneGrid = GetZoneGrid(zone);

        bool isRightSide = zone == DockZone.Right || zone == DockZone.Right2 || zone == DockZone.Right3 || zone == DockZone.Right4 || zone == DockZone.Right5 || zone == DockZone.Right6;

        if (panelsInZone.Count == 0)
        {
            // Empty zone — 64px wide strip just outside the inner neighbor.
            // "Inner neighbor" = the zone that sits between this one and the center content.
            // Left  → neighbor is the center (TopZonePanelsGrid)
            // Left2 → neighbor is the Left zone scroll-viewer (if visible) else center
            // Right  → neighbor is the center
            // Right2 → neighbor is the Right zone scroll-viewer (if visible) else center
            double StripWidth = InsertionIndicatorWidth;

            Rect neighborRect = GetInnerNeighborRect(zone);
            SquadDashTrace.Write(TraceCategory.Docking,
                $"GetColumnSlotRect(empty): zone={zone} neighborRect={neighborRect} " +
                $"topZoneGrid={(_topZoneGrid is FrameworkElement tg ? $"vis={tg.IsVisible} w={tg.ActualWidth:F0}" : "null")} " +
                $"leftSV={(_leftZoneScrollViewer is FrameworkElement lsv ? $"vis={lsv.IsVisible} w={lsv.ActualWidth:F0}" : "null")} " +
                $"rightSV={(_rightZoneScrollViewer is FrameworkElement rsv ? $"vis={rsv.IsVisible} w={rsv.ActualWidth:F0}" : "null")}");

            if (!neighborRect.IsEmpty)
            {
                // For Left/Right: when the column is collapsed (empty), the center grid extends
                // to the window edge, so neighborRect.Left ≈ 0 and Left - 8 is off-screen.
                // Overlap the center grid's near edge by 8px so the strip is always visible.
                // For Left2/Right2: neighbor is the Left/Right scroll-viewer whose far edge is
                // already at the window boundary — same overlap treatment.
                double x = zone switch
                {
                    DockZone.Left   => neighborRect.Left  - StripWidth / 2,
                    DockZone.Left2  => neighborRect.Left  - StripWidth / 2,
                    DockZone.Left3  => neighborRect.Left  - StripWidth / 2,
                    DockZone.Left4  => neighborRect.Left  - StripWidth / 2,
                    DockZone.Right  => neighborRect.Right - StripWidth / 2,
                    DockZone.Right2 => neighborRect.Right - StripWidth / 2,
                    DockZone.Right3 => neighborRect.Right - StripWidth / 2,
                    DockZone.Right4 => neighborRect.Right - StripWidth / 2,
                    _               => isRightSide ? neighborRect.Right - StripWidth / 2 : neighborRect.Left - StripWidth / 2,
                };

                // The strip spans the neighbor column's bounds.
                double stripTop    = neighborRect.Top;
                double stripHeight = neighborRect.Height;

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

    private Rect GetSyntheticInsertScreenRect(DockZone zone, SyntheticInsertKind kind, List<string> panelsInZone)
    {
        UIElement? container = GetZoneScrollViewer(zone);
        Grid? zoneGrid = GetZoneGrid(zone);

        Rect columnRect = Rect.Empty;
        if (container is FrameworkElement fe && fe.IsVisible)
            columnRect = GetScreenRect(fe);
        else if (zoneGrid is FrameworkElement gfe && gfe.IsVisible)
            columnRect = GetScreenRect(gfe);

        if (columnRect.IsEmpty)
        {
            SquadDashTrace.Write(TraceCategory.Docking,
                $"GetSyntheticInsertScreenRect: zone={zone} kind={kind} — column rect empty, falling back");
            return GetColumnSlotRect(zone, 0, panelsInZone);
        }

        double StripWidth = InsertionIndicatorWidth;

        // InsertBefore = left edge of the zone column (toward center for right-side zones,
        // toward outer for left-side zones).  InsertAfter = right edge.
        // This is symmetric: InsertBefore always lands at columnRect.Left regardless of side.
        double stripX = kind == SyntheticInsertKind.InsertBefore
            ? columnRect.Left   - StripWidth / 2
            : columnRect.Right  - StripWidth / 2;

        double stripTop    = columnRect.Top;
        double stripHeight = columnRect.Height;

        var result = new Rect(stripX, stripTop, StripWidth, stripHeight);
        SquadDashTrace.Write(TraceCategory.Docking,
            $"GetSyntheticInsertScreenRect: zone={zone} kind={kind} columnRect={columnRect} → strip={result}");
        return result;
    }

    /// <summary>
    /// Returns the screen rect of the element immediately "inside" (toward center) of the
    /// given zone — used to anchor the 64px empty-zone strip at the correct outer edge.
    /// </summary>
    private Rect GetInnerNeighborRect(DockZone zone)
    {
        // Try a chain of adjacent scroll-viewers from nearest to the top zone outward.
        // This handles cases where an intermediate zone is empty/invisible.
        UIElement?[] candidates = zone switch
        {
            DockZone.Left2  => new UIElement?[] { _leftZoneScrollViewer },
            DockZone.Left3  => new UIElement?[] { _left2ZoneScrollViewer, _leftZoneScrollViewer },
            DockZone.Left4  => new UIElement?[] { _left3ZoneScrollViewer, _left2ZoneScrollViewer, _leftZoneScrollViewer },
            DockZone.Left5  => new UIElement?[] { _left4ZoneScrollViewer, _left3ZoneScrollViewer, _left2ZoneScrollViewer, _leftZoneScrollViewer },
            DockZone.Left6  => new UIElement?[] { _left5ZoneScrollViewer, _left4ZoneScrollViewer, _left3ZoneScrollViewer, _left2ZoneScrollViewer, _leftZoneScrollViewer },
            DockZone.Right2 => new UIElement?[] { _rightZoneScrollViewer },
            DockZone.Right3 => new UIElement?[] { _right2ZoneScrollViewer, _rightZoneScrollViewer },
            DockZone.Right4 => new UIElement?[] { _right3ZoneScrollViewer, _right2ZoneScrollViewer, _rightZoneScrollViewer },
            DockZone.Right5 => new UIElement?[] { _right4ZoneScrollViewer, _right3ZoneScrollViewer, _right2ZoneScrollViewer, _rightZoneScrollViewer },
            DockZone.Right6 => new UIElement?[] { _right5ZoneScrollViewer, _right4ZoneScrollViewer, _right3ZoneScrollViewer, _right2ZoneScrollViewer, _rightZoneScrollViewer },
            _               => Array.Empty<UIElement?>(),
        };

        foreach (var candidate in candidates)
        {
            if (candidate is FrameworkElement fe && fe.IsVisible && fe.ActualWidth > 0)
            {
                var r = GetScreenRect(fe);
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"GetInnerNeighborRect: zone={zone} using adjacent SV IsVisible={fe.IsVisible} w={fe.ActualWidth:F0} → {r}");
                if (!r.IsEmpty) return r;
            }
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

        double StripW = InsertionIndicatorWidth;

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
