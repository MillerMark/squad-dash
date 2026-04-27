using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace SquadDash;

internal sealed record WorkspaceIssuePresentation(
    string Title,
    string Message,
    string? DetailText = null,
    bool ShowInstallButton = false,
    bool ShowDoctorButton = false,
    string? HelpButtonLabel = null,
    string? HelpWindowTitle = null,
    string? HelpWindowContent = null,
    WorkspaceIssueAction? Action = null,
    WorkspaceIssueAction? SecondaryAction = null,
    WorkspaceIssueExternalLink? PrimaryLink = null,
    WorkspaceIssueExternalLink? SecondaryLink = null);

internal sealed record WorkspaceIssueAction(string Label, WorkspaceIssueActionKind Kind, string? Argument = null);
internal sealed record WorkspaceIssueExternalLink(string Label, string Url);

internal enum WorkspaceIssueActionKind {
    None,
    CopyText,
    LaunchPowerShellCommand
}

internal static class WorkspaceIssueFactory {
    private const string PowerShellInstallCommand =
        "winget install --id Microsoft.PowerShell --source winget --accept-source-agreements --accept-package-agreements --disable-interactivity";
    private const string PowerShellInstallDocsUrl =
        "https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows?view=powershell-7.5";
    private const string PowerShellReleasesUrl =
        "https://github.com/PowerShell/PowerShell/releases";

    public static bool IsPowerShellAvailable() => IsCommandAvailable("pwsh");

    public static bool IsMissingPowerShellIssue(WorkspaceIssuePresentation? issue) {
        return issue is not null &&
               issue.Title.Contains("PowerShell 7", StringComparison.OrdinalIgnoreCase);
    }

    public static WorkspaceIssuePresentation? CreateStartupIssue(
        SquadInstallationState? installationState,
        DeveloperStartupIssueSimulation simulation = DeveloperStartupIssueSimulation.None,
        string? applicationRoot = null) {
        if (simulation != DeveloperStartupIssueSimulation.None)
            return CreateSimulatedStartupIssue(simulation, installationState, applicationRoot);

        var missingTools = GetMissingTools();
        if (missingTools.Length > 0) {
            var toolList = string.Join(", ", missingTools);
            return new WorkspaceIssuePresentation(
                Title: "Node.js tooling is missing",
                Message: $"SquadDash needs {toolList} on PATH before it can install or run Squad in this repo.",
                DetailText: "Install a current Node.js LTS release, then restart SquadDash.",
                HelpButtonLabel: "View Setup Steps",
                HelpWindowTitle: "Node.js Setup",
                HelpWindowContent: BuildMissingToolHelpText(missingTools),
                PrimaryLink: new WorkspaceIssueExternalLink("Node.js Downloads", "https://nodejs.org/en/download"));
        }

        if (!IsPowerShellAvailable()) {
            return CreateMissingPowerShellIssue();
        }

        if (installationState is null || installationState.IsSquadInstalledForActiveDirectory)
            return null;

        if (installationState.IsWorkspaceInitialized && !installationState.HasLocalCliCommand) {
            return new WorkspaceIssuePresentation(
                Title: "Finish installing Squad for this folder",
                Message: "This repo already has a .squad workspace, but the local Squad CLI is still missing.",
                DetailText: "Install Squad to add the workspace-local CLI, then retry your prompt.",
                ShowInstallButton: true,
                HelpButtonLabel: "View Repair Steps",
                HelpWindowTitle: "Finish Installing Squad",
                HelpWindowContent: BuildPartialInstallHelpText(installationState));
        }

        return new WorkspaceIssuePresentation(
            Title: "Squad isn't installed for this folder yet",
            Message: "This repo needs a workspace-local Squad setup before prompts can run here.",
            DetailText: installationState.HasPackageManifest
                ? "Install Squad to create the missing .squad files and finish setup."
                : "Install Squad to create package.json, install the local CLI, and initialize .squad.",
            ShowInstallButton: true,
            HelpButtonLabel: "What Install Does",
            HelpWindowTitle: "Install Squad",
            HelpWindowContent: BuildFreshInstallHelpText(installationState));
    }

    public static WorkspaceIssuePresentation CreateRuntimeIssue(
        string errorMessage,
        SquadInstallationState? installationState,
        string? applicationRoot = null) {
        var normalizedMessage = errorMessage ?? string.Empty;

        if (IsMissingPowerShellError(normalizedMessage)) {
            return CreateMissingPowerShellIssue();
        }

        if (normalizedMessage.Contains(
                "Session was not created with authentication info or custom provider",
                StringComparison.OrdinalIgnoreCase)) {
            return new WorkspaceIssuePresentation(
                Title: "GitHub Copilot sign-in is required on this machine",
                Message: "SquadDash reached the bundled SDK, but GitHub Copilot is not authenticated for this Windows user yet.",
                DetailText: "Sign in once in the Copilot CLI, then retry the prompt.",
                HelpButtonLabel: "View Sign-in Steps",
                HelpWindowTitle: "GitHub Copilot Sign-in",
                HelpWindowContent: BuildCopilotAuthHelpText(),
                PrimaryLink: new WorkspaceIssueExternalLink("Copilot Docs", "https://docs.github.com/copilot"),
                SecondaryLink: new WorkspaceIssueExternalLink("GitHub Copilot", "https://github.com/features/copilot"));
        }

        if (IsBuildOutputLockError(normalizedMessage)) {
            var buildCommand = BuildRebuildCommand(applicationRoot);
            return new WorkspaceIssuePresentation(
                Title: "Build couldn't update temp files",
                Message: "The current build environment could not replace temporary files under SquadDash\\obj.",
                DetailText: "Run the build check in a normal PowerShell window to confirm whether this is a local environment restriction or a real file lock.",
                HelpButtonLabel: "View Rebuild Steps",
                HelpWindowTitle: "Rebuild Diagnostics",
                HelpWindowContent: BuildBuildLockHelpText(normalizedMessage, buildCommand),
                Action: new WorkspaceIssueAction("Run Build in PowerShell", WorkspaceIssueActionKind.LaunchPowerShellCommand, buildCommand),
                SecondaryAction: new WorkspaceIssueAction("Copy Build Command", WorkspaceIssueActionKind.CopyText, buildCommand));
        }

        if (normalizedMessage.Contains("Error parsing:", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("vscode-jsonrpc", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("tsx", StringComparison.OrdinalIgnoreCase)) {
            return new WorkspaceIssuePresentation(
                Title: "The bundled Squad SDK needs repair",
                Message: "A bundled Node dependency failed before the prompt could complete.",
                DetailText: "Use the repair steps below, then restart SquadDash and retry.",
                HelpButtonLabel: "View Repair Steps",
                HelpWindowTitle: "Bundled SDK Repair",
                HelpWindowContent: BuildBundledSdkRepairHelpText(normalizedMessage));
        }

        if (normalizedMessage.Contains("dependencies were not found under", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("compatibility repair failed", StringComparison.OrdinalIgnoreCase)) {
            return new WorkspaceIssuePresentation(
                Title: "Bundled Squad SDK files are missing or damaged",
                Message: "SquadDash couldn't prepare its bundled SDK runtime on this machine.",
                DetailText: "Rebuild the app and verify the bundled SDK dependencies are present.",
                HelpButtonLabel: "View Repair Steps",
                HelpWindowTitle: "Bundled SDK Repair",
                HelpWindowContent: BuildBundledSdkRepairHelpText(normalizedMessage));
        }

        return new WorkspaceIssuePresentation(
            Title: "Squad couldn't finish that prompt",
            Message: "The prompt stopped before Squad returned a complete answer.",
            DetailText: "Review the repair steps, then retry the prompt.",
            HelpButtonLabel: "View Diagnostics",
            HelpWindowTitle: "Squad Runtime Diagnostics",
            HelpWindowContent: BuildGenericRuntimeHelpText(normalizedMessage));
    }

    public static WorkspaceIssuePresentation CreateSimulatedRuntimeIssue(
        DeveloperRuntimeIssueSimulation simulation,
        SquadInstallationState? installationState) {
        var issue = CreateRuntimeIssue(CreateSimulatedRuntimeErrorMessage(simulation), installationState);
        return issue with {
            Title = $"Preview: {issue.Title}",
            DetailText = BuildSimulatedDetailText(issue.DetailText),
            ShowDoctorButton = false,
            ShowInstallButton = false
        };
    }

    public static string CreateSimulatedRuntimeErrorMessage(DeveloperRuntimeIssueSimulation simulation) {
        return simulation switch {
            DeveloperRuntimeIssueSimulation.CopilotAuthRequired =>
                "Execution failed: Error: Session was not created with authentication info or custom provider",
            DeveloperRuntimeIssueSimulation.BundledSdkRepair =>
                "node:internal/modules/run_main:107 Error parsing: C:\\Source\\SquadUI\\Squad.SDK\\node_modules\\vscode-jsonrpc\\package.json",
            DeveloperRuntimeIssueSimulation.BuildTempFiles =>
                "Access to the path 'C:\\Source\\SquadUI\\SquadDash\\obj\\Debug\\net10.0-windows\\SquadDash.tmp' is denied.",
            DeveloperRuntimeIssueSimulation.GenericRuntimeFailure =>
                "The Squad SDK process exited before the prompt completed.",
            _ => "The Squad SDK process exited before the prompt completed."
        };
    }

    private static WorkspaceIssuePresentation CreateSimulatedStartupIssue(
        DeveloperStartupIssueSimulation simulation,
        SquadInstallationState? installationState,
        string? applicationRoot = null) {
        return simulation switch {
            DeveloperStartupIssueSimulation.MissingNodeTooling =>
                new WorkspaceIssuePresentation(
                    Title: "Preview: Node.js tooling is missing",
                    Message: "SquadDash needs node, npm, and npx on PATH before it can install or run Squad in this repo.",
                    DetailText: BuildSimulatedDetailText("Install a current Node.js LTS release, then restart SquadDash."),
                    HelpButtonLabel: "View Setup Steps",
                    HelpWindowTitle: "Node.js Setup",
                    HelpWindowContent: BuildMissingToolHelpText(["node", "npm", "npx"]),
                    PrimaryLink: new WorkspaceIssueExternalLink("Node.js Downloads", "https://nodejs.org/en/download")),
            DeveloperStartupIssueSimulation.SquadNotInstalled =>
                new WorkspaceIssuePresentation(
                    Title: "Preview: Squad isn't installed for this folder yet",
                    Message: "This repo needs a workspace-local Squad setup before prompts can run here.",
                    DetailText: BuildSimulatedDetailText(
                        installationState?.HasPackageManifest == true
                            ? "Install Squad to create the missing .squad files and finish setup."
                            : "Install Squad to create package.json, install the local CLI, and initialize .squad."),
                    ShowInstallButton: true,
                    HelpButtonLabel: "What Install Does",
                    HelpWindowTitle: "Install Squad",
                    HelpWindowContent: BuildFreshInstallHelpText(installationState ?? CreateSimulationInstallationState(applicationRoot))),
            DeveloperStartupIssueSimulation.PartialSquadInstall =>
                new WorkspaceIssuePresentation(
                    Title: "Preview: Finish installing Squad for this folder",
                    Message: "This repo already has a .squad workspace, but the local Squad CLI is still missing.",
                    DetailText: BuildSimulatedDetailText("Install Squad to add the workspace-local CLI, then retry your prompt."),
                    ShowInstallButton: true,
                    HelpButtonLabel: "View Repair Steps",
                    HelpWindowTitle: "Finish Installing Squad",
                    HelpWindowContent: BuildPartialInstallHelpText(installationState ?? CreateSimulationInstallationState(applicationRoot))),
            _ => null!
        };
    }

    private static string[] GetMissingTools() {
        return new[] { "node", "npm", "npx" }
            .Where(tool => !IsCommandAvailable(tool))
            .ToArray();
    }

    private static string BuildSimulatedDetailText(string? detailText) {
        const string simulationNotice = "Developer simulation is active.";
        return string.IsNullOrWhiteSpace(detailText)
            ? simulationNotice
            : $"{detailText} {simulationNotice}";
    }

    private static bool IsCommandAvailable(string commandName) {
        var extensions = Path.HasExtension(commandName)
            ? new[] { string.Empty }
            : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var directory in EnumerateSearchDirectories()) {
            if (Path.HasExtension(commandName)) {
                if (File.Exists(Path.Combine(directory, commandName)))
                    return true;

                continue;
            }

            foreach (var extension in extensions) {
                if (File.Exists(Path.Combine(directory, commandName + extension)))
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateSearchDirectories() {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                var normalized = NormalizeDirectory(directory);
                if (seen.Add(normalized))
                    yield return normalized;
            }
        }
    }

    private static string NormalizeDirectory(string directory) {
        return Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string BuildMissingToolHelpText(IReadOnlyList<string> missingTools) {
        var builder = new StringBuilder();
        builder.AppendLine("SquadDash could not find the required Node.js tools on PATH.");
        builder.AppendLine();
        builder.AppendLine("Missing:");
        builder.AppendLine(string.Join(", ", missingTools));
        builder.AppendLine();
        builder.AppendLine("What to do");
        builder.AppendLine("1. Install a current Node.js LTS release on this machine.");
        builder.AppendLine("2. Close every SquadDash window.");
        builder.AppendLine("3. Open a new terminal so PATH refreshes.");
        builder.AppendLine("4. Restart SquadDash and try again.");
        return builder.ToString().TrimEnd();
    }

    private static string BuildFreshInstallHelpText(SquadInstallationState installationState) {
        var builder = new StringBuilder();
        builder.AppendLine($"Folder: {installationState.ActiveDirectory}");
        builder.AppendLine();
        builder.AppendLine("Install Squad will:");
        builder.AppendLine("1. Create package.json if this repo does not already have one.");
        builder.AppendLine("2. Install the workspace-local Squad CLI into node_modules.");
        builder.AppendLine("3. Apply the Windows compatibility patches SquadDash expects.");
        builder.AppendLine("4. Initialize the .squad workspace for this repo.");
        return builder.ToString().TrimEnd();
    }

    private static string BuildPartialInstallHelpText(SquadInstallationState installationState) {
        var builder = new StringBuilder();
        builder.AppendLine($"Folder: {installationState.ActiveDirectory}");
        builder.AppendLine();
        builder.AppendLine("This repo already has .squad files, but the local Squad CLI is missing.");
        builder.AppendLine();
        builder.AppendLine("What to do");
        builder.AppendLine("1. Click Install Squad to restore the workspace-local CLI.");
        builder.AppendLine("2. If Install Squad still fails, use Cleanup > Run Squad Doctor to inspect the local setup.");
        builder.AppendLine("3. Retry the prompt after repair completes.");
        return builder.ToString().TrimEnd();
    }

    private static string BuildCopilotAuthHelpText() {
        var builder = new StringBuilder();
        builder.AppendLine("SquadDash uses the local GitHub Copilot CLI session on this machine.");
        builder.AppendLine();
        builder.AppendLine("What to do");
        builder.AppendLine("1. Open PowerShell.");
        builder.AppendLine("2. Change to your repo root.");
        builder.AppendLine("3. Run .\\node_modules\\.bin\\copilot.cmd");
        builder.AppendLine("4. In the Copilot CLI, run /login");
        builder.AppendLine("5. Complete the GitHub sign-in flow.");
        builder.AppendLine("6. Restart SquadDash and retry the prompt.");
        builder.AppendLine();
        builder.AppendLine("Optional");
        builder.AppendLine("Run gh auth status to confirm your GitHub sign-in state.");
        return builder.ToString().TrimEnd();
    }

    private static WorkspaceIssuePresentation CreateMissingPowerShellIssue() {
        return new WorkspaceIssuePresentation(
            Title: "PowerShell 7 is required on this machine",
            Message: "Some Squad tasks need PowerShell 7+ (`pwsh.exe`), but SquadDash couldn't find it on PATH.",
            DetailText: "Install PowerShell 7, then restart SquadDash and retry the task.",
            HelpButtonLabel: "View Install Steps",
            HelpWindowTitle: "Install PowerShell 7",
            HelpWindowContent: BuildMissingPowerShellHelpText(),
            Action: new WorkspaceIssueAction("Install PowerShell 7", WorkspaceIssueActionKind.LaunchPowerShellCommand, PowerShellInstallCommand),
            SecondaryAction: new WorkspaceIssueAction("Copy Install Command", WorkspaceIssueActionKind.CopyText, PowerShellInstallCommand),
            PrimaryLink: new WorkspaceIssueExternalLink("PowerShell Install Docs", PowerShellInstallDocsUrl),
            SecondaryLink: new WorkspaceIssueExternalLink("PowerShell Releases", PowerShellReleasesUrl));
    }

    private static string BuildMissingPowerShellHelpText() {
        var builder = new StringBuilder();
        builder.AppendLine("SquadDash could not find PowerShell 7+ (`pwsh.exe`) on PATH.");
        builder.AppendLine();
        builder.AppendLine("What to do");
        builder.AppendLine("1. Click Install PowerShell 7 to start the standard Windows install.");
        builder.AppendLine("2. If you prefer to run it yourself, use this command:");
        builder.AppendLine(PowerShellInstallCommand);
        builder.AppendLine("3. Close every SquadDash window after the install finishes.");
        builder.AppendLine("4. Open SquadDash again and retry the task.");
        return builder.ToString().TrimEnd();
    }

    private static string BuildBundledSdkRepairHelpText(string rawError) {
        var builder = new StringBuilder();
        builder.AppendLine("The bundled SDK runtime failed before your prompt could finish.");
        builder.AppendLine();
        builder.AppendLine("What to do");
        builder.AppendLine("1. Close every SquadDash window.");
        builder.AppendLine("2. Rebuild and relaunch SquadDash from this repo.");
        builder.AppendLine("3. If the error persists, reinstall dependencies in the bundled SDK folder and rebuild again.");
        builder.AppendLine();
        builder.AppendLine("Raw error");
        builder.AppendLine(rawError);
        return builder.ToString().TrimEnd();
    }

    private static string BuildGenericRuntimeHelpText(string rawError) {
        var builder = new StringBuilder();
        builder.AppendLine("SquadDash started the prompt, but the runtime stopped before the answer completed.");
        builder.AppendLine();
        builder.AppendLine("What to do");
        builder.AppendLine("1. Retry the prompt once.");
        builder.AppendLine("2. If it fails again, use Cleanup > Run Squad Doctor.");
        builder.AppendLine("3. Review the raw error below for the exact failing component.");
        builder.AppendLine();
        builder.AppendLine("Raw error");
        builder.AppendLine(rawError);
        return builder.ToString().TrimEnd();
    }

    private static bool IsBuildOutputLockError(string errorMessage) {
        return errorMessage.Contains("Access to the path", StringComparison.OrdinalIgnoreCase) &&
               errorMessage.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase) &&
               errorMessage.Contains(".tmp", StringComparison.OrdinalIgnoreCase) &&
               errorMessage.Contains("denied", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissingPowerShellError(string errorMessage) {
        return errorMessage.Contains("pwsh.exe", StringComparison.OrdinalIgnoreCase) &&
               (errorMessage.Contains("PowerShell 6+", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("PowerShell 7", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("not available in this environment", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("isn't installed on this machine", StringComparison.OrdinalIgnoreCase));
    }

    private static SquadInstallationState CreateSimulationInstallationState(string? applicationRoot = null) {
        var folder = applicationRoot ?? string.Empty;
        return new SquadInstallationState(
            folder,
            Path.Combine(folder, ".squad"),
            Path.Combine(folder, ".squad", "team.md"),
            Path.Combine(folder, "package.json"),
            Path.Combine(folder, "node_modules", ".bin", "squad.cmd"),
            IsWorkspaceInitialized: false,
            HasPackageManifest: true,
            HasLocalCliCommand: false,
            IsSquadInstalledForActiveDirectory: false);
    }

    private static string BuildRebuildCommand(string? applicationRoot = null) {
        var solutionPath = Path.Combine(applicationRoot ?? string.Empty, "SquadUI.slnx");
        return $"dotnet build \"{solutionPath}\" --no-restore -verbosity:quiet";
    }

    private static string BuildBuildLockHelpText(string rawError, string buildCommand) {
        var builder = new StringBuilder();
        builder.AppendLine("The build did not fail because of PowerShell syntax.");
        builder.AppendLine("It failed while trying to replace temporary files under SquadDash\\obj.");
        builder.AppendLine();
        builder.AppendLine("What to do");
        builder.AppendLine("1. Click Run Build in PowerShell to open a normal terminal and run the verification build automatically.");
        builder.AppendLine("2. If you prefer to run it yourself, use this command:");
        builder.AppendLine(buildCommand);
        builder.AppendLine("3. If that succeeds, the original failure came from the restricted build environment, not from SquadDash itself.");
        builder.AppendLine("4. If it still fails in normal PowerShell, then inspect running dotnet or MSBuild processes and retry.");
        builder.AppendLine();
        builder.AppendLine("Raw error");
        builder.AppendLine(rawError);
        return builder.ToString().TrimEnd();
    }

}
