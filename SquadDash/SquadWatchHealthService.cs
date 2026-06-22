using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SquadDash;

internal sealed class SquadWatchHealthService {
    private readonly ISquadCommandRunner _commandRunner;

    public SquadWatchHealthService()
        : this(new WatchHealthCommandRunner()) {
    }

    internal SquadWatchHealthService(ISquadCommandRunner commandRunner) {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public async Task<SquadWatchHealthResult> GetHealthAsync(string activeDirectory) {
        var result = await _commandRunner
            .RunAsync(SquadCliCommands.WatchHealth, activeDirectory)
            .ConfigureAwait(false);

        return SquadWatchHealthResult.FromCommandResult(result);
    }

    private sealed class WatchHealthCommandRunner : ISquadCommandRunner {
        public Task<SquadCommandResult> RunAsync(SquadCliCommandDefinition command, string activeDirectory) {
            return new SquadInstallerServiceProcessRunner().RunAsync(command, activeDirectory);
        }
    }
}

internal sealed record SquadWatchHealthResult(
    bool Success,
    bool IsRunning,
    string Summary,
    IReadOnlyList<string> Lines,
    string? ErrorText = null) {

    public static SquadWatchHealthResult Checking { get; } =
        new(true, false, "Checking watch health...", ["Checking watch health..."]);

    public static SquadWatchHealthResult FromCommandResult(SquadCommandResult result) {
        var combined = string.Join(
            Environment.NewLine,
            new[] { result.StandardOutput, result.StandardError }
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        var lines = combined
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        if (LooksLikeUnsupportedWatchHealth(combined)) {
            return new SquadWatchHealthResult(
                false,
                false,
                "Watch health is unavailable with this Squad CLI.",
                ["Watch health requires Squad CLI 0.10.0 or newer."],
                combined);
        }

        if (LooksLikeMissingLocalCli(combined)) {
            return new SquadWatchHealthResult(
                false,
                false,
                "Local Squad CLI is not installed for this workspace.",
                ["Install or upgrade Squad for this workspace, then try Watch Health again."],
                combined);
        }

        if (lines.Length == 0) {
            return new SquadWatchHealthResult(
                result.Success,
                false,
                result.Success ? "Watch health returned no output." : result.Message,
                [result.Message],
                result.Success ? null : combined);
        }

        var isRunning = lines.Any(line =>
            line.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Watch is running", StringComparison.OrdinalIgnoreCase));
        var summary = lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? result.Message;

        if (lines.Any(line => line.Contains("No watch instance detected", StringComparison.OrdinalIgnoreCase)))
            summary = "No watch instance detected.";
        else if (lines.Any(line => line.Contains("stale", StringComparison.OrdinalIgnoreCase)))
            summary = "Stale watch state detected.";
        else if (lines.Any(line => line.Contains("corrupt", StringComparison.OrdinalIgnoreCase) ||
                                  line.Contains("invalid pid", StringComparison.OrdinalIgnoreCase)))
            summary = "Watch state file is invalid.";

        return new SquadWatchHealthResult(
            result.Success,
            isRunning,
            summary,
            lines,
            result.Success ? null : combined);
    }

    private static bool LooksLikeUnsupportedWatchHealth(string text) {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("unknown option", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("unknown command", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("unrecognized option", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeMissingLocalCli(string text) {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("Cannot find module", StringComparison.OrdinalIgnoreCase) &&
               text.Contains("@bradygaster", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class SquadInstallerServiceProcessRunner : ISquadCommandRunner {
    public async Task<SquadCommandResult> RunAsync(
        SquadCliCommandDefinition command,
        string activeDirectory) {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        try {
            using var process = new System.Diagnostics.Process {
                StartInfo = new System.Diagnostics.ProcessStartInfo {
                    FileName = command.FileName,
                    Arguments = command.Arguments,
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
                    $"Failed to start {command.DisplayName}.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(outputClosed.Task, errorClosed.Task).ConfigureAwait(false);

            var success = process.ExitCode == 0;
            var message = success
                ? $"{command.DisplayName} completed."
                : $"{command.DisplayName} failed with exit code {process.ExitCode}.";

            return new SquadCommandResult(
                success,
                process.ExitCode,
                stdout.ToString(),
                stderr.ToString(),
                message);
        }
        catch (Exception ex) {
            return new SquadCommandResult(
                false,
                null,
                stdout.ToString(),
                stderr.ToString(),
                $"Unable to launch {command.DisplayName}: {ex.Message}");
        }
    }
}
