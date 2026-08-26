using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SquadDash;

internal sealed class SquadCliResolutionService : ISquadCliResolutionService
{
    private const string SquadPackageName = "@bradygaster/squad-cli";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly ParsedSemVersion AgentCmdMinimumVersion = new(0, 9, 5);
    private static readonly Regex SemVersionRegex = new(
        @"(?<!\d)(?:v)?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-[0-9A-Za-z\.-]+)?(?:\+[0-9A-Za-z\.-]+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IProcessRunner _processRunner;
    private readonly IEnvironmentProvider _environmentProvider;
    private readonly ISquadCliSelectionStore? _selectionStore;

    public SquadCliResolutionService(
        IProcessRunner processRunner,
        IEnvironmentProvider environmentProvider,
        ISquadCliSelectionStore? selectionStore = null)
    {
        _processRunner = processRunner;
        _environmentProvider = environmentProvider;
        _selectionStore = selectionStore;
    }

    public Task<ResolvedCliCandidate> ResolveAsync(string workspaceDir)
    {
        var mode = _selectionStore?.GetSelectionMode(workspaceDir) ?? CliSelectionMode.Auto;
        if (mode != CliSelectionMode.CustomPath)
            return ResolveWithModeAsync(mode, workspaceDir);

        var customPath = _selectionStore?.GetCustomPath(workspaceDir);
        return ResolveWithModeAsync(mode, customPath ?? string.Empty);
    }

    public async Task<ResolvedCliCandidate> ResolveWithModeAsync(CliSelectionMode mode, string workspaceDir)
    {
        var mergedPathEntries = GetMergedPathEntries();

        ResolvedCliCandidate candidate = mode switch
        {
            CliSelectionMode.Auto => await ResolveAutoAsync(workspaceDir, mergedPathEntries).ConfigureAwait(false),
            CliSelectionMode.WorkspaceLocal => ResolveWorkspaceLocal(workspaceDir, mergedPathEntries),
            CliSelectionMode.GlobalNpm => await ResolveGlobalNpmAsync(mode, workspaceDir, mergedPathEntries).ConfigureAwait(false),
            CliSelectionMode.NativeSystem => await ResolveNativeSystemAsync(mode, mergedPathEntries).ConfigureAwait(false),
            CliSelectionMode.CustomPath => ResolveCustomPath(workspaceDir),
            _ => CreateUnavailableCandidate(mode, CliInstallSource.Unknown)
        };

        return await ProbeVersionAsync(candidate).ConfigureAwait(false);
    }

    public async Task<ResolvedCliCandidate> ProbeVersionAsync(ResolvedCliCandidate candidate)
    {
        var version = candidate.Version;
        var capability = candidate.AgentCmdSupport;
        var probeError = candidate.ProbeError;

        if (candidate.IsAvailable)
        {
            var probeResult = await RunVersionProbeAsync(candidate).ConfigureAwait(false);
            if (probeResult.TimedOut)
            {
                capability = CliCapabilityStatus.Unknown;
                probeError = "Timed out while probing CLI version.";
            }
            else if (probeResult.ExitCode != 0)
            {
                capability = CliCapabilityStatus.Unknown;
                probeError = BuildProbeError(probeResult);
            }
            else if (TryParseSemVersion(probeResult.Stdout, out var parsedInstalled, out var normalizedInstalled))
            {
                version = normalizedInstalled;
                capability = parsedInstalled >= AgentCmdMinimumVersion
                    ? CliCapabilityStatus.Supported
                    : CliCapabilityStatus.NotSupported;
                probeError = null;
            }
            else
            {
                capability = CliCapabilityStatus.Unknown;
                probeError = "Unable to parse CLI version output.";
            }
        }

        var targetVersion = await ProbeTargetVersionAsync().ConfigureAwait(false);
        var updateInstruction = CreateUpdateInstruction(candidate, version, targetVersion);
        return candidate with
        {
            Version = version,
            AgentCmdSupport = capability,
            ProbeError = probeError,
            UpdateInstruction = updateInstruction
        };
    }

    private async Task<ResolvedCliCandidate> ResolveAutoAsync(
        string workspaceDir,
        IReadOnlyList<string> mergedPathEntries)
    {
        var workspace = ResolveWorkspaceLocal(workspaceDir, mergedPathEntries);
        if (workspace.IsAvailable)
        {
            return workspace with { SelectionMode = CliSelectionMode.Auto };
        }

        var global = await ResolveGlobalNpmAsync(CliSelectionMode.Auto, workspaceDir, mergedPathEntries).ConfigureAwait(false);
        if (global.IsAvailable)
        {
            return global;
        }

        var native = await ResolveNativeSystemAsync(CliSelectionMode.Auto, mergedPathEntries).ConfigureAwait(false);
        if (native.IsAvailable)
        {
            return native;
        }

        return CreateUnavailableCandidate(CliSelectionMode.Auto, CliInstallSource.Unknown);
    }

    private ResolvedCliCandidate ResolveWorkspaceLocal(string workspaceDir, IReadOnlyList<string> mergedPathEntries)
    {
        var shimPath = Path.Combine(workspaceDir, "node_modules", ".bin", "squad.cmd");
        var hasShim = _environmentProvider.FileExists(shimPath);
        var packageRoot = Path.Combine(workspaceDir, "node_modules", "@bradygaster", "squad-cli");
        var entryScriptPath = ResolveEntryScriptPath(packageRoot);
        var nodeExePath = FindExecutableOnPath(mergedPathEntries, "node.exe");

        if (!string.IsNullOrWhiteSpace(entryScriptPath) && !string.IsNullOrWhiteSpace(nodeExePath))
        {
            return CreateResolvedCandidate(
                CliSelectionMode.WorkspaceLocal,
                CliInstallSource.WorkspaceNpm,
                CliRuntimeType.NodeScript,
                nodeExePath,
                entryScriptPath);
        }

        if (hasShim)
        {
            return CreateResolvedCandidate(
                CliSelectionMode.WorkspaceLocal,
                CliInstallSource.WorkspaceNpm,
                CliRuntimeType.NpmShim,
                shimPath);
        }

        return CreateUnavailableCandidate(
            CliSelectionMode.WorkspaceLocal,
            CliInstallSource.WorkspaceNpm,
            "Run 'npm install --save-dev @bradygaster/squad-cli' to install Squad CLI in this workspace.");
    }

    private async Task<ResolvedCliCandidate> ResolveGlobalNpmAsync(
        CliSelectionMode selectionMode,
        string workspaceDir,
        IReadOnlyList<string> mergedPathEntries)
    {
        var npmPrefixResult = await ProbeNpmPrefixAsync().ConfigureAwait(false);
        foreach (var pathEntry in mergedPathEntries)
        {
            if (ContainsNodeModules(pathEntry))
            {
                continue;
            }

            var shimPath = Path.Combine(pathEntry, "squad.cmd");
            if (!_environmentProvider.FileExists(shimPath))
            {
                continue;
            }

            var shimDirectory = Path.GetDirectoryName(shimPath) ?? pathEntry;
            if (npmPrefixResult.Prefix is not null && IsPathWithin(shimDirectory, npmPrefixResult.Prefix))
            {
                var packageRoot = Path.Combine(npmPrefixResult.Prefix, "node_modules", "@bradygaster", "squad-cli");
                var entryScriptPath = ResolveEntryScriptPath(packageRoot);
                if (!string.IsNullOrWhiteSpace(entryScriptPath))
                {
                    var nodeExePath = FindExecutableOnPath(mergedPathEntries, "node.exe");
                    if (!string.IsNullOrWhiteSpace(nodeExePath))
                    {
                        return CreateResolvedCandidate(
                            selectionMode,
                            CliInstallSource.GlobalNpm,
                            CliRuntimeType.NodeScript,
                            nodeExePath,
                            entryScriptPath);
                    }
                }

                return CreateResolvedCandidate(
                    selectionMode,
                    CliInstallSource.GlobalNpm,
                    CliRuntimeType.NpmShim,
                    shimPath);
            }

            return CreateResolvedCandidate(
                selectionMode,
                CliInstallSource.NativeOther,
                CliRuntimeType.NpmShim,
                shimPath);
        }

        return CreateUnavailableCandidate(selectionMode, CliInstallSource.GlobalNpm);
    }

    private async Task<ResolvedCliCandidate> ResolveNativeSystemAsync(
        CliSelectionMode selectionMode,
        IReadOnlyList<string> mergedPathEntries)
    {
        var npmPrefixResult = await ProbeNpmPrefixAsync().ConfigureAwait(false);
        foreach (var pathEntry in mergedPathEntries)
        {
            if (ContainsNodeModules(pathEntry))
            {
                continue;
            }

            var exePath = Path.Combine(pathEntry, "squad.exe");
            if (_environmentProvider.FileExists(exePath))
            {
                return CreateResolvedCandidate(
                    selectionMode,
                    CliInstallSource.NativeOther,
                    CliRuntimeType.NativeExecutable,
                    exePath);
            }

            var cmdPath = Path.Combine(pathEntry, "squad.cmd");
            if (!_environmentProvider.FileExists(cmdPath))
            {
                continue;
            }

            if (npmPrefixResult.Prefix is not null && IsPathWithin(pathEntry, npmPrefixResult.Prefix))
            {
                continue;
            }

            return CreateResolvedCandidate(
                selectionMode,
                CliInstallSource.NativeOther,
                CliRuntimeType.NpmShim,
                cmdPath);
        }

        return CreateUnavailableCandidate(selectionMode, CliInstallSource.NativeOther);
    }

    private ResolvedCliCandidate ResolveCustomPath(string customPath)
    {
        if (!_environmentProvider.FileExists(customPath))
        {
            return CreateUnavailableCandidate(
                CliSelectionMode.CustomPath,
                CliInstallSource.Custom,
                "Check your custom installation source for updates.");
        }

        var runtimeType = GetRuntimeTypeFromExtension(customPath);
        return CreateResolvedCandidate(
            CliSelectionMode.CustomPath,
            CliInstallSource.Custom,
            runtimeType,
            customPath);
    }

    private async Task<CliInvocationResult> RunVersionProbeAsync(ResolvedCliCandidate candidate)
    {
        if (candidate.RuntimeType == CliRuntimeType.NpmShim)
        {
            var cmdInvocation = $"{CliCmdEscaping.CmdQuoteToken(candidate.ExecutablePath)} --version";
            return await _processRunner
                .RunAsync("cmd.exe", ["/d", "/s", "/c", cmdInvocation], ProbeTimeout)
                .ConfigureAwait(false);
        }

        if (candidate.RuntimeType == CliRuntimeType.NodeScript && !string.IsNullOrWhiteSpace(candidate.EntryScriptPath))
        {
            return await _processRunner
                .RunAsync(candidate.ExecutablePath, [candidate.EntryScriptPath, "--version"], ProbeTimeout)
                .ConfigureAwait(false);
        }

        return await _processRunner
            .RunAsync(candidate.ExecutablePath, ["--version"], ProbeTimeout)
            .ConfigureAwait(false);
    }

    private async Task<string?> ProbeTargetVersionAsync()
    {
        var target = await RunNpmAsync(["view", SquadPackageName, "version"]).ConfigureAwait(false);

        return target.ExitCode == 0 && !target.TimedOut && TryParseSemVersion(target.Stdout, out _, out var parsed)
            ? parsed
            : null;
    }

    private async Task<(string? Prefix, bool TimedOut)> ProbeNpmPrefixAsync()
    {
        var result = await RunNpmAsync(["config", "get", "prefix"]).ConfigureAwait(false);

        if (result.TimedOut || result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
        {
            return (null, result.TimedOut);
        }

        var normalized = NormalizePath(result.Stdout.Trim());
        return (normalized, false);
    }

    private Task<CliInvocationResult> RunNpmAsync(string[] arguments)
    {
        if (OperatingSystem.IsWindows())
        {
            var mergedPathEntries = GetMergedPathEntries();
            var nodeExePath = FindExecutableOnPath(mergedPathEntries, "node.exe");
            if (!string.IsNullOrWhiteSpace(nodeExePath))
            {
                foreach (var pathEntry in mergedPathEntries)
                {
                    var npmCliPath = Path.Combine(pathEntry, "node_modules", "npm", "bin", "npm-cli.js");
                    if (_environmentProvider.FileExists(npmCliPath))
                    {
                        return _processRunner.RunAsync(
                            nodeExePath,
                            new[] { npmCliPath }.Concat(arguments).ToArray(),
                            ProbeTimeout);
                    }
                }
            }

            var npmExePath = FindExecutableOnPath(mergedPathEntries, "npm.exe");
            if (!string.IsNullOrWhiteSpace(npmExePath))
            {
                return _processRunner.RunAsync(npmExePath, arguments, ProbeTimeout);
            }
        }

        return _processRunner.RunAsync("npm", arguments, ProbeTimeout);
    }

    private CliUpdateInstruction CreateUpdateInstruction(
        ResolvedCliCandidate candidate,
        string? installedVersion,
        string? targetVersion)
    {
        var canAutoUpdate = false;
        if (candidate.IsAvailable &&
            (candidate.DetectedSource is CliInstallSource.WorkspaceNpm or CliInstallSource.GlobalNpm) &&
            TryParseSemVersion(installedVersion, out var installedParsed, out _) &&
            TryParseSemVersion(targetVersion, out var targetParsed, out _))
        {
            canAutoUpdate = targetParsed > installedParsed;
        }

        return candidate.DetectedSource switch
        {
            CliInstallSource.WorkspaceNpm when !candidate.IsAvailable =>
                new CliUpdateInstruction(
                    UpdateCommand: null,
                    CanAutoUpdate: false,
                    DisplayMessage: "Run 'npm install --save-dev @bradygaster/squad-cli' to install Squad CLI in this workspace.",
                    TargetVersion: targetVersion),

            CliInstallSource.WorkspaceNpm =>
                new CliUpdateInstruction(
                    UpdateCommand: canAutoUpdate ? $"npm install --save-dev {SquadPackageName}@{targetVersion}" : null,
                    CanAutoUpdate: canAutoUpdate,
                    DisplayMessage: null,
                    TargetVersion: targetVersion),

            CliInstallSource.GlobalNpm =>
                new CliUpdateInstruction(
                    UpdateCommand: canAutoUpdate ? $"npm install -g {SquadPackageName}@{targetVersion}" : null,
                    CanAutoUpdate: canAutoUpdate,
                    DisplayMessage: null,
                    TargetVersion: targetVersion),

            CliInstallSource.NativeOther =>
                new CliUpdateInstruction(
                    UpdateCommand: null,
                    CanAutoUpdate: false,
                    DisplayMessage: "Squad CLI was installed outside npm. Check your installation source for updates.",
                    TargetVersion: targetVersion),

            CliInstallSource.Custom =>
                new CliUpdateInstruction(
                    UpdateCommand: null,
                    CanAutoUpdate: false,
                    DisplayMessage: "Check your custom installation source for updates.",
                    TargetVersion: targetVersion),

            _ => new CliUpdateInstruction(
                UpdateCommand: null,
                CanAutoUpdate: false,
                DisplayMessage: null,
                TargetVersion: targetVersion)
        };
    }

    private IReadOnlyList<string> GetMergedPathEntries()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<string>();
        foreach (var target in new[]
                 {
                     EnvironmentVariableTarget.Process,
                     EnvironmentVariableTarget.User,
                     EnvironmentVariableTarget.Machine
                 })
        {
            var pathValue = _environmentProvider.GetEnvironmentVariable("PATH", target);
            if (string.IsNullOrWhiteSpace(pathValue))
            {
                continue;
            }

            foreach (var entry in pathValue.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var normalized = NormalizePath(entry);
                if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
                {
                    continue;
                }

                merged.Add(normalized);
            }
        }

        return merged;
    }

    private string? ResolveEntryScriptPath(string packageRoot)
    {
        var packageJsonPath = Path.Combine(packageRoot, "package.json");
        var binValue = TryReadBinPathFromPackageJson(packageJsonPath);
        if (!string.IsNullOrWhiteSpace(binValue))
        {
            var candidatePath = Path.Combine(packageRoot, NormalizeRelativePath(binValue));
            if (_environmentProvider.FileExists(candidatePath))
            {
                return candidatePath;
            }
        }

        var fallbackPath = Path.Combine(packageRoot, "dist", "cli-entry.js");
        return _environmentProvider.FileExists(fallbackPath) ? fallbackPath : null;
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
    }

    private string? TryReadBinPathFromPackageJson(string packageJsonPath)
    {
        if (!_environmentProvider.FileExists(packageJsonPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
            if (!document.RootElement.TryGetProperty("bin", out var binElement))
            {
                return null;
            }

            if (binElement.ValueKind == JsonValueKind.String)
            {
                return binElement.GetString();
            }

            if (binElement.ValueKind == JsonValueKind.Object &&
                binElement.TryGetProperty("squad", out var squadElement) &&
                squadElement.ValueKind == JsonValueKind.String)
            {
                return squadElement.GetString();
            }
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("CliResolution", $"Unable to read package.json bin.squad path: {ex.Message}");
        }

        return null;
    }

    private string? FindExecutableOnPath(IReadOnlyList<string> mergedPathEntries, string fileName)
    {
        foreach (var pathEntry in mergedPathEntries)
        {
            var candidate = Path.Combine(pathEntry, fileName);
            if (_environmentProvider.FileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool TryParseSemVersion(string? value, out ParsedSemVersion version, out string normalized)
    {
        version = default;
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = SemVersionRegex.Match(value);
        if (!match.Success ||
            !int.TryParse(match.Groups["major"].Value, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, out var minor) ||
            !int.TryParse(match.Groups["patch"].Value, out var patch))
        {
            return false;
        }

        version = new ParsedSemVersion(major, minor, patch);
        normalized = $"{major}.{minor}.{patch}";
        return true;
    }

    private static string? BuildProbeError(CliInvocationResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Stderr))
        {
            return result.Stderr.Trim();
        }

        if (!string.IsNullOrWhiteSpace(result.Stdout))
        {
            return result.Stdout.Trim();
        }

        return $"CLI exited with code {result.ExitCode}.";
    }

    private static ResolvedCliCandidate CreateResolvedCandidate(
        CliSelectionMode selectionMode,
        CliInstallSource source,
        CliRuntimeType runtimeType,
        string executablePath,
        string? entryScriptPath = null)
    {
        return new ResolvedCliCandidate(
            SelectionMode: selectionMode,
            DetectedSource: source,
            RuntimeType: runtimeType,
            ExecutablePath: executablePath,
            EntryScriptPath: entryScriptPath,
            Version: null,
            IsAvailable: true,
            AgentCmdSupport: CliCapabilityStatus.Unknown,
            ProbeError: null,
            UpdateInstruction: new CliUpdateInstruction(null, false, null, null));
    }

    private static ResolvedCliCandidate CreateUnavailableCandidate(
        CliSelectionMode selectionMode,
        CliInstallSource source,
        string? displayMessage = null)
    {
        return new ResolvedCliCandidate(
            SelectionMode: selectionMode,
            DetectedSource: source,
            RuntimeType: CliRuntimeType.NpmShim,
            ExecutablePath: string.Empty,
            EntryScriptPath: null,
            Version: null,
            IsAvailable: false,
            AgentCmdSupport: CliCapabilityStatus.Unknown,
            ProbeError: null,
            UpdateInstruction: new CliUpdateInstruction(
                UpdateCommand: null,
                CanAutoUpdate: false,
                DisplayMessage: displayMessage,
                TargetVersion: null));
    }

    private static CliRuntimeType GetRuntimeTypeFromExtension(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".exe" => CliRuntimeType.NativeExecutable,
            ".cmd" => CliRuntimeType.NpmShim,
            _ => CliRuntimeType.NodeScript
        };
    }

    private static bool ContainsNodeModules(string path)
    {
        return path.IndexOf("node_modules", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsPathWithin(string candidatePath, string containerPath)
    {
        var normalizedCandidate = NormalizePath(candidatePath);
        var normalizedContainer = NormalizePath(containerPath);
        if (normalizedCandidate.Length == 0 || normalizedContainer.Length == 0)
        {
            return false;
        }

        if (string.Equals(normalizedCandidate, normalizedContainer, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = normalizedContainer.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedContainer
            : normalizedContainer + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        var trimmed = path.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(trimmed)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("CliResolution", $"Path normalization failed for '{trimmed}': {ex.Message}");
            return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private readonly record struct ParsedSemVersion(int Major, int Minor, int Patch) : IComparable<ParsedSemVersion>
    {
        public int CompareTo(ParsedSemVersion other)
        {
            var major = Major.CompareTo(other.Major);
            if (major != 0)
            {
                return major;
            }

            var minor = Minor.CompareTo(other.Minor);
            if (minor != 0)
            {
                return minor;
            }

            return Patch.CompareTo(other.Patch);
        }

        public static bool operator >(ParsedSemVersion left, ParsedSemVersion right) => left.CompareTo(right) > 0;
        public static bool operator <(ParsedSemVersion left, ParsedSemVersion right) => left.CompareTo(right) < 0;
        public static bool operator >=(ParsedSemVersion left, ParsedSemVersion right) => left.CompareTo(right) >= 0;
        public static bool operator <=(ParsedSemVersion left, ParsedSemVersion right) => left.CompareTo(right) <= 0;
    }
}
