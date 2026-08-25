using System.Collections.Generic;
using System.Threading.Tasks;

namespace SquadDash;

internal interface IWorkspaceUpgradeService
{
    Task<WorkspaceUpgradeDryRunResult> DryRunAsync(ResolvedCliCandidate candidate, string workspaceDir);
    Task<WorkspaceUpgradeResult> UpgradeAsync(ResolvedCliCandidate candidate, string workspaceDir);
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
}

internal sealed record WorkspaceUpgradeResult(
    bool Success,
    bool IsPartial,
    string? ErrorMessage,
    IReadOnlyList<string> SafeAdditionsOrRefreshes,
    IReadOnlyList<string> TrackedLocalChangesAtRisk,
    IReadOnlyList<string> GeneratedFilesAtRisk);

