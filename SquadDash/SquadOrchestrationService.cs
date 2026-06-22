using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SquadDash;

internal sealed class SquadOrchestrationService {
    private readonly ISquadCommandRunner _commandRunner;

    public SquadOrchestrationService()
        : this(new SquadInstallerServiceProcessRunner()) {
    }

    internal SquadOrchestrationService(ISquadCommandRunner commandRunner) {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public Task<SquadCommandResult> DiscoverAsync(string activeDirectory) {
        return _commandRunner.RunAsync(SquadCliCommands.DiscoverSquads, activeDirectory);
    }

    public Task<SquadCommandResult> DelegateAsync(string activeDirectory, string squadName, string description) {
        if (string.IsNullOrWhiteSpace(squadName)) {
            return Task.FromResult(new SquadCommandResult(
                false,
                null,
                string.Empty,
                string.Empty,
                "Squad name is required."));
        }

        if (string.IsNullOrWhiteSpace(description)) {
            return Task.FromResult(new SquadCommandResult(
                false,
                null,
                string.Empty,
                string.Empty,
                "Delegate description is required."));
        }

        return RunLocalCliAsync(
            activeDirectory,
            "Delegate to Squad",
            "delegate",
            squadName.Trim(),
            description.Trim());
    }

    private static async Task<SquadCommandResult> RunLocalCliAsync(
        string activeDirectory,
        string displayName,
        params string[] args) {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        try {
            var cliEntryPath = Path.Combine(activeDirectory, SquadCliCommands.LocalCliEntryPath);
            using var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "node",
                    WorkingDirectory = activeDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                },
                EnableRaisingEvents = true
            };

            process.StartInfo.ArgumentList.Add(cliEntryPath);
            foreach (var arg in args)
                process.StartInfo.ArgumentList.Add(arg);
            process.StartInfo.Environment["PATH"] = SquadProcessEnvironment.BuildMergedPathEnvironmentValue();

            var outputClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var errorClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            process.OutputDataReceived += (_, e) => {
                if (e.Data is null) {
                    outputClosed.TrySetResult(true);
                    return;
                }

                stdout.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (_, e) => {
                if (e.Data is null) {
                    errorClosed.TrySetResult(true);
                    return;
                }

                stderr.AppendLine(e.Data);
            };

            if (!process.Start()) {
                return new SquadCommandResult(
                    false,
                    null,
                    string.Empty,
                    string.Empty,
                    $"Failed to start {displayName}.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(outputClosed.Task, errorClosed.Task).ConfigureAwait(false);

            var success = process.ExitCode == 0;
            return new SquadCommandResult(
                success,
                process.ExitCode,
                stdout.ToString(),
                stderr.ToString(),
                success ? $"{displayName} completed." : $"{displayName} failed with exit code {process.ExitCode}.");
        }
        catch (Exception ex) {
            return new SquadCommandResult(
                false,
                null,
                stdout.ToString(),
                stderr.ToString(),
                $"Unable to launch {displayName}: {ex.Message}");
        }
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
