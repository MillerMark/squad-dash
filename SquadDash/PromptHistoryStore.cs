using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Collections.Generic;

namespace SquadDash;

internal sealed class PromptHistoryStore {
    private const int MaxEntries = 200;
    private const string MutexName = @"Local\SquadDash.PromptHistory";
    private readonly string _historyPath;

    public PromptHistoryStore() {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var historyDirectory = Path.Combine(appData, "SquadDash");
        Directory.CreateDirectory(historyDirectory);
        _historyPath = Path.Combine(historyDirectory, "prompt-history.json");
    }

    public IReadOnlyList<string> Load() {
        using var mutex = AcquireMutex();

        if (!File.Exists(_historyPath)) {
            return [];
        }

        try {
            var json = File.ReadAllText(_historyPath);
            var entries = JsonSerializer.Deserialize<List<string>>(json);
            return entries ?? [];
        }
        catch {
            return [];
        }
    }

    public void Save(IReadOnlyList<string> entries) {
        using var mutex = AcquireMutex();

        IReadOnlyList<string> trimmed = entries;
        if (entries.Count > MaxEntries) {
            trimmed = entries
                .Skip(entries.Count - MaxEntries)
                .ToArray();
        }

        JsonFileStorage.AtomicWrite(_historyPath, trimmed);
    }

    private static MutexLease AcquireMutex() {
        return MutexLease.Acquire(MutexName);
    }
}
