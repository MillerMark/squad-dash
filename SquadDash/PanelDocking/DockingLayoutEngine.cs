#nullable enable

namespace SquadDash.PanelDocking;

/// <summary>
/// Pure static logic for docking layout — no WPF dependencies.
/// Replicates the zone filtering and suppression logic of DockingMapBuilder
/// and the move logic of PanelDockingService.MovePanel.
/// </summary>
internal static class DockingLayoutEngine
{
    // ── Zone name helpers ────────────────────────────────────────────────────

    public static string GetZoneDisplayName(DockZone zone) => zone switch
    {
        DockZone.Top    => "Top",
        DockZone.Left   => "Left 1",
        DockZone.Left2  => "Left 2",
        DockZone.Left3  => "Left 3",
        DockZone.Left4  => "Left 4",
        DockZone.Right  => "Right 1",
        DockZone.Right2 => "Right 2",
        DockZone.Right3 => "Right 3",
        DockZone.Right4 => "Right 4",
        _               => zone.ToString()
    };

    /// <summary>
    /// Parses a zone display name (e.g. "Left 3") into the corresponding <see cref="DockZone"/>.
    /// Returns <see cref="DockZone.Top"/> for unrecognised values.
    /// </summary>
    public static DockZone ParseZoneDisplayName(string displayName) => displayName switch
    {
        "Top"     => DockZone.Top,
        "Left 1"  => DockZone.Left,
        "Left 2"  => DockZone.Left2,
        "Left 3"  => DockZone.Left3,
        "Left 4"  => DockZone.Left4,
        "Right 1" => DockZone.Right,
        "Right 2" => DockZone.Right2,
        "Right 3" => DockZone.Right3,
        "Right 4" => DockZone.Right4,
        _         => DockZone.Top,
    };

    public static string GetZoneFileTag(DockZone zone) => zone switch
    {
        DockZone.Top    => "Top",
        DockZone.Left   => "Left1",
        DockZone.Left2  => "Left2",
        DockZone.Left3  => "Left3",
        DockZone.Left4  => "Left4",
        DockZone.Right  => "Right1",
        DockZone.Right2 => "Right2",
        DockZone.Right3 => "Right3",
        DockZone.Right4 => "Right4",
        _               => zone.ToString()
    };

    // ── JSON serialization helpers ───────────────────────────────────────────

    public static Dictionary<string, List<string>> LayoutToJson(PanelLayoutData layout)
    {
        var result = new Dictionary<string, List<string>>();
        // When the layout carries visibility information, only serialize panels the user
        // can actually see.  Health/Trace and other always-hidden panels live in Slots
        // (they have a zone assignment) but must not pollute test-case snapshots.
        bool hasVisibility = layout.VisiblePanelIds.Count > 0;
        foreach (var zone in new[] { DockZone.Top, DockZone.Left, DockZone.Left2, DockZone.Left3, DockZone.Left4, DockZone.Right, DockZone.Right2, DockZone.Right3, DockZone.Right4 })
        {
            result[GetZoneDisplayName(zone)] = layout.Slots
                .Where(s => s.Zone == zone && (!hasVisibility || layout.VisiblePanelIds.Contains(s.PanelId)))
                .OrderBy(s => s.Order)
                .Select(s => s.PanelId)
                .ToList();
        }
        return result;
    }

    public static PanelLayoutData ParseLayoutFromJson(Dictionary<string, List<string>> json)
    {
        var displayToZone = new Dictionary<string, DockZone>(StringComparer.OrdinalIgnoreCase)
        {
            ["Top"]     = DockZone.Top,
            ["Left 1"]  = DockZone.Left,
            ["Left 2"]  = DockZone.Left2,
            ["Left 3"]  = DockZone.Left3,
            ["Left 4"]  = DockZone.Left4,
            ["Right 1"] = DockZone.Right,
            ["Right 2"] = DockZone.Right2,
            ["Right 3"] = DockZone.Right3,
            ["Right 4"] = DockZone.Right4,
        };

        var slots = new List<PanelSlot>();
        var allPanelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (zoneName, panelIds) in json)
        {
            if (!displayToZone.TryGetValue(zoneName, out var zone)) continue;
            for (int i = 0; i < panelIds.Count; i++)
            {
                slots.Add(new PanelSlot(panelIds[i], zone, i));
                allPanelIds.Add(panelIds[i]);
            }
        }

        return new PanelLayoutData
        {
            Slots = slots,
            VisiblePanelIds = allPanelIds,
        };
    }

    // ── Slot button building ─────────────────────────────────────────────────

    /// <summary>
    /// Returns all destination (non-source) slot buttons for the given source panel,
    /// applying the same zone-filtering and suppression logic as DockingMapBuilder.
    /// </summary>
    public static List<SlotButtonInfo> BuildSlotButtons(string sourcePanelId, PanelLayoutData layout)
    {
        var result = new List<SlotButtonInfo>();

        var topPanels    = FilterZone(PanelsInZone(layout, DockZone.Top),    sourcePanelId, layout.VisiblePanelIds);
        var leftPanels   = FilterZone(PanelsInZone(layout, DockZone.Left),   sourcePanelId, layout.VisiblePanelIds);
        var rightPanels  = FilterZone(PanelsInZone(layout, DockZone.Right),  sourcePanelId, layout.VisiblePanelIds);
        var left2Panels  = FilterZone(PanelsInZone(layout, DockZone.Left2),  sourcePanelId, layout.VisiblePanelIds);
        var right2Panels = FilterZone(PanelsInZone(layout, DockZone.Right2), sourcePanelId, layout.VisiblePanelIds);
        var left3Panels  = FilterZone(PanelsInZone(layout, DockZone.Left3),  sourcePanelId, layout.VisiblePanelIds);
        var right3Panels = FilterZone(PanelsInZone(layout, DockZone.Right3), sourcePanelId, layout.VisiblePanelIds);
        var left4Panels  = FilterZone(PanelsInZone(layout, DockZone.Left4),  sourcePanelId, layout.VisiblePanelIds);
        var right4Panels = FilterZone(PanelsInZone(layout, DockZone.Right4), sourcePanelId, layout.VisiblePanelIds);
        var left5Panels  = FilterZone(PanelsInZone(layout, DockZone.Left5),  sourcePanelId, layout.VisiblePanelIds);
        var right5Panels = FilterZone(PanelsInZone(layout, DockZone.Right5), sourcePanelId, layout.VisiblePanelIds);
        var left6Panels  = FilterZone(PanelsInZone(layout, DockZone.Left6),  sourcePanelId, layout.VisiblePanelIds);
        var right6Panels = FilterZone(PanelsInZone(layout, DockZone.Right6), sourcePanelId, layout.VisiblePanelIds);

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

        // Mirrors DockingMapBuilder suppression logic exactly:
        // Case 1: both outer and inner zones are empty — suppress the outer zone.
        // Case 2: source is the sole occupant of its zone AND the sibling is empty — no-op move.
        // Bug 1 fix: don't suppress Left2/Right2 when the outer tier has panels.
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
        // Also suppress an empty outer zone when source is the sole occupant of the adjacent
        // inner zone — dragging there is a no-op (panel moves out, normalization slides it back).
        bool suppressLeft3  = (left3Panels.Count == 0 && !sourceInLeft3 && suppressLeft2)
                           || (left3Panels.Count == 0 && !sourceInLeft3
                               && left2Panels.Count == 0 && !sourceInLeft2
                               && leftPanels.Count > 0)
                           || (sourceInLeft2  && left2Panels.Count  == 1 && left3Panels.Count  == 0);
        bool suppressRight3 = (right3Panels.Count == 0 && !sourceInRight3 && suppressRight2)
                           || (right3Panels.Count == 0 && !sourceInRight3
                               && right2Panels.Count == 0 && !sourceInRight2
                               && rightPanels.Count > 0)
                           || (sourceInRight2 && right2Panels.Count == 1 && right3Panels.Count == 0);

        // Suppress Left4/Right4 when empty AND Left3/Right3 is also empty,
        // or when source is the sole occupant of the adjacent inner zone (no-op move).
        bool suppressLeft4  = (left4Panels.Count == 0 && !sourceInLeft4
                           && (suppressLeft3 || (left3Panels.Count == 0 && !sourceInLeft3)))
                           || (sourceInLeft3  && left3Panels.Count  == 1 && left4Panels.Count  == 0);
        bool suppressRight4 = (right4Panels.Count == 0 && !sourceInRight4
                           && (suppressRight3 || (right3Panels.Count == 0 && !sourceInRight3)))
                           || (sourceInRight3 && right3Panels.Count == 1 && right4Panels.Count == 0);

        bool suppressLeft5  = (left5Panels.Count == 0 && !sourceInLeft5
                           && (suppressLeft4 || (left4Panels.Count == 0 && !sourceInLeft4)))
                           || (sourceInLeft4  && left4Panels.Count  == 1 && left5Panels.Count  == 0);
        bool suppressRight5 = (right5Panels.Count == 0 && !sourceInRight5
                           && (suppressRight4 || (right4Panels.Count == 0 && !sourceInRight4)))
                           || (sourceInRight4 && right4Panels.Count == 1 && right5Panels.Count == 0);

        bool suppressLeft6  = (left6Panels.Count == 0 && !sourceInLeft6
                           && (suppressLeft5 || (left5Panels.Count == 0 && !sourceInLeft5)))
                           || (sourceInLeft5  && left5Panels.Count  == 1 && left6Panels.Count  == 0);
        bool suppressRight6 = (right6Panels.Count == 0 && !sourceInRight6
                           && (suppressRight5 || (right5Panels.Count == 0 && !sourceInRight5)))
                           || (sourceInRight5 && right5Panels.Count == 1 && right6Panels.Count == 0);

        // When source is outside a side, use column-position slots; otherwise use panel-position slots
        if (!suppressLeft6)
            AddZoneSlots(result, sourcePanelId, left6Panels, sourceInLeft6, DockZone.Left6);

        if (!suppressLeft5)
            AddZoneSlots(result, sourcePanelId, left5Panels, sourceInLeft5, DockZone.Left5);

        if (!suppressLeft4)
            AddZoneSlots(result, sourcePanelId, left4Panels, sourceInLeft4, DockZone.Left4);

        if (!suppressLeft3)
            AddZoneSlots(result, sourcePanelId, left3Panels, sourceInLeft3, DockZone.Left3);

        if (!suppressLeft2)
            AddZoneSlots(result, sourcePanelId, left2Panels, sourceInLeft2, DockZone.Left2);

        if (!suppressLeft)
            AddZoneSlots(result, sourcePanelId, leftPanels, sourceInLeft, DockZone.Left);

        AddZoneSlots(result, sourcePanelId, topPanels, sourceInTop, DockZone.Top);

        if (!suppressRight)
            AddZoneSlots(result, sourcePanelId, rightPanels, sourceInRight, DockZone.Right);

        if (!suppressRight2)
            AddZoneSlots(result, sourcePanelId, right2Panels, sourceInRight2, DockZone.Right2);

        if (!suppressRight3)
            AddZoneSlots(result, sourcePanelId, right3Panels, sourceInRight3, DockZone.Right3);

        if (!suppressRight4)
            AddZoneSlots(result, sourcePanelId, right4Panels, sourceInRight4, DockZone.Right4);

        if (!suppressRight5)
            AddZoneSlots(result, sourcePanelId, right5Panels, sourceInRight5, DockZone.Right5);

        if (!suppressRight6)
            AddZoneSlots(result, sourcePanelId, right6Panels, sourceInRight6, DockZone.Right6);

        return result;
    }

    private static void AddZoneSlots(
        List<SlotButtonInfo> result,
        string sourcePanelId,
        List<string> zonePanels,
        bool sourceInZone,
        DockZone zone)
    {
        if (zonePanels.Count == 0 && !sourceInZone)
        {
            // Rule D: one destination slot at order=0
            result.Add(new SlotButtonInfo(zone, 0));
            return;
        }

        // Rule B (sourceInZone) or Rule C (!sourceInZone)
        for (int i = 0; i < zonePanels.Count; i++)
        {
            string pid = zonePanels[i];
            if (!Same(pid, sourcePanelId))
                result.Add(new SlotButtonInfo(zone, i));
        }

        if (!sourceInZone)
        {
            // Rule C: append-at-end insertion slot
            result.Add(new SlotButtonInfo(zone, zonePanels.Count));
        }
    }

    /// <summary>
    /// Adds column-position slots for an entire side when source is outside that side.
    /// Shows N+1 slots for N occupied columns.
    /// </summary>
    private static void AddSideColumnSlots(
        List<SlotButtonInfo> result,
        string sourcePanelId,
        List<string> innerPanels,
        List<string> middlePanels,
        List<string> outerPanels,
        bool suppressInner,
        bool suppressMiddle,
        bool suppressOuter,
        DockZone innerZone,
        DockZone middleZone,
        DockZone outerZone)
    {
        // Count occupied columns
        int occupiedCount = 0;
        bool innerOccupied = !suppressInner && innerPanels.Count > 0;
        bool middleOccupied = !suppressMiddle && middlePanels.Count > 0;
        bool outerOccupied = !suppressOuter && outerPanels.Count > 0;

        if (innerOccupied) occupiedCount++;
        if (middleOccupied) occupiedCount++;
        if (outerOccupied) occupiedCount++;

        // Always offer the innermost position
        if (!suppressInner)
            result.Add(new SlotButtonInfo(innerZone, -100)); // -100 = column-position marker

        // If inner is occupied or middle exists, offer middle position
        if (innerOccupied && !suppressMiddle)
            result.Add(new SlotButtonInfo(middleZone, -100));

        // If inner+middle are occupied or outer exists, offer outer position
        if ((innerOccupied && middleOccupied) || (innerOccupied && suppressMiddle) && !suppressOuter)
            result.Add(new SlotButtonInfo(outerZone, -100));
    }

    // ── Move logic ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a new <see cref="PanelLayoutData"/> with the source panel moved
    /// to the target zone at the target order.  Pure data mutation — no WPF.
    /// </summary>
    public static PanelLayoutData ApplyMove(
        string sourcePanelId,
        DockZone targetZone,
        int targetOrder,
        PanelLayoutData layout)
    {
        // Special case: targetOrder == -100 indicates column-position insertion with cross-zone shuffling.
        // This happens when dragging from outside a side to that side (e.g., Top → Left side).
        // The system shuffles existing panels outward to make room at the target column.
        if (targetOrder == -100)
        {
            return ApplyMoveWithColumnShuffle(sourcePanelId, targetZone, layout);
        }

        var existing = layout.Slots.FirstOrDefault(s =>
            string.Equals(s.PanelId, sourcePanelId, StringComparison.OrdinalIgnoreCase));

        bool sameZone = existing is not null && existing.Zone == targetZone;

        List<PanelSlot> newSlots;

        if (sameZone)
        {
            if (targetOrder < 0)
                return layout;

            var zoneSlots = layout.Slots
                .Where(s => s.Zone == targetZone)
                .OrderBy(s => s.Order)
                .Select(s => s.PanelId)
                .ToList();

            int currentIdx = zoneSlots.FindIndex(id =>
                string.Equals(id, sourcePanelId, StringComparison.OrdinalIgnoreCase));
            int clampedTarget = Math.Clamp(targetOrder, 0, zoneSlots.Count - 1);

            if (currentIdx == clampedTarget)
                return layout;

            zoneSlots.RemoveAt(currentIdx);
            zoneSlots.Insert(Math.Clamp(clampedTarget, 0, zoneSlots.Count), sourcePanelId);

            newSlots = layout.Slots
                .Where(s => s.Zone != targetZone)
                .Concat(zoneSlots.Select((id, i) => new PanelSlot(id, targetZone, i)))
                .ToList();
        }
        else
        {
            var slots = layout.Slots
                .Where(s => !string.Equals(s.PanelId, sourcePanelId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var targetZoneSlots = slots
                .Where(s => s.Zone == targetZone)
                .OrderBy(s => s.Order)
                .ToList();

            int insertAt = targetOrder < 0
                ? targetZoneSlots.Count
                : Math.Clamp(targetOrder, 0, targetZoneSlots.Count);

            var targetIds = targetZoneSlots.Select(s => s.PanelId).ToList();
            targetIds.Insert(insertAt, sourcePanelId);

            slots = slots.Where(s => s.Zone != targetZone).ToList();
            slots.AddRange(targetIds.Select((id, i) => new PanelSlot(id, targetZone, i)));
            newSlots = slots;
        }

        return new PanelLayoutData
        {
            Slots = newSlots,
            VisiblePanelIds = layout.VisiblePanelIds,
        };
    }

    /// <summary>
    /// Applies a move to a specific column position, shuffling existing panels outward to make room.
    /// Used when dragging from outside a side (e.g., Top → Left side) to insert at a column position.
    /// </summary>
    private static PanelLayoutData ApplyMoveWithColumnShuffle(
        string sourcePanelId,
        DockZone targetZone,
        PanelLayoutData layout)
    {
        // Determine which side the target zone belongs to
        DockZone[] sideZones;
        int targetColumnIndex; // 0=innermost, 1=middle, 2=outermost

        if (targetZone == DockZone.Left || targetZone == DockZone.Left2 || targetZone == DockZone.Left3 || targetZone == DockZone.Left4)
        {
            sideZones = new[] { DockZone.Left, DockZone.Left2, DockZone.Left3, DockZone.Left4 };
            targetColumnIndex = targetZone == DockZone.Left ? 0 : targetZone == DockZone.Left2 ? 1 : targetZone == DockZone.Left3 ? 2 : 3;
        }
        else if (targetZone == DockZone.Right || targetZone == DockZone.Right2 || targetZone == DockZone.Right3 || targetZone == DockZone.Right4)
        {
            sideZones = new[] { DockZone.Right, DockZone.Right2, DockZone.Right3, DockZone.Right4 };
            targetColumnIndex = targetZone == DockZone.Right ? 0 : targetZone == DockZone.Right2 ? 1 : targetZone == DockZone.Right3 ? 2 : 3;
        }
        else
        {
            // Not a left/right zone; fall back to normal move at position 0
            return ApplyMove(sourcePanelId, targetZone, 0, layout);
        }

        // Remove source panel from current location
        var slotsWithoutSource = layout.Slots
            .Where(s => !Same(s.PanelId, sourcePanelId))
            .ToList();

        // Collect panels currently in each column of this side
        var columnPanels = new List<string>[4];
        for (int i = 0; i < 4; i++)
        {
            columnPanels[i] = slotsWithoutSource
                .Where(s => s.Zone == sideZones[i])
                .OrderBy(s => s.Order)
                .Select(s => s.PanelId)
                .ToList();
        }

        // Shuffle panels outward from target column to make room.
        // We must shift from outermost to innermost to avoid cascading everything to the outermost column.
        // E.g., if dropping at column 0 (Left) when Left and Left2 are occupied:
        //   - Left2 panels → Left3
        //   - Left panels → Left2
        //   - Insert incoming at Left
        for (int i = 3; i > targetColumnIndex; i--)
        {
            // Move panels from column i-1 to column i
            columnPanels[i] = columnPanels[i - 1];
        }

        // Clear target column and insert incoming panel at position 0
        columnPanels[targetColumnIndex] = new List<string> { sourcePanelId };

        // Rebuild slots from other zones + the shuffled side columns
        var newSlots = slotsWithoutSource.Where(s => !sideZones.Contains(s.Zone)).ToList();
        for (int i = 0; i < 4; i++)
        {
            newSlots.AddRange(columnPanels[i].Select((pid, order) => new PanelSlot(pid, sideZones[i], order)));
        }

        return new PanelLayoutData
        {
            Slots = newSlots,
            VisiblePanelIds = layout.VisiblePanelIds,
        };
    }

    // ── Preview description ──────────────────────────────────────────────────

    /// <summary>
    /// Returns a human-readable description of where a panel would land if the
    /// user clicked the slot at (zone, targetOrder).
    /// Format: "Left 1, 2/3" (position / total slots after move).
    /// </summary>
    public static string GetNormalizedPreviewDescription(
        DockZone zone,
        int targetOrder,
        PanelLayoutData layout)
    {
        var zoneName = GetZoneDisplayName(zone);

        int visibleInZone = layout.Slots
            .Count(s => s.Zone == zone && layout.VisiblePanelIds.Contains(s.PanelId));

        int total    = visibleInZone + 1;
        
        // Special case: targetOrder == -100 indicates column-position insertion (shuffle mode).
        // In this case, the incoming panel will be inserted at position 1 within the target column.
        int position = targetOrder == -100 ? 1 : targetOrder + 1;

        return $"{zoneName}, {position}/{total}";
    }

    // ── N+1 Validation ───────────────────────────────────────────────────────

    /// <summary>
    /// Validates the N+1 rule on the built slots: for N occupied zones on a side, exactly N+1 drop-target slots are required.
    /// Returns a list of violation descriptions; empty list means the layout is valid.
    /// </summary>
    public static IReadOnlyList<string> ValidateN1RuleOnSlots(IReadOnlyList<SlotButtonInfo> slots, PanelLayoutData layout, string sourcePanelId)
    {
        var violations = new List<string>();
        CheckSideN1OnSlots(slots, layout, sourcePanelId, new[] { DockZone.Left, DockZone.Left2, DockZone.Left3, DockZone.Left4, DockZone.Left5, DockZone.Left6 }, "Left", violations);
        CheckSideN1OnSlots(slots, layout, sourcePanelId, new[] { DockZone.Right, DockZone.Right2, DockZone.Right3, DockZone.Right4, DockZone.Right5, DockZone.Right6 }, "Right", violations);
        return violations;
    }

    private static void CheckSideN1OnSlots(
        IReadOnlyList<SlotButtonInfo> slots,
        PanelLayoutData layout,
        string sourcePanelId,
        DockZone[] sideZones,
        string sideName,
        List<string> violations)
    {
        // Count occupied zones in the layout
        var occupiedZones = new HashSet<DockZone>();
        foreach (var zone in sideZones)
        {
            if (layout.Slots.Any(s => s.Zone == zone))
                occupiedZones.Add(zone);
        }

        if (occupiedZones.Count == 0)
            return;

        // Count distinct zones in the actual slots returned by BuildSlotButtons
        var slotsInZones = new HashSet<DockZone>();
        foreach (var slot in slots)
        {
            if (sideZones.Contains(slot.Zone))
                slotsInZones.Add(slot.Zone);
        }

        int occupiedCount = occupiedZones.Count;
        int expectedZones = occupiedCount + 1;
        int actualZones = slotsInZones.Count;

        if (actualZones < expectedZones)
        {
            var occupiedList = string.Join(", ", occupiedZones.OrderBy(z => Array.IndexOf(sideZones, z))
                .Select(GetZoneDisplayName));
            violations.Add(
                $"{sideName}: N+1 rule violated — {occupiedCount} occupied zone(s) ({occupiedList}) require {expectedZones} slot zone(s), got {actualZones}");
        }
    }

    // ── N+1 Validation (Legacy - works on layout only) ───────────────────────────────────────────────────────

    /// <summary>
    /// Validates the N+1 rule: for N occupied zones on a side, exactly N+1 drop-target slots are required.
    /// Returns a list of violation descriptions; empty list means the layout is valid.
    /// Each violation includes which zones are occupied and how many slots exist.
    /// </summary>
    public static IReadOnlyList<string> ValidateN1Rule(PanelLayoutData layout, string sourcePanelId)
    {
        var violations = new List<string>();

        // Check each side separately
        CheckSideN1(layout, sourcePanelId, new[] { DockZone.Left, DockZone.Left2, DockZone.Left3, DockZone.Left4, DockZone.Left5, DockZone.Left6 }, "Left", violations);
        CheckSideN1(layout, sourcePanelId, new[] { DockZone.Right, DockZone.Right2, DockZone.Right3, DockZone.Right4, DockZone.Right5, DockZone.Right6 }, "Right", violations);

        return violations;
    }

    private static void CheckSideN1(
        PanelLayoutData layout,
        string sourcePanelId,
        DockZone[] sideZones,
        string sideName,
        List<string> violations)
    {
        // Count occupied zones: zones that have at least one panel
        var occupiedZones = new HashSet<DockZone>();
        foreach (var zone in sideZones)
        {
            bool hasPanel = layout.Slots.Any(s => s.Zone == zone);
            if (hasPanel)
                occupiedZones.Add(zone);
        }

        if (occupiedZones.Count == 0)
            return; // No zones occupied, no N+1 rule applies

        int occupiedCount = occupiedZones.Count;
        int expectedZones = occupiedCount + 1;

        // Check that we have enough zone capacity to satisfy N+1
        // At minimum, we need at least one empty zone beyond the occupied zones
        // to allow insertion at the far end.
        var maxOccupiedIndex = occupiedZones.Max(z => Array.IndexOf(sideZones, z));
        int availableZonesAfterOccupied = sideZones.Length - maxOccupiedIndex - 1;

        if (availableZonesAfterOccupied < 1)
        {
            var occupiedList = string.Join(", ", occupiedZones.OrderBy(z => Array.IndexOf(sideZones, z))
                .Select(GetZoneDisplayName));
            violations.Add(
                $"{sideName}: N+1 rule violated — {occupiedCount} occupied zone(s) ({occupiedList}) extend to the edge; need at least one empty zone beyond for insertion");
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static List<string> PanelsInZone(PanelLayoutData layout, DockZone zone) =>
        layout.Slots
              .Where(s => s.Zone == zone)
              .OrderBy(s => s.Order)
              .Select(s => s.PanelId)
              .ToList();

    private static List<string> FilterZone(
        List<string> panels,
        string sourcePanelId,
        IReadOnlySet<string> visiblePanelIds) =>
        panels.Where(p => visiblePanelIds.Contains(p) || Same(p, sourcePanelId)).ToList();

    private static bool Same(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
