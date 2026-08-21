using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SquadDash;

internal sealed class SquadWatchHealthService {
    private readonly ISquadCommandRunner _commandRunner;
    private readonly ISquadCliResolutionService _cliResolutionService;

    public SquadWatchHealthService()
        : this(
            new WatchHealthCommandRunner(),
            new SquadCliResolutionService(new SystemProcessRunner(), new SystemEnvironmentProvider())) {
    }

    internal SquadWatchHealthService(
        ISquadCommandRunner commandRunner,
        ISquadCliResolutionService cliResolutionService) {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        _cliResolutionService = cliResolutionService ?? throw new ArgumentNullException(nameof(cliResolutionService));
    }

    public async Task<SquadWatchHealthResult> GetHealthAsync(string activeDirectory) {
        var candidate = await _cliResolutionService.ResolveAsync(activeDirectory).ConfigureAwait(false);
        if (!candidate.IsAvailable) {
            return SquadWatchHealthResult.FromCommandResult(new SquadCommandResult(
                false,
                null,
                string.Empty,
                candidate.ProbeError ?? string.Empty,
                candidate.UpdateInstruction.DisplayMessage ?? "Squad CLI is not available for this workspace."));
        }

        var result = await _commandRunner
            .RunAsync(candidate, activeDirectory, "Check Squad Watch Health", ["watch", "--health"])
            .ConfigureAwait(false);

        return SquadWatchHealthResult.FromCommandResult(result);
    }

    public async Task<SquadCommandResult> StartWatchAsync(
        string activeDirectory,
        int intervalMinutes,
        bool execute,
        bool verbose,
        string? notifyLevel) {
        var candidate = await _cliResolutionService.ResolveAsync(activeDirectory).ConfigureAwait(false);
        if (!candidate.IsAvailable) {
            return new SquadCommandResult(
                false,
                null,
                string.Empty,
                candidate.ProbeError ?? string.Empty,
                candidate.UpdateInstruction.DisplayMessage ?? "Squad CLI is not available for this workspace.");
        }

        return await Task.Run(() => StartLongRunningWatch(
            candidate,
            activeDirectory,
            intervalMinutes,
            execute,
            verbose,
            notifyLevel)).ConfigureAwait(false);
    }

    public async Task<SquadCommandResult> StopWatchAsync(int processId) {
        try {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited) {
                return new SquadCommandResult(
                    true,
                    0,
                    string.Empty,
                    string.Empty,
                    $"Watch process {processId} has already exited.");
            }

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            return new SquadCommandResult(
                true,
                0,
                string.Empty,
                string.Empty,
                $"Stopped watch process {processId}.");
        }
        catch (ArgumentException) {
            return new SquadCommandResult(
                true,
                0,
                string.Empty,
                string.Empty,
                $"Watch process {processId} is not running.");
        }
        catch (Exception ex) {
            return new SquadCommandResult(
                false,
                null,
                string.Empty,
                ex.Message,
                $"Unable to stop watch process {processId}: {ex.Message}");
        }
    }

    private static SquadCommandResult StartLongRunningWatch(
        ResolvedCliCandidate candidate,
        string activeDirectory,
        int intervalMinutes,
        bool execute,
        bool verbose,
        string? notifyLevel) {
        try {
            if (!candidate.IsAvailable) {
                return new SquadCommandResult(
                    false,
                    null,
                    string.Empty,
                    candidate.ProbeError ?? string.Empty,
                    candidate.UpdateInstruction.DisplayMessage ?? "Squad CLI is not available for this workspace.");
            }

            var commandArgs = BuildWatchArguments(intervalMinutes, execute, verbose, notifyLevel);
            var startInfo = SquadCliCommands.BuildCommandStartInfo(candidate, activeDirectory, commandArgs);

            using var process = new Process {
                StartInfo = startInfo
            };
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = false;
            process.StartInfo.RedirectStandardError = false;
            process.StartInfo.CreateNoWindow = true;

            process.StartInfo.Environment["PATH"] = SquadProcessEnvironment.BuildMergedPathEnvironmentValue();

            if (!process.Start()) {
                return new SquadCommandResult(
                    false,
                    null,
                    string.Empty,
                    string.Empty,
                    "Failed to start Squad Watch.");
            }

            var processId = process.Id;
            WriteWatchHealthPidFile(activeDirectory, processId, commandArgs);
            return new SquadCommandResult(
                true,
                0,
                $"Started Squad Watch with PID {processId}.",
                string.Empty,
                $"Started Squad Watch with PID {processId}.");
        }
        catch (Exception ex) {
            return new SquadCommandResult(
                false,
                null,
                string.Empty,
                ex.Message,
                $"Unable to start Squad Watch: {ex.Message}");
        }
    }

    internal static string GetWatchHealthPidPath(string teamRoot) {
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(teamRoot)))
            .ToLowerInvariant()[..12];
        return Path.Combine(Path.GetTempPath(), $"squad-watch-{hash}.json");
    }

    private static void WriteWatchHealthPidFile(
        string teamRoot,
        int processId,
        IReadOnlyList<string> commandArgs) {
        var pidPath = GetWatchHealthPidPath(teamRoot);
        var pidInfo = new {
            pid = processId,
            startedAt = DateTimeOffset.UtcNow.ToString("O"),
            user = ProbeCurrentGhUser() ?? "(unknown)",
            interval = TryParseInterval(commandArgs) ?? 0,
            repo = teamRoot,
            capabilities = BuildCapabilities(commandArgs)
        };

        File.WriteAllText(
            pidPath,
            JsonSerializer.Serialize(pidInfo, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static int? TryParseInterval(IReadOnlyList<string> arguments) {
        for (var index = 0; index < arguments.Count - 1; index++) {
            if (!string.Equals(arguments[index], "--interval", StringComparison.OrdinalIgnoreCase))
                continue;

            if (int.TryParse(arguments[index + 1], out var interval))
                return interval;
        }

        return null;
    }

    private static string[] BuildCapabilities(IReadOnlyList<string> arguments) {
        var capabilities = new List<string>();
        if (arguments.Any(argument => string.Equals(argument, "--execute", StringComparison.OrdinalIgnoreCase)))
            capabilities.Add("execute");
        if (arguments.Any(argument => string.Equals(argument, "--verbose", StringComparison.OrdinalIgnoreCase)))
            capabilities.Add("verbose");
        for (var index = 0; index < arguments.Count - 1; index++) {
            if (!string.Equals(arguments[index], "--notify-level", StringComparison.OrdinalIgnoreCase))
                continue;

            capabilities.Add($"notify:{arguments[index + 1]}");
            break;
        }

        return capabilities.ToArray();
    }

    private static IReadOnlyList<string> BuildWatchArguments(
        int intervalMinutes,
        bool execute,
        bool verbose,
        string? notifyLevel) {
        var interval = Math.Clamp(intervalMinutes, 1, 1440);
        var commandArgs = new List<string> { "watch", "--interval", interval.ToString() };
        if (execute)
            commandArgs.Add("--execute");
        if (verbose)
            commandArgs.Add("--verbose");
        if (!string.IsNullOrWhiteSpace(notifyLevel) &&
            notifyLevel is "all" or "important" or "none") {
            commandArgs.Add("--notify-level");
            commandArgs.Add(notifyLevel);
        }

        return commandArgs;
    }

    private static string? ProbeCurrentGhUser() {
        try {
            using var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "gh",
                    Arguments = "api user -q .login",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };
            process.StartInfo.Environment["PATH"] = SquadProcessEnvironment.BuildMergedPathEnvironmentValue();

            if (!process.Start())
                return null;

            var exited = process.WaitForExit(5000);
            if (!exited) {
                try { process.Kill(entireProcessTree: true); }
                catch { /* best effort */ }
                return null;
            }

            var login = process.StandardOutput.ReadToEnd().Trim();
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(login)
                ? login
                : null;
        }
        catch {
            return null;
        }
    }

    private sealed class WatchHealthCommandRunner : ISquadCommandRunner {
        public Task<SquadCommandResult> RunAsync(SquadCliCommandDefinition command, string activeDirectory) {
            return new SquadInstallerServiceProcessRunner().RunAsync(command, activeDirectory);
        }

        public Task<SquadCommandResult> RunAsync(
            ResolvedCliCandidate candidate,
            string activeDirectory,
            string displayName,
            IReadOnlyList<string> commandArguments) {
            return new SquadInstallerServiceProcessRunner().RunAsync(
                candidate,
                activeDirectory,
                displayName,
                commandArguments);
        }
    }
}

internal sealed record SquadWatchHealthResult(
    bool Success,
    bool IsRunning,
    string Summary,
    IReadOnlyList<string> Lines,
    string? ErrorText = null,
    int? ProcessId = null) {

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
        var processId = TryParseProcessId(lines);
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
            result.Success ? null : combined,
            processId);
    }

    private static int? TryParseProcessId(IEnumerable<string> lines) {
        foreach (var line in lines) {
            var match = Regex.Match(line, @"\bPID:\s*(\d+)\b", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var processId))
                return processId;
        }

        return null;
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
    public Task<SquadCommandResult> RunAsync(
        SquadCliCommandDefinition command,
        string activeDirectory) {
        var startInfo = new System.Diagnostics.ProcessStartInfo {
            FileName = command.FileName,
            Arguments = command.Arguments,
            WorkingDirectory = activeDirectory
        };
        return RunStartInfoAsync(startInfo, command.DisplayName);
    }

    public Task<SquadCommandResult> RunAsync(
        ResolvedCliCandidate candidate,
        string activeDirectory,
        string displayName,
        IReadOnlyList<string> commandArguments) {
        if (!candidate.IsAvailable) {
            return Task.FromResult(new SquadCommandResult(
                false,
                null,
                string.Empty,
                candidate.ProbeError ?? string.Empty,
                candidate.UpdateInstruction.DisplayMessage ?? "Squad CLI is not available."));
        }

        var startInfo = SquadCliCommands.BuildCommandStartInfo(
            candidate,
            activeDirectory,
            commandArguments);
        return RunStartInfoAsync(startInfo, displayName);
    }

    private static async Task<SquadCommandResult> RunStartInfoAsync(
        System.Diagnostics.ProcessStartInfo startInfo,
        string displayName) {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        try {
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;
            startInfo.Environment["PATH"] = SquadProcessEnvironment.BuildMergedPathEnvironmentValue();

            using var process = new System.Diagnostics.Process {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

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

            // Cap pipe-drain wait at 5 seconds — same guard as SquadInstallerService.
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await Task.WhenAll(outputClosed.Task, errorClosed.Task)
                    .WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* pipe drain timed out */ }

            var success = process.ExitCode == 0;
            var message = success
                ? $"{displayName} completed."
                : $"{displayName} failed with exit code {process.ExitCode}.";

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
                $"Unable to launch {displayName}: {ex.Message}");
        }
    }
}
