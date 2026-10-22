using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace SquadDash;

internal static partial class JsonFileStorage {
    // Windows ReplaceFile/MoveFile can transiently fail with IOException
    // ("Unable to remove the file to be replaced") or UnauthorizedAccessException when the
    // destination is briefly locked by another process (antivirus, Search indexer, Explorer,
    // or a lingering read handle) even though the caller has serialized its own writers.
    // A bounded retry-with-backoff absorbs these transient locks without masking real errors.
    private const int CommitMaxAttempts = 20;

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

            CommitReplacingPrevious(temporaryPath, path);
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

    // Commits the already-written temp file onto the destination, retrying transient Windows
    // sharing locks. On failure the destination is left untouched (ReplaceFile/MoveFile do not
    // modify the destination when they throw), preserving the "previous destination intact" guarantee.
    private static void CommitReplacingPrevious(string temporaryPath, string path) {
        for (var attempt = 1; ; attempt++) {
            try {
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, destinationBackupFileName: null);
                else
                    File.Move(temporaryPath, path);
                return;
            }
            catch (Exception ex) when (attempt < CommitMaxAttempts && IsTransientCommitLock(ex)) {
                Thread.Sleep(BackoffDelayMilliseconds(attempt));
            }
        }
    }

    // FileNotFoundException/DirectoryNotFoundException indicate the temp file or directory is
    // genuinely gone, not a transient lock, so they are not retried.
    private static bool IsTransientCommitLock(Exception ex) =>
        ex is UnauthorizedAccessException ||
        (ex is IOException and not FileNotFoundException and not DirectoryNotFoundException);

    private static int BackoffDelayMilliseconds(int attempt) =>
        Math.Min(10 * attempt, 250);
}
