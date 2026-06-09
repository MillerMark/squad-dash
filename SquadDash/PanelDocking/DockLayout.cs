#nullable enable

namespace SquadDash.PanelDocking;

/// <summary>
/// A named snapshot of panel placement that can be saved and restored per workspace.
/// </summary>
public sealed class DockLayout
{
    /// <summary>User-visible name for this layout (e.g. "Default", "Focus", "Review").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>One slot entry per dockable panel included in this layout.</summary>
    public List<PanelSlot> Slots { get; set; } = new();

    /// <summary>Saved width of the left dock zone column; null means use default.</summary>
    public double? LeftZoneWidth { get; set; }

    /// <summary>Saved width of the right dock zone column; null means use default.</summary>
    public double? RightZoneWidth { get; set; }

    /// <summary>Saved width of the outer-left (Left2) dock zone column; null means use default.</summary>
    public double? Left2ZoneWidth { get; set; }

    /// <summary>Saved width of the outer-right (Right2) dock zone column; null means use default.</summary>
    public double? Right2ZoneWidth { get; set; }

    /// <summary>Saved width of the outermost-left (Left3) dock zone column; null means use default.</summary>
    public double? Left3ZoneWidth { get; set; }

    /// <summary>Saved width of the outermost-right (Right3) dock zone column; null means use default.</summary>
    public double? Right3ZoneWidth { get; set; }

    /// <summary>Saved width of the outermost-left (Left4) dock zone column; null means use default.</summary>
    public double? Left4ZoneWidth { get; set; }

    /// <summary>Saved width of the outermost-right (Right4) dock zone column; null means use default.</summary>
    public double? Right4ZoneWidth { get; set; }

    /// <summary>Saved width of the outermost-left (Left5) dock zone column; null means use default.</summary>
    public double? Left5ZoneWidth { get; set; }

    /// <summary>Saved width of the outermost-right (Right5) dock zone column; null means use default.</summary>
    public double? Right5ZoneWidth { get; set; }

    /// <summary>Saved width of the outermost-left (Left6) dock zone column; null means use default.</summary>
    public double? Left6ZoneWidth { get; set; }

    /// <summary>Saved width of the outermost-right (Right6) dock zone column; null means use default.</summary>
    public double? Right6ZoneWidth { get; set; }

    /// <summary>Saved absolute pixel widths for each top-zone panel column (index = rank 0..5). Null means equal/auto distribution.</summary>
    public List<double>? TopZonePanelWidths { get; set; }

    /// <summary>
    /// Returns the canonical default layout: every dockable panel in the Top zone,
    /// ordered to match their current left-to-right position in the status strip.
    /// </summary>
    public static DockLayout CreateDefault() => new()
    {
        Name = "Default",
        Slots =
        [
            new PanelSlot("loop",        DockZone.Top, 0),
            new PanelSlot("tasks",       DockZone.Top, 1),
            new PanelSlot("approvals",   DockZone.Top, 2),
            new PanelSlot("inbox",       DockZone.Top, 3),
            new PanelSlot("maintenance", DockZone.Top, 4),
            new PanelSlot("notes",       DockZone.Top, 5),
            new PanelSlot("health",      DockZone.Top, 6),
            new PanelSlot("trace",       DockZone.Top, 7),
        ]
    };
}
