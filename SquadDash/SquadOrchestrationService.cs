using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SquadDash;

internal sealed class SquadOrchestrationService {
    private readonly ISquadCommandRunner _commandRunner;
    private readonly ISquadCliResolutionService _cliResolutionService;

    public SquadOrchestrationService()
        : this(
            new SquadInstallerServiceProcessRunner(),
            new SquadCliResolutionService(new SystemProcessRunner(), new SystemEnvironmentProvider())) {
    }

    internal SquadOrchestrationService(
        ISquadCommandRunner commandRunner,
        ISquadCliResolutionService cliResolutionService) {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        _cliResolutionService = cliResolutionService ?? throw new ArgumentNullException(nameof(cliResolutionService));
    }

    public async Task<SquadCommandResult> DiscoverAsync(string activeDirectory) {
        var candidate = await _cliResolutionService.ResolveAsync(activeDirectory).ConfigureAwait(false);
        if (!candidate.IsAvailable) {
            return new SquadCommandResult(
                false,
                null,
                string.Empty,
                candidate.ProbeError ?? string.Empty,
                candidate.UpdateInstruction.DisplayMessage ?? "Squad CLI is not available for this workspace.");
        }

        return await _commandRunner.RunAsync(
                candidate,
                activeDirectory,
                "Discover Squads",
                ["discover"])
            .ConfigureAwait(false);
    }

    public async Task<SquadCommandResult> DelegateAsync(string activeDirectory, string squadName, string description) {
        if (string.IsNullOrWhiteSpace(squadName)) {
            return new SquadCommandResult(
                false,
                null,
                string.Empty,
                string.Empty,
                "Squad name is required.");
        }

        if (string.IsNullOrWhiteSpace(description)) {
            return new SquadCommandResult(
                false,
                null,
                string.Empty,
                string.Empty,
                "Delegate description is required.");
        }

        var candidate = await _cliResolutionService.ResolveAsync(activeDirectory).ConfigureAwait(false);
        if (!candidate.IsAvailable) {
            return new SquadCommandResult(
                false,
                null,
                string.Empty,
                candidate.ProbeError ?? string.Empty,
                candidate.UpdateInstruction.DisplayMessage ?? "Squad CLI is not available for this workspace.");
        }

        return await _commandRunner.RunAsync(
                candidate,
                activeDirectory,
                "Delegate to Squad",
                ["delegate", squadName.Trim(), description.Trim()])
            .ConfigureAwait(false);
    }
}

internal static class SquadProcessEnvironment {
    public static string BuildMergedPathEnvironmentValue() {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new List<string>();

        foreach (var scope in new[] {
                     EnvironmentVariableTarget.Process,
                     EnvironmentVariableTarget.User,
                     EnvironmentVariableTarget.Machine
                 }) {
            var pathValue = Environment.GetEnvironmentVariable("PATH", scope);
            if (string.IsNullOrWhiteSpace(pathValue))
                continue;

            foreach (var rawDirectory in pathValue.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                var directory = rawDirectory.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    continue;

                var normalized = Path.GetFullPath(directory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (seen.Add(normalized))
                    directories.Add(normalized);
            }
        }

        return string.Join(Path.PathSeparator, directories);
    }
}
