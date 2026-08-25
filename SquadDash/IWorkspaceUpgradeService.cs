using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SquadDash;

internal interface IWorkspaceUpgradeService
{
    Task<WorkspaceUpgradeDryRunResult> DryRunAsync(ResolvedCliCandidate candidate, string workspaceDir);
    Task<WorkspaceUpgradeResult> UpgradeAsync(ResolvedCliCandidate candidate, string workspaceDir);
}

internal static class WorkspaceUpgradeVersionPolicy
{
    internal static string? ResolveDisplayVersion(
        bool isPending,
        string? recordedPendingVersion,
        string? activeCliVersion)
    {
        if (isPending && !string.IsNullOrWhiteSpace(recordedPendingVersion))
            return recordedPendingVersion.Trim();
        return string.IsNullOrWhiteSpace(activeCliVersion) ? null : activeCliVersion.Trim();
    }

    internal static string ResolveSimulationVersion(
        string? latestAvailableVersion,
        string? activeCliVersion) =>
        !string.IsNullOrWhiteSpace(latestAvailableVersion)
            ? latestAvailableVersion.Trim()
            : !string.IsNullOrWhiteSpace(activeCliVersion)
                ? activeCliVersion.Trim()
                : "simulated-v1.0";

    internal static bool MatchesActiveCandidate(string? targetVersion, string? candidateVersion)
    {
        if (string.IsNullOrWhiteSpace(targetVersion) || string.IsNullOrWhiteSpace(candidateVersion))
            return false;
        return string.Equals(
            targetVersion.Trim().TrimStart('v', 'V'),
            candidateVersion.Trim().TrimStart('v', 'V'),
            StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record WorkspaceUpgradeDryRunResult(
    IReadOnlyList<string> SafeAdditionsOrRefreshes,
    IReadOnlyList<string> TrackedLocalChangesAtRisk,
    IReadOnlyList<string> GeneratedFilesAtRisk)
{
    public static WorkspaceUpgradeDryRunResult Empty { get; } = new(
        [],
        [],
        []);

    public bool Success { get; init; } = true;

    public string? ErrorMessage { get; init; }

    public static WorkspaceUpgradeDryRunResult Failed(string? errorMessage) =>
        new([], [], []) {
            Success = false,
            ErrorMessage = errorMessage?.Trim()
        };
}

internal sealed record WorkspaceUpgradeResult(
    bool Success,
    bool IsPartial,
    string? ErrorMessage,
    IReadOnlyList<string> SafeAdditionsOrRefreshes,
    IReadOnlyList<string> TrackedLocalChangesAtRisk,
    IReadOnlyList<string> GeneratedFilesAtRisk);

