using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SquadDash;

internal static class Program {
    private static IWorkspacePaths _workspacePaths = null!;

    [STAThread]
    private static int Main(string[] args) {
        try {
            _workspacePaths = WorkspacePathsProvider.Discover();

            if (TryGetDeployBuildOutput(args, out var buildOutputDirectory))
                return DeployAndRestart(buildOutputDirectory!);

            if (TryGetCompleteDeployRestartRequest(args, out var deployRestartRequestId, out var stagedBuildOutputDirectory))
                return CompleteDeferredDeployRestart(deployRestartRequestId!, stagedBuildOutputDirectory!);

            if (TryGetCompleteRestartRequest(args, out var requestId))
                return CompleteRestart(requestId!);

            var startup = StartupFolderParser.ParseArguments(args);
            return LaunchPayload(startup.StartupFolder, startup.RefreshScreenshots, startup.RefreshScreenshotName, startup.RestartRelaunch);
        }
        catch (Exception ex) {
            // Console.Error is invisible when the launcher is spawned from Explorer
            // (context menu, desktop shortcut, WinGet launch). Use a MessageBox so
            // the user sees the failure rather than "nothing happening".
            ShowErrorDialog(
                "SquadDash — Launch Failed",
                $"SquadDash could not start.\n\n{ex.Message}\n\nSee the trace log for full details.");
            return 1;
        }
    }

    private static int DeployAndRestart(string buildOutputDirectory) {
        var appRoot = _workspacePaths.ApplicationRoot;
        var slotStore = new RuntimeSlotStateStore(_workspacePaths.RunRootDirectory);
        var registry = new RunningInstanceRegistry();
        var restartStateStore = new RestartCoordinatorStateStore();
        var instances = registry.LoadLiveInstances(appRoot);
        var restartRequestId = instances.Count > 0 ? Guid.NewGuid().ToString("N") : null;
        var restartRequestSaved = false;

        var activeState = slotStore.Load();
        string nextSlot;

        WaitForBuildOutputCompleteness(buildOutputDirectory, TimeSpan.FromSeconds(10));

        try {
            try {
                nextSlot = PrepareNextSlot(slotStore, activeState.ActiveSlot);
            }
            catch when (instances.Count > 0) {
                var stagedBuildOutput = StageBuildOutputForDeferredDeployment(restartRequestId!, buildOutputDirectory);
                SaveRestartRequest(restartStateStore, appRoot, restartRequestId!, instances);
                restartRequestSaved = true;
                RequestCloseRegisteredInstances(instances);
                StartDetachedDeferredDeployCoordinator(restartRequestId!, stagedBuildOutput);
                return 0;
            }

            var nextSlotDirectory = slotStore.GetSlotDirectory(nextSlot);
            CopyDirectory(buildOutputDirectory, nextSlotDirectory);
            EnsurePayloadSupportFiles(buildOutputDirectory, nextSlotDirectory);

            var nextSlotPayloadPath = slotStore.GetPayloadPath(nextSlot);
            var slotIsComplete = IsPayloadDeploymentComplete(nextSlotPayloadPath);
            if (slotIsComplete)
                slotStore.Save(new RuntimeSlotState(nextSlot, DateTimeOffset.UtcNow));

            if (instances.Count > 0) {
                if (!restartRequestSaved)
                    SaveRestartRequest(restartStateStore, appRoot, restartRequestId!, instances);

                StartDetachedRestartCoordinator(restartRequestId!);
            }
        }
        catch {
            if (restartRequestSaved && restartRequestId is not null) {
                restartStateStore.ClearRequest(appRoot);
                restartStateStore.ClearPlan(appRoot, restartRequestId);
            }

            throw;
        }

        return 0;
    }

    private static int CompleteDeferredDeployRestart(string requestId, string stagedBuildOutputDirectory) {
        var appRoot = _workspacePaths.ApplicationRoot;
        var slotStore = new RuntimeSlotStateStore(_workspacePaths.RunRootDirectory);
        var restartStateStore = new RestartCoordinatorStateStore();
        var plan = restartStateStore.LoadPlan(appRoot, requestId);
        if (plan is null)
            return 0;

        var relaunched = false;
        try {
            WaitForRegisteredInstancesToExit(plan.Instances);

            var activeState = slotStore.Load();
            var nextSlot = PrepareNextSlot(slotStore, activeState.ActiveSlot);
            var nextSlotDirectory = slotStore.GetSlotDirectory(nextSlot);

            CopyDirectory(stagedBuildOutputDirectory, nextSlotDirectory);
            EnsurePayloadSupportFiles(stagedBuildOutputDirectory, nextSlotDirectory);

            var nextSlotPayloadPath = slotStore.GetPayloadPath(nextSlot);
            if (!IsPayloadDeploymentComplete(nextSlotPayloadPath))
                throw new InvalidOperationException("Deferred deployment did not produce a complete SquadDash payload.");

            slotStore.Save(new RuntimeSlotState(nextSlot, DateTimeOffset.UtcNow));
            WaitAndRelaunchInstances(plan.Instances);
            relaunched = true;
        }
        catch {
            if (!relaunched) {
                try {
                    WaitAndRelaunchInstances(plan.Instances);
                }
                catch {
                }
            }

            throw;
        }
        finally {
            restartStateStore.ClearRequest(appRoot);
            restartStateStore.ClearPlan(appRoot, requestId);
            TryDeleteDirectory(GetDeferredDeploymentRoot(requestId));
        }

        return 0;
    }

    private static void SaveRestartRequest(
        RestartCoordinatorStateStore restartStateStore,
        string appRoot,
        string requestId,
        IReadOnlyList<RunningInstanceRecord> instances) {
        var requestedAt = DateTimeOffset.UtcNow;
        restartStateStore.SavePlan(new RestartPlanState(
            appRoot,
            requestId,
            requestedAt,
            instances));
        restartStateStore.SaveRequest(new RestartRequestState(
            appRoot,
            requestId,
            requestedAt));
    }

    private static int CompleteRestart(string requestId) {
        var appRoot = _workspacePaths.ApplicationRoot;
        var restartStateStore = new RestartCoordinatorStateStore();
        var plan = restartStateStore.LoadPlan(appRoot, requestId);
        if (plan is null)
            return 0;

        try {
            WaitAndRelaunchInstances(plan.Instances);
        }
        finally {
            restartStateStore.ClearRequest(appRoot);
            restartStateStore.ClearPlan(appRoot, requestId);
        }

        return 0;
    }

    private static int LaunchPayload(string? startupFolder, bool refreshScreenshots = false, string? refreshScreenshotName = null, bool restartRelaunch = false) {
        var slotStore = new RuntimeSlotStateStore(_workspacePaths.RunRootDirectory);
        var state = slotStore.Load();
        var payloadPath = ResolvePayloadPath(slotStore, state.ActiveSlot);

        if (!File.Exists(payloadPath))
            throw new FileNotFoundException("Could not find SquadDash payload executable.", payloadPath);

        if (!TryCheckRuntimeRequirements(payloadPath, out var missingRequirement)) {
            var message =
                $"SquadDash requires the .NET Desktop Runtime to run, but it is not installed.\n\n" +
                $"Missing: {missingRequirement}\n\n" +
                $"Download it from:\nhttps://dotnet.microsoft.com/download/dotnet/10.0";
            ShowErrorDialog("SquadDash — .NET Desktop Runtime Required", message);
            return 1;
        }

        var workingDirectory = ResolveWorkingDirectory(startupFolder);
        var arguments = BuildPayloadArguments(startupFolder, _workspacePaths.ApplicationRoot, refreshScreenshots, refreshScreenshotName, restartRelaunch);

        var process = Process.Start(new ProcessStartInfo {
            FileName = payloadPath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        });

        if (process is null)
            throw new InvalidOperationException("Failed to launch SquadDash payload.");

        return 0;
    }

    private static void RequestCloseRegisteredInstances(IReadOnlyList<RunningInstanceRecord> instances) {
        var trackedProcesses = new List<(RunningInstanceRecord Record, Process Process)>();
        foreach (var instance in instances) {
            try {
                var process = Process.GetProcessById(instance.ProcessId);
                trackedProcesses.Add((instance, process));
                process.CloseMainWindow();
            }
            catch {
            }
        }

        foreach (var (_, process) in trackedProcesses) {
            process.Dispose();
        }
    }

    private static void WaitForRegisteredInstancesToExit(IReadOnlyList<RunningInstanceRecord> instances) {
        while (instances.Any(IsProcessAlive))
            Thread.Sleep(250);
    }

    /// <summary>
    /// Relaunches each instance as soon as it exits, rather than waiting for all
    /// instances to exit before relaunching any. This lets idle instances (e.g. other
    /// workspaces that were not busy) come back immediately while a busy instance
    /// finishes its current turn.
    /// </summary>
    private static void WaitAndRelaunchInstances(IReadOnlyList<RunningInstanceRecord> instances) {
        var pending = DeduplicateRelaunchInstances(instances);

        while (pending.Count > 0) {
            for (var i = pending.Count - 1; i >= 0; i--) {
                if (!IsProcessAlive(pending[i])) {
                    RelaunchInstance(pending[i]);
                    pending.RemoveAt(i);
                }
            }

            if (pending.Count > 0)
                Thread.Sleep(250);
        }
    }

    private static void RelaunchInstance(RunningInstanceRecord instance) {
        var launcherPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
            throw new FileNotFoundException("Could not resolve the SquadDash launcher path.", launcherPath);

        var workspaceFolder = ResolveRelaunchWorkspaceFolder(instance);

        Process.Start(new ProcessStartInfo {
            FileName = launcherPath,
            Arguments = $"{QuoteArgument(workspaceFolder)} --restart-relaunch",
            WorkingDirectory = workspaceFolder,
            UseShellExecute = true
        });
    }

    private static void StartDetachedRestartCoordinator(string requestId) {
        var launcherPath = CreateDetachedRestartCoordinatorLauncher(requestId);

        Process.Start(new ProcessStartInfo {
            FileName = launcherPath,
            Arguments = $"--complete-restart {QuoteArgument(requestId)}",
            WorkingDirectory = _workspacePaths.ApplicationRoot,
            UseShellExecute = true
        });
    }

    private static void StartDetachedDeferredDeployCoordinator(string requestId, string stagedBuildOutputDirectory) {
        var launcherPath = CreateDetachedRestartCoordinatorLauncher(requestId);

        Process.Start(new ProcessStartInfo {
            FileName = launcherPath,
            Arguments = $"--complete-deploy-restart {QuoteArgument(requestId)} {QuoteArgument(stagedBuildOutputDirectory)}",
            WorkingDirectory = _workspacePaths.ApplicationRoot,
            UseShellExecute = true
        });
    }

    private static string ResolvePayloadPath(RuntimeSlotStateStore slotStore, string? activeSlot) {
        if (!string.IsNullOrWhiteSpace(activeSlot)) {
            var slotPayload = slotStore.GetPayloadPath(activeSlot);
            if (IsPayloadDeploymentComplete(slotPayload))
                return slotPayload;
        }

        var localPayload = Path.Combine(AppContext.BaseDirectory, RuntimeSlotNames.PayloadFileName);
        if (IsPayloadDeploymentComplete(localPayload))
            return localPayload;

        var installedPayload = Path.Combine(AppContext.BaseDirectory, "app", RuntimeSlotNames.PayloadFileName);
        if (IsPayloadDeploymentComplete(installedPayload))
            return installedPayload;

        return localPayload;
    }

    private static bool IsPayloadDeploymentComplete(string payloadPath) {
        if (!File.Exists(payloadPath))
            return false;

        var payloadDirectory = Path.GetDirectoryName(payloadPath);
        if (string.IsNullOrWhiteSpace(payloadDirectory))
            return false;

        var payloadName = Path.GetFileNameWithoutExtension(payloadPath);
        var runtimeConfigPath = Path.Combine(payloadDirectory, payloadName + ".runtimeconfig.json");
        var depsPath = Path.Combine(payloadDirectory, payloadName + ".deps.json");

        return File.Exists(runtimeConfigPath) && File.Exists(depsPath);
    }

    private static void WaitForBuildOutputCompleteness(string buildOutputDirectory, TimeSpan timeout) {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline) {
            if (IsPayloadDeploymentComplete(Path.Combine(buildOutputDirectory, RuntimeSlotNames.PayloadFileName)))
                return;

            Thread.Sleep(100);
        }
    }

    private static void EnsurePayloadSupportFiles(string sourceDirectory, string destinationDirectory) {
        var payloadName = Path.GetFileNameWithoutExtension(RuntimeSlotNames.PayloadFileName);
        foreach (var suffix in new[] { ".deps.json", ".runtimeconfig.json", ".pdb" }) {
            var fileName = payloadName + suffix;
            var sourcePath = Path.Combine(sourceDirectory, fileName);
            if (!File.Exists(sourcePath))
                continue;

            var destinationPath = Path.Combine(destinationDirectory, fileName);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static string ResolveWorkingDirectory(string? startupFolder) {
        if (!string.IsNullOrWhiteSpace(startupFolder) && Directory.Exists(startupFolder))
            return startupFolder;

        return Environment.CurrentDirectory;
    }

    private static string BuildPayloadArguments(string? startupFolder, string applicationRoot, bool refreshScreenshots, string? refreshScreenshotName, bool restartRelaunch) {
        var arguments = new List<string> {
            "--app-root",
            QuoteArgument(applicationRoot)
        };

        if (!string.IsNullOrWhiteSpace(startupFolder)) {
            arguments.Add("--workspace");
            arguments.Add(QuoteArgument(startupFolder));
        }

        if (refreshScreenshots) {
            arguments.Add("--refresh-screenshots");
            if (!string.IsNullOrWhiteSpace(refreshScreenshotName))
                arguments.Add(QuoteArgument(refreshScreenshotName));
        }

        if (restartRelaunch)
            arguments.Add("--restart-relaunch");

        return string.Join(" ", arguments);
    }

    private static List<RunningInstanceRecord> DeduplicateRelaunchInstances(IReadOnlyList<RunningInstanceRecord> instances) {
        var pending = new List<RunningInstanceRecord>();
        var seenWorkspaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var instance in instances) {
            var workspaceFolder = ResolveRelaunchWorkspaceFolder(instance);
            var key = NormalizePathForKey(workspaceFolder);
            if (seenWorkspaces.Add(key))
                pending.Add(instance);
        }

        return pending;
    }

    private static string ResolveRelaunchWorkspaceFolder(RunningInstanceRecord instance) {
        var preferred = !string.IsNullOrWhiteSpace(instance.ActiveWorkspaceFolder)
            ? instance.ActiveWorkspaceFolder
            : instance.WorkspaceFolder;

        if (!string.IsNullOrWhiteSpace(preferred) && Directory.Exists(preferred))
            return preferred;

        if (!string.IsNullOrWhiteSpace(instance.WorkspaceFolder) && Directory.Exists(instance.WorkspaceFolder))
            return instance.WorkspaceFolder;

        return Environment.CurrentDirectory;
    }

    private static string NormalizePathForKey(string path) {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool TryGetDeployBuildOutput(string[] args, out string? buildOutputDirectory) {
        buildOutputDirectory = null;

        for (var index = 0; index < args.Length; index++) {
            if (!string.Equals(args[index], "--deploy-build-output", StringComparison.OrdinalIgnoreCase))
                continue;

            if (index + 1 < args.Length) {
                var normalized = StartupFolderParser.Normalize(args[index + 1]);
                if (!string.IsNullOrWhiteSpace(normalized))
                    buildOutputDirectory = Path.GetFullPath(normalized);
            }

            return !string.IsNullOrWhiteSpace(buildOutputDirectory);
        }

        return false;
    }

    private static bool TryGetCompleteRestartRequest(string[] args, out string? requestId) {
        requestId = null;

        for (var index = 0; index < args.Length; index++) {
            if (!string.Equals(args[index], "--complete-restart", StringComparison.OrdinalIgnoreCase))
                continue;

            if (index + 1 < args.Length) {
                var normalized = StartupFolderParser.Normalize(args[index + 1]);
                if (!string.IsNullOrWhiteSpace(normalized))
                    requestId = normalized;
            }

            return !string.IsNullOrWhiteSpace(requestId);
        }

        return false;
    }

    private static bool TryGetCompleteDeployRestartRequest(
        string[] args,
        out string? requestId,
        out string? stagedBuildOutputDirectory) {
        requestId = null;
        stagedBuildOutputDirectory = null;

        for (var index = 0; index < args.Length; index++) {
            if (!string.Equals(args[index], "--complete-deploy-restart", StringComparison.OrdinalIgnoreCase))
                continue;

            if (index + 2 < args.Length) {
                var normalizedRequestId = StartupFolderParser.Normalize(args[index + 1]);
                var normalizedBuildOutput = StartupFolderParser.Normalize(args[index + 2]);

                if (!string.IsNullOrWhiteSpace(normalizedRequestId))
                    requestId = normalizedRequestId;
                if (!string.IsNullOrWhiteSpace(normalizedBuildOutput))
                    stagedBuildOutputDirectory = Path.GetFullPath(normalizedBuildOutput);
            }

            return !string.IsNullOrWhiteSpace(requestId) &&
                   !string.IsNullOrWhiteSpace(stagedBuildOutputDirectory);
        }

        return false;
    }

    private static string StageBuildOutputForDeferredDeployment(string requestId, string buildOutputDirectory) {
        var stagedPayloadDirectory = GetDeferredDeploymentPayloadDirectory(requestId);
        ResetDirectory(stagedPayloadDirectory, GetDeferredDeploymentRoot(requestId));
        CopyDirectory(buildOutputDirectory, stagedPayloadDirectory);
        EnsurePayloadSupportFiles(buildOutputDirectory, stagedPayloadDirectory);
        return stagedPayloadDirectory;
    }

    private static string GetDeferredDeploymentRoot(string requestId) {
        return Path.Combine(_workspacePaths.RunRootDirectory, "pending-deployments", requestId);
    }

    private static string GetDeferredDeploymentPayloadDirectory(string requestId) {
        return Path.Combine(GetDeferredDeploymentRoot(requestId), "payload");
    }

    private static string CreateDetachedRestartCoordinatorLauncher(string requestId) {
        var launcherPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
            throw new FileNotFoundException("Could not resolve the SquadDash launcher path.", launcherPath);

        PruneStaleDetachedCoordinatorDirectories();

        var sourceDirectory = AppContext.BaseDirectory;
        var coordinatorDirectory = Path.Combine(_workspacePaths.RunRootDirectory, "restart-coordinators", requestId);
        Directory.CreateDirectory(coordinatorDirectory);

        var launcherFileName = Path.GetFileName(launcherPath);
        var destinationLauncherPath = Path.Combine(coordinatorDirectory, launcherFileName);

        foreach (var fileName in new[] {
            launcherFileName,
            "SquadDash.dll",
            "SquadDash.deps.json",
            "SquadDash.runtimeconfig.json",
            "SquadDash.pdb"
        }) {
            var sourcePath = Path.Combine(sourceDirectory, fileName);
            if (!File.Exists(sourcePath))
                continue;

            File.Copy(sourcePath, Path.Combine(coordinatorDirectory, fileName), overwrite: true);
        }

        if (!File.Exists(destinationLauncherPath))
            throw new FileNotFoundException("Could not create detached restart coordinator launcher.", destinationLauncherPath);

        return destinationLauncherPath;
    }

    private static void PruneStaleDetachedCoordinatorDirectories() {
        var coordinatorRoot = Path.Combine(_workspacePaths.RunRootDirectory, "restart-coordinators");
        if (!Directory.Exists(coordinatorRoot))
            return;

        var cutoff = DateTime.UtcNow - TimeSpan.FromDays(2);
        foreach (var directory in Directory.GetDirectories(coordinatorRoot)) {
            try {
                if (Directory.GetLastWriteTimeUtc(directory) >= cutoff)
                    continue;

                NormalizeAttributes(directory);
                Directory.Delete(directory, recursive: true);
            }
            catch {
            }
        }
    }

    private static void TryDeleteDirectory(string targetDirectory) {
        try {
            if (!Directory.Exists(targetDirectory))
                return;

            NormalizeAttributes(targetDirectory);
            Directory.Delete(targetDirectory, recursive: true);
        }
        catch {
        }
    }

    private static void ResetDirectory(string targetDirectory, string allowedRootDirectory) {
        var normalizedTarget = Path.GetFullPath(targetDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(allowedRootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!normalizedTarget.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to reset a directory outside the Run root.");

        if (Directory.Exists(normalizedTarget)) {
            NormalizeAttributes(normalizedTarget);
            Directory.Delete(normalizedTarget, recursive: true);
        }

        Directory.CreateDirectory(normalizedTarget);
    }

    private static string PrepareNextSlot(RuntimeSlotStateStore slotStore, string? activeSlot) {
        var preferredSlot = RuntimeSlotNames.Toggle(activeSlot);
        var fallbackSlot = RuntimeSlotNames.Toggle(preferredSlot);
        var candidates = new[] { preferredSlot, fallbackSlot }.Distinct(StringComparer.OrdinalIgnoreCase);

        Exception? lastError = null;
        foreach (var candidate in candidates) {
            try {
                ResetDirectory(slotStore.GetSlotDirectory(candidate), _workspacePaths.RunRootDirectory);
                return candidate;
            }
            catch (Exception ex) {
                lastError = ex;
            }
        }

        throw new IOException("Could not prepare either runtime slot for deployment.", lastError);
    }

    private static void NormalizeAttributes(string targetDirectory) {
        foreach (var directory in Directory.GetDirectories(targetDirectory, "*", SearchOption.AllDirectories))
            File.SetAttributes(directory, FileAttributes.Directory);

        foreach (var file in Directory.GetFiles(targetDirectory, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        File.SetAttributes(targetDirectory, FileAttributes.Directory);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory) {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories)) {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)) {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static string QuoteArgument(string value) {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static bool IsProcessAlive(RunningInstanceRecord record) {
        try {
            using var process = Process.GetProcessById(record.ProcessId);
            if (process.HasExited)
                return false;

            return process.StartTime.ToUniversalTime().Ticks == record.ProcessStartedAtUtcTicks;
        }
        catch {
            return false;
        }
    }

    private static bool TryCheckRuntimeRequirements(string payloadPath, out string? missingRequirement) {
        missingRequirement = null;
        try {
            var payloadDir = Path.GetDirectoryName(payloadPath)!;
            var payloadName = Path.GetFileNameWithoutExtension(payloadPath);
            var runtimeConfigPath = Path.Combine(payloadDir, payloadName + ".runtimeconfig.json");

            if (!File.Exists(runtimeConfigPath))
                return true;

            using var doc = JsonDocument.Parse(File.ReadAllText(runtimeConfigPath));

            if (!doc.RootElement.TryGetProperty("runtimeOptions", out var opts))
                return true;
            if (!opts.TryGetProperty("frameworks", out var frameworks))
                return true;

            var dotnetRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");

            foreach (var framework in frameworks.EnumerateArray()) {
                if (!framework.TryGetProperty("name", out var nameEl)) continue;
                if (!framework.TryGetProperty("version", out var versionEl)) continue;

                var frameworkName = nameEl.GetString() ?? string.Empty;
                var frameworkVersion = versionEl.GetString() ?? string.Empty;

                if (!IsFrameworkInstalled(dotnetRoot, frameworkName, frameworkVersion)) {
                    missingRequirement = $"{frameworkName} {frameworkVersion}";
                    return false;
                }
            }

            return true;
        }
        catch {
            return true;
        }
    }

    private static bool IsFrameworkInstalled(string dotnetRoot, string frameworkName, string minimumVersionStr) {
        var frameworkDir = Path.Combine(dotnetRoot, "shared", frameworkName);
        if (!Directory.Exists(frameworkDir))
            return false;

        if (!Version.TryParse(minimumVersionStr, out var minimumVersion))
            return true;

        return Directory.GetDirectories(frameworkDir)
            .Select(d => {
                var name = Path.GetFileName(d);
                var simple = name.Split('-')[0];
                return Version.TryParse(simple, out var v) ? v : null;
            })
            .Any(v => v != null && v.Major == minimumVersion.Major && v >= minimumVersion);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint hWnd, string text, string caption, uint type);

    private static void ShowErrorDialog(string caption, string text) {
        const uint MB_OK = 0x00000000;
        const uint MB_ICONERROR = 0x00000010;
        MessageBox(0, text, caption, MB_OK | MB_ICONERROR);
    }
}
