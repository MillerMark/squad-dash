namespace SquadDash.Screenshots;

/// <summary>
/// Carries the command-line options that control a programmatic screenshot refresh run.
/// Resolved from the <c>--refresh-screenshots [name]</c> launch argument by
/// <see cref="SquadDash.App"/> and forwarded to <see cref="ScreenshotRefreshRunner"/>.
/// </summary>
/// <param name="Mode">
///   Whether to refresh all definitions, a single named definition, or skip (normal interactive mode).
/// </param>
/// <param name="TargetName">
///   The kebab-case name of the definition to refresh.
///   Only meaningful when <paramref name="Mode"/> is <see cref="ScreenshotRefreshMode.Named"/>.
/// </param>
public record ScreenshotRefreshOptions(
    ScreenshotRefreshMode Mode,
    string?               TargetName)
{
    /// <summary>Singleton representing normal interactive startup — no refresh.</summary>
    public static readonly ScreenshotRefreshOptions None = new(ScreenshotRefreshMode.None, null);
}
