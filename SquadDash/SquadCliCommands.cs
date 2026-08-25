using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SquadDash;

internal static class SquadCliCommands {
    public const string LocalCliEntryPath = "node_modules\\@bradygaster\\squad-cli\\dist\\cli-entry.js";

    public static SquadCliCommandDefinition InstallLocalCli { get; } =
        new("cmd.exe", "/c npm install --save-dev @bradygaster/squad-cli", "Install local Squad CLI");

    // Use node + local path (same pattern as StartWatch) to avoid npx prompting
    // "Need to install? (y)" which blocks forever when stdin is not a real terminal.
    public static SquadCliCommandDefinition Init { get; } =
        new("node", $"\"{LocalCliEntryPath}\" init", "Install Squad");

    public static SquadCliCommandDefinition Doctor { get; } =
        new("cmd.exe", "/c npx @bradygaster/squad-cli doctor", "Run Squad Doctor");

    public static SquadCliCommandDefinition WatchHealth { get; } =
        new("node", $"\"{LocalCliEntryPath}\" watch --health", "Check Squad Watch Health");

    public static SquadCliCommandDefinition StartWatch(
        int intervalMinutes,
        bool execute,
        bool verbose,
        string? notifyLevel) {
        var interval = Math.Clamp(intervalMinutes, 1, 1440);
        var arguments = $"\"{LocalCliEntryPath}\" watch --interval {interval}";
        if (execute)
            arguments += " --execute";
        if (verbose)
            arguments += " --verbose";
        if (!string.IsNullOrWhiteSpace(notifyLevel) &&
            notifyLevel is "all" or "important" or "none") {
            arguments += $" --notify-level {notifyLevel}";
        }

        return new SquadCliCommandDefinition("node", arguments, "Start Squad Watch");
    }

    public static SquadCliCommandDefinition DiscoverSquads { get; } =
        new("node", $"\"{LocalCliEntryPath}\" discover", "Discover Squads");

    // Verified by inspecting the installed v0.10.0 command router and upgrade implementation.
    // Never execute `upgrade --help` in an active workspace: v0.10.0 dispatches `upgrade`
    // before examining the trailing help flag and performs a real workspace upgrade.
    public const bool WorkspaceUpgradeDryRunSupported = false;
    public const string WorkspaceUpgradeDryRunUnsupportedMessage =
        "Workspace upgrade dry-run is not supported by the installed Squad CLI version. Use Apply Workspace Update to proceed directly.";

    public static IReadOnlyList<string> WorkspaceUpgradeDryRunArguments { get; } = [];

    public static IReadOnlyList<string> WorkspaceUpgradeApplyArguments { get; } =
        ["upgrade"];

    internal static ProcessStartInfo BuildCommandStartInfo(
        ResolvedCliCandidate candidate,
        string workingDirectory,
        IReadOnlyList<string> commandArguments)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentNullException.ThrowIfNull(commandArguments);

        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory
        };

        switch (candidate.RuntimeType)
        {
            case CliRuntimeType.NpmShim:
                startInfo.FileName = "cmd.exe";
                startInfo.ArgumentList.Add("/d");
                startInfo.ArgumentList.Add("/s");
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add(BuildCmdInvocation(candidate.ExecutablePath, commandArguments));
                break;
            case CliRuntimeType.NodeScript:
                if (string.IsNullOrWhiteSpace(candidate.EntryScriptPath))
                {
                    throw new InvalidOperationException("NodeScript runtime requires EntryScriptPath.");
                }

                startInfo.FileName = candidate.ExecutablePath;
                startInfo.ArgumentList.Add(candidate.EntryScriptPath);
                AddArgumentTokens(startInfo, commandArguments);
                break;
            case CliRuntimeType.NativeExecutable:
                startInfo.FileName = candidate.ExecutablePath;
                AddArgumentTokens(startInfo, commandArguments);
                break;
            default:
                throw new InvalidOperationException($"Unsupported CLI runtime type: {candidate.RuntimeType}.");
        }

        return startInfo;
    }

    internal static ProcessStartInfo BuildCommandStartInfo(
        ResolvedCliCandidate candidate,
        string workingDirectory,
        params string[] commandArguments)
    {
        return BuildCommandStartInfo(candidate, workingDirectory, (IReadOnlyList<string>)commandArguments);
    }

    private static string BuildCmdInvocation(string executablePath, IReadOnlyList<string> commandArguments)
    {
        var allTokens = new List<string>(commandArguments.Count + 1) { executablePath };
        allTokens.AddRange(commandArguments);
        return string.Join(" ", allTokens.Select(CliCmdEscaping.CmdQuoteToken));
    }

    private static void AddArgumentTokens(ProcessStartInfo startInfo, IReadOnlyList<string> commandArguments)
    {
        foreach (var argument in commandArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }
}

internal sealed record SquadCliCommandDefinition(
    string FileName,
    string Arguments,
    string DisplayName);
