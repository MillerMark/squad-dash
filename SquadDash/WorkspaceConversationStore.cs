using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace SquadDash;

internal sealed class WorkspaceConversationStore {
    private const int MaxTurns = 200;
    private const int MaxAgentThreads = 80;
    private const int MaxPromptHistoryEntries = 200;
    private const int MaxRecentSessionIds = 12;
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(14);
    private readonly string _rootDirectory;

    public WorkspaceConversationStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SquadDash",
            "workspaces")) {
    }

    internal WorkspaceConversationStore(string rootDirectory) {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Root directory cannot be empty.", nameof(rootDirectory));

        _rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_rootDirectory);
    }

    public WorkspaceConversationState Load(string workspaceFolder) {
        var normalizedWorkspace = NormalizeWorkspaceFolder(workspaceFolder);
        using var mutex = AcquireMutex(normalizedWorkspace);

        return LoadStateWithRecovery(normalizedWorkspace, repairPrimary: true);
    }

    public WorkspaceConversationState Save(string workspaceFolder, WorkspaceConversationState state) {
        var normalizedWorkspace = NormalizeWorkspaceFolder(workspaceFolder);
        using var mutex = AcquireMutex(normalizedWorkspace);

        var normalized = NormalizeState(state);
        var existing = LoadCore(normalizedWorkspace);
        if (WouldOverwriteNonEmptyState(existing, normalized)) {
            CreateBackup(normalizedWorkspace, existing);
            return existing;
        }

        if (HasMeaningfulContent(existing))
            CreateBackup(normalizedWorkspace, existing);

        SaveCore(normalizedWorkspace, normalized);
        return normalized;
    }

    private WorkspaceConversationState LoadCore(string normalizedWorkspace) {
        return LoadStateWithRecovery(normalizedWorkspace, repairPrimary: false);
    }

    public string GetSessionConfigDirectory(string workspaceFolder) {
        var normalizedWorkspace = NormalizeWorkspaceFolder(workspaceFolder);
        var directory = Path.Combine(GetWorkspaceStateDirectoryCore(normalizedWorkspace), "sdk-config");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private void SaveCore(string normalizedWorkspace, WorkspaceConversationState state) {
        var path = GetConversationPath(normalizedWorkspace);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        JsonFileStorage.AtomicWrite(path, state);
    }

    private WorkspaceConversationState LoadStateWithRecovery(string normalizedWorkspace, bool repairPrimary) {
        var path = GetConversationPath(normalizedWorkspace);
        if (TryLoadState(path, out var state)) {
            var normalized = NormalizeState(state ?? WorkspaceConversationState.Empty);

            var preferredBackupPath = path + ".bak";
            if (!HasMeaningfulContent(normalized) &&
                !IsExplicitClear(normalized) &&
                TryLoadState(preferredBackupPath, out var preferredBackupState)) {
                var preferredNormalizedBackup = NormalizeState(preferredBackupState ?? WorkspaceConversationState.Empty);
                if (HasMeaningfulContent(preferredNormalizedBackup)) {
                    if (repairPrimary)
                        SaveCore(normalizedWorkspace, preferredNormalizedBackup);

                    return preferredNormalizedBackup;
                }
            }

            if (repairPrimary && !Equals(state, normalized))
                SaveCore(normalizedWorkspace, normalized);

            return normalized;
        }

        var backupPath = path + ".bak";
        if (!TryLoadState(backupPath, out var backupState))
            return WorkspaceConversationState.Empty;

        var normalizedBackup = NormalizeState(backupState ?? WorkspaceConversationState.Empty);
        if (repairPrimary && HasMeaningfulContent(normalizedBackup))
            SaveCore(normalizedWorkspace, normalizedBackup);

        return normalizedBackup;
    }

    private static bool TryLoadState(string path, out WorkspaceConversationState? state) {
        state = null;
        if (!File.Exists(path))
            return false;

        try {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return false;

            state = JsonSerializer.Deserialize<WorkspaceConversationState>(json);
            return state is not null;
        }
        catch {
            state = null;
            return false;
        }
    }

    private void CreateBackup(string normalizedWorkspace, WorkspaceConversationState state) {
        if (!HasMeaningfulContent(state))
            return;

        var backupPath = GetConversationPath(normalizedWorkspace) + ".bak";
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions {
            WriteIndented = true
        });
        File.WriteAllText(backupPath, json);
    }

    private static bool WouldOverwriteNonEmptyState(
        WorkspaceConversationState existing,
        WorkspaceConversationState incoming) {
        return HasMeaningfulContent(existing) &&
               !HasMeaningfulContent(incoming) &&
               !IsExplicitClear(incoming);
    }

    private static bool HasMeaningfulContent(WorkspaceConversationState state) {
        return !string.IsNullOrWhiteSpace(state.SessionId) ||
               state.GetRecentSessionIds().Count > 0 ||
               !string.IsNullOrWhiteSpace(state.PromptDraft) ||
               state.PromptHistory.Count > 0 ||
               state.Turns.Count > 0 ||
               state.GetThreads().Count > 0;
    }

    private static bool IsExplicitClear(WorkspaceConversationState state) {
        return state.ClearedAt is not null && !HasMeaningfulContent(state);
    }

    private WorkspaceConversationState NormalizeState(WorkspaceConversationState state) {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - RetentionPeriod;

        var turns = state.Turns
            .Where(turn => turn.Timestamp >= cutoff)
            .Select(turn => NormalizeTurn(turn))
            .OrderBy(turn => turn.StartedAt)
            .ThenBy(turn => turn.Timestamp)
            .ThenBy(turn => turn.Prompt, StringComparer.Ordinal)
            .TakeLast(MaxTurns)
            .ToArray();

        var sessionId = string.IsNullOrWhiteSpace(state.SessionId)
            ? null
            : state.SessionId.Trim();
        var sessionUpdatedAt = state.SessionUpdatedAt?.ToUniversalTime();
        var recentSessionIds = NormalizeRecentSessionIds(state.GetRecentSessionIds(), sessionId);

        if (sessionUpdatedAt is { } updatedAt && updatedAt < cutoff && turns.Length == 0) {
            sessionId = null;
            sessionUpdatedAt = null;
        }

        var threads = state.GetThreads()
            .Select(thread => NormalizeThread(thread, cutoff))
            .Where(thread => thread.Turns.Count > 0 ||
                             !string.IsNullOrWhiteSpace(thread.Prompt) ||
                             !string.IsNullOrWhiteSpace(thread.DetailText) ||
                             !string.IsNullOrWhiteSpace(thread.LatestResponse))
            .OrderBy(thread => thread.StartedAt)
            .TakeLast(MaxAgentThreads)
            .ToArray();

        return new WorkspaceConversationState(
            sessionId,
            sessionUpdatedAt,
            NormalizePromptDraft(state.PromptDraft),
            NormalizePromptHistory(state.PromptHistory),
            turns,
            threads,
            recentSessionIds,
            turns.Length == 0 &&
            threads.Length == 0 &&
            string.IsNullOrWhiteSpace(sessionId) &&
            recentSessionIds.Count == 0 &&
            string.IsNullOrWhiteSpace(state.PromptDraft) &&
            state.PromptHistory.Count == 0
                ? state.ClearedAt?.ToUniversalTime()
                : null) {
            PromptDraftCaretIndex    = state.PromptDraftCaretIndex,
            PromptDraftSelectionStart  = state.PromptDraftSelectionStart,
            PromptDraftSelectionLength = state.PromptDraftSelectionLength,
            QueuedPromptEntries      = state.QueuedPromptEntries is { Count: > 0 } qe ? qe : null,
            LoopMode                 = state.LoopMode,
            LoopContinuousContext    = state.LoopContinuousContext,
        };
    }

    private static TranscriptTurnRecord NormalizeTurn(
        TranscriptTurnRecord turn,
        string? owningThreadStatus = null,
        DateTimeOffset? owningThreadCompletedAt = null) {
        var thoughts = turn.GetThoughts()
            .Select(NormalizeThought)
            .ToArray();
        var responseSegments = turn.GetResponseSegments()
            .Select(segment => new TranscriptResponseSegmentRecord(segment.Text.TrimEnd()) {
                Sequence = NormalizeSequence(segment.Sequence)
            })
            .ToArray();
        var inferredTerminalToolSuccess = owningThreadStatus?.Trim() switch {
            "Completed" => true,
            "Failed" => false,
            "Cancelled" => false,
            _ => (bool?)null
        };
        var tools = turn.Tools
            .Select(tool => {
                var isCompleted = tool.IsCompleted;
                var success = tool.Success;
                var finishedAt = tool.FinishedAt?.ToUniversalTime();
                var detailContent = tool.DetailContent?.Trim();
                var inferredCompletion = false;

                if (!isCompleted && inferredTerminalToolSuccess is { } terminalSuccess) {
                    isCompleted = true;
                    success = terminalSuccess;
                    finishedAt ??= owningThreadCompletedAt?.ToUniversalTime();
                    inferredCompletion = true;
                }

                // Any tool still marked incomplete at load time was abandoned (the session
                // ended without a tool_complete event).  Mark it failed so it never shows
                // "Status: Running" on a past turn.
                if (!isCompleted) {
                    isCompleted = true;
                    success = false;
                    inferredCompletion = true;
                }

                if (inferredCompletion) {
                    // Build the persisted detail-content string via the data-layer helper,
                    // not via ToolTranscriptFormatter, so the persistence layer stays free
                    // of display-layer dependencies.  The formatter's BuildDetailContent
                    // method delegates here for parity.
                    detailContent = ToolTranscriptDetailContent.Build(new ToolTranscriptDetail(
                        NormalizeDescriptor(tool.Descriptor),
                        tool.ArgsJson?.Trim(),
                        tool.OutputText?.Trim(),
                        tool.StartedAt.ToUniversalTime(),
                        finishedAt,
                        tool.ProgressText?.Trim(),
                        isCompleted,
                        success));
                }

                return new TranscriptToolRecord(
                    tool.ToolCallId?.Trim(),
                    NormalizeDescriptor(tool.Descriptor),
                    tool.ArgsJson?.Trim(),
                    tool.StartedAt.ToUniversalTime(),
                    finishedAt,
                    tool.ProgressText?.Trim(),
                    tool.OutputText?.Trim(),
                    detailContent,
                    isCompleted,
                    success) {
                    ThinkingBlockSequence = NormalizeSequence(tool.ThinkingBlockSequence)
                };
            })
            .ToArray();

        return new TranscriptTurnRecord(
            turn.StartedAt.ToUniversalTime(),
            turn.CompletedAt?.ToUniversalTime(),
            turn.Prompt.Trim(),
            turn.ThinkingText.Trim(),
            turn.ResponseText.TrimEnd(),
            turn.ThinkingCollapsed,
            tools,
            thoughts,
            responseSegments);
    }

    private static TranscriptThoughtRecord NormalizeThought(TranscriptThoughtRecord thought) {
        return new TranscriptThoughtRecord(
            string.IsNullOrWhiteSpace(thought.Speaker)
                ? "Coordinator"
                : thought.Speaker.Trim(),
            thought.Text.Trim(),
            thought.Placement) {
            Sequence = NormalizeSequence(thought.Sequence)
        };
    }

    private TranscriptThreadRecord NormalizeThread(TranscriptThreadRecord thread, DateTimeOffset cutoff) {
        var turns = thread.Turns
            .Where(turn => turn.Timestamp >= cutoff)
            .Select(turn => NormalizeTurn(turn, thread.StatusText, thread.CompletedAt))
            .TakeLast(MaxTurns)
            .ToArray();

        var startedAt = thread.StartedAt.ToUniversalTime();
        var completedAt = thread.CompletedAt?.ToUniversalTime();
        if (completedAt is { } finishedAt && finishedAt < cutoff && turns.Length == 0)
            completedAt = null;

        return new TranscriptThreadRecord(
            string.IsNullOrWhiteSpace(thread.ThreadId)
                ? Guid.NewGuid().ToString("N")
                : thread.ThreadId.Trim(),
            string.IsNullOrWhiteSpace(thread.Title)
                ? "Background Agent"
                : thread.Title.Trim(),
            NormalizeSingleLine(thread.AgentId),
            NormalizeSingleLine(thread.ToolCallId),
            NormalizeSingleLine(thread.AgentName),
            NormalizeSingleLine(thread.AgentDisplayName),
            NormalizeMultiline(thread.AgentDescription),
            NormalizeSingleLine(thread.AgentType),
            NormalizeSingleLine(thread.AgentCardKey),
            NormalizeMultiline(thread.Prompt),
            NormalizeMultilineTrimEnd(thread.LatestResponse),
            NormalizeMultilineTrimEnd(thread.LastCoordinatorAnnouncedResponse),
            NormalizeSingleLine(thread.LatestIntent),
            NormalizeRecentActivity(thread.RecentActivity),
            NormalizeMultiline(thread.ErrorText),
            string.IsNullOrWhiteSpace(thread.StatusText) ? string.Empty : thread.StatusText.Trim(),
            string.IsNullOrWhiteSpace(thread.DetailText) ? string.Empty : thread.DetailText.Trim(),
            startedAt,
            completedAt,
            turns,
            NormalizeSingleLine(thread.OriginAgentDisplayName),
            NormalizeSingleLine(thread.OriginParentToolCallId));
    }

    private static int? NormalizeSequence(int? sequence) {
        return sequence is > 0 ? sequence : null;
    }

    private static ToolTranscriptDescriptor NormalizeDescriptor(ToolTranscriptDescriptor descriptor) {
        return new ToolTranscriptDescriptor(
            descriptor.ToolName.Trim(),
            descriptor.Description?.Trim(),
            descriptor.Command?.Trim(),
            descriptor.Path?.Trim(),
            descriptor.Intent?.Trim(),
            descriptor.Skill?.Trim(),
            descriptor.DisplayText?.Trim());
    }

    private static string? NormalizePromptDraft(string? promptDraft) {
        return promptDraft is null
            ? null
            : promptDraft.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static string? NormalizeMultiline(string? value) {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    private static string? NormalizeMultilineTrimEnd(string? value) {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
    }

    private static string? NormalizeSingleLine(string? value) {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static IReadOnlyList<string> NormalizePromptHistory(IReadOnlyList<string>? promptHistory) {
        if (promptHistory is null)
            return Array.Empty<string>();

        return promptHistory
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Select(entry => entry.Replace("\r\n", "\n").Replace('\r', '\n').Trim())
            .TakeLast(MaxPromptHistoryEntries)
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeRecentSessionIds(
        IReadOnlyList<string>? recentSessionIds,
        string? currentSessionId) {
        var deduped = new LinkedList<string>();

        if (!string.IsNullOrWhiteSpace(currentSessionId))
            deduped.AddLast(currentSessionId.Trim());

        if (recentSessionIds is not null) {
            foreach (var entry in recentSessionIds) {
                if (string.IsNullOrWhiteSpace(entry))
                    continue;

                var normalized = entry.Trim();
                if (deduped.Contains(normalized))
                    continue;

                deduped.AddLast(normalized);
            }
        }

        return deduped
            .Take(MaxRecentSessionIds)
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeRecentActivity(IReadOnlyList<string>? recentActivity) {
        if (recentActivity is null)
            return Array.Empty<string>();

        return recentActivity
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Select(entry => entry.Trim())
            .TakeLast(12)
            .ToArray();
    }

    private string GetConversationPath(string normalizedWorkspace) {
        return Path.Combine(GetWorkspaceStateDirectoryCore(normalizedWorkspace), "conversation.json");
    }

    /// <summary>
    /// Returns the per-workspace state directory for the given workspace folder path.
    /// The directory is the same one used by <see cref="Load"/> and <see cref="Save"/>.
    /// </summary>
    internal string GetWorkspaceStateDirectory(string workspaceFolder) {
        var normalized = NormalizeWorkspaceFolder(workspaceFolder);
        return GetWorkspaceStateDirectoryCore(normalized);
    }

    private string GetWorkspaceStateDirectoryCore(string normalizedWorkspace) {
        var directoryName = BuildWorkspaceDirectoryName(normalizedWorkspace);
        return Path.Combine(_rootDirectory, directoryName);
    }

    private static string BuildWorkspaceDirectoryName(string normalizedWorkspace) {
        var name = Path.GetFileName(normalizedWorkspace);
        if (string.IsNullOrWhiteSpace(name))
            name = "workspace";

        var sanitized = new string(
            name.Select(character => char.IsLetterOrDigit(character) ? character : '-')
                .ToArray())
            .Trim('-');

        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "workspace";

        var hash = ComputeHash(normalizedWorkspace);
        return $"{sanitized}-{hash[..12]}";
    }

    private static string ComputeHash(string value) {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(bytes.Length * 2);

        foreach (var valueByte in bytes)
            builder.Append(valueByte.ToString("x2"));

        return builder.ToString();
    }

    private static string NormalizeWorkspaceFolder(string workspaceFolder) {
        if (string.IsNullOrWhiteSpace(workspaceFolder))
            throw new ArgumentException("Workspace folder cannot be empty.", nameof(workspaceFolder));

        return Path.GetFullPath(workspaceFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static MutexLease AcquireMutex(string normalizedWorkspace) {
        var hash = ComputeHash(normalizedWorkspace);
        return MutexLease.Acquire($@"Local\SquadDash.WorkspaceConversation.{hash[..24]}");
    }
}

internal sealed record WorkspaceConversationState(
    string? SessionId,
    DateTimeOffset? SessionUpdatedAt,
    string? PromptDraft,
    IReadOnlyList<string> PromptHistory,
    IReadOnlyList<TranscriptTurnRecord> Turns,
    IReadOnlyList<TranscriptThreadRecord>? Threads = null,
    IReadOnlyList<string>? RecentSessionIds = null,
    DateTimeOffset? ClearedAt = null) {

    public int? PromptDraftCaretIndex { get; init; }
    public int? PromptDraftSelectionStart { get; init; }
    public int? PromptDraftSelectionLength { get; init; }
    /// <summary>Legacy: plain-text list (no dictation flag). Kept for backward-compatible deserialization only.</summary>
    public IReadOnlyList<string>? QueuedPrompts { get; init; }
    /// <summary>Current format: preserves dictation flag per item. Takes precedence over <see cref="QueuedPrompts"/>.</summary>
    public IReadOnlyList<QueuedPromptEntry>? QueuedPromptEntries { get; init; }
    public LoopMode? LoopMode { get; init; }
    public bool? LoopContinuousContext { get; init; }

    public static WorkspaceConversationState Empty { get; } =
        new(
            null,
            null,
            null,
            Array.Empty<string>(),
            Array.Empty<TranscriptTurnRecord>(),
            Array.Empty<TranscriptThreadRecord>(),
            Array.Empty<string>());

    public IReadOnlyList<TranscriptThreadRecord> GetThreads() => Threads ?? Array.Empty<TranscriptThreadRecord>();

    public IReadOnlyList<string> GetRecentSessionIds() => RecentSessionIds ?? Array.Empty<string>();
}

internal sealed record QueuedPromptEntry(string Text, bool IsDictated);

internal enum TranscriptThoughtPlacement {
    BeforeTools,
    AfterTools
}

internal sealed record TranscriptTurnRecord(
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Prompt,
    string ThinkingText,
    string ResponseText,
    bool ThinkingCollapsed,
    IReadOnlyList<TranscriptToolRecord> Tools,
    IReadOnlyList<TranscriptThoughtRecord>? Thoughts = null,
    IReadOnlyList<TranscriptResponseSegmentRecord>? ResponseSegments = null) {

    public DateTimeOffset Timestamp => CompletedAt ?? StartedAt;

    public IReadOnlyList<TranscriptThoughtRecord> GetThoughts() {
        if (Thoughts is { Count: > 0 })
            return Thoughts;

        if (string.IsNullOrWhiteSpace(ThinkingText))
            return Array.Empty<TranscriptThoughtRecord>();

        return [
            new TranscriptThoughtRecord(
                "Coordinator",
                ThinkingText.Trim(),
                TranscriptThoughtPlacement.BeforeTools)
        ];
    }

    public IReadOnlyList<TranscriptResponseSegmentRecord> GetResponseSegments() {
        if (ResponseSegments is { Count: > 0 })
            return ResponseSegments;

        if (string.IsNullOrWhiteSpace(ResponseText))
            return Array.Empty<TranscriptResponseSegmentRecord>();

        return [new TranscriptResponseSegmentRecord(ResponseText.TrimEnd())];
    }
}

internal sealed record TranscriptThoughtRecord(
    string Speaker,
    string Text,
    TranscriptThoughtPlacement Placement) {
    public int? Sequence { get; init; }
}

internal sealed record TranscriptToolRecord(
    string? ToolCallId,
    ToolTranscriptDescriptor Descriptor,
    string? ArgsJson,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? ProgressText,
    string? OutputText,
    string? DetailContent,
    bool IsCompleted,
    bool Success) {
    public int? ThinkingBlockSequence { get; init; }
}

internal sealed record TranscriptResponseSegmentRecord(
    string Text) {
    public int? Sequence { get; init; }
}

internal sealed record TranscriptThreadRecord(
    string ThreadId,
    string Title,
    string? AgentId,
    string? ToolCallId,
    string? AgentName,
    string? AgentDisplayName,
    string? AgentDescription,
    string? AgentType,
    string? AgentCardKey,
    string? Prompt,
    string? LatestResponse,
    string? LastCoordinatorAnnouncedResponse,
    string? LatestIntent,
    IReadOnlyList<string> RecentActivity,
    string? ErrorText,
    string StatusText,
    string DetailText,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<TranscriptTurnRecord> Turns,
    string? OriginAgentDisplayName = null,
    string? OriginParentToolCallId = null);
