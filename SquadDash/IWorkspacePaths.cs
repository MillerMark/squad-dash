namespace SquadDash;

/// <summary>
/// Provides resolved file-system paths for the SquadDash application installation.
/// </summary>
/// <remarks>
/// Replaces the mutable-static <see cref="WorkspacePaths"/> service-locator.
/// Inject this interface into constructors instead of reading static properties directly.
/// The canonical implementation is <see cref="WorkspacePathsProvider"/>.
/// </remarks>
internal interface IWorkspacePaths {
    /// <summary>
    /// The application root directory — the folder that contains both
    /// <c>SquadDash\</c> and <c>Squad.SDK\</c>.
    /// </summary>
    string ApplicationRoot { get; }

    /// <summary>
    /// Full path to the <c>Squad.SDK\</c> directory under <see cref="ApplicationRoot"/>.
    /// </summary>
    string SquadSdkDirectory { get; }

    /// <summary>
    /// Full path to the <c>Run\</c> directory under <see cref="ApplicationRoot"/>.
    /// This is where A/B runtime slot directories live.
    /// </summary>
    string RunRootDirectory { get; }

    /// <summary>
    /// Full path to the bundled agent image assets directory.
    /// Derived from <see cref="System.AppContext.BaseDirectory"/>, not
    /// <see cref="ApplicationRoot"/>, because assets are deployed alongside the binary.
    /// </summary>
    string AgentImageAssetsDirectory { get; }

    /// <summary>
    /// Full path to the bundled role icon assets directory (<c>Assets\Roles\</c>).
    /// Used as the fallback image source when no per-agent PNG exists.
    /// </summary>
    string RoleIconAssetsDirectory { get; }

    /// <summary>
    /// Full path to the <c>docs/screenshots</c> directory under <see cref="ApplicationRoot"/>.
    /// Screenshot sub-directories (e.g. <c>baseline/</c>) live beneath this path.
    /// </summary>
    string ScreenshotsDirectory { get; }
}
