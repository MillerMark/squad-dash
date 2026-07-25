namespace SquadDash;

/// <summary>
/// Indicates whether a data item is shared (version-controlled, visible to all team members)
/// or local (machine-specific, stored in AppData).
/// </summary>
public enum DataScope
{
    /// <summary>Stored in the workspace .squad/ folder; committed to version control.</summary>
    Shared,

    /// <summary>Stored in AppData; machine-local, not committed.</summary>
    Local
}
