using System;
using System.IO;
using System.Linq;
using System.Text;
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

    private string? BuildTasksContextInstruction() {
        if (string.IsNullOrEmpty(TasksFilePath) || !File.Exists(TasksFilePath))
            return null;
        string[] lines;
        try {
            lines = File.ReadAllLines(TasksFilePath);
        }
        catch {
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

    // ── Constants (copied from MainWindow) ────────────────────────────────
    // Instructs AI to append a machine-readable summary at the end of every response.
    // Wrapped in <system_notification> so StripSystemNotifications removes it from the
    // displayed transcript. The raw ResponseTextBuilder retains it for extraction.
    private const string TurnSummaryInstruction =
        "At the very end of your response, on its own line, append a machine-readable turn summary in this exact format " +
        "(it is stripped from the displayed transcript and is never shown to the user):\n" +
        "<system_notification>{\"notification\": \"one short sentence — 10 words or fewer — describing what you did or answered. If you made or reported a git commit, the description must name what was committed.\"}</system_notification>";

    private const string QuickReplyInstruction =
        "When you offer quick replies, append a machine-readable block exactly in this format:\nQUICK_REPLIES_JSON:\n[\n  {\n    \"label\": \"Option A\",\n    \"routeMode\": \"continue_current_agent\",\n    \"reason\": \"One short routing reason.\"\n  },\n  {\n    \"label\": \"Option B\",\n    \"routeMode\": \"start_named_agent\",\n    \"targetAgent\": \"orion-vale\",\n    \"reason\": \"One short routing reason.\"\n  }\n]\nOnly emit quick replies when the user can act on them immediately. Do not emit quick replies while background agents are still working, while you are only reporting progress, or while the next step is blocked on unfinished work. Do not emit quick replies in the same response where you launch, assign, queue, delegate, or hand off new background work. If you tell the user that an agent is starting, is running, will continue in the background, that you will report back later, or that they should use `/tasks` for status, emit no quick replies at all in that response. Quick replies are only allowed after the relevant agent work has finished and the user can immediately choose the next real step. Each quick reply must include `label` and `routeMode`. `routeMode` must be one of `continue_current_agent`, `start_named_agent`, `start_coordinator`, `fanout_team`, or `done`. Include `targetAgent` only when `routeMode` is `start_named_agent`, using a roster handle from `.squad/team.md`. Use `continue_current_agent` only when the next step should stay with the same agent who produced the current response. Use `.squad/team.md` and `.squad/routing.md` to choose the correct owner. Keep the label and metadata aligned: if the button says to run, ask, hand off to, or start a different agent, utility agent, or specialist, do not use `continue_current_agent`; use `start_named_agent` with the correct `targetAgent` instead. In particular, if the next step is to run Scribe, Ralph, or any agent other than the one who produced the current response, the quick reply must use `start_named_agent` and name that agent explicitly. When a quick reply names or implies an owner for follow-up work, delegated work, backlog items, reviews, or test work, keep that owner aligned with `.squad/routing.md` instead of assigning by convenience. Do not assign testing, QA, verification, or coverage work to a non-testing specialist unless `.squad/routing.md` explicitly gives them that ownership or you clearly describe the work as collaboration under the testing lead. Never include no-op buttons — every quick reply must cause something meaningful to happen. Do not include a lone \"Done\" button when it would just send an empty acknowledgement. Do not include a \"No\" or \"Cancel\" button on a yes/no question unless clicking it would actually trigger a useful action; if declining means doing nothing, omit it entirely. If the only honest reply is \"you're finished\", emit no quick replies at all. Do NOT emit buttons like \"Looks good — what's next?\", \"Looks good\", \"What's next?\", \"All done\", or any variant that is just an acknowledgement or a vague invitation to continue — these are no-ops because clicking them gives the AI nothing actionable to act on. A quick reply is only valid if clicking it causes a specific, identifiable action: routing to a named agent, starting a named task, or asking a concrete question. If there is no active task list and no specific next step you can name, emit no quick replies at all.";

    private static readonly TimeSpan PromptNoActivityWarningThreshold = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PromptNoActivityStallThreshold   = TimeSpan.FromMinutes(2);

    // ── Injected — Bridge ─────────────────────────────────────────────────
    private readonly Func<string, string, string?, string?, Task> _runPromptAsync;
    private readonly Func<string, string, string, string?, string?, Task> _runNamedAgentDelegationAsync;
    private readonly Func<string, string, string?, string, string?, string?, Task> _runNamedAgentDirectAsync;

    // ── Injected — Workspace ──────────────────────────────────────────────
    private readonly Func<SessionWorkspace?>            _getCurrentWorkspace;
    private readonly Func<ApplicationSettingsSnapshot>  _getSettingsSnapshot;

    // ── Injected — Conversation ───────────────────────────────────────────
    private readonly TranscriptConversationManager _conversationManager;

    // ── Injected — Background Tasks ───────────────────────────────────────
    private readonly BackgroundTaskPresenter _backgroundTaskPresenter;

    // ── Injected — CLI Adapter ────────────────────────────────────────────
    private readonly SquadCliAdapter _squadCliAdapter;

    // ── Injected — Transcript Rendering ──────────────────────────────────
    private readonly Func<string, TranscriptTurnView>           _beginTranscriptTurn;
    private readonly Action                                      _finalizeCurrentTurnResponse;
    private readonly Action<string, Brush?>                      _appendLine;
    private readonly Action<TranscriptThreadState>               _selectTranscriptThread;
    private readonly Func<TranscriptThreadState>                 _getCoordinatorThread;

    // ── Injected — Agent Data ─────────────────────────────────────────────
    private readonly Func<IReadOnlyList<AgentStatusCard>> _getAgents;
    private readonly Func<string?>                        _getCurrentSessionState;

    // ── Injected — Running State ──────────────────────────────────────────
    private readonly Func<bool>   _getIsPromptRunning;
    private readonly Action<bool> _setIsPromptRunning;
    private readonly Func<bool>   _getIsClosing;
    private readonly Func<bool>   _getRestartPending;
    private readonly Action       _close;

    // ── Injected — PromptBox ──────────────────────────────────────────────
    private readonly Action      _clearPromptTextBox;
    private readonly Action      _focusPromptTextBox;
    private readonly Func<bool>  _isPromptTextBoxEnabled;

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
    private readonly Action                     _runDoctor;
    private readonly Func<HireAgentWindow.HireAgentSubmission?> _showHireAgentWindow;
    private readonly Action<string>             _enqueuePrompt;
    private readonly Action                     _showScreenshotOverlay;

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
    private readonly Func<TranscriptResponseEntry?>   _getLastQuickReplyEntry;
    private readonly Action                           _setLastQuickReplyEntryNull;
    private readonly Action<TranscriptResponseEntry>  _renderResponseEntry;
    private readonly Action<TranscriptThreadState>    _ensureThreadFooterAtEnd;
    private readonly Action                           _scrollToEndIfAtBottom;

    // ── Injected — Tool Entries (for MarkActiveToolsAsFailed) ────────────
    private readonly Func<IEnumerable<ToolTranscriptEntry>> _getToolEntries;
    private readonly Action<ToolTranscriptEntry>            _renderToolEntry;
    private readonly Action                                 _updateToolSpinnerState;

    // ── IWorkspacePaths ───────────────────────────────────────────────────
    private readonly IWorkspacePaths _workspacePaths;

    /// <summary>
    /// When set, returns the HOST_COMMANDS catalog instruction to inject into every prompt.
    /// MainWindow sets this after constructing the HostCommandRegistry.
    /// </summary>
    internal Func<string?>? GetHostCommandCatalogInstruction { get; set; }

    // ── Owned fields ──────────────────────────────────────────────────────
    private bool             _clearConfirmationPending;
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
    private int              _toolStartCount;
    private int              _toolCompleteCount;
    private bool             _promptNoActivityWarningShown;
    private bool             _promptStallWarningShown;
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
        // Workspace
        Func<SessionWorkspace?> getCurrentWorkspace,
        Func<ApplicationSettingsSnapshot> getSettingsSnapshot,
        // Conversation
        TranscriptConversationManager conversationManager,
        // Background Tasks
        BackgroundTaskPresenter backgroundTaskPresenter,
        // CLI Adapter
        SquadCliAdapter squadCliAdapter,
        // Transcript Rendering
        Func<string, TranscriptTurnView> beginTranscriptTurn,
        Action finalizeCurrentTurnResponse,
        Action<string, Brush?> appendLine,
        Action<TranscriptThreadState> selectTranscriptThread,
        Func<TranscriptThreadState> getCoordinatorThread,
        // Agent Data
        Func<IReadOnlyList<AgentStatusCard>> getAgents,
        Func<string?> getCurrentSessionState,
        // Running State
        Func<bool> getIsPromptRunning,
        Action<bool> setIsPromptRunning,
        Func<bool> getIsClosing,
        Func<bool> getRestartPending,
        Action close,
        // PromptBox
        Action clearPromptTextBox,
        Action focusPromptTextBox,
        Func<bool> isPromptTextBoxEnabled,
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
        Action runDoctor,
        Func<HireAgentWindow.HireAgentSubmission?> showHireAgentWindow,
        Action<string> enqueuePrompt,
        Action showScreenshotOverlay,
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
        Func<TranscriptResponseEntry?> getLastQuickReplyEntry,
        Action setLastQuickReplyEntryNull,
        Action<TranscriptResponseEntry> renderResponseEntry,
        Action<TranscriptThreadState> ensureThreadFooterAtEnd,
        Action scrollToEndIfAtBottom,
        // Tool entries (for MarkActiveToolsAsFailed)
        Func<IEnumerable<ToolTranscriptEntry>> getToolEntries,
        Action<ToolTranscriptEntry> renderToolEntry,
        Action updateToolSpinnerState,
        IWorkspacePaths workspacePaths) {

        _runPromptAsync                        = runPromptAsync;
        _runNamedAgentDelegationAsync          = runNamedAgentDelegationAsync;
        _runNamedAgentDirectAsync              = runNamedAgentDirectAsync;
        _getCurrentWorkspace                   = getCurrentWorkspace;
        _getSettingsSnapshot                   = getSettingsSnapshot;
        _conversationManager                   = conversationManager;
        _backgroundTaskPresenter               = backgroundTaskPresenter;
        _squadCliAdapter                       = squadCliAdapter;
        _beginTranscriptTurn                   = beginTranscriptTurn;
        _finalizeCurrentTurnResponse           = finalizeCurrentTurnResponse;
        _appendLine                            = appendLine;
        _selectTranscriptThread                = selectTranscriptThread;
        _getCoordinatorThread                  = getCoordinatorThread;
        _getAgents                             = getAgents;
        _getCurrentSessionState                = getCurrentSessionState;
        _getIsPromptRunning                    = getIsPromptRunning;
        _setIsPromptRunning                    = setIsPromptRunning;
        _getIsClosing                          = getIsClosing;
        _getRestartPending                     = getRestartPending;
        _close                                 = close;
        _clearPromptTextBox                    = clearPromptTextBox;
        _focusPromptTextBox                    = focusPromptTextBox;
        _isPromptTextBoxEnabled                = isPromptTextBoxEnabled;
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
        _runDoctor                             = runDoctor;
        _showHireAgentWindow                   = showHireAgentWindow;
        _enqueuePrompt                         = enqueuePrompt;
        _showScreenshotOverlay                 = showScreenshotOverlay;
        _showRuntimeIssue                      = showRuntimeIssue;
        _clearRuntimeIssue                     = clearRuntimeIssue;
        _waitForRoutingRepairSettleAsync       = waitForRoutingRepairSettleAsync;
        _maybePublishRoutingIssue              = maybePublishRoutingIssue;
        _promptHealthTimer                     = promptHealthTimer;
        _waitForPostedUiActionsAsync           = waitForPostedUiActionsAsync;
        _getModelObservedThisSession           = getModelObservedThisSession;
        _getLastQuickReplyEntry                = getLastQuickReplyEntry;
        _setLastQuickReplyEntryNull            = setLastQuickReplyEntryNull;
        _renderResponseEntry                   = renderResponseEntry;
        _ensureThreadFooterAtEnd               = ensureThreadFooterAtEnd;
        _scrollToEndIfAtBottom                 = scrollToEndIfAtBottom;
        _getToolEntries                        = getToolEntries;
        _renderToolEntry                       = renderToolEntry;
        _updateToolSpinnerState                = updateToolSpinnerState;
        _workspacePaths                        = workspacePaths;

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
                $"No bridge activity for {idleFor.TotalSeconds:0}s since prompt start. state={_getCurrentSessionState() ?? "(unknown)"}");
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
        _toolStartCount               = 0;
        _toolCompleteCount            = 0;
        _promptNoActivityWarningShown = false;
        _promptStallWarningShown      = false;
        _lastSessionReadyEvent        = null;
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
        if (_promptNoActivityWarningShown || _promptStallWarningShown) {
            SquadDashTrace.Write(
                "PromptHealth",
                $"Prompt activity resumed after quiet period. activity={activityName} idleMs={(now - (_lastPromptActivityAt ?? now)).TotalMilliseconds:0}");
            _promptNoActivityWarningShown = false;
            _promptStallWarningShown      = false;
        }

        _lastPromptActivityAt = now;
        _lastPromptActivityName = activityName;

        if (_currentPromptStartedAt is { } startedAt) {
            var elapsed = now - startedAt;
            SquadDashTrace.Write(
                "PromptHealth",
                $"Activity={activityName} elapsedMs={elapsed.TotalMilliseconds:0}");
        }
    }

    internal void MarkActivity(SquadSdkEvent evt) {
        var activityName = evt.Type ?? "event";
        MarkActivity(activityName);

        if (!_getIsPromptRunning())
            return;

        var now = _lastPromptActivityAt ?? DateTimeOffset.Now;
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
                        SquadDashTrace.Write(
                            "PromptHealth",
                            $"Thinking chunk chars={thinkingTextLength} totalThinkingChars={_thinkingCharacterCount} totalThinkingChunks={_thinkingTextDeltaCount} avgThinkingCharsPerSec={FormatThinkingCharsPerSecond()}");
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
                break;
        }
    }

    private void StopPromptHealthMonitoring(string outcome) {
        if (_currentPromptStartedAt is { } startedAt) {
            var finishedAt = DateTimeOffset.Now;
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
        _toolStartCount               = 0;
        _toolCompleteCount            = 0;
        _promptNoActivityWarningShown = false;
        _promptStallWarningShown      = false;
        _lastSessionReadyEvent        = null;
        _currentPromptContextDiagnostics = null;
    }

    private string FormatElapsedSincePrompt(DateTimeOffset timestamp) {
        if (_currentPromptStartedAt is not { } startedAt)
            return "unknown";

        return StatusTimingPresentation.FormatDuration(timestamp - startedAt);
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

        // Intercept clear confirmation responses first
        if (_clearConfirmationPending) {
            if (string.Equals(trimmed, "Yes", StringComparison.OrdinalIgnoreCase))
                return HandleClearConfirmed(prompt, clearPromptBox);
            if (string.Equals(trimmed, "No", StringComparison.OrdinalIgnoreCase))
                return HandleClearCancelled(prompt, clearPromptBox);
            // Any other input cancels the pending confirmation
            _clearConfirmationPending = false;
        }

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

    private bool HandleLocalDoctorCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/doctor");
        return ExecuteLocalUiCommand(prompt, addToHistory, clearPromptBox, _runDoctor);
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
                    _appendLine($"{modelLabel}: **{_squadCliAdapter.LastObservedModel}**", null);
                }
                else {
                    _appendLine("Active model: *(not yet observed — run a prompt first)*", null);
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
            if (!_getIsClosing() && _isPromptTextBoxEnabled())
                _focusPromptTextBox();
            return true;
        }

        if (_getIsPromptRunning()) {
            _enqueuePrompt(submission.PromptText);
            return true;
        }

        _ = ExecutePromptAsync(submission.PromptText, addToHistory: false, clearPromptBox: false);
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
                () => _appendLine($"Usage: `/{commandName} <agent name>`", null));
        }

        if (addToHistory)
            _conversationManager.AddPromptToHistory(prompt);

        MaybeClearPromptAfterLocalCommand(prompt, clearPromptBox);

        if (_getIsPromptRunning()) {
            return ExecuteLocalCoordinatorCommand(
                prompt,
                addToHistory: false,
                clearPromptBox: false,
                () => _appendLine($"Finish the current prompt before trying to /{commandName} an agent.", null),
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
                _appendLine("## Session", null);
                _appendLine("Use `/session fresh` to detach from the current SDK session without clearing the transcript.", null);
                _appendLine("Use `/sessions` to list remembered session ids.", null);
                _appendLine("Use `/resume <id-prefix>` to point the next prompt back at an older session.", null);
            });
    }

    private bool HandleLocalHelpCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/help");

        return ExecuteLocalCoordinatorCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            () => {
                _appendLine("## How it works", null);
                _appendLine("Just type what you need — Squad routes your message to the right agent.", null);
                _appendLine("- `@AgentName message` — send directly to one agent", null);
                _appendLine("- `@Agent1 @Agent2 message` — send to multiple agents in parallel", null);
                _appendLine(string.Empty, null);
                _appendLine("## Squad Commands", null);
                _appendLine("- `/agents` — List all team members", null);
                _appendLine("- `/history [N]` — See recent messages (default 10)", null);
                _appendLine("- `/nap [--deep]` — Context hygiene (compress, prune, archive)", null);
                _appendLine("- `/resume <id>` — Restore a past session by ID prefix", null);
                _appendLine("- `/session` — Manage SDK sessions (type `/session` for details)", null);
                _appendLine("- `/sessions` — List saved sessions", null);
                _appendLine("- `/status` — Check your team & what's happening", null);
                _appendLine("- `/version` — Show version", null);
                _appendLine(string.Empty, null);
                _appendLine("## SquadDash Commands", null);
                _appendLine("- `/activate <name>` — Restore an inactive or retired agent to the active roster", null);
                _appendLine("- `/agents` — List all team members", null);
                _appendLine("- `/clear` — Clear the transcript (with confirmation)", null);
                _appendLine("- `/deactivate <name>` — Remove an agent from the active roster without deleting them", null);
                _appendLine("- `/doctor` — Run Squad Doctor diagnostics", null);
                _appendLine("- `/dropTasks` — Hide the live tasks popup", null);
                _appendLine("- `/help` — Show this command reference", null);
                _appendLine("- `/hire` — Open the visual hire-agent workflow", null);
                _appendLine("- `/model` — Show the active AI model", null);
                _appendLine("- `/retire <name>` — Archive an agent and remove them from the active roster", null);
                _appendLine("- `/session` — Manage SDK sessions (type `/session` for details)", null);
                _appendLine("- `/sessions` — List saved sessions", null);
                _appendLine("- `/status` — Show team, session, and current turn state", null);
                _appendLine("- `/tasks` — Show or refresh the live tasks popup", null);
                _appendLine("- `/trace` — Open the live trace window", null);
                _appendLine("- `/version` — Show Squad and SquadDash version", null);
            });
    }

    private bool HandleLocalStatusCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/status");

        return ExecuteLocalCoordinatorCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            () => {
                var workspace = _getCurrentWorkspace()?.FolderPath ?? "*(none)*";
                var knownSessionIds = _conversationManager.GetKnownSessionIds();
                _appendLine($"## {Path.GetFileName(_getCurrentWorkspace()?.FolderPath ?? "Squad Status")}", null);
                _appendLine($"- **Workspace:** `{workspace}`", null);
                _appendLine($"- **Session:** {(_conversationManager.CurrentSessionId is not null ? $"`{_conversationManager.CurrentSessionId}`" : "*(not started)*")}", null);
                _appendLine($"- **State:** {_getCurrentSessionState() ?? "Unknown"}", null);
                if (knownSessionIds.Count > 0)
                    _appendLine($"- **Remembered Sessions:** {string.Join(", ", knownSessionIds.Select(FormatSessionListEntry))}", null);
                if (!string.IsNullOrWhiteSpace(LatestSessionDiagnosticsSummary))
                    _appendLine($"- **Session Forensics:** {LatestSessionDiagnosticsSummary}", null);
                if (!string.IsNullOrWhiteSpace(_squadCliAdapter.LastObservedModel)) {
                    var modelLabel = _getModelObservedThisSession() ? "Model" : "Model (last session)";
                    _appendLine($"- **{modelLabel}:** {_squadCliAdapter.LastObservedModel}", null);
                }

                var currentTurnLine = CurrentTurnStatusPresentation.BuildReportLine(_backgroundTaskPresenter.BuildCurrentTurnStatusSnapshot());
                if (!string.IsNullOrWhiteSpace(currentTurnLine)) {
                    _appendLine(string.Empty, null);
                    _appendLine("## Current Turn", null);
                    _appendLine("- " + currentTurnLine, null);
                }

                var agents = _getAgents();
                var teamCards = agents.Where(c => !c.IsLeadAgent).ToList();
                if (teamCards.Count > 0) {
                    _appendLine(string.Empty, null);
                    _appendLine("## Team", null);
                    foreach (var card in teamCards) {
                        var status = string.IsNullOrWhiteSpace(card.StatusText) ? "Idle" : card.StatusText;
                        var detail = string.IsNullOrWhiteSpace(card.DetailText) ? string.Empty : $" — {card.DetailText}";
                        var roleLabel = string.IsNullOrWhiteSpace(card.RoleText) ? string.Empty : $" ({card.RoleText})";
                        _appendLine($"- **{card.Name}**{roleLabel}: {status}{detail}", null);
                    }
                }

                if (_backgroundTaskPresenter.HasBackgroundTasks()) {
                    _appendLine(string.Empty, null);
                    _appendLine("## Background Tasks", null);
                    _appendLine(_backgroundTaskPresenter.BuildBackgroundTaskReport(), null);
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
                _appendLine("## Sessions", null);
                _appendLine(
                    $"Current: {(_conversationManager.CurrentSessionId is null ? "*(fresh / not started)*" : $"`{_conversationManager.CurrentSessionId}`")}",
                    null);

                if (knownSessionIds.Count == 0) {
                    _appendLine("Remembered: *(none yet)*", null);
                }
                else {
                    _appendLine("Remembered:", null);
                    foreach (var sessionId in knownSessionIds)
                        _appendLine($"- {FormatSessionListEntry(sessionId)}", null);
                }

                _appendLine(string.Empty, null);
                _appendLine("Use `/session fresh` to detach from the current SDK session without clearing the transcript.", null);
                _appendLine("Use `/resume <id-prefix>` to point the next prompt back at one of the remembered sessions.", null);
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

                _appendLine("## Session", null);
                if (previousSessionId is null) {
                    _appendLine("There was no active SDK session to detach. The next prompt will start fresh.", null);
                    return;
                }

                _appendLine($"Detached from `{previousSessionId}`.", null);
                _appendLine("The transcript stayed in place. The next prompt will create a brand-new SDK session.", null);
                _appendLine($"To return later, run `/resume {TranscriptConversationManager.AbbreviateSessionId(previousSessionId)}`.", null);
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
                _appendLine("## Session", null);
                if (!result.Succeeded) {
                    _appendLine(result.ErrorMessage ?? "Unable to resume that session.", ThemeBrush("SystemErrorText"));
                    return;
                }

                _updateLeadAgent("Ready", string.Empty, "Ready to continue with the selected SDK session.");
                _updateSessionState("Ready");
                _refreshAgentCards();
                _refreshSidebar();
                _appendLine($"Next prompt will use `{result.SelectedSessionId}`.", null);
                _appendLine("The transcript was left as-is so you can keep investigating this workspace.", null);
            });
    }

    private bool HandleLocalAgentsCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/agents");

        return ExecuteLocalCoordinatorCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            () => {
                var agents = _getAgents();
                _appendLine("## Team Members", null);

                // Output coordinator first
                var leadAgent = agents.FirstOrDefault(c => c.IsLeadAgent);
                if (leadAgent is not null)
                    _appendLine($"- **{leadAgent.Name}** *(coordinator)*", null);

                // Output permanent team members
                foreach (var card in agents.Where(c => !c.IsLeadAgent && !c.IsDynamicAgent)) {
                    var roleLabel = string.IsNullOrWhiteSpace(card.RoleText) ? string.Empty : $" — {card.RoleText}";
                    _appendLine($"- **{card.Name}**{roleLabel}", null);
                }

                // Output temporary agents if any exist
                var dynamicAgents = agents.Where(c => c.IsDynamicAgent).ToList();
                if (dynamicAgents.Count > 0) {
                    _appendLine(string.Empty, null);
                    _appendLine("**Temporary Agents:**", null);
                    foreach (var card in dynamicAgents) {
                        var roleLabel = string.IsNullOrWhiteSpace(card.RoleText) ? string.Empty : $" — {card.RoleText}";
                        _appendLine($"- **{card.Name}**{roleLabel}", null);
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
                _appendLine("## Version", null);
                _appendLine($"- **SquadDash:** {appVersion}", null);
                if (!string.IsNullOrWhiteSpace(_squadCliAdapter.SquadVersion))
                    _appendLine($"- **Squad CLI:** {_squadCliAdapter.SquadVersion}", null);
                else
                    _appendLine("- **Squad CLI:** *(not resolved)*", null);
                if (!string.IsNullOrWhiteSpace(_squadCliAdapter.LastObservedModel))
                    _appendLine($"- **Model:** {_squadCliAdapter.LastObservedModel}", null);
            });
    }

    private bool HandleLocalTestTableCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/testtable");

        return ExecuteLocalCoordinatorCommand(
            prompt,
            addToHistory,
            clearPromptBox,
            () => {
                _appendLine("This is text BEFORE the table. Select from here, drag through the table, and include the line below it, then copy and paste.", null);
                _appendLine(string.Empty, null);
                _appendLine("| Animal | Speed | Habitat |", null);
                _appendLine("| --- | --- | --- |", null);
                _appendLine("| Cheetah | 120 km/h | Savanna |", null);
                _appendLine("| Peregrine Falcon | 390 km/h | Cliffs |", null);
                _appendLine("| Sailfish | 110 km/h | Ocean |", null);
                _appendLine(string.Empty, null);
                _appendLine("This is text AFTER the table. If the table rows appear between these two lines when you paste, the fix is working.", null);
            });
    }

    private bool HandleLocalClearCommand(string prompt, bool addToHistory, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Local command intercepted prompt=/clear");

        if (addToHistory)
            _conversationManager.AddPromptToHistory(prompt);

        _selectTranscriptThread(_getCoordinatorThread());
        _beginTranscriptTurn(prompt);

        if (clearPromptBox)
            _clearPromptTextBox();

        _clearConfirmationPending = true;
        _appendLine("Are you sure you want to clear this transcript?\n\n[Yes] [No]", null);

        _finalizeCurrentTurnResponse();
        _conversationManager.SaveCurrentTurnToConversation(DateTimeOffset.Now);
        _getCoordinatorThread().CurrentTurn = null;
        return true;
    }

    private bool HandleClearConfirmed(string prompt, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Clear transcript confirmed by user");
        _clearConfirmationPending = false;

        if (clearPromptBox)
            _clearPromptTextBox();

        var workspace = _getCurrentWorkspace();
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

    private bool HandleClearCancelled(string prompt, bool clearPromptBox) {
        SquadDashTrace.Write("UI", "Clear transcript cancelled by user");
        _clearConfirmationPending = false;

        if (clearPromptBox)
            _clearPromptTextBox();

        _selectTranscriptThread(_getCoordinatorThread());
        _beginTranscriptTurn(prompt);
        _appendLine("Transcript clear cancelled.", null);
        _finalizeCurrentTurnResponse();
        _conversationManager.SaveCurrentTurnToConversation(DateTimeOffset.Now);
        _getCoordinatorThread().CurrentTurn = null;
        return true;
    }

    private void MaybeClearPromptAfterLocalCommand(string prompt, bool clearPromptBox) {
        if (!clearPromptBox)
            return;

        if (LocalPromptSubmissionPolicy.ShouldRetainPromptAfterLocalSubmit(prompt, _getIsPromptRunning())) {
            _updateInteractiveControlState();
            return;
        }

        _clearPromptTextBox();
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

        if (!_getIsClosing() && _isPromptTextBoxEnabled())
            _focusPromptTextBox();

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

        var executionPlan = LocalCommandTurnExecutionPolicy.Create(_getIsPromptRunning(), _getCoordinatorThread().CurrentTurn);
        _selectTranscriptThread(_getCoordinatorThread());
        var localTurn = _beginTranscriptTurn(prompt);

        try {
            MaybeClearPromptAfterLocalCommand(prompt, clearPromptBox);
            renderResponse();
            _finalizeCurrentTurnResponse();
            _conversationManager.SaveTranscriptTurnToConversation(localTurn, DateTimeOffset.Now);
        }
        finally {
            if (executionPlan.ShouldRestoreSuspendedTurn &&
                executionPlan.SuspendedTurn is { } suspendedTurn) {
                _getCoordinatorThread().CurrentTurn = suspendedTurn;
                MoveTranscriptTurnToDocumentEnd(suspendedTurn);
            }
            else {
                _getCoordinatorThread().CurrentTurn = null;
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
                    _appendLine("## Session", null);
                    _appendLine("Wait for the current prompt to finish before switching SDK sessions.", ThemeBrush("SystemWarningText"));
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

        _ensureThreadFooterAtEnd(turn.OwnerThread);
    }

    private string FormatSessionListEntry(string sessionId) {
        var abbreviated = TranscriptConversationManager.AbbreviateSessionId(sessionId);
        return string.Equals(sessionId, _conversationManager.CurrentSessionId, StringComparison.OrdinalIgnoreCase)
            ? $"`{sessionId}` *(current; {abbreviated})*"
            : $"`{sessionId}` *({abbreviated})*";
    }

    internal void InjectUniverseSelectorTurn() {
        _universeSelectionPending = true;
        _selectTranscriptThread(_getCoordinatorThread());
        _beginTranscriptTurn(string.Empty);
        _appendLine("Ready to create a team? Select a universe:\n\n" +
            string.Join(" ", MainWindow.UniverseSelectorOptions.Select(u => $"[{u}]")), null);
        _finalizeCurrentTurnResponse();
        _conversationManager.SaveCurrentTurnToConversation(DateTimeOffset.Now);
        _getCoordinatorThread().CurrentTurn = null;
        _scrollToEndIfAtBottom();
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
        string? sessionIdOverride = null) {
        await ExecuteCoordinatorTurnAsync(
            prompt,
            addToHistory,
            clearPromptBox,
            allowLocalCommandHandling: true,
            async (workspace, configDirectory) => {
                var bridgePrompt = BuildBridgePrompt(prompt);
                await _runPromptAsync(
                    bridgePrompt,
                    workspace.FolderPath,
                    sessionIdOverride ?? _conversationManager.CurrentSessionId,
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

    private async Task ExecuteCoordinatorTurnAsync(
        string visiblePrompt,
        bool addToHistory,
        bool clearPromptBox,
        bool allowLocalCommandHandling,
        Func<SessionWorkspace, string?, Task> runBridgeTurnAsync) {
        if (_getCurrentWorkspace() is null)
            return;

        if (allowLocalCommandHandling && TryHandleLocalCommand(visiblePrompt, addToHistory, clearPromptBox))
            return;

        DisableTranscriptQuickReplies(_getCoordinatorThread());

        var workspace = _getCurrentWorkspace()!;
        SquadDashTrace.Write(
            "UI",
            $"ExecutePromptAsync start prompt={visiblePrompt} cwd={workspace.FolderPath}");

        _setIsPromptRunning(true);
        _conversationManager.ResetHistoryNavigation();
        _updateInteractiveControlState();

        if (addToHistory)
            _conversationManager.AddPromptToHistory(visiblePrompt);

        _selectTranscriptThread(_getCoordinatorThread());
        _beginTranscriptTurn(visiblePrompt);

        if (clearPromptBox)
            _clearPromptTextBox();

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
            var settings = _getSettingsSnapshot();
            if (settings.RuntimeIssueSimulation != DeveloperRuntimeIssueSimulation.None) {
                throw new InvalidOperationException(
                    WorkspaceIssueFactory.CreateSimulatedRuntimeErrorMessage(
                        settings.RuntimeIssueSimulation));
            }

            await runBridgeTurnAsync(workspace, configDirectory);
            _clearRuntimeIssue();
            SquadDashTrace.Write("UI", "ExecutePromptAsync completed successfully.");
            _refreshAgentCards();
            _refreshSidebar();
        }
        catch (OperationCanceledException) {
            SquadDashTrace.Write("UI", "ExecutePromptAsync aborted by user.");
            StopPromptHealthMonitoring("aborted");
            MarkActiveToolsAsFailed("Aborted");
            _appendLine("[aborted]", ThemeBrush("SystemInfoText"));
            _backgroundTaskPresenter.RefreshLeadAgentBackgroundStatus();
        }
        catch (Exception ex) {
            SquadDashTrace.Write("UI", $"ExecutePromptAsync failed: {ex}");
            StopPromptHealthMonitoring("error");
            var issue = _showRuntimeIssue(ex.Message);
            MarkActiveToolsAsFailed(issue.Message);
            _appendLine("[error] " + issue.Message, Brushes.Red);
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
            if (_currentPromptStartedAt is not null)
                StopPromptHealthMonitoring("completed");

            // Bridge events are posted onto the UI dispatcher. Wait for any already-posted
            // callbacks to run before persisting and clearing the live turn so tail response
            // chunks are not dropped after the bridge reports completion.
            await _waitForPostedUiActionsAsync();

            _conversationManager.SaveCurrentTurnToConversation(DateTimeOffset.UtcNow);
            _getCoordinatorThread().CurrentTurn = null;
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
                if (_isPromptTextBoxEnabled())
                    _focusPromptTextBox();
            }
        }
    }

    private void DisableTranscriptQuickReplies(TranscriptThreadState thread) {
        var lastEntry = _getLastQuickReplyEntry();
        if (lastEntry?.AllowQuickReplies == true) {
            DisableQuickReplies(lastEntry);
            _setLastQuickReplyEntryNull();
        }

        if (thread.CurrentTurn is null)
            return;

        foreach (var entry in thread.CurrentTurn.ResponseEntries.Where(candidate => candidate.AllowQuickReplies))
            DisableQuickReplies(entry);
    }

    /// <summary>
    /// Disables quick-reply buttons on a response entry and re-renders it.
    /// Called internally and from MainWindow's QuickReplyButton_Click.
    /// </summary>
    internal void DisableQuickReplies(TranscriptResponseEntry entry) {
        if (!entry.AllowQuickReplies)
            return;

        entry.AllowQuickReplies = false;
        _renderResponseEntry(entry);
    }

    private string BuildBridgePrompt(string prompt) {
        var pending   = _getPendingSupplementalInstruction();
        var docsCtx   = DocumentationModeActive ? BuildDocumentationContextInstruction() : null;
        var tasksCtx  = BuildTasksContextInstruction();
        var queueCtx  = PendingQueueItemCount > 0 ? BuildQueueContextInstruction(PendingQueueItemCount) : null;

        var triggeredCtx = BuildTriggeredInjections(prompt);

        var hostCmdCtx = GetHostCommandCatalogInstruction?.Invoke();
        // TurnSummaryInstruction must come BEFORE hostCmdCtx so the AI emits <system_notification>
        // first and HOST_COMMAND_JSON last. The parser regex requires HOST_COMMAND_JSON at the
        // very end of the response; placing it after TurnSummaryInstruction would break the match.
        var parts = new[] { pending, docsCtx, tasksCtx, queueCtx, triggeredCtx, TurnSummaryInstruction, hostCmdCtx }.Where(p => p is not null).ToArray();
        var supplemental = parts.Length == 0 ? null : string.Join("\n\n", parts);
        var buildResult = SquadBridgePromptBuilder.Build(
            prompt,
            QuickReplyInstruction,
            _getPendingQuickReplyRoutingInstruction(),
            _getPendingQuickReplyRouteMode(),
            supplemental,
            _getCurrentWorkspace()?.FolderPath);
        SquadDashTrace.Write("Routing", $"Bridge prompt context: {buildResult.RoutingSummary}");
        return buildResult.PromptText;
    }

    private string? BuildTriggeredInjections(string prompt) {
        var workspaceFolder = _getCurrentWorkspace()?.FolderPath;
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

    private void MarkActiveToolsAsFailed(string errorMessage) {
        var coordinatorTurn = _getCoordinatorThread().CurrentTurn;
        foreach (var entry in _getToolEntries().Where(item =>
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
            _renderToolEntry(entry);
        }

        _updateToolSpinnerState();
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
