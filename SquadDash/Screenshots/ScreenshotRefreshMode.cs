namespace SquadDash.Screenshots;

/// <summary>
/// Specifies the scope of a programmatic screenshot refresh run.
/// </summary>
public enum ScreenshotRefreshMode
{
    /// <summary>Normal interactive mode — no automated refresh.</summary>
    None,

    /// <summary>Refresh every definition in <see cref="ScreenshotDefinitionRegistry"/>.</summary>
    All,

    /// <summary>Refresh a single named definition supplied via <see cref="ScreenshotRefreshOptions.TargetName"/>.</summary>
    Named,
}
