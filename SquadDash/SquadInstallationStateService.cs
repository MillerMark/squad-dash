using System.IO;

namespace SquadDash;

internal sealed class SquadInstallationStateService {
    public SquadInstallationState GetState(string activeDirectory) {
        var normalizedDirectory = Path.GetFullPath(activeDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var layout = SquadWorkspaceLayoutResolver.Resolve(normalizedDirectory);
        var squadFolderPath = layout?.TeamSquadFolderPath ?? Path.Combine(normalizedDirectory, ".squad");
        var teamFilePath = layout?.TeamFilePath ?? Path.Combine(squadFolderPath, "team.md");
        var packageJsonPath = Path.Combine(normalizedDirectory, "package.json");
        var localSquadShimPath = Path.Combine(normalizedDirectory, "node_modules", ".bin", "squad.cmd");
        var localSquadCliEntryPath = ResolveWorkspaceRelativePath(normalizedDirectory, SquadCliCommands.LocalCliEntryPath);
        var localSquadCommandPath = File.Exists(localSquadShimPath)
            ? localSquadShimPath
            : localSquadCliEntryPath;
        var workspaceInitialized = File.Exists(teamFilePath);
        var hasPackageManifest = File.Exists(packageJsonPath);
        var hasLocalCli = File.Exists(localSquadShimPath) || File.Exists(localSquadCliEntryPath);

        return new SquadInstallationState(
            normalizedDirectory,
            squadFolderPath,
            teamFilePath,
            packageJsonPath,
            localSquadCommandPath,
            workspaceInitialized,
            hasPackageManifest,
            hasLocalCli,
            workspaceInitialized && hasLocalCli,
            layout?.ProjectSquadFolderPath,
            layout?.TeamSquadFolderPath,
            layout?.IsRemote ?? false,
            layout?.StateBackend,
            layout?.StateLocation,
            layout?.ProjectKey,
            layout?.ResolutionReason);
    }

    private static string ResolveWorkspaceRelativePath(string workspacePath, string relativePath) =>
        Path.Combine(new[] { workspacePath }
            .Concat(relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToArray());
}

internal sealed record SquadInstallationState(
    string ActiveDirectory,
    string SquadFolderPath,
    string TeamFilePath,
    string PackageJsonPath,
    string LocalSquadCommandPath,
    bool IsWorkspaceInitialized,
    bool HasPackageManifest,
    bool HasLocalCliCommand,
    bool IsSquadInstalledForActiveDirectory,
    string? ProjectSquadFolderPath = null,
    string? TeamSquadFolderPath = null,
    bool UsesRemoteTeamRoot = false,
    string? StateBackend = null,
    string? StateLocation = null,
    string? ProjectKey = null,
    string? SquadResolutionReason = null);
