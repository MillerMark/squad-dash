using System.IO;
using System.Text;

namespace SquadDash;

internal sealed class SquadInstallationStateService {
    public SquadInstallationState GetState(string activeDirectory, ResolvedCliCandidate resolvedCandidate) {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeDirectory);
        ArgumentNullException.ThrowIfNull(resolvedCandidate);

        var normalizedDirectory = Path.GetFullPath(activeDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var layout = SquadWorkspaceLayoutResolver.Resolve(normalizedDirectory);
        var squadFolderPath = layout?.TeamSquadFolderPath ?? Path.Combine(normalizedDirectory, ".squad");
        var teamFilePath = layout?.TeamFilePath ?? Path.Combine(squadFolderPath, "team.md");
        var packageJsonPath = Path.Combine(normalizedDirectory, "package.json");
        var workspaceInitialized = File.Exists(teamFilePath);
        var hasPackageManifest = File.Exists(packageJsonPath);
        var resolvedCommandPath = resolvedCandidate.EntryScriptPath ?? resolvedCandidate.ExecutablePath;
        var hasResolvedCli = resolvedCandidate.IsAvailable && !string.IsNullOrWhiteSpace(resolvedCommandPath);

        return new SquadInstallationState(
            normalizedDirectory,
            squadFolderPath,
            teamFilePath,
            packageJsonPath,
            resolvedCommandPath ?? string.Empty,
            workspaceInitialized,
            hasPackageManifest,
            hasResolvedCli,
            workspaceInitialized && hasResolvedCli,
            layout?.ProjectSquadFolderPath,
            layout?.TeamSquadFolderPath,
            layout?.IsRemote ?? false,
            layout?.StateBackend,
            layout?.StateLocation,
            layout?.ProjectKey,
            layout?.ResolutionReason,
            resolvedCandidate.DetectedSource,
            resolvedCandidate.UpdateInstruction.DisplayMessage);
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
    bool IsSquadInstalledForActiveDirectory,
    string? ProjectSquadFolderPath = null,
    string? TeamSquadFolderPath = null,
    bool UsesRemoteTeamRoot = false,
    string? StateBackend = null,
    string? StateLocation = null,
    string? ProjectKey = null,
    string? SquadResolutionReason = null,
    CliInstallSource DetectedCliSource = CliInstallSource.Unknown,
    string? CliUpdateGuidance = null);

internal static class SquadInstallDiagnosticsFormatter {
    public static string Build(SquadInstallationState state) {
        var localSquadShimPath = Path.Combine(
            state.ActiveDirectory,
            "node_modules",
            ".bin",
            "squad.cmd");
        var localSquadCliEntryPath = ResolveWorkspaceRelativePath(
            state.ActiveDirectory,
            SquadCliCommands.LocalCliEntryPath);

        var builder = new StringBuilder();
        builder.AppendLine($"Workspace: {state.ActiveDirectory}");
        builder.AppendLine($".squad/team.md: {FoundOrMissing(File.Exists(state.TeamFilePath))}");
        builder.AppendLine($"package.json: {FoundOrMissing(File.Exists(state.PackageJsonPath))}");
        builder.AppendLine($"local Squad CLI shim: {FoundOrMissing(File.Exists(localSquadShimPath))}");
        builder.AppendLine($"local Squad CLI entry: {FoundOrMissing(File.Exists(localSquadCliEntryPath))}");
        builder.AppendLine($"resolved .squad path: {state.SquadFolderPath}");
        builder.AppendLine();
        builder.AppendLine("Paths");
        builder.AppendLine($"team.md: {state.TeamFilePath}");
        builder.AppendLine($"package.json: {state.PackageJsonPath}");
        builder.AppendLine($"local Squad CLI shim: {localSquadShimPath}");
        builder.AppendLine($"local Squad CLI entry: {localSquadCliEntryPath}");
        builder.AppendLine();
        builder.AppendLine("State");
        builder.AppendLine($"workspace initialized: {YesOrNo(state.IsWorkspaceInitialized)}");
        builder.AppendLine($"local CLI available: {YesOrNo(state.HasLocalCliCommand)}");
        builder.AppendLine($"SquadDash considers this folder installed: {YesOrNo(state.IsSquadInstalledForActiveDirectory)}");
        builder.AppendLine($"CLI source: {DescribeCliSource(state.DetectedCliSource)}");

        if (!string.IsNullOrWhiteSpace(state.SquadResolutionReason))
            builder.AppendLine($"resolution: {state.SquadResolutionReason}");
        if (!string.IsNullOrWhiteSpace(state.CliUpdateGuidance))
            builder.AppendLine($"update guidance: {state.CliUpdateGuidance}");
        if (state.ProjectSquadFolderPath is { Length: > 0 } &&
            !string.Equals(state.ProjectSquadFolderPath, state.SquadFolderPath, StringComparison.OrdinalIgnoreCase))
            builder.AppendLine($"project .squad path: {state.ProjectSquadFolderPath}");
        if (!string.IsNullOrWhiteSpace(state.StateLocation))
            builder.AppendLine($"state location: {state.StateLocation}");
        if (!string.IsNullOrWhiteSpace(state.StateBackend))
            builder.AppendLine($"state backend: {state.StateBackend}");
        if (!string.IsNullOrWhiteSpace(state.ProjectKey))
            builder.AppendLine($"project key: {state.ProjectKey}");

        return builder.ToString().TrimEnd();
    }

    private static string ResolveWorkspaceRelativePath(string workspacePath, string relativePath) =>
        Path.Combine(new[] { workspacePath }
            .Concat(relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToArray());

    private static string FoundOrMissing(bool exists) => exists ? "found" : "missing";

    private static string YesOrNo(bool value) => value ? "yes" : "no";

    private static string DescribeCliSource(CliInstallSource source) =>
        source switch {
            CliInstallSource.WorkspaceNpm => "workspace npm",
            CliInstallSource.GlobalNpm => "global npm",
            CliInstallSource.NativeWinget => "native (winget)",
            CliInstallSource.NativeOther => "native",
            CliInstallSource.Custom => "custom",
            _ => "unknown"
        };
}
