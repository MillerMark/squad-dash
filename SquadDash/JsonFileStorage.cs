using System.IO;
using System.Text.Json;

namespace SquadDash;

/// <summary>
/// Atomic JSON file write helper.
/// Writes to a temp file then renames/copies to avoid partial-write corruption.
/// </summary>
internal static partial class JsonFileStorage {
    private static readonly JsonSerializerOptions DefaultWriteOptions =
        new() { WriteIndented = true };

    public static readonly JsonSerializerOptions PrettyPrint = DefaultWriteOptions;

    /// <summary>
    /// Serializes <paramref name="payload"/> to JSON and writes it to
    /// <paramref name="path"/> atomically via a temp-file rename.
    /// Creates a backup before overwriting.
    /// The containing directory must already exist.
    /// </summary>
    public static void AtomicWrite<T>(string path, T payload,
        JsonSerializerOptions? options = null) {

        var tempPath = path + ".tmp";
        var backupPath = path + ".backup";
        var json = JsonSerializer.Serialize(payload, options ?? DefaultWriteOptions);
        File.WriteAllText(tempPath, json);

        if (File.Exists(path)) {
            // Create backup before replacing
            try {
                File.Copy(path, backupPath, overwrite: true);
            }
            catch {
                // Backup failure is non-fatal; continue with atomic write
            }
            
            try {
                File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            catch (IOException) {
                File.Copy(tempPath, path, overwrite: true);
                File.Delete(tempPath);
            }
        }
        else {
            File.Move(tempPath, path);
        }
    }

    /// <summary>
    /// Writes pre-serialized <paramref name="content"/> to <paramref name="path"/>
    /// atomically via a temp-file rename. Creates a backup before overwriting.
    /// The containing directory must already exist.
    /// </summary>
    public static void AtomicWrite(string path, string content) {
        var tempPath = path + ".tmp";
        var backupPath = path + ".backup";
        File.WriteAllText(tempPath, content);

        if (File.Exists(path)) {
            // Create backup before replacing
            try {
                File.Copy(path, backupPath, overwrite: true);
            }
            catch {
                // Backup failure is non-fatal; continue with atomic write
            }
            
            try {
                File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            catch (IOException) {
                File.Copy(tempPath, path, overwrite: true);
                File.Delete(tempPath);
            }
        }
        else {
            File.Move(tempPath, path);
        }
    }

    /// <summary>
    /// Reads and deserializes a JSON file, returning <paramref name="defaultValue"/>
    /// if the file does not exist or deserialization fails.
    /// </summary>
    public static T ReadOrDefault<T>(string path, T defaultValue,
        JsonSerializerOptions? options = null) where T : class {
        if (!File.Exists(path))
            return defaultValue;
        try {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, options) ?? defaultValue;
        }
        catch {
            return defaultValue;
        }
    }
}
