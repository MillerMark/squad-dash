using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace SquadDash;

internal sealed class SquadCliAdapter {
    private readonly IWorkspacePaths _workspacePaths;
    private readonly Action<string, Exception> _onError;
    private string? _squadVersion;
    private string? _lastObservedModel;

    public string? SquadVersion => _squadVersion;

    public string? LastObservedModel {
        get => _lastObservedModel;
        set => _lastObservedModel = value;
    }

    public SquadCliAdapter(IWorkspacePaths workspacePaths, Action<string, Exception> onError) {
        _workspacePaths = workspacePaths;
        _onError = onError;
    }

    public async Task ResolveSquadVersionAsync() {
        _squadVersion = await Task.Run(TryResolveSquadVersion);
    }

    public void LaunchPowerShellCommandWindow(WorkspaceIssueAction action) {
        var appRoot = _workspacePaths.ApplicationRoot;
        var completionMessage = action.Label switch {
            "Run Build in PowerShell" => "Build check completed successfully.",
            "Install PowerShell 7" => "PowerShell install command completed.",
            _ => action.Label + " completed."
        };
        var failureMessage = action.Label switch {
            "Run Build in PowerShell" => "Build check failed with exit code ",
            "Install PowerShell 7" => "PowerShell install failed with exit code ",
            _ => action.Label + " failed with exit code "
        };
        var script = string.Join("; ", [
            "$machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')",
            "$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')",
            "$mergedPath = @($machinePath, $userPath) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique",
            "$env:PATH = ($mergedPath -join ';')",
            $"Set-Location -LiteralPath {ToPowerShellSingleQuotedLiteral(appRoot)}",
            action.Argument!,
            "Write-Host ''",
            $"if ($LASTEXITCODE -eq 0) {{ Write-Host {ToPowerShellSingleQuotedLiteral(completionMessage)} -ForegroundColor Green }} else {{ Write-Host ({ToPowerShellSingleQuotedLiteral(failureMessage)} + $LASTEXITCODE + '.') -ForegroundColor Red }}"
        ]);

        Process.Start(new ProcessStartInfo {
            FileName = "powershell.exe",
            Arguments = $"-NoExit -Command \"& {{ {EscapePowerShellCommandArgument(script)} }}\"",
            WorkingDirectory = appRoot,
            UseShellExecute = true
        });
    }

    public void OpenFolderInExplorer(string? folderPath, string dialogTitle) {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return;

        try {
            Process.Start(new ProcessStartInfo {
                FileName = "explorer.exe",
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex) {
            MessageBox.Show(
                $"Unable to open the folder.\n\n{ex.Message}",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public void OpenExternalLink(string target) {
        try {
            Process.Start(new ProcessStartInfo(target) {
                UseShellExecute = true
            });
        }
        catch (Exception ex) {
            _onError("Open Link", ex);
        }
    }

    private string? TryResolveSquadVersion() {
        try {
            var process = Process.Start(new ProcessStartInfo {
                FileName = "cmd.exe",
                Arguments = "/c npx squad --version",
                WorkingDirectory = _workspacePaths.ApplicationRoot,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return null;

            var standardOutput = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(standardOutput))
                return standardOutput;
        }
        catch {
        }

        return null;
    }

    private static string ToPowerShellSingleQuotedLiteral(string value) {
        return $"'{value.Replace("'", "''")}'";
    }

    private static string EscapePowerShellCommandArgument(string value) {
        return value.Replace("\"", "\\\"");
    }
}
