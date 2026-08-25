using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace SquadDash;

internal sealed class SquadCliAdapter {
    private readonly IWorkspacePaths _workspacePaths;
    private readonly ISquadCliResolutionService _cliResolutionService;
    private Action<string, Exception> _onError;
    private string? _squadVersion;
    private string? _lastObservedModel;
    private string? _latestSquadVersion;
    private ResolvedCliCandidate? _lastResolvedCandidate;

    public string? SquadVersion => _squadVersion;
    public string? LatestSquadVersion => _latestSquadVersion;
    public ResolvedCliCandidate? LastResolvedCandidate => _lastResolvedCandidate;

    public string? LastObservedModel {
        get => _lastObservedModel;
        set => _lastObservedModel = value;
    }

    public SquadCliAdapter(IWorkspacePaths workspacePaths, ISquadCliResolutionService cliResolutionService)
        : this(
            workspacePaths,
            cliResolutionService,
            (_, ex) => SquadDashTrace.Write("SquadCli", $"Adapter operation failed: {ex.Message}")) {
    }

    public SquadCliAdapter(
        IWorkspacePaths workspacePaths,
        ISquadCliResolutionService cliResolutionService,
        Action<string, Exception> onError) {
        _workspacePaths = workspacePaths;
        _cliResolutionService = cliResolutionService;
        _onError = onError;
    }

    public void SetErrorHandler(Action<string, Exception> onError) {
        _onError = onError ?? throw new ArgumentNullException(nameof(onError));
    }

    public async Task ResolveSquadVersionAsync(string? workspaceDirectory = null) {
        var workingDirectory = string.IsNullOrWhiteSpace(workspaceDirectory)
            ? _workspacePaths.ApplicationRoot
            : workspaceDirectory;

        try {
            var candidate = await _cliResolutionService.ResolveAsync(workingDirectory).ConfigureAwait(false);
            _lastResolvedCandidate = candidate;
            _squadVersion = candidate.IsAvailable ? candidate.Version : null;
            _latestSquadVersion = candidate.UpdateInstruction.TargetVersion;
            SquadDashTrace.Write(
                "CliResolution",
                $"Resolved source={candidate.DetectedSource} installed={_squadVersion ?? "(unknown)"} " +
                $"target={_latestSquadVersion ?? "(unknown)"} canAutoUpdate={candidate.UpdateInstruction.CanAutoUpdate}.");
        }
        catch (Exception ex) {
            SquadDashTrace.Write("SquadCli", $"Version resolve failed: {ex.Message}");
            _squadVersion = null;
        }
    }

    public Task CheckForSquadUpdateAsync() {
        // Target version is now fetched inside ResolveSquadVersionAsync via the resolution
        // service's ProbeTargetVersionAsync step. This method is retained for API compatibility
        // but no longer performs a separate npm fetch.
        return Task.CompletedTask;
    }

    public Process? LaunchPowerShellCommandWindow(WorkspaceIssueAction action) {
        return Process.Start(CreatePowerShellCommandStartInfo(_workspacePaths.ApplicationRoot, action));
    }

    internal static ProcessStartInfo CreatePowerShellCommandStartInfo(
        string appRoot,
        WorkspaceIssueAction action) {
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
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        return new ProcessStartInfo {
            FileName = "powershell.exe",
            Arguments = $"-NoExit -EncodedCommand {encodedScript}",
            WorkingDirectory = appRoot,
            UseShellExecute = true
        };
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

    private static string ToPowerShellSingleQuotedLiteral(string value) {
        return $"'{value.Replace("'", "''")}'";
    }

}
