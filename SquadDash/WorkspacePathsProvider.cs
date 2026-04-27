using System;
using System.IO;

namespace SquadDash;

/// <summary>
/// Immutable implementation of <see cref="IWorkspacePaths"/>.
/// Construct once at application startup and inject via constructors.
/// </summary>
/// <remarks>
/// Migration path from <see cref="WorkspacePaths"/> (mutable static):
/// <list type="number">
///   <item><description>
///     In <c>App.xaml.cs</c>: replace <c>WorkspacePaths.Initialize(root)</c>
///     with <c>var paths = new WorkspacePathsProvider(root ?? WorkspacePaths.ApplicationRoot);</c>
///     and pass <c>paths</c> to <c>MainWindow</c>.
///   </description></item>
///   <item><description>
///     In <c>SquadDashLauncher/Program.cs</c>: replace <c>WorkspacePaths.Initialize(null)</c>
///     with <c>var paths = WorkspacePathsProvider.Discover();</c>
///   </description></item>
///   <item><description>
///     Replace all static <c>WorkspacePaths.*</c> reads with injected <c>IWorkspacePaths.*</c>
///     reads in: <c>MainWindow</c>, <c>AgentInfoWindow</c>, <c>WorkspaceIssuePresentation</c>,
///     <c>SquadCliAdapter</c>, <c>SquadSdkProcess</c>, <c>SquadDashRuntimeStamp</c>,
///     <c>PromptExecutionController</c>.
///   </description></item>
///   <item><description>
///     Once all call sites are migrated, delete <c>WorkspacePaths.cs</c>.
///   </description></item>
/// </list>
/// </remarks>
internal sealed class WorkspacePathsProvider : IWorkspacePaths {
    public WorkspacePathsProvider(string applicationRoot) {
        if (string.IsNullOrWhiteSpace(applicationRoot))
            throw new ArgumentException("Application root cannot be empty.", nameof(applicationRoot));

        ApplicationRoot = Path.GetFullPath(applicationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Discovers the application root by walking up the directory tree from
    /// <see cref="AppContext.BaseDirectory"/> until a folder containing both
    /// <c>SquadDash\</c> and <c>Squad.SDK\</c> is found.
    /// Use this overload in the launcher where the root is not passed as an argument.
    /// </summary>
    public static WorkspacePathsProvider Discover() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null) {
            if (Directory.Exists(Path.Combine(directory.FullName, "SquadDash")) &&
                Directory.Exists(Path.Combine(directory.FullName, "Squad.SDK"))) {
                return new WorkspacePathsProvider(directory.FullName);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the application root containing SquadDash and Squad.SDK.");
    }

    /// <inheritdoc/>
    public string ApplicationRoot { get; }

    /// <inheritdoc/>
    public string SquadSdkDirectory =>
        Path.Combine(ApplicationRoot, "Squad.SDK");

    /// <inheritdoc/>
    public string RunRootDirectory =>
        Path.Combine(ApplicationRoot, "Run");

    /// <inheritdoc/>
    public string AgentImageAssetsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "agents");

    /// <inheritdoc/>
    public string RoleIconAssetsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "Roles");

    /// <inheritdoc/>
    public string ScreenshotsDirectory =>
        Path.Combine(ApplicationRoot, "docs", "screenshots");
}
