using System;
using System.Threading.Tasks;

namespace SquadDash;

/// <summary>
/// User-declared preference for selecting a Squad CLI installation.
/// </summary>
public enum CliSelectionMode
{
    Auto,
    WorkspaceLocal,
    GlobalNpm,
    NativeSystem,
    CustomPath
}

/// <summary>
/// Concrete installation source detected by the resolution service.
/// </summary>
public enum CliInstallSource
{
    WorkspaceNpm,
    GlobalNpm,
    NativeOther,
    Custom,
    Unknown
}

/// <summary>
/// Invocation runtime style for a resolved CLI candidate.
/// </summary>
public enum CliRuntimeType
{
    NodeScript,
    NpmShim,
    NativeExecutable
}

/// <summary>
/// Capability status for agent command support.
/// </summary>
public enum CliCapabilityStatus
{
    Supported,
    NotSupported,
    Unknown
}

public record CliUpdateInstruction(
    string? UpdateCommand,
    bool CanAutoUpdate,
    string? DisplayMessage,
    string? TargetVersion);

public record ResolvedCliCandidate(
    CliSelectionMode SelectionMode,
    CliInstallSource DetectedSource,
    CliRuntimeType RuntimeType,
    string ExecutablePath,
    string? EntryScriptPath,
    string? Version,
    bool IsAvailable,
    CliCapabilityStatus AgentCmdSupport,
    string? ProbeError,
    CliUpdateInstruction UpdateInstruction);

public interface ISquadCliResolutionService
{
    Task<ResolvedCliCandidate> ResolveAsync(string workspaceDir);
    Task<ResolvedCliCandidate> ResolveWithModeAsync(CliSelectionMode mode, string workspaceDir);
    Task<ResolvedCliCandidate> ProbeVersionAsync(ResolvedCliCandidate candidate);
}

public record CliInvocationResult(
    int ExitCode,
    string? Stdout,
    string? Stderr,
    bool TimedOut);

public interface IProcessRunner
{
    Task<CliInvocationResult> RunAsync(string fileName, string[] args, TimeSpan timeout);
}

public interface IEnvironmentProvider
{
    string? GetEnvironmentVariable(string name, EnvironmentVariableTarget target);
    bool FileExists(string path);
}
