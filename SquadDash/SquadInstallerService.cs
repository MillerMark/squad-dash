using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SquadDash;

internal sealed class SquadInstallerService {
    private readonly ISquadCommandRunner _commandRunner;

    public SquadInstallerService()
        : this(new ProcessSquadCommandRunner()) {
    }

    internal SquadInstallerService(ISquadCommandRunner commandRunner) {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public async Task<SquadCommandResult> InstallAsync(
        string activeDirectory,
        IProgress<string>? progress = null) {
        progress?.Report("Checking Node.js tooling...");

        var prerequisites = await ValidatePrerequisitesAsync(activeDirectory).ConfigureAwait(false);
        if (!prerequisites.Success) {
            return new SquadCommandResult(
                false,
                null,
                string.Empty,
                string.Empty,
                prerequisites.Message,
                prerequisites.MissingTools);
        }

        progress?.Report("Ensuring package.json exists...");
        var packageManifestResult = EnsurePackageManifest(activeDirectory);
        if (!packageManifestResult.Success)
            return packageManifestResult;

        progress?.Report("Installing local Squad CLI...");
        var installCliResult = await _commandRunner.RunAsync(
            SquadCliCommands.InstallLocalCli,
            activeDirectory).ConfigureAwait(false);
        if (!installCliResult.Success)
            return CombineInstallResults(packageManifestResult, installCliResult);

        progress?.Report("Applying Windows compatibility fixes...");
        var compatibilityResult = SquadRuntimeCompatibility.Repair(
            activeDirectory,
            "Workspace Squad CLI");
        if (!compatibilityResult.Success)
            return CombineInstallResults(packageManifestResult, installCliResult, compatibilityResult);

        if (IsWorkspaceInitialized(activeDirectory)) {
            return CombineInstallResults(
                packageManifestResult,
                installCliResult,
                compatibilityResult,
                new SquadCommandResult(
                    true,
                    0,
                    "Squad workspace already initialized.",
                    string.Empty,
                    "Squad is installed and ready to run locally."));
        }

        progress?.Report("Running Squad bootstrap...");
        var initResult = await _commandRunner.RunAsync(SquadCliCommands.Init, activeDirectory).ConfigureAwait(false);

        if (initResult.Success)
            WriteSquadDashUniverseFiles(activeDirectory);

        return CombineInstallResults(packageManifestResult, installCliResult, compatibilityResult, initResult);
    }

    public async Task<SquadCommandResult> RunDoctorAsync(
        string activeDirectory,
        IProgress<string>? progress = null) {
        progress?.Report("Running Squad doctor...");

        var prerequisites = await ValidatePrerequisitesAsync(activeDirectory).ConfigureAwait(false);
        if (!prerequisites.Success) {
            return new SquadCommandResult(
                false,
                null,
                string.Empty,
                string.Empty,
                prerequisites.Message,
                prerequisites.MissingTools);
        }

        var doctorResult = await _commandRunner.RunAsync(SquadCliCommands.Doctor, activeDirectory).ConfigureAwait(false);
        if (doctorResult.Success)
            SquadScribeWorkspaceRepairService.Repair(activeDirectory);

        return doctorResult;
    }

    private async Task<SquadPrerequisiteCheckResult> ValidatePrerequisitesAsync(string activeDirectory) {
        var missingTools = new List<string>();

        foreach (var toolName in new[] { "node", "npm", "npx" }) {
            var result = await RunWhereAsync(toolName, activeDirectory).ConfigureAwait(false);
            if (!result.Success)
                missingTools.Add(toolName);
        }

        if (missingTools.Count == 0)
            return SquadPrerequisiteCheckResult.SuccessResult;

        return new SquadPrerequisiteCheckResult(
            false,
            $"Missing required tooling: {string.Join(", ", missingTools)}. Install Node.js so node, npm, and npx are available on PATH.",
            missingTools);
    }

    private Task<SquadCommandResult> RunWhereAsync(string toolName, string activeDirectory) {
        var command = new SquadCliCommandDefinition("where.exe", toolName, $"Locate {toolName}");
        return _commandRunner.RunAsync(command, activeDirectory);
    }

    private static SquadCommandResult EnsurePackageManifest(string activeDirectory) {
        try {
            var packageJsonPath = Path.Combine(activeDirectory, "package.json");
            if (File.Exists(packageJsonPath)) {
                return new SquadCommandResult(
                    true,
                    0,
                    $"package.json already exists at {packageJsonPath}",
                    string.Empty,
                    "Package manifest is ready.");
            }

            var packageName = BuildPackageName(activeDirectory);
            var packageJson = new JsonObject {
                ["name"] = packageName,
                ["private"] = true,
                ["version"] = "1.0.0"
            };

            var json = packageJson.ToJsonString(new JsonSerializerOptions {
                WriteIndented = true
            });

            File.WriteAllText(packageJsonPath, json + Environment.NewLine, Encoding.UTF8);

            return new SquadCommandResult(
                true,
                0,
                $"Created package.json at {packageJsonPath}",
                string.Empty,
                "Package manifest is ready.");
        }
        catch (Exception ex) {
            return new SquadCommandResult(
                false,
                null,
                string.Empty,
                ex.Message,
                $"Unable to create package.json: {ex.Message}");
        }
    }

    public static void EnsureSquadDashUniverseFiles(string activeDirectory) {
        if (!Directory.Exists(Path.Combine(activeDirectory, ".squad")))
            return;

        WriteSquadDashUniverseFiles(activeDirectory);
        SquadScribeWorkspaceRepairService.Repair(activeDirectory);
    }

    private static void WriteSquadDashUniverseFiles(string activeDirectory) {
        try {
            var universesDir = Path.Combine(activeDirectory, ".squad", "universes");
            Directory.CreateDirectory(universesDir);

            var squadDashMd = LoadEmbeddedSquadDashMd();
            EnsureUniverseMarkdown(universesDir, "squaddash.md", squadDashMd);
            EnsureUniverseMarkdown(universesDir, "squaddash-profiles.md", LoadEmbeddedSquadDashProfilesMd());

            // Also write to .squad/templates/universes/ so the agent init flow can
            // find the file at the standard templates path without a ⚠ warning.
            var templateUniversesDir = Path.Combine(activeDirectory, ".squad", "templates", "universes");
            Directory.CreateDirectory(templateUniversesDir);
            EnsureUniverseMarkdown(templateUniversesDir, "squaddash.md", squadDashMd);

            EnsureCastingStateFiles(activeDirectory);
            PatchCastingPolicy(activeDirectory);
        }
        catch {
            // Non-fatal — Squad installed successfully; user can add universe files manually.
        }
    }

    public static string? LoadEmbeddedSquadDashMdPublic() => LoadEmbeddedSquadDashMd();
    public static string? LoadEmbeddedSquadDashProfilesMdPublic() => LoadEmbeddedSquadDashProfilesMd();

    private static void EnsureUniverseMarkdown(string universesDir, string fileName, string? content) {
        var destinationPath = Path.Combine(universesDir, fileName);
        if (File.Exists(destinationPath) || content is null)
            return;

        File.WriteAllText(destinationPath, content, Encoding.UTF8);
    }

    private static string? LoadEmbeddedSquadDashMd() => LoadEmbeddedMarkdown("squaddash.md");

    private static string? LoadEmbeddedSquadDashProfilesMd() => LoadEmbeddedMarkdown("squaddash-profiles.md");

    private static string? LoadEmbeddedMarkdown(string resourceFileName) {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceFileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            return null;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void PatchCastingPolicy(string activeDirectory) {
        var policyPath = Path.Combine(activeDirectory, ".squad", "casting", "policy.json");
        if (!File.Exists(policyPath))
            return;

        var json = JsonNode.Parse(File.ReadAllText(policyPath));
        if (json is null)
            return;

        var allowlist = json["allowlist_universes"]?.AsArray();
        var capacity = json["universe_capacity"]?.AsObject();

        if (allowlist is not null && !allowlist.Any(n => string.Equals(n?.GetValue<string>(), SquadDashUniverseName, StringComparison.Ordinal)))
            allowlist.Insert(0, JsonValue.Create(SquadDashUniverseName));

        if (capacity is not null)
            capacity[SquadDashUniverseName] = SquadDashUniverseCapacity;

        File.WriteAllText(policyPath, json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    private static void EnsureCastingStateFiles(string activeDirectory) {
        var castingDirectory = Path.Combine(activeDirectory, ".squad", "casting");
        Directory.CreateDirectory(castingDirectory);

        EnsureCastingStateFile(
            activeDirectory,
            Path.Combine(castingDirectory, "policy.json"),
            "casting-policy.json",
            CreateDefaultCastingPolicyJson);
        EnsureCastingStateFile(
            activeDirectory,
            Path.Combine(castingDirectory, "history.json"),
            "casting-history.json",
            () => "{\n  \"universe_usage_history\": [],\n  \"assignment_cast_snapshots\": {}\n}\n");
        EnsureCastingStateFile(
            activeDirectory,
            Path.Combine(castingDirectory, "registry.json"),
            "casting-registry.json",
            () => "{\n  \"agents\": {}\n}\n");
    }

    private static void EnsureCastingStateFile(
        string activeDirectory,
        string destinationPath,
        string templateFileName,
        Func<string> fallbackFactory) {
        if (File.Exists(destinationPath))
            return;

        var templatePath = Path.Combine(activeDirectory, ".squad", "templates", templateFileName);
        if (File.Exists(templatePath)) {
            File.Copy(templatePath, destinationPath, overwrite: false);
            return;
        }

        File.WriteAllText(destinationPath, fallbackFactory(), Encoding.UTF8);
    }

    private static string CreateDefaultCastingPolicyJson() {
        var policy = new JsonObject {
            ["casting_policy_version"] = "1.1",
            ["allowlist_universes"] = new JsonArray(JsonValue.Create(SquadDashUniverseName)),
            ["universe_capacity"] = new JsonObject {
                [SquadDashUniverseName] = SquadDashUniverseCapacity
            }
        };

        return policy.ToJsonString(new JsonSerializerOptions {
            WriteIndented = true
        }) + Environment.NewLine;
    }

    internal const string SquadDashUniverseName = "SquadDash Universe";
    internal const int SquadDashUniverseCapacity = 30;

    private static bool IsWorkspaceInitialized(string activeDirectory) {
        var teamFilePath = Path.Combine(activeDirectory, ".squad", "team.md");
        return File.Exists(teamFilePath);
    }

    private static string BuildPackageName(string activeDirectory) {
        var folderName = Path.GetFileName(
            activeDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(folderName))
            return "squad-workspace";

        var builder = new StringBuilder(folderName.Length);
        var lastWasSeparator = false;

        foreach (var character in folderName.ToLowerInvariant()) {
            if (char.IsLetterOrDigit(character)) {
                builder.Append(character);
                lastWasSeparator = false;
                continue;
            }

            if (character is '.' or '-' or '_') {
                builder.Append(character);
                lastWasSeparator = false;
                continue;
            }

            if (lastWasSeparator)
                continue;

            builder.Append('-');
            lastWasSeparator = true;
        }

        var value = builder
            .ToString()
            .Trim('-', '.', '_');

        return string.IsNullOrWhiteSpace(value)
            ? "squad-workspace"
            : value;
    }

    private static SquadCommandResult CombineInstallResults(params SquadCommandResult[] steps) {
        var combinedOutput = new StringBuilder();
        var combinedError = new StringBuilder();

        foreach (var step in steps) {
            AppendSection(combinedOutput, step.Message, step.StandardOutput);
            AppendSection(combinedError, step.Message, step.StandardError);
        }

        var failure = steps.LastOrDefault(step => !step.Success);
        if (failure is not null) {
            return new SquadCommandResult(
                false,
                failure.ExitCode,
                combinedOutput.ToString().TrimEnd(),
                combinedError.ToString().TrimEnd(),
                failure.Message,
                failure.MissingTools);
        }

        return new SquadCommandResult(
            true,
            steps.LastOrDefault()?.ExitCode ?? 0,
            combinedOutput.ToString().TrimEnd(),
            combinedError.ToString().TrimEnd(),
            "Squad is installed and ready to run locally.");
    }

    private static void AppendSection(StringBuilder builder, string title, string content) {
        if (string.IsNullOrWhiteSpace(content))
            return;

        if (builder.Length > 0)
            builder.AppendLine();

        builder.AppendLine($"[{title}]");
        builder.AppendLine(content.TrimEnd());
    }

    private sealed class ProcessSquadCommandRunner : ISquadCommandRunner {
        public async Task<SquadCommandResult> RunAsync(
            SquadCliCommandDefinition command,
            string activeDirectory) {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            try {
                using var process = new Process {
                    StartInfo = new ProcessStartInfo {
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

                process.StartInfo.Environment["PATH"] = BuildMergedPathEnvironmentValue();

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

        private static string BuildMergedPathEnvironmentValue() {
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
}

internal interface ISquadCommandRunner {
    Task<SquadCommandResult> RunAsync(SquadCliCommandDefinition command, string activeDirectory);
}

internal sealed record SquadCommandResult(
    bool Success,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    string Message,
    IReadOnlyList<string>? MissingTools = null) {

    public string ToDisplayText() {
        var builder = new StringBuilder();
        builder.AppendLine(Message);

        if (MissingTools is { Count: > 0 }) {
            builder.AppendLine();
            builder.AppendLine("Missing tools:");
            builder.AppendLine(string.Join(", ", MissingTools));
        }

        if (ExitCode is not null) {
            builder.AppendLine();
            builder.AppendLine($"Exit code: {ExitCode}");
        }

        if (!string.IsNullOrWhiteSpace(StandardOutput)) {
            builder.AppendLine();
            builder.AppendLine("stdout");
            builder.AppendLine(StandardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(StandardError)) {
            builder.AppendLine();
            builder.AppendLine("stderr");
            builder.AppendLine(StandardError.TrimEnd());
        }

        return builder.ToString().TrimEnd();
    }
}

internal sealed record SquadPrerequisiteCheckResult(
    bool Success,
    string Message,
    IReadOnlyList<string> MissingTools) {

    public static SquadPrerequisiteCheckResult SuccessResult { get; } =
        new(true, "Tooling is available.", Array.Empty<string>());
}
