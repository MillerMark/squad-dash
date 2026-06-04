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

    /// <summary>
    /// Checks if a zone has exactly one panel and is not suppressed.
    /// Solo-panel zones should not generate thin slots on both sides, as moving the sole
    /// panel to an immediately adjacent zone would be a no-op.
    /// </summary>
    private static bool IsSoloPanelZone(int panelCount, bool isOccupied, bool isSuppressed)
    {
        return !isSuppressed && isOccupied && panelCount == 1;
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
        var allSlots = new List<SlotButtonViewModel>();

        // Partition panels by zone, ordered by their Order field.
        // Filter to only visible panels so the map reflects what the user actually sees.
        // The source panel is always included (user clicked its grip, so it is visible).
        var topPanels    = FilterZone(PanelsInZone(currentLayout, DockZone.Top),    sourcePanelId, visiblePanelIds);
        var leftPanels   = FilterZone(PanelsInZone(currentLayout, DockZone.Left),   sourcePanelId, visiblePanelIds);
        var rightPanels  = FilterZone(PanelsInZone(currentLayout, DockZone.Right),  sourcePanelId, visiblePanelIds);
        var left2Panels  = FilterZone(PanelsInZone(currentLayout, DockZone.Left2),  sourcePanelId, visiblePanelIds);
        var right2Panels = FilterZone(PanelsInZone(currentLayout, DockZone.Right2), sourcePanelId, visiblePanelIds);
        var left3Panels  = FilterZone(PanelsInZone(currentLayout, DockZone.Left3),  sourcePanelId, visiblePanelIds);
        var right3Panels = FilterZone(PanelsInZone(currentLayout, DockZone.Right3), sourcePanelId, visiblePanelIds);
        var left4Panels  = FilterZone(PanelsInZone(currentLayout, DockZone.Left4),  sourcePanelId, visiblePanelIds);
        var right4Panels = FilterZone(PanelsInZone(currentLayout, DockZone.Right4), sourcePanelId, visiblePanelIds);
        var left5Panels  = FilterZone(PanelsInZone(currentLayout, DockZone.Left5),  sourcePanelId, visiblePanelIds);
        var right5Panels = FilterZone(PanelsInZone(currentLayout, DockZone.Right5), sourcePanelId, visiblePanelIds);
        var left6Panels  = FilterZone(PanelsInZone(currentLayout, DockZone.Left6),  sourcePanelId, visiblePanelIds);
        var right6Panels = FilterZone(PanelsInZone(currentLayout, DockZone.Right6), sourcePanelId, visiblePanelIds);

        bool sourceInTop    = topPanels.Any(p => Same(p, sourcePanelId));
        bool sourceInLeft   = leftPanels.Any(p => Same(p, sourcePanelId));
        bool sourceInRight  = rightPanels.Any(p => Same(p, sourcePanelId));
        bool sourceInLeft2  = left2Panels.Any(p => Same(p, sourcePanelId));
        bool sourceInRight2 = right2Panels.Any(p => Same(p, sourcePanelId));
        bool sourceInLeft3  = left3Panels.Any(p => Same(p, sourcePanelId));
        bool sourceInRight3 = right3Panels.Any(p => Same(p, sourcePanelId));
        bool sourceInLeft4  = left4Panels.Any(p => Same(p, sourcePanelId));
        bool sourceInRight4 = right4Panels.Any(p => Same(p, sourcePanelId));
        bool sourceInLeft5  = left5Panels.Any(p => Same(p, sourcePanelId));
        bool sourceInRight5 = right5Panels.Any(p => Same(p, sourcePanelId));
        bool sourceInLeft6  = left6Panels.Any(p => Same(p, sourcePanelId));
        bool sourceInRight6 = right6Panels.Any(p => Same(p, sourcePanelId));

        // ── Suppress sibling zone when it would be a no-op target ─────────────
        // Case 1: both outer and inner zones are empty — suppress the outer zone
        //         so the user sees a single "dock here" slot per side.
        // Case 2: source is the SOLE occupant of its zone AND the sibling zone is
        //         empty — moving there is visually identical (source zone collapses,
        //         sibling expands with the same single panel). Suppress that target.
        // Bug 1 fix: don't suppress Left2/Right2 when the outer tier (Left3/Right3/Left4/Right4) has panels.
        // Without this, Left3-occupied → Left2 never shown (only 1 thin instead of 2).
        bool suppressLeft2  = (left2Panels.Count == 0 && !sourceInLeft2 && leftPanels.Count  == 0 && !sourceInLeft
                               && left3Panels.Count == 0 && !sourceInLeft3
                               && left4Panels.Count == 0 && !sourceInLeft4
                               && left5Panels.Count == 0 && !sourceInLeft5
                               && left6Panels.Count == 0 && !sourceInLeft6)
                           || (sourceInLeft  && leftPanels.Count  == 1 && left2Panels.Count  == 0);
        bool suppressRight2 = (right2Panels.Count == 0 && !sourceInRight2 && rightPanels.Count == 0 && !sourceInRight
                               && right3Panels.Count == 0 && !sourceInRight3
                               && right4Panels.Count == 0 && !sourceInRight4
                               && right5Panels.Count == 0 && !sourceInRight5
                               && right6Panels.Count == 0 && !sourceInRight6)
                           || (sourceInRight && rightPanels.Count == 1 && right2Panels.Count == 0);
        // Symmetric: source sole in outer zone, inner zone is empty.
        bool suppressLeft   = sourceInLeft2  && left2Panels.Count  == 1 && leftPanels.Count  == 0;
        bool suppressRight  = sourceInRight2 && right2Panels.Count == 1 && rightPanels.Count == 0;

        // For the outermost tier, tie suppression to whether the middle tier is suppressed.
        // Bug 2 fix: when the innermost zone (Left/Right) is the sole occupied zone and
        // Left2/Right2 is already thin, suppress Left3/Right3 — one outer thin is enough.
        // (Two adjacent outer-edge thins with nothing beyond them violates the "no two adjacent
        // narrow columns" rule.)
        bool suppressLeft3  = (left3Panels.Count == 0 && !sourceInLeft3 && suppressLeft2)
                           || (left3Panels.Count == 0 && !sourceInLeft3
                               && left2Panels.Count == 0 && !sourceInLeft2
                               && leftPanels.Count > 0);
        bool suppressRight3 = (right3Panels.Count == 0 && !sourceInRight3 && suppressRight2)
                           || (right3Panels.Count == 0 && !sourceInRight3
                               && right2Panels.Count == 0 && !sourceInRight2
                               && rightPanels.Count > 0);

        // Suppress Left4/Right4 when empty AND Left3/Right3 is also empty (suppress the redundant
        // further-out thin slot; only the nearest empty zone shows as a drop-target).
        bool suppressLeft4  = left4Panels.Count == 0 && !sourceInLeft4
                           && (suppressLeft3 || (left3Panels.Count == 0 && !sourceInLeft3));
        bool suppressRight4 = right4Panels.Count == 0 && !sourceInRight4
                           && (suppressRight3 || (right3Panels.Count == 0 && !sourceInRight3));

        bool suppressLeft5  = left5Panels.Count == 0 && !sourceInLeft5
                           && (suppressLeft4 || (left4Panels.Count == 0 && !sourceInLeft4));
        bool suppressRight5 = right5Panels.Count == 0 && !sourceInRight5
                           && (suppressRight4 || (right4Panels.Count == 0 && !sourceInRight4));

        bool suppressLeft6  = left6Panels.Count == 0 && !sourceInLeft6
                           && (suppressLeft5 || (left5Panels.Count == 0 && !sourceInLeft5));
        bool suppressRight6 = right6Panels.Count == 0 && !sourceInRight6
                           && (suppressRight5 || (right5Panels.Count == 0 && !sourceInRight5));

        // ── Algorithmic N+1 thin drop-target generation ──────────────────────
        // General rule: For N occupied adjacent zones on a side, insert N+1 thin drop-targets.
        // Occupied flags (computed here for synthetic-width calculation; also reused in layout walk).
        bool left6Occupied  = !suppressLeft6 && left6Panels.Count > 0;
        bool left5Occupied  = !suppressLeft5 && left5Panels.Count > 0;
        bool left4Occupied  = !suppressLeft4 && left4Panels.Count > 0;
        bool left3Occupied = !suppressLeft3 && left3Panels.Count > 0;
        bool left2Occupied = !suppressLeft2 && left2Panels.Count > 0;
        bool leftOccupied  = !suppressLeft  && leftPanels.Count  > 0;
        bool right6Occupied = !suppressRight6 && right6Panels.Count > 0;
        bool right5Occupied = !suppressRight5 && right5Panels.Count > 0;
        bool right4Occupied  = !suppressRight4 && right4Panels.Count > 0;
        bool right3Occupied = !suppressRight3 && right3Panels.Count > 0;
        bool right2Occupied = !suppressRight2 && right2Panels.Count > 0;
        bool rightOccupied  = !suppressRight  && rightPanels.Count  > 0;

        // Count occupied zones on each side (non-suppressed with panels).
        int leftOccupiedCount  = (left6Occupied ? 1 : 0) + (left5Occupied ? 1 : 0) + (left4Occupied ? 1 : 0) + (left3Occupied ? 1 : 0) + (left2Occupied ? 1 : 0) + (leftOccupied  ? 1 : 0);
        int rightOccupiedCount = (rightOccupied ? 1 : 0) + (right2Occupied ? 1 : 0) + (right3Occupied ? 1 : 0) + (right4Occupied ? 1 : 0) + (right5Occupied ? 1 : 0) + (right6Occupied ? 1 : 0);

        // ── Compute zone dimensions ──────────────────────────────────────────

        // Left/Left2 zone column content height = total slot heights + gaps.
        // Rule B (sourceInZone): emits exactly N slots — no insertion extras.
        // When source is the sole occupant this produces 1 slot (the "you are here"
        // IsSourcePanel button); no no-op insertion slots are ever generated.
        double leftColContentHeight   = suppressLeft   ? 0 : ZoneColumnHeight(leftPanels.Count,   sourceInLeft,   ColSlotHeight);
        double rightColContentHeight  = suppressRight  ? 0 : ZoneColumnHeight(rightPanels.Count,  sourceInRight,  ColSlotHeight);
        double left2ColContentHeight  = suppressLeft2  ? 0 : ZoneColumnHeight(left2Panels.Count,  sourceInLeft2,  ColSlotHeight);
        double right2ColContentHeight = suppressRight2 ? 0 : ZoneColumnHeight(right2Panels.Count, sourceInRight2, ColSlotHeight);
        double left3ColContentHeight  = suppressLeft3  ? 0 : ZoneColumnHeight(left3Panels.Count,  sourceInLeft3,  ColSlotHeight);
        double right3ColContentHeight = suppressRight3 ? 0 : ZoneColumnHeight(right3Panels.Count, sourceInRight3, ColSlotHeight);
        double left4ColContentHeight  = suppressLeft4  ? 0 : ZoneColumnHeight(left4Panels.Count,  sourceInLeft4,  ColSlotHeight);
        double right4ColContentHeight = suppressRight4 ? 0 : ZoneColumnHeight(right4Panels.Count, sourceInRight4, ColSlotHeight);
        double left5ColContentHeight  = suppressLeft5  ? 0 : ZoneColumnHeight(left5Panels.Count,  sourceInLeft5,  ColSlotHeight);
        double right5ColContentHeight = suppressRight5 ? 0 : ZoneColumnHeight(right5Panels.Count, sourceInRight5, ColSlotHeight);
        double left6ColContentHeight  = suppressLeft6  ? 0 : ZoneColumnHeight(left6Panels.Count,  sourceInLeft6,  ColSlotHeight);
        double right6ColContentHeight = suppressRight6 ? 0 : ZoneColumnHeight(right6Panels.Count, sourceInRight6, ColSlotHeight);

        // Top zone content width
        double topContentWidth = ZoneRowWidth(topPanels.Count, sourceInTop, TopSlotWidth);

        // Overall popup inner height (excluding padding):
        //   max of all column heights, top slot height + lower half breathing room
        double innerHeight = Math.Max(
            Math.Max(
                Math.Max(
                    Math.Max(
                        Math.Max(
                            Math.Max(leftColContentHeight,  left2ColContentHeight),
                            Math.Max(left3ColContentHeight, left4ColContentHeight)),
                        Math.Max(
                            Math.Max(left5ColContentHeight, left6ColContentHeight),
                            Math.Max(rightColContentHeight, right2ColContentHeight))),
                    Math.Max(
                        Math.Max(right3ColContentHeight, right4ColContentHeight),
                        Math.Max(right5ColContentHeight, right6ColContentHeight))),
                TopSlotHeight * 2),   // top zone occupies upper half; lower half is breathing room
            0);

        // Clamp minimum inner height so popup never looks too small
        innerHeight = Math.Max(innerHeight, TopSlotHeight * 2 + ZoneGutter);
        // Cap maximum inner height so stacked panels shrink rather than blowing out the popup
        innerHeight = Math.Min(innerHeight, MaxInnerHeight);

        // ── Compute zone widths ──────────────────────────────────────────────

        // All side zones: ColSlotWidth when they have panels; ColSlotWidthEmpty when empty (drop-target only).
        double leftZoneWidth   = suppressLeft   ? 0 : (leftPanels.Count   == 0 && !sourceInLeft   ? ColSlotWidthEmpty : ColSlotWidth);
        double rightZoneWidth  = suppressRight  ? 0 : (rightPanels.Count  == 0 && !sourceInRight  ? ColSlotWidthEmpty : ColSlotWidth);
        double left2ZoneWidth  = suppressLeft2  ? 0 : (left2Panels.Count  == 0 && !sourceInLeft2  ? ColSlotWidthEmpty : ColSlotWidth);
        double right2ZoneWidth = suppressRight2 ? 0 : (right2Panels.Count == 0 && !sourceInRight2 ? ColSlotWidthEmpty : ColSlotWidth);
        double left3ZoneWidth  = suppressLeft3  ? 0 : (left3Panels.Count  == 0 && !sourceInLeft3  ? ColSlotWidthEmpty : ColSlotWidth);
        double right3ZoneWidth = suppressRight3 ? 0 : (right3Panels.Count == 0 && !sourceInRight3 ? ColSlotWidthEmpty : ColSlotWidth);
        double left4ZoneWidth  = suppressLeft4  ? 0 : (left4Panels.Count  == 0 && !sourceInLeft4  ? ColSlotWidthEmpty : ColSlotWidth);
        double right4ZoneWidth = suppressRight4 ? 0 : (right4Panels.Count == 0 && !sourceInRight4 ? ColSlotWidthEmpty : ColSlotWidth);
        double left5ZoneWidth  = suppressLeft5  ? 0 : (left5Panels.Count  == 0 && !sourceInLeft5  ? ColSlotWidthEmpty : ColSlotWidth);
        double right5ZoneWidth = suppressRight5 ? 0 : (right5Panels.Count == 0 && !sourceInRight5 ? ColSlotWidthEmpty : ColSlotWidth);
        double left6ZoneWidth  = suppressLeft6  ? 0 : (left6Panels.Count  == 0 && !sourceInLeft6  ? ColSlotWidthEmpty : ColSlotWidth);
        double right6ZoneWidth = suppressRight6 ? 0 : (right6Panels.Count == 0 && !sourceInRight6 ? ColSlotWidthEmpty : ColSlotWidth);

        double topZoneWidth = Math.Max(topContentWidth, TopSlotWidth);

        double popupHeight = innerHeight + PopupPadding * 2 + LabelRowHeight;

        // ── Layout slot positions (relative to inner canvas, 0,0 at top-left) ──

        // Compute zone positions algorithmically by walking left-to-right.
        // When we encounter a pair of adjacent occupied zones, insert a thin between them.
        // If the outermost occupied zone isn't at the edge, add an outer thin first.
        
        double curX = 0;
        var leftThinPositions  = new List<(double X, DockZone TargetZone, int TargetOrder, SyntheticInsertKind Kind)>();
        
        // Left side layout: walk outermost (Left6) to innermost (Left)
        double left6X = 0, left5X = 0, left4X = 0, left3X = 0, left2X = 0, leftX = 0;
        
        if (!suppressLeft6)
        {
            // Add synthetic outer thin only when Left6 is occupied (no natural outer-edge thin exists)
            if (left6Occupied && leftOccupiedCount >= 2)
            {
                leftThinPositions.Add((curX, DockZone.Left6, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
            }
            left6X = curX;
            curX += left6ZoneWidth;

            // Thin between Left6 and Left5 if both occupied
            bool left6IsSolo = IsSoloPanelZone(left6Panels.Count, left6Occupied, suppressLeft6);
            bool left5IsSolo = IsSoloPanelZone(left5Panels.Count, left5Occupied, suppressLeft5);
            if (left6Occupied && left5Occupied)
            {
                curX += InnerZoneGap;
                leftThinPositions.Add((curX, DockZone.Left5, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"    [solo-panel-check] Thin between Left6 (solo={left6IsSolo}) and Left5 (solo={left5IsSolo}): created");
            }
            else if (!suppressLeft5)
            {
                curX += InnerZoneGap;
            }
        }

        if (!suppressLeft5)
        {
            // Add synthetic outer thin only when Left5 is occupied and Left6 is NOT occupied
            if (left5Occupied && !left6Occupied && leftOccupiedCount >= 2)
            {
                leftThinPositions.Add((curX, DockZone.Left5, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
            }
            left5X = curX;
            curX += left5ZoneWidth;

            // Thin between Left5 and Left4 if both occupied
            bool left5IsSolo = IsSoloPanelZone(left5Panels.Count, left5Occupied, suppressLeft5);
            bool left4IsSolo = IsSoloPanelZone(left4Panels.Count, left4Occupied, suppressLeft4);
            if (left5Occupied && left4Occupied)
            {
                curX += InnerZoneGap;
                leftThinPositions.Add((curX, DockZone.Left4, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"    [solo-panel-check] Thin between Left5 (solo={left5IsSolo}) and Left4 (solo={left4IsSolo}): created");
            }
            else if (!suppressLeft4)
            {
                curX += InnerZoneGap;
            }
        }

        if (!suppressLeft4)
        {
            // Add synthetic outer thin only when Left4 is the outermost occupied zone (Left5/Left6 not occupied)
            if (left4Occupied && !left5Occupied && !left6Occupied && leftOccupiedCount >= 2)
            {
                leftThinPositions.Add((curX, DockZone.Left4, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
            }
            left4X = curX;
            curX += left4ZoneWidth;
            
            // Thin between Left4 and Left3 if both occupied
            bool left4IsSolo = IsSoloPanelZone(left4Panels.Count, left4Occupied, suppressLeft4);
            bool left3IsSolo = IsSoloPanelZone(left3Panels.Count, left3Occupied, suppressLeft3);
            if (left4Occupied && left3Occupied)
            {
                curX += InnerZoneGap;
                leftThinPositions.Add((curX, DockZone.Left3, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"    [solo-panel-check] Thin between Left4 (solo={left4IsSolo}) and Left3 (solo={left3IsSolo}): created");
            }
            else if (!suppressLeft3)
            {
                curX += InnerZoneGap;
            }
        }
        
        if (!suppressLeft3)
        {
            // Add synthetic outer thin only when Left3 is occupied and Left4/Left5/Left6 are NOT occupied
            if (left3Occupied && !left4Occupied && !left5Occupied && !left6Occupied && leftOccupiedCount >= 2)
            {
                leftThinPositions.Add((curX, DockZone.Left3, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
            }
            left3X = curX;
            curX += left3ZoneWidth;
            
            // Thin between Left3 and Left2 if both occupied
            bool left3IsSolo = IsSoloPanelZone(left3Panels.Count, left3Occupied, suppressLeft3);
            bool left2IsSolo = IsSoloPanelZone(left2Panels.Count, left2Occupied, suppressLeft2);
            if (left3Occupied && left2Occupied)
            {
                curX += InnerZoneGap;
                leftThinPositions.Add((curX, DockZone.Left2, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"    [solo-panel-check] Thin between Left3 (solo={left3IsSolo}) and Left2 (solo={left2IsSolo}): created");
            }
            else if (!suppressLeft2)
            {
                curX += InnerZoneGap;
            }
        }
        
        if (!suppressLeft2)
        {
            // NOTE: When Left2 is empty it renders as a natural thin zone (ColSlotWidthEmpty).
            // That natural thin IS already the outer drop target — do NOT add a synthetic thin
            // before it; that would create two adjacent Left2 thins (the bug we're fixing here).
            // The synthetic outer thin is only needed when Left2 is occupied (full-width), but
            // that case is handled by the left2Occupied&&leftOccupied inter-zone thin below.
            left2X = curX;
            curX += left2ZoneWidth;
            
            // Thin between Left2 and Left if both occupied.
            bool left2IsSolo = IsSoloPanelZone(left2Panels.Count, left2Occupied, suppressLeft2);
            bool leftIsSolo = IsSoloPanelZone(leftPanels.Count, leftOccupied, suppressLeft);
            if (left2Occupied && leftOccupied)
            {
                curX += InnerZoneGap;
                leftThinPositions.Add((curX, DockZone.Left, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"    [solo-panel-check] Thin between Left2 (solo={left2IsSolo}) and Left (solo={leftIsSolo}): created");
            }
            else if (!suppressLeft)
            {
                curX += InnerZoneGap;
            }
        }
        
        if (!suppressLeft)
        {
            // If all outer zones suppressed and Left is empty but would need outer thin
            if (suppressLeft6 && suppressLeft5 && suppressLeft4 && suppressLeft3 && suppressLeft2 && !leftOccupied && leftOccupiedCount >= 2)
            {
                leftThinPositions.Add((curX, DockZone.Left, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
            }
            leftX = curX;
            curX += leftZoneWidth;
            // Inner-edge thin after Left when L3/L4/L5/L6 is the natural outer thin and L2+L both occupied.
            // This is the 3rd required thin: L3+/L4+/L5+/L6+=outer-natural, L2↔L=between, inner=after-L.
            if (!left3Occupied && !left4Occupied && !left5Occupied && !left6Occupied && left2Occupied && leftOccupied)
            {
                curX += InnerZoneGap;
                leftThinPositions.Add((curX, DockZone.Left, leftPanels.Count, SyntheticInsertKind.InsertAfter));
                curX += ColSlotWidthEmpty;
                // No trailing InnerZoneGap; ZoneGutter follows immediately after
            }
            // Inner-edge thin after Left when L3/L4/L5/L6 is occupied, L2 is the natural empty thin, and Left is occupied.
            // 3 thins: L3+/L4+/L5+/L6+-outer-synth (x=0), L2-natural-middle (x=M), inner-after-L (x=here).
            // Drop on inner thin → InsertBefore Left@0 (Case B): shifts Left→Left2, panel lands at Left.
            else if ((left3Occupied || left4Occupied || left5Occupied || left6Occupied) && !left2Occupied && leftOccupied)
            {
                curX += InnerZoneGap;
                leftThinPositions.Add((curX, DockZone.Left, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty;
                // No trailing InnerZoneGap; ZoneGutter follows immediately after
            }
            // Inner-edge thin after Left when only Left is occupied (N=1 case).
            // Left2 is the natural outer thin — Left provides the inner thin.
            // N+1 for N=1: Left2-natural (outer) + inner-after-Left = 2 thins total.
            else if (!left6Occupied && !left5Occupied && !left4Occupied && !left3Occupied && !left2Occupied && leftOccupied && !suppressLeft2)
            {
                curX += InnerZoneGap;
                leftThinPositions.Add((curX, DockZone.Left, leftPanels.Count, SyntheticInsertKind.InsertAfter));
                curX += ColSlotWidthEmpty;
                // No trailing InnerZoneGap; ZoneGutter follows immediately after
            }
        }
        if (!suppressLeft)
            curX += ZoneGutter;
        else if (!suppressLeft2)
            curX += ZoneGutter;
        else if (!suppressLeft3)
            curX += ZoneGutter;
        else if (!suppressLeft4)
            curX += ZoneGutter;
        else if (!suppressLeft5)
            curX += ZoneGutter;
        else if (!suppressLeft6)
            curX += ZoneGutter;
        
        double topX = curX;
        curX += topZoneWidth;
        
        // Gap after top zone
        if (!suppressRight)
            curX += ZoneGutter;
        else if (!suppressRight2)
            curX += ZoneGutter;
        else if (!suppressRight3)
            curX += ZoneGutter;
        else if (!suppressRight4)
            curX += ZoneGutter;
        else if (!suppressRight5)
            curX += ZoneGutter;
        else if (!suppressRight6)
            curX += ZoneGutter;
        
        // Right side layout: walk innermost (Right) to outermost (Right6)
        double rightX = 0, right2X = 0, right3X = 0, right4X = 0, right5X = 0, right6X = 0;
        var rightThinPositions = new List<(double X, DockZone TargetZone, int TargetOrder, SyntheticInsertKind Kind)>();
        
        if (!suppressRight)
        {
            // If all outer zones suppressed and Right is empty but would need outer thin
            if (suppressRight2 && suppressRight3 && suppressRight4 && suppressRight5 && suppressRight6 && !rightOccupied && rightOccupiedCount >= 2)
            {
                rightThinPositions.Add((curX, DockZone.Right, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
            }
            // When R3/R4/R5/R6 is a natural thin (empty) and R+R2 are both occupied, add inner-thin before Right
            // to provide the 3rd drop point (inner=before-R, mid=between-R-and-R2, R3+/R4+=outer-natural)
            if (!suppressRight3 && !right3Occupied && !right4Occupied && !right5Occupied && !right6Occupied && rightOccupied && right2Occupied)
            {
                rightThinPositions.Add((curX, DockZone.Right, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
            }
            // Inner-edge thin before Right when only Right is occupied (N=1 case).
            // Right2 is the natural outer thin — this provides the inner drop target.
            // N+1 for N=1: inner-before-Right + Right2-natural (outer) = 2 thins total.
            else if (suppressRight3 && suppressRight4 && suppressRight5 && suppressRight6 && !right2Occupied && rightOccupied && !suppressRight2)
            {
                rightThinPositions.Add((curX, DockZone.Right, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
            }
            // N=3+ case: Right3 or Right4 or Right5 or Right6 is also occupied. The above branches only cover
            // Right3+ being empty. When outer zones are occupied we still need the inner-left thin.
            else if (rightOccupied && (right3Occupied || right4Occupied || right5Occupied || right6Occupied))
            {
                rightThinPositions.Add((curX, DockZone.Right, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
            }
            rightX = curX;
            curX += rightZoneWidth;
            
            // Thin between Right and Right2 if both occupied
            bool rightIsSolo = IsSoloPanelZone(rightPanels.Count, rightOccupied, suppressRight);
            bool right2IsSolo = IsSoloPanelZone(right2Panels.Count, right2Occupied, suppressRight2);
            if (rightOccupied && right2Occupied)
            {
                curX += InnerZoneGap;
                rightThinPositions.Add((curX, DockZone.Right2, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"    [solo-panel-check] Thin between Right (solo={rightIsSolo}) and Right2 (solo={right2IsSolo}): created");
            }
            else if (!suppressRight2)
            {
                curX += InnerZoneGap;
            }
        }
        
        if (!suppressRight2)
        {
            // NOTE: When Right2 is empty it renders as a natural thin zone. That thin IS already
            // the outer drop target — do NOT add a synthetic thin before it (same adjacent-thin
            // bug that was fixed on the left side).
            right2X = curX;
            curX += right2ZoneWidth;
            
            // Thin between Right2 and Right3 if both occupied
            bool right2IsSolo = IsSoloPanelZone(right2Panels.Count, right2Occupied, suppressRight2);
            bool right3IsSolo = IsSoloPanelZone(right3Panels.Count, right3Occupied, suppressRight3);
            if (right2Occupied && right3Occupied)
            {
                curX += InnerZoneGap;
                rightThinPositions.Add((curX, DockZone.Right3, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"    [solo-panel-check] Thin between Right2 (solo={right2IsSolo}) and Right3 (solo={right3IsSolo}): created");
            }
            else if (!suppressRight3)
            {
                curX += InnerZoneGap;
            }
        }
        
        if (!suppressRight3)
        {
            right3X = curX;
            curX += right3ZoneWidth;

            // Thin between Right3 and Right4 if both occupied.
            // InsertBefore Right4@0: user wants to insert between R3 and R4; preview shows left edge of R4.
            bool right3IsSolo = IsSoloPanelZone(right3Panels.Count, right3Occupied, suppressRight3);
            bool right4IsSolo = IsSoloPanelZone(right4Panels.Count, right4Occupied, suppressRight4);
            if (right3Occupied && right4Occupied)
            {
                curX += InnerZoneGap;
                rightThinPositions.Add((curX, DockZone.Right4, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"    [solo-panel-check] Thin between Right3 (solo={right3IsSolo}) and Right4 (solo={right4IsSolo}): created");
            }
            else if (!suppressRight4)
            {
                curX += InnerZoneGap;
            }
            // Add synthetic outer thin AFTER Right3 zone slot when Right3 is the outermost occupied zone
            // and Right4 is suppressed (no natural outer-edge thin zone exists beyond Right3).
            else if (right3Occupied && !right4Occupied && rightOccupiedCount >= 2)
            {
                curX += InnerZoneGap;
                rightThinPositions.Add((curX, DockZone.Right3, right3Panels.Count, SyntheticInsertKind.InsertAfter));
                curX += ColSlotWidthEmpty;
            }
        }

        if (!suppressRight4)
        {
            right4X = curX;
            curX += right4ZoneWidth;
            // Thin between Right4 and Right5 if both occupied.
            bool right4IsSolo = IsSoloPanelZone(right4Panels.Count, right4Occupied, suppressRight4);
            bool right5IsSolo = IsSoloPanelZone(right5Panels.Count, right5Occupied, suppressRight5);
            if (right4Occupied && right5Occupied)
            {
                curX += InnerZoneGap;
                rightThinPositions.Add((curX, DockZone.Right5, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"    [solo-panel-check] Thin between Right4 (solo={right4IsSolo}) and Right5 (solo={right5IsSolo}): created");
            }
            else if (!suppressRight5)
            {
                curX += InnerZoneGap;
            }
            // Add synthetic outer thin AFTER Right4 zone slot when Right4 is the outermost occupied zone
            // and Right5/Right6 are suppressed.
            else if (right4Occupied && !right5Occupied && !right6Occupied && rightOccupiedCount >= 2)
            {
                curX += InnerZoneGap;
                rightThinPositions.Add((curX, DockZone.Right4, right4Panels.Count, SyntheticInsertKind.InsertAfter));
                curX += ColSlotWidthEmpty;
            }
        }

        if (!suppressRight5)
        {
            right5X = curX;
            curX += right5ZoneWidth;
            // Thin between Right5 and Right6 if both occupied.
            bool right5IsSolo = IsSoloPanelZone(right5Panels.Count, right5Occupied, suppressRight5);
            bool right6IsSolo = IsSoloPanelZone(right6Panels.Count, right6Occupied, suppressRight6);
            if (right5Occupied && right6Occupied)
            {
                curX += InnerZoneGap;
                rightThinPositions.Add((curX, DockZone.Right6, 0, SyntheticInsertKind.InsertBefore));
                curX += ColSlotWidthEmpty + InnerZoneGap;
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"    [solo-panel-check] Thin between Right5 (solo={right5IsSolo}) and Right6 (solo={right6IsSolo}): created");
            }
            else if (!suppressRight6)
            {
                curX += InnerZoneGap;
            }
            // Add synthetic outer thin AFTER Right5 zone slot when Right5 is the outermost occupied zone
            // and Right6 is suppressed (no natural outer-edge thin zone exists beyond Right5).
            else if (right5Occupied && !right6Occupied && rightOccupiedCount >= 2)
            {
                curX += InnerZoneGap;
                rightThinPositions.Add((curX, DockZone.Right5, right5Panels.Count, SyntheticInsertKind.InsertAfter));
                curX += ColSlotWidthEmpty;
            }
        }

        if (!suppressRight6)
        {
            right6X = curX;
            curX += right6ZoneWidth;
            // Add synthetic outer thin AFTER Right6 zone slot (outer-right edge).
            if (right6Occupied && rightOccupiedCount >= 2)
            {
                curX += InnerZoneGap;
                rightThinPositions.Add((curX, DockZone.Right6, right6Panels.Count, SyntheticInsertKind.InsertAfter));
                curX += ColSlotWidthEmpty;
            }
        }

        double innerWidth = curX;
        double popupWidth = innerWidth + PopupPadding * 2;

        // ── Synthetic thin drop-target slots ─────────────────────────────────
        // Algorithmically generated based on occupied zone pairs
        foreach (var (x, targetZone, targetOrder, kind) in leftThinPositions)
        {
            allSlots.Add(new SlotButtonViewModel(
                Label: "—", IsSourcePanel: false, IsExpansionButton: false,
                X: x, Y: LabelRowHeight,
                Width: ColSlotWidthEmpty, Height: innerHeight,
                TargetZone: targetZone, TargetOrder: targetOrder,
                SourcePanelId: sourcePanelId)
            { InsertKind = kind });
        }
        
        foreach (var (x, targetZone, targetOrder, kind) in rightThinPositions)
        {
            allSlots.Add(new SlotButtonViewModel(
                Label: "—", IsSourcePanel: false, IsExpansionButton: false,
                X: x, Y: LabelRowHeight,
                Width: ColSlotWidthEmpty, Height: innerHeight,
                TargetZone: targetZone, TargetOrder: targetOrder,
                SourcePanelId: sourcePanelId)
            { InsertKind = kind });
        }

        // ── Left side slots ──────────────────────────────────────────────────
        if (!suppressLeft6)
            BuildColumnSlots(
                allSlots, sourcePanelId, left6Panels, sourceInLeft6,
                left6X, LabelRowHeight, left6ZoneWidth, ColSlotHeight, innerHeight, DockZone.Left6);

        if (!suppressLeft5)
            BuildColumnSlots(
                allSlots, sourcePanelId, left5Panels, sourceInLeft5,
                left5X, LabelRowHeight, left5ZoneWidth, ColSlotHeight, innerHeight, DockZone.Left5);

        if (!suppressLeft4)
            BuildColumnSlots(
                allSlots, sourcePanelId, left4Panels, sourceInLeft4,
                left4X, LabelRowHeight, left4ZoneWidth, ColSlotHeight, innerHeight, DockZone.Left4);

        if (!suppressLeft3)
            BuildColumnSlots(
                allSlots, sourcePanelId, left3Panels, sourceInLeft3,
                left3X, LabelRowHeight, left3ZoneWidth, ColSlotHeight, innerHeight, DockZone.Left3);

        if (!suppressLeft2)
            BuildColumnSlots(
                allSlots, sourcePanelId, left2Panels, sourceInLeft2,
                left2X, LabelRowHeight, left2ZoneWidth, ColSlotHeight, innerHeight, DockZone.Left2);

        if (!suppressLeft)
            BuildColumnSlots(
                allSlots, sourcePanelId, leftPanels, sourceInLeft,
                leftX, LabelRowHeight, leftZoneWidth, ColSlotHeight, innerHeight, DockZone.Left);

        // Top zone slots — top-aligned so their tops line up with the column panel tops
        BuildRowSlots(
            allSlots,
            sourcePanelId,
            topPanels,
            sourceInTop,
            topX, LabelRowHeight,
            TopSlotWidth, TopSlotHeight,
            topZoneWidth,
            DockZone.Top);

        // ── Right side slots ─────────────────────────────────────────────────
        if (!suppressRight)
            BuildColumnSlots(
                allSlots, sourcePanelId, rightPanels, sourceInRight,
                rightX, LabelRowHeight, rightZoneWidth, ColSlotHeight, innerHeight, DockZone.Right);

        if (!suppressRight2)
            BuildColumnSlots(
                allSlots, sourcePanelId, right2Panels, sourceInRight2,
                right2X, LabelRowHeight, right2ZoneWidth, ColSlotHeight, innerHeight, DockZone.Right2);

        if (!suppressRight3)
            BuildColumnSlots(
                allSlots, sourcePanelId, right3Panels, sourceInRight3,
                right3X, LabelRowHeight, right3ZoneWidth, ColSlotHeight, innerHeight, DockZone.Right3);

        if (!suppressRight4)
            BuildColumnSlots(
                allSlots, sourcePanelId, right4Panels, sourceInRight4,
                right4X, LabelRowHeight, right4ZoneWidth, ColSlotHeight, innerHeight, DockZone.Right4);

        if (!suppressRight5)
            BuildColumnSlots(
                allSlots, sourcePanelId, right5Panels, sourceInRight5,
                right5X, LabelRowHeight, right5ZoneWidth, ColSlotHeight, innerHeight, DockZone.Right5);

        if (!suppressRight6)
            BuildColumnSlots(
                allSlots, sourcePanelId, right6Panels, sourceInRight6,
                right6X, LabelRowHeight, right6ZoneWidth, ColSlotHeight, innerHeight, DockZone.Right6);

        // ── Separators ───────────────────────────────────────────────────────
        // Thin pill-shaped vertical dividers between the top zone and each side group.
        // A separator is placed in the ZoneGutter midpoint when that side exists.
        const double SeparatorWidth = 4.0;
        // Left separator: topX > 0 means at least one left zone exists with a gutter before the top zone.
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
        // Right separator: top zone does not extend to innerWidth means a right side exists.
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

        // ── Find the source panel slot center ───────────────────────────────
        var srcSlot = allSlots.FirstOrDefault(s => s.IsSourcePanel);
        double srcCenterX = srcSlot is not null
            ? srcSlot.X + srcSlot.Width  / 2 + PopupPadding
            : popupWidth  / 2;
        double srcCenterY = srcSlot is not null
            ? srcSlot.Y + srcSlot.Height / 2 + PopupPadding
            : popupHeight / 2;

        // ── Section label center X positions ────────────────────────────────
        var leftZoneSlots = allSlots.Where(s => !s.IsSeparator &&
            (s.TargetZone == DockZone.Left || s.TargetZone == DockZone.Left2 || s.TargetZone == DockZone.Left3 || s.TargetZone == DockZone.Left4 || s.TargetZone == DockZone.Left5 || s.TargetZone == DockZone.Left6)).ToList();
        bool hasLeftSection = leftZoneSlots.Count > 0;
        double leftSectionCenterX = hasLeftSection
            ? (leftZoneSlots.Min(s => s.X) + leftZoneSlots.Max(s => s.X + s.Width)) / 2
            : 0;

        var topZoneSlots = allSlots.Where(s => !s.IsSeparator && s.TargetZone == DockZone.Top).ToList();
        double topSectionCenterX = topZoneSlots.Count > 0
            ? (topZoneSlots.Min(s => s.X) + topZoneSlots.Max(s => s.X + s.Width)) / 2
            : innerWidth / 2;

        var rightZoneSlots = allSlots.Where(s => !s.IsSeparator &&
            (s.TargetZone == DockZone.Right || s.TargetZone == DockZone.Right2 || s.TargetZone == DockZone.Right3 || s.TargetZone == DockZone.Right4 || s.TargetZone == DockZone.Right5 || s.TargetZone == DockZone.Right6)).ToList();
        bool hasRightSection = rightZoneSlots.Count > 0;
        double rightSectionCenterX = hasRightSection
            ? (rightZoneSlots.Min(s => s.X) + rightZoneSlots.Max(s => s.X + s.Width)) / 2
            : 0;

        // ── Trace slot dump (visible in the Docking trace panel at runtime) ───
        SquadDashTrace.Write(TraceCategory.Docking,
            $"BuildDockingMap src={sourcePanelId}: {allSlots.Count} slots, popup={popupWidth:F0}×{popupHeight:F0}");
        foreach (var s in allSlots)
            SquadDashTrace.Write(TraceCategory.Docking,
                $"  zone={s.TargetZone,-8} order={s.TargetOrder,3}  x={s.X,5:F0} y={s.Y,4:F0}"
                + $"  w={s.Width,4:F0} h={s.Height,3:F0}"
                + (s.IsSourcePanel ? "  [src]" : "")
                + (s.IsSeparator   ? "  [sep]" : "")
                + (string.IsNullOrEmpty(s.Label) ? "" : $"  '{s.Label}'"));

        // Thin-check summary: synthetic thins + natural empty zone thins per side
        int actualThinLeft  = allSlots.Count(s => !s.IsSeparator && s.Width < ColSlotWidth
            && (s.TargetZone == DockZone.Left  || s.TargetZone == DockZone.Left2  || s.TargetZone == DockZone.Left3  || s.TargetZone == DockZone.Left4  || s.TargetZone == DockZone.Left5  || s.TargetZone == DockZone.Left6));
        int actualThinRight = allSlots.Count(s => !s.IsSeparator && s.Width < ColSlotWidth
            && (s.TargetZone == DockZone.Right || s.TargetZone == DockZone.Right2 || s.TargetZone == DockZone.Right3 || s.TargetZone == DockZone.Right4 || s.TargetZone == DockZone.Right5 || s.TargetZone == DockZone.Right6));

        // Validate: warn if adjacent thin slots exist on the same side (visual regression indicator)
        var leftThinSlots  = allSlots.Where(s => !s.IsSeparator && s.Width < ColSlotWidth
            && (s.TargetZone == DockZone.Left  || s.TargetZone == DockZone.Left2  || s.TargetZone == DockZone.Left3  || s.TargetZone == DockZone.Left4  || s.TargetZone == DockZone.Left5  || s.TargetZone == DockZone.Left6))
            .OrderBy(s => s.X).ToList();
        var rightThinSlots = allSlots.Where(s => !s.IsSeparator && s.Width < ColSlotWidth
            && (s.TargetZone == DockZone.Right || s.TargetZone == DockZone.Right2 || s.TargetZone == DockZone.Right3 || s.TargetZone == DockZone.Right4 || s.TargetZone == DockZone.Right5 || s.TargetZone == DockZone.Right6))
            .OrderBy(s => s.X).ToList();
        for (int i = 1; i < leftThinSlots.Count; i++)
        {
            var s1 = leftThinSlots[i - 1]; var s2 = leftThinSlots[i];
            if (Math.Abs(s2.X - s1.X) < ColSlotWidth)
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"  [thin-layout WARNING] adjacent thin slots detected: {s1.TargetZone} x={s1.X:F0} and {s2.TargetZone} x={s2.X:F0}");
        }
        for (int i = 1; i < rightThinSlots.Count; i++)
        {
            var s1 = rightThinSlots[i - 1]; var s2 = rightThinSlots[i];
            if (Math.Abs(s2.X - s1.X) < ColSlotWidth)
                SquadDashTrace.Write(TraceCategory.Docking,
                    $"  [thin-layout WARNING] adjacent thin slots detected: {s1.TargetZone} x={s1.X:F0} and {s2.TargetZone} x={s2.X:F0}");
        }

        var leftSynthDesc  = string.Join(", ", leftThinPositions.Select(t  => $"{t.Kind} {t.TargetZone}@{t.TargetOrder}"));
        var rightSynthDesc = string.Join(", ", rightThinPositions.Select(t => $"{t.Kind} {t.TargetZone}@{t.TargetOrder}"));
        SquadDashTrace.Write(TraceCategory.Docking,
            $"  [thin-check] left: thinSlots={actualThinLeft} (synth={leftThinPositions.Count}: [{leftSynthDesc}])  right: thinSlots={actualThinRight} (synth={rightThinPositions.Count}: [{rightSynthDesc}])");

        // N+1 rule: for N>=2 occupied zones on a side, expect exactly N+1 thin drop-target slots
        int expectedThinLeft  = leftOccupiedCount  >= 2 ? leftOccupiedCount  + 1 : 0;
        int expectedThinRight = rightOccupiedCount >= 2 ? rightOccupiedCount + 1 : 0;
        if ((expectedThinLeft  > 0 && actualThinLeft  != expectedThinLeft)
         || (expectedThinRight > 0 && actualThinRight != expectedThinRight))
            SquadDashTrace.Write(TraceCategory.Docking,
                $"  [thin-check WARNING] left: expected={expectedThinLeft} actual={actualThinLeft}  right: expected={expectedThinRight} actual={actualThinRight}");

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

            // N+1 rule: N occupied zones → N+1 thin drop-targets required (N >= 1)
            if (occupiedZoneCount >= 1)
            {
                int expectedThins = occupiedZoneCount + 1;
                if (thinSlots.Count != expectedThins)
                    violations.Add(
                        $"{sideName}: N+1 rule violated — {occupiedZoneCount} occupied zone(s) require {expectedThins} thin slot(s), got {thinSlots.Count}");
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

        CheckSide([DockZone.Left, DockZone.Left2, DockZone.Left3, DockZone.Left4],    "Left");
        CheckSide([DockZone.Right, DockZone.Right2, DockZone.Right3, DockZone.Right4], "Right");

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

            CheckNoopLateral([DockZone.Left, DockZone.Left2, DockZone.Left3, DockZone.Left4], "Left");
            CheckNoopLateral([DockZone.Right, DockZone.Right2, DockZone.Right3, DockZone.Right4], "Right");
        }

        return violations;
    }

    private static List<string> PanelsInZone(DockLayout layout, DockZone zone) =>
        layout.Slots
              .Where(s => s.Zone == zone)
              .OrderBy(s => s.Order)
              .Select(s => s.PanelId)
              .ToList();

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
