using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SquadDash.GuidedTours;

/// <summary>
/// Persists guided tours to the workspace override file at
/// <c>.squad/guided-tours.json</c>.
/// </summary>
internal static class GuidedTourSaver
{
    internal const int BackupCount = 5;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Serialises <paramref name="tours"/> to
    /// <c>&lt;workspaceFolderPath&gt;/.squad/guided-tours.json</c>,
    /// creating the directory if necessary.
    /// </summary>
    internal static void Save(List<GuidedTour> tours, string workspaceFolderPath)
    {
        var path = Path.Combine(workspaceFolderPath, ".squad", "guided-tours.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = JsonSerializer.Serialize(tours, JsonOptions);
        var hash = ComputeHash(content);
        var stepCounts = string.Join(",", tours.Select(t => t.Steps.Count));

        SquadDashTrace.Write(TraceCategory.Callouts,
            $"GuidedTourSaver.Save: path=\"{path}\", tours={tours.Count}, steps=[{stepCounts}], hash={hash}");

        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, System.StringComparison.Ordinal))
        {
            SquadDashTrace.Write(TraceCategory.Callouts,
                $"GuidedTourSaver.Save: unchanged; write and backup skipped, hash={hash}");
            return;
        }

        if (File.Exists(path))
            RotateBackups(path);

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content);
        File.Move(tempPath, path, overwrite: true);
        SquadDashTrace.Write(TraceCategory.Callouts,
            $"GuidedTourSaver.Save: completed, path=\"{path}\", hash={hash}");
    }

    internal static string ComputeHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..12];

    private static void RotateBackups(string path)
    {
        var oldest = $"{path}.bak.{BackupCount}";
        if (File.Exists(oldest)) File.Delete(oldest);
        for (var index = BackupCount - 1; index >= 1; index--)
        {
            var source = $"{path}.bak.{index}";
            if (File.Exists(source)) File.Move(source, $"{path}.bak.{index + 1}");
        }
        File.Copy(path, $"{path}.bak.1");
    }
}
