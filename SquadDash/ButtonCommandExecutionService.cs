using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SquadDash;

/// <summary>
/// Capture on click, before awaiting or consulting mutable UI state. Pass FolderPath from the
/// opened SessionWorkspace and its coordinator-supplied AppData state directory, never a cached branch.
/// </summary>
internal sealed record ButtonWorkspaceSnapshot(string FolderPath, string WorkspaceStateDirectory) {
    public static ButtonWorkspaceSnapshot Capture(SessionWorkspace workspace, string workspaceStateDirectory) {
        ArgumentNullException.ThrowIfNull(workspace);
        return new ButtonWorkspaceSnapshot(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.FolderPath)),
            ButtonsStore.ValidateStateDirectory(workspaceStateDirectory));
    }
}

internal sealed record ButtonInvocation(
    ButtonDefinition Button,
    ButtonWorkspaceSnapshot Workspace,
    string WorkspaceName,
    string Branch,
    string CommandText,
    string WorkingDirectory);

internal sealed record ButtonExecutionResult(
    ButtonDefinition Button,
    ButtonWorkspaceSnapshot Workspace,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    string? Error,
    string? CleanupError = null) {
    public bool Succeeded => ExitCode == 0 && Error is null;
}

/// <summary>
/// Step 2 handoff: await ExecuteAsync(savedButton, capturedWorkspace), then route the returned output
/// by result.Workspace, not the currently selected panel. Display Error and CleanupError to the user.
/// No confirmation, AI path, timeout, cancellation/kill switch, or UI-workspace lifetime coupling.
///
/// Each invocation owns a unique temporary script beneath the originating workspace state directory.
/// PowerShell uses one UTF-8 BOM .ps1, invoked by a fixed encoded UTF-8 console bootstrap
/// (no pasted-script command-line length limit or line-by-line scopes).
/// Raw commands use a UTF-8 .cmd with batch semantics; cmd echo is disabled and its code page set to UTF-8.
/// On ordinary completion only process handles and the temporary script are cleaned up. A runner IO/wait
/// failure attempts to kill its owned process tree, logging cleanup failures; it does not kill other processes.
/// Host termination can leave scripts/children behind. Detached descendants are not supervised, and inherited
/// stdout/stderr handles may delay completion. There is intentionally no arbitrary deployment deadline.
/// </summary>
internal sealed class ButtonCommandExecutionService {
    private const string PowerShellBootstrap =
        "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)\n" +
        "& $env:SQUADDASH_BUTTON_SCRIPT\n" +
        "if (-not $?) { if ($LASTEXITCODE) { exit $LASTEXITCODE }; exit 1 }\n";

    public async Task<ButtonExecutionResult> ExecuteAsync(ButtonDefinition button, ButtonWorkspaceSnapshot workspace) {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(workspace);
        string? scriptPath = null;
        string? cleanupError = null;
        ButtonExecutionResult result;
        try {
            // Null explicitly requests the existing identity path to read HEAD now, not use a UI branch cache.
            var identity = WorkspaceIdentityPresentation.Resolve(workspace.FolderPath, branch: null);
            var invocation = PrepareInvocation(button, workspace, identity.Branch);
            workspace = invocation.Workspace;
            if (button.Content.CommandType == ButtonCommandType.PowerShell &&
                !await Task.Run(WorkspaceIssueFactory.IsPowerShellAvailable).ConfigureAwait(false))
                throw new InvalidOperationException("PowerShell 7+ (pwsh.exe) is not available on PATH.");
            if (button.Content.CommandType == ButtonCommandType.Url) {
                var startInfo = BuildStartInfo(invocation, scriptPath: string.Empty);
                // Shell activation can reuse an existing browser and return no new process.
                using var browser = Process.Start(startInfo);
                SquadDashTrace.Write("Buttons", $"Button {button.Id} for '{workspace.FolderPath}' opened URL '{startInfo.FileName}'.");
                return new ButtonExecutionResult(button, workspace, 0, string.Empty, string.Empty, null);
            }

            var scriptDirectory = Path.Combine(workspace.WorkspaceStateDirectory, "button-runs");
            Directory.CreateDirectory(scriptDirectory);
            scriptPath = Path.Combine(scriptDirectory, Guid.NewGuid().ToString("N") +
                (button.Content.CommandType == ButtonCommandType.PowerShell ? ".ps1" : ".cmd"));
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier:
                button.Content.CommandType == ButtonCommandType.PowerShell);
            await File.WriteAllTextAsync(scriptPath, BuildScriptContent(invocation), encoding).ConfigureAwait(false);

            SquadDashTrace.Write("Buttons", $"Starting button {button.Id} for '{workspace.FolderPath}'.");
            var output = await RunProcessAsync(BuildStartInfo(invocation, scriptPath)).ConfigureAwait(false);
            var error = output.ExitCode == 0 ? null : $"Button command exited with code {output.ExitCode}.";
            result = new ButtonExecutionResult(button, workspace, output.ExitCode, output.Stdout, output.Stderr, error);
            SquadDashTrace.Write("Buttons", $"Button {button.Id} for '{workspace.FolderPath}' exited with code {output.ExitCode}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception
                                   or InvalidOperationException or ArgumentException or NotSupportedException
                                   or System.Security.SecurityException) {
            SquadDashTrace.Write("Buttons", $"Button {button.Id} for '{workspace.FolderPath}' failed: {ex.Message}");
            result = new ButtonExecutionResult(button, workspace, null, string.Empty, string.Empty, ex.Message);
        }
        finally {
            // Cleanup also runs if an unexpected exception propagates out of the service.
            if (scriptPath is not null) {
                try {
                    File.Delete(scriptPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) {
                    SquadDashTrace.Write("Buttons", $"Script cleanup failed for '{scriptPath}': {ex.Message}");
                    cleanupError = $"Could not remove temporary button script '{scriptPath}': {ex.Message}";
                }
            }
        }

        return result with { CleanupError = cleanupError };
    }

    /// <summary>
    /// Deterministic preparation seam: supply the invocation-time branch, not an editor-time value.
    /// Validates the workspace and working directory before any command process is launched.
    /// Branch availability is required, including for commands that do not interpolate it.
    /// </summary>
    public static ButtonInvocation PrepareInvocation(
        ButtonDefinition button, ButtonWorkspaceSnapshot workspace, string? branch) {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(workspace);
        ButtonsStore.ValidateContent(button.Content);
        if (button.Id == Guid.Empty || button.ContentRevision < 1)
            throw new ArgumentException("Execution requires a saved button definition.", nameof(button));
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace.FolderPath);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.FolderPath));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Workspace directory does not exist: {root}");
        if (string.IsNullOrWhiteSpace(branch) || branch.StartsWith("(detached", StringComparison.Ordinal) || branch == "HEAD")
            throw new InvalidOperationException("The workspace has no available Git branch (it may have detached HEAD). No command was launched.");

        var name = Path.GetFileName(root);
        var command = ButtonPlaceholders.Resolve(button.Content.CommandText, root, name, branch);
        var configuredDirectory = button.Content.WorkingDirectory;
        var workingDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? root
            : Path.GetFullPath(ButtonPlaceholders.Resolve(configuredDirectory, root, name, branch), root);
        if (!Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException($"Button working directory does not exist: {workingDirectory}");

        return new ButtonInvocation(button,
            new ButtonWorkspaceSnapshot(root, ButtonsStore.ValidateStateDirectory(workspace.WorkspaceStateDirectory)),
            name, branch, command, workingDirectory);
    }

    public static string BuildScriptContent(ButtonInvocation invocation) =>
        invocation.Button.Content.CommandType switch {
            ButtonCommandType.PowerShell => invocation.CommandText,
            ButtonCommandType.RawCommand => "@echo off\r\n" + invocation.CommandText,
            _ => throw new ArgumentException("Unsupported button command type.", nameof(invocation))
        };

    /// <summary>
    /// Inspectable launch seam. Only a fixed launcher travels on the command line, never the pasted script.
    /// The temporary script path is passed through this invocation's environment.
    /// cmd's outer quotes are intentional: /s strips those, preserving quotes around the script path.
    /// </summary>
    public static ProcessStartInfo BuildStartInfo(ButtonInvocation invocation, string scriptPath) {
        ArgumentNullException.ThrowIfNull(invocation);
        switch (invocation.Button.Content.CommandType) {
            case ButtonCommandType.Url:
                var url = ValidateBrowserUrl(invocation.CommandText);
                return new ProcessStartInfo {
                    FileName = url,
                    WorkingDirectory = invocation.WorkingDirectory,
                    UseShellExecute = true
                };
            case ButtonCommandType.PowerShell:
                ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
                var powerShellStartInfo = BuildCapturedStartInfo(invocation.WorkingDirectory, scriptPath);
                powerShellStartInfo.FileName = "pwsh.exe";
                foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-EncodedCommand",
                             Convert.ToBase64String(Encoding.Unicode.GetBytes(PowerShellBootstrap)) })
                    powerShellStartInfo.ArgumentList.Add(argument);
                return powerShellStartInfo;
            case ButtonCommandType.RawCommand:
                ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
                var rawStartInfo = BuildCapturedStartInfo(invocation.WorkingDirectory, scriptPath);
                rawStartInfo.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                // Environment expansion avoids reparsing percent characters in the AppData path.
                rawStartInfo.Arguments = "/d /s /c \"chcp 65001 >nul & \"%SQUADDASH_BUTTON_SCRIPT%\"\"";
                return rawStartInfo;
            default:
                throw new ArgumentException("Unsupported button command type.", nameof(invocation));
        }
    }

    private static ProcessStartInfo BuildCapturedStartInfo(string workingDirectory, string scriptPath) {
        var startInfo = new ProcessStartInfo {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.Environment["SQUADDASH_BUTTON_SCRIPT"] = scriptPath;
        return startInfo;
    }

    internal static string ValidateBrowserUrl(string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var url))
            throw new ArgumentException("Button URL must be an absolute URL.", nameof(value));
        if (!string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Button URL must use http or https.", nameof(value));
        if (string.IsNullOrWhiteSpace(url.Host))
            throw new ArgumentException("Button URL must include a host name.", nameof(value));
        return url.AbsoluteUri;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(ProcessStartInfo startInfo) {
        using var process = new Process { StartInfo = startInfo };
        var started = false;
        try {
            started = process.Start();
            if (!started)
                throw new InvalidOperationException($"Could not start '{startInfo.FileName}'.");
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync()).ConfigureAwait(false);
            return (process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception) {
            if (started) {
                try {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (Exception cleanupError) when (cleanupError is Win32Exception or InvalidOperationException or NotSupportedException) {
                    SquadDashTrace.Write("Buttons", $"Process cleanup failed after '{ex.Message}': {cleanupError.Message}");
                }
            }
            throw;
        }
    }
}
