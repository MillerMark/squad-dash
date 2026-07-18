using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SquadDash.GuidedTours;

/// <summary>
/// Persists guided tours to the tracked application asset.
/// </summary>
internal static class GuidedTourSaver
{
    internal const int RecoveryVersionLimit = 50;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Serialises <paramref name="tours"/> to
    /// <c>&lt;workspaceFolderPath&gt;/SquadDash/Assets/guided-tours.json</c>,
    /// creating the directory if necessary.
    /// </summary>
    internal static void Save(List<GuidedTour> tours, string workspaceFolderPath)
    {
        var path = GetPath(workspaceFolderPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = JsonSerializer.Serialize(tours, JsonOptions);
        var hash = ComputeHash(content);
        var stepCounts = string.Join(",", tours.Select(t => t.Steps.Count));

        SquadDashTrace.Write(TraceCategory.Callouts,
            $"GuidedTourSaver.Save: path=\"{path}\", tours={tours.Count}, steps=[{stepCounts}], hash={hash}");

        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, System.StringComparison.Ordinal))
        {
            SquadDashTrace.Write(TraceCategory.Callouts,
                $"GuidedTourSaver.Save: unchanged; write skipped, hash={hash}");
            return;
        }

        if (File.Exists(path))
            ArchiveCurrentVersion(path, workspaceFolderPath);

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content);
        // Force the complete temporary file through the OS cache before the
        // atomic replacement. This protects edits during rapid app restarts.
        using (var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Write, FileShare.Read))
            stream.Flush(flushToDisk: true);
        File.Move(tempPath, path, overwrite: true);
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"GuidedTourSaver.Save: completed, path=\"{path}\", hash={hash}");
    }

    internal static string ComputeHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..12];

    internal static string GetPath(string workspaceFolderPath) =>
        Path.Combine(workspaceFolderPath, "SquadDash", "Assets", "guided-tours.json");

    internal static string GetRecoveryDirectory(string workspaceFolderPath) =>
        Path.Combine(SquadDashPaths.WorkspaceStateDirectory(workspaceFolderPath), "guided-tour-history");

    private static void ArchiveCurrentVersion(string path, string workspaceFolderPath)
    {
        try
        {
            var existingContent = File.ReadAllText(path);
            var existingHash = ComputeHash(existingContent);
            var directory = GetRecoveryDirectory(workspaceFolderPath);
            Directory.CreateDirectory(directory);
            var archivePath = Path.Combine(
                directory,
                $"guided-tours-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}-{existingHash}.json");
            File.Copy(path, archivePath, overwrite: false);

            foreach (var stalePath in Directory.GetFiles(directory, "guided-tours-*.json")
                         .OrderByDescending(File.GetCreationTimeUtc)
                         .Skip(RecoveryVersionLimit))
                File.Delete(stalePath);

            SquadDashTrace.Write(TraceCategory.Callouts,
                $"GuidedTourSaver.Save: archived previous version path=\"{archivePath}\", hash={existingHash}");
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write(TraceCategory.Callouts,
                $"GuidedTourSaver.Save: recovery archive failed, error={ex.Message}");
        }
    }
}
