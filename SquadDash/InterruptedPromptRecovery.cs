using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace SquadDash;

internal enum InterruptedPromptExecutionKind {
    Coordinator,
    NamedAgentDelegation,
    NamedAgentDirect,
}

internal sealed record InterruptedPromptQueueContext(
    string Id,
    int QueueNumber,
    string? SourceTag,
    bool IsSystemInjected,
    string? DisplayLabel);

internal sealed record InterruptedPromptRecoveryState(
    int SchemaVersion,
    string RecoveryId,
    DateTimeOffset InterruptedAt,
    InterruptedPromptExecutionKind ExecutionKind,
    string Prompt,
    string DisplayPrompt,
    IReadOnlyList<FollowUpAttachment> Attachments,
    string? TargetAgentHandle,
    string? TargetAgentLabel,
    string? HandoffContext,
    string? SessionId,
    IReadOnlyList<string> ActiveAgentLabels,
    string? CoordinatorLabel,
    InterruptedPromptQueueContext? QueueContext,
    int FailureCount,
    string ErrorMessage,
    int? InactivitySeconds) {

    internal const int CurrentSchemaVersion = 1;
}

internal sealed record InterruptedPromptDispatch(
    InterruptedPromptExecutionKind ExecutionKind,
    string Prompt,
    string DisplayPrompt,
    string? TargetAgentHandle = null,
    string? HandoffContext = null,
    string? SessionId = null,
    bool PreserveExistingRecovery = false);

internal sealed class InterruptedPromptRecoveryStore {
    private const string FileName = "interrupted-prompt-recovery.json";
    private readonly string _path;

    internal InterruptedPromptRecoveryStore(string workspaceStateDirectory) {
        _path = Path.Combine(workspaceStateDirectory, FileName);
    }

    internal string PathForDiagnostics => _path;

    internal InterruptedPromptRecoveryState? Load() {
        var state = JsonFileStorage.ReadOrDefault<InterruptedPromptRecoveryState>(_path, null!);
        if (state is null ||
            state.SchemaVersion != InterruptedPromptRecoveryState.CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(state.RecoveryId) ||
            string.IsNullOrWhiteSpace(state.Prompt)) {
            return null;
        }

        return state with {
            Attachments = state.Attachments ?? Array.Empty<FollowUpAttachment>(),
            ActiveAgentLabels = state.ActiveAgentLabels ?? Array.Empty<string>(),
            FailureCount = Math.Max(1, state.FailureCount),
        };
    }

    internal void Save(InterruptedPromptRecoveryState state) {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        JsonFileStorage.AtomicWrite(_path, state);
        SquadDashTrace.Write(
            "Recovery",
            $"Saved interrupted prompt recovery id={state.RecoveryId} kind={state.ExecutionKind} target={state.TargetAgentHandle ?? "(coordinator)"} agents={state.ActiveAgentLabels.Count} path={_path}");
    }

    internal bool Clear(string reason) {
        try {
            var existed = File.Exists(_path) || File.Exists(_path + ".tmp");
            if (File.Exists(_path))
                File.Delete(_path);
            var tempPath = _path + ".tmp";
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            SquadDashTrace.Write("Recovery", $"Cleared interrupted prompt recovery reason={reason} path={_path}");
            return existed;
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Recovery", $"Could not clear interrupted prompt recovery reason={reason}: {ex.Message}");
            return false;
        }
    }
}

internal static partial class InterruptedPromptRecoveryPresentation {
    internal const string EditPromptAction = "Edit prompt";
    internal const string ViewDiagnosticsAction = "View diagnostics";
    internal const string DismissAction = "Dismiss recovery";
    internal const string RunDoctorAction = "Run Squad Doctor";

    internal static bool IsRecoverableInactivityTimeout(Exception exception) =>
        exception is TimeoutException &&
        exception.Message.Contains("without bridge activity", StringComparison.OrdinalIgnoreCase) &&
        exception.Message.Contains("Timed out waiting for Squad", StringComparison.OrdinalIgnoreCase);

    internal static int? ExtractInactivitySeconds(string errorMessage) {
        var match = InactivitySecondsRegex().Match(errorMessage ?? string.Empty);
        return match.Success && int.TryParse(match.Groups[1].Value, out var seconds)
            ? seconds
            : null;
    }

    internal static string BuildPrimaryActionLabel(InterruptedPromptRecoveryState state) {
        var targetLabel = CleanLabel(state.TargetAgentLabel);
        if (!string.IsNullOrWhiteSpace(targetLabel))
            return $"Continue with {targetLabel}";

        var coordinator = CleanLabel(state.CoordinatorLabel);
        if (!string.IsNullOrWhiteSpace(coordinator))
            return $"Continue with {coordinator}";

        var activeAgents = state.ActiveAgentLabels
            .Select(CleanLabel)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (activeAgents.Length == 1)
            return $"Continue with {activeAgents[0]}";
        if (activeAgents.Length == 2)
            return $"Continue with {activeAgents[0]} and {activeAgents[1]}";
        if (activeAgents.Length > 2)
            return $"Continue with {activeAgents[0]} + {activeAgents.Length - 1} agents";

        return "Continue previous work";
    }

    internal static string BuildCardText(InterruptedPromptRecoveryState state, bool restoredAfterRestart = false) {
        var duration = state.InactivitySeconds is { } seconds
            ? FormatDuration(seconds)
            : "the inactivity limit";
        var restoredText = restoredAfterRestart
            ? " SquadDash recovered the interrupted turn after restart."
            : string.Empty;
        var activeAgents = state.ActiveAgentLabels
            .Select(CleanLabel)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var activeAgentsText = activeAgents.Length == 0
            ? string.Empty
            : $" Previously active agents: {FormatNaturalList(activeAgents)}.";
        var doctorAction = state.FailureCount > 1 ? $" [{RunDoctorAction}]" : string.Empty;
        return $"Squad stopped producing activity for {duration}, so SquadDash restarted the runtime.{restoredText}\n\n" +
               $"SquadDash paused queued prompts and loop progress at this checkpoint.{activeAgentsText} Continuing will first inspect current changes and agent output, then resume only unfinished work.\n\n" +
               $"[{BuildPrimaryActionLabel(state)}] [{EditPromptAction}] [{ViewDiagnosticsAction}] [{DismissAction}]{doctorAction}";
    }

    /// <summary>
    /// A durable recovery is actionable only while its failed turn is still the newest real
    /// transcript turn. A later prompt supersedes it even if the old card remains visible.
    /// </summary>
    internal static bool IsStillLatestUnresolved(
        InterruptedPromptRecoveryState state,
        WorkspaceConversationState conversation) {
        var latestTurn = conversation.Turns.LastOrDefault(turn =>
            !turn.IsSessionBoundary &&
            (!string.IsNullOrWhiteSpace(turn.Prompt) || IsRecoveryCard(state, turn.ResponseText)));
        if (latestTurn is null)
            return false;

        // A card synthesized during an earlier restart is the durable checkpoint surface,
        // not a later AI interaction. Empty-prompt system notices after it are ignored above.
        if (IsRecoveryCard(state, latestTurn.ResponseText))
            return true;

        if (latestTurn.StartedAt > state.InterruptedAt)
            return false;

        return string.Equals(latestTurn.Prompt.Trim(), state.DisplayPrompt.Trim(), StringComparison.Ordinal) ||
               string.Equals(latestTurn.Prompt.Trim(), state.Prompt.Trim(), StringComparison.Ordinal) ||
               LooksLikeRuntimeFailure(latestTurn.ResponseText);
    }

    private static bool IsRecoveryCard(InterruptedPromptRecoveryState state, string responseText) =>
        responseText.Contains($"[{BuildPrimaryActionLabel(state)}]", StringComparison.OrdinalIgnoreCase) &&
        responseText.Contains($"[{EditPromptAction}]", StringComparison.OrdinalIgnoreCase) &&
        responseText.Contains("Squad stopped producing activity", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeRuntimeFailure(string responseText) =>
        responseText.Contains("The prompt stopped before Squad returned a complete answer", StringComparison.OrdinalIgnoreCase) ||
        responseText.Contains("Squad stopped producing activity", StringComparison.OrdinalIgnoreCase) ||
        responseText.Contains("Squad couldn't finish that prompt", StringComparison.OrdinalIgnoreCase);

    internal static string BuildContinuationPrompt(InterruptedPromptRecoveryState state) {
        var builder = new StringBuilder();
        builder.AppendLine("Continue the work interrupted by the previous runtime inactivity timeout.");
        builder.AppendLine("Before changing anything, inspect the current working tree, recent tool output, agent threads, and any produced artifacts. Reconcile what already completed and resume only unfinished work; do not blindly repeat commands or edits that may already have succeeded.");

        var activeAgents = state.ActiveAgentLabels
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (activeAgents.Length > 0) {
            builder.AppendLine();
            builder.AppendLine($"Previously active agents: {string.Join(", ", activeAgents)}. Reuse or resume their work when it remains available, and recover equivalent ownership if their prior runtime threads cannot be resumed.");
        }

        if (state.QueueContext is { } queue) {
            builder.AppendLine();
            builder.AppendLine($"The interrupted request came from queue item #{queue.QueueNumber}{(string.IsNullOrWhiteSpace(queue.DisplayLabel) ? string.Empty : $" ({queue.DisplayLabel})")}. Preserve its original intent and queue provenance while continuing.");
        }

        builder.AppendLine();
        builder.AppendLine("Original request:");
        builder.Append(state.Prompt.Trim());
        return builder.ToString();
    }

    internal static string BuildEditablePrompt(InterruptedPromptRecoveryState state) {
        var prompt = string.IsNullOrWhiteSpace(state.DisplayPrompt)
            ? state.Prompt
            : state.DisplayPrompt;
        var bodyStart = AttachmentBlockFormatter.StripTypedAttachmentHeaders(prompt);
        return bodyStart >= 0 ? prompt[bodyStart..] : prompt;
    }

    internal static string BuildDiagnosticsText(InterruptedPromptRecoveryState state, string? recoveryPath) {
        var builder = new StringBuilder();
        builder.AppendLine("SquadDash detected a runtime inactivity timeout and restarted the bridge process.");
        builder.AppendLine();
        builder.AppendLine($"Interrupted: {state.InterruptedAt.ToLocalTime():F}");
        builder.AppendLine($"Execution route: {state.ExecutionKind}");
        builder.AppendLine($"Target agent: {state.TargetAgentLabel ?? state.TargetAgentHandle ?? "Coordinator"}");
        if (state.ActiveAgentLabels.Count > 0)
            builder.AppendLine($"Previously active agents: {string.Join(", ", state.ActiveAgentLabels)}");
        builder.AppendLine($"Failure count: {state.FailureCount}");
        if (!string.IsNullOrWhiteSpace(state.SessionId))
            builder.AppendLine($"Session: {state.SessionId}");
        if (!string.IsNullOrWhiteSpace(recoveryPath))
            builder.AppendLine($"Recovery record: {recoveryPath}");
        builder.AppendLine();
        builder.AppendLine("Raw error");
        builder.Append(state.ErrorMessage);
        return builder.ToString();
    }

    private static string CleanLabel(string? value) =>
        (value ?? string.Empty).Trim().Replace("[", string.Empty, StringComparison.Ordinal).Replace("]", string.Empty, StringComparison.Ordinal);

    private static string FormatNaturalList(IReadOnlyList<string> values) => values.Count switch {
        0 => string.Empty,
        1 => values[0],
        2 => $"{values[0]} and {values[1]}",
        _ => $"{string.Join(", ", values.Take(values.Count - 1))}, and {values[^1]}",
    };

    private static string FormatDuration(int seconds) {
        if (seconds >= 60 && seconds % 60 == 0) {
            var minutes = seconds / 60;
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }
        return seconds == 1 ? "1 second" : $"{seconds} seconds";
    }

    [GeneratedRegex(@"after\s+(\d+)\s+seconds", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InactivitySecondsRegex();
}

internal static partial class InterruptedPromptRecoveryDetector {
    private const int MaxTraceTailBytes = 512 * 1024;
    private static readonly TimeSpan CorrelationWindow = TimeSpan.FromMinutes(5);

    internal static InterruptedPromptRecoveryState? TryDetect(
        WorkspaceConversationState conversation,
        string workspaceStateDirectory) {
        var turn = conversation.Turns.LastOrDefault(candidate => !candidate.IsSessionBoundary);
        if (turn is null || !LooksLikeRuntimeFailure(turn.ResponseText))
            return null;

        var traceFailure = ReadLatestTraceFailure(Path.Combine(workspaceStateDirectory, "trace.log"));
        if (traceFailure is null)
            return null;

        var completedAt = turn.CompletedAt ?? turn.StartedAt;
        if ((traceFailure.Value.OccurredAt - completedAt).Duration() > CorrelationWindow)
            return null;

        var relatedThreads = conversation.GetThreads()
            .Where(thread => thread.StartedAt >= turn.StartedAt - TimeSpan.FromSeconds(15))
            .Where(thread => thread.StartedAt <= completedAt + TimeSpan.FromSeconds(15))
            .Where(thread => thread.CompletedAt is null || thread.CompletedAt <= completedAt + CorrelationWindow)
            .OrderBy(thread => thread.StartedAt)
            .ToArray();
        var exactPromptThreads = relatedThreads
            .Where(thread => string.Equals(thread.Prompt?.Trim(), turn.Prompt.Trim(), StringComparison.Ordinal))
            .Where(thread => !string.IsNullOrWhiteSpace(thread.AgentName) || !string.IsNullOrWhiteSpace(thread.AgentDisplayName))
            .ToArray();
        var directThread = exactPromptThreads.Length == 1 ? exactPromptThreads[0] : null;
        var activeLabels = relatedThreads
            .Select(thread => FirstNonEmpty(thread.AgentDisplayName, thread.Title, thread.AgentName))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var kind = directThread is null
            ? InterruptedPromptExecutionKind.Coordinator
            : InterruptedPromptExecutionKind.NamedAgentDirect;
        var targetHandle = directThread is null
            ? null
            : FirstNonEmpty(directThread.AgentName, directThread.AgentCardKey, directThread.AgentDisplayName);
        var targetLabel = directThread is null
            ? null
            : FirstNonEmpty(directThread.AgentDisplayName, directThread.Title, directThread.AgentName);

        return new InterruptedPromptRecoveryState(
            InterruptedPromptRecoveryState.CurrentSchemaVersion,
            Guid.NewGuid().ToString("N"),
            traceFailure.Value.OccurredAt.ToUniversalTime(),
            kind,
            turn.Prompt,
            turn.Prompt,
            RecoverTypedAttachments(turn.Prompt),
            targetHandle,
            targetLabel,
            null,
            conversation.SessionId,
            activeLabels,
            "Squad",
            null,
            1,
            traceFailure.Value.ErrorMessage,
            traceFailure.Value.InactivitySeconds);
    }

    private static bool LooksLikeRuntimeFailure(string responseText) =>
        responseText.Contains("The prompt stopped before Squad returned a complete answer", StringComparison.OrdinalIgnoreCase) ||
        responseText.Contains("Squad stopped producing activity", StringComparison.OrdinalIgnoreCase) ||
        responseText.Contains("Squad couldn't finish that prompt", StringComparison.OrdinalIgnoreCase);

    private static (DateTimeOffset OccurredAt, string ErrorMessage, int InactivitySeconds)? ReadLatestTraceFailure(string tracePath) {
        string tail;
        try {
            if (!File.Exists(tracePath))
                return null;
            using var stream = new FileStream(tracePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxTraceTailBytes)
                stream.Seek(-MaxTraceTailBytes, SeekOrigin.End);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            tail = reader.ReadToEnd();
        }
        catch {
            return null;
        }

        var match = TraceTimeoutRegex().Matches(tail).Cast<Match>().LastOrDefault();
        if (match is null ||
            !int.TryParse(match.Groups["seconds"].Value, out var seconds) ||
            !DateTimeOffset.TryParseExact(
                match.Groups["timestamp"].Value,
                "yyyy-MM-dd HH:mm:ss.fff zzz",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var occurredAt)) {
            return null;
        }

        return (
            occurredAt,
            $"Timed out waiting for Squad to finish responding after {seconds} seconds without bridge activity.",
            seconds);
    }

    private static IReadOnlyList<FollowUpAttachment> RecoverTypedAttachments(string prompt) =>
        AttachmentBlockFormatter.ExtractAttachmentBlocks(prompt)
            .Select(block => {
                var metadata = AttachmentBlockFormatter.ExtractAttachmentMetadata(block);
                var content = AttachmentBlockFormatter.ExtractAttachmentContent(block);
                var label = FirstNonEmpty(metadata.Title, metadata.Type, "Attachment")!;
                return new FollowUpAttachment(string.Empty, label, null, ContentBlock: content);
            })
            .ToArray();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    [GeneratedRegex(@"(?m)^(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[Bridge\] Prompt inactivity timeout reached after (?<seconds>\d+)s\.")]
    private static partial Regex TraceTimeoutRegex();
}
