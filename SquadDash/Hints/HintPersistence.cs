using System.IO;

namespace SquadDash.Hints;

internal sealed class HintPersistence {
    private static readonly string HistoryFilePath =
        Path.Combine(SquadDashPaths.AppData, "hints-history.json");

    private List<HintRecord> _history = new();
    private bool _historyLoaded;

    // ── Registry (workspace .squad folder, committed to git) ─────────────────

    public List<HintDefinition> LoadRegistry(string workspaceRoot) {
        var path = Path.Combine(workspaceRoot, ".squad", "hints-registry.json");
        return JsonFileStorage.ReadOrDefault(path, new List<HintDefinition>());
    }

    public void SaveRegistry(string workspaceRoot, List<HintDefinition> hints) {
        var squadDir = Path.Combine(workspaceRoot, ".squad");
        Directory.CreateDirectory(squadDir);
        JsonFileStorage.AtomicWrite(Path.Combine(squadDir, "hints-registry.json"), hints);
    }

    // ── History (per-machine AppData, never committed) ────────────────────────

    public List<HintRecord> LoadHistory() {
        _history = JsonFileStorage.ReadOrDefault(HistoryFilePath, new List<HintRecord>());
        _historyLoaded = true;
        return _history;
    }

    public void SaveHistory(List<HintRecord> history) {
        Directory.CreateDirectory(SquadDashPaths.AppData);
        JsonFileStorage.AtomicWrite(HistoryFilePath, history);
        _history = history;
        _historyLoaded = true;
    }

    // ── Record helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the history record for <paramref name="hintId"/>, creating and
    /// registering an empty one in memory if it does not yet exist.
    /// The new record is NOT saved until <see cref="UpdateRecord"/> is called.
    /// </summary>
    public HintRecord? GetRecord(string hintId) {
        if (!_historyLoaded) LoadHistory();
        var record = _history.FirstOrDefault(r => r.HintId == hintId);
        if (record is null) {
            record = new HintRecord { HintId = hintId };
            _history.Add(record);
        }
        return record;
    }

    /// <summary>Upserts <paramref name="record"/> into the history list and saves to disk immediately.</summary>
    public void UpdateRecord(HintRecord record) {
        if (!_historyLoaded) LoadHistory();
        var index = _history.FindIndex(r => r.HintId == record.HintId);
        if (index >= 0)
            _history[index] = record;
        else
            _history.Add(record);
        SaveHistory(_history);
    }
}
