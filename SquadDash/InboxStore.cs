using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SquadDash;

/// <summary>
/// Manages per-workspace inbox messages stored at <c>{squadFolder}/inbox/{id}.json</c>.
/// Thread-safe; all file I/O is guarded by a lock.
/// </summary>
public class InboxStore
{
    private const string InboxSubfolder    = "inbox";
    private const string ArchiveSubfolder  = "archive";

    private readonly string _squadFolder;
    private readonly string _inboxFolder;
    private readonly string _workspaceFolder;
    private readonly object _sync = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented            = true,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Regex MaintenanceReportPrefixRegex =
        new(@"(?i)^maintenance\s+report\s*[:\-\u2013\u2014]?\s*",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TrailingDateRegex =
        new(@"[\s\-\u2013\u2014]+\d{4}[-/]\d{2}[-/]\d{2}\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WhitespaceCollapseRegex =
        new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WordTokenRegex =
        new(@"[a-z0-9_]{3,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public InboxStore(string squadFolder)
    {
        _squadFolder     = squadFolder;
        _inboxFolder     = Path.Combine(squadFolder, InboxSubfolder);
        _workspaceFolder = Path.GetDirectoryName(squadFolder) ?? squadFolder;
    }

    /// <summary>Returns all messages sorted newest-first.</summary>
    public IReadOnlyList<InboxMessage> LoadAll()
    {
        lock (_sync)
        {
            EnsureInboxDirectory();
            return Directory
                .EnumerateFiles(_inboxFolder, "*.json", SearchOption.TopDirectoryOnly)
                .Select(TryReadMessage)
                .Where(m => m is not null)
                .Cast<InboxMessage>()
                .OrderByDescending(m => m.Timestamp)
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>Writes or overwrites the message file for <paramref name="message"/>.</summary>
    public void Save(InboxMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Subject))
        {
            var normalized = NormalizeSubject(message.Subject);
            if (normalized != message.Subject)
                message = message with { Subject = normalized };
        }
        lock (_sync)
        {
            EnsureInboxDirectory();
            var json = JsonSerializer.Serialize(message, JsonOptions);
            JsonFileStorage.AtomicWrite(GetFilePath(message.Id), json);
        }
    }

    public InboxMessage? FindRecentSimilarMessage(InboxMessage message, TimeSpan window)
    {
        if (message is null)
            return null;

        var normalizedSubject = NormalizeSubject(message.Subject ?? string.Empty);
        var normalizedFrom    = (message.From ?? string.Empty).Trim();
        var timestamp         = message.Timestamp;

        lock (_sync)
        {
            if (!Directory.Exists(_inboxFolder))
                return null;

            return Directory
                .EnumerateFiles(_inboxFolder, "*.json", SearchOption.TopDirectoryOnly)
                .Select(TryReadMessage)
                .Where(existing => existing is not null)
                .Cast<InboxMessage>()
                .Where(existing =>
                    string.Equals(NormalizeSubject(existing.Subject ?? string.Empty), normalizedSubject, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((existing.From ?? string.Empty).Trim(), normalizedFrom, StringComparison.OrdinalIgnoreCase) &&
                    timestamp - existing.Timestamp <= window &&
                    existing.Timestamp - timestamp <= window &&
                    AreBodiesSimilar(existing.Body, message.Body))
                .OrderByDescending(existing => existing.Timestamp)
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// Strips common "Code Health Report:" prefixes and trailing date suffixes that
    /// AI agents may include in subject lines despite prompt instructions.
    /// </summary>
    internal static string NormalizeSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return subject;

        // Strip leading "Maintenance Report" prefix (case-insensitive)
        var stripped = MaintenanceReportPrefixRegex.Replace(subject.Trim(), string.Empty).Trim();

        // Strip trailing date (e.g. " — 2026-05-24", " -- 2026-05-24", " - 2026/05/24")
        stripped = TrailingDateRegex.Replace(stripped, string.Empty).Trim();

        return string.IsNullOrWhiteSpace(stripped) ? subject : stripped;
    }

    /// <summary>Loads the message, sets <see cref="InboxMessage.Read"/> = true, and saves.</summary>
    public void MarkRead(string id)
    {
        lock (_sync)
        {
            var msg = TryReadMessage(GetFilePath(id));
            if (msg is null) return;
            var updated = msg with { Read = true };
            var json    = JsonSerializer.Serialize(updated, JsonOptions);
            JsonFileStorage.AtomicWrite(GetFilePath(id), json);
        }
    }

    /// <summary>Loads the message, sets <see cref="InboxMessage.Read"/> = false, and saves.</summary>
    public void MarkUnread(string id)
    {
        lock (_sync)
        {
            var msg = TryReadMessage(GetFilePath(id));
            if (msg is null) return;
            var updated = msg with { Read = false };
            var json    = JsonSerializer.Serialize(updated, JsonOptions);
            JsonFileStorage.AtomicWrite(GetFilePath(id), json);
        }
    }

    /// <summary>Deletes the message file.</summary>
    public void Delete(string id)
    {
        lock (_sync)
        {
            var path = GetFilePath(id);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>Moves the message file to <c>inbox/archive/</c>.</summary>
    public void Archive(string id)
    {
        lock (_sync)
        {
            var src = GetFilePath(id);
            if (!File.Exists(src)) return;

            var archiveDir = Path.Combine(_inboxFolder, ArchiveSubfolder);
            Directory.CreateDirectory(archiveDir);

            var dst = Path.Combine(archiveDir, Path.GetFileName(src));
            File.Move(src, dst, overwrite: true);
        }
    }

    /// <summary>Records that the user clicked an action; persists the update to disk.</summary>
    public void MarkActionUsed(string messageId, string actionLabel)
    {
        lock (_sync)
        {
            var msg = GetById(messageId);
            if (msg is null) return;

            if (msg.UsedActions.Contains(actionLabel)) return;

            var updated = msg with { UsedActions = [.. msg.UsedActions, actionLabel] };
            Save(updated);

            // Replace in-memory too if callers have a reference
            // (callers should re-call LoadAll() to refresh)
        }
    }

    /// <summary>Returns the message with the given <paramref name="id"/>, or null if not found.</summary>
    public InboxMessage? GetById(string id)
    {
        lock (_sync)
        {
            return TryReadMessage(GetFilePath(id));
        }
    }

    /// <summary>Returns the absolute path to the JSON file for the given message id.</summary>
    public string GetMessageFilePath(string id) => GetFilePath(id);

    /// <summary>
    /// Returns true if any inbox message attachment references the given absolute file path.
    /// Attachment paths that are relative are resolved against the workspace folder.
    /// </summary>
    public bool IsReferencedByInboxMessage(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            var normalizedTarget = Path.GetFullPath(filePath);

            lock (_sync)
            {
                if (!Directory.Exists(_inboxFolder))
                    return false;

                foreach (var file in Directory.EnumerateFiles(_inboxFolder, "*.json", SearchOption.TopDirectoryOnly))
                {
                    var msg = TryReadMessage(file);
                    if (msg is null) continue;

                    foreach (var att in msg.Attachments)
                    {
                        if (string.IsNullOrWhiteSpace(att.Path)) continue;

                        try
                        {
                            var resolved = Path.IsPathRooted(att.Path)
                                ? Path.GetFullPath(att.Path)
                                : Path.GetFullPath(Path.Combine(_workspaceFolder, att.Path));

                            if (string.Equals(resolved, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                        catch { /* skip malformed paths */ }
                    }
                }
            }

            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// Returns true if any saved inbox message has a file write time strictly greater than
    /// <paramref name="since"/>. File write time is set when <see cref="Save"/> writes the
    /// message, making it a reliable and fast proxy for <see cref="InboxMessage.Timestamp"/>.
    /// </summary>
    public bool HasMessageSavedSince(DateTimeOffset since)
    {
        lock (_sync)
        {
            if (!Directory.Exists(_inboxFolder))
                return false;

            foreach (var file in Directory.EnumerateFiles(_inboxFolder, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) > since.UtcDateTime)
                        return true;
                }
                catch { /* skip inaccessible files */ }
            }

            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GetFilePath(string id) =>
        Path.Combine(_inboxFolder, $"{id}.json");

    private void EnsureInboxDirectory() =>
        Directory.CreateDirectory(_inboxFolder);

    private static InboxMessage? TryReadMessage(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<InboxMessage>(json, JsonOptions);
        }
        catch { return null; }
    }

    private static bool AreBodiesSimilar(string? first, string? second)
    {
        var firstNormalized  = NormalizeBodyForDuplicateCheck(first);
        var secondNormalized = NormalizeBodyForDuplicateCheck(second);

        if (string.IsNullOrWhiteSpace(firstNormalized) || string.IsNullOrWhiteSpace(secondNormalized))
            return string.Equals(firstNormalized, secondNormalized, StringComparison.Ordinal);

        if (string.Equals(firstNormalized, secondNormalized, StringComparison.Ordinal))
            return true;

        if (firstNormalized.Length >= 120 &&
            secondNormalized.Length >= 120 &&
            (firstNormalized.Contains(secondNormalized, StringComparison.Ordinal) ||
             secondNormalized.Contains(firstNormalized, StringComparison.Ordinal)))
            return true;

        var firstWords  = ExtractComparableWords(firstNormalized);
        var secondWords = ExtractComparableWords(secondNormalized);
        if (firstWords.Count == 0 || secondWords.Count == 0)
            return false;

        var overlap = firstWords.Count(word => secondWords.Contains(word));
        var coverage = overlap / (double)Math.Min(firstWords.Count, secondWords.Count);
        return coverage >= 0.75;
    }

    private static string NormalizeBodyForDuplicateCheck(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        return WhitespaceCollapseRegex.Replace(body.ToLowerInvariant(), " ").Trim();
    }

    private static HashSet<string> ExtractComparableWords(string text) =>
        WordTokenRegex.Matches(text)
            .Select(match => match.Value)
            .Take(4000)
            .ToHashSet(StringComparer.Ordinal);
}

