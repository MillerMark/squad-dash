using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace SquadDash;

internal static class SquadDashRuntimeStamp {
    public const string BridgeMode = "persistent";
    public const string BridgeFeatures =
        "request-ids,local-/tasks,control-abort,background-task-cache,subagent-events,completion-notices,agent-threads,thread-cards,background-report-handoff";

    public static void WriteStartupStamp(IWorkspacePaths workspacePaths) {
        SquadDashTrace.Write("Startup", BuildAppStampLine(workspacePaths));
        SquadDashTrace.Write("Startup", BuildSdkStampLine(workspacePaths));
    }

    public static string BuildBridgeStamp() =>
        $"bridgeMode={BridgeMode} bridgeFeatures={BridgeFeatures}";

    private static string BuildAppStampLine(IWorkspacePaths workspacePaths) {
        var assembly = typeof(SquadDashRuntimeStamp).Assembly;
        var processPath = Environment.ProcessPath;
        var assemblyPath = assembly.Location;
        var processStartedAt = TryGetCurrentProcessStartedAt();
        var assemblyVersion = assembly.GetName().Version?.ToString() ?? "(unknown)";
        var informationalVersion =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            "(none)";
        var fileVersion =
            TryGetFileVersion(processPath) ??
            TryGetFileVersion(assemblyPath) ??
            "(unknown)";
        var slot = DetectRunSlot(processPath) ?? DetectRunSlot(AppContext.BaseDirectory) ?? "(none)";

        return
            $"RuntimeStamp slot={slot} pid={Environment.ProcessId} " +
            $"startedAt={FormatDateTime(processStartedAt)} " +
            $"processPath={NormalizePath(processPath) ?? "(unknown)"} " +
            $"processWrite={FormatFileWriteTime(processPath)} " +
            $"appBase={NormalizePath(AppContext.BaseDirectory) ?? "(unknown)"} " +
            $"appDll={NormalizePath(assemblyPath) ?? "(unknown)"} " +
            $"appDllWrite={FormatFileWriteTime(assemblyPath)} " +
            $"assemblyVersion={assemblyVersion} " +
            $"fileVersion={fileVersion} " +
            $"informationalVersion={informationalVersion} " +
            $"appRoot={NormalizePath(workspacePaths.ApplicationRoot) ?? "(unknown)"} " +
            BuildBridgeStamp();
    }

    private static string BuildSdkStampLine(IWorkspacePaths workspacePaths) {
        var sdkDirectory = workspacePaths.SquadSdkDirectory;
        var runPromptPath = Path.Combine(sdkDirectory, "runPrompt.js");
        var squadServicePath = Path.Combine(sdkDirectory, "squadService.js");

        return
            $"SdkStamp sdkDir={NormalizePath(sdkDirectory) ?? "(unknown)"} " +
            $"runPrompt.js={FormatFileWriteTime(runPromptPath)} " +
            $"squadService.js={FormatFileWriteTime(squadServicePath)}";
    }

    private static string? DetectRunSlot(string? path) {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        DirectoryInfo? directory = null;

        try {
            var normalizedPath = Path.GetFullPath(path);
            directory = File.Exists(normalizedPath)
                ? new FileInfo(normalizedPath).Directory
                : new DirectoryInfo(normalizedPath);
        }
        catch {
            return null;
        }

        while (directory is not null) {
            if (string.Equals(directory.Parent?.Name, "Run", StringComparison.OrdinalIgnoreCase))
                return directory.Name;

            directory = directory.Parent;
        }

        return null;
    }

    private static DateTimeOffset? TryGetCurrentProcessStartedAt() {
        try {
            using var process = Process.GetCurrentProcess();
            return process.StartTime;
        }
        catch {
            return null;
        }
    }

    private static string FormatFileWriteTime(string? path) {
        if (string.IsNullOrWhiteSpace(path))
            return "(unknown)";

        try {
            return File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss zzz");
        }
        catch {
            return "(unavailable)";
        }
    }

    private static string? TryGetFileVersion(string? path) {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try {
            return FileVersionInfo.GetVersionInfo(path).FileVersion;
        }
        catch {
            return null;
        }
    }

    private static string FormatDateTime(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "(unknown)";

    private static string? NormalizePath(string? path) {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch {
            return path;
        }
    }
}
