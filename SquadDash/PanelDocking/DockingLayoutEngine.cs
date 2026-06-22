#nullable enable

namespace SquadDash.PanelDocking;

/// <summary>
/// Pure static logic for docking layout — no WPF dependencies.
/// Replicates the zone filtering and suppression logic of DockingMapBuilder
/// and the move logic of PanelDockingService.MovePanel.
/// </summary>
internal static class DockingLayoutEngine
{
    private static readonly DockZone[] LeftSideZones = BuildSideZones("Left");
    private static readonly DockZone[] RightSideZones = BuildSideZones("Right");

    // ── Zone name helpers ────────────────────────────────────────────────────

    public static string GetZoneDisplayName(DockZone zone) => zone switch
    {
        DockZone.Top => "Top",
        _ when IsLeftSideZone(zone) => $"Left {ZoneTier(zone) + 1}",
        _ when IsRightSideZone(zone) => $"Right {ZoneTier(zone) + 1}",
        _ => zone.ToString()
    };

    /// <summary>
    /// Parses a zone display name (e.g. "Left 3") into the corresponding <see cref="DockZone"/>.
    /// Returns <see cref="DockZone.Top"/> for unrecognised values.
    /// </summary>
    public static DockZone ParseZoneDisplayName(string displayName)
    {
        if (string.Equals(displayName, "Top", StringComparison.OrdinalIgnoreCase))
            return DockZone.Top;

        if (TryParseSideDisplayName(displayName, "Left", LeftSideZones, out var leftZone))
            return leftZone;

        if (TryParseSideDisplayName(displayName, "Right", RightSideZones, out var rightZone))
            return rightZone;

        return DockZone.Top;
    }

    public static string GetZoneFileTag(DockZone zone) => zone switch
    {
        DockZone.Top => "Top",
        _ when IsLeftSideZone(zone) => $"Left{ZoneTier(zone) + 1}",
        _ when IsRightSideZone(zone) => $"Right{ZoneTier(zone) + 1}",
        _ => zone.ToString()
    };

    // ── JSON serialization helpers ───────────────────────────────────────────

    public static Dictionary<string, List<string>> LayoutToJson(PanelLayoutData layout)
    {
        var result = new Dictionary<string, List<string>>();
        // When the layout carries visibility information, only serialize panels the user
        // can actually see.  Health/Trace and other always-hidden panels live in Slots
        // (they have a zone assignment) but must not pollute test-case snapshots.
        bool hasVisibility = layout.VisiblePanelIds.Count > 0;
        foreach (var zone in EnumerateSerializableZones())
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
        var slots = new List<PanelSlot>();
        var allPanelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (zoneName, panelIds) in json)
        {
            var zone = ParseZoneDisplayName(zoneName);
            if (zone == DockZone.Top && !string.Equals(zoneName, "Top", StringComparison.OrdinalIgnoreCase))
                continue;

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

        var topPanels = FilterZone(PanelsInZone(layout, DockZone.Top), sourcePanelId, layout.VisiblePanelIds);
        bool sourceInTop = topPanels.Any(p => Same(p, sourcePanelId));

        var leftStates = BuildSideStates(layout, sourcePanelId, LeftSideZones);
        var rightStates = BuildSideStates(layout, sourcePanelId, RightSideZones);
        ApplySideSuppression(leftStates);
        ApplySideSuppression(rightStates);

        AddSideSlots(result, sourcePanelId, leftStates, isLeft: true);
        AddZoneSlots(result, sourcePanelId, topPanels, sourceInTop, DockZone.Top);
        AddSideSlots(result, sourcePanelId, rightStates, isLeft: false);

        return result;
    }

    private sealed class SideSlotState
    {
        public required DockZone Zone { get; init; }
        public required List<string> Panels { get; init; }
        public required bool SourceInZone { get; init; }
        public bool Suppressed { get; set; }
        public bool EmptyWithoutSource => Panels.Count == 0 && !SourceInZone;
    }

    private static List<SideSlotState> BuildSideStates(
        PanelLayoutData layout,
        string sourcePanelId,
        IReadOnlyList<DockZone> sideZones) =>
        sideZones
            .Select(zone =>
            {
                var panels = FilterZone(PanelsInZone(layout, zone), sourcePanelId, layout.VisiblePanelIds);
                return new SideSlotState
                {
                    Zone = zone,
                    Panels = panels,
                    SourceInZone = panels.Any(p => Same(p, sourcePanelId)),
                };
            })
            .ToList();

    private static void ApplySideSuppression(List<SideSlotState> states)
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

    private static void AddSideSlots(
        List<SlotButtonInfo> result,
        string sourcePanelId,
        IReadOnlyList<SideSlotState> states,
        bool isLeft)
    {
        var visualOrder = isLeft ? states.Reverse() : states;
        foreach (var state in visualOrder.Where(s => !s.Suppressed))
            AddZoneSlots(result, sourcePanelId, state.Panels, state.SourceInZone, state.Zone);
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

        if (IsLeftSideZone(targetZone))
        {
            sideZones = LeftSideZones;
            targetColumnIndex = Array.IndexOf(sideZones, targetZone);
        }
        else if (IsRightSideZone(targetZone))
        {
            sideZones = RightSideZones;
            targetColumnIndex = Array.IndexOf(sideZones, targetZone);
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
        var columnPanels = new List<string>[sideZones.Length];
        for (int i = 0; i < sideZones.Length; i++)
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
        for (int i = sideZones.Length - 1; i > targetColumnIndex; i--)
        {
            // Move panels from column i-1 to column i
            columnPanels[i] = columnPanels[i - 1];
        }

        // Clear target column and insert incoming panel at position 0
        columnPanels[targetColumnIndex] = new List<string> { sourcePanelId };

        // Rebuild slots from other zones + the shuffled side columns
        var newSlots = slotsWithoutSource.Where(s => !sideZones.Contains(s.Zone)).ToList();
        for (int i = 0; i < sideZones.Length; i++)
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
        CheckSideN1OnSlots(slots, layout, sourcePanelId, LeftSideZones, "Left", violations);
        CheckSideN1OnSlots(slots, layout, sourcePanelId, RightSideZones, "Right", violations);
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
        CheckSideN1(layout, sourcePanelId, LeftSideZones, "Left", violations);
        CheckSideN1(layout, sourcePanelId, RightSideZones, "Right", violations);

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

    private static IEnumerable<DockZone> EnumerateSerializableZones() =>
        new[] { DockZone.Top }.Concat(LeftSideZones).Concat(RightSideZones);

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

    private static bool TryParseSideDisplayName(
        string displayName,
        string sideName,
        IReadOnlyList<DockZone> zones,
        out DockZone zone)
    {
        zone = DockZone.Top;
        if (!displayName.StartsWith(sideName + " ", StringComparison.OrdinalIgnoreCase))
            return false;

        string suffix = displayName[(sideName.Length + 1)..];
        if (!int.TryParse(suffix, out int tier) || tier < 1 || tier > zones.Count)
            return false;

        zone = zones[tier - 1];
        return true;
    }
}
