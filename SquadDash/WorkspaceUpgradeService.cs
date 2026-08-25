using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SquadDash;

internal sealed class WorkspaceUpgradeService : IWorkspaceUpgradeService
{
    private static readonly Regex DryRunFileActionRegex = new(
        @"^(create|overwrite)\s+(?<path>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GeneratedRiskLineRegex = new(
        @"^generated\s+(?<path>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TrackedRiskLineRegex = new(
        @"^(?<path>.+?)\s+(has tracked local changes|has local changes|would be overwritten)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ISquadCommandRunner _commandRunner;
    private readonly SquadInstallerService _installerService;
    private readonly ISquadCliResolutionService _cliResolutionService;

    public WorkspaceUpgradeService()
        : this(
            new SquadInstallerServiceProcessRunner(),
            new SquadInstallerService(),
            new SquadCliResolutionService(new SystemProcessRunner(), new SystemEnvironmentProvider()))
    {
    }

    internal WorkspaceUpgradeService(
        ISquadCommandRunner commandRunner,
        SquadInstallerService installerService,
        ISquadCliResolutionService cliResolutionService)
    {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        _installerService = installerService ?? throw new ArgumentNullException(nameof(installerService));
        _cliResolutionService = cliResolutionService ?? throw new ArgumentNullException(nameof(cliResolutionService));
    }

    public async Task<WorkspaceUpgradeDryRunResult> DryRunAsync(ResolvedCliCandidate candidate, string workspaceDir)
    {
        if (!candidate.IsAvailable)
            return WorkspaceUpgradeDryRunResult.Failed(BuildCliUnavailableMessage(candidate));

        var result = await _commandRunner
            .RunAsync(candidate, workspaceDir, "Dry Run Workspace Upgrade", SquadCliCommands.WorkspaceUpgradeDryRunArguments)
            .ConfigureAwait(false);

        var parsed = ParseDryRunOutput(result.StandardOutput, result.StandardError);
        if (!result.Success)
        {
            return parsed with
            {
                Success = false,
                ErrorMessage = BuildErrorMessage(result)
            };
        }

        return parsed;
    }

    public async Task<WorkspaceUpgradeResult> UpgradeAsync(ResolvedCliCandidate candidate, string workspaceDir)
    {
        if (!candidate.IsAvailable)
        {
            return new WorkspaceUpgradeResult(
                Success: false,
                IsPartial: false,
                ErrorMessage: BuildCliUnavailableMessage(candidate),
                SafeAdditionsOrRefreshes: [],
                TrackedLocalChangesAtRisk: [],
                GeneratedFilesAtRisk: []);
        }

        var upgradeCommandResult = await _commandRunner
            .RunAsync(candidate, workspaceDir, "Apply Workspace Upgrade", SquadCliCommands.WorkspaceUpgradeApplyArguments)
            .ConfigureAwait(false);
        var parsed = ParseDryRunOutput(upgradeCommandResult.StandardOutput, upgradeCommandResult.StandardError);

        if (!upgradeCommandResult.Success)
        {
            return new WorkspaceUpgradeResult(
                Success: false,
                IsPartial: HasAnyEntries(parsed) || LooksLikePartialFailure(upgradeCommandResult),
                ErrorMessage: BuildErrorMessage(upgradeCommandResult),
                SafeAdditionsOrRefreshes: parsed.SafeAdditionsOrRefreshes,
                TrackedLocalChangesAtRisk: parsed.TrackedLocalChangesAtRisk,
                GeneratedFilesAtRisk: parsed.GeneratedFilesAtRisk);
        }

        var doctorResult = await _installerService
            .RunDoctorAsync(workspaceDir)
            .ConfigureAwait(false);

        SquadInstallerService.EnsureSquadDashUniverseFiles(workspaceDir);

        var resolvedCandidate = await _cliResolutionService
            .ResolveAsync(workspaceDir)
            .ConfigureAwait(false);

        if (!doctorResult.Success)
        {
            return new WorkspaceUpgradeResult(
                Success: false,
                IsPartial: true,
                ErrorMessage: $"Workspace upgrade completed, but post-upgrade validation failed: {doctorResult.Message}",
                SafeAdditionsOrRefreshes: parsed.SafeAdditionsOrRefreshes,
                TrackedLocalChangesAtRisk: parsed.TrackedLocalChangesAtRisk,
                GeneratedFilesAtRisk: parsed.GeneratedFilesAtRisk);
        }

        if (!resolvedCandidate.IsAvailable)
        {
            return new WorkspaceUpgradeResult(
                Success: false,
                IsPartial: true,
                ErrorMessage: BuildCliUnavailableMessage(resolvedCandidate),
                SafeAdditionsOrRefreshes: parsed.SafeAdditionsOrRefreshes,
                TrackedLocalChangesAtRisk: parsed.TrackedLocalChangesAtRisk,
                GeneratedFilesAtRisk: parsed.GeneratedFilesAtRisk);
        }

        return new WorkspaceUpgradeResult(
            Success: true,
            IsPartial: false,
            ErrorMessage: null,
            SafeAdditionsOrRefreshes: parsed.SafeAdditionsOrRefreshes,
            TrackedLocalChangesAtRisk: parsed.TrackedLocalChangesAtRisk,
            GeneratedFilesAtRisk: parsed.GeneratedFilesAtRisk);
    }

    internal static WorkspaceUpgradeDryRunResult ParseDryRunOutput(params string?[] outputs)
    {
        var safe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var generated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawOutput in outputs)
        {
            if (string.IsNullOrWhiteSpace(rawOutput))
                continue;

            var lines = rawOutput
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var line in lines)
            {
                TryClassifyLine(line, safe, tracked, generated);
            }
        }

        return new WorkspaceUpgradeDryRunResult(
            SafeAdditionsOrRefreshes: safe.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            TrackedLocalChangesAtRisk: tracked.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            GeneratedFilesAtRisk: generated.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void TryClassifyLine(
        string line,
        ISet<string> safe,
        ISet<string> tracked,
        ISet<string> generated)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        var trimmed = line.Trim();
        var actionMatch = DryRunFileActionRegex.Match(trimmed);
        if (actionMatch.Success)
        {
            safe.Add(NormalizePath(actionMatch.Groups["path"].Value));
            return;
        }

        if (trimmed.Contains("squad.agent.md has local customizations", StringComparison.OrdinalIgnoreCase))
        {
            tracked.Add(".github\\agents\\squad.agent.md");
            return;
        }

        var trackedMatch = TrackedRiskLineRegex.Match(trimmed);
        if (trackedMatch.Success)
        {
            tracked.Add(NormalizePath(trackedMatch.Groups["path"].Value));
            return;
        }

        var generatedMatch = GeneratedRiskLineRegex.Match(trimmed);
        if (generatedMatch.Success)
        {
            generated.Add(NormalizePath(generatedMatch.Groups["path"].Value));
            return;
        }

        if (trimmed.Contains("squad.agent.md is unmodified — would refresh with latest template", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("squad.agent.md does not exist — would create from template", StringComparison.OrdinalIgnoreCase))
        {
            safe.Add(".github\\agents\\squad.agent.md");
            return;
        }

        if (!trimmed.Contains("generated", StringComparison.OrdinalIgnoreCase))
            return;

        if (!trimmed.Contains("overwrite", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.Contains("destructive", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.Contains("risk", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.Contains("affected", StringComparison.OrdinalIgnoreCase))
            return;

        var separatorIndex = trimmed.IndexOf(':');
        if (separatorIndex > 0 && separatorIndex < trimmed.Length - 1)
        {
            generated.Add(NormalizePath(trimmed[(separatorIndex + 1)..]));
        }
    }

    private static bool HasAnyEntries(WorkspaceUpgradeDryRunResult result)
    {
        return result.SafeAdditionsOrRefreshes.Count > 0 ||
               result.TrackedLocalChangesAtRisk.Count > 0 ||
               result.GeneratedFilesAtRisk.Count > 0;
    }

    private static bool LooksLikePartialFailure(SquadCommandResult result)
    {
        var combined = string.Join(
            Environment.NewLine,
            new[] { result.StandardOutput, result.StandardError }
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        return combined.Contains("partial", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("some files", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("already applied", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCliUnavailableMessage(ResolvedCliCandidate candidate)
    {
        return candidate.UpdateInstruction.DisplayMessage ??
               candidate.ProbeError ??
               "Squad CLI is not available for this workspace.";
    }

    private static string BuildErrorMessage(SquadCommandResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
            return result.Message.Trim();
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            return result.StandardError.Trim();
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            return result.StandardOutput.Trim();
        return "Workspace upgrade command failed.";
    }

    private static string NormalizePath(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Replace('/', '\\');
    }
}
