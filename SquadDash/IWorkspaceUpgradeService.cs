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
        string? activeCliVersion,
        string? latestAvailableVersion)
    {
        if (isPending && !string.IsNullOrWhiteSpace(recordedPendingVersion))
            return SelectNewerVersion(recordedPendingVersion, latestAvailableVersion);
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

    private static string SelectNewerVersion(string recordedVersion, string? latestVersion)
    {
        var recorded = recordedVersion.Trim();
        if (string.IsNullOrWhiteSpace(latestVersion))
            return recorded;
        var latest = latestVersion.Trim();
        return TryParseVersion(latest, out var latestParsed) &&
               TryParseVersion(recorded, out var recordedParsed) &&
               latestParsed > recordedParsed
            ? latest
            : recorded;
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        var normalized = value.Trim().TrimStart('v', 'V');
        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
            normalized = normalized[..suffixIndex];
        if (Version.TryParse(normalized, out var parsed) && parsed is not null)
        {
            version = parsed;
            return true;
        }

        version = new Version(0, 0);
        return false;
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

