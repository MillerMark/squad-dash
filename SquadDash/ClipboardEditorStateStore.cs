using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace SquadDash;

internal sealed class ClipboardEditorStateStore {
    private readonly string _stateDirectory;

    public ClipboardEditorStateStore()
        : this(SquadDashPaths.AppData) { }

    internal ClipboardEditorStateStore(string stateDirectory) {
        if (string.IsNullOrWhiteSpace(stateDirectory))
            throw new ArgumentException("State directory cannot be empty.", nameof(stateDirectory));

        _stateDirectory = Path.GetFullPath(stateDirectory);
        Directory.CreateDirectory(_stateDirectory);
    }

    public string GetStateFilePath(string editorId, bool isPending) {
        var suffix = isPending ? "pending" : "active";
        return Path.Combine(_stateDirectory, $"clipboard-editor-{editorId}-{suffix}.json");
    }

    /// <summary>Returns the path for the un-annotated source image sidecar PNG.</summary>
    public string GetSourceImagePath(string editorId)
        => Path.Combine(_stateDirectory, $"clipboard-editor-{editorId}-source.png");

    public async Task SaveAsync(ClipboardEditorSessionState state, string reason) {
        state.Validate();

        var pendingPath = GetStateFilePath(state.EditorId, isPending: true);
        var activePath  = GetStateFilePath(state.EditorId, isPending: false);

        var json = JsonSerializer.Serialize(state, JsonFileStorage.PrettyPrint);

        int[] backoffMs = [1, 5, 10];
        foreach (var delay in backoffMs) {
            try {
                Directory.CreateDirectory(_stateDirectory);
                await File.WriteAllTextAsync(pendingPath, json).ConfigureAwait(false);

                // Rename pending → active
                if (File.Exists(activePath))
                    File.Delete(activePath);
                File.Move(pendingPath, activePath);

                SquadDashTrace.Write("ClipboardPersist", $"Saved editor {state.EditorId}: {reason}");
                return;
            }
            catch (IOException ex) {
                SquadDashTrace.Write("ClipboardPersist",
                    $"SaveAsync retry after {delay}ms for editor {state.EditorId}: {ex.Message}");
                await Task.Delay(delay).ConfigureAwait(false);
            }
        }

        // Final attempt — if it fails, log and return gracefully (don't throw)
        try {
            Directory.CreateDirectory(_stateDirectory);
            await File.WriteAllTextAsync(pendingPath, json).ConfigureAwait(false);

            if (File.Exists(activePath))
                File.Delete(activePath);
            File.Move(pendingPath, activePath);

            SquadDashTrace.Write("ClipboardPersist", $"Saved editor {state.EditorId}: {reason}");
        }
        catch (IOException ex) {
            SquadDashTrace.Write("ClipboardPersist",
                $"SaveAsync failed for editor {state.EditorId}: {ex.Message}");
        }
    }

    public async Task<ClipboardEditorSessionState?> LoadAsync(string editorId) {
        var activePath  = GetStateFilePath(editorId, isPending: false);
        var pendingPath = GetStateFilePath(editorId, isPending: true);

        var path = File.Exists(activePath)  ? activePath
                 : File.Exists(pendingPath) ? pendingPath
                 : null;

        if (path is null)
            return null;

        try {
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize<ClipboardEditorSessionState>(json);
            if (state is null) {
                SquadDashTrace.Write("ClipboardPersist", $"LoadAsync: deserialized null from {path}");
                return null;
            }
            state.Validate();
            return state;
        }
        catch (JsonException ex) {
            SquadDashTrace.Write("ClipboardPersist", $"LoadAsync: invalid JSON in {path}: {ex.Message}");
            return null;
        }
        catch (InvalidOperationException ex) {
            SquadDashTrace.Write("ClipboardPersist", $"LoadAsync: validation failed for {path}: {ex.Message}");
            return null;
        }
        catch (IOException ex) {
            SquadDashTrace.Write("ClipboardPersist", $"LoadAsync: I/O error reading {path}: {ex.Message}");
            return null;
        }
    }

    public async Task<List<(string editorId, ClipboardEditorSessionState state)>> GetAllPendingAsync() {
        var results = new List<(string, ClipboardEditorSessionState)>();

        if (!Directory.Exists(_stateDirectory))
            return results;

        var activeFiles = Directory.GetFiles(_stateDirectory, "clipboard-editor-*-active.json");
        foreach (var file in activeFiles) {
            try {
                var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                var state = JsonSerializer.Deserialize<ClipboardEditorSessionState>(json);
                if (state is null) {
                    SquadDashTrace.Write("ClipboardPersist", $"GetAllPendingAsync: null deserialized from {file}");
                    continue;
                }
                state.Validate();
                results.Add((state.EditorId, state));
            }
            catch (Exception ex) {
                SquadDashTrace.Write("ClipboardPersist",
                    $"GetAllPendingAsync: skipping invalid file {file}: {ex.Message}");
            }
        }

        return results;
    }

    public Task DeleteAsync(string editorId, bool isPending) {
        var path = GetStateFilePath(editorId, isPending);
        try {
            if (File.Exists(path)) {
                File.Delete(path);
                SquadDashTrace.Write("ClipboardPersist", $"Deleted editor state {path}");
            }
        }
        catch (IOException ex) {
            SquadDashTrace.Write("ClipboardPersist", $"DeleteAsync failed for {path}: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    public async Task CleanupStaleFilesAsync(int maxAgeHours = 168) {
        if (!Directory.Exists(_stateDirectory))
            return;

        var cutoff = DateTime.UtcNow.AddHours(-maxAgeHours);
        var staleFiles = Directory.GetFiles(_stateDirectory, "clipboard-editor-*-pending.json");
        int count = 0;

        foreach (var file in staleFiles) {
            try {
                var lastWrite = File.GetLastWriteTimeUtc(file);
                if (lastWrite < cutoff) {
                    File.Delete(file);
                    count++;
                }
            }
            catch (IOException ex) {
                SquadDashTrace.Write("ClipboardPersist", $"CleanupStaleFilesAsync: failed to delete {file}: {ex.Message}");
            }
        }

        SquadDashTrace.Write("ClipboardPersist", $"Cleaned up {count} stale editor states");
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
