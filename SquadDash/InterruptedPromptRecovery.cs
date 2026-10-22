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
    int? InactivitySeconds,
    string? FailureTitle = null,
    string? FailureCategory = null,
    DateTimeOffset? OriginalTurnStartedAt = null,
    IReadOnlyList<string>? ActiveAgentThreadIds = null,
    string? PlanId = null,
    string? PlanTaskId = null,
    string? PlanRevision = null,
    string? PlanExecutionAttemptId = null,
    DateTimeOffset? LastConfirmedActivityAt = null,
    string? LastConfirmedActivity = null,
    int? PendingChangedFileCount = null,
    bool AutomaticContinuationAttempted = false) {

    internal const int CurrentSchemaVersion = 1;
}

internal static class InterruptedPromptAutomaticRecoveryPolicy
{
    internal static bool ShouldAutomaticallyContinue(InterruptedPromptRecoveryState recovery) =>
        recovery.FailureCount == 1 && !recovery.AutomaticContinuationAttempted;
}

internal static class InterruptedPromptPlanRecoveryPolicy
{
    internal static bool MatchesActiveImplementationAttempt(
        InterruptedPromptRecoveryState recovery,
        ActiveLoopExecutionState? execution)
    {
        if (execution?.IsExecutingPlan != true ||
            execution.ActiveVerificationTaskId is not null ||
            execution.ActiveValidationId is not null ||
            execution.PlanExecutionAttempt is not { Status: "active" } attempt)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(recovery.PlanExecutionAttemptId))
        {
            return string.Equals(recovery.PlanId, attempt.PlanId, StringComparison.Ordinal) &&
                   string.Equals(recovery.PlanTaskId, attempt.TaskId, StringComparison.Ordinal) &&
                   string.Equals(recovery.PlanRevision, attempt.Revision, StringComparison.Ordinal) &&
                   string.Equals(recovery.PlanExecutionAttemptId, attempt.AttemptId, StringComparison.Ordinal);
        }

        // Recovery records written before plan identity was persisted can be upgraded safely
        // only when their original prompt contains the exact active attempt capability scope.
        return recovery.Prompt.Contains(attempt.AttemptId, StringComparison.Ordinal) &&
               string.Equals(execution.DecomposeGroupId, attempt.PlanId, StringComparison.Ordinal) &&
               string.Equals(execution.DecomposeRevision, attempt.Revision, StringComparison.Ordinal);
    }
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
            ActiveAgentThreadIds = state.ActiveAgentThreadIds ?? Array.Empty<string>(),
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

    internal static bool IsRecoverableInactivityTimeout(Exception exception) {
        var message = exception.Message ?? string.Empty;
        if (exception is TimeoutException &&
            message.Contains("without bridge activity", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("Timed out waiting for Squad", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        // Legacy SDK wall-clock deadlines and the current request-tree inactivity lease are
        // surfaced as InvalidOperationException rather than TimeoutException. Both are
        // recoverable interruptions because agents and tools may have produced useful work.
        return SessionIdleTimeoutRegex().IsMatch(message) ||
               RequestTreeInactivityTimeoutRegex().IsMatch(message) ||
               message.Contains("Provider request timed out", StringComparison.OrdinalIgnoreCase);
    }

    internal static int? ExtractInactivitySeconds(string errorMessage) {
        var match = InactivitySecondsRegex().Match(errorMessage ?? string.Empty);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var seconds))
            return seconds;

        var millisecondsMatch = TimeoutMillisecondsRegex().Match(errorMessage ?? string.Empty);
        return millisecondsMatch.Success &&
               long.TryParse(millisecondsMatch.Groups[1].Value, out var milliseconds)
            ? (int)Math.Max(1, Math.Ceiling(milliseconds / 1000d))
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
        var isBridgeInactivity = state.ErrorMessage.Contains(
            "without bridge activity",
            StringComparison.OrdinalIgnoreCase);
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
        var failureLead = isBridgeInactivity
            ? $"Squad stopped producing activity for {duration}, so SquadDash restarted the runtime."
            : $"{state.FailureTitle ?? "Provider request timed out"} after {duration} before Squad returned a complete answer.";
        var timeoutClarification = isBridgeInactivity
            ? string.Empty
            : " This timeout does not by itself indicate that the model ran out of tokens or budget; SquadDash reports those conditions separately when the provider supplies them.";
        var evidence = BuildRecoveryEvidence(state);
        var automaticRecoveryText = state.AutomaticContinuationAttempted || state.FailureCount > 1
            ? " SquadDash already attempted automatic recovery; this checkpoint now needs your direction."
            : string.Empty;
        return $"{failureLead}{restoredText}{timeoutClarification}{automaticRecoveryText}{evidence}\n\n" +
               $"SquadDash marked this request Interrupted—recoverable and paused queued prompts and loop progress at this checkpoint.{activeAgentsText} Child-agent work may have produced partial edits or artifacts, and any later child report remains part of this recovery.\n\n" +
               "Continuing will preserve the same request lineage, inspect existing changes and child-agent output, clean up temporary resources created by the interrupted run when identifiable, and continue only if the audit finds remaining work.\n\n" +
               $"[{BuildPrimaryActionLabel(state)}] [{EditPromptAction}] [{ViewDiagnosticsAction}] [{DismissAction}]{doctorAction}";
    }

    internal static string BuildAutomaticRecoveryText(
        InterruptedPromptRecoveryState state,
        bool restoredAfterRestart = false) {
        var duration = state.InactivitySeconds is { } seconds
            ? FormatDuration(seconds)
            : "the inactivity limit";
        var restoredText = restoredAfterRestart
            ? " The preserved recovery checkpoint was restored after restart."
            : string.Empty;
        return $"Squad stopped producing bridge activity for {duration}, so SquadDash restarted the runtime.{restoredText}" +
               BuildRecoveryEvidence(state) + "\n\n" +
               "SquadDash is automatically auditing and continuing the preserved work. Queued prompts and loop progress remain held only while that audit runs; no action is required unless recovery cannot continue safely.";
    }

    internal static string BuildResumeAttemptText(InterruptedPromptRecoveryState state) {
        var target = CleanLabel(state.TargetAgentLabel);
        var route = string.IsNullOrWhiteSpace(target)
            ? "the preserved request"
            : $"{target}'s preserved request";
        return $"Attempting to recover the interrupted request from {route}. " +
               "SquadDash is preserving its recovery lineage, partial edits, artifacts, and child-agent output while it determines whether any work remains.";
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
        builder.AppendLine("Continue the same work interrupted by the previous timeout. This is a recovery of the existing request, not a new task.");
        builder.AppendLine($"Recovery lineage: {state.RecoveryId}.");
        builder.AppendLine("Before changing anything, inspect the current working tree, recent tool output, agent threads, and any produced artifacts. Reconcile what already completed and determine whether anything remains. Continue only work the audit finds unfinished; do not blindly repeat commands or edits that may already have succeeded.");
        builder.AppendLine("Treat overlapping edits and artifacts as candidate work from this interrupted request unless evidence shows they predated it. Do not reject or abandon them merely because they are already present; audit them, adopt the valid work, and correct the remainder.");
        builder.AppendLine("Inspect child-agent status and reports. Reuse completed child work, wait for or reconcile work still finishing, and clean up temporary processes, configuration files, and validation artifacts created solely for the interrupted run. Explicitly report anything that cannot be safely cleaned up.");

        if (state.OriginalTurnStartedAt is { } startedAt)
            builder.AppendLine($"The interrupted request began at {startedAt:O}; use that timestamp as one provenance clue when distinguishing task work from older user-owned changes.");

        var activeAgents = state.ActiveAgentLabels
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (activeAgents.Length > 0) {
            builder.AppendLine();
            builder.AppendLine($"Previously active agents: {string.Join(", ", activeAgents)}. Reuse or resume their work when it remains available, and recover equivalent ownership if their prior runtime threads cannot be resumed.");
        }

        var activeThreadIds = (state.ActiveAgentThreadIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (activeThreadIds.Length > 0) {
            builder.AppendLine();
            builder.AppendLine($"Previously active agent thread IDs: {string.Join(", ", activeThreadIds)}. Inspect and continue these exact threads before spawning replacements; the IDs are part of the preserved task lineage.");
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
        var isBridgeInactivity = state.ErrorMessage.Contains(
            "without bridge activity",
            StringComparison.OrdinalIgnoreCase);
        builder.AppendLine(isBridgeInactivity
            ? "SquadDash detected a runtime inactivity timeout and restarted the bridge process."
            : "SquadDash detected that the provider or agent session reached its request deadline before returning a complete answer.");
        if (!isBridgeInactivity)
            builder.AppendLine("The reported timeout does not identify token, context, or account-budget exhaustion.");
        builder.AppendLine();
        builder.AppendLine($"Interrupted: {state.InterruptedAt.ToLocalTime():F}");
        builder.AppendLine($"Execution route: {state.ExecutionKind}");
        builder.AppendLine($"Target agent: {state.TargetAgentLabel ?? state.TargetAgentHandle ?? "Coordinator"}");
        if (state.ActiveAgentLabels.Count > 0)
            builder.AppendLine($"Previously active agents: {string.Join(", ", state.ActiveAgentLabels)}");
        if ((state.ActiveAgentThreadIds?.Count ?? 0) > 0)
            builder.AppendLine($"Previously active agent threads: {string.Join(", ", state.ActiveAgentThreadIds!)}");
        builder.AppendLine($"Failure count: {state.FailureCount}");
        if (!string.IsNullOrWhiteSpace(state.FailureTitle))
            builder.AppendLine($"Failure: {state.FailureTitle}");
        if (!string.IsNullOrWhiteSpace(state.FailureCategory))
            builder.AppendLine($"Failure category: {state.FailureCategory}");
        if (state.OriginalTurnStartedAt is { } startedAt)
            builder.AppendLine($"Original turn started: {startedAt.ToLocalTime():F}");
        if (state.LastConfirmedActivityAt is { } lastActivityAt)
            builder.AppendLine($"Last confirmed activity: {lastActivityAt.ToLocalTime():F}");
        if (!string.IsNullOrWhiteSpace(state.LastConfirmedActivity))
            builder.AppendLine($"Last activity detail: {state.LastConfirmedActivity}");
        if (state.PendingChangedFileCount is { } changedFileCount)
            builder.AppendLine($"Current changed worktree entries: {changedFileCount}");
        builder.AppendLine($"Automatic continuation attempted: {state.AutomaticContinuationAttempted}");
        if (!string.IsNullOrWhiteSpace(state.SessionId))
            builder.AppendLine($"Session: {state.SessionId}");
        if (!string.IsNullOrWhiteSpace(recoveryPath))
            builder.AppendLine($"Recovery record: {recoveryPath}");
        builder.AppendLine();
        builder.AppendLine("Raw error");
        builder.Append(state.ErrorMessage);
        return builder.ToString();
    }

    private static string BuildRecoveryEvidence(InterruptedPromptRecoveryState state) {
        var facts = new List<string>();
        if (!string.IsNullOrWhiteSpace(state.LastConfirmedActivity)) {
            var activityOwners = state.ActiveAgentLabels
                .Select(CleanLabel)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToArray();
            var activityOwner = activityOwners.Length == 1
                ? $"{activityOwners[0]} — "
                : string.Empty;
            var timing = state.LastConfirmedActivityAt is { } activityAt
                ? $" at {activityAt.ToLocalTime():t}"
                : string.Empty;
            facts.Add($"Last confirmed activity: {activityOwner}{state.LastConfirmedActivity.Trim()}{timing}");
        }
        else if (state.LastConfirmedActivityAt is { } activityAt) {
            facts.Add($"Last confirmed activity was at {activityAt.ToLocalTime():t}");
        }

        if (state.PendingChangedFileCount is { } changedFileCount) {
            facts.Add(changedFileCount == 1
                ? "The working tree currently contains 1 changed entry"
                : $"The working tree currently contains {changedFileCount} changed entries");
        }

        facts.Add("No completion handoff was received");
        return facts.Count == 0 ? string.Empty : $"\n\n{string.Join(". ", facts)}.";
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
        if (seconds >= 3600 && seconds % 3600 == 0) {
            var hours = seconds / 3600;
            return hours == 1 ? "1 hour" : $"{hours} hours";
        }
        if (seconds >= 60 && seconds % 60 == 0) {
            var minutes = seconds / 60;
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }
        return seconds == 1 ? "1 second" : $"{seconds} seconds";
    }

    [GeneratedRegex(@"after\s+(\d+)\s+seconds", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InactivitySecondsRegex();

    [GeneratedRegex(@"Timeout\s+after\s+(\d+)ms\s+waiting\s+for\s+session\.idle", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SessionIdleTimeoutRegex();

    [GeneratedRegex(@"Timeout\s+after\s+(\d+)ms\s+without\s+meaningful\s+activity\s+in\s+the\s+request\s+tree", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequestTreeInactivityTimeoutRegex();

    [GeneratedRegex(@"Timeout\s+after\s+(\d+)ms", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimeoutMillisecondsRegex();
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
        var activeThreadIds = relatedThreads
            .Select(thread => thread.ThreadId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
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

        var errorMessage = traceFailure.Value.ErrorMessage;
        var failure = ProviderFailurePresentation.Analyze(errorMessage);
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
            errorMessage,
            traceFailure.Value.InactivitySeconds,
            failure.Title,
            failure.Category.ToString(),
            turn.StartedAt,
            activeThreadIds);
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
                var notes = AttachmentBlockFormatter.ExtractAttachmentNotes(block);
                if (string.Equals(metadata.Type, "transcript", StringComparison.Ordinal))
                {
                    return new FollowUpAttachment(
                        string.Empty,
                        label,
                        null,
                        TranscriptQuote: content,
                        Notes: notes,
                        TranscriptSource: AttachmentBlockFormatter.ExtractTranscriptSource(block));
                }
                return new FollowUpAttachment(
                    string.Empty,
                    label,
                    null,
                    ContentBlock: content,
                    Notes: notes);
            })
            .ToArray();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    [GeneratedRegex(@"(?m)^(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[Bridge\] Prompt inactivity timeout reached after (?<seconds>\d+)s\.")]
    private static partial Regex TraceTimeoutRegex();
}
