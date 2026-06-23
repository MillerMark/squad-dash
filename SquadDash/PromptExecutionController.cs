using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// Owns prompt execution lifecycle: slash-command dispatch, async bridge execution,
/// prompt health monitoring, and all related transcript orchestration.
/// </summary>
internal sealed class PromptExecutionController {

    /// <summary>
    /// When true, a documentation placeholder instruction is appended to every prompt so that
    /// all agents use the standard screenshot placeholder format.
    /// MainWindow sets this whenever documentation mode is toggled.
    /// </summary>
    internal bool DocumentationModeActive { get; set; }

    /// <summary>
    /// Number of queued prompts still waiting after this one (set by MainWindow before
    /// each queue-driven dispatch, reset to 0 for user-typed dispatches).
    /// When > 0, a notice is injected into the prompt so the AI knows more items are
    /// coming automatically and can signal if it needs human input first.
    /// </summary>
    internal int PendingQueueItemCount { get; set; }

    /// <summary>
    /// Sentinel the AI must append (verbatim, at the end of its response) to signal that
    /// it needs a human response before the queue continues.
    /// SquadDash strips this from the rendered output and pauses the queue drain.
    /// </summary>
    internal const string QueueAwaitInputSentinel = "[[AWAITING_INPUT]]";

    /// <summary>Quick reply label shown on an interrupted-bridge message that re-runs the last prompt.</summary>
    internal const string ResendLastPromptQuickReply = "Resend last prompt";

    /// <summary>Quick reply label shown on an interrupted-bridge message that runs <c>git diff --stat</c> and injects the output as context for the AI.</summary>
    internal const string CheckGitDiffQuickReply = "Check git diff";

    /// <summary>
    /// Full path of the documentation file currently open in the docs panel.
    /// Injected into every prompt so agents know which file the user is referring to.
    /// </summary>
    internal string? ActiveDocumentPath { get; set; }

    /// <summary>
    /// Root folder of the docs tree for the current workspace (e.g. {workspace}/docs).
    /// Injected into every prompt so agents know where to create new topics.
    /// </summary>
    internal string? DocsRootFolder { get; set; }

    /// <summary>
    /// Full path to .squad/tasks.md for the current workspace.
    /// When set and the file exists, open (unchecked) task items are summarised and
    /// injected into every prompt so agents stay aware of outstanding work.
    /// </summary>
    internal string? TasksFilePath { get; set; }

    // Cache for tasks.md — avoids re-reading the file on every prompt dispatch.
    private string?   _tasksFilePathCached;
    private string[]? _tasksLinesCache;
    private DateTime  _tasksLastWriteTime;

    private string? BuildTasksContextInstruction() {
        if (string.IsNullOrEmpty(TasksFilePath) || !File.Exists(TasksFilePath))
            return null;
        string[] lines;
        try {
            var lastWrite = File.GetLastWriteTimeUtc(TasksFilePath);
            if (_tasksLinesCache is not null &&
                string.Equals(_tasksFilePathCached, TasksFilePath, StringComparison.OrdinalIgnoreCase) &&
                lastWrite == _tasksLastWriteTime)
            {
                lines = _tasksLinesCache;
            }
            else
            {
                lines = File.ReadAllLines(TasksFilePath);
                _tasksLinesCache     = lines;
                _tasksFilePathCached = TasksFilePath;
                _tasksLastWriteTime  = lastWrite;
            }
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Tasks", $"BuildTasksContextInstruction: failed to read {TasksFilePath}: {ex.Message}");
            return null;
        }
        return TasksContextBuilder.Build(lines);
    }

    private string BuildDocumentationContextInstruction() {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(
            """
            The documentation panel is open. When writing or editing documentation that will need a screenshot, use this exact two-line placeholder format — do not invent your own format:

            ![Screenshot: brief description](images/descriptive-filename.png)
            > 📸 *Screenshot needed: Detailed description of what to capture in this screenshot.*

            The image path should be relative to the document's folder (e.g. `images/my-feature.png`). SquadDash detects this pattern and lets the user right-click to paste a screenshot from clipboard directly into that location.
            """);

        if (!string.IsNullOrEmpty(DocsRootFolder))
            sb.AppendLine($"The documentation root folder for this workspace is: `{DocsRootFolder}`");

        if (!string.IsNullOrEmpty(ActiveDocumentPath))
        {
            var fileName = Path.GetFileName(ActiveDocumentPath);
            var folder   = Path.GetDirectoryName(ActiveDocumentPath);
            sb.AppendLine($"The currently open document is: **{fileName}** (full path: `{ActiveDocumentPath}`)");
            if (!string.IsNullOrEmpty(folder))
                sb.AppendLine($"Its folder is: `{folder}`");
            sb.AppendLine(
                $"If the user refers to \"this document\", \"this page\", \"add a section\", \"update the docs\", " +
                $"or uses similar phrasing without naming a specific file, they are most likely referring to `{fileName}`." +
                " New topics should be created in the same folder unless the user specifies otherwise.");
        }

        return sb.ToString().TrimEnd();
    }

    // ── Injected — Prompt Instructions ───────────────────────────────────
    private readonly IPromptInstructionProvider _instructionProvider;
    private readonly ISquadBridgePromptBuilder _promptBuilder;

    private static readonly TimeSpan PromptNoActivityWarningThreshold= TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PromptNoActivityStallThreshold   = TimeSpan.FromMinutes(2);
    // After 5 minutes of silence the prompt is considered dead: the restart-deferral gate
    // is lifted so a pending build-restart can proceed, and the "Cancel stalled agent"
    // recovery button becomes visible in the Queue tab.
    private static readonly TimeSpan PromptNoActivityDeadThreshold    = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PromptHealthDeltaTraceInterval   = TimeSpan.FromSeconds(1);

    // ── Injected — Bridge ─────────────────────────────────────────────────
    private readonly Func<string, string, string?, string?, Task> _runPromptAsync;
    private readonly Func<string, string, string, string?, string?, Task> _runNamedAgentDelegationAsync;
    private readonly Func<string, string, string?, string, string?, string?, Task> _runNamedAgentDirectAsync;

    // ── Injected — Interfaces ─────────────────────────────────────────────
    private readonly IWorkspaceContext     _workspaceContext;
    private readonly IPromptBoxState       _promptBoxState;
    private readonly ITranscriptRenderSink _transcriptSink;
    private readonly IAgentRosterView      _agentRosterView;

    // ── Injected — Conversation ───────────────────────────────────────────
    private readonly TranscriptConversationManager _conversationManager;

    // ── Injected — Background Tasks ───────────────────────────────────────
    private readonly BackgroundTaskPresenter _backgroundTaskPresenter;

    // ── Injected — CLI Adapter ────────────────────────────────────────────
    private readonly SquadCliAdapter _squadCliAdapter;

    // ── Injected — Running State ──────────────────────────────────────────
    private readonly Func<bool>   _getIsPromptRunning;
    private readonly Action<bool> _setIsPromptRunning;
    private readonly Func<bool>   _getIsClosing;
    private readonly Func<bool>   _getRestartPending;
    private readonly Action       _close;

    // ── Injected — Routing Flags ──────────────────────────────────────────
    private readonly Func<bool>   _getPendingRoutingRepairRecheck;
    private readonly Action<bool> _setPendingRoutingRepairRecheck;
    private readonly Func<string?> _getPendingSupplementalInstruction;
    private readonly Action        _clearPendingSupplementalInstruction;
    private readonly Func<string?> _getPendingQuickReplyRoutingInstruction;
    private readonly Func<string?> _getPendingQuickReplyRouteMode;

    // ── Injected — UI Orchestration ───────────────────────────────────────
    private readonly Action                     _updateInteractiveControlState;
    private readonly Action<string, string, string> _updateLeadAgent;
    private readonly Action<string>             _updateSessionState;
    private readonly Action                     _refreshAgentCards;
    private readonly Action                     _refreshSidebar;
    private readonly Action<string>             _setInstallStatus;
    private readonly Func<bool>                 _canShowOwnedWindow;
    private readonly Action<string, string>     _showTextWindow;
    private readonly Action                     _clearSessionView;
    private readonly Action                     _showTasksStatusWindow;
    private readonly Action                     _hideTasksStatusWindow;
    private readonly Action                     _showApprovalWindow;
    private readonly Action                     _showLiveTraceWindow;
    private readonly Action                     _showScreenshotHealthWindow;
    private readonly Action                     _runDoctor;
    private readonly Func<HireAgentWindow.HireAgentSubmission?> _showHireAgentWindow;
    private readonly Action<string, bool>       _enqueuePrompt;
    private readonly Action                     _showScreenshotOverlay;
    private readonly Action                     _triggerCodeHealthCycle;

    // ── Injected — Runtime Issue ──────────────────────────────────────────
    private readonly Func<string, WorkspaceIssuePresentation> _showRuntimeIssue;
    private readonly Action                                    _clearRuntimeIssue;

    // ── Injected — Routing Concern ────────────────────────────────────────
    private readonly Func<Task>          _waitForRoutingRepairSettleAsync;
    private readonly Action<string, bool> _maybePublishRoutingIssue;

    // ── Injected — Health Timer ───────────────────────────────────────────
    private readonly DispatcherTimer _promptHealthTimer;
    private readonly Func<Task> _waitForPostedUiActionsAsync;

    // ── Injected — Misc MainWindow helpers ────────────────────────────────
    private readonly Func<bool>                       _getModelObservedThisSession;

    // ── IWorkspacePaths ───────────────────────────────────────────────────
    private readonly IWorkspacePaths _workspacePaths;
    private readonly Func<IReadOnlyList<FollowUpAttachment>> _getSubmittedAttachments;
    /// When set, returns the HOST_COMMANDS catalog instruction to inject into every prompt.
    /// MainWindow sets this after constructing the HostCommandRegistry.
    /// </summary>
    internal Func<string?>? GetHostCommandCatalogInstruction { get; set; }

    // ── Owned fields ──────────────────────────────────────────────────────
    private bool             _universeSelectionPending;
    private DateTimeOffset?  _currentPromptStartedAt;
    private DateTimeOffset?  _lastPromptActivityAt;
    private string?          _lastPromptActivityName;
    private DateTimeOffset?  _sessionReadyAt;
    private DateTimeOffset?  _firstToolAt;
    private DateTimeOffset?  _firstResponseAt;
    private DateTimeOffset?  _lastResponseAt;
    private int              _responseDeltaCount;
    private int              _responseCharacterCount;
    private TimeSpan         _longestResponseGap;
    private TimeSpan         _totalResponseGap;
    private DateTimeOffset?  _firstThinkingTextAt;
    private DateTimeOffset?  _lastThinkingTextAt;
    private int              _thinkingDeltaCount;
    private int              _thinkingTextDeltaCount;
    private int              _thinkingCharacterCount;
    private TimeSpan         _longestThinkingGap;
    private TimeSpan         _totalThinkingGap;
    private DateTimeOffset?  _lastPromptHealthDeltaTraceAt;
    private int              _pendingHealthThinkingChunks;
    private int              _pendingHealthThinkingChars;
    private int              _pendingHealthResponseChunks;
    private int              _pendingHealthResponseChars;
    private int              _toolStartCount;
    private int              _toolCompleteCount;
    private bool             _promptNoActivityWarningShown;
    private bool             _promptStallWarningShown;
    private bool             _promptDeadWarningShown;

    // ── Silent-completion watchdog ────────────────────────────────────────
    private record SilentCompletionCandidate(SilentCompletionFollowUpReport Report, int ResponseCharsAtArrival);
    private readonly List<SilentCompletionCandidate> _silentCompletionCandidates = new();

    // ── Per-item sim state (set by /test-queue; consumed as items execute) ─
    /// <summary>
    /// The queue item currently being dispatched by MainWindow.
    /// Set by MainWindow before each <c>ExecutePromptAsync</c> call that originates
    /// from a queue drain, and cleared immediately after. Null for direct (non-queued)
    /// dispatches. Used to detect sim items at execution time.
    /// </summary>
    internal PromptQueueItem? CurrentDispatchedItem { get; set; }

    /// <summary>
    /// Sim metadata for the active-draft tab created by a <c>$ActiveDraft$</c> directive.
    /// Set by <c>/test-queue</c>, consumed on the next direct (non-queued) dispatch.
    /// Persisted via <see cref="WorkspaceConversationState.ActiveDraftSimResponse"/> and restored on startup.
    /// </summary>
    private (string SimResponse, int SimDelaySeconds)? _activeDraftSimEntry;

    internal (string SimResponse, int SimDelaySeconds)? ActiveDraftSimEntry
    {
        get => _activeDraftSimEntry;
        set => _activeDraftSimEntry = value;
    }

    /// <summary>
    /// Set when the user aborts a running prompt. Causes the next prompt to be prefixed
    /// with a retraction notice so the AI ignores the aborted turn.
    /// </summary>
    private bool             _nextPromptShouldRetractAborted;
    private SquadSdkEvent?   _lastSessionReadyEvent;
    private PromptContextDiagnostics? _currentPromptContextDiagnostics;

    /// <summary>
    /// The name of the tool currently executing (null when idle).
    /// MainWindow reads this via <c>_pec.ActiveToolName</c> for status-bar display.
    /// MainWindow's <c>SyncActiveToolName</c> writes this.
    /// </summary>
    internal string? ActiveToolName { get; set; }

    /// <summary>
    /// True while a <c>loop_started</c> event has been received and no corresponding
    /// <c>loop_stopped</c> or <c>loop_error</c> has arrived yet.
    /// UI indicator bindings (e.g. Lyra Morn's loop badge) should poll or observe this.
    /// </summary>
    internal bool IsLoopRunning { get; private set; }

    internal void SetIsLoopRunning(bool value) => IsLoopRunning = value;

    // ── Exposed read-only health state (BackgroundTaskPresenter forward-ref) ──
    internal bool            PromptNoActivityWarningShown => _promptNoActivityWarningShown;
    internal bool            PromptStallWarningShown      => _promptStallWarningShown;
    // True once the prompt has been silent for PromptNoActivityDeadThreshold (5 min).
    // When this is set the restart-deferral gate is lifted and the recovery button appears.
    internal bool            PromptAppearsDeadShown       => _promptDeadWarningShown;
    internal DateTimeOffset? CurrentPromptStartedAt       => _currentPromptStartedAt;
    internal DateTimeOffset? LastPromptActivityAt         => _lastPromptActivityAt;
    internal string?         LastPromptActivityName       => _lastPromptActivityName;
    internal DateTimeOffset? SessionReadyAt               => _sessionReadyAt;
    internal DateTimeOffset? FirstToolAt                  => _firstToolAt;
    internal DateTimeOffset? FirstResponseAt              => _firstResponseAt;
    internal DateTimeOffset? LastResponseAt               => _lastResponseAt;
    internal int             ResponseDeltaCount           => _responseDeltaCount;
    internal int             ResponseCharacterCount       => _responseCharacterCount;
    internal TimeSpan?       LongestResponseGap           => _longestResponseGap > TimeSpan.Zero ? _longestResponseGap : null;
    internal TimeSpan?       AverageResponseGap           => _responseDeltaCount > 1 ? TimeSpan.FromTicks(_totalResponseGap.Ticks / (_responseDeltaCount - 1)) : null;
    internal DateTimeOffset? FirstThinkingTextAt         => _firstThinkingTextAt;
    internal DateTimeOffset? LastThinkingTextAt          => _lastThinkingTextAt;
    internal int             ThinkingDeltaCount           => _thinkingDeltaCount;
    internal int             ThinkingTextDeltaCount       => _thinkingTextDeltaCount;
    internal int             ThinkingCharacterCount       => _thinkingCharacterCount;
    internal TimeSpan?       LongestThinkingGap           => _longestThinkingGap > TimeSpan.Zero ? _longestThinkingGap : null;
    internal TimeSpan?       AverageThinkingGap           => _thinkingTextDeltaCount > 1 ? TimeSpan.FromTicks(_totalThinkingGap.Ticks / (_thinkingTextDeltaCount - 1)) : null;
    internal int             ToolStartCount               => _toolStartCount;
    internal int             ToolCompleteCount            => _toolCompleteCount;
    internal string?         LatestSessionDiagnosticsSummary => SessionResumeDiagnosticsPresentation.BuildSummary(_lastSessionReadyEvent);

    // ─────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────

    internal PromptExecutionController(
        // Bridge
        Func<string, string, string?, string?, Task> runPromptAsync,
        Func<string, string, string, string?, string?, Task> runNamedAgentDelegationAsync,
        Func<string, string, string?, string, string?, string?, Task> runNamedAgentDirectAsync,
        // Core interfaces
        IWorkspaceContext workspaceContext,
        IPromptBoxState promptBoxState,
        ITranscriptRenderSink transcriptSink,
        IAgentRosterView agentRosterView,
        // Conversation
        TranscriptConversationManager conversationManager,
        // Background Tasks
        BackgroundTaskPresenter backgroundTaskPresenter,
        // CLI Adapter
        SquadCliAdapter squadCliAdapter,
        // Running State
        Func<bool> getIsPromptRunning,
        Action<bool> setIsPromptRunning,
        Func<bool> getIsClosing,
        Func<bool> getRestartPending,
        Action close,
        // Routing Flags
        Func<bool> getPendingRoutingRepairRecheck,
        Action<bool> setPendingRoutingRepairRecheck,
        Func<string?> getPendingSupplementalInstruction,
        Action clearPendingSupplementalInstruction,
        Func<string?> getPendingQuickReplyRoutingInstruction,
        Func<string?> getPendingQuickReplyRouteMode,
        // UI Orchestration
        Action updateInteractiveControlState,
        Action<string, string, string> updateLeadAgent,
        Action<string> updateSessionState,
        Action refreshAgentCards,
        Action refreshSidebar,
        Action<string> setInstallStatus,
        Func<bool> canShowOwnedWindow,
        Action<string, string> showTextWindow,
        Action clearSessionView,
        Action showTasksStatusWindow,
        Action hideTasksStatusWindow,
        Action showApprovalWindow,
        Action showLiveTraceWindow,
        Action showScreenshotHealthWindow,
        Action runDoctor,
        Func<HireAgentWindow.HireAgentSubmission?> showHireAgentWindow,
        Action<string, bool> enqueuePrompt,
        Action showScreenshotOverlay,
        Action triggerCodeHealthCycle,
        // Runtime Issue
        Func<string, WorkspaceIssuePresentation> showRuntimeIssue,
        Action clearRuntimeIssue,
        // Routing Concern
        Func<Task> waitForRoutingRepairSettleAsync,
        Action<string, bool> maybePublishRoutingIssue,
        // Health Timer
        DispatcherTimer promptHealthTimer,
        Func<Task> waitForPostedUiActionsAsync,
        // Misc MainWindow helpers
        Func<bool> getModelObservedThisSession,
        // Tool entries (for MarkActiveToolsAsFailed)
        IWorkspacePaths workspacePaths,
        IPromptInstructionProvider? instructionProvider = null,
        Func<IReadOnlyList<FollowUpAttachment>>? getSubmittedAttachments = null,
        ISquadBridgePromptBuilder? promptBuilder = null) {

        _runPromptAsync                        = runPromptAsync;
        _runNamedAgentDelegationAsync          = runNamedAgentDelegationAsync;
        _runNamedAgentDirectAsync              = runNamedAgentDirectAsync;
        _workspaceContext                      = workspaceContext;
        _promptBoxState                        = promptBoxState;
        _transcriptSink                        = transcriptSink;
        _agentRosterView                       = agentRosterView;
        _conversationManager                   = conversationManager;
        _backgroundTaskPresenter               = backgroundTaskPresenter;
        _squadCliAdapter                       = squadCliAdapter;
        _getIsPromptRunning                    = getIsPromptRunning;
        _setIsPromptRunning                    = setIsPromptRunning;
        _getIsClosing                          = getIsClosing;
        _getRestartPending                     = getRestartPending;
        _close                                 = close;
        _getPendingRoutingRepairRecheck        = getPendingRoutingRepairRecheck;
        _setPendingRoutingRepairRecheck        = setPendingRoutingRepairRecheck;
        _getPendingSupplementalInstruction     = getPendingSupplementalInstruction;
        _clearPendingSupplementalInstruction   = clearPendingSupplementalInstruction;
        _getPendingQuickReplyRoutingInstruction = getPendingQuickReplyRoutingInstruction;
        _getPendingQuickReplyRouteMode         = getPendingQuickReplyRouteMode;
        _updateInteractiveControlState         = updateInteractiveControlState;
        _updateLeadAgent                       = updateLeadAgent;
        _updateSessionState                    = updateSessionState;
        _refreshAgentCards                     = refreshAgentCards;
        _refreshSidebar                        = refreshSidebar;
        _setInstallStatus                      = setInstallStatus;
        _canShowOwnedWindow                    = canShowOwnedWindow;
        _showTextWindow                        = showTextWindow;
        _clearSessionView                      = clearSessionView;
        _showTasksStatusWindow                 = showTasksStatusWindow;
        _hideTasksStatusWindow                 = hideTasksStatusWindow;
        _showApprovalWindow                    = showApprovalWindow;
        _showLiveTraceWindow                   = showLiveTraceWindow;
        _showScreenshotHealthWindow            = showScreenshotHealthWindow;
        _runDoctor                             = runDoctor;
        _showHireAgentWindow                   = showHireAgentWindow;
        _enqueuePrompt                         = enqueuePrompt;
        _showScreenshotOverlay                 = showScreenshotOverlay;
        _triggerCodeHealthCycle               = triggerCodeHealthCycle;
        _showRuntimeIssue                      = showRuntimeIssue;
        _clearRuntimeIssue                     = clearRuntimeIssue;
        _waitForRoutingRepairSettleAsync       = waitForRoutingRepairSettleAsync;
        _maybePublishRoutingIssue              = maybePublishRoutingIssue;
        _promptHealthTimer                     = promptHealthTimer;
        _waitForPostedUiActionsAsync           = waitForPostedUiActionsAsync;
        _getModelObservedThisSession           = getModelObservedThisSession;
        _workspacePaths                        = workspacePaths;
        _instructionProvider                   = instructionProvider ?? new DefaultPromptInstructionProvider();
        _getSubmittedAttachments               = getSubmittedAttachments ?? (() => Array.Empty<FollowUpAttachment>());
        _promptBuilder                         = promptBuilder ?? new SquadBridgePromptBuilder();

        _promptHealthTimer.Tick += PromptHealthTimer_Tick;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Prompt Health Monitoring (Straggler group)
    // ─────────────────────────────────────────────────────────────────────

    private void PromptHealthTimer_Tick(object? sender, EventArgs e) {
        if (!_getIsPromptRunning() || _lastPromptActivityAt is null)
            return;

        var idleFor = DateTimeOffset.Now - _lastPromptActivityAt.Value;
        if (!_promptNoActivityWarningShown && idleFor >= PromptNoActivityWarningThreshold) {
            _promptNoActivityWarningShown = true;
            SquadDashTrace.Write(
                "PromptHealth",
                $"No bridge activity for {idleFor.TotalSeconds:0}s since prompt start. state={_agentRosterView.CurrentSessionState ?? "(unknown)"}");
            _updateLeadAgent("Waiting", string.Empty, "Waiting for the next response chunk.");
            _updateSessionState("Waiting");
        }

        if (!_promptStallWarningShown && idleFor >= PromptNoActivityStallThreshold) {
            _promptStallWarningShown = true;
            SquadDashTrace.Write(
                "PromptHealth",
                $"Prompt appears stalled after {idleFor.TotalSeconds:0}s without bridge activity.");
            _updateLeadAgent("Waiting", string.Empty, "Still waiting for Squad to finish this turn.");
            _updateSessionState("Waiting");
        }

        if (!_promptDeadWarningShown && idleFor >= PromptNoActivityDeadThreshold) {
            _promptDeadWarningShown = true;
            SquadDashTrace.Write(
                "PromptHealth",
                $"Prompt assumed dead after {idleFor.TotalSeconds:0}s without bridge activity. Restart gate lifted; recovery button visible.");
        }
    }

    private void StartPromptHealthMonitoring() {
        var now = DateTimeOffset.Now;
        _currentPromptStartedAt       = now;
        _lastPromptActivityAt         = now;
        _lastPromptActivityName       = "prompt_start";
        _sessionReadyAt               = null;
        _firstToolAt                  = null;
        _firstResponseAt              = null;
        _lastResponseAt               = null;
        _responseDeltaCount           = 0;
        _responseCharacterCount       = 0;
        _longestResponseGap           = TimeSpan.Zero;
        _totalResponseGap             = TimeSpan.Zero;
        _firstThinkingTextAt          = null;
        _lastThinkingTextAt           = null;
        _thinkingDeltaCount           = 0;
        _thinkingTextDeltaCount       = 0;
        _thinkingCharacterCount       = 0;
        _longestThinkingGap           = TimeSpan.Zero;
        _totalThinkingGap             = TimeSpan.Zero;
        _lastPromptHealthDeltaTraceAt = now;
        _pendingHealthThinkingChunks  = 0;
        _pendingHealthThinkingChars   = 0;
        _pendingHealthResponseChunks  = 0;
        _pendingHealthResponseChars   = 0;
        _toolStartCount               = 0;
        _toolCompleteCount            = 0;
        _promptNoActivityWarningShown = false;
        _promptStallWarningShown      = false;
        _lastSessionReadyEvent        = null;
        _silentCompletionCandidates.Clear();
        _promptHealthTimer.Start();
        SquadDashTrace.Write("PromptHealth", $"Prompt started at {now:O}");
    }

    /// <summary>
    /// Records bridge activity. Called from MainWindow's HandleEvent and HandleBridgeError.
    /// </summary>
    internal void MarkActivity(string activityName) {
        if (!_getIsPromptRunning())
            return;

        var now = DateTimeOffset.Now;
        RecordPromptActivity(activityName, now, writeActivityTrace: true);
    }

    private void RecordPromptActivity(string activityName, DateTimeOffset now, bool writeActivityTrace) {
        if (_promptNoActivityWarningShown || _promptStallWarningShown || _promptDeadWarningShown) {
            SquadDashTrace.Write(
                "PromptHealth",
                $"Prompt activity resumed after quiet period. activity={activityName} idleMs={(now - (_lastPromptActivityAt ?? now)).TotalMilliseconds:0}");
            _promptNoActivityWarningShown = false;
            _promptStallWarningShown      = false;
            _promptDeadWarningShown       = false;
        }

        _lastPromptActivityAt = now;
        _lastPromptActivityName = activityName;

        if (writeActivityTrace && _currentPromptStartedAt is { } startedAt) {
            var elapsed = now - startedAt;
            SquadDashTrace.Write(
                "PromptHealth",
                $"Activity={activityName} elapsedMs={elapsed.TotalMilliseconds:0}");
        }
    }

    internal void MarkActivity(SquadSdkEvent evt) {
        var activityName = evt.Type ?? "event";
        if (!_getIsPromptRunning())
            return;

        if (!ShouldCountPromptActivity(evt, _getRestartPending()))
            return;

        var now = _lastPromptActivityAt ?? DateTimeOffset.Now;
        var isStreamingDelta = evt.Type is "thinking_delta" or "response_delta" or "subagent_thinking_delta";
        if (isStreamingDelta)
            RecordPromptActivity(activityName, DateTimeOffset.Now, writeActivityTrace: false);
        else {
            FlushPromptHealthDeltaTrace(DateTimeOffset.Now, force: true);
            MarkActivity(activityName);
        }

        now = _lastPromptActivityAt ?? DateTimeOffset.Now;
        switch (evt.Type) {
            case "session_ready":
                _lastSessionReadyEvent = evt;
                if (_sessionReadyAt is null) {
                    _sessionReadyAt = now;
                    SquadDashTrace.Write(
                        "PromptHealth",
                        $"Session ready after {FormatElapsedSincePrompt(now)}.");
                    if (SessionResumeDiagnosticsPresentation.BuildSummary(evt) is { } diagnostics)
                        SquadDashTrace.Write("PromptHealth", $"Session diagnostics: {diagnostics}");
                }
                break;

            case "tool_start":
                _toolStartCount++;
                if (_firstToolAt is null) {
                    _firstToolAt = now;
                    SquadDashTrace.Write(
                        "PromptHealth",
                        $"First tool started after {FormatElapsedSincePrompt(now)}.");
                }
                break;

            case "tool_complete":
                _toolCompleteCount++;
                break;

            case "thinking_delta":
                _thinkingDeltaCount++;
                var thinkingTextLength = evt.Text?.Length ?? 0;
                if (!string.IsNullOrWhiteSpace(evt.Text)) {
                    if (_firstThinkingTextAt is null) {
                        _firstThinkingTextAt = now;
                        SquadDashTrace.Write(
                            "PromptHealth",
                            $"First thinking text arrived after {FormatElapsedSincePrompt(now)}.");
                    }

                    if (_lastThinkingTextAt is { } lastThinkingTextAt) {
                        var thinkingGap = now - lastThinkingTextAt;
                        _totalThinkingGap += thinkingGap;
                        if (thinkingGap > _longestThinkingGap)
                            _longestThinkingGap = thinkingGap;

                        if (thinkingGap >= TimeSpan.FromSeconds(5)) {
                            SquadDashTrace.Write(
                                "PromptHealth",
                                $"Observed thinking gap of {StatusTimingPresentation.FormatDuration(thinkingGap)} between thought chunks.");
                        }
                    }

                    _lastThinkingTextAt = now;
                    _thinkingTextDeltaCount++;
                    _thinkingCharacterCount += thinkingTextLength;
                    if (thinkingTextLength > 0) {
                        _pendingHealthThinkingChunks++;
                        _pendingHealthThinkingChars += thinkingTextLength;
                        FlushPromptHealthDeltaTrace(now, force: false);
                    }
                }
                break;

            case "response_delta":
                var chunkLength = evt.Chunk?.Length ?? 0;
                if (_firstResponseAt is null) {
                    _firstResponseAt = now;
                    SquadDashTrace.Write(
                        "PromptHealth",
                        $"First response text arrived after {FormatElapsedSincePrompt(now)}.");
                }

                if (_lastResponseAt is { } lastResponseAt) {
                    var gap = now - lastResponseAt;
                    _totalResponseGap += gap;
                    if (gap > _longestResponseGap)
                        _longestResponseGap = gap;

                    if (gap >= TimeSpan.FromSeconds(5)) {
                        SquadDashTrace.Write(
                            "PromptHealth",
                            $"Observed response gap of {StatusTimingPresentation.FormatDuration(gap)} between streamed chunks.");
                    }
                }

                _lastResponseAt = now;
                _responseDeltaCount++;
                _responseCharacterCount += chunkLength;
                if (chunkLength > 0) {
                    _pendingHealthResponseChunks++;
                    _pendingHealthResponseChars += chunkLength;
                    FlushPromptHealthDeltaTrace(now, force: false);
                }
                break;
        }
    }

    internal static bool ShouldCountPromptActivity(SquadSdkEvent evt, bool restartPending) {
        if (!restartPending)
            return true;

        if (string.Equals(evt.Type, "usage", StringComparison.Ordinal))
            return false;

        if (evt.Type is "tool_start" or "tool_progress" or "tool_complete"
            or "subagent_tool_start" or "subagent_tool_progress" or "subagent_tool_complete" &&
            string.Equals(evt.ToolName, "read_powershell", StringComparison.OrdinalIgnoreCase) &&
            IsDelayOnlyPowerShellRead(evt.Args))
            return false;

        return true;
    }

    private static bool IsDelayOnlyPowerShellRead(JsonElement args) {
        if (args.ValueKind != JsonValueKind.Object)
            return false;

        return args.TryGetProperty("delay", out _) &&
               args.TryGetProperty("shellId", out _) &&
               args.EnumerateObject().All(property =>
                   property.NameEquals("delay") ||
                   property.NameEquals("shellId"));
    }

    private void FlushPromptHealthDeltaTrace(DateTimeOffset now, bool force) {
        if (!force &&
            _lastPromptHealthDeltaTraceAt is { } last &&
            now - last < PromptHealthDeltaTraceInterval)
            return;

        if (_pendingHealthThinkingChunks == 0 && _pendingHealthResponseChunks == 0)
            return;

        SquadDashTrace.Write(
            "PromptHealth",
            $"Stream chunk summary thinkingChunks={_pendingHealthThinkingChunks} thinkingChars={_pendingHealthThinkingChars} " +
            $"totalThinkingChars={_thinkingCharacterCount} totalThinkingChunks={_thinkingTextDeltaCount} avgThinkingCharsPerSec={FormatThinkingCharsPerSecond()} " +
            $"responseChunks={_pendingHealthResponseChunks} responseChars={_pendingHealthResponseChars} " +
            $"totalResponseChars={_responseCharacterCount} totalResponseChunks={_responseDeltaCount}");

        _pendingHealthThinkingChunks = 0;
        _pendingHealthThinkingChars = 0;
        _pendingHealthResponseChunks = 0;
        _pendingHealthResponseChars = 0;
        _lastPromptHealthDeltaTraceAt = now;
    }

    private void StopPromptHealthMonitoring(string outcome) {
        if (_currentPromptStartedAt is { } startedAt) {
            var finishedAt = DateTimeOffset.Now;
            FlushPromptHealthDeltaTrace(finishedAt, force: true);
            SquadDashTrace.Write(
                "PromptHealth",
                $"Prompt finished outcome={outcome} totalMs={(finishedAt - startedAt).TotalMilliseconds:0} " +
                $"sessionReadyMs={FormatElapsedMilliseconds(startedAt, _sessionReadyAt)} " +
                $"firstToolMs={FormatElapsedMilliseconds(startedAt, _firstToolAt)} " +
                $"firstResponseMs={FormatElapsedMilliseconds(startedAt, _firstResponseAt)} " +
                $"lastResponseMs={FormatElapsedMilliseconds(startedAt, _lastResponseAt)} " +
                $"responseChunks={_responseDeltaCount} responseChars={_responseCharacterCount} " +
                $"avgResponseGapMs={FormatAverageGapMilliseconds()} " +
                $"longestResponseGapMs={(int)_longestResponseGap.TotalMilliseconds} " +
                $"firstThinkingTextMs={FormatElapsedMilliseconds(startedAt, _firstThinkingTextAt)} " +
                $"lastThinkingTextMs={FormatElapsedMilliseconds(startedAt, _lastThinkingTextAt)} " +
                $"thinkingDeltas={_thinkingDeltaCount} toolStarts={_toolStartCount} toolCompletes={_toolCompleteCount} " +
                $"thinkingTextDeltas={_thinkingTextDeltaCount} thinkingChars={_thinkingCharacterCount} " +
                $"avgThinkingCharsPerSec={FormatThinkingCharsPerSecond()} avgThinkingCharsPerChunk={FormatAverageThinkingChunkSize()} " +
                $"avgThinkingGapMs={FormatAverageThinkingGapMilliseconds()} longestThinkingGapMs={(int)_longestThinkingGap.TotalMilliseconds} " +
                $"lastActivity={_lastPromptActivityName ?? "(unknown)"} " +
                $"{FormatPromptContextDiagnostics(finishedAt)}");
        }

        // Check for silent completions before response counters are cleared.
        if ((outcome == "completed" || outcome == "interrupted") && _silentCompletionCandidates.Count > 0)
            MaybeInjectSilentCompletionFollowUp();
        _silentCompletionCandidates.Clear();

        _promptHealthTimer.Stop();
        _currentPromptStartedAt       = null;
        _lastPromptActivityAt         = null;
        _lastPromptActivityName       = null;
        _sessionReadyAt               = null;
        _firstToolAt                  = null;
        _firstResponseAt              = null;
        _lastResponseAt               = null;
        _responseDeltaCount           = 0;
        _responseCharacterCount       = 0;
        _longestResponseGap           = TimeSpan.Zero;
        _totalResponseGap             = TimeSpan.Zero;
        _firstThinkingTextAt          = null;
        _lastThinkingTextAt           = null;
        _thinkingDeltaCount           = 0;
        _thinkingTextDeltaCount       = 0;
        _thinkingCharacterCount       = 0;
        _longestThinkingGap           = TimeSpan.Zero;
        _totalThinkingGap             = TimeSpan.Zero;
        _lastPromptHealthDeltaTraceAt = null;
        _pendingHealthThinkingChunks  = 0;
        _pendingHealthThinkingChars   = 0;
        _pendingHealthResponseChunks  = 0;
        _pendingHealthResponseChars   = 0;
        _toolStartCount               = 0;
        _toolCompleteCount            = 0;
        _promptNoActivityWarningShown = false;
        _promptStallWarningShown      = false;
        _promptDeadWarningShown       = false;
        _lastSessionReadyEvent        = null;
        _currentPromptContextDiagnostics = null;
    }

    private string FormatElapsedSincePrompt(DateTimeOffset timestamp) {
        if (_currentPromptStartedAt is not { } startedAt)
            return "unknown";

        return StatusTimingPresentation.FormatDuration(timestamp - startedAt);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Silent-completion watchdog
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called from <c>HandleSubagentCompleted</c> when the coordinator's turn is active
    /// and the subagent report was promoted into that turn.  Registers the agent as a
    /// silent-completion candidate so we can detect if the coordinator's turn ends
    /// without generating any follow-up response text.
    /// </summary>
    internal void NotifySubagentCompletedDuringTurn(TranscriptThreadState thread, string fallbackAgentDisplayName) {
        if (!_getIsPromptRunning())
            return;

        var agentDisplayName = !string.IsNullOrWhiteSpace(thread.Title)
            ? thread.Title.Trim()
            : fallbackAgentDisplayName;
        var reportText = !string.IsNullOrWhiteSpace(thread.LastCoordinatorAnnouncedResponse)
            ? thread.LastCoordinatorAnnouncedResponse
            : thread.LatestResponse;

        _silentCompletionCandidates.Add(new SilentCompletionCandidate(
            new SilentCompletionFollowUpReport(
                agentDisplayName,
                string.IsNullOrWhiteSpace(thread.ThreadId) ? null : thread.ThreadId,
                thread.CompletedAt,
                thread.Prompt,
                reportText),
            _responseCharacterCount));
        SquadDashTrace.Write(
            "PromptHealth",
            $"SilentCompletionWatchdog: registered candidate agent={agentDisplayName} thread={thread.ThreadId} responseCharsAtArrival={_responseCharacterCount}");
    }

    /// <summary>
    /// Checks whether any registered subagent produced zero coordinator response text
    /// after it completed.  If so, enqueues a follow-up prompt so the coordinator
    /// reviews and reports the silently-received results to the user.
    /// Must be called before <c>_responseCharacterCount</c> is reset.
    /// </summary>
    private void MaybeInjectSilentCompletionFollowUp() {
        var silentReports = _silentCompletionCandidates
            .Where(c => _responseCharacterCount == c.ResponseCharsAtArrival)
            .Select(c => c.Report)
            .ToList();

        if (silentReports.Count == 0) {
            SquadDashTrace.Write(
                "PromptHealth",
                "SilentCompletionWatchdog: all candidates produced response text — no injection needed.");
            return;
        }

        var injection = SilentCompletionFollowUpPromptBuilder.Build(silentReports);

        SquadDashTrace.Write(
            "PromptHealth",
            $"SilentCompletionWatchdog: injecting follow-up for {silentReports.Count} agent(s): {string.Join(", ", silentReports.Select(r => r.AgentDisplayName))}");

        _enqueuePrompt(injection, true /* isSystemInjected */);
    }


    private static string FormatElapsedMilliseconds(DateTimeOffset startedAt, DateTimeOffset? milestoneAt) =>
        milestoneAt is { } value
            ? ((int)(value - startedAt).TotalMilliseconds).ToString()
            : "n/a";

    private string FormatAverageGapMilliseconds() =>
        _responseDeltaCount > 1
            ? ((int)(_totalResponseGap.TotalMilliseconds / (_responseDeltaCount - 1))).ToString()
            : "n/a";

    private string FormatAverageThinkingGapMilliseconds() =>
        _thinkingTextDeltaCount > 1
            ? ((int)(_totalThinkingGap.TotalMilliseconds / (_thinkingTextDeltaCount - 1))).ToString()
            : "n/a";

    private string FormatThinkingCharsPerSecond() =>
        PromptTraceMetrics.FormatCharsPerSecond(_thinkingCharacterCount, _firstThinkingTextAt, _lastThinkingTextAt);

    private string FormatAverageThinkingChunkSize() =>
        PromptTraceMetrics.FormatAverageChunkSize(_thinkingCharacterCount, _thinkingTextDeltaCount);

    private string FormatPromptContextDiagnostics(DateTimeOffset now) =>
        _currentPromptContextDiagnostics is null
            ? "context=(none)"
            : PromptContextDiagnosticsPresentation.BuildTraceSummary(_currentPromptContextDiagnostics, now);

    // ─────────────────────────────────────────────────────────────────────
    // Local Command Dispatch
    // ─────────────────────────────────────────────────────────────────────

    internal bool TryHandleLocalCommand(
        string prompt,
        bool addToHistory,
        bool clearPromptBox) {
        var trimmed = prompt.Trim();

        // Intercept universe selection responses — only if the input matches a known option.
        // Free-typed text cancels the pending prompt and falls through to normal processing.
        if (_universeSelectionPending) {
            if (MainWindow.UniverseSelectorOptions.Any(u => string.Equals(u, trimmed, StringComparison.OrdinalIgnoreCase)))
                return HandleUniverseSelection(trimmed);
            _universeSelectionPending = false;
        }

        if (TryMatchCommandWithArguments(trimmed, "/activate", out var activateArgs))
            return HandleLocalActivateCommand(prompt, activateArgs, addToHistory, clearPromptBox);

        if (TryMatchCommandWithArguments(trimmed, "/deactivate", out var deactivateArgs))
            return HandleLocalDeactivateCommand(prompt, deactivateArgs, addToHistory, clearPromptBox);

        if (TryMatchCommandWithArguments(trimmed, "/retire", out var retireArgs))
            return HandleLocalRetireCommand(prompt, retireArgs, addToHistory, clearPromptBox);

        if (TryMatchCommandWithArguments(trimmed, "/session", out var sessionArgs))
            return HandleLocalSessionCommand(prompt, sessionArgs, addToHistory, clearPromptBox);

        if (string.Equals(trimmed, "/tasks", StringComparison.OrdinalIgnoreCase))
            return HandleLocalTasksCommand(prompt, addToHistory, clearPromptBox);

        if (string.Equals(trimmed, "/approval", StringComparison.OrdinalIgnoreCase))
            return HandleLocalApprovalCommand(prompt, addToHistory, clearPromptBox);

        if (string.Equals(trimmed, "/dropTasks", StringComparison.OrdinalIgnoreCase))
            return HandleLocalDropTasksCommand(prompt, addToHistory, clearPromptBox);

        if (string.Equals(trimmed, "/trace", StringComparison.OrdinalIgnoreCase))
            return HandleLocalTraceCommand(prompt, addToHistory, clearPromptBox);

        if (string.Equals(trimmed, "/health", StringComparison.OrdinalIgnoreCase))
            return HandleLocalHealthCommand(prompt, addToHistory, clearPromptBox);

        if (string.Equals(trimmed, "/doctor", StringComparison.OrdinalIgnoreCase))
            return HandleLocalDoctorCommand(prompt, addToHistory, clearPromptBox);

        if (string.Equals(trimmed, "/help", StringComparison.OrdinalIgnoreCase))
            return HandleLocalHelpCommand(prompt, addToHistory, clearPromptBox);

        if (string.Equals(trimmed, "/hire", StringComparison.OrdinalIgnoreCase))
            return HandleLocalHireCommand(prompt, addToHistory, clearPromptBox);

        if (string.Equals(trimmed, "/model", StringComparison.OrdinalIgnoreCase))
            return HandleLocalModelCommand(prompt, addToHistory, clearPromptBox);

        if (string.Equals(trimmed, "/status", StringComparison.OrdinalIgnoreCase))
            return HandleLocalStatusCommand(prompt, addToHistory, clearPromptBox);

        if (string.Equals(trimmed, "/sessions", StringComparison.OrdinalIgnoreCase))
            return HandleLocalSessionsCommand(prompt, addToHistory, clearPromptBox);

        if (TryMatchCommandWithArguments(trimmed, "/resume", out var resumeArgs))
            return HandleLocalResumeCommand(prompt, resumeArgs, addToHistory, clearPromptBox);

        if (string.Equals(trimmed, "/agents", StringComparison.OrdinalIgnoreCase))
            return HandleLocalAgentsCommand(prompt, addToHistory, clearPromptBox);

        if (string.Equals(trimmed, "/version", StringComparison.OrdinalIgnoreCase))
            return HandleLocalVersionCommand(prompt, addToHistory, clearPromptBox);

        if (string.Equals(trimmed, "/testtable", StringComparison.OrdinalIgnoreCase))
            return HandleLocalTestTableCommand(prompt, addToHistory, clearPromptBox);

        if (string.Equals(trimmed, "/clear", StringComparison.OrdinalIgnoreCase))
            return HandleLocalClearCommand(prompt, addToHistory, clearPromptBox);

        if (TryMatchCommandWithBody(trimmed, "/test-queue", out var testQueueBody))
            return HandleLocalTestQueueCommand(prompt, testQueueBody, addToHistory, clearPromptBox);
        if (TryMatchCommandWithBody(trimmed, "/queue-sim", out var queueSimBody))
            return HandleLocalTestQueueCommand(prompt, queueSimBody, addToHistory, clearPromptBox);

        if (string.Equals(trimmed, "/codehealth", StringComparison.OrdinalIgnoreCase))
            return HandleLocalCodeHealthCommand(prompt, addToHistory, clearPromptBox);

        return false;
    }

    private bool HandleLocalTasksCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write(
            "UI",
            $"Local command intercepted prompt=/tasks sessionId={_conversationManager.CurrentSessionId ?? "(none)"} {_backgroundTaskPresenter.BuildBackgroundTaskTraceSummary()}");

        return ExecuteLocalUiCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            _showTasksStatusWindow);
    }

    private bool HandleLocalApprovalCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/approval");

        return ExecuteLocalUiCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            _showApprovalWindow);
    }

    private bool HandleLocalDropTasksCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/dropTasks");

        return ExecuteLocalUiCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            _hideTasksStatusWindow);
    }

    private bool HandleLocalTraceCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/trace");

        return ExecuteLocalUiCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            _showLiveTraceWindow);
    }

    private bool HandleLocalHealthCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/health");

        return ExecuteLocalUiCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            _showScreenshotHealthWindow);
    }

    private bool HandleLocalDoctorCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/doctor");
        return ExecuteLocalUiCommand(prompt, addToHistory, clearPromptBox, _runDoctor);
    }

    private bool HandleLocalCodeHealthCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/maintenance");
        return ExecuteLocalUiCommand(prompt, addToHistory, clearPromptBox, _triggerCodeHealthCycle);
    }

    private bool HandleLocalModelCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", $"Local command intercepted prompt=/model lastObservedModel={_squadCliAdapter.LastObservedModel ?? "(unknown)"}");

        return ExecuteLocalCoordinatorCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            () => {
                if (!string.IsNullOrWhiteSpace(_squadCliAdapter.LastObservedModel)) {
                    var modelLabel = _getModelObservedThisSession()
                        ? "Active model"
                        : "Model (last session)";
                    _transcriptSink.AppendLine($"{modelLabel}: **{_squadCliAdapter.LastObservedModel}**", null);
                }
                else {
                    _transcriptSink.AppendLine("Active model: *(not yet observed — run a prompt first)*", null);
                }
            });
    }

    private bool HandleLocalHireCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/hire");

        if (addToHistory)
            _conversationManager.AddPromptToHistory(prompt);

        MaybeClearPromptAfterLocalCommand(prompt, clearPromptBox);
        var submission = _showHireAgentWindow();
        if (submission is null) {
            if (!_getIsClosing() && _promptBoxState.IsPromptTextBoxEnabled)
                _promptBoxState.FocusPromptTextBox();
            return true;
        }

        _enqueuePrompt(submission.PromptText, false);
        return true;
    }

    private bool HandleLocalActivateCommand(string prompt, string targetName, bool addToHistory, bool clearPromptBox) =>
        HandleLifecycleCommand(
            prompt,
            targetName,
            addToHistory,
            clearPromptBox,
            "activate",
            "Use Squad's roster-management workflow to restore this team member to active service.",
            [
                "If the member exists with status `inactive`, restore them to the active roster and update `.squad/team.md`, `.squad/routing.md`, and `.squad/casting/registry.json`.",
                "If the member exists with status `retired`, restore them only if the user is explicitly asking to bring them back; restore any archived agent folder from `.squad/agents/_alumni/` when needed.",
                "Set the final registry status to `active`.",
                "If the member is already active, report that and make no destructive changes.",
                "If the supplied name is ambiguous, stop and ask for clarification before changing files."
            ]);

    private bool HandleLocalDeactivateCommand(string prompt, string targetName, bool addToHistory, bool clearPromptBox) =>
        HandleLifecycleCommand(
            prompt,
            targetName,
            addToHistory,
            clearPromptBox,
            "deactivate",
            "Use Squad's roster-management workflow to remove this member from the active roster without deleting their knowledge.",
            [
                "If the member is currently active, remove them from the active roster in `.squad/team.md` and from `.squad/routing.md`.",
                "Preserve the existing agent folder, charter, and history.",
                "Set the registry status to `inactive` in `.squad/casting/registry.json`.",
                "If the member is already inactive, report that and make no destructive changes.",
                "If the member is retired, stop and ask whether the user wants them reactivated instead.",
                "If the supplied name is ambiguous, stop and ask for clarification before changing files."
            ]);

    private bool HandleLocalRetireCommand(string prompt, string targetName, bool addToHistory, bool clearPromptBox) =>
        HandleLifecycleCommand(
            prompt,
            targetName,
            addToHistory,
            clearPromptBox,
            "retire",
            "Follow Squad's documented remove-member flow so the member is archived, not silently deleted.",
            [
                "Remove the member from `.squad/team.md` and `.squad/routing.md`.",
                "Move the agent folder to `.squad/agents/_alumni/` if it exists.",
                "Set the registry status to `retired` in `.squad/casting/registry.json` and preserve the name reservation.",
                "Do not hard-delete the member's knowledge unless the user explicitly asks for a permanent delete later.",
                "If the member is already retired, report that and make no destructive changes.",
                "If the supplied name is ambiguous, stop and ask for clarification before changing files."
            ]);

    private bool HandleLifecycleCommand(
        string prompt,
        string targetName,
        bool addToHistory,
        bool clearPromptBox,
        string commandName,
        string summaryInstruction,
        IReadOnlyList<string> guidanceLines) {
        SquadDashTrace.Write("UI", $"Local command intercepted prompt=/{commandName} target={targetName}");

        if (string.IsNullOrWhiteSpace(targetName)) {
            return ExecuteLocalCoordinatorCommand(
                prompt,
                addToHistory,
                clearPromptBox,
                () => _transcriptSink.AppendLine($"Usage: `/{commandName} <agent name>`", null));
        }

        if (addToHistory)
            _conversationManager.AddPromptToHistory(prompt);

        MaybeClearPromptAfterLocalCommand(prompt, clearPromptBox);

        if (_getIsPromptRunning()) {
            return ExecuteLocalCoordinatorCommand(
                prompt,
                addToHistory: false,
                clearPromptBox: false,
                () => _transcriptSink.AppendLine($"Finish the current prompt before trying to /{commandName} an agent.", null),
                refreshLeadAgentBackgroundStatusAfterCompletion: true);
        }

        var lifecyclePrompt = BuildLifecyclePrompt(targetName, summaryInstruction, guidanceLines);
        _ = ExecutePromptAsync(lifecyclePrompt, addToHistory: false, clearPromptBox: false);
        return true;
    }

    private static string BuildLifecyclePrompt(
        string targetName,
        string summaryInstruction,
        IReadOnlyList<string> guidanceLines) {
        var builder = new StringBuilder();
        builder.AppendLine($"Update team member status for \"{targetName}\" in this workspace.");
        builder.AppendLine();
        builder.AppendLine(summaryInstruction);
        builder.AppendLine();
        builder.AppendLine("Keep these files consistent:");
        builder.AppendLine("- `.squad/team.md`");
        builder.AppendLine("- `.squad/routing.md`");
        builder.AppendLine("- `.squad/casting/registry.json`");
        builder.AppendLine("- `.squad/casting/history.json` when the change should be reflected there");
        builder.AppendLine();
        foreach (var line in guidanceLines)
            builder.AppendLine("- " + line);
        return builder.ToString().TrimEnd();
    }

    private static bool TryMatchCommandWithArguments(string trimmedPrompt, string command, out string arguments) {
        if (string.Equals(trimmedPrompt, command, StringComparison.OrdinalIgnoreCase)) {
            arguments = string.Empty;
            return true;
        }

        if (trimmedPrompt.StartsWith(command + " ", StringComparison.OrdinalIgnoreCase)) {
            arguments = trimmedPrompt[command.Length..].Trim();
            return true;
        }

        arguments = string.Empty;
        return false;
    }

    private bool HandleLocalSessionCommand(
        string prompt,
        string arguments,
        bool addToHistory,
        bool clearPromptBox) {
        var normalizedArgs = arguments.Trim();
        if (string.IsNullOrWhiteSpace(normalizedArgs))
            return HandleLocalSessionsCommand(prompt, addToHistory, clearPromptBox);

        if (string.Equals(normalizedArgs, "fresh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedArgs, "new", StringComparison.OrdinalIgnoreCase)) {
            return HandleLocalFreshSessionCommand(prompt, addToHistory, clearPromptBox);
        }

        return ExecuteLocalCoordinatorCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            () => {
                _transcriptSink.AppendLine("## Session", null);
                _transcriptSink.AppendLine("Use `/session fresh` to detach from the current SDK session without clearing the transcript.", null);
                _transcriptSink.AppendLine("Use `/sessions` to list remembered session ids.", null);
                _transcriptSink.AppendLine("Use `/resume <id-prefix>` to point the next prompt back at an older session.", null);
            });
    }

    private bool HandleLocalHelpCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/help");

        return ExecuteLocalCoordinatorCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            () => {
                _transcriptSink.AppendLine("## How it works", null);
                _transcriptSink.AppendLine("Just type what you need — Squad routes your message to the right agent.", null);
                _transcriptSink.AppendLine("- `@AgentName message` — send directly to one agent", null);
                _transcriptSink.AppendLine("- `@Agent1 @Agent2 message` — send to multiple agents in parallel", null);
                _transcriptSink.AppendLine(string.Empty, null);
                _transcriptSink.AppendLine("## Squad Commands", null);
                _transcriptSink.AppendLine("- `/agents` — List all team members", null);
                _transcriptSink.AppendLine("- `/history [N]` — See recent messages (default 10)", null);
                _transcriptSink.AppendLine("- `/nap [--deep]` — Context hygiene (compress, prune, archive)", null);
                _transcriptSink.AppendLine("- `/resume <id>` — Restore a past session by ID prefix", null);
                _transcriptSink.AppendLine("- `/session` — Manage SDK sessions (type `/session` for details)", null);
                _transcriptSink.AppendLine("- `/sessions` — List saved sessions", null);
                _transcriptSink.AppendLine("- `/status` — Check your team & what's happening", null);
                _transcriptSink.AppendLine("- `/version` — Show version", null);
                _transcriptSink.AppendLine(string.Empty, null);
                _transcriptSink.AppendLine("## SquadDash Commands", null);
                _transcriptSink.AppendLine("- `/activate <name>` — Restore an inactive or retired agent to the active roster", null);
                _transcriptSink.AppendLine("- `/agents` — List all team members", null);
                _transcriptSink.AppendLine("- `/clear` — Clear the transcript (with confirmation)", null);
                _transcriptSink.AppendLine("- `/deactivate <name>` — Remove an agent from the active roster without deleting them", null);
                _transcriptSink.AppendLine("- `/doctor` — Run Squad Doctor diagnostics", null);
                _transcriptSink.AppendLine("- `/dropTasks` — Hide the live tasks popup", null);
                _transcriptSink.AppendLine("- `/help` — Show this command reference", null);
                _transcriptSink.AppendLine("- `/hire` — Open the visual hire-agent workflow", null);
                _transcriptSink.AppendLine("- `/model` — Show the active AI model", null);
                _transcriptSink.AppendLine("- `/queue-sim` — Alias for `/test-queue` with the default scenario", null);
                _transcriptSink.AppendLine("- `/test-queue` — Enqueue sim items to exercise queue mechanics without a real AI call", null);
                _transcriptSink.AppendLine("- `/test-queue` DSL: `$QueuePrompt$(\"prompt\", \"response\", delaySecs)` and `$ActiveDraft$(\"prompt\", \"response\", delaySecs)`", null);
                _transcriptSink.AppendLine("- `/retire <name>` — Archive an agent and remove them from the active roster", null);
                _transcriptSink.AppendLine("- `/session` — Manage SDK sessions (type `/session` for details)", null);
                _transcriptSink.AppendLine("- `/sessions` — List saved sessions", null);
                _transcriptSink.AppendLine("- `/status` — Show team, session, and current turn state", null);
                _transcriptSink.AppendLine("- `/tasks` — Show or refresh the live tasks popup", null);
                _transcriptSink.AppendLine("- `/trace` — Open the live trace window", null);
                _transcriptSink.AppendLine("- `/version` — Show Squad and SquadDash version", null);
            });
    }

    private bool HandleLocalTestQueueCommand(string prompt, string body, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/test-queue");

        // ── Precondition guard ────────────────────────────────────────────
        var queueCount  = _promptBoxState.QueueCount;
        var boxText     = _promptBoxState.PromptBoxText.Trim();
        var hasOtherDraftContent =
            !string.IsNullOrWhiteSpace(boxText) &&
            !boxText.StartsWith("/test-queue", StringComparison.OrdinalIgnoreCase) &&
            !boxText.StartsWith("/queue-sim",  StringComparison.OrdinalIgnoreCase);

        if (queueCount > 0 || hasOtherDraftContent) {
            return ExecuteLocalCoordinatorCommand(
                prompt,
                addToHistory,
                clearPromptBox,
                () => _transcriptSink.AppendLine("[Queue test] Cannot start: queue must be empty and prompt box must be blank.", null));
        }

        // ── Parse DSL (or use default scenario) ───────────────────────────
        var entries = string.IsNullOrWhiteSpace(body)
            ? BuildDefaultTestQueueScenario()
            : ParseTestQueueDsl(body);

        if (entries.Count == 0) {
            return ExecuteLocalCoordinatorCommand(
                prompt,
                addToHistory,
                clearPromptBox,
                () => _transcriptSink.AppendLine("[Queue test] No valid `$QueuePrompt$` or `$ActiveDraft$` directives found in the body.", null));
        }

        var queueEntries    = entries.Where(e => !e.IsActiveDraft).ToList();
        var activeDraftEntry = entries.FirstOrDefault(e => e.IsActiveDraft);

        // Enqueue $QueuePrompt$ items in DSL order (first = rightmost = drains first).
        int seqBase = 1;
        foreach (var entry in queueEntries) {
            var item = new PromptQueueItem {
                Text            = entry.PromptText,
                SequenceNumber  = seqBase++,
                IsSimEntry      = true,
                SimResponse     = entry.FakeResponse,
                SimDelaySeconds = entry.DelaySeconds,
            };
            _promptBoxState.EnqueueSimItem(item);
        }

        // Set the active-draft box text and stash sim metadata for when it is submitted.
        if (activeDraftEntry is not null) {
            _promptBoxState.SetPromptBoxText(activeDraftEntry.PromptText);
            _activeDraftSimEntry = (activeDraftEntry.FakeResponse, activeDraftEntry.DelaySeconds);
        }

        int totalItems = queueEntries.Count + (activeDraftEntry is not null ? 1 : 0);
        return ExecuteLocalCoordinatorCommand(
            prompt,
            addToHistory,
            clearPromptBox: activeDraftEntry is null && clearPromptBox,
            () => {
                _transcriptSink.AppendLine("## Queue Test Started", null);
                _transcriptSink.AppendLine($"Enqueued **{queueEntries.Count}** sim item(s){(activeDraftEntry is not null ? " and set the active draft" : "")} — no real AI calls will be made.", null);
                _transcriptSink.AppendLine("Each item uses its configured delay and fake response. Try **pause/resume** or **Ctrl+Enter** to prioritize.", null);
            });
    }

    // ── DSL types and parsing ─────────────────────────────────────────────

    private record TestQueueEntry(string PromptText, string FakeResponse, int DelaySeconds, bool IsActiveDraft);

    private static IReadOnlyList<TestQueueEntry> ParseTestQueueDsl(string body) {
        var results = new List<TestQueueEntry>();
        foreach (var rawLine in body.Split('\n')) {
            var line = rawLine.Trim().TrimEnd(';').Trim();
            if (line.Length == 0) continue;

            bool isActiveDraft;
            if (line.StartsWith("$QueuePrompt$", StringComparison.OrdinalIgnoreCase))
                isActiveDraft = false;
            else if (line.StartsWith("$ActiveDraft$", StringComparison.OrdinalIgnoreCase))
                isActiveDraft = true;
            else
                continue;

            var parenStart = line.IndexOf('(');
            var parenEnd   = line.LastIndexOf(')');
            if (parenStart < 0 || parenEnd <= parenStart) continue;

            var argSpan = line[(parenStart + 1)..parenEnd];
            if (!TryParseSimDslArgs(argSpan, out var promptText, out var fakeResponse, out var delaySecs))
                continue;

            results.Add(new TestQueueEntry(promptText, fakeResponse, delaySecs, isActiveDraft));
        }
        return results;
    }

    private static bool TryParseSimDslArgs(
        string argSpan,
        out string promptText,
        out string fakeResponse,
        out int    delaySecs) {
        promptText   = string.Empty;
        fakeResponse = string.Empty;
        delaySecs    = 1;

        int pos = 0;
        if (!TryReadQuotedString(argSpan, ref pos, out promptText))  return false;
        SkipCommaAndWhitespace(argSpan, ref pos);
        if (!TryReadQuotedString(argSpan, ref pos, out fakeResponse)) return false;
        SkipCommaAndWhitespace(argSpan, ref pos);
        if (!TryReadInt(argSpan, ref pos, out delaySecs))             return false;
        return true;
    }

    private static bool TryReadQuotedString(string src, ref int pos, out string value) {
        value = string.Empty;
        while (pos < src.Length && char.IsWhiteSpace(src[pos])) pos++;
        if (pos >= src.Length || src[pos] != '"') return false;
        pos++; // consume opening quote
        var sb = new System.Text.StringBuilder();
        while (pos < src.Length && src[pos] != '"')
            sb.Append(src[pos++]);
        if (pos < src.Length) pos++; // consume closing quote
        value = sb.ToString();
        return true;
    }

    private static bool TryReadInt(string src, ref int pos, out int value) {
        value = 1;
        while (pos < src.Length && char.IsWhiteSpace(src[pos])) pos++;
        int start = pos;
        while (pos < src.Length && char.IsDigit(src[pos])) pos++;
        return pos > start && int.TryParse(src[start..pos], out value);
    }

    private static void SkipCommaAndWhitespace(string src, ref int pos) {
        while (pos < src.Length && char.IsWhiteSpace(src[pos])) pos++;
        if (pos < src.Length && src[pos] == ',') pos++;
        while (pos < src.Length && char.IsWhiteSpace(src[pos])) pos++;
    }

    private static IReadOnlyList<TestQueueEntry> BuildDefaultTestQueueScenario() =>
        new[] {
            new TestQueueEntry("Queue test item 1 of 8 — pause window",
                "✅ [Queue sim] Item 1 — fake response. (4-second delay gives time to test the pause button)",
                4, IsActiveDraft: false),
            new TestQueueEntry("Queue test item 2 of 8", "✅ [Queue sim] Item 2 — fake response.",  2, IsActiveDraft: false),
            new TestQueueEntry("Queue test item 3 of 8", "✅ [Queue sim] Item 3 — fake response.",  2, IsActiveDraft: false),
            new TestQueueEntry("Queue test item 4 of 8 — quick replies",
                "✅ [Queue sim] Item 4 — this response includes quick reply buttons.\n\nQUICK_REPLIES_JSON:\n[\n  { \"label\": \"Continue queue\", \"routeMode\": \"sim\", \"reason\": \"Queue sim test button — no AI dispatch.\" },\n  { \"label\": \"Stop here\", \"routeMode\": \"sim\", \"reason\": \"Queue sim test button — no AI dispatch.\" }\n]",
                2, IsActiveDraft: false),
            new TestQueueEntry("Queue test item 5 of 8", "✅ [Queue sim] Item 5 — fake response.",  1, IsActiveDraft: false),
            new TestQueueEntry("Queue test item 6 of 8", "✅ [Queue sim] Item 6 — fake response.",  1, IsActiveDraft: false),
            new TestQueueEntry("Queue test item 7 of 8", "✅ [Queue sim] Item 7 — fake response.",  1, IsActiveDraft: false),
            new TestQueueEntry("Queue test item 8 of 8 (draft)", "✅ [Queue sim] Draft item — fake response.", 1, IsActiveDraft: true),
        };

    /// <summary>
    /// Like <see cref="TryMatchCommandWithArguments"/> but also accepts a newline as the
    /// separator between the command name and its body, enabling multi-line DSL bodies.
    /// </summary>
    private static bool TryMatchCommandWithBody(string trimmedPrompt, string command, out string body) {
        if (string.Equals(trimmedPrompt, command, StringComparison.OrdinalIgnoreCase)) {
            body = string.Empty;
            return true;
        }
        if (trimmedPrompt.StartsWith(command, StringComparison.OrdinalIgnoreCase) &&
            trimmedPrompt.Length > command.Length) {
            var ch = trimmedPrompt[command.Length];
            if (ch == ' ' || ch == '\n' || ch == '\r') {
                body = trimmedPrompt[command.Length..].TrimStart();
                return true;
            }
        }
        body = string.Empty;
        return false;
    }

    private bool HandleLocalStatusCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/status");

        return ExecuteLocalCoordinatorCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            () => {
                var workspace = _workspaceContext.GetCurrentWorkspace()?.FolderPath ?? "*(none)*";
                var knownSessionIds = _conversationManager.GetKnownSessionIds();
                _transcriptSink.AppendLine($"## {Path.GetFileName(_workspaceContext.GetCurrentWorkspace()?.FolderPath ?? "Squad Status")}", null);
                _transcriptSink.AppendLine($"- **Workspace:** `{workspace}`", null);
                _transcriptSink.AppendLine($"- **Session:** {(_conversationManager.CurrentSessionId is not null ? $"`{_conversationManager.CurrentSessionId}`" : "*(not started)*")}", null);
                _transcriptSink.AppendLine($"- **State:** {_agentRosterView.CurrentSessionState ?? "Unknown"}", null);
                if (knownSessionIds.Count > 0)
                    _transcriptSink.AppendLine($"- **Remembered Sessions:** {string.Join(", ", knownSessionIds.Select(FormatSessionListEntry))}", null);
                if (!string.IsNullOrWhiteSpace(LatestSessionDiagnosticsSummary))
                    _transcriptSink.AppendLine($"- **Session Forensics:** {LatestSessionDiagnosticsSummary}", null);
                if (!string.IsNullOrWhiteSpace(_squadCliAdapter.LastObservedModel)) {
                    var modelLabel = _getModelObservedThisSession() ? "Model" : "Model (last session)";
                    _transcriptSink.AppendLine($"- **{modelLabel}:** {_squadCliAdapter.LastObservedModel}", null);
                }

                var currentTurnLine = CurrentTurnStatusPresentation.BuildReportLine(_backgroundTaskPresenter.BuildCurrentTurnStatusSnapshot());
                if (!string.IsNullOrWhiteSpace(currentTurnLine)) {
                    _transcriptSink.AppendLine(string.Empty, null);
                    _transcriptSink.AppendLine("## Current Turn", null);
                    _transcriptSink.AppendLine("- " + currentTurnLine, null);
                }

                var agents = _agentRosterView.GetAgents();
                var teamCards = agents.Where(c => !c.IsLeadAgent).ToList();
                if (teamCards.Count > 0) {
                    _transcriptSink.AppendLine(string.Empty, null);
                    _transcriptSink.AppendLine("## Team", null);
                    foreach (var card in teamCards) {
                        var status = string.IsNullOrWhiteSpace(card.StatusText) ? "Idle" : card.StatusText;
                        var detail = string.IsNullOrWhiteSpace(card.DetailText) ? string.Empty : $" — {card.DetailText}";
                        var roleLabel = string.IsNullOrWhiteSpace(card.RoleText) ? string.Empty : $" ({card.RoleText})";
                        _transcriptSink.AppendLine($"- **{card.Name}**{roleLabel}: {status}{detail}", null);
                    }
                }

                if (_backgroundTaskPresenter.HasBackgroundTasks()) {
                    _transcriptSink.AppendLine(string.Empty, null);
                    _transcriptSink.AppendLine("## Background Tasks", null);
                    _transcriptSink.AppendLine(_backgroundTaskPresenter.BuildBackgroundTaskReport(), null);
                }
            });
    }

    private bool HandleLocalSessionsCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/sessions");

        return ExecuteLocalCoordinatorCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            () => {
                var knownSessionIds = _conversationManager.GetKnownSessionIds();
                _transcriptSink.AppendLine("## Sessions", null);
                _transcriptSink.AppendLine(
                    $"Current: {(_conversationManager.CurrentSessionId is null ? "*(fresh / not started)*" : $"`{_conversationManager.CurrentSessionId}`")}",
                    null);

                if (knownSessionIds.Count == 0) {
                    _transcriptSink.AppendLine("Remembered: *(none yet)*", null);
                }
                else {
                    _transcriptSink.AppendLine("Remembered:", null);
                    foreach (var sessionId in knownSessionIds)
                        _transcriptSink.AppendLine($"- {FormatSessionListEntry(sessionId)}", null);
                }

                _transcriptSink.AppendLine(string.Empty, null);
                _transcriptSink.AppendLine("Use `/session fresh` to detach from the current SDK session without clearing the transcript.", null);
                _transcriptSink.AppendLine("Use `/resume <id-prefix>` to point the next prompt back at one of the remembered sessions.", null);
            });
    }

    private bool HandleLocalFreshSessionCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", $"Local command intercepted prompt=/session fresh current={_conversationManager.CurrentSessionId ?? "(none)"}");

        return ExecuteSessionMutationCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            () => {
                var previousSessionId = _conversationManager.StartFreshSessionPreservingTranscript();
                _updateLeadAgent("Ready", string.Empty, "Ready to start a fresh SDK session.");
                _updateSessionState("Ready");
                _refreshAgentCards();
                _refreshSidebar();

                _transcriptSink.AppendLine("## Session", null);
                if (previousSessionId is null) {
                    _transcriptSink.AppendLine("There was no active SDK session to detach. The next prompt will start fresh.", null);
                    return;
                }

                _transcriptSink.AppendLine($"Detached from `{previousSessionId}`.", null);
                _transcriptSink.AppendLine("The transcript stayed in place. The next prompt will create a brand-new SDK session.", null);
                _transcriptSink.AppendLine($"To return later, run `/resume {TranscriptConversationManager.AbbreviateSessionId(previousSessionId)}`.", null);
            });
    }

    private bool HandleLocalResumeCommand(string prompt, string arguments, bool addToHistory, bool clearPromptBox) {
        var normalizedArgs = arguments.Trim();
        SquadDashTrace.Write("UI", $"Local command intercepted prompt=/resume args={normalizedArgs}");

        return ExecuteSessionMutationCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            () => {
                var result = _conversationManager.TryResumeSession(normalizedArgs);
                _transcriptSink.AppendLine("## Session", null);
                if (!result.Succeeded) {
                    _transcriptSink.AppendLine(result.ErrorMessage ?? "Unable to resume that session.", ThemeBrush("SystemErrorText"));
                    return;
                }

                _updateLeadAgent("Ready", string.Empty, "Ready to continue with the selected SDK session.");
                _updateSessionState("Ready");
                _refreshAgentCards();
                _refreshSidebar();
                _transcriptSink.AppendLine($"Next prompt will use `{result.SelectedSessionId}`.", null);
                _transcriptSink.AppendLine("The transcript was left as-is so you can keep investigating this workspace.", null);
            });
    }

    private bool HandleLocalAgentsCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/agents");

        return ExecuteLocalCoordinatorCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            () => {
                var agents = _agentRosterView.GetAgents();
                _transcriptSink.AppendLine("## Team Members", null);

                // Output coordinator first
                var leadAgent = agents.FirstOrDefault(c => c.IsLeadAgent);
                if (leadAgent is not null)
                    _transcriptSink.AppendLine($"- **{leadAgent.Name}** *(coordinator)*", null);

                // Output permanent team members
                foreach (var card in agents.Where(c => !c.IsLeadAgent && !c.IsDynamicAgent)) {
                    var roleLabel = string.IsNullOrWhiteSpace(card.RoleText) ? string.Empty : $" — {card.RoleText}";
                    _transcriptSink.AppendLine($"- **{card.Name}**{roleLabel}", null);
                }

                // Output temporary agents if any exist
                var dynamicAgents = agents.Where(c => c.IsDynamicAgent).ToList();
                if (dynamicAgents.Count > 0) {
                    _transcriptSink.AppendLine(string.Empty, null);
                    _transcriptSink.AppendLine("**Temporary Agents:**", null);
                    foreach (var card in dynamicAgents) {
                        var roleLabel = string.IsNullOrWhiteSpace(card.RoleText) ? string.Empty : $" — {card.RoleText}";
                        _transcriptSink.AppendLine($"- **{card.Name}**{roleLabel}", null);
                    }
                }
            });
    }

    private bool HandleLocalVersionCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/version");

        return ExecuteLocalCoordinatorCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            () => {
                var appVersion = System.Reflection.Assembly.GetExecutingAssembly()
                    .GetName().Version?.ToString() ?? "unknown";
                _transcriptSink.AppendLine("## Version", null);
                _transcriptSink.AppendLine($"- **SquadDash:** {appVersion}", null);
                if (!string.IsNullOrWhiteSpace(_squadCliAdapter.SquadVersion))
                    _transcriptSink.AppendLine($"- **Squad CLI:** {_squadCliAdapter.SquadVersion}", null);
                else
                    _transcriptSink.AppendLine("- **Squad CLI:** *(not resolved)*", null);
                if (!string.IsNullOrWhiteSpace(_squadCliAdapter.LastObservedModel))
                    _transcriptSink.AppendLine($"- **Model:** {_squadCliAdapter.LastObservedModel}", null);
            });
    }

    private bool HandleLocalTestTableCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/testtable");

        return ExecuteLocalCoordinatorCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            () => {
                _transcriptSink.AppendLine("This is text BEFORE the table. Select from here, drag through the table, and include the line below it, then copy and paste.", null);
                _transcriptSink.AppendLine(string.Empty, null);
                _transcriptSink.AppendLine("| Animal | Speed | Habitat |", null);
                _transcriptSink.AppendLine("| --- | --- | --- |", null);
                _transcriptSink.AppendLine("| Cheetah | 120 km/h | Savanna |", null);
                _transcriptSink.AppendLine("| Peregrine Falcon | 390 km/h | Cliffs |", null);
                _transcriptSink.AppendLine("| Sailfish | 110 km/h | Ocean |", null);
                _transcriptSink.AppendLine(string.Empty, null);
                _transcriptSink.AppendLine("This is text AFTER the table. If the table rows appear between these two lines when you paste, the fix is working.", null);
            });
    }

    private bool HandleLocalClearCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/clear");

        var result = MessageBox.Show(
            "Are you sure you want to clear the transcript?",
            "Clear Transcript",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
            return HandleClearConfirmed(prompt, clearPromptBox);
        else
            return true;
    }

    private bool HandleClearConfirmed(string prompt, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Clear transcript confirmed by user");

        if (clearPromptBox)
            _promptBoxState.ClearPromptTextBox();

        var workspace = _workspaceContext.GetCurrentWorkspace();
        if (workspace is not null) {
            _conversationManager.ConversationStore.Save(
                workspace.FolderPath,
                WorkspaceConversationState.Empty with {
                    ClearedAt = DateTimeOffset.UtcNow
                });
        }

        _clearSessionView();
        return true;
    }

    private void MaybeClearPromptAfterLocalCommand(string prompt, bool clearPromptBox) {
        if (!clearPromptBox)
            return;

        if (LocalPromptSubmissionPolicy.ShouldRetainPromptAfterLocalSubmit(prompt, _getIsPromptRunning())) {
            _updateInteractiveControlState();
            return;
        }

        _promptBoxState.ClearPromptTextBox();
    }

    private bool ExecuteLocalUiCommand(
        string prompt,
        bool addToHistory,
        bool clearPromptBox,
        Action action) {
        if (addToHistory)
            _conversationManager.AddPromptToHistory(prompt);

        MaybeClearPromptAfterLocalCommand(prompt, clearPromptBox);
        action();

        if (!_getIsClosing() && _promptBoxState.IsPromptTextBoxEnabled)
            _promptBoxState.FocusPromptTextBox();

        return true;
    }

    private bool ExecuteLocalCoordinatorCommand(
        string prompt,
        bool addToHistory,
        bool clearPromptBox,
        Action renderResponse,
        bool refreshLeadAgentBackgroundStatusAfterCompletion = false) {
        if (addToHistory)
            _conversationManager.AddPromptToHistory(prompt);

        var executionPlan = LocalCommandTurnExecutionPolicy.Create(_getIsPromptRunning(), _transcriptSink.CoordinatorThread.CurrentTurn);
        _transcriptSink.SelectTranscriptThread(_transcriptSink.CoordinatorThread);
        var localTurn = _transcriptSink.BeginTranscriptTurn(prompt);

        try {
            MaybeClearPromptAfterLocalCommand(prompt, clearPromptBox);
            renderResponse();
            _transcriptSink.FinalizeCurrentTurnResponse();
            _conversationManager.SaveTranscriptTurnToConversation(localTurn, DateTimeOffset.Now, "local-command-execute");
        }
        finally {
            if (executionPlan.ShouldRestoreSuspendedTurn &&
                executionPlan.SuspendedTurn is { } suspendedTurn) {
                SquadDashTrace.Write("Persistence", "Coordinator CurrentTurn restored reason=local-command-suspended-turn");
                _transcriptSink.CoordinatorThread.CurrentTurn = suspendedTurn;
                MoveTranscriptTurnToDocumentEnd(suspendedTurn);
            }
            else {
                SquadDashTrace.Write("Persistence", "Coordinator CurrentTurn cleared reason=local-command-execute");
                _transcriptSink.CoordinatorThread.CurrentTurn = null;
            }
        }

        if (refreshLeadAgentBackgroundStatusAfterCompletion &&
            executionPlan.ShouldRefreshLeadStatusAfterCompletion) {
            _backgroundTaskPresenter.RefreshLeadAgentBackgroundStatus();
        }

        return true;
    }

    private bool ExecuteSessionMutationCommand(
        string prompt,
        bool addToHistory,
        bool clearPromptBox,
        Action mutateSession) {
        if (_getIsPromptRunning()) {
            return ExecuteLocalCoordinatorCommand(
                prompt,
                addToHistory,
                clearPromptBox,
                () => {
                    _transcriptSink.AppendLine("## Session", null);
                    _transcriptSink.AppendLine("Wait for the current prompt to finish before switching SDK sessions.", ThemeBrush("SystemWarningText"));
                },
                refreshLeadAgentBackgroundStatusAfterCompletion: true);
        }

        return ExecuteLocalCoordinatorCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            mutateSession,
            refreshLeadAgentBackgroundStatusAfterCompletion: true);
    }

    private void MoveTranscriptTurnToDocumentEnd(TranscriptTurnView turn) {
        var document = turn.OwnerThread.Document;
        var blocks = turn.TopLevelBlocks
            .Where(block => ReferenceEquals(block.Parent, document))
            .ToArray();
        if (blocks.Length == 0)
            return;

        foreach (var block in blocks)
            document.Blocks.Remove(block);

        foreach (var block in blocks)
            document.Blocks.Add(block);

        _transcriptSink.EnsureThreadFooterAtEnd(turn.OwnerThread);
    }

    private string FormatSessionListEntry(string sessionId) {
        var abbreviated = TranscriptConversationManager.AbbreviateSessionId(sessionId);
        return string.Equals(sessionId, _conversationManager.CurrentSessionId, StringComparison.OrdinalIgnoreCase)
            ? $"`{sessionId}` *(current; {abbreviated})*"
            : $"`{sessionId}` *({abbreviated})*";
    }

    internal void InjectUniverseSelectorTurn() {
        _universeSelectionPending = true;
        _transcriptSink.SelectTranscriptThread(_transcriptSink.CoordinatorThread);
        _transcriptSink.BeginTranscriptTurn(string.Empty);
        _transcriptSink.AppendLine("Ready to create a team? Select a universe:\n\n" +
            string.Join(" ", MainWindow.UniverseSelectorOptions.Select(u => $"[{u}]")), null);
        _transcriptSink.FinalizeCurrentTurnResponse();
        _conversationManager.SaveCurrentTurnToConversation(DateTimeOffset.Now, "named-agent-command-turn");
        SquadDashTrace.Write("Persistence", "Coordinator CurrentTurn cleared reason=universe-selector-turn");
        _transcriptSink.CoordinatorThread.CurrentTurn = null;
        _transcriptSink.ScrollToEndIfAtBottom();
    }

    private bool HandleUniverseSelection(string universeName) {
        _universeSelectionPending = false;

        var llmPrompt = string.Equals(universeName, SquadInstallerService.SquadDashUniverseName, StringComparison.OrdinalIgnoreCase)
            ? $"Create a team from the {SquadInstallerService.SquadDashUniverseName}. Before selecting members, read `.squad/universes/squaddash.md` — it describes each character's best-fit work, areas to avoid, and related roles. Choose the best fit for this project."
            : $"Create a suitable team from the {universeName} universe.";

        _ = ExecutePromptAsync(llmPrompt, addToHistory: false, clearPromptBox: false);
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Core Prompt Execution
    // ─────────────────────────────────────────────────────────────────────

    internal async Task ExecutePromptAsync(
        string prompt,
        bool addToHistory,
        bool clearPromptBox,
        string? sessionIdOverride = null,
        string? displayPrompt = null) {
        await ExecuteCoordinatorTurnAsync(
            displayPrompt ?? prompt,
            addToHistory,
            clearPromptBox,
            allowLocalCommandHandling: true,
            async (workspace, configDirectory) => {
                var bridgePrompt = BuildBridgePrompt(prompt);
                var sessionId = sessionIdOverride ?? _conversationManager.CurrentSessionId;
                SavePromptDispatchDiagnostic(
                    workspace,
                    sessionId,
                    configDirectory,
                    CurrentDispatchedItem,
                    PendingQueueItemCount,
                    displayPrompt ?? prompt,
                    bridgePrompt);
                await _runPromptAsync(
                    bridgePrompt,
                    workspace.FolderPath,
                    sessionId,
                    configDirectory);
            });
    }

    internal async Task ExecuteNamedAgentDelegationAsync(
        string selectedOption,
        string targetAgentHandle,
        bool addToHistory,
        bool clearPromptBox) {
        if (string.IsNullOrWhiteSpace(targetAgentHandle))
            throw new ArgumentException("Target agent handle cannot be empty.", nameof(targetAgentHandle));

        await ExecuteCoordinatorTurnAsync(
            selectedOption,
            addToHistory,
            clearPromptBox,
            allowLocalCommandHandling: false,
            async (workspace, configDirectory) => {
                var currentSessionId = _conversationManager.CurrentSessionId;
                if (string.IsNullOrWhiteSpace(currentSessionId))
                    throw new InvalidOperationException(
                        "Named-agent quick replies require the active coordinator session so Squad can delegate in-place.");

                await _runNamedAgentDelegationAsync(
                    selectedOption,
                    targetAgentHandle,
                    workspace.FolderPath,
                    currentSessionId,
                    configDirectory);
            });
    }

    internal async Task ExecuteNamedAgentDirectAsync(
        string targetAgentHandle,
        string selectedOption,
        string? handoffContext,
        bool addToHistory,
        bool clearPromptBox) {
        if (string.IsNullOrWhiteSpace(targetAgentHandle))
            throw new ArgumentException("Target agent handle cannot be empty.", nameof(targetAgentHandle));

        await ExecuteCoordinatorTurnAsync(
            selectedOption,
            addToHistory,
            clearPromptBox,
            allowLocalCommandHandling: false,
            async (workspace, configDirectory) => {
                var currentSessionId = _conversationManager.CurrentSessionId;
                await _runNamedAgentDirectAsync(
                    targetAgentHandle,
                    selectedOption,
                    handoffContext,
                    workspace.FolderPath,
                    currentSessionId,
                    configDirectory);
            });
    }

    /// <summary>
    /// Runs a code health task as a named-agent direct turn in the target agent's own thread.
    /// Does NOT write anything to the coordinator transcript — the coordinator only receives
    /// the lightweight stub that MainWindow's <c>onTaskCompleted</c> callback appends after
    /// this method returns.
    /// </summary>
    internal async Task ExecuteCodeHealthTurnAsync(
        string targetAgentHandle,
        string prompt) {
        var workspace = _workspaceContext.GetCurrentWorkspace();
        if (workspace is null) return;

        _setIsPromptRunning(true);
        _updateInteractiveControlState();
        ActiveToolName = null;

        try {
            var configDirectory = _conversationManager.ConversationStore
                .GetSessionConfigDirectory(workspace.FolderPath);
            var currentSessionId = _conversationManager.CurrentSessionId;

            await _runNamedAgentDirectAsync(
                targetAgentHandle,
                prompt,
                null,
                workspace.FolderPath,
                currentSessionId,
                configDirectory);

            _clearRuntimeIssue();
            _refreshAgentCards();
            _refreshSidebar();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) {
            SquadDashTrace.Write("UI", $"ExecuteCodeHealthTurnAsync failed: {ex}");
        }
        finally {
            await _waitForPostedUiActionsAsync();
            _transcriptSink.CoordinatorThread.CurrentTurn = null;
            _setIsPromptRunning(false);
            _updateInteractiveControlState();
            if (_promptBoxState.IsPromptTextBoxEnabled)
                _promptBoxState.FocusPromptTextBox();
        }
    }

    private async Task ExecuteCoordinatorTurnAsync(
        string visiblePrompt,
        bool addToHistory,
        bool clearPromptBox,
        bool allowLocalCommandHandling,
        Func<SessionWorkspace, string?, Task> runBridgeTurnAsync) {
        if (_workspaceContext.GetCurrentWorkspace() is null)
            return;

        if (allowLocalCommandHandling && TryHandleLocalCommand(visiblePrompt, addToHistory, clearPromptBox))
            return;

        DisableTranscriptQuickReplies(_transcriptSink.CoordinatorThread);

        var workspace = _workspaceContext.GetCurrentWorkspace()!;
        SquadDashTrace.Write(
            "UI",
            $"ExecutePromptAsync start prompt={visiblePrompt} cwd={workspace.FolderPath}");

        _setIsPromptRunning(true);
        _conversationManager.ResetHistoryNavigation();
        _updateInteractiveControlState();

        if (addToHistory) {
            // Strip attachment XML preamble before storing history: the attachments are
            // already stored separately in PromptHistoryEntry.Attachments and re-applied
            // on Ctrl+Up navigation, so the history text should be just the user's typed message.
            var historyText = visiblePrompt;
            var bodyStart = AttachmentBlockFormatter.StripTypedAttachmentHeaders(visiblePrompt);
            if (bodyStart >= 0) historyText = visiblePrompt[bodyStart..];
            _conversationManager.AddPromptToHistory(historyText, _getSubmittedAttachments());
        }

        _transcriptSink.SelectTranscriptThread(_transcriptSink.CoordinatorThread);
        _transcriptSink.BeginTranscriptTurn(visiblePrompt);

        if (clearPromptBox)
            _promptBoxState.ClearPromptTextBox();

        _currentPromptContextDiagnostics = _conversationManager.CapturePromptContextDiagnostics();
        SquadDashTrace.Write(
            "PromptHealth",
            $"Turn context visiblePromptChars={visiblePrompt.Length} {PromptContextDiagnosticsPresentation.BuildTraceSummary(_currentPromptContextDiagnostics, DateTimeOffset.UtcNow)}");
        ActiveToolName = null;
        _updateLeadAgent("Thinking", string.Empty, "Coordinating the current turn.");
        _updateSessionState("Running");
        StartPromptHealthMonitoring();

        try {
            var configDirectory = _conversationManager.ConversationStore.GetSessionConfigDirectory(workspace.FolderPath);
            var settings = _workspaceContext.GetSettingsSnapshot();
            if (settings.GetRuntimeIssueSimulation(workspace.FolderPath) != DeveloperRuntimeIssueSimulation.None) {
                throw new InvalidOperationException(
                    WorkspaceIssueFactory.CreateSimulatedRuntimeErrorMessage(
                        settings.GetRuntimeIssueSimulation(workspace.FolderPath)));
            }

            if (CurrentDispatchedItem?.IsSimEntry == true) {
                var simItem = CurrentDispatchedItem;
                _updateLeadAgent("Thinking", string.Empty, "Simulating AI response…");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, simItem.SimDelaySeconds)));
                _transcriptSink.AppendLine(simItem.SimResponse ?? $"[Queue sim] Processed \"{visiblePrompt}\"", null);
                _transcriptSink.FinalizeCurrentTurnResponse();
                _clearRuntimeIssue();
            }
            else if (_activeDraftSimEntry is { } draftSim) {
                _activeDraftSimEntry = null;
                _updateLeadAgent("Thinking", string.Empty, "Simulating AI response…");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, draftSim.SimDelaySeconds)));
                _transcriptSink.AppendLine(draftSim.SimResponse, null);
                _transcriptSink.FinalizeCurrentTurnResponse();
                _clearRuntimeIssue();
            }
            else {
                await runBridgeTurnAsync(workspace, configDirectory);
                _clearRuntimeIssue();
                _refreshAgentCards();
                _refreshSidebar();
            }
            SquadDashTrace.Write("UI", "ExecutePromptAsync completed successfully.");
        }
        catch (OperationCanceledException ex) {
            if (IsUserRequestedAbort(ex)) {
                SquadDashTrace.Write("UI", $"ExecutePromptAsync aborted by user: {ex.GetType().Name}: {ex.Message}");
                StopPromptHealthMonitoring("aborted");
                MarkActiveToolsAsFailed("Aborted");
                _transcriptSink.AppendLine("[aborted]", ThemeBrush("SystemInfoText"));
                _backgroundTaskPresenter.RefreshLeadAgentBackgroundStatus();
                _nextPromptShouldRetractAborted = true;
            }
            else {
                var message = string.IsNullOrWhiteSpace(ex.Message)
                    ? "The prompt was interrupted before it completed."
                    : ex.Message;
                SquadDashTrace.Write("UI", $"ExecutePromptAsync interrupted: {ex.GetType().Name}: {message}");
                StopPromptHealthMonitoring("interrupted");
                MarkActiveToolsAsFailed("Interrupted");
                _transcriptSink.AppendLine(
                    $"[interrupted] {message}\n\n[{ResendLastPromptQuickReply}] [{CheckGitDiffQuickReply}]",
                    ThemeBrush("SystemInfoText"));
                _backgroundTaskPresenter.RefreshLeadAgentBackgroundStatus();
            }
        }
        catch (Exception ex) {
            SquadDashTrace.Write("UI", $"ExecutePromptAsync failed: {ex}");
            StopPromptHealthMonitoring("error");
            var issue = _showRuntimeIssue(ex.Message);
            MarkActiveToolsAsFailed(issue.Message);
            _transcriptSink.AppendLine("[error] " + issue.Message, Brushes.Red);
            _updateLeadAgent("Error", string.Empty, "The last prompt ended with an error.");
            _updateSessionState("Error");
            _setInstallStatus(issue.Message);

            if (_canShowOwnedWindow()) {
                _showTextWindow(
                    issue.HelpWindowTitle ?? "Squad Runtime Diagnostics",
                    issue.HelpWindowContent ?? BuildRuntimeDiagnostics(ex.Message, workspace.FolderPath));
            }
        }
        finally {
            // Bridge events are posted onto the UI dispatcher. Wait for any already-posted
            // callbacks to run before persisting and clearing the live turn so tail response
            // chunks are not dropped after the bridge reports completion.
            await _waitForPostedUiActionsAsync();

            if (_currentPromptStartedAt is not null)
                StopPromptHealthMonitoring("completed");

            _conversationManager.SaveCurrentTurnToConversation(DateTimeOffset.UtcNow, "execute-coordinator-finally");
            SquadDashTrace.Write("Persistence", "Coordinator CurrentTurn cleared reason=execute-coordinator-finally");
            _transcriptSink.CoordinatorThread.CurrentTurn = null;
            _setIsPromptRunning(false);
            _updateInteractiveControlState();
            var shouldRecheckRoutingRepair = _getPendingRoutingRepairRecheck();
            _setPendingRoutingRepairRecheck(false);
            _clearPendingSupplementalInstruction();

            if (shouldRecheckRoutingRepair) {
                await _waitForRoutingRepairSettleAsync();
                _refreshAgentCards();
                _refreshSidebar();
                _maybePublishRoutingIssue("repair-recheck", true);
            }
            else {
                _maybePublishRoutingIssue("prompt-finished", false);
            }

            if (_getRestartPending() && !_getIsClosing()) {
                _close();
            }
            else if (!_getIsClosing()) {
                if (_promptBoxState.IsPromptTextBoxEnabled)
                    _promptBoxState.FocusPromptTextBox();
            }
        }
    }

    private void DisableTranscriptQuickReplies(TranscriptThreadState thread) {
        var lastEntry = _transcriptSink.LastQuickReplyEntry;
        if (lastEntry is not null) {
            if (lastEntry.AllowQuickReplies)
                DisableQuickReplies(lastEntry);
            _transcriptSink.ClearLastQuickReplyEntry();
        }

        if (thread.CurrentTurn is null)
            return;

        foreach (var entry in thread.CurrentTurn.ResponseEntries.Where(candidate => candidate.AllowQuickReplies))
            DisableQuickReplies(entry);
    }

    private static bool IsUserRequestedAbort(OperationCanceledException ex) =>
        string.Equals(ex.Message, "Prompt aborted by user.", StringComparison.Ordinal);

    /// <summary>
    /// Disables quick-reply buttons on a response entry and re-renders it.
    /// Called internally and from MainWindow's QuickReplyButton_Click.
    /// </summary>
    internal void DisableQuickReplies(TranscriptResponseEntry entry) {
        if (!entry.AllowQuickReplies)
            return;

        entry.AllowQuickReplies = false;
        _transcriptSink.RenderResponseEntry(entry);
    }

    private string BuildBridgePrompt(string prompt) {
        var pending   = _getPendingSupplementalInstruction();
        var docsCtx   = DocumentationModeActive ? BuildDocumentationContextInstruction() : null;
        var tasksCtx  = BuildTasksContextInstruction();
        var queueCtx      = PendingQueueItemCount > 0 ? BuildQueueContextInstruction(PendingQueueItemCount) : null;
        var questionCtx   = PendingQueueItemCount > 0 && prompt.Contains('?') ? BuildQueuedQuestionInboxHint() : null;

        if (_nextPromptShouldRetractAborted) {
            _nextPromptShouldRetractAborted = false;
            SquadDashTrace.Write("UI", "Injecting abort retraction prefix for next prompt.");
            prompt =
                "[System note: The previous user message in this conversation was cancelled mid-stream by the user. " +
                "Disregard it entirely — do not act on it, refer to it, or treat it as a pending task. " +
                "The user's actual request follows below.]\n\n" +
                prompt;
        }

        var triggeredCtx = BuildTriggeredInjections(prompt);

        var hostCmdCtx = GetHostCommandCatalogInstruction?.Invoke();
        // TurnSummaryInstruction must come BEFORE hostCmdCtx so the AI emits <system_notification>
        // first and HOST_COMMAND_JSON last. The parser regex requires HOST_COMMAND_JSON at the
        // very end of the response; placing it after TurnSummaryInstruction would break the match.
        // InboxMessageInstruction is placed before TurnSummaryInstruction so the ordering is:
        //   … triggeredCtx, inboxCtx, commitReportingCtx, TurnSummaryInstruction, hostCmdCtx
        var inboxCtx = _instructionProvider.Get().InboxMessage;
        var commitReportingCtx = _instructionProvider.Get().CommitReporting;
        var parts = new[] { pending, docsCtx, tasksCtx, queueCtx, questionCtx, triggeredCtx, inboxCtx, commitReportingCtx, _instructionProvider.Get().TurnSummary, hostCmdCtx }.Where(p => p is not null).ToArray();
        var supplemental = parts.Length == 0 ? null : string.Join("\n\n", parts);
        var buildResult = _promptBuilder.Build(
            prompt,
            BuildQuickReplyInstruction(PendingQueueItemCount),
            _getPendingQuickReplyRoutingInstruction(),
            _getPendingQuickReplyRouteMode(),
            supplemental,
            _workspaceContext.GetCurrentWorkspace()?.FolderPath,
            _instructionProvider.Get().CoordinatorDelegationAccountability);
        SquadDashTrace.Write("Routing", $"Bridge prompt context: {buildResult.RoutingSummary} accountability=included");
        return buildResult.PromptText;
    }

    private static void SavePromptDispatchDiagnostic(
        SessionWorkspace workspace,
        string? sessionId,
        string? configDirectory,
        PromptQueueItem? queueItem,
        int pendingQueueItemCount,
        string visiblePrompt,
        string bridgePrompt) {
        var record = PromptDispatchDiagnosticsStore.CreateRecord(
            workspace.FolderPath,
            sessionId,
            configDirectory,
            queueItem,
            pendingQueueItemCount,
            visiblePrompt,
            bridgePrompt);
        PromptDispatchDiagnosticsStore.TryAppend(workspace.FolderPath, record);
    }

    private string? BuildTriggeredInjections(string prompt) {
        var workspaceFolder = _workspaceContext.GetCurrentWorkspace()?.FolderPath;
        var variables = TriggeredInjectionEvaluator.BuildVariables(workspaceFolder);
        var fired = TriggeredInjectionEvaluator.Evaluate(prompt, BuiltInPromptInjections.All, variables);
        if (fired.Count == 0)
            return null;

        foreach (var (injection, _) in fired)
            SquadDashTrace.Write("PromptInjection", $"Triggered injection fired: '{injection.Id}'");

        return string.Join("\n\n", fired.Select(f => f.ResolvedText));
    }

    private static string BuildQueueContextInstruction(int pendingCount) {
        var itemWord = pendingCount == 1 ? "prompt" : "prompts";
        return
            $"There {(pendingCount == 1 ? "is" : "are")} {pendingCount} queued {itemWord} " +
            $"that will dispatch automatically after this turn completes. " +
            $"If you need a human response before those run — for example if you have a question " +
            $"or need a decision — end your response with the exact sentinel text on its own line:\n" +
            $"{QueueAwaitInputSentinel}\n" +
            $"SquadDash will pause the queue and wait for user input before continuing. " +
            $"If you do not need human input, do not include the sentinel; the next queued prompt will run automatically.";
    }

    internal static string BuildQueuedQuestionInboxHint() =>
        "The user's prompt contains a question. There are also queued prompts that will run " +
        "automatically — the user may have stepped away and might not be watching the transcript " +
        "in real time. If your answer is detailed or requires action on their part, send them an " +
        "inbox message summarizing the key information so they can find it later. You do not need " +
        "to send an inbox message for brief or trivial answers.";

    private string BuildQuickReplyInstruction(int pendingQueueCount) {
        if (pendingQueueCount <= 0)
            return _instructionProvider.Get().QuickReply;

        // When a queue is running, quick reply buttons pause the queue and interrupt the
        // automatic flow. Append a strong advisory so the model only reaches for a button
        // when a genuine blocking decision is needed — not for convenience or confirmation.
        var itemWord = pendingQueueCount == 1 ? "item" : "items";
        var queueCaveat =
            $"IMPORTANT — there {(pendingQueueCount == 1 ? "is" : "are")} {pendingQueueCount} queued " +
            $"{itemWord} that will run automatically after this turn. " +
            $"Quick reply buttons pause the queue and require the user to click before those items continue. " +
            $"Only emit a quick reply button if you face a genuine blocking decision that you cannot resolve " +
            $"with good judgment — something where proceeding without explicit user input would risk going " +
            $"in the wrong direction entirely. " +
            $"If you can handle it yourself (add a task to the backlog, pick a reasonable default, " +
            $"take the safe conservative action), do so and let the queue continue. " +
            $"When in doubt, decide rather than ask.";

        return _instructionProvider.Get().QuickReply + "\n\n" + queueCaveat;
    }

    private void MarkActiveToolsAsFailed(string errorMessage) {
        var coordinatorTurn = _transcriptSink.CoordinatorThread.CurrentTurn;
        foreach (var entry in _transcriptSink.GetToolEntries().Where(item =>
                     !item.IsCompleted &&
                     ReferenceEquals(item.Turn, coordinatorTurn))) {
            entry.IsCompleted = true;
            entry.Success     = false;
            entry.FinishedAt  = DateTimeOffset.Now;
            entry.OutputText  = MergeToolOutput(entry.OutputText, errorMessage);
            entry.DetailContent = ToolTranscriptFormatter.BuildDetailContent(new ToolTranscriptDetail(
                entry.Descriptor,
                entry.ArgsJson,
                entry.OutputText,
                entry.StartedAt,
                entry.FinishedAt,
                entry.ProgressText,
                entry.IsCompleted,
                Success: false));
            _transcriptSink.RenderToolEntry(entry);
        }

        _transcriptSink.UpdateToolSpinnerState();
        // All active tools are now completed — tool name is definitively null
        ActiveToolName = null;
    }

    private string BuildRuntimeDiagnostics(string errorMessage, string activeDirectory) {
        var builder = new StringBuilder();
        builder.AppendLine($"Active directory: {activeDirectory}");
        builder.AppendLine($"Bundled SDK: {_workspacePaths.SquadSdkDirectory}");
        builder.AppendLine();
        builder.AppendLine("What to try");
        builder.AppendLine("1. Use the Install Squad button to repair the workspace-local CLI and compatibility patches.");
        builder.AppendLine("2. Retry the prompt so the bundled SDK runtime can restart with the repaired dependencies.");
        builder.AppendLine("3. If the error persists, inspect the details below (there may be a failing package path).");
        builder.AppendLine();
        builder.AppendLine("Error");
        builder.AppendLine(errorMessage);
        return builder.ToString().TrimEnd();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Static helpers
    // ─────────────────────────────────────────────────────────────────────

    private static string? MergeToolOutput(string? existingOutput, string? newOutput) {
        if (string.IsNullOrWhiteSpace(newOutput))
            return existingOutput;
        if (string.IsNullOrWhiteSpace(existingOutput))
            return newOutput.TrimEnd();
        if (existingOutput.Contains(newOutput, StringComparison.Ordinal))
            return existingOutput;

        return existingOutput.TrimEnd() + Environment.NewLine + newOutput.TrimEnd();
    }

    private static Brush ThemeBrush(string key) =>
        (Brush?)Application.Current.Resources[key] ?? Brushes.Gray;
}

internal sealed record SilentCompletionFollowUpReport(
    string AgentDisplayName,
    string? ThreadId,
    DateTimeOffset? CompletedAt,
    string? Prompt,
    string? Report);

internal static class SilentCompletionFollowUpPromptBuilder {
    private const int MaxReportChars = 12000;
    private const int MaxPromptChars = 3000;

    internal static string Build(IReadOnlyList<SilentCompletionFollowUpReport> reports) {
        if (reports.Count == 0)
            return string.Empty;

        var names = string.Join(", ", reports.Select(r => $"**{r.AgentDisplayName}**"));
        var sb = new StringBuilder();
        sb.AppendLine("The following subagents completed their work while your turn was active, but no response was generated.");
        sb.AppendLine($"Please report back to the user using the exact completed work below: {names}.");
        sb.AppendLine();
        sb.AppendLine("Important: do not search by agent name or infer from older work. Use these thread IDs, prompts, and reports as the source of truth.");

        for (var i = 0; i < reports.Count; i++) {
            var report = reports[i];
            sb.AppendLine();
            sb.AppendLine($"## Completed subagent {i + 1}: {report.AgentDisplayName}");
            sb.AppendLine($"Thread ID: {ValueOrMissing(report.ThreadId)}");
            sb.AppendLine($"Completed at: {report.CompletedAt?.ToString("u") ?? "(unknown)"}");
            sb.AppendLine();
            sb.AppendLine("Original prompt:");
            sb.AppendLine(TrimForPrompt(report.Prompt, MaxPromptChars, "(no original prompt was captured)", "Original prompt"));
            sb.AppendLine();
            sb.AppendLine("Captured report:");
            sb.AppendLine(TrimForPrompt(report.Report, MaxReportChars, "(no report text was captured)", "Report"));
        }

        return sb.ToString().TrimEnd();
    }

    private static string ValueOrMissing(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(unknown)" : value.Trim();

    private static string TrimForPrompt(string? value, int maxChars, string missingText, string label) {
        if (string.IsNullOrWhiteSpace(value))
            return missingText;

        var trimmed = value.Trim();
        if (trimmed.Length <= maxChars)
            return trimmed;

        return trimmed[..maxChars] + Environment.NewLine + Environment.NewLine + $"[{label} truncated for prompt size; use the thread ID above for the full report.]";
    }
}


