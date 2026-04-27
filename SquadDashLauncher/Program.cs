using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;

namespace SquadDash;

internal static class Program {
    private static readonly TimeSpan GracefulRestartWait = TimeSpan.FromMinutes(20);
    private static IWorkspacePaths _workspacePaths = null!;

    [STAThread]
    private static int Main(string[] args) {
        try {
            _workspacePaths = WorkspacePathsProvider.Discover();

            if (TryGetDeployBuildOutput(args, out var buildOutputDirectory))
                return DeployAndRestart(buildOutputDirectory!);

            if (TryGetCompleteRestartRequest(args, out var requestId))
                return CompleteRestart(requestId!);

            var startup = StartupFolderParser.ParseArguments(args);
            return LaunchPayload(startup.StartupFolder, startup.RefreshScreenshots, startup.RefreshScreenshotName);
        }
        catch (Exception ex) {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int DeployAndRestart(string buildOutputDirectory) {
        var appRoot = _workspacePaths.ApplicationRoot;
        var slotStore = new RuntimeSlotStateStore(_workspacePaths.RunRootDirectory);
        var registry = new RunningInstanceRegistry();
        var restartStateStore = new RestartCoordinatorStateStore();
        var instances = registry.LoadLiveInstances(appRoot);

        var activeState = slotStore.Load();
        string nextSlot;

        WaitForBuildOutputCompleteness(buildOutputDirectory, TimeSpan.FromSeconds(10));

        try {
            nextSlot = PrepareNextSlot(slotStore, activeState.ActiveSlot);
        }
        catch when (instances.Count > 0) {
            ForceCloseRegisteredInstances(instances);
            nextSlot = PrepareNextSlot(slotStore, activeState.ActiveSlot);
        }

        var nextSlotDirectory = slotStore.GetSlotDirectory(nextSlot);
        CopyDirectory(buildOutputDirectory, nextSlotDirectory);
        EnsurePayloadSupportFiles(buildOutputDirectory, nextSlotDirectory);

        var nextSlotPayloadPath = slotStore.GetPayloadPath(nextSlot);
        var slotIsComplete = IsPayloadDeploymentComplete(nextSlotPayloadPath);
        if (slotIsComplete)
            slotStore.Save(new RuntimeSlotState(nextSlot, DateTimeOffset.UtcNow));

        if (instances.Count > 0) {
            var requestId = Guid.NewGuid().ToString("N");
            restartStateStore.SavePlan(new RestartPlanState(
                appRoot,
                requestId,
                DateTimeOffset.UtcNow,
                instances));
            restartStateStore.SaveRequest(new RestartRequestState(
                appRoot,
                requestId,
                DateTimeOffset.UtcNow));
            StartDetachedRestartCoordinator(requestId);
        }

        return 0;
    }

    private static int CompleteRestart(string requestId) {
        var appRoot = _workspacePaths.ApplicationRoot;
        var restartStateStore = new RestartCoordinatorStateStore();
        var plan = restartStateStore.LoadPlan(appRoot, requestId);
        if (plan is null)
            return 0;

        try {
            WaitForRegisteredInstancesToExit(plan.Instances, GracefulRestartWait);
            RelaunchInstances(plan.Instances);
        }
        finally {
            restartStateStore.ClearRequest(appRoot);
            restartStateStore.ClearPlan(appRoot, requestId);
        }

        return 0;
    }

    private static int LaunchPayload(string? startupFolder, bool refreshScreenshots = false, string? refreshScreenshotName = null) {
        var slotStore = new RuntimeSlotStateStore(_workspacePaths.RunRootDirectory);
        var state = slotStore.Load();
        var payloadPath = ResolvePayloadPath(slotStore, state.ActiveSlot);

        if (!File.Exists(payloadPath))
            throw new FileNotFoundException("Could not find SquadDash payload executable.", payloadPath);

        var workingDirectory = ResolveWorkingDirectory(startupFolder);
        var arguments = BuildPayloadArguments(startupFolder, _workspacePaths.ApplicationRoot, refreshScreenshots, refreshScreenshotName);

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

    private static void ForceCloseRegisteredInstances(IReadOnlyList<RunningInstanceRecord> instances) {
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

        var deadline = DateTime.UtcNow.AddSeconds(15);
        foreach (var (_, process) in trackedProcesses) {
            try {
                while (!process.HasExited && DateTime.UtcNow < deadline) {
                    process.WaitForExit(250);
                }

                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch {
            }
            finally {
                process.Dispose();
            }
        }
    }

    private static void WaitForRegisteredInstancesToExit(
        IReadOnlyList<RunningInstanceRecord> instances,
        TimeSpan timeout) {
        var deadline = DateTime.UtcNow + timeout;

        foreach (var instance in instances) {
            while (DateTime.UtcNow < deadline && IsProcessAlive(instance))
                Thread.Sleep(250);
        }

        foreach (var instance in instances.Where(IsProcessAlive)) {
            try {
                using var process = Process.GetProcessById(instance.ProcessId);
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch {
            }
        }
    }

    private static void RelaunchInstances(IReadOnlyList<RunningInstanceRecord> instances) {
        var launcherPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
            throw new FileNotFoundException("Could not resolve the SquadDash launcher path.", launcherPath);

        foreach (var instance in instances) {
            var workspaceFolder = Directory.Exists(instance.WorkspaceFolder)
                ? instance.WorkspaceFolder
                : Environment.CurrentDirectory;

            Process.Start(new ProcessStartInfo {
                FileName = launcherPath,
                Arguments = QuoteArgument(workspaceFolder),
                WorkingDirectory = workspaceFolder,
                UseShellExecute = true
            });
        }
    }

    private static void StartDetachedRestartCoordinator(string requestId) {
        var launcherPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
            throw new FileNotFoundException("Could not resolve the SquadDash launcher path.", launcherPath);

        Process.Start(new ProcessStartInfo {
            FileName = launcherPath,
            Arguments = $"--complete-restart {QuoteArgument(requestId)}",
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

    private static string BuildPayloadArguments(string? startupFolder, string applicationRoot, bool refreshScreenshots, string? refreshScreenshotName) {
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

        return string.Join(" ", arguments);
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
}
