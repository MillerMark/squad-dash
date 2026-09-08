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
    private readonly ISquadCliResolutionService _cliResolutionService;

    public SquadInstallerService()
        : this(
            new ProcessSquadCommandRunner(),
            new SquadCliResolutionService(new SystemProcessRunner(), new SystemEnvironmentProvider())) {
    }

    internal SquadInstallerService(ISquadCommandRunner commandRunner)
        : this(
            commandRunner,
            new SquadCliResolutionService(new SystemProcessRunner(), new SystemEnvironmentProvider())) {
    }

    internal SquadInstallerService(
        ISquadCommandRunner commandRunner,
        ISquadCliResolutionService cliResolutionService) {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        _cliResolutionService = cliResolutionService ?? throw new ArgumentNullException(nameof(cliResolutionService));
    }

    public async Task<SquadCommandResult> InstallAsync(
        string activeDirectory,
        IProgress<string>? progress = null) {
        progress?.Report("Resolving Squad CLI...");
        var resolvedCandidate = await _cliResolutionService.ResolveAsync(activeDirectory).ConfigureAwait(false);
        if (resolvedCandidate.IsAvailable) {
            return await InitializeWorkspaceAsync(activeDirectory, resolvedCandidate, progress).ConfigureAwait(false);
        }

        progress?.Report("No Squad CLI detected. Checking for winget...");
        var wingetProbe = await RunWhereAsync("winget", activeDirectory).ConfigureAwait(false);
        if (wingetProbe.Success) {
            progress?.Report("Installing native Squad CLI via winget...");
            var wingetInstallResult = await _commandRunner
                .RunAsync(SquadCliCommands.InstallNativeCliWithWinget, activeDirectory)
                .ConfigureAwait(false);
            if (wingetInstallResult.Success) {
                progress?.Report("Re-resolving Squad CLI after winget install...");
                var nativeCandidate = await _cliResolutionService.ResolveAsync(activeDirectory).ConfigureAwait(false);
                if (nativeCandidate.IsAvailable) {
                    return await InitializeWorkspaceAsync(
                            activeDirectory,
                            nativeCandidate,
                            progress,
                            TagSource(wingetInstallResult, GetInstallSourceLabel(nativeCandidate)))
                        .ConfigureAwait(false);
                }
            }

            progress?.Report("Native install did not yield a usable CLI. Falling back to workspace npm...");
            return await InstallViaWorkspaceNpmAsync(
                    activeDirectory,
                    progress,
                    TagSource(wingetInstallResult, "native (winget)"))
                .ConfigureAwait(false);
        }

        progress?.Report("winget not found on PATH. Falling back to workspace npm...");
        return await InstallViaWorkspaceNpmAsync(activeDirectory, progress).ConfigureAwait(false);
    }

    private async Task<SquadCommandResult> InstallViaWorkspaceNpmAsync(
        string activeDirectory,
        IProgress<string>? progress,
        params SquadCommandResult[] priorSteps) {
        progress?.Report("Checking Node.js/npm tooling for workspace npm install...");
        var prerequisites = await ValidatePrerequisitesAsync(
            activeDirectory,
            requireNode: true,
            requireNpm: true).ConfigureAwait(false);
        if (!prerequisites.Success) {
            return CombineInstallResults(
                priorSteps.Append(new SquadCommandResult(
                        false,
                        null,
                        string.Empty,
                        string.Empty,
                        prerequisites.Message,
                        prerequisites.MissingTools,
                        "workspace npm"))
                    .ToArray());
        }

        progress?.Report("Ensuring package.json exists for workspace npm install...");
        var packageManifestResult = EnsurePackageManifest(activeDirectory);
        if (!packageManifestResult.Success) {
            return CombineInstallResults(
                priorSteps.Append(TagSource(packageManifestResult, "workspace npm")).ToArray());
        }

        progress?.Report("Installing workspace-local Squad CLI via npm...");
        var installCliResult = await _commandRunner.RunAsync(
            SquadCliCommands.InstallLocalCli,
            activeDirectory).ConfigureAwait(false);
        if (!installCliResult.Success) {
            return CombineInstallResults(priorSteps.Concat(new[] {
                TagSource(packageManifestResult, "workspace npm"),
                TagSource(installCliResult, "workspace npm")
            }).ToArray());
        }

        progress?.Report("Applying Windows compatibility fixes...");
        var compatibilityResult = SquadRuntimeCompatibility.Repair(
            activeDirectory,
            "Workspace Squad CLI");
        if (!compatibilityResult.Success) {
            return CombineInstallResults(priorSteps.Concat(new[] {
                TagSource(packageManifestResult, "workspace npm"),
                TagSource(installCliResult, "workspace npm"),
                TagSource(compatibilityResult, "workspace npm")
            }).ToArray());
        }

        var resolvedWorkspaceCli = await _cliResolutionService
            .ResolveWithModeAsync(CliSelectionMode.WorkspaceLocal, activeDirectory)
            .ConfigureAwait(false);
        if (!resolvedWorkspaceCli.IsAvailable) {
            return CombineInstallResults(priorSteps.Concat(new[] {
                TagSource(packageManifestResult, "workspace npm"),
                TagSource(installCliResult, "workspace npm"),
                TagSource(compatibilityResult, "workspace npm"),
                new SquadCommandResult(
                    false,
                    null,
                    string.Empty,
                    resolvedWorkspaceCli.ProbeError ?? string.Empty,
                    resolvedWorkspaceCli.UpdateInstruction.DisplayMessage ??
                    "Workspace Squad CLI could not be resolved after installation.",
                    null,
                    "workspace npm")
            }).ToArray());
        }

        return await InitializeWorkspaceAsync(
                activeDirectory,
                resolvedWorkspaceCli,
                progress,
                priorSteps.Concat(new[] {
                    TagSource(packageManifestResult, "workspace npm"),
                    TagSource(installCliResult, "workspace npm"),
                    TagSource(compatibilityResult, "workspace npm")
                }).ToArray())
            .ConfigureAwait(false);
    }

    public async Task<SquadCommandResult> RunDoctorAsync(
        string activeDirectory,
        IProgress<string>? progress = null) {
        progress?.Report("Running Squad doctor...");

        var resolvedCandidate = await _cliResolutionService.ResolveAsync(activeDirectory).ConfigureAwait(false);
        if (!resolvedCandidate.IsAvailable) {
            return new SquadCommandResult(
                false,
                null,
                string.Empty,
                resolvedCandidate.ProbeError ?? string.Empty,
                resolvedCandidate.UpdateInstruction.DisplayMessage ??
                "Squad CLI is not available for this workspace.");
        }

        var prerequisites = await ValidatePrerequisitesAsync(activeDirectory, resolvedCandidate).ConfigureAwait(false);
        if (!prerequisites.Success) {
            return new SquadCommandResult(
                false,
                null,
                string.Empty,
                string.Empty,
                prerequisites.Message,
                prerequisites.MissingTools);
        }

        var doctorResult = await _commandRunner.RunAsync(
                resolvedCandidate,
                activeDirectory,
                "Run Squad Doctor",
                ["doctor"])
            .ConfigureAwait(false);
        if (doctorResult.Success) {
            SquadScribeWorkspaceRepairService.Repair(activeDirectory);
            SquadPlanningWorkspaceRepairService.Repair(activeDirectory);
        }

        return doctorResult;
    }

    private async Task<SquadCommandResult> InitializeWorkspaceAsync(
        string activeDirectory,
        ResolvedCliCandidate resolvedCandidate,
        IProgress<string>? progress,
        params SquadCommandResult[] priorSteps) {
        var sourceLabel = GetInstallSourceLabel(resolvedCandidate);
        var prerequisites = await ValidatePrerequisitesAsync(activeDirectory, resolvedCandidate).ConfigureAwait(false);
        if (!prerequisites.Success) {
            return CombineInstallResults(
                priorSteps.Append(new SquadCommandResult(
                        false,
                        null,
                        string.Empty,
                        string.Empty,
                        prerequisites.Message,
                        prerequisites.MissingTools,
                        sourceLabel))
                    .ToArray());
        }

        if (IsWorkspaceInitialized(activeDirectory)) {
            return CombineInstallResults(
                priorSteps.Append(new SquadCommandResult(
                        true,
                        0,
                        "Squad workspace already initialized.",
                        string.Empty,
                        "Squad is installed and ready to run locally.",
                        null,
                        sourceLabel))
                    .ToArray());
        }

        progress?.Report($"Running Squad bootstrap via {sourceLabel} CLI...");
        var initResult = await _commandRunner.RunAsync(
                resolvedCandidate,
                activeDirectory,
                "Install Squad",
                ["init"])
            .ConfigureAwait(false);

        if (initResult.Success)
            WriteSquadDashUniverseFiles(activeDirectory);

        return CombineInstallResults(priorSteps.Append(TagSource(initResult, sourceLabel)).ToArray());
    }

    private async Task<SquadPrerequisiteCheckResult> ValidatePrerequisitesAsync(
        string activeDirectory,
        ResolvedCliCandidate resolvedCandidate) {
        var requireNpm = resolvedCandidate.DetectedSource is CliInstallSource.WorkspaceNpm or CliInstallSource.GlobalNpm;
        var requireNode = requireNpm || resolvedCandidate.RuntimeType == CliRuntimeType.NodeScript;
        return await ValidatePrerequisitesAsync(activeDirectory, requireNode, requireNpm).ConfigureAwait(false);
    }

    private async Task<SquadPrerequisiteCheckResult> ValidatePrerequisitesAsync(
        string activeDirectory,
        bool requireNode,
        bool requireNpm) {
        if (!requireNode && !requireNpm)
            return SquadPrerequisiteCheckResult.SuccessResult;

        var missingTools = new List<string>();
        var requiredTools = new List<string>();
        if (requireNode)
            requiredTools.Add("node");
        if (requireNpm)
            requiredTools.Add("npm");

        foreach (var toolName in requiredTools) {
            var result = await RunWhereAsync(toolName, activeDirectory).ConfigureAwait(false);
            if (!result.Success)
                missingTools.Add(toolName);
        }

        if (missingTools.Count == 0)
            return SquadPrerequisiteCheckResult.SuccessResult;

        var prerequisiteHint = requireNpm
            ? "Install Node.js and npm so npm-based Squad CLI tooling is available on PATH."
            : "Install Node.js so required tooling is available on PATH.";
        return new SquadPrerequisiteCheckResult(
            false,
            $"Missing required tooling: {string.Join(", ", missingTools)}. {prerequisiteHint}",
            missingTools);
    }

    private static string GetInstallSourceLabel(ResolvedCliCandidate candidate) {
        return candidate.DetectedSource switch {
            CliInstallSource.WorkspaceNpm => "workspace npm",
            CliInstallSource.GlobalNpm => "global npm",
            CliInstallSource.NativeWinget => "native (winget)",
            CliInstallSource.NativeOther => "native",
            CliInstallSource.Custom => "custom",
            _ => "unknown"
        };
    }

    private static SquadCommandResult TagSource(SquadCommandResult result, string source) =>
        result with { InstallSource = source };

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

            var json = packageJson.ToJsonString(JsonFileStorage.PrettyPrint);

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
        var layout = SquadWorkspaceLayoutResolver.Resolve(activeDirectory);
        if (layout is null)
            return;

        if (!Directory.Exists(layout.TeamSquadFolderPath))
            return;

        WriteSquadDashUniverseFiles(activeDirectory, layout.TeamSquadFolderPath);
        SquadScribeWorkspaceRepairService.Repair(activeDirectory);
        SquadPlanningWorkspaceRepairService.Repair(activeDirectory);
    }

    private static void WriteSquadDashUniverseFiles(string activeDirectory, string? squadFolderPath = null) {
        try {
            var squadDir = squadFolderPath ?? Path.Combine(activeDirectory, ".squad");
            var universesDir = Path.Combine(squadDir, "universes");
            Directory.CreateDirectory(universesDir);

            var squadDashMd = LoadEmbeddedSquadDashMd();
            // Always overwrite SquadDash-owned roster files so new agents are visible
            // after an app update without requiring a fresh workspace install.
            UpdateUniverseMarkdown(universesDir, "squaddash.md", squadDashMd);
            UpdateUniverseMarkdown(universesDir, "squaddash-profiles.md", LoadEmbeddedSquadDashProfilesMd());

            // Also write to .squad/templates/universes/ so the agent init flow can
            // find the file at the standard templates path without a ⚠ warning.
            var templateUniversesDir = Path.Combine(squadDir, "templates", "universes");
            Directory.CreateDirectory(templateUniversesDir);
            UpdateUniverseMarkdown(templateUniversesDir, "squaddash.md", squadDashMd);

            EnsureLoopFiles(squadDir);
            EnsureCodeHealthFile(squadDir);
            EnsureCastingStateFiles(squadDir);
            PatchCastingPolicy(squadDir);
            EnsureCodeHealthStateInGitIgnore(activeDirectory);
        }
        catch {
            // Non-fatal — Squad installed successfully; user can add universe files manually.
        }
    }

    private static void EnsureLoopFiles(string squadDir) {
        EnsureLoopFile(squadDir, "loop-filtered-tasks.md");
        EnsureLoopFile(squadDir, "loop-fix-test-failures.md");
        EnsureLoopFile(squadDir, "loop-interactive-repair.md");
        UpdateHostOwnedLoopFile(squadDir, "loop-executing-plan.md");
    }

    private static void EnsureCodeHealthFile(string squadDir) {
        var destPath = Path.Combine(squadDir, "code-health.md");
        if (File.Exists(destPath))
            return;

        CodeHealthMdParser.WriteWorkspaceSettingsFile(destPath, new CodeHealthMdConfig(
            Configured: true,
            EnabledOnIdle: false,
            IdleTimeout: 15,
            MaxTasksPerSession: 5,
            Safety: "branch"));
    }

    private static void EnsureLoopFile(string squadDir, string fileName) {
        var destPath = Path.Combine(squadDir, fileName);
        if (File.Exists(destPath))
            return;

        var content = LoadEmbeddedMarkdown(fileName);
        if (content is null)
            return;

        File.WriteAllText(destPath, content, Encoding.UTF8);
    }

    private static void UpdateHostOwnedLoopFile(string squadDir, string fileName) {
        var content = LoadEmbeddedMarkdown(fileName);
        if (content is null)
            return;

        var destPath = Path.Combine(squadDir, fileName);
        if (File.Exists(destPath) &&
            string.Equals(File.ReadAllText(destPath), content, StringComparison.Ordinal))
            return;

        File.WriteAllText(destPath, content, Encoding.UTF8);
    }

    public static string? LoadEmbeddedSquadDashMdPublic() => LoadEmbeddedSquadDashMd();
    public static string? LoadEmbeddedSquadDashProfilesMdPublic() => LoadEmbeddedSquadDashProfilesMd();
    internal static string? LoadEmbeddedLoopMarkdownPublic(string fileName) => LoadEmbeddedMarkdown(fileName);
    public static string? LoadEmbeddedCastingReferenceMdPublic() => LoadEmbeddedMarkdown("casting-reference.md");
    internal static string? LoadEmbeddedCodeHealthMdPublic() => LoadEmbeddedMarkdown("code-health.md");

    private static void EnsureUniverseMarkdown(string universesDir, string fileName, string? content) {
        var destinationPath = Path.Combine(universesDir, fileName);
        if (File.Exists(destinationPath) || content is null)
            return;

        File.WriteAllText(destinationPath, content, Encoding.UTF8);
    }

    private static void UpdateUniverseMarkdown(string universesDir, string fileName, string? content) {
        if (content is null) return;
        var destinationPath = Path.Combine(universesDir, fileName);
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

    private static void PatchCastingPolicy(string squadDir) {
        var policyPath = Path.Combine(squadDir, "casting", "policy.json");
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

        File.WriteAllText(policyPath, json.ToJsonString(JsonFileStorage.PrettyPrint), Encoding.UTF8);
    }

    /// <summary>
    /// Ensures Squad ephemeral files and SquadDash runtime files are excluded from version
    /// control by adding any missing entries to the workspace <c>.gitignore</c>. Creates
    /// the file if absent. Returns <c>true</c> if the file was modified.
    /// </summary>
    internal static bool EnsureCodeHealthStateInGitIgnore(string workspaceFolder) {
        // Each tuple: (gitignore entry, comment line written above it on first add)
        (string Entry, string Comment)[] required = [
            // Squad ephemeral directories — never track runtime state or user-local data
            (".squad/inbox/",              "# Squad: user inbox messages (ephemeral, user-local)"),
            (".squad/orchestration-log/",  "# Squad: agent orchestration logs (runtime, not for version control)"),
            (".squad/log/",                "# Squad: Squad runtime logs"),
            (".squad/decisions/inbox/",    "# Squad: decisions inbox (user-local, ephemeral)"),
            (".squad/sessions/",           "# Squad: session state (user-local, ephemeral)"),
            (".squad-workstream",          "# Squad: SubSquad activation file (local to this machine)"),
            
            // SquadDash-specific entries
            ("code-health-state.json",       "# SquadDash: maintenance state (auto-managed)"),
            (".squad/code-health-reports/",  "# SquadDash: maintenance reports (runtime, not for version control)"),
        ];

        var gitIgnorePath = Path.Combine(workspaceFolder, ".gitignore");

        try {
            HashSet<string> existing;
            if (File.Exists(gitIgnorePath)) {
                var lines = File.ReadAllLines(gitIgnorePath)
                    .Select(l => l.Trim())
                    .ToArray();
                existing = new HashSet<string>(lines, StringComparer.OrdinalIgnoreCase);
            }
            else {
                existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var missing = required.Where(r => !existing.Contains(r.Entry)).ToList();
            if (missing.Count == 0)
                return false;

            var sb = new System.Text.StringBuilder();
            foreach (var (entry, comment) in missing) {
                sb.AppendLine();
                sb.AppendLine(comment);
                sb.AppendLine(entry);
            }
            var toAppend = sb.ToString();

            if (!File.Exists(gitIgnorePath))
                File.WriteAllText(gitIgnorePath, toAppend.TrimStart(), Encoding.UTF8);
            else
                File.AppendAllText(gitIgnorePath, toAppend, Encoding.UTF8);

            SquadDashTrace.Write(TraceCategory.General,
                $"SquadInstallerService: added {missing.Count} entry(s) to {gitIgnorePath}: " +
                string.Join(", ", missing.Select(r => r.Entry)));
            return true;
        }
        catch (Exception ex) {
            SquadDashTrace.Write(TraceCategory.General,
                $"SquadInstallerService: failed to update .gitignore at {gitIgnorePath}: {ex.Message}");
            return false;
        }
    }

    private static void EnsureCastingStateFiles(string squadDir) {
        var castingDirectory = Path.Combine(squadDir, "casting");
        Directory.CreateDirectory(castingDirectory);

        EnsureCastingStateFile(
            squadDir,
            Path.Combine(castingDirectory, "policy.json"),
            "casting-policy.json",
            CreateDefaultCastingPolicyJson);
        EnsureCastingStateFile(
            squadDir,
            Path.Combine(castingDirectory, "history.json"),
            "casting-history.json",
            () => "{\n  \"universe_usage_history\": [],\n  \"assignment_cast_snapshots\": {}\n}\n");
        EnsureCastingStateFile(
            squadDir,
            Path.Combine(castingDirectory, "registry.json"),
            "casting-registry.json",
            () => "{\n  \"agents\": {}\n}\n");
    }

    private static void EnsureCastingStateFile(
        string squadDir,
        string destinationPath,
        string templateFileName,
        Func<string> fallbackFactory) {
        if (File.Exists(destinationPath))
            return;

        var templatePath = Path.Combine(squadDir, "templates", templateFileName);
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

        return policy.ToJsonString(JsonFileStorage.PrettyPrint) + Environment.NewLine;
    }

    internal const string SquadDashUniverseName = "SquadDash Universe";
    internal const int SquadDashUniverseCapacity = 30;

    private static bool IsWorkspaceInitialized(string activeDirectory) {
        var layout = SquadWorkspaceLayoutResolver.Resolve(activeDirectory);
        return layout is not null && File.Exists(layout.TeamFilePath);
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
        var source = steps.LastOrDefault(step => !string.IsNullOrWhiteSpace(step.InstallSource))?.InstallSource;
        var terminalStep = steps.LastOrDefault();

        foreach (var step in steps) {
            AppendSection(combinedOutput, step.Message, step.StandardOutput);
            AppendSection(combinedError, step.Message, step.StandardError);
        }

        if (terminalStep is not null && !terminalStep.Success) {
            return new SquadCommandResult(
                false,
                terminalStep.ExitCode,
                combinedOutput.ToString().TrimEnd(),
                combinedError.ToString().TrimEnd(),
                terminalStep.Message,
                terminalStep.MissingTools,
                source);
        }

        return new SquadCommandResult(
            true,
            terminalStep?.ExitCode ?? 0,
            combinedOutput.ToString().TrimEnd(),
            combinedError.ToString().TrimEnd(),
            "Squad is installed and ready to run locally.",
            null,
            source);
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
        public Task<SquadCommandResult> RunAsync(
            SquadCliCommandDefinition command,
            string activeDirectory) {
            var startInfo = new ProcessStartInfo {
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

            var startInfo = SquadCliCommands.BuildCommandStartInfo(candidate, activeDirectory, commandArguments);
            return RunStartInfoAsync(startInfo, displayName);
        }

        private static async Task<SquadCommandResult> RunStartInfoAsync(ProcessStartInfo startInfo, string displayName) {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            try {
                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.RedirectStandardInput = true;
                startInfo.CreateNoWindow = true;
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;
                startInfo.Environment["PATH"] = BuildMergedPathEnvironmentValue();

                using var process = new Process {
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
                process.StandardInput.Close();

                using var processCts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(3));
                try {
                    await process.WaitForExitAsync(processCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    try {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (Exception ex) {
                        SquadDashTrace.Write("SquadCli", $"Timed-out process kill failed: {ex.Message}");
                    }

                    return new SquadCommandResult(
                        false,
                        null,
                        stdout.ToString(),
                        stderr.ToString(),
                        $"{displayName} timed out after 3 minutes.");
                }

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                try {
                    await Task.WhenAll(outputClosed.Task, errorClosed.Task)
                        .WaitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                }

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
    Task<SquadCommandResult> RunAsync(
        ResolvedCliCandidate candidate,
        string activeDirectory,
        string displayName,
        IReadOnlyList<string> commandArguments);
}

internal sealed record SquadCommandResult(
    bool Success,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    string Message,
    IReadOnlyList<string>? MissingTools = null,
    string? InstallSource = null) {

    public string ToDisplayText() {
        var builder = new StringBuilder();
        builder.AppendLine(Message);

        if (!string.IsNullOrWhiteSpace(InstallSource)) {
            builder.AppendLine();
            builder.AppendLine($"Install source: {InstallSource}");
        }

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


