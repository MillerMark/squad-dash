using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace SquadDash;

internal sealed record MarkdownDetailStateReadResult(bool IsExpanded, Exception? Error = null) {
    public bool Succeeded => Error is null;
}

internal sealed record MarkdownDetailStateWriteResult(Exception? Error = null) {
    public bool Succeeded => Error is null;
}

/// <summary>
/// Application-global %LOCALAPPDATA%\SquadDash\markdown-detail-state.json, independent of content schemas.
/// Stores only versioned hashed section keys and booleans. Every operation reloads under a path-derived
/// named mutex; writes replace atomically with no in-place fallback, preserving unrelated entries.
/// Load never writes, including unseen/default state. Consumers must restore IsExpanded BEFORE
/// attaching change handlers (or suppress handlers while restoring); Save is for explicit user changes,
/// never view initialization, rebuild, or window disposal. The last serialized user change wins.
/// IO/access/JSON/schema/lock failures are traced as MarkdownDetailStateStore and returned in Error.
/// Failed loads return IsExpanded=false AND Succeeded=false, allowing rendering without claiming a
/// successful read. Failed saves/removals return Succeeded=false; callers must not claim persistence.
/// Damaged/unsupported documents remain untouched, and all mutations refuse to reset them.
/// Invalid API arguments throw. Construction does not access storage. There is no bulk reset/pruning.
/// </summary>
internal sealed class MarkdownDetailStateStore {
    internal sealed record Changed(string FilePath, string Key, bool IsExpanded);
    internal static WeakEventBroker Changes { get; } = new();
    private readonly string _mutexName;
    private readonly TimeSpan _mutexTimeout;
    private readonly JsonFileCommitRetrySettings _commitRetrySettings;
    public string FilePath { get; }

    public MarkdownDetailStateStore() : this(Path.Combine(SquadDashPaths.AppData, "markdown-detail-state.json")) { }

    internal MarkdownDetailStateStore(
        string filePath,
        TimeSpan? mutexTimeout = null,
        JsonFileCommitRetrySettings? commitRetrySettings = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _mutexTimeout = mutexTimeout ?? TimeSpan.FromSeconds(2);
        if (_mutexTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(mutexTimeout));
        _commitRetrySettings = commitRetrySettings ?? JsonFileCommitRetrySettings.Default;
        FilePath = Path.GetFullPath(filePath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FilePath.ToUpperInvariant())));
        _mutexName = $@"Local\SquadDash.MarkdownDetailState.{hash}";
    }

    public MarkdownDetailStateReadResult Load(string sectionKey) {
        ValidateKey(sectionKey);
        try {
            using var mutex = AcquireMutex();
            var entries = ReadCore();
            return new(entries.TryGetValue(sectionKey, out var expanded) && expanded);
        }
        catch (Exception ex) when (IsStorageFailure(ex)) {
            ReportFailure("Load", ex);
            return new(false, ex);
        }
    }

    /// <summary>Saves both expanded and collapsed transitions, merging only this key into current disk state.</summary>
    public MarkdownDetailStateWriteResult Save(string sectionKey, bool isExpanded) {
        ValidateKey(sectionKey);
        var result = Mutate("Save", entries => {
            entries[sectionKey] = isExpanded;
            return true;
        });
        if (result.Succeeded)
            Changes.Publish(new Changed(FilePath, sectionKey, isExpanded));
        return result;
    }

    /// <summary>Fixture cleanup may remove only an explicitly identified section, never a workspace or prefix.</summary>
    public MarkdownDetailStateWriteResult Remove(string sectionKey) {
        ValidateKey(sectionKey);
        return Mutate("Remove", entries => entries.Remove(sectionKey));
    }

    private MarkdownDetailStateWriteResult Mutate(string operation, Func<Dictionary<string, bool>, bool> mutate) {
        try {
            using var mutex = AcquireMutex();
            var entries = ReadCore();
            if (mutate(entries)) {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                JsonFileStorage.AtomicWritePreservingPrevious(
                    FilePath,
                    new StateDocument(1, entries),
                    _commitRetrySettings);
            }
            return new();
        }
        catch (Exception ex) when (IsStorageFailure(ex)) {
            ReportFailure(operation, ex);
            return new(ex);
        }
    }

    private MutexLease AcquireMutex() {
        if (!MutexLease.TryAcquire(_mutexName, _mutexTimeout, out var lease) || lease is null)
            throw new TimeoutException("Timed out waiting for Markdown detail state storage.");
        return lease;
    }

    private Dictionary<string, bool> ReadCore() {
        Stream stream;
        try {
            stream = File.OpenRead(FilePath);
        }
        catch (FileNotFoundException) {
            return [];
        }
        catch (DirectoryNotFoundException) {
            return [];
        }

        using (stream)
        using (var document = JsonDocument.Parse(stream)) {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Expected a Markdown detail state document.");
            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject()) {
                if (!properties.Add(property.Name) || property.Name is not ("SchemaVersion" or "Entries"))
                    throw new InvalidDataException("Unknown or duplicate Markdown detail state document property.");
            }
            if (!root.TryGetProperty("SchemaVersion", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var schemaVersion) || schemaVersion != 1 ||
                !root.TryGetProperty("Entries", out var items) || items.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Unsupported or invalid Markdown detail state document; storage was not changed.");

            var entries = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var item in items.EnumerateObject()) {
                if (!MarkdownDetailIdentity.IsSectionKey(item.Name) ||
                    item.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                    !entries.TryAdd(item.Name, item.Value.GetBoolean()))
                    throw new InvalidDataException("Invalid or duplicate Markdown detail state entry; storage was not changed.");
            }
            return entries;
        }
    }

    private static void ValidateKey(string key) {
        ArgumentNullException.ThrowIfNull(key);
        if (!MarkdownDetailIdentity.IsSectionKey(key))
            throw new ArgumentException("Use a versioned section key from MarkdownDetailIdentity.", nameof(key));
    }

    private static bool IsStorageFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException
            or System.Security.SecurityException or TimeoutException or WaitHandleCannotBeOpenedException;

    private void ReportFailure(string operation, Exception error) =>
        SquadDashTrace.Write("MarkdownDetailStateStore", $"{operation} failed for '{FilePath}': {error.Message}");

    private sealed record StateDocument(int SchemaVersion, Dictionary<string, bool> Entries);
}
