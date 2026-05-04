namespace SquadDash;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

/// <summary>
/// Persists workspace notes as JSON metadata (notes.json) plus per-note .md files under
/// workspaceStateDirectory/notes/. Follows the CommitApprovalStore pattern.
/// </summary>
internal sealed class NotesStore {

    private const string MetaFileName = "notes.json";

    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

    private readonly string _notesDirectory;
    private readonly string _metaFilePath;

    public NotesStore(string workspaceStateDirectory) {
        _notesDirectory = Path.Combine(workspaceStateDirectory, "notes");
        Directory.CreateDirectory(_notesDirectory);
        _metaFilePath = Path.Combine(workspaceStateDirectory, MetaFileName);
    }

    // ── Metadata (list of notes) ──────────────────────────────────────────────

    /// <summary>Loads all note metadata. Returns empty list on any error.</summary>
    public List<NoteItem> LoadAll() {
        try {
            if (!File.Exists(_metaFilePath)) return [];
            var json  = File.ReadAllText(_metaFilePath);
            var items = JsonSerializer.Deserialize<List<NoteItem>>(json, s_options);
            return items ?? [];
        }
        catch {
            return [];
        }
    }

    /// <summary>Atomically overwrites the metadata file with the current note list.</summary>
    public void SaveAll(IReadOnlyList<NoteItem> items) {
        try {
            JsonFileStorage.AtomicWrite(_metaFilePath, items, s_options);
        }
        catch (Exception ex) {
            SquadDashTrace.Write("NotesStore", $"SaveAll failed: {ex.Message}");
        }
    }

    // ── Per-note content ──────────────────────────────────────────────────────

    /// <summary>Returns the full path to the .md file for the given note Id.</summary>
    public string GetNotePath(Guid id) =>
        Path.Combine(_notesDirectory, $"{id:N}.md");

    /// <summary>Reads the markdown content of a note. Returns empty string on any error.</summary>
    public string LoadContent(Guid id) {
        try {
            var path = GetNotePath(id);
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch {
            return string.Empty;
        }
    }

    /// <summary>Writes the initial content for a new note .md file.</summary>
    public void WriteContent(Guid id, string markdown) {
        try {
            File.WriteAllText(GetNotePath(id), markdown, new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex) {
            SquadDashTrace.Write("NotesStore", $"WriteContent {id} failed: {ex.Message}");
        }
    }

    /// <summary>Deletes the .md file for the given note. Silent on failure.</summary>
    public void DeleteContent(Guid id) {
        try {
            var path = GetNotePath(id);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) {
            SquadDashTrace.Write("NotesStore", $"DeleteContent {id} failed: {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Derives an auto-title from raw text: first few words, max 40 chars, trailing ellipsis
    /// if truncated.
    /// </summary>
    public static string DeriveTitle(string text) {
        if (string.IsNullOrWhiteSpace(text)) return "Note";

        // Strip leading markdown headings (#, ##, …)
        var stripped = text.TrimStart();
        if (stripped.StartsWith('#'))
            stripped = stripped.TrimStart('#').TrimStart();

        // Take the first line only
        var firstLine = stripped.Split('\n', 2)[0].Trim();

        if (firstLine.Length == 0) return "Note";

        const int maxChars = 40;
        if (firstLine.Length <= maxChars) return firstLine;

        // Truncate at a word boundary near maxChars
        var cut = firstLine.LastIndexOf(' ', maxChars);
        return cut > 0 ? firstLine[..cut] + "…" : firstLine[..maxChars] + "…";
    }
}
