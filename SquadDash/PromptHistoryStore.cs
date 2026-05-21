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
        var historyDirectory = SquadDashPaths.AppData;
        Directory.CreateDirectory(historyDirectory);
        _historyPath = Path.Combine(historyDirectory, "prompt-history.json");
    }

    public IReadOnlyList<string> Load() {
        using var mutex = AcquireMutex();
        return JsonFileStorage.ReadOrDefault<List<string>>(_historyPath, []);
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
