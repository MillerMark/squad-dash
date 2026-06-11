using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace SquadDash;

internal sealed class SquadCliAdapter {
    private readonly IWorkspacePaths _workspacePaths;
    private readonly Action<string, Exception> _onError;
    private string? _squadVersion;
    private string? _lastObservedModel;
    private string? _latestSquadVersion;

    public string? SquadVersion => _squadVersion;
    public string? LatestSquadVersion => _latestSquadVersion;

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

    public async Task CheckForSquadUpdateAsync() {
        _latestSquadVersion = await TryFetchLatestSquadVersionAsync();
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
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) {
            MessageBox.Show(
                $"Unable to open the folder.\n\n{ex.Message}",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public void OpenExternalLink(string target) {
        try {
            if (target.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("edge://", StringComparison.OrdinalIgnoreCase)) {
                if (!TryOpenBrowserInternalUrl(target))
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return;
            }
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) {
            _onError("Open Link", ex);
        }
    }

    private static bool TryOpenBrowserInternalUrl(string url) {
        string[] candidates = url.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase)
            ? [
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                             @"Google\Chrome\Application\chrome.exe"),
              ]
            : [
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
              ];

        var exe = Array.Find(candidates, File.Exists);
        if (exe is null) return false;

        Process.Start(new ProcessStartInfo(exe) {
            ArgumentList = { url },
            UseShellExecute = false
        });
        return true;
    }

    private static async Task<string?> TryFetchLatestSquadVersionAsync() {
        try {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var json = await client.GetStringAsync(
                "https://registry.npmjs.org/@bradygaster/squad-cli/latest").ConfigureAwait(false);
            var match = Regex.Match(json, @"""version""\s*:\s*""([^""]+)""");
            if (match.Success)
                return match.Groups[1].Value;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) {
            SquadDashTrace.Write("SquadCli", $"Version check failed: {ex.Message}");
        }
        return null;
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
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException) {
            SquadDashTrace.Write("SquadCli", $"Version resolve failed: {ex.Message}");
        }

        return null;
    }

    private static string ToPowerShellSingleQuotedLiteral(string value) {
        return $"'{value.Replace("'", "''")}'";
    }

    private static string EscapePowerShellCommandArgument(string value) {
        // Single-quoted PS strings are fully literal — no variable/backtick expansion
        return "'" + value.Replace("'", "''") + "'";
    }
}
