using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed record PromptDispatchDiagnosticRecord(
    [property: JsonPropertyName("capturedAt")]
    DateTimeOffset CapturedAt,
    [property: JsonPropertyName("workspaceFolder")]
    string WorkspaceFolder,
    [property: JsonPropertyName("sessionId")]
    string? SessionId,
    [property: JsonPropertyName("configDirectory")]
    string? ConfigDirectory,
    [property: JsonPropertyName("source")]
    string Source,
    [property: JsonPropertyName("queueItemId")]
    string? QueueItemId,
    [property: JsonPropertyName("queueNumber")]
    int? QueueNumber,
    [property: JsonPropertyName("queueSequenceNumber")]
    int? QueueSequenceNumber,
    [property: JsonPropertyName("queueRemainingAfterDispatch")]
    int PendingQueueItemCount,
    [property: JsonPropertyName("isDictated")]
    bool IsDictated,
    [property: JsonPropertyName("isFromRemote")]
    bool IsFromRemote,
    [property: JsonPropertyName("isSystemInjected")]
    bool IsSystemInjected,
    [property: JsonPropertyName("sourceTag")]
    string? SourceTag,
    [property: JsonPropertyName("visiblePromptHash")]
    string VisiblePromptHash,
    [property: JsonPropertyName("visiblePromptLength")]
    int VisiblePromptLength,
    [property: JsonPropertyName("bridgePromptHash")]
    string BridgePromptHash,
    [property: JsonPropertyName("bridgePromptLength")]
    int BridgePromptLength,
    [property: JsonPropertyName("visiblePrompt")]
    string VisiblePrompt,
    [property: JsonPropertyName("bridgePrompt")]
    string BridgePrompt);

internal static class PromptDispatchDiagnosticsStore {
    internal const int MaxRecords = 20;
    internal const string RelativePath = ".squad/diagnostics/recent-prompts.json";

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true
    };

    internal static string GetPath(string workspaceFolder) =>
        Path.Combine(workspaceFolder, ".squad", "diagnostics", "recent-prompts.json");

    internal static void Append(
        string workspaceFolder,
        PromptDispatchDiagnosticRecord record,
        int maxRecords = MaxRecords) {
        if (string.IsNullOrWhiteSpace(workspaceFolder))
            return;

        var path = GetPath(workspaceFolder);
        var records = Load(path);
        records.Add(record);
        var trimmed = records
            .OrderByDescending(candidate => candidate.CapturedAt)
            .Take(Math.Max(1, maxRecords))
            .OrderBy(candidate => candidate.CapturedAt)
            .ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(trimmed, JsonOptions);
        JsonFileStorage.AtomicWrite(path, json);
    }

    internal static void TryAppend(
        string workspaceFolder,
        PromptDispatchDiagnosticRecord record,
        int maxRecords = MaxRecords) {
        try {
            Append(workspaceFolder, record, maxRecords);
            SquadDashTrace.Write(
                "PromptHealth",
                $"Prompt dispatch diagnostic saved path={RelativePath} source={record.Source} queueNumber={record.QueueNumber?.ToString() ?? "(none)"} bridgeChars={record.BridgePromptLength}");
        }
        catch (Exception ex) {
            SquadDashTrace.Write(
                "PromptHealth",
                $"Prompt dispatch diagnostic save failed: {ex.Message}");
        }
    }

    internal static PromptDispatchDiagnosticRecord CreateRecord(
        string workspaceFolder,
        string? sessionId,
        string? configDirectory,
        PromptQueueItem? queueItem,
        int pendingQueueItemCount,
        string visiblePrompt,
        string bridgePrompt,
        DateTimeOffset? capturedAt = null) {
        var source = queueItem is null
            ? "direct"
            : queueItem.IsSystemInjected
                ? "queue-system"
                : "queue";

        return new PromptDispatchDiagnosticRecord(
            capturedAt ?? DateTimeOffset.UtcNow,
            workspaceFolder,
            string.IsNullOrWhiteSpace(sessionId) ? null : sessionId,
            string.IsNullOrWhiteSpace(configDirectory) ? null : configDirectory,
            source,
            queueItem?.Id,
            queueItem?.QueueNumber,
            queueItem?.SequenceNumber,
            pendingQueueItemCount,
            queueItem?.IsDictated ?? false,
            queueItem?.IsFromRemote ?? false,
            queueItem?.IsSystemInjected ?? false,
            queueItem?.SourceTag,
            Sha256(visiblePrompt),
            visiblePrompt.Length,
            Sha256(bridgePrompt),
            bridgePrompt.Length,
            visiblePrompt,
            bridgePrompt);
    }

    private static List<PromptDispatchDiagnosticRecord> Load(string path) {
        if (!File.Exists(path))
            return [];

        try {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<PromptDispatchDiagnosticRecord>>(json, JsonOptions) ?? [];
        }
        catch {
            return [];
        }
    }

    private static string Sha256(string text) {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
