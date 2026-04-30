namespace SquadDash;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

internal sealed class CommitApprovalStore {
    private const string FileName = "commit-approvals.json";
    private const int MaxItems = 200;
    private readonly string _filePath;
    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

    public CommitApprovalStore(string workspaceStateDirectory) {
        Directory.CreateDirectory(workspaceStateDirectory);
        _filePath = Path.Combine(workspaceStateDirectory, FileName);
    }

    /// <summary>
    /// Returns empty list if file absent or parse fails — never throws.
    /// </summary>
    public List<CommitApprovalItem> Load() {
        try {
            if (!File.Exists(_filePath)) return [];
            var json = File.ReadAllText(_filePath);
            var items = JsonSerializer.Deserialize<List<CommitApprovalItem>>(json, s_options) ?? [];
            // Cap at MaxItems, keeping newest by TurnStartedAt
            if (items.Count > MaxItems)
                items = [.. items.OrderByDescending(i => i.TurnStartedAt).Take(MaxItems)];
            return items;
        }
        catch {
            return [];
        }
    }

    /// <summary>
    /// Atomic write. Call on every Add and every IsApproved toggle.
    /// </summary>
    public void Save(IReadOnlyList<CommitApprovalItem> items) {
        try {
            JsonFileStorage.AtomicWrite(_filePath, items, s_options);
        }
        catch (Exception ex) {
            SquadDashTrace.Write("ApprovalStore", $"Save failed: {ex.Message}");
        }
    }
}
