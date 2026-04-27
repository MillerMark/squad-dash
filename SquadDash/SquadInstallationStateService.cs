using System.IO;

namespace SquadDash;

internal sealed class SquadInstallationStateService {
    public SquadInstallationState GetState(string activeDirectory) {
        var normalizedDirectory = Path.GetFullPath(activeDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var squadFolderPath = Path.Combine(normalizedDirectory, ".squad");
        var teamFilePath = Path.Combine(squadFolderPath, "team.md");
        var packageJsonPath = Path.Combine(normalizedDirectory, "package.json");
        var localSquadCommandPath = Path.Combine(normalizedDirectory, "node_modules", ".bin", "squad.cmd");
        var workspaceInitialized = File.Exists(teamFilePath);
        var hasPackageManifest = File.Exists(packageJsonPath);
        var hasLocalCli = File.Exists(localSquadCommandPath);

        return new SquadInstallationState(
            normalizedDirectory,
            squadFolderPath,
            teamFilePath,
            packageJsonPath,
            localSquadCommandPath,
            workspaceInitialized,
            hasPackageManifest,
            hasLocalCli,
            workspaceInitialized && hasLocalCli);
    }
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
    bool IsSquadInstalledForActiveDirectory);
