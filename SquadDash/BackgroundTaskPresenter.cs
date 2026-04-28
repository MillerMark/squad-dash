using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Owns all background-task state and the methods that reason about background
/// agents, shells, and fallback live threads.  Extracted from MainWindow step 8.
/// </summary>
internal sealed class BackgroundTaskPresenter {

    // ── Static timing constants ──────────────────────────────────────────────

    private static readonly TimeSpan BackgroundCompletionVisibilityWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan BackgroundAnnouncementDedupWindow    = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan BackgroundReportPromotionDelay       = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BackgroundStallThreshold             = TimeSpan.FromMinutes(2);

    // ── Injectable state ─────────────────────────────────────────────────────

    private readonly AgentThreadRegistry                   _agentThreadRegistry;
    private readonly Action<string, Brush>                 _appendLine;
    private readonly Action                                _syncAgentCards;
    private readonly Func<bool>                            _isPromptRunning;
    private readonly Func<TranscriptTurnView?>             _currentTurn;
    private readonly Func<string, Brush>                   _themeBrush;
    private readonly Action<Action, string>                _tryPostToUi;
    private readonly Func<bool>                            _isClosing;
    private readonly Action<string, string, string>        _updateLeadAgent;
    private readonly Action<string>                        _updateSessionState;
    private readonly Action<TranscriptThreadState>         _persistAgentThreadSnapshot;
    private readonly Func<CurrentTurnStatusSnapshot>       _currentTurnSnapshot;
    private readonly TimeSpan                              _agentActiveDisplayLinger;
    private readonly TimeSpan                              _dynamicAgentHistoryRetention;

    // ── Owned mutable state ──────────────────────────────────────────────────

    private IReadOnlyList<SquadBackgroundAgentInfo> _backgroundAgents = Array.Empty<SquadBackgroundAgentInfo>();
    private IReadOnlyList<SquadBackgroundShellInfo> _backgroundShells = Array.Empty<SquadBackgroundShellInfo>();
    private string?          _lastCompletedBackgroundTaskSummary;
    private DateTimeOffset?  _lastCompletedBackgroundTaskAt;
    private string?          _lastBackgroundAnnouncementKey;
    private DateTimeOffset?  _lastBackgroundAnnouncementAt;
    private bool             _skipNextBackgroundCompletionFallback;
    private readonly Dictionary<string, int> _backgroundReportPromotionGenerations
        = new(StringComparer.OrdinalIgnoreCase);

    // ── Exposed state (MainWindow reads/writes these) ────────────────────────

    internal IReadOnlyList<SquadBackgroundAgentInfo> BackgroundAgents {
        get => _backgroundAgents;
        set => _backgroundAgents = value;
    }

    internal IReadOnlyList<SquadBackgroundShellInfo> BackgroundShells {
        get => _backgroundShells;
        set => _backgroundShells = value;
    }

    internal bool SkipNextBackgroundCompletionFallback {
        get => _skipNextBackgroundCompletionFallback;
        set => _skipNextBackgroundCompletionFallback = value;
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    internal BackgroundTaskPresenter(
        AgentThreadRegistry             agentThreadRegistry,
        Action<string, Brush>           appendLine,
        Action                          syncAgentCards,
        Func<bool>                      isPromptRunning,
        Func<TranscriptTurnView?>       currentTurn,
        Func<string, Brush>             themeBrush,
        Action<Action, string>          tryPostToUi,
        Func<bool>                      isClosing,
        Action<string, string, string>  updateLeadAgent,
        Action<string>                  updateSessionState,
        Action<TranscriptThreadState>   persistAgentThreadSnapshot,
        Func<CurrentTurnStatusSnapshot> currentTurnSnapshot,
        TimeSpan                        agentActiveDisplayLinger,
        TimeSpan                        dynamicAgentHistoryRetention) {
        _agentThreadRegistry          = agentThreadRegistry;
        _appendLine                   = appendLine;
        _syncAgentCards               = syncAgentCards;
        _isPromptRunning              = isPromptRunning;
        _currentTurn                  = currentTurn;
        _themeBrush                   = themeBrush;
        _tryPostToUi                  = tryPostToUi;
        _isClosing                    = isClosing;
        _updateLeadAgent              = updateLeadAgent;
        _updateSessionState           = updateSessionState;
        _persistAgentThreadSnapshot   = persistAgentThreadSnapshot;
        _currentTurnSnapshot          = currentTurnSnapshot;
        _agentActiveDisplayLinger     = agentActiveDisplayLinger;
        _dynamicAgentHistoryRetention = dynamicAgentHistoryRetention;
    }

    // ── State management ─────────────────────────────────────────────────────

    /// <summary>Clears all owned mutable state — called by MainWindow.ClearSessionView.</summary>
    internal void ClearState() {
        _backgroundReportPromotionGenerations.Clear();
        _backgroundAgents = Array.Empty<SquadBackgroundAgentInfo>();
        _backgroundShells = Array.Empty<SquadBackgroundShellInfo>();
        _lastCompletedBackgroundTaskSummary = null;
        _lastCompletedBackgroundTaskAt      = null;
        _lastBackgroundAnnouncementKey      = null;
        _lastBackgroundAnnouncementAt       = null;
        _skipNextBackgroundCompletionFallback = false;
    }

    /// <summary>
    /// Removes a thread's pending report-promotion entry — called by
    /// MainWindow.RemoveTemporaryAgents when purging inactive threads.
    /// </summary>
    internal void RemovePromotionEntry(string threadId) =>
        _backgroundReportPromotionGenerations.Remove(threadId);

    // ── Public-facing query ──────────────────────────────────────────────────

    internal bool HasBackgroundTasks() =>
        _backgroundAgents.Count > 0 || _backgroundShells.Count > 0 || GetFallbackLiveBackgroundThreads().Count > 0;

    internal BackgroundAbortTarget? TryResolveAbortTarget(TranscriptThreadState? selectedThread, bool allowSingleFallback = true) {
        if (selectedThread is { Kind: TranscriptThreadKind.Agent, IsPlaceholderThread: false }) {
            var matchingAgent = TryFindLiveBackgroundAgentForThread(selectedThread);
            if (matchingAgent is not null && !string.IsNullOrWhiteSpace(matchingAgent.AgentId)) {
                return new BackgroundAbortTarget(
                    matchingAgent.AgentId.Trim(),
                    "agent",
                    ResolveBackgroundAgentDisplayLabel(matchingAgent));
            }
        }

        if (!allowSingleFallback)
            return null;

        var candidates = BuildAbortCandidates();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    internal IReadOnlyList<BackgroundAbortTarget> GetAbortTargets() =>
        BuildAbortCandidates();

    internal bool IsThreadCurrentRunForDisplay(TranscriptThreadState thread) {
        if (thread.IsPlaceholderThread || AgentThreadRegistry.IsTerminalBackgroundStatus(thread.StatusText))
            return false;

        return thread.IsCurrentBackgroundRun || IsThreadBackedByLiveBackgroundTask(thread);
    }

    internal bool IsThreadStalledForDisplay(TranscriptThreadState thread, DateTimeOffset now) {
        if (!IsThreadCurrentRunForDisplay(thread))
            return false;

        return now - AgentThreadRegistry.GetThreadLastActivityAt(thread) >= BackgroundStallThreshold;
    }

    internal string BuildStalledStatusText(TranscriptThreadState thread, DateTimeOffset now) {
        var quietFor = now - AgentThreadRegistry.GetThreadLastActivityAt(thread);
        return $"Stalled? Quiet for {StatusTimingPresentation.FormatDuration(quietFor)}";
    }

    // ── Lead-agent status ────────────────────────────────────────────────────

    internal void RefreshLeadAgentBackgroundStatus() {
        if (HasBackgroundTasks()) {
            _updateLeadAgent("Background", string.Empty, BuildBackgroundTaskStatusText());
            _updateSessionState("Background work");
            return;
        }

        if (!string.IsNullOrWhiteSpace(_lastCompletedBackgroundTaskSummary) &&
            _lastCompletedBackgroundTaskAt is { } completedAt &&
            DateTimeOffset.Now - completedAt <= BackgroundCompletionVisibilityWindow) {
            _updateLeadAgent("Completed", string.Empty, _lastCompletedBackgroundTaskSummary!);
            _updateSessionState("Ready");
            return;
        }

        _updateLeadAgent("Ready", string.Empty, string.Empty);
        _updateSessionState("Ready");
    }

    private string BuildBackgroundTaskStatusText() {
        var agentLabels = _backgroundAgents
            .Take(2)
            .Select(ResolveBackgroundAgentDisplayLabel)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Concat(GetFallbackLiveBackgroundThreads().Select(BuildBackgroundAgentLabel))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Concat(_backgroundShells.Take(1).Select(BuildBackgroundShellLabel))
            .ToArray();

        if (agentLabels.Length == 0)
            return "Background work is still running.";

        return string.Join(" | ", agentLabels);
    }

    // ── Label builders (static) ──────────────────────────────────────────────

    internal static string BuildBackgroundAgentLabel(SquadBackgroundAgentInfo agent) {
        var displayName = agent.AgentDisplayName?.Trim();
        var agentName   = agent.AgentName?.Trim();
        var description = agent.Description?.Trim();
        var agentId     = agent.AgentId?.Trim();

        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName;

        if (!string.IsNullOrWhiteSpace(agentName))
            return AgentNameHumanizer.Humanize(agentName);

        if (!string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(agentId) &&
            !description.Contains(agentId, StringComparison.OrdinalIgnoreCase)) {
            return $"{description} ({agentId})";
        }

        if (!string.IsNullOrWhiteSpace(description))
            return description;

        if (!string.IsNullOrWhiteSpace(agentId))
            return $"Agent {agentId}";

        return "Background agent";
    }

    internal static string BuildBackgroundAgentLabel(TranscriptThreadState thread) {
        var title   = thread.Title?.Trim();
        var agentId = thread.AgentId?.Trim();

        if (!AgentThreadRegistry.HasRosterBackedIdentity(thread) &&
            !string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(agentId) &&
            !title.Contains(agentId, StringComparison.OrdinalIgnoreCase)) {
            return $"{title} ({agentId})";
        }

        if (!string.IsNullOrWhiteSpace(title))
            return title;

        if (!string.IsNullOrWhiteSpace(agentId))
            return $"Agent {agentId}";

        return "Background agent";
    }

    internal string ResolveBackgroundAgentDisplayLabel(SquadBackgroundAgentInfo agent) {
        if (!string.IsNullOrWhiteSpace(agent.ToolCallId) &&
            _agentThreadRegistry.ThreadsByToolCallId.TryGetValue(agent.ToolCallId.Trim(), out var threadByToolCallId)) {
            return BuildBackgroundAgentLabel(threadByToolCallId);
        }

        if (!string.IsNullOrWhiteSpace(agent.ToolCallId) &&
            _agentThreadRegistry.LaunchesByToolCallId.TryGetValue(agent.ToolCallId.Trim(), out var launchInfo)) {
            return BuildBackgroundAgentLaunchLabel(launchInfo, agent.AgentId);
        }

        if (!string.IsNullOrWhiteSpace(agent.AgentId)) {
            var agentId = agent.AgentId.Trim();
            var existingThread = _agentThreadRegistry.ThreadOrder.FirstOrDefault(thread =>
                string.Equals(thread.AgentId, agentId, StringComparison.OrdinalIgnoreCase));
            if (existingThread is not null)
                return BuildBackgroundAgentLabel(existingThread);
        }

        var matchedThread = _agentThreadRegistry.FindExistingAgentThread(
            agent.ToolCallId,
            agent.AgentId,
            agent.AgentName,
            agent.AgentDisplayName);
        if (matchedThread is not null)
            return BuildBackgroundAgentLabel(matchedThread);

        return BuildBackgroundAgentLabel(agent);
    }

    private static string BuildBackgroundAgentLaunchLabel(BackgroundAgentLaunchInfo launchInfo, string? agentId) {
        var displayName = launchInfo.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(launchInfo.AccentKey))
            return displayName;

        var effectiveAgentId = string.IsNullOrWhiteSpace(agentId)
            ? launchInfo.TaskName?.Trim()
            : agentId.Trim();

        if (string.IsNullOrWhiteSpace(effectiveAgentId) ||
            displayName.Contains(effectiveAgentId, StringComparison.OrdinalIgnoreCase)) {
            return displayName;
        }

        return $"{displayName} ({effectiveAgentId})";
    }

    private string BuildBackgroundAgentReportLine(SquadBackgroundAgentInfo agent, DateTimeOffset now) {
        var label  = ResolveBackgroundAgentDisplayLabel(agent);
        var status = MainWindow.BuildTimedStatusText(
            agent.Status,
            TryParseTimestamp(agent.StartedAt),
            TryParseTimestamp(agent.CompletedAt),
            now);
        var detail = !string.IsNullOrWhiteSpace(agent.LatestIntent)
            ? agent.LatestIntent!.Trim()
            : agent.RecentActivity is { Length: > 0 }
                ? agent.RecentActivity[^1]
                : !string.IsNullOrWhiteSpace(agent.LatestResponse)
                    ? MainWindow.BuildThreadPreview(agent.LatestResponse!)
                    : string.Empty;

        if (string.IsNullOrWhiteSpace(detail))
            return $"{label} [{status}]";

        return $"{label} [{status}] - {detail}";
    }

    private static string BuildBackgroundShellLabel(SquadBackgroundShellInfo shell) {
        var description = shell.Description?.Trim();
        var shellId     = shell.ShellId?.Trim();

        if (!string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(shellId) &&
            !description.Contains(shellId, StringComparison.OrdinalIgnoreCase)) {
            return $"{description} ({shellId})";
        }

        if (!string.IsNullOrWhiteSpace(description))
            return description;

        if (!string.IsNullOrWhiteSpace(shellId))
            return $"Shell {shellId}";

        return "Background shell";
    }

    private string BuildBackgroundShellReportLine(SquadBackgroundShellInfo shell, DateTimeOffset now) {
        var label  = BuildBackgroundShellLabel(shell);
        var status = MainWindow.BuildTimedStatusText(
            shell.Status,
            TryParseTimestamp(shell.StartedAt),
            TryParseTimestamp(shell.CompletedAt),
            now);
        var detail = !string.IsNullOrWhiteSpace(shell.RecentOutput)
            ? MainWindow.BuildThreadPreview(shell.RecentOutput!)
            : !string.IsNullOrWhiteSpace(shell.Command)
                ? shell.Command!.Trim()
                : string.Empty;

        if (string.IsNullOrWhiteSpace(detail))
            return $"{label} [{status}]";

        return $"{label} [{status}] - {detail}";
    }

    // ── Current-turn snapshot ────────────────────────────────────────────────

    internal CurrentTurnStatusSnapshot BuildCurrentTurnStatusSnapshot() =>
        _currentTurnSnapshot();

    // ── Task report ──────────────────────────────────────────────────────────

    internal string BuildBackgroundTaskReport() =>
        BuildBackgroundTaskReport(DateTimeOffset.Now);

    internal string BuildBackgroundTaskReport(DateTimeOffset now) {
        var lines          = new List<string>();
        var fallbackThreads = GetFallbackLiveBackgroundThreads();
        var currentTurnLine = CurrentTurnStatusPresentation.BuildReportLine(BuildCurrentTurnStatusSnapshot(), now);

        if (!string.IsNullOrWhiteSpace(currentTurnLine)) {
            lines.Add("Current turn:");
            lines.Add("• " + currentTurnLine);
            lines.Add(string.Empty);
        }

        if (_backgroundAgents.Count > 0 || _backgroundShells.Count > 0 || fallbackThreads.Count > 0) {
            lines.Add("In progress:");

            foreach (var agent in _backgroundAgents)
                lines.Add("• " + BuildBackgroundAgentReportLine(agent, now));

            foreach (var thread in fallbackThreads)
                lines.Add("• " + BuildFallbackBackgroundThreadReportLine(thread, now));

            foreach (var shell in _backgroundShells)
                lines.Add("• " + BuildBackgroundShellReportLine(shell, now));
        }
        else {
            lines.Add("No live background tasks.");
        }

        if (!string.IsNullOrWhiteSpace(_lastCompletedBackgroundTaskSummary)) {
            lines.Add(string.Empty);
            lines.Add("Last completed:");
            lines.Add(_lastCompletedBackgroundTaskSummary!);
            if (_lastCompletedBackgroundTaskAt is not null)
                lines.Add($"Updated: {StatusTimingPresentation.FormatAgo(now - _lastCompletedBackgroundTaskAt.Value)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    // ── Trace summary ────────────────────────────────────────────────────────

    internal string BuildBackgroundTaskTraceSummary() {
        var agentLabels = _backgroundAgents
            .Select(agent => !string.IsNullOrWhiteSpace(agent.AgentId)
                ? agent.AgentId.Trim()
                : ResolveBackgroundAgentDisplayLabel(agent))
            .Take(4)
            .ToArray();
        var fallbackLabels = GetFallbackLiveBackgroundThreads()
            .Select(BuildBackgroundAgentLabel)
            .Take(4)
            .ToArray();
        var shellLabels = _backgroundShells
            .Select(shell => !string.IsNullOrWhiteSpace(shell.ShellId)
                ? shell.ShellId.Trim()
                : BuildBackgroundShellLabel(shell))
            .Take(4)
            .ToArray();

        return
            $"agents={_backgroundAgents.Count} [{FormatTraceLabels(agentLabels)}] " +
            $"fallbackAgents={fallbackLabels.Length} [{FormatTraceLabels(fallbackLabels)}] " +
            $"shells={_backgroundShells.Count} [{FormatTraceLabels(shellLabels)}]";
    }

    private static string FormatTraceLabels(string[] labels) =>
        labels.Length == 0
            ? "(none)"
            : string.Join(", ", labels);

    // ── Removed-task handling ────────────────────────────────────────────────

    internal void HandleRemovedBackgroundTasks(
        IReadOnlyList<SquadBackgroundAgentInfo> previousAgents,
        IReadOnlyList<SquadBackgroundShellInfo> previousShells) {
        var removedAgents = previousAgents
            .Where(previous => !ContainsBackgroundAgent(_backgroundAgents, previous))
            .ToArray();
        var removedShells = previousShells
            .Where(previous => !ContainsBackgroundShell(_backgroundShells, previous))
            .ToArray();

        if (removedAgents.Length == 0 && removedShells.Length == 0)
            return;

        if (_skipNextBackgroundCompletionFallback) {
            _skipNextBackgroundCompletionFallback = false;
            return;
        }

        foreach (var removedAgent in removedAgents) {
            var thread = _agentThreadRegistry.FindAgentThread(removedAgent);
            if (thread is not null) {
                thread.WasObservedAsBackgroundTask = false;
                ObserveBackgroundAgentActivity(thread, "background_tasks_removed");
            }
        }

        if (removedAgents.Length == 0 && removedShells.Length > 0) {
            RecordBackgroundCompletion(
                BuildBackgroundTaskCompletionSummary(removedAgents, removedShells),
                BuildBackgroundCompletionKey(removedAgents, removedShells));
        }
    }

    // ── Completion recording ─────────────────────────────────────────────────

    internal void RecordBackgroundCompletion(string summary, string announcementKey) {
        if (string.IsNullOrWhiteSpace(summary))
            return;

        _lastCompletedBackgroundTaskSummary = summary.Trim();
        _lastCompletedBackgroundTaskAt      = DateTimeOffset.Now;

        SquadDashTrace.Write(
            "UI",
            $"Background completion recorded key={announcementKey} summary={_lastCompletedBackgroundTaskSummary}");

        AppendBackgroundNotice(_lastCompletedBackgroundTaskSummary, _themeBrush("TaskSuccessText"), announcementKey);
    }

    internal void AppendBackgroundNotice(string text, Brush color, string announcementKey) {
        if (string.IsNullOrWhiteSpace(text) || ShouldSuppressBackgroundAnnouncement(announcementKey))
            return;

        _lastBackgroundAnnouncementKey = announcementKey;
        _lastBackgroundAnnouncementAt  = DateTimeOffset.Now;

        if (!_isPromptRunning() && _currentTurn() is null)
            _appendLine("[background] " + text, color);
    }

    private bool ShouldSuppressBackgroundAnnouncement(string announcementKey) {
        return !string.IsNullOrWhiteSpace(announcementKey) &&
            !string.IsNullOrWhiteSpace(_lastBackgroundAnnouncementKey) &&
            string.Equals(_lastBackgroundAnnouncementKey, announcementKey, StringComparison.OrdinalIgnoreCase) &&
            _lastBackgroundAnnouncementAt is { } previousAnnouncementAt &&
            DateTimeOffset.Now - previousAnnouncementAt <= BackgroundAnnouncementDedupWindow;
    }

    // ── Containment helpers ──────────────────────────────────────────────────

    private bool ContainsBackgroundAgent(
        IReadOnlyList<SquadBackgroundAgentInfo> agents,
        SquadBackgroundAgentInfo candidate) {
        if (!string.IsNullOrWhiteSpace(candidate.ToolCallId)) {
            return agents.Any(agent =>
                string.Equals(agent.ToolCallId, candidate.ToolCallId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(candidate.AgentId)) {
            return agents.Any(agent =>
                string.Equals(agent.AgentId, candidate.AgentId, StringComparison.OrdinalIgnoreCase));
        }

        var candidateLabel = ResolveBackgroundAgentDisplayLabel(candidate);
        return agents.Any(agent =>
            string.Equals(ResolveBackgroundAgentDisplayLabel(agent), candidateLabel, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsBackgroundShell(
        IReadOnlyList<SquadBackgroundShellInfo> shells,
        SquadBackgroundShellInfo candidate) {
        if (!string.IsNullOrWhiteSpace(candidate.ShellId)) {
            return shells.Any(shell =>
                string.Equals(shell.ShellId, candidate.ShellId, StringComparison.OrdinalIgnoreCase));
        }

        var candidateLabel = BuildBackgroundShellLabel(candidate);
        return shells.Any(shell =>
            string.Equals(BuildBackgroundShellLabel(shell), candidateLabel, StringComparison.OrdinalIgnoreCase));
    }

    // ── Completion summary / key builders ────────────────────────────────────

    private string BuildBackgroundTaskCompletionSummary(
        IReadOnlyList<SquadBackgroundAgentInfo> removedAgents,
        IReadOnlyList<SquadBackgroundShellInfo> removedShells) {
        var labels = removedAgents
            .Select(ResolveBackgroundAgentDisplayLabel)
            .Concat(removedShells.Select(BuildBackgroundShellLabel))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return labels.Length switch {
            0 => "Background work completed.",
            1 => labels[0] + " completed.",
            _ => "Completed background work: " + string.Join(" | ", labels)
        };
    }

    private string BuildBackgroundCompletionKey(
        IReadOnlyList<SquadBackgroundAgentInfo> agents,
        IReadOnlyList<SquadBackgroundShellInfo> shells) {
        var parts = agents
            .Select(agent => !string.IsNullOrWhiteSpace(agent.ToolCallId)
                ? "tool-call:" + agent.ToolCallId.Trim()
                : !string.IsNullOrWhiteSpace(agent.AgentId)
                ? "agent:" + agent.AgentId.Trim()
                : "agent-label:" + ResolveBackgroundAgentDisplayLabel(agent))
            .Concat(shells.Select(shell => !string.IsNullOrWhiteSpace(shell.ShellId)
                ? "shell:" + shell.ShellId.Trim()
                : "shell-label:" + BuildBackgroundShellLabel(shell)))
            .OrderBy(part => part, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parts.Length == 0
            ? "background:none"
            : string.Join("|", parts);
    }

    // ── Thread observation ───────────────────────────────────────────────────

    internal void ObserveBackgroundAgentActivity(TranscriptThreadState thread, string reason) {
        thread.LastObservedActivityAt = DateTimeOffset.Now;
        ScheduleAgentThreadFadeIfNeeded(thread);

        if (!thread.WasObservedAsBackgroundTask && !AgentThreadRegistry.IsTerminalBackgroundStatus(thread.StatusText))
            return;

        var isLiveBackgroundTask = IsThreadBackedByLiveBackgroundTask(thread);
        var isTerminal           = AgentThreadRegistry.IsTerminalBackgroundStatus(thread.StatusText);
        if (isLiveBackgroundTask && !isTerminal)
            return;

        ScheduleBackgroundAgentReportPromotion(thread, reason);
    }

    private void ScheduleAgentThreadFadeIfNeeded(TranscriptThreadState thread) {
        if (thread.IsPlaceholderThread || AgentThreadRegistry.IsTerminalBackgroundStatus(thread.StatusText))
            return;

        var observedAt = thread.LastObservedActivityAt ?? DateTimeOffset.Now;
        _ = Task.Run(async () => {
            try {
                await Task.Delay(_agentActiveDisplayLinger).ConfigureAwait(false);
            }
            catch {
                return;
            }

            _tryPostToUi(() => {
                if (_isClosing())
                    return;

                if (thread.LastObservedActivityAt != observedAt)
                    return;

                if (IsThreadBackedByLiveBackgroundTask(thread) || AgentThreadRegistry.IsTerminalBackgroundStatus(thread.StatusText))
                    return;

                _agentThreadRegistry.NormalizeInactiveThreadState(thread);
                _syncAgentCards();
            }, "ThreadFade");
        });
    }

    // ── Report promotion ─────────────────────────────────────────────────────

    private void ScheduleBackgroundAgentReportPromotion(TranscriptThreadState thread, string reason) {
        var nextGeneration = _backgroundReportPromotionGenerations.TryGetValue(thread.ThreadId, out var currentGeneration)
            ? currentGeneration + 1
            : 1;
        _backgroundReportPromotionGenerations[thread.ThreadId] = nextGeneration;

        _ = Task.Run(async () => {
            try {
                await Task.Delay(BackgroundReportPromotionDelay).ConfigureAwait(false);
            }
            catch {
                return;
            }

            _tryPostToUi(
                () => TryPromoteScheduledBackgroundAgentReport(thread.ThreadId, nextGeneration, reason),
                "BackgroundAgentReportPromotion");
        });
    }

    private void TryPromoteScheduledBackgroundAgentReport(
        string threadId,
        int    generation,
        string reason) {
        if (!_backgroundReportPromotionGenerations.TryGetValue(threadId, out var currentGeneration) ||
            currentGeneration != generation) {
            return;
        }

        if (!_agentThreadRegistry.ThreadsByKey.TryGetValue(threadId, out var thread))
            return;

        TryPromoteBackgroundAgentReportToCoordinator(thread, reason);
    }

    private void TryPromoteBackgroundAgentReportToCoordinator(
        TranscriptThreadState thread,
        string                reason) {
        if (_isPromptRunning() || _currentTurn() is not null) {
            ScheduleBackgroundAgentReportPromotion(thread, reason + ":deferred");
            return;
        }

        var announcement = BackgroundAgentReportAnnouncementBuilder.TryBuild(
            thread.Title,
            thread.AgentId,
            thread.Prompt,
            thread.LatestResponse,
            thread.LastCoordinatorAnnouncedResponse,
            thread.WasObservedAsBackgroundTask,
            IsThreadBackedByLiveBackgroundTask(thread),
            AgentThreadRegistry.IsTerminalBackgroundStatus(thread.StatusText));
        if (announcement is null)
            return;

        thread.LastCoordinatorAnnouncedResponse = announcement.FullResponse;
        _persistAgentThreadSnapshot(thread);

        SquadDashTrace.Write(
            "UI",
            $"Promoted background report thread={thread.ThreadId} reason={reason} chars={announcement.Body.Length}");
    }

    // ── Thread ↔ background-task mapping ────────────────────────────────────

    private bool IsThreadBackedBySnapshotBackgroundTask(TranscriptThreadState thread) {
        var snapshot = AgentThreadRegistry.CreateBackgroundTaskThreadSnapshot(thread);
        return BackgroundTaskStateResolver.IsThreadBackedBySnapshot(
            snapshot,
            _backgroundAgents,
            ResolveBackgroundAgentDisplayLabel,
            BuildBackgroundTaskThreadLabel);
    }

    private bool IsFallbackLiveBackgroundThread(TranscriptThreadState thread) {
        var snapshot = AgentThreadRegistry.CreateBackgroundTaskThreadSnapshot(thread);
        return BackgroundTaskStateResolver.IsFallbackLiveThread(
            snapshot,
            _backgroundAgents,
            _isPromptRunning(),
            DateTimeOffset.Now,
            _agentActiveDisplayLinger,
            ResolveBackgroundAgentDisplayLabel,
            BuildBackgroundTaskThreadLabel);
    }

    private IReadOnlyList<TranscriptThreadState> GetFallbackLiveBackgroundThreads() {
        if (_agentThreadRegistry.ThreadOrder.Count == 0)
            return Array.Empty<TranscriptThreadState>();

        var fallbackThreadIds = BackgroundTaskStateResolver.GetFallbackLiveThreads(
                _backgroundAgents,
                _agentThreadRegistry.ThreadOrder.Select(AgentThreadRegistry.CreateBackgroundTaskThreadSnapshot).ToArray(),
                _isPromptRunning(),
                DateTimeOffset.Now,
                _agentActiveDisplayLinger,
                ResolveBackgroundAgentDisplayLabel,
                BuildBackgroundTaskThreadLabel)
            .Select(thread => thread.ThreadId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (fallbackThreadIds.Count == 0)
            return Array.Empty<TranscriptThreadState>();

        return _agentThreadRegistry.ThreadOrder
            .Where(thread => fallbackThreadIds.Contains(thread.ThreadId))
            .OrderByDescending(AgentThreadRegistry.GetThreadLastActivityAt)
            .ThenBy(BuildBackgroundAgentLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool IsThreadBackedByLiveBackgroundTask(TranscriptThreadState thread) {
        if (IsThreadBackedBySnapshotBackgroundTask(thread))
            return true;

        return IsFallbackLiveBackgroundThread(thread);
    }

    private SquadBackgroundAgentInfo? TryFindLiveBackgroundAgentForThread(TranscriptThreadState thread) {
        if (!string.IsNullOrWhiteSpace(thread.ToolCallId)) {
            var toolCallId = thread.ToolCallId.Trim();
            var byToolCallId = _backgroundAgents.FirstOrDefault(agent =>
                string.Equals(agent.ToolCallId?.Trim(), toolCallId, StringComparison.OrdinalIgnoreCase));
            if (byToolCallId is not null)
                return byToolCallId;
        }

        var threadLabel = BuildBackgroundAgentLabel(thread);
        if (!string.IsNullOrWhiteSpace(threadLabel)) {
            var byLabel = _backgroundAgents.FirstOrDefault(agent =>
                string.Equals(
                    ResolveBackgroundAgentDisplayLabel(agent),
                    threadLabel,
                    StringComparison.OrdinalIgnoreCase));
            if (byLabel is not null)
                return byLabel;
        }

        if (!string.IsNullOrWhiteSpace(thread.AgentDisplayName)) {
            var displayName = thread.AgentDisplayName.Trim();
            var byDisplayName = _backgroundAgents.FirstOrDefault(agent =>
                string.Equals(agent.AgentDisplayName?.Trim(), displayName, StringComparison.OrdinalIgnoreCase));
            if (byDisplayName is not null)
                return byDisplayName;
        }

        if (!string.IsNullOrWhiteSpace(thread.AgentName)) {
            var agentName = thread.AgentName.Trim();
            var byAgentName = _backgroundAgents.FirstOrDefault(agent =>
                string.Equals(agent.AgentName?.Trim(), agentName, StringComparison.OrdinalIgnoreCase));
            if (byAgentName is not null)
                return byAgentName;
        }

        return null;
    }

    private List<BackgroundAbortTarget> BuildAbortCandidates() {
        var candidates = new List<BackgroundAbortTarget>();
        var seenTaskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var agent in _backgroundAgents) {
            var taskId = agent.AgentId?.Trim();
            if (string.IsNullOrWhiteSpace(taskId) ||
                IsTerminalBackgroundStatus(agent.Status) ||
                !seenTaskIds.Add(taskId))
                continue;

            candidates.Add(new BackgroundAbortTarget(
                taskId,
                "agent",
                ResolveBackgroundAgentDisplayLabel(agent)));
        }

        foreach (var shell in _backgroundShells) {
            var taskId = shell.ShellId?.Trim();
            if (string.IsNullOrWhiteSpace(taskId) ||
                IsTerminalBackgroundStatus(shell.Status) ||
                !seenTaskIds.Add(taskId))
                continue;

            candidates.Add(new BackgroundAbortTarget(
                taskId,
                "shell",
                BuildBackgroundShellLabel(shell)));
        }

        foreach (var thread in GetFallbackLiveBackgroundThreads()) {
            var taskId = !string.IsNullOrWhiteSpace(thread.AgentId)
                ? thread.AgentId.Trim()
                : thread.ToolCallId?.Trim();
            if (string.IsNullOrWhiteSpace(taskId) ||
                !seenTaskIds.Add(taskId))
                continue;

            candidates.Add(new BackgroundAbortTarget(
                taskId,
                "agent",
                BuildBackgroundAgentLabel(thread)));
        }

        return candidates;
    }

    // ── Fallback-thread report line ──────────────────────────────────────────

    private string BuildFallbackBackgroundThreadReportLine(TranscriptThreadState thread, DateTimeOffset now) {
        var isPlanningWork = BackgroundWorkClassifier.IsPlanningWork(
            thread.Prompt,
            thread.LatestResponse,
            thread.LatestIntent,
            thread.DetailText);
        var status = string.IsNullOrWhiteSpace(thread.StatusText)
            ? "Running"
            : AgentThreadRegistry.HumanizeThreadStatus(thread.StatusText);

        if (isPlanningWork &&
            (string.IsNullOrWhiteSpace(status) ||
             string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(status, "Streaming", StringComparison.OrdinalIgnoreCase))) {
            status = "Planning";
        }

        status = MainWindow.BuildTimedStatusText(status, thread.StartedAt, thread.CompletedAt, now);

        var detail = !string.IsNullOrWhiteSpace(thread.LatestIntent)
            ? thread.LatestIntent!.Trim()
            : thread.RecentActivity is { Length: > 0 }
                ? thread.RecentActivity[^1]
                : !string.IsNullOrWhiteSpace(thread.LatestResponse)
                    ? MainWindow.BuildThreadPreview(thread.LatestResponse!)
                    : !string.IsNullOrWhiteSpace(thread.DetailText)
                        ? thread.DetailText
                        : isPlanningWork
                            ? "Planning work in progress."
                            : string.Empty;

        if (string.IsNullOrWhiteSpace(detail))
            return $"{BuildBackgroundAgentLabel(thread)} [{status}]";

        return $"{BuildBackgroundAgentLabel(thread)} [{status}] - {detail}";
    }

    // ── Thread active / surface helpers ──────────────────────────────────────

    internal bool IsThreadActiveForDisplay(TranscriptThreadState thread) {
        if (thread.IsPlaceholderThread)
            return false;

        if (IsThreadCurrentRunForDisplay(thread))
            return true;

        if (string.IsNullOrWhiteSpace(thread.StatusText))
            return false;

        if (AgentThreadRegistry.IsTerminalBackgroundStatus(thread.StatusText))
            return false;

        return DateTimeOffset.Now - AgentThreadRegistry.GetThreadLastActivityAt(thread) <= _agentActiveDisplayLinger;
    }

    internal static bool ShouldSurfaceDynamicAgentCard(
        TranscriptThreadState thread,
        DateTimeOffset        now,
        TimeSpan              dynamicAgentHistoryRetention) {
        if (AgentThreadRegistry.HasRosterBackedIdentity(thread) || thread.IsPlaceholderThread || !AgentThreadRegistry.HasPersistableThreadContent(thread))
            return false;

        if (AgentThreadRegistry.IsTerminalBackgroundStatus(thread.StatusText))
            return now - AgentThreadRegistry.GetThreadLastActivityAt(thread) <= dynamicAgentHistoryRetention;

        return true;
    }

    // ── Thread-label builders (static) ───────────────────────────────────────

    internal static string BuildBackgroundTaskThreadLabel(BackgroundTaskThreadSnapshot thread) {
        var title   = thread.Title?.Trim();
        var agentId = thread.AgentId?.Trim();

        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(agentId) &&
            !title.Contains(agentId, StringComparison.OrdinalIgnoreCase)) {
            return $"{title} ({agentId})";
        }

        if (!string.IsNullOrWhiteSpace(title))
            return title;

        if (!string.IsNullOrWhiteSpace(agentId))
            return $"Agent {agentId}";

        return "Background agent";
    }

    internal static string BuildThreadCompletionSummary(TranscriptThreadState thread) {
        var label = BuildBackgroundAgentLabel(thread);
        return BackgroundWorkClassifier.BuildCompletionSummary(
            label,
            thread.Prompt,
            thread.LatestResponse,
            thread.LatestIntent,
            thread.DetailText);
    }

    internal static string BuildThreadFailureSummary(TranscriptThreadState thread, string? message) {
        var label = BuildBackgroundAgentLabel(thread);
        return string.IsNullOrWhiteSpace(message)
            ? label + " failed."
            : label + " failed: " + message.Trim();
    }

    internal static string BuildThreadAnnouncementKey(TranscriptThreadState thread) =>
        !string.IsNullOrWhiteSpace(thread.ToolCallId)
            ? "tool-call:" + thread.ToolCallId.Trim()
            : !string.IsNullOrWhiteSpace(thread.AgentId)
                ? "agent:" + thread.AgentId.Trim()
                : "thread:" + thread.ThreadId;

    // ── Timestamp helper ─────────────────────────────────────────────────────

    private static DateTimeOffset? TryParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static bool IsTerminalBackgroundStatus(string? status) =>
        status?.Trim().ToLowerInvariant() switch {
            "completed" => true,
            "failed" => true,
            "cancelled" => true,
            "killed" => true,
            _ => false
        };
}

internal sealed record BackgroundAbortTarget(
    string TaskId,
    string TaskKind,
    string DisplayLabel);
