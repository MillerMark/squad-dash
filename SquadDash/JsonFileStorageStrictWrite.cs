using System;
using System.IO;
using System.Text.Json;

namespace SquadDash;

internal static partial class JsonFileStorage {
    /// <summary>
    /// Opt-in replace-only commit: a failed write leaves the previous destination intact.
    /// Unlike AtomicWrite, this never falls back to overwriting the destination in place.
    /// The caller must serialize writers and create the containing directory.
    /// </summary>
    public static void AtomicWritePreservingPrevious<T>(string path, T payload) {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                JsonSerializer.Serialize(stream, payload, PrettyPrint);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
                File.Replace(temporaryPath, path, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, path);
        }
        finally {
            try {
                File.Delete(temporaryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) {
                // Cleanup must not hide the commit error or turn a durable commit into a false failure.
                SquadDashTrace.Write("ButtonsStore", $"Temporary JSON cleanup failed for '{temporaryPath}': {ex.Message}");
            }
        }
    }
}
