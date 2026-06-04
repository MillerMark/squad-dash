#nullable enable

namespace SquadDash.PanelDocking;

/// <summary>
/// Builds the DockingMapViewModel from the current layout and source panel.
/// Pure data logic — no WPF dependencies.
/// </summary>
internal static class DockingMapBuilder
{
    // Abbreviated panel labels (≤ 6 characters)
    private static readonly Dictionary<string, string> PanelLabels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["loop"]        = "Loop",
            ["tasks"]       = "Tasks",
            ["approvals"]   = "Aprvl",
            ["notes"]       = "Notes",
            ["inbox"]       = "Inbox",
            ["maintenance"] = "Maint",
        };

    // Sizing constants
    private const double ColSlotWidth      = 48;
    private const double ColSlotWidthEmpty = ColSlotWidth / 3;  // narrower placeholder for empty (drop-target-only) zones
    private const double ColSlotHeight     = 48;
    private const double TopSlotWidth      = 40;
    private const double TopSlotHeight     = 50;
    private const double SlotGap           = 4;
    private const double ZoneGutter        = 28;  // pill (4px) + 10px breathing room on each side; used between side zone and top zone
    private const double InnerZoneGap      = 2;   // tight gap between sibling zone pairs (Left↔Left2, Right↔Right2)
    private const double MaxInnerHeight    = 200.0; // caps popup height so stacked panels shrink gracefully
    private const double PopupPadding      = 8;
    private const double LabelRowHeight    = 16;    // reserved space above slots for section labels
    private static readonly DockZone[] LeftSideZones  = BuildSideZones("Left");
    private static readonly DockZone[] RightSideZones = BuildSideZones("Right");

    private sealed class SideZoneState
    {
        public required DockZone Zone { get; init; }
        public required int Tier { get; init; }
        public required List<string> Panels { get; init; }
        public required bool SourceInZone { get; init; }
        public bool Suppressed { get; set; }
        public bool Occupied { get; set; }
        public double Width { get; set; }
        public double X { get; set; }
        public double ContentHeight { get; set; }

        public bool EmptyWithoutSource => Panels.Count == 0 && !SourceInZone;
    }

    private readonly record struct SyntheticThin(
        double X,
        DockZone TargetZone,
        int TargetOrder,
        SyntheticInsertKind Kind);

    private readonly record struct SideSequenceItem(
        SideZoneState? Zone,
        SyntheticInsertKind InsertKind,
        DockZone TargetZone,
        int TargetOrder)
    {
        public bool IsSynthetic => Zone is null;
        public double Width => IsSynthetic ? ColSlotWidthEmpty : Zone!.Width;

        public static SideSequenceItem ForZone(SideZoneState zone) =>
            new(zone, SyntheticInsertKind.None, zone.Zone, 0);

        public static SideSequenceItem ForSynthetic(
            DockZone targetZone,
            int targetOrder,
            SyntheticInsertKind kind) =>
            new(null, kind, targetZone, targetOrder);
    }

    private static DockingMapViewModel BuildDockingMapFromSideStates(
        string sourcePanelId,
        DockLayout currentLayout,
        IReadOnlySet<string>? visiblePanelIds)
    {
        var allSlots = new List<SlotButtonViewModel>();

        var topPanels = FilterZone(PanelsInZone(currentLayout, DockZone.Top), sourcePanelId, visiblePanelIds);
        bool sourceInTop = topPanels.Any(p => Same(p, sourcePanelId));

        var leftStates = BuildSideStates(currentLayout, sourcePanelId, visiblePanelIds, LeftSideZones);
        var rightStates = BuildSideStates(currentLayout, sourcePanelId, visiblePanelIds, RightSideZones);
        PrepareSideStates(leftStates);
        PrepareSideStates(rightStates);

        double topContentWidth = ZoneRowWidth(topPanels.Count, sourceInTop, TopSlotWidth);
        double topZoneWidth = Math.Max(topContentWidth, TopSlotWidth);

        double innerHeight = Math.Max(
            Math.Max(MaxContentHeight(leftStates), MaxContentHeight(rightStates)),
            TopSlotHeight * 2);
        innerHeight = Math.Max(innerHeight, TopSlotHeight * 2 + ZoneGutter);
        innerHeight = Math.Min(innerHeight, MaxInnerHeight);

        double popupHeight = innerHeight + PopupPadding * 2 + LabelRowHeight;

        double curX = 0;
        var leftThinPositions = LayoutSide(leftStates, isLeft: true, ref curX, currentLayout, topPanels, rightStates, sourcePanelId);
        if (HasVisibleSide(leftStates))
            curX += ZoneGutter;

        double topX = curX;
        curX += topZoneWidth;

        if (HasVisibleSide(rightStates))
            curX += ZoneGutter;
        var rightThinPositions = LayoutSide(rightStates, isLeft: false, ref curX, currentLayout, topPanels, leftStates, sourcePanelId);

        // Filter out adjacent thins for solo-panel source zones
        // Only filter on the side where the source actually is
        var sourceZone = DockLayout_FindSourceZone(currentLayout, sourcePanelId);
        if (sourceZone.HasValue)
        {
            // Only filter Left thins if source is on a Left zone
            if (LeftSideZones.Contains(sourceZone.Value))
            {
                leftThinPositions = FilterAdjacentThinsForSoloPanelZone(
                    leftThinPositions, currentLayout, sourceZone.Value, sourcePanelId, LeftSideZones);
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[docking-trace] Filter returned {leftThinPositions.Count} thins for Left side");
                var leftThinsDesc = string.Join(", ", leftThinPositions.Select(t => $"{t.TargetZone}@{t.TargetOrder}({t.X:F0})"));
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[docking-trace] Thins after filter (Left): {leftThinsDesc}");
                
                // Also filter RIGHT thins (cross-side thins) that target this solo-panel LEFT zone
                // Only if there are multiple occupied zones on the LEFT side (so filtering doesn't break N+1)
                var panelsInSourceZone = PanelsInZone(currentLayout, sourceZone.Value);
                if (panelsInSourceZone.Count == 1 && LeftSideZones.Count(z => PanelsInZone(currentLayout, z).Count > 0) > 1)
                {
                    rightThinPositions = FilterCrossSideThinsThatTargetSoloPanelZone(
                        rightThinPositions, sourceZone.Value, currentLayout);
                }
            }
            else
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[docking-trace] Skipping filter for Left side (source not on Left)");
            }
            
            // Only filter Right thins if source is on a Right zone
            if (RightSideZones.Contains(sourceZone.Value))
            {
                rightThinPositions = FilterAdjacentThinsForSoloPanelZone(
                    rightThinPositions, currentLayout, sourceZone.Value, sourcePanelId, RightSideZones);
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[docking-trace] Filter returned {rightThinPositions.Count} thins for Right side");
                var rightThinsDesc = string.Join(", ", rightThinPositions.Select(t => $"{t.TargetZone}@{t.TargetOrder}({t.X:F0})"));
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[docking-trace] Thins after filter (Right): {rightThinsDesc}");
                
                // Also filter LEFT thins (cross-side thins) that target this solo-panel RIGHT zone
                // Only if there are multiple occupied zones on the RIGHT side (so filtering doesn't break N+1)
                var panelsInSourceZone = PanelsInZone(currentLayout, sourceZone.Value);
                if (panelsInSourceZone.Count == 1 && RightSideZones.Count(z => PanelsInZone(currentLayout, z).Count > 0) > 1)
                {
                    leftThinPositions = FilterCrossSideThinsThatTargetSoloPanelZone(
                        leftThinPositions, sourceZone.Value, currentLayout);
                }
            }
            else
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[docking-trace] Skipping filter for Right side (source not on Right)");
            }
        }

        double innerWidth = curX;
        double popupWidth = innerWidth + PopupPadding * 2;

        AddSyntheticThinSlots(allSlots, sourcePanelId, leftThinPositions, innerHeight);
        AddSyntheticThinSlots(allSlots, sourcePanelId, rightThinPositions, innerHeight);

        foreach (var state in SideVisualOrder(leftStates, isLeft: true).Where(s => !s.Suppressed))
        {
            BuildColumnSlots(
                allSlots, sourcePanelId, state.Panels, state.SourceInZone,
                state.X, LabelRowHeight, state.Width, ColSlotHeight, innerHeight, state.Zone);
        }

        BuildRowSlots(
            allSlots,
            sourcePanelId,
            topPanels,
            sourceInTop,
            topX, LabelRowHeight,
            TopSlotWidth, TopSlotHeight,
            topZoneWidth,
            DockZone.Top);

        foreach (var state in SideVisualOrder(rightStates, isLeft: false).Where(s => !s.Suppressed))
        {
            BuildColumnSlots(
                allSlots, sourcePanelId, state.Panels, state.SourceInZone,
                state.X, LabelRowHeight, state.Width, ColSlotHeight, innerHeight, state.Zone);
        }

        const double SeparatorWidth = 4.0;
        if (topX > 0)
        {
            double sepX = topX - ZoneGutter / 2.0 - SeparatorWidth / 2.0;
            allSlots.Add(new SlotButtonViewModel(
                Label: string.Empty, IsSourcePanel: false, IsExpansionButton: false,
                X: sepX, Y: LabelRowHeight,
                Width: SeparatorWidth, Height: innerHeight,
                TargetZone: DockZone.Top, TargetOrder: -1,
                SourcePanelId: sourcePanelId)
            { IsSeparator = true });
        }

        double topZoneRightEdge = topX + topZoneWidth;
        if (topZoneRightEdge < innerWidth)
        {
            double sepX = topZoneRightEdge + ZoneGutter / 2.0 - SeparatorWidth / 2.0;
            allSlots.Add(new SlotButtonViewModel(
                Label: string.Empty, IsSourcePanel: false, IsExpansionButton: false,
                X: sepX, Y: LabelRowHeight,
                Width: SeparatorWidth, Height: innerHeight,
                TargetZone: DockZone.Top, TargetOrder: -2,
                SourcePanelId: sourcePanelId)
            { IsSeparator = true });
        }

        var srcSlot = allSlots.FirstOrDefault(s => s.IsSourcePanel);
        double srcCenterX = srcSlot is not null
            ? srcSlot.X + srcSlot.Width / 2 + PopupPadding
            : popupWidth / 2;
        double srcCenterY = srcSlot is not null
            ? srcSlot.Y + srcSlot.Height / 2 + PopupPadding
            : popupHeight / 2;

        var leftZoneSlots = allSlots.Where(s => !s.IsSeparator && IsLeftSideZone(s.TargetZone)).ToList();
        bool hasLeftSection = leftZoneSlots.Count > 0;
        double leftSectionCenterX = hasLeftSection
            ? (leftZoneSlots.Min(s => s.X) + leftZoneSlots.Max(s => s.X + s.Width)) / 2
            : 0;

        var topZoneSlots = allSlots.Where(s => !s.IsSeparator && s.TargetZone == DockZone.Top).ToList();
        double topSectionCenterX = topZoneSlots.Count > 0
            ? (topZoneSlots.Min(s => s.X) + topZoneSlots.Max(s => s.X + s.Width)) / 2
            : innerWidth / 2;

        var rightZoneSlots = allSlots.Where(s => !s.IsSeparator && IsRightSideZone(s.TargetZone)).ToList();
        bool hasRightSection = rightZoneSlots.Count > 0;
        double rightSectionCenterX = hasRightSection
            ? (rightZoneSlots.Min(s => s.X) + rightZoneSlots.Max(s => s.X + s.Width)) / 2
            : 0;

        // Count final thin slots before returning
        int finalLeftThins = allSlots.Count(s => s.Width < ColSlotWidth && IsLeftSideZone(s.TargetZone) && !s.IsSeparator);
        int finalRightThins = allSlots.Count(s => s.Width < ColSlotWidth && IsRightSideZone(s.TargetZone) && !s.IsSeparator);
        
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[docking-trace] Final thin count for Left: {finalLeftThins} thins");
        var finalLeftDesc = string.Join(", ", allSlots
            .Where(s => s.Width < ColSlotWidth && IsLeftSideZone(s.TargetZone) && !s.IsSeparator)
            .Select(s => $"{s.TargetZone}@{s.TargetOrder}({s.X:F0})"));
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[docking-trace] Final Left thins: {finalLeftDesc}");
        
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[docking-trace] Final thin count for Right: {finalRightThins} thins");
        var finalRightDesc = string.Join(", ", allSlots
            .Where(s => s.Width < ColSlotWidth && IsRightSideZone(s.TargetZone) && !s.IsSeparator)
            .Select(s => $"{s.TargetZone}@{s.TargetOrder}({s.X:F0})"));
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[docking-trace] Final Right thins: {finalRightDesc}");

        TraceMap(sourcePanelId, allSlots, popupWidth, popupHeight, leftThinPositions, rightThinPositions);

        return new DockingMapViewModel(
            allSlots,
            popupWidth,
            popupHeight,
            srcCenterX,
            srcCenterY,
            leftSectionCenterX,
            topSectionCenterX,
            rightSectionCenterX,
            hasLeftSection,
            hasRightSection);
    }

    private static List<SideZoneState> BuildSideStates(
        DockLayout layout,
        string sourcePanelId,
        IReadOnlySet<string>? visiblePanelIds,
        IReadOnlyList<DockZone> sideZones) =>
        sideZones
            .Select((zone, index) =>
            {
                var panels = FilterZone(PanelsInZone(layout, zone), sourcePanelId, visiblePanelIds);
                return new SideZoneState
                {
                    Zone = zone,
                    Tier = index,
                    Panels = panels,
                    SourceInZone = panels.Any(p => Same(p, sourcePanelId)),
                };
            })
            .ToList();

    private static void PrepareSideStates(List<SideZoneState> states)
    {
        ApplySideSuppression(states);

        foreach (var state in states)
        {
            state.Occupied = !state.Suppressed && state.Panels.Count > 0;
            state.Width = state.Suppressed
                ? 0
                : state.EmptyWithoutSource ? ColSlotWidthEmpty : ColSlotWidth;
            state.ContentHeight = state.Suppressed
                ? 0
                : ZoneColumnHeight(state.Panels.Count, state.SourceInZone, ColSlotHeight);
        }
    }

    private static void ApplySideSuppression(List<SideZoneState> states)
    {
        if (states.Count == 0)
            return;

        if (states.Count > 1)
        {
            states[0].Suppressed =
                states[1].SourceInZone &&
                states[1].Panels.Count == 1 &&
                states[0].Panels.Count == 0;

            states[1].Suppressed =
                (states[1].EmptyWithoutSource &&
                 states[0].EmptyWithoutSource &&
                 states.Skip(2).All(s => s.EmptyWithoutSource)) ||
                (states[0].SourceInZone &&
                 states[0].Panels.Count == 1 &&
                 states[1].Panels.Count == 0);
        }

        for (int i = 2; i < states.Count; i++)
        {
            states[i].Suppressed =
                (states[i].EmptyWithoutSource &&
                 (states[i - 1].Suppressed || states[i - 1].EmptyWithoutSource)) ||
                (states[i - 1].SourceInZone &&
                 states[i - 1].Panels.Count == 1 &&
                 states[i].Panels.Count == 0);
        }
    }

    private static double MaxContentHeight(IEnumerable<SideZoneState> states) =>
        states.Select(s => s.ContentHeight).DefaultIfEmpty(0).Max();

    private static bool HasVisibleSide(IEnumerable<SideZoneState> states) =>
        states.Any(s => !s.Suppressed);

    private static List<SyntheticThin> LayoutSide(
        List<SideZoneState> states,
        bool isLeft,
        ref double curX,
        DockLayout currentLayout,
        List<string> topPanels,
        List<SideZoneState> otherSideStates,
        string sourcePanelId)
    {
        var sideName = isLeft ? "Left" : "Right";
        var visible = SideVisualOrder(states, isLeft).Where(s => !s.Suppressed).ToList();
        
        // Log all zones before filtering
        var allZonesDesc = string.Join(", ", states.Select((s, i) => 
            $"{s.Zone}(Tier={s.Tier},Occ={s.Occupied},Supp={s.Suppressed},Panels={s.Panels.Count})"));
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[build-side-seq] Available zones on {sideName}: [{allZonesDesc}]");
        
        var visibleZonesDesc = string.Join(", ", visible.Select((s, i) => 
            $"{s.Zone}(Tier={s.Tier},Occ={s.Occupied})"));
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[build-side-seq] Visible (non-suppressed) zones on {sideName}: [{visibleZonesDesc}] (count={visible.Count})");
        
        var thins = new List<SyntheticThin>();
        if (visible.Count == 0)
            return thins;

        var sequence = BuildSideSequence(states, visible, isLeft);
        for (int i = 0; i < sequence.Count; i++)
        {
            if (i > 0)
                curX += InnerZoneGap;

            var item = sequence[i];
            if (item.IsSynthetic)
            {
                thins.Add(new SyntheticThin(curX, item.TargetZone, item.TargetOrder, item.InsertKind));
                curX += item.Width;
                continue;
            }

            item.Zone!.X = curX;
            curX += item.Width;
        }

        SquadDashTrace.Write(TraceCategory.Docking,
            $"[docking-trace] LayoutSide {sideName}: Generated {thins.Count} synthetic thins from layout sequence");
        var thinsDesc = string.Join(", ", thins.Select(t => $"{t.TargetZone}@{t.TargetOrder}({t.X:F0})"));
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[docking-trace] LayoutSide {sideName}: Generated thins: {thinsDesc}");

        // Generate cross-side thins only if this side has no occupied zones (except possibly within-side thins)
        var occupiedOnThisSide = states.Where(s => s.Occupied).ToList();
        if (occupiedOnThisSide.Count == 0)
        {
            GenerateCrossSideThins(thins, isLeft, topPanels, otherSideStates, currentLayout, sourcePanelId);
        }

        return thins;
    }

    /// <summary>
    /// Generates cross-side thin slots that allow moving panels to other occupied sides.
    /// For example, if processing the Right side and the Top zone has panels, adds a thin for Top.
    /// Similarly, if the Left side has occupied zones, adds thins for those zones.
    /// </summary>
    private static void GenerateCrossSideThins(
        List<SyntheticThin> thins,
        bool isLeft,
        List<string> topPanels,
        List<SideZoneState> otherSideStates,
        DockLayout currentLayout,
        string sourcePanelId)
    {
        var sideName = isLeft ? "Left" : "Right";
        var otherSideName = isLeft ? "Right" : "Left";

        // Check if Top zone has panels
        if (topPanels.Count > 0)
        {
            // Don't add duplicate cross-side thins if they already exist
            if (!thins.Any(t => t.TargetZone == DockZone.Top))
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[cross-side-thin] {sideName} side: Top zone is occupied, adding cross-side thin to Top@0");
                
                // Add a thin at the beginning for the Top zone
                // Using a position of 0.0 as placeholder - the actual X position is recalculated in AddSyntheticThinSlots
                thins.Add(new SyntheticThin(0.0, DockZone.Top, 0, SyntheticInsertKind.InsertBefore));
            }
        }

        // Check for occupied zones on the other side
        var occupiedOtherZones = otherSideStates.Where(s => s.Occupied && !s.Suppressed).ToList();
        if (occupiedOtherZones.Count > 0)
        {
            var occupiedZonesDesc = string.Join(", ", occupiedOtherZones.Select(z => z.Zone));
            SquadDashTrace.Write(TraceCategory.Docking,
                $"[cross-side-thin] {sideName} side: {otherSideName} side has occupied zones: [{occupiedZonesDesc}]");

            // Add thins for each occupied zone on the other side
            foreach (var otherZone in occupiedOtherZones)
            {
                // Don't add duplicate cross-side thins if they already exist
                if (!thins.Any(t => t.TargetZone == otherZone.Zone))
                {
                    SquadDashTrace.Write(TraceCategory.Docking,
                        $"[cross-side-thin]   Adding cross-side thin to {otherZone.Zone}@0");
                    
                    thins.Add(new SyntheticThin(0.0, otherZone.Zone, 0, SyntheticInsertKind.InsertBefore));
                }
            }
        }
    }

    private static List<SideSequenceItem> BuildSideSequence(
        IReadOnlyList<SideZoneState> states,
        IReadOnlyList<SideZoneState> visible,
        bool isLeft)
    {
        var sequence = new List<SideSequenceItem>();
        var occupied = states.Where(s => s.Occupied).ToList();
        var sideName = isLeft ? "Left" : "Right";
        
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[build-side-seq] === Starting BuildSideSequence for {sideName} side ===");
        
        var occupiedZonesList = string.Join(", ", occupied.Select(s => $"{s.Zone}(Tier={s.Tier},Panels={s.Panels.Count})"));
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[build-side-seq] Occupied zones on {sideName}: [{occupiedZonesList}] (count={occupied.Count})");
        
        if (occupied.Count == 0)
        {
            sequence.AddRange(visible.Select(SideSequenceItem.ForZone));
            SquadDashTrace.Write(TraceCategory.Docking,
                $"[build-side-seq] No occupied zones - adding all {visible.Count} visible zones as regular items");
            SquadDashTrace.Write(TraceCategory.Docking,
                $"[build-side-seq] === BuildSideSequence complete for {sideName}: {sequence.Count} items ===");
            return sequence;
        }

        int minOccupiedTier = occupied.Min(s => s.Tier);
        int maxOccupiedTier = occupied.Max(s => s.Tier);
        var innermostOccupied = states[minOccupiedTier];
        var outermostOccupied = states[maxOccupiedTier];
        
        // Detailed tier range logging
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[build-side-seq] Tier range calculation:");
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[build-side-seq]   First occupied tier: {minOccupiedTier} ({innermostOccupied.Zone})");
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[build-side-seq]   Last occupied tier: {maxOccupiedTier} ({outermostOccupied.Zone})");
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[build-side-seq]   Range for iteration: [tiers {minOccupiedTier} to {maxOccupiedTier}]");
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[build-side-seq]   Total zones on {sideName}: {states.Count}, zones in visible/iterable: {visible.Count}");
        
        bool sourceIsOnlySidePanel =
            occupied.Count == 1 &&
            occupied[0].SourceInZone &&
            occupied[0].Panels.Count == 1;

        bool needsInnerSynthetic =
            !sourceIsOnlySidePanel &&
            !states.Any(s => s.Tier < minOccupiedTier && !s.Suppressed && !s.Occupied);
        bool needsOuterSynthetic =
            !sourceIsOnlySidePanel &&
            !states.Any(s => s.Tier > maxOccupiedTier && !s.Suppressed && !s.Occupied);

        SquadDashTrace.Write(TraceCategory.Docking,
            $"[build-side-seq] Synthetic injection logic:");
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[build-side-seq]   sourceIsOnlySidePanel={sourceIsOnlySidePanel}");
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[build-side-seq]   needsInnerSynthetic={needsInnerSynthetic} (no empty {(isLeft ? "inner" : "inner")} zones between min occupied and edge)");
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[build-side-seq]   needsOuterSynthetic={needsOuterSynthetic} (no empty {(isLeft ? "outer" : "outer")} zones after max occupied)");

        for (int i = 0; i < visible.Count; i++)
        {
            var state = visible[i];
            var nextState = i + 1 < visible.Count ? visible[i + 1] : null;

            SquadDashTrace.Write(TraceCategory.Docking,
                $"[build-side-seq] Zone iteration: checking zone {i} of {visible.Count}");
            SquadDashTrace.Write(TraceCategory.Docking,
                $"[build-side-seq]   Zone: {state.Zone} (Tier={state.Tier}, Occupied={state.Occupied}, Panels={state.Panels.Count})");

            if (isLeft && needsOuterSynthetic && state == outermostOccupied)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[build-side-seq]   Included in loop? YES (outermost occupied on Left side, needs outer synthetic)");
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[build-side-seq]   Adding synthetic InsertBefore {state.Zone}@0 (left outer synthetic)");
                sequence.Add(SideSequenceItem.ForSynthetic(
                    state.Zone, 0, SyntheticInsertKind.InsertBefore));
            }
            else if (!isLeft && needsInnerSynthetic && state == innermostOccupied)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[build-side-seq]   Included in loop? YES (innermost occupied on Right side, needs inner synthetic)");
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[build-side-seq]   Adding synthetic InsertBefore {state.Zone}@0 (right inner synthetic)");
                sequence.Add(SideSequenceItem.ForSynthetic(
                    state.Zone, 0, SyntheticInsertKind.InsertBefore));
            }
            else
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[build-side-seq]   Included in loop? YES");
            }

            SquadDashTrace.Write(TraceCategory.Docking,
                $"[build-side-seq]   Adding regular zone item: {state.Zone}");
            sequence.Add(SideSequenceItem.ForZone(state));

            if (nextState is not null)
            {
                int tierDiff = Math.Abs(state.Tier - nextState.Tier);
                bool bothOccupied = state.Occupied && nextState.Occupied;
                bool nextEmpty = !nextState.Occupied;
                
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[build-side-seq] Adjacency between {state.Zone}@Tier{state.Tier} and {nextState.Zone}@Tier{nextState.Tier}:");
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[build-side-seq]   tierDiff = {tierDiff}, occupied: both={bothOccupied}, next={!nextEmpty}");
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[build-side-seq]   Condition check: tierDiff==1? {tierDiff == 1}, both occupied? {bothOccupied}, next empty? {nextEmpty}");
                
                // For adjacent zones (Tier difference = 1), use the existing adjacent logic
                // For non-adjacent zones (Tier difference > 1), still generate a thin for N+1 rule compliance
                if (state.Occupied && nextState.Occupied && tierDiff == 1)
                {
                    SquadDashTrace.Write(TraceCategory.Docking,
                        $"[build-side-seq]   Final decision: INCLUDE thin (both occupied + adjacent)");
                    SquadDashTrace.Write(TraceCategory.Docking,
                        $"[build-side-seq]   Adding synthetic InsertBefore {nextState.Zone}@0");
                    sequence.Add(SideSequenceItem.ForSynthetic(
                        nextState.Zone, 0, SyntheticInsertKind.InsertBefore));
                }
                else if (tierDiff > 1)
                {
                    // Non-adjacent zones: generate synthetic thin for drop targets
                    SquadDashTrace.Write(TraceCategory.Docking,
                        $"[build-side-seq]   Final decision: INCLUDE thin (non-adjacent zones, tierDiff={tierDiff}>1)");
                    SquadDashTrace.Write(TraceCategory.Docking,
                        $"[build-side-seq]   Adding synthetic InsertBefore {nextState.Zone}@0 (non-adjacent bridge)");
                    sequence.Add(SideSequenceItem.ForSynthetic(
                        nextState.Zone, 0, SyntheticInsertKind.InsertBefore));
                }
                else
                {
                    var reason = "";
                    if (!state.Occupied && !nextState.Occupied) reason = "both empty";
                    else if (!state.Occupied) reason = "current empty";
                    else if (!nextState.Occupied) reason = "next empty";
                    else reason = "both occupied but tierDiff!=1";
                    
                    SquadDashTrace.Write(TraceCategory.Docking,
                        $"[build-side-seq]   Final decision: SKIP thin (reason: {reason})");
                }
            }

            if (isLeft && needsInnerSynthetic && state == innermostOccupied)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[build-side-seq]   Adding synthetic InsertAfter {state.Zone}@{state.Panels.Count} (left inner synthetic)");
                sequence.Add(SideSequenceItem.ForSynthetic(
                    state.Zone, state.Panels.Count, SyntheticInsertKind.InsertAfter));
            }
            else if (!isLeft && needsOuterSynthetic && state == outermostOccupied)
            {
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[build-side-seq]   Adding synthetic InsertAfter {state.Zone}@{state.Panels.Count} (right outer synthetic)");
                sequence.Add(SideSequenceItem.ForSynthetic(
                    state.Zone, state.Panels.Count, SyntheticInsertKind.InsertAfter));
            }
        }

        var synthCount = sequence.Count(s => s.IsSynthetic);
        var thinDesc = string.Join(", ", sequence.Where(s => s.IsSynthetic).Select(s => $"{s.InsertKind} {s.TargetZone}@{s.TargetOrder}"));
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[build-side-seq] === BuildSideSequence complete for {sideName}: {sequence.Count} total items ({synthCount} synthetic) ===");
        SquadDashTrace.Write(TraceCategory.Docking,
            $"[build-side-seq]   Synthetic thins: [{thinDesc}]");

        return sequence;
    }

    private static IEnumerable<SideZoneState> SideVisualOrder(
        IEnumerable<SideZoneState> states,
        bool isLeft) =>
        isLeft ? states.OrderByDescending(s => s.Tier) : states.OrderBy(s => s.Tier);

    private static void AddSyntheticThinSlots(
        List<SlotButtonViewModel> allSlots,
        string sourcePanelId,
        IEnumerable<SyntheticThin> thins,
        double innerHeight)
    {
        foreach (var thin in thins)
        {
            allSlots.Add(new SlotButtonViewModel(
                Label: "—", IsSourcePanel: false, IsExpansionButton: false,
                X: thin.X, Y: LabelRowHeight,
                Width: ColSlotWidthEmpty, Height: innerHeight,
                TargetZone: thin.TargetZone, TargetOrder: thin.TargetOrder,
                SourcePanelId: sourcePanelId)
            { InsertKind = thin.Kind });
        }
    }

    private static void TraceMap(
        string sourcePanelId,
        IReadOnlyList<SlotButtonViewModel> allSlots,
        double popupWidth,
        double popupHeight,
        IReadOnlyList<SyntheticThin> leftThinPositions,
        IReadOnlyList<SyntheticThin> rightThinPositions)
    {
        SquadDashTrace.Write(TraceCategory.Docking,
            $"BuildDockingMap src={sourcePanelId}: {allSlots.Count} slots, popup={popupWidth:F0}x{popupHeight:F0}");
        foreach (var s in allSlots)
            SquadDashTrace.Write(TraceCategory.Docking,
                $"  zone={s.TargetZone,-8} order={s.TargetOrder,3}  x={s.X,5:F0} y={s.Y,4:F0}"
                + $"  w={s.Width,4:F0} h={s.Height,3:F0}"
                + (s.IsSourcePanel ? "  [src]" : "")
                + (s.IsSeparator ? "  [sep]" : "")
                + (string.IsNullOrEmpty(s.Label) ? "" : $"  '{s.Label}'"));

        foreach (var violation in FindAdjacentThinViolations(allSlots))
            SquadDashTrace.Write(TraceCategory.Docking, $"  [thin-layout WARNING] {violation}");

        var leftSynthDesc = string.Join(", ", leftThinPositions.Select(t => $"{t.Kind} {t.TargetZone}@{t.TargetOrder}"));
        var rightSynthDesc = string.Join(", ", rightThinPositions.Select(t => $"{t.Kind} {t.TargetZone}@{t.TargetOrder}"));
        SquadDashTrace.Write(TraceCategory.Docking,
            $"  [thin-check] left synth={leftThinPositions.Count}: [{leftSynthDesc}]  right synth={rightThinPositions.Count}: [{rightSynthDesc}]");
    }

    /// <summary>
    /// Builds the full DockingMapViewModel for the given source panel and current layout.
    /// Implements Rules A, B, C, D from the spec.
    /// </summary>
    internal static DockingMapViewModel BuildDockingMap(
        string sourcePanelId,
        DockLayout currentLayout,
        IReadOnlySet<string>? visiblePanelIds = null)
    {
        return BuildDockingMapFromSideStates(sourcePanelId, currentLayout, visiblePanelIds);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans the slot list for layout violations:
    /// (1) Adjacent thin slots on the same side — two narrow drop-target columns side by side.
    /// (2) N+1 thin rule — for N occupied zones on a side, exactly N+1 thin drop-targets are required.
    ///     Applies for N >= 1 (single occupied zone still needs an outer AND an inner thin).
    /// (3) No-op outer thin — source is the sole, innermost-occupied panel on a side and a Rule-D
    ///     thin for the immediately adjacent outer zone is present.  Dropping there would leave the
    ///     panel at the same visual position (source zone collapses, adjacent zone opens in its place).
    /// Returns one description string per violation found; empty list means no violations.
    /// </summary>
    internal static IReadOnlyList<string> FindAdjacentThinViolations(IReadOnlyList<SlotButtonViewModel> slots)
    {
        var violations = new List<string>();

        void CheckSide(DockZone[] sideZones, string sideName)
        {
            var sideSlots = slots.Where(s => !s.IsSeparator && sideZones.Contains(s.TargetZone)).ToList();

            // Thin slots: width < ColSlotWidth (natural empty-zone or synthetic insert)
            var thinSlots = sideSlots.Where(s => s.Width < ColSlotWidth).OrderBy(s => s.X).ToList();

            // Occupied zones: zones that have at least one full-width slot
            int occupiedZoneCount = sideZones
                .Count(z => sideSlots.Any(s => s.Width >= ColSlotWidth && s.TargetZone == z));
            bool sourceIsOnlySidePanel =
                sideSlots.Count(s => s.Width >= ColSlotWidth) == 1 &&
                sideSlots.Any(s => s.Width >= ColSlotWidth && s.IsSourcePanel);

            // N+1 rule: N occupied zones → N+1 thin drop-targets required (N >= 1)
            if (occupiedZoneCount >= 1 && !sourceIsOnlySidePanel)
            {
                int expectedThins = occupiedZoneCount + 1;
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"[docking-trace] N+1 check {sideName}: occupiedZones={occupiedZoneCount}, expected={expectedThins}, actual={thinSlots.Count}");
                if (thinSlots.Count != expectedThins)
                {
                    SquadDashTrace.Write(TraceCategory.Docking,
                        $"[docking-trace] Thin generation blocked — extra thins were added AFTER filter. Investigate source.");
                    violations.Add(
                        $"{sideName}: N+1 rule violated — {occupiedZoneCount} occupied zone(s) require {expectedThins} thin slot(s), got {thinSlots.Count}");
                }
            }

            // Adjacent-thin check: no two thin slots should be side by side
            for (int i = 1; i < thinSlots.Count; i++)
            {
                var s1 = thinSlots[i - 1];
                var s2 = thinSlots[i];
                if (Math.Abs(s2.X - s1.X) < ColSlotWidth)
                    violations.Add(
                        $"{sideName}: adjacent thin slots — {s1.TargetZone} (x={s1.X:F0}) and {s2.TargetZone} (x={s2.X:F0})");
            }
        }

        CheckSide(LeftSideZones, "Left");
        CheckSide(RightSideZones, "Right");

        // Rule (3): No-op outer thin.
        // sideZones[0] = innermost, sideZones[N-1] = outermost.
        var srcSlot = slots.FirstOrDefault(s => !s.IsSeparator && s.IsSourcePanel);
        if (srcSlot is not null)
        {
            void CheckNoopLateral(DockZone[] sideZones, string sideName)
            {
                int srcIdx = Array.IndexOf(sideZones, srcSlot.TargetZone);
                if (srcIdx < 0) return; // source not on this side

                // Source must be sole occupant of its zone (exactly one full-width slot there)
                bool isSole = slots.Count(s => !s.IsSeparator
                    && s.TargetZone == sideZones[srcIdx] && s.Width >= ColSlotWidth) == 1;
                if (!isSole) return;

                // Source must be the innermost occupied zone on this side
                for (int i = 0; i < srcIdx; i++)
                {
                    if (slots.Any(s => !s.IsSeparator && s.TargetZone == sideZones[i] && s.Width >= ColSlotWidth))
                        return; // a more-inward zone has panels
                }

                // Adjacent outer zone must have a Rule-D thin (empty-zone placeholder, not a synthetic insert)
                if (srcIdx + 1 >= sideZones.Length) return;
                var adjacentOutward = sideZones[srcIdx + 1];
                bool hasNoopThin = slots.Any(s =>
                    !s.IsSeparator &&
                    s.TargetZone == adjacentOutward &&
                    !s.IsSyntheticInsert &&
                    s.Width < ColSlotWidth);
                if (hasNoopThin)
                    violations.Add(
                        $"{sideName}: no-op outer thin — '{srcSlot.SourcePanelId}' is sole at innermost position " +
                        $"({DockingLayoutEngine.GetZoneDisplayName(sideZones[srcIdx])}), " +
                        $"but thin for {DockingLayoutEngine.GetZoneDisplayName(adjacentOutward)} is shown; " +
                        $"dropping there is visually identical to staying in place");
            }

            CheckNoopLateral(LeftSideZones, "Left");
            CheckNoopLateral(RightSideZones, "Right");
        }

        return violations;
    }

    private static List<string> PanelsInZone(DockLayout layout, DockZone zone) =>
        layout.Slots
              .Where(s => s.Zone == zone)
              .OrderBy(s => s.Order)
              .Select(s => s.PanelId)
              .ToList();

    private static DockZone[] BuildSideZones(string prefix) =>
        Enum.GetValues<DockZone>()
            .Where(zone => ZoneNameMatchesPrefix(zone, prefix))
            .OrderBy(ZoneTier)
            .ToArray();

    private static bool ZoneNameMatchesPrefix(DockZone zone, string prefix)
    {
        string name = zone.ToString();
        if (name == prefix)
            return true;

        return name.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(name[prefix.Length..], out _);
    }

    private static int ZoneTier(DockZone zone)
    {
        string name = zone.ToString();
        int digitStart = name.TakeWhile(char.IsLetter).Count();
        return digitStart == name.Length
            ? 0
            : int.Parse(name[digitStart..], System.Globalization.CultureInfo.InvariantCulture) - 1;
    }

    private static bool IsLeftSideZone(DockZone zone) => LeftSideZones.Contains(zone);

    private static bool IsRightSideZone(DockZone zone) => RightSideZones.Contains(zone);

    private static List<string> FilterZone(
        List<string> panels,
        string sourcePanelId,
        IReadOnlySet<string>? visiblePanelIds) =>
        visiblePanelIds is null
            ? panels
            : panels.Where(p => visiblePanelIds.Contains(p) || Same(p, sourcePanelId)).ToList();

    private static bool Same(string panelId, string sourcePanelId) =>
        string.Equals(panelId, sourcePanelId, StringComparison.OrdinalIgnoreCase);

    private static string LabelFor(string panelId) =>
        PanelLabels.TryGetValue(panelId, out var lbl) ? lbl : panelId[..Math.Min(6, panelId.Length)];

    /// <summary>
    /// Finds which zone contains the source panel, if any.
    /// </summary>
    private static DockZone? DockLayout_FindSourceZone(DockLayout layout, string sourcePanelId)
    {
        var slot = layout.Slots.FirstOrDefault(s => Same(s.PanelId, sourcePanelId));
        return slot?.Zone;
    }

    /// <summary>
    /// Filters out synthetic thin slots that are in the same zone as a solo-panel source,
    /// or immediately adjacent to it, but only if the N+1 rule is still satisfied after filtering.
    /// 
    /// When a panel is the only occupant of its zone:
    /// - Thins for that same zone are no-ops (inserting another panel in the same zone doesn't change docking)
    /// - Thins for immediately adjacent zones are also typically no-ops (visually identical layout)
    /// 
    /// We filter these no-ops, but only if we can still maintain N+1 drop targets after filtering.
    /// </summary>
    private static List<SyntheticThin> FilterAdjacentThinsForSoloPanelZone(
        IReadOnlyList<SyntheticThin> thins,
        DockLayout layout,
        DockZone sourceZone,
        string sourcePanelId,
        IReadOnlyList<DockZone> sideZones)
    {
        var sideName = sideZones == LeftSideZones ? "Left" : "Right";
        var srcZoneName = DockingLayoutEngine.GetZoneDisplayName(sourceZone);
        
        int sourceZoneIdx = -1;
        for (int i = 0; i < sideZones.Count; i++)
        {
            if (sideZones[i] == sourceZone)
            {
                sourceZoneIdx = i;
                break;
            }
        }

        if (sourceZoneIdx < 0)
        {
            SquadDashTrace.Write(TraceCategory.Docking,
                $"  [adjacent-thin-filter] {sideName}: sourceZone {srcZoneName} not in side zones, returning unfiltered ({thins.Count} thins)");
            return new List<SyntheticThin>(thins);
        }

        // Check if source is the sole occupant of its zone
        var panelsInSourceZone = PanelsInZone(layout, sourceZone);
        bool isSoloPanelZone = panelsInSourceZone.Count == 1 && Same(panelsInSourceZone[0], sourcePanelId);

        if (!isSoloPanelZone)
        {
            SquadDashTrace.Write(TraceCategory.Docking,
                $"  [adjacent-thin-filter] {sideName}: sourceZone {srcZoneName} is not solo-panel (has {panelsInSourceZone.Count} panels), returning unfiltered ({thins.Count} thins)");
            return new List<SyntheticThin>(thins);
        }

        // Count occupied zones on this side to check N+1 rule
        int occupiedZoneCount = sideZones.Count(z => PanelsInZone(layout, z).Count > 0);
        int expectedThins = occupiedZoneCount + 1;

        SquadDashTrace.Write(TraceCategory.Docking,
            $"  [adjacent-thin-filter] {sideName}: solo-panel source zone {srcZoneName} detected. occupiedZones={occupiedZoneCount}, expectedThins={expectedThins}, actualThins={thins.Count}");

        // Check if source is the only panel on the side
        if (occupiedZoneCount == 1)
        {
            SquadDashTrace.Write(TraceCategory.Docking,
                $"  [adjacent-thin-filter] {sideName}: only one occupied zone on this side, no adjacent zones to filter");
            return new List<SyntheticThin>(thins);
        }

        // Source is alone in its zone and there are other occupied zones.
        // Check N+1 rule FIRST: we need at least expectedThins drop targets available.
        // If we can't meet N+1 after filtering, don't attempt to filter at all.
        
        // Identify the zones to filter: source zone itself, and adjacent zones.
        DockZone? leftAdjacentZone = sourceZoneIdx > 0 ? sideZones[sourceZoneIdx - 1] : null;
        DockZone? rightAdjacentZone = sourceZoneIdx < sideZones.Count - 1 ? sideZones[sourceZoneIdx + 1] : null;

        // Count how many thins would survive filtering (before actually filtering)
        int thinsAfterFilter = thins.Count(thin => 
            thin.TargetZone != sourceZone && 
            thin.TargetZone != leftAdjacentZone && 
            thin.TargetZone != rightAdjacentZone);

        // Check N+1 rule BEFORE filtering: do we have enough thins to work with?
        if (thinsAfterFilter < expectedThins)
        {
            SquadDashTrace.Write(TraceCategory.Docking,
                $"  [adjacent-thin-filter] {sideName}: N+1 rule check BEFORE filter: {thinsAfterFilter} would remain, but need {expectedThins}. Cannot safely filter.");
            SquadDashTrace.Write(TraceCategory.Docking,
                $"  [adjacent-thin-filter] {sideName}: Keeping all {thins.Count} thins (all are necessary for N+1 compliance)");
            return new List<SyntheticThin>(thins);
        }

        // N+1 rule ALLOWS filtering. Now apply the filter to remove no-ops.
        var filtered = thins
            .Where(thin => thin.TargetZone != sourceZone && 
                          thin.TargetZone != leftAdjacentZone && 
                          thin.TargetZone != rightAdjacentZone)
            .ToList();

        if (filtered.Count < thins.Count)
        {
            var removed = thins.Where(t => !filtered.Contains(t)).ToList();
            var zonesToFilter = new List<string> { srcZoneName };
            if (leftAdjacentZone.HasValue)
                zonesToFilter.Add($"{DockingLayoutEngine.GetZoneDisplayName(leftAdjacentZone.Value)} (left)");
            if (rightAdjacentZone.HasValue)
                zonesToFilter.Add($"{DockingLayoutEngine.GetZoneDisplayName(rightAdjacentZone.Value)} (right)");

            SquadDashTrace.Write(TraceCategory.Docking,
                $"  [adjacent-thin-filter] {sideName}: N+1 rule satisfied after filter. Removing {removed.Count} no-op thin(s) for solo-panel zone {srcZoneName} " +
                $"(zones: {string.Join(", ", zonesToFilter)})");
            foreach (var thin in removed)
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"    Removed: {thin.Kind} {DockingLayoutEngine.GetZoneDisplayName(thin.TargetZone)}@{thin.TargetOrder}");
        }
        else
        {
            SquadDashTrace.Write(TraceCategory.Docking,
                $"  [adjacent-thin-filter] {sideName}: No thins matched filter zones");
        }

        return filtered;
    }

    /// <summary>
    /// Filters out cross-side thin slots that target a solo-panel source zone.
    /// Should only be called when there are multiple occupied zones on the source side,
    /// so that filtering doesn't violate N+1 rule.
    /// </summary>
    private static List<SyntheticThin> FilterCrossSideThinsThatTargetSoloPanelZone(
        IReadOnlyList<SyntheticThin> thins,
        DockZone soloPanelZone,
        DockLayout currentLayout)
    {
        var filtered = thins.Where(thin => thin.TargetZone != soloPanelZone).ToList();
        
        if (filtered.Count < thins.Count)
        {
            var removed = thins.Where(t => !filtered.Contains(t)).ToList();
            SquadDashTrace.Write(TraceCategory.Docking,
                $"[cross-side-filter] Removing {removed.Count} cross-side thin(s) targeting solo-panel zone {DockingLayoutEngine.GetZoneDisplayName(soloPanelZone)} " +
                $"(multiple occupied zones on that side)");
            foreach (var thin in removed)
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"  Removed cross-side thin: {DockingLayoutEngine.GetZoneDisplayName(thin.TargetZone)}@{thin.TargetOrder}");
        }
        
        return filtered;
    }

    // Total height needed for a column zone (Rule B has N slots, Rule C has N+1, Rule D has 1)
    private static double ZoneColumnHeight(int panelCount, bool sourceInZone, double slotHeight)
    {
        if (panelCount == 0) return slotHeight; // Rule D: 1 full-height slot
        int buttonCount = sourceInZone ? panelCount : panelCount + 1; // Rule B vs C
        return buttonCount * slotHeight + Math.Max(0, buttonCount - 1) * SlotGap;
    }

    // Total width needed for a row zone
    private static double ZoneRowWidth(int panelCount, bool sourceInZone, double slotWidth)
    {
        if (panelCount == 0) return slotWidth; // Rule D
        int buttonCount = sourceInZone ? panelCount : panelCount + 1;
        return buttonCount * slotWidth + Math.Max(0, buttonCount - 1) * SlotGap;
    }

    /// <summary>
    /// Adds slot buttons for a column (Left or Right) zone.
    /// </summary>
    private static void BuildColumnSlots(
        List<SlotButtonViewModel> result,
        string sourcePanelId,
        List<string> zonePanels,
        bool sourceInZone,
        double zoneX, double zoneY,
        double slotW, double slotH,
        double zoneAvailableHeight,
        DockZone zone)
    {
        if (zonePanels.Count == 0 && !sourceInZone)
        {
            // Rule D: one full-height "dock here" slot
            result.Add(new SlotButtonViewModel(
                Label:           "—",
                IsSourcePanel:   false,
                IsExpansionButton: false,
                X:               zoneX,
                Y:               zoneY,
                Width:           slotW,
                Height:          zoneAvailableHeight,
                TargetZone:      zone,
                TargetOrder:     0,
                SourcePanelId:   sourcePanelId));
            return;
        }

        // Rule B (sourceInZone) or Rule C (!sourceInZone)
        int buttonCount = sourceInZone ? zonePanels.Count : zonePanels.Count + 1;

        // Stretch slots to fill the full available height (equal share each)
        double effectiveSlotH = buttonCount > 0
            ? (zoneAvailableHeight - Math.Max(0, buttonCount - 1) * SlotGap) / buttonCount
            : slotH;
        effectiveSlotH = Math.Max(effectiveSlotH, 16.0); // minimum slot height even when many panels stack

        double curY = zoneY;

        for (int i = 0; i < zonePanels.Count; i++)
        {
            string pid    = zonePanels[i];
            bool isSrc    = Same(pid, sourcePanelId);
            result.Add(new SlotButtonViewModel(
                Label:           LabelFor(pid),
                IsSourcePanel:   isSrc,
                IsExpansionButton: false,
                X:               zoneX,
                Y:               curY,
                Width:           slotW,
                Height:          effectiveSlotH,
                TargetZone:      zone,
                TargetOrder:     i,
                SourcePanelId:   sourcePanelId));
            curY += effectiveSlotH + SlotGap;
        }

        if (!sourceInZone)
        {
            // Append-at-end insertion slot (Rule C)
            result.Add(new SlotButtonViewModel(
                Label:           "+",
                IsSourcePanel:   false,
                IsExpansionButton: false,
                X:               zoneX,
                Y:               curY,
                Width:           slotW,
                Height:          effectiveSlotH,
                TargetZone:      zone,
                TargetOrder:     zonePanels.Count,
                SourcePanelId:   sourcePanelId));
        }
    }

    /// <summary>
    /// Adds slot buttons for the Top zone row.
    /// </summary>
    private static void BuildRowSlots(
        List<SlotButtonViewModel> result,
        string sourcePanelId,
        List<string> zonePanels,
        bool sourceInZone,
        double zoneX, double zoneY,
        double slotW, double slotH,
        double zoneAvailableWidth,
        DockZone zone)
    {
        if (zonePanels.Count == 0 && !sourceInZone)
        {
            // Rule D: one full-width "dock here" slot
            result.Add(new SlotButtonViewModel(
                Label:           "—",
                IsSourcePanel:   false,
                IsExpansionButton: false,
                X:               zoneX,
                Y:               zoneY,
                Width:           zoneAvailableWidth,
                Height:          slotH,
                TargetZone:      zone,
                TargetOrder:     0,
                SourcePanelId:   sourcePanelId));
            return;
        }

        int buttonCount = sourceInZone ? zonePanels.Count : zonePanels.Count + 1;
        double totalW   = buttonCount * slotW + Math.Max(0, buttonCount - 1) * SlotGap;

        // Center the row horizontally
        double startX = zoneX + Math.Max(0, (zoneAvailableWidth - totalW) / 2);
        double curX   = startX;

        for (int i = 0; i < zonePanels.Count; i++)
        {
            string pid = zonePanels[i];
            bool isSrc = Same(pid, sourcePanelId);
            result.Add(new SlotButtonViewModel(
                Label:           LabelFor(pid),
                IsSourcePanel:   isSrc,
                IsExpansionButton: false,
                X:               curX,
                Y:               zoneY,
                Width:           slotW,
                Height:          slotH,
                TargetZone:      zone,
                TargetOrder:     i,
                SourcePanelId:   sourcePanelId));
            curX += slotW + SlotGap;
        }

        if (!sourceInZone)
        {
            // Append-at-end insertion slot (Rule C)
            result.Add(new SlotButtonViewModel(
                Label:           "+",
                IsSourcePanel:   false,
                IsExpansionButton: false,
                X:               curX,
                Y:               zoneY,
                Width:           slotW,
                Height:          slotH,
                TargetZone:      zone,
                TargetOrder:     zonePanels.Count,
                SourcePanelId:   sourcePanelId));
        }
    }
}
