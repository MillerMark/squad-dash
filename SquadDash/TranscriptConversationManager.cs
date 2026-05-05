using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// Owns workspace conversation state, prompt history, and all persistence operations
/// for transcript turns, agent threads, and conversation state.
/// Extracted from MainWindow to keep conversation lifecycle concerns cohesive.
/// </summary>
internal sealed class TranscriptConversationManager {
    private const int MaxRememberedSessionIds = 12;
    private const int QuickReplyHandoffCoordinatorTurnCount = 4;
    private const int QuickReplyHandoffAgentContextCount = 4;

    // ── Owned state ────────────────────────────────────────────────────────────
    private WorkspaceConversationState _conversationState = WorkspaceConversationState.Empty;
    private readonly WorkspaceConversationStore _conversationStore = new();
    private string? _currentSessionId;
    private readonly List<string> _promptHistory = [];
    private int? _historyIndex;
    private string? _historyDraft;
    private bool _isApplyingHistoryEntry;
    private (string FolderPath, WorkspaceConversationState State)? _pendingConversationSave;
    private readonly object _backgroundSaveGate = new();
    private readonly object _conversationStoreSaveGate = new();
    private (string FolderPath, WorkspaceConversationState State, long Version)? _queuedConversationSave;
    private bool _backgroundSaveLoopRunning;
    private volatile CancellationTokenSource? _backgroundSaveCts;
    private long _nextConversationSaveVersion;
    private long _latestRequestedConversationSaveVersion;
    private readonly DispatcherTimer _agentThreadSnapshotPersistTimer;
    private bool _hasPendingAgentThreadSnapshotPersist;

    // Lazy-render store: agent thread FlowDocuments are NOT populated at startup.
    // Turns are stored here and flushed into the document the first time the user
    // selects that thread.  Keyed by ThreadId (case-insensitive).
    private readonly Dictionary<string, IReadOnlyList<TranscriptTurnRecord>> _pendingAgentRenders
        = new(StringComparer.OrdinalIgnoreCase);

    // ── Virtual window — coordinator transcript ─────────────────────────────────
    // At startup only the last InitialTurnWindow turns are rendered.  Older turns
    // live here and are prepended in batches of PrependBatchSize as the user scrolls
    // toward the top.  Never used for agent threads (they are lazy-rendered separately).
    private IReadOnlyList<TranscriptTurnRecord> _allCoordinatorTurns = [];
    private int _coordinatorRenderedFromIndex;            // index of the first currently-rendered turn
    private const int InitialTurnWindow = 30;
    private const int PrependBatchSize  = 20;
    private static readonly TimeSpan AgentThreadSnapshotPersistDebounce = TimeSpan.FromMilliseconds(1000);
    private bool _prependInProgress;

    // ── Properties exposed to MainWindow ───────────────────────────────────────
    internal WorkspaceConversationState ConversationState {
        get => _conversationState;
        set => _conversationState = value;
    }

    internal WorkspaceConversationStore ConversationStore => _conversationStore;

    internal string? CurrentSessionId {
        get => _currentSessionId;
        set => SetCurrentSessionId(value);
    }

    internal List<string> PromptHistory => _promptHistory;

    internal int? HistoryIndex {
        get => _historyIndex;
        set => _historyIndex = value;
    }

    internal string? HistoryDraft {
        get => _historyDraft;
        set => _historyDraft = value;
    }

    internal bool IsApplyingHistoryEntry {
        get => _isApplyingHistoryEntry;
        set => _isApplyingHistoryEntry = value;
    }

    internal (string FolderPath, WorkspaceConversationState State)? PendingConversationSave {
        get => _pendingConversationSave;
        set => _pendingConversationSave = value;
    }

    // ── Injected dependencies ──────────────────────────────────────────────────
    private readonly Func<SessionWorkspace?>                                          _getWorkspace;
    private readonly Func<string>                                                     _getPromptText;
    private readonly Action<string, int, int, int>                                    _setPromptText;
    private readonly Func<(int caretIndex, int selectionStart, int selectionLength)>  _getPromptCaretState;
    private readonly Func<bool>                                                       _isClosing;
    private readonly Action<TranscriptThreadState, TranscriptTurnRecord, bool>        _renderPersistedTurn;
    private readonly Func<TranscriptThreadState>                                      _coordinatorThread;
    private readonly Func<TranscriptThreadState?>                                     _selectedThread;
    private readonly Action<string>                                                   _maybePublishRoutingIssue;
    private readonly Action                                                           _syncAgentCardsWithThreads;
    private readonly Dispatcher                                                       _dispatcher;
    private readonly Action                                                           _scrollOutputToEnd;
    private readonly AgentThreadRegistry                                              _agentThreadRegistry;
    private readonly Func<IReadOnlyDictionary<string, ToolTranscriptEntry>>          _getToolEntries;
    private readonly Func<TranscriptTurnView?>                                        _getCurrentTurn;
    private readonly Action                                                           _setCurrentTurnNull;
    // Bracket the bulk history-load insertion in a BeginChange/EndChange pair on the
    // RichTextBox so WPF does not attempt to layout the FlowDocument after every
    // Blocks.Add call — instead it fires exactly one layout pass when EndChange() is called.
    private readonly Action                                                           _beginBulkDocumentLoad;
    private readonly Action                                                           _endBulkDocumentLoad;
    // Virtual-window prepend support (coordinator transcript only).
    // _prependTurnsBatch inserts a batch of turns at the FRONT of the FlowDocument.
    // _getScrollableHeight / _getVerticalOffset / _scrollToAbsoluteOffset / _updateScrollLayout
    // are used to compensate the viewport after a prepend so the visible content does not jump.
    private readonly Action<TranscriptThreadState, IReadOnlyList<TranscriptTurnRecord>> _prependTurnsBatch;
    private readonly Func<double>                                                     _getScrollableHeight;
    private readonly Func<double>                                                     _getVerticalOffset;
    private readonly Action<double>                                                   _scrollToAbsoluteOffset;
    private readonly Action                                                           _updateScrollLayout;

    internal TranscriptConversationManager(
        Func<SessionWorkspace?> getWorkspace,
        Func<string> getPromptText,
        Action<string, int, int, int> setPromptText,
        Func<(int caretIndex, int selectionStart, int selectionLength)> getPromptCaretState,
        Func<bool> isClosing,
        Action<TranscriptThreadState, TranscriptTurnRecord, bool> renderPersistedTurn,
        Func<TranscriptThreadState> coordinatorThread,
        Func<TranscriptThreadState?> selectedThread,
        Action<string> maybePublishRoutingIssue,
        Action syncAgentCardsWithThreads,
        Dispatcher dispatcher,
        Action scrollOutputToEnd,
        AgentThreadRegistry agentThreadRegistry,
        Func<IReadOnlyDictionary<string, ToolTranscriptEntry>> getToolEntries,
        Func<TranscriptTurnView?> getCurrentTurn,
        Action setCurrentTurnNull,
        Action beginBulkDocumentLoad,
        Action endBulkDocumentLoad,
        Action<TranscriptThreadState, IReadOnlyList<TranscriptTurnRecord>> prependTurnsBatch,
        Func<double> getScrollableHeight,
        Func<double> getVerticalOffset,
        Action<double> scrollToAbsoluteOffset,
        Action updateScrollLayout) {
        _getWorkspace              = getWorkspace;
        _getPromptText             = getPromptText;
        _setPromptText             = setPromptText;
        _getPromptCaretState       = getPromptCaretState;
        _isClosing                 = isClosing;
        _renderPersistedTurn       = renderPersistedTurn;
        _coordinatorThread         = coordinatorThread;
        _selectedThread            = selectedThread;
        _maybePublishRoutingIssue  = maybePublishRoutingIssue;
        _syncAgentCardsWithThreads = syncAgentCardsWithThreads;
        _dispatcher                = dispatcher;
        _scrollOutputToEnd         = scrollOutputToEnd;
        _agentThreadRegistry       = agentThreadRegistry;
        _getToolEntries            = getToolEntries;
        _getCurrentTurn            = getCurrentTurn;
        _setCurrentTurnNull        = setCurrentTurnNull;
        _beginBulkDocumentLoad     = beginBulkDocumentLoad;
        _endBulkDocumentLoad       = endBulkDocumentLoad;
        _prependTurnsBatch         = prependTurnsBatch;
        _getScrollableHeight       = getScrollableHeight;
        _getVerticalOffset         = getVerticalOffset;
        _scrollToAbsoluteOffset    = scrollToAbsoluteOffset;
        _updateScrollLayout        = updateScrollLayout;
        _agentThreadSnapshotPersistTimer = new DispatcherTimer(
            AgentThreadSnapshotPersistDebounce,
            DispatcherPriority.Background,
            OnAgentThreadSnapshotPersistTimerTick,
            _dispatcher) {
            IsEnabled = false
        };
    }

    // ── Workspace conversation load ─────────────────────────────────────────────

    internal async Task LoadWorkspaceConversationAsync() {
        CancelScheduledAgentThreadSnapshotPersist();
        // Clear any pending renders left over from a previous workspace.
        _pendingAgentRenders.Clear();
        var workspace = _getWorkspace();
        if (workspace is null) {
            _conversationState = WorkspaceConversationState.Empty;
            _currentSessionId = null;
            _promptHistory.Clear();
            ApplyPromptText(string.Empty);
            return;
        }

        // T0 → T1: measure deserialization of the saved conversation JSON.
        // Run the file I/O + JSON parse off the UI thread to avoid blocking Loaded/OpenWorkspace.
        var dataSw = Stopwatch.StartNew();
        var folderPath = workspace.FolderPath;
        _conversationState = await Task.Run(() => _conversationStore.Load(folderPath)).ConfigureAwait(true);
        dataSw.Stop();
        var dataLoadMs = dataSw.ElapsedMilliseconds;

        SquadDashTrace.Write(TraceCategory.Performance, $"DESER: {dataLoadMs}ms turns={_conversationState.Turns.Count}");

        // Reset virtual window so stale state from a previous workspace never leaks in.
        _allCoordinatorTurns        = [];
        _coordinatorRenderedFromIndex = 0;

        _currentSessionId = _conversationState.SessionId;
        _promptHistory.Clear();
        _promptHistory.AddRange(
            _conversationState.PromptHistory.Where(entry => !string.IsNullOrWhiteSpace(entry)));
        ApplyPromptText(
            _conversationState.PromptDraft ?? string.Empty,
            _conversationState.PromptDraftCaretIndex,
            _conversationState.PromptDraftSelectionStart ?? 0,
            _conversationState.PromptDraftSelectionLength ?? 0);

        var threadRestoreSw = Stopwatch.StartNew();
        // RestorePersistedAgentThreads now returns pending (thread, turns) pairs instead
        // of firing concurrent renders, so we can serialize them below.
        var agentRenders = _agentThreadRegistry.RestorePersistedAgentThreads(_conversationState.GetThreads());
        threadRestoreSw.Stop();
        SquadDashTrace.Write(TraceCategory.Performance, $"THREAD_RESTORE: {threadRestoreSw.ElapsedMilliseconds}ms");
        var turns = _conversationState.Turns;
        // Render agent threads sequentially then the coordinator turn — all in one
        // fire-and-forget Task.  Sequential execution eliminates the O(n²) dispatcher
        // queue stacking that occurred when every thread called InvokeAsync concurrently:
        // thread k used to wait for the combined render time of threads 0…k-1 before its
        // own work could start.
        _ = RenderAllSequentiallyAsync(agentRenders, turns, dataLoadMs);
        _syncAgentCardsWithThreads();
    }

    internal IReadOnlyList<string> GetKnownSessionIds() {
        return _conversationState.GetRecentSessionIds();
    }

    // Renders the coordinator thread immediately and defers agent threads for lazy
    // on-demand rendering.  Previously all agent threads were rendered sequentially
    // before the coordinator, blocking the UI for ~4+ seconds on large workspaces.
    // Now only the coordinator (the view the user sees first) is rendered eagerly;
    // each agent thread's turns are stored in _pendingAgentRenders and flushed into
    // its FlowDocument the first time the user selects that thread.
    private async Task RenderAllSequentiallyAsync(
        IReadOnlyList<(TranscriptThreadState Thread, IReadOnlyList<TranscriptTurnRecord> Turns)> agentRenders,
        IReadOnlyList<TranscriptTurnRecord> coordinatorTurns,
        long dataLoadMs) {
        // Store agent thread turns for lazy on-demand rendering — do NOT render now.
        foreach (var (thread, turns) in agentRenders)
            _pendingAgentRenders[thread.ThreadId] = turns;

        if (agentRenders.Count > 0)
            SquadDashTrace.Write(TraceCategory.Performance,
                $"LAZY_DEFERRED: {agentRenders.Count} agent thread(s) queued for on-demand render");

        // Render the coordinator thread immediately — this is what the user sees first.
        // If there are more turns than InitialTurnWindow, only render the last window
        // and store the full list for virtual prepend as the user scrolls up.
        if (coordinatorTurns.Count > 0) {
            _allCoordinatorTurns = coordinatorTurns;
            if (coordinatorTurns.Count > InitialTurnWindow) {
                var startIndex = coordinatorTurns.Count - InitialTurnWindow;
                _coordinatorRenderedFromIndex = startIndex;
                SquadDashTrace.Write(TraceCategory.Performance,
                    $"VIRTUAL_INITIAL: rendering turns {startIndex}–{coordinatorTurns.Count - 1} of {coordinatorTurns.Count} total");
                await RenderConversationHistoryAsync(_coordinatorThread(), coordinatorTurns.Skip(startIndex).ToList(), dataLoadMs);
            } else {
                _coordinatorRenderedFromIndex = 0;
                await RenderConversationHistoryAsync(_coordinatorThread(), coordinatorTurns, dataLoadMs);
            }
        }
        else {
            SquadDashTrace.Write("PERF", $"LOAD DATA: turns=0 data={dataLoadMs}ms (no turns to render)");
            _maybePublishRoutingIssue("workspace-loaded");
            // Dispatch _scrollOutputToEnd even when there are no turns to render so that
            // TranscriptScrollController.EndLoad() is always called — clearing the
            // IsLoadingTranscript flag and issuing the one post-load scroll.
            _ = _dispatcher.InvokeAsync(_scrollOutputToEnd,
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>Returns <c>true</c> if the thread has turns that have not yet been
    /// rendered into its <see cref="FlowDocument"/>.</summary>
    internal bool HasPendingRender(TranscriptThreadState thread) =>
        thread.Kind == TranscriptThreadKind.Agent &&
        _pendingAgentRenders.ContainsKey(thread.ThreadId);

    /// <summary>
    /// Renders a deferred agent thread's conversation history on demand.
    /// Called the first time the user selects an agent thread whose turns were not
    /// rendered at startup.  The thread's <see cref="FlowDocument"/> must already be
    /// assigned to <c>OutputTextBox</c> before calling this so that
    /// <c>BeginChange</c>/<c>EndChange</c> suppress intermediate layout passes.
    /// </summary>
    internal async Task EnsureAgentThreadRenderedAsync(TranscriptThreadState thread) {
        if (thread.Kind != TranscriptThreadKind.Agent)
            return;
        if (!_pendingAgentRenders.TryGetValue(thread.ThreadId, out var turns))
            return;

        // Remove immediately so a second rapid selection of the same thread does not
        // queue a second render while the first is still running.
        _pendingAgentRenders.Remove(thread.ThreadId);

        SquadDashTrace.Write(TraceCategory.Performance,
            $"LAZY_RENDER_START: thread={thread.ThreadId} turns={turns.Count}");
        await RenderConversationHistoryAsync(thread, turns, 0);
        SquadDashTrace.Write(TraceCategory.Performance,
            $"LAZY_RENDER_END: thread={thread.ThreadId}");
    }

    /// <summary>
    /// Returns <c>true</c> if there are coordinator turns that have not yet been
    /// prepended to the FlowDocument (i.e. the user has not yet scrolled far enough
    /// up to trigger loading all of them).
    /// </summary>
    internal bool HasMoreTurnsAbove => _coordinatorRenderedFromIndex > 0;
    internal bool IsTurnRendered(int turnIndex) => turnIndex >= _coordinatorRenderedFromIndex;

    /// <summary>
    /// Prepends the next batch of older coordinator turns to the top of the
    /// coordinator FlowDocument and compensates the scroll position so the visible
    /// content does not jump.
    /// </summary>
    /// <returns>
    /// <c>true</c> if turns were prepended; <c>false</c> if already at the beginning
    /// or if a prepend is already in progress.
    /// </returns>
    internal async Task<bool> PrependOlderTurnsAsync() {
        if (_prependInProgress)             return false;
        if (_coordinatorRenderedFromIndex <= 0) return false;

        _prependInProgress = true;
        try {
            var endIndex   = _coordinatorRenderedFromIndex;
            var startIndex = Math.Max(0, endIndex - PrependBatchSize);
            var batch      = _allCoordinatorTurns.Skip(startIndex).Take(endIndex - startIndex).ToList();

            // Update the index BEFORE the await so that a rapid second call
            // (which clears _prependInProgress and checks the index) sees the
            // already-committed boundary and does not double-render.
            _coordinatorRenderedFromIndex = startIndex;

            await _dispatcher.InvokeAsync(() => {
                // Capture scroll state before the prepend so we can restore it
                // after the FlowDocument grows upward.
                double scrollableHeightBefore = _getScrollableHeight();
                double verticalOffsetBefore   = _getVerticalOffset();

                _beginBulkDocumentLoad();
                try {
                    _prependTurnsBatch(_coordinatorThread(), batch);
                }
                finally {
                    _endBulkDocumentLoad();
                }

                // Force the deferred layout pass to complete so ScrollableHeight
                // reflects the newly prepended blocks before we compute the delta.
                _updateScrollLayout();

                double heightAdded = _getScrollableHeight() - scrollableHeightBefore;
                if (heightAdded > 0) {
                    // Set an absolute target rather than a delta so the result is
                    // correct whether or not the extent-grow re-anchor in
                    // TranscriptScrollController already fired.
                    _scrollToAbsoluteOffset(verticalOffsetBefore + heightAdded);
                }
            }, System.Windows.Threading.DispatcherPriority.Normal);

            SquadDashTrace.Write(TraceCategory.Performance,
                $"VIRTUAL_PREPEND: prepended turns {startIndex}–{endIndex - 1}, remaining_above={startIndex}");
            return true;
        }
        finally {
            _prependInProgress = false;
        }
    }

    internal PromptContextDiagnostics CapturePromptContextDiagnostics() {
        var coordinatorTurns = _conversationState.Turns;
        var threads = _conversationState.GetThreads();
        var agentTurns = threads.SelectMany(thread => thread.Turns).ToArray();
        var allTurnStartTimes = coordinatorTurns
            .Select(turn => turn.StartedAt)
            .Concat(agentTurns.Select(turn => turn.StartedAt))
            .OrderBy(timestamp => timestamp)
            .ToArray();
        DateTimeOffset? transcriptStartedAt = allTurnStartTimes.Length > 0
            ? allTurnStartTimes[0]
            : null;

        return new PromptContextDiagnostics(
            _currentSessionId,
            _conversationState.SessionUpdatedAt,
            transcriptStartedAt,
            coordinatorTurns.Count,
            threads.Count,
            agentTurns.Length,
            _promptHistory.Count,
            _conversationState.GetRecentSessionIds().Count,
            coordinatorTurns.Sum(turn => CountChars(turn.Prompt)),
            coordinatorTurns.Sum(turn => CountChars(turn.ResponseText)),
            coordinatorTurns.Sum(turn => CountChars(turn.ThinkingText)),
            agentTurns.Sum(turn => CountChars(turn.Prompt)),
            agentTurns.Sum(turn => CountChars(turn.ResponseText)),
            agentTurns.Sum(turn => CountChars(turn.ThinkingText)));
    }

    internal string BuildQuickReplyHandoffContext(
        TranscriptResponseEntry sourceEntry,
        string selectedOption,
        string? targetAgentLabel,
        string? routeMode,
        string? targetAgentHandle) {
        var sourceRecord = BuildTranscriptTurnRecord(sourceEntry.Turn, DateTimeOffset.UtcNow);
        var sourceThreadTitle = ResolveQuickReplySourceThreadTitle(sourceEntry.Turn.OwnerThread);

        var priorTurns = _conversationState.Turns
            .Where(turn => !IsSameTranscriptTurn(turn, sourceRecord))
            .Where(turn => turn.StartedAt <= sourceRecord.StartedAt)
            .TakeLast(Math.Max(0, QuickReplyHandoffCoordinatorTurnCount - 1))
            .Select(turn => BuildQuickReplyTurnContext("Coordinator", turn, isSourceTurn: false))
            .ToList();

        priorTurns.Add(BuildQuickReplyTurnContext(sourceThreadTitle, sourceRecord, isSourceTurn: true));
        var contextWindowStart = priorTurns
            .Select(turn => turn.StartedAt)
            .DefaultIfEmpty(sourceRecord.StartedAt)
            .Min();

        return QuickReplyContextPromptBuilder.BuildHandoffContext(
            selectedOption,
            targetAgentLabel,
            routeMode,
            targetAgentHandle,
            priorTurns,
            BuildQuickReplyAgentContexts(contextWindowStart));
    }

    internal string? StartFreshSessionPreservingTranscript() {
        var previousSessionId = _currentSessionId;
        SetCurrentSessionId(null);
        PersistSessionPointer();
        return previousSessionId;
    }

    internal SessionSelectionResult TryResumeSession(string sessionIdPrefix) {
        if (string.IsNullOrWhiteSpace(sessionIdPrefix))
            return SessionSelectionResult.Failure("Specify a session id or prefix to resume.");

        var normalizedPrefix = sessionIdPrefix.Trim();
        var matches = GetKnownSessionIds()
            .Where(candidate => candidate.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (matches.Length == 0)
            return SessionSelectionResult.Failure($"No remembered session matches `{normalizedPrefix}`.");

        if (matches.Length > 1) {
            return SessionSelectionResult.Failure(
                $"Session prefix `{normalizedPrefix}` is ambiguous: {string.Join(", ", matches.Select(AbbreviateSessionId))}");
        }

        SetCurrentSessionId(matches[0]);
        PersistSessionPointer();
        return SessionSelectionResult.Success(matches[0]);
    }

    internal async Task RenderConversationHistoryAsync(
        TranscriptThreadState thread,
        IReadOnlyList<TranscriptTurnRecord> turns,
        long dataLoadMs = 0) {
        // T1 → T2: insert all turns into the FlowDocument.
        //
        // Previous implementation: one await _dispatcher.InvokeAsync(..., Background) per turn.
        // Background priority (level 4) yields to Input (5), Loaded (6), Render (7), DataBind (8),
        // Normal (9) — so every turn render waited for a full layout/render pass, meaning
        // ~one-frame delay per turn × N turns (e.g. 142 turns × ~50 ms = ~7 s).
        //
        // New implementation: batch all turns into a SINGLE InvokeAsync at Normal priority,
        // wrapped in FlowDocument.BeginChange()/EndChange(). BeginChange defers all TextChanged
        // notifications so WPF does not attempt to layout the document after every Blocks.Add
        // call — instead it fires exactly one layout pass when EndChange() is called.
        // This collapses N=142 layout invalidations into one and eliminates 141 dispatcher
        // round-trips, attacking both the data-insertion wall and the post-EndLoad scroll
        // instability (layout is stable before the scroll fires).
        var uiSw = Stopwatch.StartNew();
        var beginBulkMs   = 0L;
        var turnLoopMs    = 0L;
        var endBulkMs     = 0L;
        var dispatchWaitMs = 0L;
        // queuedAt is read inside the lambda to isolate dispatcher queue-wait time
        // from actual render time — useful for diagnosing future stacking regressions.
        var queuedAt = Stopwatch.StartNew();
        await _dispatcher.InvokeAsync(() => {
            dispatchWaitMs = queuedAt.ElapsedMilliseconds;
            SquadDashTrace.Write(TraceCategory.Performance, $"DISPATCH_WAIT: {dispatchWaitMs}ms turns={turns.Count}");
            var phaseSw = Stopwatch.StartNew();
            _beginBulkDocumentLoad();
            beginBulkMs = phaseSw.ElapsedMilliseconds;
            phaseSw.Restart();
            try {
                for (var i = 0; i < turns.Count; i++) {
                    if (_isClosing()) return;
                    _renderPersistedTurn(thread, turns[i], i == turns.Count - 1);
                }
            } finally {
                turnLoopMs = phaseSw.ElapsedMilliseconds;
                phaseSw.Restart();
                _endBulkDocumentLoad();
                endBulkMs = phaseSw.ElapsedMilliseconds;
            }
        }, System.Windows.Threading.DispatcherPriority.Normal);
        uiSw.Stop();

        var uiMs    = uiSw.ElapsedMilliseconds;
        var totalMs = dataLoadMs + uiMs;
        SquadDashTrace.Write("PERF", $"LOAD DATA:  turns={turns.Count} data={dataLoadMs}ms");
        SquadDashTrace.Write("PERF", $"LOAD UI:    turns={turns.Count} ui={uiMs}ms");
        SquadDashTrace.Write("PERF", $"LOAD COMPLETE: data={dataLoadMs}ms ui={uiMs}ms total={totalMs}ms turns={turns.Count}");
        SquadDashTrace.Write(TraceCategory.Performance, $"DISPATCH_WAIT: {dispatchWaitMs}ms turns={turns.Count}");
        SquadDashTrace.Write(TraceCategory.Performance, $"BULK_BEGIN: {beginBulkMs}ms");
        SquadDashTrace.Write(TraceCategory.Performance, $"TURN_RENDER: turns={turns.Count} {turnLoopMs}ms");
        SquadDashTrace.Write(TraceCategory.Performance, $"LAYOUT_PASS: {endBulkMs}ms");

        thread.CurrentTurn = null;
        // Dispatch the post-load scroll fire-and-forget at ContextIdle so WPF's layout
        // and render pipeline (queued by EndChange) can drain at higher priorities first.
        // We do NOT await this — the FlowDocument content is already rendered; the scroll
        // to bottom is cosmetic and must not block the startup/load completion path.
        if (ReferenceEquals(_selectedThread() ?? _coordinatorThread(), thread))
        {
            var scrollStart = Stopwatch.GetTimestamp();
            _ = _dispatcher.InvokeAsync(() =>
            {
                var scrollMs = (long)((Stopwatch.GetTimestamp() - scrollStart) * 1000.0 / Stopwatch.Frequency);
                SquadDashTrace.Write(TraceCategory.Performance, $"SCROLL_SETTLE: {scrollMs}ms (queue wait before scroll)");
                _scrollOutputToEnd();
            }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        if (ReferenceEquals(thread, _coordinatorThread()))
            _maybePublishRoutingIssue("history-restored");
    }

    internal void RenderConversationHistory(IReadOnlyList<TranscriptTurnRecord> turns) {
        _beginBulkDocumentLoad();
        try {
            for (var i = 0; i < turns.Count; i++)
                _renderPersistedTurn(_coordinatorThread(), turns[i], i == turns.Count - 1);
        } finally {
            _endBulkDocumentLoad();
        }
        _setCurrentTurnNull();
        _scrollOutputToEnd();
    }

    // ── Save operations ─────────────────────────────────────────────────────────

    internal void SaveCurrentTurnToConversation(DateTimeOffset completedAt) {
        if (_getWorkspace() is null || _getCurrentTurn() is null)
            return;

        SaveTranscriptTurnToConversation(_getCurrentTurn()!, completedAt);
    }

    internal void SaveTranscriptTurnToConversation(TranscriptTurnView turn, DateTimeOffset completedAt) {
        if (_getWorkspace() is null)
            return;

        var turnRecord = BuildTranscriptTurnRecord(turn, completedAt);
        var turns = _conversationState.Turns
            .Where(existing =>
                existing.StartedAt != turnRecord.StartedAt ||
                !string.Equals(existing.Prompt, turnRecord.Prompt, StringComparison.Ordinal))
            .ToList();

        turns.Add(turnRecord);

        PersistConversationState(_conversationState with {
            SessionId = _currentSessionId,
            SessionUpdatedAt = _currentSessionId is null
                ? _conversationState.SessionUpdatedAt
                : DateTimeOffset.UtcNow,
            PromptDraft = _getPromptText(),
            PromptHistory = _promptHistory.ToArray(),
            Turns = turns,
            Threads = BuildPersistedAgentThreadRecords(includeCurrentTurns: false)
        });
    }

    internal void SaveAgentThreadToConversation(TranscriptThreadState thread, DateTimeOffset completedAt) {
        if (_getWorkspace() is null || thread.CurrentTurn is null)
            return;

        var turnRecord = BuildTranscriptTurnRecord(thread.CurrentTurn, completedAt);
        thread.SavedTurns.RemoveAll(existing =>
            existing.StartedAt == turnRecord.StartedAt &&
            string.Equals(existing.Prompt, turnRecord.Prompt, StringComparison.Ordinal));
        thread.SavedTurns.Add(turnRecord);

        PersistConversationState(_conversationState with {
            SessionId = _currentSessionId,
            SessionUpdatedAt = _currentSessionId is null
                ? _conversationState.SessionUpdatedAt
                : DateTimeOffset.UtcNow,
            PromptDraft = _getPromptText(),
            PromptHistory = _promptHistory.ToArray(),
            Threads = BuildPersistedAgentThreadRecords(includeCurrentTurns: false)
        });
    }

    internal void PersistAgentThreadSnapshot(TranscriptThreadState thread) {
        if (_getWorkspace() is null || !AgentThreadRegistry.HasPersistableThreadContent(thread))
            return;

        CancelScheduledAgentThreadSnapshotPersist();
        PersistConversationStateInBackground(_conversationState with {
            SessionId = _currentSessionId,
            SessionUpdatedAt = _currentSessionId is null
                ? _conversationState.SessionUpdatedAt
                : DateTimeOffset.UtcNow,
            PromptDraft = _getPromptText(),
            PromptHistory = _promptHistory.ToArray(),
            Threads = BuildPersistedAgentThreadRecords(includeCurrentTurns: true)
        });
    }

    internal void SchedulePersistAgentThreadSnapshot(TranscriptThreadState thread) {
        if (_getWorkspace() is null || !AgentThreadRegistry.HasPersistableThreadContent(thread))
            return;

        _hasPendingAgentThreadSnapshotPersist = true;
        _agentThreadSnapshotPersistTimer.Stop();
        _agentThreadSnapshotPersistTimer.Start();
    }

    internal void PersistConversationState(WorkspaceConversationState state) {
        // Always use the background queue — never block the UI thread with synchronous file I/O.
        // EmergencySave() calls SaveConversationStateSerially() directly for shutdown safety.
        PersistConversationStateInBackground(state);
    }

    /// <summary>
    /// Attaches an agent report stub to the last saved coordinator turn so it is
    /// re-rendered as a button when the conversation is loaded on next startup.
    /// If there are no saved turns yet the call is a no-op (the button was already
    /// appended live and will be gone on restart, but this is an uncommon edge case).
    /// </summary>
    internal void AppendAgentReportToLastTurn(string agentLabel, string reportPath) {
        if (_getWorkspace() is null)
            return;

        var turns = _conversationState.Turns;
        if (turns.Count == 0)
            return;

        var lastTurn  = turns[^1];
        var existing  = lastTurn.AgentReports ?? [];
        var newReport = new AgentReportInfo(agentLabel, reportPath);
        var updated   = existing.Concat([newReport]).ToArray();

        var newTurns = turns.ToList();
        newTurns[^1] = lastTurn with { AgentReports = updated };

        PersistConversationState(_conversationState with {
            Turns = newTurns
        });
        _conversationState = _conversationState with { Turns = newTurns };
    }

    internal void SaveWorkspaceInputState() {
        if (_getWorkspace() is null)
            return;

        PersistConversationState(_conversationState with {
            SessionId = _currentSessionId,
            PromptDraft = _getPromptText(),
            PromptHistory = _promptHistory.ToArray(),
            Threads = BuildPersistedAgentThreadRecords(includeCurrentTurns: false)
        });
    }

    internal void CaptureWorkspaceInputState() {
        var workspace = _getWorkspace();
        if (workspace is null)
            return;

        try {
            var (caretIndex, selectionStart, selectionLength) = _getPromptCaretState();
            _pendingConversationSave = (
                workspace.FolderPath,
                _conversationState with {
                    SessionId          = _currentSessionId,
                    PromptDraft        = _getPromptText(),
                    PromptDraftCaretIndex    = caretIndex,
                    PromptDraftSelectionStart  = selectionStart,
                    PromptDraftSelectionLength = selectionLength,
                    PromptHistory      = _promptHistory.ToArray(),
                    Threads            = BuildPersistedAgentThreadRecords(includeCurrentTurns: false)
                });
        }
        catch {
        }
    }

    internal void EmergencySave() {
        var workspace = _getWorkspace();
        if (workspace is null)
            return;

        try {
            // Signal any in-flight background save to abort after it acquires the named mutex.
            // This prevents a 5-7 second UI-thread freeze when EmergencySave races with an
            // ongoing background save that is holding _conversationStoreSaveGate.
            var cts = _backgroundSaveCts;
            if (cts is not null) {
                _backgroundSaveCts = null;
                cts.Cancel();
                cts.Dispose();
            }

            var turns = _conversationState.Turns.ToList();

            if (_getCurrentTurn() is not null) {
                // Snapshot the in-flight turn with the text accumulated so far.
                var partialRecord = BuildTranscriptTurnRecord(_getCurrentTurn()!, DateTimeOffset.UtcNow);
                turns.RemoveAll(existing =>
                    existing.StartedAt == partialRecord.StartedAt &&
                    string.Equals(existing.Prompt, partialRecord.Prompt, StringComparison.Ordinal));
                turns.Add(partialRecord);
            }

            var (caretIndex, selectionStart, selectionLength) = _getPromptCaretState();
            var state = _conversationState with {
                SessionId          = _currentSessionId,
                PromptDraft        = _getPromptText(),
                PromptDraftCaretIndex    = caretIndex,
                PromptDraftSelectionStart  = selectionStart,
                PromptDraftSelectionLength = selectionLength,
                PromptHistory      = _promptHistory.ToArray(),
                Turns              = turns,
                Threads            = BuildPersistedAgentThreadRecords(includeCurrentTurns: true)
            };

            var version = RegisterConversationSaveRequest();
            _conversationState = state;
            var emergencySaveSw = Stopwatch.StartNew();
            var savedState = SaveConversationStateSerially(workspace.FolderPath, state, version, skipIfStale: false);
            emergencySaveSw.Stop();
            ApplySavedConversationStateIfCurrent(version, savedState);
            SquadDashTrace.Write("Shutdown", $"EmergencySave: saved {turns.Count} turns, promptDraft={state.PromptDraft?.Length ?? 0} chars, saveMs={emergencySaveSw.ElapsedMilliseconds}ms.");
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Shutdown", $"EmergencySave failed: {ex.Message}");
        }
    }

    private static int CountChars(string? text) => text?.Length ?? 0;

    private IReadOnlyList<QuickReplyHandoffAgentContext> BuildQuickReplyAgentContexts(DateTimeOffset windowStart) =>
        _agentThreadRegistry.ThreadOrder
            .Where(thread => thread.Kind == TranscriptThreadKind.Agent && !thread.IsPlaceholderThread)
            .Select(BuildQuickReplyAgentContext)
            .Where(context =>
                (context.LastActivityAt ?? DateTimeOffset.MinValue) >= windowStart &&
                (!string.IsNullOrWhiteSpace(context.UserPrompt) ||
                 !string.IsNullOrWhiteSpace(context.AssistantResponse) ||
                 context.RecentActivity.Count > 0))
            .OrderByDescending(context => context.LastActivityAt ?? DateTimeOffset.MinValue)
            .Take(QuickReplyHandoffAgentContextCount)
            .ToArray();

    private QuickReplyHandoffAgentContext BuildQuickReplyAgentContext(TranscriptThreadState thread) {
        var latestTurn = GetLatestTurnRecord(thread);
        var prompt = latestTurn?.Prompt ?? thread.Prompt;
        var response = latestTurn?.ResponseText ?? thread.LatestResponse ?? thread.LastCoordinatorAnnouncedResponse;
        var label = AgentThreadRegistry.ResolveThreadDisplayName(
            thread.AgentDisplayName,
            thread.AgentName,
            thread.AgentId);

        return new QuickReplyHandoffAgentContext(
            label,
            prompt,
            response,
            thread.RecentActivity,
            AgentThreadRegistry.GetThreadLastActivityAt(thread));
    }

    private TranscriptTurnRecord? GetLatestTurnRecord(TranscriptThreadState thread) {
        if (thread.CurrentTurn is not null)
            return BuildTranscriptTurnRecord(thread.CurrentTurn, DateTimeOffset.UtcNow);

        return thread.SavedTurns
            .OrderByDescending(turn => turn.Timestamp)
            .FirstOrDefault();
    }

    private static QuickReplyHandoffTurnContext BuildQuickReplyTurnContext(
        string threadTitle,
        TranscriptTurnRecord turn,
        bool isSourceTurn) =>
        new(
            threadTitle,
            turn.Prompt,
            turn.ResponseText,
            turn.StartedAt,
            isSourceTurn);

    private static string ResolveQuickReplySourceThreadTitle(TranscriptThreadState thread) =>
        thread.Kind == TranscriptThreadKind.Coordinator
            ? "Coordinator"
            : AgentThreadRegistry.ResolveThreadDisplayName(
                thread.AgentDisplayName,
                thread.AgentName,
                thread.AgentId);

    private static bool IsSameTranscriptTurn(TranscriptTurnRecord left, TranscriptTurnRecord right) =>
        left.StartedAt.ToUniversalTime() == right.StartedAt.ToUniversalTime() &&
        string.Equals(left.Prompt, right.Prompt, StringComparison.Ordinal);

    // ── Record builders ─────────────────────────────────────────────────────────

    internal TranscriptTurnRecord BuildTranscriptTurnRecord(
        TranscriptTurnView turn,
        DateTimeOffset completedAt) {
        var rawResponseText = MainWindow.GetSanitizedTurnResponseText(turn);

        var toolEntries = _getToolEntries();
        var tools = toolEntries.Values
            .Where(entry => ReferenceEquals(entry.Turn, turn))
            .OrderBy(entry => entry.StartedAt)
            .Select(BuildTranscriptToolRecord)
            .ToArray();
        var thoughts = turn.ThoughtEntries
            .Select(BuildTranscriptThoughtRecord)
            .ToArray();
        var responseSegments = turn.ResponseEntries
            .Select(BuildTranscriptResponseSegmentRecord)
            .ToArray();

        return new TranscriptTurnRecord(
            turn.StartedAt.ToUniversalTime(),
            completedAt.ToUniversalTime(),
            turn.Prompt,
            string.Join(
                Environment.NewLine + Environment.NewLine,
                thoughts.Select(thought => thought.Text).Where(text => !string.IsNullOrWhiteSpace(text))),
            rawResponseText,
            turn.ThinkingBlocks.Count == 0 || turn.ThinkingBlocks.All(block => !block.Expander.IsExpanded),
            tools,
            thoughts,
            responseSegments);
    }

    private static TranscriptThoughtRecord BuildTranscriptThoughtRecord(TranscriptThoughtEntry entry) {
        var placement = entry.Turn.ThinkingBlocks.Any(block => block.Sequence < entry.Sequence)
            ? TranscriptThoughtPlacement.AfterTools
            : TranscriptThoughtPlacement.BeforeTools;
        return new TranscriptThoughtRecord(
            entry.Speaker,
            MainWindow.FormatThinkingText(entry.RawTextBuilder.ToString()),
            placement) {
            Sequence = entry.Sequence
        };
    }

    private static TranscriptToolRecord BuildTranscriptToolRecord(ToolTranscriptEntry entry) {
        return new TranscriptToolRecord(
            entry.ToolCallId,
            entry.Descriptor,
            entry.ArgsJson,
            entry.StartedAt.ToUniversalTime(),
            entry.FinishedAt?.ToUniversalTime(),
            entry.ProgressText,
            entry.OutputText,
            entry.DetailContent ?? ToolTranscriptFormatter.BuildDetailContent(new ToolTranscriptDetail(
                entry.Descriptor,
                entry.ArgsJson,
                entry.OutputText,
                entry.StartedAt,
                entry.FinishedAt,
                entry.ProgressText,
                entry.IsCompleted,
                entry.Success)),
            entry.IsCompleted,
            entry.Success) {
            ThinkingBlockSequence = entry.ThinkingBlock.Sequence
        };
    }

    private static TranscriptResponseSegmentRecord BuildTranscriptResponseSegmentRecord(TranscriptResponseEntry entry) {
        return new TranscriptResponseSegmentRecord(MainWindow.SanitizeResponseText(entry.RawTextBuilder.ToString())) {
            Sequence = entry.Sequence
        };
    }

    internal TranscriptThreadRecord BuildTranscriptThreadRecord(TranscriptThreadState thread, bool includeCurrentTurn) {
        var turns = thread.SavedTurns.ToList();

        if (includeCurrentTurn && thread.CurrentTurn is not null) {
            var partialTurn = BuildTranscriptTurnRecord(thread.CurrentTurn, DateTimeOffset.UtcNow);
            turns.RemoveAll(existing =>
                existing.StartedAt == partialTurn.StartedAt &&
                string.Equals(existing.Prompt, partialTurn.Prompt, StringComparison.Ordinal));
            turns.Add(partialTurn);
        }

        return new TranscriptThreadRecord(
            thread.ThreadId,
            thread.Title,
            thread.AgentId,
            thread.ToolCallId,
            thread.AgentName,
            thread.AgentDisplayName,
            thread.AgentDescription,
            thread.AgentType,
            thread.AgentCardKey,
            thread.Prompt,
            MainWindow.SanitizeResponseTextOrNull(thread.LatestResponse),
            MainWindow.SanitizeResponseTextOrNull(thread.LastCoordinatorAnnouncedResponse),
            thread.LatestIntent,
            thread.RecentActivity,
            thread.ErrorText,
            thread.StatusText,
            thread.DetailText,
            thread.StartedAt,
            thread.CompletedAt,
            turns,
            thread.OriginAgentDisplayName,
            thread.OriginParentToolCallId);
    }

    internal IReadOnlyList<TranscriptThreadRecord> BuildPersistedAgentThreadRecords(bool includeCurrentTurns) {
        return _agentThreadRegistry.ThreadOrder
            .Where(thread => !thread.IsPlaceholderThread)
            .Select(thread => BuildTranscriptThreadRecord(thread, includeCurrentTurns))
            .Where(record => record.Turns.Count > 0 ||
                             !string.IsNullOrWhiteSpace(record.Prompt) ||
                             !string.IsNullOrWhiteSpace(record.DetailText) ||
                             !string.IsNullOrWhiteSpace(record.LatestResponse))
            .ToArray();
    }

    // ── Prompt history ──────────────────────────────────────────────────────────

    internal void AddPromptToHistory(string prompt) {
        if (_promptHistory.Count == 0 || _promptHistory[^1] != prompt)
            _promptHistory.Add(prompt);
        _conversationState = _conversationState with {
            PromptHistory = _promptHistory.ToArray()
        };
    }

    internal void UpdatePromptDraftState() {
        _conversationState = _conversationState with {
            PromptDraft = _getPromptText()
        };
    }

    internal void UpdateQueuedPromptsState(
        IReadOnlyList<PromptQueueItem> items,
        Dictionary<string, List<FollowUpAttachment>>? attachments = null,
        bool queueRightmostHeld = false,
        bool loopQueuedToDequeue = false) {
        IReadOnlyList<QueuedPromptEntry>? entries = null;
        if (items.Count > 0)
        {
            entries = items.Select(i => {
                List<FollowUpAttachmentDto>? dtos = null;
                if (attachments is not null &&
                    attachments.TryGetValue(i.Id, out var list) && list.Count > 0)
                {
                    dtos = list
                        .Select(a => new FollowUpAttachmentDto(a.CommitSha, a.Description, a.OriginalPrompt, a.TranscriptQuote))
                        .ToList();
                }
                return new QueuedPromptEntry(i.Text, i.IsDictated, dtos);
            }).ToArray();
        }
        _conversationState = _conversationState with {
            QueuedPromptEntries = entries,
            QueueRightmostHeld  = queueRightmostHeld ? true : null,
            LoopQueuedToDequeue = loopQueuedToDequeue ? true : null,
        };
    }

    internal void UpdateLoopSettingsState(LoopMode mode, bool continuousContext) {
        _conversationState = _conversationState with {
            LoopMode              = mode,
            LoopContinuousContext = continuousContext,
        };
    }

    internal void NavigateHistory(int direction) {
        var result = PromptHistoryNavigator.Navigate(
            _promptHistory,
            _historyIndex,
            _historyDraft,
            _getPromptText(),
            direction);

        if (!result.Changed)
            return;

        _historyDraft = result.HistoryDraft;
        _historyIndex = result.HistoryIndex;
        ApplyPromptText(result.Text);
    }

    internal void ResetHistoryNavigation() {
        _historyIndex = null;
        _historyDraft = null;
    }

    /// <summary>
    /// Resets the virtual-window state so that stale coordinator turns and
    /// pending agent renders from a previous session can never reappear after
    /// a /clear.  Must be called from ClearSessionView() alongside the
    /// FlowDocument and ConversationState resets.
    /// </summary>
    internal void ResetVirtualWindow()
    {
        _allCoordinatorTurns          = [];
        _coordinatorRenderedFromIndex = 0;
        _pendingAgentRenders.Clear();
    }

    internal void ApplyPromptText(string text, int? caretIndex = null, int selectionStart = 0, int selectionLength = 0) {
        _isApplyingHistoryEntry = true;

        try {
            _setPromptText(text, caretIndex ?? text.Length, selectionStart, selectionLength);
        }
        finally {
            _isApplyingHistoryEntry = false;
        }
    }

    private void SetCurrentSessionId(string? sessionId) {
        _currentSessionId = NormalizeSessionId(sessionId);
        _conversationState = _conversationState with {
            SessionId = _currentSessionId,
            RecentSessionIds = BuildRecentSessionIds(_conversationState.GetRecentSessionIds(), _currentSessionId)
        };
    }

    private void PersistSessionPointer() {
        PersistConversationState(_conversationState with {
            SessionId = _currentSessionId,
            SessionUpdatedAt = DateTimeOffset.UtcNow,
            PromptDraft = _getPromptText(),
            PromptHistory = _promptHistory.ToArray(),
            Threads = BuildPersistedAgentThreadRecords(includeCurrentTurns: false)
        });
    }

    private void CancelScheduledAgentThreadSnapshotPersist() {
        _hasPendingAgentThreadSnapshotPersist = false;
        _agentThreadSnapshotPersistTimer.Stop();
    }

    private void OnAgentThreadSnapshotPersistTimerTick(object? sender, EventArgs e) {
        _agentThreadSnapshotPersistTimer.Stop();
        if (!_hasPendingAgentThreadSnapshotPersist)
            return;

        _hasPendingAgentThreadSnapshotPersist = false;
        PersistConversationStateInBackground(_conversationState with {
            SessionId = _currentSessionId,
            SessionUpdatedAt = _currentSessionId is null
                ? _conversationState.SessionUpdatedAt
                : DateTimeOffset.UtcNow,
            PromptDraft = _getPromptText(),
            PromptHistory = _promptHistory.ToArray(),
            Threads = BuildPersistedAgentThreadRecords(includeCurrentTurns: true)
        });
    }

    private void PersistConversationStateInBackground(WorkspaceConversationState state) {
        var workspace = _getWorkspace();
        if (workspace is null) {
            _conversationState = state;
            return;
        }

        var version = RegisterConversationSaveRequest();
        _conversationState = state;
        QueueConversationSave(workspace.FolderPath, state, version);
    }

    private void QueueConversationSave(string folderPath, WorkspaceConversationState state, long version) {
        var shouldStartWorker = false;
        var replacedPendingSave = false;
        lock (_backgroundSaveGate) {
            replacedPendingSave = _queuedConversationSave is not null;
            _queuedConversationSave = (folderPath, state, version);
            if (!_backgroundSaveLoopRunning) {
                _backgroundSaveLoopRunning = true;
                _backgroundSaveCts = new CancellationTokenSource();
                shouldStartWorker = true;
            }
        }

        SquadDashTrace.Write(
            "Persistence",
            $"QueueConversationSave: queued background save version={version} workerStart={shouldStartWorker} coalesced={replacedPendingSave} turns={state.Turns.Count} threads={state.GetThreads().Count}");

        if (shouldStartWorker)
            _ = Task.Run(ProcessQueuedConversationSavesAsync);
    }

    private Task ProcessQueuedConversationSavesAsync() {
        // Snapshot the token once for the lifetime of this worker loop.
        // EmergencySave can cancel it to interrupt any in-flight background save.
        var ct = _backgroundSaveCts?.Token ?? CancellationToken.None;
        try {
            while (true) {
                (string FolderPath, WorkspaceConversationState State, long Version)? nextSave;
                lock (_backgroundSaveGate) {
                    nextSave = _queuedConversationSave;
                    _queuedConversationSave = null;
                    if (nextSave is null) {
                        _backgroundSaveLoopRunning = false;
                        SquadDashTrace.Write("Persistence", "ProcessQueuedConversationSavesAsync: queue drained");
                        return Task.CompletedTask;
                    }
                }

                try {
                    var saveSw = Stopwatch.StartNew();
                    if (HasNewerConversationSaveRequest(nextSave.Value.Version)) {
                        SquadDashTrace.Write(
                            "Persistence",
                            $"ProcessQueuedConversationSavesAsync: skipped stale background save version={nextSave.Value.Version}");
                        continue;
                    }

                    SquadDashTrace.Write(
                        "Persistence",
                        $"ProcessQueuedConversationSavesAsync: starting background save version={nextSave.Value.Version} turns={nextSave.Value.State.Turns.Count} threads={nextSave.Value.State.GetThreads().Count}");
                    var savedState = SaveConversationStateSerially(
                        nextSave.Value.FolderPath,
                        nextSave.Value.State,
                        nextSave.Value.Version,
                        skipIfStale: true,
                        ct: ct);
                    saveSw.Stop();
                    ApplySavedConversationStateIfCurrent(nextSave.Value.Version, savedState);
                    var moreQueued = false;
                    lock (_backgroundSaveGate)
                        moreQueued = _queuedConversationSave is not null;
                    SquadDashTrace.Write(
                        "Persistence",
                        $"ProcessQueuedConversationSavesAsync: finished background save version={nextSave.Value.Version} elapsedMs={saveSw.ElapsedMilliseconds} newerSaveQueued={moreQueued}");
                }
                catch (OperationCanceledException) {
                    // EmergencySave canceled us — exit immediately so the UI-thread save can proceed.
                    SquadDashTrace.Write("Persistence", "ProcessQueuedConversationSavesAsync: canceled by EmergencySave, yielding to shutdown save.");
                    break;
                }
                catch (Exception ex) {
                    SquadDashTrace.Write("Persistence", $"Background conversation save failed: {ex.Message}");
                }
            }
        }
        finally {
            lock (_backgroundSaveGate) _backgroundSaveLoopRunning = false;
        }
        return Task.CompletedTask;
    }

    private long RegisterConversationSaveRequest() {
        lock (_backgroundSaveGate) {
            var version = ++_nextConversationSaveVersion;
            _latestRequestedConversationSaveVersion = version;
            return version;
        }
    }

    private bool HasNewerConversationSaveRequest(long version) {
        lock (_backgroundSaveGate)
            return version < _latestRequestedConversationSaveVersion;
    }

    private WorkspaceConversationState SaveConversationStateSerially(
        string folderPath,
        WorkspaceConversationState state,
        long version,
        bool skipIfStale,
        CancellationToken ct = default) {
        lock (_conversationStoreSaveGate) {
            if (skipIfStale && HasNewerConversationSaveRequest(version)) {
                SquadDashTrace.Write(
                    "Persistence",
                    $"SaveConversationStateSerially: skipped stale save version={version}");
                return state;
            }

            return _conversationStore.Save(folderPath, state, ct);
        }
    }

    private void ApplySavedConversationStateIfCurrent(long version, WorkspaceConversationState state) {
        if (HasNewerConversationSaveRequest(version))
            return;

        _conversationState = state;
    }

    private static string? NormalizeSessionId(string? sessionId) {
        return string.IsNullOrWhiteSpace(sessionId)
            ? null
            : sessionId.Trim();
    }

    private static IReadOnlyList<string> BuildRecentSessionIds(
        IReadOnlyList<string> existingIds,
        string? prioritizedSessionId) {
        var ordered = new List<string>(MaxRememberedSessionIds);

        void Add(string? candidate) {
            var normalized = NormalizeSessionId(candidate);
            if (normalized is null)
                return;

            if (ordered.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                return;

            ordered.Add(normalized);
        }

        Add(prioritizedSessionId);
        foreach (var existingId in existingIds)
            Add(existingId);

        return ordered.Take(MaxRememberedSessionIds).ToArray();
    }

    internal static string AbbreviateSessionId(string sessionId) {
        var normalized = NormalizeSessionId(sessionId) ?? string.Empty;
        return normalized.Length <= 12 ? normalized : normalized[..12] + "...";
    }

    // ── Transcript search ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns plain text for a coordinator turn that can be used for substring
    /// search.  The result is the concatenation of the user prompt and the
    /// assistant response, separated by a double newline.  Searching both fields
    /// separately (see <see cref="SearchTurnsAsync"/>) gives finer-grained
    /// <see cref="TurnSearchMatch"/> records; this helper is provided for callers
    /// that need a single string representation (e.g. clipboard copy, plain-text
    /// export).
    /// </summary>
    /// <param name="turnIndex">0-based index into <c>_allCoordinatorTurns</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   Thrown if <paramref name="turnIndex"/> is outside the valid range.
    /// </exception>
    /// <summary>
    /// Returns the <see cref="DateTimeOffset"/> of the coordinator turn at
    /// <paramref name="turnIndex"/>, or <c>null</c> if the index is out of range.
    /// Used by search navigation in MainWindow to locate the correct
    /// <see cref="PromptEntry"/> after <see cref="EnsureTurnRenderedAsync"/> prepends
    /// batches in non-chronological render order.
    /// </summary>
    internal DateTimeOffset? GetCoordinatorTurnStartedAt(int turnIndex) {
        if (turnIndex < 0 || turnIndex >= _allCoordinatorTurns.Count)
            return null;
        return _allCoordinatorTurns[turnIndex].StartedAt;
    }

    /// <summary>
    /// Returns the 0-based index of the coordinator turn whose <c>StartedAt</c>
    /// matches <paramref name="timestamp"/>, or -1 if no such turn exists.
    /// </summary>
    internal int FindCoordinatorTurnIndexByTimestamp(DateTimeOffset timestamp) {
        var turns = _allCoordinatorTurns;
        for (var i = 0; i < turns.Count; i++) {
            if (turns[i].StartedAt == timestamp)
                return i;
        }
        return -1;
    }

    internal string GetTurnText(int turnIndex) {
        if (turnIndex < 0 || turnIndex >= _allCoordinatorTurns.Count)
            throw new ArgumentOutOfRangeException(nameof(turnIndex),
                $"Turn index {turnIndex} is out of range [0, {_allCoordinatorTurns.Count}).");

        var turn = _allCoordinatorTurns[turnIndex];
        return string.Concat(turn.Prompt ?? string.Empty, "\n\n", turn.ResponseText ?? string.Empty);
    }

    /// <summary>
    /// Searches every coordinator turn (including turns that have not yet been
    /// rendered into the FlowDocument) for occurrences of <paramref name="query"/>
    /// and returns one <see cref="TurnSearchMatch"/> per occurrence.
    ///
    /// <para>
    /// Matching is case-insensitive.  The search is run on a thread-pool thread
    /// via <c>Task.Run</c> to keep the UI thread responsive.  The
    /// <c>_allCoordinatorTurns</c> list reference is captured <em>before</em> the
    /// background work starts; because the reference is replaced atomically (never
    /// mutated in-place) and reads/writes to a reference are atomic on all .NET
    /// platforms, this is safe without a lock.
    /// </para>
    ///
    /// <para>
    /// Each turn is searched in two passes — first the user <c>Prompt</c>, then the
    /// assistant <c>ResponseText</c> — so the <c>TurnRole</c> field unambiguously
    /// tells the UI which part of the turn matched.  Multiple matches within the
    /// same field each produce a separate <see cref="TurnSearchMatch"/>.
    /// </para>
    /// </summary>
    internal async Task<IReadOnlyList<TurnSearchMatch>> SearchTurnsAsync(
        string query,
        CancellationToken ct = default) {

        if (string.IsNullOrEmpty(query))
            return [];

        // Snapshot the reference before going off-thread.
        // _allCoordinatorTurns is replaced atomically; we read a stable snapshot.
        var turns = _allCoordinatorTurns;

        return await Task.Run(() => {
            var results = new List<TurnSearchMatch>();
            const StringComparison cmp = StringComparison.OrdinalIgnoreCase;
            const int MaxExcerptLength = 120;
            const int ExcerptPad       = 40; // chars of context on each side of the match

            for (var i = 0; i < turns.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var turn = turns[i];

                ScanField(turn.Prompt ?? string.Empty, "user", i, query, cmp, MaxExcerptLength, ExcerptPad, results);

                // Strip quick-reply blocks before scanning — the renderer also strips them
                // (via TryExtractQuickReplyOptionMetadata), so leaving them in causes phantom
                // match counts that cascade into skip-count errors across all later turns.
                var responseText = turn.ResponseText ?? string.Empty;
                if (QuickReplyOptionParser.TryExtractWithMetadata(responseText, out var cleanedResponse, out _))
                    responseText = cleanedResponse;
                ScanField(responseText, "assistant", i, query, cmp, MaxExcerptLength, ExcerptPad, results);
            }

            return (IReadOnlyList<TurnSearchMatch>)results;
        }, ct);
    }

    private static void ScanField(
        string text,
        string role,
        int turnIndex,
        string query,
        StringComparison cmp,
        int maxExcerptLength,
        int excerptPad,
        List<TurnSearchMatch> results) {

        var searchFrom = 0;
        while (searchFrom < text.Length) {
            var offset = text.IndexOf(query, searchFrom, cmp);
            if (offset < 0) break;

            var excerptStart = Math.Max(0, offset - excerptPad);
            var excerptEnd   = Math.Min(text.Length, offset + query.Length + excerptPad);
            var rawExcerpt   = text[excerptStart..excerptEnd];

            // Truncate to MaxExcerptLength and add ellipsis markers.
            string excerpt;
            if (rawExcerpt.Length > maxExcerptLength) {
                excerpt = rawExcerpt[..maxExcerptLength] + "…";
            } else {
                var prefix = excerptStart > 0             ? "…" : string.Empty;
                var suffix = excerptEnd   < text.Length   ? "…" : string.Empty;
                excerpt    = prefix + rawExcerpt + suffix;
            }

            results.Add(new TurnSearchMatch(turnIndex, role, excerpt, offset));
            searchFrom = offset + query.Length;
        }
    }

    /// <summary>
    /// Ensures that the coordinator turn at <paramref name="turnIndex"/> is
    /// rendered in the FlowDocument, prepending batches of older turns as needed.
    ///
    /// <para>
    /// Must be called from a context that can safely dispatch to the UI thread.
    /// The method marshals all FlowDocument mutations through the
    /// <see cref="Dispatcher"/> injected at construction time.
    /// </para>
    ///
    /// <para>
    /// If the turn is already rendered (<c>turnIndex &gt;= _coordinatorRenderedFromIndex</c>)
    /// this returns immediately.  Otherwise it calls <see cref="PrependOlderTurnsAsync"/>
    /// in a loop — each call prepends up to <c>PrependBatchSize</c> turns and
    /// updates <c>_coordinatorRenderedFromIndex</c> — until the target turn is
    /// within the rendered window.
    /// </para>
    /// </summary>
    /// <param name="turnIndex">
    ///   0-based index into <c>_allCoordinatorTurns</c> of the turn that must be
    ///   visible after this call returns.
    /// </param>
    internal async Task EnsureTurnRenderedAsync(int turnIndex) {
        if (turnIndex < 0 || turnIndex >= _allCoordinatorTurns.Count)
            return;

        // Fast path: already rendered.
        if (turnIndex >= _coordinatorRenderedFromIndex)
            return;

        SquadDashTrace.Write(TraceCategory.Performance,
            $"SEARCH_ENSURE_RENDER: target={turnIndex} current_from={_coordinatorRenderedFromIndex}");

        // Prepend batches until the target turn is in the rendered window.
        // PrependOlderTurnsAsync guards against re-entrancy via _prependInProgress,
        // so we loop and retry until we reach the target.
        while (_coordinatorRenderedFromIndex > turnIndex) {
            var before = _coordinatorRenderedFromIndex;
            var didPrepend = await PrependOlderTurnsAsync();
            if (!didPrepend) {
                // Either already at start or a prepend is in progress —
                // yield to let any in-flight prepend complete, then try again.
                await _dispatcher.InvokeAsync(
                    () => { /* no-op — just yields to the dispatcher queue */ },
                    DispatcherPriority.Background);

                // If nothing moved after the yield, bail out to avoid an infinite
                // loop (e.g. _allCoordinatorTurns changed underneath us).
                if (_coordinatorRenderedFromIndex >= before)
                    break;
            }
        }

        SquadDashTrace.Write(TraceCategory.Performance,
            $"SEARCH_ENSURE_RENDER_DONE: target={turnIndex} final_from={_coordinatorRenderedFromIndex}");
    }
}

internal readonly record struct SessionSelectionResult(
    bool Succeeded,
    string? SelectedSessionId,
    string? ErrorMessage) {

    internal static SessionSelectionResult Success(string sessionId) =>
        new(true, sessionId, null);

    internal static SessionSelectionResult Failure(string message) =>
        new(false, null, message);
}

/// <summary>
/// A single substring match found by <see cref="TranscriptConversationManager.SearchTurnsAsync"/>.
/// </summary>
/// <param name="TurnIndex">
///   0-based index into <c>_allCoordinatorTurns</c>.  Pass this to
///   <see cref="TranscriptConversationManager.EnsureTurnRenderedAsync"/> to make the
///   turn visible before navigating to it.
/// </param>
/// <param name="TurnRole">
///   Which part of the turn contained the match: <c>"user"</c> for the
///   <see cref="TranscriptTurnRecord.Prompt"/> field or <c>"assistant"</c>
///   for the <see cref="TranscriptTurnRecord.ResponseText"/> field.
/// </param>
/// <param name="MatchExcerpt">
///   A ≤120-character snippet of text surrounding the match, with leading/trailing
///   <c>…</c> markers when the excerpt is not at a field boundary.
/// </param>
/// <param name="MatchOffset">
///   Character offset of the match within the <em>searched field</em>
///   (<c>Prompt</c> or <c>ResponseText</c>).  The UI layer can use this for
///   in-excerpt highlighting.
/// </param>
/// <param name="Thread">
///   The agent transcript thread that contains this match, or <c>null</c> for the
///   main coordinator transcript.
/// </param>
internal record TurnSearchMatch(
    int    TurnIndex,
    string TurnRole,
    string MatchExcerpt,
    int    MatchOffset,
    TranscriptThreadState? Thread = null);
