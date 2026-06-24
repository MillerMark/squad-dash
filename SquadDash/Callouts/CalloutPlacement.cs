namespace SquadDash;

/// <summary>
/// Preferred placement direction for a callout relative to its target.
/// The callout body appears on the named side; the tail points toward the target.
/// </summary>
public enum CalloutPlacement
{
    /// <summary>Automatically choose the best available position.</summary>
    Auto,
    /// <summary>Callout body above the target (tail points down, angle ~270).</summary>
    North,
    /// <summary>Callout body to the upper-right of the target (angle ~225).</summary>
    NorthEast,
    /// <summary>Callout body to the right of the target (tail points left, angle ~180).</summary>
    East,
    /// <summary>Callout body to the lower-right of the target (angle ~135).</summary>
    SouthEast,
    /// <summary>Callout body below the target (tail points up, angle ~90).</summary>
    South,
    /// <summary>Callout body to the lower-left of the target (angle ~45).</summary>
    SouthWest,
    /// <summary>Callout body to the left of the target (tail points right, angle ~0).</summary>
    West,
    /// <summary>Callout body to the upper-left of the target (angle ~315).</summary>
    NorthWest,
}
