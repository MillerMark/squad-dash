using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Net.Http;
using System.Text.Json;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Windows.Navigation;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using SquadDash.Screenshots;
using SquadDash.Screenshots.Fixtures;
using Shapes = System.Windows.Shapes;

namespace SquadDash;

public partial class MainWindow : Window
{
    private const string PostInstallPrompt =
        "Take a look at my code base and suggest a starting Squad team.";
    internal static readonly string[] UniverseSelectorOptions = [
        SquadInstallerService.SquadDashUniverseName,
        "Star Wars", "The Matrix", "Alien", "Firefly",
        "Ocean's Eleven", "The Simpsons", "Marvel Cinematic Universe",
        "Breaking Bad", "Futurama"
    ];
    private const string LeadAgentDefaultAccentHex = "#FF3E63B8";
    private const string ObservedAgentDefaultAccentHex = "#FF4472C4";
    private const string DynamicAgentDefaultAccentHex = "#FFD0D5DB";
    private const double TranscriptFontSizeMin = 11;
    private const double TranscriptFontSizeMax = 28;
    private const double TranscriptFontSizeStep = 1;
    private const double PromptFontSizeMin = 12;
    private const double PromptFontSizeMax = 30;
    private const double PromptFontSizeStep = 1;
    private const double DocSourceFontSizeMin = 8;
    private const double DocSourceFontSizeMax = 28;
    private const double DocSourceFontSizeStep = 1;
    private static readonly TimeSpan MultiLineHintCooldown = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan AgentActiveDisplayLinger = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DynamicAgentHistoryRetention = TimeSpan.FromDays(2);
    private static readonly TimeSpan ResponseRenderCadence = TimeSpan.FromMilliseconds(60);
    private const int DelegationOutcomeRollupWindow = 8;
    private const int DynamicAgentHistoryCardLimit = 6;
    private static readonly string[] ToolSpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private static readonly AgentAccentPaletteOption[] AgentAccentPalette = [
        new("#FF4472C4"),
        new("#FF4F5FB8"),
        new("#FF7A4EB5"),
        new("#FFAD4F8C"),
        new("#FFB35852"),
        new("#FFA96E34"),
        new("#FF6E8140"),
        new("#FF3E7F97"),
        new("#FF111111"),
        new("#FF3A3A3A"),
        new("#FF6A6A6A"),
        new("#FF9A9A9A")
    ];
    private readonly SquadSdkProcess _bridge;
    private readonly ApplicationSettingsStore _settingsStore = new();
    private readonly SquadTeamRosterLoader _teamRosterLoader = new();
    private readonly SquadRoutingDocumentService _routingDocumentService = new();
    private readonly SquadInstallationStateService _installationStateService = new();
    private readonly SquadInstallerService _installerService = new();
    private readonly RunningInstanceRegistry _instanceRegistry = new();
    private readonly RestartCoordinatorStateStore _restartCoordinatorStateStore = new();
    private readonly WorkspaceOpenCoordinator _workspaceOpenCoordinator;
    private readonly InstanceActivationChannel _instanceActivationChannel;
    private PreferencesWindow? _preferencesWindow;
    private readonly PushNotificationService _pushNotificationService;
    private readonly ObservableCollection<AgentStatusCard> _agents = [];
    private readonly ObservableCollection<AgentStatusCard> _activeAgentCards = [];
    private readonly ObservableCollection<AgentStatusCard> _inactiveAgentCards = [];
    private readonly DispatcherTimer _historyHintTimer;
    private readonly DispatcherTimer _toolSpinnerTimer;
    private readonly DispatcherTimer _promptHealthTimer;
    private readonly DispatcherTimer _statusPresentationTimer;
    private readonly DispatcherTimer _responseRenderTimer;
    private PromptExecutionController _pec = null!; // initialized in constructor after all services
    private LoopController _loopController = null!; // initialized in constructor after _pec
    private FileSystemWatcher? _inboxWatcher;
    private FileSystemWatcher? _teamFileWatcher;
    private FileSystemWatcher? _restartRequestWatcher;
    private readonly DispatcherTimer _teamRefreshDebounceTimer;
    private FileSystemWatcher? _docsWatcher;
    private CancellationTokenSource? _docsRefreshCts;
    private Point _docsDragStartPoint;
    private TreeViewItem? _docsDragItem;
    private bool _docsDragInProgress;
    private SessionWorkspace? _currentWorkspace;
    private SquadInstallationState? _currentInstallationState;
    private SquadRoutingDocumentAssessment? _currentRoutingAssessment;
    private WorkspaceIssuePresentation? _startupIssue;
    private WorkspaceIssuePresentation? _runtimeIssue;
    private string? _dismissedWorkspaceIssueKey;
    private string? _currentSolutionPath;
    private string? _currentSolutionName;
    private AgentStatusCard? _leadAgent;
    private bool _isApplyingIntelliSenseAccept;
    private IntelliSenseState? _intelliSenseState;
    private string[] _currentQuickReplyOptions = [];
    private TranscriptResponseEntry? _lastQuickReplyEntry;
    private TranscriptResponseEntry? _routingIssueQuickReplyEntry;
    private string? _lastMissingUtilityAgentNoticeKey;
    private string? _pendingQuickReplyRoutingInstruction;
    private PendingQuickReplyLaunchState? _pendingQuickReplyLaunch;
    private string? _pendingSupplementalPromptInstruction;
    private string? _announcedRoutingIssueFingerprint;
    private bool _pendingRoutingRepairRecheck;
    private bool _pendingPowerShellInstallRecheck;
    private TasksStatusWindow? _tasksStatusWindow;
    private TraceWindow? _traceWindow;
    // Offset (floating window Left/Top minus main window Right/Top) last set by the user
    // dragging the floating window. Null means "use default snap position".
    private Vector? _tasksWindowOffset;
    private Vector? _traceWindowOffset;
    // Set true while we are programmatically moving a floating window so its
    // LocationChanged does not overwrite the saved offset.
    private bool _movingFloatingWindow;
    private bool _isInstallingSquad;
    private bool _isClosing;
    private bool _isPromptRunning;
    private readonly PromptQueue _promptQueue = new();
    private int _promptQueueSeq;
    private string? _queuePreEditDraft;
    private int _queuePreEditDraftCaretIndex;
    private int _queuePreEditDraftSelectionStart;
    private int _queuePreEditDraftSelectionLength;
    private string? _activeTabId;   // null = Active Draft; otherwise a queued item Id
    private bool _restartPending;
    private DeferredShutdownMode _deferredShutdown;
    private bool _transcriptFullScreenEnabled;
    private bool _fullScreenPromptVisible;
    private bool _documentationModeEnabled;
    private string? _currentDocPath;  // tracks currently displayed doc for link resolution
    private bool _activeAgentLaneNudgeScheduled;
    private bool _inactiveAgentLaneNudgeScheduled;
    private int _toolSpinnerFrame;
    private double _transcriptFontSize = 14;
    private double _promptFontSize = 14;
    private double _docSourceFontSize = 12;
    private double _docPreviewScrollY;
    private readonly List<Image> _toolIconImages = [];
    private readonly HashSet<TranscriptResponseEntry> _pendingResponseEntryRenders = [];
    private readonly PostedUiActionTracker _postedUiActionTracker = new();
    private readonly UiActionReplayRegistry _uiActionReplayRegistry = new();
    private readonly FixtureLoaderRegistry _fixtureLoaderRegistry = new();
    private readonly Queue<DelegationOutcomeTelemetry> _recentDelegationOutcomes = new();
    // _activeToolName moved to PromptExecutionController.ActiveToolName
    private AgentThreadRegistry _agentThreadRegistry = null!;
    private BackgroundTaskPresenter _backgroundTaskPresenter = null!;
    private TranscriptConversationManager _conversationManager = null!;
    private MarkdownDocumentRenderer _markdownRenderer = null!;
    private TranscriptScrollController _scrollController = null!;
    private bool _modelObservedThisSession;
    private readonly Queue<(string Text, Brush? Brush)> _deferredSystemLines = new();
    private string? _currentSessionState;
    // _clearConfirmationPending, _universeSelectionPending moved to PromptExecutionController
    private readonly SquadCliAdapter _squadCliAdapter;
    private readonly IWorkspacePaths _workspacePaths;
    private readonly ScreenshotRefreshOptions _screenshotRefreshOptions;
    private string? _lastHandledRestartRequestId;
    private TranscriptThreadState? _coordinatorThread;
    private TranscriptThreadState? _selectedTranscriptThread;
    private UiExceptionPanelState? _activeUiException;

    // ── Transcript search state ─────────────────────────────────────────────────
    private IReadOnlyList<TurnSearchMatch> _searchMatches = [];
    private int _searchMatchCursor = -1;
    private CancellationTokenSource? _searchCts;
    private DispatcherTimer? _searchDebounceTimer;
    private SearchHighlightAdorner? _searchAdorner;
    private ScrollbarMarkerAdorner? _scrollbarAdorner;
    // Pointer cache — built on first RefreshAdornerHighlights after a search, reused on Next/Prev.
    private List<(TextPointer Start, TextPointer End, string Text)>? _cachedSearchPointers;
    // Set true while navigating to a match in a different thread to suppress search-state clear.
    private bool _searchNavigating;
    private int[] _cachedMatchToCursor = [];  // match i → index in _cachedSearchPointers, -1 if BUC/skip
    private TextPointer?[] _cachedMatchScrollPointer = [];  // match i → pointer to scroll to
    private TextBlock?[] _cachedMatchBucCell = [];  // match i → BUC table cell, null if not a BUC match
    // TextBlocks inside table cells that currently carry a search-highlight background.
    private readonly HashSet<TextBlock> _bucHighlightedCells = [];
    private ScrollBar? _transcriptScrollBar;
    private string? _lastAgentImageFolder;
    private ScrollViewer? _transcriptScrollViewer;

    // ── Doc source find-in-source bar state ────────────────────────────────────
    private Border? _docSourceFindBar;
    private TextBox? _docSourceFindTextBox;
    private TextBlock? _docSourceFindMatchCount;
    private Canvas? _docSourceFindOverlay;
    private DispatcherTimer? _docSourceFindDebounceTimer;
    private List<int> _docSourceFindMatches = [];
    private int _docSourceFindCurrentIndex = -1;
    private Canvas? _docSourceOverlayCanvas;     // persistent overlay canvas for highlights
    private Shapes.Rectangle? _docSourceHoverHighlight;
    private DispatcherTimer? _docSourceHoverTimer;
    private int _loopCurrentIteration;
    private DateTimeOffset _loopNextIterationAt;
    private bool _loopIsWaiting;
    private bool _loopPanelVisible = true;
    private bool _loopOutputHasContent;
    private bool _loopQueued;
    private bool _tasksPanelVisible = false;
    private string? _watchCycleId;
    private int _watchFleetSize;
    private int _watchWaveIndex;
    private int _watchWaveCount;
    private int _watchAgentCount;
    private string? _watchPhase;
    private bool _remoteAccessActive;
    private MenuItem? _remoteAccessMenuItem;
    private ApplicationSettingsSnapshot _settingsSnapshot = ApplicationSettingsSnapshot.Empty;
    private string _activeThemeName = "Light";
    private readonly long _processStartedAtUtcTicks = ProcessIdentity.GetCurrentProcessStartedAtUtcTicks();
    private readonly string? _startupFolderArgument;
    private WorkspaceOwnershipLease? _startupWorkspaceLease;
    private WorkspaceOwnershipLease? _workspaceOwnershipLease;
    private bool _startupInitialized;
    private (string FolderPath, WorkspaceWindowPlacement Placement)? _pendingWindowPlacement;
    private (bool TasksOpen, bool TraceOpen)? _pendingUtilityWindowState;
    private (bool Open, List<string>? ExpandedNodes, string? SelectedTopic, double? DocsPanelWidth, double? DocsTopicsWidth, double? DocsPanelWidthFraction, double? DocsTopicsWidthFraction, bool? DocsSourceOpen, double? DocsSourceWidth)? _pendingDocsPanelState;
    private WorkspaceDocsPanelState? _docsPanelState; // loaded at startup, updated on save
    // _currentPromptStartedAt, _lastPromptActivityAt, _promptNoActivityWarningShown,
    // _promptStallWarningShown moved to PromptExecutionController

    private TranscriptThreadState CoordinatorThread => _coordinatorThread ??= CreateCoordinatorTranscriptThread();
    private bool IsLoopRunning => _pec is { IsLoopRunning: true };
    private bool IsNativeLoopRunning => IsLoopRunning && _settingsSnapshot.LoopMode == LoopMode.NativeAgents;
    private TranscriptTurnView? _currentTurn
    {
        get => CoordinatorThread.CurrentTurn;
        set => CoordinatorThread.CurrentTurn = value;
    }

    private sealed class SecondaryTranscriptEntry
    {
        public AgentStatusCard Agent { get; set; } = null!;
        public TranscriptThreadState Thread { get; init; } = null!;
        public RichTextBox TranscriptBox { get; init; } = null!;
        public TextBlock TitleBlock { get; init; } = null!;
        public Button NavUpButton { get; init; } = null!;
        public Button NavDownButton { get; init; } = null!;
        public Button CloseButton { get; init; } = null!;
        public Border PanelBorder { get; init; } = null!;
        public Grid ContentGrid { get; init; } = null!;
        public TranscriptScrollController ScrollController { get; init; } = null!;
        public DispatcherTimer? CountdownTimer { get; set; }
        public int CountdownSecondsRemaining { get; set; }
        public TextBlock? CountdownOverlay { get; set; }
        public bool IsAutoOpenedInMultiMode { get; set; }
        public bool CountdownCancelled { get; set; }
        public bool CountdownStarted { get; set; }
        public DispatcherTimer? PostponeTimer { get; set; }
    }

    private readonly List<SecondaryTranscriptEntry> _secondaryTranscripts = new();
    private DispatcherTimer? _transcriptTitleRefreshTimer;
    private DispatcherTimer? _completedTimeFooterTimer;
    private TranscriptSelectionController _selectionController = null!; // initialized in constructor
    private HashSet<AgentStatusCard> _prevActiveAgentCards = new();
    private bool _mainTranscriptVisible = true;

    // Push-to-talk state
    private enum PttState { Idle, TapDown, TapReleased, Active }
    private PttState _pttState = PttState.Idle;
    private bool _promptHasVoiceInput;
    private bool _pttHadPreexistingText;
    private bool _pttShiftTappedDuringRecording;
    private bool _voiceStartedWithSendEnabled;
    private DateTime _ctrlFirstDownTime;
    private DateTime _ctrlFirstReleaseTime;
    private SpeechRecognitionService? _speechService;
    private PushToTalkWindow? _pttWindow;
    private TextBox? _pttTargetTextBox;   // resolved at activation; null = PromptTextBox
    private int _sessionCaretIndex;       // caret captured before PTT panel becomes visible
    private int _sessionSelectionLength;  // selection length captured before PTT panel becomes visible
    private DispatcherTimer? _promptNavHintTimer;
    private string? _workspaceGitHubUrl;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private const int PttMaxTapHoldMs = 250;
    const int PttDoubleClickTime = 350;

    private sealed record UiExceptionPanelState(
        string Title,
        string Summary,
        string Details);

    internal MainWindow(string? startupFolder = null, WorkspaceOwnershipLease? startupWorkspaceLease = null, IWorkspacePaths? workspacePaths = null, ScreenshotRefreshOptions? screenshotRefreshOptions = null)
    {
        _workspacePaths = workspacePaths ?? WorkspacePathsProvider.Discover();
        _screenshotRefreshOptions = screenshotRefreshOptions ?? ScreenshotRefreshOptions.None;
        _bridge = new SquadSdkProcess(_workspacePaths);
        _startupFolderArgument = startupFolder;
        _startupWorkspaceLease = startupWorkspaceLease;
        _squadCliAdapter = new SquadCliAdapter(_workspacePaths, (op, ex) => HandleUiCallbackException(op, ex));
        _workspaceOpenCoordinator = new WorkspaceOpenCoordinator(_instanceRegistry);
        _pushNotificationService = new PushNotificationService(_settingsStore);
        InitializeComponent();
        _scrollController = new TranscriptScrollController(OutputTextBox, Dispatcher);
        _scrollController.SetScrollToBottomButton(ScrollToBottomButton);
        _agentThreadRegistry = new AgentThreadRegistry(
            beginTranscriptTurn: (thread, prompt) => BeginTranscriptTurn(thread, prompt),
            finalizeCurrentTurnResponse: thread => FinalizeCurrentTurnResponse(thread),
            collapseCurrentTurnThinking: thread => CollapseCurrentTurnThinking(thread),
            renderToolEntry: entry => RenderToolEntry(entry),
            updateToolSpinnerState: () => UpdateToolSpinnerState(),
            syncActiveToolName: () => SyncActiveToolName(),
            syncThreadChip: thread => SyncThreadChip(thread),
            syncTaskToolTranscriptLink: thread => SyncTaskToolTranscriptLink(thread),
            appendText: (thread, text) => AppendText(thread, text),
            syncAgentCards: () => RefreshAgentCards(),
            syncAgentCardsWithThreads: () => SyncAgentCardsWithThreads(),
            getKnownTeamAgentDescriptors: () => GetKnownTeamAgentDescriptors(),
            updateTranscriptThreadBadge: () => UpdateTranscriptThreadBadge(),
            isThreadActiveForDisplay: thread => _backgroundTaskPresenter.IsThreadActiveForDisplay(thread),
            observeBackgroundAgentActivity: (thread, reason) => _backgroundTaskPresenter.ObserveBackgroundAgentActivity(thread, reason),
            renderConversationHistory: (thread, turns) => _conversationManager.RenderConversationHistoryAsync(thread, turns),
            resolveBackgroundAgentDisplayLabel: agent => _backgroundTaskPresenter.ResolveBackgroundAgentDisplayLabel(agent),
            buildAgentLabel: thread => BackgroundTaskPresenter.BuildBackgroundAgentLabel(thread));
        _backgroundTaskPresenter = new BackgroundTaskPresenter(
            agentThreadRegistry: _agentThreadRegistry,
            appendLine: (text, brush) => AppendLine(text, brush),
            syncAgentCards: () => SyncAgentCardsWithThreads(),
            isPromptRunning: () => _isPromptRunning,
            currentTurn: () => _currentTurn,
            themeBrush: key => ThemeBrush(key),
            tryPostToUi: (action, source) => TryPostToUi(action, source),
            isClosing: () => _isClosing,
            updateLeadAgent: (status, bubble, detail) => UpdateLeadAgent(status, bubble, detail),
            updateSessionState: state => UpdateSessionState(state),
            persistAgentThreadSnapshot: thread => _conversationManager.PersistAgentThreadSnapshot(thread),
            currentTurnSnapshot: () => new CurrentTurnStatusSnapshot(
                                                  _isPromptRunning,
                                                  _pec.PromptNoActivityWarningShown,
                                                  _pec.PromptStallWarningShown,
                                                  _pec.CurrentPromptStartedAt,
                                                  _pec.LastPromptActivityAt,
                                                  _pec.LastPromptActivityName,
                                                  _pec.SessionReadyAt,
                                                  _pec.FirstToolAt,
                                                  _pec.FirstResponseAt,
                                                  _pec.LastResponseAt,
                                                  _pec.ResponseDeltaCount,
                                                  _pec.ResponseCharacterCount,
                                                  _pec.LongestResponseGap,
                                                  _pec.AverageResponseGap,
                                                  _pec.FirstThinkingTextAt,
                                                  _pec.LastThinkingTextAt,
                                                  _pec.ThinkingDeltaCount,
                                                  _pec.ThinkingTextDeltaCount,
                                                  _pec.ThinkingCharacterCount,
                                                  _pec.LongestThinkingGap,
                                                  _pec.AverageThinkingGap,
                                                  _pec.ToolStartCount,
                                                  _pec.ToolCompleteCount),
            agentActiveDisplayLinger: AgentActiveDisplayLinger,
            dynamicAgentHistoryRetention: DynamicAgentHistoryRetention);
        _conversationManager = new TranscriptConversationManager(
            getWorkspace: () => _currentWorkspace,
            getPromptText: () => PromptTextBox.Text,
            setPromptText: (text, caretIndex, selectionStart, selectionLength) => {
                PromptTextBox.Text = text;
                if (selectionLength > 0)
                    PromptTextBox.Select(selectionStart, selectionLength);
                else
                    PromptTextBox.CaretIndex = caretIndex;
            },
            getPromptCaretState: () => (PromptTextBox.CaretIndex, PromptTextBox.SelectionStart, PromptTextBox.SelectionLength),
            isClosing: () => _isClosing,
            renderPersistedTurn: (thread, turn, isLast) => RenderPersistedTurn(thread, turn, isLast),
            coordinatorThread: () => CoordinatorThread,
            selectedThread: () => _selectedTranscriptThread,
            maybePublishRoutingIssue: reason => MaybePublishRoutingIssueSystemEntry(reason),
            syncAgentCardsWithThreads: () => SyncAgentCardsWithThreads(),
            dispatcher: Dispatcher,
            scrollOutputToEnd: () =>
            {
                // During initial history load IsLoadingTranscript is true — route to EndLoad()
                // so the suppression flag is cleared and exactly one post-load scroll fires.
                // Outside of load (normal streaming) IsLoadingTranscript is false — use the
                // standard debounced RequestScrollToEnd path.
                if (_scrollController.IsLoadingTranscript)
                {
                    _scrollController.EndLoad();
                    LoadingTranscriptOverlay.Visibility = Visibility.Collapsed;
                }
                else
                    _scrollController.RequestScrollToEnd();
            },
            agentThreadRegistry: _agentThreadRegistry,
            getToolEntries: () => _agentThreadRegistry.ToolEntries,
            getCurrentTurn: () => _currentTurn,
            setCurrentTurnNull: () => { _currentTurn = null; },
            // Bracket bulk history-load in a RichTextBox BeginChange/EndChange pair.
            // This prevents the WPF TextEditor from issuing a layout pass after every
            // Blocks.Add call; instead exactly one layout pass fires when EndChange() is
            // called, collapsing N layout invalidations into one.
            beginBulkDocumentLoad: () => OutputTextBox.BeginChange(),
            endBulkDocumentLoad: () => OutputTextBox.EndChange(),
            prependTurnsBatch: (thread, turns) => PrependPersistedTurnsBatch(thread, turns),
            getScrollableHeight: () => _scrollController.GetScrollableHeight(),
            getVerticalOffset: () => _scrollController.GetVerticalOffset(),
            scrollToAbsoluteOffset: target => _scrollController.ScrollToAbsoluteOffset(target),
            updateScrollLayout: () => OutputTextBox.UpdateLayout());
        // Wire the near-top prepend trigger: when the user scrolls within 400 px of the
        // top of the coordinator transcript, TranscriptScrollController calls this to
        // load the next batch of older turns from the virtual window.
        _scrollController.RequestPrependOlderTurns =
            () => _ = _conversationManager.PrependOlderTurnsAsync();
        _instanceActivationChannel = new InstanceActivationChannel(
            _workspacePaths.ApplicationRoot,
            Environment.ProcessId,
            _processStartedAtUtcTicks,
            () => TryPostToUi(ActivateOwnedWindow, "InstanceActivation.Request"),
            ex => SquadDashTrace.Write("Workspace", $"Activation listener failed: {ex.Message}"));
        _instanceActivationChannel.Start();

        ActiveAgentItemsControl.ItemsSource = _activeAgentCards;
        InactiveAgentItemsControl.ItemsSource = _inactiveAgentCards;
        _activeAgentCards.CollectionChanged += (_, _) =>
        {
            try { HandleActiveAgentCountdownCheck(); }
            catch (Exception ex) { HandleUiCallbackException("_activeAgentCards.CollectionChanged", ex); }
        };
        _selectionController = new TranscriptSelectionController(_agents);
        _selectionController.OpenPanelRequested += (card, thread, isAuto) =>
            OpenSecondaryPanel(card, thread, isAutoOpenedInMultiMode: isAuto);
        _selectionController.ClosePanelRequested += (card, thread) =>
        {
            var entry = _secondaryTranscripts.FirstOrDefault(e => e.Agent == card && e.Thread == thread);
            if (entry is not null) CloseSecondaryPanel(entry);
        };
        _selectionController.ShowMainRequested += () =>
        {
            ShowMainTranscript();
            SelectTranscriptThread(CoordinatorThread);
        };
        _selectionController.HideMainRequested += HideMainTranscript;
        StatusAgentPanelsGrid.SizeChanged += (_, e) =>
        {
            try
            {
                UpdateAgentPanelWidths();
                SquadDashTrace.Write("AgentCards",
                    $"SizeChanged: H {e.PreviousSize.Height:F0} → {e.NewSize.Height:F0} " +
                    $"W {e.PreviousSize.Width:F0} → {e.NewSize.Width:F0} | " +
                    $"StackTrace caller: {new System.Diagnostics.StackTrace(1, false).GetFrame(0)?.GetMethod()?.Name ?? "?"}");
            }
            catch (Exception ex)
            {
                HandleUiCallbackException("StatusAgentPanelsGrid.SizeChanged", ex);
            }
        };

        OutputTextBox.Document = CoordinatorThread.Document;
        OutputTextBox.CommandBindings.Add(new CommandBinding(
            System.Windows.Input.ApplicationCommands.Copy,
            OutputTextBox_CopyExecuted,
            OutputTextBox_CopyCanExecute));
        ApplyTranscriptFontSize();
        ApplyPromptFontSize();
        SelectTranscriptThread(CoordinatorThread);

        _historyHintTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _historyHintTimer.Tick += (_, _) =>
        {
            try
            {
                HideHistoryHint();
            }
            catch (Exception ex)
            {
                HandleUiCallbackException("HistoryHintTimer.Tick", ex);
            }
        };

        _toolSpinnerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _toolSpinnerTimer.Tick += (_, _) =>
        {
            try
            {
                AdvanceToolSpinner();
            }
            catch (Exception ex)
            {
                HandleUiCallbackException("ToolSpinnerTimer.Tick", ex);
            }
        };

        _promptHealthTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        // Tick handler wired by PromptExecutionController

        _statusPresentationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _statusPresentationTimer.Tick += (_, _) =>
        {
            try
            {
                RefreshStatusPresentation();
            }
            catch (Exception ex)
            {
                HandleUiCallbackException("StatusPresentationTimer.Tick", ex);
            }
        };
        _statusPresentationTimer.Start();

        _responseRenderTimer = new DispatcherTimer
        {
            Interval = ResponseRenderCadence
        };
        _responseRenderTimer.Tick += (_, _) =>
        {
            try
            {
                FlushPendingResponseEntryRenders();
            }
            catch (Exception ex)
            {
                HandleUiCallbackException("ResponseRenderTimer.Tick", ex);
            }
        };

        _teamRefreshDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _teamRefreshDebounceTimer.Tick += TeamRefreshDebounceTimer_Tick;

        _bridge.EventReceived += (_, evt) => TryPostToUi(() => HandleEvent(evt), "Bridge.EventReceived");
        _bridge.ErrorReceived += (_, text) => TryPostToUi(() => HandleBridgeError(text), "Bridge.ErrorReceived");

        UpdateStatusTitle();
        UpdateLeadAgent("Ready", string.Empty, string.Empty);
        UpdateSessionState("Ready");

        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        Loaded += MainWindow_Loaded;
        ContentRendered += MainWindow_ContentRendered;
        Activated += MainWindow_Activated;
        LocationChanged += (_, _) =>
        {
            try
            {
                OnMainWindowMoved();
            }
            catch (Exception ex)
            {
                HandleUiCallbackException("Window.LocationChanged", ex);
            }
        };
        SizeChanged += (_, _) =>
        {
            try
            {
                OnMainWindowMoved();
            }
            catch (Exception ex)
            {
                HandleUiCallbackException("Window.SizeChanged", ex);
            }
        };
        StateChanged += (_, _) =>
        {
            try
            {
                OnMainWindowMoved();
                UpdateMaximizeRestoreIcon();
            }
            catch (Exception ex)
            {
                HandleUiCallbackException("Window.StateChanged", ex);
            }
        };

        SourceInitialized += (_, _) =>
        {
            try
            {
                var source = System.Windows.Interop.HwndSource.FromHwnd(
                    new System.Windows.Interop.WindowInteropHelper(this).Handle);
                source?.AddHook(NativeMethods.MaximizeWorkAreaHook);
            }
            catch (Exception ex)
            {
                HandleUiCallbackException("Window.SourceInitialized", ex);
            }
        };

        IntelliSensePopup.PreviewMouseDown += (_, _) =>
        {
            try
            {
                PromptTextBox.Focus();
            }
            catch (Exception ex)
            {
                HandleUiCallbackException("IntelliSensePopup.PreviewMouseDown", ex);
            }
        };

        // ── Search box event wiring ────────────────────────────────────────────
        SearchBox.TextChanged += (_, _) =>
        {
            try
            {
                _searchDebounceTimer?.Stop();
                _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _searchDebounceTimer.Tick += async (_, _) =>
                {
                    try
                    {
                        _searchDebounceTimer?.Stop();
                        _searchDebounceTimer = null;
                        await ExecuteSearchAsync(SearchBox.Text);
                    }
                    catch (Exception ex)
                    {
                        HandleUiCallbackException("SearchDebounceTimer.Tick", ex);
                    }
                };
                _searchDebounceTimer.Start();
            }
            catch (Exception ex)
            {
                HandleUiCallbackException("SearchBox.TextChanged", ex);
            }
        };
        SearchBox.KeyDown += async (_, e) =>
        {
            try
            {
                if (e.Key == Key.Enter)
                {
                    await NavigateToMatchAsync(_searchMatchCursor + 1);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    ClearSearch();
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () => PromptTextBox.Focus());
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                HandleUiCallbackException("SearchBox.KeyDown", ex);
            }
        };
        FindPrevButton.Click += async (_, _) =>
        {
            try
            {
                await NavigateToMatchAsync(_searchMatchCursor - 1);
            }
            catch (Exception ex)
            {
                HandleUiCallbackException("FindPrevButton.Click", ex);
            }
        };
        FindNextButton.Click += async (_, _) =>
        {
            try
            {
                await NavigateToMatchAsync(_searchMatchCursor + 1);
            }
            catch (Exception ex)
            {
                HandleUiCallbackException("FindNextButton.Click", ex);
            }
        };

        _pec = new PromptExecutionController(
            runPromptAsync: (prompt, cwd, sessionId, configDir) => _bridge.RunPromptAsync(prompt, cwd, sessionId, configDir),
            runNamedAgentDelegationAsync: (selectedOption, targetAgentHandle, cwd, sessionId, configDir) =>
                _bridge.RunNamedAgentDelegationAsync(selectedOption, targetAgentHandle, cwd, sessionId, configDir),
            getCurrentWorkspace: () => _currentWorkspace,
            getSettingsSnapshot: () => _settingsSnapshot,
            conversationManager: _conversationManager,
            backgroundTaskPresenter: _backgroundTaskPresenter,
            squadCliAdapter: _squadCliAdapter,
            beginTranscriptTurn: prompt => BeginTranscriptTurn(prompt),
            finalizeCurrentTurnResponse: () => FinalizeCurrentTurnResponse(),
            appendLine: (text, brush) => AppendLine(text, brush),
            selectTranscriptThread: thread => SelectTranscriptThread(thread),
            getCoordinatorThread: () => CoordinatorThread,
            getAgents: () => _agents,
            getCurrentSessionState: () => _currentSessionState,
            getIsPromptRunning: () => _isPromptRunning,
            setIsPromptRunning: v => {
                _isPromptRunning = v;
                if (v)
                {
                    // Clear stale completion timestamp so it doesn't show "Completed just now"
                    // while the new turn is still running.
                    CoordinatorThread.CompletedAt = null;
                }
                else
                {
                    CoordinatorThread.CompletedAt = DateTimeOffset.Now;
                    UpdateCompletedTimeFooters();
                    if (_deferredShutdown == DeferredShutdownMode.AfterCurrentTurn)
                    {
                        // User chose "close after this turn" — don't drain, just close.
                        Close();
                    }
                    else if (_deferredShutdown == DeferredShutdownMode.AfterAllQueued)
                    {
                        if (_promptQueue.Count > 0 && GetAutoDispatchCandidate() is not null)
                            _ = DrainQueueAsync(); // keep draining
                        else
                            Close(); // queue exhausted — shut down
                    }
                    else
                    {
                        if (_promptQueue.Count > 0 && GetAutoDispatchCandidate() is not null)
                            _ = DrainQueueAsync();
                    }
                }
                SyncQueuePanel();
            },
            getIsClosing: () => _isClosing,
            getRestartPending: () => _restartPending,
            close: () => Close(),
            clearPromptTextBox: () => PromptTextBox.Clear(),
            focusPromptTextBox: () => PromptTextBox.Focus(),
            isPromptTextBoxEnabled: () => PromptTextBox.IsEnabled,
            getPendingRoutingRepairRecheck: () => _pendingRoutingRepairRecheck,
            setPendingRoutingRepairRecheck: v => _pendingRoutingRepairRecheck = v,
            getPendingSupplementalInstruction: () => _pendingSupplementalPromptInstruction,
            clearPendingSupplementalInstruction: () => { _pendingSupplementalPromptInstruction = null; },
            getPendingQuickReplyRoutingInstruction: () => _pendingQuickReplyRoutingInstruction,
            getPendingQuickReplyRouteMode: () => _pendingQuickReplyLaunch?.RouteMode,
            updateInteractiveControlState: () => UpdateInteractiveControlState(),
            updateLeadAgent: (status, bubble, detail) => UpdateLeadAgent(status, bubble, detail),
            updateSessionState: state => UpdateSessionState(state),
            refreshAgentCards: () => RefreshAgentCards(),
            refreshSidebar: () => RefreshSidebar(),
            setInstallStatus: msg => SetInstallStatus(msg),
            canShowOwnedWindow: () => CanShowOwnedWindow(),
            showTextWindow: (title, content) => ShowTextWindow(title, content),
            clearSessionView: () => ClearSessionView(),
            showTasksStatusWindow: () => ShowTasksStatusWindow(),
            hideTasksStatusWindow: () => HideTasksStatusWindow(),
            showLiveTraceWindow: () => ShowTraceWindow(),
            runDoctor: () => RunDoctorButton_Click(null!, null!),
            showHireAgentWindow: () => ShowHireAgentWindow(),
            showScreenshotOverlay: () => ShowScreenshotOverlay(),
            showRuntimeIssue: msg => ShowRuntimeIssue(msg),
            clearRuntimeIssue: () => ClearRuntimeIssue(),
            waitForRoutingRepairSettleAsync: () => WaitForRoutingRepairStateToSettleAsync(),
            maybePublishRoutingIssue: (reason, force) => MaybePublishRoutingIssueSystemEntry(reason, force),
            promptHealthTimer: _promptHealthTimer,
            waitForPostedUiActionsAsync: () => _postedUiActionTracker.WaitForDrainAsync(),
            getModelObservedThisSession: () => _modelObservedThisSession,
            getLastQuickReplyEntry: () => _lastQuickReplyEntry,
            setLastQuickReplyEntryNull: () => { _lastQuickReplyEntry = null; },
            renderResponseEntry: entry => RenderResponseEntry(entry),
            ensureThreadFooterAtEnd: thread => EnsureThreadFooterAtEnd(thread),
            scrollToEndIfAtBottom: () => ScrollToEndIfAtBottom(),
            getToolEntries: () => _agentThreadRegistry.ToolEntries.Values,
            renderToolEntry: entry => RenderToolEntry(entry),
            updateToolSpinnerState: () => UpdateToolSpinnerState(),
            workspacePaths: _workspacePaths);

        _loopController = new LoopController(
            // ExecutePromptAsync accesses WPF components — must run on the UI thread.
            executePromptAsync: (prompt, sessionId) =>
                Dispatcher.InvokeAsync(() =>
                    _pec.ExecutePromptAsync(
                        prompt,
                        addToHistory: false,
                        clearPromptBox: false,
                        sessionIdOverride: sessionId))
                .Task.Unwrap(),
            abortPrompt: () => _bridge.AbortPrompt(),
            onIterationStarted: n =>
                Dispatcher.Invoke(() => OnNativeLoopIterationStarted(n)),
            onStopped: () =>
                Dispatcher.Invoke(OnNativeLoopStopped),
            onError: msg =>
                Dispatcher.Invoke(() => OnNativeLoopError(msg)),
            onIterationCompleted: n =>
                Dispatcher.Invoke(() => OnNativeLoopIterationCompleted(n)),
            onWaiting: nextAt =>
                Dispatcher.Invoke(() => OnNativeLoopWaiting(nextAt)),
            onBeforeWait: () =>
                Dispatcher.InvokeAsync(() => DrainQueueIfNeededAsync())
                          .Task.Unwrap());

        _markdownRenderer = new MarkdownDocumentRenderer(
            getFontSize: () => _transcriptFontSize,
            getWorkspaceGitHubUrl: () => _workspaceGitHubUrl,
            onLinkClicked: target =>
            {
                if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    _ = OpenExternalLinkWithCommitCheckAsync(target);
                else
                    OpenTranscriptThread(target, scrollToStart: true);
            },
            onException: (op, ex) => HandleUiCallbackException(op, ex),
            resolveContinuationThread: entry => TryResolveQuickReplyContinuationThread(entry),
            onQuickReplyButtonClick: QuickReplyButton_Click,
            appendResponseSegment: (thread, text, newLine) => AppendResponseSegment(thread, text, newLine),
            scrollToEndIfAtBottom: thread => ScrollToEndIfAtBottom(thread),
            getCoordinatorThread: () => CoordinatorThread);

        RegisterUiReplayActions();
        RegisterFixtureLoaders();
    }

    // ── Replay action registration ──────────────────────────────────────────

    /// <summary>
    /// Registers all <see cref="IReplayableUiAction"/> instances with
    /// <see cref="_uiActionReplayRegistry"/> at startup.
    /// Add new concrete actions here as they are implemented in Phase 3.
    /// </summary>
    private void RegisterUiReplayActions()
    {
        _uiActionReplayRegistry.Register(new OpenPreferencesWindowAction(
            openPreferencesWindow: () => PreferencesMenuItem_Click(this, new System.Windows.RoutedEventArgs()),
            getPreferencesWindow: () => _preferencesWindow is { IsVisible: true } ? _preferencesWindow : null));
    }

    // ── Fixture loader registration ─────────────────────────────────────────

    /// <summary>
    /// Registers all <see cref="IFixtureLoader"/> implementations with
    /// <see cref="_fixtureLoaderRegistry"/> at startup.
    /// Each loader receives only the references it needs via constructor injection;
    /// none of them hold a back-reference to <c>MainWindow</c>.
    /// </summary>
    private void RegisterFixtureLoaders()
    {
        // windowGeometry must be first — all layout-dependent loaders require the window
        // to be at its target geometry before they run.
        _fixtureLoaderRegistry.Register("windowGeometry", new WindowGeometryFixtureLoader(
            mainWindow: this,
            dispatcher: Dispatcher));

        // viewMode must come before agentCard — view mode controls panel visibility, which
        // affects agent-card layout.
        _fixtureLoaderRegistry.Register("viewMode", new ViewModeFixtureLoader(
            getTranscriptFullScreen: () => _transcriptFullScreenEnabled,
            setTranscriptFullScreen: v => { _transcriptFullScreenEnabled = v; ApplyViewMode(); },
            dispatcher: Dispatcher));

        // agentOrder must come after viewMode (panel visibility is set) and before agentCard
        // (card state depends on cards already being in their final positions).
        _fixtureLoaderRegistry.Register("agentOrder", new AgentOrderFixtureLoader(
            agents: _agents,
            dispatcher: Dispatcher));

        _fixtureLoaderRegistry.Register("agentCard", new AgentCardFixtureLoader(
            agents: _agents,
            dispatcher: Dispatcher));

        _fixtureLoaderRegistry.Register("transcript", new TranscriptFixtureLoader(
            getCoordinatorThread: () => CoordinatorThread,
            scrollController: _scrollController,
            dispatcher: Dispatcher,
            repoRoot: _workspacePaths.ApplicationRoot));

        _fixtureLoaderRegistry.Register("voiceFeedback", new VoiceFeedbackFixtureLoader(
            promptTextBox: PromptTextBox,
            ownerWindow: this,
            dispatcher: Dispatcher));

        _fixtureLoaderRegistry.Register("backgroundTask", new BackgroundTaskFixtureLoader(
            getBackgroundAgents: () => _backgroundTaskPresenter.BackgroundAgents,
            setBackgroundAgents: agents => _backgroundTaskPresenter.BackgroundAgents = agents,
            refreshDisplay: () => _backgroundTaskPresenter.RefreshLeadAgentBackgroundStatus(),
            dispatcher: Dispatcher));

        _fixtureLoaderRegistry.Register("quickReplies", new QuickReplyFixtureLoader(
            getCoordinatorThread: () => CoordinatorThread,
            dispatcher: Dispatcher));

        // scrollPosition must come after agentCard — agent-card layout must be settled
        // before scroll positions are meaningful.
        _fixtureLoaderRegistry.Register("scrollPosition", new ScrollPositionFixtureLoader(
            getTranscriptOffset: () => _scrollController.GetVerticalOffset(),
            setTranscriptOffset: v => _scrollController.ScrollToAbsoluteOffset(v),
            getActiveRosterOffset: () => ActiveAgentsScrollViewer.HorizontalOffset,
            setActiveRosterOffset: v => ActiveAgentsScrollViewer.ScrollToHorizontalOffset(v),
            getInactiveRosterOffset: () => InactiveAgentsScrollViewer.HorizontalOffset,
            setInactiveRosterOffset: v => InactiveAgentsScrollViewer.ScrollToHorizontalOffset(v),
            dispatcher: Dispatcher));

        // promptText must come after scrollPosition — complete UI layout should be
        // established before the prompt text is set.
        _fixtureLoaderRegistry.Register("promptText", new PromptTextFixtureLoader(
            promptTextBox: PromptTextBox,
            dispatcher: Dispatcher));
    }

    private void AddWorkspaceMenuSeparator()
    {
        WorkspaceMenuItem.Items.Add(new Separator
        {
            Style = (Style)FindResource("ThemedMenuSeparatorStyle")
        });
    }
    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        try
        {
            ContentRendered -= MainWindow_ContentRendered;
            UpdateAgentPanelWidths();
            SquadDashTrace.Write(
                "Startup",
                $"ContentRendered: ActiveH={ActiveAgentItemsControl.ActualHeight:F0} ActiveViewport={ActiveAgentsScrollViewer.ActualHeight:F0} " +
                $"InactiveH={InactiveAgentItemsControl.ActualHeight:F0} InactiveViewport={InactiveAgentsScrollViewer.ActualHeight:F0} RootH={StatusAgentPanelsGrid.ActualHeight:F0}");
            if (ActiveAgentItemsControl.ActualHeight < 1 || InactiveAgentItemsControl.ActualHeight < 1)
                ScheduleAgentPanelLayoutRefresh();
            TryNudgeAgentLaneLayout();
            SquadDashTrace.Write(
                "Startup",
                $"ContentRendered post-refresh: ActiveH={ActiveAgentItemsControl.ActualHeight:F0} ActiveViewport={ActiveAgentsScrollViewer.ActualHeight:F0} " +
                $"InactiveH={InactiveAgentItemsControl.ActualHeight:F0} InactiveViewport={InactiveAgentsScrollViewer.ActualHeight:F0} RootH={StatusAgentPanelsGrid.ActualHeight:F0}");
            PromptTextBox.Focus();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(MainWindow_ContentRendered), ex);
        }
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        try
        {
            // Re-sync scroll state on every activation so that an RDP reconnect —
            // which can leave the transcript viewport at the top without firing the
            // events that normally show/hide the scroll-to-bottom button — is corrected
            // the moment the user sees the window.
            _scrollController.SyncScrollState();

            if (!_pendingPowerShellInstallRecheck)
                return;

            _pendingPowerShellInstallRecheck = false;
            RefreshInstallationState();
            if (WorkspaceIssueFactory.IsPowerShellAvailable() &&
                WorkspaceIssueFactory.IsMissingPowerShellIssue(_runtimeIssue))
            {
                ClearRuntimeIssue();
                RefreshInstallationState();
                SetInstallStatus("PowerShell 7 was detected. Setup looks good now.");
            }
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(MainWindow_Activated), ex);
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Wire the search highlight adorner unconditionally (idempotent).
            if (_searchAdorner is null)
            {
                var adornerLayer = AdornerLayer.GetAdornerLayer(OutputTextBox);
                if (adornerLayer is not null)
                {
                    _searchAdorner = new SearchHighlightAdorner(OutputTextBox);
                    adornerLayer.Add(_searchAdorner);
                }
            }

            // Wire the scrollbar marker adorner onto the vertical ScrollBar inside OutputTextBox.
            if (_scrollbarAdorner is null)
            {
                _transcriptScrollViewer = FindScrollViewer(OutputTextBox);
                if (_transcriptScrollViewer is not null)
                {
                    _transcriptScrollBar =
                        _transcriptScrollViewer.Template?.FindName("PART_VerticalScrollBar", _transcriptScrollViewer) as ScrollBar
                        ?? FindVerticalScrollBar(_transcriptScrollViewer);

                    if (_transcriptScrollBar is not null)
                    {
                        var sbLayer = AdornerLayer.GetAdornerLayer(_transcriptScrollBar);
                        if (sbLayer is not null)
                        {
                            _scrollbarAdorner = new ScrollbarMarkerAdorner(_transcriptScrollBar);
                            sbLayer.Add(_scrollbarAdorner);
                        }
                    }
                }
            }

            if (_startupInitialized)
                return;

            _startupInitialized = true;
            SquadDashTrace.Write("Startup", "MainWindow loaded. Beginning deferred startup initialization.");

            ConfigureRestartRequestWatcher();
            InitializeWorkspace(_startupFolderArgument);
            RestoreUtilityWindowVisibility();
            await _squadCliAdapter.ResolveSquadVersionAsync();
            UpdateStatusTitle();
            _ = _squadCliAdapter.CheckForSquadUpdateAsync().ContinueWith(_ => Dispatcher.Invoke(UpdateSquadUpdateBadge));
            SquadDashTrace.Write("Startup", "Deferred startup initialization completed.");

            // Give the prompt text box focus on startup so the user can type immediately.
            if (PromptTextBox.IsVisible)
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () => PromptTextBox.Focus());

            // Screenshot refresh mode: run the automated pass then shut down.
            if (_screenshotRefreshOptions.Mode != ScreenshotRefreshMode.None)
                await RunScreenshotRefreshAsync(_screenshotRefreshOptions);
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("Startup", $"Deferred startup initialization failed: {ex}");
            HandleUiCallbackException(nameof(MainWindow_Loaded), ex);
        }
    }

    // ── Screenshot refresh mode ─────────────────────────────────────────────────────

    private async Task RunScreenshotRefreshAsync(ScreenshotRefreshOptions options)
    {
        SquadDashTrace.Write("Screenshot", $"Starting refresh run: Mode={options.Mode} Target={options.TargetName ?? "(all)"}");

        try
        {
            var screenshotsDir = _workspacePaths.ScreenshotsDirectory;
            var definitions = await ScreenshotDefinitionRegistry.LoadAsync(screenshotsDir).ConfigureAwait(true);
            var runner = new ScreenshotRefreshRunner(
                definitions,
                _uiActionReplayRegistry,
                _fixtureLoaderRegistry,
                screenshotsDir,
                applyThemeAsync: async name =>
                {
                    await Dispatcher.InvokeAsync(() => ApplyTheme(name));
                });

            runner.CaptureRequested += OnScreenshotCaptureRequested;

            try
            {
                await runner.RunAsync(options).ConfigureAwait(true);
            }
            finally
            {
                runner.CaptureRequested -= OnScreenshotCaptureRequested;
            }
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("Screenshot", $"Refresh run failed: {ex}");
            Console.Error.WriteLine($"[screenshot] Refresh aborted: {ex.Message}");
        }
        finally
        {
            SquadDashTrace.Write("Screenshot", "Refresh run complete — shutting down.");
            Application.Current.Shutdown();
        }
    }

    private void OnScreenshotCaptureRequested(object? sender, ScreenshotCaptureRequestedEventArgs e)
    {
        try
        {
            // The runner may raise this event from a thread-pool thread (ConfigureAwait(false)
            // inside RunOneAsync). All WPF visual-tree access and RenderTargetBitmap work must
            // run on the dispatcher thread — so we marshal the entire capture body through
            // Dispatcher.Invoke, including the render-flush pass.
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // Allow WPF to finish any pending layout/rendering before capturing.
                    Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                    var dpi = VisualTreeHelper.GetDpi(this);
                    var pxW = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
                    var pxH = (int)Math.Round(ActualHeight * dpi.DpiScaleY);

                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        pxW, pxH,
                        dpi.PixelsPerInchX, dpi.PixelsPerInchY,
                        System.Windows.Media.PixelFormats.Pbgra32);
                    rtb.Render(this);
                    rtb.Freeze();

                    // If the definition specified a sub-region, crop the RTB to that area.
                    BitmapSource bitmapToSave = rtb;
                    if (e.CaptureBounds is { } bounds)
                    {
                        var pixelX = (int)Math.Round(bounds.X * bounds.DpiX);
                        var pixelY = (int)Math.Round(bounds.Y * bounds.DpiY);
                        var pixelW = (int)Math.Round(bounds.Width * bounds.DpiX);
                        var pixelH = (int)Math.Round(bounds.Height * bounds.DpiY);

                        // Clamp to the RTB dimensions so we never request an out-of-bounds rect.
                        pixelX = Math.Max(0, Math.Min(pixelX, rtb.PixelWidth - 1));
                        pixelY = Math.Max(0, Math.Min(pixelY, rtb.PixelHeight - 1));
                        pixelW = Math.Max(1, Math.Min(pixelW, rtb.PixelWidth - pixelX));
                        pixelH = Math.Max(1, Math.Min(pixelH, rtb.PixelHeight - pixelY));

                        bitmapToSave = new CroppedBitmap(
                            rtb, new System.Windows.Int32Rect(pixelX, pixelY, pixelW, pixelH));
                    }

                    var dir = Path.GetDirectoryName(e.OutputPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    using var stream = File.Create(e.OutputPath);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmapToSave));
                    encoder.Save(stream);

                    e.SignalSaved();
                }
                catch (Exception ex)
                {
                    e.SignalFailed(ex.Message);
                }
            });
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(OnScreenshotCaptureRequested), ex);
        }
    }

    private void InitializeWorkspace(string? startupFolder)
    {
        _settingsSnapshot = _settingsStore.Load();
        _promptFontSize = Math.Clamp(_settingsSnapshot.PromptFontSize, PromptFontSizeMin, PromptFontSizeMax);
        _transcriptFontSize = Math.Clamp(_settingsSnapshot.TranscriptFontSize, TranscriptFontSizeMin, TranscriptFontSizeMax);
        _docSourceFontSize = Math.Clamp(_settingsSnapshot.DocSourceFontSize, DocSourceFontSizeMin, DocSourceFontSizeMax);
        _squadCliAdapter.LastObservedModel = _settingsSnapshot.LastUsedModel;
        ApplyViewMode();
        ApplyPromptFontSize();
        ApplyTranscriptFontSize();
        ApplyDocSourceFontSize();
        ApplyTheme(_settingsSnapshot.Theme ?? "Light");
        RefreshRecentFoldersMenu(_settingsSnapshot.RecentFolders);
        UpdateVoiceHintVisibility();
        RefreshInstallationState();
        RefreshDeveloperRuntimeIssuePreview();

        var candidateFolder = StartupWorkspaceResolver.Resolve(
            startupFolder,
            _settingsSnapshot.LastOpenedFolder,
            TryGetApplicationRoot());

        if (!string.IsNullOrWhiteSpace(candidateFolder))
        {
            OpenWorkspace(
                candidateFolder,
                rememberFolder: true,
                closeWindowIfActivatedExisting: true,
                showBlockedDialog: false);
            return;
        }

        UpdateWindowTitle();
        RefreshAgentCards();
        RefreshSidebar();
        UpdateInteractiveControlState();
        UpdateRunningInstanceRegistration();
    }

    private string? TryGetApplicationRoot()
    {
        try
        {
            return _workspacePaths.ApplicationRoot;
        }
        catch
        {
            return null;
        }
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentWorkspace is null)
            {
                MessageBox.Show(
                    "Open a folder before sending a prompt.",
                    "No Workspace",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Editing a queued tab → dispatch that specific item directly.
            if (_activeTabId is not null)
            {
                await DispatchQueuedTabAsync(_activeTabId);
                return;
            }

            var prompt = PromptTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(prompt))
                return;

            if (_isPromptRunning || IsNativeLoopRunning)
            {
                EnqueueCurrentPrompt();
                return;
            }

            if (_promptHasVoiceInput)
            {
                _promptHasVoiceInput = false;
                prompt += "\n(some or all of this prompt was dictated by voice)";
            }

            _markdownRenderer.DismissKeyboardHint();
            await _pec.ExecutePromptAsync(prompt, addToHistory: true, clearPromptBox: true);

            // In fullscreen mode the prompt was peeked temporarily — hide it again now.
            if (_transcriptFullScreenEnabled && _fullScreenPromptVisible)
                HideFullScreenPrompt();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException("Run", ex);
        }
    }

    // ── Prompt Queue ──────────────────────────────────────────────────────────

    private void EnqueueCurrentPrompt()
    {
        var text = PromptTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        if (_promptHasVoiceInput)
        {
            _promptHasVoiceInput = false;
            text += "\n(some or all of this prompt was dictated by voice)";
        }

        _promptQueue.Enqueue(text, ++_promptQueueSeq);
        PromptTextBox.Clear();
        SyncQueuePanel();
    }

    private async Task DispatchQueuedTabAsync(string id)
    {
        var item = _promptQueue.Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;

        var prompt = PromptTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt)) return;

        item.Text = prompt;

        // Switch back to Active Draft.
        _activeTabId       = null;
        PromptTextBox.Text = _queuePreEditDraft ?? "";
        _queuePreEditDraft = null;

        if (_isPromptRunning || IsNativeLoopRunning)
        {
            // Coordinator busy — item stays in queue with updated text.
            SyncQueuePanel();
            return;
        }

        _promptQueue.Remove(id);
        SyncQueuePanel();

        try
        {
            await _pec.ExecutePromptAsync(prompt, addToHistory: true, clearPromptBox: false);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(DispatchQueuedTabAsync), ex);
        }
    }

    private PromptQueueItem? GetAutoDispatchCandidate()
    {
        var items = _promptQueue.Items;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i].Id != _activeTabId)
                return items[i];
        }
        return null;
    }

    private async Task DrainQueueAsync()
    {
        if (_isPromptRunning || IsNativeLoopRunning) return;

        var item = GetAutoDispatchCandidate();
        if (item is null)
        {
            await MaybeFireQueuedLoopAsync();
            return;
        }

        var seqNum = item.SequenceNumber;
        _promptQueue.Remove(item.Id);
        SyncQueuePanel();

        AppendLine($"📤 Dispatching queued item #{seqNum}…", (Brush)FindResource("SubtleText"));

        try
        {
            await _pec.ExecutePromptAsync(item.Text, addToHistory: true, clearPromptBox: false);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(DrainQueueAsync), ex);
        }
        // Further drain is triggered by setIsPromptRunning(false) callback.
    }

    private async Task DrainQueueIfNeededAsync()
    {
        while (!_isPromptRunning && !IsNativeLoopRunning)
        {
            var item = GetAutoDispatchCandidate();
            if (item is null) break;

            var seqNum = item.SequenceNumber;
            _promptQueue.Remove(item.Id);
            SyncQueuePanel();

            AppendLine($"📤 Dispatching queued item #{seqNum}…", (Brush)FindResource("SubtleText"));

            try
            {
                await _pec.ExecutePromptAsync(item.Text, addToHistory: true, clearPromptBox: false);
            }
            catch (Exception ex)
            {
                HandleUiCallbackException(nameof(DrainQueueIfNeededAsync), ex);
                break;
            }
        }

        await MaybeFireQueuedLoopAsync();
    }

    private async Task MaybeFireQueuedLoopAsync()
    {
        if (!_loopQueued || _isPromptRunning || IsLoopRunning) return;
        _loopQueued = false;
        SyncLoopPanel();
        try
        {
            AppendLine("▶ Starting queued loop…", (Brush)FindResource("SubtleText"));
            await StartLoopImmediateAsync();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(MaybeFireQueuedLoopAsync), ex);
        }
    }

    private void SyncQueuePanel()
    {
        var items = _promptQueue.Items;
        QueueTabBorder.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueTabStrip.Children.Clear();

        if (items.Count > 0)
        {
            QueueTabStrip.Children.Add(CreateQueueTab(null, "Active Draft"));
            // Render newest item first (left) → oldest item last (far right).
            // Queue drains oldest-first so #1 is always the next to dispatch.
            foreach (var item in items.Reverse())
                QueueTabStrip.Children.Add(CreateQueueTab(item.Id, $"#{item.SequenceNumber}"));
        }

        _conversationManager.UpdateQueuedPromptsState(items.Select(i => i.Text).ToArray());
        SyncSendButton();
    }

    private UIElement CreateQueueTab(string? id, string label)
    {
        bool isActive = _activeTabId == id;

        var textBlock = new TextBlock
        {
            Text              = label,
            FontSize          = 12,
            FontWeight        = isActive ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // SetResourceReference keeps the brush live — updates automatically on theme switch.
        textBlock.SetResourceReference(
            TextBlock.ForegroundProperty,
            isActive ? "LabelText" : "QueueTabInactiveText");

        var tab = new Border
        {
            Padding         = new Thickness(12, 6, 12, 6),
            Margin          = new Thickness(0, 0, 1, 0),
            Cursor          = Cursors.Hand,
            BorderThickness = new Thickness(0, 0, 0, isActive ? 2 : 0),
            Background      = Brushes.Transparent,
            Child           = textBlock,
        };
        if (isActive)
            tab.SetResourceReference(Border.BorderBrushProperty, "QueueTabActiveBorder");
        else
            tab.BorderBrush = Brushes.Transparent;

        if (id is not null)
        {
            var capturedId = id;
            var cm         = new ContextMenu();

            // Activate the tab on right-click so user can see what they're deleting.
            cm.Opened += (_, _) => OnQueueTabClicked(capturedId);

            var deleteItem = new MenuItem { Header = "Delete queued item…" };
            deleteItem.Click += (_, _) => OnQueueTabDeleteConfirm(capturedId, tab);
            cm.Items.Add(deleteItem);
            tab.ContextMenu = cm;
        }

        tab.MouseLeftButtonUp += (_, _) => OnQueueTabClicked(id);
        return tab;
    }

    private void OnQueueTabClicked(string? id)
    {
        if (_activeTabId == id) return;

        // Save current content + caret before switching.
        if (_activeTabId is null)
        {
            _queuePreEditDraft              = PromptTextBox.Text;
            _queuePreEditDraftCaretIndex    = PromptTextBox.CaretIndex;
            _queuePreEditDraftSelectionStart  = PromptTextBox.SelectionStart;
            _queuePreEditDraftSelectionLength = PromptTextBox.SelectionLength;
        }
        else
        {
            var current = _promptQueue.Items.FirstOrDefault(i => i.Id == _activeTabId);
            if (current is not null)
            {
                current.Text            = PromptTextBox.Text;
                current.CaretIndex      = PromptTextBox.CaretIndex;
                current.SelectionStart  = PromptTextBox.SelectionStart;
                current.SelectionLength = PromptTextBox.SelectionLength;
            }
        }

        _activeTabId = id;

        if (id is null)
        {
            PromptTextBox.Text           = _queuePreEditDraft ?? "";
            PromptTextBox.SelectionStart  = _queuePreEditDraftSelectionStart;
            PromptTextBox.SelectionLength = _queuePreEditDraftSelectionLength;
            if (_queuePreEditDraftSelectionLength == 0)
                PromptTextBox.CaretIndex = _queuePreEditDraftCaretIndex;
            _queuePreEditDraft = null;
        }
        else
        {
            var target = _promptQueue.Items.FirstOrDefault(i => i.Id == id);
            if (target is not null)
            {
                PromptTextBox.Text           = target.Text;
                PromptTextBox.SelectionStart  = target.SelectionStart;
                PromptTextBox.SelectionLength = target.SelectionLength;
                if (target.SelectionLength == 0)
                    PromptTextBox.CaretIndex = target.CaretIndex;
            }
        }

        SyncQueuePanel();
        PromptTextBox.Focus();
    }

    private void OnQueueTabRemove(string id)
    {
        if (_activeTabId == id)
        {
            _activeTabId       = null;
            PromptTextBox.Text = _queuePreEditDraft ?? "";
            _queuePreEditDraft = null;
        }
        _promptQueue.Remove(id);
        SyncQueuePanel();
    }

    private void OnQueueTabDeleteConfirm(string id, FrameworkElement anchor)
    {
        var item = _promptQueue.Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;

        var preview = item.Text.Length > 60 ? item.Text[..57] + "…" : item.Text;

        var dialog = new QueueItemDeleteConfirmWindow(
            $"#{item.SequenceNumber}",
            preview,
            GetScreenRect(anchor),
            item.Text)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
            OnQueueTabRemove(id);
    }

    /// <summary>Returns the bounding rect of a UI element in screen coordinates.</summary>
    private static Rect GetScreenRect(FrameworkElement element)
    {
        var topLeft     = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return new Rect(topLeft, bottomRight);
    }

    private void SyncSendButton()
    {
        bool coordinatorBusy = _isPromptRunning || IsNativeLoopRunning;
        if (_activeTabId is not null)
        {
            // On a queued tab: "Send" only if coordinator is free; "Queue" otherwise (re-saves the edit in place).
            RunButton.Content = coordinatorBusy ? "Queue" : "Send";
            return;
        }
        bool queueMode = coordinatorBusy || _promptQueue.Count > 0;
        RunButton.Content = queueMode ? "Queue" : "Send";
    }

    private async void AbortButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var abortTargets = BuildAbortConfirmationTargets();
            if (abortTargets.Count == 0)
            {
                var selectedThread = _selectedTranscriptThread ?? CoordinatorThread;
                SquadDashTrace.Write(
                    "UI",
                    $"AbortButton clicked but no abortable prompt or background task was resolved for thread={selectedThread.ThreadId}");
                return;
            }

            var dialog = new AbortAgentsConfirmationWindow(
                abortTargets,
                BuildAbortConfirmationTargets,
                GetScreenRect(AbortButton))
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true || dialog.SelectedTargets.Count == 0)
            {
                SquadDashTrace.Write("UI", "AbortButton confirmation cancelled.");
                return;
            }

            await AbortConfirmedTargetsAsync(dialog.SelectedTargets).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException("Abort", ex);
        }
    }

    private IReadOnlyList<AbortAgentsConfirmationTarget> BuildAbortConfirmationTargets()
    {
        var targets = new List<AbortAgentsConfirmationTarget>();
        var seenBackgroundTaskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_isPromptRunning)
        {
            targets.Add(new AbortAgentsConfirmationTarget(
                "coordinator",
                "coordinator",
                "Coordinator",
                _pec.CurrentPromptStartedAt ?? CoordinatorThread.CurrentTurn?.StartedAt ?? DateTimeOffset.Now,
                IsCoordinator: true));
        }

        foreach (var target in _backgroundTaskPresenter.GetAbortTargets())
        {
            if (string.IsNullOrWhiteSpace(target.TaskId) || !seenBackgroundTaskIds.Add(target.TaskId))
                continue;

            targets.Add(new AbortAgentsConfirmationTarget(
                target.TaskId,
                target.TaskKind,
                target.DisplayLabel,
                target.StartedAt,
                IsCoordinator: false));
        }

        return targets;
    }

    private async Task AbortConfirmedTargetsAsync(IReadOnlyList<AbortAgentsConfirmationTarget> targets)
    {
        var abortCoordinator = targets.Any(target => target.IsCoordinator);
        if (abortCoordinator)
        {
            SquadDashTrace.Write("UI", "AbortButton confirmed — aborting active coordinator prompt.");
            _bridge.AbortPrompt();
        }

        foreach (var target in targets.Where(target => !target.IsCoordinator))
        {
            SquadDashTrace.Write(
                "UI",
                $"AbortButton confirmed — cancelling background {target.TaskKind} task={target.TaskId} label={target.DisplayLabel}");
            await _bridge.CancelBackgroundTaskAsync(
                target.TaskId,
                _conversationManager.CurrentSessionId).ConfigureAwait(true);
        }
    }

    private void HandleEvent(SquadSdkEvent evt)
    {
        var loggedChunkLength = evt.Type switch
        {
            "thinking_delta" => evt.Text?.Length ?? 0,
            "response_delta" => evt.Chunk?.Length ?? 0,
            _ => evt.Chunk?.Length ?? 0
        };
        SquadDashTrace.Write(
            "UI",
            $"HandleEvent type={evt.Type ?? "(null)"} tool={evt.ToolName ?? "(none)"} chunkLen={loggedChunkLength}");
        if (!string.Equals(evt.Type, "sdk_diagnostics", StringComparison.Ordinal))
            _pec.MarkActivity(evt);

        if (!string.IsNullOrWhiteSpace(evt.Model))
        {
            var model = evt.Model.Trim();
            _squadCliAdapter.LastObservedModel = model;
            if (!_modelObservedThisSession ||
                !string.Equals(_settingsSnapshot.LastUsedModel, model, StringComparison.Ordinal))
            {
                _modelObservedThisSession = true;
                _settingsSnapshot = _settingsStore.SaveLastUsedModel(model);
            }
        }

        switch (evt.Type)
        {
            case "session_ready":
                HandleSessionReady(evt);
                break;

            case "session_reset":
                HandleSessionReset(evt);
                break;

            case "thinking_started":
                EnsureCurrentTurnThinkingVisible();
                UpdateLeadAgent("Thinking", string.Empty, "Reasoning through the request.");
                UpdateSessionState("Thinking");
                break;

            case "thinking_delta":
                var thought = NormalizeThinkingChunk(evt.Text);
                if (!string.IsNullOrWhiteSpace(thought))
                {
                    UpdateLeadAgent("Thinking", string.Empty, FormatThinkingText(thought));
                    AppendThinkingText(thought, evt.Speaker);
                }
                break;

            case "tool":
                UpdateLeadAgent(
                    "Tooling",
                    string.Empty,
                    "Waiting for live tool execution events.");
                UpdateSessionState("Using tool");
                break;

            case "tool_start":
                StartToolExecution(evt);
                break;

            case "tool_progress":
                UpdateToolExecution(evt);
                break;

            case "tool_complete":
                CompleteToolExecution(evt);
                break;

            case "response_delta":
                NotePendingQuickReplyCoordinatorResponse();
                AppendText(evt.Chunk ?? string.Empty);
                UpdateLeadAgent("Streaming", string.Empty, "Writing the response.");
                UpdateSessionState("Streaming");
                break;

            case "sdk_diagnostics":
                HandleSdkDiagnostics(evt);
                break;

            case "background_tasks_changed":
                HandleBackgroundTasksChanged(evt);
                break;

            case "task_complete":
                HandleTaskComplete(evt);
                break;

            case "subagent_started":
                HandleSubagentStarted(evt);
                break;

            case "subagent_message_delta":
                HandleSubagentMessageDelta(evt);
                break;

            case "subagent_message":
                HandleSubagentMessage(evt);
                break;

            case "subagent_tool_start":
                HandleSubagentToolStart(evt);
                break;

            case "subagent_tool_progress":
                HandleSubagentToolProgress(evt);
                break;

            case "subagent_tool_complete":
                HandleSubagentToolComplete(evt);
                break;

            case "subagent_completed":
                HandleSubagentCompleted(evt);
                break;

            case "subagent_failed":
                HandleSubagentFailed(evt);
                break;

            case "loop_started":
                HandleLoopStarted(evt);
                break;

            case "loop_iteration":
                HandleLoopIteration(evt);
                break;

            case "loop_stopped":
                HandleLoopStopped(evt);
                _ = _pushNotificationService.NotifyEventAsync("loop_stopped", "SquadDash", "Loop stopped");
                break;

            case "loop_error":
                HandleLoopError(evt);
                break;

            case "loop_output":
                HandleLoopOutput(evt);
                break;

            case "watch_fleet_dispatched":
                HandleWatchFleetDispatched(evt);
                break;

            case "watch_wave_dispatched":
                HandleWatchWaveDispatched(evt);
                break;

            case "watch_hydration":
                HandleWatchHydration(evt);
                break;

            case "watch_retro":
                HandleWatchRetro(evt);
                break;

            case "watch_monitor_notification":
                HandleWatchMonitorNotification(evt);
                break;

            case "rc_started":
                HandleRcStarted(evt);
                break;

            case "rc_stopped":
                HandleRcStopped(evt);
                _ = _pushNotificationService.NotifyEventAsync("rc_connection_dropped", "SquadDash", "Remote connection dropped");
                break;

            case "rc_error":
                HandleRcError(evt);
                break;

            case "done":
                _pec.ActiveToolName = null;
                FinalizeCurrentTurnResponse();
                CollapseCurrentTurnThinking();
                _conversationManager.SaveCurrentTurnToConversation(DateTimeOffset.Now);
                _backgroundTaskPresenter.RefreshLeadAgentBackgroundStatus();
                FlushDeferredSystemLines();
                {
                    var agentName = _leadAgent?.Name ?? "Agent";
                    _ = _pushNotificationService.NotifyEventAsync("assistant_turn_complete", "SquadDash", $"{agentName} turn complete");
                }
                break;

            case "error":
                _pec.ActiveToolName = null;
                _conversationManager.SaveCurrentTurnToConversation(DateTimeOffset.Now);
                UpdateLeadAgent("Error", string.Empty, evt.Message ?? "Unknown error");
                UpdateSessionState("Error");
                FlushDeferredSystemLines();
                break;
        }
    }

    private void HandleSdkDiagnostics(SquadSdkEvent evt)
    {
        var summary = evt.DiagnosticPhase switch
        {
            "send_started" => $"send started method={evt.SendMethod ?? "(unknown)"}",
            "first_sdk_event" => $"first sdk event type={evt.DiagnosticEventType ?? evt.FirstSdkEventType ?? "(unknown)"} after={FormatSdkDiagnosticMs(evt.MillisecondsSinceSendStart)}",
            "first_thinking_event" => $"first thinking event after={FormatSdkDiagnosticMs(evt.TimeToFirstThinkingMs)}",
            "first_response_event" => $"first response event after={FormatSdkDiagnosticMs(evt.TimeToFirstResponseMs)}",
            "send_completed" => $"send completed total={FormatSdkDiagnosticMs(evt.MillisecondsSinceSendStart)} firstSdk={FormatSdkDiagnosticMs(evt.TimeToFirstSdkEventMs)} firstThinking={FormatSdkDiagnosticMs(evt.TimeToFirstThinkingMs)} firstResponse={FormatSdkDiagnosticMs(evt.TimeToFirstResponseMs)}",
            "send_failed" => $"send failed total={FormatSdkDiagnosticMs(evt.MillisecondsSinceSendStart)} firstSdk={FormatSdkDiagnosticMs(evt.TimeToFirstSdkEventMs)} firstThinking={FormatSdkDiagnosticMs(evt.TimeToFirstThinkingMs)} firstResponse={FormatSdkDiagnosticMs(evt.TimeToFirstResponseMs)} message={evt.Message ?? "(none)"}",
            _ => $"phase={evt.DiagnosticPhase ?? "(unknown)"} event={evt.DiagnosticEventType ?? evt.FirstSdkEventType ?? "(none)"} after={FormatSdkDiagnosticMs(evt.MillisecondsSinceSendStart)}"
        };

        SquadDashTrace.Write("SDK", summary);
    }

    private static string FormatSdkDiagnosticMs(int? value)
    {
        return value is int ms
            ? $"{ms}ms"
            : "(n/a)";
    }

    private void HandleBridgeError(string text)
    {
        SquadDashTrace.Write("UI", $"Bridge stderr: {text}");
        _pec.MarkActivity("bridge-stderr");

        if (text.Contains("ExperimentalWarning: SQLite") ||
            text.Contains("Use `node --trace-warnings"))
        {
            return;
        }

        AppendLine("[stderr] " + text, ThemeBrush("SystemErrorText"));
    }

    private void HandleSessionReady(SquadSdkEvent evt)
    {
        if (_currentWorkspace is null || string.IsNullOrWhiteSpace(evt.SessionId))
            return;

        var sessionChanged = !string.Equals(
            _conversationManager.CurrentSessionId,
            evt.SessionId,
            StringComparison.OrdinalIgnoreCase);
        if (sessionChanged && evt.SessionResumed != true && _recentDelegationOutcomes.Count > 0)
        {
            _recentDelegationOutcomes.Clear();
            SquadDashTrace.Write("Routing", "Delegation outcome rollup reset for fresh coordinator session.");
        }

        _conversationManager.CurrentSessionId = evt.SessionId;
        _conversationManager.PersistConversationState(_conversationManager.ConversationState with
        {
            SessionId = _conversationManager.CurrentSessionId,
            SessionUpdatedAt = DateTimeOffset.UtcNow
        });

        SquadDashTrace.Write(
            "UI",
            $"Session ready id={evt.SessionId} resumed={evt.SessionResumed?.ToString() ?? "(unknown)"} diagnostics={SessionResumeDiagnosticsPresentation.BuildSummary(evt) ?? "(none)"}");
    }

    private void HandleSessionReset(SquadSdkEvent evt)
    {
        var diagnostics = PromptContextDiagnosticsPresentation.BuildTraceSummary(
            _conversationManager.CapturePromptContextDiagnostics(),
            DateTimeOffset.UtcNow);
        SquadDashTrace.Write(
            "Routing",
            $"Session reset requested after provider rejection. {diagnostics}");
        if (_recentDelegationOutcomes.Count > 0)
        {
            _recentDelegationOutcomes.Clear();
            SquadDashTrace.Write("Routing", "Delegation outcome rollup cleared after session reset.");
        }
        _conversationManager.CurrentSessionId = null;

        if (_currentWorkspace is not null)
        {
            _conversationManager.PersistConversationState(_conversationManager.ConversationState with
            {
                SessionId = null,
                SessionUpdatedAt = DateTimeOffset.UtcNow
            });
        }

        AppendLine(
            "[info] " + (string.IsNullOrWhiteSpace(evt.Message)
                ? "Squad reset the previous session after a provider error and is retrying your prompt in a fresh session."
                : evt.Message),
            ThemeBrush("SystemInfoText"));
        UpdateLeadAgent("Recovering", string.Empty, "Resetting the active Squad session and retrying the prompt.");
        UpdateSessionState("Recovering");
    }

    private void HandleBackgroundTasksChanged(SquadSdkEvent evt)
    {
        var previousAgents = _backgroundTaskPresenter.BackgroundAgents;
        var previousShells = _backgroundTaskPresenter.BackgroundShells;
        _backgroundTaskPresenter.BackgroundAgents = evt.BackgroundAgents ?? Array.Empty<SquadBackgroundAgentInfo>();
        _backgroundTaskPresenter.BackgroundShells = evt.BackgroundShells ?? Array.Empty<SquadBackgroundShellInfo>();

        _agentThreadRegistry.SyncBackgroundAgentThreads(_backgroundTaskPresenter.BackgroundAgents);
        SyncAgentCardsWithThreads();

        SquadDashTrace.Write(
            "UI",
            $"Background tasks updated session={evt.SessionId ?? _conversationManager.CurrentSessionId ?? "(unknown)"} {_backgroundTaskPresenter.BuildBackgroundTaskTraceSummary()}");

        _backgroundTaskPresenter.HandleRemovedBackgroundTasks(previousAgents, previousShells);

        if (!_isPromptRunning)
            _backgroundTaskPresenter.RefreshLeadAgentBackgroundStatus();

        UpdateInteractiveControlState();
    }

    private void HandleTaskComplete(SquadSdkEvent evt)
    {
        SquadDashTrace.Write(
            "UI",
            $"Background task completed summary={evt.Summary ?? "(none)"}");

        if (!string.IsNullOrWhiteSpace(evt.Summary))
        {
            _backgroundTaskPresenter.SkipNextBackgroundCompletionFallback = true;
            _backgroundTaskPresenter.RecordBackgroundCompletion(
                evt.Summary.Trim(),
                $"task-summary:{evt.Summary.Trim()}");
        }

        if (!_isPromptRunning && !_backgroundTaskPresenter.HasBackgroundTasks())
            _backgroundTaskPresenter.RefreshLeadAgentBackgroundStatus();
    }

    private void HandleSubagentStarted(SquadSdkEvent evt)
    {
        NotePendingQuickReplySubagentStarted(evt);
        if (ShouldSuppressSilentBackgroundAgent(evt))
        {
            SquadDashTrace.Write("UI", $"Silent background agent started agent={evt.AgentDisplayName ?? evt.AgentName ?? evt.AgentId ?? "(unknown)"}");
            return;
        }

        var thread = _agentThreadRegistry.GetOrCreateAgentThread(evt);
        _agentThreadRegistry.EnsureAgentThreadTurnStarted(thread);
        _agentThreadRegistry.UpdateAgentThreadLifecycle(thread, evt, statusText: "Running", detailText: evt.AgentDescription ?? "Background work started.");
        SquadDashTrace.Write(
            "UI",
            $"Subagent started {BackgroundTaskPresenter.BuildBackgroundAgentLabel(thread)} description={evt.AgentDescription?.Trim() ?? "(none)"}");
        SyncAgentCardsWithThreads();
        _backgroundTaskPresenter.ObserveBackgroundAgentActivity(thread, "subagent_started");
        _conversationManager.PersistAgentThreadSnapshot(thread);
    }

    private void HandleSubagentMessageDelta(SquadSdkEvent evt)
    {
        if (ShouldSuppressSilentBackgroundAgent(evt))
            return;

        var thread = _agentThreadRegistry.GetOrCreateAgentThread(evt);
        _agentThreadRegistry.EnsureAgentThreadTurnStarted(thread);
        thread.StatusText = "Streaming";
        thread.IsCurrentBackgroundRun = true;
        if (!string.IsNullOrWhiteSpace(evt.Chunk))
        {
            AppendText(thread, evt.Chunk!);
            thread.ResponseStreamed = true;
            thread.LatestResponse = GetSanitizedTurnResponseTextOrNull(thread.CurrentTurn);
        }

        if (!string.IsNullOrWhiteSpace(thread.LatestResponse))
            thread.DetailText = BuildThreadPreview(thread.LatestResponse!);

        SyncThreadChip(thread);
        UpdateAgentCardFromThread(thread, syncBuckets: false);
        _conversationManager.SchedulePersistAgentThreadSnapshot(thread);
    }

    private void HandleSubagentMessage(SquadSdkEvent evt)
    {
        if (ShouldSuppressSilentBackgroundAgent(evt))
            return;

        var thread = _agentThreadRegistry.GetOrCreateAgentThread(evt);
        _agentThreadRegistry.EnsureAgentThreadTurnStarted(thread);
        thread.IsCurrentBackgroundRun = true;

        if (!string.IsNullOrWhiteSpace(evt.ReasoningText))
            AppendThinkingText(thread, evt.ReasoningText!, thread.Title);

        if (!thread.ResponseStreamed && !string.IsNullOrWhiteSpace(evt.Text))
        {
            AppendText(thread, evt.Text!);
        }

        thread.LatestResponse = GetSanitizedTurnResponseTextOrNull(thread.CurrentTurn);
        thread.DetailText = !string.IsNullOrWhiteSpace(thread.LatestResponse)
            ? BuildThreadPreview(thread.LatestResponse!)
            : thread.DetailText;
        FinalizeCurrentTurnResponse(thread);
        thread.ResponseStreamed = false;
        SyncThreadChip(thread);
        UpdateAgentCardFromThread(thread);
        _backgroundTaskPresenter.ObserveBackgroundAgentActivity(thread, "subagent_message");
        _conversationManager.SaveAgentThreadToConversation(thread, DateTimeOffset.UtcNow);
    }

    private void HandleSubagentToolStart(SquadSdkEvent evt)
    {
        if (ShouldSuppressSilentBackgroundAgent(evt))
            return;

        var thread = _agentThreadRegistry.GetOrCreateAgentThread(evt);
        _agentThreadRegistry.EnsureAgentThreadTurnStarted(thread);
        StartToolExecution(thread, evt);
        thread.StatusText = "Tooling";
        thread.IsCurrentBackgroundRun = true;
        thread.DetailText = ToolTranscriptFormatter.BuildRunningText(CreateToolDescriptor(evt), evt.ProgressMessage);
        SyncThreadChip(thread);
        UpdateAgentCardFromThread(thread);
        _backgroundTaskPresenter.ObserveBackgroundAgentActivity(thread, "subagent_tool_start");
        _conversationManager.PersistAgentThreadSnapshot(thread);
    }

    private void HandleSubagentToolProgress(SquadSdkEvent evt)
    {
        if (ShouldSuppressSilentBackgroundAgent(evt))
            return;

        var thread = _agentThreadRegistry.GetOrCreateAgentThread(evt);
        _agentThreadRegistry.EnsureAgentThreadTurnStarted(thread);
        UpdateToolExecution(thread, evt);
        thread.StatusText = "Tooling";
        thread.IsCurrentBackgroundRun = true;
        thread.DetailText = ToolTranscriptFormatter.BuildRunningText(CreateToolDescriptor(evt), evt.ProgressMessage);
        SyncThreadChip(thread);
        UpdateAgentCardFromThread(thread, syncBuckets: false);
        _backgroundTaskPresenter.ObserveBackgroundAgentActivity(thread, "subagent_tool_progress");
        _conversationManager.SchedulePersistAgentThreadSnapshot(thread);
    }

    private void HandleSubagentToolComplete(SquadSdkEvent evt)
    {
        if (ShouldSuppressSilentBackgroundAgent(evt))
            return;

        var thread = _agentThreadRegistry.GetOrCreateAgentThread(evt);
        _agentThreadRegistry.EnsureAgentThreadTurnStarted(thread);
        CompleteToolExecution(thread, evt);
        thread.StatusText = "Running";
        thread.IsCurrentBackgroundRun = true;
        if (!string.IsNullOrWhiteSpace(evt.OutputText))
            thread.DetailText = BuildThreadPreview(evt.OutputText);

        SyncThreadChip(thread);
        UpdateAgentCardFromThread(thread);
        _backgroundTaskPresenter.ObserveBackgroundAgentActivity(thread, "subagent_tool_complete");
        _conversationManager.PersistAgentThreadSnapshot(thread);
    }

    private void HandleSubagentCompleted(SquadSdkEvent evt)
    {
        if (ShouldSuppressSilentBackgroundAgent(evt))
        {
            SquadDashTrace.Write("UI", $"Silent background agent completed agent={evt.AgentDisplayName ?? evt.AgentName ?? evt.AgentId ?? "(unknown)"}");
            return;
        }

        var thread = _agentThreadRegistry.GetOrCreateAgentThread(evt);
        _agentThreadRegistry.UpdateAgentThreadLifecycle(thread, evt, statusText: "Completed", detailText: AgentThreadRegistry.BuildThreadCompletionDetail(thread, evt));
        _agentThreadRegistry.FinalizeAgentThread(thread);
        var summary = BackgroundTaskPresenter.BuildThreadCompletionSummary(thread);
        SquadDashTrace.Write("UI", $"Subagent completed {summary}");

        _backgroundTaskPresenter.SkipNextBackgroundCompletionFallback = true;
        _backgroundTaskPresenter.RecordBackgroundCompletion(summary, BackgroundTaskPresenter.BuildThreadAnnouncementKey(thread));
        SyncAgentCardsWithThreads();
        _backgroundTaskPresenter.ObserveBackgroundAgentActivity(thread, "subagent_completed");
        _conversationManager.SaveAgentThreadToConversation(thread, DateTimeOffset.UtcNow);
    }

    private void HandleSubagentFailed(SquadSdkEvent evt)
    {
        if (ShouldSuppressSilentBackgroundAgent(evt))
        {
            SquadDashTrace.Write("UI", $"Silent background agent failed agent={evt.AgentDisplayName ?? evt.AgentName ?? evt.AgentId ?? "(unknown)"} message={evt.Message ?? "(none)"}");
            return;
        }

        var thread = _agentThreadRegistry.GetOrCreateAgentThread(evt);
        var summary = BackgroundTaskPresenter.BuildThreadFailureSummary(thread, evt.Message);
        _agentThreadRegistry.UpdateAgentThreadLifecycle(thread, evt, statusText: "Failed", detailText: summary);
        _agentThreadRegistry.FinalizeAgentThread(thread);
        SquadDashTrace.Write("UI", $"Subagent failed {summary}");

        _backgroundTaskPresenter.SkipNextBackgroundCompletionFallback = true;
        _backgroundTaskPresenter.AppendBackgroundNotice(summary, ThemeBrush("TaskFailureText"), BackgroundTaskPresenter.BuildThreadAnnouncementKey(thread) + ":failed");
        SyncAgentCardsWithThreads();
        _backgroundTaskPresenter.ObserveBackgroundAgentActivity(thread, "subagent_failed");
        _conversationManager.SaveAgentThreadToConversation(thread, DateTimeOffset.UtcNow);
    }

    private void HandleLoopStarted(SquadSdkEvent evt)
    {
        _pec.SetIsLoopRunning(true);
        _loopCurrentIteration = 0;
        _loopQueued = false;
        var label = string.IsNullOrWhiteSpace(evt.LoopMdPath)
            ? "🔁 Loop started"
            : $"🔁 Loop started: {evt.LoopMdPath.Replace('\\', '/')}";
        AppendLine(label);
        SquadDashTrace.Write("UI", $"Loop started mdPath={evt.LoopMdPath ?? "(none)"}");
        SyncLoopPanel();
    }

    private void HandleLoopIteration(SquadSdkEvent evt)
    {
        if (evt.LoopIteration is int n) _loopCurrentIteration = n;
        var iterLabel = evt.LoopIteration is int m ? $"↩ Iteration {m}" : "↩ Iteration";
        AppendLine(iterLabel);
        SquadDashTrace.Write("UI", $"Loop iteration={evt.LoopIteration?.ToString() ?? "(unknown)"}");
        SyncLoopPanel();
    }

    private void HandleLoopStopped(SquadSdkEvent evt)
    {
        _pec.SetIsLoopRunning(false);
        _loopCurrentIteration = 0;
        AppendLine("✅ Loop stopped");
        SquadDashTrace.Write("UI", $"Loop stopped mdPath={evt.LoopMdPath ?? "(none)"}");
        SyncLoopPanel();
    }

    private void HandleLoopError(SquadSdkEvent evt)
    {
        _pec.SetIsLoopRunning(false);
        _loopCurrentIteration = 0;
        var errorLabel = string.IsNullOrWhiteSpace(evt.Message)
            ? "❌ Loop error"
            : $"❌ Loop error: {evt.Message}";
        AppendLine(errorLabel, ThemeBrush("SystemErrorText"));
        SquadDashTrace.Write("UI", $"Loop error message={evt.Message ?? "(none)"}");
        SyncLoopPanel();
    }

    // ── Native-loop controller callbacks (LoopMode.NativeAgents) ───────────

    private void OnNativeLoopIterationStarted(int iteration)
    {
        _loopCurrentIteration = iteration;
        _loopIsWaiting = false;
        _pec.SetIsLoopRunning(true);
        AppendLine($"↩ Round {iteration}");
        SyncLoopPanel();
    }

    private void OnNativeLoopStopped()
    {
        _pec.SetIsLoopRunning(false);
        _loopCurrentIteration = 0;
        _loopIsWaiting = false;
        AppendLine("✅ Loop stopped");
        SyncLoopPanel();
    }

    private void OnNativeLoopError(string msg)
    {
        _pec.SetIsLoopRunning(false);
        _loopCurrentIteration = 0;
        _loopIsWaiting = false;
        AppendLine($"❌ Loop error: {msg}", ThemeBrush("SystemErrorText"));
        SyncLoopPanel();
    }

    private void OnNativeLoopIterationCompleted(int iteration)
    {
        AppendLine($"  ✓ Round {iteration} complete");
        SyncLoopPanel();
    }

    private void OnNativeLoopWaiting(DateTimeOffset nextAt)
    {
        _loopNextIterationAt = nextAt;
        _loopIsWaiting = true;
        SyncLoopPanel();
    }

    private static readonly Regex AnsiEscapeRegex = new(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);
    private static readonly SolidColorBrush LoopStderrBrush = new(Color.FromRgb(0xFF, 0x44, 0x44));
    private static readonly SolidColorBrush LoopLifecycleBrush = new(Color.FromRgb(0x88, 0x88, 0x88));

    private void HandleLoopOutput(SquadSdkEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.OutputLine)) return;
        var raw = evt.OutputLine!;
        SquadDashTrace.Write("LoopOutput", raw);
        var line = AnsiEscapeRegex.Replace(raw, "").Trim();
        if (string.IsNullOrWhiteSpace(line)) return;
        if (line.StartsWith("[stderr]", StringComparison.Ordinal))
            AppendLoopOutputLine(line, LoopLifecycleBrush);
        else
            AppendLoopOutputLine(line);
    }

    private void AppendLoopOutputLine(string text, Brush? brush = null)
    {
        var p = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
        var run = new Run(text);
        if (brush is not null)
            run.Foreground = brush;
        p.Inlines.Add(run);
        LoopOutputTextBox.Document.Blocks.Add(p);
        LoopOutputTextBox.ScrollToEnd();
        if (!_loopOutputHasContent)
        {
            _loopOutputHasContent = true;
            SyncLoopOutputPane();
        }
    }

    private void BackupAndClearLoopOutput()
    {
        if (LoopOutputTextBox is null) return;
        if (LoopOutputTextBox.Document.Blocks.Count > 0)
        {
            var range = new TextRange(
                LoopOutputTextBox.Document.ContentStart,
                LoopOutputTextBox.Document.ContentEnd);
            LoopOutputStore.SaveLog(range.Text);
            LoopOutputTextBox.Document.Blocks.Clear();
        }
        _loopOutputHasContent = false;
        SyncLoopOutputPane();
    }

    private void SyncLoopOutputPane()
    {
        if (LoopOutputBorder is null) return;
        bool show = _loopPanelVisible && (_loopOutputHasContent || IsLoopRunning);
        var vis = show ? Visibility.Visible : Visibility.Collapsed;
        LoopOutputBorder.Visibility = vis;
        LoopOutputSplitter.Visibility = vis;
        if (LoopOutputSplitterColumnDef is not null)
            LoopOutputSplitterColumnDef.Width = show ? new GridLength(8) : new GridLength(0);
        if (LoopOutputColumnDef is not null)
        {
            if (show && LoopOutputColumnDef.ActualWidth < 1)
                LoopOutputColumnDef.Width = new GridLength(320);
            else if (!show)
                LoopOutputColumnDef.Width = new GridLength(0);
        }
    }

    private void LoopOutputClearButton_Click(object sender, RoutedEventArgs e)
    {
        try { BackupAndClearLoopOutput(); }
        catch (Exception ex) { HandleUiCallbackException(nameof(LoopOutputClearButton_Click), ex); }
    }

    private void LoopOutputClearMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try { BackupAndClearLoopOutput(); }
        catch (Exception ex) { HandleUiCallbackException(nameof(LoopOutputClearMenuItem_Click), ex); }
    }

    private void HandleWatchFleetDispatched(SquadSdkEvent evt)
    {
        _watchCycleId = evt.WatchCycleId ?? Guid.NewGuid().ToString("N")[..8];
        _watchFleetSize = evt.WatchFleetSize ?? 0;
        _watchWaveIndex = 0;
        _watchWaveCount = 0;
        _watchAgentCount = 0;
        _watchPhase = null;
        AppendLine($"👁 Watch: fleet dispatched ({_watchFleetSize} agents)");
        SquadDashTrace.Write("Watch", $"fleet cycleId={_watchCycleId} size={_watchFleetSize}");
        SyncWatchPanel();
    }

    private void HandleWatchWaveDispatched(SquadSdkEvent evt)
    {
        _watchWaveIndex = evt.WatchWaveIndex ?? _watchWaveIndex;
        _watchWaveCount = evt.WatchWaveCount ?? _watchWaveCount;
        _watchAgentCount = evt.WatchAgentCount ?? _watchAgentCount;
        AppendLine($"👁 Watch: wave {_watchWaveIndex + 1}/{_watchWaveCount} ({_watchAgentCount} agents)");
        SquadDashTrace.Write("Watch", $"wave {_watchWaveIndex + 1}/{_watchWaveCount} agents={_watchAgentCount}");
        SyncWatchPanel();
    }

    private void HandleWatchHydration(SquadSdkEvent evt)
    {
        _watchPhase = evt.WatchPhase;
        SquadDashTrace.Write("Watch", $"hydration phase={_watchPhase ?? "(none)"}");
        SyncWatchPanel();
    }

    private void HandleWatchRetro(SquadSdkEvent evt)
    {
        var summary = evt.WatchRetroSummary;
        AppendLine(string.IsNullOrWhiteSpace(summary)
            ? "👁 Watch: retro complete"
            : $"👁 Watch: retro — {summary}");
        SquadDashTrace.Write("Watch", $"retro cycleId={_watchCycleId} summary={summary ?? "(none)"}");
        _watchCycleId = null;
        _watchFleetSize = 0;
        _watchWaveIndex = 0;
        _watchWaveCount = 0;
        _watchAgentCount = 0;
        _watchPhase = null;
        SyncWatchPanel();
    }

    private void HandleWatchMonitorNotification(SquadSdkEvent evt)
    {
        var channel = evt.WatchNotificationChannel ?? "unknown";
        var sent = evt.WatchNotificationSent == true ? "sent" : "skipped";
        AppendLine($"👁 Watch: monitor notification ({channel}, {sent})");
        SquadDashTrace.Write("Watch", $"monitor channel={channel} sent={sent} recipient={evt.WatchNotificationRecipient ?? "(none)"}");
    }

    private void SyncWatchPanel()
    {
        if (WatchPanelBorder is null) return;

        bool active = _watchCycleId is not null;
        WatchPanelBorder.Visibility = active ? Visibility.Visible : Visibility.Collapsed;

        if (!active) return;

        WatchStatusStack.Children.Clear();

        if (_watchFleetSize > 0)
            WatchStatusStack.Children.Add(MakeWatchRow($"Fleet: {_watchFleetSize} agents"));

        if (_watchWaveCount > 0)
            WatchStatusStack.Children.Add(MakeWatchRow($"Wave {_watchWaveIndex + 1} of {_watchWaveCount}"));
        else if (_watchAgentCount > 0)
            WatchStatusStack.Children.Add(MakeWatchRow($"{_watchAgentCount} agents dispatched"));

        if (!string.IsNullOrWhiteSpace(_watchPhase))
            WatchStatusStack.Children.Add(MakeWatchRow($"Phase: {_watchPhase}"));
    }

    private TextBlock MakeWatchRow(string text) =>
        new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = (Brush)FindResource("ActivePanelSubtitle"),
            TextWrapping = TextWrapping.Wrap
        };

    private void HandleRcStarted(SquadSdkEvent evt)
    {
        _remoteAccessActive = true;
        UpdateRemoteAccessMenuHeader();
        var port = evt.RcPort is int p ? p : 0;
        var url = evt.RcUrl ?? $"http://localhost:{port}";
        AppendLine($"📡 Remote access started: {url}");
        if (!string.IsNullOrWhiteSpace(evt.RcLanUrl))
            AppendLine($"  LAN URL: {evt.RcLanUrl}");
        SquadDashTrace.Write("UI", $"RC started port={port} url={url}");
    }

    private void HandleRcStopped(SquadSdkEvent evt)
    {
        _remoteAccessActive = false;
        UpdateRemoteAccessMenuHeader();
        AppendLine("📡 Remote access stopped");
        SquadDashTrace.Write("UI", "RC stopped");
    }

    private void HandleRcError(SquadSdkEvent evt)
    {
        _remoteAccessActive = false;
        UpdateRemoteAccessMenuHeader();
        var errorLabel = string.IsNullOrWhiteSpace(evt.Message)
            ? "❌ Remote access error"
            : $"❌ Remote access error: {evt.Message}";
        AppendLine(errorLabel, ThemeBrush("SystemErrorText"));
        SquadDashTrace.Write("UI", $"RC error message={evt.Message ?? "(none)"}");
    }

    private void UpdateRemoteAccessMenuHeader()
    {
        if (_remoteAccessMenuItem is null) return;
        _remoteAccessMenuItem.Header = _remoteAccessActive
            ? "Stop _Remote Access"
            : "Start _Remote Access";
    }

    private void SyncLoopPanel()
    {
        if (LoopPanelBorder is null) return;
        SyncSendButton();
        LoopPanelBorder.Visibility = _loopPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        bool running = IsLoopRunning;

        bool nativeMode = _settingsSnapshot.LoopMode == LoopMode.NativeAgents;
        bool busyCoordinator = _isPromptRunning && nativeMode && !running;

        // In Queue Loop state: coordinator is busy in native mode, loop not yet running.
        if (busyCoordinator || _loopQueued)
        {
            StartLoopButton.IsEnabled = !_loopQueued; // disable once already queued
            StartLoopButton.Content   = _loopQueued ? "Loop Queued" : "Queue Loop";
        }
        else
        {
            StartLoopButton.IsEnabled = !running;
            StartLoopButton.Content   = "Start Loop";
        }

        StopLoopButton.IsEnabled = running;
        AbortLoopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

        LoopModeNativeRadio.IsEnabled = !running;
        LoopModeCliRadio.IsEnabled = !running;
        LoopModeNativeRadio.IsChecked = nativeMode;
        LoopModeCliRadio.IsChecked = _settingsSnapshot.LoopMode == LoopMode.SquadCli;

        LoopContinuousContextCheckBox.IsEnabled = !running;
        LoopContinuousContextCheckBox.IsChecked = _settingsSnapshot.LoopContinuousContext;

        string status;
        if (_loopQueued)
            status = "⏸ Loop queued — waiting for coordinator";
        else if (running
            && nativeMode
            && _loopController.StopState == LoopStopState.StopRequested)
            status = "◌ Stopping after this iteration…";
        else if (running && _loopIsWaiting)
        {
            var remaining = _loopNextIterationAt - DateTimeOffset.Now;
            status = remaining.TotalSeconds > 60
                ? $"⏳ Waiting · next in {(int)remaining.TotalMinutes}m"
                : remaining.TotalSeconds > 0
                    ? $"⏳ Waiting · next in {(int)remaining.TotalSeconds}s"
                    : "⏳ Waiting…";
        }
        else if (running)
            status = _loopCurrentIteration > 0
                ? $"● Running · Round {_loopCurrentIteration}"
                : "● Running";
        else
            status = "";

        LoopStatusLabel.Text = status;
        SyncLoopOutputPane();
    }

    private void SyncTasksPanel()
    {
        if (TasksPanelBorder is null) return;
        TasksPanelBorder.Visibility = _tasksPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        if (_tasksPanelVisible)
            LoadTasksPanel();
    }

    private void PersistTasksPanelVisible()
    {
        var state = _docsPanelState ?? _settingsStore.GetDocsPanelState(_currentWorkspace?.FolderPath);
        _docsPanelState = state with { TasksPanelVisible = _tasksPanelVisible };
        _settingsSnapshot = _settingsStore.SaveDocsPanelState(_currentWorkspace?.FolderPath, _docsPanelState);
    }

    private void LoadTasksPanel()
    {
        if (TasksItemsPanel is null) return;
        var workspace = _currentWorkspace;
        if (workspace is null)
        {
            ShowTasksPanelEmpty("No workspace open");
            return;
        }

        var tasksPath = Path.Combine(workspace.SquadFolderPath, "tasks.md");
        if (!File.Exists(tasksPath))
        {
            ShowTasksPanelEmpty("No tasks.md found");
            return;
        }

        string[] lines;
        try { lines = File.ReadAllLines(tasksPath); }
        catch { ShowTasksPanelEmpty("Could not read tasks.md"); return; }

        var groups = TasksPanelParser.Parse(lines)
            .Where(g => g.Items.Count > 0)
            .ToList();

        // Clear existing content (keep the empty TextBlock)
        for (int i = TasksItemsPanel.Children.Count - 1; i >= 0; i--)
        {
            var child = TasksItemsPanel.Children[i];
            if (child == TasksEmptyTextBlock) continue;
            TasksItemsPanel.Children.RemoveAt(i);
        }

        if (groups.Count == 0)
        {
            ShowTasksPanelEmpty("No open tasks");
            return;
        }

        TasksEmptyTextBlock.Visibility = Visibility.Collapsed;

        const int MaxTaskItems = 10;
        var itemsRendered = 0;

        foreach (var group in groups)
        {
            if (itemsRendered >= MaxTaskItems) break;

            // Priority heading: colored dot + label text
            var headingRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 3)
            };
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = PriorityDotColor(group.Emoji),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            };
            var headingLabel = new TextBlock
            {
                Text = group.Label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            headingLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            headingRow.Children.Add(dot);
            headingRow.Children.Add(headingLabel);
            TasksItemsPanel.Children.Add(headingRow);

            foreach (var item in group.Items)
            {
                if (itemsRendered >= MaxTaskItems) break;

                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                var checkbox = new Border
                {
                    Width = 10,
                    Height = 10,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(1),
                    Background = Brushes.Transparent,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 6, 0),
                    IsHitTestVisible = false
                };
                checkbox.SetResourceReference(Border.BorderBrushProperty, "BodyText");
                var label = new TextBlock
                {
                    Text = item,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 220
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
                row.Children.Add(checkbox);
                row.Children.Add(label);
                TasksItemsPanel.Children.Add(row);
                itemsRendered++;
            }
        }
    }

    private Brush PriorityDotColor(string emoji) => emoji switch {
        "🔴" => (Brush)FindResource("TaskPriorityHigh"),
        "🟡" => (Brush)FindResource("TaskPriorityMid"),
        "🟢" => (Brush)FindResource("TaskPriorityLow"),
        _    => Brushes.Gray
    };

    private void ShowTasksPanelEmpty(string message)
    {
        if (TasksEmptyTextBlock is null) return;
        // Remove all non-empty-text children
        for (int i = TasksItemsPanel.Children.Count - 1; i >= 0; i--)
        {
            if (TasksItemsPanel.Children[i] != TasksEmptyTextBlock)
                TasksItemsPanel.Children.RemoveAt(i);
        }
        TasksEmptyTextBlock.Text = message;
        TasksEmptyTextBlock.Visibility = Visibility.Visible;
    }

    private async void StartLoopButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentWorkspace is null) return;

            // In native-agents mode, if the coordinator is busy, queue the loop start.
            if (_settingsSnapshot.LoopMode == LoopMode.NativeAgents && (_isPromptRunning || _promptQueue.HasReadyItems))
            {
                _loopQueued = true;
                SyncLoopPanel();
                return;
            }

            await StartLoopImmediateAsync();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(StartLoopButton_Click), ex);
        }
    }

    private async Task StartLoopImmediateAsync()
    {
        if (_currentWorkspace is null) return;
        BackupAndClearLoopOutput();
        var loopMdPath = Path.Combine(_currentWorkspace.SquadFolderPath, "loop.md");

        if (_settingsSnapshot.LoopMode == LoopMode.NativeAgents)
        {
            var config = LoopMdParser.Parse(loopMdPath);
            if (config == null)
            {
                AppendLine(
                    "❌ Loop not configured — check loop.md has configured: true",
                    ThemeBrush("SystemErrorText"));
                return;
            }
            await _loopController.StartAsync(config, _settingsSnapshot.LoopContinuousContext);
        }
        else
        {
            await _bridge.RunLoopAsync(loopMdPath, _currentWorkspace.FolderPath,
                _conversationManager.CurrentSessionId);
        }
    }

    private async void StopLoopButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_settingsSnapshot.LoopMode == LoopMode.NativeAgents)
            {
                AppendLoopOutputLine("⏹ Clean loop termination requested — current iteration will finish then stop.", LoopLifecycleBrush);
                _loopController.RequestStop();
                SyncLoopPanel();
            }
            else
            {
                AppendLoopOutputLine("⏹ Clean loop termination requested — current iteration will finish then stop.", LoopLifecycleBrush);
                await _bridge.StopLoopAsync();
            }
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(StopLoopButton_Click), ex);
        }
    }

    private void LoopModeNativeRadio_Click(object sender, RoutedEventArgs e)
    {
        _settingsSnapshot = _settingsStore.SaveLoopMode(LoopMode.NativeAgents);
        _conversationManager.UpdateLoopSettingsState(LoopMode.NativeAgents, _settingsSnapshot.LoopContinuousContext);
    }

    private void LoopModeCliRadio_Click(object sender, RoutedEventArgs e)
    {
        _settingsSnapshot = _settingsStore.SaveLoopMode(LoopMode.SquadCli);
        _conversationManager.UpdateLoopSettingsState(LoopMode.SquadCli, _settingsSnapshot.LoopContinuousContext);
    }

    private void LoopContinuousContextCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _settingsSnapshot = _settingsStore.SaveLoopContinuousContext(
            LoopContinuousContextCheckBox.IsChecked == true);
        _conversationManager.UpdateLoopSettingsState(_settingsSnapshot.LoopMode, _settingsSnapshot.LoopContinuousContext);
    }

    private async void AbortLoopButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = MessageBox.Show(
                "Abort the current agent and stop the loop immediately? The current iteration's work may be incomplete.",
                "Abort Loop",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.OK)
            {
                AppendLoopOutputLine("⚡ Loop abruptly terminated via Abort — current iteration may be incomplete.", new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x44)));
                if (_settingsSnapshot.LoopMode == LoopMode.NativeAgents)
                    _loopController.RequestAbort();
                else
                    await _bridge.StopLoopAsync();
            }
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(AbortLoopButton_Click), ex);
        }
    }

    private static bool ShouldSuppressSilentBackgroundAgent(SquadSdkEvent evt) =>
        SilentBackgroundAgentPolicy.ShouldSuppressThread(evt.AgentId, evt.AgentName, evt.AgentDisplayName);


    private static readonly string[] SlashCommands = [
        "/activate", "/add-dir", "/agents", "/allow-all", "/changelog", "/clear",
        "/context", "/copy", "/delegate", "/deactivate", "/diff", "/doctor", "/experimental", "/feedback",
        "/fleet", "/dropTasks", "/help", "/hire", "/ide", "/init", "/instructions", "/login", "/logout",
        "/lsp", "/mcp", "/model", "/new", "/plan", "/pr", "/research", "/restart",
        "/resume", "/review", "/rewind", "/rename", "/retire", "/session", "/sessions", "/screenshot", "/share", "/skills",
        "/status", "/tasks", "/trace", "/update", "/usage", "/version"
    ];

    private void SyncAgentCardsWithThreads()
    {
        foreach (var thread in _agentThreadRegistry.ThreadOrder)
        {
            _agentThreadRegistry.NormalizeThreadAgentIdentity(thread);
            _agentThreadRegistry.NormalizeInactiveThreadState(thread);
        }

        EnsureDynamicAgentCards();
        UpdateAvatarSizes();
        UpdateAgentCardVisibility();

        foreach (var card in _agents)
        {
            card.IsTranscriptTargetSelected = false;
            if (!card.IsLeadAgent)
                card.Threads.Clear();
        }

        // Reset IsSecondaryPanelOpen on all threads before re-sync
        foreach (var thread in _agentThreadRegistry.ThreadOrder)
            thread.IsSecondaryPanelOpen = false;

        foreach (var thread in _agentThreadRegistry.ThreadOrder.OrderBy(candidate => candidate.StartedAt))
        {
            var card = FindAgentCardForThread(thread);
            if (card is null)
                continue;

            card.Threads.Add(thread);
        }

        RefreshSecondaryTranscriptEntries();
        SyncSelectionControllerWithUiState("SyncAgentCardsWithThreads");
        SyncTranscriptTargetIndicators();

        foreach (var card in _agents)
            SyncCardThreads(card);

        SyncAgentCardBuckets();
        // Re-derive IsSecondaryPanelOpen from live secondary panels
        foreach (var entry in _secondaryTranscripts)
            entry.Thread.IsSecondaryPanelOpen = true;
        UpdateTranscriptThreadBadge();
        ScheduleAgentPanelLayoutRefresh();
    }

    private void EnsureDynamicAgentCards()
    {
        var existingDynamicCards = _agents
            .Where(card => card.IsDynamicAgent)
            .ToArray();
        foreach (var dynamicCard in existingDynamicCards)
            _agents.Remove(dynamicCard);

        var now = DateTimeOffset.Now;
        var inactiveAllowance = DynamicAgentHistoryCardLimit;
        foreach (var thread in _agentThreadRegistry.ThreadOrder
                     .Where(candidate => BackgroundTaskPresenter.ShouldSurfaceDynamicAgentCard(candidate, now, DynamicAgentHistoryRetention))
                     .GroupBy(candidate => candidate.Title, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.OrderByDescending(AgentThreadRegistry.GetThreadLastActivityAt).First())
                     .OrderByDescending(_backgroundTaskPresenter.IsThreadActiveForDisplay)
                     .ThenByDescending(AgentThreadRegistry.GetThreadLastActivityAt))
        {
            if (!_backgroundTaskPresenter.IsThreadActiveForDisplay(thread))
            {
                if (now - AgentThreadRegistry.GetThreadLastActivityAt(thread) > DynamicAgentHistoryRetention)
                    continue;

                if (inactiveAllowance <= 0)
                    continue;

                inactiveAllowance--;
            }

            if (FindAgentCardForThread(thread, includeDynamicCards: false) is not null)
                continue;

            var card = new AgentStatusCard(
                thread.Title,
                GetAgentInitial(thread.Title),
                string.IsNullOrWhiteSpace(thread.AgentType) ? "Background Agent" : AgentThreadRegistry.HumanizeAgentName(thread.AgentType),
                thread.StatusText,
                string.Empty,
                thread.DetailText,
                DynamicAgentDefaultAccentHex,
                accentStorageKey: "dynamic:" + thread.Title,
                isDynamicAgent: true);
            ApplyAgentAccent(card, ResolveAgentAccentHex(card, isLeadAgent: false), persist: false);
            ApplyAgentImage(card, ResolveAgentImagePath(card), persist: false);
            _agents.Add(card);
        }
    }

    private AgentStatusCard? FindAgentCardForThread(
        TranscriptThreadState thread,
        bool includeDynamicCards = true)
    {
        return _agents.FirstOrDefault(card =>
            !card.IsLeadAgent &&
            (includeDynamicCards || !card.IsDynamicAgent) &&
            CardMatchesThread(card, thread));
    }

    private static bool CardMatchesThread(AgentStatusCard card, TranscriptThreadState thread)
    {
        if (!card.IsDynamicAgent && AgentThreadRegistry.HasRosterBackedIdentity(thread) &&
            !string.Equals(card.AccentStorageKey, thread.AgentCardKey?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(thread.AgentCardKey) &&
            string.Equals(card.AccentStorageKey, thread.AgentCardKey.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(thread.AgentDisplayName) &&
            string.Equals(card.Name, thread.AgentDisplayName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(thread.AgentName))
        {
            if (string.Equals(card.AccentStorageKey, thread.AgentName.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(card.Name, AgentThreadRegistry.HumanizeAgentName(thread.AgentName), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(thread.AgentId))
        {
            if (string.Equals(card.AccentStorageKey, thread.AgentId.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(card.Name, AgentThreadRegistry.HumanizeAgentName(thread.AgentId), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(thread.Title) &&
            string.Equals(card.Name, AgentThreadRegistry.HumanizeAgentName(thread.Title), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(card.Name, thread.Title, StringComparison.OrdinalIgnoreCase);
    }

    private TranscriptThreadState? GetPrimaryThread(AgentStatusCard card)
    {
        var threads = card.Threads
            .Where(thread => !thread.IsPlaceholderThread)
            .ToArray();

        var currentRunThread = threads
            .Where(_backgroundTaskPresenter.IsThreadCurrentRunForDisplay)
            .OrderByDescending(AgentThreadRegistry.GetThreadLastActivityAt)
            .ThenByDescending(thread => thread.StartedAt)
            .FirstOrDefault();
        if (currentRunThread is not null)
            return currentRunThread;

        return threads
            .OrderByDescending(AgentThreadRegistry.GetThreadLastActivityAt)
            .ThenByDescending(thread => thread.StartedAt)
            .FirstOrDefault();
    }

    private string BuildAgentCardDisplayName(
        AgentStatusCard card,
        TranscriptThreadState? primaryThread,
        DateTimeOffset now)
    {
        if (primaryThread is null || !_backgroundTaskPresenter.IsThreadCurrentRunForDisplay(primaryThread))
            return card.Name;

        return StatusTimingPresentation.AppendRunningSuffix(card.Name, primaryThread.StartedAt, now);
    }

    private static string BuildAgentCardStatusText(TranscriptThreadState thread, DateTimeOffset now)
    {
        return BuildTimedStatusText(thread.StatusText, thread.StartedAt, thread.CompletedAt, now);
    }

    private static bool IsStickyTerminalBackgroundStatus(string? statusText)
    {
        if (string.IsNullOrWhiteSpace(statusText))
            return false;

        return statusText.Trim() switch
        {
            "Failed" => true,
            "Cancelled" => true,
            _ => false
        };
    }

    private static bool ShouldDisplayTerminalAgentStatus(TranscriptThreadState thread, DateTimeOffset now)
    {
        if (IsStickyTerminalBackgroundStatus(thread.StatusText))
            return true;

        return string.Equals(thread.StatusText?.Trim(), "Completed", StringComparison.OrdinalIgnoreCase)
            && now - AgentThreadRegistry.GetThreadLastActivityAt(thread) <= AgentActiveDisplayLinger;
    }

    private void SyncCardThreads(AgentStatusCard card, DateTimeOffset? nowOverride = null)
    {
        var now = nowOverride ?? DateTimeOffset.Now;
        var orderedThreads = card.Threads
            .OrderByDescending(thread => thread.StartedAt)
            .ToArray();

        var visibleChipLimit = 3;
        var chipIndex = 0;
        foreach (var orderedThread in orderedThreads)
        {
            if (AgentThreadRegistry.HasMeaningfulThreadTranscript(orderedThread))
            {
                chipIndex++;
                orderedThread.SequenceNumber = chipIndex;
                orderedThread.ChipVisibility = chipIndex <= visibleChipLimit
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            else
            {
                orderedThread.SequenceNumber = 0;
                orderedThread.ChipVisibility = Visibility.Collapsed;
            }

            SyncThreadChip(orderedThread);
        }

        // Reorder the Threads collection in-place so the ItemsControl renders chips
        // left-to-right as #1, #2, #3, [+N]. SequenceNumber=1 is the most recent thread.
        // Threads with SequenceNumber=0 (no meaningful transcript) are pushed to the end.
        SortThreadsForDisplay(card.Threads);

        var primaryThread = GetPrimaryThread(card);

        card.DisplayName = BuildAgentCardDisplayName(card, primaryThread, now);

        if (primaryThread is null)
        {
            if (card.IsDynamicAgent)
            {
                card.StatusText = string.Empty;
                card.DetailText = string.Empty;
            }

            card.ThreadChipsVisibility = Visibility.Collapsed;
            card.OverflowChipVisibility = Visibility.Collapsed;
            card.OverflowChipText = string.Empty;
            return;
        }

        var isCurrentRunThread = _backgroundTaskPresenter.IsThreadCurrentRunForDisplay(primaryThread);
        card.StatusText = isCurrentRunThread
            ? _backgroundTaskPresenter.IsThreadStalledForDisplay(primaryThread, now)
                ? _backgroundTaskPresenter.BuildStalledStatusText(primaryThread, now)
                : BuildAgentCardStatusText(primaryThread, now)
            : ShouldDisplayTerminalAgentStatus(primaryThread, now)
                ? BuildAgentCardStatusText(primaryThread, now)
                : string.Empty;
        card.DetailText = isCurrentRunThread
            ? primaryThread.DetailText
            : string.Empty;
        var meaningfulThreadCount = orderedThreads.Count(AgentThreadRegistry.HasMeaningfulThreadTranscript);
        card.ThreadChipsVisibility = meaningfulThreadCount >= 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        var overflowCount = Math.Max(0, meaningfulThreadCount - visibleChipLimit);
        card.OverflowChipVisibility = overflowCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        card.OverflowChipText = overflowCount > 0 ? $"+{overflowCount}" : string.Empty;
        if (card.IsDynamicAgent)
        {
            card.RoleText = string.IsNullOrWhiteSpace(primaryThread.AgentType)
                ? "Background Agent"
                : AgentThreadRegistry.HumanizeAgentName(primaryThread.AgentType);
        }
    }

    /// <summary>
    /// Sorts <paramref name="threads"/> in-place so the ItemsControl renders chip buttons
    /// left-to-right as #1, #2, #3 (most-recent first). Threads with SequenceNumber=0
    /// (no meaningful transcript) are moved to the end. Uses ObservableCollection.Move()
    /// so WPF receives fine-grained CollectionChanged notifications rather than a full reset.
    /// </summary>
    private static void SortThreadsForDisplay(ObservableCollection<TranscriptThreadState> threads)
    {
        // Build the desired order: numbered threads ascending (1, 2, 3…), then un-numbered (0).
        var sorted = threads
            .OrderBy(t => t.SequenceNumber == 0 ? int.MaxValue : t.SequenceNumber)
            .ToList();

        for (var targetIndex = 0; targetIndex < sorted.Count; targetIndex++)
        {
            var currentIndex = threads.IndexOf(sorted[targetIndex]);
            if (currentIndex != targetIndex)
                threads.Move(currentIndex, targetIndex);
        }
    }

    private void UpdateAgentCardFromThread(TranscriptThreadState thread, bool syncBuckets = true)
    {
        var card = FindAgentCardForThread(thread);
        if (card is null)
        {
            SquadDashTrace.Write("AgentCards",
                $"UpdateAgentCardFromThread: card missing for thread={thread.ThreadId} selected={thread.IsSelected}; falling back to full sync");
            SyncAgentCardsWithThreads();
            return;
        }

        if (!card.Threads.Contains(thread))
            card.Threads.Add(thread);

        SyncTranscriptTargetIndicators();
        SyncCardThreads(card);
        if (syncBuckets)
        {
            SquadDashTrace.Write("AgentCards",
                $"UpdateAgentCardFromThread: full bucket sync card={card.Name} thread={thread.ThreadId} selected={thread.IsSelected} status={thread.StatusText}");
            SyncAgentCardBuckets();
        }
        else
        {
            SquadDashTrace.Write("AgentCards",
                $"UpdateAgentCardFromThread: lightweight refresh card={card.Name} thread={thread.ThreadId} selected={thread.IsSelected} status={thread.StatusText}");
        }
        if (thread.IsSelected)
            UpdateTranscriptThreadBadge();
    }

    private void SyncThreadChip(TranscriptThreadState thread)
    {
        thread.ChipLabel = thread.SequenceNumber > 0 ? $"#{thread.SequenceNumber}" : "#";
        thread.ChipToolTip = BuildThreadChipToolTip(thread);
        thread.ChipFontWeight = thread.IsSelected ? FontWeights.SemiBold : FontWeights.Normal;

        var status = thread.StatusText.Trim();
        if (thread.IsSelected)
        {
            thread.ChipBackground = (Brush)Application.Current.Resources["ChipSelectedSurface"];
            thread.ChipBorderBrush = (Brush)Application.Current.Resources["ChipSelectedBorder"];
            thread.ChipForeground = (Brush)Application.Current.Resources["ChipSelectedText"];
        }
        else
        {
            switch (status)
            {
                case "Completed":
                    thread.ChipBackground = (Brush)Application.Current.Resources["ChipCompletedSurface"];
                    thread.ChipBorderBrush = (Brush)Application.Current.Resources["ChipCompletedBorder"];
                    thread.ChipForeground = (Brush)Application.Current.Resources["ChipCompletedText"];
                    break;

                case "Failed":
                case "Cancelled":
                    thread.ChipBackground = (Brush)Application.Current.Resources["ChipFailedSurface"];
                    thread.ChipBorderBrush = (Brush)Application.Current.Resources["ChipFailedBorder"];
                    thread.ChipForeground = (Brush)Application.Current.Resources["ChipFailedText"];
                    break;

                default:
                    thread.ChipBackground = (Brush)Application.Current.Resources["ChipSurface"];
                    thread.ChipBorderBrush = (Brush)Application.Current.Resources["ChipBorder"];
                    thread.ChipForeground = (Brush)Application.Current.Resources["ChipText"];
                    break;
            }
        }

        var chipCard = FindAgentCardForThread(thread);
        thread.ChipSelectionIndicatorBrush = (thread.IsSelected || thread.IsSecondaryPanelOpen) && chipCard is not null
            ? chipCard.EffectiveAccentBrush
            : Brushes.Transparent;
    }

    private static string BuildThreadChipToolTip(TranscriptThreadState thread)
    {
        var lines = new List<string> {
            thread.Title
        };

        if (!string.IsNullOrWhiteSpace(thread.LatestIntent))
            lines.Add(thread.LatestIntent.Trim());
        if (!string.IsNullOrWhiteSpace(thread.StatusText))
            lines.Add("Status: " + thread.StatusText);
        if (!string.IsNullOrWhiteSpace(thread.DetailText))
            lines.Add(thread.DetailText);
        if (!string.IsNullOrWhiteSpace(thread.AgentId))
            lines.Add("Agent: " + thread.AgentId);

        return string.Join(Environment.NewLine, lines);
    }

    private void AppendLine(string text, Brush? color = null) =>
        AppendLine(CoordinatorThread, text, color);

    private void AppendLine(TranscriptThreadState thread, string text, Brush? color = null)
    {
        if (thread.CurrentTurn is not null)
        {
            if (thread.CurrentTurn.ResponseTextBuilder.Length > 0)
                thread.CurrentTurn.ResponseTextBuilder.AppendLine();
            if (!string.IsNullOrEmpty(text))
                thread.CurrentTurn.ResponseTextBuilder.Append(text);
            AppendResponseSegment(thread, text, startOnNewLine: true);
            ScrollToEndIfAtBottom(thread);
            return;
        }

        var paragraph = CreateTranscriptParagraph();

        if (!string.IsNullOrEmpty(text))
        {
            if (color is null)
            {
                _markdownRenderer.AppendInlineMarkdown(paragraph.Inlines, text);
            }
            else
            {
                var run = new Run(text)
                {
                    Foreground = color
                };
                paragraph.Inlines.Add(run);
            }
        }

        thread.Document.Blocks.Add(paragraph);
        ScrollToEndIfAtBottom(thread);
    }

    private void AppendText(string text) =>
        AppendText(CoordinatorThread, text);

    private void AppendText(TranscriptThreadState thread, string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        CollapseCurrentTurnThoughts(thread);
        thread.CurrentTurn?.ResponseTextBuilder.Append(text);
        AppendResponseSegment(thread, text);
        ScrollToEndIfAtBottom(thread);
        _searchAdorner?.InvalidateHighlights();
    }

    private static void AppendParagraphText(
        Paragraph paragraph,
        string? text,
        Brush? color = null,
        bool startOnNewLine = false)
    {
        if (paragraph is null || string.IsNullOrEmpty(text))
            return;

        if (startOnNewLine && paragraph.Inlines.Count > 0)
            paragraph.Inlines.Add(new LineBreak());

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var segments = normalized.Split('\n');

        for (var index = 0; index < segments.Length; index++)
        {
            if (index > 0)
                paragraph.Inlines.Add(new LineBreak());

            if (segments[index].Length == 0)
                continue;

            var run = new Run(segments[index]);
            if (color is not null)
                run.Foreground = color;

            paragraph.Inlines.Add(run);
        }
    }

    private void OutputTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        try
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                return;

            // Capture text anchor under the mouse before zoom
            var mousePos = e.GetPosition(OutputTextBox);
            var anchor = OutputTextBox.GetPositionFromPoint(mousePos, snapToText: true);

            _transcriptFontSize = Math.Clamp(
                _transcriptFontSize + (e.Delta > 0 ? TranscriptFontSizeStep : -TranscriptFontSizeStep),
                TranscriptFontSizeMin,
                TranscriptFontSizeMax);
            ApplyTranscriptFontSize();
            _settingsSnapshot = _settingsStore.SaveTranscriptFontSize(_transcriptFontSize);

            // After layout, scroll so the anchor stays under the mouse
            if (anchor is not null)
            {
                _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                {
                    var sv = OutputTextBox.Template?.FindName("PART_ContentHost", OutputTextBox) as ScrollViewer;
                    if (sv is null)
                        return;
                    var newRect = anchor.GetCharacterRect(LogicalDirection.Forward);
                    sv.ScrollToVerticalOffset(sv.VerticalOffset + (newRect.Top - mousePos.Y));
                });
            }

            e.Handled = true;
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(OutputTextBox_PreviewMouseWheel), ex);
        }
    }

    /// <summary>
    /// Clicking anywhere inside the transcript RichTextBox dismisses the floating
    /// scroll-to-bottom button (the user has re-engaged with the transcript directly).
    /// Uses PreviewMouseDown so the event fires before the RichTextBox consumes it.
    /// Note: clicking the overlay Button itself does NOT tunnel through OutputTextBox
    /// because the Button is a sibling in the Grid, not a child of the RichTextBox.
    /// </summary>
    private void OutputTextBox_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            _scrollController.DismissScrollButton();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(OutputTextBox_PreviewMouseDown), ex);
        }
    }

    /// <summary>
    /// Clicking the floating scroll-to-bottom button jumps to the end of the transcript
    /// and re-enables auto-scroll.
    /// </summary>
    private void ScrollToBottomButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _scrollController.ScrollToBottom();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(ScrollToBottomButton_Click), ex);
        }
    }

    private static double ToolIconSizeForFontSize(double fontSize) => Math.Round(fontSize * 1.1);

    private void ApplyTranscriptFontSize()
    {
        OutputTextBox.FontSize = _transcriptFontSize;
        var iconSize = ToolIconSizeForFontSize(_transcriptFontSize);
        foreach (var img in _toolIconImages)
        {
            img.Width = iconSize;
            img.Height = iconSize;
        }
        foreach (var entry in _secondaryTranscripts)
            entry.TranscriptBox.FontSize = _transcriptFontSize;
        foreach (var thread in EnumerateTranscriptThreads())
            ApplyTranscriptFontSizeToDocument(thread.Document);

        // Adorner overlay text uses the RichTextBox FontSize — invalidate so it redraws
        // at the new size without waiting for a layout pass.
        _searchAdorner?.InvalidateHighlights();
    }

    private void ApplyTranscriptFontSizeToDocument(FlowDocument document)
    {
        document.FontSize = _transcriptFontSize;

        var codeBlockFontSize = _transcriptFontSize * 0.9;
        foreach (var block in document.Blocks.OfType<Section>())
            foreach (var inner in block.Blocks.OfType<BlockUIContainer>())
                if (inner.Child is TextBox { Tag: "codeblock" } codeBox)
                    codeBox.FontSize = codeBlockFontSize;
    }

    private ContextMenu CreateThinkingContextMenu(TranscriptTurnView view)
    {
        var copyMenuItem = new MenuItem
        {
            Header = "Copy Thinking Block",
            Tag = view
        };
        copyMenuItem.Click += CopyThinkingMenuItem_Click;

        var menu = new ContextMenu();
        menu.Items.Add(copyMenuItem);
        menu.Opened += ThinkingContextMenu_Opened;
        return menu;
    }

    private void ThinkingContextMenu_Opened(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not ContextMenu { Items.Count: > 0 } menu)
                return;

            if (menu.Items[0] is not MenuItem { Tag: TranscriptTurnView view } copyMenuItem)
                return;

            copyMenuItem.IsEnabled = !string.IsNullOrWhiteSpace(BuildThinkingClipboardText(view));
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(ThinkingContextMenu_Opened), ex);
        }
    }

    private void CopyThinkingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem { Tag: TranscriptTurnView view })
                return;

            var thinkingText = BuildThinkingClipboardText(view);

            if (string.IsNullOrWhiteSpace(thinkingText))
                return;

            Clipboard.SetText(thinkingText);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(CopyThinkingMenuItem_Click), ex);
        }
    }

    private string BuildThinkingClipboardText(TranscriptTurnView view)
    {
        var builder = new StringBuilder();
        foreach (var item in GetOrderedThinkingNarrativeItems(view))
        {
            switch (item)
            {
                case TranscriptThoughtEntry thought:
                    var thoughtText = FormatThinkingText(thought.RawTextBuilder.ToString());
                    if (!string.IsNullOrWhiteSpace(thoughtText))
                        builder.AppendLine($"{thought.Speaker}: {thoughtText}");
                    break;

                case TranscriptThinkingBlockView block:
                    builder.AppendLine("Tooling...");
                    foreach (var entry in block.ToolEntries.OrderBy(tool => tool.StartedAt))
                    {
                        var icon = entry.IconTextBlock.Text?.Trim();
                        var emoji = ToolTranscriptFormatter.GetToolEmoji(entry.Descriptor).Trim();
                        var message = ExtractInlineText(entry.MessageTextBlock.Inlines).Trim();
                        var combined = string.Join(" ", new[] { icon, emoji, message }.Where(part => !string.IsNullOrWhiteSpace(part)));
                        if (!string.IsNullOrWhiteSpace(combined))
                            builder.AppendLine("  " + combined);
                    }
                    break;
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static IEnumerable<object> GetOrderedThinkingNarrativeItems(TranscriptTurnView view)
    {
        return view.ThoughtEntries
            .Cast<object>()
            .Concat(view.ThinkingBlocks)
            .OrderBy(item => item switch
            {
                TranscriptThoughtEntry thought => thought.Sequence,
                TranscriptThinkingBlockView block => block.Sequence,
                _ => int.MaxValue
            });
    }

    private static string ExtractInlineText(InlineCollection inlines) =>
        TranscriptCopyService.ExtractInlineText(inlines);

    private void OutputTextBox_CopyExecuted(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
    {
        try
        {
            var text = TranscriptCopyService.BuildSelectionText(OutputTextBox);
            if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
            e.Handled = true;
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(OutputTextBox_CopyExecuted), ex);
        }
    }

    private void OutputTextBox_CopyCanExecute(object sender, System.Windows.Input.CanExecuteRoutedEventArgs e)
    {
        try
        {
            e.CanExecute = !OutputTextBox.Selection.IsEmpty;
            e.Handled = true;
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(OutputTextBox_CopyCanExecute), ex);
        }
    }

    private void PromptTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        try
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                return;

            _promptFontSize = Math.Clamp(
                _promptFontSize + (e.Delta > 0 ? PromptFontSizeStep : -PromptFontSizeStep),
                PromptFontSizeMin,
                PromptFontSizeMax);

            ApplyPromptFontSize();
            _settingsSnapshot = _settingsStore.SavePromptFontSize(_promptFontSize);
            e.Handled = true;
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(PromptTextBox_PreviewMouseWheel), ex);
        }
    }

    private void ApplyPromptFontSize()
    {
        PromptTextBox.FontSize = _promptFontSize;
    }

    private void ApplyDocSourceFontSize()
    {
        if (DocSourceTextBox is not null)
            DocSourceTextBox.FontSize = _docSourceFontSize;
    }

    private void DocSourceTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        try
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                return;

            _docSourceFontSize = Math.Clamp(
                _docSourceFontSize + (e.Delta > 0 ? DocSourceFontSizeStep : -DocSourceFontSizeStep),
                DocSourceFontSizeMin,
                DocSourceFontSizeMax);

            ApplyDocSourceFontSize();
            _settingsSnapshot = _settingsStore.SaveDocSourceFontSize(_docSourceFontSize);
            e.Handled = true;
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(DocSourceTextBox_PreviewMouseWheel), ex);
        }
    }

    private void PromptTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            var modifiers = Keyboard.Modifiers;
            var action = PromptInputBehavior.ResolveAction(
                MapPromptInputKey(e.Key),
                ctrlPressed: modifiers.HasFlag(ModifierKeys.Control),
                shiftPressed: modifiers.HasFlag(ModifierKeys.Shift),
                runButtonEnabled: RunButton.IsEnabled,
                isMultiLinePrompt: IsMultiLinePrompt(),
                isIntelliSenseOpen: _intelliSenseState is not null);

            switch (action)
            {
                case PromptInputAction.SubmitPrompt:
                    RunButton_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                    break;

                case PromptInputAction.NavigateHistoryPrevious:
                    _conversationManager.NavigateHistory(-1);
                    e.Handled = true;
                    break;

                case PromptInputAction.NavigateHistoryNext:
                    _conversationManager.NavigateHistory(1);
                    e.Handled = true;
                    break;

                case PromptInputAction.IntelliSenseUp:
                    _intelliSenseState = IntelliSenseController.MoveSelection(_intelliSenseState!, -1);
                    UpdateIntelliSensePopup();
                    e.Handled = true;
                    break;

                case PromptInputAction.IntelliSenseDown:
                    _intelliSenseState = IntelliSenseController.MoveSelection(_intelliSenseState!, +1);
                    UpdateIntelliSensePopup();
                    e.Handled = true;
                    break;

                case PromptInputAction.IntelliSenseAccept:
                    ApplyIntelliSenseAccept(andSubmit: e.Key == Key.Return || e.Key == Key.Enter || e.Key == Key.Tab);
                    e.Handled = true;
                    break;

                case PromptInputAction.IntelliSenseDismiss:
                    _intelliSenseState = null;
                    UpdateIntelliSensePopup();
                    e.Handled = true;
                    break;
            }
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(PromptTextBox_KeyDown), ex);
        }
    }

    private static bool IsCtrlKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl;

    private static bool IsShiftKey(Key key) =>
        key is Key.LeftShift or Key.RightShift;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            // ── Feature 1: Guard against rerouting input when DocSourceTextBox has focus ──
            if (DocSourceTextBox?.IsFocused == true)
            {
                // Allow Ctrl+F to trigger find-in-source instead of transcript search
                if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    ShowDocSourceFindBar();
                    e.Handled = true;
                    return;
                }
                // Ctrl+B: wrap selection (or insert empty pair) in markdown bold
                if (e.Key == Key.B && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    DocSourceTextBox_ApplyBold();
                    e.Handled = true;
                    return;
                }
                // Ctrl+I: wrap selection in markdown italic
                if (e.Key == Key.I && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    DocSourceTextBox_ApplyItalic();
                    e.Handled = true;
                    return;
                }
                // Let bare Ctrl key events fall through so the double-tap PTT state machine
                // can track them. All other keys are eaten here.
                if (!IsCtrlKey(e.Key))
                    return;
            }

            // Also guard the transcript search box and the doc find text box
            if (SearchBox?.IsFocused == true) return;
            if (_docSourceFindTextBox?.IsFocused == true) return;

            // ── Fullscreen transcript: any printable key (no Ctrl/Alt) peeks prompt without exiting fullscreen ──
            // Only intercept on the FIRST key (when prompt is hidden); once visible let the TextBox handle input normally.
            if (_transcriptFullScreenEnabled
                && !_fullScreenPromptVisible
                && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == ModifierKeys.None
                && IsPrintableKey(e.Key))
            {
                ShowFullScreenPrompt();
                var ch = KeyToChar(e.Key, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                if (ch.HasValue)
                    PromptTextBox.AppendText(ch.Value.ToString());
                PromptTextBox.CaretIndex = PromptTextBox.Text.Length;
                PromptTextBox.Focus();
                e.Handled = true;
                return;
            }

            // ── Search shortcuts ─────────────────────────────────────────────────
            if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                SearchBox?.Focus();
                SearchBox?.SelectAll();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F3)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                    _ = NavigateToMatchAsync(_searchMatchCursor - 1);
                else
                    _ = NavigateToMatchAsync(_searchMatchCursor + 1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F11)
            {
                SetTranscriptFullScreen(!_transcriptFullScreenEnabled);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && _transcriptFullScreenEnabled && _pttState != PttState.Active)
            {
                e.Handled = true;
                if (_fullScreenPromptVisible)
                    HideFullScreenPrompt();
                else
                    SetTranscriptFullScreen(false);
                return;
            }

            // ── Screenshot shortcut (Ctrl+Shift+C) ──────────────────────────────
            if (e.Key == Key.C &&
                (Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
                (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                ShowScreenshotOverlay();
                e.Handled = true;
                return;
            }

            // ── Fullscreen: paste clipboard text to prompt (Ctrl+V or Shift+Insert) ──
            if (_transcriptFullScreenEnabled &&
                ((e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0) ||
                 (e.Key == Key.Insert && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)) &&
                Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (!_fullScreenPromptVisible)
                    ShowFullScreenPrompt();
                PromptTextBox.AppendText(text);
                PromptTextBox.CaretIndex = PromptTextBox.Text.Length;
                PromptTextBox.Focus();
                e.Handled = true;
                return;
            }

            // ── Prompt text box: Ctrl+B for markdown bold ─────────────────────
            if (e.Key == Key.B &&
                (Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
                PromptTextBox?.IsFocused == true)
            {
                PromptTextBox_ApplyBold();
                e.Handled = true;
                return;
            }

            // ── Page Up / Page Down: scroll main transcript ───────────────────
            if (e.Key == Key.PageUp && PromptTextBox?.IsFocused != true)
            {
                _scrollController.ScrollPageUp();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.PageDown && PromptTextBox?.IsFocused != true)
            {
                _scrollController.ScrollPageDown();
                e.Handled = true;
                return;
            }

            switch (_pttState)
            {
                case PttState.Idle:
                    if (IsCtrlKey(e.Key) && !e.IsRepeat)
                    {
                        _ctrlFirstDownTime = DateTime.UtcNow;
                        _pttState = PttState.TapDown;
                    }
                    break;

                case PttState.TapDown:
                    if (IsCtrlKey(e.Key))
                    {
                        // Still holding first Ctrl — check if held too long
                        if (e.IsRepeat && (DateTime.UtcNow - _ctrlFirstDownTime).TotalMilliseconds > PttMaxTapHoldMs)
                            _pttState = PttState.Idle;
                    }
                    else
                    {
                        // Any other key invalidates the sequence
                        _pttState = PttState.Idle;
                    }
                    break;

                case PttState.TapReleased:
                    if (IsCtrlKey(e.Key) && !e.IsRepeat)
                    {
                        var gapMs = (DateTime.UtcNow - _ctrlFirstReleaseTime).TotalMilliseconds;
                        if (gapMs <= PttDoubleClickTime)
                        {
                            // Resolve the target TextBox at activation time.
                            // DocSourceTextBox focus is still valid here since only Ctrl key was pressed.
                            _pttTargetTextBox = DocSourceTextBox?.IsFocused == true
                                ? DocSourceTextBox
                                : PromptTextBox;

                            if (_pttTargetTextBox != null)
                            {
                                // Capture caret/selection before the PTT panel becomes visible (layout shifts can reset it).
                                _sessionCaretIndex      = _pttTargetTextBox.SelectionStart;
                                _sessionSelectionLength = _pttTargetTextBox.SelectionLength;
                                // Only auto-send when the target is the prompt box.
                                _voiceStartedWithSendEnabled = _pttTargetTextBox == PromptTextBox && !_isPromptRunning;
                                _pttState = PttState.Active;
                                _ = StartPushToTalkAsync();
                            }
                        }
                        else
                        {
                            // Too slow — treat as fresh first tap
                            _ctrlFirstDownTime = DateTime.UtcNow;
                            _pttState = PttState.TapDown;
                        }
                    }
                    else if (!IsCtrlKey(e.Key))
                    {
                        _pttState = PttState.Idle;
                    }
                    break;

                case PttState.Active:
                    if (IsCtrlKey(e.Key) && e.IsRepeat)
                    {
                        // Still holding Ctrl — keep recording
                    }
                    else if (e.Key == Key.Escape)
                    {
                        e.Handled = true;
                        _ = StopPushToTalkAsync(send: false);
                    }
                    else if (IsShiftKey(e.Key))
                    {
                        // Shift held during recording — keep recording, will suppress send on Ctrl release
                    }
                    else if (!IsCtrlKey(e.Key))
                    {
                        // Any other key disengages PTT (no send)
                        _ = StopPushToTalkAsync(send: false);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(Window_PreviewKeyDown), ex);
        }
    }

    /// <summary>Resets the PTT double-tap state machine to Idle (called when an owned window closes).</summary>
    internal void ResetPttState()
    {
        _pttState = PttState.Idle;
        _ctrlFirstDownTime = default;
        _ctrlFirstReleaseTime = default;
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        try
        {
            switch (_pttState)
            {
                case PttState.TapDown:
                    if (IsCtrlKey(e.Key))
                    {
                        var heldMs = (DateTime.UtcNow - _ctrlFirstDownTime).TotalMilliseconds;
                        if (heldMs <= PttMaxTapHoldMs)
                        {
                            _ctrlFirstReleaseTime = DateTime.UtcNow;
                            _pttState = PttState.TapReleased;
                        }
                        else
                        {
                            _pttState = PttState.Idle;
                        }
                    }
                    break;

                case PttState.Active:
                    if (IsShiftKey(e.Key))
                    {
                        _pttShiftTappedDuringRecording = true;
                        _pttWindow?.MarkShiftSuppressed();
                    }
                    else if (IsCtrlKey(e.Key))
                    {
                        var shiftHeld = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0;
                        var suppress = shiftHeld || _pttShiftTappedDuringRecording || _pttHadPreexistingText;
                        // Send only if PTT started with Send enabled AND no suppression flags
                        _ = StopPushToTalkAsync(send: _voiceStartedWithSendEnabled && !suppress);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(Window_PreviewKeyUp), ex);
        }
    }

    private async Task StartPushToTalkAsync()
    {
        var target = _pttTargetTextBox ?? PromptTextBox;

        // In fullscreen transcript mode, peek the prompt so the user can see dictated text.
        if (_transcriptFullScreenEnabled && !_fullScreenPromptVisible)
            ShowFullScreenPrompt();

        _pttHadPreexistingText = !string.IsNullOrEmpty(target.Text);
        _pttShiftTappedDuringRecording = false;
        var key = Environment.GetEnvironmentVariable("SQUAD_SPEECH_KEY", EnvironmentVariableTarget.User);
        var region = _settingsSnapshot.SpeechRegion;

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
        {
            _pttState = PttState.Idle;
            return;
        }

        // Only show the "release to send" hint when targeting the prompt and it's empty.
        _pttWindow = new PushToTalkWindow(this, showHint: target == PromptTextBox && !_pttHadPreexistingText);
        PositionPttWindow();
        _pttWindow.Show();
        _pttWindow.VolumeBar.Height = 0;

        _speechService = new SpeechRecognitionService();

        _speechService.PhraseRecognized += (_, text) =>
            Dispatcher.BeginInvoke(() => AppendSpeechToPrompt(text));

        _speechService.VolumeChanged += (_, level) =>
            Dispatcher.BeginInvoke(() =>
            {
                if (_pttWindow is not null)
                    _pttWindow.VolumeBar.Height = Math.Max(2, level * 36);
            });

        _speechService.RecognitionError += (_, msg) =>
            Dispatcher.BeginInvoke(() =>
            {
                _ = StopPushToTalkAsync(send: false);
                AppendLine("[voice error] " + msg, System.Windows.Media.Brushes.Red);
            });

        try
        {
            var phraseHints = BuildSpeechPhraseHints();
            await _speechService.StartAsync(key, region, phraseHints).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                _pttState = PttState.Idle;
                ClosePttWindow();
                _speechService?.Dispose();
                _speechService = null;
                AppendLine("[voice error] " + ex.Message, System.Windows.Media.Brushes.Red);
            });
        }
    }

    /// <summary>
    /// Builds the phrase list registered with Azure Speech to improve recognition
    /// of unusual team member names (e.g. "Lyra Morn", "Vesper Knox", "Jae Min Kade").
    /// Emits both full names and individual name tokens so partial references ("ask Lyra") work.
    /// </summary>
    private IReadOnlyList<string> BuildSpeechPhraseHints()
    {
        if (_currentWorkspace is null)
            return [];

        try
        {
            var members = _teamRosterLoader.Load(_currentWorkspace.FolderPath);
            var phrases = new List<string>(members.Count * 3);
            foreach (var member in members)
            {
                if (string.IsNullOrWhiteSpace(member.Name))
                    continue;

                phrases.Add(member.Name);

                // Also add each individual token so partial references like "ask Lyra" resolve
                foreach (var token in member.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (token.Length > 2)
                        phrases.Add(token);
            }
            return phrases;
        }
        catch
        {
            return [];
        }
    }

    private void PositionPttWindow()
    {
        if (_pttWindow is null)
            return;

        var target = _pttTargetTextBox ?? PromptTextBox;
        System.Windows.Point physicalPoint;
        try
        {
            var caretRect = target.GetRectFromCharacterIndex(_sessionCaretIndex);
            physicalPoint = target.PointToScreen(new System.Windows.Point(caretRect.Left, caretRect.Bottom));
        }
        catch
        {
            physicalPoint = target.PointToScreen(new System.Windows.Point(0, target.ActualHeight + 4));
        }

        // Get the work area (physical px) for whichever monitor the caret is on.
        var physWa = NativeMethods.GetWorkAreaForPhysicalPoint((int)physicalPoint.X, (int)physicalPoint.Y);

        // Convert everything to WPF logical DIPs.
        var logicalPoint = DpiHelper.PhysicalToLogical(target, physicalPoint);
        var logicalWaOrigin = DpiHelper.PhysicalToLogical(target, new System.Windows.Point(physWa.Left, physWa.Top));
        var logicalWaCorner = DpiHelper.PhysicalToLogical(target, new System.Windows.Point(physWa.Right, physWa.Bottom));
        var logicalWorkArea = new System.Windows.Rect(logicalWaOrigin, logicalWaCorner);

        _pttWindow.PositionUnderCaret(logicalPoint, logicalWorkArea);
    }

    private void ClosePttWindow()
    {
        _pttWindow?.Close();
        _pttWindow = null;
    }

    private async Task StopPushToTalkAsync(bool send)
    {
        _pttState = PttState.Idle;
        var wasTargetingPrompt = _pttTargetTextBox is null || _pttTargetTextBox == PromptTextBox;
        _pttTargetTextBox = null;
        ClosePttWindow();

        if (_restartPending && !_isPromptRunning)
        {
            Close();
            return;
        }

        var service = _speechService;
        _speechService = null;

        if (service != null)
        {
            try { await service.StopAsync().ConfigureAwait(false); }
            catch { }
            service.Dispose();
        }

        if (send && wasTargetingPrompt)
        {
            await Task.Delay(220).ConfigureAwait(false);
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrWhiteSpace(PromptTextBox.Text) && RunButton.IsEnabled)
                    RunButton_Click(this, new RoutedEventArgs());
            });
        }
    }

    private void AppendSpeechToPrompt(string text)
    {
        _promptHasVoiceInput = true;
        var target = _pttTargetTextBox ?? PromptTextBox;
        var current = target.Text;
        // Clamp in case text was externally modified since session start.
        var caretIndex = Math.Min(_sessionCaretIndex, current.Length);
        // If there was a selection when PTT started, replace it on the first insert.
        var selLength  = _sessionSelectionLength;
        _sessionSelectionLength = 0; // consume once; subsequent dictation appends
        var selEndIndex = Math.Min(caretIndex + selLength, current.Length);
        var leftContext = current[..caretIndex];
        var rightContext = current[selEndIndex..];
        var precedingChar = caretIndex > 0 ? current[caretIndex - 1] : '\0';
        var prefix = precedingChar != '\0' && precedingChar != ' ' && precedingChar != '(' &&
                     precedingChar != '\n' && precedingChar != '\r' ? " " : string.Empty;
        var processed = VoiceInsertionHeuristics.Apply(leftContext, text, rightContext);
        var insert = prefix + processed;
        target.Text = leftContext + insert + rightContext;
        target.CaretIndex = caretIndex + insert.Length;
        _sessionCaretIndex = caretIndex + insert.Length;
    }

    private void UpdateVoiceHintVisibility()
    {
        var hasKey = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("SQUAD_SPEECH_KEY", EnvironmentVariableTarget.User));
        var hasRegion = !string.IsNullOrWhiteSpace(_settingsSnapshot.SpeechRegion);
        VoiceHintText.Visibility = hasKey && hasRegion ? Visibility.Collapsed : Visibility.Visible;
        BuildShortcutsHint(hasKey && hasRegion);
    }

    private static Run Bold(string text) => new Run(text) { FontWeight = FontWeights.Bold };
    private static Run Normal(string text) => new Run(text);
    private static Run Gap() => new Run("  ");

    private void BuildShortcutsHint(bool includePtt)
    {
        var inlines = PromptShortcutsHintTextBlock.Inlines;
        inlines.Clear();

        // Sentence 1
        inlines.Add(Bold("Enter"));
        inlines.Add(Normal(" sends."));

        // Sentence 2
        inlines.Add(Gap());
        inlines.Add(Bold("Shift"));
        inlines.Add(Normal("+"));
        inlines.Add(Bold("Enter"));
        inlines.Add(Normal(" adds a new line."));

        // Sentence 3
        inlines.Add(Gap());
        inlines.Add(Bold("Ctrl"));
        inlines.Add(Normal("+"));
        inlines.Add(Bold("Up"));
        inlines.Add(Normal("/"));
        inlines.Add(Bold("Down"));
        inlines.Add(Normal(" reviews prompt history."));

        // Sentence 4 — PTT (only when voice is configured)
        if (includePtt)
        {
            inlines.Add(Gap());
            inlines.Add(Bold("Double-tap"));
            inlines.Add(Normal(" "));
            inlines.Add(Bold("Ctrl"));
            inlines.Add(Normal(" (and "));
            inlines.Add(Bold("hold"));
            inlines.Add(Normal(") for push to talk."));
        }
    }

    private void VoiceHintLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PreferencesMenuItem_Click(sender, e);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(VoiceHintLink_Click), ex);
        }
    }

    private void PromptTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            if (_conversationManager.IsApplyingHistoryEntry)
                return;

            if (string.IsNullOrEmpty(PromptTextBox.Text))
            {
                _promptHasVoiceInput = false;
                // In fullscreen, hide the peeked prompt when text is cleared
                if (_transcriptFullScreenEnabled && _fullScreenPromptVisible)
                    HideFullScreenPrompt();
            }

            _conversationManager.HistoryIndex = null;
            _conversationManager.HistoryDraft = PromptTextBox.Text;
            _conversationManager.UpdatePromptDraftState();
            UpdateInteractiveControlState();
            TryUpdateIntelliSense();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(PromptTextBox_TextChanged), ex);
        }
    }

    private void TryUpdateIntelliSense()
    {
        if (_conversationManager.IsApplyingHistoryEntry || _isApplyingIntelliSenseAccept)
            return;

        var text = PromptTextBox.Text;
        var caret = PromptTextBox.CaretIndex;

        if (_intelliSenseState is not null)
        {
            _intelliSenseState = IntelliSenseController.UpdateFromText(_intelliSenseState, text, caret);
            UpdateIntelliSensePopup();
            return;
        }

        // Check for [ trigger — only when [ is the first char (prompt is otherwise empty)
        if (caret == 1 && text[0] == '[')
        {
            var options = GetCurrentQuickReplyOptions();
            if (options.Length > 0)
            {
                _intelliSenseState = IntelliSenseController.TryActivate('[', caret - 1, options);
                UpdateIntelliSensePopup();
                return;
            }
        }

        // / trigger for slash commands — only when / is the first char, no spaces, and the
        // text itself contains no newline.
        //
        // Bug 3 fix: check !text.Contains('\n') (full text) rather than !text[..caret].Contains('\n').
        // When Enter inserts a newline WPF fires TextChanged before updating CaretIndex, so the
        // stale caret (pointing before the '\n') caused text[..caret] to pass the check even
        // though the text already contained a newline.  Checking the full text is caret-lag-proof.
        //
        // Bug 2 fix: pass text.Length (not caret) to UpdateFromText on re-activation.
        // WPF CaretIndex can lag by one position when TextChanged fires immediately after a
        // Backspace key event, yielding caret=1 for text "/t".  UpdateFromText("/t", 1) builds
        // filter="/" and shows ALL commands instead of the filtered subset.  Using text.Length
        // always places the filter at the end of the typed text, which is where the user's
        // logical cursor is during re-activation.
        if (caret > 0 && text[0] == '/' && !text.Contains(' ') && !text.Contains('\n'))
        {
            var activated = IntelliSenseController.TryActivate('/', 0, SlashCommands, filterIncludesTrigger: true);
            _intelliSenseState = activated is not null
                ? IntelliSenseController.UpdateFromText(activated, text, text.Length)
                : null;
            UpdateIntelliSensePopup();
        }
    }

    private string[] GetCurrentQuickReplyOptions()
    {
        var latestResponse = CoordinatorThread?.LatestResponse;
        if (string.IsNullOrEmpty(latestResponse))
            return _currentQuickReplyOptions;
        return TryExtractQuickReplyOptions(latestResponse, out _, out var options)
            ? options
            : _currentQuickReplyOptions;
    }

    private void UpdateIntelliSensePopup()
    {
        if (_intelliSenseState is null || _intelliSenseState.FilteredSuggestions.Count == 0)
        {
            IntelliSensePopup.IsOpen = false;
            return;
        }

        IntelliSenseList.Items.Clear();
        foreach (var suggestion in _intelliSenseState.FilteredSuggestions)
            IntelliSenseList.Items.Add(suggestion);

        IntelliSenseList.SelectedIndex = _intelliSenseState.SelectedIndex;
        if (IntelliSenseList.SelectedItem is not null)
            IntelliSenseList.ScrollIntoView(IntelliSenseList.SelectedItem);

        IntelliSensePopup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
        {
            var caretRect = PromptTextBox.GetRectFromCharacterIndex(PromptTextBox.CaretIndex);
            return new[] {
                new CustomPopupPlacement(
                    new Point(caretRect.Left, caretRect.Bottom),
                    PopupPrimaryAxis.Vertical)
            };
        };
        IntelliSensePopup.IsOpen = true;
    }

    private void ApplyIntelliSenseAccept(bool andSubmit)
    {
        if (_intelliSenseState is null) return;
        var (newText, newCaret) = IntelliSenseController.Accept(
            _intelliSenseState, PromptTextBox.Text, PromptTextBox.CaretIndex);

        // If Tab-completing a slash command that requires a parameter, insert a trailing
        // space and keep focus in the prompt so the user can type the argument.
        if (andSubmit && _intelliSenseState.TriggerChar == '/' && SlashCommandParameterPolicy.RequiresParameter(newText.Trim()))
        {
            newText = newText.TrimEnd() + " ";
            newCaret = newText.Length;
            andSubmit = false;
        }

        _isApplyingIntelliSenseAccept = true;
        try
        {
            _intelliSenseState = null;
            PromptTextBox.Text = newText;
            PromptTextBox.CaretIndex = newCaret;
        }
        finally
        {
            _isApplyingIntelliSenseAccept = false;
        }
        UpdateIntelliSensePopup();
        if (andSubmit)
            RunButton_Click(this, new RoutedEventArgs());
    }

    private void AgentCardBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: AgentStatusCard agentCard })
                return;
            if (e.OriginalSource is DependencyObject source && FindVisualAncestor<Button>(source) is not null)
                return;

            bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (shiftHeld)
            {
                SyncSelectionControllerWithUiState("AgentCardBorder_MouseLeftButtonUp.Shift");
                _selectionController.HandleCardClick(agentCard, shiftHeld: true);
            }
            else
            {
                ShowSingleTranscript(agentCard);
            }
            e.Handled = true;
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(AgentCardBorder_MouseLeftButtonUp), ex);
        }
    }

    private void AgentCardBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: AgentStatusCard agentCard })
                return;

            // Parse accent color
            var accentColor = (System.Windows.Media.Color)ColorConverter.ConvertFromString(agentCard.AccentColorHex);

            // If this is the lead/coordinator agent, apply glow to main transcript border
            if (agentCard.IsLeadAgent)
            {
                if (_mainTranscriptVisible && MainTranscriptBorder is not null)
                {
                    var glow = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = accentColor,
                        BlurRadius = 20,
                        ShadowDepth = 0,
                        Opacity = 0.4
                    };
                    MainTranscriptBorder.Effect = glow;

                    var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation(0.4, 1.0, TimeSpan.FromMilliseconds(2000));
                    glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, opacityAnim);
                }
                return;
            }

            // Find open secondary transcript panel for this agent
            var entry = _secondaryTranscripts.FirstOrDefault(t => ReferenceEquals(t.Agent, agentCard));
            if (entry is null)
                return;

            // Apply pulsing glow effect to secondary panel
            var secondaryGlow = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = accentColor,
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 0.4
            };
            entry.PanelBorder.Effect = secondaryGlow;

            var secondaryOpacityAnim = new System.Windows.Media.Animation.DoubleAnimation(0.4, 1.0, TimeSpan.FromMilliseconds(2000));
            secondaryGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, secondaryOpacityAnim);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(AgentCardBorder_MouseEnter), ex);
        }
    }

    private void AgentCardBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: AgentStatusCard agentCard })
                return;

            // If this is the lead/coordinator agent, remove glow from main transcript border
            if (agentCard.IsLeadAgent)
            {
                if (MainTranscriptBorder is not null && MainTranscriptBorder.Effect is System.Windows.Media.Effects.DropShadowEffect mainGlow)
                {
                    mainGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
                    MainTranscriptBorder.Effect = null;
                }
                return;
            }

            // Find open secondary transcript panel for this agent
            var entry = _secondaryTranscripts.FirstOrDefault(t => ReferenceEquals(t.Agent, agentCard));
            if (entry is null)
                return;

            // Remove glow effect from secondary panel
            if (entry.PanelBorder.Effect is System.Windows.Media.Effects.DropShadowEffect glow)
            {
                glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
                entry.PanelBorder.Effect = null;
            }
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(AgentCardBorder_MouseLeave), ex);
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T target)
                return target;

            source = source switch
            {
                Visual or System.Windows.Media.Media3D.Visual3D => System.Windows.Media.VisualTreeHelper.GetParent(source),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(source)
            };
        }

        return null;
    }

    private void AgentThreadChipButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: TranscriptThreadState thread })
                return;

            var card = FindAgentCardForThread(thread);
            if (card is null) return;

            bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            SyncSelectionControllerWithUiState("AgentThreadChipButton_Click");
            _selectionController.HandleChipClick(card, thread, shiftHeld);
            e.Handled = true;
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(AgentThreadChipButton_Click), ex);
        }
    }

    private void OverflowChipBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: AgentStatusCard card })
                return;

            // Overflow threads are those assigned a sequence number beyond the visible limit (3)
            // but still tracked in card.Threads. ChipVisibility is Collapsed for these.
            var overflowThreads = card.Threads
                .Where(t => t.SequenceNumber > 3)
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            if (overflowThreads.Count == 0)
                return;

            var menu = new ContextMenu();
            foreach (var thread in overflowThreads)
            {
                var label = $"#{thread.SequenceNumber}";
                var time = FormatRelativeTime(thread.StartedAt);
                var item = new MenuItem
                {
                    Header = $"{label}  —  {time}",
                    ToolTip = BuildThreadChipToolTip(thread),
                    Tag = thread
                };
                item.Click += OverflowMenuThreadItem_Click;
                menu.Items.Add(item);
            }

            var fe = (FrameworkElement)sender;
            fe.ContextMenu = menu;
            menu.IsOpen = true;
            e.Handled = true;
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(OverflowChipBorder_MouseLeftButtonUp), ex);
        }
    }

    private void OverflowMenuThreadItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem { Tag: TranscriptThreadState thread })
                return;

            var card = FindAgentCardForThread(thread);
            if (card is not null)
            {
                SyncSelectionControllerWithUiState("OverflowMenuThreadItem_Click");
                _selectionController.HandleChipClick(card, thread, shiftHeld: false);
            }
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(OverflowMenuThreadItem_Click), ex);
        }
    }

    private void AgentNameButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: AgentStatusCard agent })
                return;
            if (!agent.NameIsClickable)
                return;

            var targetPath = agent.IsLeadAgent && _currentWorkspace is not null
                ? Path.Combine(_currentWorkspace.SquadFolderPath, "team.md")
                : agent.CharterPath;

            OpenMarkdownFile(targetPath, agent.IsLeadAgent ? "Squad Team" : $"{agent.Name} Charter");
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(AgentNameButton_Click), ex);
        }
    }

    private void AgentDocumentsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: AgentStatusCard agent })
                return;

            var documents = new List<MarkdownDocumentSpec>(2);
            if (!string.IsNullOrWhiteSpace(agent.CharterPath) && File.Exists(agent.CharterPath))
                documents.Add(new MarkdownDocumentSpec("charter", agent.CharterPath));
            if (!string.IsNullOrWhiteSpace(agent.HistoryPath) && File.Exists(agent.HistoryPath))
                documents.Add(new MarkdownDocumentSpec("history", agent.HistoryPath));

            if (documents.Count == 0)
                return;

            OpenMarkdownFiles(documents, $"{agent.Name} Charter & History");
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(AgentDocumentsButton_Click), ex);
        }
    }

    private void AgentAccentBorder_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is not Border { DataContext: AgentStatusCard agentCard } border)
                return;

            var menu = CreateAgentAccentContextMenu(agentCard);
            menu.PlacementTarget = border;
            border.ContextMenu = menu;
            menu.IsOpen = true;
            e.Handled = true;
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(AgentAccentBorder_PreviewMouseRightButtonUp), ex);
        }
    }

    private ContextMenu CreateAgentAccentContextMenu(AgentStatusCard agentCard)
    {
        var menu = new ContextMenu();
        var primaryThread = GetPrimaryThread(agentCard);
        var now = DateTimeOffset.Now;

        var agentInfoItem = new MenuItem
        {
            Header = "Agent Info",
            Tag = agentCard
        };
        agentInfoItem.Click += AgentInfoMenuItem_Click;
        menu.Items.Add(agentInfoItem);

        var openCharterItem = new MenuItem
        {
            Header = "Open Charter",
            Tag = agentCard
        };
        openCharterItem.Click += AgentOpenCharterMenuItem_Click;
        var hasCharter = (!string.IsNullOrWhiteSpace(agentCard.CharterPath) && File.Exists(agentCard.CharterPath))
                      || (!string.IsNullOrWhiteSpace(agentCard.HistoryPath) && File.Exists(agentCard.HistoryPath));
        if (hasCharter)
            menu.Items.Add(openCharterItem);

        if (primaryThread is not null && _backgroundTaskPresenter.IsThreadStalledForDisplay(primaryThread, now))
        {
            var abortTarget = _backgroundTaskPresenter.TryResolveAbortTarget(primaryThread, allowSingleFallback: false);
            if (abortTarget is not null)
            {
                var abortItem = new MenuItem
                {
                    Header = "Abort Current Run",
                    Tag = abortTarget
                };
                abortItem.Click += AgentAbortCurrentRunMenuItem_Click;
                menu.Items.Add(abortItem);
            }

            var copyDiagnosticsItem = new MenuItem
            {
                Header = "Copy Stall Diagnostics",
                Tag = BuildAgentStallDiagnostics(agentCard, primaryThread, now)
            };
            copyDiagnosticsItem.Click += AgentCopyStallDiagnosticsMenuItem_Click;
            menu.Items.Add(copyDiagnosticsItem);

            menu.Items.Add(new Separator());
        }
        else
        {
            menu.Items.Add(new Separator());
        }

        // Accent Color submenu
        var accentSubmenu = new MenuItem { Header = "Accent Color" };
        for (var index = 0; index < AgentAccentPalette.Length; index++)
        {
            if (index == 8)
                accentSubmenu.Items.Add(new Separator());

            var paletteOption = AgentAccentPalette[index];
            var swatchBrush = ColorUtilities.AccentBrush(paletteOption.Hex);
            var swatch = new Border
            {
                Width = 56,
                Height = 18,
                Background = swatchBrush,
                BorderBrush = string.Equals(
                        agentCard.AccentColorHex,
                        paletteOption.Hex,
                        StringComparison.OrdinalIgnoreCase)
                    ? Brushes.White
                    : Brushes.Transparent,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(5)
            };

            var menuItem = new MenuItem
            {
                Header = swatch,
                Tag = new AgentAccentSelection(agentCard, paletteOption.Hex),
                StaysOpenOnClick = false,
                ToolTip = paletteOption.Hex
            };
            menuItem.Click += AgentAccentColorMenuItem_Click;
            accentSubmenu.Items.Add(menuItem);
        }
        menu.Items.Add(accentSubmenu);

        menu.Items.Add(new Separator());

        // Choose Image...
        var chooseImageItem = new MenuItem
        {
            Header = "Choose Image...",
            Tag = agentCard
        };
        chooseImageItem.Click += AgentChooseImageMenuItem_Click;
        menu.Items.Add(chooseImageItem);

        // Remove Custom Image (only shown if user has set a custom image)
        if (_currentWorkspace is not null &&
            _settingsSnapshot.AgentImagePathsByWorkspace.TryGetValue(_currentWorkspace.FolderPath, out var imgs) &&
            imgs.ContainsKey(agentCard.AccentStorageKey))
        {
            var removeImageItem = new MenuItem
            {
                Header = "Remove Custom Image",
                Tag = agentCard
            };
            removeImageItem.Click += AgentRemoveImageMenuItem_Click;
            menu.Items.Add(removeImageItem);
        }

        return menu;
    }

    private async void AgentAbortCurrentRunMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: BackgroundAbortTarget abortTarget })
            return;

        try
        {
            SquadDashTrace.Write(
                "UI",
                $"Agent context menu abort requested taskKind={abortTarget.TaskKind} taskId={abortTarget.TaskId} label={abortTarget.DisplayLabel}");
            await _bridge.CancelBackgroundTaskAsync(
                abortTarget.TaskId,
                _conversationManager.CurrentSessionId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException("AgentAbortCurrentRun", ex);
        }
    }

    private void AgentCopyStallDiagnosticsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem { Tag: string diagnostics } || string.IsNullOrWhiteSpace(diagnostics))
                return;

            Clipboard.SetText(diagnostics);
            SquadDashTrace.Write("UI", "Copied stalled-agent diagnostics to clipboard.");
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(AgentCopyStallDiagnosticsMenuItem_Click), ex);
        }
    }

    private string BuildAgentStallDiagnostics(AgentStatusCard agentCard, TranscriptThreadState thread, DateTimeOffset now)
    {
        var lastActivityAt = AgentThreadRegistry.GetThreadLastActivityAt(thread);
        var quietFor = now - lastActivityAt;
        return string.Join(Environment.NewLine, [
            $"Agent: {agentCard.Name}",
            $"ThreadId: {thread.ThreadId}",
            $"ToolCallId: {thread.ToolCallId ?? "(none)"}",
            $"Status: {thread.StatusText}",
            $"Started: {thread.StartedAt:O}",
            $"LastActivity: {lastActivityAt:O}",
            $"QuietFor: {StatusTimingPresentation.FormatDuration(quietFor)}",
            $"CurrentRun: {thread.IsCurrentBackgroundRun}",
            $"SessionId: {_conversationManager.CurrentSessionId ?? "(none)"}",
            $"PromptRunning: {_isPromptRunning}",
            $"PromptState: {_currentSessionState ?? "(unknown)"}"
        ]);
    }

    private void AgentInfoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem { Tag: AgentStatusCard agentCard })
                return;
            AgentInfoWindow.Show(this, agentCard, _currentWorkspace?.FolderPath, _workspacePaths.AgentImageAssetsDirectory);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(AgentInfoMenuItem_Click), ex);
        }
    }

    private void AgentOpenCharterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem { Tag: AgentStatusCard agent })
                return;

            var documents = new List<MarkdownDocumentSpec>(2);
            if (!string.IsNullOrWhiteSpace(agent.CharterPath) && File.Exists(agent.CharterPath))
                documents.Add(new MarkdownDocumentSpec("charter", agent.CharterPath));
            if (!string.IsNullOrWhiteSpace(agent.HistoryPath) && File.Exists(agent.HistoryPath))
                documents.Add(new MarkdownDocumentSpec("history", agent.HistoryPath));

            if (documents.Count == 0)
                return;

            OpenMarkdownFiles(documents, $"{agent.Name} Charter & History");
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(AgentOpenCharterMenuItem_Click), ex);
        }
    }

    private void AgentAccentColorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem { Tag: AgentAccentSelection selection })
                return;

            ApplyAgentAccent(selection.AgentCard, selection.AccentHex, persist: true);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(AgentAccentColorMenuItem_Click), ex);
        }
    }

    private void AgentChooseImageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem { Tag: AgentStatusCard agentCard })
                return;

            var agentsDir = _workspacePaths.AgentImageAssetsDirectory;
            var initialDir = _lastAgentImageFolder
                               ?? (Directory.Exists(agentsDir) ? agentsDir : null);
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Choose image for {agentCard.Name}",
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.gif|All files (*.*)|*.*",
                Multiselect = false,
                InitialDirectory = initialDir
            };

            if (dialog.ShowDialog(this) != true)
                return;

            _lastAgentImageFolder = Path.GetDirectoryName(dialog.FileName);
            ApplyAgentImage(agentCard, dialog.FileName, persist: true);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(AgentChooseImageMenuItem_Click), ex);
        }
    }

    private void AgentRemoveImageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem { Tag: AgentStatusCard agentCard })
                return;

            var fallbackPath = AgentImagePathResolver.ResolveBundledPath(agentCard, _workspacePaths.AgentImageAssetsDirectory);
            ApplyAgentImage(agentCard, fallbackPath, persist: true);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(AgentRemoveImageMenuItem_Click), ex);
        }
    }

    private void OpenSquadFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentInstallationState is null || !Directory.Exists(_currentInstallationState.SquadFolderPath))
                return;

            _squadCliAdapter.OpenFolderInExplorer(_currentInstallationState.SquadFolderPath, "Open .squad Folder");
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(OpenSquadFolderMenuItem_Click), ex);
        }
    }

    private void SquadCliMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string workingDir = _currentWorkspace?.FolderPath ?? _workspacePaths.ApplicationRoot;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = @"-NoExit -Command ""npx squad""",
                WorkingDirectory = workingDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(SquadCliMenuItem_Click), ex);
        }
    }

    private async void RemoteAccessMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentWorkspace is null)
                return;

            if (_remoteAccessActive)
            {
                await _bridge.StopRemoteAsync().ConfigureAwait(false);
            }
            else
            {
                var repo = System.IO.Path.GetFileName(_currentWorkspace.FolderPath);
                var machine = System.Environment.MachineName;
                await _bridge.StartRemoteAsync(
                    repo: repo,
                    branch: "main",
                    machine: machine,
                    squadDir: _currentWorkspace.SquadFolderPath,
                    cwd: _currentWorkspace.FolderPath,
                    sessionId: _conversationManager.CurrentSessionId).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(RemoteAccessMenuItem_Click), ex);
        }
    }

    private async void InstallSquadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentWorkspace is null)
                return;

            if (IsDeveloperSimulationActive())
            {
                SetInstallStatus("Developer simulation is active. Install Squad is disabled while previewing issue states.");
                ShowTextWindow(
                    "Developer Simulation",
                    "Install Squad is disabled while a developer issue simulation is active.\n\nClear the simulation in Preferences to run the real install flow.");
                return;
            }

            SetInstallUiState(isInstalling: true, "Checking prerequisites...");
            var progress = new Progress<string>(text => SetInstallStatus(text));

            var result = await _installerService
                .InstallAsync(_currentWorkspace.FolderPath, progress);

            RefreshInstallationState();

            if (result.Success && _currentInstallationState?.IsSquadInstalledForActiveDirectory == true)
            {
                SetInstallUiState(isInstalling: false, "Squad installed successfully. Starting the first Squad turn...");
                RefreshAgentCards();
                RefreshSidebar();
                MaybePromptForUniverseSelection();
                MaybePublishMissingUtilityAgentNotice();
                return;
            }

            var failureMessage = result.Success
                ? "Squad setup completed, but the local Squad command is still unavailable."
                : result.Message;
            var activeDirectory = _currentWorkspace.FolderPath;

            SetInstallUiState(isInstalling: false, failureMessage);

            ShowTextWindow(
                "Squad Install Diagnostics",
                BuildInstallDiagnostics(result, activeDirectory));
        }
        catch (Exception ex)
        {
            SetInstallUiState(isInstalling: false, "Squad install failed.");
            HandleUiCallbackException("Install Squad", ex);
        }
    }

    private async void RunDoctorButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentWorkspace is null)
                return;

            if (IsDeveloperSimulationActive())
            {
                SetInstallStatus("Developer simulation is active. Cleanup > Run Squad Doctor is disabled while previewing issue states.");
                ShowTextWindow(
                    "Developer Simulation",
                    "Cleanup > Run Squad Doctor is disabled while a developer issue simulation is active.\n\nClear the simulation in Preferences to run the real doctor flow.");
                return;
            }

            SetInstallUiState(isInstalling: true, "Running Squad doctor...");
            var progress = new Progress<string>(text => SetInstallStatus(text));

            var result = await _installerService
                .RunDoctorAsync(_currentWorkspace.FolderPath, progress);

            SetInstallUiState(isInstalling: false, result.Message);

            ShowTextWindow(
                "Squad Doctor",
                BuildInstallDiagnostics(result, _currentWorkspace.FolderPath));
        }
        catch (Exception ex)
        {
            SetInstallUiState(isInstalling: false, "Squad doctor failed.");
            HandleUiCallbackException("Run Doctor", ex);
        }
    }

    private void RunDoctorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RunDoctorButton_Click(sender, e);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(RunDoctorMenuItem_Click), ex);
        }
    }

    private void ToolIconGalleryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ToolIconPreviewWindow.Show(this);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(ToolIconGalleryMenuItem_Click), ex);
        }
    }

    private void OpenFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Multiselect = false
            };

            if (_currentWorkspace is not null)
            {
                dialog.InitialDirectory = _currentWorkspace.FolderPath;
            }

            if (dialog.ShowDialog(this) == true)
            {
                OpenWorkspace(dialog.FolderName, rememberFolder: true);
            }
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(OpenFolderMenuItem_Click), ex);
        }
    }

    private void PreferencesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_preferencesWindow is { IsVisible: true })
            {
                _preferencesWindow.Activate();
                return;
            }

            var showDevOptions = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            _preferencesWindow = PreferencesWindow.Open(
                CanShowOwnedWindow() ? this : null,
                _settingsStore,
                _settingsSnapshot,
                _pushNotificationService,
                showDevOptions,
                snapshot =>
                {
                    _settingsSnapshot = snapshot;
                    _pushNotificationService.ReloadProvider();
                    UpdateVoiceHintVisibility();
                    RefreshInstallationState();
                    RefreshDeveloperRuntimeIssuePreview();
                });
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(PreferencesMenuItem_Click), ex);
        }
    }

    private void ViewMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        var shiftDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (ToolIconGalleryMenuItem is not null)
            ToolIconGalleryMenuItem.Visibility = shiftDown ? Visibility.Visible : Visibility.Collapsed;
        if (ToolIconGallerySeparator is not null)
            ToolIconGallerySeparator.Visibility = shiftDown ? Visibility.Visible : Visibility.Collapsed;
        if (ViewLoopPanelMenuItem is not null)
            ViewLoopPanelMenuItem.IsChecked = _loopPanelVisible;
        if (ViewTasksMenuItem is not null)
            ViewTasksMenuItem.IsChecked = _tasksPanelVisible;
    }

    private void RecentFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem { Tag: string folderPath })
                return;

            OpenWorkspace(folderPath, rememberFolder: true);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(RecentFolderMenuItem_Click), ex);
        }
    }

    private void NormalViewMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetTranscriptFullScreen(false);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(NormalViewMenuItem_Click), ex);
        }
    }

    private void FullScreenTranscriptMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetTranscriptFullScreen(true);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(FullScreenTranscriptMenuItem_Click), ex);
        }
    }

    private void RemoveTemporaryAgentsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RemoveTemporaryAgents();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(RemoveTemporaryAgentsMenuItem_Click), ex);
        }
    }

    private void SetTranscriptFullScreen(bool enabled)
    {
        if (_transcriptFullScreenEnabled == enabled)
            return;

        _transcriptFullScreenEnabled = enabled;
        _fullScreenPromptVisible = false; // reset peek state on any fullscreen transition
        ApplyViewMode();

        // Persist fullscreen state per workspace.
        var state = _docsPanelState ?? _settingsStore.GetDocsPanelState(_currentWorkspace?.FolderPath);
        _docsPanelState = state with { FullScreenTranscript = enabled };
        _settingsSnapshot = _settingsStore.SaveDocsPanelState(_currentWorkspace?.FolderPath, _docsPanelState);
    }

    private void ShowFullScreenPrompt()
    {
        _fullScreenPromptVisible = true;
        if (PromptBorder is not null)
            PromptBorder.Visibility = Visibility.Visible;
        // Focus after layout so the caret appears inside the text box.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            PromptTextBox.Focus();
        });
    }

    private void HideFullScreenPrompt()
    {
        _fullScreenPromptVisible = false;
        if (PromptBorder is not null)
            PromptBorder.Visibility = Visibility.Collapsed;
        PromptTextBox.Clear();
    }

    private void ViewDocumentationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetDocumentationMode(!_documentationModeEnabled);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(ViewDocumentationMenuItem_Click), ex);
        }
    }

    private void ViewTasksMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _tasksPanelVisible = !_tasksPanelVisible;
            SyncTasksPanel();
            if (ViewTasksMenuItem is not null)
                ViewTasksMenuItem.IsChecked = _tasksPanelVisible;
            PersistTasksPanelVisible();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(ViewTasksMenuItem_Click), ex);
        }
    }

    private void LoopPanelCloseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _loopPanelVisible = false;
            SyncLoopPanel();
            if (ViewLoopPanelMenuItem is not null)
                ViewLoopPanelMenuItem.IsChecked = false;
        }
        catch (Exception ex) { HandleUiCallbackException(nameof(LoopPanelCloseButton_Click), ex); }
    }

    private void TasksPanelCloseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _tasksPanelVisible = false;
            SyncTasksPanel();
            if (ViewTasksMenuItem is not null)
                ViewTasksMenuItem.IsChecked = false;
            PersistTasksPanelVisible();
        }
        catch (Exception ex) { HandleUiCallbackException(nameof(TasksPanelCloseButton_Click), ex); }
    }

    private void EditTasksMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var workspace = _currentWorkspace;
            if (workspace is null) return;

            var tasksPath = Path.Combine(workspace.SquadFolderPath, "tasks.md");
            if (!File.Exists(tasksPath)) return;

            MarkdownDocumentWindow.Show(
                CanShowOwnedWindow() ? this : null,
                "Tasks",
                tasksPath,
                showSource: true);
        }
        catch (Exception ex) { HandleUiCallbackException(nameof(EditTasksMenuItem_Click), ex); }
    }

    private void ViewLoopPanelMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _loopPanelVisible = !_loopPanelVisible;
            SyncLoopPanel();
            if (ViewLoopPanelMenuItem is not null)
                ViewLoopPanelMenuItem.IsChecked = _loopPanelVisible;
        }
        catch (Exception ex) { HandleUiCallbackException(nameof(ViewLoopPanelMenuItem_Click), ex); }
    }

    private void SetDocumentationMode(bool enabled, bool persistChange = true)
    {
        if (_documentationModeEnabled == enabled)
            return;

        _documentationModeEnabled = enabled;
        _pec.DocumentationModeActive = enabled;
        _pec.DocsRootFolder = enabled
            ? DocTopicsLoader.FindDocsFolderPath(_currentWorkspace?.FolderPath)
            : null;
        ApplyViewMode();

        if (enabled)
        {
            PopulateDocumentationTopics();

            if (persistChange)
            {
                var workspaceFolder = _currentWorkspace?.FolderPath;
                var existingState = _docsPanelState ?? _settingsStore.GetDocsPanelState(workspaceFolder);
                _docsPanelState = existingState is not null
                    ? existingState with { Open = true }
                    : new WorkspaceDocsPanelState { Open = true };
                _settingsSnapshot = _settingsStore.SaveDocsPanelState(workspaceFolder, _docsPanelState);
            }
        }
        else if (persistChange)
        {
            var nodes = DocTopicsTreeView is not null
                ? CollectExpandedDocNodes(DocTopicsTreeView.Items)
                : null;
            var topic = (DocTopicsTreeView?.SelectedItem as TreeViewItem)?.Tag as string;

            // Capture splitter widths (only if panel is currently visible/expanded)
            double? docsPanelWidth = null;
            double? docsPanelWidthFraction = null;
            if (DocsPanelColumn is not null && DocsPanelColumn.ActualWidth > 0)
            {
                docsPanelWidth = DocsPanelColumn.ActualWidth;
                if (MainGrid is not null && MainGrid.ActualWidth > 0)
                    docsPanelWidthFraction = DocsPanelColumn.ActualWidth / MainGrid.ActualWidth;
            }
            bool? docsSourceOpen = DocsSourceColumn is not null && DocsSourceColumn.ActualWidth > 0;
            double? docsSourceWidth = (DocsSourceColumn?.ActualWidth > 0) ? DocsSourceColumn.ActualWidth : null;

            var workspaceFolder = _currentWorkspace?.FolderPath;
            _docsPanelState = new WorkspaceDocsPanelState
            {
                Open = false,
                ExpandedNodes = nodes,
                SelectedTopic = topic,
                PanelWidth = docsPanelWidth,
                PanelWidthFraction = docsPanelWidthFraction,
                SourceOpen = docsSourceOpen,
                SourceWidth = docsSourceWidth,
            };
            _settingsSnapshot = _settingsStore.SaveDocsPanelState(workspaceFolder, _docsPanelState);
        }
    }

    private void PopulateDocumentationTopics()
    {
        if (DocTopicsTreeView is null)
            return;

        DocTopicsLoader.LoadTopics(DocTopicsTreeView, out var firstItemToSelect, _currentWorkspace?.FolderPath);

        // Wire up selection handler
        DocTopicsTreeView.SelectedItemChanged -= DocTopicsTreeView_SelectedItemChanged;
        DocTopicsTreeView.SelectedItemChanged += DocTopicsTreeView_SelectedItemChanged;

        // Wire up WebBrowser navigation handler for clickable links
        if (DocMarkdownViewer is not null)
        {
            DocMarkdownViewer.Navigating -= DocMarkdownViewer_Navigating;
            DocMarkdownViewer.Navigating += DocMarkdownViewer_Navigating;
            DocMarkdownViewer.LoadCompleted -= DocMarkdownViewer_LoadCompleted_InjectHover;
            DocMarkdownViewer.LoadCompleted += DocMarkdownViewer_LoadCompleted_InjectHover;
            DocMarkdownViewer.ObjectForScripting = new DocViewerScriptingBridge(this);
        }

        // ── Expansion ─────────────────────────────────────────────────────────────
        var savedExpandedNodes = (_docsPanelState ?? _settingsStore.GetDocsPanelState(_currentWorkspace?.FolderPath)).ExpandedNodes;
        if (savedExpandedNodes is not null)
            ApplyDocNodeExpansion(DocTopicsTreeView.Items,
                new HashSet<string>(savedExpandedNodes, StringComparer.OrdinalIgnoreCase));
        else
            ExpandAllDocNodes(DocTopicsTreeView.Items);  // default: all expanded

        // ── Selection ─────────────────────────────────────────────────────────────
        var savedTopic = (_docsPanelState ?? _settingsStore.GetDocsPanelState(_currentWorkspace?.FolderPath)).SelectedTopic;
        if (!string.IsNullOrEmpty(savedTopic))
        {
            var savedItem = FindDocNodeByTag(DocTopicsTreeView.Items, savedTopic);
            if (savedItem is not null)
            {
                savedItem.IsSelected = true;
                ConfigureDocsWatcher();
                return;
            }
        }

        // Fallback: select first item (default behaviour)
        if (firstItemToSelect is not null)
            firstItemToSelect.IsSelected = true;
        else if (DocTopicsTreeView.Items.Count > 0)
            RenderDocumentationWelcome();
        else
            DocMarkdownViewer?.Navigate("about:blank");

        // Configure FileSystemWatcher to auto-refresh on .md file changes
        ConfigureDocsWatcher();
    }

    // ── Doc-tree helpers ─────────────────────────────────────────────────────────

    /// <summary>Recursively expands every node in the docs tree.</summary>
    private static void ExpandAllDocNodes(ItemCollection items)
    {
        foreach (var item in items.OfType<TreeViewItem>())
        {
            item.IsExpanded = true;
            ExpandAllDocNodes(item.Items);
        }
    }

    /// <summary>
    /// Recursively sets <see cref="TreeViewItem.IsExpanded"/> based on whether the
    /// node's Tag (file path) or Header string is in <paramref name="keys"/>.
    /// </summary>
    private static void ApplyDocNodeExpansion(ItemCollection items, IReadOnlySet<string> keys)
    {
        foreach (var item in items.OfType<TreeViewItem>())
        {
            var tagKey = item.Tag as string;
            var headerKey = item.Header?.ToString();
            item.IsExpanded = (!string.IsNullOrEmpty(tagKey) && keys.Contains(tagKey))
                           || (!string.IsNullOrEmpty(headerKey) && keys.Contains(headerKey));
            ApplyDocNodeExpansion(item.Items, keys);
        }
    }

    /// <summary>
    /// Recursively finds the first <see cref="TreeViewItem"/> whose Tag matches
    /// <paramref name="tag"/> (case-insensitive file-path comparison).
    /// </summary>
    private static TreeViewItem? FindDocNodeByTag(ItemCollection items, string tag)
    {
        foreach (var item in items.OfType<TreeViewItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                return item;
            var found = FindDocNodeByTag(item.Items, tag);
            if (found is not null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Recursively collects the key (Tag path, or Header string) of every expanded
    /// node in the docs tree.
    /// </summary>
    private static List<string> CollectExpandedDocNodes(ItemCollection items)
    {
        var result = new List<string>();
        foreach (var item in items.OfType<TreeViewItem>())
        {
            if (item.IsExpanded)
            {
                var key = item.Tag as string ?? item.Header?.ToString();
                if (!string.IsNullOrEmpty(key))
                    result.Add(key);
            }
            result.AddRange(CollectExpandedDocNodes(item.Items));
        }
        return result;
    }

    private void DocTopicsTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DocMarkdownViewer is null || e.NewValue is not TreeViewItem item)
            return;

        var filePath = item.Tag as string;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        // If the same topic is re-selected (e.g. by a watcher-triggered tree reload)
        // and the source panel has unsaved edits, don't overwrite the user's work.
        bool isSameTopic = string.Equals(filePath, _currentDocPath, StringComparison.OrdinalIgnoreCase);
        bool sourceVisible = DocSourcePanel?.Visibility == Visibility.Visible;

        // Flush any pending source edit to disk before switching topics (not when re-selecting same)
        if (sourceVisible && !isSameTopic)
        {
            _docSourceSaveTimer?.Stop();
            SaveDocSourceToDisk();
        }

        try
        {
            _currentDocPath = filePath;  // store for link navigation
            // Keep BOTH in-memory stores current so DocsWatcher-triggered reloads (which read
            // _docsPanelState) restore the NEW topic rather than reverting to the old one.
            _settingsSnapshot = _settingsSnapshot with { DocsSelectedTopic = filePath };
            _docsPanelState = (_docsPanelState ?? new WorkspaceDocsPanelState()) with { SelectedTopic = filePath };
            _pec.ActiveDocumentPath = filePath;

            if (isSameTopic && sourceVisible)
                return;  // preview and source are already showing this topic with live edits — leave them alone

            var markdown = File.ReadAllText(filePath);
            var title = item.Header?.ToString() ?? "Documentation";
            var html = MarkdownHtmlBuilder.Build(markdown, title,
                filePath: filePath, isDark: AgentStatusCard.IsDarkTheme);
            DocMarkdownViewer.NavigateToString(html);

            // Refresh source editor if it's open
            if (sourceVisible)
                PopulateDocSourceEditor();
        }
        catch (Exception ex)
        {
            _currentDocPath = null;
            // Show error in viewer
            var errorMarkdown = $"# Error Loading Document\n\nFailed to load `{Path.GetFileName(filePath)}`:\n\n```\n{ex.Message}\n```";
            var html = MarkdownHtmlBuilder.Build(errorMarkdown, "Error",
                filePath: null, isDark: AgentStatusCard.IsDarkTheme);
            DocMarkdownViewer.NavigateToString(html);
        }
    }

    private void DocMarkdownViewer_Navigating(object sender, NavigatingCancelEventArgs e)
    {
        var uri = e.Uri;
        // NavigateToString fires Navigating with null URI — let it through
        if (uri == null) return;

        var uriString = uri.ToString();

        // about:blank is the initial load — let it through
        if (uriString == "about:blank" || uriString.StartsWith("about:")) return;

        // We handle all real URI navigation ourselves
        e.Cancel = true;

        // External URLs: open in system browser
        if (uriString.StartsWith("http://") || uriString.StartsWith("https://"))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = uriString, UseShellExecute = true });
            }
            catch { /* ignore */ }
            return;
        }

        // Internal doc links: resolve relative path and navigate to that doc
        try
        {
            string? resolvedPath = null;

            // Try as a file URI
            if (uri.IsFile)
            {
                resolvedPath = uri.LocalPath;
            }
            else
            {
                // Try resolving relative to current doc directory
                var currentDocPath = _currentDocPath;
                if (!string.IsNullOrEmpty(currentDocPath))
                {
                    var currentDir = System.IO.Path.GetDirectoryName(currentDocPath);
                    if (!string.IsNullOrEmpty(currentDir))
                    {
                        var relativePart = Uri.UnescapeDataString(uriString);
                        resolvedPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(currentDir, relativePart));
                    }
                }
            }

            if (resolvedPath != null && resolvedPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                && System.IO.File.Exists(resolvedPath))
            {
                // Find the TreeViewItem with this path as its Tag and select it
                NavigateToDocByPath(resolvedPath);
                return;
            }
        }
        catch { /* ignore bad paths */ }

        // Anything else (anchors, javascript, etc.) — already cancelled, just return
    }

    private void NavigateToDocByPath(string path)
    {
        if (DocTopicsTreeView is null) return;

        var item = FindDocNodeByTag(DocTopicsTreeView.Items, path);
        if (item is not null)
        {
            item.IsSelected = true;
            item.BringIntoView();
        }
    }

    private void RenderDocumentationWelcome()
    {
        if (DocMarkdownViewer is null)
            return;

        const string welcomeMarkdown = """
            # Documentation

            Select a topic from the tree on the left to start reading.

            ## Adding docs to your repo

            The documentation panel reads from a `docs/` folder in your workspace root.
            If no `docs/` folder exists, the topic tree will be empty.

            To get started:

            1. Create a `docs/` folder in your repository root
            2. Add Markdown (`.md`) files — one file per topic
            3. Optionally add a `SUMMARY.md` in GitBook format to control the order and hierarchy of topics
            4. Click **Add Document** above to scaffold a new topic

            ## Folder structure

            ```
            your-repo/
            └── docs/
                ├── SUMMARY.md          ← optional: controls tree order
                ├── README.md           ← home page
                ├── getting-started/
                │   └── installation.md
                └── reference/
                    └── configuration.md
            ```

            ## SUMMARY.md format

            ```markdown
            * [Home](README.md)

            ## Getting Started

            * [Getting Started](getting-started/README.md)
              * [Installation](getting-started/installation.md)
            ```

            Without a `SUMMARY.md`, folders and files are listed alphabetically.
            """;

        try
        {
            var html = MarkdownHtmlBuilder.Build(welcomeMarkdown, "Documentation",
                filePath: null, isDark: AgentStatusCard.IsDarkTheme);
            DocMarkdownViewer.NavigateToString(html);
        }
        catch
        {
            // WebBrowser may not be ready; ignore
        }
    }

    private void AddDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        // Placeholder — Add Document functionality not yet implemented
    }

    private void ViewPagesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workspaceGitHubUrl is null) return;
        try
        {
            var uri = new Uri(_workspaceGitHubUrl);
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length < 2) return;
            var pagesUrl = $"https://{segments[0]}.github.io/{segments[1]}/";
            Process.Start(new ProcessStartInfo(pagesUrl) { UseShellExecute = true });
        }
        catch { }
    }

    // ── Source editor (View Source panel) ────────────────────────────────────

    private bool _suppressDocSourceTextChanged;
    private DispatcherTimer? _docSourceSaveTimer;

    private void ViewSourceButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var showing = DocsSourceColumn?.Width.Value == 0;
            if (showing)
                ShowDocSourcePanel();
            else
                HideDocSourcePanel();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(ViewSourceButton_Click), ex);
        }
    }

    private void ShowDocSourcePanel()
    {
        if (DocsSourceSplitterColumn is null || DocsSourceColumn is null) return;

        // Split the viewer's current available width evenly between preview and source editor.
        const double splitterWidth = 6;
        double availableWidth = (DocsPanelColumn?.ActualWidth ?? 600)
                                - (DocsTopicsColumn?.ActualWidth ?? 220)
                                - splitterWidth;
        double sourceWidth = Math.Max(100, availableWidth / 2);

        DocsSourceSplitterColumn.Width = new GridLength(splitterWidth);
        DocsSourceColumn.Width = new GridLength(sourceWidth, GridUnitType.Pixel);
        if (DocSourceSplitter is not null) DocSourceSplitter.Visibility = Visibility.Visible;
        if (DocSourcePanel is not null) DocSourcePanel.Visibility = Visibility.Visible;
        if (ViewSourceButton is not null) ViewSourceButton.Content = "Hide Source";

        ApplyDocSourceFontSize();
        PopulateDocSourceEditor();

        // Inject hover JS now that the source panel is visible — the initial LoadCompleted
        // may have fired before the panel was shown (e.g., on startup restore).
        if (DocMarkdownViewer is not null)
        {
            try { DocMarkdownViewer.InvokeScript("eval", new object[] { HoverInjectionScript }); }
            catch { }
        }
    }

    private void HideDocSourcePanel()
    {
        if (DocsSourceSplitterColumn is null || DocsSourceColumn is null) return;

        _docSourceSaveTimer?.Stop();
        DocsSourceSplitterColumn.Width = new GridLength(0);
        DocsSourceColumn.Width = new GridLength(0);
        if (DocSourceSplitter is not null) DocSourceSplitter.Visibility = Visibility.Collapsed;
        if (DocSourcePanel is not null) DocSourcePanel.Visibility = Visibility.Collapsed;
        if (ViewSourceButton is not null) ViewSourceButton.Content = "View Source";
    }

    private void PopulateDocSourceEditor()
    {
        if (DocSourceTextBox is null) return;

        _suppressDocSourceTextChanged = true;
        try
        {
            DocSourceTextBox.Text = string.IsNullOrEmpty(_currentDocPath) || !File.Exists(_currentDocPath)
                ? string.Empty
                : File.ReadAllText(_currentDocPath);
        }
        finally
        {
            _suppressDocSourceTextChanged = false;
        }
    }

    private void DocSourceTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressDocSourceTextChanged) return;

        // Live-update the markdown preview
        RefreshDocMarkdownViewerFromSource();

        // Debounce save to disk
        if (_docSourceSaveTimer is null)
        {
            _docSourceSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _docSourceSaveTimer.Tick += DocSourceSaveTimer_Tick;
        }
        _docSourceSaveTimer.Stop();
        _docSourceSaveTimer.Start();
    }

    private void DocSourceSaveTimer_Tick(object? sender, EventArgs e)
    {
        _docSourceSaveTimer?.Stop();
        SaveDocSourceToDisk();
    }

    private static string HoverInjectionScript => MarkdownDocumentScripts.HoverInjectionScript;

    private void DocMarkdownViewer_LoadCompleted_InjectHover(object sender, NavigationEventArgs e)
    {
        if (DocSourcePanel?.Visibility != Visibility.Visible) return;
        try
        {
            DocMarkdownViewer.InvokeScript("eval", new object[] { HoverInjectionScript });
        }
        catch { }
    }

    private void RefreshDocMarkdownViewerFromSource()
    {
        if (DocMarkdownViewer is null || DocSourceTextBox is null) return;

        // Capture current scroll position before re-rendering
        try
        {
            var result = DocMarkdownViewer.InvokeScript("eval",
                new object[] { "document.documentElement.scrollTop || document.body.scrollTop" });
            if (result is not null && double.TryParse(result.ToString(), out var y))
                _docPreviewScrollY = y;
        }
        catch { /* WebBrowser has no document yet */ }

        var markdown = DocSourceTextBox.Text;
        var title = string.IsNullOrEmpty(_currentDocPath) ? "Documentation" : Path.GetFileNameWithoutExtension(_currentDocPath);
        var html = MarkdownHtmlBuilder.Build(markdown, title, filePath: _currentDocPath, isDark: AgentStatusCard.IsDarkTheme);

        // Restore scroll after load completes
        var scrollY = _docPreviewScrollY;
        LoadCompletedEventHandler? restoreScroll = null;
        restoreScroll = (s, e) =>
        {
            DocMarkdownViewer.LoadCompleted -= restoreScroll;
            try
            {
                DocMarkdownViewer.InvokeScript("eval",
                    new object[] { $"document.documentElement.scrollTop={scrollY};document.body.scrollTop={scrollY};" });
            }
            catch { }
        };
        DocMarkdownViewer.LoadCompleted += restoreScroll;

        DocMarkdownViewer.NavigateToString(html);
    }

    // Feature 3: Highlight doc source from hover
    internal void HighlightDocSourceFromHover(string lineHint)
    {
        if (DocSourceTextBox is null || string.IsNullOrEmpty(lineHint)) return;
        if (!int.TryParse(lineHint, out var lineNum) || lineNum < 1) return;

        var lines = DocSourceTextBox.Text.Split('\n');
        if (lineNum > lines.Length) return;

        // Find the start position of the line
        int startPos = 0;
        for (int i = 0; i < lineNum - 1; i++)
        {
            startPos += lines[i].Length + 1; // +1 for newline
        }

        var lineLength = lineNum - 1 < lines.Length ? lines[lineNum - 1].Length : 0;
        HighlightDocSourceRange(startPos, lineLength);
    }

    private Canvas EnsureDocSourceOverlayCanvas()
    {
        if (_docSourceOverlayCanvas is not null) return _docSourceOverlayCanvas;

        if (DocSourcePanel is null) throw new InvalidOperationException("DocSourcePanel is null");

        Grid grid;
        if (DocSourcePanel.Child is Grid existingGrid)
        {
            grid = existingGrid;
        }
        else
        {
            var child = DocSourcePanel.Child;
            grid = new Grid();
            DocSourcePanel.Child = grid;
            if (child is not null)
                grid.Children.Add(child);
        }

        _docSourceOverlayCanvas = new Canvas
        {
            IsHitTestVisible = false,
            Background = Brushes.Transparent
        };
        grid.Children.Add(_docSourceOverlayCanvas);
        return _docSourceOverlayCanvas;
    }

    private void HighlightDocSourceRange(int start, int length)
    {
        if (DocSourceTextBox is null || DocSourcePanel is null) return;

        // Remove existing hover highlight
        if (_docSourceHoverHighlight is not null)
        {
            (_docSourceHoverHighlight.Parent as Canvas)?.Children.Remove(_docSourceHoverHighlight);
            _docSourceHoverHighlight = null;
        }

        _docSourceHoverTimer?.Stop();

        if (length <= 0) return;

        // Get the bounding rect of the character in TextBox space
        var rect = DocSourceTextBox.GetRectFromCharacterIndex(start);
        if (rect == Rect.Empty) return;

        var overlayCanvas = EnsureDocSourceOverlayCanvas();

        // Convert TextBox-local coordinates to overlay Canvas coordinates
        var origin = DocSourceTextBox.TranslatePoint(new Point(0, 0), overlayCanvas);
        var charTopLeft = DocSourceTextBox.TranslatePoint(rect.TopLeft, overlayCanvas);

        var isDark = AgentStatusCard.IsDarkTheme;
        var highlightColor = isDark
            ? Color.FromArgb(60, 255, 220, 80)    // warm amber tint on dark
            : Color.FromArgb(50, 100, 180, 255);   // cool blue tint on light

        double highlightWidth = Math.Max(DocSourceTextBox.ActualWidth - (charTopLeft.X - origin.X), 0);

        _docSourceHoverHighlight = new Shapes.Rectangle
        {
            Width = highlightWidth,
            Height = Math.Max(rect.Height, 14),
            Fill = new SolidColorBrush(highlightColor),
            IsHitTestVisible = false
        };

        Canvas.SetLeft(_docSourceHoverHighlight, charTopLeft.X);
        Canvas.SetTop(_docSourceHoverHighlight, charTopLeft.Y);
        overlayCanvas.Children.Add(_docSourceHoverHighlight);

        // Auto-clear after 1 second
        _docSourceHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _docSourceHoverTimer.Tick += (s, e) =>
        {
            _docSourceHoverTimer.Stop();
            if (_docSourceHoverHighlight is not null)
            {
                (_docSourceHoverHighlight.Parent as Canvas)?.Children.Remove(_docSourceHoverHighlight);
                _docSourceHoverHighlight = null;
            }
        };
        _docSourceHoverTimer.Start();
    }

    private void SaveDocSourceToDisk()
    {
        if (DocSourceTextBox is null || string.IsNullOrEmpty(_currentDocPath)) return;
        try
        {
            File.WriteAllText(_currentDocPath, DocSourceTextBox.Text);
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("DocSource", $"Failed to save doc source: {ex.Message}");
        }
    }

    private void DocSourceTextBox_ApplyBold()
    {
        if (DocSourceTextBox is null) return;
        ApplyMarkdownBold(DocSourceTextBox);
    }

    private void PromptTextBox_ApplyBold()
    {
        ApplyMarkdownBold(PromptTextBox);
    }

    private static void ApplyMarkdownBold(TextBox box) => MarkdownEditorCommands.ApplyBold(box);

    private void DocSourceTextBox_ApplyItalic()
    {
        if (DocSourceTextBox is null) return;
        ApplyMarkdownItalic(DocSourceTextBox);
        DocSourceTextBox.Focus();
    }

    private static void ApplyMarkdownItalic(TextBox box) => MarkdownEditorCommands.ApplyItalic(box);

    private void DocSourceTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        var hasSelection = DocSourceTextBox?.SelectionLength > 0;
        if (DocBoldButton is not null) DocBoldButton.IsEnabled = hasSelection;
        if (DocItalicButton is not null) DocItalicButton.IsEnabled = hasSelection;
    }

    private void DocBoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (DocSourceTextBox is null) return;
        ApplyMarkdownBold(DocSourceTextBox);
        DocSourceTextBox.Focus();
    }

    private void DocItalicButton_Click(object sender, RoutedEventArgs e)
    {
        DocSourceTextBox_ApplyItalic();
    }

    private void DocLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (DocSourceTextBox is null) return;
        DocSourceTextBox_InsertLink();
        DocSourceTextBox.Focus();
    }

    private void DocSourceTextBox_InsertLink()
    {
        if (DocSourceTextBox is null) return;
        var selStart = DocSourceTextBox.SelectionStart;
        var selLen = DocSourceTextBox.SelectionLength;
        if (selLen > 0)
        {
            var text = DocSourceTextBox.SelectedText;
            var md = $"[{text}](url)";
            DocSourceTextBox.SelectedText = md;
            DocSourceTextBox.SelectionStart = selStart;
            DocSourceTextBox.SelectionLength = md.Length;
        }
        else
        {
            var caret = DocSourceTextBox.CaretIndex;
            const string md = "[text](url)";
            DocSourceTextBox.Text = DocSourceTextBox.Text.Insert(caret, md);
            DocSourceTextBox.SelectionStart = caret;
            DocSourceTextBox.SelectionLength = md.Length;
        }
    }

    private void DocImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (DocSourceTextBox is null) return;
        DocSourceTextBox_InsertImagePlaceholder();
        DocSourceTextBox.Focus();
    }

    private void DocSourceTextBox_InsertImagePlaceholder()
    {
        if (DocSourceTextBox is null) return;
        var caret = DocSourceTextBox.CaretIndex;
        const string placeholder =
            "![Screenshot: brief description](images/descriptive-filename.png)\n" +
            "> 📸 *Screenshot needed: Detailed description of what to capture in this screenshot.*";
        DocSourceTextBox.Text = DocSourceTextBox.Text.Insert(caret, placeholder);
        DocSourceTextBox.CaretIndex = caret + placeholder.Length;
    }

    private void DocTableButton_Click(object sender, RoutedEventArgs e)
    {
        if (DocSourceTextBox is null) return;
        DocSourceTextBox_InsertTable();
        DocSourceTextBox.Focus();
    }

    private void DocSourceTextBox_InsertTable()
    {
        if (DocSourceTextBox is null) return;
        var caret = DocSourceTextBox.CaretIndex;
        const string table =
            "| Column 1 | Column 2 | Column 3 |\n" +
            "|----------|----------|----------|\n" +
            "| Cell     | Cell     | Cell     |";
        DocSourceTextBox.Text = DocSourceTextBox.Text.Insert(caret, table);
        DocSourceTextBox.CaretIndex = caret + table.Length;
    }

    private void DocInlineCodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DocSourceTextBox is null) return;
        DocSourceTextBox_InsertInlineCode();
        DocSourceTextBox.Focus();
    }

    private void DocSourceTextBox_InsertInlineCode()
    {
        if (DocSourceTextBox is null) return;
        var selStart = DocSourceTextBox.SelectionStart;
        var selLen = DocSourceTextBox.SelectionLength;
        if (selLen > 0)
        {
            var text = DocSourceTextBox.SelectedText;
            var md = $"`{text}`";
            DocSourceTextBox.SelectedText = md;
            DocSourceTextBox.SelectionStart = selStart;
            DocSourceTextBox.SelectionLength = md.Length;
        }
        else
        {
            var caret = DocSourceTextBox.CaretIndex;
            DocSourceTextBox.Text = DocSourceTextBox.Text.Insert(caret, "``");
            DocSourceTextBox.CaretIndex = caret + 1;
        }
    }

    private void DocCodeBlockButton_Click(object sender, RoutedEventArgs e)
    {
        if (DocSourceTextBox is null) return;
        DocSourceTextBox_InsertCodeBlock();
        DocSourceTextBox.Focus();
    }

    private void DocSourceTextBox_InsertCodeBlock()
    {
        if (DocSourceTextBox is null) return;
        var selStart = DocSourceTextBox.SelectionStart;
        var selLen = DocSourceTextBox.SelectionLength;
        if (selLen > 0)
        {
            var text = DocSourceTextBox.SelectedText;
            var md = $"\n```\n{text}\n```\n";
            DocSourceTextBox.SelectedText = md;
            DocSourceTextBox.SelectionStart = selStart;
            DocSourceTextBox.SelectionLength = md.Length;
        }
        else
        {
            var caret = DocSourceTextBox.CaretIndex;
            const string fence = "\n```\n\n```\n";
            DocSourceTextBox.Text = DocSourceTextBox.Text.Insert(caret, fence);
            DocSourceTextBox.CaretIndex = caret + 5; // position inside the fence
        }
    }

    private void DocSourceTextBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DocSourceTextBox is null) return;
        try
        {
            var menu = new ContextMenu();

            var cutItem = new MenuItem
            {
                Header = "Cu_t",
                Style = (Style)FindResource("ThemedMenuItemStyle"),
                Command = ApplicationCommands.Cut,
                CommandTarget = DocSourceTextBox
            };
            var copyItem = new MenuItem
            {
                Header = "_Copy",
                Style = (Style)FindResource("ThemedMenuItemStyle"),
                Command = ApplicationCommands.Copy,
                CommandTarget = DocSourceTextBox
            };
            var pasteItem = new MenuItem
            {
                Header = "_Paste",
                Style = (Style)FindResource("ThemedMenuItemStyle"),
                Command = ApplicationCommands.Paste,
                CommandTarget = DocSourceTextBox
            };

            cutItem.IsEnabled = DocSourceTextBox.SelectionLength > 0;
            copyItem.IsEnabled = DocSourceTextBox.SelectionLength > 0;
            pasteItem.IsEnabled = Clipboard.ContainsText();

            menu.Items.Add(cutItem);
            menu.Items.Add(copyItem);
            menu.Items.Add(pasteItem);

            if (Clipboard.ContainsImage())
            {
                menu.Items.Add(new Separator { Style = (Style)FindResource("ThemedMenuSeparatorStyle") });
                var imgItem = new MenuItem
                {
                    Header = "Paste image from clipboard",
                    Style = (Style)FindResource("ThemedMenuItemStyle")
                };
                imgItem.Click += (_, _) => DocSourceTextBox_PasteImageFromClipboard();
                menu.Items.Add(imgItem);
            }

            menu.PlacementTarget = DocSourceTextBox;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(DocSourceTextBox_PreviewMouseRightButtonDown), ex);
        }
    }

    private void DocSourceTextBox_PasteImageFromClipboard()
    {
        if (DocSourceTextBox is null || !Clipboard.ContainsImage()) return;
        if (string.IsNullOrEmpty(_currentDocPath)) return;

        var clipImg = Clipboard.GetImage()!;
        var editor = new ClipboardImageEditorWindow(this, clipImg);
        editor.ShowDialog();
        if (editor.Result is not { } image) return;

        var docName = Path.GetFileNameWithoutExtension(_currentDocPath);
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var fileName = $"{docName}-{timestamp}.png";
        var docDir = Path.GetDirectoryName(_currentDocPath)!;
        var imagesDir = Path.Combine(docDir, "images");
        Directory.CreateDirectory(imagesDir);
        var fullImagePath = Path.Combine(imagesDir, fileName);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.OpenWrite(fullImagePath))
            encoder.Save(stream);

        var caretIndex = DocSourceTextBox.CaretIndex;
        var markdown = $"![{docName} screenshot](images/{fileName})";
        DocSourceTextBox.Text = DocSourceTextBox.Text.Insert(caretIndex, markdown);
        DocSourceTextBox.CaretIndex = caretIndex + markdown.Length;
    }

    // ── Feature 2: Find-in-source bar ───────────────────────────────────────────
    private void ShowDocSourceFindBar()
    {
        if (DocSourcePanel is null || DocSourceTextBox is null) return;

        if (_docSourceFindBar is not null)
        {
            _docSourceFindTextBox?.Focus();
            _docSourceFindTextBox?.SelectAll();
            return;
        }

        // Ensure overlay canvas (and Grid wrapper) exist
        var overlayCanvas = EnsureDocSourceOverlayCanvas();
        var grid = DocSourcePanel.Child as Grid;
        if (grid is null) return;

        // Create a separate Canvas for find match highlights
        _docSourceFindOverlay = new Canvas
        {
            IsHitTestVisible = false,
            Background = Brushes.Transparent
        };
        grid.Children.Add(_docSourceFindOverlay);

        // Re-render highlights whenever the user scrolls the source editor
        var sv = FindVisualChild<ScrollViewer>(DocSourceTextBox);
        if (sv is not null)
            sv.ScrollChanged += DocSourceFind_ScrollChanged;

        // Create find bar
        var findPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };

        _docSourceFindTextBox = new TextBox
        {
            Width = 150,
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 6, 0)
        };
        _docSourceFindTextBox.TextChanged += DocSourceFind_TextChanged;
        _docSourceFindTextBox.PreviewKeyDown += DocSourceFind_KeyDown;

        var prevBtn = new Button
        {
            Content = "▲",
            Width = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 2, 0)
        };
        prevBtn.Click += (s, e) => DocSourceFind_NavigatePrevious();

        var nextBtn = new Button
        {
            Content = "▼",
            Width = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 6, 0)
        };
        nextBtn.Click += (s, e) => DocSourceFind_NavigateNext();

        _docSourceFindMatchCount = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 11
        };
        _docSourceFindMatchCount.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 24,
            Padding = new Thickness(0)
        };
        closeBtn.Click += (s, e) => HideDocSourceFindBar();

        findPanel.Children.Add(_docSourceFindTextBox);
        findPanel.Children.Add(prevBtn);
        findPanel.Children.Add(nextBtn);
        findPanel.Children.Add(_docSourceFindMatchCount);
        findPanel.Children.Add(closeBtn);

        _docSourceFindBar = new Border
        {
            Child = findPanel,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 10, 10, 0)
        };
        _docSourceFindBar.SetResourceReference(Border.BackgroundProperty, "PopupSurface");
        _docSourceFindBar.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        _docSourceFindBar.BorderThickness = new Thickness(1);

        grid.Children.Add(_docSourceFindBar);

        _docSourceFindTextBox.Focus();
    }

    private void HideDocSourceFindBar()
    {
        if (_docSourceFindBar is null) return;

        // Unsubscribe scroll listener
        if (DocSourceTextBox is not null)
        {
            var sv = FindVisualChild<ScrollViewer>(DocSourceTextBox);
            if (sv is not null)
                sv.ScrollChanged -= DocSourceFind_ScrollChanged;
        }

        var grid = DocSourcePanel?.Child as Grid;
        if (grid is not null)
        {
            grid.Children.Remove(_docSourceFindBar);
            if (_docSourceFindOverlay is not null)
                grid.Children.Remove(_docSourceFindOverlay);
        }

        _docSourceFindBar = null;
        _docSourceFindTextBox = null;
        _docSourceFindMatchCount = null;
        _docSourceFindOverlay = null;
        _docSourceFindMatches.Clear();
        _docSourceFindCurrentIndex = -1;
        DocSourceTextBox?.Focus();
    }

    private void DocSourceFind_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        DocSourceFind_RenderHighlights();
    }

    private void DocSourceFind_TextChanged(object sender, TextChangedEventArgs e)
    {
        _docSourceFindDebounceTimer?.Stop();
        _docSourceFindDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _docSourceFindDebounceTimer.Tick += (s, args) =>
        {
            _docSourceFindDebounceTimer.Stop();
            DocSourceFind_UpdateMatches();
        };
        _docSourceFindDebounceTimer.Start();
    }

    private void DocSourceFind_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideDocSourceFindBar();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter || e.Key == Key.F3)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                DocSourceFind_NavigatePrevious();
            else
                DocSourceFind_NavigateNext();
            e.Handled = true;
        }
    }

    private void DocSourceFind_UpdateMatches()
    {
        if (DocSourceTextBox is null || _docSourceFindTextBox is null || _docSourceFindOverlay is null) return;

        _docSourceFindMatches.Clear();
        _docSourceFindCurrentIndex = -1;
        _docSourceFindOverlay.Children.Clear();

        var searchText = _docSourceFindTextBox.Text;
        if (string.IsNullOrEmpty(searchText))
        {
            if (_docSourceFindMatchCount is not null)
                _docSourceFindMatchCount.Text = string.Empty;
            return;
        }

        var text = DocSourceTextBox.Text;
        var index = 0;
        while ((index = text.IndexOf(searchText, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            _docSourceFindMatches.Add(index);
            index += searchText.Length;
        }

        if (_docSourceFindMatches.Count > 0)
            _docSourceFindCurrentIndex = 0;

        DocSourceFind_RenderHighlights();
        DocSourceFind_UpdateMatchCountDisplay();

        if (_docSourceFindCurrentIndex >= 0)
            DocSourceFind_ScrollToCurrentMatch();
    }

    private void DocSourceFind_RenderHighlights()
    {
        if (DocSourceTextBox is null || _docSourceFindOverlay is null || _docSourceFindTextBox is null) return;

        _docSourceFindOverlay.Children.Clear();

        var isDark = AgentStatusCard.IsDarkTheme;
        var matchBg = isDark ? Color.FromArgb(200, 74, 62, 16) : Color.FromArgb(200, 200, 224, 255);
        var currentBg = isDark ? Color.FromArgb(220, 200, 160, 0) : Color.FromArgb(220, 32, 96, 192);
        var searchLen = _docSourceFindTextBox.Text.Length;
        if (searchLen == 0) return;

        // Draw match rectangles.
        // GetRectFromCharacterIndex returns coords in TextBox local space (accounting for
        // current scroll). TranslatePoint converts them to the overlay Canvas coordinate space.
        for (int i = 0; i < _docSourceFindMatches.Count; i++)
        {
            var pos = _docSourceFindMatches[i];
            var startRect = DocSourceTextBox.GetRectFromCharacterIndex(pos);
            if (startRect == Rect.Empty) continue; // off-screen — skip, ScrollChanged will re-render

            // Use start+end rects for accurate width (handles variable-width fonts too)
            var endPos = Math.Min(pos + searchLen, DocSourceTextBox.Text.Length);
            var endRect = DocSourceTextBox.GetRectFromCharacterIndex(endPos);
            double highlightWidth = (endRect != Rect.Empty && endRect.Left >= startRect.Left)
                ? Math.Max(2, endRect.Left - startRect.Left)
                : Math.Max(2, searchLen * (startRect.Width > 0 ? startRect.Width : 8));

            var canvasOrigin = DocSourceTextBox.TranslatePoint(
                new Point(startRect.Left, startRect.Top), _docSourceFindOverlay);

            var highlight = new Shapes.Rectangle
            {
                Width = highlightWidth,
                Height = Math.Max(2, startRect.Height),
                Fill = new SolidColorBrush(i == _docSourceFindCurrentIndex ? currentBg : matchBg),
                Opacity = 0.55
            };

            Canvas.SetLeft(highlight, canvasOrigin.X);
            Canvas.SetTop(highlight, canvasOrigin.Y);
            _docSourceFindOverlay.Children.Add(highlight);
        }

        // Draw scrollbar tick marks proportional to the total text length
        if (DocSourceTextBox.Text.Length > 0 && _docSourceFindMatches.Count > 0)
        {
            var sv = FindVisualChild<ScrollViewer>(DocSourceTextBox);
            var scrollBar = sv is not null ? FindVisualChild<ScrollBar>(sv) : null;
            double trackHeight = scrollBar?.ActualHeight ?? DocSourceTextBox.ActualHeight;

            foreach (var pos in _docSourceFindMatches)
            {
                var fraction = (double)pos / DocSourceTextBox.Text.Length;
                var tick = new Shapes.Rectangle
                {
                    Width = 4,
                    Height = 3,
                    Fill = new SolidColorBrush(matchBg)
                };
                Canvas.SetRight(tick, 0);
                Canvas.SetTop(tick, fraction * trackHeight);
                _docSourceFindOverlay.Children.Add(tick);
            }
        }
    }

    private void DocSourceFind_UpdateMatchCountDisplay()
    {
        if (_docSourceFindMatchCount is null) return;

        if (_docSourceFindMatches.Count == 0)
            _docSourceFindMatchCount.Text = "No matches";
        else
            _docSourceFindMatchCount.Text = $"{_docSourceFindCurrentIndex + 1} / {_docSourceFindMatches.Count}";
    }

    private void DocSourceFind_NavigateNext()
    {
        if (_docSourceFindMatches.Count == 0) return;

        _docSourceFindCurrentIndex = (_docSourceFindCurrentIndex + 1) % _docSourceFindMatches.Count;
        DocSourceFind_RenderHighlights();
        DocSourceFind_UpdateMatchCountDisplay();
        DocSourceFind_ScrollToCurrentMatch();
    }

    private void DocSourceFind_NavigatePrevious()
    {
        if (_docSourceFindMatches.Count == 0) return;

        _docSourceFindCurrentIndex--;
        if (_docSourceFindCurrentIndex < 0)
            _docSourceFindCurrentIndex = _docSourceFindMatches.Count - 1;

        DocSourceFind_RenderHighlights();
        DocSourceFind_UpdateMatchCountDisplay();
        DocSourceFind_ScrollToCurrentMatch();
    }

    private void DocSourceFind_ScrollToCurrentMatch()
    {
        if (DocSourceTextBox is null || _docSourceFindCurrentIndex < 0 || _docSourceFindCurrentIndex >= _docSourceFindMatches.Count) return;

        var pos = _docSourceFindMatches[_docSourceFindCurrentIndex];

        // Scroll vertically to the matching line
        DocSourceTextBox.ScrollToLine(DocSourceTextBox.GetLineIndexFromCharacterIndex(pos));

        // After the vertical scroll settles, handle horizontal scroll, re-render highlights
        // (positions changed due to scroll), then return focus to the find box.
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            var sv = FindVisualChild<ScrollViewer>(DocSourceTextBox);
            if (sv is not null && DocSourceTextBox is not null && _docSourceFindTextBox is not null)
            {
                var matchRect = DocSourceTextBox.GetRectFromCharacterIndex(pos);
                if (matchRect != Rect.Empty)
                {
                    // Bring the match into horizontal view with a small margin
                    const double margin = 24;
                    if (matchRect.Left < 0)
                        sv.ScrollToHorizontalOffset(Math.Max(0, sv.HorizontalOffset + matchRect.Left - margin));
                    else if (matchRect.Right > DocSourceTextBox.ActualWidth)
                        sv.ScrollToHorizontalOffset(sv.HorizontalOffset + matchRect.Right - DocSourceTextBox.ActualWidth + margin);
                }
            }

            DocSourceFind_RenderHighlights();
            _docSourceFindTextBox?.Focus();
        });
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;
            var result = FindVisualChild<T>(child);
            if (result is not null)
                return result;
        }
        return null;
    }

    private static bool IsPrintableKey(Key key) =>
        (key >= Key.A && key <= Key.Z) ||
        (key >= Key.D0 && key <= Key.D9) ||
        (key >= Key.NumPad0 && key <= Key.NumPad9) ||
        key is Key.OemTilde or Key.OemMinus or Key.OemPlus or Key.OemOpenBrackets
            or Key.OemCloseBrackets or Key.OemPipe or Key.OemSemicolon or Key.OemQuotes
            or Key.OemComma or Key.OemPeriod or Key.OemQuestion or Key.Space;

    private static char? KeyToChar(Key key, bool shift)
    {
        if (key >= Key.A && key <= Key.Z)
            return shift ? (char)('A' + (key - Key.A)) : (char)('a' + (key - Key.A));
        if (key >= Key.D0 && key <= Key.D9)
        {
            var digits = "0123456789";
            var shifted = ")!@#$%^&*(";
            int i = key - Key.D0;
            return shift ? shifted[i] : digits[i];
        }
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return (char)('0' + (key - Key.NumPad0));
        return key switch
        {
            Key.Space => ' ',
            Key.OemTilde => shift ? '~' : '`',
            Key.OemMinus => shift ? '_' : '-',
            Key.OemPlus => shift ? '+' : '=',
            Key.OemOpenBrackets => shift ? '{' : '[',
            Key.OemCloseBrackets => shift ? '}' : ']',
            Key.OemPipe => shift ? '|' : '\\',
            Key.OemSemicolon => shift ? ':' : ';',
            Key.OemQuotes => shift ? '"' : '\'',
            Key.OemComma => shift ? '<' : ',',
            Key.OemPeriod => shift ? '>' : '.',
            Key.OemQuestion => shift ? '?' : '/',
            _ => null
        };
    }

    private void ApplyViewMode()
    {
        if (NormalViewMenuItem is not null)
            NormalViewMenuItem.IsChecked = !_transcriptFullScreenEnabled;

        if (FullScreenTranscriptMenuItem is not null)
            FullScreenTranscriptMenuItem.IsChecked = _transcriptFullScreenEnabled;

        if (ViewDocumentationMenuItem is not null)
            ViewDocumentationMenuItem.IsChecked = _documentationModeEnabled;

        if (StatusPanelBorder is not null)
            StatusPanelBorder.Visibility = _transcriptFullScreenEnabled ? Visibility.Collapsed : Visibility.Visible;

        if (PromptBorder is not null)
            PromptBorder.Visibility = (_transcriptFullScreenEnabled && !_fullScreenPromptVisible)
                ? Visibility.Collapsed
                : Visibility.Visible;

        // In fullscreen the status panel and prompt are hidden, so TranscriptPanelsGrid's
        // own top/bottom margin (which provides separation from those neighbours) would
        // double up with MainGrid's outer margin (14px) and make the top/bottom gaps twice
        // as large as the left/right gaps.  Zero it out in fullscreen so all four sides are
        // balanced at the outer 14px.
        if (TranscriptPanelsGrid is not null)
            TranscriptPanelsGrid.Margin = _transcriptFullScreenEnabled
                ? new Thickness(0)
                : new Thickness(0, 14, 0, 14);

        // Documentation mode: show docs panel whenever documentation mode is active
        var docsVisible = _documentationModeEnabled;

        if (DocsSplitter is not null)
            DocsSplitter.Visibility = docsVisible ? Visibility.Visible : Visibility.Collapsed;

        if (DocsSplitterColumn is not null)
            DocsSplitterColumn.Width = docsVisible ? new GridLength(8) : new GridLength(0);

        if (DocsPanel is not null)
            DocsPanel.Visibility = docsVisible ? Visibility.Visible : Visibility.Collapsed;

        if (DocsPanelColumn is not null)
        {
            if (docsVisible)
            {
                // Restore saved width or default to 600
                var width = _docsPanelState?.PanelWidth ?? _settingsSnapshot.DocsPanelWidth ?? 600;
                DocsPanelColumn.Width = new GridLength(width);
            }
            else
            {
                DocsPanelColumn.Width = new GridLength(0);
            }
        }

        UpdateAgentCardVisibility();
    }

    private void RemoveTemporaryAgents()
    {
        var removableThreads = _agentThreadRegistry.ThreadOrder
            .Where(thread => !AgentThreadRegistry.HasRosterBackedIdentity(thread) && !thread.IsPlaceholderThread && !_backgroundTaskPresenter.IsThreadActiveForDisplay(thread))
            .ToArray();

        if (removableThreads.Length == 0)
        {
            MessageBox.Show(
                this,
                "No inactive temporary agents are available to remove.",
                "Cleanup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var removedSelectedThread = _selectedTranscriptThread is not null &&
                                    removableThreads.Any(thread => ReferenceEquals(thread, _selectedTranscriptThread));

        foreach (var thread in removableThreads)
            _backgroundTaskPresenter.RemovePromotionEntry(thread.ThreadId);

        _agentThreadRegistry.RemoveThreads(removableThreads);

        if (removedSelectedThread)
            SelectTranscriptThread(CoordinatorThread);

        SyncAgentCardsWithThreads();
        _conversationManager.PersistConversationState(_conversationManager.ConversationState with
        {
            SessionId = _conversationManager.CurrentSessionId,
            PromptDraft = PromptTextBox.Text,
            PromptHistory = _conversationManager.PromptHistory.ToArray(),
            Threads = _conversationManager.BuildPersistedAgentThreadRecords(includeCurrentTurns: false)
        });
    }

    private void UpdateStatusTitle()
    {
        var version = _squadCliAdapter.SquadVersion;

        if (SquadVersionTextBlock is not null)
        {
            SquadVersionTextBlock.Text = string.IsNullOrWhiteSpace(version) ? "Squad" : $"Squad v{version}";
        }

        if (SquadDashVersionTextBlock is not null)
        {
            SquadDashVersionTextBlock.Text = $"SquadDash v{AppVersion.Full}";
        }
    }

    private void OpenWorkspace(
        string folderPath,
        bool rememberFolder,
        bool closeWindowIfActivatedExisting = false,
        bool showBlockedDialog = true)
    {
        if (!Directory.Exists(folderPath))
        {
            MessageBox.Show(
                $"The folder does not exist:\n{folderPath}",
                "Folder Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var targetWorkspace = SessionWorkspace.Create(folderPath);
        if (_currentWorkspace is not null &&
            string.Equals(_currentWorkspace.FolderPath, targetWorkspace.FolderPath, StringComparison.OrdinalIgnoreCase))
        {
            if (rememberFolder)
                RememberWorkspaceFolder(targetWorkspace.FolderPath);

            ActivateOwnedWindow();
            return;
        }

        var workspaceLease = TakeStartupWorkspaceLease(targetWorkspace.FolderPath);
        if (workspaceLease is null &&
            !WorkspaceStartupRoutingPolicy.ShouldBypassSingleInstanceRouting(_screenshotRefreshOptions))
        {
            var decision = _workspaceOpenCoordinator.ReserveOrActivate(
                _workspacePaths.ApplicationRoot,
                targetWorkspace.FolderPath,
                Environment.ProcessId,
                _processStartedAtUtcTicks,
                _workspaceOwnershipLease);

            switch (decision.Disposition)
            {
                case WorkspaceOpenDisposition.AlreadyOpenHere:
                    if (rememberFolder)
                        RememberWorkspaceFolder(targetWorkspace.FolderPath);

                    ActivateOwnedWindow();
                    return;

                case WorkspaceOpenDisposition.ActivatedExisting:
                    if (rememberFolder)
                        RememberWorkspaceFolder(targetWorkspace.FolderPath);

                    SquadDashTrace.Write(
                        "Workspace",
                        $"Activated an existing SquadDash instance for workspace={targetWorkspace.FolderPath}.");
                    if (closeWindowIfActivatedExisting && _currentWorkspace is null)
                        Dispatcher.BeginInvoke(Close);

                    return;

                case WorkspaceOpenDisposition.Blocked:
                    SquadDashTrace.Write(
                        "Workspace",
                        $"Workspace open was blocked because another instance already owns {targetWorkspace.FolderPath}.");

                    if (showBlockedDialog)
                    {
                        MessageBox.Show(
                            this,
                            $"That workspace is already open in another SquadDash window:{Environment.NewLine}{targetWorkspace.FolderPath}",
                            "Workspace Already Open",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }

                    if (closeWindowIfActivatedExisting && _currentWorkspace is null)
                        Dispatcher.BeginInvoke(Close);

                    return;

                case WorkspaceOpenDisposition.OpenHere:
                    workspaceLease = decision.Lease;
                    break;
            }
        }

        _conversationManager.SaveWorkspaceInputState();

        var openWsSw = Stopwatch.StartNew();

        var previousLease = _workspaceOwnershipLease;
        _workspaceOwnershipLease = workspaceLease;
        _startupWorkspaceLease = null;

        _currentWorkspace = targetWorkspace;
        _currentSolutionPath = _currentWorkspace.SolutionPath;
        _currentSolutionName = _currentWorkspace.SolutionName;
        _workspaceGitHubUrl = TryResolveGitHubUrl(_currentWorkspace.FolderPath);
        ViewPagesButton.Visibility = _workspaceGitHubUrl is not null ? Visibility.Visible : Visibility.Collapsed;
        ClearRuntimeIssue();

        var repairSw = Stopwatch.StartNew();
        SquadScribeWorkspaceRepairService.Repair(_currentWorkspace.FolderPath);
        repairSw.Stop();
        SquadDashTrace.Write(TraceCategory.Performance, $"WORKSPACE_REPAIR: {repairSw.ElapsedMilliseconds}ms folder={_currentWorkspace.FolderPath}");

        RefreshInstallationState();
        RefreshDeveloperRuntimeIssuePreview();
        SquadInstallerService.EnsureSquadDashUniverseFiles(_currentWorkspace.FolderPath);
        ConfigureTeamFileWatcher();

        if (rememberFolder)
            RememberWorkspaceFolder(_currentWorkspace.FolderPath);

        ClearSessionView();
        RefreshAgentCards();
        // Suppress per-turn scroll operations during history load; EndLoad() will issue
        // exactly one scroll-to-bottom once all stored turns have been appended.
        _scrollController.BeginLoad();

        SquadDashTrace.Write(TraceCategory.Performance, $"LOAD_CONVERSATION_START: folder={_currentWorkspace.FolderPath}");
        var loadConvSw = Stopwatch.StartNew();
        _conversationManager.LoadWorkspaceConversation();
        loadConvSw.Stop();
        SquadDashTrace.Write(TraceCategory.Performance, $"LOAD_CONVERSATION_END: {loadConvSw.ElapsedMilliseconds}ms");

        _loopQueued = false;

        // Restore queued prompts saved before last shutdown.
        var savedQueue = _conversationManager.ConversationState.QueuedPrompts;
        if (savedQueue is { Count: > 0 })
        {
            _promptQueueSeq = 0;
            foreach (var text in savedQueue)
                _promptQueue.Enqueue(text, ++_promptQueueSeq);
            SyncQueuePanel();
        }

        // Restore per-workspace loop settings (override global app settings).
        var savedState = _conversationManager.ConversationState;
        if (savedState.LoopMode is { } savedLoopMode)
            _settingsSnapshot = _settingsStore.SaveLoopMode(savedLoopMode);
        if (savedState.LoopContinuousContext is { } savedContinuous)
            _settingsSnapshot = _settingsStore.SaveLoopContinuousContext(savedContinuous);

        RestoreWorkspaceWindowPlacement();
        _conversationManager.ResetHistoryNavigation();
        UpdateWindowTitle();
        UpdateStatusTitle();
        UpdateLeadAgent("Ready", string.Empty, string.Empty);
        UpdateSessionState("Ready");
        RefreshSidebar();
        UpdateInteractiveControlState();
        ScrollToEndIfAtBottom();
        MaybePromptForUniverseSelection();
        MaybePublishMissingUtilityAgentNotice();
        UpdateRunningInstanceRegistration();

        openWsSw.Stop();
        SquadDashTrace.Write(TraceCategory.Performance, $"OPEN_WORKSPACE_TOTAL: {openWsSw.ElapsedMilliseconds}ms");

        previousLease?.Dispose();
    }

    private void MaybePromptForUniverseSelection()
    {
        if (_currentWorkspace is null ||
            _currentInstallationState?.IsSquadInstalledForActiveDirectory != true)
        {
            return;
        }

        var members = _teamRosterLoader.Load(_currentWorkspace.FolderPath);
        if (!SquadTeamRosterLoader.HasNonUtilityMembers(members))
            _pec.InjectUniverseSelectorTurn();
    }

    private void MaybePublishMissingUtilityAgentNotice()
    {
        if (_currentWorkspace is null ||
            _currentInstallationState?.IsSquadInstalledForActiveDirectory != true)
        {
            _lastMissingUtilityAgentNoticeKey = null;
            return;
        }

        var members = _teamRosterLoader.Load(_currentWorkspace.FolderPath);
        if (!SquadTeamRosterLoader.HasNonUtilityMembers(members))
        {
            _lastMissingUtilityAgentNoticeKey = null;
            return;
        }

        var missingUtilities = SquadTeamRosterLoader.GetMissingUtilityAgentNames(_currentWorkspace.FolderPath);
        if (missingUtilities.Count == 0)
        {
            _lastMissingUtilityAgentNoticeKey = null;
            return;
        }

        var noticeKey = _currentWorkspace.FolderPath + "|" + string.Join("|", missingUtilities);
        if (string.Equals(_lastMissingUtilityAgentNoticeKey, noticeKey, StringComparison.OrdinalIgnoreCase))
            return;

        _lastMissingUtilityAgentNoticeKey = noticeKey;
        AppendLine(
            $"[info] Squad checked the team setup and found missing built-in utility agents: {string.Join(", ", missingUtilities)}. Shared workflows like decision merging or backlog monitoring may be incomplete until they are restored.",
            ThemeBrush("SystemInfoText"));
    }

    private WorkspaceOwnershipLease? TakeStartupWorkspaceLease(string folderPath)
    {
        if (_startupWorkspaceLease is null ||
            !_startupWorkspaceLease.Matches(_workspacePaths.ApplicationRoot, folderPath))
        {
            return null;
        }

        var lease = _startupWorkspaceLease;
        _startupWorkspaceLease = null;
        return lease;
    }

    private void RememberWorkspaceFolder(string folderPath)
    {
        _settingsSnapshot = _settingsStore.RememberFolder(folderPath);
        RefreshRecentFoldersMenu(_settingsSnapshot.RecentFolders);
    }

    private void RefreshRecentFoldersMenu(IReadOnlyList<string> recentFolders)
    {
        RecentFoldersMenuItem.Items.Clear();

        var existingFolders = recentFolders
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .ToArray();

        if (existingFolders.Length == 0)
        {
            RecentFoldersMenuItem.IsEnabled = false;
            RecentFoldersMenuItem.Items.Add(new MenuItem
            {
                Header = "(No recent folders)",
                IsEnabled = false
            });
            return;
        }

        RecentFoldersMenuItem.IsEnabled = true;
        foreach (var folder in existingFolders)
        {
            RecentFoldersMenuItem.Items.Add(new MenuItem
            {
                Header = folder,
                Tag = folder
            });
        }

        foreach (var item in RecentFoldersMenuItem.Items.OfType<MenuItem>())
        {
            item.Click += RecentFolderMenuItem_Click;
        }
    }

    private Paragraph CreateTranscriptParagraph(double bottomMargin = 6)
    {
        return new Paragraph
        {
            Margin = new Thickness(0, 0, 0, bottomMargin)
        };
    }

    private TranscriptThreadState CreateCoordinatorTranscriptThread()
    {
        return new TranscriptThreadState(
            "coordinator",
            TranscriptThreadKind.Coordinator,
            "Coordinator",
            DateTimeOffset.Now);
    }

    private IEnumerable<TranscriptThreadState> EnumerateTranscriptThreads()
    {
        yield return CoordinatorThread;

        foreach (var thread in _agentThreadRegistry.ThreadOrder)
            yield return thread;
    }

    private void SelectTranscriptThread(TranscriptThreadState thread, bool scrollToStart = false)
    {
        _selectedTranscriptThread = thread;

        // Preserve search state when navigating to a match in a different thread.
        // When _searchNavigating is set, the caller owns restoring state after the switch.
        if (!_searchNavigating)
        {
            _searchMatches = [];
            _searchMatchCursor = -1;
            _searchAdorner?.Clear();
            _scrollbarAdorner?.Clear();
            _cachedSearchPointers = null;
            ClearBucCellHighlights();
            if (!string.IsNullOrEmpty(SearchBox.Text))
                SearchBox.Text = string.Empty;
            UpdateSearchUi();
        }

        foreach (var candidate in EnumerateTranscriptThreads())
            candidate.IsSelected = ReferenceEquals(candidate, thread);

        UpdateTransientTranscriptFooters(thread);
        UpdateCompletedTimeFooters();
        // close that panel before assigning to OutputTextBox — FlowDocument can only
        // belong to one RichTextBox at a time.
        if (thread.Document.Parent is RichTextBox secondaryOwner && secondaryOwner != OutputTextBox)
        {
            var secondaryEntry = _secondaryTranscripts.FirstOrDefault(e => e.TranscriptBox == secondaryOwner);
            if (secondaryEntry != null)
                CloseSecondaryPanel(secondaryEntry);
            else
                secondaryOwner.Document = new FlowDocument(); // detach without a tracked panel
        }

        OutputTextBox.Document = thread.Document;
        ApplyTranscriptFontSizeToDocument(thread.Document);
        UpdateTranscriptThreadBadge();
        SyncAgentCardsWithThreads();
        SyncPromptNavButtons();

        // If this agent thread's turns were deferred at startup (lazy rendering),
        // render them now.  The document is already assigned to OutputTextBox above so
        // BeginChange/EndChange in RenderConversationHistoryAsync suppress intermediate
        // layout passes correctly.  The Normal-priority dispatch ensures the render
        // completes before any subsequent Input-priority click events can fire, so there
        // is no race between the render and the user switching threads again.
        if (_conversationManager.HasPendingRender(thread))
            _ = _conversationManager.EnsureAgentThreadRenderedAsync(thread);

        // When switching to a thread with no focused prompt, briefly flash the
        // thread's start time in the same hint label so the user knows when
        // this conversation began.
        if (thread.PromptNavIndex == -1)
        {
            PromptNavHintTextBlock.Text = FormatRelativeTime(thread.StartedAt);
            ShowPromptNavHintWithFadeOut();
        }

        ScrollTranscriptThread(thread, scrollToStart);
        UpdateInteractiveControlState();
    }

    private void UpdateTranscriptThreadBadge()
    {
        var thread = _selectedTranscriptThread ?? CoordinatorThread;
        if (thread.Kind == TranscriptThreadKind.Coordinator)
        {
            TranscriptTitleTextBlock.Text = "Coordinator";
            return;
        }

        var title = thread.Title?.Trim();
        var displayTitle = string.IsNullOrWhiteSpace(title) ? "Agent" : AbbreviateAgentName(title);
        var possessive = $"{displayTitle}'s transcript";
        TranscriptTitleTextBlock.ToolTip = BuildGpaTooltip(displayTitle);

        var intent = thread.LatestIntent?.Trim();
        if (!string.IsNullOrWhiteSpace(intent))
        {
            const int MaxIntentLength = 60;
            var truncated = intent.Length > MaxIntentLength
                ? intent[..MaxIntentLength].TrimEnd() + "…"
                : intent;
            TranscriptTitleTextBlock.Text = $"{possessive} — {truncated}";
        }
        else
        {
            TranscriptTitleTextBlock.Text = possessive;
        }
    }

    private void UpdateTransientTranscriptFooters(TranscriptThreadState selectedThread)
    {
        foreach (var thread in EnumerateTranscriptThreads())
        {
            if (thread.TransientFooterParagraph is null)
                continue;

            thread.Document.Blocks.Remove(thread.TransientFooterParagraph);
            thread.TransientFooterParagraph = null;
        }

        if (selectedThread.Kind != TranscriptThreadKind.Agent)
            return;

        var footerParagraph = CreateTranscriptParagraph(bottomMargin: 0);
        footerParagraph.Margin = new Thickness(0, 14, 0, 0);
        _markdownRenderer.AppendInlineMarkdown(footerParagraph.Inlines, "[Back to main transcript](thread:coordinator)");
        selectedThread.Document.Blocks.Add(footerParagraph);
        selectedThread.TransientFooterParagraph = footerParagraph;
    }

    private void EnsureThreadFooterAtEnd(TranscriptThreadState thread)
    {
        // Determine expected last block.
        var expectedLast = (Block?)thread.CompletedTimeParagraph ?? thread.TransientFooterParagraph;
        if (expectedLast is null)
            return;

        if (ReferenceEquals(thread.Document.Blocks.LastBlock, expectedLast) &&
            (thread.TransientFooterParagraph is null || thread.CompletedTimeParagraph is null ||
             ReferenceEquals(thread.Document.Blocks.LastBlock.PreviousBlock, thread.TransientFooterParagraph)))
            return;

        // Re-anchor both footers in correct order: TransientFooter then CompletedTime.
        if (thread.TransientFooterParagraph is not null)
        {
            thread.Document.Blocks.Remove(thread.TransientFooterParagraph);
            thread.Document.Blocks.Add(thread.TransientFooterParagraph);
        }
        if (thread.CompletedTimeParagraph is not null)
        {
            thread.Document.Blocks.Remove(thread.CompletedTimeParagraph);
            thread.Document.Blocks.Add(thread.CompletedTimeParagraph);
        }
    }

    private void ScrollTranscriptThread(TranscriptThreadState thread, bool scrollToStart)
    {
        EnsureThreadFooterAtEnd(thread);
        _scrollController.OnThreadSelected(scrollToStart);
    }

    // ── Completed-time footer ────────────────────────────────────────────────

    private IEnumerable<TranscriptThreadState> GetVisibleTranscriptThreads()
    {
        if (_mainTranscriptVisible && _selectedTranscriptThread is not null)
            yield return _selectedTranscriptThread;
        foreach (var entry in _secondaryTranscripts)
            yield return entry.Thread;
    }

    private Paragraph CreateCompletedTimeParagraph(string text)
    {
        var p = CreateTranscriptParagraph(bottomMargin: 0);
        p.Margin = new Thickness(0, 10, 0, 0);
        var run = new Run(text) { FontSize = 11 };
        run.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
        p.Inlines.Add(run);
        return p;
    }

    private void UpdateCompletedTimeFooters()
    {
        var visibleCompleted = GetVisibleTranscriptThreads()
            .Where(t => t.CompletedAt is not null)
            .ToHashSet(ReferenceEqualityComparer.Instance);

        foreach (var thread in EnumerateTranscriptThreads())
        {
            if (visibleCompleted.Contains(thread))
            {
                var text = $"Completed {StatusTimingPresentation.FormatRelativeTimestamp(thread.CompletedAt!.Value)}";
                if (thread.CompletedTimeParagraph is null)
                {
                    var p = CreateCompletedTimeParagraph(text);
                    thread.Document.Blocks.Add(p);
                    thread.CompletedTimeParagraph = p;
                }
                else
                {
                    if (thread.CompletedTimeParagraph.Inlines.FirstInline is Run run)
                        run.Text = text;
                    if (!ReferenceEquals(thread.Document.Blocks.LastBlock, thread.CompletedTimeParagraph))
                    {
                        thread.Document.Blocks.Remove(thread.CompletedTimeParagraph);
                        thread.Document.Blocks.Add(thread.CompletedTimeParagraph);
                    }
                }
            }
            else if (thread.CompletedTimeParagraph is not null)
            {
                thread.Document.Blocks.Remove(thread.CompletedTimeParagraph);
                thread.CompletedTimeParagraph = null;
            }
        }

        if (visibleCompleted.Count > 0)
            EnsureCompletedTimeFooterTimerRunning();
        else
            _completedTimeFooterTimer?.Stop();
    }

    private void EnsureCompletedTimeFooterTimerRunning()
    {
        if (_completedTimeFooterTimer is null)
        {
            _completedTimeFooterTimer = new DispatcherTimer(TimeSpan.FromSeconds(60), DispatcherPriority.Background,
                (_, _) =>
                {
                    try { UpdateCompletedTimeFooters(); }
                    catch (Exception ex) { HandleUiCallbackException("CompletedTimeFooterTimer.Tick", ex); }
                },
                Dispatcher);
        }
        if (!_completedTimeFooterTimer.IsEnabled)
            _completedTimeFooterTimer.Start();
    }



    private void ShowSingleTranscript(AgentStatusCard agent)
    {
        foreach (var entry in _secondaryTranscripts.ToList())
            CloseSecondaryPanel(entry);

        ShowMainTranscript();
        SelectTranscriptThread(GetTranscriptThreadForAgent(agent));
    }

    private void ToggleAgentTranscriptVisibility(AgentStatusCard agent)
    {
        if (agent.IsLeadAgent)
        {
            if (_mainTranscriptVisible && CoordinatorThread.IsSelected)
            {
                if (_secondaryTranscripts.Count > 0)
                    HideMainTranscript();

                return;
            }

            ShowMainTranscript();
            SelectTranscriptThread(CoordinatorThread);
            return;
        }

        var existing = _secondaryTranscripts.FirstOrDefault(entry => entry.Agent == agent);
        if (existing is not null)
        {
            CloseSecondaryPanel(existing);
            if (!_mainTranscriptVisible && _secondaryTranscripts.Count == 0)
            {
                ShowMainTranscript();
                SelectTranscriptThread(CoordinatorThread);
            }

            return;
        }

        var thread = GetTranscriptThreadForAgent(agent);
        if (_mainTranscriptVisible && thread.IsSelected)
        {
            SelectTranscriptThread(CoordinatorThread);
            return;
        }

        OpenSecondaryPanel(agent, GetTranscriptThreadForAgent(agent), isAutoOpenedInMultiMode: false);
    }

    private void OpenSecondaryPanel(AgentStatusCard agent, TranscriptThreadState thread, bool isAutoOpenedInMultiMode)
    {
        if (_secondaryTranscripts.Any(e => ReferenceEquals(e.Thread, thread)))
        {
            SquadDashTrace.Write(TraceCategory.TranscriptPanels,
                $"OpenSecondaryPanel skipped duplicate thread={thread.ThreadId} agent={agent.Name} seq={thread.SequenceNumber}");
            SyncSelectionControllerWithUiState("OpenSecondaryPanel.duplicate");

            // Find the existing panel and flash it
            var existingEntry = _secondaryTranscripts.First(e => ReferenceEquals(e.Thread, thread));
            FlashGlowHighlight(existingEntry.PanelBorder, ColorFromHex(agent.AccentColorHex));
            return;
        }

        // If this thread's document is already displayed in the main OutputTextBox,
        // opening a secondary panel would throw (FlowDocument can only belong to
        // one RichTextBox at a time).  Flash the main transcript border instead.
        if (thread.Document.Parent == OutputTextBox)
        {
            SquadDashTrace.Write(TraceCategory.TranscriptPanels,
                $"OpenSecondaryPanel skipped main-owned thread={thread.ThreadId} agent={agent.Name} selectedMain={thread.IsSelected}");
            SyncSelectionControllerWithUiState("OpenSecondaryPanel.mainOwner");
            FlashGlowHighlight(MainTranscriptBorder, ColorFromHex(agent.AccentColorHex));
            return;
        }

        var entry = CreateSecondaryTranscriptPanel(agent, thread);
        entry.IsAutoOpenedInMultiMode = isAutoOpenedInMultiMode;
        _secondaryTranscripts.Add(entry);
        EnsureTranscriptTitleRefreshTimerRunning();
        thread.IsSecondaryPanelOpen = true;
        UpdateCompletedTimeFooters();
        SquadDashTrace.Write(TraceCategory.TranscriptPanels,
            $"OpenSecondaryPanel opened thread={thread.ThreadId} agent={agent.Name} seq={thread.SequenceNumber} auto={isAutoOpenedInMultiMode} title=\"{entry.TitleBlock.Text}\"");
        SyncSelectionControllerWithUiState("OpenSecondaryPanel.opened");
        SyncTranscriptTargetIndicators();
        RebuildTranscriptPanelsGrid();
        FlashGlowHighlight(entry.PanelBorder, ColorFromHex(agent.AccentColorHex));
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            try { entry.ScrollController.ScrollToBottom(); }
            catch (Exception ex) { HandleUiCallbackException("SecondaryPanel.InitialScroll", ex); }
        });
    }

    private void CloseSecondaryPanel(SecondaryTranscriptEntry entry)
    {
        CancelAutoCloseCountdown(entry);
        if (ReferenceEquals(entry.TranscriptBox.Document, entry.Thread.Document))
            entry.TranscriptBox.Document = new FlowDocument();
        SquadDashTrace.Write(TraceCategory.TranscriptPanels,
            $"CloseSecondaryPanel closing thread={entry.Thread.ThreadId} agent={entry.Agent.Name} seq={entry.Thread.SequenceNumber} title=\"{entry.TitleBlock.Text}\"");
        _secondaryTranscripts.Remove(entry);
        entry.Thread.IsSecondaryPanelOpen = false;
        if (_secondaryTranscripts.Count == 0)
            _transcriptTitleRefreshTimer?.Stop();
        UpdateCompletedTimeFooters();
        SyncSelectionControllerWithUiState("CloseSecondaryPanel.closed");
        SyncTranscriptTargetIndicators();
        RebuildTranscriptPanelsGrid();
    }

    private void ShowMainTranscript()
    {
        _mainTranscriptVisible = true;
        _selectionController.SetMainVisible(true);
        MainTranscriptBorder.Visibility = Visibility.Visible;
        SyncSelectionControllerWithUiState("ShowMainTranscript");
        SyncTranscriptTargetIndicators();
        RebuildTranscriptPanelsGrid();
    }

    private void HideMainTranscript()
    {
        _mainTranscriptVisible = false;
        _selectionController.SetMainVisible(false);
        MainTranscriptBorder.Visibility = Visibility.Collapsed;
        SyncSelectionControllerWithUiState("HideMainTranscript");
        SyncTranscriptTargetIndicators();
        RebuildTranscriptPanelsGrid();
    }

    private void HandleActiveAgentCountdownCheck()
    {
        foreach (var entry in _secondaryTranscripts.ToList())
        {
            if (!entry.IsAutoOpenedInMultiMode)
            {
                CancelAutoCloseCountdown(entry);
                continue;
            }

            var isStillActive = _backgroundTaskPresenter.IsThreadActiveForDisplay(entry.Thread);
            if (isStillActive)
            {
                // Agent is still running — stop any active countdown without permanently
                // cancelling, so the countdown can restart once the agent finishes.
                entry.CountdownStarted = false;
                entry.CountdownTimer?.Stop();
                entry.CountdownTimer = null;
                entry.CountdownSecondsRemaining = 0;
                entry.PostponeTimer?.Stop();
                entry.PostponeTimer = null;
                if (entry.CountdownOverlay is not null)
                {
                    entry.ContentGrid.Children.Remove(entry.CountdownOverlay);
                    entry.CountdownOverlay = null;
                }
                continue;
            }

            if (entry.CountdownTimer is null)
                StartAutoCloseCountdown(entry);
        }
    }

    private void RebuildTranscriptPanelsGrid()
    {
        // Remove all children (DocsSplitter and DocsPanel are at the root grid level, not in TranscriptPanelsGrid)
        TranscriptPanelsGrid.Children.Clear();
        TranscriptPanelsGrid.ColumnDefinitions.Clear();

        if (_secondaryTranscripts.Count == 0)
        {
            // Only main panel — always give it the full column.
            TranscriptPanelsGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetColumn(MainTranscriptBorder, 0);
            TranscriptPanelsGrid.Children.Add(MainTranscriptBorder);

            return;
        }

        // Build the ordered list of panels to display.
        // Main is included only when _mainTranscriptVisible; its Visibility property
        // was already set by ShowMainTranscript / HideMainTranscript — do NOT touch it here.
        var panels = new List<UIElement>();
        if (_mainTranscriptVisible)
            panels.Add(MainTranscriptBorder);
        foreach (var entry in _secondaryTranscripts)
            panels.Add(entry.PanelBorder);

        // Column layout: panel(1*), splitter(8px), panel(1*), splitter(8px), ...
        for (int i = 0; i < panels.Count; i++)
        {
            TranscriptPanelsGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
                MinWidth = 0
            });
            if (i < panels.Count - 1)
                TranscriptPanelsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        }

        for (int i = 0; i < panels.Count; i++)
        {
            Grid.SetColumn(panels[i], i * 2);
            TranscriptPanelsGrid.Children.Add(panels[i]);
        }

        for (int i = 0; i < panels.Count - 1; i++)
        {
            var splitter = new GridSplitter
            {
                Width = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ResizeDirection = GridResizeDirection.Columns
            };
            splitter.DragCompleted += (_, _) => ClampTranscriptColumnWidths();
            Grid.SetColumn(splitter, i * 2 + 1);
            TranscriptPanelsGrid.Children.Add(splitter);
        }
    }

    private void RefreshSecondaryTranscriptEntries()
    {
        var seenThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _secondaryTranscripts.ToList())
        {
            if (!seenThreadIds.Add(entry.Thread.ThreadId))
            {
                SquadDashTrace.Write(TraceCategory.TranscriptPanels,
                    $"RefreshSecondaryTranscriptEntries closing duplicate panel thread={entry.Thread.ThreadId} title=\"{entry.TitleBlock.Text}\"");
                CloseSecondaryPanel(entry);
                continue;
            }

            var currentCard = FindAgentCardForThread(entry.Thread);
            if (currentCard is null)
            {
                SquadDashTrace.Write(TraceCategory.TranscriptPanels,
                    $"RefreshSecondaryTranscriptEntries closing stale panel thread={entry.Thread.ThreadId} title=\"{entry.TitleBlock.Text}\" reason=no-card");
                CloseSecondaryPanel(entry);
                continue;
            }

            var hasAlternateMeaningfulThread = currentCard.Threads.Any(thread =>
                !ReferenceEquals(thread, entry.Thread) &&
                AgentThreadRegistry.HasMeaningfulThreadTranscript(thread));
            if (!AgentThreadRegistry.HasMeaningfulThreadTranscript(entry.Thread) && hasAlternateMeaningfulThread)
            {
                SquadDashTrace.Write(TraceCategory.TranscriptPanels,
                    $"RefreshSecondaryTranscriptEntries closing empty placeholder panel thread={entry.Thread.ThreadId} agent={currentCard.Name} reason=alternate-meaningful-thread");
                CloseSecondaryPanel(entry);
                continue;
            }

            if (!ReferenceEquals(entry.Agent, currentCard))
            {
                SquadDashTrace.Write(TraceCategory.TranscriptPanels,
                    $"RefreshSecondaryTranscriptEntries remapped panel thread={entry.Thread.ThreadId} oldAgent={entry.Agent.Name} newAgent={currentCard.Name}");
                entry.Agent = currentCard;
            }

            var newTitle = BuildSecondaryTranscriptTitle(currentCard, entry.Thread);
            if (!string.Equals(entry.TitleBlock.Text, newTitle, StringComparison.Ordinal))
            {
                SquadDashTrace.Write(TraceCategory.TranscriptPanels,
                    $"RefreshSecondaryTranscriptEntries retitled thread={entry.Thread.ThreadId} old=\"{entry.TitleBlock.Text}\" new=\"{newTitle}\"");
                entry.TitleBlock.Text = newTitle;
            }

            entry.Thread.IsSecondaryPanelOpen = true;
        }
    }

    private void SyncSelectionControllerWithUiState(string reason)
    {
        _selectionController.ReconcilePanels(
            _secondaryTranscripts.Select(entry => (entry.Agent, entry.Thread)),
            _mainTranscriptVisible);

        var visibleThread = _mainTranscriptVisible
            ? (_selectedTranscriptThread?.ThreadId ?? CoordinatorThread.ThreadId)
            : "(hidden)";
        var panels = _secondaryTranscripts.Count == 0
            ? "(none)"
            : string.Join(", ", _secondaryTranscripts.Select(entry =>
                $"{entry.Agent.Name}:{entry.Thread.ThreadId}:seq{entry.Thread.SequenceNumber}:auto={entry.IsAutoOpenedInMultiMode}"));
        SquadDashTrace.Write(TraceCategory.TranscriptPanels,
            $"SyncSelectionControllerWithUiState reason={reason} mainVisible={_mainTranscriptVisible} selectedMain={visibleThread} panels={panels}");
    }

    private void ClampTranscriptColumnWidths()
    {
        double totalWidth = TranscriptPanelsGrid.ActualWidth;
        double minWidth = totalWidth / 7.0;
        for (int i = 0; i < TranscriptPanelsGrid.ColumnDefinitions.Count; i += 2)
        {
            var col = TranscriptPanelsGrid.ColumnDefinitions[i];
            if (col.ActualWidth < minWidth)
                col.Width = new GridLength(minWidth);
        }
    }

    private SecondaryTranscriptEntry CreateSecondaryTranscriptPanel(AgentStatusCard agent, TranscriptThreadState thread)
    {
        var titleText = BuildSecondaryTranscriptTitle(agent, thread);
        var baseDisplayName = AbbreviateAgentName(
            AgentThreadRegistry.ResolveSecondaryTranscriptDisplayName(thread, agent.Name));
        var titleBlock = new TextBlock
        {
            Text = titleText,
            ToolTip = BuildGpaTooltip(baseDisplayName),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        var navUp = CreateSecondaryNavButton(up: true);
        var navDown = CreateSecondaryNavButton(up: false);

        var closeBtn = new Button
        {
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Close panel"
        };
        closeBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        var closeViewbox = new Viewbox { Width = 10, Height = 10 };
        var closePath = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 1,1 L 9,9 M 9,1 L 1,9"),
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        closePath.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "LabelText");
        closeViewbox.Child = closePath;
        closeBtn.Content = closeViewbox;

        var rtb = new RichTextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsDocumentEnabled = true,
            IsReadOnly = true,
            IsUndoEnabled = false,
            FontSize = _transcriptFontSize,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        rtb.SetResourceReference(RichTextBox.ForegroundProperty, "LabelText");

        var scrollToBottomChevron = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 1,3 L 9,11 L 17,3"),
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
        };
        scrollToBottomChevron.SetBinding(
            System.Windows.Shapes.Shape.StrokeProperty,
            new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1)
            });
        var scrollToBottomViewbox = new Viewbox
        {
            Width = 16,
            Height = 13,
            Stretch = Stretch.Uniform,
            Child = scrollToBottomChevron
        };

        var scrollToBottomOverlay = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 24),
            Width = 36,
            Height = 36,
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            Content = scrollToBottomViewbox,
        };
        Panel.SetZIndex(scrollToBottomOverlay, 10);
        scrollToBottomOverlay.SetResourceReference(Control.StyleProperty, "ScrollToBottomButtonStyle");

        var scrollController = new TranscriptScrollController(rtb, Dispatcher);
        scrollController.SetScrollToBottomButton(scrollToBottomOverlay);
        scrollToBottomOverlay.Click += (_, _) =>
        {
            try { scrollController.ScrollToBottom(); }
            catch (Exception ex) { HandleUiCallbackException("SecondaryPanel.ScrollToBottom", ex); }
        };

        var headerDock = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 12) };
        var rightStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        rightStack.Children.Add(navUp);
        rightStack.Children.Add(navDown);
        rightStack.Children.Add(closeBtn);
        DockPanel.SetDock(rightStack, Dock.Right);
        headerDock.Children.Add(rightStack);
        headerDock.Children.Add(titleBlock);

        var contentGrid = new Grid();
        contentGrid.Children.Add(rtb);
        contentGrid.Children.Add(scrollToBottomOverlay);

        var outerDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(headerDock, Dock.Top);
        outerDock.Children.Add(headerDock);
        outerDock.Children.Add(contentGrid);

        var outerBorder = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0)
        };
        outerBorder.SetResourceReference(Border.BackgroundProperty, "TranscriptSurface");
        outerBorder.SetResourceReference(Border.BorderBrushProperty, "TranscriptBorder");
        outerBorder.Child = outerDock;

        var entry = new SecondaryTranscriptEntry
        {
            Agent = agent,
            Thread = thread,
            TranscriptBox = rtb,
            TitleBlock = titleBlock,
            NavUpButton = navUp,
            NavDownButton = navDown,
            CloseButton = closeBtn,
            PanelBorder = outerBorder,
            ContentGrid = contentGrid,
            ScrollController = scrollController
        };

        // Postpone auto-close on mouse move/scroll; permanently cancel on click
        outerBorder.MouseMove += (_, _) => { try { if (entry.CountdownStarted && !entry.CountdownCancelled) PostponeAutoCloseCountdown(entry); } catch { } };
        outerBorder.PreviewMouseDown += (_, _) => { try { CancelAutoCloseCountdown(entry); } catch { } };
        outerBorder.PreviewMouseWheel += (_, _) => { try { if (entry.CountdownStarted && !entry.CountdownCancelled) PostponeAutoCloseCountdown(entry); } catch { } };

        closeBtn.Click += (_, _) => { try { CloseSecondaryPanel(entry); } catch (Exception ex) { HandleUiCallbackException("SecondaryPanel.Close", ex); } };

        rtb.PreviewMouseWheel += (_, e) =>
        {
            try
            {
                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
                _transcriptFontSize = Math.Clamp(
                    _transcriptFontSize + (e.Delta > 0 ? TranscriptFontSizeStep : -TranscriptFontSizeStep),
                    TranscriptFontSizeMin,
                    TranscriptFontSizeMax);
                ApplyTranscriptFontSize();
                _settingsSnapshot = _settingsStore.SaveTranscriptFontSize(_transcriptFontSize);
                e.Handled = true;
            }
            catch (Exception ex) { HandleUiCallbackException("SecondaryPanel.PreviewMouseWheel", ex); }
        };

        if (thread.Document.Parent is RichTextBox currentOwner && currentOwner != rtb)
            currentOwner.Document = new FlowDocument();
        rtb.Document = thread.Document;
        if (_conversationManager.HasPendingRender(thread))
            _ = _conversationManager.EnsureAgentThreadRenderedAsync(thread);

        return entry;
    }

    private static Button CreateSecondaryNavButton(bool up)
    {
        var btn = new Button
        {
            Width = 26,
            Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, up ? 4 : 0, 0),
            IsEnabled = false
        };
        btn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        btn.ToolTip = up ? "Previous prompt" : "Next prompt";
        var pathData = up ? "M 1,8 L 5,2 L 9,8" : "M 1,2 L 5,8 L 9,2";
        var vb = new Viewbox { Width = 12, Height = 10, Stretch = Stretch.Uniform, Opacity = 0.8 };
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(pathData),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent
        };
        path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "LabelText");
        vb.Child = path;
        btn.Content = vb;
        return btn;
    }

    private void LoadAgentTranscriptIntoBox(AgentStatusCard agent, RichTextBox rtb)
    {
        var thread = GetTranscriptThreadForAgent(agent);

        var doc = thread.Document;

        // FlowDocument can only belong to one RichTextBox at a time.  If it is
        // already parented to a different RTB (safety net — Case 1 in OpenSecondaryPanel
        // should have caught the OutputTextBox conflict before we get here), detach it
        // from the current owner by giving that owner a temporary empty document.
        if (doc.Parent is RichTextBox currentOwner && currentOwner != rtb)
        {
            currentOwner.Document = new FlowDocument();
        }

        rtb.Document = doc;
        if (_conversationManager.HasPendingRender(thread))
            _ = _conversationManager.EnsureAgentThreadRenderedAsync(thread);
    }

    private void EnsureTranscriptTitleRefreshTimerRunning()
    {
        if (_transcriptTitleRefreshTimer is null)
        {
            _transcriptTitleRefreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(60), DispatcherPriority.Background,
                (_, _) =>
                {
                    foreach (var e in _secondaryTranscripts.ToList())
                        RefreshSecondaryTranscriptTitle(e.Thread);
                },
                Dispatcher);
        }
        if (!_transcriptTitleRefreshTimer.IsEnabled)
            _transcriptTitleRefreshTimer.Start();
    }

    private string AbbreviateAgentName(string name) =>
        name.Replace("General Purpose Agent", "GPA", StringComparison.OrdinalIgnoreCase)
            .Replace("general purpose agent", "GPA");

    private static string? BuildGpaTooltip(string displayName) =>
        displayName.Contains("GPA", StringComparison.Ordinal)
            ? displayName.Replace("GPA", "General Purpose Agent", StringComparison.Ordinal)
            : null;

    private string BuildSecondaryTranscriptTitle(AgentStatusCard agent, TranscriptThreadState thread)
    {
        var displayName = AbbreviateAgentName(
            AgentThreadRegistry.ResolveSecondaryTranscriptDisplayName(thread, agent.Name));
        return thread.PromptParagraphs.Count > 0
            ? $"{displayName} - {FormatRelativeTime(thread.PromptParagraphs[0].Timestamp)}"
            : displayName;
    }

    private void RefreshSecondaryTranscriptTitle(TranscriptThreadState thread)
    {
        foreach (var entry in _secondaryTranscripts.Where(entry => ReferenceEquals(entry.Thread, thread)))
        {
            var title = BuildSecondaryTranscriptTitle(entry.Agent, thread);
            if (!string.Equals(entry.TitleBlock.Text, title, StringComparison.Ordinal))
            {
                SquadDashTrace.Write(TraceCategory.TranscriptPanels,
                    $"RefreshSecondaryTranscriptTitle thread={thread.ThreadId} old=\"{entry.TitleBlock.Text}\" new=\"{title}\"");
                entry.TitleBlock.Text = title;
                var baseDisplayName = AbbreviateAgentName(
                    AgentThreadRegistry.ResolveSecondaryTranscriptDisplayName(thread, entry.Agent.Name));
                entry.TitleBlock.ToolTip = BuildGpaTooltip(baseDisplayName);
            }
        }
    }

    private TranscriptThreadState GetTranscriptThreadForAgent(AgentStatusCard agent) =>
        agent.IsLeadAgent
            ? CoordinatorThread
            : _agentThreadRegistry.GetOrCreateAgentDisplayThread(agent);

    private void SyncTranscriptTargetIndicators()
    {
        var visibleMainThread = _mainTranscriptVisible
            ? _selectedTranscriptThread ?? CoordinatorThread
            : null;

        foreach (var card in _agents)
        {
            if (card.IsLeadAgent)
            {
                card.IsTranscriptTargetSelected = visibleMainThread is not null &&
                                                  ReferenceEquals(visibleMainThread, CoordinatorThread);
                continue;
            }

            card.IsTranscriptTargetSelected =
                card.Threads.Any(thread => ReferenceEquals(thread, visibleMainThread)) ||
                _secondaryTranscripts.Any(entry =>
                    ReferenceEquals(entry.Thread, visibleMainThread) ||
                    ReferenceEquals(entry.Agent, card) ||
                    ReferenceEquals(FindAgentCardForThread(entry.Thread), card));
        }
    }

    private void CancelAutoCloseCountdown(SecondaryTranscriptEntry entry)
    {
        entry.CountdownTimer?.Stop();
        entry.CountdownTimer = null;
        entry.CountdownSecondsRemaining = 0;
        entry.PostponeTimer?.Stop();
        entry.PostponeTimer = null;
        entry.CountdownCancelled = true;
        if (entry.CountdownOverlay is not null)
        {
            entry.ContentGrid.Children.Remove(entry.CountdownOverlay);
            entry.CountdownOverlay = null;
        }
    }

    private void PostponeAutoCloseCountdown(SecondaryTranscriptEntry entry)
    {
        // If permanently cancelled by a click, do nothing
        if (entry.CountdownCancelled)
            return;

        // Stop the 10-second countdown if running, remove overlay
        if (entry.CountdownTimer is not null)
        {
            entry.CountdownTimer.Stop();
            entry.CountdownTimer = null;
            entry.CountdownSecondsRemaining = 0;
        }
        if (entry.CountdownOverlay is not null)
        {
            entry.ContentGrid.Children.Remove(entry.CountdownOverlay);
            entry.CountdownOverlay = null;
        }

        // Reset (or start) the 2-minute postpone timer
        entry.PostponeTimer?.Stop();
        entry.PostponeTimer = null;

        var postponeTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        postponeTimer.Tick += (_, _) =>
        {
            postponeTimer.Stop();
            entry.PostponeTimer = null;
            // 2-minute postpone expired — restart the 10-second countdown
            StartAutoCloseCountdown(entry);
        };
        entry.PostponeTimer = postponeTimer;
        postponeTimer.Start();
    }

    private void StartAutoCloseCountdown(SecondaryTranscriptEntry entry)
    {
        entry.CountdownStarted = true;
        if (entry.CountdownCancelled)
            return;

        entry.CountdownSecondsRemaining = 10;

        var overlay = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 13,
            IsHitTestVisible = false
        };
        overlay.SetValue(Panel.ZIndexProperty, 20);
        overlay.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        overlay.Text = $"Closing this transcript in {entry.CountdownSecondsRemaining} seconds";
        entry.CountdownOverlay = overlay;
        entry.ContentGrid.Children.Add(overlay);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            entry.CountdownSecondsRemaining--;
            if (entry.CountdownSecondsRemaining <= 0)
            {
                timer.Stop();
                CloseSecondaryPanel(entry);
            }
            else if (entry.CountdownOverlay is not null)
            {
                entry.CountdownOverlay.Text = entry.CountdownSecondsRemaining == 1
                    ? "Closing this transcript in 1 second"
                    : $"Closing this transcript in {entry.CountdownSecondsRemaining} seconds";
            }
        };
        entry.CountdownTimer = timer;
        timer.Start();
    }

    private void FlashGlowHighlight(Border targetBorder, System.Windows.Media.Color accentColor)
    {
        var glow = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = accentColor,
            BlurRadius = 0,
            ShadowDepth = 0,
            Opacity = 0
        };
        targetBorder.Effect = glow;

        var blurAnim = new System.Windows.Media.Animation.DoubleAnimation(0, 24, TimeSpan.FromMilliseconds(200))
        {
            AutoReverse = true,
            RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2)
        };
        var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        {
            AutoReverse = true,
            RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2)
        };

        blurAnim.Completed += (_, _) => targetBorder.Effect = null;

        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, blurAnim);
        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, opacityAnim);
    }

    private static System.Windows.Media.Color ColorFromHex(string hex)
    {
        try { return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex); }
        catch { return System.Windows.Media.Colors.CornflowerBlue; }
    }

    private void OpenTranscriptThread(string target, bool scrollToStart)
    {
        if (string.Equals(target, "coordinator", StringComparison.OrdinalIgnoreCase))
        {
            if (!_mainTranscriptVisible)
                ShowMainTranscript();
            SelectTranscriptThread(CoordinatorThread, scrollToStart);
            return;
        }

        var thread = _agentThreadRegistry.ThreadOrder.FirstOrDefault(candidate =>
            string.Equals(candidate.ThreadId, target, StringComparison.OrdinalIgnoreCase));
        if (thread is null)
            return;

        var card = FindAgentCardForThread(thread);
        if (card is null)
            return;

        OpenSecondaryPanel(card, thread, isAutoOpenedInMultiMode: false);
    }

    internal static string SanitizeResponseText(string? text) =>
        ToolTranscriptFormatter.StripSystemNotifications(text).TrimEnd();

    internal static string? SanitizeResponseTextOrNull(string? text)
    {
        var sanitized = SanitizeResponseText(text);
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    internal static string GetSanitizedTurnResponseText(TranscriptTurnView? turn) =>
        SanitizeResponseText(turn?.ResponseTextBuilder.ToString());

    private static string? GetSanitizedTurnResponseTextOrNull(TranscriptTurnView? turn) =>
        SanitizeResponseTextOrNull(turn?.ResponseTextBuilder.ToString());

    internal static string BuildThreadPreview(string text)
    {
        var collapsed = CollapseWhitespace(RemoveQuickReplySuffix(SanitizeResponseText(text)));
        if (collapsed.Length <= 120)
            return collapsed;

        return collapsed[..117] + "...";
    }

    private static string RemoveQuickReplySuffix(string text)
    {
        return TryExtractQuickReplyOptions(text, out var body, out _) ? body : text;
    }

    private static bool TryExtractQuickReplyOptions(
        string text,
        out string body,
        out string[] options)
    {
        return QuickReplyOptionParser.TryExtract(text, out body, out options);
    }

    private static bool TryExtractQuickReplyOptionMetadata(
        string text,
        out string body,
        out QuickReplyOptionMetadata[] options)
    {
        return QuickReplyOptionParser.TryExtractWithMetadata(text, out body, out options);
    }

    private void AppendResponseSegment(string text, bool startOnNewLine = false) =>
        AppendResponseSegment(CoordinatorThread, text, startOnNewLine);

    private void AppendResponseSegment(
        TranscriptThreadState thread,
        string text,
        bool startOnNewLine = false)
    {
        if (thread.CurrentTurn is null || string.IsNullOrEmpty(text))
            return;

        var entry = GetOrCreateResponseEntry(thread.CurrentTurn);
        if (startOnNewLine && entry.RawTextBuilder.Length > 0)
            entry.RawTextBuilder.Append('\n');

        entry.RawTextBuilder.Append(text);
        QueueResponseEntryRender(
            entry,
            flushImmediately: startOnNewLine || ShouldRenderResponseEntryImmediately(entry, text));
    }

    private TranscriptTurnView BeginTranscriptTurn(string prompt) =>
        BeginTranscriptTurn(CoordinatorThread, prompt);

    private TranscriptTurnView BeginTranscriptTurn(TranscriptThreadState thread, string? prompt)
    {
        prompt ??= string.Empty;
        thread.CurrentTurn = CreateTranscriptTurnView(thread, prompt, DateTimeOffset.Now, thinkingExpanded: true);
        thread.ResponseStreamed = false;

        // Prompt submission always forces the viewport to the bottom so the user can
        // see what they just typed — even if they were scrolled away reading earlier
        // content.  Only applies when there is actual prompt text and the thread is
        // the one currently displayed; otherwise fall back to the normal gated scroll.
        if (!string.IsNullOrEmpty(prompt) && ReferenceEquals(_selectedTranscriptThread ?? CoordinatorThread, thread))
        {
            EnsureThreadFooterAtEnd(thread);
            _scrollController.ForceScrollToBottom();
        }
        else
        {
            ScrollToEndIfAtBottom(thread);
        }

        return thread.CurrentTurn;
    }

    private TranscriptTurnView CreateTranscriptTurnView(
        TranscriptThreadState thread,
        string prompt,
        DateTimeOffset startedAt,
        bool thinkingExpanded)
    {
        var topLevelBlocks = new List<Block>();
        var separatorParagraph = CreateTranscriptParagraph(bottomMargin: 2);
        var separatorRun = new Run(ToolTranscriptFormatter.BuildPromptSeparator());
        separatorRun.SetResourceReference(TextElement.ForegroundProperty, "TurnDividerText");
        separatorParagraph.Inlines.Add(separatorRun);
        thread.Document.Blocks.Add(separatorParagraph);
        topLevelBlocks.Add(separatorParagraph);

        if (thread.Kind == TranscriptThreadKind.Agent)
        {
            var startMarkerParagraph = CreateTranscriptParagraph(bottomMargin: 4);
            var startMarkerRun = new Run(ToolTranscriptFormatter.BuildAgentTurnStartMarker(prompt, startedAt))
            {
                FontWeight = FontWeights.SemiBold
            };
            startMarkerRun.SetResourceReference(TextElement.ForegroundProperty, "AgentTaskStartText");
            startMarkerParagraph.Inlines.Add(startMarkerRun);
            thread.Document.Blocks.Add(startMarkerParagraph);
            topLevelBlocks.Add(startMarkerParagraph);
        }

        var promptParagraph = CreateTranscriptParagraph(bottomMargin: 8);
        const string voiceAnnotation = "\n(some or all of this prompt was dictated by voice)";
        var promptBody = prompt.EndsWith(voiceAnnotation, StringComparison.Ordinal)
            ? prompt[..^voiceAnnotation.Length]
            : prompt;

        if (!string.IsNullOrEmpty(promptBody))
        {
            if (thread.Kind == TranscriptThreadKind.Coordinator && !string.IsNullOrWhiteSpace(_settingsSnapshot.UserName))
            {
                var prefixRun = new Run($"{_settingsSnapshot.UserName}: ") { FontWeight = FontWeights.SemiBold };
                prefixRun.SetResourceReference(TextElement.ForegroundProperty, "UserPromptPrefixText");
                promptParagraph.Inlines.Add(prefixRun);
                var bodyRun = new Run(promptBody) { FontWeight = FontWeights.SemiBold };
                bodyRun.SetResourceReference(TextElement.ForegroundProperty, "UserPromptText");
                promptParagraph.Inlines.Add(bodyRun);
            }
            else
            {
                var prefix = thread.Kind == TranscriptThreadKind.Coordinator ? null : $"{thread.Title}: ";
                if (prefix is not null)
                {
                    var prefixRun = new Run(prefix) { FontWeight = FontWeights.SemiBold };
                    prefixRun.SetResourceReference(TextElement.ForegroundProperty, "UserPromptPrefixText");
                    promptParagraph.Inlines.Add(prefixRun);
                }
                var bodyRun = new Run(promptBody) { FontWeight = FontWeights.SemiBold };
                bodyRun.SetResourceReference(TextElement.ForegroundProperty, "UserPromptText");
                promptParagraph.Inlines.Add(bodyRun);
            }
            if (promptBody != prompt)
            {
                var voiceRun = new Run("\n(some or all of this prompt was dictated by voice)")
                {
                    FontWeight = FontWeights.Normal
                };
                voiceRun.SetResourceReference(TextElement.ForegroundProperty, "VoiceAnnotationText");
                promptParagraph.Inlines.Add(voiceRun);
            }
            thread.Document.Blocks.Add(promptParagraph);
            topLevelBlocks.Add(promptParagraph);
            thread.PromptParagraphs.Add(new PromptEntry(promptParagraph, startedAt));
            RefreshSecondaryTranscriptTitle(thread);
            if (ReferenceEquals(_selectedTranscriptThread ?? CoordinatorThread, thread))
                SyncPromptNavButtons();
        }

        var narrativeSection = new Section();
        thread.Document.Blocks.Add(narrativeSection);
        topLevelBlocks.Add(narrativeSection);

        var view = new TranscriptTurnView(
            thread,
            prompt,
            startedAt,
            narrativeSection,
            topLevelBlocks);
        return view;
    }

    /// <summary>
    /// Inserts a batch of older coordinator turns at the TOP (beginning) of
    /// <paramref name="thread"/>'s <see cref="FlowDocument"/>, preserving document
    /// order so that the oldest turn in <paramref name="turns"/> ends up first.
    ///
    /// <para>
    /// Because <see cref="RenderPersistedTurn"/> always <em>appends</em> to the
    /// document, we first let it append all batch turns to the end, collect the
    /// newly added blocks, remove them, then insert them before the original first
    /// block in document order.  Inserting before the same anchor in forward order
    /// (a, b, c) correctly produces [a, b, c, anchor, …] because each successive
    /// call to <c>InsertBefore(anchor, x)</c> places <c>x</c> immediately in front
    /// of the (stationary) anchor, accumulating content in the right sequence.
    /// </para>
    /// </summary>
    private void PrependPersistedTurnsBatch(
        TranscriptThreadState thread,
        IReadOnlyList<TranscriptTurnRecord> turns)
    {
        if (turns.Count == 0) return;

        var anchor = thread.Document.Blocks.FirstBlock;

        if (anchor is null)
        {
            // Document is empty — just render normally.
            for (var i = 0; i < turns.Count; i++)
                RenderPersistedTurn(thread, turns[i], i == turns.Count - 1);
            return;
        }

        // Snapshot how many blocks exist before the batch render.
        var blocksBefore = thread.Document.Blocks.Count;

        // Render each turn — they append to the END of the document.
        for (var i = 0; i < turns.Count; i++)
            RenderPersistedTurn(thread, turns[i], isLastTurn: false);

        // Collect only the newly appended blocks (everything after blocksBefore).
        var newBlocks = thread.Document.Blocks.Skip(blocksBefore).ToList();
        if (newBlocks.Count == 0) return;

        // Detach them from the end of the document.
        foreach (var b in newBlocks)
            thread.Document.Blocks.Remove(b);

        // Re-insert before the original first block in forward order.
        // InsertBefore(anchor, x) always places x immediately before anchor,
        // so inserting a, b, c in sequence gives [a, b, c, anchor, …].
        foreach (var b in newBlocks)
            thread.Document.Blocks.InsertBefore(anchor, b);
    }

    private void RenderPersistedTurn(TranscriptThreadState thread, TranscriptTurnRecord turn, bool isLastTurn = false)
    {
        var view = CreateTranscriptTurnView(
            thread,
            turn.Prompt,
            turn.StartedAt,
            thinkingExpanded: !turn.ThinkingCollapsed);
        if (!RenderStructuredPersistedNarrative(view, turn, isLastTurn))
            RenderLegacyPersistedNarrative(view, turn, isLastTurn);

        view.ResponseTextBuilder.Clear();
        view.ResponseTextBuilder.Append(turn.ResponseText);
        foreach (var block in view.ThinkingBlocks)
            block.Expander.IsExpanded = !turn.ThinkingCollapsed;

        if (view.ThoughtBlocks.Count > 0 && turn.CompletedAt is not null)
        {
            foreach (var block in view.ThoughtBlocks)
            {
                SetCollapsedBlockHeader(block.HeaderTextBlock, "Thinking...");
                block.Expander.IsExpanded = false;
            }
        }
        else
        {
            foreach (var block in view.ThoughtBlocks)
                block.Expander.IsExpanded = false;
        }
    }

    private bool RenderStructuredPersistedNarrative(TranscriptTurnView view, TranscriptTurnRecord turn, bool isLastTurn = false)
    {
        var thoughts = turn.GetThoughts().ToArray();
        var responseSegments = turn.GetResponseSegments().ToArray();
        if (thoughts.Length == 0 && turn.Tools.Count == 0 && responseSegments.Length == 0)
            return true;

        if (thoughts.Any(thought => !thought.Sequence.HasValue) ||
            turn.Tools.Any(tool => !tool.ThinkingBlockSequence.HasValue) ||
            responseSegments.Any(segment => !segment.Sequence.HasValue))
        {
            return false;
        }

        var toolGroups = turn.Tools
            .GroupBy(tool => tool.ThinkingBlockSequence!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(tool => tool.StartedAt).ToArray());

        var sortedGroupKeys = toolGroups.Keys.OrderBy(k => k).ToArray();

        // Pre-compute per-group durations. When tool FinishedAt timestamps are
        // unreliable (same as StartedAt), fall back to the next group's earliest
        // StartedAt as the ceiling — captures actual inter-group wall-clock time.
        var groupDurations = new Dictionary<int, TimeSpan?>();
        for (int i = 0; i < sortedGroupKeys.Length; i++)
        {
            var key = sortedGroupKeys[i];
            var tools = toolGroups[key];
            var groupStart = tools.Min(t => t.StartedAt);
            var groupEnd = tools.Max(t => t.FinishedAt ?? t.StartedAt);

            if (groupEnd <= groupStart && i + 1 < sortedGroupKeys.Length)
                groupEnd = toolGroups[sortedGroupKeys[i + 1]].Min(t => t.StartedAt);

            groupDurations[key] = groupEnd > groupStart ? groupEnd - groupStart : null;
        }

        var lastResponseSequence = responseSegments.Length > 0
            ? responseSegments.Max(s => s.Sequence!.Value)
            : -1;

        var items = new List<(int Sequence, int SortOrder, Action Render)>();
        items.AddRange(thoughts.Select(thought => (
            thought.Sequence!.Value,
            0,
            (Action)(() => RenderPersistedThought(view, thought)))));
        items.AddRange(toolGroups.Select(group => (
            group.Key,
            1,
            (Action)(() =>
            {
                var collapsed = turn.ThinkingCollapsed;
                var block = CreateThinkingBlock(view, sequence: group.Key, isExpanded: !collapsed);
                foreach (var tool in group.Value)
                    RenderPersistedTool(block, tool);
                if (collapsed && groupDurations.TryGetValue(group.Key, out var duration) && duration.HasValue)
                    SetCollapsedBlockHeader(block.HeaderTextBlock, "Tooling...",
                        StatusTimingPresentation.FormatDuration(duration.Value));
            }))));
        items.AddRange(responseSegments.Select(segment => (
            segment.Sequence!.Value,
            2,
            (Action)(() => RenderPersistedResponse(view, segment,
                allowQuickReplies: isLastTurn && segment.Sequence!.Value == lastResponseSequence)))));

        foreach (var item in items.OrderBy(entry => entry.Sequence).ThenBy(entry => entry.SortOrder))
            item.Render();

        return true;
    }

    private void RenderLegacyPersistedNarrative(TranscriptTurnView view, TranscriptTurnRecord turn, bool isLastTurn = false)
    {
        foreach (var thought in turn.GetThoughts().Where(entry => entry.Placement == TranscriptThoughtPlacement.BeforeTools))
            RenderPersistedThought(view, thought);

        if (turn.Tools.Count > 0)
        {
            var block = CreateThinkingBlock(view, isExpanded: !turn.ThinkingCollapsed);
            var orderedTools = turn.Tools.OrderBy(t => t.StartedAt).ToArray();
            foreach (var tool in orderedTools)
                RenderPersistedTool(block, tool);
            if (turn.ThinkingCollapsed)
            {
                var toolStart = orderedTools.First().StartedAt;
                var toolEnd = orderedTools.Max(t => t.FinishedAt ?? t.StartedAt);
                // When timestamps collapse (all same), span across tool StartedAt values
                if (toolEnd <= toolStart)
                    toolEnd = orderedTools.Last().StartedAt;
                if (toolEnd > toolStart)
                    SetCollapsedBlockHeader(block.HeaderTextBlock, "Tooling...",
                        StatusTimingPresentation.FormatDuration(toolEnd - toolStart));
            }
        }

        foreach (var thought in turn.GetThoughts().Where(entry => entry.Placement == TranscriptThoughtPlacement.AfterTools))
            RenderPersistedThought(view, thought);

        if (!string.IsNullOrWhiteSpace(turn.ResponseText))
            RenderPersistedResponse(view, new TranscriptResponseSegmentRecord(turn.ResponseText)
            {
                Sequence = AllocateNarrativeSequence(view)
            }, allowQuickReplies: isLastTurn);
    }

    private void FinalizeCurrentTurnResponse() =>
        FinalizeCurrentTurnResponse(CoordinatorThread);

    private void FinalizeCurrentTurnResponse(TranscriptThreadState thread)
    {
        if (thread.CurrentTurn is null)
            return;

        foreach (var entry in thread.CurrentTurn.ResponseEntries)
            FlushResponseEntryRender(entry, force: true);
    }

    private bool ShouldRenderResponseEntryImmediately(TranscriptResponseEntry entry, string text)
    {
        if (entry.LastRenderedAt is null)
            return true;

        if (text.IndexOfAny(['\r', '\n']) >= 0 ||
            text.Contains("```", StringComparison.Ordinal))
            return true;

        return DateTimeOffset.Now - entry.LastRenderedAt.Value >= ResponseRenderCadence;
    }

    private void QueueResponseEntryRender(TranscriptResponseEntry entry, bool flushImmediately)
    {
        entry.HasPendingRender = true;
        _pendingResponseEntryRenders.Add(entry);

        if (flushImmediately)
        {
            FlushResponseEntryRender(entry, force: true);
            return;
        }

        if (!_responseRenderTimer.IsEnabled)
            _responseRenderTimer.Start();
    }

    private void FlushPendingResponseEntryRenders()
    {
        if (_pendingResponseEntryRenders.Count == 0)
        {
            _responseRenderTimer.Stop();
            return;
        }

        var pendingEntries = _pendingResponseEntryRenders.ToArray();
        foreach (var entry in pendingEntries)
            FlushResponseEntryRender(entry, force: false);

        if (_pendingResponseEntryRenders.Count == 0)
            _responseRenderTimer.Stop();
    }

    private void FlushResponseEntryRender(TranscriptResponseEntry entry, bool force)
    {
        if (!entry.HasPendingRender && !force)
            return;

        _pendingResponseEntryRenders.Remove(entry);
        entry.HasPendingRender = false;
        entry.LastRenderedAt = DateTimeOffset.Now;
        RenderResponseEntry(entry);
    }

    private void RenderResponseEntry(TranscriptResponseEntry entry)
    {
        var sanitizedText = SanitizeResponseText(entry.RawTextBuilder.ToString());
        var newBlocks = BuildResponseBlocks(entry, sanitizedText, entry.AllowQuickReplies).ToList();
        if (newBlocks.Count == 0)
            newBlocks.Add(CreateTranscriptParagraph(bottomMargin: 18));

        // Swap blocks in-place so the section never empties — Blocks.Clear() collapses the
        // document height synchronously, clamping VerticalOffset before the rebuild adds
        // content back. That clamp is what causes the scroll thumb to jump mid-transcript.
        var oldBlocks = entry.Section.Blocks.ToList();
        int shared = Math.Min(oldBlocks.Count, newBlocks.Count);

        for (int i = 0; i < shared; i++)
        {
            entry.Section.Blocks.InsertAfter(oldBlocks[i], newBlocks[i]);
            entry.Section.Blocks.Remove(oldBlocks[i]);
        }
        for (int i = shared; i < newBlocks.Count; i++)
            entry.Section.Blocks.Add(newBlocks[i]);
        for (int i = shared; i < oldBlocks.Count; i++)
            entry.Section.Blocks.Remove(oldBlocks[i]);
    }

    private IEnumerable<Block> BuildResponseBlocks(
        TranscriptResponseEntry entry,
        string responseText,
        bool allowQuickReplies)
    {
        var quickReplyOptions = Array.Empty<QuickReplyOptionMetadata>();
        if (TryExtractQuickReplyOptionMetadata(responseText, out var cleanedResponseText, out var extractedOptions))
        {
            responseText = cleanedResponseText;
            if (allowQuickReplies)
                quickReplyOptions = extractedOptions;
        }

        var normalized = responseText.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var paragraphLines = new List<string>();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();

            // Code fence
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                foreach (var block in BuildParagraphBlocks(paragraphLines))
                    yield return block;
                paragraphLines.Clear();

                index++;
                var codeLines = new List<string>();
                while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    codeLines.Add(lines[index]);
                    index++;
                }

                yield return BuildCodeBlock(string.Join("\n", codeLines));
                continue;
            }

            // Table
            if (_markdownRenderer.TryReadMarkdownTable(lines, index, out var nextIndex, out var tableLines))
            {
                foreach (var block in BuildParagraphBlocks(paragraphLines))
                    yield return block;
                paragraphLines.Clear();
                yield return _markdownRenderer.BuildMarkdownTable(tableLines);
                index = nextIndex;
                continue;
            }

            paragraphLines.Add(line);
        }

        foreach (var block in BuildParagraphBlocks(paragraphLines))
            yield return block;

        if (quickReplyOptions.Length > 0)
        {
            yield return BuildQuickReplyBlock(entry, quickReplyOptions);
            var hintParagraph = CreateTranscriptParagraph(bottomMargin: 6);
            var hintRun = new Run("Press \u201c[\u201d to respond with the keyboard.")
            {
                FontSize = 11
            };
            hintRun.SetResourceReference(TextElement.ForegroundProperty, "KeyboardHintText");
            hintParagraph.Inlines.Add(hintRun);
            yield return hintParagraph;
        }
    }

    private IEnumerable<Paragraph> BuildParagraphBlocks(List<string> lines)
    {
        if (lines.Count == 0)
            yield break;

        var i = 0;
        while (i < lines.Count)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Blank line — flush nothing, just skip
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                i++;
                continue;
            }

            // Heading
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                var level = 0;
                while (level < trimmed.Length && trimmed[level] == '#') level++;
                var headingText = trimmed[level..].TrimStart();
                var p = CreateTranscriptParagraph(bottomMargin: level <= 2 ? 6 : 4);
                p.Inlines.Add(new Run(headingText)
                {
                    FontWeight = FontWeights.Bold,
                    FontSize = level == 1 ? 18 : level == 2 ? 16 : 14
                });
                yield return p;
                i++;
                continue;
            }

            // Blockquote
            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                var quoteLines = new List<string>();
                while (i < lines.Count && lines[i].TrimStart().StartsWith("> ", StringComparison.Ordinal))
                {
                    quoteLines.Add(lines[i].TrimStart()[2..]);
                    i++;
                }
                yield return BuildBlockquote(string.Join("\n", quoteLines));
                continue;
            }

            // Bullet list
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                var bullets = new List<string>();
                while (i < lines.Count)
                {
                    var t = lines[i].TrimStart();
                    if (!t.StartsWith("- ", StringComparison.Ordinal) && !t.StartsWith("* ", StringComparison.Ordinal))
                        break;
                    bullets.Add(t[2..]);
                    i++;
                }
                for (var b = 0; b < bullets.Count; b++)
                    yield return BuildBulletParagraph(bullets[b], isLast: b == bullets.Count - 1);
                continue;
            }

            // Numbered list
            if (Regex.IsMatch(trimmed, @"^\d+\. "))
            {
                var items = new List<(int Num, string Text)>();
                while (i < lines.Count)
                {
                    var t = lines[i].TrimStart();
                    var m = Regex.Match(t, @"^(\d+)\. (.*)");
                    if (!m.Success) break;
                    items.Add((int.Parse(m.Groups[1].Value), m.Groups[2].Value));
                    i++;
                }
                for (var n = 0; n < items.Count; n++)
                    yield return BuildNumberedListParagraph(items[n].Num, items[n].Text, isLast: n == items.Count - 1);
                continue;
            }

            // Plain paragraph — collect until blank line or block-level element
            var paragraphLines = new List<string>();
            while (i < lines.Count)
            {
                var t = lines[i].TrimStart();
                if (string.IsNullOrWhiteSpace(t))
                    break;
                if (t.StartsWith("#", StringComparison.Ordinal) ||
                    t.StartsWith("> ", StringComparison.Ordinal) ||
                    t.StartsWith("- ", StringComparison.Ordinal) ||
                    t.StartsWith("* ", StringComparison.Ordinal) ||
                    t.StartsWith("```", StringComparison.Ordinal))
                    break;
                paragraphLines.Add(lines[i]);
                i++;
            }

            if (paragraphLines.Count > 0)
            {
                foreach (var pl in paragraphLines)
                {
                    var p = CreateTranscriptParagraph(bottomMargin: 4);
                    _markdownRenderer.AppendInlineMarkdown(p.Inlines, pl.TrimStart());
                    yield return p;
                }
            }
        }
    }

    private Paragraph BuildNumberedListParagraph(int number, string text, bool isLast = false)
    {
        var p = new Paragraph
        {
            Margin = new Thickness(16, 1, 0, isLast ? 12 : 1),
            TextIndent = -12
        };
        var markerRun = new Run($"{number}. ");
        markerRun.SetResourceReference(TextElement.ForegroundProperty, "ListMarkerText");
        p.Inlines.Add(markerRun);
        _markdownRenderer.AppendInlineMarkdown(p.Inlines, text);
        return p;
    }

    private Paragraph BuildBulletParagraph(string text, bool isLast = false)
    {
        var p = new Paragraph
        {
            Margin = new Thickness(16, 1, 0, isLast ? 12 : 1),
            TextIndent = -12
        };
        var markerRun = new Run("• ");
        markerRun.SetResourceReference(TextElement.ForegroundProperty, "ListMarkerText");
        p.Inlines.Add(markerRun);
        _markdownRenderer.AppendInlineMarkdown(p.Inlines, text);
        return p;
    }

    private Paragraph BuildBlockquote(string text)
    {
        var p = new Paragraph
        {
            Margin = new Thickness(12, 2, 0, 8),
            Padding = new Thickness(10, 4, 10, 4),
            BorderThickness = new Thickness(3, 0, 0, 0),
        };
        p.SetResourceReference(Block.BorderBrushProperty, "QuoteBorder");
        p.SetResourceReference(Block.BackgroundProperty, "QuoteSurface");
        p.SetResourceReference(TextElement.ForegroundProperty, "BlockquoteBodyText");
        _markdownRenderer.AppendInlineMarkdown(p.Inlines, text);
        return p;
    }

    private Block BuildCodeBlock(string code)
    {
        var textBox = new TextBox
        {
            Text = code,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontFamily = new FontFamily("Consolas"),
            FontSize = _transcriptFontSize * 0.9,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 8, 10, 8),
            Tag = "codeblock"
        };
        textBox.SetResourceReference(Control.BackgroundProperty, "CodeSurface");
        textBox.SetResourceReference(Control.ForegroundProperty, "CodeText");
        return new BlockUIContainer(textBox) { Margin = new Thickness(0, 2, 0, 10) };
    }

    private sealed record QuickReplyButtonPayload(
        TranscriptResponseEntry Entry,
        string Option,
        string? RoutingInstruction,
        string? ContinuationAgentLabel,
        string? RouteMode,
        string? TargetAgentHandle);

    private sealed class PendingQuickReplyLaunchState
    {
        public PendingQuickReplyLaunchState(
            string routeMode,
            string? expectedAgentHandle,
            string? expectedAgentLabel,
            string selectedOption,
            PromptContextDiagnostics contextDiagnostics,
            string contextDiagnosticsSummary,
            string? sessionIdAtClick)
        {
            RouteMode = routeMode;
            ExpectedAgentHandle = expectedAgentHandle;
            ExpectedAgentLabel = expectedAgentLabel;
            SelectedOption = selectedOption;
            ContextDiagnostics = contextDiagnostics;
            ContextDiagnosticsSummary = contextDiagnosticsSummary;
            SessionIdAtClick = sessionIdAtClick;
        }

        public string RouteMode { get; }
        public string? ExpectedAgentHandle { get; }
        public string? ExpectedAgentLabel { get; }
        public string SelectedOption { get; }
        public PromptContextDiagnostics ContextDiagnostics { get; }
        public string ContextDiagnosticsSummary { get; }
        public string? SessionIdAtClick { get; }
        public bool ExpectedAgentStarted { get; set; }
        public bool CoordinatorRespondedBeforeLaunch { get; set; }
    }

    private sealed record DelegationOutcomeTelemetry(
        bool Succeeded,
        string RiskBand,
        int TotalChars,
        int TotalTurns,
        string SelectedOption,
        string? ExpectedAgentHandle);

    private Block BuildQuickReplyBlock(TranscriptResponseEntry entry, IReadOnlyList<QuickReplyOptionMetadata> options)
    {
        _currentQuickReplyOptions = options
            .Select(option => option.Label)
            .ToArray();
        _lastQuickReplyEntry = entry;
        var routeDecisions = options
            .Select(option => (Option: option, Decision: BuildQuickReplyRouting(entry, option)))
            .ToArray();
        var captionText = QuickReplyRoutePresentation.BuildCaption(
            routeDecisions.Select(item => new QuickReplyRoutePresentation.RouteInfo(
                item.Decision.RouteMode,
                item.Decision.ContinuationAgentLabel,
                item.Decision.Reason)).ToArray());
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical
        };

        if (!string.IsNullOrWhiteSpace(captionText))
        {
            var caption = new TextBlock
            {
                Text = captionText,
                Margin = new Thickness(0, 0, 0, 6),
                FontSize = 12
            };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "AgentRoleText");
            stack.Children.Add(caption);
        }

        var panel = new WrapPanel
        {
            Margin = new Thickness(0, 2, 0, 0),
            Orientation = Orientation.Horizontal
        };

        foreach (var routeDecision in routeDecisions)
        {
            var option = routeDecision.Option.Label;
            var routedQuickReply = routeDecision.Decision;
            var button = new Button
            {
                Content = option,
                Tag = new QuickReplyButtonPayload(
                    entry,
                    option,
                    routedQuickReply.RoutingInstruction,
                    routedQuickReply.ContinuationAgentLabel,
                    routedQuickReply.RouteMode,
                    routedQuickReply.TargetAgentHandle),
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(10, 4, 10, 4),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                MinHeight = 28,
                ToolTip = QuickReplyRoutePresentation.BuildButtonToolTip(
                    new QuickReplyRoutePresentation.RouteInfo(
                        routedQuickReply.RouteMode,
                        routedQuickReply.ContinuationAgentLabel,
                        routedQuickReply.Reason))
            };
            if (Application.Current.TryFindResource("QuickReplyButtonStyle") is Style quickReplyStyle)
                button.Style = quickReplyStyle;
            button.SetResourceReference(Control.BackgroundProperty, "QuickReplySurface");
            button.SetResourceReference(Control.ForegroundProperty, "QuickReplyText");
            button.SetResourceReference(Control.BorderBrushProperty, "QuickReplyBorder");
            button.Click += QuickReplyButton_Click;
            panel.Children.Add(button);
        }

        stack.Children.Add(panel);
        var container = new BlockUIContainer(stack) { Margin = new Thickness(0, 2, 0, 10) };
        container.Tag = new QuickReplyCopyData(
            options.Select(o => o.Label).ToArray(),
            captionText);
        return container;
    }

    private void TranscriptHyperlink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Hyperlink { Tag: string target })
                return;

            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                _squadCliAdapter.OpenExternalLink(target);
                return;
            }

            OpenTranscriptThread(target, scrollToStart: true);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(TranscriptHyperlink_Click), ex);
        }
    }

    private void OpenToolTranscriptButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: ToolTranscriptEntry entry } ||
                string.IsNullOrWhiteSpace(entry.TranscriptThreadId))
            {
                return;
            }

            OpenTranscriptThread(entry.TranscriptThreadId, scrollToStart: true);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(OpenToolTranscriptButton_Click), ex);
        }
    }

    private sealed record QuickReplyRoutingDecision(
        string? RoutingInstruction,
        string? ContinuationAgentLabel,
        string? RouteMode,
        string? TargetAgentHandle,
        string? Reason);

    private QuickReplyRoutingDecision BuildQuickReplyRouting(TranscriptResponseEntry entry, QuickReplyOptionMetadata option)
    {
        var trimmedOption = option.Label.Trim();
        if (string.IsNullOrWhiteSpace(trimmedOption))
            return new QuickReplyRoutingDecision(null, null, null, null, null);

        var routeMode = NormalizeQuickReplyRouteMode(option.RouteMode);
        var reason = string.IsNullOrWhiteSpace(option.Reason)
            ? null
            : option.Reason.Trim();

        if (string.Equals(routeMode, "continue_current_agent", StringComparison.OrdinalIgnoreCase))
        {
            var continuationThread = TryResolveQuickReplyContinuationThread(entry);
            var continuationHandle = GetQuickReplyAgentHandle(continuationThread);
            if (continuationThread is null || string.IsNullOrWhiteSpace(continuationHandle))
                return new QuickReplyRoutingDecision(null, null, routeMode, null, reason);

            var continuationLabel = ResolveQuickReplyAgentLabel(continuationThread);
            var continuationInstruction = "Route this quick-reply follow-up to @" + continuationHandle.Trim() +
                                          ". Have that agent continue from their most recent work on this task, follow their charter, and carry out the user's selected next step: " +
                                          trimmedOption;
            return new QuickReplyRoutingDecision(continuationInstruction, continuationLabel, routeMode, continuationHandle, reason);
        }

        if (string.Equals(routeMode, "start_named_agent", StringComparison.OrdinalIgnoreCase))
        {
            var targetHandle = NormalizeQuickReplyAgentHandle(option.TargetAgent);
            if (!string.IsNullOrWhiteSpace(targetHandle))
            {
                var targetLabel = ResolveQuickReplyAgentLabel(targetHandle);
                var targetInstruction = "Route this quick-reply follow-up to @" + targetHandle +
                                        ". This is a new task owned by that specialist according to the quick-reply metadata. Have them follow their charter and carry out the user's selected next step: " +
                                        trimmedOption;
                return new QuickReplyRoutingDecision(targetInstruction, targetLabel, routeMode, targetHandle, reason);
            }
        }

        if (string.Equals(routeMode, "fanout_team", StringComparison.OrdinalIgnoreCase))
        {
            return new QuickReplyRoutingDecision(
                "Treat this quick-reply follow-up as a coordinator-owned multi-agent task. Use `.squad/team.md` and `.squad/routing.md` to delegate the user's selected next step: " + trimmedOption,
                null,
                routeMode,
                null,
                reason);
        }

        if (string.Equals(routeMode, "start_coordinator", StringComparison.OrdinalIgnoreCase))
        {
            return new QuickReplyRoutingDecision(
                "Keep this quick-reply follow-up with the Coordinator. Carry out the user's selected next step directly: " + trimmedOption,
                null,
                routeMode,
                null,
                reason);
        }

        if (string.Equals(routeMode, "done", StringComparison.OrdinalIgnoreCase))
            return new QuickReplyRoutingDecision(null, null, routeMode, null, reason);

        var targetThread = TryResolveQuickReplyContinuationThread(entry);
        var agentHandle = GetQuickReplyAgentHandle(targetThread);
        if (targetThread is null || string.IsNullOrWhiteSpace(agentHandle))
            return new QuickReplyRoutingDecision(null, null, null, null, reason);

        var agentLabel = ResolveQuickReplyAgentLabel(targetThread);
        var routingInstruction = "Route this quick-reply follow-up to @" + agentHandle.Trim() +
                                 ". Have that agent continue from their most recent work on this task, follow their charter, and carry out the user's selected next step: " +
                                 trimmedOption;
        return new QuickReplyRoutingDecision(routingInstruction, agentLabel, null, agentHandle, reason);
    }

    private PendingQuickReplyLaunchState? CreatePendingQuickReplyLaunch(QuickReplyButtonPayload payload)
    {
        if (!QuickReplyAgentLaunchPolicy.RequiresObservedNamedAgentLaunch(payload.RouteMode, payload.TargetAgentHandle))
            return null;

        var contextDiagnostics = _conversationManager.CapturePromptContextDiagnostics();
        var diagnostics = PromptContextDiagnosticsPresentation.BuildTraceSummary(
            contextDiagnostics,
            DateTimeOffset.UtcNow);
        return new PendingQuickReplyLaunchState(
            payload.RouteMode!.Trim(),
            payload.TargetAgentHandle,
            payload.ContinuationAgentLabel,
            payload.Option.Trim(),
            contextDiagnostics,
            diagnostics,
            _conversationManager.CurrentSessionId);
    }

    private void NotePendingQuickReplyCoordinatorResponse()
    {
        var pendingLaunch = _pendingQuickReplyLaunch;
        if (pendingLaunch is null ||
            pendingLaunch.ExpectedAgentStarted ||
            pendingLaunch.CoordinatorRespondedBeforeLaunch)
        {
            return;
        }

        pendingLaunch.CoordinatorRespondedBeforeLaunch = true;
        SquadDashTrace.Write(
            "Routing",
            $"Named-agent quick reply produced coordinator response text before launch agent={pendingLaunch.ExpectedAgentHandle ?? "(unknown)"} option='{pendingLaunch.SelectedOption}' sessionAtClick={pendingLaunch.SessionIdAtClick ?? "(none)"} {pendingLaunch.ContextDiagnosticsSummary}");
    }

    private void NotePendingQuickReplySubagentStarted(SquadSdkEvent evt)
    {
        var pendingLaunch = _pendingQuickReplyLaunch;
        if (pendingLaunch is null || pendingLaunch.ExpectedAgentStarted)
            return;

        if (!QuickReplyAgentLaunchPolicy.MatchesExpectedAgent(
                pendingLaunch.ExpectedAgentHandle,
                pendingLaunch.ExpectedAgentLabel,
                evt))
        {
            return;
        }

        pendingLaunch.ExpectedAgentStarted = true;
        RecordDelegationOutcome(pendingLaunch, succeeded: true);
        SquadDashTrace.Write(
            "Routing",
            $"Named-agent quick reply observed expected launch agent={pendingLaunch.ExpectedAgentHandle ?? "(unknown)"} option='{pendingLaunch.SelectedOption}' sessionAtClick={pendingLaunch.SessionIdAtClick ?? "(none)"} {pendingLaunch.ContextDiagnosticsSummary}");
    }

    private void MaybeReportPendingQuickReplyLaunchFailure(PendingQuickReplyLaunchState? pendingLaunch)
    {
        if (pendingLaunch is null || pendingLaunch.ExpectedAgentStarted)
            return;

        var message = QuickReplyAgentLaunchPolicy.BuildLaunchFailureMessage(
            pendingLaunch.SelectedOption,
            pendingLaunch.ExpectedAgentLabel,
            pendingLaunch.ExpectedAgentHandle);
        RecordDelegationOutcome(pendingLaunch, succeeded: false);
        SquadDashTrace.Write(
            "Routing",
            $"Named-agent quick reply failed to launch expected agent={pendingLaunch.ExpectedAgentHandle ?? "(unknown)"} option='{pendingLaunch.SelectedOption}' sessionAtClick={pendingLaunch.SessionIdAtClick ?? "(none)"} {pendingLaunch.ContextDiagnosticsSummary} detail=\"{message}\"");
    }

    private void RecordDelegationOutcome(PendingQuickReplyLaunchState pendingLaunch, bool succeeded)
    {
        var riskBand = PromptContextDiagnosticsPresentation.GetRiskBand(
            pendingLaunch.ContextDiagnostics,
            DateTimeOffset.UtcNow);
        var totalTurns = pendingLaunch.ContextDiagnostics.CoordinatorTurnCount +
                         pendingLaunch.ContextDiagnostics.AgentThreadTurnCount;
        _recentDelegationOutcomes.Enqueue(
            new DelegationOutcomeTelemetry(
                succeeded,
                riskBand,
                pendingLaunch.ContextDiagnostics.TotalChars,
                totalTurns,
                pendingLaunch.SelectedOption,
                pendingLaunch.ExpectedAgentHandle));

        while (_recentDelegationOutcomes.Count > DelegationOutcomeRollupWindow)
            _recentDelegationOutcomes.Dequeue();

        SquadDashTrace.Write(
            "Routing",
            BuildDelegationOutcomeRollupTrace(succeeded, riskBand, pendingLaunch.SelectedOption, pendingLaunch.ExpectedAgentHandle));
    }

    private string BuildDelegationOutcomeRollupTrace(
        bool latestSucceeded,
        string latestRiskBand,
        string selectedOption,
        string? expectedAgentHandle)
    {
        var outcomes = _recentDelegationOutcomes.ToArray();
        var failures = outcomes.Count(outcome => !outcome.Succeeded);
        var successes = outcomes.Length - failures;
        var failureRate = outcomes.Length == 0
            ? 0
            : (int)Math.Round(failures * 100d / outcomes.Length, MidpointRounding.AwayFromZero);
        var highRiskFailures = outcomes.Count(outcome => !outcome.Succeeded && string.Equals(outcome.RiskBand, "high", StringComparison.OrdinalIgnoreCase));
        var mediumOrHighFailures = outcomes.Count(outcome =>
            !outcome.Succeeded &&
            !string.Equals(outcome.RiskBand, "low", StringComparison.OrdinalIgnoreCase));
        var clusterHint =
            failures >= 3 && highRiskFailures >= 2
                ? "high-risk-failure-cluster"
                : failures >= 3 && mediumOrHighFailures >= 2
                    ? "possible-session-growth-cluster"
                    : failures >= 2 && successes == 0
                        ? "back-to-back-failures"
                        : "none";

        return
            $"Delegation outcome rollup latest={(latestSucceeded ? "success" : "failure")} latestRisk={latestRiskBand} " +
            $"agent={expectedAgentHandle ?? "(unknown)"} option='{selectedOption}' recent={outcomes.Length} successes={successes} failures={failures} " +
            $"failureRatePct={failureRate} highRiskFailures={highRiskFailures} mediumOrHighFailures={mediumOrHighFailures} clusterHint={clusterHint}";
    }

    private TranscriptThreadState? TryResolveQuickReplyContinuationThread(TranscriptResponseEntry entry)
    {
        var ownerThread = entry.Turn.OwnerThread;
        if (CanRouteQuickReplyToAgent(ownerThread))
            return ownerThread;

        if (ownerThread.Kind != TranscriptThreadKind.Coordinator)
            return null;

        var now = DateTimeOffset.Now;
        var candidates = _agentThreadRegistry.ThreadOrder
            .Where(CanRouteQuickReplyToAgent)
            .Select(thread => new
            {
                Thread = thread,
                ActivityAt = thread.LastObservedActivityAt ?? thread.CompletedAt
            })
            .Where(candidate => candidate.ActivityAt is { } activityAt &&
                                now - activityAt <= MarkdownDocumentRenderer.QuickReplyAgentContinuationWindow)
            .OrderByDescending(candidate => candidate.ActivityAt)
            .ToArray();

        if (candidates.Length != 1)
            return null;

        return candidates[0].Thread;
    }

    private static bool CanRouteQuickReplyToAgent(TranscriptThreadState? thread)
    {
        if (thread is null || thread.Kind != TranscriptThreadKind.Agent || thread.IsPlaceholderThread)
            return false;

        return !string.IsNullOrWhiteSpace(thread.AgentName) ||
               !string.IsNullOrWhiteSpace(thread.AgentId);
    }

    private static string? GetQuickReplyAgentHandle(TranscriptThreadState? thread)
    {
        if (thread is null)
            return null;

        if (!string.IsNullOrWhiteSpace(thread.AgentName))
            return thread.AgentName.Trim();
        if (!string.IsNullOrWhiteSpace(thread.AgentId))
            return thread.AgentId.Trim();

        return null;
    }

    private static string? ResolveQuickReplyAgentLabel(TranscriptThreadState? thread)
    {
        if (thread is null)
            return null;

        if (!string.IsNullOrWhiteSpace(thread.AgentDisplayName))
            return thread.AgentDisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(thread.AgentName))
            return AgentThreadRegistry.HumanizeAgentName(thread.AgentName);
        if (!string.IsNullOrWhiteSpace(thread.AgentId))
            return AgentThreadRegistry.HumanizeAgentName(thread.AgentId);

        return null;
    }

    private string? ResolveQuickReplyAgentLabel(string? handle)
    {
        var normalizedHandle = NormalizeQuickReplyAgentHandle(handle);
        if (string.IsNullOrWhiteSpace(normalizedHandle))
            return null;

        var matchingThread = _agentThreadRegistry.ThreadOrder.FirstOrDefault(thread =>
            string.Equals(NormalizeQuickReplyAgentHandle(thread.AgentName), normalizedHandle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizeQuickReplyAgentHandle(thread.AgentId), normalizedHandle, StringComparison.OrdinalIgnoreCase));
        var threadLabel = ResolveQuickReplyAgentLabel(matchingThread);
        if (!string.IsNullOrWhiteSpace(threadLabel))
            return threadLabel;

        if (_currentWorkspace is not null)
        {
            var rosterMatch = _teamRosterLoader.Load(_currentWorkspace.FolderPath)
                .FirstOrDefault(member => string.Equals(
                    NormalizeQuickReplyAgentHandle(DeriveQuickReplyAgentHandle(member.Name, member.CharterPath)),
                    normalizedHandle,
                    StringComparison.OrdinalIgnoreCase));
            if (rosterMatch is not null)
                return rosterMatch.Name;
        }

        return AgentThreadRegistry.HumanizeAgentName(normalizedHandle);
    }

    private static string? NormalizeQuickReplyRouteMode(string? routeMode)
    {
        if (string.IsNullOrWhiteSpace(routeMode))
            return null;

        return routeMode.Trim().ToLowerInvariant();
    }

    private static string? NormalizeQuickReplyAgentHandle(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
            return null;

        return handle.Trim().TrimStart('@').ToLowerInvariant();
    }

    private static string DeriveQuickReplyAgentHandle(string? name, string? charterPath)
    {
        if (!string.IsNullOrWhiteSpace(charterPath))
        {
            var normalized = charterPath.Replace('\\', '/');
            const string marker = "agents/";
            var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                var afterMarker = normalized[(markerIndex + marker.Length)..];
                var slashIndex = afterMarker.IndexOf('/');
                if (slashIndex > 0)
                    return afterMarker[..slashIndex].Trim().ToLowerInvariant();
            }
        }

        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var builder = new StringBuilder();
        var previousWasSeparator = false;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                previousWasSeparator = false;
                continue;
            }

            if (previousWasSeparator)
                continue;

            builder.Append('-');
            previousWasSeparator = true;
        }

        return builder.ToString().Trim('-');
    }

    private bool IsRoutingIssueQuickReply(QuickReplyButtonPayload payload)
    {
        return _routingIssueQuickReplyEntry is not null &&
               ReferenceEquals(payload.Entry, _routingIssueQuickReplyEntry);
    }

    private void HandleRoutingIgnoreQuickReply(TranscriptResponseEntry entry)
    {
        _pec.DisableQuickReplies(entry);
        _routingIssueQuickReplyEntry = null;

        if (!string.IsNullOrWhiteSpace(_currentRoutingAssessment?.IssueFingerprint))
            SetIgnoredRoutingIssueFingerprintForCurrentWorkspace(_currentRoutingAssessment.IssueFingerprint);

        ShowSystemTranscriptEntry(RoutingIssueWorkflow.BuildIgnoredMessage());
    }

    private async Task HandleRoutingRepairQuickReplyAsync(TranscriptResponseEntry entry)
    {
        if (!CanRunRoutingRepairPrompt())
        {
            ShowSystemTranscriptEntry(RoutingIssueWorkflow.BuildRepairBlockedMessage());
            return;
        }

        _pec.DisableQuickReplies(entry);
        _routingIssueQuickReplyEntry = null;

        var backupPath = _currentWorkspace is null
            ? null
            : _routingDocumentService.BackupExistingRoutingFile(_currentWorkspace.FolderPath);
        ShowSystemTranscriptEntry(RoutingIssueWorkflow.BuildRepairQueuedMessage(backupPath));
        _pendingSupplementalPromptInstruction = RoutingIssueWorkflow.BuildRepairInstruction();
        _pendingRoutingRepairRecheck = true;

        await _pec.ExecutePromptAsync(string.Empty, addToHistory: false, clearPromptBox: false);
    }

    private async void QuickReplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: QuickReplyButtonPayload payload } ||
            string.IsNullOrWhiteSpace(payload.Option))
            return;

        if (_isPromptRunning || _currentWorkspace is null)
            return;

        try
        {
            if (IsRoutingIssueQuickReply(payload))
            {
                var option = payload.Option.Trim();
                if (string.Equals(option, RoutingIssueWorkflow.IgnoreQuickReply, StringComparison.OrdinalIgnoreCase))
                {
                    HandleRoutingIgnoreQuickReply(payload.Entry);
                    return;
                }

                if (string.Equals(option, RoutingIssueWorkflow.RepairQuickReply, StringComparison.OrdinalIgnoreCase))
                {
                    await HandleRoutingRepairQuickReplyAsync(payload.Entry);
                    return;
                }
            }

            _pec.DisableQuickReplies(payload.Entry);
            var pendingLaunch = CreatePendingQuickReplyLaunch(payload);
            _pendingQuickReplyLaunch = pendingLaunch;
            var promptText = payload.Option.Trim();
            var requiresNamedAgentDelegation = QuickReplyAgentLaunchPolicy.RequiresObservedNamedAgentLaunch(
                payload.RouteMode,
                payload.TargetAgentHandle);
            _pendingSupplementalPromptInstruction = null;
            _pendingQuickReplyRoutingInstruction = requiresNamedAgentDelegation || string.IsNullOrWhiteSpace(payload.RoutingInstruction)
                ? null
                : payload.RoutingInstruction.Trim();
            SquadDashTrace.Write(
                "UI",
                string.IsNullOrWhiteSpace(payload.ContinuationAgentLabel)
                    ? $"Quick reply selected option='{payload.Option.Trim()}' routed=coordinator mode={payload.RouteMode ?? "(legacy)"}"
                    : $"Quick reply selected option='{payload.Option.Trim()}' routed={payload.ContinuationAgentLabel} mode={payload.RouteMode ?? "(legacy)"}");
            if (requiresNamedAgentDelegation)
            {
                SquadDashTrace.Write(
                    "Routing",
                    $"Quick reply entering same-session delegation target={payload.TargetAgentHandle?.Trim().TrimStart('@') ?? "(unknown)"} option='{promptText}'");
                await _pec.ExecuteNamedAgentDelegationAsync(
                    promptText,
                    payload.TargetAgentHandle!,
                    addToHistory: true,
                    clearPromptBox: false);
            }
            else
            {
                await _pec.ExecutePromptAsync(promptText, addToHistory: true, clearPromptBox: false);
            }

            MaybeReportPendingQuickReplyLaunchFailure(pendingLaunch);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException("Quick Reply", ex);
        }
        finally
        {
            _pendingQuickReplyLaunch = null;
            _pendingQuickReplyRoutingInstruction = null;
            _pendingSupplementalPromptInstruction = null;
        }
    }

    private void AppendTextRuns(InlineCollection inlines, string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        _markdownRenderer.AppendInlineMarkdown(inlines, text);
    }

    private void EnsureCurrentTurnThinkingVisible() =>
        EnsureCurrentTurnThinkingVisible(CoordinatorThread);

    private void EnsureCurrentTurnThinkingVisible(TranscriptThreadState thread)
    {
        if (thread.CurrentTurn is null)
            return;

        CollapseCurrentTurnThoughts(thread);
        GetLatestThinkingBlock(thread.CurrentTurn)?.Expander.SetCurrentValue(Expander.IsExpandedProperty, true);
    }

    private void AppendThinkingText(string text, string? speaker) =>
        AppendThinkingText(CoordinatorThread, text, speaker);

    private void AppendThinkingText(TranscriptThreadState thread, string text, string? speaker)
    {
        if (thread.CurrentTurn is null || string.IsNullOrWhiteSpace(text))
            return;

        var thoughtEntry = GetOrCreateThoughtEntry(thread.CurrentTurn, speaker);
        AppendThoughtChunk(thoughtEntry.RawTextBuilder, text);

        if (thread.CurrentTurn.ThoughtBlocks.LastOrDefault() is { } thoughtBlock)
            thoughtBlock.LastUpdatedAt = DateTimeOffset.Now;

        RenderThoughtEntry(thoughtEntry);
        ScrollToEndIfAtBottom(thread);
    }

    private static void AppendThoughtChunk(StringBuilder builder, string text)
    {
        var chunk = NormalizeThinkingChunk(text);
        if (string.IsNullOrWhiteSpace(chunk))
            return;

        builder.Append(chunk);
    }

    private void ScrollToEndIfAtBottom() =>
        ScrollToEndIfAtBottom(CoordinatorThread);

    private void ScrollToEndIfAtBottom(TranscriptThreadState thread)
    {
        if (!ReferenceEquals(_selectedTranscriptThread ?? CoordinatorThread, thread))
            return;

        EnsureThreadFooterAtEnd(thread);
        _scrollController.RequestScrollToEnd();
    }

    private void ScrollToPromptParagraph(Paragraph paragraph)
    {
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            var sv = OutputTextBox.Template?.FindName("PART_ContentHost", OutputTextBox) as ScrollViewer;
            if (sv is null) return;

            var tp = paragraph.ContentStart;
            var rect = tp.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward);

            // rect is in coordinates relative to the scroll viewer's visible area.
            // Skip scrolling if the target is already fully within the viewport.
            var isFullyVisible = rect.Top >= 0 && rect.Bottom <= sv.ViewportHeight;
            if (!isFullyVisible)
            {
                double targetOffset = sv.VerticalOffset + rect.Top;
                _scrollController.ScrollToOffset(targetOffset);
            }

            SyncPromptNavButtons();
        });
    }

    private void SyncPromptNavButtons()
    {
        var thread = _selectedTranscriptThread ?? CoordinatorThread;
        var count = thread.PromptParagraphs.Count;
        var idx = thread.PromptNavIndex;

        var isCoordinatorThread = ReferenceEquals(thread, CoordinatorThread);
        PromptNavButtonsPanel.Visibility = isCoordinatorThread || count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;

        PromptNavUpButton.IsEnabled = count > 0 && (idx == -1 || idx > 0);
        PromptNavDownButton.IsEnabled = count > 0 && idx != -1 && idx < count - 1;

        if (idx == -1)
        {
            HidePromptNavHint();
        }
        else
        {
            PromptNavHintTextBlock.Text = FormatRelativeTime(thread.PromptParagraphs[idx].Timestamp);
            ShowPromptNavHintWithFadeOut();
        }
    }

    private static string FormatRelativeTime(DateTimeOffset timestamp)
    {
        return StatusTimingPresentation.FormatRelativeTimestamp(timestamp);
    }

    private void ShowPromptNavHintWithFadeOut()
    {
        // Cancel any in-flight fade and restart the hold timer
        PromptNavHintTextBlock.BeginAnimation(OpacityProperty, null);
        PromptNavHintTextBlock.Opacity = 1;
        PromptNavHintTextBlock.Visibility = Visibility.Visible;

        if (_promptNavHintTimer is null)
        {
            _promptNavHintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
            _promptNavHintTimer.Tick += (_, _) =>
            {
                _promptNavHintTimer.Stop();
                var fade = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromSeconds(2)))
                {
                    FillBehavior = FillBehavior.Stop
                };
                fade.Completed += (_, _) => HidePromptNavHint();
                PromptNavHintTextBlock.BeginAnimation(OpacityProperty, fade);
            };
        }

        _promptNavHintTimer.Stop();
        _promptNavHintTimer.Start();
    }

    private void HidePromptNavHint()
    {
        _promptNavHintTimer?.Stop();
        PromptNavHintTextBlock.BeginAnimation(OpacityProperty, null);
        PromptNavHintTextBlock.Opacity = 1;
        PromptNavHintTextBlock.Visibility = Visibility.Collapsed;
    }

    private void PromptNavUpButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var thread = _selectedTranscriptThread ?? CoordinatorThread;
            var count = thread.PromptParagraphs.Count;
            if (count == 0) return;

            // First ↑ from no position → jump to most-recent prompt; subsequent ↑ → go back
            thread.PromptNavIndex = thread.PromptNavIndex == -1
                ? count - 1
                : Math.Max(0, thread.PromptNavIndex - 1);

            ScrollToPromptParagraph(thread.PromptParagraphs[thread.PromptNavIndex].Paragraph);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(PromptNavUpButton_Click), ex);
        }
    }

    private void PromptNavDownButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var thread = _selectedTranscriptThread ?? CoordinatorThread;
            var count = thread.PromptParagraphs.Count;
            if (count == 0 || thread.PromptNavIndex < 0) return;

            thread.PromptNavIndex = Math.Min(count - 1, thread.PromptNavIndex + 1);
            ScrollToPromptParagraph(thread.PromptParagraphs[thread.PromptNavIndex].Paragraph);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(PromptNavDownButton_Click), ex);
        }
    }

    private TranscriptThoughtEntry GetOrCreateThoughtEntry(
        TranscriptTurnView turn,
        string? speaker)
    {
        var normalizedSpeaker = string.IsNullOrWhiteSpace(speaker)
            ? "Coordinator"
            : AgentThreadRegistry.HumanizeAgentName(speaker);
        var existing = GetLatestThoughtEntry(turn);
        if (existing is not null &&
            string.Equals(existing.Speaker, normalizedSpeaker, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        return CreateThoughtEntry(turn, normalizedSpeaker);
    }

    private TranscriptThoughtEntry CreateThoughtEntry(
        TranscriptTurnView turn,
        string speaker,
        int? sequence = null)
    {
        var block = GetOrCreateThoughtBlock(turn);

        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };
        block.ContentPanel.Children.Add(textBlock);

        var entry = new TranscriptThoughtEntry(
            turn,
            AllocateNarrativeSequence(turn, sequence),
            speaker,
            textBlock);
        turn.ThoughtEntries.Add(entry);
        block.ThoughtEntries.Add(entry);
        return entry;
    }

    private TranscriptThoughtBlockView GetOrCreateThoughtBlock(TranscriptTurnView turn)
    {
        var latestBlock = turn.ThoughtBlocks.LastOrDefault();
        if (latestBlock is not null)
        {
            var latestSeq = latestBlock.ThoughtEntries.LastOrDefault()?.Sequence
                            ?? latestBlock.Sequence;
            if (latestSeq > Math.Max(GetLatestThinkingBlockSequence(turn), GetLatestResponseSequence(turn)))
                return latestBlock;
        }

        return CreateThoughtBlock(turn);
    }

    private TranscriptThoughtBlockView CreateThoughtBlock(TranscriptTurnView turn, int? sequence = null)
    {
        var header = new TextBlock
        {
            Text = "Thinking...",
            FontWeight = FontWeights.SemiBold
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "ThinkingText");

        var contentPanel = new StackPanel
        {
            Margin = new Thickness(18, 4, 0, 4)
        };

        var expander = new Expander
        {
            Header = header,
            Content = contentPanel,
            IsExpanded = true,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Margin = new Thickness(0, 0, 0, 6)
        };
        if (TryFindResource("TranscriptExpanderStyle") is Style expanderStyle)
            expander.Style = expanderStyle;

        var container = new BlockUIContainer(expander);
        turn.NarrativeSection.Blocks.Add(container);

        var block = new TranscriptThoughtBlockView(
            turn,
            AllocateNarrativeSequence(turn, sequence),
            header,
            expander,
            contentPanel);
        container.Tag = block;
        block.StartedAt = DateTimeOffset.Now;
        turn.ThoughtBlocks.Add(block);
        expander.ContextMenu = CreateThinkingContextMenu(turn);
        return block;
    }

    private void RenderPersistedThought(TranscriptTurnView turn, TranscriptThoughtRecord thought)
    {
        var entry = CreateThoughtEntry(turn, AgentThreadRegistry.HumanizeAgentName(thought.Speaker), thought.Sequence);
        entry.RawTextBuilder.Append(thought.Text);
        RenderThoughtEntry(entry);
    }

    private void RenderPersistedResponse(TranscriptTurnView turn, TranscriptResponseSegmentRecord responseSegment, bool allowQuickReplies = false)
    {
        var entry = CreateResponseEntry(turn, responseSegment.Sequence);
        entry.AllowQuickReplies = allowQuickReplies;
        entry.RawTextBuilder.Append(responseSegment.Text);
        RenderResponseEntry(entry);
    }

    private static TranscriptThoughtEntry? GetLatestThoughtEntry(TranscriptTurnView turn)
    {
        var latestThought = turn.ThoughtEntries.LastOrDefault();
        if (latestThought is null)
            return null;

        return latestThought.Sequence > Math.Max(GetLatestThinkingBlockSequence(turn), GetLatestResponseSequence(turn))
            ? latestThought
            : null;
    }

    private static TranscriptThinkingBlockView? GetLatestThinkingBlock(TranscriptTurnView turn)
    {
        var latestBlock = turn.ThinkingBlocks.LastOrDefault();
        if (latestBlock is null)
            return null;

        return latestBlock.Sequence > Math.Max(GetLatestThoughtSequence(turn), GetLatestResponseSequence(turn))
            ? latestBlock
            : null;
    }

    private static TranscriptResponseEntry? GetLatestResponseEntry(TranscriptTurnView turn)
    {
        var latestResponse = turn.ResponseEntries.LastOrDefault();
        if (latestResponse is null)
            return null;

        return latestResponse.Sequence > Math.Max(GetLatestThoughtSequence(turn), GetLatestThinkingBlockSequence(turn))
            ? latestResponse
            : null;
    }

    private static int GetLatestThoughtSequence(TranscriptTurnView turn)
    {
        return turn.ThoughtEntries.LastOrDefault()?.Sequence ?? 0;
    }

    private static int GetLatestThinkingBlockSequence(TranscriptTurnView turn)
    {
        return turn.ThinkingBlocks.LastOrDefault()?.Sequence ?? 0;
    }

    private static int GetLatestResponseSequence(TranscriptTurnView turn)
    {
        return turn.ResponseEntries.LastOrDefault()?.Sequence ?? 0;
    }

    private static int AllocateNarrativeSequence(TranscriptTurnView turn, int? explicitSequence = null)
    {
        if (explicitSequence is { } sequence && sequence > 0)
        {
            turn.NextNarrativeSequence = Math.Max(turn.NextNarrativeSequence, sequence + 1);
            return sequence;
        }

        return turn.NextNarrativeSequence++;
    }

    private TranscriptResponseEntry GetOrCreateResponseEntry(TranscriptTurnView turn)
    {
        var latest = GetLatestResponseEntry(turn);
        if (latest is not null)
            return latest;

        return CreateResponseEntry(turn);
    }

    private TranscriptResponseEntry CreateResponseEntry(TranscriptTurnView turn, int? sequence = null)
    {
        var section = new Section();
        turn.NarrativeSection.Blocks.Add(section);
        var entry = new TranscriptResponseEntry(turn, AllocateNarrativeSequence(turn, sequence), section);
        turn.ResponseEntries.Add(entry);
        return entry;
    }

    private void RenderThoughtEntry(TranscriptThoughtEntry thought)
    {
        var text = FormatThinkingText(thought.RawTextBuilder.ToString());
        if (string.IsNullOrWhiteSpace(text))
        {
            thought.TextBlock.Inlines.Clear();
            return;
        }

        thought.TextBlock.Inlines.Clear();
        var prefixRun = new Run($"{thought.Speaker}: ") { FontWeight = FontWeights.SemiBold };
        prefixRun.SetResourceReference(TextElement.ForegroundProperty, "ThinkingText");
        thought.TextBlock.Inlines.Add(prefixRun);
        var bodyRun = new Run(text);
        bodyRun.SetResourceReference(TextElement.ForegroundProperty, "ThinkingText");
        thought.TextBlock.Inlines.Add(new Italic(bodyRun));
    }

    private static string NormalizeThinkingChunk(string? text)
    {
        return string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    internal static string FormatThinkingText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        normalized = Regex.Replace(normalized, @"(?<=\w)\s+'(?=\w)", "'");
        normalized = Regex.Replace(
            normalized,
            @"(?<=[A-Za-z]{4,})\s+(?=(?:ize|ized|ization|ise|ised|ises|ing|ed|er|ers|ly|ment|ments|tion|tions|able|ible|ality|ality|ities|ity)\b)",
            string.Empty,
            RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+([,.;:!?%\)\]\}])", "$1");
        normalized = Regex.Replace(normalized, @"([\(\[\{])\s+", "$1");
        return normalized;
    }

    private Brush ResolveThoughtBrush(string speaker)
    {
        if (string.Equals(speaker, "Coordinator", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(speaker, "Squad", StringComparison.OrdinalIgnoreCase))
        {
            return FindAgentAccentBrush("Squad") ?? Brushes.DarkGoldenrod;
        }

        return FindAgentAccentBrush(speaker) ?? Brushes.DarkGoldenrod;
    }

    private Brush? FindAgentAccentBrush(string speaker)
    {
        var card = _agents.FirstOrDefault(agent =>
            string.Equals(agent.Name, speaker, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(agent.AccentStorageKey, speaker, StringComparison.OrdinalIgnoreCase));
        return card?.EffectiveAccentBrush;
    }

    private void CollapseCurrentTurnThinking() =>
        CollapseCurrentTurnThinking(CoordinatorThread);

    private void CollapseCurrentTurnThinking(TranscriptThreadState thread)
    {
        if (thread.CurrentTurn is null)
            return;

        foreach (var block in thread.CurrentTurn.ThinkingBlocks)
        {
            if (block.Expander.IsExpanded &&
                block.LastUpdatedAt is { } lastUpdatedAt &&
                lastUpdatedAt > block.StartedAt)
            {
                SetCollapsedBlockHeader(block.HeaderTextBlock, "Tooling...",
                    StatusTimingPresentation.FormatDuration(lastUpdatedAt - block.StartedAt));
            }

            block.Expander.IsExpanded = false;
        }

        CollapseCurrentTurnThoughts(thread);
    }

    private void CollapseCurrentTurnThoughts(TranscriptThreadState thread)
    {
        if (thread.CurrentTurn is null)
            return;

        foreach (var block in thread.CurrentTurn.ThoughtBlocks)
        {
            if (block.Expander.IsExpanded &&
                block.LastUpdatedAt is { } lastUpdatedAt &&
                lastUpdatedAt > block.StartedAt)
            {
                SetCollapsedBlockHeader(block.HeaderTextBlock, "Thinking...",
                    StatusTimingPresentation.FormatDuration(lastUpdatedAt - block.StartedAt));
            }

            block.Expander.IsExpanded = false;
        }
    }

    private static void SetCollapsedBlockHeader(TextBlock header, string label, string? duration = null)
    {
        header.Text = string.Empty;
        header.Inlines.Clear();
        var labelRun = new Run(label) { FontWeight = FontWeights.SemiBold };
        header.Inlines.Add(labelRun);
        if (!string.IsNullOrWhiteSpace(duration))
        {
            var durationRun = new Run($" {duration}") { FontWeight = FontWeights.Normal };
            durationRun.SetResourceReference(TextElement.ForegroundProperty, "ThinkingMetaText");
            header.Inlines.Add(durationRun);
        }
    }

    private void StartToolExecution(SquadSdkEvent evt) =>
        StartToolExecution(CoordinatorThread, evt);

    private void StartToolExecution(TranscriptThreadState thread, SquadSdkEvent evt)
    {
        if (!TryGetOrCreateToolEntry(thread, evt, out var entry))
            return;

        _agentThreadRegistry.CaptureBackgroundAgentLaunchInfo(evt);

        if (!string.IsNullOrWhiteSpace(evt.ProgressMessage))
            entry.ProgressText = evt.ProgressMessage;

        EnsureCurrentTurnThinkingVisible(thread);
        RenderToolEntry(entry);
        UpdateToolSpinnerState();
        if (thread.Kind == TranscriptThreadKind.Coordinator)
        {
            SyncActiveToolName();
            UpdateLeadAgent(
                "Tooling",
                string.Empty,
                ToolTranscriptFormatter.BuildRunningText(entry.Descriptor, entry.ProgressText));
            UpdateSessionState("Using tool");
        }

        ScrollToEndIfAtBottom(thread);
    }

    private void UpdateToolExecution(SquadSdkEvent evt) =>
        UpdateToolExecution(CoordinatorThread, evt);

    private void UpdateToolExecution(TranscriptThreadState thread, SquadSdkEvent evt)
    {
        if (!TryGetOrCreateToolEntry(thread, evt, out var entry))
            return;

        if (!string.IsNullOrWhiteSpace(evt.ProgressMessage))
            entry.ProgressText = evt.ProgressMessage;
        if (!string.IsNullOrWhiteSpace(evt.PartialOutput))
            entry.OutputText = MergeToolOutput(entry.OutputText, evt.PartialOutput);

        EnsureCurrentTurnThinkingVisible(thread);
        RenderToolEntry(entry);
        UpdateToolSpinnerState();
        if (thread.Kind == TranscriptThreadKind.Coordinator)
        {
            SyncActiveToolName();
            UpdateLeadAgent(
                "Tooling",
                string.Empty,
                ToolTranscriptFormatter.BuildRunningText(entry.Descriptor, entry.ProgressText));
            UpdateSessionState("Using tool");
        }

        ScrollToEndIfAtBottom(thread);
    }

    private void CompleteToolExecution(SquadSdkEvent evt) =>
        CompleteToolExecution(CoordinatorThread, evt);

    private void CompleteToolExecution(TranscriptThreadState thread, SquadSdkEvent evt)
    {
        if (!TryGetOrCreateToolEntry(thread, evt, out var entry))
            return;

        _agentThreadRegistry.CaptureBackgroundAgentLaunchInfo(evt);

        entry.IsCompleted = true;
        entry.Success = evt.Success ?? true;
        entry.FinishedAt = ParseTimestamp(evt.FinishedAt);

        if (entry.FinishedAt is { } finishedAt)
            entry.ThinkingBlock.LastUpdatedAt = finishedAt;

        if (!string.IsNullOrWhiteSpace(evt.OutputText))
            entry.OutputText = evt.OutputText.Trim();

        entry.DetailContent = ToolTranscriptFormatter.BuildDetailContent(new ToolTranscriptDetail(
            entry.Descriptor,
            entry.ArgsJson,
            entry.OutputText,
            entry.StartedAt,
            entry.FinishedAt,
            entry.ProgressText,
            entry.IsCompleted,
            entry.Success));

        RenderToolEntry(entry);
        UpdateToolSpinnerState();
        if (thread.Kind == TranscriptThreadKind.Coordinator)
        {
            SyncActiveToolName();
            if (string.IsNullOrWhiteSpace(_pec.ActiveToolName))
                UpdateSessionState("Running");
            else
                UpdateSessionState("Using tool");
        }

        ScrollToEndIfAtBottom(thread);
    }

    private bool TryGetOrCreateToolEntry(
        TranscriptThreadState thread,
        SquadSdkEvent evt,
        out ToolTranscriptEntry entry)
    {
        if (thread.CurrentTurn is null || string.IsNullOrWhiteSpace(evt.ToolCallId))
        {
            entry = null!;
            return false;
        }

        if (_agentThreadRegistry.TryGetToolEntry(evt.ToolCallId, out entry))
        {
            SyncTaskToolTranscriptLink(entry);
            return true;
        }

        entry = CreateToolEntry(
            GetOrCreateThinkingBlockForNewTool(thread.CurrentTurn),
            evt.ToolCallId,
            CreateToolDescriptor(evt),
            TryFormatJson(evt.Args),
            ParseTimestamp(evt.StartedAt));
        _agentThreadRegistry.SetToolEntry(evt.ToolCallId, entry);
        return true;
    }

    private void SyncTaskToolTranscriptLink(TranscriptThreadState thread)
    {
        if (string.IsNullOrWhiteSpace(thread.ToolCallId) ||
            !_agentThreadRegistry.TryGetToolEntry(thread.ToolCallId, out var entry))
        {
            return;
        }

        entry.TranscriptThreadId = thread.ThreadId;
        SyncTaskToolTranscriptLink(entry);
    }

    private void SyncTaskToolTranscriptLink(ToolTranscriptEntry entry)
    {
        if (!string.Equals(entry.Descriptor.ToolName, "task", StringComparison.OrdinalIgnoreCase))
        {
            entry.TranscriptThreadId = null;
            entry.TranscriptButton.Visibility = Visibility.Collapsed;
            entry.TranscriptButton.ToolTip = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.TranscriptThreadId) &&
            _agentThreadRegistry.ThreadsByToolCallId.TryGetValue(entry.ToolCallId, out var thread))
        {
            entry.TranscriptThreadId = thread.ThreadId;
        }

        var isVisible = !string.IsNullOrWhiteSpace(entry.TranscriptThreadId);
        entry.TranscriptButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        entry.TranscriptButton.ToolTip = isVisible ? "Open transcript" : null;
    }

    private ToolTranscriptEntry CreateToolEntry(
        TranscriptThinkingBlockView block,
        string toolCallId,
        ToolTranscriptDescriptor descriptor,
        string? argsJson,
        DateTimeOffset startedAt)
    {
        var iconTextBlock = new TextBlock
        {
            Text = ToolSpinnerFrames[_toolSpinnerFrame],
            Width = 24,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconTextBlock.SetResourceReference(TextBlock.ForegroundProperty, "ToolRunningIcon");

        var iconSize = ToolIconSizeForFontSize(_transcriptFontSize);
        var emojiImage = new Image
        {
            Width = iconSize,
            Height = iconSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 4, 0)
        };
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(emojiImage, System.Windows.Media.BitmapScalingMode.HighQuality);
        _toolIconImages.Add(emojiImage);

        var messageTextBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        messageTextBlock.SetResourceReference(TextBlock.ForegroundProperty, "ToolBodyText");

        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        headerPanel.Children.Add(iconTextBlock);
        headerPanel.Children.Add(emojiImage);
        headerPanel.Children.Add(messageTextBlock);

        var transcriptButton = new Button
        {
            Content = "Transcript",
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent
        };
        transcriptButton.SetResourceReference(Control.ForegroundProperty, "ToolActionLink");
        transcriptButton.SetResourceReference(Control.BorderBrushProperty, "ToolActionLinkBorder");
        headerPanel.Children.Add(transcriptButton);

        var detailTextBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            MinHeight = 140,
            MaxHeight = 260
        };
        detailTextBox.SetResourceReference(TextBox.BackgroundProperty, "CodeSurface");
        detailTextBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        detailTextBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");

        var detailPanel = new StackPanel
        {
            Margin = new Thickness(8, 6, 0, 2)
        };

        var expander = new Expander
        {
            Header = headerPanel,
            IsExpanded = false,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Margin = new Thickness(0, 2, 0, 2)
        };
        if (TryFindResource("TranscriptExpanderStyle") is Style expanderStyle)
            expander.Style = expanderStyle;

        var entry = new ToolTranscriptEntry(
            toolCallId,
            block.Turn,
            block,
            descriptor,
            argsJson,
            startedAt,
            expander,
            iconTextBlock,
            emojiImage,
            messageTextBlock,
            detailTextBox,
            transcriptButton);
        block.Turn.ToolEntries.Add(entry);
        block.ToolEntries.Add(entry);

        expander.ContextMenu = CreateThinkingContextMenu(block.Turn);
        headerPanel.ContextMenu = CreateThinkingContextMenu(block.Turn);
        iconTextBlock.ContextMenu = CreateThinkingContextMenu(block.Turn);
        emojiImage.ContextMenu = CreateThinkingContextMenu(block.Turn);
        messageTextBlock.ContextMenu = CreateThinkingContextMenu(block.Turn);
        transcriptButton.ContextMenu = CreateThinkingContextMenu(block.Turn);
        transcriptButton.Tag = entry;
        transcriptButton.Click += OpenToolTranscriptButton_Click;
        SyncTaskToolTranscriptLink(entry);

        var openButton = new Button
        {
            Content = "Open Details Window",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(8, 2, 8, 2),
            Tag = entry
        };
        openButton.Click += OpenToolDetailsButton_Click;

        detailPanel.Children.Add(openButton);
        detailPanel.Children.Add(detailTextBox);
        expander.Content = detailPanel;

        block.ContentPanel.Children.Add(expander);
        return entry;
    }

    private void RenderPersistedTool(TranscriptThinkingBlockView block, TranscriptToolRecord tool)
    {
        var toolCallId = string.IsNullOrWhiteSpace(tool.ToolCallId)
            ? Guid.NewGuid().ToString("N")
            : tool.ToolCallId;
        var entry = CreateToolEntry(
            block,
            toolCallId,
            tool.Descriptor,
            tool.ArgsJson,
            tool.StartedAt);
        _agentThreadRegistry.SetToolEntry(toolCallId, entry);

        entry.FinishedAt = tool.FinishedAt;
        entry.ProgressText = tool.ProgressText;
        entry.OutputText = tool.OutputText;
        entry.DetailContent = tool.DetailContent;
        entry.IsCompleted = tool.IsCompleted;
        entry.Success = tool.Success;
        RenderToolEntry(entry);
    }

    private TranscriptThinkingBlockView GetOrCreateThinkingBlockForNewTool(TranscriptTurnView turn)
    {
        var latestBlock = GetLatestThinkingBlock(turn);
        if (latestBlock is not null)
        {
            latestBlock.Expander.IsExpanded = true;
            return latestBlock;
        }

        return CreateThinkingBlock(turn, isExpanded: true);
    }

    private TranscriptThinkingBlockView CreateThinkingBlock(
        TranscriptTurnView turn,
        int? sequence = null,
        bool isExpanded = true)
    {
        var header = new TextBlock
        {
            Text = "Tooling...",
            FontWeight = FontWeights.SemiBold
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "ThinkingText");

        var contentPanel = new StackPanel
        {
            Margin = new Thickness(18, 4, 0, 2)
        };

        var expander = new Expander
        {
            Header = header,
            Content = contentPanel,
            IsExpanded = isExpanded,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Margin = new Thickness(0, 0, 0, 6)
        };
        if (TryFindResource("TranscriptExpanderStyle") is Style expanderStyle)
            expander.Style = expanderStyle;

        var container = new BlockUIContainer(expander);
        turn.NarrativeSection.Blocks.Add(container);

        var block = new TranscriptThinkingBlockView(
            turn,
            AllocateNarrativeSequence(turn, sequence),
            header,
            expander,
            contentPanel);
        container.Tag = block;
        block.StartedAt = DateTimeOffset.Now;
        turn.ThinkingBlocks.Add(block);
        header.ContextMenu = CreateThinkingContextMenu(turn);
        expander.ContextMenu = CreateThinkingContextMenu(turn);
        return block;
    }

    private void RenderToolEntry(ToolTranscriptEntry entry)
    {
        SyncTaskToolTranscriptLink(entry);
        entry.IconTextBlock.Text = entry.IsCompleted
            ? entry.Success ? "✔️" : "⚠"
            : ToolSpinnerFrames[_toolSpinnerFrame];
        entry.IconTextBlock.SetResourceReference(TextBlock.ForegroundProperty,
            entry.IsCompleted
                ? (entry.Success ? "ToolSuccessIcon" : "ToolFailureIcon")
                : "ToolRunningIcon");
        entry.MessageTextBlock.SetResourceReference(TextBlock.ForegroundProperty,
            entry.IsCompleted && !entry.Success ? "ToolFailureText" : "ToolBodyText");
        entry.MessageTextBlock.Inlines.Clear();
        RenderToolMessage(entry);
        entry.DetailTextBox.Text = entry.DetailContent ?? ToolTranscriptFormatter.BuildDetailContent(new ToolTranscriptDetail(
            entry.Descriptor,
            entry.ArgsJson,
            entry.OutputText,
            entry.StartedAt,
            entry.FinishedAt,
            entry.ProgressText,
            entry.IsCompleted,
            entry.Success));
    }

    private void RenderToolMessage(ToolTranscriptEntry entry)
    {
        var iconKey = $"ToolIcon_{entry.Descriptor.ToolName.Trim()}";
        entry.EmojiImage.Source = (TryFindResource(iconKey) ?? TryFindResource("ToolIcon_default"))
            as System.Windows.Media.ImageSource;
        entry.EmojiImage.Visibility = entry.EmojiImage.Source is not null
            ? Visibility.Visible : Visibility.Collapsed;

        if (entry.IsCompleted &&
            entry.Success &&
            ToolTranscriptFormatter.TryBuildEditDiffSummary(entry.Descriptor, entry.OutputText) is { } diffSummary)
        {
            var fileRun = new Run(diffSummary.DisplayName);
            fileRun.SetResourceReference(TextElement.ForegroundProperty,
                diffSummary.IsDeletedFile ? "DiffDeletedFileText" : "TableCellText");

            if (diffSummary.IsDeletedFile)
                fileRun.TextDecorations = TextDecorations.Strikethrough;

            entry.MessageTextBlock.Inlines.Add(fileRun);
            entry.MessageTextBlock.Inlines.Add(new Run(" "));
            var addedRun = new Run($"+{diffSummary.AddedLineCount}")
            {
                FontWeight = FontWeights.SemiBold
            };
            addedRun.SetResourceReference(TextElement.ForegroundProperty, "DiffAddedText");
            entry.MessageTextBlock.Inlines.Add(addedRun);
            entry.MessageTextBlock.Inlines.Add(new Run(" "));
            var removedRun = new Run($"-{diffSummary.RemovedLineCount}")
            {
                FontWeight = FontWeights.SemiBold
            };
            removedRun.SetResourceReference(TextElement.ForegroundProperty, "DiffRemovedText");
            entry.MessageTextBlock.Inlines.Add(removedRun);

            if (diffSummary.IsNewFile)
                entry.MessageTextBlock.Inlines.Add(new Run(" ➕"));

            return;
        }

        var rawText = entry.IsCompleted
            ? ToolTranscriptFormatter.BuildCompletedText(entry.Descriptor, entry.Success, entry.ProgressText, entry.OutputText)
            : ToolTranscriptFormatter.BuildRunningText(entry.Descriptor, entry.ProgressText);

        // Strip any emoji prefix the formatter prepended — the icon is now shown as a DrawingImage
        var toolEmoji = ToolTranscriptFormatter.GetToolEmoji(entry.Descriptor);
        entry.MessageTextBlock.Text = !string.IsNullOrEmpty(toolEmoji) && rawText.StartsWith(toolEmoji, StringComparison.Ordinal)
            ? rawText[toolEmoji.Length..].TrimStart(' ')
            : rawText;
    }

    private void AdvanceToolSpinner()
    {
        _toolSpinnerFrame = (_toolSpinnerFrame + 1) % ToolSpinnerFrames.Length;

        var runningEntries = _agentThreadRegistry.ToolEntries.Values.Where(item => !item.IsCompleted).ToList();

        foreach (var entry in runningEntries)
            RenderToolEntry(entry);

        // Update the elapsed time label on each ThinkingBlock that has running tools
        var activeBlocks = runningEntries
            .Select(e => e.ThinkingBlock)
            .Distinct();
        var now = DateTimeOffset.Now;
        foreach (var block in activeBlocks)
        {
            var elapsed = now - block.StartedAt;
            if (elapsed > TimeSpan.Zero)
            {
                block.HeaderTextBlock.Inlines.Clear();
                var liveLabel = new Run("Tooling...") { FontWeight = FontWeights.SemiBold };
                var liveDuration = new Run($" {StatusTimingPresentation.FormatDuration(elapsed)}") { FontWeight = FontWeights.Normal };
                liveDuration.SetResourceReference(TextElement.ForegroundProperty, "ThinkingMetaText");
                block.HeaderTextBlock.Inlines.Add(liveLabel);
                block.HeaderTextBlock.Inlines.Add(liveDuration);
            }
        }
    }

    private void UpdateToolSpinnerState()
    {
        if (_agentThreadRegistry.ToolEntries.Values.Any(entry => !entry.IsCompleted))
        {
            if (!_toolSpinnerTimer.IsEnabled)
                _toolSpinnerTimer.Start();
            return;
        }

        _toolSpinnerTimer.Stop();
        _toolSpinnerFrame = 0;
    }

    private void SyncActiveToolName()
    {
        _pec.ActiveToolName = _agentThreadRegistry.ToolEntries.Values
            .Where(entry => !entry.IsCompleted && ReferenceEquals(entry.Turn, CoordinatorThread.CurrentTurn))
            .Select(entry => ToolTranscriptFormatter.HumanizeToolName(entry.Descriptor.ToolName))
            .LastOrDefault();
    }

    private void OpenToolDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: ToolTranscriptEntry entry })
                return;

            ShowTextWindow(
                $"{ToolTranscriptFormatter.HumanizeToolName(entry.Descriptor.ToolName)} Tool Details",
                entry.DetailContent ?? ToolTranscriptFormatter.BuildDetailContent(new ToolTranscriptDetail(
                    entry.Descriptor,
                    entry.ArgsJson,
                    entry.OutputText,
                    entry.StartedAt,
                    entry.FinishedAt,
                    entry.ProgressText,
                    entry.IsCompleted,
                    entry.Success)));
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(OpenToolDetailsButton_Click), ex);
        }
    }

    private ToolTranscriptDescriptor CreateToolDescriptor(SquadSdkEvent evt)
    {
        return new ToolTranscriptDescriptor(
            evt.ToolName ?? "tool",
            evt.Description,
            evt.Command,
            evt.Path,
            evt.Intent,
            evt.Skill,
            BuildToolDisplayText(evt));
    }

    private string? BuildToolDisplayText(SquadSdkEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.ToolName))
            return null;

        return evt.ToolName.Trim() switch
        {
            "glob" => TryGetJsonString(evt.Args, "pattern"),
            "grep" => BuildRelativeToolPathLabel(TryGetJsonString(evt.Args, "path") ?? evt.Path)
                      ?? TryGetJsonString(evt.Args, "pattern"),
            "view" => BuildRelativeToolPathLabel(TryGetJsonString(evt.Args, "path") ?? evt.Path),
            "edit" => BuildRelativeToolPathLabel(TryGetJsonString(evt.Args, "path") ?? evt.Path),
            "create" => BuildRelativeToolPathLabel(TryGetJsonString(evt.Args, "path") ?? evt.Path),
            "web_fetch" => StripUrlScheme(TryGetJsonString(evt.Args, "url")),
            "task" => TryGetJsonString(evt.Args, "description"),
            "skill" => TryGetJsonString(evt.Args, "skill") ?? evt.Skill,
            "store_memory" => TryGetJsonString(evt.Args, "subject") ?? TryGetJsonString(evt.Args, "fact"),
            "report_intent" => TryGetJsonString(evt.Args, "intent") ?? evt.Intent,
            "sql" => TryGetJsonString(evt.Args, "description"),
            "powershell" => TryGetJsonString(evt.Args, "description") ?? TryGetJsonString(evt.Args, "command") ?? evt.Command,
            _ => null
        };
    }

    private string? BuildRelativeToolPathLabel(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = Path.GetFullPath(path);
        if (_currentWorkspace is null)
            return Path.GetFileName(normalized) is { Length: > 0 } name ? name : normalized;

        try
        {
            var rel = Path.GetRelativePath(_currentWorkspace.FolderPath, normalized);
            if (rel == ".")
                rel = Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(rel) ? normalized : rel;
        }
        catch
        {
            return normalized;
        }
    }

    private TeamAgentDescriptor[] GetKnownTeamAgentDescriptors()
    {
        return _agents
            .Where(card => !card.IsLeadAgent && !card.IsDynamicAgent)
            .Select(card => new TeamAgentDescriptor(card.Name, card.AccentStorageKey, card.RoleText))
            .ToArray();
    }

    private static string? StripUrlScheme(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var trimmed = url.Trim();
        foreach (var scheme in new[] { "https://", "http://" })
        {
            if (trimmed.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                return trimmed[scheme.Length..];
        }

        return trimmed;
    }

    private static string? TryGetJsonString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static DateTimeOffset ParseTimestamp(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : DateTimeOffset.Now;
    }

    private static DateTimeOffset? TryParseTimestamp(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : null;
    }

    internal static string BuildTimedStatusText(
        string? statusText,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        DateTimeOffset now)
    {
        var status = AgentThreadRegistry.HumanizeThreadStatus(statusText);
        if (string.IsNullOrWhiteSpace(status))
            status = completedAt is null ? "Running" : "Completed";

        var effectiveStartedAt = startedAt ?? completedAt ?? now;
        return StatusTimingPresentation.BuildStatus(status, effectiveStartedAt, completedAt, now);
    }

    private static string? TryFormatJson(JsonElement element)
    {
        return element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? null
            : FormatJson(element);
    }

    private static string? MergeToolOutput(string? existingOutput, string? newOutput)
    {
        if (string.IsNullOrWhiteSpace(newOutput))
            return existingOutput;
        if (string.IsNullOrWhiteSpace(existingOutput))
            return newOutput.TrimEnd();
        if (existingOutput.Contains(newOutput, StringComparison.Ordinal))
            return existingOutput;

        return existingOutput.TrimEnd() + Environment.NewLine + newOutput.TrimEnd();
    }

    private void ClearSessionView()
    {
        DisposeInboxWatcher();
        DisposeTeamFileWatcher();
        _pec.ActiveToolName = null;
        _conversationManager.CurrentSessionId = null;
        _currentTurn = null;
        CoordinatorThread.Document.Blocks.Clear();
        _agentThreadRegistry.ClearAll();
        _backgroundTaskPresenter.ClearState();
        _routingIssueQuickReplyEntry = null;
        _announcedRoutingIssueFingerprint = null;
        _pendingSupplementalPromptInstruction = null;
        _pendingRoutingRepairRecheck = false;
        _conversationManager.ConversationState = WorkspaceConversationState.Empty;
        _toolSpinnerTimer.Stop();
        _toolSpinnerFrame = 0;
        SelectTranscriptThread(CoordinatorThread);
    }

    private string? GetIgnoredRoutingIssueFingerprintForCurrentWorkspace()
    {
        if (_currentWorkspace is null)
            return null;

        return _settingsSnapshot.IgnoredRoutingIssueFingerprintsByWorkspace.TryGetValue(
            _currentWorkspace.FolderPath,
            out var fingerprint)
            ? fingerprint
            : null;
    }

    private void SetIgnoredRoutingIssueFingerprintForCurrentWorkspace(string? fingerprint)
    {
        if (_currentWorkspace is null)
            return;

        _settingsSnapshot = _settingsStore.SaveIgnoredRoutingIssueFingerprint(
            _currentWorkspace.FolderPath,
            fingerprint);
    }

    private void ClearIgnoredRoutingIssueFingerprintIfResolved()
    {
        if (_currentWorkspace is null ||
            _currentRoutingAssessment is { NeedsRepair: true })
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(GetIgnoredRoutingIssueFingerprintForCurrentWorkspace()))
            SetIgnoredRoutingIssueFingerprintForCurrentWorkspace(null);
    }

    private bool CanRunRoutingRepairPrompt()
    {
        return _currentWorkspace is not null &&
               !_isInstallingSquad &&
               _currentInstallationState?.IsSquadInstalledForActiveDirectory == true &&
               _startupIssue is null;
    }

    private TranscriptResponseEntry? ShowSystemTranscriptEntry(string text)
    {
        if (_isClosing || string.IsNullOrWhiteSpace(text))
            return null;

        SelectTranscriptThread(CoordinatorThread);
        var turn = BeginTranscriptTurn(string.Empty);
        AppendLine(text);
        FinalizeCurrentTurnResponse();
        _currentTurn = null;
        return turn.ResponseEntries.LastOrDefault();
    }

    private void MaybePublishRoutingIssueSystemEntry(string reason, bool force = false)
    {
        if (_isClosing || _isPromptRunning || _currentWorkspace is null)
            return;

        var assessment = _currentRoutingAssessment;
        if (assessment is null)
        {
            _routingIssueQuickReplyEntry = null;
            _announcedRoutingIssueFingerprint = null;
            return;
        }

        if (!assessment.NeedsRepair || string.IsNullOrWhiteSpace(assessment.IssueFingerprint))
        {
            _routingIssueQuickReplyEntry = null;
            _announcedRoutingIssueFingerprint = null;
            return;
        }

        if (string.Equals(
                assessment.IssueFingerprint,
                GetIgnoredRoutingIssueFingerprintForCurrentWorkspace(),
                StringComparison.OrdinalIgnoreCase))
        {
            _routingIssueQuickReplyEntry = null;
            return;
        }

        if (!force &&
            string.Equals(
                assessment.IssueFingerprint,
                _announcedRoutingIssueFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _routingIssueQuickReplyEntry = ShowSystemTranscriptEntry(
            RoutingIssueWorkflow.BuildSystemEntry(assessment));
        _announcedRoutingIssueFingerprint = assessment.IssueFingerprint;
        SquadDashTrace.Write(
            "Routing",
            $"Published routing issue entry reason={reason} status={assessment.Status} fingerprint={assessment.IssueFingerprint}");
    }

    private async Task WaitForRoutingRepairStateToSettleAsync()
    {
        if (_currentWorkspace is null)
            return;

        var attempts = 6;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            RefreshInstallationState();
            if (_currentRoutingAssessment is null || !_currentRoutingAssessment.NeedsRepair)
                return;

            await Task.Delay(250);
        }
    }

    private void RefreshInstallationState()
    {
        _currentInstallationState = _currentWorkspace is null
            ? null
            : _installationStateService.GetState(_currentWorkspace.FolderPath);
        _currentRoutingAssessment = _currentWorkspace is not null &&
                                    _settingsSnapshot.StartupIssueSimulation == DeveloperStartupIssueSimulation.None
            ? _routingDocumentService.Assess(_currentWorkspace.FolderPath)
            : null;
        ClearIgnoredRoutingIssueFingerprintIfResolved();

        _startupIssue = WorkspaceIssueFactory.CreateStartupIssue(
            _currentInstallationState,
            _settingsSnapshot.StartupIssueSimulation);
        UpdateWorkspaceIssuePanel();
        UpdateInteractiveControlState();
    }

    private void SetInstallUiState(bool isInstalling, string statusText)
    {
        _isInstallingSquad = isInstalling;
        SetInstallStatus(statusText);
        UpdateInteractiveControlState();
    }

    private void SetInstallStatus(string statusText)
    {
        InstallStatusTextBlock.Text = statusText;
        InstallStatusTextBlock.Visibility = string.IsNullOrWhiteSpace(statusText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateWorkspaceIssuePanel()
    {
        var issue = _runtimeIssue ?? _startupIssue;
        var issueKey = WorkspaceIssuePanelState.BuildDismissalKey(issue);
        var isDismissed = issue is not null &&
                          string.Equals(_dismissedWorkspaceIssueKey, issueKey, StringComparison.Ordinal);
        WorkspaceIssuePanelBorder.Visibility = issue is null || isDismissed
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (issue is null)
        {
            _dismissedWorkspaceIssueKey = null;
            WorkspaceIssueTitleTextBlock.Text = string.Empty;
            WorkspaceIssueDetailTextBlock.Text = string.Empty;
            WorkspaceIssueDetailTextBlock.Visibility = Visibility.Collapsed;
            SetInstallStatus(string.Empty);
            InstallSquadButton.Visibility = Visibility.Collapsed;
            IssueHelpButton.Visibility = Visibility.Collapsed;
            IssueActionButton.Visibility = Visibility.Collapsed;
            IssueSecondaryActionButton.Visibility = Visibility.Collapsed;
            IssuePrimaryLinkButton.Visibility = Visibility.Collapsed;
            IssueSecondaryLinkButton.Visibility = Visibility.Collapsed;
            IssueDismissButton.Visibility = Visibility.Collapsed;
            return;
        }

        WorkspaceIssueTitleTextBlock.Text = issue.Title;
        SetInstallStatus(issue.Message);
        WorkspaceIssueDetailTextBlock.Text = issue.DetailText ?? string.Empty;
        WorkspaceIssueDetailTextBlock.Visibility = string.IsNullOrWhiteSpace(issue.DetailText)
            ? Visibility.Collapsed
            : Visibility.Visible;

        InstallSquadButton.Visibility = issue.ShowInstallButton && _currentWorkspace is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

        IssueHelpButton.Content = string.IsNullOrWhiteSpace(issue.HelpButtonLabel)
            ? "View Fix Steps"
            : issue.HelpButtonLabel;
        IssueHelpButton.Visibility = string.IsNullOrWhiteSpace(issue.HelpWindowContent)
            ? Visibility.Collapsed
            : Visibility.Visible;

        ConfigureIssueActionButton(IssueActionButton, issue.Action);
        ConfigureIssueActionButton(IssueSecondaryActionButton, issue.SecondaryAction);
        ConfigureIssueLinkButton(IssuePrimaryLinkButton, issue.PrimaryLink);
        ConfigureIssueLinkButton(IssueSecondaryLinkButton, issue.SecondaryLink);
        IssueDismissButton.Visibility = Visibility.Visible;
    }

    private static void ConfigureIssueActionButton(Button button, WorkspaceIssueAction? action)
    {
        if (action is null)
        {
            button.Visibility = Visibility.Collapsed;
            button.Tag = null;
            button.Content = string.Empty;
            return;
        }

        button.Visibility = Visibility.Visible;
        button.Tag = action;
        button.Content = action.Label;
    }

    private static void ConfigureIssueLinkButton(Button button, WorkspaceIssueExternalLink? link)
    {
        if (link is null)
        {
            button.Visibility = Visibility.Collapsed;
            button.Tag = null;
            button.Content = string.Empty;
            return;
        }

        button.Visibility = Visibility.Visible;
        button.Tag = link.Url;
        button.Content = link.Label;
    }

    private WorkspaceIssuePresentation ShowRuntimeIssue(string errorMessage)
    {
        _runtimeIssue = _settingsSnapshot.RuntimeIssueSimulation == DeveloperRuntimeIssueSimulation.None
            ? WorkspaceIssueFactory.CreateRuntimeIssue(errorMessage, _currentInstallationState)
            : WorkspaceIssueFactory.CreateSimulatedRuntimeIssue(
                _settingsSnapshot.RuntimeIssueSimulation,
                _currentInstallationState);
        _dismissedWorkspaceIssueKey = null;
        UpdateWorkspaceIssuePanel();
        return _runtimeIssue;
    }

    private void ClearRuntimeIssue()
    {
        if (_runtimeIssue is null)
            return;

        _runtimeIssue = null;
        UpdateWorkspaceIssuePanel();
    }

    private void RefreshDeveloperRuntimeIssuePreview()
    {
        if (_settingsSnapshot.RuntimeIssueSimulation == DeveloperRuntimeIssueSimulation.None)
        {
            ClearRuntimeIssue();
            return;
        }

        _runtimeIssue = WorkspaceIssueFactory.CreateSimulatedRuntimeIssue(
            _settingsSnapshot.RuntimeIssueSimulation,
            _currentInstallationState);
        _dismissedWorkspaceIssueKey = null;
        UpdateWorkspaceIssuePanel();
    }

    private void IssueDismissButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var issue = _runtimeIssue ?? _startupIssue;
            if (issue is null)
                return;

            _dismissedWorkspaceIssueKey = WorkspaceIssuePanelState.BuildDismissalKey(issue);
            UpdateWorkspaceIssuePanel();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(IssueDismissButton_Click), ex);
        }
    }

    private bool IsDeveloperSimulationActive()
    {
        return _settingsSnapshot.StartupIssueSimulation != DeveloperStartupIssueSimulation.None ||
               _settingsSnapshot.RuntimeIssueSimulation != DeveloperRuntimeIssueSimulation.None;
    }

    private void IssueHelpButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var issue = _runtimeIssue ?? _startupIssue;
            if (issue is null || string.IsNullOrWhiteSpace(issue.HelpWindowContent))
                return;

            ShowTextWindow(
                issue.HelpWindowTitle ?? "Squad Help",
                issue.HelpWindowContent);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(IssueHelpButton_Click), ex);
        }
    }

    private void IssueLinkButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: string target })
                return;

            _squadCliAdapter.OpenExternalLink(target);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(IssueLinkButton_Click), ex);
        }
    }

    private void IssueActionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ExecuteIssueAction(sender);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(IssueActionButton_Click), ex);
        }
    }

    private void IssueSecondaryActionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ExecuteIssueAction(sender);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(IssueSecondaryActionButton_Click), ex);
        }
    }

    private void ExecuteIssueAction(object sender)
    {
        if (sender is not FrameworkElement { Tag: WorkspaceIssueAction action })
            return;

        switch (action.Kind)
        {
            case WorkspaceIssueActionKind.CopyText:
                if (string.IsNullOrWhiteSpace(action.Argument))
                    return;
                Clipboard.SetText(action.Argument);
                SetInstallStatus(BuildIssueActionStatusMessage(action, launched: false));
                break;

            case WorkspaceIssueActionKind.LaunchPowerShellCommand:
                if (string.IsNullOrWhiteSpace(action.Argument))
                    return;
                if (string.Equals(action.Label, "Install PowerShell 7", StringComparison.OrdinalIgnoreCase))
                    _pendingPowerShellInstallRecheck = true;
                _squadCliAdapter.LaunchPowerShellCommandWindow(action);
                SetInstallStatus(BuildIssueActionStatusMessage(action, launched: true));
                break;
        }
    }

    private static string BuildIssueActionStatusMessage(WorkspaceIssueAction action, bool launched)
    {
        if (action.Kind == WorkspaceIssueActionKind.CopyText)
            return "Copied the command to the clipboard.";

        return action.Label switch
        {
            "Run Build in PowerShell" => "Opened a PowerShell window to run the build check.",
            "Install PowerShell 7" => "Opened a PowerShell window to install PowerShell 7.",
            _ => launched
                ? $"Opened a PowerShell window for {action.Label.ToLowerInvariant()}."
                : $"Completed {action.Label.ToLowerInvariant()}."
        };
    }

    private void UpdateInteractiveControlState()
    {
        var state = InteractiveControlStateCalculator.Calculate(
            hasWorkspace: _currentWorkspace is not null,
            squadInstalled: _currentInstallationState?.IsSquadInstalledForActiveDirectory == true,
            isInstallingSquad: _isInstallingSquad,
            isPromptRunning: _isPromptRunning,
            canAbortBackgroundTask: _backgroundTaskPresenter.GetAbortTargets().Count > 0,
            currentPromptText: PromptTextBox.Text);

        StatusAgentPanelsGrid.IsEnabled = state.AgentItemsEnabled;
        ActiveAgentItemsControl.IsEnabled = state.AgentItemsEnabled;
        InactiveAgentItemsControl.IsEnabled = state.AgentItemsEnabled;
        OutputTextBox.IsEnabled = state.OutputEnabled;
        PromptTextBox.IsEnabled = state.PromptEnabled;
        RunButton.IsEnabled = state.RunEnabled
            || ((_isPromptRunning || IsLoopRunning) && _currentWorkspace is not null);
        AbortButton.IsEnabled = state.AbortEnabled;
        if (RunDoctorMenuItem is not null)
            RunDoctorMenuItem.IsEnabled = state.RunDoctorEnabled;
        InstallSquadButton.IsEnabled = state.InstallSquadEnabled;
        IssueHelpButton.IsEnabled = true;
        IssueActionButton.IsEnabled = true;
        IssueSecondaryActionButton.IsEnabled = true;
        IssuePrimaryLinkButton.IsEnabled = true;
        IssueSecondaryLinkButton.IsEnabled = true;
    }

    private void UpdateWindowTitle()
    {
        var solutionDisplay = _currentSolutionName is { Length: > 0 }
            ? Path.GetFileNameWithoutExtension(_currentSolutionName)
            : null;
        Title = solutionDisplay is { Length: > 0 }
            ? $"SquadDash - {solutionDisplay}"
            : _currentWorkspace is { FolderPath.Length: > 0 }
                ? $"SquadDash - {Path.GetFileName(_currentWorkspace.FolderPath)}"
                : "SquadDash";
        // Keep the titlebar workspace label in sync
        WorkspaceTitleDisplay = solutionDisplay is { Length: > 0 }
            ? solutionDisplay
            : _currentWorkspace is { FolderPath.Length: > 0 }
                ? Path.GetFileName(_currentWorkspace.FolderPath)
                : "Squad Dash";
    }

    // -----------------------------------------------------------------------
    // Custom titlebar — WorkspaceTitleDisplay dependency property
    // -----------------------------------------------------------------------

    public static readonly DependencyProperty WorkspaceTitleDisplayProperty =
        DependencyProperty.Register(
            nameof(WorkspaceTitleDisplay),
            typeof(string),
            typeof(MainWindow),
            new PropertyMetadata("Squad Dash"));

    public string WorkspaceTitleDisplay
    {
        get => (string)GetValue(WorkspaceTitleDisplayProperty);
        set => SetValue(WorkspaceTitleDisplayProperty, value);
    }

    // -----------------------------------------------------------------------
    // Custom titlebar — caption button handlers
    // -----------------------------------------------------------------------

    private void WorkspaceTitleText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var path = _currentSolutionPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                // Fall back to folder if no solution file
                var folder = _currentWorkspace?.FolderPath;
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    Process.Start("explorer.exe", folder);
                return;
            }
            Process.Start("explorer.exe", $"/select,\"{path}\"");
            e.Handled = true;
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(WorkspaceTitleText_MouseLeftButtonUp), ex);
        }
    }

    private void WorkspaceTitleText_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        var showItem = new MenuItem { Header = "Show in Explorer" };
        showItem.Click += (_, _) => WorkspaceTitleText_MouseLeftButtonUp(sender, e);
        menu.Items.Add(showItem);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void VersionTextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ShowVersionContextMenu();
        e.Handled = true;
    }

    private void VersionTextBlock_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ShowVersionContextMenu();
        e.Handled = true;
    }

    private void SquadUpdateBadge_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ShowVersionContextMenu();
        e.Handled = true;
    }

    private void ShowVersionContextMenu()
    {
        var menu = new ContextMenu();
        var copyItem = new MenuItem { Header = "Copy Squad system info" };
        copyItem.Click += (_, _) => CopySquadSystemInfoToClipboard();
        menu.Items.Add(copyItem);

        var latestVersion = _squadCliAdapter.LatestSquadVersion;
        if (!string.IsNullOrWhiteSpace(latestVersion) && IsNewerSquadVersion(latestVersion, _squadCliAdapter.SquadVersion))
        {
            menu.Items.Add(new Separator());
            var updateItem = new MenuItem { Header = $"Update Squad CLI to v{latestVersion}" };
            updateItem.Click += (_, _) => RunSquadCliUpdate(latestVersion);
            menu.Items.Add(updateItem);
        }

        menu.IsOpen = true;
    }

    private void UpdateSquadUpdateBadge()
    {
        if (SquadUpdateBadge is null)
            return;
        var latestVersion = _squadCliAdapter.LatestSquadVersion;
        var installedVersion = _squadCliAdapter.SquadVersion;
        var hasUpdate = !string.IsNullOrWhiteSpace(latestVersion) && IsNewerSquadVersion(latestVersion, installedVersion);
        SquadUpdateBadge.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
        if (hasUpdate)
            SquadUpdateBadge.ToolTip = $"Squad CLI v{latestVersion} available — click to update";
    }

    private void RunSquadCliUpdate(string targetVersion)
    {
        var action = new WorkspaceIssueAction(
            "Update Squad CLI",
            WorkspaceIssueActionKind.LaunchPowerShellCommand,
            $"npm install @bradygaster/squad-cli@{targetVersion}");
        _squadCliAdapter.LaunchPowerShellCommandWindow(action);
    }

    private static bool IsNewerSquadVersion(string candidate, string? current)
    {
        if (string.IsNullOrWhiteSpace(current))
            return false;
        var a = ParseSimpleVersion(candidate);
        var b = ParseSimpleVersion(current);
        if (a is null || b is null)
            return false;
        for (var i = 0; i < 3; i++)
        {
            if (a[i] > b[i]) return true;
            if (a[i] < b[i]) return false;
        }
        return false;
    }

    private static int[]? ParseSimpleVersion(string v)
    {
        var parts = v.TrimStart('v').Split('.');
        if (parts.Length < 3)
            return null;
        var result = new int[3];
        for (var i = 0; i < 3; i++)
        {
            var numPart = parts[i].Split('-')[0];
            if (!int.TryParse(numPart, out result[i]))
                return null;
        }
        return result;
    }

    private void CopySquadSystemInfoToClipboard()
    {
        try
        {
            var squadVersion = _squadCliAdapter.SquadVersion;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"SquadDash version: {AppVersion.Full}");
            sb.AppendLine($"Squad version:     {(string.IsNullOrWhiteSpace(squadVersion) ? "(unknown)" : squadVersion)}");
            sb.AppendLine($"Workspace folder:  {_currentWorkspace?.FolderPath ?? "(none)"}");
            if (!string.IsNullOrWhiteSpace(_currentSolutionPath))
                sb.AppendLine($"Solution file:     {_currentSolutionPath}");
            sb.AppendLine($"OS:                {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            sb.AppendLine($".NET runtime:      {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"Architecture:      {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            Clipboard.SetText(sb.ToString());
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(CopySquadSystemInfoToClipboard), ex);
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SystemCommands.MinimizeWindow(this);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(MinimizeButton_Click), ex);
        }
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(this);
            else
                SystemCommands.MaximizeWindow(this);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(MaximizeRestoreButton_Click), ex);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SystemCommands.CloseWindow(this);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(CloseButton_Click), ex);
        }
    }

    private void TitlebarGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            // WindowChrome handles native drag for non-interactive areas (CaptionHeight region).
            // This handler catches double-click on the background to maximize/restore.
            if (e.ClickCount == 2 && e.OriginalSource is Grid)
            {
                if (WindowState == WindowState.Maximized)
                    SystemCommands.RestoreWindow(this);
                else
                    SystemCommands.MaximizeWindow(this);
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(TitlebarGrid_MouseLeftButtonDown), ex);
        }
    }

    private void UpdateMaximizeRestoreIcon()
    {
        if (MaximizeIconCanvas is null) return;
        if (WindowState == WindowState.Maximized)
        {
            MaximizeIconCanvas.Visibility = Visibility.Collapsed;
            RestoreIconCanvas.Visibility = Visibility.Visible;
            MaximizeRestoreButton.ToolTip = "Restore";
        }
        else
        {
            MaximizeIconCanvas.Visibility = Visibility.Visible;
            RestoreIconCanvas.Visibility = Visibility.Collapsed;
            MaximizeRestoreButton.ToolTip = "Maximize";
        }
    }

    private void ActivateOwnedWindow()
    {
        if (_isClosing)
            return;

        try
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            if (!IsVisible)
                Show();

            Activate();

            var wasTopmost = Topmost;
            if (!wasTopmost)
            {
                Topmost = true;
                Topmost = false;
            }

            var handle = new WindowInteropHelper(this).Handle;
            if (handle != nint.Zero)
                NativeMethods.TryActivateWindow(handle);

            Focus();
            if (PromptTextBox.IsEnabled)
                PromptTextBox.Focus();
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("Workspace", $"Window activation failed: {ex.Message}");
        }
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        try
        {
            _isClosing = true;
            _promptHealthTimer.Stop();
            _statusPresentationTimer.Stop();
            _speechService?.Dispose();
            _speechService = null;
            var pendingPlacement = _pendingWindowPlacement;
            var pendingUtilityWindowState = _pendingUtilityWindowState;
            var pendingDocsPanelState = _pendingDocsPanelState;
            var pendingConversation = _conversationManager.PendingConversationSave;
            await Task.WhenAll(
                Task.Run(RemoveRunningInstanceRegistration),
                Task.Run(() =>
                {
                    if (pendingPlacement is { } p)
                        _settingsStore.SaveWindowPlacement(p.FolderPath, p.Placement);
                }),
                Task.Run(() =>
                {
                    if (pendingUtilityWindowState is { } u)
                        _settingsStore.SaveUtilityWindowState(u.TasksOpen, u.TraceOpen);
                }),
                Task.Run(() =>
                {
                    if (pendingDocsPanelState is { } docs)
                        _settingsStore.SaveDocsPanelState(_currentWorkspace?.FolderPath, new WorkspaceDocsPanelState
                        {
                            Open = docs.Open ? null : false,
                            ExpandedNodes = docs.ExpandedNodes,
                            SelectedTopic = docs.SelectedTopic,
                            PanelWidth = docs.DocsPanelWidth,
                            PanelWidthFraction = docs.DocsPanelWidthFraction,
                            SourceOpen = docs.DocsSourceOpen,
                            SourceWidth = docs.DocsSourceWidth,
                        });
                }),
                Task.Run(() =>
                {
                    if (pendingConversation is { } c)
                        _conversationManager.ConversationStore.Save(c.FolderPath, c.State);
                }),
                _bridge.DisposeAsync().AsTask(),
                _instanceActivationChannel.DisposeAsync().AsTask());
            _workspaceOwnershipLease?.Dispose();
            _workspaceOwnershipLease = null;
            _startupWorkspaceLease?.Dispose();
            _startupWorkspaceLease = null;
            DisposeInboxWatcher();
            DisposeTeamFileWatcher();
            DisposeRestartRequestWatcher();
            DisposeDocsWatcher();
            _toolSpinnerTimer.Stop();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException("Shutdown", ex, showDialog: false);
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        try
        {
            // If a deferred shutdown was already scheduled and has now fired, honour it — skip dialog.
            bool isDeferredClose = _deferredShutdown != DeferredShutdownMode.None;

            if (_pttState == PttState.Active)
            {
                e.Cancel = true;
                _restartPending = true;
                SquadDashTrace.Write("Shutdown", "Close requested while PTT voice recording is active. Deferring restart until recording stops.");
                _conversationManager.EmergencySave();
                return;
            }

            bool isBusy = _isPromptRunning || IsNativeLoopRunning || _promptQueue.Count > 0;
            if (isBusy && !isDeferredClose)
            {
                e.Cancel = true;
                _conversationManager.EmergencySave();

                var dialog = new ShutdownProtectionWindow(
                    isRunning:     _isPromptRunning,
                    hasQueue:      _promptQueue.Count > 0,
                    isLoopRunning: IsNativeLoopRunning)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() != true)
                {
                    SquadDashTrace.Write("Shutdown", "Close cancelled by user.");
                    return;
                }

                switch (dialog.Choice)
                {
                    case ShutdownChoice.CloseNow:
                        SquadDashTrace.Write("Shutdown", "User chose Close Now — proceeding immediately.");
                        e.Cancel = false;
                        break; // fall through to cleanup below

                    case ShutdownChoice.AfterCurrentTurn:
                        _deferredShutdown = DeferredShutdownMode.AfterCurrentTurn;
                        SquadDashTrace.Write("Shutdown", "Deferred shutdown scheduled: after current turn.");
                        return;

                    case ShutdownChoice.AfterAllQueued:
                        _deferredShutdown = DeferredShutdownMode.AfterAllQueued;
                        SquadDashTrace.Write("Shutdown", "Deferred shutdown scheduled: after all queued items.");
                        return;

                    default:
                        return;
                }
            }

            if (e.Cancel) return;

            _deferredShutdown = DeferredShutdownMode.None;
            _isClosing = true;
            SquadDashTrace.Write("Shutdown", "Main window closing.");
            _instanceActivationChannel.Stop();
            _conversationManager.CaptureWorkspaceInputState();
            CaptureWindowPlacement();
            _pendingUtilityWindowState = (
                _tasksStatusWindow is { IsVisible: true },
                _traceWindow is { IsVisible: true });
            // Capture docs panel state (only when panel is open; closed state is already
            // written by SetDocumentationMode when the user toggles it off).
            if (_documentationModeEnabled)
            {
                double? docsPanelWidth = null;
                double? docsPanelWidthFraction = null;
                if (DocsPanelColumn is not null && DocsPanelColumn.ActualWidth > 0)
                {
                    docsPanelWidth = DocsPanelColumn.ActualWidth;
                    if (MainGrid is not null && MainGrid.ActualWidth > 0)
                        docsPanelWidthFraction = DocsPanelColumn.ActualWidth / MainGrid.ActualWidth;
                }
                bool? docsSourceOpen = DocsSourceColumn is not null && DocsSourceColumn.ActualWidth > 0;
                double? docsSourceWidth = (DocsSourceColumn?.ActualWidth > 0) ? DocsSourceColumn.ActualWidth : null;

                _pendingDocsPanelState = (
                    Open: true,
                    ExpandedNodes: DocTopicsTreeView is not null
                        ? CollectExpandedDocNodes(DocTopicsTreeView.Items)
                        : null,
                    SelectedTopic: (DocTopicsTreeView?.SelectedItem as TreeViewItem)?.Tag as string,
                    DocsPanelWidth: docsPanelWidth,
                    DocsTopicsWidth: null,
                    DocsPanelWidthFraction: docsPanelWidthFraction,
                    DocsTopicsWidthFraction: null,
                    DocsSourceOpen: docsSourceOpen,
                    DocsSourceWidth: docsSourceWidth);

                // Save synchronously here — MainWindow_Closed is async void and may not
                // complete before the process exits.
                var s = _pendingDocsPanelState.Value;
                _settingsStore.SaveDocsPanelState(_currentWorkspace?.FolderPath, new WorkspaceDocsPanelState
                {
                    Open = s.Open ? null : false,
                    ExpandedNodes = s.ExpandedNodes,
                    SelectedTopic = s.SelectedTopic,
                    PanelWidth = s.DocsPanelWidth,
                    PanelWidthFraction = s.DocsPanelWidthFraction,
                    SourceOpen = s.DocsSourceOpen,
                    SourceWidth = s.DocsSourceWidth,
                });
            }
            // Write synchronously so state is on disk before the process exits.
            if (_pendingDocsPanelState is { } docs)
                _settingsStore.SaveDocsPanelState(_currentWorkspace?.FolderPath, new WorkspaceDocsPanelState
                {
                    Open = docs.Open ? null : false,
                    ExpandedNodes = docs.ExpandedNodes,
                    SelectedTopic = docs.SelectedTopic,
                    PanelWidth = docs.DocsPanelWidth,
                    PanelWidthFraction = docs.DocsPanelWidthFraction,
                    SourceOpen = docs.DocsSourceOpen,
                    SourceWidth = docs.DocsSourceWidth,
                });
            _conversationManager.EmergencySave();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(MainWindow_Closing), ex, showDialog: false);
        }
    }

    /// <summary>
    /// Synchronously flushes all conversation state (including any in-flight turn) to disk.
    /// Safe to call from Closing, unhandled exception handlers, or any crash path.
    /// </summary>

    private void UpdateRunningInstanceRegistration()
    {
        try
        {
            var launchFolder = _currentWorkspace?.FolderPath;
            if (string.IsNullOrWhiteSpace(launchFolder))
            {
                launchFolder = StartupWorkspaceResolver.Resolve(
                    null,
                    _settingsSnapshot.LastOpenedFolder,
                    TryGetApplicationRoot()) ?? Environment.CurrentDirectory;
            }

            _instanceRegistry.Upsert(new RunningInstanceRecord(
                _workspacePaths.ApplicationRoot,
                launchFolder,
                Environment.ProcessId,
                _processStartedAtUtcTicks,
                DateTimeOffset.UtcNow.Ticks)
            {
                ActiveWorkspaceFolder = _currentWorkspace?.FolderPath
            });
        }
        catch
        {
        }
    }

    private void CaptureWindowPlacement()
    {
        if (_currentWorkspace is null)
            return;

        try
        {
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            _pendingWindowPlacement = (
                _currentWorkspace.FolderPath,
                new WorkspaceWindowPlacement(
                    bounds.Left,
                    bounds.Top,
                    bounds.Width,
                    bounds.Height,
                    WindowState == WindowState.Maximized));
        }
        catch
        {
        }
    }

    private void SaveWorkspaceWindowPlacement()
    {
        if (_currentWorkspace is null)
            return;

        try
        {
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            _settingsSnapshot = _settingsStore.SaveWindowPlacement(
                _currentWorkspace.FolderPath,
                new WorkspaceWindowPlacement(
                    bounds.Left,
                    bounds.Top,
                    bounds.Width,
                    bounds.Height,
                    WindowState == WindowState.Maximized));
        }
        catch
        {
        }
    }

    private void RestoreWorkspaceWindowPlacement()
    {
        if (_currentWorkspace is null)
            return;

        if (!_settingsSnapshot.WindowPlacementByWorkspace.TryGetValue(_currentWorkspace.FolderPath, out var placement))
            return;

        if (!placement.IsUsable)
            return;

        WindowState = WindowState.Normal;
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;

        if (!IsPlacementOnScreen(placement))
        {
            Left = SystemParameters.WorkArea.Left;
            Top = SystemParameters.WorkArea.Top;
        }

        if (placement.IsMaximized)
            WindowState = WindowState.Maximized;
    }

    private static bool IsPlacementOnScreen(WorkspaceWindowPlacement placement)
    {
        // Check that a usable strip across the top of the window (title bar area) intersects
        // at least one monitor's working area, so the user can always grab and move the window.
        var titleBarLeft = (int)placement.Left;
        var titleBarTop = (int)placement.Top;
        var titleBarRight = (int)(placement.Left + Math.Min(placement.Width, 200));
        var titleBarBottom = (int)(placement.Top + 30);

        return NativeMethods.IsRectOnAnyMonitor(titleBarLeft, titleBarTop, titleBarRight, titleBarBottom);
    }

    private void RemoveRunningInstanceRegistration()
    {
        try
        {
            _instanceRegistry.Remove(
                _workspacePaths.ApplicationRoot,
                Environment.ProcessId,
                _processStartedAtUtcTicks);
        }
        catch
        {
        }
    }

    internal void ReportUnhandledUiException(string operation, Exception ex, bool showPanel = true)
    {
        if (Dispatcher.CheckAccess())
        {
            HandleUiCallbackException(operation, ex, showDialog: showPanel);
            return;
        }

        TryPostToUi(
            () => HandleUiCallbackException(operation, ex, showDialog: showPanel),
            $"Unhandled.{operation}");
    }

    private void HandleUiCallbackException(string operation, Exception ex, bool showDialog = true)
    {
        SquadDashTrace.Write("UI", $"{operation} callback failed: {ex}");

        if (!showDialog || _isClosing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        try
        {
            ShowExceptionPanel(operation, ex);
        }
        catch (Exception panelEx)
        {
            SquadDashTrace.Write("UI", $"Failed to show exception panel for {operation}: {panelEx}");

            if (!CanShowOwnedWindow())
                return;

            try
            {
                MessageBox.Show(
                    this,
                    $"{operation} failed.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    operation,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
            }
        }
    }

    private void ShowExceptionPanel(string operation, Exception ex)
    {
        var title = $"{operation} failed";
        var summary = string.IsNullOrWhiteSpace(ex.Message)
            ? "An unexpected error occurred."
            : ex.Message.Trim();
        var details = BuildExceptionPanelDetails(operation, ex);

        _activeUiException = new UiExceptionPanelState(title, summary, details);
        ExceptionPanelTitleTextBlock.Text = title;
        ExceptionPanelSummaryTextBlock.Text = summary;
        ExceptionPanelTextBox.Text = details;
        ExceptionPanelTextBox.ScrollToHome();
        ExceptionPanelBorder.Visibility = Visibility.Visible;
        UpdateLeadAgent("Error", string.Empty, summary);
        UpdateSessionState("Error");
    }

    private static string BuildExceptionPanelDetails(string operation, Exception ex)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Operation: {operation}");
        builder.AppendLine($"Occurred: {DateTimeOffset.Now:O}");
        builder.AppendLine();
        builder.AppendLine(ex.ToString());
        return builder.ToString().TrimEnd();
    }

    private void CopyExceptionDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeUiException is null || string.IsNullOrWhiteSpace(_activeUiException.Details))
                return;

            Clipboard.SetText(_activeUiException.Details);
            SquadDashTrace.Write("UI", "Copied exception details to clipboard.");
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(CopyExceptionDetailsButton_Click), ex);
        }
    }

    private void DismissExceptionPanelButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DismissExceptionPanel();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(DismissExceptionPanelButton_Click), ex);
        }
    }

    private void DismissExceptionPanel()
    {
        _activeUiException = null;
        ExceptionPanelTextBox.Clear();
        ExceptionPanelSummaryTextBlock.Text = string.Empty;
        ExceptionPanelTitleTextBlock.Text = "Unexpected error";
        ExceptionPanelBorder.Visibility = Visibility.Collapsed;
    }

    private static PromptInputKey MapPromptInputKey(Key key)
    {
        return key switch
        {
            Key.Return or Key.Enter => PromptInputKey.Enter,
            Key.Up => PromptInputKey.Up,
            Key.Down => PromptInputKey.Down,
            Key.Tab => PromptInputKey.Tab,
            Key.Escape => PromptInputKey.Escape,
            _ => PromptInputKey.Other
        };
    }

    private bool IsMultiLinePrompt()
    {
        return PromptTextBox.LineCount > 1 || PromptTextBox.Text.Contains('\n');
    }

    private void HideHistoryHint()
    {
        _historyHintTimer.Stop();
        HistoryHintBorder.Visibility = Visibility.Collapsed;
    }

    private MenuItem? OpenSquadFolderMenuItem;

    private void RefreshSidebar()
    {
        ClearWorkspaceMenuFileItems();
        DisposeInboxWatcher();

        if (_currentWorkspace is null)
        {
            OpenSquadFolderMenuItem?.IsEnabled = false;
            UpdateInteractiveControlState();
            SyncTasksPanel();
            return;
        }

        RefreshInstallationState();

        var squadRoot = _currentWorkspace.SquadFolderPath;
        var squadFolderExists = Directory.Exists(squadRoot);
        ConfigureTeamFileWatcher();

        foreach (var relativePath in new[] {
                     "ceremonies.md",
                     "decisions.md",
                     "history.md",
                     Path.Combine("identity", "now.md"),
                     "routing.md",
                     Path.Combine("skills", "project-conventions", "SKILL.md"),
                     "team.md",
                     Path.Combine("identity", "wisdom.md")
                 }.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            AddWorkspaceFileMenuItem(relativePath);
        }

        AddWorkspaceMenuSeparator();

        OpenSquadFolderMenuItem = new MenuItem
        {
            Header = "📂 _Squad Folder",
            Name = "OpenSquadFolderMenuItem",
            IsEnabled = squadFolderExists,
            Style = (Style)FindResource("ThemedMenuItemStyle")
        };
        OpenSquadFolderMenuItem.Click += OpenSquadFolderMenuItem_Click;
        WorkspaceMenuItem.Items.Add(OpenSquadFolderMenuItem);

        AddWorkspaceFolderMenuItem(Path.Combine("decisions", "inbox"));

        var loopMdPath = Path.Combine(_currentWorkspace.SquadFolderPath, "loop.md");
        var loopMenuItem = new MenuItem
        {
            Header = "🔁 loop.md",
            Style = (Style)FindResource("ThemedMenuItemStyle")
        };
        loopMenuItem.Click += (_, _) => OpenOrCreateLoopMd(loopMdPath);
        WorkspaceMenuItem.Items.Add(loopMenuItem);

        UpdateLoopPanelButtonStates();

        var tasksMdPath = Path.Combine(_currentWorkspace.SquadFolderPath, "tasks.md");
        if (File.Exists(tasksMdPath))
        {
            var tasksEntry = new SidebarEntry("📋 tasks.md", string.Empty, tasksMdPath, true, SidebarEntryKind.File);
            AddWorkspaceEntryMenuItem(tasksEntry);
        }
        _pec.TasksFilePath = File.Exists(tasksMdPath) ? tasksMdPath : null;

        AddWorkspaceMenuSeparator();

        var squadCliMenuItem = new MenuItem
        {
            Header = "Squad _CLI",
            Style = (Style)FindResource("ThemedMenuItemStyle")
        };
        squadCliMenuItem.Click += SquadCliMenuItem_Click;
        WorkspaceMenuItem.Items.Add(squadCliMenuItem);

        _remoteAccessMenuItem = new MenuItem
        {
            Header = "Start _Remote Access",
            Style = (Style)FindResource("ThemedMenuItemStyle")
        };
        _remoteAccessMenuItem.Click += RemoteAccessMenuItem_Click;
        WorkspaceMenuItem.Items.Add(_remoteAccessMenuItem);

        ConfigureInboxWatcher(Path.Combine(squadRoot, "decisions", "inbox"));
        UpdateInteractiveControlState();
        SyncTasksPanel();
    }

    private void ClearWorkspaceMenuFileItems()
    {
        // WorkspaceMenuItem has no static XAML children — every item is dynamic,
        // so a full clear is both correct and simpler than tag-filtering.
        WorkspaceMenuItem.Items.Clear();
    }

    private void AddWorkspaceFileMenuItem(string relativePath)
    {
        if (_currentWorkspace is null)
            return;

        var path = Path.Combine(_currentWorkspace.SquadFolderPath, relativePath);
        if (!File.Exists(path))
            return;

        var entry = new SidebarEntry(
            "📄" + Path.GetFileName(relativePath),
            string.Empty,
            path,
            true,
            SidebarEntryKind.File);
        AddWorkspaceEntryMenuItem(entry);
    }

    private void AddWorkspaceFolderMenuItem(string relativePath)
    {
        if (_currentWorkspace is null)
            return;

        var path = Path.Combine(_currentWorkspace.SquadFolderPath, relativePath);
        if (!Directory.Exists(path))
            return;

        var fileCount = CountFiles(path);
        var entry = new SidebarEntry(
            $"📂{Path.GetFileName(relativePath)} folder ({fileCount})",
            string.Empty,
            path,
            true,
            SidebarEntryKind.Folder);
        AddWorkspaceEntryMenuItem(entry);
    }

    private void AddWorkspaceEntryMenuItem(SidebarEntry entry)
    {
        //WorkspaceFileSeparator.Visibility = Visibility.Visible;
        var item = new MenuItem
        {
            Header = entry.Title,
            Tag = entry,
            Style = (Style)FindResource("ThemedMenuItemStyle")
        };
        item.Click += (_, _) => OpenSidebarEntry(entry);
        WorkspaceMenuItem.Items.Add(item);
    }

    private void OpenOrCreateLoopMd(string loopMdPath)
    {
        try
        {
            if (!File.Exists(loopMdPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(loopMdPath)!);
                File.WriteAllText(loopMdPath, """
                    # Loop Instructions

                    You are running in autonomous loop mode. On each iteration:

                    1. Check for outstanding tasks in `.squad/tasks.md`
                    2. Pick the highest-priority unchecked item
                    3. Work on it and mark it `[x]` when done
                    4. Report what you accomplished

                    Stop looping when all tasks are complete or when instructed.
                    """);
                UpdateLoopPanelButtonStates();
            }

            OpenMarkdownFile(loopMdPath, "Loop Instructions", showSource: true);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(OpenOrCreateLoopMd), ex);
        }
    }

    private void UpdateLoopPanelButtonStates()
    {
        if (_currentWorkspace is null) return;
        var loopMdPath = Path.Combine(_currentWorkspace.SquadFolderPath, "loop.md");
        var loopExists = File.Exists(loopMdPath);

        if (StartLoopButton is not null)
            StartLoopButton.IsEnabled = loopExists;

        if (CreateEditLoopFileButton is not null)
            CreateEditLoopFileButton.Content = loopExists ? "📝 Edit Loop File" : "📝 Create Loop File";
    }

    private void CreateEditLoopFileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentWorkspace is null) return;
            var loopMdPath = Path.Combine(_currentWorkspace.SquadFolderPath, "loop.md");
            OpenOrCreateLoopMd(loopMdPath);
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(CreateEditLoopFileButton_Click), ex);
        }
    }

    private void ConfigureInboxWatcher(string inboxPath)
    {
        DisposeInboxWatcher();
        if (!Directory.Exists(inboxPath))
            return;

        _inboxWatcher = new FileSystemWatcher(inboxPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
        };
        _inboxWatcher.Created += InboxWatcher_Changed;
        _inboxWatcher.Deleted += InboxWatcher_Changed;
        _inboxWatcher.Renamed += InboxWatcher_Renamed;
        _inboxWatcher.Changed += InboxWatcher_Changed;
        _inboxWatcher.EnableRaisingEvents = true;
    }

    private void ConfigureTeamFileWatcher()
    {
        DisposeTeamFileWatcher();
        if (_currentWorkspace is null)
            return;

        var squadFolderPath = _currentWorkspace.SquadFolderPath;
        if (!Directory.Exists(squadFolderPath))
            return;

        _teamFileWatcher = new FileSystemWatcher(squadFolderPath, "*.md")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };
        _teamFileWatcher.Changed += TeamFileWatcher_Changed;
        _teamFileWatcher.Created += TeamFileWatcher_Changed;
        _teamFileWatcher.Deleted += TeamFileWatcher_Changed;
        _teamFileWatcher.Renamed += TeamFileWatcher_Renamed;
        _teamFileWatcher.EnableRaisingEvents = true;
    }

    private void InboxWatcher_Changed(object sender, FileSystemEventArgs e)
    {
        try
        {
            TryPostToUi(RefreshSidebar, "InboxWatcher.Changed");
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(InboxWatcher_Changed), ex, showDialog: false);
        }
    }

    private void InboxWatcher_Renamed(object sender, RenamedEventArgs e)
    {
        try
        {
            TryPostToUi(RefreshSidebar, "InboxWatcher.Renamed");
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(InboxWatcher_Renamed), ex, showDialog: false);
        }
    }

    private void TeamFileWatcher_Changed(object sender, FileSystemEventArgs e)
    {
        try
        {
            TryPostToUi(() => HandleSquadMarkdownWatcherChange(e.FullPath), "TeamFileWatcher.Changed");
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(TeamFileWatcher_Changed), ex, showDialog: false);
        }
    }

    private void TeamFileWatcher_Renamed(object sender, RenamedEventArgs e)
    {
        try
        {
            TryPostToUi(() => HandleSquadMarkdownWatcherRename(e.OldFullPath, e.FullPath), "TeamFileWatcher.Renamed");
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(TeamFileWatcher_Renamed), ex, showDialog: false);
        }
    }

    private void HandleSquadMarkdownWatcherChange(string? fullPath)
    {
        if (_currentWorkspace is null) return;

        // Always check if loop.md existence changed so loop panel buttons stay current.
        if (fullPath is not null &&
            fullPath.EndsWith("loop.md", StringComparison.OrdinalIgnoreCase))
        {
            UpdateLoopPanelButtonStates();
        }

        // Reload the tasks panel whenever tasks.md changes.
        if (fullPath is not null &&
            fullPath.EndsWith("tasks.md", StringComparison.OrdinalIgnoreCase) &&
            _tasksPanelVisible)
        {
            LoadTasksPanel();
        }

        if (!RoutingIssueWatchPathPolicy.IsRelevantPath(_currentWorkspace.SquadFolderPath, fullPath))
            return;

        ScheduleAgentRefreshFromTeamWatcher();
    }

    private void HandleSquadMarkdownWatcherRename(string? oldFullPath, string? newFullPath)
    {
        if (_currentWorkspace is null)
            return;

        if (!RoutingIssueWatchPathPolicy.IsRelevantPath(_currentWorkspace.SquadFolderPath, oldFullPath) &&
            !RoutingIssueWatchPathPolicy.IsRelevantPath(_currentWorkspace.SquadFolderPath, newFullPath))
        {
            return;
        }

        ScheduleAgentRefreshFromTeamWatcher();
    }

    private void ScheduleAgentRefreshFromTeamWatcher()
    {
        _teamRefreshDebounceTimer.Stop();
        _teamRefreshDebounceTimer.Start();
    }

    private void TeamRefreshDebounceTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            _teamRefreshDebounceTimer.Stop();
            RefreshInstallationState();
            RefreshAgentCards();
            RefreshSidebar();
            MaybePublishRoutingIssueSystemEntry("team-files-changed");
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(TeamRefreshDebounceTimer_Tick), ex);
        }
    }

    private void DisposeInboxWatcher()
    {
        if (_inboxWatcher is null)
            return;

        _inboxWatcher.EnableRaisingEvents = false;
        _inboxWatcher.Created -= InboxWatcher_Changed;
        _inboxWatcher.Deleted -= InboxWatcher_Changed;
        _inboxWatcher.Renamed -= InboxWatcher_Renamed;
        _inboxWatcher.Changed -= InboxWatcher_Changed;
        _inboxWatcher.Dispose();
        _inboxWatcher = null;
    }

    private void DisposeTeamFileWatcher()
    {
        _teamRefreshDebounceTimer.Stop();

        if (_teamFileWatcher is null)
            return;

        _teamFileWatcher.EnableRaisingEvents = false;
        _teamFileWatcher.Changed -= TeamFileWatcher_Changed;
        _teamFileWatcher.Created -= TeamFileWatcher_Changed;
        _teamFileWatcher.Deleted -= TeamFileWatcher_Changed;
        _teamFileWatcher.Renamed -= TeamFileWatcher_Renamed;
        _teamFileWatcher.Dispose();
        _teamFileWatcher = null;
    }

    private void ConfigureRestartRequestWatcher()
    {
        DisposeRestartRequestWatcher();

        try
        {
            var requestPath = _restartCoordinatorStateStore.GetRequestPathForWatcher(_workspacePaths.ApplicationRoot);
            var directory = Path.GetDirectoryName(requestPath);
            var fileName = Path.GetFileName(requestPath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
                return;

            Directory.CreateDirectory(directory);
            _lastHandledRestartRequestId = _restartCoordinatorStateStore.LoadRequest(_workspacePaths.ApplicationRoot)?.RequestId;

            _restartRequestWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };
            _restartRequestWatcher.Changed += RestartRequestWatcher_Changed;
            _restartRequestWatcher.Created += RestartRequestWatcher_Changed;
            _restartRequestWatcher.Renamed += RestartRequestWatcher_Renamed;
            _restartRequestWatcher.EnableRaisingEvents = true;
        }
        catch
        {
        }
    }

    private void DisposeRestartRequestWatcher()
    {
        if (_restartRequestWatcher is null)
            return;

        _restartRequestWatcher.EnableRaisingEvents = false;
        _restartRequestWatcher.Changed -= RestartRequestWatcher_Changed;
        _restartRequestWatcher.Created -= RestartRequestWatcher_Changed;
        _restartRequestWatcher.Renamed -= RestartRequestWatcher_Renamed;
        _restartRequestWatcher.Dispose();
        _restartRequestWatcher = null;
    }

    private void RestartRequestWatcher_Changed(object sender, FileSystemEventArgs e)
    {
        try
        {
            TryPostToUi(HandleRestartRequestChanged, "RestartRequestWatcher.Changed");
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(RestartRequestWatcher_Changed), ex, showDialog: false);
        }
    }

    private void RestartRequestWatcher_Renamed(object sender, RenamedEventArgs e)
    {
        try
        {
            TryPostToUi(HandleRestartRequestChanged, "RestartRequestWatcher.Renamed");
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(RestartRequestWatcher_Renamed), ex, showDialog: false);
        }
    }

    private void ConfigureDocsWatcher()
    {
        DisposeDocsWatcher();

        try
        {
            var docsPath = DocTopicsLoader.FindDocsFolderPath();
            if (string.IsNullOrEmpty(docsPath) || !Directory.Exists(docsPath))
                return;

            _docsWatcher = new FileSystemWatcher(docsPath, "*.md")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };
            _docsWatcher.Created += DocsWatcher_Changed;
            _docsWatcher.Deleted += DocsWatcher_Changed;
            _docsWatcher.Renamed += DocsWatcher_Renamed;
            _docsWatcher.Changed += DocsWatcher_Changed;
            _docsWatcher.EnableRaisingEvents = true;
        }
        catch
        {
        }
    }

    private void DisposeDocsWatcher()
    {
        if (_docsWatcher is null)
            return;

        _docsWatcher.EnableRaisingEvents = false;
        _docsWatcher.Created -= DocsWatcher_Changed;
        _docsWatcher.Deleted -= DocsWatcher_Changed;
        _docsWatcher.Renamed -= DocsWatcher_Renamed;
        _docsWatcher.Changed -= DocsWatcher_Changed;
        _docsWatcher.Dispose();
        _docsWatcher = null;
        _docsRefreshCts?.Cancel();
        _docsRefreshCts?.Dispose();
        _docsRefreshCts = null;
    }

    private void DocsWatcher_Changed(object sender, FileSystemEventArgs e)
    {
        try
        {
            // Skip refreshes for the file we're actively editing in the source panel —
            // the change was caused by our own debounced save, not an external modification.
            if (!string.IsNullOrEmpty(_currentDocPath)
                && string.Equals(e.FullPath, _currentDocPath, StringComparison.OrdinalIgnoreCase))
                return;

            ScheduleDebouncedDocsRefresh();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(DocsWatcher_Changed), ex, showDialog: false);
        }
    }

    private void DocsWatcher_Renamed(object sender, RenamedEventArgs e)
    {
        try
        {
            ScheduleDebouncedDocsRefresh();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(DocsWatcher_Renamed), ex, showDialog: false);
        }
    }

    private void ScheduleDebouncedDocsRefresh()
    {
        // Cancel any pending refresh
        _docsRefreshCts?.Cancel();
        _docsRefreshCts?.Dispose();

        var cts = new CancellationTokenSource();
        _docsRefreshCts = cts;

        // Schedule refresh after 150ms debounce
        Task.Delay(150, cts.Token).ContinueWith(async _ =>
        {
            if (!cts.Token.IsCancellationRequested)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        PopulateDocumentationTopics();
                    }
                    catch (Exception ex)
                    {
                        HandleUiCallbackException(nameof(ScheduleDebouncedDocsRefresh), ex, showDialog: false);
                    }
                }, System.Windows.Threading.DispatcherPriority.Normal, cts.Token);
            }
        }, TaskScheduler.Default);
    }

    private void HandleRestartRequestChanged()
    {
        var request = _restartCoordinatorStateStore.LoadRequest(_workspacePaths.ApplicationRoot);
        if (request is null || string.Equals(request.RequestId, _lastHandledRestartRequestId, StringComparison.Ordinal))
            return;

        _lastHandledRestartRequestId = request.RequestId;
        _restartPending = true;

        if (_isPromptRunning)
        {
            SetInstallStatus("Build finished. Restart will happen after the current Squad turn completes.");
            UpdateSessionState("Restart pending");
            return;
        }

        if (_pttState == PttState.Active)
        {
            SetInstallStatus("Build finished. Restart will happen after voice recording completes.");
            UpdateSessionState("Restart pending");
            return;
        }

        Close();
    }

    private void TryPostToUi(Action action, string source)
    {
        if (_isClosing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        try
        {
            if (Dispatcher.CheckAccess())
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    HandleUiCallbackException(source, ex);
                }
                return;
            }

            var sequence = _postedUiActionTracker.RegisterPostedAction();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        HandleUiCallbackException(source, ex);
                    }
                }
                finally
                {
                    _postedUiActionTracker.MarkCompleted(sequence);
                }
            }));
        }
        catch (ObjectDisposedException ex)
        {
            SquadDashTrace.Write("Shutdown", $"{source} ignored after disposal: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            SquadDashTrace.Write("Shutdown", $"{source} ignored during dispatcher shutdown: {ex.Message}");
        }
    }

    private static int CountFiles(string folderPath)
    {
        try
        {
            return Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories).Count();
        }
        catch
        {
            return 0;
        }
    }

    private void RefreshAgentCards()
    {
        if (_leadAgent is null)
        {
            _leadAgent = new AgentStatusCard(
                AgentThreadRegistry.HumanizeAgentName("Squad"),
                "S",
                "Coordinator",
                "Ready",
                string.Empty,
                string.Empty,
                LeadAgentDefaultAccentHex,
                accentStorageKey: "Squad",
                isLeadAgent: true);
            _agents.Add(_leadAgent);
        }

        ApplyAgentAccent(_leadAgent, ResolveAgentAccentHex(_leadAgent, isLeadAgent: true), persist: false);
        ApplyAgentImage(_leadAgent, ResolveAgentImagePath(_leadAgent), persist: false);

        while (_agents.Count > 1)
            _agents.RemoveAt(_agents.Count - 1);

        if (_currentWorkspace is null)
        {
            SquadDashTrace.Write("AgentCards", "RefreshAgentCards: no workspace, showing lead only.");
            _leadAgent.DetailText = string.Empty;
            UpdateAgentCardVisibility();
            ScheduleAgentPanelLayoutRefresh();
            return;
        }

        var members = _teamRosterLoader.Load(_currentWorkspace.FolderPath);
        SquadDashTrace.Write("AgentCards", $"RefreshAgentCards: workspace={_currentWorkspace.FolderPath} members={members.Count}");

        foreach (var member in members)
        {
            var card = new AgentStatusCard(
                AgentThreadRegistry.HumanizeAgentName(member.Name),
                GetAgentInitial(member.Name),
                member.Role,
                member.Status,
                string.Empty,
                string.Empty,
                ObservedAgentDefaultAccentHex,
                accentStorageKey: member.AccentKey,
                charterPath: member.CharterPath,
                historyPath: member.HistoryPath,
                folderPath: member.FolderPath,
                isCompact: member.IsUtilityAgent && !AgentRosterVisibilityPolicy.IsScribeAgent(member.Name, member.FolderPath),
                isUtilityAgent: member.IsUtilityAgent);

            ApplyAgentAccent(card, ResolveAgentAccentHex(card, isLeadAgent: false), persist: false);
            ApplyAgentImage(card, ResolveAgentImagePath(card), persist: false);
            _agents.Add(card);
        }

        UpdateAvatarSizes();
        SquadDashTrace.Write("AgentCards", $"RefreshAgentCards: total cards={_agents.Count}");
        UpdateAgentCardVisibility();
        SyncAgentCardsWithThreads();

        // Walk up the visual tree logging heights at each level
        SquadDashTrace.Write("AgentCards",
            $"Status panels: ActiveCount={_activeAgentCards.Count} InactiveCount={_inactiveAgentCards.Count} " +
            $"ActiveH={ActiveAgentItemsControl.ActualHeight:F0} ActiveViewport={ActiveAgentsScrollViewer.ActualHeight:F0} " +
            $"InactiveH={InactiveAgentItemsControl.ActualHeight:F0} InactiveViewport={InactiveAgentsScrollViewer.ActualHeight:F0} RootH={StatusAgentPanelsGrid.ActualHeight:F0}");
        System.Windows.FrameworkElement? node = StatusAgentPanelsGrid;
        for (var depth = 0; depth < 6 && node is not null; depth++)
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(node) as System.Windows.FrameworkElement;
            if (parent is null) break;
            SquadDashTrace.Write("AgentCards",
                $"  Ancestor[{depth}] {parent.GetType().Name} '{parent.Name}': " +
                $"W={parent.ActualWidth:F0} H={parent.ActualHeight:F0} DesiredH={parent.DesiredSize.Height:F0} Vis={parent.Visibility}");
            node = parent;
        }
    }

    private void UpdateLeadAgent(string status, string bubble, string detail)
    {
        if (_leadAgent is null)
            return;

        _leadAgent.StatusText = status;
        _leadAgent.BubbleText = bubble;
        _leadAgent.DetailText = detail;
    }

    private void UpdateAgentCardVisibility()
    {
        foreach (var agent in _agents)
        {
            agent.CardVisibility = AgentRosterVisibilityPolicy.ShouldShow(agent)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        SyncAgentCardBuckets();
    }

    private void SyncAgentCardBuckets()
    {
        var visibleCards = _agents
            .Where(agent => agent.CardVisibility == Visibility.Visible)
            .ToArray();
        foreach (var card in visibleCards)
        {
            card.IsInActivePanel = card.IsLeadAgent || card.Threads.Any(_backgroundTaskPresenter.IsThreadCurrentRunForDisplay);
        }

        // Active panel: stable insertion order — never reorder cards already present.
        // Only remove cards that left the active set and append cards that just entered.
        var shouldBeActive = visibleCards.Where(static c => c.IsInActivePanel).ToHashSet();
        for (var i = _activeAgentCards.Count - 1; i >= 0; i--)
        {
            if (!shouldBeActive.Contains(_activeAgentCards[i]))
                _activeAgentCards.RemoveAt(i);
        }
        var alreadyActive = _activeAgentCards.ToHashSet();
        foreach (var card in visibleCards.Where(c => c.IsInActivePanel && !alreadyActive.Contains(c))
                                         .OrderBy(GetAgentCardBucketSortKey))
            _activeAgentCards.Add(card);

        // Inactive panel: full rebuild (sorted by last activity).
        _inactiveAgentCards.Clear();
        foreach (var card in visibleCards.Where(static card => !card.IsInActivePanel).OrderBy(GetAgentCardBucketSortKey))
            _inactiveAgentCards.Add(card);

        foreach (var card in visibleCards)
        {
            var (group, sortTicks, _) = GetAgentCardBucketSortKey(card);
            var bestThread = card.Threads
                .Where(static t => !t.IsPlaceholderThread)
                .OrderByDescending(AgentThreadRegistry.GetThreadLastActivityAt)
                .FirstOrDefault();
            var lastActivity = bestThread is not null ? AgentThreadRegistry.GetThreadLastActivityAt(bestThread).ToString("o") : "(none)";
            SquadDashTrace.Write("AgentCards",
                $"SyncAgentCardBuckets: card={card.Name} group={group} sortTicks={sortTicks} " +
                $"threads={card.Threads.Count} lastActivity={lastActivity} active={card.IsInActivePanel}");
        }

        var currentActive = _activeAgentCards.ToHashSet();
        foreach (var added in currentActive.Except(_prevActiveAgentCards).Where(c => !c.IsLeadAgent))
            _selectionController.OnAgentEnteredActivePanel(added);
        foreach (var removed in _prevActiveAgentCards.Except(currentActive).Where(c => !c.IsLeadAgent))
            _selectionController.OnAgentLeftActivePanel(removed);
        _prevActiveAgentCards = currentActive;
    }

    private static (int Group, long SortTicks, string Name) GetAgentCardBucketSortKey(AgentStatusCard card) =>
        AgentCardSorting.ComputeSortKey(
            card.IsLeadAgent,
            card.IsDynamicAgent,
            card.Threads
                .Where(static t => !t.IsPlaceholderThread)
                .Select(static t => AgentThreadRegistry.GetThreadLastActivityAt(t).UtcTicks)
                .ToArray(),
            card.Name,
            isScribe: string.Equals(card.AccentStorageKey, "scribe", StringComparison.OrdinalIgnoreCase));

    private void UpdateAgentPanelWidths()
    {
        var availableWidth = StatusAgentPanelsGrid.ActualWidth;
        if (availableWidth <= 0)
            return;

        var maxActiveWidth = Math.Max(360, Math.Floor(availableWidth * 0.8));
        ActiveAgentsPanelBorder.MaxWidth = maxActiveWidth;
        ActiveAgentsColumnDefinition.Width = GridLength.Auto;
    }

    private void ScheduleAgentPanelLayoutRefresh()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            UpdateAgentPanelWidths();
            ActiveAgentsPanelBorder.InvalidateMeasure();
            ActiveAgentsPanelBorder.InvalidateArrange();
            ActiveAgentsScrollViewer.InvalidateMeasure();
            ActiveAgentsScrollViewer.InvalidateArrange();
            ActiveAgentItemsControl.InvalidateMeasure();
            ActiveAgentItemsControl.InvalidateArrange();
            InactiveAgentsPanelBorder.InvalidateMeasure();
            InactiveAgentsPanelBorder.InvalidateArrange();
            InactiveAgentsScrollViewer.InvalidateMeasure();
            InactiveAgentsScrollViewer.InvalidateArrange();
            InactiveAgentItemsControl.InvalidateMeasure();
            InactiveAgentItemsControl.InvalidateArrange();
            StatusAgentPanelsGrid.InvalidateMeasure();
            StatusAgentPanelsGrid.InvalidateArrange();
            StatusAgentPanelsGrid.UpdateLayout();
            ActiveAgentsScrollViewer.ScrollToLeftEnd();
            ActiveAgentsScrollViewer.ScrollToTop();
            InactiveAgentsScrollViewer.ScrollToLeftEnd();
            InactiveAgentsScrollViewer.ScrollToTop();
            TryNudgeAgentLaneLayout();
        }));
    }

    private void TryNudgeAgentLaneLayout()
    {
        TryNudgeAgentLaneLayout(
            _activeAgentCards,
            ActiveAgentItemsControl,
            "active");
        TryNudgeAgentLaneLayout(
            _inactiveAgentCards,
            InactiveAgentItemsControl,
            "inactive");
    }

    private void TryNudgeAgentLaneLayout(
        ObservableCollection<AgentStatusCard> targetCollection,
        ItemsControl itemsControl,
        string laneName)
    {
        if (IsAgentLaneNudgeScheduled(laneName) || targetCollection.Count == 0 || itemsControl.ActualHeight > 0)
            return;

        SetAgentLaneNudgeScheduled(laneName, true);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            try
            {
                if (itemsControl.ActualHeight > 0)
                {
                    SetAgentLaneNudgeScheduled(laneName, false);
                    return;
                }

                SquadDashTrace.Write("AgentCards", $"Nudging {laneName} lane because ActualHeight is still zero.");
                var placeholder = CreateAgentLanePlaceholderCard(laneName);
                targetCollection.Add(placeholder);
                itemsControl.InvalidateMeasure();
                itemsControl.InvalidateArrange();
                StatusAgentPanelsGrid.InvalidateMeasure();
                StatusAgentPanelsGrid.InvalidateArrange();
                StatusAgentPanelsGrid.UpdateLayout();

                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
                {
                    try
                    {
                        targetCollection.Remove(placeholder);
                        itemsControl.InvalidateMeasure();
                        itemsControl.InvalidateArrange();
                        StatusAgentPanelsGrid.InvalidateMeasure();
                        StatusAgentPanelsGrid.InvalidateArrange();
                        StatusAgentPanelsGrid.UpdateLayout();
                    }
                    finally
                    {
                        SetAgentLaneNudgeScheduled(laneName, false);
                    }
                }));
            }
            catch
            {
                SetAgentLaneNudgeScheduled(laneName, false);
            }
        }));
    }

    private bool IsAgentLaneNudgeScheduled(string laneName) =>
        string.Equals(laneName, "active", StringComparison.OrdinalIgnoreCase)
            ? _activeAgentLaneNudgeScheduled
            : _inactiveAgentLaneNudgeScheduled;

    private void SetAgentLaneNudgeScheduled(string laneName, bool value)
    {
        if (string.Equals(laneName, "active", StringComparison.OrdinalIgnoreCase))
            _activeAgentLaneNudgeScheduled = value;
        else
            _inactiveAgentLaneNudgeScheduled = value;
    }

    private static AgentStatusCard CreateAgentLanePlaceholderCard(string laneName)
    {
        var placeholder = new AgentStatusCard(
            "Placeholder",
            "P",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            DynamicAgentDefaultAccentHex,
            accentStorageKey: "placeholder:" + laneName,
            isDynamicAgent: true);
        placeholder.CardVisibility = Visibility.Hidden;
        return placeholder;
    }

    private void UpdateSessionState(string state)
    {
        _currentSessionState = state;
        var activeToolName = _pec is null ? null : _pec.ActiveToolName;
        SessionStateTextBlock.Text = string.IsNullOrWhiteSpace(activeToolName)
            ? state
            : $"{state} | Tool: {activeToolName}";
    }

    private Brush ResolveBrushResource(string key, Brush fallback)
    {
        return TryFindResource(key) as Brush ?? fallback;
    }

    private static Brush ThemeBrush(string key) =>
        (Brush?)Application.Current.Resources[key] ?? Brushes.Gray;

    private void ApplyTheme(string themeName)
    {
        var themeUri = string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase)
            ? new Uri("Themes/Light.xaml", UriKind.Relative)
            : new Uri("Themes/Dark.xaml", UriKind.Relative);

        var mergedDicts = Application.Current.Resources.MergedDictionaries;
        var existing = mergedDicts.FirstOrDefault(d =>
            d.Source?.OriginalString?.Contains("/Themes/") == true);
        if (existing is not null)
            mergedDicts.Remove(existing);

        mergedDicts.Add(new ResourceDictionary { Source = themeUri });
        _activeThemeName = themeName;

        // Re-render adorners so they pick up the new theme's brush tokens.
        _searchAdorner?.InvalidateHighlights();
        _scrollbarAdorner?.InvalidateVisual();

        var isDark = string.Equals(themeName, "Dark", StringComparison.OrdinalIgnoreCase);
        AgentStatusCard.SetTheme(isDark);

        // Re-render doc-source find highlights so they use the new theme's colours.
        DocSourceFind_RenderHighlights();
        foreach (var agent in _agents)
            agent.NotifyThemeChanged();

        foreach (var thread in _agentThreadRegistry.ThreadOrder)
            SyncThreadChip(thread);

        MarkdownDocumentWindow.RefreshAllOpenWindows();

        RefreshDocumentationViewer();

        // Rebuild queue tabs so any newly created elements pick up the new theme tokens.
        SyncQueuePanel();

        UpdateThemeMenuState();
    }

    private void RefreshDocumentationViewer()
    {
        // Re-render the current doc topic (if any) so HTML is regenerated with new theme CSS
        if (DocMarkdownViewer is null || DocsPanel is null || DocsPanel.Visibility != Visibility.Visible)
            return;

        var currentItem = DocTopicsTreeView?.SelectedItem as TreeViewItem;
        if (currentItem is null)
            return;

        var filePath = currentItem.Tag as string;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        try
        {
            var markdown = File.ReadAllText(filePath);
            var title = currentItem.Header?.ToString() ?? "Documentation";
            var html = MarkdownHtmlBuilder.Build(markdown, title,
                filePath: filePath, isDark: AgentStatusCard.IsDarkTheme);
            DocMarkdownViewer.NavigateToString(html);
        }
        catch
        {
            // Silently ignore errors during theme refresh
        }
    }

    // ── Screenshot-paste feature ──────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="DocViewerScriptingBridge"/> when the user right-clicks a
    /// 📸 placeholder blockquote in the docs viewer.  Shows a context menu with the
    /// "Use screenshot on clipboard" action.
    /// </summary>
    public void ShowDocScreenshotContextMenu(string imagePath)
    {
        var menu = new ContextMenu();
        var pasteItem = new MenuItem { Header = "Use screenshot on clipboard" };
        pasteItem.IsEnabled = Clipboard.ContainsImage();
        pasteItem.Click += (s, e) => PasteScreenshotToDoc(imagePath);
        menu.Items.Add(pasteItem);
        menu.PlacementTarget = DocMarkdownViewer;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void PasteScreenshotToDoc(string imagePath)
    {
        if (!Clipboard.ContainsImage())
        {
            MessageBox.Show("No image found on clipboard.", "Screenshot",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrEmpty(_currentDocPath)) return;

        if (string.IsNullOrWhiteSpace(imagePath))
        {
            MessageBox.Show(
                "Could not determine the image file path for this placeholder.\n\n" +
                "Make sure the markdown has an ![alt](path/to/image.png) line immediately before the 📸 blockquote.",
                "Screenshot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Ensure the target has a file extension — protect against accidentally writing to a directory.
        if (string.IsNullOrEmpty(Path.GetExtension(imagePath)))
        {
            MessageBox.Show(
                $"The image path \"{imagePath}\" has no file extension. Expected a path like images/screenshot.png.",
                "Screenshot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var docDir = Path.GetDirectoryName(_currentDocPath)!;
        var fullImagePath = Path.Combine(docDir, imagePath.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(fullImagePath)!);

        var clipImg = Clipboard.GetImage()!;
        var editor = new ClipboardImageEditorWindow(this, clipImg);
        editor.ShowDialog();
        if (editor.Result is not { } image) return;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.OpenWrite(fullImagePath))
            encoder.Save(stream);

        // Remove the 📸 placeholder line that corresponds to this image.
        // Match using forward-slash paths (as written in markdown).
        var lines = File.ReadAllLines(_currentDocPath).ToList();
        var fwdSlashPath = imagePath.Replace('\\', '/');
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            if ((lines[i].Contains("📸") || lines[i].Contains("Screenshot needed")) &&
                i > 0 && lines[i - 1].Replace('\\', '/').Contains(fwdSlashPath))
            {
                lines.RemoveAt(i);
                // Remove the blank line immediately after the placeholder, if any
                if (i < lines.Count && string.IsNullOrWhiteSpace(lines[i]))
                    lines.RemoveAt(i);
                break;
            }
        }
        File.WriteAllLines(_currentDocPath, lines);

        // Reload the current doc in the viewer
        var markdown = File.ReadAllText(_currentDocPath);
        var title = (DocTopicsTreeView?.SelectedItem as TreeViewItem)?.Header?.ToString() ?? "Documentation";
        var html = MarkdownHtmlBuilder.Build(markdown, title, filePath: _currentDocPath, isDark: AgentStatusCard.IsDarkTheme);
        DocMarkdownViewer.NavigateToString(html);
    }

    /// <summary>
    /// Called by <see cref="DocViewerScriptingBridge.ShowImageMenu"/> when the user right-clicks
    /// an existing image in the docs viewer. Shows a "Replace with image on clipboard" option.
    /// </summary>
    public void ShowImageContextMenu(string imagePath)
    {
        var menu = new ContextMenu();

        var replaceItem = new MenuItem { Header = "Replace with image on clipboard" };
        replaceItem.IsEnabled = Clipboard.ContainsImage();
        replaceItem.Click += (s, e) => ReplaceScreenshotInDoc(imagePath);
        menu.Items.Add(replaceItem);

        var resolvedPath = ResolveDocImagePath(imagePath);
        var showInFolderItem = new MenuItem { Header = "Show image in folder" };
        showInFolderItem.IsEnabled = !string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath);
        showInFolderItem.Click += (_, _) => ShowFileInExplorer(resolvedPath!);
        menu.Items.Add(showInFolderItem);

        menu.PlacementTarget = DocMarkdownViewer;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private string? ResolveDocImagePath(string imagePath)
    {
        if (string.IsNullOrEmpty(_currentDocPath) || string.IsNullOrWhiteSpace(imagePath))
            return null;
        var docDir = Path.GetDirectoryName(_currentDocPath);
        if (string.IsNullOrEmpty(docDir)) return null;
        return Path.Combine(docDir, imagePath.Replace('/', '\\'));
    }

    private static void ShowFileInExplorer(string fullPath)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{fullPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("UI", $"ShowFileInExplorer failed: {ex.Message}");
        }
    }

    private void ReplaceScreenshotInDoc(string imagePath)
    {
        if (!Clipboard.ContainsImage())
        {
            MessageBox.Show("No image found on clipboard.", "Replace Screenshot",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrEmpty(_currentDocPath)) return;
        if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrEmpty(Path.GetExtension(imagePath)))
        {
            MessageBox.Show($"Cannot determine image file path from \"{imagePath}\".",
                "Replace Screenshot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var docDir = Path.GetDirectoryName(_currentDocPath)!;
        var fullImagePath = Path.Combine(docDir, imagePath.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(fullImagePath)!);

        var clipImg = Clipboard.GetImage()!;
        var editor = new ClipboardImageEditorWindow(this, clipImg);
        editor.ShowDialog();
        if (editor.Result is not { } image) return;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        // Overwrite existing file (OpenWrite truncates)
        using (var stream = File.OpenWrite(fullImagePath))
        {
            stream.SetLength(0);
            encoder.Save(stream);
        }

        // Remove the 📸 placeholder line that immediately follows the image line.
        var lines = File.ReadAllLines(_currentDocPath).ToList();
        var fwdSlashPath = imagePath.Replace('\\', '/');
        for (int i = 0; i < lines.Count - 1; i++)
        {
            if (lines[i].Replace('\\', '/').Contains(fwdSlashPath))
            {
                int nextI = i + 1;
                if (nextI < lines.Count &&
                    (lines[nextI].Contains("📸") || lines[nextI].Contains("Screenshot needed")))
                {
                    lines.RemoveAt(nextI);
                    if (nextI < lines.Count && string.IsNullOrWhiteSpace(lines[nextI]))
                        lines.RemoveAt(nextI);
                    break;
                }
            }
        }
        File.WriteAllLines(_currentDocPath, lines);

        // Reload the doc so the viewer shows the updated image
        var markdown = File.ReadAllText(_currentDocPath);
        var title = (DocTopicsTreeView?.SelectedItem as TreeViewItem)?.Header?.ToString() ?? "Documentation";
        var html = MarkdownHtmlBuilder.Build(markdown, title, filePath: _currentDocPath, isDark: AgentStatusCard.IsDarkTheme);
        DocMarkdownViewer.NavigateToString(html);
    }

    /// <summary>
    /// Called by <see cref="DocViewerScriptingBridge.Navigate"/> when JS link-click
    /// handling routes a navigation request through the COM bridge instead of the
    /// browser's default navigation (which fires <see cref="DocMarkdownViewer_Navigating"/>).
    /// </summary>
    internal void InvokeDocNavigation(string href)
    {
        if (string.IsNullOrEmpty(href)) return;

        if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try { Process.Start(new ProcessStartInfo { FileName = href, UseShellExecute = true }); }
            catch { }
            return;
        }

        try
        {
            string? resolvedPath = null;
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                resolvedPath = uri.LocalPath;
            }
            else if (!string.IsNullOrEmpty(_currentDocPath))
            {
                var currentDir = Path.GetDirectoryName(_currentDocPath);
                if (!string.IsNullOrEmpty(currentDir))
                {
                    var relativePart = Uri.UnescapeDataString(href);
                    resolvedPath = Path.GetFullPath(Path.Combine(currentDir, relativePart));
                }
            }

            if (resolvedPath != null &&
                resolvedPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(resolvedPath))
            {
                NavigateToDocByPath(resolvedPath);
            }
        }
        catch { }
    }


    private void UpdateThemeMenuState()
    {
        if (ThemeToggleMenuItem is not null)
            ThemeToggleMenuItem.Header = string.Equals(_activeThemeName, "Dark", StringComparison.OrdinalIgnoreCase)
                ? "_Light Theme"
                : "_Dark Theme";
    }

    private void SetTheme(string themeName)
    {
        if (string.Equals(_activeThemeName, themeName, StringComparison.OrdinalIgnoreCase))
            return;
        ApplyTheme(themeName);
        _settingsSnapshot = _settingsStore.SaveTheme(themeName);
    }

    private void ThemeToggleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetTheme(string.Equals(_activeThemeName, "Dark", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark");
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(ThemeToggleMenuItem_Click), ex);
        }
    }

    private static string CollapseWhitespace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string FormatJson(JsonElement element)
    {
        return JsonSerializer.Serialize(element, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private void OpenSidebarEntry(SidebarEntry entry)
    {
        if (_currentWorkspace is null)
        {
            ShowTextWindow(entry.Title, "No workspace is open.");
            return;
        }

        if (entry.Kind == SidebarEntryKind.Message)
        {
            ShowTextWindow(entry.Title, entry.Subtitle);
            return;
        }

        if (!entry.Exists)
        {
            ShowTextWindow(entry.Title, $"Expected path not found:{Environment.NewLine}{entry.Path}");
            return;
        }

        if (entry.Kind == SidebarEntryKind.Folder)
        {
            _squadCliAdapter.OpenFolderInExplorer(entry.Path, $"Open {entry.Title}");
            return;
        }

        if (Path.GetExtension(entry.Path).Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            OpenMarkdownFile(entry.Path, entry.Title);
            return;
        }

        ShowTextWindow(entry.Title, BuildSidebarEntryContent(entry));
    }

    private string BuildSidebarEntryContent(SidebarEntry entry)
    {
        if (_currentWorkspace is null)
            return "No workspace is open.";

        if (entry.Kind == SidebarEntryKind.Message)
            return entry.Subtitle;

        if (!entry.Exists)
            return $"Expected path not found:\n{entry.Path}";

        try
        {
            return entry.Kind switch
            {
                SidebarEntryKind.File => File.ReadAllText(entry.Path),
                SidebarEntryKind.Folder => BuildFolderContent(entry.Path),
                _ => entry.Subtitle
            };
        }
        catch (Exception ex)
        {
            return $"Unable to read {entry.Path}\n\n{ex.Message}";
        }
    }

    private static string BuildFolderContent(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return $"Folder not found:\n{folderPath}";

        var builder = new StringBuilder();
        builder.AppendLine(folderPath);

        var files = Directory
            .EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
            .OrderBy(path => path)
            .ToArray();

        if (files.Length == 0)
        {
            builder.AppendLine();
            builder.AppendLine("(Folder is empty)");
            return builder.ToString().TrimEnd();
        }

        foreach (var file in files)
        {
            builder.AppendLine();
            builder.AppendLine(new string('=', 72));
            builder.AppendLine(file);
            builder.AppendLine(new string('=', 72));
            builder.AppendLine(File.ReadAllText(file));
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildInstallDiagnostics(SquadCommandResult result, string activeDirectory)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Active directory: {activeDirectory}");
        builder.AppendLine();
        builder.AppendLine(result.ToDisplayText());
        return builder.ToString().TrimEnd();
    }

    private bool CanShowOwnedWindow()
    {
        return !_isClosing &&
               !Dispatcher.HasShutdownStarted &&
               !Dispatcher.HasShutdownFinished &&
               IsLoaded &&
               PresentationSource.FromVisual(this) is not null;
    }

    private void OpenMarkdownFile(string? filePath, string title, bool showSource = false)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        OpenMarkdownFiles([new MarkdownDocumentSpec(Path.GetFileNameWithoutExtension(filePath), filePath)], title, showSource);
    }

    private void OpenMarkdownFiles(IReadOnlyList<MarkdownDocumentSpec> files, string title, bool showSource = false)
    {
        if (files.Count == 0)
            return;

        try
        {
            MarkdownDocumentWindow.Show(
                CanShowOwnedWindow() ? this : null,
                title,
                files,
                showSource);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to open the markdown file.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ShowTextWindow(string title, string content)
    {
        var window = new Window
        {
            Title = title,
            Width = 900,
            Height = 700,
            MinWidth = 640,
            MinHeight = 480
        };
        window.SetResourceReference(BackgroundProperty, "AppSurface");

        if (CanShowOwnedWindow())
            window.Owner = this;

        var textBox = new TextBox
        {
            Text = content,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(12),
            BorderThickness = new Thickness(0)
        };
        textBox.SetResourceReference(TextBox.BackgroundProperty, "AppSurface");
        textBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
        window.Content = textBox;

        window.Show();
    }

    private HireAgentWindow.HireAgentSubmission? ShowHireAgentWindow()
    {
        if (_currentWorkspace is null || !CanShowOwnedWindow())
            return null;

        var existingNames = _agents
            .Where(card => !card.IsLeadAgent && !card.IsDynamicAgent)
            .Select(card => card.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        var universes = HireAgentWindow.LoadCatalog(
            _currentWorkspace.FolderPath,
            _settingsSnapshot,
            _workspacePaths.AgentImageAssetsDirectory,
            existingNames);
        var activeUniverse = HireAgentWindow.ResolveActiveUniverseName(_currentWorkspace.FolderPath);
        var submission = HireAgentWindow.Show(
            this,
            universes,
            activeUniverse,
            _workspacePaths.RoleIconAssetsDirectory,
            (agentKey, imagePath) =>
            {
                _settingsSnapshot = _settingsStore.SaveAgentImagePath(
                    _currentWorkspace.FolderPath,
                    agentKey,
                    imagePath);
            });
        if (submission is null)
            return null;

        if (!string.IsNullOrWhiteSpace(submission.ImagePath))
        {
            foreach (var candidate in HireAgentWindow.BuildImageKeyCandidates(submission.AgentName))
            {
                _settingsSnapshot = _settingsStore.SaveAgentImagePath(
                    _currentWorkspace.FolderPath,
                    candidate,
                    submission.ImagePath);
            }
        }

        return submission;
    }

    private void RefreshStatusPresentation()
    {
        var now = DateTimeOffset.Now;

        foreach (var card in _agents.Where(candidate => !candidate.IsLeadAgent))
            SyncCardThreads(card, now);

        RefreshTasksStatusWindow(now);
    }

    private void RestoreUtilityWindowVisibility()
    {
        if (_settingsSnapshot.TasksWindowOpen)
        {
            SquadDashTrace.Write("Startup", "Restoring tasks window from previous session.");
            ShowTasksStatusWindow();
        }

        if (_settingsSnapshot.TraceWindowOpen)
        {
            SquadDashTrace.Write("Startup", "Restoring live trace window from previous session.");
            ShowTraceWindow();
        }

        RestoreDocsPanelState();
    }

    private void RestoreDocsPanelState()
    {
        _docsPanelState = _settingsStore.GetDocsPanelState(_currentWorkspace?.FolderPath);

        // Restore per-workspace fullscreen transcript state.
        if (_docsPanelState.FullScreenTranscript == true)
        {
            _transcriptFullScreenEnabled = true;
            ApplyViewMode();
        }

        // Restore tasks panel visibility.
        if (_docsPanelState.TasksPanelVisible == true)
        {
            _tasksPanelVisible = true;
            SyncTasksPanel();
            if (ViewTasksMenuItem is not null)
                ViewTasksMenuItem.IsChecked = true;
        }

        // Open: null (absent) or true = open (the default). false = explicitly closed.
        if (_docsPanelState.Open == false)
            return;

        SquadDashTrace.Write("Startup", "Restoring documentation panel from previous session.");
        // Open the panel without persisting — this is a startup restore, not a user action.
        SetDocumentationMode(true, persistChange: false);

        // Defer proportional width restore until after WM_SIZE is processed (Input=5) and the
        // subsequent layout pass (Render=7) so that MainGrid.ActualWidth reflects the restored
        // window size.  Background (4) is lower priority than both, guaranteeing correct values.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            ApplyDocsPanelProportionalWidths();
        });
    }

    private void ApplyDocsPanelProportionalWidths()
    {
        var docState = _docsPanelState ?? _settingsStore.GetDocsPanelState(_currentWorkspace?.FolderPath);
        // Restore docs panel width (proportional takes priority over absolute)
        if (DocsPanelColumn is not null && MainGrid is not null && MainGrid.ActualWidth > 0)
        {
            double width;
            if (docState.PanelWidthFraction is { } fraction && fraction > 0 && fraction < 1)
                width = MainGrid.ActualWidth * fraction;
            else
                width = docState.PanelWidth ?? 600;
            DocsPanelColumn.Width = new GridLength(Math.Max(200, width));
        }

        // Restore View Source panel state
        if (docState.SourceOpen == true)
        {
            var sourceWidth = docState.SourceWidth ?? 300;
            ShowDocSourcePanel();
            if (DocsSourceColumn is not null)
                DocsSourceColumn.Width = new GridLength(Math.Max(100, sourceWidth));
        }
    }

    // ── Docs TreeView drag-and-drop ───────────────────────────────────────────────

    private void DocTopicsTreeView_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            var item = FindAncestorTreeViewItem(e.OriginalSource as DependencyObject);
            if (item is null) return;

            var filePath = item.Tag as string;
            if (string.IsNullOrEmpty(filePath)) return;

            item.IsSelected = true;

            var copyLinkItem = new MenuItem { Header = "Copy markdown link" };
            copyLinkItem.Click += (_, _) => DocTopicsTreeView_CopyMarkdownLink(item);

            var menu = new ContextMenu();
            menu.Items.Add(copyLinkItem);
            menu.PlacementTarget = DocTopicsTreeView;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(DocTopicsTreeView_MouseRightButtonUp), ex);
        }
    }

    private void DocTopicsTreeView_CopyMarkdownLink(TreeViewItem item)
    {
        var filePath = item.Tag as string;
        if (string.IsNullOrEmpty(filePath)) return;

        var title = item.Header?.ToString() ?? Path.GetFileNameWithoutExtension(filePath);
        var docsRoot = DocTopicsLoader.FindDocsFolderPath(_currentWorkspace?.FolderPath);

        string relativePath;
        if (!string.IsNullOrEmpty(docsRoot) &&
            filePath.StartsWith(docsRoot, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = filePath.Substring(docsRoot.Length)
                .TrimStart('\\', '/')
                .Replace('\\', '/');
        }
        else
        {
            relativePath = Path.GetFileName(filePath);
        }

        Clipboard.SetText($"[{title}]({relativePath})");
    }

    private void DocTopicsTreeView_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _docsDragStartPoint = e.GetPosition(null);
        _docsDragItem = FindAncestorTreeViewItem(e.OriginalSource as DependencyObject);
        _docsDragInProgress = false;
    }

    private void DocTopicsTreeView_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _docsDragItem is null || _docsDragInProgress)
            return;

        var pos = e.GetPosition(null);
        var diff = pos - _docsDragStartPoint;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _docsDragInProgress = true;
        var data = new DataObject("DocTopicTreeViewItem", _docsDragItem);
        DragDrop.DoDragDrop(_docsDragItem, data, DragDropEffects.Move);
        _docsDragInProgress = false;
        _docsDragItem = null;
        HideDocTopicsDropIndicator();
    }

    private void DocTopicsTreeView_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("DocTopicTreeViewItem"))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        UpdateDocTopicsDropIndicator(e.GetPosition(DocTopicsTreeView));
        e.Handled = true;
    }

    private void DocTopicsTreeView_DragLeave(object sender, DragEventArgs e)
    {
        HideDocTopicsDropIndicator();
    }

    private void DocTopicsTreeView_Drop(object sender, DragEventArgs e)
    {
        HideDocTopicsDropIndicator();
        if (!e.Data.GetDataPresent("DocTopicTreeViewItem"))
            return;

        var draggedItem = e.Data.GetData("DocTopicTreeViewItem") as TreeViewItem;
        if (draggedItem is null)
            return;

        var dropTarget = FindDropTarget(e.GetPosition(DocTopicsTreeView), out bool insertAfter);
        if (dropTarget is null || ReferenceEquals(dropTarget, draggedItem))
            return;

        ReorderDocTopics(draggedItem, dropTarget, insertAfter);
    }

    private static TreeViewItem? FindAncestorTreeViewItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TreeViewItem item)
                return item;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void UpdateDocTopicsDropIndicator(Point posInTreeView)
    {
        if (DocTopicsDropIndicatorCanvas is null || DocTopicsDropIndicator is null)
            return;

        var hitItem = FindItemAtPoint(DocTopicsTreeView, posInTreeView);
        if (hitItem is null)
        {
            HideDocTopicsDropIndicator();
            return;
        }

        var itemBounds = hitItem.TransformToAncestor(DocTopicsTreeView).TransformBounds(
            new Rect(0, 0, hitItem.ActualWidth, hitItem.ActualHeight));

        bool inTopHalf = posInTreeView.Y < itemBounds.Y + itemBounds.Height / 2;
        double lineY = inTopHalf ? itemBounds.Y : itemBounds.Bottom;

        DocTopicsDropIndicator.Width = DocTopicsDropIndicatorCanvas.ActualWidth > 0
            ? DocTopicsDropIndicatorCanvas.ActualWidth
            : DocTopicsTreeView.ActualWidth;
        Canvas.SetTop(DocTopicsDropIndicator, Math.Max(0, lineY - 1));
        Canvas.SetLeft(DocTopicsDropIndicator, 0);
        DocTopicsDropIndicator.Visibility = Visibility.Visible;
    }

    private void HideDocTopicsDropIndicator()
    {
        if (DocTopicsDropIndicator is not null)
            DocTopicsDropIndicator.Visibility = Visibility.Collapsed;
    }

    private static TreeViewItem? FindItemAtPoint(TreeView treeView, Point point)
    {
        var result = VisualTreeHelper.HitTest(treeView, point);
        if (result?.VisualHit is null) return null;
        return FindAncestorTreeViewItem(result.VisualHit);
    }

    private TreeViewItem? FindDropTarget(Point posInTreeView, out bool insertAfter)
    {
        insertAfter = false;
        var hitItem = FindItemAtPoint(DocTopicsTreeView, posInTreeView);
        if (hitItem is null) return null;

        var itemBounds = hitItem.TransformToAncestor(DocTopicsTreeView).TransformBounds(
            new Rect(0, 0, hitItem.ActualWidth, hitItem.ActualHeight));
        insertAfter = posInTreeView.Y >= itemBounds.Y + itemBounds.Height / 2;
        return hitItem;
    }

    private void ReorderDocTopics(TreeViewItem draggedItem, TreeViewItem targetItem, bool insertAfter)
    {
        var docsRoot = DocTopicsLoader.FindDocsFolderPath(_currentWorkspace?.FolderPath);
        if (string.IsNullOrEmpty(docsRoot)) return;

        var summaryPath = Path.Combine(docsRoot, "SUMMARY.md");
        if (!File.Exists(summaryPath)) return;

        var lines = File.ReadAllLines(summaryPath).ToList();

        int draggedLineIndex = FindSummaryLineIndex(lines, draggedItem, docsRoot);
        int targetLineIndex = FindSummaryLineIndex(lines, targetItem, docsRoot);

        if (draggedLineIndex < 0 || targetLineIndex < 0 || draggedLineIndex == targetLineIndex)
            return;

        var draggedLine = lines[draggedLineIndex];
        lines.RemoveAt(draggedLineIndex);

        if (targetLineIndex > draggedLineIndex)
            targetLineIndex--;

        int insertIndex = insertAfter ? targetLineIndex + 1 : targetLineIndex;
        insertIndex = Math.Clamp(insertIndex, 0, lines.Count);

        var targetIsGroup = targetItem.Tag is null;
        if (targetIsGroup && !insertAfter)
        {
            draggedLine = "  " + draggedLine.TrimStart();
            insertIndex = targetLineIndex + 1;
        }
        else
        {
            var refLine = insertAfter
                ? (targetLineIndex < lines.Count ? lines[targetLineIndex] : string.Empty)
                : (targetLineIndex < lines.Count ? lines[targetLineIndex] : string.Empty);
            var refIndent = new string(' ', refLine.TakeWhile(char.IsWhiteSpace).Count());
            draggedLine = refIndent + draggedLine.TrimStart();
        }

        lines.Insert(insertIndex, draggedLine);

        if (_docsWatcher is not null)
            _docsWatcher.EnableRaisingEvents = false;

        try
        {
            File.WriteAllLines(summaryPath, lines);
        }
        finally
        {
            if (_docsWatcher is not null)
                _docsWatcher.EnableRaisingEvents = true;
        }

        PopulateDocumentationTopics();
    }

    private static int FindSummaryLineIndex(List<string> lines, TreeViewItem item, string docsRoot)
    {
        var filePath = item.Tag as string;
        var header = item.Header?.ToString();

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (filePath is not null)
            {
                var start = line.IndexOf('(');
                var end = line.IndexOf(')', start + 1);
                if (start >= 0 && end > start)
                {
                    var href = line.Substring(start + 1, end - start - 1);
                    var relPath = Path.GetRelativePath(docsRoot, filePath).Replace('\\', '/');
                    if (string.Equals(href, relPath, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }

            if (header is not null && filePath is null)
            {
                var titleStart = line.IndexOf('[');
                var titleEnd = line.IndexOf(']', titleStart + 1);
                if (titleStart >= 0 && titleEnd > titleStart)
                {
                    var title = line.Substring(titleStart + 1, titleEnd - titleStart - 1);
                    if (string.Equals(title, header, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
        }
        return -1;
    }

    private void ShowTasksStatusWindow()
    {
        if (_tasksStatusWindow is null)
        {
            SquadDashTrace.Write("UI", "Showing live tasks popup.");
            _tasksStatusWindow = new TasksStatusWindow();
            if (CanShowOwnedWindow())
                _tasksStatusWindow.Owner = this;

            _tasksStatusWindow.Closed += (_, _) => { _tasksStatusWindow = null; _tasksWindowOffset = null; };
            _tasksStatusWindow.LocationChanged += (_, _) => OnTasksWindowMoved();
            _tasksStatusWindow.Show();
        }

        RefreshTasksStatusWindow(DateTimeOffset.Now);
        PositionTasksStatusWindow();
    }

    private void HideTasksStatusWindow()
    {
        if (_tasksStatusWindow is not null)
            SquadDashTrace.Write("UI", "Hiding live tasks popup.");
        _tasksStatusWindow?.Close();
    }

    private void RefreshTasksStatusWindow(DateTimeOffset now)
    {
        if (_tasksStatusWindow is null)
            return;

        _tasksStatusWindow.UpdateContent(_backgroundTaskPresenter.BuildBackgroundTaskReport(now));
        // Do NOT reposition here — content updates must not move a user-placed window.
    }

    // ── Floating-window positioning ──────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the main window moves. Translates all floating windows by the same delta
    /// so they maintain their relative position. After the move, validates each window is
    /// on-screen; off-screen windows are snapped back to the default position.
    /// </summary>
    private void OnMainWindowMoved()
    {
        PositionTasksStatusWindow();
        PositionTraceWindow();
        ValidateFloatingWindowPosition(ref _tasksWindowOffset, _tasksStatusWindow);
        ValidateFloatingWindowPosition(ref _traceWindowOffset, _traceWindow);
    }

    /// <summary>
    /// Returns the Left/Top of the default snap position for a floating window relative
    /// to the main window's upper-right corner.
    /// </summary>
    private (double left, double top) DefaultFloatingWindowSnap(Window floater, double topOffset = 0)
    {
        const double margin = 18;
        var ownerWidth = ActualWidth > 0 ? ActualWidth : Width;
        double left = Left + Math.Max(margin, ownerWidth - floater.Width - margin);
        double top = Top + SystemParameters.WindowCaptionHeight + margin + topOffset;
        return (left, top);
    }

    /// <summary>
    /// Moves <paramref name="floater"/> to the position described by <paramref name="offset"/>
    /// (offset from main window's Right/Top), or to the default snap position if offset is null.
    /// </summary>
    private void ApplyFloatingWindowPosition(Window floater, Vector? offset, double defaultTopOffset = 0)
    {
        double newLeft, newTop;
        if (offset is { } off)
        {
            double mainRight = Left + (ActualWidth > 0 ? ActualWidth : Width);
            newLeft = mainRight + off.X;
            newTop = Top + off.Y;
        }
        else
        {
            (newLeft, newTop) = DefaultFloatingWindowSnap(floater, defaultTopOffset);
        }

        _movingFloatingWindow = true;
        try
        {
            floater.Left = newLeft;
            floater.Top = newTop;
        }
        finally
        {
            _movingFloatingWindow = false;
        }
    }

    /// <summary>
    /// Checks whether the centre of <paramref name="floater"/> falls on any monitor and the
    /// window is fully within that monitor. If not, resets the saved offset to null (which
    /// causes the next position call to snap back to the default).
    /// </summary>
    private void ValidateFloatingWindowPosition(ref Vector? offset, Window? floater)
    {
        if (floater is not { IsLoaded: true })
            return;

        double cx = floater.Left + floater.Width / 2;
        double cy = floater.Top + floater.Height / 2;

        // If the centre is on no monitor, or the window bleeds off screen, reset.
        bool centreOnScreen = NativeMethods.IsRectOnAnyMonitor((int)cx, (int)cy, (int)cx + 1, (int)cy + 1);
        if (!centreOnScreen)
            offset = null;
    }

    private void PositionTasksStatusWindow()
    {
        if (_tasksStatusWindow is not { IsLoaded: true } || WindowState == WindowState.Minimized)
            return;

        ApplyFloatingWindowPosition(_tasksStatusWindow, _tasksWindowOffset);
    }

    private void PositionTraceWindow()
    {
        if (_traceWindow is not { IsLoaded: true } || WindowState == WindowState.Minimized)
            return;

        // If tasks window is at default position, stack trace below it.
        double defaultTopOffset = _tasksWindowOffset is null && _tasksStatusWindow is { IsLoaded: true }
            ? _tasksStatusWindow.Height + 18
            : 0;
        ApplyFloatingWindowPosition(_traceWindow, _traceWindowOffset, defaultTopOffset);
    }

    /// <summary>
    /// Records the floating window's position as an offset from the main window's top-right
    /// corner whenever the user moves it (i.e. not a programmatic move).
    /// </summary>
    private void OnFloatingWindowMoved(Window floater, ref Vector? offsetField)
    {
        if (_movingFloatingWindow)
            return;

        double mainRight = Left + (ActualWidth > 0 ? ActualWidth : Width);
        offsetField = new Vector(floater.Left - mainRight, floater.Top - Top);
    }

    private void OnTasksWindowMoved()
    {
        if (_tasksStatusWindow is not null)
            OnFloatingWindowMoved(_tasksStatusWindow, ref _tasksWindowOffset);
    }

    private void OnTraceWindowMoved()
    {
        if (_traceWindow is not null)
            OnFloatingWindowMoved(_traceWindow, ref _traceWindowOffset);
    }

    private void ShowScreenshotOverlay()
    {
        if (!CanShowOwnedWindow())
            return;

        var saveDir = Path.Combine(_workspacePaths.ScreenshotsDirectory, "baseline");
        var overlay = new ScreenshotOverlayWindow(this, saveDir, _activeThemeName, _settingsSnapshot.SpeechRegion ?? string.Empty);
        overlay.ScreenshotSaved += OnInteractiveCaptureCompleted;
        overlay.ScreenshotFailed += (_, error) => Dispatcher.InvokeAsync(() =>
            AppendLine($"[screenshot error] {error}", ThemeBrush("SystemErrorText")));
        overlay.Closed += (_, _) => ResetPttState();
        overlay.Show();
    }

    // ── Interactive capture completion ───────────────────────────────────────

    /// <summary>
    /// Fired by <see cref="ScreenshotOverlayWindow.ScreenshotSaved"/> after the PNG
    /// has been provisionally saved.  Runs the full post-capture pipeline:
    /// edge-anchor warnings, name suggestion, manifest build + save, definition
    /// registry upsert, PNG rename, and transcript confirmation.
    /// </summary>
    private async void OnInteractiveCaptureCompleted(object? sender, ScreenshotSavedEventArgs e)
    {
        try
        {
            // ── Step 2: Warn about unnamed anchors (non-blocking) ─────────────
            foreach (var anchor in e.Anchors)
            {
                if (anchor.NeedsName)
                {
                    var elementType = anchor.Element?.GetType().Name ?? "unknown";
                    AppendLine(
                        $"⚠️ Screenshot anchor at {anchor.Edge} edge has no x:Name — " +
                        $"element path: {elementType}. Consider naming it.");
                }
            }

            // ── Step 4: Convert EdgeAnchor[] → EdgeAnchorRecord[] ─────────────
            var anchorRecords = e.Anchors
                .Select(a => new Screenshots.EdgeAnchorRecord(
                    Edge: a.Edge,
                    ElementNames: a.UniqueNames,
                    NeedsName: a.NeedsName,
                    ElementLeft: a.ElementBounds.Left,
                    ElementTop: a.ElementBounds.Top,
                    ElementWidth: a.ElementBounds.Width,
                    ElementHeight: a.ElementBounds.Height,
                    DistanceToEdge: a.DistanceToEdge))
                .ToArray();

            var topAnchor = anchorRecords[0];
            var rightAnchor = anchorRecords[1];
            var bottomAnchor = anchorRecords[2];
            var leftAnchor = anchorRecords[3];

            // ── Step 5: Use the name confirmed in the overlay rename UI ──────
            // The overlay's EnterRenameMode() called ScreenshotNamingHelper.SuggestName()
            // and let the user edit/confirm it before capture was taken.
            var acceptedName = e.AcceptedName;

            // ── Step 6: Build ScreenshotManifest ──────────────────────────────
            var dpi = VisualTreeHelper.GetDpi(this);
            var sel = e.SelectionRect;
            var bounds = new Screenshots.CaptureBounds(
                X: sel.X,
                Y: sel.Y,
                Width: sel.Width,
                Height: sel.Height,
                DpiX: dpi.DpiScaleX,
                DpiY: dpi.DpiScaleY);

            var manifest = new Screenshots.ScreenshotManifest(
                Version: 1,
                Name: acceptedName,
                Description: string.Empty,
                Theme: _activeThemeName,
                Region: e.IsFullWindow ? "full" : "custom",
                CapturedAt: DateTime.UtcNow,
                Bounds: bounds,
                Top: topAnchor,
                Right: rightAnchor,
                Bottom: bottomAnchor,
                Left: leftAnchor,
                ReplayActionId: null,
                FixturePath: null);

            // Save manifest sidecar alongside the PNG.
            var screenshotsDir = _workspacePaths.ScreenshotsDirectory;
            var baselineDir = Path.Combine(screenshotsDir, "baseline");
            Directory.CreateDirectory(baselineDir);

            var theme = _activeThemeName.ToLowerInvariant();
            var jsonFileName = $"{acceptedName}-{theme}.json";
            var jsonPath = Path.Combine(baselineDir, jsonFileName);

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            await using (var fs = File.Open(jsonPath, FileMode.Create, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(fs, manifest, jsonOptions);

            // Rename the provisional PNG to its final accepted-name path.
            var pngFileName = $"{acceptedName}-{theme}.png";
            var finalPngPath = Path.Combine(baselineDir, pngFileName);
            if (!string.Equals(e.PngPath, finalPngPath, StringComparison.OrdinalIgnoreCase))
                File.Move(e.PngPath, finalPngPath, overwrite: true);

            // ── Step 7: Upsert ScreenshotDefinition into registry ─────────────
            var definition = new Screenshots.ScreenshotDefinition(
                Name: acceptedName,
                Description: string.Empty,
                Theme: _activeThemeName,
                ReplayActionId: null,
                FixturePath: null,
                Top: topAnchor,
                Right: rightAnchor,
                Bottom: bottomAnchor,
                Left: leftAnchor,
                Bounds: bounds);

            var registry = await Screenshots.ScreenshotDefinitionRegistry.LoadAsync(screenshotsDir);
            registry.AddOrUpdate(definition);
            await registry.SaveAsync();

            // ── Step 8: Transcript confirmation ───────────────────────────────
            var topName = topAnchor.ElementNames.Count > 0 ? string.Join(", ", topAnchor.ElementNames) : "(unnamed)";
            var rightName = rightAnchor.ElementNames.Count > 0 ? string.Join(", ", rightAnchor.ElementNames) : "(unnamed)";
            var bottomName = bottomAnchor.ElementNames.Count > 0 ? string.Join(", ", bottomAnchor.ElementNames) : "(unnamed)";
            var leftName = leftAnchor.ElementNames.Count > 0 ? string.Join(", ", leftAnchor.ElementNames) : "(unnamed)";

            AppendLine(
                $"📷 Screenshot saved: `{acceptedName}` → `{finalPngPath}`\n" +
                $"   Metadata: `{jsonPath}`\n" +
                $"   Anchors: top={topName} right={rightName} bottom={bottomName} left={leftName}");
        }
        catch (Exception ex)
        {
            AppendLine($"[screenshot error] {ex.Message}", ThemeBrush("SystemErrorText"));

            // Best-effort cleanup: remove the provisional PNG if it still exists.
            if (File.Exists(e.PngPath))
            {
                try { File.Delete(e.PngPath); } catch { /* ignore */ }
            }
        }
    }

    private void ShowTraceWindow()
    {
        if (_traceWindow is null)
        {
            SquadDashTrace.Write("UI", "Showing live trace popup.");
            _traceWindow = new TraceWindow(_settingsStore);
            if (CanShowOwnedWindow())
                _traceWindow.Owner = this;

            _traceWindow.Closed += (_, _) =>
            {
                _scrollController.TraceTarget = null;
                SquadDashTrace.TraceTarget = null;
                _traceWindow = null;
                _traceWindowOffset = null;
            };
            _traceWindow.LocationChanged += (_, _) => OnTraceWindowMoved();

            _scrollController.TraceTarget = _traceWindow;
            SquadDashTrace.TraceTarget = _traceWindow;
            _traceWindow.Show();
        }
        else
        {
            _traceWindow.Activate();
        }

        PositionTraceWindow();
    }

    private void HideLiveTraceWindow()
    {
        if (_traceWindow is not null)
            SquadDashTrace.Write("UI", "Hiding live trace popup.");
        _traceWindow?.Close();
    }

    private string ResolveAgentAccentHex(AgentStatusCard agentCard, bool isLeadAgent)
    {
        if (_currentWorkspace is not null &&
            _settingsSnapshot.AgentAccentColorsByWorkspace.TryGetValue(_currentWorkspace.FolderPath, out var workspaceColors))
        {
            if (workspaceColors.TryGetValue(agentCard.AccentStorageKey, out var savedByKey) &&
                !string.IsNullOrWhiteSpace(savedByKey))
            {
                return savedByKey;
            }

            if (workspaceColors.TryGetValue(agentCard.Name, out var savedByName) &&
                !string.IsNullOrWhiteSpace(savedByName))
            {
                return savedByName;
            }
        }

        if (agentCard.IsDynamicAgent)
            return DynamicAgentDefaultAccentHex;

        return isLeadAgent ? LeadAgentDefaultAccentHex : ObservedAgentDefaultAccentHex;
    }

    private void ApplyAgentAccent(AgentStatusCard agentCard, string accentHex, bool persist)
    {
        agentCard.AccentColorHex = accentHex;

        if (!persist || _currentWorkspace is null)
            return;

        _settingsSnapshot = _settingsStore.SaveAgentAccentColor(
            _currentWorkspace.FolderPath,
            agentCard.AccentStorageKey,
            accentHex);
    }

    private string? ResolveAgentImagePath(AgentStatusCard card)
    {
        if (_currentWorkspace is not null &&
            _settingsSnapshot.AgentImagePathsByWorkspace.TryGetValue(_currentWorkspace.FolderPath, out var workspaceImages))
        {
            foreach (var candidate in HireAgentWindow.BuildImageKeyCandidates(card.Name).Prepend(card.AccentStorageKey))
            {
                if (workspaceImages.TryGetValue(candidate, out var userImagePath) &&
                    !string.IsNullOrWhiteSpace(userImagePath) &&
                    File.Exists(userImagePath))
                {
                    return userImagePath;
                }
            }
        }

        var bundledPath = AgentImagePathResolver.ResolveBundledPath(card, _workspacePaths.AgentImageAssetsDirectory);
        return bundledPath ?? AgentImagePathResolver.ResolveRoleIconPath(card, _workspacePaths.RoleIconAssetsDirectory);
    }

    private static ImageSource? LoadAgentImageFromPath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return null;

        try
        {
            // Use StreamSource instead of UriSource to bypass WPF's internal per-URI
            // bitmap cache.  This ensures a re-selected file is always read fresh from
            // disk even if the same path was used before (e.g. after external edits).
            using var stream = File.OpenRead(imagePath);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void ApplyAgentImage(AgentStatusCard card, string? imagePath, bool persist)
    {
        card.AgentImageSource = LoadAgentImageFromPath(imagePath);
        UpdateAvatarSizes();

        if (!persist || _currentWorkspace is null)
            return;

        _settingsSnapshot = _settingsStore.SaveAgentImagePath(
            _currentWorkspace.FolderPath,
            card.AccentStorageKey,
            imagePath);
    }

    private void UpdateAvatarSizes()
    {
        // Circle size and font are fixed; no dynamic resizing needed.
    }

    private static string GetAgentInitial(string agentName)
    {
        return string.IsNullOrWhiteSpace(agentName)
            ? "?"
            : AgentThreadRegistry.HumanizeAgentName(agentName).Trim()[..1].ToUpperInvariant();
    }

    private static string? TryResolveGitHubUrl(string workspaceFolderPath)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "remote get-url origin",
                WorkingDirectory = workspaceFolderPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return null;

            var remoteUrl = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(remoteUrl))
                return null;

            if (remoteUrl.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
                remoteUrl = "https://github.com/" + remoteUrl["git@github.com:".Length..];

            if (remoteUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                remoteUrl = remoteUrl[..^4];

            if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) &&
                uri.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase))
                return remoteUrl;
        }
        catch
        {
        }

        return null;
    }

    internal void EmergencySave() => _conversationManager.EmergencySave();

    private async Task OpenExternalLinkWithCommitCheckAsync(string url)
    {
        // Check if this is a GitHub commit URL — verify locally via git before opening
        if (_workspaceGitHubUrl is not null &&
            url.StartsWith(_workspaceGitHubUrl, StringComparison.OrdinalIgnoreCase) &&
            url.Contains("/commit/", StringComparison.OrdinalIgnoreCase))
        {
            var sha = url[(url.LastIndexOf('/') + 1)..];
            if (!string.IsNullOrWhiteSpace(sha) && !await IsCommitOnRemoteAsync(sha).ConfigureAwait(false))
            {
                var push = Dispatcher.Invoke(() => MessageBox.Show(
                    this,
                    "This commit doesn't appear to have been pushed to GitHub yet.\n\nWould you like to push all changes now?",
                    "Commit not found",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes);

                if (push)
                    await PushToOriginAsync().ConfigureAwait(false);

                return;
            }
        }

        _squadCliAdapter.OpenExternalLink(url);
    }

    private async Task<bool> IsCommitOnRemoteAsync(string sha)
    {
        var folderPath = _currentWorkspace?.FolderPath;
        if (string.IsNullOrWhiteSpace(folderPath))
            return true; // can't check — assume pushed to avoid false positives

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"branch -r --contains {sha}",
                WorkingDirectory = folderPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return true;

            var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            // Any output means the commit is on at least one remote branch
            return !string.IsNullOrWhiteSpace(stdout);
        }
        catch
        {
            return true; // can't check — assume pushed to avoid false positives
        }
    }

    private void AppendSystemLineOrDefer(string text, Brush? brush = null)
    {
        if (_isPromptRunning)
            _deferredSystemLines.Enqueue((text, brush));
        else
            AppendLine(text, brush);
    }

    private void FlushDeferredSystemLines()
    {
        while (_deferredSystemLines.TryDequeue(out var item))
            AppendLine(item.Text, item.Brush);
    }

    private async Task PushToOriginAsync()
    {
        var folderPath = _currentWorkspace?.FolderPath;
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            Dispatcher.Invoke(() => AppendSystemLineOrDefer("⚠ No workspace folder — cannot push.", ThemeBrush("SystemErrorText")));
            return;
        }

        Dispatcher.Invoke(() => AppendSystemLineOrDefer("Pushing to origin…"));

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "push",
                WorkingDirectory = folderPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                Dispatcher.Invoke(() => AppendSystemLineOrDefer("⚠ Failed to start git process.", ThemeBrush("SystemErrorText")));
                return;
            }

            var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            Dispatcher.Invoke(() =>
            {
                if (process.ExitCode == 0)
                    AppendSystemLineOrDefer("✓ Pushed successfully.");
                else
                    AppendSystemLineOrDefer($"⚠ git push failed (exit {process.ExitCode}): {stderr.Trim()}", ThemeBrush("SystemErrorText"));
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => AppendSystemLineOrDefer($"⚠ Push error: {ex.Message}", ThemeBrush("SystemErrorText")));
        }
    }

    // ── Transcript search ─────────────────────────────────────────────────────

    /// <summary>
    /// Runs a search against the active transcript, updates match state, and
    /// navigates to the first result.  Cancels any in-flight search first so
    /// rapid typing does not stack results.
    /// </summary>
    private async Task ExecuteSearchAsync(string query)
    {
        // Cancel the previous search and dispose before starting a new one.
        var previous = _searchCts;
        _searchCts = null;
        previous?.Cancel();
        previous?.Dispose();

        if (string.IsNullOrEmpty(query) || query.Length < 3)
        {
            _searchMatches = [];
            _searchMatchCursor = -1;
            _searchAdorner?.Clear();
            _scrollbarAdorner?.Clear();
            _cachedSearchPointers = null;
            ClearBucCellHighlights();
            UpdateSearchUi();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _cachedSearchPointers = null;  // Invalidate stale pointer cache from previous search.

        try
        {
            // Always search the coordinator transcript first (async, may be large).
            var coordinatorMatches = await _conversationManager.SearchTurnsAsync(query, cts.Token);
            if (cts.IsCancellationRequested) return;

            // Show coordinator results immediately so the user gets fast feedback.
            _searchMatches = coordinatorMatches;
            _searchMatchCursor = coordinatorMatches.Count > 0 ? 0 : -1;
            UpdateSearchUi();

            // Then search all agent threads synchronously (all turns already in memory).
            var allMatches = new List<TurnSearchMatch>(coordinatorMatches);
            foreach (var agentThread in _agentThreadRegistry.ThreadOrder)
            {
                if (cts.IsCancellationRequested) return;
                var agentMatches = SearchAgentThread(agentThread, query, agentThread);
                allMatches.AddRange(agentMatches);
            }

            if (cts.IsCancellationRequested) return;

            _searchMatches = allMatches;
            _searchMatchCursor = allMatches.Count > 0 ? 0 : -1;
            UpdateSearchUi();

            if (allMatches.Count > 0)
                await NavigateToMatchAsync(0);
        }
        catch (OperationCanceledException)
        {
            // Expected when superseded by a newer search — safe to ignore.
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("Search", $"ExecuteSearchAsync failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Searches <paramref name="thread"/>.SavedTurns for <paramref name="query"/>
    /// using the same case-insensitive excerpt format as
    /// <see cref="TranscriptConversationManager.SearchTurnsAsync"/>.
    /// Agent threads are fully rendered at selection time so no async is needed.
    /// <paramref name="sourceThread"/> is stored on each match so
    /// <see cref="NavigateToMatchAsync"/> can switch transcripts when needed.
    /// </summary>
    private static IReadOnlyList<TurnSearchMatch> SearchAgentThread(
        TranscriptThreadState thread, string query, TranscriptThreadState? sourceThread = null)
    {
        if (string.IsNullOrEmpty(query))
            return [];

        var results = new List<TurnSearchMatch>();
        const StringComparison cmp = StringComparison.OrdinalIgnoreCase;
        const int MaxExcerptLength = 120;
        const int ExcerptPad = 40;

        for (var i = 0; i < thread.SavedTurns.Count; i++)
        {
            var turn = thread.SavedTurns[i];
            ScanSearchField(turn.Prompt ?? string.Empty, "user", i, query, cmp, MaxExcerptLength, ExcerptPad, results, sourceThread);
            ScanSearchField(turn.ResponseText ?? string.Empty, "assistant", i, query, cmp, MaxExcerptLength, ExcerptPad, results, sourceThread);
        }

        return results;
    }

    private static void ScanSearchField(
        string text,
        string role,
        int turnIndex,
        string query,
        StringComparison cmp,
        int maxExcerptLength,
        int excerptPad,
        List<TurnSearchMatch> results,
        TranscriptThreadState? thread = null)
    {
        var searchFrom = 0;
        while (searchFrom < text.Length)
        {
            var offset = text.IndexOf(query, searchFrom, cmp);
            if (offset < 0) break;

            var excerptStart = Math.Max(0, offset - excerptPad);
            var excerptEnd = Math.Min(text.Length, offset + query.Length + excerptPad);
            var rawExcerpt = text[excerptStart..excerptEnd];

            string excerpt;
            if (rawExcerpt.Length > maxExcerptLength)
            {
                excerpt = rawExcerpt[..maxExcerptLength] + "…";
            }
            else
            {
                var prefix = excerptStart > 0 ? "…" : string.Empty;
                var suffix = excerptEnd < text.Length ? "…" : string.Empty;
                excerpt = prefix + rawExcerpt + suffix;
            }

            results.Add(new TurnSearchMatch(turnIndex, role, excerpt, offset, thread));
            searchFrom = offset + query.Length;
        }
    }

    /// <summary>
    /// Navigates to the match at <paramref name="index"/> (wraps around),
    /// ensuring the turn is rendered and scrolling the match into view.
    /// Uses a pointer cache to skip the full document re-walk when the turn is
    /// already rendered, keeping navigation latency well under 100 ms.
    /// </summary>
    private async Task NavigateToMatchAsync(int index)
    {
        if (_searchMatches.Count == 0) return;

        index = ((index % _searchMatches.Count) + _searchMatches.Count) % _searchMatches.Count;
        _searchMatchCursor = index;
        UpdateSearchUi();

        var match = _searchMatches[index];
        var matchThread = match.Thread;  // null = coordinator

        // If the match is in a different thread than currently displayed, switch to it.
        // _searchNavigating suppresses the search-state clear inside SelectTranscriptThread.
        var activeThread = _selectedTranscriptThread ?? CoordinatorThread;
        var targetThread = matchThread ?? CoordinatorThread;
        if (!ReferenceEquals(activeThread, targetThread))
        {
            _searchNavigating = true;
            try
            {
                SelectTranscriptThread(targetThread);
                _cachedSearchPointers = null;  // Pointers are document-specific; invalidate after switch.
                // Allow the document assignment and layout to settle before adorner rebuild.
                await Dispatcher.BeginInvoke(DispatcherPriority.Loaded, static () => { }).Task;
            }
            finally
            {
                _searchNavigating = false;
            }
            activeThread = targetThread;
        }

        // Fast path: pointer cache is valid and the turn is already in the FlowDocument.
        // Just nudge the adorner cursor — no document walk needed.
        if (_cachedSearchPointers is not null
            && (activeThread.Kind != TranscriptThreadKind.Coordinator
                || _conversationManager.IsTurnRendered(match.TurnIndex)))
        {
            var cursorInList = index < _cachedMatchToCursor.Length ? _cachedMatchToCursor[index] : -1;
            _searchAdorner?.UpdateCurrentIndex(cursorInList);
            UpdateBucActiveHighlight(index);
            ScrollToMatchPointerIfNeeded(
                index < _cachedMatchScrollPointer.Length ? _cachedMatchScrollPointer[index] : null);
            SyncPromptNavButtons();
            return;
        }

        // Slow path: the turn may need to be prepended into the FlowDocument.
        if (activeThread.Kind == TranscriptThreadKind.Coordinator && matchThread is null)
        {
            // Invalidate the cache before prepending so stale pointers aren't used.
            if (!_conversationManager.IsTurnRendered(match.TurnIndex))
                _cachedSearchPointers = null;
            await _conversationManager.EnsureTurnRenderedAsync(match.TurnIndex);
        }

        // Schedule a full adorner rebuild once layout has settled.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RefreshAdornerHighlights);
    }

    /// <summary>
    /// Walks the FlowDocument forward from <paramref name="start"/>, returning a
    /// <see cref="TextRange"/> spanning the first case-insensitive occurrence of
    /// <paramref name="searchText"/>, or <c>null</c> if not found.
    /// </summary>
    private static TextRange? FindTextFromPointer(TextPointer start, string searchText)
    {
        if (string.IsNullOrEmpty(searchText)) return null;

        var navigator = start;
        while (navigator != null)
        {
            if (navigator.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var runText = navigator.GetTextInRun(LogicalDirection.Forward);
                var idx = runText.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var matchStart = navigator.GetPositionAtOffset(idx, LogicalDirection.Forward);
                    var matchEnd = matchStart?.GetPositionAtOffset(searchText.Length, LogicalDirection.Forward);
                    if (matchStart != null && matchEnd != null)
                        return new TextRange(matchStart, matchEnd);
                }
            }
            navigator = navigator.GetNextContextPosition(LogicalDirection.Forward);
        }
        return null;
    }

    /// <summary>
    /// Stateful forward-walking search helper that correctly handles
    /// <see cref="BlockUIContainer"/> elements (rendered code blocks and tables).
    /// Unlike chained <see cref="FindTextFromPointer"/> calls, this walker counts
    /// occurrences inside UIElement containers so that skip counts remain accurate
    /// even when some matches cannot be highlighted.
    /// </summary>
    private sealed class SearchWalker
    {
        private TextPointer? _cursor;
        // When a BlockUIContainer holds N occurrences, we return ContentEnd N times
        // (one per occurrence) so the caller's skip count stays correct.
        private int _pendingBucCount;
        private TextPointer? _pendingBucEnd;
        // Tracks the BUC element and occurrence index of the most-recently returned BUC match.
        private BlockUIContainer? _lastBucElement;
        private int _lastBucOccurrenceIndex;
        private int _lastBucTotalCount;

        public BlockUIContainer? LastBucElement => _lastBucElement;
        public int LastBucOccurrenceIndex => _lastBucOccurrenceIndex;

        public SearchWalker(TextPointer start) => _cursor = start;

        /// <summary>
        /// Returns the <see cref="TextRange"/> of the next occurrence of
        /// <paramref name="searchText"/>, or <c>null</c> when exhausted.
        /// A <b>zero-length</b> range (Start == End) signals a match inside a
        /// <see cref="BlockUIContainer"/> — the cursor is advanced correctly
        /// but the range cannot be drawn by the adorner.
        /// </summary>
        public TextRange? FindNext(string searchText)
        {
            // Drain remaining occurrences that were inside the last BUC.
            if (_pendingBucCount > 0)
            {
                _pendingBucCount--;
                _lastBucOccurrenceIndex = _lastBucTotalCount - _pendingBucCount - 1;
                return _pendingBucEnd is not null
                    ? new TextRange(_pendingBucEnd, _pendingBucEnd)
                    : null;
            }

            if (_cursor is null) return null;

            var nav = _cursor;
            while (nav is not null)
            {
                var ctx = nav.GetPointerContext(LogicalDirection.Forward);

                if (ctx == TextPointerContext.Text)
                {
                    var runText = nav.GetTextInRun(LogicalDirection.Forward);
                    var idx = runText.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var matchStart = nav.GetPositionAtOffset(idx, LogicalDirection.Forward);
                        var matchEnd = matchStart?.GetPositionAtOffset(searchText.Length, LogicalDirection.Forward);
                        if (matchStart is not null && matchEnd is not null)
                        {
                            _cursor = matchEnd;
                            return new TextRange(matchStart, matchEnd);
                        }
                    }
                }
                else if (ctx == TextPointerContext.ElementStart)
                {
                    // Detect BlockUIContainer (rendered table or code block).
                    var elem = nav.GetAdjacentElement(LogicalDirection.Forward);
                    if (elem is BlockUIContainer buc)
                    {
                        var bucText = GetBlockUIContainerText(buc);
                        if (!string.IsNullOrEmpty(bucText))
                        {
                            var count = CountOccurrences(bucText, searchText);
                            if (count > 0)
                            {
                                var bucEnd = buc.ContentEnd;
                                _cursor = bucEnd;
                                _pendingBucEnd = bucEnd;
                                _pendingBucCount = count - 1;
                                _lastBucElement = buc;
                                _lastBucOccurrenceIndex = 0;
                                _lastBucTotalCount = count;
                                // Zero-length range signals "found in UIElement, cannot highlight via adorner".
                                return new TextRange(bucEnd, bucEnd);
                            }
                        }
                    }
                }

                nav = nav.GetNextContextPosition(LogicalDirection.Forward);
            }

            _cursor = null;
            return null;
        }

        private static string? GetBlockUIContainerText(BlockUIContainer buc) =>
            buc.Child switch
            {
                StackPanel { Tag: string tableText } => tableText,
                TextBox tb => tb.Text,
                _ => null,
            };

        private static int CountOccurrences(string text, string search)
        {
            var count = 0;
            var from = 0;
            while (true)
            {
                var idx = text.IndexOf(search, from, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                count++;
                from = idx + search.Length;
            }
            return count;
        }
    }

    /// <summary>
    /// Rebuilds the adorner highlight list from all currently-rendered matches and
    /// updates the current-match index.  Skips matches whose turns are not yet in
    /// the FlowDocument.  Also highlights matching table cells inside
    /// <see cref="BlockUIContainer"/> elements by applying a background brush to
    /// the cell's TextBlock.  Populates the pointer cache used by
    /// <see cref="NavigateToMatchAsync"/> for fast cursor-update navigation.
    /// </summary>
    private void RefreshAdornerHighlights()
    {
        if (_searchAdorner is null) return;

        var query = SearchBox.Text;
        if (_searchMatches.Count == 0 || string.IsNullOrEmpty(query) || query.Length < 3)
        {
            _searchAdorner.Clear();
            _scrollbarAdorner?.Clear();
            _cachedSearchPointers = null;
            ClearBucCellHighlights();
            return;
        }

        var activeThread = _selectedTranscriptThread ?? CoordinatorThread;
        var pointers = new List<(TextPointer Start, TextPointer End, string Text)>(_searchMatches.Count);
        var cursorInList = -1;

        // Per-match cache arrays — rebuilt on every full refresh, then reused by the fast path.
        var matchToCursor = new int[_searchMatches.Count];
        var matchScrollPointer = new TextPointer?[_searchMatches.Count];
        var matchBucCell = new TextBlock?[_searchMatches.Count];
        Array.Fill(matchToCursor, -1);

        // Clear previously-highlighted BUC table cells before re-applying them.
        ClearBucCellHighlights();

        var walkerByKey = new Dictionary<(int TurnIndex, string Role), SearchWalker>();
        TextPointer? currentMatchPointer = null;

        for (var i = 0; i < _searchMatches.Count; i++)
        {
            var match = _searchMatches[i];
            var key = (match.TurnIndex, match.TurnRole);

            if (!walkerByKey.TryGetValue(key, out var walker))
            {
                // Only highlight matches that belong to the currently visible thread.
                var matchThread = match.Thread ?? CoordinatorThread;
                if (!ReferenceEquals(matchThread, activeThread))
                {
                    matchToCursor[i] = -1;
                    continue;
                }
                var searchFrom = GetSearchFromPointerSync(match, activeThread);
                if (searchFrom is null)
                {
                    SquadDashTrace.Write(TraceCategory.UI,
                        $"SEARCH_HIGHLIGHT[{i}] turn={match.TurnIndex} role={match.TurnRole} SKIPPED(unrendered)");
                    continue;
                }
                walker = new SearchWalker(searchFrom);
                walkerByKey[key] = walker;
            }

            var range = walker.FindNext(query);
            if (range is null)
            {
                SquadDashTrace.Write(TraceCategory.UI,
                    $"SEARCH_HIGHLIGHT[{i}] turn={match.TurnIndex} role={match.TurnRole} SKIPPED(walker_exhausted) cursor={i == _searchMatchCursor}");
                continue;
            }

            matchScrollPointer[i] = range.Start;

            // Zero-length range = match inside a BlockUIContainer (table / code block).
            // Cannot highlight via the adorner; apply a background brush to the cell instead.
            if (range.Start.CompareTo(range.End) == 0)
            {
                SquadDashTrace.Write(TraceCategory.UI,
                    $"SEARCH_HIGHLIGHT[{i}] turn={match.TurnIndex} role={match.TurnRole} BUC_MATCH cursor={i == _searchMatchCursor}");
                if (walker.LastBucElement is not null)
                {
                    var bucCell = GetTableCellByOccurrence(walker.LastBucElement, walker.LastBucOccurrenceIndex, query);
                    if (bucCell is not null)
                    {
                        _bucHighlightedCells.Add(bucCell);
                        matchBucCell[i] = bucCell;
                    }
                }
                if (i == _searchMatchCursor)
                    currentMatchPointer = range.Start;
                continue;
            }

            if (i == _searchMatchCursor)
            {
                cursorInList = pointers.Count;
                currentMatchPointer = range.Start;
            }

            matchToCursor[i] = pointers.Count;
            var actualText = new TextRange(range.Start, range.End).Text;
            SquadDashTrace.Write(TraceCategory.UI,
                $"SEARCH_HIGHLIGHT[{i}] turn={match.TurnIndex} role={match.TurnRole} TEXT_MATCH listIdx={pointers.Count} cursor={i == _searchMatchCursor} text='{actualText}'");
            pointers.Add((range.Start, range.End, string.IsNullOrEmpty(actualText) ? query : actualText));
        }

        // Apply BUC cell backgrounds + dark text: all inactive first, then the active cell on top.
        // Read brushes from the current theme resources (same as SearchHighlightAdorner) so
        // that BUC highlights automatically match the active theme.
        var bucInactiveBg = GetThemeBrush("SearchHighlight", Color.FromRgb(98, 84, 44));
        var bucActiveBg = GetThemeBrush("SearchHighlightCurrent", Color.FromRgb(255, 229, 122));
        var bucInactiveFg = GetThemeBrush("SearchHighlightText", Color.FromRgb(18, 13, 0));
        var bucActiveFg = GetThemeBrush("SearchHighlightTextCurrent", Color.FromRgb(0, 0, 0));
        foreach (var cell in _bucHighlightedCells)
        {
            cell.Background = bucInactiveBg;
            cell.Foreground = bucInactiveFg;
        }
        if (_searchMatchCursor >= 0 && _searchMatchCursor < matchBucCell.Length
            && matchBucCell[_searchMatchCursor] is { } activeBucCell)
        {
            activeBucCell.Background = bucActiveBg;
            activeBucCell.Foreground = bucActiveFg;
        }

        _searchAdorner.SetMatches(pointers, cursorInList);

        // Persist the pointer cache for fast-path navigation.
        _cachedSearchPointers = pointers;
        _cachedMatchToCursor = matchToCursor;
        _cachedMatchScrollPointer = matchScrollPointer;
        _cachedMatchBucCell = matchBucCell;

        // Scroll to the current match.  Fall back to the first rendered match if the
        // current match is unrendered (handles auto-scroll when typing finds match 0
        // in an older, not-yet-prepended turn).
        if (currentMatchPointer is null && pointers.Count > 0)
            currentMatchPointer = pointers[0].Start;
        ScrollToMatchPointerIfNeeded(currentMatchPointer);

        SyncPromptNavButtons();

        // Compute proportional positions for the scrollbar marker adorner.
        if (_scrollbarAdorner is not null && _transcriptScrollViewer is not null)
        {
            var totalHeight = _transcriptScrollViewer.ExtentHeight;
            if (totalHeight > 0)
            {
                var positions = new List<double>(pointers.Count);
                foreach (var (s, _, _) in pointers)
                {
                    if (s is null) continue;
                    var rect = s.GetCharacterRect(LogicalDirection.Forward);
                    if (rect.IsEmpty) continue;
                    var docY = rect.Top + _transcriptScrollViewer.VerticalOffset;
                    positions.Add(docY / totalHeight);
                }
                _scrollbarAdorner.SetPositions(positions);
            }
            else
            {
                _scrollbarAdorner.Clear();
            }
        }
    }

    // ── Search helper methods ──────────────────────────────────────────────────

    /// <summary>
    /// Scrolls the transcript so that <paramref name="pointer"/> is visible.
    /// If the pointer is already fully visible, does nothing.
    /// </summary>
    private void ScrollToMatchPointerIfNeeded(TextPointer? pointer)
    {
        if (pointer is null) return;
        var sv = OutputTextBox.Template?.FindName("PART_ContentHost", OutputTextBox) as ScrollViewer;
        if (sv is null) return;

        var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
        if (rect.IsEmpty)
        {
            var para = pointer.Paragraph;
            if (para is not null)
                rect = para.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        }

        if (rect.IsEmpty) return;

        var isFullyVisible = rect.Top >= 0 && rect.Bottom <= sv.ViewportHeight;
        SquadDashTrace.Write(TraceCategory.UI,
            $"SEARCH_SCROLL cursor={_searchMatchCursor} rectTop={rect.Top:F0} rectBottom={rect.Bottom:F0} vp={sv.ViewportHeight:F0} offset={sv.VerticalOffset:F0} fullyVisible={isFullyVisible}");
        if (!isFullyVisible)
            _scrollController.ScrollToOffset(sv.VerticalOffset + rect.Top);
    }

    /// <summary>
    /// Resets all highlighted BUC table cells to the inactive brush, then marks the
    /// cell at <paramref name="matchIndex"/> as the active (bright) cell.
    /// </summary>
    private void UpdateBucActiveHighlight(int matchIndex)
    {
        var bucInactiveBg = GetThemeBrush("SearchHighlight", Color.FromRgb(98, 84, 44));
        var bucActiveBg = GetThemeBrush("SearchHighlightCurrent", Color.FromRgb(255, 229, 122));
        var bucInactiveFg = GetThemeBrush("SearchHighlightText", Color.FromRgb(18, 13, 0));
        var bucActiveFg = GetThemeBrush("SearchHighlightTextCurrent", Color.FromRgb(0, 0, 0));
        foreach (var cell in _bucHighlightedCells)
        {
            cell.Background = bucInactiveBg;
            cell.Foreground = bucInactiveFg;
        }
        if (_cachedMatchBucCell is not null
            && matchIndex >= 0 && matchIndex < _cachedMatchBucCell.Length
            && _cachedMatchBucCell[matchIndex] is { } active)
        {
            active.Background = bucActiveBg;
            active.Foreground = bucActiveFg;
        }
    }

    /// <summary>
    /// Removes all BUC cell background highlights and clears the tracked set.
    /// Also restores each cell's Foreground to the theme-defined value via ClearValue.
    /// </summary>
    private void ClearBucCellHighlights()
    {
        foreach (var cell in _bucHighlightedCells)
        {
            cell.Background = null;
            cell.ClearValue(TextBlock.ForegroundProperty);
        }
        _bucHighlightedCells.Clear();
    }

    /// <summary>
    /// Finds the <see cref="TextBlock"/> in <paramref name="buc"/>'s StackPanel whose
    /// cumulative occurrence of <paramref name="query"/> equals
    /// <paramref name="occurrenceIndex"/> (0-based across all cells, left→right, top→bottom).
    /// Returns <c>null</c> if the cell cannot be located.
    /// </summary>
    private static TextBlock? GetTableCellByOccurrence(BlockUIContainer buc, int occurrenceIndex, string query)
    {
        if (buc.Child is not StackPanel sp) return null;
        var count = 0;
        foreach (var rowChild in sp.Children)
        {
            if (rowChild is not Grid grid) continue;
            foreach (var colChild in grid.Children)
            {
                if (colChild is not Border border || border.Child is not TextBlock tb) continue;
                var cellText = GetTextBlockContent(tb);
                var cellCount = CountSubstringOccurrences(cellText, query);
                if (count + cellCount > occurrenceIndex)
                    return tb;  // target occurrence is inside this cell
                count += cellCount;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the text content of a <see cref="TextBlock"/>.  Uses the Inlines tree
    /// if populated (as done by <c>AppendTextRuns</c>), otherwise falls back to
    /// <see cref="TextBlock.Text"/>.
    /// </summary>
    private static string GetTextBlockContent(TextBlock tb)
    {
        if (tb.Inlines.Count == 0)
            return tb.Text;
        var sb = new System.Text.StringBuilder();
        AppendInlineText(tb.Inlines, sb);
        return sb.ToString();
    }

    /// <summary>
    /// Recursively appends the plain text of all <see cref="Run"/> and container
    /// <see cref="Span"/> elements within <paramref name="inlines"/>.
    /// </summary>
    private static void AppendInlineText(InlineCollection inlines, System.Text.StringBuilder sb)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    sb.Append(run.Text);
                    break;
                case Span span:
                    AppendInlineText(span.Inlines, sb);
                    break;
            }
        }
    }

    /// <summary>
    /// Counts the number of non-overlapping case-insensitive occurrences of
    /// <paramref name="query"/> inside <paramref name="text"/>.
    /// </summary>
    private static int CountSubstringOccurrences(string text, string query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return 0;
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(query, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += query.Length;
        }
        return count;
    }

    private static SolidColorBrush MakeFrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>
    /// Looks up a <see cref="Brush"/> from the current application theme resources by
    /// <paramref name="key"/>.  Falls back to a new brush with <paramref name="fallback"/>
    /// if the key is not found (e.g. in tests or before resources load).
    /// </summary>
    private static Brush GetThemeBrush(string key, Color fallback)
        => Application.Current?.Resources[key] as Brush ?? new SolidColorBrush(fallback);

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindScrollViewer(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static ScrollBar? FindVerticalScrollBar(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollBar sb && sb.Orientation == Orientation.Vertical) return sb;
            var found = FindVerticalScrollBar(child);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>
    /// Returns the TextPointer from which to begin scanning for <paramref name="match"/>,
    /// using only already-rendered paragraphs (no async rendering).
    /// Returns <c>null</c> if the turn is not yet in the document.
    /// </summary>
    private TextPointer? GetSearchFromPointerSync(TurnSearchMatch match, TranscriptThreadState thread)
    {
        if (thread.Kind == TranscriptThreadKind.Coordinator)
        {
            var startedAt = _conversationManager.GetCoordinatorTurnStartedAt(match.TurnIndex);
            if (!startedAt.HasValue) return null;
            var entry = CoordinatorThread.PromptParagraphs.FirstOrDefault(e => e.Timestamp == startedAt.Value);
            if (entry is null) return null;
            return match.TurnRole == "assistant" ? entry.Paragraph.ContentEnd : entry.Paragraph.ContentStart;
        }
        else
        {
            if (match.TurnIndex < 0 || match.TurnIndex >= thread.PromptParagraphs.Count) return null;
            var entry = thread.PromptParagraphs[match.TurnIndex];
            return match.TurnRole == "assistant" ? entry.Paragraph.ContentEnd : entry.Paragraph.ContentStart;
        }
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ClearSearch();
        }
        catch (Exception ex)
        {
            HandleUiCallbackException(nameof(ClearSearchButton_Click), ex);
        }
    }

    private void ClearSearch()
    {
        _searchCts?.Cancel();
        _searchDebounceTimer?.Stop();
        _searchMatches = [];
        _searchMatchCursor = 0;
        SearchBox.Text = string.Empty;
        _searchAdorner?.Clear();
        _scrollbarAdorner?.Clear();
        _cachedSearchPointers = null;
        ClearBucCellHighlights();
        UpdateSearchUi();
    }

    /// <summary>
    /// Updates the visibility and text of the FindPrev / FindNext buttons and the
    /// match-count label based on current <see cref="_searchMatches"/> state.
    /// Must be called on the UI thread.
    /// </summary>
    private void UpdateSearchUi()
    {
        if (string.IsNullOrEmpty(SearchBox.Text))
        {
            // No active search — hide all navigation chrome.
            FindPrevButton.Visibility = Visibility.Collapsed;
            FindNextButton.Visibility = Visibility.Collapsed;
            SearchMatchCountText.Visibility = Visibility.Collapsed;
            ClearSearchButton.Visibility = Visibility.Collapsed;
        }
        else if (SearchBox.Text.Length < 3)
        {
            // Query too short to search — prompt the user.
            FindPrevButton.Visibility = Visibility.Collapsed;
            FindNextButton.Visibility = Visibility.Collapsed;
            SearchMatchCountText.Visibility = Visibility.Visible;
            SearchMatchCountText.Text = "Type at least 3 characters";
            ClearSearchButton.Visibility = Visibility.Visible;
        }
        else if (_searchMatches.Count == 0)
        {
            // Query entered but no matches found.
            FindPrevButton.Visibility = Visibility.Collapsed;
            FindNextButton.Visibility = Visibility.Collapsed;
            SearchMatchCountText.Visibility = Visibility.Visible;
            SearchMatchCountText.Text = "No matches";
            ClearSearchButton.Visibility = Visibility.Visible;
        }
        else
        {
            // One or more matches — show full navigation chrome.
            FindPrevButton.Visibility = Visibility.Visible;
            FindNextButton.Visibility = Visibility.Visible;
            SearchMatchCountText.Visibility = Visibility.Visible;
            SearchMatchCountText.Text = $"{_searchMatchCursor + 1} of {_searchMatches.Count}";
            ClearSearchButton.Visibility = Visibility.Visible;
        }
    }
}

/// <summary>
/// COM-visible scripting bridge that allows JavaScript in the docs <see cref="System.Windows.Controls.WebBrowser"/>
/// to call back into <see cref="MainWindow"/>.  Set as
/// <c>DocMarkdownViewer.ObjectForScripting</c> so that <c>window.external.*</c>
/// calls in the rendered HTML reach this object.
/// </summary>
[ComVisible(true)]
[System.Runtime.InteropServices.ClassInterface(System.Runtime.InteropServices.ClassInterfaceType.AutoDispatch)]
public sealed class DocViewerScriptingBridge
{
    private readonly MainWindow _window;

    public DocViewerScriptingBridge(MainWindow window) => _window = window;

    /// <summary>Handles hyperlink navigation forwarded from the JS click handler.</summary>
    public void Navigate(string href)
    {
        _window.Dispatcher.BeginInvoke(() => _window.InvokeDocNavigation(href));
    }

    /// <summary>
    /// Called when the user right-clicks a 📸 placeholder blockquote.
    /// <paramref name="imagePath"/> is the relative path from the <c>src</c>
    /// attribute of the <c>&lt;img&gt;</c> immediately preceding the blockquote.
    /// </summary>
    public void ShowScreenshotMenu(string imagePath)
    {
        SquadDashTrace.Write("DocViewer", $"ShowScreenshotMenu called: {imagePath}");
        _window.Dispatcher.BeginInvoke(() => _window.ShowDocScreenshotContextMenu(imagePath));
    }

    /// <summary>
    /// Called when the user right-clicks an existing image in the docs viewer.
    /// <paramref name="imagePath"/> is the relative path from the <c>src</c> attribute.
    /// </summary>
    public void ShowImageMenu(string imagePath)
    {
        SquadDashTrace.Write("DocViewer", $"ShowImageMenu called: {imagePath}");
        _window.Dispatcher.BeginInvoke(() => _window.ShowImageContextMenu(imagePath));
    }

    /// <summary>
    /// Feature 3: Called when hovering over an element in the doc viewer.
    /// <paramref name="lineHint"/> is the line number from data-source-line attribute.
    /// </summary>
    public void HoverElement(string lineHint)
    {
        _window.Dispatcher.BeginInvoke(() => _window.HighlightDocSourceFromHover(lineHint));
    }
}
