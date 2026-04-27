using System.IO;
using System.Text.Json;

namespace SquadDash;

/// <summary>
/// Atomic JSON file write helper.
/// Writes to a temp file then renames/copies to avoid partial-write corruption.
/// </summary>
internal static class JsonFileStorage {
    private static readonly JsonSerializerOptions DefaultWriteOptions =
        new() { WriteIndented = true };

    /// <summary>
    /// Serializes <paramref name="payload"/> to JSON and writes it to
    /// <paramref name="path"/> atomically via a temp-file rename.
    /// The containing directory must already exist.
    /// </summary>
    public static void AtomicWrite<T>(string path, T payload,
        JsonSerializerOptions? options = null) {

        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(payload, options ?? DefaultWriteOptions);
        File.WriteAllText(tempPath, json);

        if (File.Exists(path)) {
            try {
                File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            catch {
                File.Copy(tempPath, path, overwrite: true);
                File.Delete(tempPath);
            }
        }
        else {
            File.Move(tempPath, path);
        }
    }
}
