using System;
using System.IO;

namespace SquadDash;

/// <summary>
/// Application-wide environment flags resolved once at startup.
/// </summary>
/// <remarks>
/// Call <see cref="Initialize"/> from <c>App.xaml.cs</c> immediately after
/// the application root is determined. All other code can read the static
/// properties without any injection.
/// </remarks>
internal static class SquadDashEnvironment {
    private static bool _initialized;

    /// <summary>
    /// <c>true</c> when the application is running from a developer source tree —
    /// detected by the presence of <c>squad-dash.slnx</c> in the application root.
    /// </summary>
    public static bool IsDeveloperMode { get; private set; }

    /// <summary>
    /// Initializes environment flags. Must be called exactly once, as early as
    /// possible in <c>App.OnStartup</c>.
    /// </summary>
    /// <param name="applicationRoot">
    /// The resolved application root directory (from <see cref="WorkspacePathsProvider"/>).
    /// </param>
    public static void Initialize(string applicationRoot) {
        if (_initialized)
            throw new InvalidOperationException("SquadDashEnvironment.Initialize called more than once.");

        IsDeveloperMode = File.Exists(Path.Combine(applicationRoot, "squad-dash.slnx"));
        _initialized    = true;
    }
}
