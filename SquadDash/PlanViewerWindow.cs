using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace SquadDash;

internal sealed class PlanViewerWindow : ChromedWindow
{
    private const double BaseNodeWidth = 220;
    private const double BaseNodeHeight = 112;
    private const double BaseColumnSpacing = 360;
    private const double BaseRowSpacing = 152;
    private const string VerityCrossHandle = "verity-cross";
    private const string SquadCoordinatorHandle = "Squad";
    private double _scaleFactor;
    private double NodeWidth;
    private double NodeHeight;
    private double ColumnSpacing;
    private double RowSpacing;

    private PendingDecomposePlan? _plan;
    private Plan? _durablePlan;
    private IReadOnlyList<(Point Center, DecomposedSubTask[] Targets, string[] Dependencies, int MinTargetLevel, int MaxDepLevel)>? _visualizationGates;
    private IReadOnlyList<IReadOnlyList<PlanGraphConnectorGroup>>? _visualizationGateConnectorGroups;
    private FrameworkElement?[]? _allGateBadgeElements;

    private readonly string? _activeBranch;
    private readonly double _quickReplyFontSize;
    private readonly Func<DecomposePlanActionDefinition, Task<bool>>? _applyAction;
    private readonly Action<Plan>? _onGatesChanged;
    private readonly Action<Plan>? _onStartPlan;
    private readonly Action<Plan>? _onResumePlan;
    private readonly Action<Plan>? _onPausePlan;
    private readonly Func<Plan, Task<bool>>? _onAdoptVerifiedCommitRange;
    private readonly string? _interruptedPrimaryActionLabel;
    private readonly string? _interruptedPrimaryActionHint;
    private readonly Func<Plan, PlanRecoveryActionSetPresentation?>? _resolveInterruptedActionSet;
    private readonly Action<Plan>? _onEndPlan;
    private readonly Action<Plan, string>? _onApproveGate;
    private readonly Action<Plan, string>? _onUnapproveGate;
    private readonly Func<PlanPreflightBlockedException, Task>? _viewPreflightChanges;
    private readonly Func<Task<bool>>? _isPreflightWorkspaceClean;
    private readonly Func<Task<IReadOnlyList<string>?>>? _getPreflightDirtyPaths;
    private readonly Func<Task<bool>>? _initializeRepository;
    private readonly Action<string>? _onOpenCommit;
    private readonly Action<IReadOnlyList<PlanEvidenceCommit>>? _onOpenCommits;
    private readonly Action<Plan>? _onOpenInterruptedChanges;
    private readonly Func<PlanTask, Task<IReadOnlyList<PlanTaskFileChange>>>? _resolveTaskFileChanges;
    private readonly Action<PlanTask, string>? _onOpenTaskFileDiff;
    private readonly Action<Plan>? _onContinueInterruptedTask;
    private readonly Action<Plan>? _onReplanRemainingWork;
    private readonly Action<PlanHumanReviewChecklistItem, bool>? _onReviewSelection;
    private readonly Action<Plan, string, IReadOnlyList<PlanHumanReviewChecklistItem>>? _onAddressUncheckedItems;
    private readonly Func<string, (ImageSource? Image, string Initial, Brush Accent)?>? _resolveAgentAvatar;
    private readonly Func<string, bool>? _isTaskActivityActive;
    private readonly Func<Plan, bool>? _isPlanRunnerActive;
    private readonly Func<string, bool>? _isRecoveryAssessmentActive;
    private readonly Func<string, PlanTaskCorrectionActivityState>? _resolveTaskCorrectionActivity;
    private readonly Func<string, bool>? _isApprovalChangeActivityActive;
    private readonly Func<DecomposedTaskGroup, PlanDefinitionEditResult>? _saveDefinitionEdit;
    private readonly Action<PendingDecomposePlan>? _requestAiReview;
    private readonly Action<PlanVisualizerReviewPayload?>? _saveReviewState;
    private readonly Action<Dictionary<string, double>>? _saveLayoutNudges;
    private readonly Action<Plan, string>? _onFixValidation;
    private readonly Action<Plan, string>? _onReviewValidation;
    private readonly Func<ApplicationSettingsSnapshot>? _getSettings;
    private readonly Func<IReadOnlyList<RosterAgent>>? _getAssignableAgents;
    private readonly Func<PendingDecomposePlan, bool>? _isPendingPlanExecutionPreflightActive;
    private readonly Action<string>? _onDescriptionLinkClicked;
    private readonly Action<string, string>? _onOpenWorkMetrics;
    private PttTextBoxAttachment? _pttAttachment;
    private System.Windows.Threading.DispatcherTimer? _preflightPollTimer;
    private PlanViewerLiveSyncHandler? _liveSyncHandler;
    private Action<PlanTaskActivityPulseEvent>? _taskActivityPulseHandler;
    private Action<PlanValidationActivityPulseEvent>? _validationActivityPulseHandler;
    private Action<PlanRecoveryAssessmentActivityEvent>? _recoveryAssessmentActivityHandler;
    private Action<PlanTaskCorrectionActivityEvent>? _taskCorrectionActivityHandler;
    private Action<PlanApprovalChangeActivityEvent>? _approvalChangeActivityHandler;
    private readonly HashSet<string> _recoveryAssessmentTaskIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlanTaskCorrectionActivityState> _taskCorrectionStates =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeApprovalChangeGateIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dismissedApprovalChangeGateIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivitySpinner> _taskSpinnersById =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivitySpinner> _validationSpinnersById =
        new(StringComparer.Ordinal);
    private Border? _contentHolder;
    private ScrollViewer? _graphScroll;
    private Canvas? _graphCanvas;
    private readonly PlanVisualizerViewportStore _viewportStore;
    private readonly string? _squadFolderPath;
    private readonly string _viewportGroupId;
    private readonly bool _canPersistViewport;
    private readonly bool _isReadOnlyFixture;
    private readonly PlanVisualizerViewportState? _initialViewport;
    private readonly System.Windows.Threading.DispatcherTimer _viewportSaveTimer;
    private PlanVisualizerViewportState _viewportState;
    private double _zoomFactor;
    private bool _viewportReady;
    private bool _autoScrollForPlanProgressPending;
    private bool _isViewerClosed;
    private bool _spacePanHeld;
    private bool _isGraphPanning;
    private Point _panStart;
    private double _panHorizontalOffset;
    private double _panVerticalOffset;
    private FlowDocument? _detailDocument;
    private FlowDocumentScrollViewer? _detailViewer;
    private PlanDescriptionPresentation? _descriptionPresentation;
    private PlanDescriptionDocumentSession? _descriptionSession;
    private readonly List<FlowDocument> _descriptionPreviews = [];
    private TextBox? _descriptionFindInput;
    private double _inspectorFontScale = 1.0;
    private WrapPanel? _inspectorToolbarPanel;
    private Func<string?>? _inspectorEditorSaveAction;
    private TextBlock? _inspectorEditorStatus;
    private string? _lastStagedMutationError;
    private TextBox? _inspectorPrimaryEditor;
    private FrameworkElement? _header;
    private FrameworkElement? _readingHint;
    private double _canvasContentWidth;
    private double _canvasContentHeight;
    private double _activeVisibilityStart = double.NaN;
    private double _activeVisibilityEnd = double.NaN;
    private AutomaticScrollTarget _automaticScrollTarget;
    private DateTime _lastViewerInteractionUtc = DateTime.MinValue;
    private bool _initialLayoutApplied;
    private bool _windowGeometryRestored;
    private StagedPlanEditHistory? _editHistory;
    private bool _isInspectorEditing;
    private bool _focusPrimaryEditorAfterRebuild;
    private PlanVisualizerReviewPayload? _review;
    private int _reviewIndex = -1;
    private FrmUltimateCallout? _reviewCallout;
    private bool _isAiReviewPending;
    private string? _reviewStatusMessage;
    private TextBlock? _actionStatusText;
    private string? _actionStatusMessage;
    private (Dictionary<string, Point> OldPositions, IReadOnlyList<string> AffectedTaskIds)? _pendingAnimation;
    private int _buildSeq;
    private readonly PlanGraphEdgeRenderingContract _graphEdgeRenderingContract;

    private enum AutomaticScrollTarget
    {
        None,
        PlanStart,
        ActiveRange,
        PlanEnd,
    }

    private record SelectedElementIdentity(string Kind, string Id);
    private record CommittedColumnReorder(string ColumnTaskId, IReadOnlyList<string> NewTaskOrder);
    private sealed record ContractExampleEditorRow(
        Border Card,
        TextBlock Heading,
        string ExampleId,
        TextBox Title,
        TextBox Input,
        TextBox ExpectedBehavior,
        TextBox Language);
    private sealed class DragConnectorState
    {
        public PlanGraphConnectorGroup Group { get; set; } = null!;
        public PlanGraphConnectorEndpointRole Role { get; init; }
        public double? StartOffsetWithinCard { get; init; }
        public double? EndOffsetWithinCard { get; init; }
        public string? SourceTaskId { get; init; }
        public string? TargetTaskId { get; init; }
        public int BaseSkipCount { get; init; }
        public bool IsDashed { get; init; }
    }
    private sealed class TaskCardDragState
    {
        public FrameworkElement Element { get; init; } = null!;
        public string TaskId { get; init; } = string.Empty;
        public Point MouseOffsetWithinCard { get; init; }
        public Point DragStartCanvasPoint { get; init; }
        public double OriginalTop { get; init; }
        public double ColumnX { get; init; }
        public double MilestoneTop { get; init; }
        public double MilestoneBottom { get; init; }
        public Effect? OriginalCardEffect { get; init; }
        public IReadOnlyList<DragConnectorState> ConnectedConnectorStates { get; init; } = [];
        public IReadOnlyList<(UIElement Connector, Effect? OriginalEffect)> ConnectedConnectors { get; init; } = [];
        public IReadOnlyList<(UIElement Element, double OriginalTop)> DragOverlayElements { get; init; } = [];
        public IReadOnlyList<(FrameworkElement Card, double OriginalTop, string TaskId)> SiblingCards { get; init; } = [];
        public List<string> CurrentColumnOrder { get; init; } = [];
        public IReadOnlyList<string> PreDragColumnOrder { get; init; } = [];
        public IReadOnlyDictionary<string, double> PreDragTaskNudges { get; init; } =
            new Dictionary<string, double>(StringComparer.Ordinal);
        public Dictionary<string, double> DisplacedSiblingTops { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, IReadOnlyList<(UIElement Element, double OriginalTop)>> SiblingOverlayElements { get; init; } =
            new(StringComparer.Ordinal);
        public IReadOnlyList<double> ColumnSlotTops { get; init; } = [];
        public string ColumnIdentifier { get; init; } = string.Empty;
        public bool IsActive { get; set; }
    }
    private SelectedElementIdentity? _selectedElement;
    private (Border border, Thickness original)? _selectionBorderState;
    private TextBlock? _selectedStageTitleBlock; // stage title whose Foreground was overridden on selection
    private FrameworkElement? _selectionGlowElement;
    private readonly Dictionary<string, List<Border>> _agentChipElements = new(StringComparer.OrdinalIgnoreCase);
    private List<Border> _selectedAgentChipBorders = [];
    private TaskCardDragState? _taskCardDragState;
    private CommittedColumnReorder? _lastCommittedColumnOrder;
    private readonly Dictionary<string, List<PlanGraphConnectorGroup>> _connectorsByTask = new(StringComparer.Ordinal);
    private Dictionary<string, Border> _borderByTask = new(StringComparer.Ordinal);
    private Dictionary<string, string> _taskOrdinalById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Point> _taskCardPositions = new(StringComparer.Ordinal);
    private Dictionary<string, double> _taskNudges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _naturalTaskSlotTops = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<UIElement>> _taskDragOverlaysByTask = new(StringComparer.Ordinal);
    private readonly List<ValidationShieldPresenter.LayoutRect> _allClusterFootprints = [];
    private readonly List<double> _lockedMilestoneBoundaryXs = [];
    private bool _suppressTaskCardCancelOnLostCapture;
    private readonly List<SelectedElementIdentity> _inspectorHistory = [];
    private int _inspectorHistoryIndex = -1;
    private bool _suppressHistoryPush;

    internal string? CurrentRevision => _plan?.Revision;

    internal PlanViewerWindow(
        PendingDecomposePlan plan,
        string? activeBranch,
        double quickReplyFontSize,
        Func<DecomposePlanActionDefinition, Task<bool>>? applyAction = null,
        Plan? durablePlan = null,
        Action<Plan>? onGatesChanged = null,
        Action<Plan>? onStartPlan    = null,
        Action<Plan>? onResumePlan   = null,
        Action<Plan>? onPausePlan    = null,
        Func<Plan, Task<bool>>? onAdoptVerifiedCommitRange = null,
        string? interruptedPrimaryActionLabel = null,
        string? interruptedPrimaryActionHint = null,
        Action<Plan>? onEndPlan      = null,
        Action<Plan, string>? onApproveGate = null,
        Action<Plan, string>? onUnapproveGate = null,
        Func<PlanPreflightBlockedException, Task>? viewPreflightChanges = null,
        Func<Task<bool>>? isPreflightWorkspaceClean = null,
        Func<Task<IReadOnlyList<string>?>>? getPreflightDirtyPaths = null,
        WeakEventBroker? broker = null,
        Action<string>? onOpenCommit = null,
        Func<string, (ImageSource? Image, string Initial, Brush Accent)?>? resolveAgentAvatar = null,
        Func<string, bool>? isTaskActivityActive = null,
        Func<Plan, bool>? isPlanRunnerActive = null,
        Func<string, bool>? isRecoveryAssessmentActive = null,
        Func<string, PlanTaskCorrectionActivityState>? resolveTaskCorrectionActivity = null,
        Func<string, bool>? isApprovalChangeActivityActive = null,
        Func<Task<bool>>? initializeRepository = null,
        Action<IReadOnlyList<PlanEvidenceCommit>>? onOpenCommits = null,
        Func<PlanTask, Task<IReadOnlyList<PlanTaskFileChange>>>? resolveTaskFileChanges = null,
        Action<PlanTask, string>? onOpenTaskFileDiff = null,
        Action<Plan>? onContinueInterruptedTask = null,
        Action<Plan>? onReplanRemainingWork = null,
        Action<PlanHumanReviewChecklistItem, bool>? onReviewSelection = null,
        Action<Plan, string, IReadOnlyList<PlanHumanReviewChecklistItem>>? onAddressUncheckedItems = null,
        Func<DecomposedTaskGroup, PlanDefinitionEditResult>? saveDefinitionEdit = null,
        Action<PendingDecomposePlan>? requestAiReview = null,
        Action<PlanVisualizerReviewPayload?>? saveReviewState = null,
        Action<Dictionary<string, double>>? saveLayoutNudges = null,
        Action<Plan, string>? onFixValidation = null,
        Action<Plan, string>? onReviewValidation = null,
        Func<ApplicationSettingsSnapshot>? getSettings = null,
        Func<IReadOnlyList<RosterAgent>>? getAssignableAgents = null,
        Func<Plan, PlanRecoveryActionSetPresentation?>? resolveInterruptedActionSet = null,
        Action<Plan>? onOpenInterruptedChanges = null,
        Func<PendingDecomposePlan, bool>? isPendingPlanExecutionPreflightActive = null,
        double lineThicknessMultiplier = 1.0,
        string? squadFolderPath = null,
        Action<string>? onDescriptionLinkClicked = null,
        Action<string, string>? onOpenWorkMetrics = null,
        bool isReadOnlyFixture = false)
        : base(
            captionHeight: CloseButtonHeight,
            resizeMode: ResizeMode.CanResize,
            resizeBorderThickness: 8)
    {
        const double baseFontSize = 12.0;
        var currentFontSize = Application.Current?.Resources["FontSizeBody"] is double fs ? fs : baseFontSize;
        _scaleFactor = currentFontSize / baseFontSize;

        NodeWidth      = BaseNodeWidth * _scaleFactor;
        NodeHeight     = BaseNodeHeight * _scaleFactor;
        ColumnSpacing  = BaseColumnSpacing * _scaleFactor;
        RowSpacing     = BaseRowSpacing * _scaleFactor;

        _activeBranch       = activeBranch;
        _quickReplyFontSize = quickReplyFontSize;
        _applyAction        = applyAction;
        _onGatesChanged     = onGatesChanged;
        _onStartPlan        = onStartPlan;
        _onResumePlan       = onResumePlan;
        _onPausePlan        = onPausePlan;
        _onAdoptVerifiedCommitRange = onAdoptVerifiedCommitRange;
        _interruptedPrimaryActionLabel = interruptedPrimaryActionLabel;
        _interruptedPrimaryActionHint = interruptedPrimaryActionHint;
        _resolveInterruptedActionSet = resolveInterruptedActionSet;
        _onEndPlan          = onEndPlan;
        _onApproveGate      = onApproveGate;
        _onUnapproveGate    = onUnapproveGate;
        _viewPreflightChanges = viewPreflightChanges;
        _isPreflightWorkspaceClean = isPreflightWorkspaceClean;
        _getPreflightDirtyPaths = getPreflightDirtyPaths;
        _initializeRepository = initializeRepository;
        _onOpenCommit = onOpenCommit;
        _onOpenCommits = onOpenCommits;
        _onOpenInterruptedChanges = onOpenInterruptedChanges;
        _resolveTaskFileChanges = resolveTaskFileChanges;
        _onOpenTaskFileDiff = onOpenTaskFileDiff;
        _onContinueInterruptedTask = onContinueInterruptedTask;
        _onReplanRemainingWork = onReplanRemainingWork;
        _onReviewSelection = onReviewSelection;
        _onAddressUncheckedItems = onAddressUncheckedItems;
        _resolveAgentAvatar = resolveAgentAvatar;
        _isTaskActivityActive = isTaskActivityActive;
        _isPlanRunnerActive = isPlanRunnerActive;
        _isRecoveryAssessmentActive = isRecoveryAssessmentActive;
        _resolveTaskCorrectionActivity = resolveTaskCorrectionActivity;
        _isApprovalChangeActivityActive = isApprovalChangeActivityActive;
        _saveDefinitionEdit = saveDefinitionEdit;
        _requestAiReview = requestAiReview;
        _saveReviewState = saveReviewState;
        _saveLayoutNudges = saveLayoutNudges;
        _onFixValidation = onFixValidation;
        _onReviewValidation = onReviewValidation;
        _getSettings = getSettings;
        _getAssignableAgents = getAssignableAgents;
        _isPendingPlanExecutionPreflightActive = isPendingPlanExecutionPreflightActive;
        _onDescriptionLinkClicked = onDescriptionLinkClicked;
        _onOpenWorkMetrics = onOpenWorkMetrics;
        _squadFolderPath = squadFolderPath;
        _viewportStore = new PlanVisualizerViewportStore(squadFolderPath);
        _viewportGroupId = plan.Group.GroupId;
        _isReadOnlyFixture = isReadOnlyFixture;
        _canPersistViewport = squadFolderPath is not null && !isReadOnlyFixture;
        _initialViewport = isReadOnlyFixture ? null : _viewportStore.Load(_viewportGroupId);
        _zoomFactor = isReadOnlyFixture ? 1.0 : _initialViewport?.Zoom ?? _viewportStore.LoadDefaultZoom();
        _viewportState = _initialViewport ?? new PlanVisualizerViewportState(_zoomFactor, 0, 0);
        _viewportSaveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        _viewportSaveTimer.Tick += (_, _) => SaveGraphViewport();
        _graphEdgeRenderingContract = new PlanGraphEdgeRenderingContract(lineThicknessMultiplier);
        if (_getSettings is not null)
            _pttAttachment = new PttTextBoxAttachment(_getSettings, this, Dispatcher);
        if (saveDefinitionEdit is not null)
            _editHistory = new StagedPlanEditHistory(plan.Group);

        Title     = $"Plan — {durablePlan?.DisplayName ?? plan.Group.GroupTitle}";
        Width     = 1200;
        Height    = 720;
        MinWidth  = 760;
        MinHeight = 480;

        PreviewMouseDown += (_, _) => _lastViewerInteractionUtc = DateTime.UtcNow;
        PreviewKeyDown += (_, _) => _lastViewerInteractionUtc = DateTime.UtcNow;
        PreviewKeyDown += OnPlanViewerPreviewKeyDown;
        CommandManager.AddPreviewExecutedHandler(this, (_, e) =>
        {
            if (e.Command != ApplicationCommands.Find || _isInspectorEditing) return;
            ShowDescriptionFind();
            e.Handled = true;
        });
        CommandManager.AddPreviewCanExecuteHandler(this, (_, e) =>
        {
            if (e.Command != ApplicationCommands.Find || _isInspectorEditing) return;
            e.CanExecute = true;
            e.Handled = true;
        });
        PreviewKeyUp += OnGraphPanPreviewKeyUp;
        Deactivated += (_, _) => ReleaseGraphPan();
        PreviewGotKeyboardFocus += (_, e) =>
        {
            if (IsGraphTextInput(e.NewFocus))
                ReleaseGraphPan();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (!e.Handled &&
                _pttAttachment?.HandlePreviewKeyDown(e, Keyboard.FocusedElement as TextBox) == true)
                e.Handled = true;
        };
        PreviewKeyUp += (_, e) => _pttAttachment?.HandlePreviewKeyUp(e);
        ContentRendered += OnInitialContentRendered;

        BuildContent(plan, durablePlan);
        Closing += (_, _) => SaveGraphViewport();
        Closed += (_, _) =>
        {
            _isViewerClosed = true;
            DetachDescriptionDocuments();
            ReleaseGraphPan();
            _viewportSaveTimer.Stop();
            _preflightPollTimer?.Stop();
            _reviewCallout?.Close();
            if (_pttAttachment?.IsActive == true) _ = _pttAttachment.StopAsync();
            _pttAttachment?.Dispose();
        };

        if (broker is not null && durablePlan is not null)
        {
            _liveSyncHandler = new PlanViewerLiveSyncHandler(
                durablePlan.PlanId,
                durablePlan,
                broker,
                updatedPlan =>
                {
                    if (!Dispatcher.CheckAccess())
                    {
                        Dispatcher.BeginInvoke(() => ApplyLiveUpdate(updatedPlan));
                        return;
                    }
                    ApplyLiveUpdate(updatedPlan);
                },
                Dispatcher);
            Closed += (_, _) => _liveSyncHandler?.Detach();

            _taskActivityPulseHandler = activity =>
            {
                if (!string.Equals(activity.PlanId, durablePlan.PlanId, StringComparison.Ordinal))
                    return;
                void ApplyPulse()
                {
                    if (_taskSpinnersById.TryGetValue(activity.TaskId, out var spinner))
                        spinner.Pulse(activity.Kind);
                }
                if (Dispatcher.CheckAccess()) ApplyPulse();
                else Dispatcher.BeginInvoke(ApplyPulse);
            };
            broker.Subscribe(_taskActivityPulseHandler);
            Closed += (_, _) => broker.Unsubscribe(_taskActivityPulseHandler);

            _validationActivityPulseHandler = activity =>
            {
                if (!string.Equals(activity.PlanId, durablePlan.PlanId, StringComparison.Ordinal))
                    return;
                void ApplyPulse()
                {
                    if (_validationSpinnersById.TryGetValue(activity.ValidationId, out var spinner))
                        spinner.Pulse(activity.Kind);
                }
                if (Dispatcher.CheckAccess()) ApplyPulse();
                else Dispatcher.BeginInvoke(ApplyPulse);
            };
            broker.Subscribe(_validationActivityPulseHandler);
            Closed += (_, _) => broker.Unsubscribe(_validationActivityPulseHandler);

            _recoveryAssessmentActivityHandler = activity =>
            {
                if (!string.Equals(activity.PlanId, durablePlan.PlanId, StringComparison.Ordinal))
                    return;
                void ApplyActivity()
                {
                    if (activity.IsActive)
                        _recoveryAssessmentTaskIds.Add(activity.TaskId);
                    else
                        _recoveryAssessmentTaskIds.Remove(activity.TaskId);
                    if (_plan is not null && _durablePlan is not null)
                        RebuildPreservingScroll(_plan, _durablePlan);
                }
                if (Dispatcher.CheckAccess()) ApplyActivity();
                else Dispatcher.BeginInvoke(ApplyActivity);
            };
            broker.Subscribe(_recoveryAssessmentActivityHandler);
            Closed += (_, _) => broker.Unsubscribe(_recoveryAssessmentActivityHandler);

            _taskCorrectionActivityHandler = activity =>
            {
                if (!string.Equals(activity.PlanId, durablePlan.PlanId, StringComparison.Ordinal))
                    return;
                void ApplyActivity()
                {
                    if (activity.State == PlanTaskCorrectionActivityState.None)
                        _taskCorrectionStates.Remove(activity.TaskId);
                    else
                        _taskCorrectionStates[activity.TaskId] = activity.State;
                    if (_plan is not null && _durablePlan is not null)
                        RebuildPreservingScroll(_plan, _durablePlan);
                }
                if (Dispatcher.CheckAccess()) ApplyActivity();
                else Dispatcher.BeginInvoke(ApplyActivity);
            };
            broker.Subscribe(_taskCorrectionActivityHandler);
            Closed += (_, _) => broker.Unsubscribe(_taskCorrectionActivityHandler);

            _approvalChangeActivityHandler = activity =>
            {
                if (!string.Equals(activity.PlanId, durablePlan.PlanId, StringComparison.Ordinal))
                    return;
                void ApplyActivity()
                {
                    if (activity.IsActive)
                        _activeApprovalChangeGateIds.Add(activity.GateId);
                    else
                    {
                        _activeApprovalChangeGateIds.Remove(activity.GateId);
                        _dismissedApprovalChangeGateIds.Remove(activity.GateId);
                    }
                    if (_plan is not null && _durablePlan is not null)
                        RebuildPreservingScroll(_plan, _durablePlan);
                }
                if (Dispatcher.CheckAccess()) ApplyActivity();
                else Dispatcher.BeginInvoke(ApplyActivity);
            };
            broker.Subscribe(_approvalChangeActivityHandler);
            Closed += (_, _) => broker.Unsubscribe(_approvalChangeActivityHandler);
        }
    }

    private void RestoreInspectorFontScale()
    {
        _inspectorFontScale = _isReadOnlyFixture ? 1.0 : _viewportStore.LoadInspectorFontScale();
        ApplyInspectorFontScale();
    }

    private void ApplyInspectorFontScale()
    {
        // Document zoom also scales explicitly sized headings and embedded inspector editors.
        if (_detailViewer is not null)
            _detailViewer.Zoom = _inspectorFontScale * 100.0;
    }

    private void OnInspectorPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _lastViewerInteractionUtc = DateTime.UtcNow;
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;

        e.Handled = true;
        if (e.Delta == 0)
            return;

        var scale = Math.Clamp(_inspectorFontScale * Math.Pow(1.1, e.Delta / 120.0),
            PlanVisualizerViewportStore.MinimumInspectorFontScale,
            PlanVisualizerViewportStore.MaximumInspectorFontScale);
        if (scale == _inspectorFontScale)
            return;

        _inspectorFontScale = scale;
        ApplyInspectorFontScale();
        try
        {
            if (!_isReadOnlyFixture)
                _viewportStore.SaveInspectorFontScale(_inspectorFontScale);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            SquadDashTrace.Write("PlanViewer", $"Could not save inspector font scale: {ex.Message}");
            ShowActionStatus("Inspector font size could not be saved", ex.Message);
        }
    }

    private void OnGraphScrollPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _lastViewerInteractionUtc = DateTime.UtcNow;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            if (_graphScroll is null || _graphCanvas is null || e.Delta == 0)
                return;

            var zoom = Math.Clamp(_zoomFactor * Math.Pow(1.1, e.Delta / 120.0),
                PlanVisualizerViewportStore.MinimumZoom, PlanVisualizerViewportStore.MaximumZoom);
            if (zoom == _zoomFactor)
                return;

            EndGraphPan();
            _graphScroll.UpdateLayout();
            var anchor = e.GetPosition(_graphCanvas);
            var pointer = e.GetPosition(_graphScroll);
            _zoomFactor = zoom;
            _graphCanvas.LayoutTransform = new ScaleTransform(zoom, zoom);
            _graphScroll.UpdateLayout();
            var movedAnchor = _graphCanvas.TranslatePoint(anchor, _graphScroll);
            _graphScroll.ScrollToHorizontalOffset(
                _graphScroll.HorizontalOffset + movedAnchor.X - pointer.X);
            _graphScroll.ScrollToVerticalOffset(
                _graphScroll.VerticalOffset + movedAnchor.Y - pointer.Y);
            _graphScroll.UpdateLayout();
            QueueGraphViewportSave();
            try
            {
                if (!_isReadOnlyFixture)
                    _viewportStore.SaveDefaultZoom(zoom);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                ReportViewportSaveFailure(ex);
            }
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.Shift) return;
        if (_graphScroll is null) return;
        var delta = e.Delta > 0 ? -SystemParameters.WheelScrollLines * 16.0 : SystemParameters.WheelScrollLines * 16.0;
        _graphScroll.ScrollToHorizontalOffset(_graphScroll.HorizontalOffset + delta);
        e.Handled = true;
    }

    private static bool IsGraphTextInput(IInputElement? element) =>
        element is TextBoxBase or PasswordBox or ComboBox { IsEditable: true };

    private void OnGraphPanPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || !_spacePanHeld)
            return;

        var isTextInput = IsGraphTextInput(Keyboard.FocusedElement);
        ReleaseGraphPan();
        if (!isTextInput)
            e.Handled = true;
    }

    private void OnGraphPanMouseEnter(object sender, MouseEventArgs e)
    {
        if (IsActive && Keyboard.IsKeyDown(Key.Space) &&
            !IsGraphTextInput(Keyboard.FocusedElement) && _taskCardDragState is null)
            _spacePanHeld = true;
        UpdateGraphPanCursor();
    }

    private void OnGraphPanMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_spacePanHeld || !Keyboard.IsKeyDown(Key.Space) ||
            IsGraphTextInput(Keyboard.FocusedElement) ||
            _graphScroll is null || _taskCardDragState is not null || Mouse.Captured is not null)
            return;

        _graphScroll.UpdateLayout();
        _panStart = e.GetPosition(_graphScroll);
        _panHorizontalOffset = _graphScroll.HorizontalOffset;
        _panVerticalOffset = _graphScroll.VerticalOffset;
        if (!_graphScroll.CaptureMouse())
            return;

        _isGraphPanning = true;
        UpdateGraphPanCursor();
        e.Handled = true;
    }

    private void OnGraphPanMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isGraphPanning || _graphScroll is null)
            return;
        if (e.LeftButton != MouseButtonState.Pressed || !Keyboard.IsKeyDown(Key.Space))
        {
            EndGraphPan();
            return;
        }

        var delta = e.GetPosition(_graphScroll) - _panStart;
        _graphScroll.ScrollToHorizontalOffset(_panHorizontalOffset - delta.X);
        _graphScroll.ScrollToVerticalOffset(_panVerticalOffset - delta.Y);
        _lastViewerInteractionUtc = DateTime.UtcNow;
        e.Handled = true;
    }

    private void OnGraphPanMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isGraphPanning)
            return;
        EndGraphPan();
        e.Handled = true;
    }

    private void EndGraphPan()
    {
        _isGraphPanning = false;
        if (_graphScroll?.IsMouseCaptured == true)
            _graphScroll.ReleaseMouseCapture();
        UpdateGraphPanCursor();
    }

    private void ReleaseGraphPan()
    {
        _spacePanHeld = false;
        EndGraphPan();
    }

    private void UpdateGraphPanCursor()
    {
        if (_graphScroll is null)
            return;
        var showHand = _spacePanHeld && (_graphScroll.IsMouseOver || _isGraphPanning);
        if (showHand)
        {
            _graphScroll.Cursor = _isGraphPanning ? AnnotationCursors.ClosedHand : AnnotationCursors.OpenHand;
            _graphScroll.ForceCursor = true;
        }
        else
        {
            _graphScroll.ClearValue(CursorProperty);
            _graphScroll.ClearValue(ForceCursorProperty);
        }
    }

    private void QueueGraphViewportSave()
    {
        if (!_viewportReady || _isViewerClosed || _graphScroll is null)
            return;
        _viewportState = new PlanVisualizerViewportState(
            _zoomFactor, _graphScroll.HorizontalOffset, _graphScroll.VerticalOffset);
        _viewportSaveTimer.Stop();
        _viewportSaveTimer.Start();
    }

    private void SaveGraphViewport()
    {
        _viewportSaveTimer.Stop();
        if (!_initialLayoutApplied || !_canPersistViewport)
            return;
        if (_viewportReady && _graphScroll is not null)
        {
            _graphScroll.UpdateLayout();
            _viewportState = new PlanVisualizerViewportState(
                _zoomFactor, _graphScroll.HorizontalOffset, _graphScroll.VerticalOffset);
        }
        try
        {
            _viewportStore.Save(_viewportGroupId, _viewportState);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            ReportViewportSaveFailure(ex);
        }
    }

    private void ReportViewportSaveFailure(Exception exception)
    {
        SquadDashTrace.Write("PlanViewer", $"Could not save graph navigation: {exception.Message}");
        ShowActionStatus("Graph navigation could not be saved", exception.Message);
    }

    private void RestoreGraphViewport(PlanVisualizerViewportState state)
    {
        if (_graphScroll is null)
            return;
        _graphScroll.UpdateLayout();
        _graphScroll.ScrollToHorizontalOffset(state.HorizontalOffset);
        _graphScroll.ScrollToVerticalOffset(state.VerticalOffset);
        _graphScroll.UpdateLayout();
    }

    private void EnsureZoomedAutomaticScrollTargetVisible()
    {
        // The existing policy uses scroll-space coordinates; graph positions exclude the zoom.
        var start = _activeVisibilityStart;
        var end = _activeVisibilityEnd;
        var margin = _graphCanvas?.Margin.Left ?? 0;
        try
        {
            _activeVisibilityStart = margin + (start - margin) * _zoomFactor;
            _activeVisibilityEnd = margin + (end - margin) * _zoomFactor;
            EnsureAutomaticScrollTargetVisible();
        }
        finally
        {
            _activeVisibilityStart = start;
            _activeVisibilityEnd = end;
        }
    }

    private void ApplyLiveUpdate(Plan updatedPlan)
    {
        if (_plan is null) return;
        var autoScrollForPlanProgress = HasNewActiveTask(_durablePlan, updatedPlan) ||
                                        HasJustCompleted(_durablePlan, updatedPlan);
        var displayedPlan = ApplyCommittedColumnReorder(updatedPlan);
        var updatedDefinition = PendingDecomposePlanAdapter.FromPlan(displayedPlan);
        _plan = updatedDefinition;
        _durablePlan = displayedPlan;
        _editHistory?.ReplaceCurrent(updatedDefinition.Group);
        if (!CanEditSelectedElement())
            _isInspectorEditing = false;
        RebuildPreservingScroll(updatedDefinition, displayedPlan, autoScrollForPlanProgress);
        Title = $"Plan — {displayedPlan.DisplayName}";
    }

    internal void NotifyFontSizeChanged()
    {
        const double baseFontSize = 12.0;
        var currentFontSize = Application.Current?.Resources["FontSizeBody"] is double fs ? fs : baseFontSize;
        _scaleFactor = currentFontSize / baseFontSize;
        NodeWidth = BaseNodeWidth * _scaleFactor;
        NodeHeight = BaseNodeHeight * _scaleFactor;
        ColumnSpacing = BaseColumnSpacing * _scaleFactor;
        RowSpacing = BaseRowSpacing * _scaleFactor;

        if (_plan is not null)
            RebuildPreservingScroll(_plan, _durablePlan);
    }

    internal void NotifyPlanRunnerStateChanged()
    {
        if (_plan is not null)
            RebuildPreservingScroll(_plan, _durablePlan);
    }

    internal void NotifyPendingPlanExecutionPreflightChanged()
    {
        if (_plan is not null)
            RebuildPreservingScroll(_plan, _durablePlan);
    }

    private void BuildContent(PendingDecomposePlan plan, Plan? durablePlan)
    {
        DetachDescriptionDocuments();
        _descriptionPresentation = new PlanDescriptionPresentation(
            _squadFolderPath is null ? null : System.IO.Path.GetDirectoryName(
                System.IO.Path.TrimEndingDirectorySeparator(_squadFolderPath)),
            durablePlan?.PlanId ?? plan.Group.GroupId,
            durablePlan?.Tasks ?? PendingDecomposePlanAdapter.ToPlan(plan, plan.CreatedAt ?? DateTimeOffset.MinValue).Tasks,
            Application.Current?.TryFindResource("FontSizeBody") is double bodyFontSize ? bodyFontSize : 12.0,
            _onDescriptionLinkClicked, CreateTaskLink);
        EndGraphPan();
        _viewportReady = false;
        _viewportSaveTimer.Stop();
        var currentBuildSeq = ++_buildSeq;
        _preflightPollTimer?.Stop();
        _preflightPollTimer = null;
        _plan = plan;
        _durablePlan = durablePlan;
        _borderByTask = new Dictionary<string, Border>(StringComparer.Ordinal);
        _taskOrdinalById = new Dictionary<string, string>(StringComparer.Ordinal);
        _inspectorHistory.Clear();
        _inspectorHistoryIndex = -1;
        var activeBranch       = _activeBranch;
        var quickReplyFontSize = _quickReplyFontSize;
        var applyAction        = _applyAction;
        Action<Plan>? onGatesChanged = _saveDefinitionEdit is not null
            ? edited => ApplyStagedMutation(PendingDecomposePlanAdapter.FromPlan(edited).Group)
            : _onGatesChanged;
        var onStartPlan        = _onStartPlan;
        var onResumePlan       = _onResumePlan;
        var onPausePlan        = _onPausePlan;
        var planRunnerActive   = durablePlan is not null &&
                                 (_isPlanRunnerActive?.Invoke(durablePlan) ?? true);
        var onAdoptVerifiedCommitRange = _onAdoptVerifiedCommitRange;
        var currentInterruptedActionSet = durablePlan is null
            ? null
            : _resolveInterruptedActionSet?.Invoke(durablePlan);
        var currentInterruptedSurfaceAction = currentInterruptedActionSet?.Primary;
        var currentInterruptedPrimaryAction = currentInterruptedSurfaceAction?.PrimaryKind is { } primaryKind
            ? new PlanRecoveryPrimaryActionPresentation(
                primaryKind,
                currentInterruptedSurfaceAction.Label,
                currentInterruptedSurfaceAction.Hint,
                currentInterruptedSurfaceAction.ProtocolAction)
            : null;
        var currentResumePlanAction = currentInterruptedActionSet?.Actions.FirstOrDefault(action =>
            action.Kind == PlanRecoverySurfaceActionKind.ResumePlan);
        var currentContinueAction = currentInterruptedActionSet?.Actions.FirstOrDefault(action =>
            action.Kind == PlanRecoverySurfaceActionKind.ContinueWorking);
        var currentReviseAction = currentInterruptedActionSet?.Actions.FirstOrDefault(action =>
            action.Kind == PlanRecoverySurfaceActionKind.ReviseRemainingPlan);
        var currentEndAction = currentInterruptedActionSet?.Actions.FirstOrDefault(action =>
            action.Kind == PlanRecoverySurfaceActionKind.EndPlan);
        var showOrdinaryLifecycleControls = PlanRecoveryLifecycleControlPolicy.ShouldShowOrdinaryControls(
            durablePlan,
            currentInterruptedActionSet);
        var primaryOwnsContinuation = onAdoptVerifiedCommitRange is not null &&
                                      currentInterruptedSurfaceAction?.Kind ==
                                          PlanRecoverySurfaceActionKind.ContinueWorking;
        var onEndPlan          = _onEndPlan;
        var onContinueInterruptedTask = _onContinueInterruptedTask;
        var onReplanRemainingWork = _onReplanRemainingWork;
        var onApproveGate      = _onApproveGate;
        var onUnapproveGate    = _onUnapproveGate;
        var onFixValidation    = _onFixValidation;
        var onReviewValidation = _onReviewValidation;
        var group = plan.Group;
        var currentInterruptedTaskId = durablePlan?.InterruptionData?.InterruptedTaskId;
        var completedWorkReview = durablePlan is null || string.IsNullOrWhiteSpace(currentInterruptedTaskId)
            ? null
            : CompletedWorkReviewPresentationBuilder.Build(durablePlan, currentInterruptedTaskId);
        var interruptedCorrectionState = string.IsNullOrWhiteSpace(currentInterruptedTaskId)
            ? PlanTaskCorrectionActivityState.None
            : ResolveTaskCorrectionActivity(currentInterruptedTaskId!);
        var hasClaimedInterruptedCorrection =
            interruptedCorrectionState != PlanTaskCorrectionActivityState.None;
        var reviewAttention = durablePlan is null
            ? null
            : PlanReviewAttentionPresentationResolver.Resolve(durablePlan, currentInterruptedTaskId);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(22, 16, 22, 2) };
        _header = header;

        var primaryControls = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Button AddPrimaryButton(string label, string hint, Action action, bool enabled = true)
        {
            var button = TranscriptQuickReplyFactory.CreateButton(
                label, quickReplyFontSize,
                toolTip: ToolTipHelper.MakeThemedToolTip(hint));
            button.Focusable = false;
            button.IsEnabled = enabled;
            button.Margin = new Thickness(0, 0, 6, 0);
            button.Click += (_, _) => action();
            primaryControls.Children.Add(button);
            return button;
        }

        var pauseRequested = PlanLoopTranscriptPresentation.IsActivePauseRequest(
            durablePlan,
            planRunnerActive);

        if (durablePlan is not null &&
            !PlanLifecycleStatus.IsTerminal(durablePlan.LifecycleStatus) &&
            showOrdinaryLifecycleControls)
        {
            if (durablePlan.IsPaused)
            {
                if (pauseRequested)
                {
                    if (onResumePlan is not null)
                        AddPrimaryButton(
                            PlanLoopTranscriptPresentation.KeepPlanRunningLabel,
                            "Cancel the pause request. The current task continues and the next task may start normally.",
                            () => onResumePlan(durablePlan));
                    AddPrimaryButton(
                        PlanLoopTranscriptPresentation.ViewActiveWorkLabel,
                        "Center the plan on the task that is still running.",
                        EnsureAutomaticScrollTargetVisible);
                }
                else if (durablePlan.LifecycleStatus == PlanLifecycleStatus.Executing &&
                         !planRunnerActive && onResumePlan is not null)
                {
                    AddPrimaryButton(
                        "Resume Plan",
                        "Resume this paused plan because it currently has no active runner.",
                        () => onResumePlan(durablePlan));
                }
                else if (onResumePlan is not null)
                    AddPrimaryButton("Resume", "Release this plan's durable pause. Work proceeds only when its current boundary allows it.",
                        () => onResumePlan(durablePlan));
            }
            else if (durablePlan.LifecycleStatus == PlanLifecycleStatus.Approved && onStartPlan is not null)
            {
                AddPrimaryButton("Start", "Begin executing this approved plan.",
                    () => onStartPlan(durablePlan));
            }
            else if (durablePlan.LifecycleStatus == PlanLifecycleStatus.Executing &&
                     planRunnerActive && onPausePlan is not null)
            {
                AddPrimaryButton("Pause", "Pause safely after the currently accepted step; no new step will start.",
                    () => onPausePlan(durablePlan));
            }
            else if (durablePlan.LifecycleStatus == PlanLifecycleStatus.Executing &&
                     !planRunnerActive && onResumePlan is not null)
            {
                AddPrimaryButton(
                    "Resume Plan",
                    "Resume this executing plan because it currently has no active runner.",
                    () => onResumePlan(durablePlan));
            }
            else if (PlanRecoveryControlPolicy.HasActionableInterruption(durablePlan) &&
                     !PlanRecoveryResumePolicy.IsSafelyResumable(durablePlan) &&
                     onPausePlan is not null)
            {
                var awaitingHumanReview = durablePlan.Tasks.Any(task =>
                    task.Status == PlanTaskStatus.HumanReviewRequired);
                AddPrimaryButton(awaitingHumanReview ? "Review & Pause" : "Pause",
                    "Hold this plan at its current review or recovery boundary.",
                    () => onPausePlan(durablePlan));
            }
        }

        if (_editHistory is not null && !pauseRequested)
        {
            if (primaryControls.Children.Count > 0)
            {
                var separator = new Border { Width = 1, Margin = new Thickness(2, 2, 8, 2) };
                separator.SetResourceReference(Border.BackgroundProperty, "SubtleBorder");
                primaryControls.Children.Add(separator);
            }
            var undo = AddPrimaryButton("Undo", "Undo the most recent plan edit.", UndoStagedEdit);
            undo.IsEnabled = CanApplyHistoryCandidate(_editHistory.PeekUndo, out var undoError);
            var redo = AddPrimaryButton("Redo", "Redo the most recently undone plan edit.", RedoStagedEdit);
            redo.IsEnabled = CanApplyHistoryCandidate(_editHistory.PeekRedo, out var redoError);
            if ((undoError ?? redoError) is { Length: > 0 } historyError)
            {
                var status = new TextBlock
                {
                    Text = "Undo/redo is blocked at the execution boundary. " + historyError,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 0, 0),
                };
                status.SetResourceReference(TextBlock.ForegroundProperty, "PriorityHigh");
                status.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
                primaryControls.Children.Add(status);
            }
        }
        if (primaryControls.Children.Count > 0)
            header.Children.Add(primaryControls);

        if (pauseRequested && durablePlan is not null)
        {
            var pauseStatus = new TextBlock
            {
                Text = PlanLoopTranscriptPresentation.BuildPauseRequestedMessage(durablePlan),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            };
            pauseStatus.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            pauseStatus.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            header.Children.Add(pauseStatus);
        }

        _actionStatusText = new TextBlock
        {
            Text = _actionStatusMessage,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = string.IsNullOrWhiteSpace(_actionStatusMessage)
                ? Visibility.Collapsed
                : Visibility.Visible,
        };
        _actionStatusText.SetResourceReference(TextBlock.ForegroundProperty, "PriorityHigh");
        _actionStatusText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        header.Children.Add(_actionStatusText);

        if (applyAction is not null)
        {
            var actionRegion = new StackPanel();
            var startPreflightActive = _isPendingPlanExecutionPreflightActive?.Invoke(plan) == true;
            if (startPreflightActive)
            {
                var status = new TextBlock
                {
                    Text = "Start is waiting for a clean workspace. Use the recovery card in the transcript to retry or dismiss it.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8),
                };
                status.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                status.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
                actionRegion.Children.Add(status);
            }
            else
            {
                var actionsPanel = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 8),
                };
                var recoveryHost = new ContentControl
                {
                    Visibility = Visibility.Collapsed,
                    Margin = new Thickness(0, 0, 0, 8),
                };
                foreach (var action in DecomposePlanInbox.BuildActionDefinitions(plan, activeBranch))
                {
                    var capturedAction = action;
                    var button = TranscriptQuickReplyFactory.CreateButton(
                        action.Label,
                        quickReplyFontSize,
                        toolTip: ToolTipHelper.MakeThemedToolTip(action.Hint));
                    button.Focusable = false;
                    button.Click += async (_, _) =>
                    {
                        ClearActionStatus();
                        actionsPanel.IsEnabled = false;
                        try
                        {
                            if (await applyAction(capturedAction))
                                Close();
                            else
                            {
                                actionsPanel.IsEnabled = true;
                                EnsureActionStatus("The plan action could not be completed. Review the current plan state and try again.");
                            }
                        }
                        catch (PlanPreflightBlockedException blocked)
                        {
                            ShowPlanPreflightRecovery(
                                blocked,
                                actionsPanel,
                                recoveryHost,
                                async () =>
                                {
                                    if (blocked.RequiresRepositoryInitialization &&
                                        (_initializeRepository is null || !await _initializeRepository()))
                                        return false;
                                    if (!await applyAction(capturedAction)) return false;
                                    Close();
                                    return true;
                                });
                        }
                        catch (Exception ex)
                        {
                            actionsPanel.IsEnabled = true;
                            SquadDashTrace.Write(TraceCategory.General,
                                $"Plan viewer action '{capturedAction.Action}' failed: {ex}");
                            ShowActionStatus("Plan action failed", ex.Message);
                        }
                    };
                    actionsPanel.Children.Add(button);
                }
                actionRegion.Children.Add(actionsPanel);
                actionRegion.Children.Add(recoveryHost);
            }
            header.Children.Add(actionRegion);
        }

        var failedValidation = durablePlan?.Validations?.FirstOrDefault(candidate =>
            candidate.Status == PlanValidationStatus.Failed);
        if (durablePlan is not null && failedValidation is not null &&
            (onFixValidation is not null || onReviewValidation is not null))
        {
            var validationPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8),
            };
            if (onFixValidation is not null)
            {
                var fixValidationButton = TranscriptQuickReplyFactory.CreateButton(
                    "Fix defect and rerun validation",
                    quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(
                        $"SquadDash adds one new implementation step to the existing plan using the recorded failure evidence for “{failedValidation.Title}.” The coordinator executes that step to fix the implicated code or configuration and add regression coverage; the planning agent does not redesign the plan. Completed steps and commits remain accepted, and SquadDash reruns validation automatically afterward. This does not recover a rejected plan revision."));
                fixValidationButton.Focusable = false;
                fixValidationButton.Click += (_, _) =>
                {
                    validationPanel.IsEnabled = false;
                    onFixValidation(durablePlan, failedValidation.ValidationId);
                    Close();
                };
                validationPanel.Children.Add(fixValidationButton);
            }
            if (onReviewValidation is not null)
            {
                var reviewText = new TextBlock
                {
                    Margin = new Thickness(12, 5, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var reviewLink = new Hyperlink(new Run("Review details")) { Cursor = Cursors.Hand };
                reviewLink.Click += (_, _) =>
                    onReviewValidation(durablePlan, failedValidation.ValidationId);
                reviewText.Inlines.Add(reviewLink);
                validationPanel.Children.Add(reviewText);
            }
            header.Children.Add(validationPanel);
        }

        if (durablePlan is not null && reviewAttention is not null)
        {
            var reviewChangeActive = IsApprovalChangeActive(reviewAttention.Gate);
            if (reviewChangeActive &&
                !_dismissedApprovalChangeGateIds.Contains(reviewAttention.Gate.GateId))
            {
                var workingCard = new StackPanel();
                var workingHeader = new Grid();
                workingHeader.ColumnDefinitions.Add(new ColumnDefinition
                    { Width = new GridLength(1, GridUnitType.Star) });
                workingHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var workingTitle = new TextBlock
                {
                    Text = "Addressing Review Items…",
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 3),
                };
                workingTitle.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
                workingTitle.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeLarge");
                workingHeader.Children.Add(workingTitle);

                var dismissWorkingCard = new Button
                {
                    Content = "×",
                    Width = 22,
                    Height = 22,
                    VerticalAlignment = VerticalAlignment.Top,
                    ToolTip = ToolTipHelper.MakeThemedToolTip(
                        "Hide this status panel. The correction will continue."),
                };
                dismissWorkingCard.SetResourceReference(Button.StyleProperty, "PanelCloseButtonStyle");
                dismissWorkingCard.SetResourceReference(Button.FontSizeProperty, "FontSizeNormal");
                Grid.SetColumn(dismissWorkingCard, 1);
                dismissWorkingCard.Click += (_, _) =>
                {
                    _dismissedApprovalChangeGateIds.Add(reviewAttention.Gate.GateId);
                    if (_plan is not null && _durablePlan is not null)
                        RebuildPreservingScroll(_plan, _durablePlan);
                };
                workingHeader.Children.Add(dismissWorkingCard);
                workingCard.Children.Add(workingHeader);

                var workingSummary = new TextBlock
                {
                    Text = "A correction is being prepared or applied. Review controls will return if human verification is still required afterward.",
                    TextWrapping = TextWrapping.Wrap,
                };
                workingSummary.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
                workingCard.Children.Add(workingSummary);

                var workingBorder = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 4, 0, 8),
                    Child = workingCard,
                };
                workingBorder.SetResourceReference(Border.BorderBrushProperty, "PriorityMid");
                workingBorder.SetResourceReference(Border.BackgroundProperty, "CardSurface");
                header.Children.Add(workingBorder);
            }
            else if (!reviewChangeActive)
            {
                var banner = PlanReviewBannerPresentation.Create(reviewAttention);
                var reviewCard = new Grid();
                reviewCard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                reviewCard.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var reviewText = new StackPanel
                {
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var reviewTitle = new TextBlock
                {
                    Text = banner.Title,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 2),
                };
                reviewTitle.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
                reviewTitle.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
                reviewText.Children.Add(reviewTitle);

                var reviewSummary = new TextBlock
                {
                    Text = banner.Summary,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 3),
                };
                reviewSummary.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
                reviewSummary.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
                reviewText.Children.Add(reviewSummary);

                var progressText = new TextBlock
                {
                    Text = banner.ProgressText,
                    TextWrapping = TextWrapping.Wrap,
                };
                progressText.SetResourceReference(TextBlock.ForegroundProperty,
                    banner.AllItemsChecked ? "PriorityLow" : "SubtleText");
                progressText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
                reviewText.Children.Add(progressText);

                var transcriptNote = new TextBlock
                {
                    Text = durablePlan.IsPaused
                        ? "Paused plans hide the transcript review card. Use these review controls here, or resume the plan to show the transcript card."
                        : "The transcript also has the live review card.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                };
                transcriptNote.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                transcriptNote.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
                reviewText.Children.Add(transcriptNote);
                reviewCard.Children.Add(reviewText);

                var currentChecks = reviewAttention.Checklist.ToDictionary(
                    item => item.ItemId, item => item.IsChecked, StringComparer.Ordinal);
                Button? completeReviewButton = null;
                Button? addressUncheckedButton = null;
                void RefreshReviewActions()
                {
                    if (completeReviewButton is not null)
                        completeReviewButton.IsEnabled = currentChecks.Count > 0 && currentChecks.Values.All(value => value);
                    if (addressUncheckedButton is not null)
                        addressUncheckedButton.IsEnabled = currentChecks.Values.Any(value => !value);
                }

                var reviewActions = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(reviewActions, 1);
                if (onAdoptVerifiedCommitRange is not null)
                {
                    var capturedPlan = durablePlan;
                    var primaryAction = PlanCheckpointPrimaryActionPresentationResolver.Resolve(
                        capturedPlan,
                        [reviewAttention.Gate.GateId]);
                    completeReviewButton = TranscriptQuickReplyFactory.CreateButton(
                        primaryAction.Label,
                        quickReplyFontSize,
                        toolTip: ToolTipHelper.MakeThemedToolTip(
                            $"Enabled after every review item is checked. {primaryAction.Hint}"));
                    completeReviewButton.Click += async (_, _) =>
                    {
                        ClearActionStatus();
                        reviewActions.IsEnabled = false;
                        try
                        {
                            if (await onAdoptVerifiedCommitRange(capturedPlan)) Close();
                            else
                            {
                                reviewActions.IsEnabled = true;
                                EnsureActionStatus("The review could not be completed. The plan remains at this checkpoint.");
                            }
                        }
                        catch (Exception ex)
                        {
                            reviewActions.IsEnabled = true;
                            SquadDashTrace.Write(TraceCategory.General, $"Complete plan review failed: {ex}");
                            ShowActionStatus("Review could not be completed", ex.Message);
                        }
                    };
                    reviewActions.Children.Add(completeReviewButton);
                }
                if (_onAddressUncheckedItems is not null)
                {
                    addressUncheckedButton = TranscriptQuickReplyFactory.CreateButton(
                        "✎ Address Unchecked Items…",
                        quickReplyFontSize,
                        toolTip: ToolTipHelper.MakeThemedToolTip(
                            "Create an editable prompt containing the unchecked review items."));
                    addressUncheckedButton.Click += (_, _) =>
                    {
                        var currentItems = reviewAttention.Checklist.Select(item => item with
                        {
                            IsChecked = currentChecks[item.ItemId],
                        }).ToArray();
                        _onAddressUncheckedItems(durablePlan, reviewAttention.Task.TaskId, currentItems);
                    };
                    reviewActions.Children.Add(addressUncheckedButton);
                }
                RefreshReviewActions();
                if (reviewActions.Children.Count > 0)
                    reviewCard.Children.Add(reviewActions);

                var reviewBorder = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 7, 10, 7),
                    Margin = new Thickness(0, 4, 0, 6),
                    Child = reviewCard,
                };
                reviewBorder.SetResourceReference(Border.BorderBrushProperty, "PriorityMid");
                reviewBorder.SetResourceReference(Border.BackgroundProperty, "CardSurface");
                header.Children.Add(reviewBorder);
            }
        }

        if (durablePlan is not null &&
            durablePlan.LifecycleStatus == PlanLifecycleStatus.Interrupted &&
            durablePlan.InterruptionData?.RecoveryAssessment is not null &&
            !hasClaimedInterruptedCorrection && reviewAttention is null)
        {
            var assessmentPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8),
            };
            var interruptedTaskId = durablePlan.InterruptionData.InterruptedTaskId;
            var interruptedTask = durablePlan.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, interruptedTaskId, StringComparison.Ordinal));
            var assessmentActive = !string.IsNullOrWhiteSpace(interruptedTaskId) &&
                (_recoveryAssessmentTaskIds.Contains(interruptedTaskId) ||
                 (_isRecoveryAssessmentActive?.Invoke(interruptedTaskId) ?? false));
            assessmentPanel.IsEnabled = !assessmentActive;
            var stepLabel = PlanRecoveryPresentationBuilder.FormatStepLabel(
                interruptedTask?.DisplayStepLabel,
                interruptedTask?.Title ?? interruptedTaskId ?? "Interrupted Step");

            if (onAdoptVerifiedCommitRange is not null && interruptedTask is not null)
            {
                var acceptButton = TranscriptQuickReplyFactory.CreateButton(
                    currentInterruptedPrimaryAction?.Label ??
                    _interruptedPrimaryActionLabel ?? $"{stepLabel} Is Complete", quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(
                        currentInterruptedPrimaryAction?.Hint ??
                        _interruptedPrimaryActionHint ??
                        $"Use the evidence links in this viewer, then mark only {stepLabel} complete and continue the plan."));
                acceptButton.Focusable = false;
                acceptButton.Click += async (_, _) =>
                {
                    ClearActionStatus();
                    assessmentPanel.IsEnabled = false;
                    try
                    {
                        if (await onAdoptVerifiedCommitRange(durablePlan)) Close();
                        else
                        {
                            assessmentPanel.IsEnabled = true;
                            EnsureActionStatus("The completed step could not be accepted. The plan remains stopped for review.");
                        }
                    }
                    catch (Exception ex)
                    {
                        assessmentPanel.IsEnabled = true;
                        SquadDashTrace.Write(TraceCategory.General, $"Accept completed plan step failed: {ex}");
                        ShowActionStatus("Completed step could not be accepted", ex.Message);
                    }
                };
                assessmentPanel.Children.Add(acceptButton);
            }
            if (!primaryOwnsContinuation && onContinueInterruptedTask is not null && interruptedTask is not null &&
                currentContinueAction is not null)
            {
                var continueButton = TranscriptQuickReplyFactory.CreateButton(
                    currentContinueAction.Label, quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(
                        currentContinueAction.Hint),
                    tone: currentContinueAction.Tone);
                continueButton.Focusable = false;
                continueButton.Click += (_, _) => { onContinueInterruptedTask(durablePlan); Close(); };
                assessmentPanel.Children.Add(continueButton);
            }
            if (onReplanRemainingWork is not null && currentReviseAction is not null)
            {
                var replanButton = TranscriptQuickReplyFactory.CreateButton(
                    currentReviseAction.Label, quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(
                        currentReviseAction.Hint),
                    tone: currentReviseAction.Tone);
                replanButton.Focusable = false;
                replanButton.Click += (_, _) => { onReplanRemainingWork(durablePlan); Close(); };
                assessmentPanel.Children.Add(replanButton);
            }
            if (onEndPlan is not null && currentEndAction is not null &&
                PlanRecoveryControlPolicy.HasActionableInterruption(durablePlan))
            {
                var endButton = TranscriptQuickReplyFactory.CreateButton(
                    currentEndAction.Label,
                    quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(currentEndAction.Hint),
                    tone: currentEndAction.Tone);
                endButton.Focusable = false;
                endButton.Click += (_, _) =>
                {
                    onEndPlan(durablePlan);
                    Close();
                };
                assessmentPanel.Children.Add(endButton);
            }
            header.Children.Add(assessmentPanel);
        }

        if (durablePlan is not null &&
            durablePlan.InterruptionData?.RecoveryAssessment is null &&
            durablePlan.LifecycleStatus is PlanLifecycleStatus.Interrupted or
                PlanLifecycleStatus.WaitingForCleanWorktree &&
            (onResumePlan is not null || onAdoptVerifiedCommitRange is not null ||
             onContinueInterruptedTask is not null || onEndPlan is not null) &&
            !hasClaimedInterruptedCorrection && reviewAttention is null)
        {
            var interruptedPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 0, 0, 8),
            };
            var safeResume = PlanRecoveryResumePolicy.IsSafelyResumable(durablePlan);
            var interruptedActionTask = durablePlan.Tasks.FirstOrDefault(task => string.Equals(
                task.TaskId,
                durablePlan.InterruptionData?.InterruptedTaskId,
                StringComparison.Ordinal));
            var assessmentActive = interruptedActionTask is not null &&
                (_recoveryAssessmentTaskIds.Contains(interruptedActionTask.TaskId) ||
                 (_isRecoveryAssessmentActive?.Invoke(interruptedActionTask.TaskId) ?? false));
            if (onAdoptVerifiedCommitRange is not null && !safeResume &&
                _onOpenInterruptedChanges is not null)
            {
                var interruptedTask = durablePlan.Tasks.FirstOrDefault(task => string.Equals(
                    task.TaskId,
                    durablePlan.InterruptionData?.InterruptedTaskId,
                    StringComparison.Ordinal));
                if (interruptedTask is not null)
                {
                    var evidence = PlanRecoveryPresentationBuilder.ResolveTaskEvidence(
                        durablePlan,
                        interruptedTask);
                    var interruptedReview = CompletedWorkReviewPresentationBuilder.Build(
                        durablePlan,
                        interruptedTask.TaskId);
                    var baselineCommit = durablePlan.InterruptionData?.TaskCommitEvidence?.BaselineCommit;
                    var comparisonLabel = interruptedReview?.ComparisonLabel ??
                        (evidence.Count > 0
                            ? PlanRecoveryPresentationBuilder.BuildCommittedReviewLabel(
                                PlanRecoveryPresentationBuilder.FormatStepLabel(
                                    interruptedTask.DisplayStepLabel,
                                    interruptedTask.Title ?? interruptedTask.TaskId),
                                baselineCommit ?? durablePlan.InterruptionData?.LastCommit,
                                evidence[^1].Commit)
                            : PlanRecoveryPresentationBuilder.BuildUnattributedReviewLabel(
                                PlanRecoveryPresentationBuilder.FormatStepLabel(
                                    interruptedTask.DisplayStepLabel,
                                    interruptedTask.Title ?? interruptedTask.TaskId),
                                baselineCommit));
                    var comparisonHint = interruptedReview?.ComparisonHint ??
                        (evidence.Count > 0
                            ? PlanRecoveryPresentationBuilder.CommittedReviewHint(
                                PlanRecoveryPresentationBuilder.FormatStepLabel(
                                    interruptedTask.DisplayStepLabel,
                                    interruptedTask.Title ?? interruptedTask.TaskId),
                                baselineCommit ?? durablePlan.InterruptionData?.LastCommit,
                                evidence[^1].Commit)
                            : PlanRecoveryPresentationBuilder.UnattributedReviewHint(
                                PlanRecoveryPresentationBuilder.FormatStepLabel(
                                    interruptedTask.DisplayStepLabel,
                                    interruptedTask.Title ?? interruptedTask.TaskId),
                                baselineCommit));
                    var linkText = new TextBlock
                    {
                        Margin = new Thickness(0, 5, 14, 12),
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = quickReplyFontSize,
                    };
                    var reviewLink = new Hyperlink(new Run(comparisonLabel))
                    {
                        Cursor = Cursors.Hand,
                        ToolTip = ToolTipHelper.MakeThemedToolTip(comparisonHint),
                    };
                    reviewLink.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
                    reviewLink.Click += (_, _) => _onOpenInterruptedChanges(durablePlan);
                    linkText.Inlines.Add(reviewLink);
                    header.Children.Add(linkText);
                }
            }
            // Safe continuation owns the recovery choice. A ready validation is simply the next
            // scheduler boundary and must not create a duplicate "Resume Validation" action.
            if (onAdoptVerifiedCommitRange is not null && !safeResume)
            {
                var capturedPlan = durablePlan;
                var capturedAction = onAdoptVerifiedCommitRange;
                var resumableValidation = PlanExecutionBoundaryPolicy.SelectValidation(capturedPlan);
                var adoptButton = TranscriptQuickReplyFactory.CreateButton(
                    resumableValidation is null
                        ? currentInterruptedPrimaryAction?.Label ??
                          _interruptedPrimaryActionLabel ?? "Continue Plan Recovery"
                        : "Resume Validation",
                    quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(
                        resumableValidation is null
                            ? currentInterruptedPrimaryAction?.Hint ??
                              _interruptedPrimaryActionHint ??
                              "Continue from the preserved plan state without launching a preliminary repository assessment."
                            : $"Continue with the ready validation “{resumableValidation.Title}.” Completed implementation steps will not be repeated."));
                adoptButton.Focusable = false;
                adoptButton.Click += async (_, _) =>
                {
                    ClearActionStatus();
                    interruptedPanel.IsEnabled = false;
                    try
                    {
                        if (await capturedAction(capturedPlan))
                            Close();
                        else
                        {
                            interruptedPanel.IsEnabled = true;
                            EnsureActionStatus("Plan recovery could not continue. The current recovery state is unchanged.");
                        }
                    }
                    catch (Exception ex)
                    {
                        interruptedPanel.IsEnabled = true;
                        SquadDashTrace.Write(TraceCategory.General,
                            $"Plan recovery assessment failed: {ex}");
                        ShowActionStatus("Plan recovery could not continue", ex.Message);
                    }
                };
                interruptedPanel.Children.Add(adoptButton);
            }
            if (!primaryOwnsContinuation &&
                onContinueInterruptedTask is not null && !safeResume && interruptedActionTask is not null &&
                currentContinueAction is not null)
            {
                var capturedPlan = durablePlan;
                var continueButton = TranscriptQuickReplyFactory.CreateButton(
                    currentContinueAction.Label,
                    quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(
                        currentContinueAction.Hint),
                    tone: currentContinueAction.Tone);
                continueButton.Focusable = false;
                continueButton.Click += (_, _) =>
                {
                    onContinueInterruptedTask(capturedPlan);
                    Close();
                };
                interruptedPanel.Children.Add(continueButton);
            }
            if (onReplanRemainingWork is not null && !safeResume && currentReviseAction is not null)
            {
                var capturedPlan = durablePlan;
                var reviseButton = TranscriptQuickReplyFactory.CreateButton(
                    currentReviseAction.Label,
                    quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(currentReviseAction.Hint),
                    tone: currentReviseAction.Tone);
                reviseButton.Focusable = false;
                reviseButton.Click += (_, _) =>
                {
                    onReplanRemainingWork(capturedPlan);
                    Close();
                };
                interruptedPanel.Children.Add(reviseButton);
            }
            if (onResumePlan is not null && safeResume && currentResumePlanAction is not null)
            {
                var capturedPlan   = durablePlan;
                var capturedAction = onResumePlan;
                var resumeButton   = TranscriptQuickReplyFactory.CreateButton(
                    currentResumePlanAction.Label,
                    quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(
                        currentResumePlanAction.Hint),
                    tone: currentResumePlanAction.Tone);
                resumeButton.Focusable = false;
                resumeButton.Click += (_, _) =>
                {
                    capturedAction(capturedPlan);
                    Close();
                };
                interruptedPanel.Children.Add(resumeButton);
            }
            if (onEndPlan is not null && currentEndAction is not null &&
                PlanRecoveryControlPolicy.HasActionableInterruption(durablePlan))
            {
                var capturedPlan   = durablePlan;
                var capturedAction = onEndPlan;
                var endButton      = TranscriptQuickReplyFactory.CreateButton(
                    currentEndAction.Label,
                    quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(currentEndAction.Hint),
                    tone: currentEndAction.Tone);
                endButton.Focusable = false;
                endButton.Click += (_, _) =>
                {
                    capturedAction(capturedPlan);
                    Close();
                };
                interruptedPanel.Children.Add(endButton);
            }
            interruptedPanel.IsEnabled = !assessmentActive;
            header.Children.Add(interruptedPanel);
        }

        if (durablePlan is not null && hasClaimedInterruptedCorrection)
        {
            var correctedTask = durablePlan.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, currentInterruptedTaskId, StringComparison.Ordinal));
            var correctedStep = PlanRecoveryPresentationBuilder.FormatStepLabel(
                correctedTask?.DisplayStepLabel,
                correctedTask?.Title ?? currentInterruptedTaskId ?? "Interrupted Step");
            var correctionStatus = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8),
            };
            if (interruptedCorrectionState == PlanTaskCorrectionActivityState.Running)
            {
                var spinner = new ActivitySpinner
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                    AccentColor = ResolvePlanActivityColor("PriorityMid", Colors.DarkOrange),
                    ToolTip = ToolTipHelper.MakeThemedToolTip(
                        $"{correctedStep} is receiving the bounded correction requested from its recovery card."),
                };
                spinner.SetResourceReference(ActivitySpinner.FontSizeProperty, "FontSizeSmall");
                spinner.SetContinuousActive(true);
                correctionStatus.Children.Add(spinner);
            }
            var statusText = new TextBlock
            {
                Text = interruptedCorrectionState == PlanTaskCorrectionActivityState.Running
                    ? $"Correcting {correctedStep}…"
                    : $"Correction prompt prepared for {correctedStep}.",
                VerticalAlignment = VerticalAlignment.Center,
            };
            statusText.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            statusText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
            correctionStatus.Children.Add(statusText);
            header.Children.Add(correctionStatus);
        }

        var hintBlock = new TextBlock
        {
            Text         = "Arrows point from prerequisite → dependent.  ALL means every incoming step must finish.  Steps in the same stage with no arrow between them are independent and may run in any order.  Hold Space + drag to pan.  Ctrl + wheel to zoom.  Shift + wheel to scroll horizontally.",
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(22, 6, 22, 8),
        };
        hintBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        hintBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        _readingHint = hintBlock;

        if (durablePlan?.InterruptionData is { } interruptionData && reviewAttention is null)
        {
            var interruptionStack = new StackPanel();
            var attentionTaskId = interruptionData.InterruptedTaskId ?? string.Empty;
            var humanReview = PlanRecoveryPresentationBuilder.BuildHumanReviewCard(
                durablePlan,
                attentionTaskId);
            var attentionCard = PlanAttentionCardPresentationBuilder.Build(
                durablePlan,
                attentionTaskId,
                humanReview,
                hasHumanChecklist: false);

            var intRow1 = new TextBlock
            {
                Text       = attentionCard.Title,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
            };
            intRow1.SetResourceReference(TextBlock.ForegroundProperty, "PriorityHigh");
            intRow1.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            interruptionStack.Children.Add(intRow1);

            var explanationRow = new TextBlock
            {
                Text = attentionCard.Explanation,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
            };
            explanationRow.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            explanationRow.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            interruptionStack.Children.Add(explanationRow);

            var reasonRow = new TextBlock
            {
                Text = "What happened: " + attentionCard.RecordedReason,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 5),
            };
            reasonRow.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            reasonRow.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            interruptionStack.Children.Add(reasonRow);

            if (completedWorkReview is not null)
            {
                foreach (var text in new[]
                {
                    completedWorkReview.EvidenceSummary,
                })
                {
                    var row = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
                    row.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                    row.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
                    interruptionStack.Children.Add(row);
                }

                var technicalText = new TextBlock
                {
                    Text = completedWorkReview.TechnicalDetails,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Consolas"),
                };
                technicalText.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                technicalText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
                var technical = new Expander
                {
                    Header = "Technical details",
                    Content = technicalText,
                    IsExpanded = false,
                    Margin = new Thickness(0, 3, 0, 0),
                };
                technical.SetResourceReference(Expander.ForegroundProperty, "SubtleText");
                interruptionStack.Children.Add(technical);
            }
            else if (interruptionData.InterruptedTaskId is { } interruptedTaskId)
            {
                var intRow2 = new TextBlock { Text = $"Last step: {interruptedTaskId}" };
                intRow2.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                intRow2.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
                interruptionStack.Children.Add(intRow2);
            }
            if (completedWorkReview is null && interruptionData.LastCommit is { } lastCommit)
            {
                var shortLastCommit = lastCommit.Length >= 7 ? lastCommit[..7] : lastCommit;
                var intRow3 = new TextBlock
                {
                    Text       = $"Last commit: {shortLastCommit}",
                    FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                };
                intRow3.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                intRow3.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
                interruptionStack.Children.Add(intRow3);
            }
            if (completedWorkReview is null && interruptionData.PartialWorkEvidence is { } evidence && evidence.Length > 0)
            {
                var excerpt = evidence.Length > 100 ? evidence[..100] + "…" : evidence;
                var intRow4 = new TextBlock { Text = excerpt, TextWrapping = TextWrapping.Wrap };
                intRow4.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                intRow4.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
                interruptionStack.Children.Add(intRow4);
            }

            var interruptionBorder = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(4),
                Margin          = new Thickness(0, 4, 0, 4),
                Padding         = new Thickness(8, 5, 8, 5),
                Child           = interruptionStack,
            };
            interruptionBorder.SetResourceReference(Border.BorderBrushProperty, "PriorityHigh");
            interruptionBorder.SetResourceReference(Border.BackgroundProperty,  "CardSurface");
            header.Children.Add(interruptionBorder);
        }

        if (durablePlan?.LifecycleStatus == PlanLifecycleStatus.AwaitingApproval &&
            onApproveGate is not null && reviewAttention is null)
        {
            var awaitingGate = durablePlan.ApprovalGates.FirstOrDefault(g =>
                g.Status == PlanGateStatus.AwaitingApproval);
            if (awaitingGate is not null)
            {
                var capturedApprPlan = durablePlan;
                var capturedApprGate = awaitingGate;
                var capturedApprove  = onApproveGate;
                var primaryAction = PlanCheckpointPrimaryActionPresentationResolver.Resolve(
                    capturedApprPlan,
                    capturedApprPlan.ApprovalGates
                        .Where(candidate => candidate.Status == PlanGateStatus.AwaitingApproval)
                        .Select(candidate => candidate.GateId)
                        .ToArray());
                var approvePanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                var gateMsg = new TextBlock
                {
                    Text              = $"⏸ Waiting for approval: {capturedApprGate.Message}",
                    TextWrapping      = TextWrapping.Wrap,
                    Margin            = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                gateMsg.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                gateMsg.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
                var approveButton = TranscriptQuickReplyFactory.CreateButton(
                    primaryAction.Label,
                    quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(primaryAction.Hint));
                approveButton.Focusable = false;
                approveButton.Click += (_, _) =>
                {
                    capturedApprove(capturedApprPlan, capturedApprGate.GateId);
                    Close();
                };
                approvePanel.Children.Add(gateMsg);
                approvePanel.Children.Add(approveButton);
                header.Children.Add(approvePanel);
            }
        }

        root.Children.Add(header);

        var canvas = new Canvas
        {
            Margin              = new Thickness(18, 4, 18, 18),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment   = VerticalAlignment.Top,
            LayoutTransform     = new ScaleTransform(_zoomFactor, _zoomFactor),
        };
        _graphCanvas = canvas;
        _agentChipElements.Clear();
        canvas.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource == canvas)
            {
                _selectedElement = null;
                ClearSelectionVisual();
                RefreshDetailPanel();
            }
        };
        canvas.MouseMove += OnGraphCanvasMouseMove;
        canvas.MouseLeftButtonUp += OnGraphCanvasMouseLeftButtonUp;
        if (SquadDashEnvironment.IsDeveloperMode &&
            _saveDefinitionEdit is not null &&
            durablePlan is not null &&
            durablePlan.LifecycleStatus != PlanLifecycleStatus.Staged)
        {
            var canvasMenu = CreateThemedMenu();
            AppendDeveloperNormalizeMenuItem(canvasMenu);
            canvas.ContextMenu = canvasMenu;
        }
        var scroll = new ScrollViewer
        {
            Content = canvas,
            Margin  = new Thickness(22, 0, 6, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
        };
        _graphScroll = scroll;
        scroll.SetResourceReference(ScrollViewer.StyleProperty,      "RosterScrollViewerStyle");
        scroll.SetResourceReference(ScrollViewer.BackgroundProperty, "AppSurface");
        canvas.SetResourceReference(Canvas.BackgroundProperty, "PlanStageCanvasBackground");
        scroll.PreviewMouseWheel += OnGraphScrollPreviewMouseWheel;
        scroll.PreviewMouseLeftButtonDown += OnGraphPanMouseDown;
        scroll.PreviewMouseMove += OnGraphPanMouseMove;
        scroll.PreviewMouseLeftButtonUp += OnGraphPanMouseUp;
        scroll.MouseEnter += OnGraphPanMouseEnter;
        scroll.MouseLeave += (_, _) => UpdateGraphPanCursor();
        scroll.LostMouseCapture += (_, _) =>
        {
            _isGraphPanning = false;
            UpdateGraphPanCursor();
        };
        scroll.ScrollChanged += (_, _) =>
        {
            if (ReferenceEquals(_graphScroll, scroll))
                QueueGraphViewportSave();
        };
        UpdateGraphPanCursor();

        var splitGrid = new Grid();
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7, GridUnitType.Star) });
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Pixel) });
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(3, GridUnitType.Star),
            MinWidth = 200,
        });

        Grid.SetColumn(scroll, 0);
        splitGrid.Children.Add(scroll);

        var splitter = new GridSplitter
        {
            Width = 5,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        splitter.SetResourceReference(GridSplitter.BackgroundProperty, "SubtleBorder");
        Grid.SetColumn(splitter, 1);
        splitGrid.Children.Add(splitter);

        var detailDocument = new FlowDocument
        {
            FontFamily    = new FontFamily("Segoe UI, Segoe UI Emoji"),
            TextAlignment = TextAlignment.Left,
        };
        detailDocument.SetResourceReference(FlowDocument.FontSizeProperty, "FontSizeMedium");
        var placeholder = new Paragraph(new Run("Click an element to view details"));
        placeholder.SetResourceReference(Paragraph.ForegroundProperty, "SubtleText");
        detailDocument.Blocks.Add(placeholder);

        var detailViewer = new FlowDocumentScrollViewer
        {
            Document = detailDocument,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsToolBarVisible = false,
            MinZoom = PlanVisualizerViewportStore.MinimumInspectorFontScale * 100.0,
            MaxZoom = PlanVisualizerViewportStore.MaximumInspectorFontScale * 100.0,
        };
        _detailViewer = detailViewer;
        RestoreInspectorFontScale();
        detailViewer.PreviewMouseWheel += OnInspectorPreviewMouseWheel;
        detailViewer.MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.XButton1)
                return;

            NavigateBack();
            e.Handled = true;
        };
        detailViewer.SetResourceReference(FlowDocumentScrollViewer.BackgroundProperty, "CardSurface");
        detailViewer.SetResourceReference(FlowDocumentScrollViewer.ForegroundProperty, "BodyText");

        var inspectorGrid = new Grid();
        inspectorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inspectorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inspectorGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbarPanel = new WrapPanel
        {
            Margin = new Thickness(6, 4, 6, 4),
        };
        toolbarPanel.SetResourceReference(WrapPanel.BackgroundProperty, "CardSurface");
        Grid.SetRow(toolbarPanel, 0);
        inspectorGrid.Children.Add(toolbarPanel);

        _descriptionFindInput = new TextBox
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(6, 0, 6, 4),
            ToolTip = "Find visible text (Enter for next match, Escape to close)",
        };
        _descriptionFindInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _descriptionFindInput.ToolTip = _descriptionSession?.FindNext(_descriptionFindInput.Text) == true
                    ? "Enter for next visible match." : "No visible matches.";
                _descriptionFindInput.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _descriptionFindInput.Visibility = Visibility.Collapsed;
                detailViewer.Focus();
                e.Handled = true;
            }
        };
        Grid.SetRow(_descriptionFindInput, 1);
        inspectorGrid.Children.Add(_descriptionFindInput);
        Grid.SetRow(detailViewer, 2);
        inspectorGrid.Children.Add(detailViewer);

        inspectorGrid.Margin = new Thickness(0, 0, 4, 0);
        Grid.SetColumn(inspectorGrid, 2);
        splitGrid.Children.Add(inspectorGrid);

        _detailDocument = detailDocument;
        _inspectorToolbarPanel = toolbarPanel;

        Grid.SetRow(splitGrid, 1);
        root.Children.Add(splitGrid);

        // Reading guidance belongs with the graph it explains, not in the plan's proposal
        // header. Keep it visible immediately below the graph and above approval details.
        Grid.SetRow(hintBlock, 2);
        root.Children.Add(hintBlock);

        _contentHolder ??= ApplyOuterBorder(titleText: group.GroupTitle);
        if (TitleLabel is { } planTitle)
            planTitle.ContextMenu = CreatePlanTitleContextMenu(plan, durablePlan);
        _contentHolder.Child = root;

        var tasksById = group.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        _taskSpinnersById.Clear();
        _validationSpinnersById.Clear();
        _taskDragOverlaysByTask.Clear();
        var displayedValidations = group.Validations ?? [];
        // Validation boundaries are graph constraints even though they do not mutate task
        // dependencies. Use them only for display-level calculation so the visual sequence
        // matches execution without rewriting the approved plan.
        var levels = ValidationShieldPresenter.ComputeValidationAwareLevels(
            group.Tasks,
            displayedValidations);
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        var columns = group.Tasks.GroupBy(task => levels[task.Id]).OrderBy(column => column.Key).ToArray();
        static bool SameIds(IEnumerable<string> left, IEnumerable<string> right) =>
            left.OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(right.OrderBy(id => id, StringComparer.Ordinal));

        var validationAnchors = new Dictionary<string, (string Kind, string? TaskId, int StageIndex, string? AllKey)>(StringComparer.Ordinal);
        foreach (var validation in displayedValidations)
        {
            var stageIndex = -1;
            for (var index = 0; index < columns.Length - 1; index++)
            {
                var immediateAfter = columns[index].Select(task => task.Id);
                var immediateBefore = columns[index + 1].Select(task => task.Id);
                var cumulativeAfter = group.Tasks.Where(task => levels[task.Id] <= columns[index].Key).Select(task => task.Id);
                var cumulativeBefore = group.Tasks.Where(task => levels[task.Id] > columns[index].Key).Select(task => task.Id);
                if ((SameIds(validation.AfterTaskIds, immediateAfter) && SameIds(validation.BeforeTaskIds, immediateBefore)) ||
                    (SameIds(validation.AfterTaskIds, cumulativeAfter) && SameIds(validation.BeforeTaskIds, cumulativeBefore)))
                {
                    stageIndex = index;
                    break;
                }
            }

            if (stageIndex >= 0)
            {
                validationAnchors[validation.ValidationId] = ("stage", null, stageIndex, null);
                continue;
            }

            var allTargets = group.Tasks
                .Where(task => task.DependsOn.Count > 1 && SameIds(task.DependsOn, validation.AfterTaskIds))
                .Select(task => task.Id)
                .ToArray();
            if (allTargets.Length > 0 && SameIds(allTargets, validation.BeforeTaskIds))
            {
                validationAnchors[validation.ValidationId] =
                    ("all", null, -1, string.Join("\u001f", validation.AfterTaskIds.OrderBy(id => id, StringComparer.Ordinal)));
                continue;
            }

            var taskAnchor = ValidationShieldPresenter.InferTaskAnchor(validation, group.Tasks);
            if (taskAnchor is not null)
            {
                validationAnchors[validation.ValidationId] = (
                    taskAnchor.Kind == ValidationShieldPresenter.AnchorKind.After ? "after" : "before",
                    taskAnchor.TaskId,
                    -1,
                    null);
                continue;
            }

            // A validation may span a proper subset of several stages and therefore not be
            // equivalent to a whole stage, task, or ALL boundary. Keep its semantic rail
            // anchor, but place rail validations over the latest prerequisite milestone—the
            // point where all of their required work can actually be complete—instead of
            // scattering them horizontally.
            var afterLevels = validation.AfterTaskIds
                .Where(levels.ContainsKey)
                .Select(id => levels[id])
                .ToArray();
            var beforeLevels = validation.BeforeTaskIds
                .Where(levels.ContainsKey)
                .Select(id => levels[id])
                .ToArray();
            var inferredStageIndex = ValidationShieldPresenter.InferComplexValidationStageIndex(
                afterLevels,
                beforeLevels,
                columns.Length);
            validationAnchors[validation.ValidationId] = ("rail", null, inferredStageIndex, null);
        }

        ValidationShieldPresenter.ShieldAnchor PresenterAnchor(
            (string Kind, string? TaskId, int StageIndex, string? AllKey) anchor) =>
            new(anchor.Kind switch
            {
                "stage" => ValidationShieldPresenter.AnchorKind.Stage,
                "all" => ValidationShieldPresenter.AnchorKind.All,
                "before" => ValidationShieldPresenter.AnchorKind.Before,
                "after" => ValidationShieldPresenter.AnchorKind.After,
                _ => ValidationShieldPresenter.AnchorKind.Rail,
            }, anchor.TaskId, anchor.StageIndex, anchor.AllKey);

        var measuredRailVisualHeight = displayedValidations.Count == 0
            ? ValidationShieldPresenter.BaseShieldVisualHeight * _scaleFactor
            : displayedValidations.Max(validation => MeasureValidationRailVisualHeight(validation.Title));
        var validationRailHeight = ValidationShieldPresenter.ComputeValidationRailHeight(
            validationAnchors.Values.Select(PresenterAnchor).ToArray(),
            _scaleFactor,
            measuredRailVisualHeight);
        const double baseOctagonSize = 16;
        var stageHeaderReserve = 36 * _scaleFactor;
        // Keep the whole graph low enough that the canvas does not clip the upper half of
        // the milestone octagon when there is no validation rail. The previous 16px reserve
        // put the control too close to the canvas edge, so add half an octagon of clearance.
        const double octagonHeadroom = baseOctagonSize * 1.5;
        var graphTop = Math.Max(stageHeaderReserve + octagonHeadroom * _scaleFactor,
                                12 * _scaleFactor + validationRailHeight);
        var validationRailRight = 0.0;
        var _deferredShieldHovers = new List<(StackPanel Row, IReadOnlyList<string> AfterTaskIds, IReadOnlyList<string> BeforeTaskIds)>();
        var approvalControlsByAnchor = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);

        static string StageAnchor(int leftStage) => $"stage:{leftStage}";
        static string AllAnchor(IEnumerable<string> targetIds) =>
            "all:" + string.Join("|", targetIds.OrderBy(id => id, StringComparer.Ordinal));
        static string TaskBeforeAnchor(string taskId) => $"task-before:{taskId}";
        static string TaskAfterAnchor(string taskId) => $"task-after:{taskId}";
        static string ApprovalLabel(string anchor) =>
            anchor.StartsWith("all:", StringComparison.Ordinal) ? "ALL" :
            anchor.StartsWith("stage:", StringComparison.Ordinal) ? "stage" : "task";

        bool IsPrimary(PlanApprovalGate? gate, string anchor) => gate is not null &&
            string.Equals(ResolvePresentationAnchor(gate), anchor, StringComparison.Ordinal);

        bool IsApprovalChangeActive(PlanApprovalGate? gate) => gate is not null &&
            ((_isApprovalChangeActivityActive?.Invoke(gate.GateId) ?? false) ||
             _activeApprovalChangeGateIds.Contains(gate.GateId) ||
             (durablePlan?.LifecycleStatus == PlanLifecycleStatus.Executing &&
              gate.Status == PlanGateStatus.Pending &&
              gate.ReworkCount > 0 &&
              gate.LastReworkRequestedAt.HasValue));

        static bool IsUnresolvedApproval(PlanApprovalGate? gate) => gate?.Status is
            PlanGateStatus.Pending or PlanGateStatus.AwaitingApproval;

        void ShowCoveredGuidance(string controllingAnchor, string label)
        {
            if (!approvalControlsByAnchor.TryGetValue(controllingAnchor, out var target)) return;
            var glow = new DropShadowEffect { Color = Color.FromRgb(0xC9, 0x4B, 0x4B), BlurRadius = 16 };
            target.Effect = glow;
            var pulse = new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(250))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3),
            };
            pulse.Completed += (_, _) => target.Effect = null;
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, pulse);

            var theme = AgentStatusCard.IsDarkTheme ? CalloutTheme.Dark : CalloutTheme.Light;
            var angle = controllingAnchor.StartsWith("stage:", StringComparison.Ordinal)
                ? FrmUltimateCallout.PlacementToAngle(CalloutPlacement.North)
                : double.MinValue;
            FrmUltimateCallout.ShowCallout(
                $"Covered by this {label} approval requirement. Click to review it, or right-click the primary control to **clear** it.",
                target, width: 360, angle: angle, theme: theme, fontSize: 14);
        }

        PlanApprovalGate? FindDurableGate(
            IReadOnlyList<string> afterIds,
            IReadOnlyList<string> beforeIds)
        {
            if (durablePlan is null) return null;
            return PlanGateManager.FindEquivalentGate(durablePlan, afterIds, beforeIds) ??
                   durablePlan.ApprovalGates.FirstOrDefault(gate =>
                       PlanGateVisualizationPolicy.GraphEquivalent(
                           durablePlan.Tasks,
                           gate.AfterTaskIds, gate.BeforeTaskIds,
                           afterIds, beforeIds));
        }

        string[] DirectDependents(string taskId) => durablePlan?.Tasks
            .Where(task => task.DependsOn.Contains(taskId, StringComparer.Ordinal))
            .Select(task => task.TaskId)
            .ToArray() ?? [];

        PlanApprovalGate? FindTaskGateAfter(string taskId) =>
            FindDurableGate([taskId], DirectDependents(taskId));

        PlanApprovalGate? FindTaskGateBefore(string taskId)
        {
            var task = durablePlan?.Tasks.FirstOrDefault(candidate =>
                string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal));
            return task is null ? null : FindDurableGate(task.DependsOn, [taskId]);
        }

        _naturalTaskSlotTops.Clear();
        foreach (var column in columns)
        {
            var tasks = column.ToArray();
            var x = 42 * _scaleFactor + column.Key * ColumnSpacing;

            var mainTitle    = $"Stage {column.Key + 1}";

            var titleBlock = new TextBlock
            {
                Text                = mainTitle,
                Width               = NodeWidth,
                TextAlignment       = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight          = FontWeights.SemiBold,
            };
            titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TitleText");
            titleBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeHeading");

            UIElement headerElement = titleBlock;

            headerElement.SetValue(FrameworkElement.TagProperty, $"stage:{column.Key}");
            if (headerElement is FrameworkElement headerFe)
            {
                headerFe.Cursor = Cursors.Hand;
                WireSelectionClick(headerFe);
            }

            Canvas.SetLeft(headerElement, x);
            // Keep stage titles near the canvas ceiling, independent of the extra clearance
            // reserved below for milestone controls.
            Canvas.SetTop(headerElement, baseOctagonSize * 0.25 * _scaleFactor);
            canvas.Children.Add(headerElement);

            var nextY = graphTop;
            for (var row = 0; row < tasks.Length; row++)
            {
                var taskHasAvatars = _resolveAgentAvatar is not null &&
                                     tasks[row].AgentAssignments is { Count: > 0 };
                var avatarOffset = taskHasAvatars
                    ? Math.Round(BaseNodeHeight * 0.375 * _scaleFactor)
                    : 0.0;
                nextY += avatarOffset;
                _naturalTaskSlotTops[tasks[row].Id] = nextY;

                var nudgedY = nextY;
                if (_taskNudges.TryGetValue(tasks[row].Id, out var nudgeLineHeights) &&
                    nudgeLineHeights != 0.0)
                {
                    var fontSizeBody = _scaleFactor * 12.0;
                    nudgedY = nextY + nudgeLineHeights * fontSizeBody;
                    var minY = row == 0
                        ? graphTop
                        : positions[tasks[row - 1].Id].Y + NodeHeight + NodeHeight * 0.1 * _scaleFactor;
                    nudgedY = Math.Max(nudgedY, minY);
                }

                positions[tasks[row].Id] = new Point(x, nudgedY);
                var attachedCount = validationAnchors.Values.Count(anchor =>
                    anchor.TaskId is not null &&
                    string.Equals(anchor.TaskId, tasks[row].Id, StringComparison.Ordinal));
                nextY += ValidationShieldPresenter.ComputeAttachedTaskSpacing(
                    attachedCount, NodeHeight, RowSpacing, _scaleFactor);
            }
        }
        _taskCardPositions.Clear();
        foreach (var entry in positions)
            _taskCardPositions[entry.Key] = entry.Value;

        _visualizationGateConnectorGroups = null;
        _allGateBadgeElements = null;

        // Tasks that share the exact same prerequisite set share one ALL gate. This expresses
        // the AND dependency without the all-to-all mesh that made the old graph ambiguous.
        var gatedGroups = group.Tasks
            .Where(task => task.DependsOn.Count > 1)
            .GroupBy(task => string.Join("\u001f", task.DependsOn.OrderBy(id => id, StringComparer.Ordinal)))
            .ToArray();
        var gatedTaskIds = gatedGroups.SelectMany(g => g).Select(task => task.Id).ToHashSet(StringComparer.Ordinal);

        // Pass 1: compute ALL-gate centers (without drawing yet).
        var gates = new List<(Point Center, DecomposedSubTask[] Targets, string[] Dependencies, int MinTargetLevel, int MaxDepLevel)>();
        foreach (var gateGroup in gatedGroups)
        {
            var targets      = gateGroup.ToArray();
            var dependencies = targets[0].DependsOn.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var sourceRight  = dependencies.Where(positions.ContainsKey).Max(id => positions[id].X + NodeWidth);
            var targetLeft   = targets.Min(task => positions[task.Id].X);
            var centers      = dependencies.Where(positions.ContainsKey).Select(id => positions[id].Y + NodeHeight / 2.0)
                                   .Concat(targets.Select(task => positions[task.Id].Y + NodeHeight / 2.0));
            var gateCenterX = (sourceRight + targetLeft) / 2;
            var gateCenterY = centers.Average();
            var allKey = string.Join("\u001f", dependencies);
            var attachedValidationCount = validationAnchors.Values.Count(anchor =>
                anchor.Kind == "all" && string.Equals(anchor.AllKey, allKey, StringComparison.Ordinal));
            if (attachedValidationCount > 0)
            {
                var foreignConnectorYs = new List<double>();
                foreach (var directTarget in group.Tasks.Where(task => task.DependsOn.Count == 1))
                {
                    var dependency = directTarget.DependsOn[0];
                    if (!positions.TryGetValue(dependency, out var sourcePosition) ||
                        !positions.TryGetValue(directTarget.Id, out var targetPosition))
                        continue;
                    var fromX = sourcePosition.X + NodeWidth;
                    var toX = targetPosition.X;
                    if (gateCenterX < fromX || gateCenterX > toX)
                        continue;
                    var ratio = Math.Clamp((gateCenterX - fromX) / Math.Max(1, toX - fromX), 0, 1);
                    var fromY = sourcePosition.Y + NodeHeight / 2.0;
                    var toY = targetPosition.Y + NodeHeight / 2.0;
                    foreignConnectorYs.Add(fromY + (toY - fromY) * ratio);
                }
                gateCenterY = ValidationShieldPresenter.AvoidConnectorOverlapForAllCluster(
                    gateCenterY, attachedValidationCount, foreignConnectorYs, _scaleFactor);
            }
            var gateCenter       = new Point(gateCenterX, gateCenterY);
            var minTargetLevel   = targets.Min(t => levels[t.Id]);
            var maxDepLevel      = dependencies.Where(positions.ContainsKey).Max(id => levels[id]);
            gates.Add((gateCenter, targets, dependencies, minTargetLevel, maxDepLevel));
        }

        _visualizationGates = gates;

        // Multiple ALL joins can occupy the same stage boundary. Arrange them using their
        // complete badge + attached-validation footprints, not badge height alone; otherwise
        // a lower ALL badge can be drawn on top of the upper join's shield or title.
        var gateIndexesByBoundary = Enumerable.Range(0, gates.Count)
            .GroupBy(index => Math.Round(gates[index].Center.X, 2));
        foreach (var boundaryIndexes in gateIndexesByBoundary)
        {
            var indexes = boundaryIndexes.ToArray();
            if (indexes.Length < 2) continue;

            int AttachedValidationCount(int gateIndex)
            {
                var allKey = string.Join("\u001f", gates[gateIndex].Dependencies);
                return validationAnchors.Values.Count(anchor =>
                    anchor.Kind == "all" &&
                    string.Equals(anchor.AllKey, allKey, StringComparison.Ordinal));
            }

            var centers = ValidationShieldPresenter.StackAllClusterCenters(
                indexes.Select(index => new ValidationShieldPresenter.AllClusterStackItem(
                    gates[index].Center.Y,
                    AttachedValidationCount(index))).ToArray(),
                _scaleFactor);
            for (var itemIndex = 0; itemIndex < indexes.Length; itemIndex++)
            {
                var gateIndex = indexes[itemIndex];
                var gate = gates[gateIndex];
                gates[gateIndex] = (
                    new Point(gate.Center.X, centers[itemIndex]),
                    gate.Targets,
                    gate.Dependencies,
                    gate.MinTargetLevel,
                    gate.MaxDepLevel);
            }
        }

        bool SameBoundary(
            IReadOnlyList<string> actualAfter,
            IReadOnlyList<string> actualBefore,
            IReadOnlyList<string> expectedAfter,
            IReadOnlyList<string> expectedBefore) =>
            actualAfter.OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(expectedAfter.OrderBy(id => id, StringComparer.Ordinal)) &&
            actualBefore.OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(expectedBefore.OrderBy(id => id, StringComparer.Ordinal));

        // A stage milestone joins the two adjacent displayed columns. Blocking the immediate
        // next stage also blocks its downstream stages through the dependency graph.
        var lockedMilestoneBoundaryXs = new List<double>();
        var stageBoundaries = new List<(string[] AfterIds, string[] BeforeIds)>();
        var stageBoundaryXs = new List<double>();

        // Compute a uniform band height from the tallest stage (most tasks), extending by the
        // octagon control height above and below so the band visually connects to the stop.
        // Use the avatar top (position.Y - avatarChipSize) as the visual ceiling so the band
        // extends up to cover the agent avatar area.
        var octagonSize = baseOctagonSize * _scaleFactor;
        var globalBandTop = columns.SelectMany(col => col).Min(task =>
        {
            var hasAvatars = _resolveAgentAvatar is not null &&
                             task.AgentAssignments is { Count: > 0 };
            var avatarHeight = hasAvatars
                ? Math.Round(BaseNodeHeight * 0.375 * _scaleFactor)
                : 0.0;
            return positions[task.Id].Y - avatarHeight;
        }) - octagonSize;
        var globalBandBottom = columns.SelectMany(col => col).Max(task => positions[task.Id].Y + NodeHeight) + octagonSize;

        for (var columnIndex = 0; columnIndex < columns.Length - 1; columnIndex++)
        {
            var leftColumn = columns[columnIndex];
            var rightColumn = columns[columnIndex + 1];
            var afterIds = leftColumn
                .Select(task => task.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            var beforeIds = rightColumn
                .Select(task => task.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            // Recognize the cumulative boundary representation written by earlier builds so
            // existing plans remain editable and render through the same milestone control.
            var legacyAfterIds = group.Tasks
                .Where(task => levels[task.Id] <= leftColumn.Key)
                .Select(task => task.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            var legacyBeforeIds = group.Tasks
                .Where(task => levels[task.Id] > leftColumn.Key)
                .Select(task => task.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            stageBoundaries.Add((afterIds, beforeIds));
            stageBoundaries.Add((legacyAfterIds, legacyBeforeIds));

            var existingGate = FindDurableGate(afterIds, beforeIds) ??
                               FindDurableGate(legacyAfterIds, legacyBeforeIds);
            var displayedGate = (group.ApprovalGates ?? []).FirstOrDefault(gate =>
                SameBoundary(gate.AfterTaskIds ?? [], gate.BeforeTaskIds ?? [], afterIds, beforeIds) ||
                SameBoundary(gate.AfterTaskIds ?? [], gate.BeforeTaskIds ?? [], legacyAfterIds, legacyBeforeIds));
            var isLocked = existingGate is not null || displayedGate is not null;
            var milestoneAnchor = StageAnchor(columnIndex + 1);
            var milestoneIsPrimary = existingGate is null || IsPrimary(existingGate, milestoneAnchor);
            var milestoneExecutionLocked = durablePlan is not null &&
                PlanApprovalControlLockPolicy.PlanHasExecutionContext(durablePlan) &&
                PlanApprovalControlLockPolicy.IsStageMilestoneLocked(durablePlan, afterIds, beforeIds);

            var leftTasks = leftColumn.ToArray();
            var leftX = positions[leftTasks[0].Id].X;
            var nextX = positions[columns[columnIndex + 1].First().Id].X;
            var boundaryX = (leftX + NodeWidth + nextX) / 2.0;
            stageBoundaryXs.Add(boundaryX);
            if (isLocked) lockedMilestoneBoundaryXs.Add(boundaryX);
            var milestoneBand = new Border
            {
                Width        = 24 * _scaleFactor,
                Height       = Math.Max(1, globalBandBottom - globalBandTop),
                CornerRadius = new CornerRadius(4),
                Opacity      = isLocked ? 0.90 : 0.56,
                ToolTip      = "Stage milestone boundary",
            };
            milestoneBand.SetResourceReference(Border.BackgroundProperty, "ActivePanelBorder");
            milestoneBand.Tag = $"milestone:{columnIndex}";
            milestoneBand.Cursor = Cursors.Hand;
            WireSelectionClick(milestoneBand);
            Canvas.SetLeft(milestoneBand, boundaryX - 12 * _scaleFactor);
            Canvas.SetTop(milestoneBand, globalBandTop);
            Panel.SetZIndex(milestoneBand, -2);
            canvas.Children.Add(milestoneBand);

            var milestoneChangeActive = IsApprovalChangeActive(existingGate);
            var milestoneVisual = PlanApprovalHistoricalPresentationPolicy.Resolve(
                milestoneExecutionLocked,
                existingGate?.Status,
                milestoneIsPrimary,
                isApprovalChangeActive: milestoneChangeActive);
            var milestoneApproved = milestoneVisual == PlanApprovalControlVisualState.ApprovedCheck;
            var milestoneAwaiting = milestoneVisual == PlanApprovalControlVisualState.AwaitingQuestion;
            var milestoneChanging = milestoneVisual == PlanApprovalControlVisualState.ActiveChangeQuestion;
            var milestoneHasQuestion = milestoneAwaiting || milestoneChanging;
            if (milestoneVisual != PlanApprovalControlVisualState.Hidden)
            {
                var milestoneStop = CreateApprovalStop(
                    isLocked,
                    milestoneApproved
                        ? BuildApprovalResolvedToolTip(existingGate, "this stage milestone")
                        : milestoneAwaiting
                        ? "Waiting for human approval at this stage milestone."
                        : milestoneExecutionLocked
                    ? PlanApprovalControlLockPolicy.LockedTooltip("Stage milestone")
                : onGatesChanged is null
                    ? isLocked
                        ? "Preview: human approval is required at this stage milestone."
                        : "Preview: this stop controls approval at the stage milestone."
                    : isLocked
                        ? milestoneIsPrimary
                            ? "Human approval is required after the stage to the left completes and before the next stage begins. Click to review; right-click to remove."
                            : "This is an equivalent view of the approval boundary. Click to review; right-click to make this the primary control."
                        : "Require human approval after the stage to the left completes and before the next stage begins.",
                milestoneExecutionLocked ? null
                : onGatesChanged is null
                    ? null
                    : () =>
                {
                    Plan updated;
                    if (isLocked && existingGate is not null)
                    {
                        updated = milestoneIsPrimary
                            ? PlanGateManager.RemoveGate(durablePlan!, existingGate.GateId)
                            : PlanGateManager.SetPresentationAnchor(durablePlan!, existingGate.GateId, milestoneAnchor);
                    }
                    else
                    {
                        var defaultQ = $"Has Stage {leftColumn.Key + 1} been reviewed and approved?";
                        var question = _saveDefinitionEdit is not null
                            ? SimpleInputDialog.Show(this, "Enter a reviewer question for this approval gate:", "Add Approval Gate", defaultQ)
                            : defaultQ;
                        if (string.IsNullOrWhiteSpace(question)) return;
                        updated = PlanGateManager.AddBoundaryGate(
                            durablePlan!,
                            afterIds,
                            beforeIds,
                            $"Review milestone before Stage {leftColumn.Key + 2}",
                            milestoneAnchor,
                            removeSubsumedTaskGates: true,
                            question: question);
                    }
                    if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                    },
                    isLocked && !milestoneIsPrimary ? 0.5 : 1.0,
                    approved: milestoneApproved,
                    awaitingApproval: milestoneAwaiting,
                    approvalChangeActive: milestoneChanging,
                    selectionAnchor: existingGate?.GateId,
                    toggleActionLabel: milestoneIsPrimary
                        ? "Remove this approval requirement"
                        : "Make this the primary approval control",
                    approveAction: milestoneAwaiting && existingGate is not null && onApproveGate is not null
                        ? () => { onApproveGate(durablePlan!, existingGate.GateId); }
                        : null,
                    unapproveAction: milestoneApproved && existingGate is not null && onUnapproveGate is not null
                        ? () => { onUnapproveGate(durablePlan!, existingGate.GateId); }
                        : null,
                    isDependent: isLocked && !milestoneIsPrimary);
                Canvas.SetLeft(milestoneStop, boundaryX - (milestoneApproved || milestoneHasQuestion ? 10 : 8) * _scaleFactor);
                Canvas.SetTop(milestoneStop, globalBandTop - octagonSize - 4 * _scaleFactor);
                Panel.SetZIndex(milestoneStop, 25);
                if (existingGate is not null && milestoneStop.ContextMenu is ContextMenu milestoneMenu)
                {
                    var blocksSubmenu = CreateApprovalBlocksSubmenu(existingGate.GateId, group);
                    if (blocksSubmenu is not null)
                    {
                        milestoneMenu.Items.Add(CreateThemedMenuSeparator());
                        milestoneMenu.Items.Add(blocksSubmenu);
                    }
                }
                if (!milestoneIsPrimary && existingGate is not null && PlanGateManager.CanEditGate(existingGate)
                    && !milestoneExecutionLocked && onGatesChanged is not null
                    && milestoneStop.ContextMenu is ContextMenu milestoneRemoveMenu)
                {
                    var capturedMilestoneRemoveId = existingGate.GateId;
                    var removeMilestoneItem = CreateThemedMenuItem("Remove this approval requirement");
                    removeMilestoneItem.Click += (_, _) =>
                    {
                        var u = PlanGateManager.RemoveGate(durablePlan!, capturedMilestoneRemoveId);
                        if (!ReferenceEquals(u, durablePlan)) onGatesChanged(u);
                    };
                    milestoneRemoveMenu.Items.Add(CreateThemedMenuSeparator());
                    milestoneRemoveMenu.Items.Add(removeMilestoneItem);
                }
                canvas.Children.Add(milestoneStop);
                approvalControlsByAnchor[milestoneAnchor] = milestoneStop;
            }
        }

        // Internal entries align with stage milestones. The additional final entry gives
        // validations with no blocked tasks—especially final audits—a real right-side anchor.
        var validationRailXs = stageBoundaryXs.ToList();
        var finalColumnRight = columns
            .SelectMany(column => column)
            .Max(task => positions[task.Id].X + NodeWidth);
        validationRailXs.Add(
            finalColumnRight + 12 * _scaleFactor +
            ValidationShieldPresenter.BaseShieldVisualWidth / 2 * _scaleFactor);

        var validationBottom = 0.0;
        if (displayedValidations.Count > 0)
        {
            var stackIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
            var fallbackNextLeft = 100 * _scaleFactor;
            foreach (var validation in displayedValidations
                         .OrderBy(candidate => candidate.AfterTaskIds
                             .Where(levels.ContainsKey)
                             .Select(id => levels[id])
                             .DefaultIfEmpty(0)
                             .Max())
                         .ThenBy(candidate => candidate.ValidationId, StringComparer.Ordinal))
            {
                var durableValidation = durablePlan?.Validations?.FirstOrDefault(candidate =>
                    string.Equals(candidate.ValidationId, validation.ValidationId, StringComparison.Ordinal));
                var validationStatus = durableValidation?.Status ?? PlanValidationStatus.Pending;
                var anchor = validationAnchors[validation.ValidationId];
                var stackKey = anchor.Kind switch
                {
                    "stage" => $"stage:{anchor.StageIndex}",
                    "all" => $"all:{anchor.AllKey}",
                    // Entry and exit validations share the task's vertical stack. Their
                    // horizontal edges differ, but a narrow task can make the two 144px
                    // validation visuals overlap if each side starts again at index zero.
                    "before" => $"task:{anchor.TaskId}",
                    "after" => $"task:{anchor.TaskId}",
                    _ => anchor.StageIndex >= 0 ? $"rail:{anchor.StageIndex}" : "rail",
                };
                var stackIndex = stackIndexes.GetValueOrDefault(stackKey);
                stackIndexes[stackKey] = stackIndex + 1;

                var isStageValidation = anchor.Kind == "stage";
                var validationWidth = isStageValidation ? 288 * _scaleFactor : 144 * _scaleFactor;

                var visual = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Width = validationWidth,
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Focusable = true,
                    ToolTip = BuildValidationToolTip(validation, durableValidation, tasksById),
                };
                System.Windows.Automation.AutomationProperties.SetName(
                    visual,
                    $"Validation: {validation.Title} — {FormatValidationStatus(validationStatus)}");
                System.Windows.Automation.AutomationProperties.SetHelpText(
                    visual,
                    validation.Description);
                var shield = CreateValidationShield(validation.ValidationId, validationStatus);
                shield.HorizontalAlignment = HorizontalAlignment.Center;
                visual.Children.Add(shield);
                var displayTitle = ValidationShieldPresenter.TruncateTitle(validation.Title);
                var title = new TextBlock
                {
                    Text = displayTitle,
                    MaxWidth = (isStageValidation ? 280 : 136) * _scaleFactor,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                title.SetResourceReference(TextBlock.ForegroundProperty,
                    validationStatus == PlanValidationStatus.Failed ? "PriorityCritical" : "LabelText");
                title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
                var titleBorder = new Border
                {
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(3, 1, 3, 1),
                    Margin = new Thickness(0, 3 * _scaleFactor, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = title,
                    ToolTip = displayTitle != validation.Title
                        ? BuildValidationToolTip(validation, durableValidation, tasksById)
                        : null,
                };
                titleBorder.SetResourceReference(Border.BackgroundProperty, "ValidationTitleBackdrop");
                visual.Children.Add(titleBorder);
                visual.Tag = $"validation:{validation.ValidationId}";
                WireSelectionClick(visual);
                if (_saveDefinitionEdit is not null)
                {
                    var capturedValidationId = validation.ValidationId;
                    var definitionEditable = IsValidationDefinitionEditable(capturedValidationId);
                    var menu = CreateThemedMenu();
                    var edit = CreateThemedMenuItem("Edit validation", definitionEditable);
                    edit.Click += (_, _) =>
                    {
                        _selectedElement = new SelectedElementIdentity("validation", capturedValidationId);
                        _isInspectorEditing = true;
                        ApplySelectionVisual();
                        RefreshDetailPanel();
                    };
                    var remove = new MenuItem { Header = "Delete validation", IsEnabled = definitionEditable };
                    remove.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                    remove.Click += (_, _) =>
                    {
                        if (!ConfirmDestructivePlanEdit($"Delete validation {capturedValidationId}?")) return;
                        ApplyStagedMutation(StagedPlanEditor.DeleteValidation(
                            _plan!.Group, capturedValidationId));
                    };
                    menu.Items.Add(edit);
                    menu.Items.Add(remove);
                    visual.ContextMenu = menu;
                }

                var taskPositionMap = positions.ToDictionary(
                    entry => entry.Key,
                    entry => (entry.Value.X, entry.Value.Y),
                    StringComparer.Ordinal);
                var gateCenterMap = gates.Select(candidate => (
                    candidate.Center.X,
                    candidate.Center.Y,
                    string.Join("\u001f", candidate.Dependencies.OrderBy(id => id, StringComparer.Ordinal)))).ToArray();
                // Attach before measuring so dynamic environment font resources resolve exactly
                // as they will in the rendered visual.
                canvas.Children.Add(visual);
                visual.Measure(new Size(validationWidth, double.PositiveInfinity));
                var measuredVisualHeight = Math.Max(
                    ValidationShieldPresenter.BaseShieldVisualHeight * _scaleFactor,
                    visual.DesiredSize.Height);
                var layout = ValidationShieldPresenter.ComputeShieldPosition(
                    PresenterAnchor(anchor),
                    stackIndex,
                    _scaleFactor,
                    validationRailXs,
                    taskPositionMap,
                    NodeWidth,
                    NodeHeight,
                    graphTop,
                    gateCenterMap,
                    ref fallbackNextLeft,
                    measuredVisualHeight);
                var left = layout.Left;
                var top = layout.Top;
                if (isStageValidation)
                    left -= 72 * _scaleFactor; // re-center the wider (288px) visual over the stage boundary

                Canvas.SetLeft(visual, left);
                Canvas.SetTop(visual, top);
                Panel.SetZIndex(visual, 30);
                _deferredShieldHovers.Add((visual, validation.AfterTaskIds, validation.BeforeTaskIds));
                if (anchor.TaskId is { } shieldTaskId)
                    RegisterTaskDragOverlay(shieldTaskId, visual);

                // Show Verity Cross avatar while a validation is actively running.
                if (validationStatus == PlanValidationStatus.Validating && _resolveAgentAvatar is not null)
                {
                    var verityInfo = _resolveAgentAvatar(VerityCrossHandle);
                    if (verityInfo is not null)
                    {
                        var chipSize = Math.Round(BaseNodeHeight * 0.375 * _scaleFactor);
                        var verityChip = CreateAgentAvatarChip(verityInfo, chipSize, VerityCrossHandle);
                        verityChip.ToolTip = ToolTipHelper.MakeThemedToolTip("Verity Cross — Fact Checker");
                        verityChip.Tag = $"agent:{VerityCrossHandle}";
                        if (!_agentChipElements.TryGetValue(VerityCrossHandle, out var chipList))
                        {
                            chipList = [];
                            _agentChipElements[VerityCrossHandle] = chipList;
                        }
                        chipList.Add((Border)verityChip);
                        WireSelectionClick(verityChip);
                        var currentShieldWidth = Math.Round(chipSize * 29.0 / 31.0);
                        var chipLeft = left + validationWidth / 2 + currentShieldWidth / 2 + 4 * _scaleFactor;
                        Canvas.SetLeft(verityChip, chipLeft);
                        Canvas.SetTop(verityChip, top);
                        Panel.SetZIndex(verityChip, 31);
                        canvas.Children.Add(verityChip);
                        if (anchor.TaskId is { } validationTaskId)
                            RegisterTaskDragOverlay(validationTaskId, verityChip);
                    }
                }
                validationRailRight = Math.Max(validationRailRight, left + validationWidth);
                validationBottom = Math.Max(validationBottom, top + measuredVisualHeight);
            }
        }

        DecomposedGate? FindDisplayedGate(
            IReadOnlyList<string> afterIds,
            IReadOnlyList<string> beforeIds) =>
            (group.ApprovalGates ?? []).FirstOrDefault(gate =>
                SameBoundary(gate.AfterTaskIds ?? [], gate.BeforeTaskIds ?? [], afterIds, beforeIds));

        bool IsStageBoundary(DecomposedGate gate) => stageBoundaries.Any(boundary =>
            SameBoundary(gate.AfterTaskIds ?? [], gate.BeforeTaskIds ?? [],
                boundary.AfterIds, boundary.BeforeIds));

        bool IsAllJoinBoundary(DecomposedGate gate) => gates.Any(allGate =>
            SameBoundary(gate.AfterTaskIds ?? [], gate.BeforeTaskIds ?? [],
                allGate.Dependencies, allGate.Targets.Select(task => task.Id).ToArray()));

        string? ResolvePresentationAnchor(PlanApprovalGate gate)
        {
            var sourceTasks = durablePlan?.Tasks ?? group.Tasks.Select(task => new PlanTask(
                task.Id, task.Title, task.Description, task.DependsOn, task.Priority,
                PlanTaskStatus.Pending)).ToArray();
            return PlanApprovalPresentationAnchorResolver.Resolve(gate, sourceTasks, levels);
        }

        var visualizationTasks = durablePlan?.Tasks ?? group.Tasks.Select(task => new PlanTask(
            task.Id, task.Title, task.Description, task.DependsOn, task.Priority,
            PlanTaskStatus.Pending)).ToArray();
        var visualizationGates = durablePlan?.ApprovalGates ?? (group.ApprovalGates ?? [])
            .Select(gate => new PlanApprovalGate(
                gate.GateId, gate.Message, gate.AfterTaskIds ?? [], gate.BeforeTaskIds ?? [],
                PlanGateStatus.Pending, Question: gate.Question)).ToArray();
        var dashedTaskEdges = PlanGateVisualizationPolicy.DashedEdges(
            visualizationTasks,
            visualizationGates,
            requireEveryIncomingAtConvergence: true);

        // Compute ALL cluster footprints for connector collision avoidance.
        var allClusterFootprints = new List<ValidationShieldPresenter.LayoutRect>();
        foreach (var (gateCenter, _, dependencies, _, _) in gates)
        {
            var allKey = string.Join("\u001f", dependencies);
            var attachedCount = validationAnchors.Values.Count(anchor =>
                anchor.Kind == "all" && string.Equals(anchor.AllKey, allKey, StringComparison.Ordinal));
            // The ALL badge is an obstacle even when no validation shields are attached.
            // Omitting bare badges allowed unrelated direct connectors to appear to enter them.
            allClusterFootprints.Add(
                ValidationShieldPresenter.ComputeAllClusterFootprint(
                    gateCenter.X, gateCenter.Y, attachedCount, _scaleFactor));
        }
        _allClusterFootprints.Clear();
        _allClusterFootprints.AddRange(allClusterFootprints);
        _lockedMilestoneBoundaryXs.Clear();
        _lockedMilestoneBoundaryXs.AddRange(lockedMilestoneBoundaryXs);

        // Pass 3: scan every edge to build sorted per-task exit/entry Y lists for spread rendering.
        // When a task has N connectors leaving its right edge, they are spread at heights
        // NodeHeight * k/(N+1) for k = 1..N (sorted top-to-bottom by destination Y).
        var rightExitAnchors = new PlanConnectorAnchorDistributor();
        var leftEntryAnchors = new PlanConnectorAnchorDistributor();

        void RegisterExit(string taskId, double otherY) =>
            rightExitAnchors.Register(taskId, otherY);
        void RegisterEntry(string taskId, double otherY) =>
            leftEntryAnchors.Register(taskId, otherY);

        foreach (var (gateCenter, targets, dependencies, _, _) in gates)
        {
            foreach (var dep in dependencies.Where(positions.ContainsKey))
                RegisterExit(dep, gateCenter.Y);
            foreach (var target in targets)
                RegisterEntry(target.Id, gateCenter.Y);
        }
        foreach (var task in group.Tasks.Where(t => !gatedTaskIds.Contains(t.Id)))
            foreach (var dep in task.DependsOn.Where(positions.ContainsKey))
            {
                RegisterExit(dep,      positions[task.Id].Y + NodeHeight / 2.0);
                RegisterEntry(task.Id, positions[dep].Y      + NodeHeight / 2.0);
            }
        rightExitAnchors.Sort();
        leftEntryAnchors.Sort();

        double SpreadExitY(string taskId, double otherY) =>
            rightExitAnchors.ResolveY(taskId, otherY, positions[taskId].Y, NodeHeight);
        double SpreadEntryY(string taskId, double otherY) =>
            leftEntryAnchors.ResolveY(taskId, otherY, positions[taskId].Y, NodeHeight);

        // Pass 4: per-task connector tracking for hover highlight.
        var connectorsByTask = _connectorsByTask;
        connectorsByTask.Clear();
        // Deferred badge hover wiring — populated during badge draw, executed after borderByTask is ready.
        var _deferredBadgeHovers = new List<(Border Badge, List<PlanGraphConnectorGroup> Cgs)>();
        void RegisterConnector(string taskId, PlanGraphConnectorGroup cg, PlanGraphConnectorEndpointRole role)
        {
            if (!connectorsByTask.TryGetValue(taskId, out var list))
                connectorsByTask[taskId] = list = [];
            if (!list.Contains(cg)) list.Add(cg);
            if (!cg.TaskIds.Contains(taskId, StringComparer.Ordinal)) cg.TaskIds.Add(taskId);
            cg.RegisterEndpointRole(taskId, role);
        }

        // Find the leftmost locked milestone boundary X strictly between fromX and toX, or NaN if none.
        double FindSplitX(double fromX, double toX) =>
            lockedMilestoneBoundaryXs.Where(bx => bx > fromX + 1.0 && bx < toX - 1.0)
                .OrderBy(bx => bx).Cast<double?>().FirstOrDefault() ?? double.NaN;

        // Draw ALL-gate connectors; collect per-gate groups so the badge can reference them later.
        var gateConnectorGroups = new List<List<PlanGraphConnectorGroup>>(gates.Count);
        foreach (var (gateCenter, targets, dependencies, minTargetLevel, maxDepLevel) in gates)
        {
            var cgsForGate = new List<PlanGraphConnectorGroup>();
            var joinBeforeIds = targets.Select(task => task.Id).ToArray();
            var joinIsLocked = FindDurableGate(dependencies, joinBeforeIds) is not null ||
                               FindDisplayedGate(dependencies, joinBeforeIds) is not null;
            // A task-exit gate belongs on that task's segment entering the ALL join. A gate on
            // the ALL join itself belongs on the shared outbound segment.
            foreach (var dependency in dependencies.Where(positions.ContainsKey))
            {
                var source  = positions[dependency];
                var depSkip = minTargetLevel - levels[dependency] - 1;
                var fromPt  = new Point(source.X + NodeWidth, SpreadExitY(dependency, gateCenter.Y));
                var toPt    = new Point(gateCenter.X - 29, gateCenter.Y);
                var cg = _graphEdgeRenderingContract.AddConnector(canvas,
                    fromPt, toPt,
                    arrowHead: false,
                    skipCount: Math.Max(0, depSkip),
                    dashed: targets.Any(target => dashedTaskEdges.Contains((dependency, target.Id))),
                    splitAtX: FindSplitX(fromPt.X, toPt.X));
                RegisterConnector(dependency, cg, PlanGraphConnectorEndpointRole.Start);
                cgsForGate.Add(cg);
            }
            foreach (var target in targets)
            {
                var targetPoint = positions[target.Id];
                var targetSkip  = levels[target.Id] - maxDepLevel - 1;
                var fromPt      = new Point(gateCenter.X + 29, gateCenter.Y);
                var toPt        = new Point(targetPoint.X, SpreadEntryY(target.Id, gateCenter.Y));
                var incomingStates = dependencies
                    .Select(dependency => dashedTaskEdges.Contains((dependency, target.Id)))
                    .ToArray();
                var combinedDashed = incomingStates.All(value => value);
                var cg = _graphEdgeRenderingContract.AddConnector(canvas,
                    fromPt, toPt,
                    arrowHead: true,
                    skipCount: Math.Max(0, targetSkip),
                    dashed: joinIsLocked || combinedDashed,
                    splitAtX: FindSplitX(fromPt.X, toPt.X));
                RegisterConnector(target.Id, cg, PlanGraphConnectorEndpointRole.End);
                cgsForGate.Add(cg);
            }
            gateConnectorGroups.Add(cgsForGate);
        }

        // Draw non-gated direct connectors with collision avoidance for ALL clusters.
        foreach (var task in group.Tasks.Where(task => !gatedTaskIds.Contains(task.Id)))
        {
            foreach (var dependency in task.DependsOn.Where(positions.ContainsKey))
            {
                var source    = positions[dependency];
                var target    = positions[task.Id];
                var skipCount = Math.Max(0, levels[task.Id] - levels[dependency] - 1);
                var fromPt    = new Point(source.X + NodeWidth, SpreadExitY(dependency, target.Y + NodeHeight / 2.0));
                var toPt      = new Point(target.X,             SpreadEntryY(task.Id,   source.Y + NodeHeight / 2.0));

                var isDashed = dashedTaskEdges.Contains((dependency, task.Id));
                var directConnector = AddDirectTaskConnector(
                    canvas,
                    fromPt,
                    toPt,
                    skipCount,
                    isDashed);
                RegisterConnector(dependency, directConnector, PlanGraphConnectorEndpointRole.Start);
                RegisterConnector(task.Id, directConnector, PlanGraphConnectorEndpointRole.End);
            }
        }

        for (int gi = 0; gi < gates.Count; gi++)
        {
            var gate = gates[gi];
            var joinAfterIds = gate.Dependencies;
            var joinBeforeIds = gate.Targets.Select(task => task.Id).ToArray();
            var existingJoinGate = FindDurableGate(joinAfterIds, joinBeforeIds);
            var displayedJoinGate = FindDisplayedGate(joinAfterIds, joinBeforeIds);
            var coveringJoinGate = existingJoinGate is null && durablePlan is not null
                ? durablePlan.ApprovalGates
                    .Where(candidate => PlanGateVisualizationPolicy.CompletelyCovers(
                        durablePlan.Tasks, candidate, joinAfterIds, joinBeforeIds))
                    .OrderByDescending(candidate => candidate.AfterTaskIds.Count + candidate.BeforeTaskIds.Count)
                    .FirstOrDefault()
                : null;
            var collectivelyCoveredJoin = existingJoinGate is null && coveringJoinGate is null &&
                durablePlan is not null &&
                PlanGateVisualizationPolicy.BoundaryIsCollectivelyCoveredByIncomingGates(
                    joinAfterIds, joinBeforeIds, durablePlan.ApprovalGates);
            var collectivelyUnresolvedJoin = collectivelyCoveredJoin && durablePlan is not null &&
                PlanGateVisualizationPolicy.BoundaryHasUnresolvedIncomingGate(
                    joinAfterIds, joinBeforeIds, durablePlan.ApprovalGates);
            var joinIsLocked = existingJoinGate is not null || displayedJoinGate is not null ||
                               coveringJoinGate is not null || collectivelyCoveredJoin;
            var joinAnchor = AllAnchor(joinBeforeIds);
            var joinGate = existingJoinGate ?? coveringJoinGate;
            var joinIsPrimary = IsPrimary(joinGate, joinAnchor);
            var joinController = coveringJoinGate is null ? null : ResolvePresentationAnchor(coveringJoinGate);
            var joinExecutionLocked = durablePlan is not null &&
                PlanApprovalControlLockPolicy.PlanHasExecutionContext(durablePlan) &&
                PlanApprovalControlLockPolicy.IsAllJoinLocked(durablePlan, joinAfterIds, joinBeforeIds);
            var badgeText = new TextBlock
            {
                Text                = "ALL",
                FontWeight          = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                Margin              = new Thickness(0, 0, 14, 0),
            };
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, "ActivePanelTitle");
            badgeText.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            var badgeContent = new Grid();
            badgeContent.Children.Add(badgeText);

            var joinChangeActive = IsApprovalChangeActive(joinGate);
            var joinVisual = PlanApprovalHistoricalPresentationPolicy.Resolve(
                joinExecutionLocked,
                joinGate?.Status,
                IsPrimary(joinGate, joinAnchor),
                hasUnresolvedEquivalent: collectivelyUnresolvedJoin,
                hasResolvedEquivalent: collectivelyCoveredJoin && !collectivelyUnresolvedJoin,
                isApprovalChangeActive: joinChangeActive);
            var joinApproved = joinVisual == PlanApprovalControlVisualState.ApprovedCheck;
            var joinAwaiting = joinVisual == PlanApprovalControlVisualState.AwaitingQuestion;
            var joinChanging = joinVisual == PlanApprovalControlVisualState.ActiveChangeQuestion;
            if (joinVisual != PlanApprovalControlVisualState.Hidden)
            {
                var joinStop = CreateApprovalStop(
                    joinIsLocked,
                    joinApproved
                        ? BuildApprovalResolvedToolTip(existingJoinGate ?? coveringJoinGate, "this ALL join")
                    : joinAwaiting
                        ? "Waiting for human approval at this ALL join."
                    : joinExecutionLocked
                        ? PlanApprovalControlLockPolicy.LockedTooltip("ALL join")
                    : onGatesChanged is null
                        ? joinIsLocked
                            ? "Preview: human approval is required at this ALL join."
                            : "Preview: this stop controls approval at the ALL join."
                        : collectivelyCoveredJoin
                            ? "Every incoming path is approved separately. Click to consolidate them into this ALL approval requirement."
                        : coveringJoinGate is not null
                            ? joinIsPrimary
                                ? "This ALL join is the primary control for a larger approval requirement. Click to review; right-click to remove."
                                : "This ALL join is covered by a larger approval requirement."
                        : joinIsLocked
                            ? joinIsPrimary
                                ? "Human approval is required after every incoming step completes and before joined work begins. Click to review; right-click to remove."
                                : "This is an equivalent view of the approval boundary. Click to review; right-click to make this the primary control."
                            : "Require human approval after every incoming step completes and before joined work begins.",
                    joinExecutionLocked ? null
                    : onGatesChanged is null
                        ? null
                        : () =>
                    {
                        if (coveringJoinGate is not null)
                        {
                            var coveringUpdated = joinIsPrimary &&
                                                  PlanGateManager.CanRemoveGate(durablePlan!, coveringJoinGate)
                                ? PlanGateManager.RemoveGate(durablePlan!, coveringJoinGate.GateId)
                                : PlanGateManager.SetPresentationAnchor(
                                    durablePlan!, coveringJoinGate.GateId, joinAnchor);
                            if (!ReferenceEquals(coveringUpdated, durablePlan)) onGatesChanged(coveringUpdated);
                            return;
                        }
                        Plan updated;
                        if (joinIsLocked && existingJoinGate is not null)
                        {
                            updated = joinIsPrimary
                                ? PlanGateManager.RemoveGate(durablePlan!, existingJoinGate.GateId)
                                : PlanGateManager.SetPresentationAnchor(durablePlan!, existingJoinGate.GateId, joinAnchor);
                        }
                        else
                        {
                            var joinTargetTitle = string.Join(", ", gate.Targets.Select(task => task.Title ?? task.Id));
                            var defaultQ = $"Has the work required for {joinTargetTitle} been reviewed and approved?";
                            var question = _saveDefinitionEdit is not null
                                ? SimpleInputDialog.Show(this, "Enter a reviewer question for this approval gate:", "Add Approval Gate", defaultQ)
                                : defaultQ;
                            if (string.IsNullOrWhiteSpace(question)) return;
                            updated = PlanGateManager.AddBoundaryGate(
                                durablePlan!,
                                joinAfterIds,
                                joinBeforeIds,
                                $"Review joined work before: {joinTargetTitle}",
                                joinAnchor,
                                removeSubsumedTaskGates: true,
                                question: question);
                        }
                        if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                    },
                    PlanGateVisualizationPolicy.ApprovalControlOpacity(
                        joinIsLocked, joinIsPrimary, collectivelyCoveredJoin),
                    approved: joinApproved,
                    awaitingApproval: joinAwaiting,
                    approvalChangeActive: joinChanging,
                    selectionAnchor: (existingJoinGate ?? coveringJoinGate)?.GateId,
                    toggleActionLabel: joinIsPrimary
                        ? "Remove this approval requirement"
                        : "Make this the primary approval control",
                    approveAction: joinAwaiting && existingJoinGate is not null && onApproveGate is not null
                        ? () => { onApproveGate(durablePlan!, existingJoinGate.GateId); }
                        : null,
                    unapproveAction: joinApproved && existingJoinGate is not null && onUnapproveGate is not null
                        ? () => { onUnapproveGate(durablePlan!, existingJoinGate.GateId); }
                        : null,
                    isDependent: PlanGateVisualizationPolicy.IsDependent(joinIsLocked, joinIsPrimary, collectivelyCoveredJoin));
                joinStop.HorizontalAlignment = HorizontalAlignment.Right;
                joinStop.VerticalAlignment = VerticalAlignment.Center;
                joinStop.Margin = new Thickness(0, 0, 4, 0);
                var joinGateForBlocks = existingJoinGate ?? coveringJoinGate;
                if (joinGateForBlocks is not null && joinStop.ContextMenu is ContextMenu joinMenu)
                {
                    var blocksSubmenu = CreateApprovalBlocksSubmenu(joinGateForBlocks.GateId, group);
                    if (blocksSubmenu is not null)
                    {
                        joinMenu.Items.Add(CreateThemedMenuSeparator());
                        joinMenu.Items.Add(blocksSubmenu);
                    }
                }
                if (!joinIsPrimary && joinGateForBlocks is not null && PlanGateManager.CanEditGate(joinGateForBlocks)
                    && !joinExecutionLocked && onGatesChanged is not null
                    && joinStop.ContextMenu is ContextMenu joinRemoveMenu)
                {
                    var capturedJoinRemoveId = joinGateForBlocks.GateId;
                    var removeJoinItem = CreateThemedMenuItem("Remove this approval requirement");
                    removeJoinItem.Click += (_, _) =>
                    {
                        var u = PlanGateManager.RemoveGate(durablePlan!, capturedJoinRemoveId);
                        if (!ReferenceEquals(u, durablePlan)) onGatesChanged(u);
                    };
                    joinRemoveMenu.Items.Add(CreateThemedMenuSeparator());
                    joinRemoveMenu.Items.Add(removeJoinItem);
                }
                badgeContent.Children.Add(joinStop);
                approvalControlsByAnchor[joinAnchor] = joinStop;
            }

            var hasApprovalControl = joinVisual != PlanApprovalControlVisualState.Hidden;
            var badgeWidth = hasApprovalControl ? 66 * _scaleFactor : 46 * _scaleFactor;
            var badgeHalf = badgeWidth / 2;
            if (!hasApprovalControl)
                badgeText.Margin = new Thickness(0);

            var badge = new Border
            {
                Width           = badgeWidth,
                Height          = 34 * _scaleFactor,
                CornerRadius    = new CornerRadius(17 * _scaleFactor),
                BorderThickness = new Thickness(1.5),
                ToolTip         = "ALL prerequisites entering this gate must finish before any outgoing step can begin.",
                Child           = badgeContent,
            };
            badge.SetResourceReference(Border.BorderBrushProperty, "ActivePanelBorder");
            badge.SetResourceReference(Border.BackgroundProperty,  "CardSurface");
            badge.Tag = $"alljoin:{gi}";
            badge.Cursor = Cursors.Hand;
            WireSelectionClick(badge);
            Canvas.SetLeft(badge, gate.Center.X - badgeHalf);
            Canvas.SetTop(badge, gate.Center.Y - 17 * _scaleFactor);
            Panel.SetZIndex(badge, 10);
            canvas.Children.Add(badge);

            // Register the badge on every connector that enters or exits this gate
            // so hover on any of those connectors (or their endpoint tasks) highlights it.
            foreach (var cg in gateConnectorGroups[gi])
                cg.GateBadges.Add(badge);

            // Wire badge hover: highlight all connectors entering/exiting this gate
            // and glow all their endpoint task nodes (wired after borderByTask is built — deferred below).
            var capturedGateCgs  = gateConnectorGroups[gi];
            var capturedBadge    = badge;
            _deferredBadgeHovers.Add((capturedBadge, capturedGateCgs));
        }

        _visualizationGateConnectorGroups = gateConnectorGroups
            .Select(list => (IReadOnlyList<PlanGraphConnectorGroup>)list.AsReadOnly())
            .ToList();
        if (_visualizationGates is { Count: > 0 })
        {
            var badgeElements = new FrameworkElement?[_visualizationGates.Count];
            for (var bgi = 0; bgi < _visualizationGates.Count; bgi++)
            {
                var gateTag = $"alljoin:{bgi}";
                badgeElements[bgi] = canvas.Children.OfType<FrameworkElement>()
                    .FirstOrDefault(c => c.Tag is string t && string.Equals(t, gateTag, StringComparison.Ordinal));
            }
            _allGateBadgeElements = badgeElements;
        }

        var avatarChipsByAgent = new Dictionary<string, List<Border>>(StringComparer.OrdinalIgnoreCase);
        var taskActivityById = durablePlan is null
            ? new Dictionary<string, PlanTaskActivityState>(StringComparer.Ordinal)
            : PlanTaskActivityResolver.Resolve(durablePlan);
        _taskOrdinalById = group.Tasks
            .Select((task, index) => (task.Id, StepLabel:
                durablePlan?.Tasks.FirstOrDefault(candidate =>
                    string.Equals(candidate.TaskId, task.Id, StringComparison.Ordinal))?.DisplayStepLabel
                ?? (index + 1).ToString()))
            .ToDictionary(item => item.Id, item => item.StepLabel, StringComparer.Ordinal);

        foreach (var task in group.Tasks)
        {
            var position = positions[task.Id];
            var durableTask = durablePlan?.Tasks.FirstOrDefault(t =>
                string.Equals(t.TaskId, task.Id, StringComparison.Ordinal));
            var activityState = taskActivityById.TryGetValue(task.Id, out var resolvedActivityState)
                ? resolvedActivityState
                : PlanTaskActivityState.Queued;
            activityState = PlanTaskActivityPresentation.ResolveLiveState(
                activityState,
                _isTaskActivityActive?.Invoke(task.Id) ?? true);
            if (_recoveryAssessmentTaskIds.Contains(task.Id) ||
                (_isRecoveryAssessmentActive?.Invoke(task.Id) ?? false))
                activityState = PlanTaskActivityState.Assessing;
            if (ResolveTaskCorrectionActivity(task.Id) == PlanTaskCorrectionActivityState.Running)
                activityState = PlanTaskActivityState.Reworking;
            var isTaskExecuting = activityState is PlanTaskActivityState.Executing or
                PlanTaskActivityState.Assessing or
                PlanTaskActivityState.Verifying or
                PlanTaskActivityState.Reworking;
            string? taskIconKey= isTaskExecuting ? null : durableTask?.Status switch
            {
                PlanTaskStatus.Complete   or
                PlanTaskStatus.Superseded => "TaskSucceeded",
                PlanTaskStatus.Failed     => "TaskFailed",
                PlanTaskStatus.Partial    => "TaskPartiallyComplete",
                PlanTaskStatus.HumanReviewRequired => "TaskAwaitingHumanReview",
                _                        => null,
            };
            string borderColorKey = durableTask?.Status == PlanTaskStatus.VerificationPending
                ? "PriorityMid"
                : PlanTaskStatus.IsVerifying(durableTask?.Status)
                ? "ActivePanelBorder"
                : durableTask?.Status switch
            {
                PlanTaskStatus.Complete   or
                PlanTaskStatus.Superseded => "PriorityLow",
                PlanTaskStatus.Executing  => "ActivePanelBorder",
                PlanTaskStatus.Reworking => "PriorityMid",
                PlanTaskStatus.HumanReviewRequired => "PriorityHigh",
                PlanTaskStatus.Failed     => "PriorityHigh",
                PlanTaskStatus.Partial    => "PriorityMid",
                _                        => "PanelBorder",
            };

            var nodeTitle = new TextBlock
            {
                Text         = task.Title ?? task.Id,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight    = 40,
                FontWeight   = FontWeights.SemiBold,
            };
            nodeTitle.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            nodeTitle.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");

            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (isTaskExecuting)
            {
                var spinnerOnRight = activityState is PlanTaskActivityState.Verifying or
                    PlanTaskActivityState.Assessing;
                var spinner = new ActivitySpinner
                {
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = spinnerOnRight
                        ? new Thickness(4, 1, -5, 0)
                        : new Thickness(-5, 1, 2, 0),
                    AccentColor = activityState == PlanTaskActivityState.Reworking
                        ? ResolvePlanActivityColor("PriorityMid", Colors.DarkOrange)
                        : ResolvePlanSpinnerColor(),
                    ToolTip = ToolTipHelper.MakeThemedToolTip(activityState switch
                    {
                        PlanTaskActivityState.Verifying =>
                            "SquadDash is independently checking the candidate work and looking for missing or overstated claims.",
                        PlanTaskActivityState.Assessing =>
                            "The Squad coordinator is assessing existing repository work before the interrupted plan continues.",
                        PlanTaskActivityState.Reworking =>
                            "The step is receiving requested correction work.",
                        _ => "This step is actively receiving work from one or more agents.",
                    }),
                };
                spinner.SetResourceReference(ActivitySpinner.FontSizeProperty, "FontSizeSmall");
                Grid.SetColumn(spinner, spinnerOnRight ? 2 : 0);
                _taskSpinnersById[task.Id] = spinner;
                if (_onOpenWorkMetrics is not null)
                {
                    spinner.Cursor = Cursors.Hand;
                    spinner.PreviewMouseLeftButtonUp += (_, e) =>
                    {
                        _onOpenWorkMetrics("task", task.Id);
                        e.Handled = true;
                    };
                }
                titleRow.Children.Add(spinner);
                if (PlanTaskActivityPresentation.KeepsSpinnerContinuouslyActive(activityState))
                    spinner.SetContinuousActive(true);
                else if (_isTaskActivityActive?.Invoke(task.Id) ?? true)
                    spinner.Pulse(SpinnerActivityKind.Thinking);
            }
            else if (taskIconKey is not null &&
                     Application.Current?.TryFindResource(taskIconKey) is Viewbox)
            {
                var iconViewbox = (Viewbox)Application.Current.FindResource(taskIconKey);
                var iconSize = 14 * _scaleFactor;
                iconViewbox.Width = iconSize;
                iconViewbox.Height = iconSize;
                var iconWrapper = new Border
                {
                    Child = iconViewbox,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 4, 0),
                };
                Grid.SetColumn(iconWrapper, 0);
                titleRow.Children.Add(iconWrapper);
            }
            Grid.SetColumn(nodeTitle, 1);
            titleRow.Children.Add(nodeTitle);

            var nodeDescription = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin       = new Thickness(0, 5, 0, 0),
            };
            nodeDescription.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            nodeDescription.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            BindTaskDescriptionPreview(task.Id, task.Description, nodeDescription);
            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.Children.Add(titleRow);
            Grid.SetRow(nodeDescription, 1);
            content.Children.Add(nodeDescription);

            var taskEvidence = durableTask is null || durablePlan is null
                ? Array.Empty<PlanEvidenceCommit>()
                : PlanRecoveryPresentationBuilder.ResolveTaskEvidence(durablePlan, durableTask).ToArray();
            if (taskEvidence.Length > 0)
            {
                var evidenceCommits = taskEvidence;
                var hasMultipleCommits = evidenceCommits.Length > 1;
                var commitSha = evidenceCommits[^1].Commit;
                var shortSha = commitSha.Length >= 7 ? commitSha[..7] : commitSha;
                var commitBlock = new TextBlock
                {
                    Margin     = new Thickness(0, 2, 0, 0),
                    FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                commitBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                commitBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
                var canOpen = hasMultipleCommits ? _onOpenCommits is not null : _onOpenCommit is not null;
                var commitLink = new Hyperlink(new Run(hasMultipleCommits ? $"{evidenceCommits.Length} commits" : shortSha))
                {
                    Cursor = canOpen ? Cursors.Hand : Cursors.Arrow,
                    IsEnabled = canOpen,
                    ToolTip = !canOpen
                        ? null
                        : ToolTipHelper.MakeThemedToolTip(hasMultipleCommits ? "Review commits and changed files" : "Open this commit"),
                };
                commitLink.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
                if (hasMultipleCommits && _onOpenCommits is not null)
                {
                    var capturedCommits = evidenceCommits;
                    commitLink.Click += (_, _) => _onOpenCommits(capturedCommits);
                }
                else if (_onOpenCommit is not null)
                {
                    var capturedCommitSha = commitSha;
                    commitLink.Click += (_, _) => _onOpenCommit(capturedCommitSha);
                }
                commitBlock.Inlines.Add(new Run(hasMultipleCommits ? string.Empty : "Commit "));
                commitBlock.Inlines.Add(commitLink);
                Grid.SetRow(commitBlock, 2);
                content.Children.Add(commitBlock);
            }

            var nodeLayout = new Grid();
            var hasAgentAvatars = _resolveAgentAvatar is not null &&
                                  task.AgentAssignments is { Count: > 0 };
            var avatarChipSize = hasAgentAvatars
                ? Math.Round(BaseNodeHeight * 0.375 * _scaleFactor)
                : 0.0;
            nodeLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            nodeLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var stepLabel = new TextBlock
            {
                Text = PlanTaskActivityPresentation.BuildStepLabel(
                    _taskOrdinalById[task.Id], activityState),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(20 * _scaleFactor, 5, 20 * _scaleFactor, 0),
            };
            stepLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            stepLabel.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");

            Grid.SetRow(stepLabel, 1);
            nodeLayout.Children.Add(content);
            nodeLayout.Children.Add(stepLabel);

            var border = new Border
            {
                Width           = NodeWidth,
                Height          = NodeHeight,
                Padding         = new Thickness(11, 8, 11, 8),
                CornerRadius    = new CornerRadius(7),
                BorderThickness = new Thickness(1.25),
                ToolTip         = _isReadOnlyFixture ? BuildTaskToolTip(task, durableTask) : "Click to inspect",
                Child           = nodeLayout,
            };
            border.SetResourceReference(Border.BackgroundProperty,  "CardSurface");
            border.SetResourceReference(Border.BorderBrushProperty, borderColorKey);
            border.Tag = $"task:{task.Id}";
            WireTaskCardDrag(border, task.Id);
            Canvas.SetLeft(border, position.X);
            Canvas.SetTop(border, position.Y);

            if (durablePlan is not null && onGatesChanged is not null)
            {
                var capturedTask = task;
                var taskEntryLocked = PlanApprovalControlLockPolicy.PlanHasExecutionContext(durablePlan) &&
                    PlanApprovalControlLockPolicy.IsTaskEntryLocked(durablePlan, capturedTask.Id);
                var taskExitLocked = PlanApprovalControlLockPolicy.PlanHasExecutionContext(durablePlan) &&
                    PlanApprovalControlLockPolicy.IsTaskExitLocked(durablePlan, capturedTask.Id);

                var addBeforeItem = new MenuItem
                {
                    Header = "Require approval before this step",
                    IsEnabled = !taskEntryLocked,
                };
                if (taskEntryLocked)
                    addBeforeItem.ToolTip = ToolTipHelper.MakeThemedToolTip(
                        PlanApprovalControlLockPolicy.LockedTooltip("Step entry"));
                addBeforeItem.Click += (_, _) =>
                {
                    if (_saveDefinitionEdit is not null)
                    {
                        var stagedGatePlan = PlanGateManager.AddGateBefore(
                            durablePlan,
                            capturedTask.Id,
                            $"Review before: {capturedTask.Title ?? capturedTask.Id}");
                        var changedGate = stagedGatePlan.ApprovalGates.FirstOrDefault(gate =>
                            durablePlan.ApprovalGates.FirstOrDefault(existing =>
                                existing.GateId == gate.GateId) is not { } existing ||
                            !Equals(existing, gate));
                        if (changedGate is not null)
                            ApplyStagedMutation(
                                PendingDecomposePlanAdapter.FromPlan(stagedGatePlan).Group,
                                selectKind: "humanapproval",
                                selectId: changedGate.GateId,
                                beginEditingSelection: true);
                        return;
                    }
                    var msg = SimpleInputDialog.Show(Window.GetWindow(border) ?? Application.Current!.MainWindow!,
                        "Enter a message for this approval gate:",
                        "Require Approval Before",
                        $"Review before: {capturedTask.Title ?? capturedTask.Id}");
                    if (msg is null) return;
                    var updated = PlanGateManager.AddGateBefore(durablePlan, capturedTask.Id, msg);
                    var ctxBeforeGate = updated.ApprovalGates.FirstOrDefault(g =>
                        PlanGateManager.CanEditGate(g) &&
                        g.BeforeTaskIds.Contains(capturedTask.Id, StringComparer.Ordinal));
                    if (ctxBeforeGate is not null)
                    {
                        var ctxBeforeAnchor = TaskBeforeAnchor(capturedTask.Id);
                        var withAnchor = PlanGateManager.SetPresentationAnchor(updated, ctxBeforeGate.GateId, ctxBeforeAnchor);
                        if (!ReferenceEquals(withAnchor, updated)) updated = withAnchor;
                    }
                    if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                };

                var addAfterItem = new MenuItem
                {
                    Header = "Require approval after this step",
                    IsEnabled = !taskExitLocked,
                };
                if (taskExitLocked)
                    addAfterItem.ToolTip = ToolTipHelper.MakeThemedToolTip(
                        PlanApprovalControlLockPolicy.LockedTooltip("Step exit"));
                addAfterItem.Click += (_, _) =>
                {
                    if (_saveDefinitionEdit is not null)
                    {
                        var stagedGatePlan = PlanGateManager.AddGateAfter(
                            durablePlan,
                            capturedTask.Id,
                            $"Review after: {capturedTask.Title ?? capturedTask.Id}");
                        var changedGate = stagedGatePlan.ApprovalGates.FirstOrDefault(gate =>
                            durablePlan.ApprovalGates.FirstOrDefault(existing =>
                                existing.GateId == gate.GateId) is not { } existing ||
                            !Equals(existing, gate));
                        if (changedGate is not null)
                            ApplyStagedMutation(
                                PendingDecomposePlanAdapter.FromPlan(stagedGatePlan).Group,
                                selectKind: "humanapproval",
                                selectId: changedGate.GateId,
                                beginEditingSelection: true);
                        return;
                    }
                    var msg = SimpleInputDialog.Show(Window.GetWindow(border) ?? Application.Current!.MainWindow!,
                        "Enter a message for this approval gate:",
                        "Require Approval After",
                        $"Review after: {capturedTask.Title ?? capturedTask.Id}");
                    if (msg is null) return;
                    var updated = PlanGateManager.AddGateAfter(durablePlan, capturedTask.Id, msg);
                    var ctxAfterGate = updated.ApprovalGates.FirstOrDefault(g =>
                        PlanGateManager.CanEditGate(g) &&
                        g.AfterTaskIds.Contains(capturedTask.Id, StringComparer.Ordinal));
                    if (ctxAfterGate is not null)
                    {
                        var ctxAfterAnchor = TaskAfterAnchor(capturedTask.Id);
                        var withAnchor = PlanGateManager.SetPresentationAnchor(updated, ctxAfterGate.GateId, ctxAfterAnchor);
                        if (!ReferenceEquals(withAnchor, updated)) updated = withAnchor;
                    }
                    if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                };

                var contextMenu = CreateThemedMenu();
                var canInsertBefore = PlanStoreUpdater.CanInsertTask(durablePlan, capturedTask.Id, insertAfter: false);
                var canInsertAfter = PlanStoreUpdater.CanInsertTask(durablePlan, capturedTask.Id, insertAfter: true);
                contextMenu.Items.Add(CreateInsertTaskMenuItem(insertAfter: false, canInsertBefore));
                contextMenu.Items.Add(CreateInsertTaskMenuItem(insertAfter: true, canInsertAfter));
                contextMenu.Items.Add(CreateThemedMenuSeparator());
                if (!PlanGateManager.IsRootTask(durablePlan, capturedTask.Id))
                    contextMenu.Items.Add(addBeforeItem);
                if (!PlanGateManager.IsLeafTask(durablePlan, capturedTask.Id))
                    contextMenu.Items.Add(addAfterItem);

                var gatesForTask = (group.ApprovalGates ?? [])
                    .Where(g => !IsStageBoundary(g) && !IsAllJoinBoundary(g))
                    .Where(g =>
                        (g.AfterTaskIds?.Contains(capturedTask.Id, StringComparer.Ordinal) ?? false) ||
                        (g.BeforeTaskIds?.Contains(capturedTask.Id, StringComparer.Ordinal) ?? false))
                    .ToArray();
                if (gatesForTask.Length > 0)
                {
                    contextMenu.Items.Add(CreateThemedMenuSeparator());
                    foreach (var approvalGate in gatesForTask)
                    {
                        var capturedGate = approvalGate;
                        var durableGate = durablePlan.ApprovalGates.FirstOrDefault(g =>
                            string.Equals(g.GateId, capturedGate.GateId, StringComparison.Ordinal));
                        var gateIsTraversed = durableGate is not null &&
                            !PlanGateManager.CanEditGate(durableGate);
                        var isRequiredVerificationProof =
                            PlanGateManager.IsRequiredVerificationProofGate(durablePlan, durableGate);
                        var relationship = (capturedGate.AfterTaskIds ?? [])
                            .Contains(capturedTask.Id, StringComparer.Ordinal)
                                ? "after"
                                : "before";
                        var removeItem = CreateThemedMenuItem(
                            isRequiredVerificationProof
                                ? "Required verification checkpoint"
                                : $"Remove approval checkpoint ({relationship})",
                            PlanGateManager.CanRemoveGate(durablePlan, durableGate));
                        if (isRequiredVerificationProof)
                            removeItem.ToolTip = ToolTipHelper.MakeThemedToolTip(
                                "This checkpoint is the verification step's only proof contract. " +
                                "Delete the verification step or give it another proof requirement before removing this checkpoint.\n\n" +
                                capturedGate.Message);
                        else if (gateIsTraversed)
                            removeItem.ToolTip = ToolTipHelper.MakeThemedToolTip(
                                PlanApprovalControlLockPolicy.LockedTooltip("Traversed gate"));
                        else
                            removeItem.ToolTip = ToolTipHelper.MakeThemedToolTip(capturedGate.Message);
                        removeItem.Click += (_, _) =>
                        {
                            var updated = PlanGateManager.RemoveGate(durablePlan, capturedGate.GateId);
                            if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                        };
                        contextMenu.Items.Add(removeItem);
                    }
                }

                if (contextMenu.Items.Count > 0)
                    border.ContextMenu = contextMenu;

                if (_saveDefinitionEdit is not null)
                {
                    var definitionEditable = IsTaskDefinitionEditable(capturedTask.Id);
                    contextMenu.Items.Add(CreateThemedMenuSeparator());
                    var assignToSubmenu1 = CreateAssignToSubmenu(capturedTask.Id);
                    if (assignToSubmenu1 is not null)
                    {
                        contextMenu.Items.Add(assignToSubmenu1);
                        contextMenu.Items.Add(CreateThemedMenuSeparator());
                    }
                    var dependsOnSubmenu1 = CreateDependsOnSubmenu(capturedTask.Id, group, levels, positions);
                    if (dependsOnSubmenu1 is not null)
                    {
                        contextMenu.Items.Add(dependsOnSubmenu1);
                        contextMenu.Items.Add(CreateThemedMenuSeparator());
                    }
                    contextMenu.Items.Add(CreateVerificationLevelSubmenu(capturedTask.Id));
                    contextMenu.Items.Add(CreateThemedMenuSeparator());
                    var addValidation = new MenuItem { Header = "Add validation after this step", IsEnabled = definitionEditable };
                    addValidation.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                    addValidation.Click += (_, _) => ApplyStagedMutation(
                        StagedPlanEditor.AddValidationAfterTask(_plan!.Group, capturedTask.Id),
                        selectKind: "validation",
                        beginEditingSelection: true);
                    contextMenu.Items.Add(addValidation);

                    var deleteTask = new MenuItem { Header = "Delete step", IsEnabled = group.Tasks.Count > 1 && definitionEditable };
                    deleteTask.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                    deleteTask.Click += (_, _) =>
                    {
                        if (!ConfirmDestructivePlanEdit(
                                $"Delete step “{capturedTask.Title ?? capturedTask.Id}” and reconnect its dependents?"))
                            return;
                        ApplyStagedMutation(StagedPlanEditor.DeleteTask(_plan!.Group, capturedTask.Id));
                    };
                    contextMenu.Items.Add(deleteTask);
                }

                AppendDeveloperNormalizeMenuItem(contextMenu);

                MenuItem CreateInsertTaskMenuItem(bool insertAfter, bool isEnabled)
                {
                    var relativePosition = insertAfter ? "after" : "before";
                    var item = CreateThemedMenuItem(
                        _saveDefinitionEdit is not null
                            ? $"Insert step {relativePosition}"
                            : $"Insert step {relativePosition}…",
                        isEnabled);
                    item.ToolTip = isEnabled
                        ? ToolTipHelper.MakeThemedToolTip(
                            $"Add a new step {relativePosition} this still-pending step and update its future dependencies.")
                        : ToolTipHelper.MakeThemedToolTip(
                            "Only the unstarted downstream portion of a plan can be changed.");
                    item.Click += (_, _) =>
                    {
                        if (_saveDefinitionEdit is not null && _plan is not null)
                        {
                            var newId = StagedPlanEditor.NextTaskId(_plan.Group);
                            ApplyStagedMutation(
                                StagedPlanEditor.InsertTask(
                                    _plan.Group, capturedTask.Id, before: !insertAfter),
                                selectKind: "task",
                                selectId: newId,
                                beginEditingSelection: true);
                            return;
                        }

                        var owner = Window.GetWindow(border) ?? Application.Current.MainWindow;
                        var newTitle = SimpleInputDialog.Show(
                            owner,
                            $"Enter a short title for the step to insert {relativePosition} “{capturedTask.Title ?? capturedTask.Id}”:",
                            $"Insert Step {relativePosition}");
                        if (newTitle is null) return;
                        var newDescription = SimpleInputDialog.Show(
                            owner,
                            "Describe the result this step must produce:",
                            "Describe Inserted Step",
                            newTitle);
                        if (newDescription is null) return;
                        var updated = PlanStoreUpdater.ApplyTaskInserted(
                            durablePlan,
                            capturedTask.Id,
                            insertAfter,
                            newTitle,
                            newDescription);
                        if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                    };
                    return item;
                }
            }

            if (_saveDefinitionEdit is not null && border.ContextMenu is null)
            {
                var capturedTaskId = task.Id;
                var definitionEditable = IsTaskDefinitionEditable(capturedTaskId);
                var contextMenu = CreateThemedMenu();
                var insertBefore = CreateStagedInsertMenuItem(capturedTaskId, before: true);
                insertBefore.IsEnabled = definitionEditable;
                contextMenu.Items.Add(insertBefore);
                var insertAfter = CreateStagedInsertMenuItem(capturedTaskId, before: false);
                insertAfter.IsEnabled = definitionEditable;
                contextMenu.Items.Add(insertAfter);
                contextMenu.Items.Add(CreateThemedMenuSeparator());
                var assignToSubmenu2 = CreateAssignToSubmenu(capturedTaskId);
                if (assignToSubmenu2 is not null)
                {
                    contextMenu.Items.Add(assignToSubmenu2);
                    contextMenu.Items.Add(CreateThemedMenuSeparator());
                }
                var dependsOnSubmenu2 = CreateDependsOnSubmenu(capturedTaskId, group, levels, positions);
                if (dependsOnSubmenu2 is not null)
                {
                    contextMenu.Items.Add(dependsOnSubmenu2);
                    contextMenu.Items.Add(CreateThemedMenuSeparator());
                }
                contextMenu.Items.Add(CreateVerificationLevelSubmenu(capturedTaskId));
                contextMenu.Items.Add(CreateThemedMenuSeparator());
                var addValidation = new MenuItem { Header = "Add validation after this step", IsEnabled = definitionEditable };
                addValidation.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                addValidation.Click += (_, _) => ApplyStagedMutation(
                    StagedPlanEditor.AddValidationAfterTask(_plan!.Group, capturedTaskId),
                    selectKind: "validation",
                    beginEditingSelection: true);
                contextMenu.Items.Add(addValidation);
                var deleteTask = new MenuItem { Header = "Delete step", IsEnabled = group.Tasks.Count > 1 && definitionEditable };
                deleteTask.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
                deleteTask.Click += (_, _) =>
                {
                    if (!ConfirmDestructivePlanEdit($"Delete step {capturedTaskId} and reconnect its dependents?"))
                        return;
                    ApplyStagedMutation(StagedPlanEditor.DeleteTask(_plan!.Group, capturedTaskId));
                };
                contextMenu.Items.Add(deleteTask);
                AppendDeveloperNormalizeMenuItem(contextMenu);
                border.ContextMenu = contextMenu;
            }

            // Hover: show glow on all connectors entering/exiting this task, bring them forward,
            // and add a glow effect to the task node itself.
            var hoveredTaskId = task.Id;
            border.MouseEnter += (_, _) =>
            {
                border.Effect = TaskNodeGlowEffect();
                if (!connectorsByTask.TryGetValue(hoveredTaskId, out var connectors)) return;
                foreach (var cg in connectors)
                {
                    cg.RefreshGlowBrushes();
                    foreach (var el in cg.GlowElements) { el.Visibility = Visibility.Visible; Panel.SetZIndex(el, 3); }
                    foreach (var el in cg.MainElements) Panel.SetZIndex(el, 4);
                    foreach (var b  in cg.GateBadges)  b.Effect = TaskNodeGlowEffect();
                }
            };
            border.MouseLeave += (_, _) =>
            {
                border.Effect = null;
                if (!connectorsByTask.TryGetValue(hoveredTaskId, out var connectors)) return;
                foreach (var cg in connectors)
                {
                    foreach (var el in cg.GlowElements) { el.Visibility = Visibility.Hidden; Panel.SetZIndex(el, 0); }
                    foreach (var el in cg.MainElements) Panel.SetZIndex(el, 0);
                    foreach (var b  in cg.GateBadges)  b.Effect = null;
                }
            };
            Panel.SetZIndex(border, 20);
            canvas.Children.Add(border);

            var verificationOnly = PlanTaskExecutionPolicy.IsVerificationOnly(task);
            if (!verificationOnly)
            {
            var verificationLevel = PlanTaskVerificationLevel.Normalize(task.VerificationLevel);
            var isTaskComplete = borderColorKey == "PriorityLow";
            FrameworkElement verificationIndicatorContent;
            {
                FrameworkElement MakeCheckmarkIcon(bool applyCompletionOpacity = true)
                {
                    if (Application.Current.FindResource("TaskSucceededGeometry") is not Geometry succeededGeometry)
                    {
                        throw new ResourceReferenceKeyNotFoundException(
                            "TaskSucceededGeometry",
                            "TaskSucceededGeometry");
                    }

                    var checkPath = new Path
                    {
                        Data = succeededGeometry,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    checkPath.SetResourceReference(Shape.FillProperty, "PriorityHigh");

                    return new Viewbox
                    {
                        Width = 12 * _scaleFactor,
                        Height = 12 * _scaleFactor,
                        Stretch = Stretch.Uniform,
                        Child = checkPath,
                        Opacity = applyCompletionOpacity ? (isTaskComplete ? 1.0 : 0.50) : 1.0,
                    };
                }

                FrameworkElement CreateStandardVerificationIndicator()
                {
                    if (!isTaskComplete)
                    {
                        var hollowCheckGeometry = Geometry.Parse(
                            "M1802.875,626.375C1790.25,613.625,1773.5,607.25,1756.75,607.25C1740.125,607.25,1723.375,613.625,1710.75,626.375L857.25,1479.75 454.75,1077.25C429.25,1051.75,388,1051.75,362.5,1077.25L178.125,1261.75C152.625,1287.25,152.625,1328.5,178.125,1354L811.125,1987C830.25,2006.125,858.25,2010.875,881.75,2001.375L881.75,2001.375C889.625,1998.125,897,1993.375,903.375,1987L1987.375,903C2012.875,877.5,2012.875,836.25,1987.375,810.75z M1836,522.875L2095.125,782C2138.625,825.5,2138.625,896.125,2095.125,939.625L1199.125,1835.625 940,2094.75C896.375,2138.25,825.875,2138.25,782.375,2094.75L523.125,1835.625 78.375,1390.75C34.875,1347.25,34.875,1276.75,78.375,1233.125L337.5,974C381,930.5,451.625,930.5,495.125,974L861.125,1340 1678.375,522.875C1721.875,479.25,1792.5,479.25,1836,522.875z");
                        var hollowCheckPath = new Path
                        {
                            Data = hollowCheckGeometry,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        hollowCheckPath.SetResourceReference(Shape.FillProperty, "PriorityHigh");
                        return new Viewbox
                        {
                            Width = 12 * _scaleFactor,
                            Height = 12 * _scaleFactor,
                            Stretch = Stretch.Uniform,
                            Child = hollowCheckPath,
                            Opacity = 1.0,
                        };
                    }

                    return MakeCheckmarkIcon(applyCompletionOpacity: false);
                }

                verificationIndicatorContent = verificationLevel switch
                {
                    PlanTaskVerificationLevel.Lightweight => CreateLightweightVerificationIndicator(),
                    PlanTaskVerificationLevel.Enhanced => CreateEnhancedVerificationIndicator(),
                    _ => CreateStandardVerificationIndicator(),
                };
                verificationIndicatorContent.Margin = verificationLevel switch
                {
                    PlanTaskVerificationLevel.Lightweight or PlanTaskVerificationLevel.Enhanced => new Thickness(13 * _scaleFactor, 0, 0, 0),
                    _ => new Thickness(14 * _scaleFactor, 0, 0, 0),
                };

                FrameworkElement CreateLightweightVerificationIndicator()
                {
                    var indicator = new Ellipse
                    {
                        Width = 10 * _scaleFactor,
                        Height = 10 * _scaleFactor,
                        StrokeThickness = 1.5 * _scaleFactor,
                        Fill = Brushes.Transparent,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Opacity = isTaskComplete ? 1.0 : 0.50,
                    };
                    indicator.SetResourceReference(Shape.StrokeProperty, "PriorityHigh");
                    if (isTaskComplete)
                        indicator.SetResourceReference(Shape.FillProperty, "PriorityHigh");
                    return indicator;
                }

                FrameworkElement CreateEnhancedVerificationIndicator()
                {
                    if (!isTaskComplete)
                    {
                        var hollowStarGeometry = Geometry.Parse(
                            "M1177.75,338.9375C1160.75,338.6875,1149.3125,354.1875,1138.625,384.875L1133.75,399.1875 1024.4375,724.3125 996.625,806.5625 542.9375,807.0625 540.3125,807.1875C509.5,807.1875,486.5,813.5625,480.875,829.5625C475.25,845.625,486.3125,861.4375,512.0625,881.25L524.0625,890.4375 877.375,1157.6875 747.875,1591.5 747.25,1594.0625C738.375,1623.5625,737.875,1647.4375,751.625,1657.4375C765.375,1667.4375,783.6875,1661.375,810.0625,1642.4375L822.3125,1633.5625 1168.5,1380.0625 1168.8125,1380.4375 1171.1875,1378.6875 1553.5625,1640.75 1555.8125,1642.125C1581.1875,1659.5625,1603.75,1667.3125,1617.5,1657.3125C1631.25,1647.25,1631,1628,1621.0625,1597.0625L1616.375,1582.6875 1480.875,1175.5625 1481.3125,1175.4375 1475,1156.625 1511.8125,1128.6875 1835.3125,891.3125 1837.3125,889.6875C1862.1875,871.5,1876.9375,852.75,1872.0625,836.5L1870,833 1869.875,829.0625C1864.6875,812.875,1846.3125,807,1813.8125,806.6875L1798.75,806.625 1396.6875,806.5625 1369.625,806.1875 1369.625,806.125 1367.3125,806.125 1358.5,806 1267.9375,536.625 1219.375,391.3125 1218.4375,388.875C1213.5625,374.25,1207.8125,362,1201.125,353.375L1200.625,352.875 1200.5,352.6875C1193.875,344.0625,1186.25,339.0625,1177.75,338.9375z M1179.125,225.9375C1209.8125,225.9375,1237.625,238.375,1257.6875,258.5L1279.375,290.625 1300.75,354.6875 1344.3125,484.125 1366.0625,549.875 1366.4375,550 1423.6875,720.25 1435.625,720.4375 1438.8125,720.4375 1438.8125,720.4375 1475.5,721.0625 1604.6875,721.0625 1606.125,722.8125 1883.4375,724.5625 1892.75,724.625C1907.75,725.9375,1918.875,730,1933.125,736.25C1958.4375,749.1875,1978.8125,771.8125,1988.25,801C1997.6875,830.1875,1994.4375,860.4375,1981.5,885.75C1973.25,899,1965.6875,908.75,1955.5625,918.8125L1948.0625,924.3125 1737.25,1078.8125 1737.375,1080.625 1631.75,1158.125 1581.8125,1196 1590.3125,1221.5625 1589.75,1221.75 1645.5625,1389.4375 1645.1875,1390.0625 1675.8125,1480.375 1733.625,1654.0625 1735.625,1692.0625C1731.6875,1720.25,1717.0625,1746.875,1692.5625,1765.375C1668.0625,1783.875,1638.375,1790.625,1610.25,1786.6875C1602.625,1785.1875,1595.9375,1783.6875,1589.5625,1781.5625L1574.1875,1774.6875 1420.1875,1669.125 1348.5625,1619.625 1348.1875,1619.8125 1169.5625,1497.375 1166.25,1499.75 1165.875,1499.25 998.3125,1621.9375 997.375,1621.625 846.9375,1732.8125 801.6875,1765.875 784.0625,1773.875C777.6875,1776.3125,771,1778.375,763.5,1780.3125C735.4375,1785.125,705.625,1779.25,680.5625,1761.5625C655.5,1743.8125,640,1717.6875,635.1875,1689.625C634.3125,1681.9375,633.75,1675.0625,633.75,1668.375L635.875,1649.3125 651.5625,1596.875 708.75,1409.1875 707.875,1408.1875 770.8125,1197.4375 603.9375,1071.25 604,1070.3125 561,1038.75 384.6875,905.375 381.6875,902.0625C377.5625,896.75,373.5625,891,369.5,884.375C356.5625,859.0625,353.25,828.8125,362.6875,799.625C372.125,770.4375,392.5625,747.8125,417.875,734.875C432.0625,728.625,443.25,724.5625,458.1875,723.25L467.5,723.1875 733.8125,721.4375 734.0625,721.1875 932.625,720.9375 970.375,609.375 992.625,543.25 994.6875,542.5 1074.1875,302.125 1077.125,293.3125C1083,279.4375,1090.25,270.125,1100.5625,258.5C1120.6875,238.375,1148.4375,225.9375,1179.125,225.9375z");
                        var hollowStarPath = new Path
                        {
                            Data = hollowStarGeometry,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        hollowStarPath.SetResourceReference(Shape.FillProperty, "PriorityHigh");
                        return new Viewbox
                        {
                            Width = 14.3 * _scaleFactor,
                            Height = 14.3 * _scaleFactor,
                            Stretch = Stretch.Uniform,
                            Margin = new Thickness(-1.30 * _scaleFactor, -1.52 * _scaleFactor, 0, 0),
                            Child = hollowStarPath,
                            Opacity = 1.0,
                        };
                    }

                    var starGeometry = Geometry.Parse(
                        "M2373.875,421.375C2393.875,421.625,2411.875,433.5,2427.625,453.875L2427.875,454.25 " +
                        "2429.125,455.375C2444.875,475.875,2458.5,504.75,2470,539.375L2472.25,545.125 " +
                        "2587,888.5 2801,1525.25 2821.875,1525.625 2827.375,1525.625 2827.375,1525.625 " +
                        "2891.25,1526.625 3841.625,1526.75 3877.375,1526.875C3954.125,1527.625,3997.5,1541.5," +
                        "4009.75,1579.875L4010,1589.125 4014.875,1597.25C4026.5,1635.75,3991.5,1680.125," +
                        "3932.75,1723L3928,1727 3163.5,2288 3076.5,2354 3091.375,2398.5 3090.25,2398.875 " +
                        "3410.5,3361.125 3421.625,3395.125C3445.25,3468.125,3445.625,3513.75,3413.25,3537.5" +
                        "C3380.75,3561.125,3327.375,3542.75,3267.375,3501.625L3262.125,3498.375 " +
                        "2358.375,2878.875 2352.625,2883 2352,2882.125 1533.75,3481.25 " +
                        "1504.75,3502.25C1442.375,3547.125,1399.125,3561.375,1366.625,3537.75" +
                        "C1334.125,3514.125,1335.375,3457.75,1356.25,3388L1357.75,3382 " +
                        "1663.875,2356.5 828.75,1724.875 800.25,1703.125C739.5,1656.25,713.375,1619," +
                        "726.625,1581C740,1543.125,794.375,1528,867.125,1528L873.375,1527.75 " +
                        "1945.75,1526.5 2011.5,1332.25 2269.875,563.75 2281.375,530" +
                        "C2306.625,457.375,2333.625,420.75,2373.875,421.375z");
                    var starPath = new Path
                    {
                        Data = starGeometry,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    starPath.SetResourceReference(Shape.FillProperty, "PriorityHigh");
                    return new Viewbox
                    {
                        Width = 14.3 * _scaleFactor,
                        Height = 14.3 * _scaleFactor,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(-1.30 * _scaleFactor, -1.52 * _scaleFactor, 0, 0),
                        Child = starPath,
                        Opacity = 1.0,
                    };
                }
            }
            var verificationIndicator = new Border
            {
                Width = 40 * _scaleFactor,
                Height = 22 * _scaleFactor,
                CornerRadius = new CornerRadius(0, 11 * _scaleFactor, 11 * _scaleFactor, 0),
                BorderThickness = new Thickness(0, 1, 1, 1),
                Background = Brushes.Transparent, // initial; overwritten immediately below
                ToolTip = ToolTipHelper.MakeThemedToolTip(
                    $"{char.ToUpperInvariant(verificationLevel[0]) + verificationLevel[1..]} verification"),
                Child = verificationIndicatorContent,
            };
            var app = Application.Current;
            verificationIndicator.SetResourceReference(Border.BackgroundProperty, "CardSurface");
            SolidColorBrush? taskBorderBrush = app?.TryFindResource(borderColorKey) as SolidColorBrush;
            if (taskBorderBrush is not null)
                verificationIndicator.BorderBrush = new SolidColorBrush(taskBorderBrush.Color) { Opacity = 0.5 };
            else
                verificationIndicator.SetResourceReference(Border.BorderBrushProperty, "SubtleBorder");
            System.Windows.Automation.AutomationProperties.SetName(
                verificationIndicator,
                $"{task.Title ?? task.Id}: {verificationLevel} verification");
            verificationIndicator.Tag = $"verification:{task.Id}";
            WireSelectionClick(verificationIndicator);
            verificationIndicator.MouseEnter += (_, _) =>
            {
                verificationIndicator.Effect = TaskNodeGlowEffect();
            };
            verificationIndicator.MouseLeave += (_, _) =>
            {
                verificationIndicator.Effect = null;
            };
            if (_saveDefinitionEdit is not null)
            {
                var capturedTaskId = task.Id;
                var capturedVerificationLevel = verificationLevel;
                var verMenu = CreateThemedMenu();

                var levelHeader = CreateThemedMenuItem("Verification level:");
                levelHeader.IsEnabled = false;
                levelHeader.FontStyle = FontStyles.Italic;
                verMenu.Items.Add(levelHeader);
                verMenu.Items.Add(CreateThemedMenuSeparator());

                foreach (var (value, label) in new[]
                {
                    (PlanTaskVerificationLevel.Lightweight, "Lightweight"),
                    (PlanTaskVerificationLevel.Standard, "Standard"),
                    (PlanTaskVerificationLevel.Enhanced, "Enhanced"),
                })
                {
                    var captured = value;
                    var item = CreateThemedMenuItem(label);
                    item.IsCheckable = true;
                    item.IsChecked = capturedVerificationLevel == value;
                    item.Click += (_, _) =>
                    {
                        if (_plan is null || !IsTaskDefinitionEditable(capturedTaskId)) return;
                        ApplyStagedMutation(StagedPlanEditor.UpdateTaskVerificationLevel(
                            _plan.Group, capturedTaskId, captured), selectKind: "task", selectId: capturedTaskId);
                    };
                    verMenu.Items.Add(item);
                }
                verificationIndicator.ContextMenu = verMenu;
            }
            Canvas.SetLeft(verificationIndicator, position.X + NodeWidth - Math.Round(17.6 * _scaleFactor));
            Canvas.SetTop(verificationIndicator, position.Y);
            Panel.SetZIndex(verificationIndicator, 18);
            canvas.Children.Add(verificationIndicator);
            RegisterTaskDragOverlay(task.Id, verificationIndicator);
            } // end if (!verificationOnly)

            if (hasAgentAvatars)
            {
                var avatarPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    MaxWidth = NodeWidth - 8 * _scaleFactor,
                    ClipToBounds = false,
                };
                foreach (var assignment in task.AgentAssignments!.Take(1))
                {
                    var info = _resolveAgentAvatar!(assignment.AgentHandle);
                    var chip = CreateAgentAvatarChip(info, avatarChipSize, assignment.AgentHandle);
                    chip.Tag = $"agent:{assignment.AgentHandle}";
                    var capturedTaskId = task.Id;
                    var capturedChipAgentHandle = assignment.AgentHandle;
                    if (!_agentChipElements.TryGetValue(assignment.AgentHandle, out var chipList))
                    {
                        chipList = [];
                        _agentChipElements[assignment.AgentHandle] = chipList;
                    }
                    chipList.Add((Border)chip);
                    WireSelectionClick(chip);
                    if (_saveDefinitionEdit is not null &&
                        _plan is not null &&
                        _getAssignableAgents is not null &&
                        IsTaskDefinitionEditable(capturedTaskId))
                    {
                        chip.ContextMenu = CreateAgentChipAssignContextMenu(
                            capturedTaskId,
                            capturedChipAgentHandle);
                    }
                    chip.ToolTip = ToolTipHelper.MakeThemedToolTip(
                        $"{HumanizeAgentHandle(assignment.AgentHandle)} — {assignment.Role}");

                    // Track for cross-highlight
                    if (!avatarChipsByAgent.TryGetValue(assignment.AgentHandle, out var list))
                    {
                        list = [];
                        avatarChipsByAgent[assignment.AgentHandle] = list;
                    }

                    // Hover glow using agent accent color
                    var accentBrush = info?.Accent;
                    var glowColor = accentBrush is SolidColorBrush scb ? scb.Color : Colors.Gray;

                    // Wrap chip in a Grid with a glow-only backing element so the
                    // DropShadowEffect doesn't blur the avatar image content.
                    // The backing needs a solid fill for DropShadowEffect to cast from;
                    // the chip sits on top and covers it — only the glow extends beyond.
                    var chipBorder = (Border)chip;
                    var glowBacking = new Border
                    {
                        Width = chipBorder.Width + 6,
                        Height = chipBorder.Height + 6,
                        CornerRadius = chipBorder.CornerRadius,
                        Background = new SolidColorBrush(glowColor) { Opacity = 0.6 },
                        Margin = new Thickness(
                            chipBorder.Margin.Left - 3,
                            chipBorder.Margin.Top - 3,
                            chipBorder.Margin.Right - 3,
                            chipBorder.Margin.Bottom - 3),
                        Visibility = Visibility.Collapsed,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    var chipWrapper = new Grid();
                    chipWrapper.Children.Add(glowBacking);
                    chipWrapper.Children.Add(chip);

                    list.Add(glowBacking);

                    var capturedHandle = assignment.AgentHandle;
                    chip.MouseEnter += (_, _) =>
                    {
                        if (avatarChipsByAgent.TryGetValue(capturedHandle, out var siblings))
                        {
                            foreach (var sibling in siblings)
                            {
                                sibling.Visibility = Visibility.Visible;
                                sibling.Effect = new System.Windows.Media.Effects.DropShadowEffect
                                {
                                    Color = glowColor,
                                    ShadowDepth = 0,
                                    BlurRadius = 24,
                                    Opacity = 0.95,
                                };
                            }
                        }
                    };
                    chip.MouseLeave += (_, _) =>
                    {
                        if (avatarChipsByAgent.TryGetValue(capturedHandle, out var siblings))
                        {
                            foreach (var sibling in siblings)
                            {
                                sibling.Effect = null;
                                sibling.Visibility = Visibility.Collapsed;
                            }
                        }
                    };

                    avatarPanel.Children.Add(chipWrapper);
                }
                Canvas.SetLeft(avatarPanel, position.X + 4 * _scaleFactor);
                Canvas.SetTop(avatarPanel, position.Y - avatarChipSize);
                Panel.SetZIndex(avatarPanel, 35);
                canvas.Children.Add(avatarPanel);
                RegisterTaskDragOverlay(task.Id, avatarPanel);
            }

            // Show Verity Cross avatar on the right side while a task is being verified.
            if (activityState == PlanTaskActivityState.Verifying && _resolveAgentAvatar is not null)
            {
                var verityInfo = _resolveAgentAvatar(VerityCrossHandle);
                if (verityInfo is not null)
                {
                    var chipSize = Math.Round(BaseNodeHeight * 0.375 * _scaleFactor);
                    var verityChip = CreateAgentAvatarChip(verityInfo, chipSize, VerityCrossHandle);
                    verityChip.ToolTip = ToolTipHelper.MakeThemedToolTip("Verity Cross — Fact Checker (verifying)");
                    verityChip.Tag = $"agent:{VerityCrossHandle}";
                    if (!_agentChipElements.TryGetValue(VerityCrossHandle, out var chipList))
                    {
                        chipList = [];
                        _agentChipElements[VerityCrossHandle] = chipList;
                    }
                    chipList.Add((Border)verityChip);
                    WireSelectionClick(verityChip);
                    Canvas.SetLeft(verityChip, position.X + NodeWidth - chipSize - 4 * _scaleFactor);
                    Canvas.SetTop(verityChip, position.Y - chipSize);
                    Panel.SetZIndex(verityChip, 35);
                    canvas.Children.Add(verityChip);
                    RegisterTaskDragOverlay(task.Id, verityChip);
                }
            }

            // Recovery assessment is coordinator-owned. Mirror the fact-checker placement so
            // the upper-right avatar and right-side spinner describe the same active work.
            if (activityState == PlanTaskActivityState.Assessing && _resolveAgentAvatar is not null)
            {
                var coordinatorInfo = _resolveAgentAvatar(SquadCoordinatorHandle);
                if (coordinatorInfo is not null)
                {
                    var chipSize = Math.Round(BaseNodeHeight * 0.375 * _scaleFactor);
                    var coordinatorChip = CreateAgentAvatarChip(
                        coordinatorInfo, chipSize, SquadCoordinatorHandle);
                    coordinatorChip.ToolTip = ToolTipHelper.MakeThemedToolTip(
                        "Squad — Coordinator (assessing completed work)");
                    coordinatorChip.Tag = $"agent:{SquadCoordinatorHandle}";
                    if (!_agentChipElements.TryGetValue(SquadCoordinatorHandle, out var chipList))
                    {
                        chipList = [];
                        _agentChipElements[SquadCoordinatorHandle] = chipList;
                    }
                    chipList.Add((Border)coordinatorChip);
                    WireSelectionClick(coordinatorChip);
                    Canvas.SetLeft(coordinatorChip, position.X + NodeWidth - chipSize - 4 * _scaleFactor);
                    Canvas.SetTop(coordinatorChip, position.Y - chipSize);
                    Panel.SetZIndex(coordinatorChip, 35);
                    canvas.Children.Add(coordinatorChip);
                    RegisterTaskDragOverlay(task.Id, coordinatorChip);
                }
            }

            _borderByTask[task.Id] = border;

            // Task entry/exit approval stops. Root tasks have no meaningful entry boundary.
            // A leaf exit is omitted unless executable final validation work follows it.
            if (durablePlan is not null)
            {
                var capturedTaskForStop = task;
                var isRoot = PlanGateManager.IsRootTask(durablePlan, capturedTaskForStop.Id);
                var isLeaf = PlanGateManager.IsLeafTask(durablePlan, capturedTaskForStop.Id);

                if (!isRoot)
                {
                    var entryExecutionLocked = PlanApprovalControlLockPolicy.PlanHasExecutionContext(durablePlan) &&
                        PlanApprovalControlLockPolicy.IsTaskEntryLocked(durablePlan, capturedTaskForStop.Id);
                    var existingBeforeGate = FindTaskGateBefore(capturedTaskForStop.Id);
                    var beforeAnchor = TaskBeforeAnchor(capturedTaskForStop.Id);
                    var coveringBeforeGate = existingBeforeGate is null
                        ? durablePlan.ApprovalGates
                            .Where(gate => PlanGateVisualizationPolicy.CompletelyCovers(
                                durablePlan.Tasks, gate, capturedTaskForStop.DependsOn, [capturedTaskForStop.Id]))
                            .OrderByDescending(gate => gate.AfterTaskIds.Count + gate.BeforeTaskIds.Count)
                            .FirstOrDefault()
                        : null;
                    var collectivelyCoveredEntry = existingBeforeGate is null && coveringBeforeGate is null &&
                        PlanGateVisualizationPolicy.BoundaryIsCollectivelyCoveredByIncomingGates(
                            capturedTaskForStop.DependsOn, [capturedTaskForStop.Id],
                            durablePlan.ApprovalGates);
                    var collectiveEntryController = collectivelyCoveredEntry
                        ? durablePlan.ApprovalGates
                            .Where(candidate => capturedTaskForStop.DependsOn.Any(id =>
                                candidate.AfterTaskIds.Contains(id, StringComparer.Ordinal)) &&
                                candidate.BeforeTaskIds.Contains(capturedTaskForStop.Id, StringComparer.Ordinal))
                            .Select(ResolvePresentationAnchor)
                            .FirstOrDefault(anchor => anchor is not null)
                        : null;
                    var beforeEngaged = existingBeforeGate is not null || coveringBeforeGate is not null ||
                                        collectivelyCoveredEntry;
                    var controllingBeforeGate = existingBeforeGate ?? coveringBeforeGate;
                    var beforeIsPrimary = IsPrimary(controllingBeforeGate, beforeAnchor);
                    var beforeController = coveringBeforeGate is null ? null : ResolvePresentationAnchor(coveringBeforeGate);
                    var collectivelyUnresolvedEntry = collectivelyCoveredEntry && durablePlan.ApprovalGates.Any(candidate =>
                        IsUnresolvedApproval(candidate) &&
                        capturedTaskForStop.DependsOn.Any(id =>
                            candidate.AfterTaskIds.Contains(id, StringComparer.Ordinal)) &&
                        candidate.BeforeTaskIds.Contains(capturedTaskForStop.Id, StringComparer.Ordinal));
                    var beforeChangeActive = IsApprovalChangeActive(controllingBeforeGate);
                    var beforeVisual = PlanApprovalHistoricalPresentationPolicy.Resolve(
                        entryExecutionLocked,
                        controllingBeforeGate?.Status,
                        IsPrimary(controllingBeforeGate, beforeAnchor),
                        hasUnresolvedEquivalent: collectivelyUnresolvedEntry,
                        hasResolvedEquivalent: collectivelyCoveredEntry && !collectivelyUnresolvedEntry,
                        isApprovalChangeActive: beforeChangeActive);
                    var beforeApproved = beforeVisual == PlanApprovalControlVisualState.ApprovedCheck;
                    var beforeAwaiting = beforeVisual == PlanApprovalControlVisualState.AwaitingQuestion;
                    var beforeChanging = beforeVisual == PlanApprovalControlVisualState.ActiveChangeQuestion;
                    var beforeHasQuestion = beforeAwaiting || beforeChanging;
                    if (beforeVisual != PlanApprovalControlVisualState.Hidden)
                    {
                        var beforeStop = CreateApprovalStop(
                            beforeEngaged,
                            beforeApproved
                                ? BuildApprovalResolvedToolTip(controllingBeforeGate, "before this step began")
                            : beforeAwaiting
                                ? "Waiting for human approval before this step begins."
                            : entryExecutionLocked
                            ? PlanApprovalControlLockPolicy.LockedTooltip("Step entry")
                        : collectivelyCoveredEntry
                            ? "This step entry is covered by every incoming approval requirement."
                        : coveringBeforeGate is not null
                            ? beforeIsPrimary
                                ? "This step entry is the primary control for a larger approval requirement. Click to review; right-click to remove."
                                : "This step entry is covered by a larger approval requirement."
                            : beforeEngaged
                            ? beforeIsPrimary
                                ? "Human approval is required before this step begins. Click to review; right-click to remove."
                                : "This is an equivalent view of the approval boundary. Click to review; right-click to make this the primary control."
                            : "Require human approval before this step begins.",
                        entryExecutionLocked || onGatesChanged is null ? null : () =>
                        {
                            if (collectivelyCoveredEntry)
                            {
                                if (collectiveEntryController is not null)
                                    ShowCoveredGuidance(collectiveEntryController, ApprovalLabel(collectiveEntryController));
                                return;
                            }
                            if (coveringBeforeGate is not null)
                            {
                                var coveringUpdated = beforeIsPrimary &&
                                                      PlanGateManager.CanRemoveGate(durablePlan, coveringBeforeGate)
                                    ? PlanGateManager.RemoveGate(durablePlan, coveringBeforeGate.GateId)
                                    : PlanGateManager.SetPresentationAnchor(
                                        durablePlan, coveringBeforeGate.GateId, beforeAnchor);
                                if (!ReferenceEquals(coveringUpdated, durablePlan)) onGatesChanged(coveringUpdated);
                                return;
                            }
                            Plan updated;
                            if (existingBeforeGate is not null)
                            {
                                updated = beforeIsPrimary
                                    ? PlanGateManager.RemoveGate(durablePlan, existingBeforeGate.GateId)
                                    : PlanGateManager.SetPresentationAnchor(durablePlan, existingBeforeGate.GateId, beforeAnchor);
                            }
                            else
                            {
                                var taskTitle = capturedTaskForStop.Title ?? capturedTaskForStop.Id;
                                var defaultQ = $"Has {taskTitle} been reviewed and approved?";
                                if (_saveDefinitionEdit is not null)
                                {
                                    var stagedGatePlan = PlanGateManager.AddGateBefore(durablePlan, capturedTaskForStop.Id,
                                        $"Review before starting: {taskTitle}", question: defaultQ);
                                    var changedGate = stagedGatePlan.ApprovalGates.FirstOrDefault(gate =>
                                        durablePlan.ApprovalGates.FirstOrDefault(existing =>
                                            existing.GateId == gate.GateId) is not { } existing ||
                                        !Equals(existing, gate));
                                    if (changedGate is not null)
                                    {
                                        // Expanded gates retain their old anchor unless explicitly moved.
                                        stagedGatePlan = PlanGateManager.SetPresentationAnchor(
                                            stagedGatePlan, changedGate.GateId, beforeAnchor);
                                        ApplyStagedMutation(
                                            PendingDecomposePlanAdapter.FromPlan(stagedGatePlan).Group,
                                            selectKind: "humanapproval",
                                            selectId: changedGate.GateId,
                                            beginEditingSelection: true);
                                    }
                                    return;
                                }
                                updated = PlanGateManager.AddGateBefore(durablePlan, capturedTaskForStop.Id,
                                    $"Review before starting: {taskTitle}", question: defaultQ);
                                // Ensure the clicked octagon is primary (ExpandCompatiblePendingGate may not update PresentationAnchor)
                                var expandedBeforeGate = updated.ApprovalGates.FirstOrDefault(g =>
                                    PlanGateManager.CanEditGate(g) &&
                                    g.BeforeTaskIds.Contains(capturedTaskForStop.Id, StringComparer.Ordinal));
                                if (expandedBeforeGate is not null)
                                {
                                    var withAnchor = PlanGateManager.SetPresentationAnchor(updated, expandedBeforeGate.GateId, beforeAnchor);
                                    if (!ReferenceEquals(withAnchor, updated)) updated = withAnchor;
                                }
                            }
                            if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                        },
                            PlanGateVisualizationPolicy.ApprovalControlOpacity(
                                beforeEngaged, beforeIsPrimary, collectivelyCoveredEntry),
                            approved: beforeApproved,
                            awaitingApproval: beforeAwaiting,
                            approvalChangeActive: beforeChanging,
                            selectionAnchor: controllingBeforeGate?.GateId,
                            toggleActionLabel: beforeIsPrimary
                                ? "Remove this approval requirement"
                                : "Make this the primary approval control",
                            approveAction: beforeAwaiting && existingBeforeGate is not null && onApproveGate is not null
                                ? () => { onApproveGate(durablePlan, existingBeforeGate.GateId); }
                                : null,
                            unapproveAction: beforeApproved && existingBeforeGate is not null && onUnapproveGate is not null
                                ? () => { onUnapproveGate(durablePlan, existingBeforeGate.GateId); }
                                : null,
                            isDependent: PlanGateVisualizationPolicy.IsDependent(beforeEngaged, beforeIsPrimary, collectivelyCoveredEntry));
                        Canvas.SetLeft(beforeStop, position.X + (beforeApproved || beforeHasQuestion ? 4 : 6) * _scaleFactor);
                        Canvas.SetTop(beforeStop, position.Y + NodeHeight - (beforeApproved || beforeHasQuestion ? 22 : 20) * _scaleFactor);
                        Panel.SetZIndex(beforeStop, 25);
                        if (controllingBeforeGate is not null && beforeStop.ContextMenu is ContextMenu beforeMenu)
                        {
                            var blocksSubmenu = CreateApprovalBlocksSubmenu(controllingBeforeGate.GateId, group);
                            if (blocksSubmenu is not null)
                            {
                                beforeMenu.Items.Add(CreateThemedMenuSeparator());
                                beforeMenu.Items.Add(blocksSubmenu);
                            }
                        }
                        if (!beforeIsPrimary && controllingBeforeGate is not null && PlanGateManager.CanEditGate(controllingBeforeGate)
                            && !entryExecutionLocked && onGatesChanged is not null
                            && beforeStop.ContextMenu is ContextMenu beforeRemoveMenu)
                        {
                            var capturedBeforeRemoveId = controllingBeforeGate.GateId;
                            var removeBeforeItem = CreateThemedMenuItem("Remove this approval requirement");
                            removeBeforeItem.Click += (_, _) =>
                            {
                                var u = PlanGateManager.RemoveGate(durablePlan, capturedBeforeRemoveId);
                                if (!ReferenceEquals(u, durablePlan)) onGatesChanged(u);
                            };
                            beforeRemoveMenu.Items.Add(CreateThemedMenuSeparator());
                            beforeRemoveMenu.Items.Add(removeBeforeItem);
                        }
                        canvas.Children.Add(beforeStop);
                        RegisterTaskDragOverlay(task.Id, beforeStop);
                        approvalControlsByAnchor[beforeAnchor] = beforeStop;
                    }
                }

                var hasFinalValidationBoundary =
                    PlanGateManager.HasFinalValidationAfterTask(durablePlan, capturedTaskForStop.Id);
                var existingAfterGate = FindTaskGateAfter(capturedTaskForStop.Id);
                if (PlanGateVisualizationPolicy.ShouldRenderTaskExitApprovalControl(
                        isLeaf, hasFinalValidationBoundary, existingAfterGate is not null))
                {
                    var exitExecutionLocked = PlanApprovalControlLockPolicy.PlanHasExecutionContext(durablePlan) &&
                        PlanApprovalControlLockPolicy.IsTaskExitLocked(durablePlan, capturedTaskForStop.Id);
                    var afterAnchor = TaskAfterAnchor(capturedTaskForStop.Id);
                    var afterBoundary = DirectDependents(capturedTaskForStop.Id);
                    var lockedAllJoinGates = gates
                        .Select(allGate => FindDurableGate(
                            allGate.Dependencies,
                            allGate.Targets.Select(target => target.Id).ToArray()))
                        .Where(gate => gate is not null)
                        .Cast<PlanApprovalGate>()
                        .DistinctBy(gate => gate.GateId)
                        .ToArray();
                    var coveringAfterGate = existingAfterGate is null
                        ? durablePlan.ApprovalGates
                            .Where(gate => PlanGateVisualizationPolicy.CompletelyCovers(
                                durablePlan.Tasks, gate, [capturedTaskForStop.Id], afterBoundary))
                            .OrderByDescending(gate => gate.AfterTaskIds.Count + gate.BeforeTaskIds.Count)
                            .FirstOrDefault()
                        : null;
                    var collectivelyCoveredByAllJoins = existingAfterGate is null &&
                        PlanGateVisualizationPolicy.TaskExitIsCollectivelyCovered(
                            durablePlan.Tasks, capturedTaskForStop.Id, lockedAllJoinGates);
                    var collectiveController = collectivelyCoveredByAllJoins
                        ? lockedAllJoinGates
                            .Select(ResolvePresentationAnchor)
                            .FirstOrDefault(anchor => anchor is not null)
                        : null;
                    var afterEngaged = existingAfterGate is not null || coveringAfterGate is not null ||
                                       collectivelyCoveredByAllJoins;
                    var afterIsPrimary = IsPrimary(existingAfterGate, afterAnchor) ||
                        (existingAfterGate is null && IsPrimary(coveringAfterGate, afterAnchor));
                    var afterController = coveringAfterGate is null ? null : ResolvePresentationAnchor(coveringAfterGate);
                    var controllingAfterGate = existingAfterGate ?? coveringAfterGate;
                    var afterRemovalBlocked = afterIsPrimary &&
                        PlanGateManager.IsRequiredVerificationProofGate(durablePlan, controllingAfterGate);
                    var collectivelyUnresolvedExit = collectivelyCoveredByAllJoins &&
                        lockedAllJoinGates.Any(IsUnresolvedApproval);
                    var afterChangeActive = IsApprovalChangeActive(controllingAfterGate);
                    var afterVisual = PlanApprovalHistoricalPresentationPolicy.Resolve(
                        exitExecutionLocked,
                        controllingAfterGate?.Status,
                        IsPrimary(controllingAfterGate, afterAnchor),
                        hasUnresolvedEquivalent: collectivelyUnresolvedExit,
                        hasResolvedEquivalent: collectivelyCoveredByAllJoins && !collectivelyUnresolvedExit,
                        isApprovalChangeActive: afterChangeActive);
                    var afterApproved = afterVisual == PlanApprovalControlVisualState.ApprovedCheck;
                    var afterAwaiting = afterVisual == PlanApprovalControlVisualState.AwaitingQuestion;
                    var afterChanging = afterVisual == PlanApprovalControlVisualState.ActiveChangeQuestion;
                    var afterHasQuestion = afterAwaiting || afterChanging;
                    if (afterVisual != PlanApprovalControlVisualState.Hidden)
                    {
                        var afterStop = CreateApprovalStop(
                            afterEngaged,
                            afterApproved
                                ? BuildApprovalResolvedToolTip(controllingAfterGate, "after this step completed")
                            : afterAwaiting
                                ? "Waiting for human approval before the final validation begins."
                            : exitExecutionLocked
                            ? PlanApprovalControlLockPolicy.LockedTooltip("Step exit")
                        : collectivelyCoveredByAllJoins
                            ? "This step exit is covered by its enabled ALL approval requirements."
                        : coveringAfterGate is not null
                            ? afterIsPrimary
                                ? "This step exit is the primary control for a larger approval requirement. Click to review; right-click to remove."
                                : "This step exit is covered by a larger approval requirement."
                            : afterEngaged
                            ? afterIsPrimary
                                ? afterRemovalBlocked
                                    ? "This checkpoint is the verification step's only proof contract. Click to review it."
                                    : "Human approval is required after this step completes. Click to review; right-click to remove."
                                : "This is an equivalent view of the approval boundary. Click to review; right-click to make this the primary control."
                            : "Require human approval after this step completes.",
                        exitExecutionLocked || onGatesChanged is null || afterRemovalBlocked ? null : () =>
                        {
                            if (collectivelyCoveredByAllJoins)
                            {
                                if (collectiveController is not null)
                                    ShowCoveredGuidance(collectiveController, ApprovalLabel(collectiveController));
                                return;
                            }
                            if (coveringAfterGate is not null)
                            {
                                var coveringUpdated = afterIsPrimary &&
                                                      PlanGateManager.CanRemoveGate(durablePlan, coveringAfterGate)
                                    ? PlanGateManager.RemoveGate(durablePlan, coveringAfterGate.GateId)
                                    : PlanGateManager.SetPresentationAnchor(
                                        durablePlan, coveringAfterGate.GateId, afterAnchor);
                                if (!ReferenceEquals(coveringUpdated, durablePlan)) onGatesChanged(coveringUpdated);
                                return;
                            }
                            Plan updated;
                            if (existingAfterGate is not null)
                            {
                                updated = afterIsPrimary
                                    ? PlanGateManager.RemoveGate(durablePlan, existingAfterGate.GateId)
                                    : PlanGateManager.SetPresentationAnchor(durablePlan, existingAfterGate.GateId, afterAnchor);
                            }
                            else
                            {
                                var taskTitle = capturedTaskForStop.Title ?? capturedTaskForStop.Id;
                                var defaultQ = $"Has {taskTitle} been reviewed and approved?";
                                if (_saveDefinitionEdit is not null)
                                {
                                    var stagedGatePlan = PlanGateManager.AddGateAfter(durablePlan, capturedTaskForStop.Id,
                                        $"Review after completing: {taskTitle}", question: defaultQ);
                                    var changedGate = stagedGatePlan.ApprovalGates.FirstOrDefault(gate =>
                                        durablePlan.ApprovalGates.FirstOrDefault(existing =>
                                            existing.GateId == gate.GateId) is not { } existing ||
                                        !Equals(existing, gate));
                                    if (changedGate is not null)
                                    {
                                        // Expanded gates retain their old anchor unless explicitly moved.
                                        stagedGatePlan = PlanGateManager.SetPresentationAnchor(
                                            stagedGatePlan, changedGate.GateId, afterAnchor);
                                        ApplyStagedMutation(
                                            PendingDecomposePlanAdapter.FromPlan(stagedGatePlan).Group,
                                            selectKind: "humanapproval",
                                            selectId: changedGate.GateId,
                                            beginEditingSelection: true);
                                    }
                                    return;
                                }
                                updated = PlanGateManager.AddGateAfter(durablePlan, capturedTaskForStop.Id,
                                    $"Review after completing: {taskTitle}", question: defaultQ);
                                // Ensure the clicked octagon is primary (ExpandCompatiblePendingGate may not update PresentationAnchor)
                                var expandedAfterGate = updated.ApprovalGates.FirstOrDefault(g =>
                                    PlanGateManager.CanEditGate(g) &&
                                    g.AfterTaskIds.Contains(capturedTaskForStop.Id, StringComparer.Ordinal));
                                if (expandedAfterGate is not null)
                                {
                                    var withAnchor = PlanGateManager.SetPresentationAnchor(updated, expandedAfterGate.GateId, afterAnchor);
                                    if (!ReferenceEquals(withAnchor, updated)) updated = withAnchor;
                                }
                            }
                            if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                        },
                            PlanGateVisualizationPolicy.ApprovalControlOpacity(
                                afterEngaged, afterIsPrimary, collectivelyCoveredByAllJoins),
                            approved: afterApproved,
                            awaitingApproval: afterAwaiting,
                            approvalChangeActive: afterChanging,
                            selectionAnchor: controllingAfterGate?.GateId,
                            toggleActionLabel: afterIsPrimary
                                ? "Remove this approval requirement"
                                : "Make this the primary approval control",
                            approveAction: afterAwaiting && existingAfterGate is not null && onApproveGate is not null
                                ? () => { onApproveGate(durablePlan, existingAfterGate.GateId); }
                                : null,
                            unapproveAction: afterApproved && existingAfterGate is not null && onUnapproveGate is not null
                                ? () => { onUnapproveGate(durablePlan, existingAfterGate.GateId); }
                                : null,
                            isDependent: PlanGateVisualizationPolicy.IsDependent(afterEngaged, afterIsPrimary, collectivelyCoveredByAllJoins));
                        Canvas.SetLeft(afterStop, position.X + NodeWidth - (afterApproved || afterHasQuestion ? 24 : 22) * _scaleFactor);
                        Canvas.SetTop(afterStop, position.Y + NodeHeight - (afterApproved || afterHasQuestion ? 22 : 20) * _scaleFactor);
                        Panel.SetZIndex(afterStop, 25);
                        if (controllingAfterGate is not null && afterStop.ContextMenu is ContextMenu afterMenu)
                        {
                            var blocksSubmenu = CreateApprovalBlocksSubmenu(controllingAfterGate.GateId, group);
                            if (blocksSubmenu is not null)
                            {
                                afterMenu.Items.Add(CreateThemedMenuSeparator());
                                afterMenu.Items.Add(blocksSubmenu);
                            }
                        }
                        if (!afterIsPrimary && controllingAfterGate is not null &&
                            PlanGateManager.CanRemoveGate(durablePlan, controllingAfterGate)
                            && !exitExecutionLocked && onGatesChanged is not null
                            && afterStop.ContextMenu is ContextMenu afterRemoveMenu)
                        {
                            var capturedAfterRemoveId = controllingAfterGate.GateId;
                            var removeAfterItem = CreateThemedMenuItem("Remove this approval requirement");
                            removeAfterItem.Click += (_, _) =>
                            {
                                var u = PlanGateManager.RemoveGate(durablePlan, capturedAfterRemoveId);
                                if (!ReferenceEquals(u, durablePlan)) onGatesChanged(u);
                            };
                            afterRemoveMenu.Items.Add(CreateThemedMenuSeparator());
                            afterRemoveMenu.Items.Add(removeAfterItem);
                        }
                        canvas.Children.Add(afterStop);
                        RegisterTaskDragOverlay(task.Id, afterStop);
                        approvalControlsByAnchor[afterAnchor] = afterStop;
                    }
                }
            }
            else
            {
                // Snapshot-only fixtures are intentionally non-editable, but still show the
                // approval affordance so the preview accurately represents the finished UI.
                var isRoot = task.DependsOn.Count == 0;
                var directDependents = group.Tasks
                    .Where(candidate => candidate.DependsOn.Contains(task.Id, StringComparer.Ordinal))
                    .Select(candidate => candidate.Id)
                    .ToArray();
                var isLeaf = directDependents.Length == 0;

                if (!isRoot)
                {
                    var beforeEngaged = FindDisplayedGate(task.DependsOn, [task.Id]) is not null;
                    var beforeStop = CreateApprovalStop(
                        beforeEngaged,
                        beforeEngaged
                            ? "Preview: human approval is required before this step."
                            : "Preview: this stop controls approval before the step.",
                        null);
                    Canvas.SetLeft(beforeStop, position.X + 6 * _scaleFactor);
                    Canvas.SetTop(beforeStop, position.Y + NodeHeight - 20 * _scaleFactor);
                    Panel.SetZIndex(beforeStop, 25);
                    canvas.Children.Add(beforeStop);
                    RegisterTaskDragOverlay(task.Id, beforeStop);
                }

                if (!isLeaf)
                {
                    var afterEngaged = FindDisplayedGate([task.Id], directDependents) is not null;
                    var afterStop = CreateApprovalStop(
                        afterEngaged,
                        afterEngaged
                            ? "Preview: human approval is required after this step."
                            : "Preview: this stop controls approval after the step.",
                        null);
                    Canvas.SetLeft(afterStop, position.X + NodeWidth - 22 * _scaleFactor);
                    Canvas.SetTop(afterStop, position.Y + NodeHeight - 20 * _scaleFactor);
                    Panel.SetZIndex(afterStop, 25);
                    canvas.Children.Add(afterStop);
                    RegisterTaskDragOverlay(task.Id, afterStop);
                }
            }
        }

        // Wire connector hover: highlight the connector, raise its Z, and glow the endpoint task nodes.
        var allConnectorGroups = connectorsByTask.Values.SelectMany(l => l).Distinct().ToList();
        foreach (var cg in allConnectorGroups)
        {
            var capturedCg = cg;
            foreach (var el in capturedCg.MainElements)
            {
                el.MouseEnter += (_, _) =>
                {
                    capturedCg.RefreshGlowBrushes();
                    foreach (var g in capturedCg.GlowElements) { g.Visibility = Visibility.Visible; Panel.SetZIndex(g, 3); }
                    foreach (var m in capturedCg.MainElements) Panel.SetZIndex(m, 4);
                    foreach (var tid in capturedCg.TaskIds)
                        if (_borderByTask.TryGetValue(tid, out var b)) b.Effect = TaskNodeGlowEffect();
                    foreach (var gb in capturedCg.GateBadges) gb.Effect = TaskNodeGlowEffect();
                };
                el.MouseLeave += (_, _) =>
                {
                    foreach (var g in capturedCg.GlowElements) { g.Visibility = Visibility.Hidden; Panel.SetZIndex(g, 0); }
                    foreach (var m in capturedCg.MainElements) Panel.SetZIndex(m, 0);
                    foreach (var tid in capturedCg.TaskIds)
                        if (_borderByTask.TryGetValue(tid, out var b)) b.Effect = null;
                    foreach (var gb in capturedCg.GateBadges) gb.Effect = null;
                };
            }
        }

        // Wire badge hover: highlight all connectors entering/exiting this ALL gate,
        // raise their Z, and glow their endpoint task nodes.
        foreach (var (badge, cgs) in _deferredBadgeHovers)
        {
            var capturedBadge = badge;
            var capturedCgs   = cgs;
            capturedBadge.MouseEnter += (_, _) =>
            {
                capturedBadge.Effect = TaskNodeGlowEffect();
                foreach (var cg in capturedCgs)
                {
                    cg.RefreshGlowBrushes();
                    foreach (var g in cg.GlowElements) { g.Visibility = Visibility.Visible; Panel.SetZIndex(g, 3); }
                    foreach (var m in cg.MainElements) Panel.SetZIndex(m, 4);
                    foreach (var tid in cg.TaskIds)
                        if (_borderByTask.TryGetValue(tid, out var b)) b.Effect = TaskNodeGlowEffect();
                }
            };
            capturedBadge.MouseLeave += (_, _) =>
            {
                capturedBadge.Effect = null;
                foreach (var cg in capturedCgs)
                {
                    foreach (var g in cg.GlowElements) { g.Visibility = Visibility.Hidden; Panel.SetZIndex(g, 0); }
                    foreach (var m in cg.MainElements) Panel.SetZIndex(m, 0);
                    foreach (var tid in cg.TaskIds)
                        if (_borderByTask.TryGetValue(tid, out var b)) b.Effect = null;
                }
            };
        }

        // Wire validation-shield hover: glow prerequisite and blocked task nodes.
        foreach (var (shieldRow, afterIds, beforeIds) in _deferredShieldHovers)
        {
            var capturedRow = shieldRow;
            var capturedAfter = afterIds;
            var capturedBefore = beforeIds;
            capturedRow.MouseEnter += (_, _) =>
            {
                foreach (var tid in capturedAfter)
                    if (_borderByTask.TryGetValue(tid, out var b)) b.Effect = TaskNodeGlowEffect();
                foreach (var tid in capturedBefore)
                    if (_borderByTask.TryGetValue(tid, out var b)) b.Effect = TaskNodeGlowEffect();
            };
            capturedRow.MouseLeave += (_, _) =>
            {
                foreach (var tid in capturedAfter)
                    if (_borderByTask.TryGetValue(tid, out var b)) b.Effect = null;
                foreach (var tid in capturedBefore)
                    if (_borderByTask.TryGetValue(tid, out var b)) b.Effect = null;
            };
        }

        // Kick off any pending dependency-edit animation after the new layout is applied.
        if (_pendingAnimation is { } animData)
        {
            var capturedBorderByTask = _borderByTask;
            var capturedSeq          = currentBuildSeq;
            Dispatcher.BeginInvoke(() =>
            {
                // If a newer build has run since we queued this callback, skip — our elements
                // are no longer in the visual tree and _pendingAnimation is still valid for the
                // newer build to consume.
                if (_buildSeq != capturedSeq) return;
                _pendingAnimation = null;
                foreach (var animTaskId in animData.AffectedTaskIds)
                {
                    if (!capturedBorderByTask.TryGetValue(animTaskId, out var animBorder)) continue;
                    if (!animData.OldPositions.TryGetValue(animTaskId, out var oldPos)) continue;
                    var newLeft = Canvas.GetLeft(animBorder);
                    var newTop  = Canvas.GetTop(animBorder);
                    var deltaX = oldPos.X - newLeft;
                    var deltaY = oldPos.Y - newTop;
                    if (Math.Abs(deltaX) < 1 && Math.Abs(deltaY) < 1) continue;
                    try
                    {
                        var transform = new TranslateTransform(deltaX, deltaY);
                        animBorder.RenderTransform = transform;
                        var duration = new Duration(TimeSpan.FromMilliseconds(350));
                        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                        var animX = new DoubleAnimation(deltaX, 0, duration) { EasingFunction = ease };
                        var animY = new DoubleAnimation(deltaY, 0, duration) { EasingFunction = ease };
                        animX.Completed += (_, _) => animBorder.RenderTransform = null;
                        transform.BeginAnimation(TranslateTransform.XProperty, animX);
                        transform.BeginAnimation(TranslateTransform.YProperty, animY);
                    }
                    catch (Exception ex)
                    {
                        SquadDashTrace.Write("PlanViewer", $"Dependency animation failed: {ex.Message}");
                        animBorder.RenderTransform = null;
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        canvas.Width  = Math.Max(
            positions.Values.Max(point => point.X) + NodeWidth + 34 * _scaleFactor,
            validationRailRight);
        canvas.Height = Math.Max(
            positions.Values.Max(point => point.Y) + NodeHeight,
            Math.Max(
                validationBottom,
                allClusterFootprints.Select(footprint => footprint.Bottom).DefaultIfEmpty(0).Max()));
        _canvasContentWidth = canvas.Width;
        _canvasContentHeight = canvas.Height;
        ResolveAutomaticScrollTarget(durablePlan, levels, positions, canvas.Margin.Left);

        // BuildContent replaces both the graph and inspector visual trees. Restore the logical
        // selection and editor after every rebuild so inserting a new definition behaves exactly
        // like selecting it and choosing its explicit Edit command.
        ApplySelectionVisual();
        RefreshDetailPanel();
        FocusPrimaryInspectorEditorIfRequested(currentBuildSeq);

    }

    private PlanTaskCorrectionActivityState ResolveTaskCorrectionActivity(string taskId)
    {
        if (_resolveTaskCorrectionActivity is not null)
            return _resolveTaskCorrectionActivity(taskId);
        if (_taskCorrectionStates.TryGetValue(taskId, out var liveState))
            return liveState;
        return PlanTaskCorrectionActivityState.None;
    }

    private void ShowPlanPreflightRecovery(
        PlanPreflightBlockedException exception,
        WrapPanel actionsPanel,
        ContentControl recoveryHost,
        Func<Task<bool>> retry)
    {
        _preflightPollTimer?.Stop();
        var content = PlanPreflightRecoveryContent.From(exception);
        actionsPanel.IsEnabled = false;
        actionsPanel.Visibility = Visibility.Collapsed;

        var stack = new StackPanel();
        var title = new TextBlock { Text = content.Title, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "PlanPreflightWarningText");
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeNormal");
        stack.Children.Add(title);

        var summary = new TextBlock
        {
            Text = content.Summary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 6),
        };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "PlanPreflightWarningText");
        summary.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        stack.Children.Add(summary);

        var files = new TextBlock
        {
            Text = content.ChangedFilesSummary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        files.SetResourceReference(TextBlock.ForegroundProperty, "PlanPreflightWarningText");
        files.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        files.Visibility = string.IsNullOrWhiteSpace(content.ChangedFilesSummary)
            ? Visibility.Collapsed
            : Visibility.Visible;
        stack.Children.Add(files);

        var detailText = new TextBlock
        {
            Text = content.TechnicalDetails,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 4, 0, 6),
        };
        detailText.SetResourceReference(TextBlock.ForegroundProperty, "PlanPreflightWarningText");
        var details = new Expander
        {
            Header = "Technical details",
            Content = detailText,
            Margin = new Thickness(0, 0, 0, 6),
        };
        details.SetResourceReference(Expander.ForegroundProperty, "PlanPreflightWarningText");
        if (TryFindResource("ThemedExpanderStyle") is Style expanderStyle)
            details.Style = expanderStyle;
        stack.Children.Add(details);

        var readiness = new TextBlock
        {
            Text = content.RecoveryGuidance,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        readiness.SetResourceReference(TextBlock.ForegroundProperty, "PlanPreflightWarningText");
        readiness.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        stack.Children.Add(readiness);

        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        var viewButton = TranscriptQuickReplyFactory.CreateButton("View Changes", _quickReplyFontSize);
        var copyButton = new CopyButton
        {
            CopyTooltip = "Copy details",
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(10, 4, 10, 4),
            MinHeight = 28,
            Cursor = Cursors.Hand,
            FontSize = _quickReplyFontSize,
        };
        if (TryFindResource("QuickReplyButtonStyle") is Style copyButtonStyle)
            copyButton.Style = copyButtonStyle;
        copyButton.SetResourceReference(Button.BackgroundProperty, "QuickReplySurface");
        copyButton.SetResourceReference(Button.ForegroundProperty, "QuickReplyText");
        copyButton.SetResourceReference(Button.BorderBrushProperty, "QuickReplyBorder");
        var retryButton = TranscriptQuickReplyFactory.CreateButton(
            exception.RequiresRepositoryInitialization
                ? "Initialize repository and start plan"
                : "Retry",
            _quickReplyFontSize);
        var dismissButton = TranscriptQuickReplyFactory.CreateButton("Keep Plan Pending", _quickReplyFontSize);
        if (!exception.RequiresRepositoryInitialization)
            buttons.Children.Add(viewButton);
        buttons.Children.Add(copyButton);
        buttons.Children.Add(retryButton);
        buttons.Children.Add(dismissButton);
        stack.Children.Add(buttons);

        var card = new Border
        {
            Child = stack,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        card.SetResourceReference(Border.BackgroundProperty, "PlanPreflightWarningSurface");
        card.SetResourceReference(Border.BorderBrushProperty, "PlanPreflightWarningBorder");
        recoveryHost.Content = card;
        recoveryHost.Visibility = Visibility.Visible;

        viewButton.IsEnabled = _viewPreflightChanges is not null;
        viewButton.Click += (_, _) =>
        {
            if (_viewPreflightChanges is not null)
                _ = _viewPreflightChanges(exception);
        };
        copyButton.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(content.ClipboardText);
                readiness.Text = "Details copied to the clipboard.";
            }
            catch
            {
                readiness.Text = "The clipboard is busy. Try Copy Details again.";
            }
        };
        dismissButton.Click += (_, _) =>
        {
            _preflightPollTimer?.Stop();
            recoveryHost.Content = null;
            recoveryHost.Visibility = Visibility.Collapsed;
            actionsPanel.IsEnabled = true;
            actionsPanel.Visibility = Visibility.Visible;
        };
        retryButton.Click += async (_, _) =>
        {
            buttons.IsEnabled = false;
            readiness.Text = exception.RequiresRepositoryInitialization
                ? "Initializing the repository…"
                : "Checking the workspace and retrying…";
            try
            {
                if (await retry()) return;
                buttons.IsEnabled = true;
                readiness.Text = "The plan did not start. Review the workspace and retry.";
            }
            catch (PlanPreflightBlockedException blocked)
            {
                ShowPlanPreflightRecovery(blocked, actionsPanel, recoveryHost, retry);
            }
            catch (Exception ex)
            {
                buttons.IsEnabled = true;
                readiness.Text = "Retry failed. Review the error details and try again.";
                SquadDashTrace.Write(TraceCategory.General, $"Plan viewer retry failed: {ex}");
                ShowActionStatus("Retry failed", ex.Message);
            }
        };

        if (_isPreflightWorkspaceClean is not null && !exception.RequiresRepositoryInitialization)
        {
            var pollInFlight = false;
            _preflightPollTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5),
            };
            _preflightPollTimer.Tick += async (_, _) =>
            {
                if (pollInFlight) return;
                pollInFlight = true;
                try
                {
                    IReadOnlyList<string>? currentDirtyPaths = null;
                    if (_getPreflightDirtyPaths is not null)
                    {
                        currentDirtyPaths = await _getPreflightDirtyPaths();
                        if (currentDirtyPaths is not null)
                        {
                            var refreshedContent = PlanPreflightRecoveryContent.TryRefreshFromDirtyPaths(
                                exception,
                                currentDirtyPaths,
                                PlanPreflightRecoveryContent.From);
                            if (refreshedContent is not null)
                            {
                                content = refreshedContent;
                                summary.Text = content.Summary;
                                files.Text = content.ChangedFilesSummary;
                                files.Visibility = string.IsNullOrWhiteSpace(content.ChangedFilesSummary)
                                    ? Visibility.Collapsed
                                    : Visibility.Visible;
                                detailText.Text = content.TechnicalDetails;
                            }
                        }
                    }
                    var clean = currentDirtyPaths is not null
                        ? currentDirtyPaths.Count == 0
                        : await _isPreflightWorkspaceClean();
                    if (!clean) return;
                    _preflightPollTimer?.Stop();
                    readiness.Text = "Workspace is clean. Retry is ready.";
                    retryButton.FontWeight = FontWeights.SemiBold;
                }
                catch { /* Leave the card unchanged when the readiness probe fails. */ }
                finally { pollInFlight = false; }
            };
            _preflightPollTimer.Start();
        }
    }

    /// <summary>
    /// Rebuilds the viewer content against a newly persisted immutable plan while preserving the
    /// existing window, location, size, focus, and owner. Rebuilding on this window also ensures
    /// every interaction handler targets the visible viewer and does not create a hidden WPF window.
    /// </summary>
    private void RebuildPreservingScroll(PendingDecomposePlan plan, Plan? durablePlan) =>
        RebuildPreservingScroll(plan, durablePlan, autoScrollForPlanProgress: false);

    private void RebuildPreservingScroll(
        PendingDecomposePlan plan,
        Plan? durablePlan,
        bool autoScrollForPlanProgress)
    {
        _autoScrollForPlanProgressPending |= autoScrollForPlanProgress;
        // Defer the content rebuild to the next dispatcher cycle so any in-flight mouse/keyboard
        // event (e.g. ButtonBase.OnMouseLeftButtonDown calling Focus()) fully completes before
        // _contentHolder.Child is replaced. Replacing the child mid-event detaches elements from
        // the visual tree while WPF's input system still holds references to them, which causes
        // a NullReferenceException inside HwndKeyboardInputProvider.AcquireFocus.
        Dispatcher.BeginInvoke(() =>
        {
            if (_isViewerClosed)
                return;
            if (_viewportReady && _graphScroll is not null)
                _viewportState = new PlanVisualizerViewportState(
                    _zoomFactor, _graphScroll.HorizontalOffset, _graphScroll.VerticalOffset);
            var viewport = _viewportState;
            BuildContent(plan, durablePlan);
            var capturedScroll = _graphScroll;
            Dispatcher.BeginInvoke(() =>
            {
                if (_isViewerClosed || !_initialLayoutApplied ||
                    !ReferenceEquals(_graphScroll, capturedScroll))
                    return;
                RestoreGraphViewport(viewport);
                if (_autoScrollForPlanProgressPending &&
                    PlanViewerAutoScrollPolicy.IsInteractionQuiet(_lastViewerInteractionUtc, DateTime.UtcNow))
                    EnsureZoomedAutomaticScrollTargetVisible();
                _autoScrollForPlanProgressPending = false;
                _viewportReady = true;
                QueueGraphViewportSave();
            }, System.Windows.Threading.DispatcherPriority.Render);
        }, System.Windows.Threading.DispatcherPriority.Normal);
    }

    internal void RefreshPlan(PendingDecomposePlan plan, Plan durablePlan)
    {
        var displayedDurablePlan = ApplyCommittedColumnReorder(durablePlan);
        var displayedPlan = _lastCommittedColumnOrder is null
            ? plan
            : PendingDecomposePlanAdapter.FromPlan(displayedDurablePlan);
        _editHistory?.ReplaceCurrent(displayedPlan.Group);
        RebuildPreservingScroll(displayedPlan, displayedDurablePlan);
        Title = $"Plan — {displayedDurablePlan.DisplayName}";
    }

    private bool ApplyStagedMutation(
        DecomposedTaskGroup candidate,
        bool recordHistory = true,
        string? selectKind = null,
        string? selectId = null,
        bool beginEditingSelection = false,
        bool preserveReview = false,
        string? failurePrefix = null,
        bool showFailureDialog = true)
    {
        _lastStagedMutationError = null;
        if (_saveDefinitionEdit is null || _plan is null)
        {
            _lastStagedMutationError = "The plan is no longer available for editing.";
            return false;
        }
        if (!StagedPlanEditor.TryValidate(candidate, out var normalized, out var error) || normalized is null)
        {
            _lastStagedMutationError =
                (failurePrefix ?? string.Empty) + (error ?? "The edit would make the plan invalid.");
            if (showFailureDialog)
                UIErrorHelper.ShowError("Edit Plan", _lastStagedMutationError, this);
            return false;
        }
        if (_durablePlan is not null &&
            _durablePlan.LifecycleStatus != PlanLifecycleStatus.Staged &&
            !StagedPlanEditor.TryValidateUnlockedChanges(_durablePlan, normalized, out error))
        {
            _lastStagedMutationError =
                (failurePrefix ?? string.Empty) + (error ??
                    "The edit would change work that has already started.");
            if (showFailureDialog)
                UIErrorHelper.ShowError("Edit Plan", _lastStagedMutationError, this);
            return false;
        }
        try
        {
            var result = _saveDefinitionEdit(normalized);
            var saved = result.DisplayedPlan;
            if (recordHistory)
                _editHistory?.Record(normalized);
            _editHistory?.ReplaceCurrent(saved.Group);
            _plan = saved;
            _durablePlan = result.DurablePlan;
            if (!preserveReview)
            {
                _reviewCallout?.Close();
                _reviewCallout = null;
                _review = null;
                _reviewIndex = -1;
            }
            _isInspectorEditing = beginEditingSelection;
            if (beginEditingSelection)
                _focusPrimaryEditorAfterRebuild = true;

            if (selectKind is not null)
            {
                selectId ??= selectKind switch
                {
                    "task" => saved.Group.Tasks.LastOrDefault()?.Id,
                    "validation" => saved.Group.Validations?.LastOrDefault()?.ValidationId,
                    "gate" => saved.Group.ApprovalGates?.LastOrDefault()?.GateId,
                    _ => null,
                };
                _selectedElement = selectId is null ? null : new SelectedElementIdentity(selectKind, selectId);
            }
            else if (_selectedElement is { } selected && !ElementStillExists(saved.Group, _durablePlan, selected))
            {
                _selectedElement = null;
            }

            // The graph rebuild is intentionally deferred until the current WPF input event
            // completes. Refresh the inspector now so an inserted-and-selected step never leaves
            // the still-visible toolbar targeting the whole plan during that dispatcher gap.
            RefreshDetailPanel();
            RebuildPreservingScroll(saved, _durablePlan);
            Title = $"Plan — {_durablePlan?.DisplayName ?? saved.Group.GroupTitle}";
            return true;
        }
        catch (Exception exception)
        {
            _lastStagedMutationError = (failurePrefix ?? string.Empty) + exception.Message;
            if (showFailureDialog)
                UIErrorHelper.ShowError("Edit Plan", _lastStagedMutationError, this);
            return false;
        }
    }

    private void NormalizeCurrentPlanFromDeveloperMenu()
    {
        if (!SquadDashEnvironment.IsDeveloperMode || _plan is null || _saveDefinitionEdit is null)
            return;

        if (!PlanTopologyNormalizer.TryNormalize(
                _plan.Group, out var normalization, out var error) || normalization is null)
        {
            UIErrorHelper.ShowError(
                "Normalize Plan",
                error ?? "The plan topology is invalid. Ask the planning AI to repair it.",
                this);
            return;
        }

        if (!normalization.HasChanges)
        {
            MessageBox.Show(
                this,
                "This plan is already normalized. No redundant task dependencies or approval-gate endpoints were found.",
                "Normalize Plan",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var previewLines = normalization.Changes.Select(change =>
        {
            var label = change.Kind switch
            {
                "task-dependency" => $"Task {change.OwnerId}: remove redundant " +
                                     (change.RemovedIds.Count == 1 ? "dependency" : "dependencies"),
                "gate-after" => $"Approval {change.OwnerId}: remove redundant after-task " +
                                (change.RemovedIds.Count == 1 ? "endpoint" : "endpoints"),
                "gate-before" => $"Approval {change.OwnerId}: remove redundant before-task " +
                                 (change.RemovedIds.Count == 1 ? "endpoint" : "endpoints"),
                _ => $"{change.OwnerId}: remove",
            };
            return $"• {label}: " + string.Join(", ", change.RemovedIds);
        });
        var message =
            "SquadDash can normalize this plan without changing task ordering or approval blocking behavior.\n\n" +
            string.Join("\n", previewLines) +
            "\n\nSave this normalized revision? The current plan file will be preserved as its .backup file.";
        if (MessageBox.Show(
                this,
                message,
                "Normalize Plan",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (!ApplyStagedMutation(normalization.Group)) return;

        MessageBox.Show(
            this,
            $"Plan normalized. Removed {normalization.RemovedDependencyCount} redundant task " +
            $"dependenc{(normalization.RemovedDependencyCount == 1 ? "y" : "ies")} and " +
            $"{normalization.RemovedGateEndpointCount} redundant approval endpoint" +
            $"{(normalization.RemovedGateEndpointCount == 1 ? "" : "s")}.\n\n" +
            "The visualization has been refreshed and the previous plan file is available as .backup.",
            "Normalize Plan",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void UndoStagedEdit()
    {
        if (!CanApplyHistoryCandidate(_editHistory?.PeekUndo, out var error))
        {
            if (!string.IsNullOrWhiteSpace(error))
                UIErrorHelper.ShowError("Undo plan edit", error, this);
            return;
        }
        var previous = _editHistory?.Undo();
        if (previous is not null && !ApplyStagedMutation(previous, recordHistory: false))
            _editHistory?.Redo();
    }

    private void RedoStagedEdit()
    {
        if (!CanApplyHistoryCandidate(_editHistory?.PeekRedo, out var error))
        {
            if (!string.IsNullOrWhiteSpace(error))
                UIErrorHelper.ShowError("Redo plan edit", error, this);
            return;
        }
        var next = _editHistory?.Redo();
        if (next is not null && !ApplyStagedMutation(next, recordHistory: false))
            _editHistory?.Undo();
    }

    private MenuItem CreateStagedInsertMenuItem(string anchorTaskId, bool before)
    {
        var item = CreateThemedMenuItem(before ? "Insert step before" : "Insert step after");
        item.Click += (_, _) =>
        {
            if (_plan is null) return;
            var newId = StagedPlanEditor.NextTaskId(_plan.Group);
            ApplyStagedMutation(
                StagedPlanEditor.InsertTask(_plan.Group, anchorTaskId, before),
                selectKind: "task",
                selectId: newId,
                beginEditingSelection: true);
        };
        return item;
    }

    private bool TryValidateUnlockedCandidate(
        DecomposedTaskGroup candidate,
        out string? error)
    {
        error = null;
        if (_durablePlan is null || _durablePlan.LifecycleStatus == PlanLifecycleStatus.Staged)
            return true;
        return StagedPlanEditor.TryValidateUnlockedChanges(_durablePlan, candidate, out error);
    }

    /// <summary>
    /// Builds a "This approval blocks" submenu for a pending gate's right-click context menu.
    /// Lists the useful blocking frontier after the gate; descendants of an already-blocked
    /// task are omitted because they are held transitively. Existing checked descendants remain
    /// available so a redundant direct block can be removed.
    /// Returns null when there are no compatible candidate tasks or no edit context.
    /// </summary>
    private MenuItem? CreateApprovalBlocksSubmenu(string gateId, DecomposedTaskGroup group)
    {
        if (_saveDefinitionEdit is null || _plan is null) return null;
        var gate = (group.ApprovalGates ?? []).FirstOrDefault(g =>
            string.Equals(g.GateId, gateId, StringComparison.Ordinal));
        if (gate is null) return null;

        var candidates = PlanGateManager.GetApprovalBlockingCandidates(group, gate)
            .OrderBy(t => t.Title ?? t.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0) return null;

        var beforeSet = (gate.BeforeTaskIds ?? []).ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<string> capturedAfterTaskIds = gate.AfterTaskIds ?? [];
        var capturedGateId = gate.GateId;
        var capturedMessage = gate.Message;
        var capturedQuestion = gate.Question ?? string.Empty;

        var submenu = CreateThemedMenuItem("This approval blocks");
        foreach (var candidate in candidates)
        {
            var capturedCandidate = candidate;
            var include = !beforeSet.Contains(capturedCandidate.Id);
            var prospectiveGroup = PlanGateManager.ApplyGateConsolidation(
                group,
                capturedAfterTaskIds,
                capturedCandidate.Id,
                include,
                capturedGateId,
                capturedMessage,
                capturedQuestion);
            var canEdit = TryValidateUnlockedCandidate(prospectiveGroup, out var lockError);
            var item = CreateThemedMenuItem(capturedCandidate.Title ?? capturedCandidate.Id, canEdit);
            item.IsCheckable = true;
            item.IsChecked = beforeSet.Contains(capturedCandidate.Id);
            if (!canEdit)
                item.ToolTip = ToolTipHelper.MakeThemedToolTip(
                    lockError ?? "This approval boundary is locked by execution history.");
            // IsChecked is already toggled before Click fires on a checkable MenuItem.
            item.Click += (_, _) =>
            {
                if (_plan is null) return;
                var include = item.IsChecked;
                var updatedGroup = PlanGateManager.ApplyGateConsolidation(
                    _plan.Group,
                    capturedAfterTaskIds,
                    capturedCandidate.Id,
                    include,
                    capturedGateId,
                    capturedMessage,
                    capturedQuestion);
                ApplyStagedMutation(updatedGroup, selectKind: "humanapproval", selectId: capturedGateId);
            };
            submenu.Items.Add(item);
        }
        return submenu;
    }

    /// <summary>
    /// Returns a group equal to <paramref name="group"/> but with <paramref name="depId"/>
    /// added to or removed from <paramref name="taskId"/>'s DependsOn list.
    /// </summary>
    private static DecomposedTaskGroup WithToggledDependency(
        DecomposedTaskGroup group, string taskId, string depId, bool add)
    {
        var modifiedTasks = group.Tasks.Select(t =>
        {
            if (!string.Equals(t.Id, taskId, StringComparison.Ordinal)) return t;
            IReadOnlyList<string> newDeps;
            if (add)
            {
                newDeps = t.DependsOn.Contains(depId, StringComparer.Ordinal)
                    ? t.DependsOn
                    : [.. t.DependsOn, depId];
            }
            else
            {
                newDeps = t.DependsOn
                    .Where(d => !string.Equals(d, depId, StringComparison.Ordinal))
                    .ToArray();
            }
            return t with { DependsOn = newDeps };
        }).ToArray();
        return group with { Tasks = modifiedTasks };
    }

    /// <summary>
    /// Builds a "Depends On" submenu for a task border context menu. Lists all other tasks
    /// grouped into Earlier/Later by topological stage. Current deps show check marks.
    /// Candidates that would create a cycle are disabled with a descriptive tooltip.
    /// Selecting/clearing an item calls <see cref="DependencyEditRecomputer.Recompute"/>;
    /// stage-changing edits require confirmation before routing through
    /// <see cref="ApplyStagedMutation"/>.
    /// </summary>
    private MenuItem CreateVerificationLevelSubmenu(string taskId)
    {
        var task = _plan?.Group.Tasks.FirstOrDefault(candidate => candidate.Id == taskId);
        var verificationOnly = PlanTaskExecutionPolicy.IsVerificationOnly(task);
        var definitionEditable = IsTaskDefinitionEditable(taskId) && !verificationOnly;
        var current = PlanTaskVerificationLevel.Normalize(task?.VerificationLevel);
        var submenu = CreateThemedMenuItem("Verification Level", definitionEditable);
        submenu.ToolTip = verificationOnly
            ? ToolTipHelper.MakeThemedToolTip(
                "Verification-only steps do not receive a second independent verification pass.")
            : !definitionEditable
                ? ToolTipHelper.MakeThemedToolTip(
                    "Verification level cannot be changed after this step enters execution history.")
                : null;

        foreach (var (value, label) in new[]
        {
            (PlanTaskVerificationLevel.Lightweight, "Lightweight"),
            (PlanTaskVerificationLevel.Standard, "Standard"),
            (PlanTaskVerificationLevel.Enhanced, "Enhanced"),
        })
        {
            var captured = value;
            var item = CreateThemedMenuItem(label);
            item.IsCheckable = true;
            item.IsChecked = current == value;
            item.Click += (_, _) =>
            {
                if (_plan is null || !IsTaskDefinitionEditable(taskId)) return;
                ApplyStagedMutation(StagedPlanEditor.UpdateTaskVerificationLevel(
                    _plan.Group, taskId, captured), selectKind: "task", selectId: taskId);
            };
            submenu.Items.Add(item);
        }

        return submenu;
    }

    private ContextMenu? CreateAgentChipAssignContextMenu(string taskId, string clickedAgentHandle)
    {
        if (_saveDefinitionEdit is null || _plan is null || _getAssignableAgents is null)
            return null;
        if (!IsTaskDefinitionEditable(taskId))
            return null;

        var task = _plan.Group.Tasks.FirstOrDefault(candidate => candidate.Id == taskId);
        if (task is null) return null;

        var agents = _getAssignableAgents()
            .Where(agent => agent.IsActive)
            .OrderBy(agent => agent.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (agents.Length == 0)
            return null;

        var menu = CreateThemedMenu();

        var headerText = new TextBlock
        {
            Text = "Assign primary agent",
            FontStyle = FontStyles.Italic,
        };
        headerText.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        var header = new MenuItem
        {
            Header = headerText,
            IsEnabled = false,
        };
        header.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        menu.Items.Add(header);
        menu.Items.Add(CreateThemedMenuSeparator());

        var primaryHandle = task.AgentAssignments?.FirstOrDefault()?.AgentHandle;

        foreach (var agent in agents)
        {
            var capturedAgent = agent;
            var item = CreateThemedMenuItem(capturedAgent.Name);
            item.IsCheckable = true;
            item.IsChecked = string.Equals(
                primaryHandle,
                capturedAgent.Handle,
                StringComparison.OrdinalIgnoreCase);
            item.ToolTip = ToolTipHelper.MakeThemedToolTip(
                $"Assign {capturedAgent.Name} as the task's primary agent.");
            item.Click += (_, _) =>
            {
                if (_plan is null || !IsTaskDefinitionEditable(taskId))
                    return;

                var mutation = StagedPlanEditor.AssignTaskToAgent(
                    _plan.Group,
                    taskId,
                    capturedAgent.Handle);
                ApplyStagedMutation(mutation, selectKind: "task", selectId: taskId);
            };
            menu.Items.Add(item);
        }

        menu.Tag = clickedAgentHandle;
        return menu;
    }

    private MenuItem? CreateAssignToSubmenu(string taskId)
    {
        if (_saveDefinitionEdit is null || _plan is null || _getAssignableAgents is null)
            return null;

        var task = _plan.Group.Tasks.FirstOrDefault(candidate => candidate.Id == taskId);
        if (task is null) return null;
        var agents = _getAssignableAgents()
            .Where(agent => agent.IsActive)
            .OrderBy(agent => agent.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var definitionEditable = IsTaskDefinitionEditable(taskId);
        var submenu = CreateThemedMenuItem("Assign primary agent", definitionEditable && agents.Length > 0);
        if (agents.Length == 0)
        {
            submenu.ToolTip = ToolTipHelper.MakeThemedToolTip(
                "No active agents are available in this workspace roster.");
            return submenu;
        }

        var primaryHandle = task.AgentAssignments?.FirstOrDefault()?.AgentHandle;
        foreach (var agent in agents)
        {
            var capturedAgent = agent;
            var item = CreateThemedMenuItem(capturedAgent.Name, definitionEditable);
            item.IsCheckable = true;
            item.IsChecked = string.Equals(
                primaryHandle,
                capturedAgent.Handle,
                StringComparison.OrdinalIgnoreCase);
            item.ToolTip = ToolTipHelper.MakeThemedToolTip(
                $"Assign {capturedAgent.Name} as the task's primary agent.");
            item.Click += (_, _) =>
            {
                if (_plan is null || !IsTaskDefinitionEditable(taskId)) return;
                var mutation = StagedPlanEditor.AssignTaskToAgent(
                    _plan.Group,
                    taskId,
                    capturedAgent.Handle);
                ApplyStagedMutation(mutation, selectKind: "task", selectId: taskId);
            };
            submenu.Items.Add(item);
        }
        return submenu;
    }

    private MenuItem? CreateDependsOnSubmenu(
        string taskId,
        DecomposedTaskGroup group,
        IReadOnlyDictionary<string, int> levels,
        Dictionary<string, Point> positions)
    {
        if (_saveDefinitionEdit is null || _plan is null) return null;

        var selectedTask = group.Tasks.FirstOrDefault(t =>
            string.Equals(t.Id, taskId, StringComparison.Ordinal));
        if (selectedTask is null) return null;

        var candidates = group.Tasks
            .Where(t => !string.Equals(t.Id, taskId, StringComparison.Ordinal))
            .OrderBy(t => levels.TryGetValue(t.Id, out var l) ? l : 0)
            .ThenBy(t => t.Title ?? t.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0) return null;

        var currentDeps   = selectedTask.DependsOn.ToHashSet(StringComparer.Ordinal);
        var selectedLevel = levels.TryGetValue(taskId, out var sl) ? sl : 0;

        var earlier = candidates.Where(t =>
            (levels.TryGetValue(t.Id, out var l) ? l : 0) < selectedLevel).ToArray();
        var later   = candidates.Where(t =>
            (levels.TryGetValue(t.Id, out var l) ? l : 0) >= selectedLevel).ToArray();

        var definitionEditable = IsTaskDefinitionEditable(taskId);
        var submenu = CreateThemedMenuItem("Depends On");

        void AddGroupHeader(string label)
        {
            var headerItem = CreateThemedMenuItem(label, isEnabled: false);
            headerItem.FontStyle = FontStyles.Italic;
            headerItem.SetResourceReference(MenuItem.ForegroundProperty, "SubtleText");
            submenu.Items.Add(headerItem);
        }

        void AddCandidateItem(DecomposedSubTask candidate)
        {
            var isCurrentDep = currentDeps.Contains(candidate.Id);

            bool wouldCycle    = false;
            string? cycleTip   = null;
            string? lockTip = null;
            var testGroup = WithToggledDependency(group, taskId, candidate.Id, add: !isCurrentDep);
            var testResult = DependencyEditRecomputer.Recompute(testGroup);
            if (!testResult.Success || testResult.RecomputedGroup is null)
            {
                wouldCycle = true;
                cycleTip = testResult.CycleIds is { Count: > 0 }
                    ? "Adding this dependency would create a cycle: " +
                      string.Join(" → ", testResult.CycleIds)
                    : "Adding this dependency would create a cycle.";
            }
            else if (!TryValidateUnlockedCandidate(testResult.RecomputedGroup, out var unlockError))
            {
                lockTip = unlockError ?? "Dependencies cannot be changed after this step has entered execution history.";
            }

            var capturedCandidate  = candidate;
            var capturedCurrentDep = isCurrentDep;
            var itemEnabled = definitionEditable && !wouldCycle && lockTip is null;
            var item = CreateThemedMenuItem(capturedCandidate.Title ?? capturedCandidate.Id, itemEnabled);
            item.IsCheckable = true;
            item.IsChecked = capturedCurrentDep;
            if (wouldCycle && cycleTip is not null)
                item.ToolTip = ToolTipHelper.MakeThemedToolTip(cycleTip);
            else if (!itemEnabled)
                item.ToolTip = ToolTipHelper.MakeThemedToolTip(
                    lockTip ?? "Dependencies cannot be changed after this step has entered execution history.");

            // IsChecked is already toggled before Click fires on a checkable MenuItem.
            item.Click += (_, _) =>
            {
                if (_plan is null || !IsTaskDefinitionEditable(taskId))
                {
                    item.IsChecked = capturedCurrentDep;
                    return;
                }
                var adding   = item.IsChecked;
                var newGroup = WithToggledDependency(_plan.Group, taskId, capturedCandidate.Id, adding);
                var result   = DependencyEditRecomputer.Recompute(newGroup);
                if (!result.Success)
                {
                    // Cycle guard — should not happen since the item was disabled, but be safe.
                    item.IsChecked = capturedCurrentDep;
                    return;
                }

                // Recompute current levels at click time (plan may have changed since menu build).
                var currentLevels = ValidationShieldPresenter.ComputeValidationAwareLevels(
                    _plan.Group.Tasks, _plan.Group.Validations ?? []);

                var movedTasks = _plan.Group.Tasks
                    .Where(t =>
                        currentLevels.TryGetValue(t.Id, out var oldLevel) &&
                        result.StageAssignments!.TryGetValue(t.Id, out var newLevel) &&
                        oldLevel != newLevel)
                    .ToList();

                if (movedTasks.Count > 0)
                {
                    var primary  = movedTasks.FirstOrDefault(t =>
                        string.Equals(t.Id, taskId, StringComparison.Ordinal)) ?? movedTasks[0];
                    var title    = primary.Title ?? primary.Id;
                    var oldStage = (currentLevels.TryGetValue(primary.Id, out var os) ? os : 0) + 1;
                    var newStage = result.StageAssignments![primary.Id] + 1;
                    var extra    = movedTasks.Count - 1;
                    var msg      = extra > 0
                        ? $"This will move '{title}' from Stage {oldStage} to Stage {newStage} " +
                          $"and reposition {extra} downstream step{(extra == 1 ? "" : "s")}. Continue?"
                        : $"This will move '{title}' from Stage {oldStage} to Stage {newStage}. Continue?";

                    var confirmed = MessageBox.Show(
                        this, msg, "Stage Change",
                        MessageBoxButton.YesNo, MessageBoxImage.Question,
                        MessageBoxResult.No) == MessageBoxResult.Yes;

                    if (!confirmed)
                    {
                        item.IsChecked = capturedCurrentDep;
                        return;
                    }

                    // Capture old positions for post-rebuild animation of affected tasks.
                    var affectedIds = movedTasks.Select(t => t.Id).ToList<string>();
                    _pendingAnimation = (
                        new Dictionary<string, Point>(positions, StringComparer.Ordinal),
                        affectedIds);
                }

                if (!ApplyStagedMutation(result.RecomputedGroup!))
                {
                    // Persistence or validation failed — roll back the transient checkbox toggle
                    // and any pending animation that was queued speculatively above.
                    item.IsChecked    = capturedCurrentDep;
                    _pendingAnimation = null;
                }
            };
            submenu.Items.Add(item);
        }

        if (earlier.Length > 0)
        {
            AddGroupHeader("Earlier steps");
            foreach (var c in earlier) AddCandidateItem(c);
        }
        if (later.Length > 0)
        {
            if (earlier.Length > 0) submenu.Items.Add(CreateThemedMenuSeparator());
            AddGroupHeader("Later steps");
            foreach (var c in later) AddCandidateItem(c);
        }

        return submenu.Items.Count > 0 ? submenu : null;
    }

    private bool ConfirmDestructivePlanEdit(string message) =>
        MessageBox.Show(this, message, "Edit Plan", MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    private static bool ElementStillExists(
        DecomposedTaskGroup group,
        Plan? durablePlan,
        SelectedElementIdentity selected) => selected.Kind switch
    {
        "task" => group.Tasks.Any(task => task.Id == selected.Id),
        "validation" => (group.Validations ?? []).Any(value => value.ValidationId == selected.Id),
        "gate" or "humanapproval" => (group.ApprovalGates ?? []).Any(value => value.GateId == selected.Id),
        "agent" => durablePlan?.Tasks.Any(task =>
            PlanTaskStatus.IsVerifying(task.Status) &&
            (task.AgentAssignments ?? []).Any(assignment =>
                string.Equals(assignment.AgentHandle, selected.Id, StringComparison.OrdinalIgnoreCase))) ?? false,
        _ => true,
    };

    internal void NotifyThemeChanged()
    {
        var spinnerColor = ResolvePlanActivityColor("ValidationValidatingSpinner", Colors.SteelBlue);
        foreach (var spinner in _validationSpinnersById.Values)
            spinner.AccentColor = spinnerColor;

        var taskSpinnerColor = ResolvePlanSpinnerColor();
        foreach (var spinner in _taskSpinnersById.Values)
            spinner.AccentColor = taskSpinnerColor;
    }

    internal void RestoreWindowGeometry(FloatingWindowPositionStore positionStore)
    {
        _windowGeometryRestored = positionStore.TryRestore($"PlanViewer:{_viewportGroupId}", this);
    }

    private void OnInitialContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnInitialContentRendered;
        // The host restores bounds before Show; content sizing must not overwrite them.
        if (!_windowGeometryRestored)
            ApplyInitialWindowLayout();
        _initialLayoutApplied = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (_isViewerClosed)
                return;
            // Saved navigation wins only on open; live progress keeps its normal auto-scroll path.
            if (_initialViewport is not null)
                RestoreGraphViewport(_initialViewport);
            else
                EnsureZoomedAutomaticScrollTargetVisible();
            _viewportReady = true;
            QueueGraphViewportSave();
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private void ApplyInitialWindowLayout()
    {
        const double graphShare = 0.70;
        const double splitterWidth = 5;
        const double graphHorizontalInsets = 22 * 2 + 18 * 2;
        const double canvasVerticalInsets = 4 + 18;
        const double windowBorderInsets = 3;

        var workArea = GetLogicalWorkArea();
        var requiredGraphColumnWidth = _canvasContentWidth * _zoomFactor + graphHorizontalInsets;
        var idealWidth = requiredGraphColumnWidth / graphShare + splitterWidth + windowBorderInsets;
        Width = Math.Min(workArea.Width, Math.Max(MinWidth, idealWidth));
        UpdateLayout();

        var fixedHeight = CloseButtonHeight + windowBorderInsets +
                          (_header?.DesiredSize.Height ?? 0) +
                          _canvasContentHeight * _zoomFactor + canvasVerticalInsets +
                          (_readingHint?.DesiredSize.Height ?? 0);
        Height = Math.Min(workArea.Height, Math.Max(MinHeight, fixedHeight));

        KeepWindowInside(workArea);
        UpdateLayout();
    }

    private Rect GetLogicalWorkArea()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero)
            return SystemParameters.WorkArea;

        var physical = NativeMethods.GetWorkAreaForWindow(hwnd);
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
            return SystemParameters.WorkArea;

        var toDip = source.CompositionTarget.TransformFromDevice;
        var topLeft = toDip.Transform(new Point(physical.Left, physical.Top));
        var bottomRight = toDip.Transform(new Point(physical.Right, physical.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private void KeepWindowInside(Rect workArea)
    {
        if (double.IsNaN(Left) || double.IsNaN(Top))
            return;

        Left = Width >= workArea.Width - 0.5
            ? workArea.Left
            : Math.Clamp(Left, workArea.Left, workArea.Right - Width);
        Top = Height >= workArea.Height - 0.5
            ? workArea.Top
            : Math.Clamp(Top, workArea.Top, workArea.Bottom - Height);
    }

    private void ResolveAutomaticScrollTarget(
        Plan? durablePlan,
        IReadOnlyDictionary<string, int> levels,
        IReadOnlyDictionary<string, Point> positions,
        double canvasLeftMargin)
    {
        _automaticScrollTarget = AutomaticScrollTarget.None;
        _activeVisibilityStart = double.NaN;
        _activeVisibilityEnd = double.NaN;
        if (durablePlan is null)
        {
            _automaticScrollTarget = AutomaticScrollTarget.PlanStart;
            return;
        }

        var activity = PlanTaskActivityResolver.Resolve(durablePlan);
        var focusTaskIds = activity
            .Where(item => IsActivelyWorking(item.Value))
            .Select(item => item.Key)
            .Concat(_recoveryAssessmentTaskIds)
            .Where(id => levels.ContainsKey(id) && positions.ContainsKey(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (focusTaskIds.Length == 0 && !IsPlanComplete(durablePlan))
        {
            var allTasksUntouched = durablePlan.Tasks.All(task => task.Status == PlanTaskStatus.Pending);
            var hasDurableCurrentTask = durablePlan.InterruptionData?.InterruptedTaskId is not null ||
                                        durablePlan.Progress.ExecutingTaskId is not null;
            if (allTasksUntouched && !hasDurableCurrentTask)
            {
                _automaticScrollTarget = AutomaticScrollTarget.PlanStart;
                return;
            }

            var nextTaskId = PlanViewerAutoScrollPolicy.SelectNextWorkTaskId(
                durablePlan.Tasks.Select(task => (task.TaskId, task.Status)).ToArray(),
                durablePlan.InterruptionData?.InterruptedTaskId,
                durablePlan.Progress.ExecutingTaskId);
            if (nextTaskId is not null && levels.ContainsKey(nextTaskId) && positions.ContainsKey(nextTaskId))
                focusTaskIds = [nextTaskId];
        }

        if (focusTaskIds.Length > 0)
        {
            var activeLevel = focusTaskIds.Max(id => levels[id]);
            var targetLevel = levels.Values
                .Where(level => level > activeLevel)
                .DefaultIfEmpty(activeLevel)
                .Min();
            _activeVisibilityStart = positions
                .Where(item => levels[item.Key] == activeLevel)
                .Min(item => item.Value.X) + canvasLeftMargin;
            _activeVisibilityEnd = positions
                .Where(item => levels[item.Key] == targetLevel)
                .Max(item => item.Value.X + NodeWidth) + canvasLeftMargin;
            _automaticScrollTarget = AutomaticScrollTarget.ActiveRange;
            return;
        }

        if (IsPlanComplete(durablePlan))
        {
            _activeVisibilityEnd = _canvasContentWidth + canvasLeftMargin;
            _automaticScrollTarget = AutomaticScrollTarget.PlanEnd;
            return;
        }

        if (durablePlan.Tasks.All(task => task.Status == PlanTaskStatus.Pending))
            _automaticScrollTarget = AutomaticScrollTarget.PlanStart;
    }

    private void EnsureAutomaticScrollTargetVisible()
    {
        if (_graphScroll is null || _automaticScrollTarget == AutomaticScrollTarget.None)
            return;

        _graphScroll.UpdateLayout();
        var requestedOffset = _automaticScrollTarget switch
        {
            AutomaticScrollTarget.PlanStart => 0,
            AutomaticScrollTarget.PlanEnd => PlanViewerAutoScrollPolicy.CalculateRightAlignedOffset(
                _graphScroll.HorizontalOffset,
                _graphScroll.ViewportWidth,
                _activeVisibilityEnd,
                _graphScroll.ExtentWidth),
            AutomaticScrollTarget.ActiveRange when !double.IsNaN(_activeVisibilityStart) &&
                                                   !double.IsNaN(_activeVisibilityEnd) =>
                PlanViewerAutoScrollPolicy.CalculateHorizontalOffset(
                    _graphScroll.HorizontalOffset,
                    _graphScroll.ViewportWidth,
                    _activeVisibilityStart,
                    _activeVisibilityEnd,
                    _graphScroll.ExtentWidth),
            _ => _graphScroll.HorizontalOffset,
        };
        if (Math.Abs(requestedOffset - _graphScroll.HorizontalOffset) > 0.5)
            _graphScroll.ScrollToHorizontalOffset(requestedOffset);
    }

    private static bool HasNewActiveTask(Plan? previousPlan, Plan currentPlan)
    {
        if (previousPlan is null)
            return false;

        var previousActive = PlanTaskActivityResolver.Resolve(previousPlan)
            .Where(item => IsActivelyWorking(item.Value))
            .Select(item => item.Key)
            .ToHashSet(StringComparer.Ordinal);
        return PlanTaskActivityResolver.Resolve(currentPlan)
            .Any(item => IsActivelyWorking(item.Value) && !previousActive.Contains(item.Key));
    }

    private static bool HasJustCompleted(Plan? previousPlan, Plan currentPlan) =>
        previousPlan is not null && !IsPlanComplete(previousPlan) && IsPlanComplete(currentPlan);

    private static bool IsPlanComplete(Plan plan) =>
        plan.LifecycleStatus == PlanLifecycleStatus.Completed ||
        plan.Tasks.Count > 0 && plan.Tasks.All(task =>
            task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded);

    private static bool IsActivelyWorking(PlanTaskActivityState state) => state is
        PlanTaskActivityState.Executing or
        PlanTaskActivityState.Assessing or
        PlanTaskActivityState.Verifying or
        PlanTaskActivityState.Reworking;

    private FrameworkElement CreateAgentAvatarChip(
        (ImageSource? Image, string Initial, Brush Accent)? info,
        double chipSize,
        string agentHandle)
    {
        UIElement content;
        if (info?.Image is { } image)
        {
            // Crop 15% from each edge to zoom into the face at center.
            // A Viewbox with a negative-margin Image achieves this without
            // needing a CroppedBitmap (which requires pixel dimensions).
            var cropFraction = 0.15;
            var innerScale = 1.0 / (1.0 - 2 * cropFraction); // ~1.4286
            var innerSize = chipSize * innerScale;
            var offset = (innerSize - chipSize) / 2;
            var img = new Image
            {
                Source = image,
                Width = innerSize,
                Height = innerSize,
                Stretch = Stretch.UniformToFill,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var clipGrid = new Grid
            {
                Width = chipSize - 4,
                Height = chipSize - 4,
                ClipToBounds = true,
            };
            img.Margin = new Thickness(-offset);
            clipGrid.Children.Add(img);
            content = clipGrid;
        }
        else
        {
            var initial = info?.Initial ?? (agentHandle.Length > 0
                ? agentHandle[0].ToString().ToUpperInvariant()
                : "?");
            var tb = new TextBlock
            {
                Text = initial,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                FontSize = chipSize * 0.5,
            };
            if (info?.Accent is { } accent)
                tb.Foreground = accent;
            else
                tb.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            content = tb;
        }

        var chip = new Border
        {
            Width = chipSize,
            Height = chipSize,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gray,
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 3 * _scaleFactor, 0),
            Child = content is Grid ? content : new Viewbox { Child = (UIElement)content },
        };
        chip.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        return chip;
    }

    private double MeasureValidationRailVisualHeight(string? validationTitle)
    {
        var titleProbe = new TextBlock
        {
            Text = ValidationShieldPresenter.TruncateTitle(validationTitle),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (TryFindResource("FontSizeBody") is double fontSize)
            titleProbe.FontSize = fontSize;
        else
            titleProbe.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        titleProbe.Measure(new Size(280 * _scaleFactor, double.PositiveInfinity));

        var shieldHeight = Math.Round(BaseNodeHeight * 0.375 * _scaleFactor);
        var titleChromeHeight = 3 * _scaleFactor + 2; // title margin plus one-pixel top/bottom padding
        return shieldHeight + titleChromeHeight + titleProbe.DesiredSize.Height;
    }

    private FrameworkElement CreateValidationShield(string validationId, string status)
    {
        var s = _scaleFactor;

        // Map status to the Viewbox resource key added in ValidationShields.xaml.
        var viewboxKey = status switch
        {
            PlanValidationStatus.Passed     => "ValidationPassedShield",
            PlanValidationStatus.Failed     => "ValidationFailedShield",
            PlanValidationStatus.Validating => "ValidationValidatingShield",
            PlanValidationStatus.Ready      => "ValidationReadyShield",
            PlanValidationStatus.Stale      => "NeedsRevalidationShield",
            _                               => "ValidationPendingShield",
        };

        var shieldHeight = Math.Round(BaseNodeHeight * 0.375 * s);
        var shieldWidth = Math.Round(shieldHeight * 29.0 / 31.0);

        var grid = new Grid
        {
            Width = shieldWidth,
            Height = shieldHeight,
            Background = Brushes.Transparent,
        };

        if (viewboxKey is not null &&
            Application.Current?.TryFindResource(viewboxKey) is Viewbox viewboxTemplate)
        {
            var viewbox = (Viewbox)Application.Current.FindResource(viewboxKey);
            viewbox.Width = shieldWidth;
            viewbox.Height = shieldHeight;
            grid.Children.Add(viewbox);
        }
        else
        {
            // Legacy path for missing Viewbox resources.
            var shield = new Path
            {
                Data = Geometry.Parse("M 12,1 L 22,5 L 22,12 C 22,18 18,22 12,25 C 6,22 2,18 2,12 L 2,5 Z"),
                Width = shieldWidth,
                Height = shieldHeight,
                Stretch = Stretch.Fill,
                StrokeThickness = 1.6,
                StrokeLineJoin = PenLineJoin.Round,
            };
            var innerIcon = new Path
            {
                Data = Geometry.Parse("M 7,13 L 10.5,16.5 L 17.5,9"),
                Width = 14 * s,
                Height = 11 * s,
                Stretch = Stretch.Fill,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            shield.SetResourceReference(Shape.FillProperty, "ValidationStaleFill");
            shield.StrokeDashArray = [3, 2];
            innerIcon.SetResourceReference(Shape.StrokeProperty, "ValidationStaleIcon");
            innerIcon.Opacity = 0.65;
            grid.Children.Add(shield);
            grid.Children.Add(innerIcon);
        }

        if (ValidationShieldPresenter.ShowsActivitySpinner(status))
        {
            var spinner = new ActivitySpinner
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AccentColor = ResolvePlanActivityColor("ValidationValidatingSpinner", Colors.SteelBlue),
                MinimumOpacity = 1.0,
                ToolTip = ToolTipHelper.MakeThemedToolTip("Validation is actively evaluating its contract."),
            };
            spinner.SetResourceReference(ActivitySpinner.FontSizeProperty, "FontSizeSmall");
            spinner.SetContinuousActive(true);
            _validationSpinnersById[validationId] = spinner;
            if (_onOpenWorkMetrics is not null)
            {
                spinner.Cursor = Cursors.Hand;
                spinner.PreviewMouseLeftButtonUp += (_, e) =>
                {
                    _onOpenWorkMetrics(PlanWorkItemKinds.Validation, validationId);
                    e.Handled = true;
                };
            }
            grid.Children.Add(spinner);
        }
        return grid;
    }

    private static Color ResolvePlanSpinnerColor()
    {
        if (Application.Current?.TryFindResource("ValidationValidatingBorder") is SolidColorBrush brush)
            return brush.Color;
        return Colors.SteelBlue;
    }

    private static Color ResolvePlanActivityColor(string resourceKey, Color fallback)
    {
        if (Application.Current?.TryFindResource(resourceKey) is SolidColorBrush brush)
            return brush.Color;
        return fallback;
    }

    private ToolTip BuildTaskToolTip(DecomposedSubTask task, PlanTask? durableTask)
    {
        var panel = new StackPanel { MaxWidth = 560 };

        TextBlock AddText(
            string text,
            string foreground = "LabelText",
            FontWeight? weight = null,
            Thickness? margin = null)
        {
            var block = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = weight ?? FontWeights.Normal,
                Margin = margin ?? new Thickness(0),
            };
            block.SetResourceReference(TextBlock.ForegroundProperty, foreground);
            block.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
            panel.Children.Add(block);
            return block;
        }

        AddText(task.Title ?? task.Id, weight: FontWeights.Bold, margin: new Thickness(0, 0, 0, 5));
        if (!string.IsNullOrWhiteSpace(task.Description))
            BindTaskDescriptionPreview(task.Id, task.Description, AddText(""));

        if (durableTask is not null)
            AddText($"Status: {durableTask.Status}", "SubtleText", FontWeights.SemiBold, new Thickness(0, 8, 0, 0));

        if (task.DependsOn.Count > 0)
        {
            AddText("Depends on:", "SubtleText", FontWeights.SemiBold, new Thickness(0, 8, 0, 2));
            foreach (var dep in task.DependsOn)
                AddText("• " + dep, "SubtleText");
        }

        return new ToolTip { Content = panel };
    }

    private static ToolTip BuildValidationToolTip(
        DecomposedValidationNode validation,
        PlanValidationNode? durableValidation,
        IReadOnlyDictionary<string, DecomposedSubTask> tasksById)
    {
        var panel = new StackPanel { MaxWidth = 560 };

        TextBlock AddText(
            string text,
            string foreground = "LabelText",
            FontWeight? weight = null,
            Thickness? margin = null)
        {
            var block = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = weight ?? FontWeights.Normal,
                Margin = margin ?? new Thickness(0),
            };
            block.SetResourceReference(TextBlock.ForegroundProperty, foreground);
            block.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
            panel.Children.Add(block);
            return block;
        }

        string TaskLabel(string id) => tasksById.TryGetValue(id, out var task)
            ? $"{task.Title ?? task.Id} ({id})"
            : id;

        AddText(validation.Title, weight: FontWeights.Bold, margin: new Thickness(0, 0, 0, 5));
        AddText(validation.Description);
        AddText(
            $"Status: {FormatValidationStatus(durableValidation?.Status ?? PlanValidationStatus.Pending)}",
            "SubtleText",
            FontWeights.SemiBold,
            new Thickness(0, 8, 0, 0));

        AddText("Runs after:", "SubtleText", FontWeights.SemiBold, new Thickness(0, 8, 0, 2));
        foreach (var taskId in validation.AfterTaskIds)
            AddText("• " + TaskLabel(taskId), "SubtleText");

        AddText("Required outputs:", "SubtleText", FontWeights.SemiBold, new Thickness(0, 8, 0, 2));
        if (validation.OutputIds is { Count: > 0 })
        {
            foreach (var outputId in validation.OutputIds)
            {
                var producer = tasksById.Values.FirstOrDefault(task =>
                    task.Outputs?.Any(output =>
                        string.Equals(output.OutputId, outputId, StringComparison.Ordinal)) == true);
                var output = producer?.Outputs?.FirstOrDefault(candidate =>
                    string.Equals(candidate.OutputId, outputId, StringComparison.Ordinal));
                var detail = output is null ? outputId : $"{outputId} — {output.Description}";
                AddText("• " + detail, "SubtleText");
            }
        }
        else
        {
            AddText("• Repository evidence from the prerequisite steps", "SubtleText");
        }

        AddText("Releases when passed:", "SubtleText", FontWeights.SemiBold, new Thickness(0, 8, 0, 2));
        if (validation.BeforeTaskIds.Count == 0)
            AddText("• Final plan completion", "SubtleText");
        else
            foreach (var taskId in validation.BeforeTaskIds)
                AddText("• " + TaskLabel(taskId), "SubtleText");

        AddText("Contract assertions:", "SubtleText", FontWeights.SemiBold, new Thickness(0, 8, 0, 2));
        foreach (var assertion in validation.Assertions)
            AddText("• " + assertion, "SubtleText");

        if (durableValidation?.Evidence is { Count: > 0 })
        {
            AddText("AI-assessed evidence:", "SubtleText", FontWeights.SemiBold, new Thickness(0, 8, 0, 2));
            foreach (var evidence in durableValidation.Evidence)
                AddText("• " + evidence, "SubtleText");
            if (!string.IsNullOrWhiteSpace(durableValidation.ValidatedCommit))
                AddText($"Evaluated at commit {durableValidation.ValidatedCommit}.", "SubtleText", margin: new Thickness(0, 4, 0, 0));
        }

        return new ToolTip { Content = panel };
    }

    private static string FormatValidationStatus(string status) => status switch
    {
        PlanValidationStatus.Ready => "Ready to validate",
        PlanValidationStatus.Validating => "Validating now",
        PlanValidationStatus.Passed => "Passed",
        PlanValidationStatus.Failed => "Failed",
        PlanValidationStatus.Stale => "Needs revalidation",
        _ => "Waiting for prerequisite steps",
    };

    private static string BuildApprovalResolvedToolTip(PlanApprovalGate? gate, string location) =>
        ApprovalResolvedTooltipPresentation.Build(gate, location);

    private FrameworkElement CreateApprovalStop(
        bool engaged,
        string toolTip,
        Action? toggle,
        double engagedOpacity = 1.0,
        bool approved = false,
        bool awaitingApproval = false,
        bool approvalChangeActive = false,
        string? selectionAnchor = null,
        string? toggleActionLabel = null,
        Action? approveAction = null,
        Action? unapproveAction = null,
        bool isDependent = false)
    {
        var s = _scaleFactor;
        FrameworkElement indicator;
        FrameworkElement? stop = null;
        Polygon? fallbackStopPolygon = null;
        var showsQuestion = awaitingApproval || approvalChangeActive;
        if (approved || showsQuestion)
        {
            var bgSize = 18 * s;
            var bg = new Border
            {
                Width = bgSize,
                Height = bgSize,
                CornerRadius = new CornerRadius(3 * s),
                BorderThickness = new Thickness(showsQuestion ? 1.1 * s : 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            bg.SetResourceReference(Border.BackgroundProperty, "PlanApprovalResolvedBg");
            if (showsQuestion)
                bg.SetResourceReference(Border.BorderBrushProperty, "PlanApprovalResolved");

            FrameworkElement glyph;
            var resourceKey = showsQuestion
                ? "TaskAwaitingHumanApproval"
                : "PlanApprovalResolvedCheck";
            if (Application.Current?.TryFindResource(resourceKey) is Viewbox resourceGlyph)
            {
                resourceGlyph.Width = (showsQuestion ? 8 : 13) * s;
                resourceGlyph.Height = 13 * s;
                resourceGlyph.HorizontalAlignment = HorizontalAlignment.Center;
                resourceGlyph.VerticalAlignment = VerticalAlignment.Center;
                glyph = resourceGlyph;
            }
            else
            {
                var fallback = new TextBlock
                {
                    Text = showsQuestion ? "?" : "✓",
                    FontSize = 14 * s,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                fallback.SetResourceReference(TextBlock.ForegroundProperty, "PlanApprovalResolved");
                glyph = fallback;
            }

            var approvedContainer = new Grid
            {
                Width = 20 * s,
                Height = 20 * s,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            approvedContainer.Children.Add(bg);
            approvedContainer.Children.Add(glyph);
            if (showsQuestion)
            {
                var glow = new DropShadowEffect
                {
                    Color = Color.FromRgb(0x58, 0xA8, 0xFF),
                    ShadowDepth = 0,
                    BlurRadius = 9 * s,
                    Opacity = 0.25,
                };
                approvedContainer.Effect = glow;
                glow.BeginAnimation(
                    DropShadowEffect.OpacityProperty,
                    new DoubleAnimation(0.18, 0.72, TimeSpan.FromMilliseconds(850))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    });
            }
            // The task-title activity indicator already communicates active approval work.
            // Keep the gate lane focused on approval state so it does not show a duplicate
            // spinner below the question-mark icon, and shows no activity while simply waiting.
            indicator = approvedContainer;
        }
        else
        {
            var stopSignResourceKey = engaged
                ? "HumanApprovalGateStopSign"
                : "HumanApprovalGateDisabled";
            if (Application.Current?.TryFindResource(stopSignResourceKey) is Viewbox stopSignResource)
            {
                stopSignResource.Width = 16 * s;
                stopSignResource.Height = 16 * s;
                stopSignResource.HorizontalAlignment = HorizontalAlignment.Center;
                stopSignResource.VerticalAlignment = VerticalAlignment.Center;
                if (!engaged)
                    stopSignResource.Opacity = 0.55;
                stop = stopSignResource;
                indicator = stopSignResource;
            }
            else
            {
                fallbackStopPolygon = new Polygon
                {
                    Points =
                    [
                        new Point(5 * s, 1 * s), new Point(11 * s, 1 * s), new Point(15 * s, 5 * s), new Point(15 * s, 11 * s),
                        new Point(11 * s, 15 * s), new Point(5 * s, 15 * s), new Point(1 * s, 11 * s), new Point(1 * s, 5 * s),
                    ],
                    StrokeThickness = 1.6,
                    Fill = engaged ? new SolidColorBrush(Color.FromRgb(0xC9, 0x4B, 0x4B)) : Brushes.Transparent,
                    StrokeLineJoin = PenLineJoin.Round,
                    Stretch = Stretch.None,
                };
                fallbackStopPolygon.SetResourceReference(Shape.StrokeProperty, "LineColor");
                stop = fallbackStopPolygon;
                indicator = fallbackStopPolygon;
            }
        }

        var hitTarget = new Grid
        {
            Width = (approved || showsQuestion ? 20 : 16) * s,
            Height = (approvalChangeActive ? 36 : approved || showsQuestion ? 20 : 16) * s,
            Background = Brushes.Transparent,
            Cursor = toggle is null ? Cursors.Arrow : Cursors.Hand,
            ToolTip = ToolTipHelper.MakeThemedToolTip(approvalChangeActive
                ? "Making requested changes so this checkpoint can be reviewed again. Human approval remains pending until the changed work is verified."
                : toolTip),
            Opacity = engaged ? engagedOpacity : 1.0,
        };
        hitTarget.Children.Add(indicator);
        const double dependentOctagonVisualScale = 0.75;
        var shrinkToDependentVisualSize = stop is not null &&
                                          !approved &&
                                          !showsQuestion &&
                                          (isDependent || !engaged);
        if (shrinkToDependentVisualSize)
        {
            indicator.RenderTransformOrigin = new Point(0.5, 0.5);
            indicator.RenderTransform = new ScaleTransform(dependentOctagonVisualScale, dependentOctagonVisualScale);
        }
        if (toggle is not null && stop is not null && selectionAnchor is null)
        {
            if (fallbackStopPolygon is not null)
            {
                hitTarget.MouseEnter += (_, _) => fallbackStopPolygon.StrokeThickness = 2.2;
                hitTarget.MouseLeave += (_, _) => fallbackStopPolygon.StrokeThickness = 1.6;
            }
            hitTarget.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                toggle();
            };
        }
        if (selectionAnchor is not null)
        {
            hitTarget.Tag = $"humanapproval:{selectionAnchor}";
            hitTarget.Cursor = Cursors.Hand;
            WireSelectionClick(hitTarget);
        }
        if (toggle is not null && stop is not null)
        {
            var menu = CreateThemedMenu();
            var actionLabel = engaged
                ? toggleActionLabel ?? "Change this approval control"
                : "Require human approval here";
            var toggleItem = CreateThemedMenuItem(actionLabel);
            toggleItem.Click += (_, _) => toggle();
            menu.Items.Add(toggleItem);
            hitTarget.ContextMenu = menu;
        }
        // Approval/unapproval context menu for the human-gate icon.
        if (approveAction is not null || unapproveAction is not null)
        {
            var menu = hitTarget.ContextMenu ?? CreateThemedMenu();
            if (menu.Items.Count > 0)
                menu.Items.Add(CreateThemedMenuSeparator());
            if (approveAction is not null)
            {
                var approveItem = CreateThemedMenuItem("Approve this step");
                approveItem.Click += (_, _) => approveAction();
                menu.Items.Add(approveItem);
            }
            if (unapproveAction is not null)
            {
                var unapproveItem = CreateThemedMenuItem("Cancel approval");
                unapproveItem.Click += (_, _) => unapproveAction();
                menu.Items.Add(unapproveItem);
            }
            hitTarget.ContextMenu = menu;
        }
        if (hitTarget.ContextMenu is ContextMenu approvalMenu)
            AppendDeveloperNormalizeMenuItem(approvalMenu);
        return hitTarget;
    }

    private ContextMenu CreatePlanTitleContextMenu(PendingDecomposePlan plan, Plan? durablePlan)
    {
        var content = PlanViewerTitleClipboardContent.Create(
            durablePlan, plan.Group.GroupTitle, _squadFolderPath);
        var menu = CreateThemedMenu();
        var copyName = CreateThemedMenuItem("Copy plan name");
        copyName.Click += (_, _) => CopyPlanTitleText(content.Name);
        menu.Items.Add(copyName);

        var copyPath = CreateThemedMenuItem("Copy plan file path", content.FilePath is not null);
        if (content.FilePath is { } filePath)
            copyPath.Click += (_, _) => CopyPlanTitleText(filePath);
        menu.Items.Add(copyPath);
        return menu;
    }

    private static void CopyPlanTitleText(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            SquadDashTrace.Write("PlanViewer", $"Could not copy plan reference to the clipboard: {ex.Message}");
        }
    }

    private static ContextMenu CreateThemedMenu()
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");
        return menu;
    }

    private static MenuItem CreateThemedMenuItem(string header, bool isEnabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = isEnabled };
        item.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        return item;
    }

    private void AppendDeveloperNormalizeMenuItem(ContextMenu menu)
    {
        const string normalizeTag = "developer-normalize-plan";
        if (!SquadDashEnvironment.IsDeveloperMode ||
            _saveDefinitionEdit is null ||
            _durablePlan is null ||
            _durablePlan.LifecycleStatus == PlanLifecycleStatus.Staged ||
            menu.Items.OfType<MenuItem>().Any(item =>
                string.Equals(item.Tag as string, normalizeTag, StringComparison.Ordinal)))
            return;

        if (menu.Items.Count > 0)
            menu.Items.Add(CreateThemedMenuSeparator());
        var normalizeItem = CreateThemedMenuItem("Normalize this plan…");
        normalizeItem.Tag = normalizeTag;
        normalizeItem.Click += (_, _) => NormalizeCurrentPlanFromDeveloperMenu();
        menu.Items.Add(normalizeItem);
    }

    private Separator CreateThemedMenuSeparator()
    {
        var separator = new Separator();
        separator.Style = TryFindResource("ThemedMenuSeparatorStyle") as Style;
        return separator;
    }

    private static Dictionary<string, int> CalculateLevels(
        IReadOnlyList<DecomposedSubTask> tasks,
        IReadOnlyDictionary<string, DecomposedSubTask> tasksById)
    {
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        int Level(string id)
        {
            if (levels.TryGetValue(id, out var known)) return known;
            if (!tasksById.TryGetValue(id, out var task) || !visiting.Add(id)) return 0;
            var validDependencies = task.DependsOn.Where(tasksById.ContainsKey).ToArray();
            var level = validDependencies.Length == 0 ? 0 : validDependencies.Max(Level) + 1;
            visiting.Remove(id);
            return levels[id] = level;
        }

        foreach (var task in tasks) Level(task.Id);
        return levels;
    }

    // Drop-shadow glow applied to a task node Border on hover.
    private const double TaskNodeGlowHue = 210.0;

    private static DropShadowEffect TaskNodeGlowEffect() => new()
    {
        Color       = HslToRgb(TaskNodeGlowHue, 0.90, 0.70),
        BlurRadius  = 18,
        ShadowDepth = 0,
        Opacity     = 0.90,
    };

    private static Color HslToRgb(double hue, double saturation, double lightness)
    {
        var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = lightness - c / 2;

        double r, g, b;
        if      (hue < 60)  { r = c; g = x; b = 0; }
        else if (hue < 120) { r = x; g = c; b = 0; }
        else if (hue < 180) { r = 0; g = c; b = x; }
        else if (hue < 240) { r = 0; g = x; b = c; }
        else if (hue < 300) { r = x; g = 0; b = c; }
        else                { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private void SelectElement(string kind, string id)
    {
        var identity = new SelectedElementIdentity(kind, id);
        _isInspectorEditing = false;
        if (_selectedElement == identity)
        {
            _selectedElement = null;
            ClearSelectionVisual();
        }
        else
        {
            if (!_suppressHistoryPush && _selectedElement is not null)
            {
                if (_inspectorHistoryIndex >= 0 && _inspectorHistoryIndex < _inspectorHistory.Count)
                    _inspectorHistory.RemoveRange(
                        _inspectorHistoryIndex,
                        _inspectorHistory.Count - _inspectorHistoryIndex);
                _inspectorHistory.Add(_selectedElement);
                _inspectorHistoryIndex = _inspectorHistory.Count;
            }
            _selectedElement = identity;
            ApplySelectionVisual();
        }
        RefreshDetailPanel();
    }

    private void NavigateToTask(string taskId)
    {
        SelectElement("task", taskId);
    }

    private void NavigateBack()
    {
        if (_inspectorHistoryIndex <= 0) return;
        _inspectorHistoryIndex--;
        var target = _inspectorHistory[_inspectorHistoryIndex];
        _suppressHistoryPush = true;
        SelectElement(target.Kind, target.Id);
        _suppressHistoryPush = false;
    }

    // Applies selection indicator: border overlays or chip border accents by selected kind.
    // Always clears previous selection first to ensure single-selection invariant.
    private void ApplySelectionVisual()
    {
        ClearSelectionVisual();
        if (_selectedElement is null || _graphCanvas is null)
            return;

        if (_selectedElement.Kind == "agent")
        {
            var handle = _selectedElement.Id;
            if (_agentChipElements.TryGetValue(handle, out var chips))
            {
                var avatarInfo = _resolveAgentAvatar?.Invoke(handle);
                var accentBrush = avatarInfo?.Accent ?? Brushes.Gray;
                foreach (var chip in chips)
                {
                    chip.BorderBrush = accentBrush;
                    chip.BorderThickness = new Thickness(4);
                    _selectedAgentChipBorders.Add(chip);
                }
            }
            return;
        }

        var tag = $"{_selectedElement.Kind}:{_selectedElement.Id}";
        FrameworkElement? target = null;
        foreach (UIElement child in _graphCanvas.Children)
        {
            if (child is FrameworkElement fe && fe.Tag is string t && t == tag)
            {
                target = fe;
                break;
            }
        }
        if (target is null)
            return;

        if (_selectedElement.Kind is "task" or "gate" && target is Border border)
        {
            // Draw a selection ring as a canvas sibling so the card itself is never
            // modified — no thickness change, no margin change, no content reflow.
            var thickness = border.BorderThickness.Left; // cards use uniform thickness
            var overlay = new Border
            {
                Width            = (border.ActualWidth  > 0 ? border.ActualWidth  : border.Width)  + 2 * thickness,
                Height           = (border.ActualHeight > 0 ? border.ActualHeight : border.Height) + 2 * thickness,
                CornerRadius     = border.CornerRadius,
                BorderThickness  = new Thickness(thickness * 2),
                Background       = Brushes.Transparent,
                IsHitTestVisible = false,
            };
            overlay.SetResourceReference(Border.BorderBrushProperty, "ChipSelectedBorder");
            var left = Canvas.GetLeft(target);
            var top  = Canvas.GetTop(target);
            Canvas.SetLeft(overlay, double.IsNaN(left) ? left : left - thickness);
            Canvas.SetTop(overlay,  double.IsNaN(top)  ? top  : top  - thickness);
            Panel.SetZIndex(overlay, Panel.GetZIndex(target) + 1);
            _graphCanvas.Children.Add(overlay);
            _selectionGlowElement = overlay;
        }
        else if (_selectedElement.Kind == "validation" && target is StackPanel validationPanel)
        {
            const double selBorderThickness = 1.5;
            var overlay = new Border
            {
                Width            = (validationPanel.ActualWidth  > 0 ? validationPanel.ActualWidth  : validationPanel.Width)  + 2 * selBorderThickness,
                Height           = (validationPanel.ActualHeight > 0 ? validationPanel.ActualHeight : validationPanel.Height) + 2 * selBorderThickness,
                CornerRadius     = new CornerRadius(3),
                BorderThickness  = new Thickness(selBorderThickness),
                Background       = Brushes.Transparent,
                IsHitTestVisible = false,
            };
            overlay.SetResourceReference(Border.BorderBrushProperty, "ChipSelectedBorder");
            var left = Canvas.GetLeft(target);
            var top  = Canvas.GetTop(target);
            Canvas.SetLeft(overlay, double.IsNaN(left) ? left : left - selBorderThickness);
            Canvas.SetTop(overlay,  double.IsNaN(top)  ? top  : top  - selBorderThickness);
            Panel.SetZIndex(overlay, Panel.GetZIndex(target) + 1);
            _graphCanvas.Children.Add(overlay);
            _selectionGlowElement = overlay;
        }
        else if (_selectedElement.Kind == "alljoin" && target is Border alljoinBorder)
        {
            var thickness = alljoinBorder.BorderThickness.Left;
            var overlay = new Border
            {
                Width            = (alljoinBorder.ActualWidth  > 0 ? alljoinBorder.ActualWidth  : alljoinBorder.Width)  + 2 * thickness,
                Height           = (alljoinBorder.ActualHeight > 0 ? alljoinBorder.ActualHeight : alljoinBorder.Height) + 2 * thickness,
                CornerRadius     = alljoinBorder.CornerRadius,
                BorderThickness  = new Thickness(thickness * 2),
                Background       = Brushes.Transparent,
                IsHitTestVisible = false,
            };
            overlay.SetResourceReference(Border.BorderBrushProperty, "ChipSelectedBorder");
            var left = Canvas.GetLeft(target);
            var top  = Canvas.GetTop(target);
            Canvas.SetLeft(overlay, double.IsNaN(left) ? left : left - thickness);
            Canvas.SetTop(overlay,  double.IsNaN(top)  ? top  : top  - thickness);
            Panel.SetZIndex(overlay, Panel.GetZIndex(target) + 1);
            _graphCanvas.Children.Add(overlay);
            _selectionGlowElement = overlay;
        }
        else if (_selectedElement.Kind == "milestone" && target is Border milestoneBorder)
        {
            const double selThickness = 1.5;
            var overlay = new Border
            {
                Width            = (milestoneBorder.ActualWidth  > 0 ? milestoneBorder.ActualWidth  : milestoneBorder.Width)  + 2 * selThickness,
                Height           = (milestoneBorder.ActualHeight > 0 ? milestoneBorder.ActualHeight : milestoneBorder.Height) + 2 * selThickness,
                CornerRadius     = milestoneBorder.CornerRadius,
                BorderThickness  = new Thickness(selThickness),
                Background       = Brushes.Transparent,
                IsHitTestVisible = false,
            };
            overlay.SetResourceReference(Border.BorderBrushProperty, "ChipSelectedBorder");
            var left = Canvas.GetLeft(target);
            var top  = Canvas.GetTop(target);
            Canvas.SetLeft(overlay, double.IsNaN(left) ? left : left - selThickness);
            Canvas.SetTop(overlay,  double.IsNaN(top)  ? top  : top  - selThickness);
            Panel.SetZIndex(overlay, Panel.GetZIndex(target) + 1);
            _graphCanvas.Children.Add(overlay);
            _selectionGlowElement = overlay;
        }
        else
        {
            var glow = new Border
            {
                Width = target.ActualWidth > 0 ? target.ActualWidth : target.Width,
                Height = target.ActualHeight > 0 ? target.ActualHeight : target.Height,
                Background = target is Border b ? b.Background : Brushes.Gray,
                CornerRadius = target is Border cb ? cb.CornerRadius : new CornerRadius(4),
                Opacity = 0.5,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1.1, 1.1),
            };
            Canvas.SetLeft(glow, Canvas.GetLeft(target));
            Canvas.SetTop(glow, Canvas.GetTop(target));
            Panel.SetZIndex(glow, Panel.GetZIndex(target) - 1);
            _graphCanvas.Children.Add(glow);
            _selectionGlowElement = glow;

            // Stage selection: boost the title to pure polarity for maximum contrast
            // against the selection glow behind it.
            if (_selectedElement.Kind == "stage" && target is TextBlock stageTb)
            {
                var isLight = Application.Current.Resources["AppSurface"] is SolidColorBrush sb
                    && (sb.Color.R + sb.Color.G + sb.Color.B) / 3 > 128;
                stageTb.Foreground = isLight ? Brushes.White : Brushes.Black;
                _selectedStageTitleBlock = stageTb;
            }
        }
    }

    // Restores previous element to its default visual state.
    private void ClearSelectionVisual()
    {
        foreach (var chip in _selectedAgentChipBorders)
        {
            chip.BorderBrush = Brushes.Gray;
            chip.BorderThickness = new Thickness(1);
        }
        _selectedAgentChipBorders.Clear();

        if (_selectionBorderState is var (border, original))
        {
            border.BorderThickness = original;
            _selectionBorderState = null;
        }
        if (_selectedStageTitleBlock is not null)
        {
            _selectedStageTitleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TitleText");
            _selectedStageTitleBlock = null;
        }
        if (_selectionGlowElement is not null && _graphCanvas is not null)
        {
            _graphCanvas.Children.Remove(_selectionGlowElement);
            _selectionGlowElement = null;
        }
    }

    // Routes selection to the appropriate detail populator for all element kinds:
    // task, validation, gate, milestone, stage. Clears panel when nothing is selected.
    private void RefreshDetailPanel()
    {
        if (_detailDocument is null)
            return;

        _descriptionSession?.Dispose();
        _descriptionSession = null;
        MarkdownVisibleContent.Detach(_detailDocument);
        RestoreInspectorFontScale();
        _inspectorEditorSaveAction = null;
        _inspectorEditorStatus = null;
        _inspectorPrimaryEditor = null;

        if (_isInspectorEditing && TryPopulateStagedEditor())
        {
            PrependInspectorToolbar();
            return;
        }

        if (_selectedElement is null)
        {
            _detailDocument.Blocks.Clear();
            var group = _plan?.Group;
            var hint = new Paragraph(new Run(group is null
                ? "Select an element to view details."
                : "(Select an element to see details)"));
            hint.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
            hint.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            _detailDocument.Blocks.Add(hint);

            if (group is not null)
            {
                var title = _durablePlan?.DisplayName ?? group.GroupTitle;
                if (string.IsNullOrWhiteSpace(title))
                    title = group.GroupTitle;
                if (!string.IsNullOrWhiteSpace(title))
                {
                    var titleParagraph = new Paragraph(new Run(title))
                    {
                        FontWeight = FontWeights.SemiBold,
                    };
                    titleParagraph.SetResourceReference(TextElement.ForegroundProperty, "LabelText");
                    titleParagraph.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                    _detailDocument.Blocks.Add(titleParagraph);
                }

                if (!string.IsNullOrWhiteSpace(group.Branch))
                {
                    var branchLabel = new Run("Branch: ");
                    branchLabel.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                    var branchValue = new Run(group.Branch);
                    branchValue.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
                    var branchParagraph = new Paragraph(branchLabel);
                    branchParagraph.Inlines.Add(branchValue);
                    branchParagraph.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                    _detailDocument.Blocks.Add(branchParagraph);
                }

                if (!string.IsNullOrWhiteSpace(group.Summary))
                    _descriptionPresentation!.AppendPlanDescription(_detailDocument, group.Summary);
            }
            AppendActiveReviewNote();
            PrependInspectorToolbar();
            return;
        }

        if (_selectedElement.Kind == "task")
        {
            var durableTask = _durablePlan?.Tasks.FirstOrDefault(t => t.TaskId == _selectedElement.Id);
            if (durableTask is not null)
            {
                PopulateTaskDetail(durableTask, _durablePlan!);
            }
            else
            {
                var subTask = _plan?.Group.Tasks.FirstOrDefault(t => t.Id == _selectedElement.Id);
                if (subTask is not null)
                {
                    // Build a minimal PlanTask from the DecomposedSubTask
                    var minimalTask = new PlanTask(
                        subTask.Id,
                        subTask.Title,
                        subTask.Description,
                        subTask.DependsOn,
                        subTask.Priority,
                        PlanTaskStatus.Pending,
                        AgentAssignments: subTask.AgentAssignments?.Select(a =>
                            new PlanAgentAssignment(a.AgentHandle, a.Role, a.AllowGenericChildren)).ToList(),
                        ContractExamples: subTask.ContractExamples?.Select(example =>
                            new PlanTaskContractExample(
                                example.ExampleId,
                                example.Title,
                                example.Input,
                                example.ExpectedBehavior,
                                example.Language)).ToArray());
                    PopulateTaskDetail(minimalTask, null!);
                }
            }
        }
        else if (_selectedElement.Kind == "validation")
        {
            var validation = _durablePlan?.Validations?.FirstOrDefault(v =>
                string.Equals(v.ValidationId, _selectedElement.Id, StringComparison.Ordinal));
            if (validation is not null)
                PopulateValidationDetail(validation, _durablePlan);
        }
        else if (_selectedElement.Kind == "verification")
        {
            var taskId = _selectedElement.Id;
            var durableTask = _durablePlan?.Tasks?.FirstOrDefault(t =>
                string.Equals(t.TaskId, taskId, StringComparison.Ordinal));
            var decomposedTask = _plan?.Group?.Tasks?.FirstOrDefault(t =>
                string.Equals(t.Id, taskId, StringComparison.Ordinal));
            var verificationLevel = PlanTaskVerificationLevel.Normalize(
                durableTask?.VerificationLevel ?? decomposedTask?.VerificationLevel);
            var verificationOnly = PlanTaskExecutionPolicy.IsVerificationOnly(durableTask)
                || PlanTaskExecutionPolicy.IsVerificationOnly(decomposedTask);
            var taskTitle = durableTask?.Title ?? decomposedTask?.Title ?? taskId;
            PopulateVerificationDetail(taskTitle, taskId, verificationLevel, verificationOnly, durableTask);
        }
        else if (_selectedElement.Kind == "gate")
        {
            if (int.TryParse(_selectedElement.Id, out var gateIndex))
            {
                var durableGates = _durablePlan?.ApprovalGates;
                if (durableGates is not null && gateIndex >= 0 && gateIndex < durableGates.Count)
                {
                    PopulateGateDetail(durableGates[gateIndex], _durablePlan);
                }
                else
                {
                    var decomposedGates = _plan is null ? null
                        : PendingDecomposePlanAdapter.MapApprovalGates(_plan.Group, _plan.Revision);
                    if (decomposedGates is not null && gateIndex >= 0 && gateIndex < decomposedGates.Count)
                    {
                        PopulateGateDetail(decomposedGates[gateIndex], null);
                    }
                }
            }
        }
        else if (_selectedElement.Kind == "milestone")
        {
            if (int.TryParse(_selectedElement.Id, out var columnIndex))
                PopulateMilestoneDetail(columnIndex);
        }
        else if (_selectedElement.Kind == "alljoin")
        {
            if (int.TryParse(_selectedElement.Id, out var gateIndex))
                PopulateAllJoinDetail(gateIndex);
        }
        else if (_selectedElement.Kind == "stage")
        {
            if (int.TryParse(_selectedElement.Id, out var stageIndex))
                PopulateStageDetail(stageIndex);
        }
        else if (_selectedElement.Kind == "humanapproval")
        {
            PopulateHumanApprovalDetail(_selectedElement.Id);
        }
        else if (_selectedElement.Kind == "agent")
        {
            PopulateAgentDetail(_selectedElement.Id);
        }
        AppendActiveReviewNote();
        PrependInspectorToolbar();
    }

    private void PrependInspectorToolbar()
    {
        if (!_isInspectorEditing && _detailViewer is not null)
            _descriptionSession = new PlanDescriptionDocumentSession(_detailViewer);
        if (_inspectorToolbarPanel is null) return;
        _inspectorToolbarPanel.Children.Clear();

        Button AddButton(string label, Action click, string? toolTip = null)
        {
            var button = TranscriptQuickReplyFactory.CreateButton(
                label, _quickReplyFontSize,
                toolTip: toolTip is null ? null : ToolTipHelper.MakeThemedToolTip(toolTip));
            button.Margin = new Thickness(0, 0, 6, 4);
            button.Click += (_, _) => click();
            _inspectorToolbarPanel.Children.Add(button);
            return button;
        }

        if (!_isInspectorEditing)
        {
            AddButton("Copy details", () => PlanDescriptionDocumentSession.TryCopyText(
                PlanDescriptionDocumentSession.GetCopyText(_detailDocument!)), "Copy visible inspector text");
            AddButton("Find", ShowDescriptionFind, "Find visible inspector text (Ctrl+F)");
        }
        if (_inspectorHistoryIndex > 0)
        {
            var backBtn = AddButton("← Back", NavigateBack, "Go back to the previously inspected element");
            backBtn.Margin = new Thickness(0, 0, 6, 4);
        }

        if (CanEditSelectedElement())
        {
            var editLabel = _selectedElement?.Kind switch
            {
                null => "Edit Plan Details",
                "task" => "Edit Task",
                "validation" => "Edit Validation",
                "gate" or "humanapproval" => "Edit Approval",
                _ => "Edit",
            };
            AddButton(_isInspectorEditing ? "Cancel edit" : editLabel, () =>
            {
                _isInspectorEditing = !_isInspectorEditing;
                if (_isInspectorEditing)
                    _focusPrimaryEditorAfterRebuild = true;
                RefreshDetailPanel();
                FocusPrimaryInspectorEditorIfRequested(_buildSeq);
            }, "Edit this staged-plan element in the inspector.");

            if (_isInspectorEditing && _inspectorEditorSaveAction is not null)
            {
                AddButton("Save changes", () =>
                {
                    var error = _inspectorEditorSaveAction();
                    if (_inspectorEditorStatus is not null)
                        _inspectorEditorStatus.Text = error ?? string.Empty;
                });
            }
        }
        else if (_saveDefinitionEdit is not null && _selectedElement is not null &&
                 _selectedElement.Kind is "task" or "validation" or "gate" or "humanapproval")
        {
            var locked = new TextBlock
            {
                Text = "This definition is frozen because its work has started or its checkpoint has been reached. " +
                       "Only future pending work can be edited.",
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 4),
            };
            locked.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            locked.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            _inspectorToolbarPanel.Children.Add(locked);
        }
        if (_requestAiReview is not null && _plan is not null)
        {
            var review = AddButton(_isAiReviewPending ? "Reviewing…" : "Review with AI", () =>
            {
                if (_isAiReviewPending || _plan is null) return;
                _isAiReviewPending = true;
                _reviewStatusMessage = null;
                _requestAiReview(_plan);
                RefreshDetailPanel();
            },
                "Ask AI to review this staged revision and attach navigable notes.");
            review.IsEnabled = !_isAiReviewPending;
            if (_isAiReviewPending)
            {
                var spinner = new ActivitySpinner
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 4),
                    AccentColor = ResolvePlanSpinnerColor(),
                };
                spinner.SetResourceReference(ActivitySpinner.FontSizeProperty, "FontSizeSmall");
                spinner.SetContinuousActive(true);
                _inspectorToolbarPanel.Children.Add(spinner);
            }
            if (!string.IsNullOrWhiteSpace(_reviewStatusMessage))
            {
                var status = new TextBlock
                {
                    Text = _reviewStatusMessage,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 4),
                };
                status.SetResourceReference(TextBlock.ForegroundProperty, "PriorityHigh");
                status.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
                _inspectorToolbarPanel.Children.Add(status);
            }
        }

        if (_review is not null)
        {
            var statusLabel = _review.Status switch
            {
                "revision-required" => "AI review: revision required",
                "ready-with-warnings" => "AI review: ready with warnings",
                _ => "AI review: ready",
            };
            var reviewStatus = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(_review.Summary)
                    ? statusLabel
                    : $"{statusLabel} · {_review.Summary}",
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 6, 4),
                ToolTip = ToolTipHelper.MakeThemedToolTip(
                    "This AI assessment is advisory. Deterministic schema violations are handled separately before staging."),
            };
            reviewStatus.SetResourceReference(
                TextBlock.ForegroundProperty,
                _review.Status == "revision-required" ? "PriorityHigh" :
                _review.Status == "ready-with-warnings" ? "ImportantText" : "SubtleText");
            reviewStatus.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            _inspectorToolbarPanel.Children.Add(reviewStatus);
        }

        if (_review?.Annotations is { Count: > 0 } annotations)
        {
            var previous = AddButton("← Prev", () => NavigateReview(_reviewIndex - 1));
            previous.IsEnabled = _reviewIndex > 0;
            var next = AddButton("Next →", () => NavigateReview(_reviewIndex + 1));
            next.IsEnabled = _reviewIndex >= 0 && _reviewIndex < annotations.Count - 1;
            var count = new TextBlock
            {
                Text = _reviewIndex >= 0
                    ? $"Review {_reviewIndex + 1} of {annotations.Count}"
                    : $"{annotations.Count} review notes",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 4),
            };
            count.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            count.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            _inspectorToolbarPanel.Children.Add(count);
        }
    }

    private bool CanEditSelectedElement() =>
        (_saveDefinitionEdit is not null && IsSelectedDefinitionEditable()) ||
        (_saveDefinitionEdit is null && _onGatesChanged is not null &&
         _selectedElement?.Kind is "gate" or "humanapproval");

    private bool CanApplyHistoryCandidate(
        DecomposedTaskGroup? candidate,
        out string? error)
    {
        error = null;
        if (candidate is null) return false;
        if (_durablePlan is null || _durablePlan.LifecycleStatus == PlanLifecycleStatus.Staged)
            return true;
        return StagedPlanEditor.TryValidateUnlockedChanges(_durablePlan, candidate, out error);
    }

    private bool IsSelectedDefinitionEditable()
    {
        if (_selectedElement is null) return DefinitionEditingAvailable;
        return _selectedElement.Kind switch
        {
            "task" => IsTaskDefinitionEditable(_selectedElement.Id),
            "validation" => IsValidationDefinitionEditable(_selectedElement.Id),
            "gate" or "humanapproval" => IsGateDefinitionEditable(_selectedElement.Id),
            _ => false,
        };
    }

    private bool DefinitionEditingAvailable =>
        _saveDefinitionEdit is not null &&
        (_durablePlan is null || !PlanLifecycleStatus.IsTerminal(_durablePlan.LifecycleStatus));

    private bool IsTaskDefinitionEditable(string taskId)
    {
        if (!DefinitionEditingAvailable || _durablePlan is null) return DefinitionEditingAvailable;
        var task = _durablePlan.Tasks.FirstOrDefault(value => value.TaskId == taskId);
        return task is null ||
               (task.Status == PlanTaskStatus.Pending &&
                !string.Equals(taskId, _durablePlan.Progress.ExecutingTaskId, StringComparison.Ordinal));
    }

    private bool IsValidationDefinitionEditable(string validationId)
    {
        if (!DefinitionEditingAvailable || _durablePlan is null) return DefinitionEditingAvailable;
        var validation = (_durablePlan.Validations ?? []).FirstOrDefault(value =>
            value.ValidationId == validationId);
        return validation is null || validation.Status == PlanValidationStatus.Pending;
    }

    private bool IsGateDefinitionEditable(string gateId)
    {
        if (!DefinitionEditingAvailable || _durablePlan is null) return DefinitionEditingAvailable;
        if (int.TryParse(gateId, out var gateIndex) &&
            gateIndex >= 0 && gateIndex < (_plan?.Group.ApprovalGates?.Count ?? 0))
            gateId = _plan!.Group.ApprovalGates![gateIndex].GateId;
        var gate = _durablePlan.ApprovalGates.FirstOrDefault(value => value.GateId == gateId);
        return gate is null || gate.Status == PlanGateStatus.Pending;
    }

    internal void SetReviewAnnotations(PlanVisualizerReviewPayload review)
    {
        if (_plan is null ||
            !string.Equals(review.GroupId, _plan.Group.GroupId, StringComparison.Ordinal) ||
            !string.Equals(review.BaseRevision, _plan.Revision, StringComparison.Ordinal))
            return;

        _reviewCallout?.Close();
        _reviewCallout = null;
        _isAiReviewPending = false;
        _reviewStatusMessage = null;
        var humanVerificationAnnotations = (review.HumanVerificationFindings ?? [])
            .Where(finding => !review.Annotations.Any(annotation =>
                annotation.TargetKind == "gate" &&
                string.Equals(annotation.TargetId, finding.GateId, StringComparison.Ordinal) &&
                annotation.Note.Contains(finding.Issue, StringComparison.OrdinalIgnoreCase)))
            .Select(finding => new PlanVisualizerReviewAnnotation(
                "gate",
                finding.GateId,
                finding.Severity,
                $"Human verification · {finding.RequirementId}",
                $"{finding.Issue} Recommendation: {finding.Recommendation}"))
            .ToArray();
        _review = humanVerificationAnnotations.Length == 0
            ? review
            : review with { Annotations = review.Annotations.Concat(humanVerificationAnnotations).ToArray() };
        _reviewIndex = _review.Annotations.Count > 0 ? 0 : -1;
        if (_reviewIndex >= 0)
            NavigateReview(_reviewIndex);
        else
            RefreshDetailPanel();
    }

    internal void SetLayoutNudges(Dictionary<string, double> nudges)
    {
        _taskNudges = new Dictionary<string, double>(nudges, StringComparer.Ordinal);
        if (_plan is not null)
            RebuildPreservingScroll(_plan, _durablePlan);
    }

    internal void SetReviewRequestError(string error)
    {
        _isAiReviewPending = false;
        _reviewStatusMessage = error;
        RefreshDetailPanel();
    }

    internal void SetActionStatus(string title, string message) =>
        ShowActionStatus(title, message);

    internal void SelectPlanDescriptionForHostAction()
    {
        _isInspectorEditing = false;
        _selectedElement = null;
        ClearSelectionVisual();
        RefreshDetailPanel();
    }

    internal bool SelectTaskDescriptionForHostAction(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            return false;
        var hasTask = _durablePlan?.Tasks.Any(task =>
                          string.Equals(task.TaskId, taskId, StringComparison.Ordinal))
                      ?? _plan?.Group.Tasks.Any(task =>
                          string.Equals(task.Id, taskId, StringComparison.Ordinal))
                      ?? false;
        if (!hasTask)
            return false;
        SelectElement("task", taskId);
        return true;
    }

    internal int ProbeVisibleInspectorMatchesForHostAction(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (_detailDocument is null || _descriptionSession is null)
            throw new InvalidOperationException("Inspector document is not ready for search probing.");
        _descriptionSession.FindNext(query);
        return PlanDescriptionDocumentSession.FindAll(_detailDocument, query).Count;
    }

    internal string? GetSelectedInspectorTaskIdForHostAction() =>
        string.Equals(_selectedElement?.Kind, "task", StringComparison.Ordinal)
            ? _selectedElement!.Id
            : null;

    private void ClearActionStatus()
    {
        _actionStatusMessage = null;
        if (_actionStatusText is null) return;
        _actionStatusText.Text = string.Empty;
        _actionStatusText.Visibility = Visibility.Collapsed;
    }

    private void EnsureActionStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(_actionStatusMessage))
            ShowActionStatus("Action not completed", message);
    }

    private void ShowActionStatus(string title, string message)
    {
        _actionStatusMessage = string.IsNullOrWhiteSpace(title)
            ? message
            : $"{title}: {message}";
        if (_actionStatusText is null) return;
        _actionStatusText.Text = _actionStatusMessage;
        _actionStatusText.Visibility = Visibility.Visible;
        _actionStatusText.BringIntoView();
    }

    private void NavigateReview(int requestedIndex)
    {
        if (_review?.Annotations is not { Count: > 0 } annotations) return;
        var index = Math.Clamp(requestedIndex, 0, annotations.Count - 1);
        var annotation = annotations[index];
        _reviewIndex = index;
        _isInspectorEditing = false;
        _selectedElement = annotation.TargetKind == "plan"
            ? null
            : new SelectedElementIdentity(
                annotation.TargetKind == "gate" ? "humanapproval" : annotation.TargetKind,
                annotation.TargetId);
        ApplySelectionVisual();
        RefreshDetailPanel();

        Dispatcher.BeginInvoke(() =>
        {
            var target = FindReviewTarget(annotation);
            if (target is null) return;
            target.BringIntoView();
            _reviewCallout?.Close();
            var severity = string.IsNullOrWhiteSpace(annotation.Severity)
                ? "Suggestion"
                : char.ToUpperInvariant(annotation.Severity[0]) + annotation.Severity[1..];
            _reviewCallout = FrmUltimateCallout.ShowCalloutBesideTarget(
                $"**{severity}: {annotation.Title}**\n\n{annotation.Note}",
                target,
                width: 380,
                fontSize: _quickReplyFontSize + 1);
            if (_reviewCallout is null) return;
            _reviewCallout.IsTourFirstStep = index == 0;
            _reviewCallout.IsTourLastStep = index == annotations.Count - 1;
            _reviewCallout.TourActionLabel = ReviewActionLabel(annotation);
            _reviewCallout.IsTourActionDestructive = IsDestructiveReviewChange(annotation);
            _reviewCallout.TourPrevRequested += (_, _) => NavigateReview(_reviewIndex - 1);
            _reviewCallout.TourNextRequested += (_, _) => NavigateReview(_reviewIndex + 1);
            _reviewCallout.TourActionRequested += (_, _) => ApplyActiveReviewChange();
            _reviewCallout.IsTourMode = true;
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static bool IsDestructiveReviewChange(PlanVisualizerReviewAnnotation annotation) =>
        annotation.Change?.Op is "removeTask" or "removeApprovalGate" or "removeValidation";

    private static string? ReviewActionLabel(PlanVisualizerReviewAnnotation annotation)
    {
        if (annotation.Change is null) return null;
        if (!string.IsNullOrWhiteSpace(annotation.ActionLabel)) return annotation.ActionLabel;
        return IsDestructiveReviewChange(annotation) ? "Delete" : "Apply";
    }

    private void ApplyActiveReviewChange()
    {
        if (_plan is null || _review?.Annotations is not { Count: > 0 } annotations ||
            _reviewIndex < 0 || _reviewIndex >= annotations.Count)
            return;
        var annotation = annotations[_reviewIndex];
        if (annotation.Change is null) return;

        var proposal = new StagedPlanRevisionProposalPayload(
            _plan.Group.GroupId,
            _plan.Revision,
            annotation.Title,
            [annotation.Change]);
        var result = StagedPlanRevisionApplier.Apply(_plan, proposal);
        if (result.Outcome is not (StagedPlanRevisionOutcome.Applied or StagedPlanRevisionOutcome.NoChanges) ||
            result.RevisedGroup is null)
        {
            UIErrorHelper.ShowError("Apply Review Suggestion",
                result.Error ?? "The suggested change is no longer valid.", this);
            return;
        }

        var remaining = annotations.Where((_, index) => index != _reviewIndex).ToArray();
        var nextIndex = Math.Min(_reviewIndex, remaining.Length - 1);
        if (!ApplyStagedMutation(result.RevisedGroup, preserveReview: true)) return;
        if (_plan is null) return;
        _reviewCallout?.Close();
        _reviewCallout = null;
        _review = remaining.Length == 0
            ? null
            : _review with { BaseRevision = _plan.Revision, Annotations = remaining };
        _reviewIndex = nextIndex;
        _saveReviewState?.Invoke(_review);
        if (_review is not null && nextIndex >= 0)
            NavigateReview(nextIndex);
        else
            RefreshDetailPanel();
    }

    private FrameworkElement? FindReviewTarget(PlanVisualizerReviewAnnotation annotation)
    {
        if (annotation.TargetKind == "plan") return _header;
        if (_graphCanvas is null) return null;
        var tags = annotation.TargetKind == "gate"
            ? new[] { $"humanapproval:{annotation.TargetId}", $"gate:{annotation.TargetId}" }
            : new[] { $"{annotation.TargetKind}:{annotation.TargetId}" };
        return _graphCanvas.Children.OfType<FrameworkElement>()
            .FirstOrDefault(element => element.Tag is string tag &&
                tags.Contains(tag, StringComparer.Ordinal));
    }

    private void AppendActiveReviewNote()
    {
        if (_detailDocument is null || _review?.Annotations is not { Count: > 0 } annotations ||
            _reviewIndex < 0 || _reviewIndex >= annotations.Count)
            return;
        var annotation = annotations[_reviewIndex];
        var heading = new Paragraph(new Run($"AI review — {annotation.Title}")
        {
            FontWeight = FontWeights.Bold,
        }) { Margin = new Thickness(0, 12, 0, 3) };
        heading.SetResourceReference(TextElement.ForegroundProperty,
            annotation.Severity == "error" ? "PriorityHigh" : "ImportantText");
        var note = new Paragraph(new Run(annotation.Note)) { Margin = new Thickness(0, 0, 0, 8) };
        note.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
        _detailDocument.Blocks.Add(heading);
        _detailDocument.Blocks.Add(note);
    }

    private bool TryPopulateStagedEditor()
    {
        if (_detailDocument is null || _plan is null || _saveDefinitionEdit is null ||
            !IsSelectedDefinitionEditable())
            return false;

        if (_selectedElement is null) return PopulatePlanEditor();

        return _selectedElement.Kind switch
        {
            "task" => PopulateTaskEditor(_selectedElement.Id),
            "validation" => PopulateValidationEditor(_selectedElement.Id),
            "gate" or "humanapproval" => PopulateGateEditor(_selectedElement.Id),
            _ => false,
        };
    }

    private bool PopulatePlanEditor()
    {
        if (_plan is null || _detailDocument is null) return false;
        _detailDocument.Blocks.Clear();
        var panel = CreateEditorPanel("Edit plan", out var status);
        var title = AddEditorField(panel, "Plan title", _plan.Group.GroupTitle, 34);
        _inspectorPrimaryEditor = title;
        var branch = AddEditorField(panel, "Branch", _plan.Group.Branch, 34);
        if (_durablePlan is not null && StagedPlanEditor.IsPlanBranchLocked(_durablePlan))
        {
            branch.IsReadOnly = true;
            branch.ToolTip = ToolTipHelper.MakeThemedToolTip(
                "The branch is fixed after plan execution starts.");
        }
        var summary = AddEditorField(panel, "Summary", _plan.Group.Summary, 140);
        var delivery = AddEditorField(panel, "Delivery", _plan.Group.Delivery, 70);
        AddEditorActions(panel, status, () =>
        {
            if (string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(branch.Text) ||
                string.IsNullOrWhiteSpace(summary.Text))
                return "Plan title, branch, and summary are required.";
            return ApplyStagedMutation(StagedPlanEditor.UpdatePlan(
                    _plan.Group, title.Text, branch.Text, summary.Text, delivery.Text),
                    showFailureDialog: false)
                ? null
                : _lastStagedMutationError;
        });
        _detailDocument.Blocks.Add(new BlockUIContainer(panel));
        return true;
    }

    private bool PopulateTaskEditor(string taskId)
    {
        var task = _plan?.Group.Tasks.FirstOrDefault(value => value.Id == taskId);
        if (task is null || _detailDocument is null) return false;
        _detailDocument.Blocks.Clear();
        var panel = CreateEditorPanel("Edit step", out var status);
        var title = AddEditorField(panel, "Title", task.Title, 34);
        _inspectorPrimaryEditor = title;
        var description = AddEditorField(panel, "Description (Markdown supported)", task.Description, 170);
        var contractExamples = CreateContractExampleEditor(panel, task.ContractExamples);
        CheckBox? parallel = null;
        // Parallel eligibility remains part of the durable plan contract. Re-enable this
        // editor control when the native plan scheduler supports concurrent task execution.
        if (PlanVisualizerFeatures.ParallelTaskExecutionEditingEnabled)
        {
            parallel = new CheckBox
            {
                Content = "Parallel eligible (indeterminate means unspecified)",
                IsThreeState = true,
                IsChecked = task.ParallelEligible,
                Margin = new Thickness(0, 8, 0, 0),
            };
            parallel.SetResourceReference(CheckBox.ForegroundProperty, "BodyText");
            panel.Children.Add(parallel);
        }
        AddEditorActions(panel, status, () =>
        {
            if (string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(description.Text))
                return "Title and description are required.";
            var updated = StagedPlanEditor.UpdateTask(
                _plan!.Group, taskId, title.Text, description.Text, task.Priority, task.DependsOn,
                BuildContractExamples(contractExamples));
            if (parallel is not null)
            {
                updated = StagedPlanEditor.UpdateTaskAdvanced(
                    updated, taskId, task.AgentAssignments, task.AgentRoutingMode, parallel.IsChecked,
                    task.GenericAgentReason, task.ExecutionMode);
            }
            return ApplyStagedMutation(updated, showFailureDialog: false)
                ? null
                : _lastStagedMutationError;
        });
        _detailDocument.Blocks.Add(new BlockUIContainer(panel));
        return true;
    }

    private List<ContractExampleEditorRow> CreateContractExampleEditor(
        StackPanel panel,
        IReadOnlyList<DecomposedTaskContractExample>? examples)
    {
        var heading = new TextBlock
        {
            Text = "Contract examples (optional)",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 3),
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        heading.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        panel.Children.Add(heading);

        var help = new TextBlock
        {
            Text = "Add compact, normative examples when they clarify the task contract. " +
                   "Include enough literal source, operation, and outcome to turn into a test without inventing behavior.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        };
        help.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        help.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        panel.Children.Add(help);

        var cardHost = new StackPanel();
        panel.Children.Add(cardHost);
        var rows = new List<ContractExampleEditorRow>();
        foreach (var example in examples ?? [])
            AddContractExampleCard(cardHost, rows, example);

        var addButton = TranscriptQuickReplyFactory.CreateButton(
            "Add example",
            _quickReplyFontSize,
            toolTip: ToolTipHelper.MakeThemedToolTip(
                "Add an input and expected-behavior example to this task."));
        addButton.SetResourceReference(Control.FontSizeProperty, "FontSizeBody");
        addButton.HorizontalAlignment = HorizontalAlignment.Left;
        addButton.Margin = new Thickness(0, 6, 0, 2);
        addButton.Click += (_, _) =>
        {
            var row = AddContractExampleCard(cardHost, rows);
            row.Card.BringIntoView();
            row.Title.Focus();
            Keyboard.Focus(row.Title);
        };
        panel.Children.Add(addButton);
        return rows;
    }

    private ContractExampleEditorRow AddContractExampleCard(
        StackPanel cardHost,
        List<ContractExampleEditorRow> rows,
        DecomposedTaskContractExample? example = null)
    {
        var content = new StackPanel();
        var cardHeader = new DockPanel { LastChildFill = true };
        var deleteButton = TranscriptQuickReplyFactory.CreateButton(
            "Delete",
            _quickReplyFontSize,
            toolTip: ToolTipHelper.MakeThemedToolTip(
                "Remove this example from the draft. Save changes to apply the removal."),
            tone: QuickReplyTone.Destructive);
        deleteButton.SetResourceReference(Control.FontSizeProperty, "FontSizeBody");
        deleteButton.Margin = new Thickness(8, 0, 0, 0);
        deleteButton.Padding = new Thickness(8, 2, 8, 2);
        deleteButton.MinHeight = 24;
        DockPanel.SetDock(deleteButton, Dock.Right);
        cardHeader.Children.Add(deleteButton);

        var cardHeading = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        cardHeading.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        cardHeading.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        cardHeader.Children.Add(cardHeading);
        content.Children.Add(cardHeader);

        var exampleId = example?.ExampleId ?? NextContractExampleId(rows);
        var title = AddEditorField(content, "Title", example?.Title, 34);
        var input = AddEditorField(content, "Exact input / setup", example?.Input, 90);
        var expectedBehavior = AddEditorField(
            content,
            "Exact expected result / error",
            example?.ExpectedBehavior,
            90);
        var language = AddEditorField(content, "Language (optional)", example?.Language, 34);
        input.FontFamily = new FontFamily("Consolas");
        expectedBehavior.FontFamily = new FontFamily("Consolas");

        var card = new Border
        {
            Child = content,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 4, 0, 4),
        };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBackground");
        card.SetResourceReference(Border.BorderBrushProperty, "LineColor");

        var row = new ContractExampleEditorRow(
            card, cardHeading, exampleId, title, input, expectedBehavior, language);
        rows.Add(row);
        cardHost.Children.Add(card);
        RenumberContractExampleCards(rows);
        deleteButton.Click += (_, _) =>
        {
            rows.Remove(row);
            cardHost.Children.Remove(card);
            RenumberContractExampleCards(rows);
        };
        return row;
    }

    private static string NextContractExampleId(IReadOnlyList<ContractExampleEditorRow> rows)
    {
        var existing = rows.Select(row => row.ExampleId).ToHashSet(StringComparer.Ordinal);
        for (var number = 1;; number++)
        {
            var candidate = $"example-{number}";
            if (!existing.Contains(candidate)) return candidate;
        }
    }

    private static void RenumberContractExampleCards(IReadOnlyList<ContractExampleEditorRow> rows)
    {
        for (var index = 0; index < rows.Count; index++)
            rows[index].Heading.Text = $"Example {index + 1}";
    }

    private static IReadOnlyList<DecomposedTaskContractExample> BuildContractExamples(
        IReadOnlyList<ContractExampleEditorRow> rows) =>
        rows.Select(row => new DecomposedTaskContractExample(
            row.ExampleId,
            row.Title.Text.Trim(),
            row.Input.Text,
            row.ExpectedBehavior.Text,
            string.IsNullOrWhiteSpace(row.Language.Text) ? null : row.Language.Text.Trim()))
            .ToArray();

    private bool PopulateValidationEditor(string validationId)
    {
        var validation = _plan?.Group.Validations?.FirstOrDefault(value =>
            value.ValidationId == validationId);
        if (validation is null || _detailDocument is null) return false;
        _detailDocument.Blocks.Clear();
        var panel = CreateEditorPanel("Edit validation", out var status);
        var title = AddEditorField(panel, "Title", validation.Title, 34);
        _inspectorPrimaryEditor = title;
        var description = AddEditorField(panel, "Description", validation.Description, 100);
        var mode = AddEditorField(panel, "Mode", validation.Mode, 34);
        var after = AddEditorField(panel, "After step IDs", string.Join(Environment.NewLine, validation.AfterTaskIds), 64);
        var before = AddEditorField(panel, "Before step IDs", string.Join(Environment.NewLine, validation.BeforeTaskIds), 64);
        var assertions = AddEditorField(panel, "Assertions (one per line)", string.Join(Environment.NewLine, validation.Assertions), 100);
        var commands = AddEditorField(panel, "Commands (one per line)", string.Join(Environment.NewLine, validation.Commands ?? []), 80);
        var outputIds = AddEditorField(panel, "Output IDs", string.Join(Environment.NewLine, validation.OutputIds ?? []), 64);
        var revalidate = new CheckBox
        {
            Content = "Revalidate at completion",
            IsChecked = validation.RevalidateAtCompletion,
            Margin = new Thickness(0, 8, 0, 0),
        };
        revalidate.SetResourceReference(CheckBox.ForegroundProperty, "BodyText");
        panel.Children.Add(revalidate);
        AddEditorActions(panel, status, () =>
        {
            if (string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(description.Text) ||
                ParseEditorList(assertions.Text).Count == 0)
                return "Title, description, and at least one assertion are required.";
            var updatedGroup = StagedPlanEditor.UpdateValidation(
                _plan!.Group, validationId, title.Text, description.Text, mode.Text,
                ParseEditorList(after.Text), ParseEditorList(before.Text),
                ParseEditorLines(assertions.Text), ParseEditorLines(commands.Text),
                revalidate.IsChecked != false, ParseEditorList(outputIds.Text));
            // When an audit is present, its completion frontier must continue to match the
            // current leaf set after structural edits.
            updatedGroup = DependencyEditRecomputer.ReconcileAuditBoundary(updatedGroup);
            return ApplyStagedMutation(updatedGroup, showFailureDialog: false)
                ? null
                : _lastStagedMutationError;
        });
        _detailDocument.Blocks.Add(new BlockUIContainer(panel));
        return true;
    }

    private bool PopulateGateEditor(string gateIdentity)
    {
        if (_plan is null || _detailDocument is null) return false;
        DecomposedGate? gate = null;
        if (int.TryParse(gateIdentity, out var gateIndex) &&
            gateIndex >= 0 && gateIndex < (_plan.Group.ApprovalGates?.Count ?? 0))
            gate = _plan.Group.ApprovalGates![gateIndex];
        gate ??= _plan.Group.ApprovalGates?.FirstOrDefault(value => value.GateId == gateIdentity);
        if (gate is null) return false;

        _detailDocument.Blocks.Clear();
        var panel = CreateEditorPanel("Edit human approval", out var status);
        var message = AddEditorField(panel, "Message", gate.Message, 75);
        _inspectorPrimaryEditor = message;
        var question = AddEditorField(panel, "Approval question", gate.Question, 75);
        var after = AddEditorField(panel, "After step IDs", string.Join(Environment.NewLine, gate.AfterTaskIds ?? []), 64);
        var before = AddEditorField(panel, "Before step IDs", string.Join(Environment.NewLine, gate.BeforeTaskIds ?? []), 64);
        var proofs = AddEditorField(
            panel,
            "Proof requirements (ID | type | title | description | question)",
            string.Join(Environment.NewLine, (gate.ProofRequirements ?? []).Select(value =>
                $"{value.RequirementId} | {value.ProofType} | {value.Title} | {value.Description} | {value.Question}")),
            100);
        var gateId = gate.GateId;
        AddEditorActions(panel, status, () =>
        {
            if (string.IsNullOrWhiteSpace(message.Text)) return "Message is required.";
            if (!TryParseProofRequirements(proofs.Text, out var parsedProofs, out var proofError))
                return proofError;
            return ApplyStagedMutation(StagedPlanEditor.UpdateGate(
                    _plan!.Group, gateId, message.Text, question.Text,
                    ParseEditorList(after.Text), ParseEditorList(before.Text), parsedProofs),
                    showFailureDialog: false)
                ? null
                : _lastStagedMutationError;
        });
        _detailDocument.Blocks.Add(new BlockUIContainer(panel));
        return true;
    }

    private StackPanel CreateEditorPanel(string heading, out TextBlock status)
    {
        var panel = new StackPanel();
        var title = new TextBlock { Text = heading, FontWeight = FontWeights.Bold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeLarge");
        panel.Children.Add(title);
        status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "PriorityHigh");
        status.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        return panel;
    }

    private static TextBox AddEditorField(
        Panel panel, string labelText, string? value, double minHeight)
    {
        var label = new TextBlock
        {
            Text = labelText,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 3),
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        label.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        var editor = new TextBox
        {
            Text = value ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = minHeight > 40,
            MinHeight = minHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(6, 4, 6, 4),
        };
        editor.SetResourceReference(TextBox.ForegroundProperty, "ImportantText");
        editor.SetResourceReference(TextBox.BackgroundProperty, "PanelBackground");
        editor.SetResourceReference(TextBox.BorderBrushProperty, "LineColor");
        editor.SetResourceReference(TextBox.FontSizeProperty, "FontSizeBody");
        editor.SetResourceReference(TextBox.CaretBrushProperty, "ImportantText");
        panel.Children.Add(label);
        panel.Children.Add(editor);
        return editor;
    }

    private void FocusPrimaryInspectorEditorIfRequested(int expectedBuildSequence)
    {
        if (!_focusPrimaryEditorAfterRebuild || _inspectorPrimaryEditor is null)
            return;

        var editor = _inspectorPrimaryEditor;
        _focusPrimaryEditorAfterRebuild = false;
        Dispatcher.BeginInvoke(() =>
        {
            if (_buildSeq != expectedBuildSequence ||
                !ReferenceEquals(_inspectorPrimaryEditor, editor) ||
                !editor.IsVisible)
                return;

            editor.Focus();
            Keyboard.Focus(editor);
            editor.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void AddEditorActions(StackPanel panel, TextBlock status, Func<string?> save)
    {
        _inspectorEditorSaveAction = save;
        _inspectorEditorStatus = status;
        panel.Children.Add(status);
    }

    private static IReadOnlyList<string> ParseEditorList(string value) =>
        value.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ParseEditorLines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

    private static bool TryParseProofRequirements(
        string value,
        out IReadOnlyList<DecomposedTaskProofRequirement> requirements,
        out string? error)
    {
        var result = new List<DecomposedTaskProofRequirement>();
        foreach (var line in ParseEditorLines(value))
        {
            var parts = line.Split('|', 5, StringSplitOptions.TrimEntries);
            if (parts.Length < 3 || parts.Take(3).Any(string.IsNullOrWhiteSpace))
            {
                requirements = [];
                error = "Each proof requirement must be: ID | type | title | description | optional question.";
                return false;
            }
            result.Add(new DecomposedTaskProofRequirement(
                parts[0], parts[1], parts.Length >= 4 ? parts[3] : string.Empty,
                parts.Length == 5 && !string.IsNullOrWhiteSpace(parts[4]) ? parts[4] : null,
                Title: parts[2]));
        }
        requirements = result;
        error = null;
        return true;
    }

    private void PopulateTaskDetail(PlanTask task, Plan? plan)
    {
        if (_detailDocument is null)
            return;

        _detailDocument.Blocks.Clear();

        // 1. Title
        var titlePara = new Paragraph(new Run(task.Title ?? task.TaskId) { FontWeight = FontWeights.Bold });
        titlePara.SetResourceReference(TextElement.ForegroundProperty, "TaskTitle");
        titlePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeTitle");
        titlePara.Margin = new Thickness(0, 0, 0, 4);
        _detailDocument.Blocks.Add(titlePara);

        // 2. Status: lowercase in parentheses, no icon; active = ImportantText, others = SubtleText
        var statusColorKey = (string.Equals(task.Status, PlanTaskStatus.Executing, StringComparison.Ordinal)
            || PlanTaskStatus.IsVerifying(task.Status)) ? "ImportantText" : "SubtleText";
        var statusRun = new Run($"({FormatTaskStatus(task.Status)?.ToLowerInvariant()})");
        statusRun.SetResourceReference(TextElement.ForegroundProperty, statusColorKey);
        var statusPara = new Paragraph(statusRun);
        statusPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        statusPara.Margin = new Thickness(0, 0, 0, 8);
        _detailDocument.Blocks.Add(statusPara);

        if (task.ExecutionOrigin is { } origin)
        {
            AddSectionHeader("Task Origin");
            var originTask = plan?.Tasks.FirstOrDefault(candidate =>
                string.Equals(candidate.TaskId, origin.OriginTaskId, StringComparison.Ordinal));
            var originLabel = originTask is null
                ? origin.OriginTaskId
                : $"Step {originTask.DisplayStepLabel ?? origin.OriginTaskId}: {originTask.Title ?? originTask.TaskId}";
            var values = new (string Label, string Value)[]
            {
                ("Discovered during", originLabel),
                ("Reported by", origin.ReportedByAgent),
                ("Reason", origin.Reason),
                ("Added", origin.AddedAt.ToLocalTime().ToString("g")),
            };
            foreach (var value in values)
            {
                var paragraph = new Paragraph();
                paragraph.Inlines.Add(new Run(value.Label + ": ") { FontWeight = FontWeights.SemiBold });
                paragraph.Inlines.Add(new Run(value.Value));
                paragraph.SetResourceReference(TextElement.ForegroundProperty, "LabelText");
                paragraph.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                paragraph.Margin = new Thickness(0, 0, 0, 3);
                _detailDocument.Blocks.Add(paragraph);
            }
        }

        // 3. The task contract comes before its later execution evidence. Pending work remains
        // editable and is therefore simply "Task Description"; once work has begun, the wording
        // makes clear that this is the original contract rather than the worker's result.
        if (!string.IsNullOrWhiteSpace(task.Description))
        {
            AddSectionHeader(string.Equals(task.Status, PlanTaskStatus.Pending, StringComparison.Ordinal)
                ? "Task Description"
                : "Original Task Description");
            AppendTaskDescription(task.Description, task.TaskId);
        }

        if (task.ContractExamples is { Count: > 0 } contractExamples)
        {
            AddSectionHeader("Normative Contract Examples");
            foreach (var example in contractExamples)
            {
                var exampleTitle = new Paragraph(new Run(example.Title)
                {
                    FontWeight = FontWeights.SemiBold,
                });
                exampleTitle.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
                exampleTitle.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                exampleTitle.Margin = new Thickness(0, 0, 0, 3);
                if (!string.IsNullOrWhiteSpace(example.Language))
                    exampleTitle.Inlines.Add(new Run($" ({example.Language})"));
                _detailDocument.Blocks.Add(exampleTitle);

                AppendContractExampleValue("Input", example.Input);
                AppendContractExampleValue("Expected behavior", example.ExpectedBehavior);
            }
        }

        // 4. Human approval follows the work description because it is performed after that work.
        if (plan is not null)
            AppendApprovalGuidance(PlanTaskInspectorPresentation.SelectApprovalGuidanceGates(task, plan));

        AppendTaskActivitySummary(PlanTaskInspectorPresentation.BuildActivitySummary(
            task,
            plan,
            _isTaskActivityActive?.Invoke(task.TaskId) ?? true,
            _recoveryAssessmentTaskIds.Contains(task.TaskId) ||
                (_isRecoveryAssessmentActive?.Invoke(task.TaskId) ?? false),
            ResolveTaskCorrectionActivity(task.TaskId) == PlanTaskCorrectionActivityState.Running));
        AppendWorkMetricsLink("task", task.TaskId);

        // 5. Completion Summary. Legacy prose is deliberately one
        // bullet; sentence boundaries are not completion-item boundaries.
        var completionItems = task.CompletionSummaryItems is { Count: > 0 }
            ? task.CompletionSummaryItems.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray()
            : !string.IsNullOrWhiteSpace(task.CompletionSummary)
                ? [task.CompletionSummary]
                : task.Handoff is { Summary.Length: > 0 } legacyHandoff
                    ? [legacyHandoff.Summary]
                    : [];
        if (completionItems.Length > 0)
        {
            AddSectionHeader("Completion Summary");
            var summaryList = new List { MarkerStyle = TextMarkerStyle.Disc };
            summaryList.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
            summaryList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            summaryList.Margin = new Thickness(0, 0, 0, 12);
            foreach (var item in completionItems)
                summaryList.ListItems.Add(new ListItem(PlanCompletionSummaryFormatter.CreateParagraph(
                    item, _taskOrdinalById, CreateTaskLink)));
            _detailDocument.Blocks.Add(summaryList);
        }

        if (string.Equals(
                task.CompletionKind ?? task.Handoff?.CompletionKind,
                DecomposeStepCompletionKind.VerifiedNoOp,
                StringComparison.Ordinal))
        {
            AddSectionHeader("Completion evidence");
            var noOpPara = new Paragraph();
            var noOpLabel = new Run("Already satisfied") { FontWeight = FontWeights.SemiBold };
            noOpLabel.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
            noOpPara.Inlines.Add(noOpLabel);
            var evaluatedCommit = task.EvaluatedCommit ?? task.Handoff?.EvaluatedCommit;
            if (!string.IsNullOrWhiteSpace(evaluatedCommit))
                noOpPara.Inlines.Add(new Run($" at evaluated snapshot {ProofProvenancePresenter.FormatShortSha(evaluatedCommit)}"));
            noOpPara.SetResourceReference(TextElement.ForegroundProperty, "LabelText");
            noOpPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            noOpPara.Margin = new Thickness(0, 0, 0, 6);
            _detailDocument.Blocks.Add(noOpPara);

            var noOpEvidence = task.NoOpEvidence ?? task.Handoff?.NoOpEvidence;
            if (noOpEvidence is { Count: > 0 })
            {
                var evidenceList = new List { MarkerStyle = TextMarkerStyle.Disc };
                evidenceList.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
                evidenceList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                evidenceList.Margin = new Thickness(0, 0, 0, 12);
                foreach (var evidence in noOpEvidence)
                    evidenceList.ListItems.Add(new ListItem(new Paragraph(new Run(
                        $"{evidence.Requirement}: {evidence.Observation} ({string.Join(", ", evidence.Locations)})"))));
                _detailDocument.Blocks.Add(evidenceList);
            }
        }

        // 6. Commit evidence
        var detailEvidence = _durablePlan is null
            ? Array.Empty<PlanEvidenceCommit>()
            : PlanRecoveryPresentationBuilder.ResolveTaskEvidence(_durablePlan, task).ToArray();
        if (detailEvidence.Length > 0)
        {
            var commits = detailEvidence;
            AddSectionHeader(commits.Length > 1 ? "Commits" : "Commit");
            var label = commits.Length > 1
                ? $"{commits.Length} commits"
                : ProofProvenancePresenter.FormatShortSha(commits[0].Commit) ?? commits[0].Commit;
            var commitPara = new Paragraph();
            var commitLink = new Hyperlink(new Run(label) { FontFamily = new FontFamily("Consolas") })
            {
                Cursor = Cursors.Hand,
                ToolTip = ToolTipHelper.MakeThemedToolTip(commits.Length > 1 ? "Review commits and changed files" : "Open this commit"),
            };
            commitLink.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
            if (commits.Length > 1 && _onOpenCommits is not null)
                commitLink.Click += (_, _) => _onOpenCommits(commits);
            else if (_onOpenCommit is not null)
                commitLink.Click += (_, _) => _onOpenCommit(commits[0].Commit);
            commitPara.Inlines.Add(commitLink);
            commitPara.SetResourceReference(TextElement.ForegroundProperty, "LabelText");
            commitPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            commitPara.Margin = new Thickness(0, 0, 0, 12);
            _detailDocument.Blocks.Add(commitPara);
        }

        // 7. Unique handoff details. The handoff commit, summary, and worker-reported
        // verification duplicate the canonical completion and independent verification above.
        if (task.Handoff is { } handoff)
        {
            if (handoff.ChangedFiles is { Count: > 0 })
            {
                AddSectionHeader("Changed Files");
                var filesPanel = BuildChangedFilesPanel(task,
                    handoff.ChangedFiles.Select(path => new PlanTaskFileChange(path, FileChangeStatus.Modified)).ToArray());
                _detailDocument.Blocks.Add(new BlockUIContainer(filesPanel) { Margin = new Thickness(0, 0, 0, 12) });
                _ = RefreshChangedFilesAsync(task, filesPanel);
            }

            if (handoff.DeferredWork is { Count: > 0 })
            {
                var deferHeader = new Paragraph(new Run("Deferred Work:") { FontWeight = FontWeights.SemiBold });
                deferHeader.SetResourceReference(TextElement.ForegroundProperty, "LabelText");
                deferHeader.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                _detailDocument.Blocks.Add(deferHeader);

                var deferList = new List();
                deferList.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
                deferList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
                deferList.MarkerStyle = TextMarkerStyle.Disc;
                foreach (var dw in handoff.DeferredWork)
                {
                    var dwPara = new Paragraph();
                    dwPara.Inlines.Add(new Run(dw.Requirement) { FontWeight = FontWeights.SemiBold });
                    dwPara.Inlines.Add(new Run($" — {dw.Reason}"));
                    deferList.ListItems.Add(new ListItem(dwPara));
                }
                _detailDocument.Blocks.Add(deferList);
            }

        }

        // 8. Verification
        var humanAcceptanceGates = ResolveHumanAcceptanceGates(task, plan);
        if (task.VerificationHistory is { Count: > 0 } || humanAcceptanceGates.Count > 0)
        {
            AddSectionHeader("Verification");
            foreach (var report in task.VerificationHistory ?? [])
            {
                var verdictPara = new Paragraph();
                var accepted = report.Verdict.Equals(PlanTaskVerificationVerdict.Accepted, StringComparison.OrdinalIgnoreCase);
                var verdictIcon = report.Verdict.Equals(PlanTaskVerificationVerdict.ReworkRequired, StringComparison.OrdinalIgnoreCase)
                    ? VerificationIconKind.Failed
                    : accepted ? VerificationIconKind.Supported : VerificationIconKind.Unclear;
                if (accepted)
                    verdictPara.Inlines.Add(new Run(VerificationCheckLeadingSpace));
                verdictPara.Inlines.Add(new InlineUIContainer(CreateVerificationIcon(
                    verdictIcon))
                {
                    BaselineAlignment = BaselineAlignment.Center,
                });
                var verdictLabel = report.Verdict switch
                {
                    PlanTaskVerificationVerdict.Accepted => "Verified",
                    PlanTaskVerificationVerdict.ReworkRequired => "Rework required",
                    PlanTaskVerificationVerdict.HumanReviewRequired => "Human review required",
                    _ => report.Verdict,
                };
                var verdictRun = new Run($"  {verdictLabel}") { FontWeight = FontWeights.SemiBold };
                verdictRun.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
                verdictPara.Inlines.Add(verdictRun);
                verdictPara.Inlines.Add(new Run($"  ({report.CompletedAt.ToLocalTime():yyyy-MM-dd HH:mm})"));
                verdictPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                _detailDocument.Blocks.Add(verdictPara);

                if (!string.IsNullOrWhiteSpace(report.Summary))
                {
                    var rSummaryPara = new Paragraph(new Run(report.Summary));
                    rSummaryPara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
                    rSummaryPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
                    rSummaryPara.Margin = new Thickness(8, 0, 0, 4);
                    _detailDocument.Blocks.Add(rSummaryPara);
                }

                if (report.ClaimFindings is { Count: > 0 })
                {
                    foreach (var finding in report.ClaimFindings)
                    {
                        var fPara = new Paragraph();
                        if (IsSupportedVerificationDisposition(finding.Disposition))
                            fPara.Inlines.Add(new Run(VerificationCheckLeadingSpace));
                        fPara.Inlines.Add(new InlineUIContainer(CreateVerificationClaimIcon(finding.Disposition))
                        {
                            BaselineAlignment = BaselineAlignment.Center,
                        });
                        fPara.Inlines.Add(new Run("  "));
                        fPara.Inlines.Add(new Run(finding.Claim));
                        fPara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
                        fPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
                        fPara.Margin = new Thickness(8, 1, 0, 1);
                        _detailDocument.Blocks.Add(fPara);

                        if (!string.IsNullOrWhiteSpace(finding.Evidence))
                        {
                            var evidencePara = new Paragraph(new Run(finding.Evidence));
                            evidencePara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                            evidencePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
                            evidencePara.Margin = new Thickness(31, 0, 0, 5);
                            _detailDocument.Blocks.Add(evidencePara);
                        }
                    }
                }
            }

            AppendHumanVerificationAcceptance(humanAcceptanceGates);
        }

        // 9. Proof Evidence
        if (task.ProofEvidence is { Count: > 0 })
        {
            AddSectionHeader("Proof Evidence");
            var proofList = new List();
            proofList.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
            proofList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
            proofList.MarkerStyle = TextMarkerStyle.None;
            proofList.Margin = new Thickness(0, 0, 0, 12);
            foreach (var proof in task.ProofEvidence)
            {
                var proofPara = new Paragraph();
                proofPara.Inlines.Add(new Run($"[{proof.ProofType}] ") { FontWeight = FontWeights.SemiBold });
                proofPara.Inlines.Add(new Run($"{proof.RequirementId}: {proof.Summary}"));
                proofList.ListItems.Add(new ListItem(proofPara));
            }
            _detailDocument.Blocks.Add(proofList);
        }

        // 10. Dependencies
        if (task.DependsOn is { Count: > 0 })
        {
            AddSectionHeader("Dependencies");
            var depList = new List();
            depList.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
            depList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            depList.MarkerStyle = TextMarkerStyle.Disc;
            depList.Margin = new Thickness(0, 0, 0, 12);
            foreach (var depId in task.DependsOn)
            {
                var depPara = new Paragraph();
                var depTask = plan?.Tasks?.FirstOrDefault(t => t.TaskId == depId);
                var depTitle = depTask?.Title ?? depId;
                if (depTask is not null)
                {
                    depPara.Inlines.Add(CreateTaskLink(depTitle, depId));
                    var depStatusText = $" ({FormatTaskStatus(depTask.Status)?.ToLowerInvariant()})";
                    var depIsActive = string.Equals(depTask.Status, PlanTaskStatus.Executing, StringComparison.Ordinal)
                        || PlanTaskStatus.IsVerifying(depTask.Status);
                    var depStatusRun = new Run(depStatusText);
                    depStatusRun.SetResourceReference(TextElement.ForegroundProperty, depIsActive ? "ImportantText" : "SubtleText");
                    depPara.Inlines.Add(depStatusRun);
                }
                else
                {
                    depPara.Inlines.Add(new Run(depTitle));
                }
                var item = new ListItem(depPara);
                depList.ListItems.Add(item);
            }
            _detailDocument.Blocks.Add(depList);
        }

        // 11. Primary agent
        if (task.AgentAssignments is { Count: > 0 })
        {
            AddSectionHeader("Primary agent");
            var agentList = new List();
            agentList.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
            agentList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            agentList.MarkerStyle = TextMarkerStyle.None;
            agentList.Margin = new Thickness(0, 0, 0, 12);
            foreach (var assignment in task.AgentAssignments.Take(1))
            {
                var agentPara = new Paragraph();
                agentPara.Inlines.Add(CreateAgentLink(assignment.AgentHandle));
                agentPara.Inlines.Add(new Run($" — {assignment.Role}"));
                agentList.ListItems.Add(new ListItem(agentPara));
            }
            _detailDocument.Blocks.Add(agentList);
        }
    }

    private void AppendContractExampleValue(string label, string value)
    {
        if (_detailDocument is null) return;

        var labelParagraph = new Paragraph(new Run(label) { FontWeight = FontWeights.SemiBold });
        labelParagraph.SetResourceReference(TextElement.ForegroundProperty, "LabelText");
        labelParagraph.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
        labelParagraph.Margin = new Thickness(0, 0, 0, 2);
        _detailDocument.Blocks.Add(labelParagraph);

        var valueParagraph = new Paragraph(new Run(value))
        {
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(7, 5, 7, 5),
        };
        valueParagraph.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
        valueParagraph.SetResourceReference(TextElement.BackgroundProperty, "PanelBackground");
        valueParagraph.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
        _detailDocument.Blocks.Add(valueParagraph);
    }

    private void AppendTaskActivitySummary(PlanTaskInspectorActivitySummary? summary)
    {
        if (_detailDocument is null || summary is null || !summary.ShouldShowCallout)
            return;

        AddSectionHeader("Current Activity");
        var activityPara = new Paragraph();
        var labelRun = new Run(summary.Label) { FontWeight = FontWeights.SemiBold };
        labelRun.SetResourceReference(TextElement.ForegroundProperty, summary.ColorKey);
        activityPara.Inlines.Add(labelRun);
        activityPara.Inlines.Add(new LineBreak());
        activityPara.Inlines.Add(new Run(summary.Description));
        activityPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
        activityPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        activityPara.Margin = new Thickness(0, 0, 0, 12);
        _detailDocument.Blocks.Add(activityPara);
    }

    private ScrollViewer BuildChangedFilesPanel(
        PlanTask task,
        IReadOnlyList<PlanTaskFileChange> files)
    {
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Content = BuildChangedFileRows(task, files),
        };
        scroll.SetResourceReference(StyleProperty, "RosterScrollViewerStyle");
        return scroll;
    }

    private StackPanel BuildChangedFileRows(
        PlanTask task,
        IReadOnlyList<PlanTaskFileChange> files)
    {
        var rows = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var file in files)
        {
            var statusText = file.Status switch
            {
                FileChangeStatus.Added => "+",
                FileChangeStatus.Deleted => "−",
                _ => "Δ",
            };
            var statusColor = file.Status switch
            {
                FileChangeStatus.Added => "DiffAddedSummary",
                FileChangeStatus.Deleted => "DiffRemovedSummary",
                _ => "DiffModifiedSummary",
            };
            var line = new TextBlock
            {
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 1, 8, 1),
            };
            line.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            var marker = new Run(statusText + " ") { FontWeight = FontWeights.Bold };
            marker.SetResourceReference(TextElement.ForegroundProperty, statusColor);
            line.Inlines.Add(marker);
            var link = new Hyperlink(new Run(file.Path))
            {
                Cursor = Cursors.Hand,
                TextDecorations = new TextDecorationCollection(),
                ToolTip = ToolTipHelper.MakeThemedToolTip("Open this file's task diff"),
            };
            link.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
            if (_onOpenTaskFileDiff is not null)
            {
                var path = file.Path;
                link.Click += (_, _) => _onOpenTaskFileDiff(task, path);
            }
            line.Inlines.Add(link);
            rows.Children.Add(line);
        }
        return rows;
    }

    private async Task RefreshChangedFilesAsync(PlanTask task, ScrollViewer scroll)
    {
        if (_resolveTaskFileChanges is null) return;
        try
        {
            var files = await _resolveTaskFileChanges(task);
            if (files.Count > 0)
                scroll.Content = BuildChangedFileRows(task, files);
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write("PlanViewer", $"Could not resolve changed-file statuses for {task.TaskId}: {ex.Message}");
        }
    }

    private const string VerificationCheckLeadingSpace = "\u2005"; // Four-per-em space: one quarter of the current font height.

    private enum VerificationIconKind { Supported, Failed, Unclear, HumanAccepted }

    private static FrameworkElement CreateVerificationIcon(VerificationIconKind kind)
    {
        var resourceKey = kind switch
        {
            VerificationIconKind.Supported => "TaskSucceeded",
            VerificationIconKind.Failed => "TaskFailed",
            VerificationIconKind.HumanAccepted => "PlanApprovalResolvedCheck",
            _ => "TaskAwaitingHumanReview",
        };
        if (Application.Current?.TryFindResource(resourceKey) is FrameworkElement icon)
        {
            icon.SetResourceReference(FrameworkElement.WidthProperty, "FontSizeLargePlus");
            icon.SetResourceReference(FrameworkElement.HeightProperty, "FontSizeLargePlus");
            icon.VerticalAlignment = VerticalAlignment.Center;
            return icon;
        }
        return new TextBlock { Text = kind == VerificationIconKind.Failed ? "×" : "✓" };
    }

    private static bool IsSupportedVerificationDisposition(string? disposition) =>
        string.Equals(disposition, "supported", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(disposition, "confirmed", StringComparison.OrdinalIgnoreCase);

    private static FrameworkElement CreateVerificationClaimIcon(string? disposition)
    {
        var isSupported = IsSupportedVerificationDisposition(disposition);
        Geometry geometry;
        if (isSupported && Application.Current?.TryFindResource("TaskSucceededGeometry") is Geometry succeededGeometry)
        {
            // Use the same checkmark silhouette as a completed task, but verification amber
            // distinguishes supported evidence from task completion state.
            geometry = succeededGeometry;
        }
        else if (isSupported)
        {
            geometry = Geometry.Parse("M0,5 L3.5,8.5 10,1.5 8.5,0 3.5,5.5 1.5,3.5z");
        }
        else
        {
            geometry = Geometry.Parse("M0,1.5 1.5,0 5,3.5 8.5,0 10,1.5 6.5,5 10,8.5 8.5,10 5,6.5 1.5,10 0,8.5 3.5,5z");
        }

        var path = new Path { Data = geometry };
        path.SetResourceReference(Shape.FillProperty, isSupported ? "PriorityHigh" : "PriorityCritical");
        var icon = new Viewbox
        {
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Child = path,
        };
        // FontSizeLargePlus is 16px at the default scale versus the former fixed 11px icon,
        // and remains tied to the application's live font-scale resource dictionary.
        icon.SetResourceReference(FrameworkElement.WidthProperty, "FontSizeLargePlus");
        icon.SetResourceReference(FrameworkElement.HeightProperty, "FontSizeLargePlus");
        return icon;
    }

    private static IReadOnlyList<PlanApprovalGate> ResolveHumanAcceptanceGates(PlanTask task, Plan? plan)
    {
        if (plan is null || task.VerificationHistory?.Any(report =>
                string.Equals(report.Verdict, PlanTaskVerificationVerdict.HumanReviewRequired,
                    StringComparison.OrdinalIgnoreCase)) != true)
            return [];
        return plan.ApprovalGates.Where(gate =>
            string.Equals(gate.Status, PlanGateStatus.Approved, StringComparison.OrdinalIgnoreCase) &&
            gate.AfterTaskIds.Contains(task.TaskId, StringComparer.Ordinal)).ToArray();
    }

    private void AppendHumanVerificationAcceptance(IReadOnlyList<PlanApprovalGate> gates)
    {
        foreach (var gate in gates)
        {
            var accepted = new Paragraph();
            accepted.Inlines.Add(new Run(VerificationCheckLeadingSpace));
            accepted.Inlines.Add(new InlineUIContainer(CreateVerificationIcon(VerificationIconKind.HumanAccepted))
            {
                BaselineAlignment = BaselineAlignment.Center,
            });
            var identity = string.IsNullOrWhiteSpace(gate.ResolvedBy) ? "user" : gate.ResolvedBy;
            accepted.Inlines.Add(new Run($"  Accepted by {identity}") { FontWeight = FontWeights.SemiBold });
            if (gate.ResolvedAt is { } resolvedAt)
                accepted.Inlines.Add(new Run($"  ({resolvedAt.ToLocalTime():yyyy-MM-dd HH:mm})"));
            accepted.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
            accepted.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            accepted.Margin = new Thickness(0, 4, 0, 4);
            _detailDocument?.Blocks.Add(accepted);
        }
    }

    private void AppendTaskDescription(string description, string taskId)
    {
        if (_detailDocument is null) return;
        _descriptionPresentation!.AppendTaskDescription(_detailDocument, taskId, description);
    }

    private void BindTaskDescriptionPreview(string taskId, string description, TextBlock preview)
    {
        var document = _descriptionPresentation!.BuildTaskDescription(taskId, description);
        _descriptionPreviews.Add(document);
        void Refresh() => preview.Text = MarkdownVisibleContent.GetText(document);
        MarkdownVisibleContent.State(document).VisibilityChanged += Refresh;
        Refresh();
    }

    private void DetachDescriptionDocuments()
    {
        _descriptionSession?.Dispose();
        _descriptionSession = null;
        if (_detailDocument is not null) MarkdownVisibleContent.Detach(_detailDocument);
        foreach (var preview in _descriptionPreviews) MarkdownVisibleContent.Detach(preview);
        _descriptionPreviews.Clear();
    }

    private void ShowDescriptionFind()
    {
        if (_isInspectorEditing || _descriptionFindInput is null) return;
        _descriptionFindInput.Visibility = Visibility.Visible;
        _descriptionFindInput.Focus();
        _descriptionFindInput.SelectAll();
    }

    private Hyperlink CreateTaskLink(string taskTitle, string taskId)
    {
        var link = new Hyperlink(new Run(taskTitle))
        {
            Cursor = Cursors.Hand,
            TextDecorations = new TextDecorationCollection(),
        };
        link.SetResourceReference(TextElement.ForegroundProperty, "TaskLinkText");
        link.MouseEnter += (_, _) =>
        {
            link.TextDecorations = new TextDecorationCollection();
            if (_borderByTask.TryGetValue(taskId, out var nodeBorder))
                nodeBorder.Effect = TaskNodeGlowEffect();
        };
        link.MouseLeave += (_, _) =>
        {
            if (_borderByTask.TryGetValue(taskId, out var nodeBorder))
                nodeBorder.Effect = null;
        };
        link.Click += (_, _) => NavigateToTask(taskId);
        return link;
    }

    private Hyperlink CreateAgentLink(string agentHandle)
    {
        var link = new Hyperlink(new Run(HumanizeAgentHandle(agentHandle)))
        {
            Cursor = Cursors.Hand,
            TextDecorations = new TextDecorationCollection(),
        };
        link.SetResourceReference(TextElement.ForegroundProperty, "TaskLinkText");
        link.Click += (_, _) => SelectElement("agent", agentHandle);
        return link;
    }

    private InlineUIContainer CreateValidationResultIcon(bool passed)
    {
        var icon = (Viewbox)FindResource(passed ? "TaskSucceeded" : "TaskFailed");
        icon.Width = 14;
        icon.Height = 14;
        icon.Margin = new Thickness(0, 0, 4, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        return new InlineUIContainer(icon)
        {
            BaselineAlignment = BaselineAlignment.Center,
        };
    }

    /// <summary>
    /// Determines whether a stored assertion evidence line represents a pass. Canonical evidence is
    /// written as "[PASS] assertion: ..." / "[FAIL] assertion: ..." by the plan validation pipeline,
    /// so the leading "[" is skipped before matching. Legacy bare "pass"/"fail" prefixes are also honored.
    /// </summary>
    private static bool EvidenceIndicatesPass(string evidence)
    {
        var trimmed = evidence.TrimStart().TrimStart('[');
        return trimmed.StartsWith("pass", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Leading vertical space to place above each assertion paragraph in the validation inspector:
    /// 80% of the body line height, computed from the current (zoom-scaled) body font size and the
    /// UI font's line spacing so the spacing tracks the active font size.
    /// </summary>
    private double GetAssertionLeadingSpace()
    {
        var fontSize = FindResource("FontSizeBody") is double resolved ? resolved : SystemFonts.MessageFontSize;
        var lineSpacing = SystemFonts.MessageFontFamily.LineSpacing;
        if (double.IsNaN(lineSpacing) || lineSpacing <= 0)
            lineSpacing = 1.3;
        return fontSize * lineSpacing * 0.8;
    }

    private void PopulateValidationDetail(PlanValidationNode validation, Plan? plan)
    {
        if (_detailDocument is null)
            return;

        _detailDocument.Blocks.Clear();

        // Title
        var titlePara = new Paragraph(new Run(validation.Title) { FontWeight = FontWeights.Bold });
        titlePara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
        titlePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeLarge");
        titlePara.Margin = new Thickness(0, 0, 0, 4);
        _detailDocument.Blocks.Add(titlePara);

        // Validation ID (monospace, subtle)
        var idPara = new Paragraph(new Run(validation.ValidationId) { FontFamily = new FontFamily("Consolas") });
        idPara.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
        idPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
        idPara.Margin = new Thickness(0, 0, 0, 4);
        _detailDocument.Blocks.Add(idPara);

        // Status with color indicator
        var status = validation.Status?.ToLowerInvariant();
        var statusColorKey = status switch
        {
            "passed" or "complete" => "TaskSuccessText",
            "failed" => "TaskFailureText",
            "executing" or "running" => "ActivePanelBorder",
            _ => "SubtleText",
        };
        var statusPara = new Paragraph();
        var statusRun = new Run(validation.Status ?? "unknown");
        if (status is "passed" or "complete" or "failed")
            statusPara.Inlines.Add(CreateValidationResultIcon(status is "passed" or "complete"));
        else
            statusRun.Text = "● " + statusRun.Text;
        statusRun.SetResourceReference(TextElement.ForegroundProperty, statusColorKey);
        statusPara.Inlines.Add(statusRun);
        statusPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        statusPara.Margin = new Thickness(0, 0, 0, 4);
        _detailDocument.Blocks.Add(statusPara);
        AppendWorkMetricsLink(PlanWorkItemKinds.Validation, validation.ValidationId);

        var hasResultView = string.Equals(validation.Status, "passed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(validation.Status, "failed", StringComparison.OrdinalIgnoreCase);

        if (hasResultView)
        {
            if (!string.IsNullOrWhiteSpace(validation.Summary))
            {
                AddSectionHeader("Summary");
                var summaryPara = new Paragraph(new Run(validation.Summary));
                summaryPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                summaryPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                summaryPara.Margin = new Thickness(0, 0, 0, 8);
                _detailDocument.Blocks.Add(summaryPara);
            }

            if (!string.IsNullOrWhiteSpace(validation.ValidatedCommit))
            {
                var commitPara = new Paragraph(new Run($"Evaluated at commit: {validation.ValidatedCommit}")
                {
                    FontFamily = new FontFamily("Consolas"),
                });
                commitPara.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                commitPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
                commitPara.Margin = new Thickness(0, 0, 0, 4);
                _detailDocument.Blocks.Add(commitPara);
            }

            if (validation.CompletedAt is { } completedAt)
            {
                var completedPara = new Paragraph(new Run($"Completed: {completedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC"));
                completedPara.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                completedPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
                completedPara.Margin = new Thickness(0, 0, 0, 8);
                _detailDocument.Blocks.Add(completedPara);
            }

            if (validation.Assertions is { Count: > 0 })
            {
                AddSectionHeader("Assertions");
                var assertList = new List();
                assertList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                assertList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                assertList.MarkerStyle = TextMarkerStyle.None;
                assertList.Margin = new Thickness(0, 0, 0, 12);
                var assertionLeading = GetAssertionLeadingSpace();
                for (int i = 0; i < validation.Assertions.Count; i++)
                {
                    var assertion = validation.Assertions[i];
                    var hasEvidence = validation.Evidence is not null
                        && i < validation.Evidence.Count
                        && !string.IsNullOrWhiteSpace(validation.Evidence[i]);
                    var para = new Paragraph();
                    if (hasEvidence)
                    {
                        var evidenceText = validation.Evidence![i];
                        var isPassedAssertion = string.Equals(validation.Status, "passed", StringComparison.OrdinalIgnoreCase)
                            || EvidenceIndicatesPass(evidenceText);
                        para.Inlines.Add(CreateValidationResultIcon(isPassedAssertion));
                        para.Inlines.Add(new Run(assertion));
                        para.Inlines.Add(new LineBreak());

                        var evidenceRun = new Run("    " + evidenceText)
                        {
                            FontStyle = FontStyles.Italic,
                        };
                        evidenceRun.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                        evidenceRun.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
                        para.Inlines.Add(evidenceRun);
                    }
                    else
                    {
                        para.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                        para.Inlines.Add(new Run("• " + assertion));
                    }

                    para.Margin = new Thickness(0);
                    // Item padding reserves leading even where WPF suppresses/collapses block margins.
                    assertList.ListItems.Add(new ListItem(para)
                    {
                        Padding = new Thickness(0, assertionLeading, 0, 0),
                    });
                }

                _detailDocument.Blocks.Add(assertList);
            }

            if (!string.IsNullOrWhiteSpace(validation.Description))
            {
                var contextLabel = new Paragraph(new Run("What this validation checked:") { FontWeight = FontWeights.SemiBold });
                contextLabel.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                contextLabel.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
                contextLabel.Margin = new Thickness(0, 0, 0, 2);
                _detailDocument.Blocks.Add(contextLabel);

                var descPara = new Paragraph(new Run(validation.Description));
                descPara.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                descPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
                descPara.Margin = new Thickness(0, 0, 0, 12);
                _detailDocument.Blocks.Add(descPara);
            }
        }
        else
        {
            // Mode
            if (!string.IsNullOrWhiteSpace(validation.Mode))
            {
                var modePara = new Paragraph();
                modePara.Inlines.Add(new Run("Mode: ") { FontWeight = FontWeights.SemiBold });
                var modeValueRun = new Run(validation.Mode);
                modeValueRun.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
                modePara.Inlines.Add(modeValueRun);
                modePara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                modePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                modePara.Margin = new Thickness(0, 0, 0, 8);
                _detailDocument.Blocks.Add(modePara);
            }

            // Description
            if (!string.IsNullOrWhiteSpace(validation.Description))
            {
                var descPara = new Paragraph(new Run(validation.Description));
                descPara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
                descPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                descPara.Margin = new Thickness(0, 0, 0, 12);
                _detailDocument.Blocks.Add(descPara);
            }

            // Assertions list
            if (validation.Assertions is { Count: > 0 })
            {
                AddSectionHeader("Assertions");
                var assertList = new List();
                assertList.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
                assertList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                assertList.MarkerStyle = TextMarkerStyle.None;
                assertList.Margin = new Thickness(0, 0, 0, 12);
                var assertionLeading = GetAssertionLeadingSpace();
                for (int i = 0; i < validation.Assertions.Count; i++)
                {
                    var assertion = validation.Assertions[i];
                    var hasEvidence = validation.Evidence is not null
                        && i < validation.Evidence.Count
                        && !string.IsNullOrWhiteSpace(validation.Evidence[i]);
                    var para = new Paragraph();
                    if (hasEvidence)
                    {
                        para.Inlines.Add(CreateValidationResultIcon(
                            EvidenceIndicatesPass(validation.Evidence![i])));
                    }
                    else
                    {
                        var indicatorRun = new Run("• ");
                        indicatorRun.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                        para.Inlines.Add(indicatorRun);
                    }
                    var assertionRun = new Run(assertion);
                    assertionRun.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
                    para.Inlines.Add(assertionRun);
                    para.Margin = new Thickness(0);
                    assertList.ListItems.Add(new ListItem(para)
                    {
                        Padding = new Thickness(0, assertionLeading, 0, 0),
                    });
                }
                _detailDocument.Blocks.Add(assertList);
            }
        }

        // After Task IDs
        AddTaskIdList("Starts After These:", validation.AfterTaskIds, plan);

        // Before Task IDs
        AddTaskIdList("Starts Before These:", validation.BeforeTaskIds, plan);
    }

    private void AppendWorkMetricsLink(string workItemKind, string workItemId)
    {
        if (_detailDocument is null || _onOpenWorkMetrics is null)
            return;

        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };
        var link = new Hyperlink(new Run("Open work metrics"))
        {
            Cursor = Cursors.Hand,
            ToolTip = ToolTipHelper.MakeThemedToolTip("View live and historical agent runs for this work item"),
        };
        link.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
        link.Click += (_, _) => _onOpenWorkMetrics(workItemKind, workItemId);
        paragraph.Inlines.Add(link);
        paragraph.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        _detailDocument.Blocks.Add(paragraph);
    }

    private FrameworkElement BuildStandardInlineIcon(double opacity)
    {
        if (opacity < 1.0)
        {
            var hollowCheckGeometry = Geometry.Parse(
                "M1802.875,626.375C1790.25,613.625,1773.5,607.25,1756.75,607.25C1740.125,607.25,1723.375,613.625,1710.75,626.375L857.25,1479.75 454.75,1077.25C429.25,1051.75,388,1051.75,362.5,1077.25L178.125,1261.75C152.625,1287.25,152.625,1328.5,178.125,1354L811.125,1987C830.25,2006.125,858.25,2010.875,881.75,2001.375L881.75,2001.375C889.625,1998.125,897,1993.375,903.375,1987L1987.375,903C2012.875,877.5,2012.875,836.25,1987.375,810.75z M1836,522.875L2095.125,782C2138.625,825.5,2138.625,896.125,2095.125,939.625L1199.125,1835.625 940,2094.75C896.375,2138.25,825.875,2138.25,782.375,2094.75L523.125,1835.625 78.375,1390.75C34.875,1347.25,34.875,1276.75,78.375,1233.125L337.5,974C381,930.5,451.625,930.5,495.125,974L861.125,1340 1678.375,522.875C1721.875,479.25,1792.5,479.25,1836,522.875z");
            var hollowPath = new Path { Data = hollowCheckGeometry };
            hollowPath.SetResourceReference(Shape.FillProperty, "TaskSucceededFill");
            return new Viewbox { Width = 16.1, Height = 16.1, Stretch = Stretch.Uniform, Child = hollowPath, Opacity = 1.0 };
        }

        if (Application.Current.FindResource("TaskSucceededGeometry") is not Geometry geom)
            return new UIElement() as FrameworkElement ?? new Border();
        var path = new Path { Data = geom };
        path.SetResourceReference(Shape.FillProperty, "TaskSucceededFill");
        return new Viewbox { Width = 16.1, Height = 16.1, Stretch = Stretch.Uniform, Child = path, Opacity = opacity };
    }

    private FrameworkElement BuildLightweightInlineIcon(double opacity)
    {
        var ellipse = new Ellipse
        {
            Width = 12,
            Height = 12,
            StrokeThickness = 1.5,
            Fill = Brushes.Transparent,
        };
        ellipse.SetResourceReference(Shape.StrokeProperty, "TaskSucceededFill");
        if (opacity >= 1.0)
            ellipse.SetResourceReference(Shape.FillProperty, "TaskSucceededFill");
        return new Border { Width = 14, Height = 14, Child = ellipse, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = opacity };
    }

    private FrameworkElement BuildEnhancedInlineIcon(double opacity)
    {
        if (opacity < 1.0)
        {
            var hollowStarGeometry = Geometry.Parse(
                "M1177.75,338.9375C1160.75,338.6875,1149.3125,354.1875,1138.625,384.875L1133.75,399.1875 1024.4375,724.3125 996.625,806.5625 542.9375,807.0625 540.3125,807.1875C509.5,807.1875,486.5,813.5625,480.875,829.5625C475.25,845.625,486.3125,861.4375,512.0625,881.25L524.0625,890.4375 877.375,1157.6875 747.875,1591.5 747.25,1594.0625C738.375,1623.5625,737.875,1647.4375,751.625,1657.4375C765.375,1667.4375,783.6875,1661.375,810.0625,1642.4375L822.3125,1633.5625 1168.5,1380.0625 1168.8125,1380.4375 1171.1875,1378.6875 1553.5625,1640.75 1555.8125,1642.125C1581.1875,1659.5625,1603.75,1667.3125,1617.5,1657.3125C1631.25,1647.25,1631,1628,1621.0625,1597.0625L1616.375,1582.6875 1480.875,1175.5625 1481.3125,1175.4375 1475,1156.625 1511.8125,1128.6875 1835.3125,891.3125 1837.3125,889.6875C1862.1875,871.5,1876.9375,852.75,1872.0625,836.5L1870,833 1869.875,829.0625C1864.6875,812.875,1846.3125,807,1813.8125,806.6875L1798.75,806.625 1396.6875,806.5625 1369.625,806.1875 1369.625,806.125 1367.3125,806.125 1358.5,806 1267.9375,536.625 1219.375,391.3125 1218.4375,388.875C1213.5625,374.25,1207.8125,362,1201.125,353.375L1200.625,352.875 1200.5,352.6875C1193.875,344.0625,1186.25,339.0625,1177.75,338.9375z M1179.125,225.9375C1209.8125,225.9375,1237.625,238.375,1257.6875,258.5L1279.375,290.625 1300.75,354.6875 1344.3125,484.125 1366.0625,549.875 1366.4375,550 1423.6875,720.25 1435.625,720.4375 1438.8125,720.4375 1438.8125,720.4375 1475.5,721.0625 1604.6875,721.0625 1606.125,722.8125 1883.4375,724.5625 1892.75,724.625C1907.75,725.9375,1918.875,730,1933.125,736.25C1958.4375,749.1875,1978.8125,771.8125,1988.25,801C1997.6875,830.1875,1994.4375,860.4375,1981.5,885.75C1973.25,899,1965.6875,908.75,1955.5625,918.8125L1948.0625,924.3125 1737.25,1078.8125 1737.375,1080.625 1631.75,1158.125 1581.8125,1196 1590.3125,1221.5625 1589.75,1221.75 1645.5625,1389.4375 1645.1875,1390.0625 1675.8125,1480.375 1733.625,1654.0625 1735.625,1692.0625C1731.6875,1720.25,1717.0625,1746.875,1692.5625,1765.375C1668.0625,1783.875,1638.375,1790.625,1610.25,1786.6875C1602.625,1785.1875,1595.9375,1783.6875,1589.5625,1781.5625L1574.1875,1774.6875 1420.1875,1669.125 1348.5625,1619.625 1348.1875,1619.8125 1169.5625,1497.375 1166.25,1499.75 1165.875,1499.25 998.3125,1621.9375 997.375,1621.625 846.9375,1732.8125 801.6875,1765.875 784.0625,1773.875C777.6875,1776.3125,771,1778.375,763.5,1780.3125C735.4375,1785.125,705.625,1779.25,680.5625,1761.5625C655.5,1743.8125,640,1717.6875,635.1875,1689.625C634.3125,1681.9375,633.75,1675.0625,633.75,1668.375L635.875,1649.3125 651.5625,1596.875 708.75,1409.1875 707.875,1408.1875 770.8125,1197.4375 603.9375,1071.25 604,1070.3125 561,1038.75 384.6875,905.375 381.6875,902.0625C377.5625,896.75,373.5625,891,369.5,884.375C356.5625,859.0625,353.25,828.8125,362.6875,799.625C372.125,770.4375,392.5625,747.8125,417.875,734.875C432.0625,728.625,443.25,724.5625,458.1875,723.25L467.5,723.1875 733.8125,721.4375 734.0625,721.1875 932.625,720.9375 970.375,609.375 992.625,543.25 994.6875,542.5 1074.1875,302.125 1077.125,293.3125C1083,279.4375,1090.25,270.125,1100.5625,258.5C1120.6875,238.375,1148.4375,225.9375,1179.125,225.9375z");
            var hollowStarPath = new Path { Data = hollowStarGeometry };
            hollowStarPath.SetResourceReference(Shape.FillProperty, "TaskSucceededFill");
            return new Viewbox { Width = 16.8, Height = 16.8, Stretch = Stretch.Uniform, Child = hollowStarPath, Opacity = 1.0 };
        }

        var starGeometry = Geometry.Parse(
            "M2373.875,421.375C2393.875,421.625,2411.875,433.5,2427.625,453.875L2427.875,454.25 " +
            "2429.125,455.375C2444.875,475.875,2458.5,504.75,2470,539.375L2472.25,545.125 " +
            "2587,888.5 2801,1525.25 2821.875,1525.625 2827.375,1525.625 2827.375,1525.625 " +
            "2891.25,1526.625 3841.625,1526.75 3877.375,1526.875C3954.125,1527.625,3997.5,1541.5," +
            "4009.75,1579.875L4010,1589.125 4014.875,1597.25C4026.5,1635.75,3991.5,1680.125," +
            "3932.75,1723L3928,1727 3163.5,2288 3076.5,2354 3091.375,2398.5 3090.25,2398.875 " +
            "3410.5,3361.125 3421.625,3395.125C3445.25,3468.125,3445.625,3513.75,3413.25,3537.5" +
            "C3380.75,3561.125,3327.375,3542.75,3267.375,3501.625L3262.125,3498.375 " +
            "2358.375,2878.875 2352.625,2883 2352,2882.125 1533.75,3481.25 " +
            "1504.75,3502.25C1442.375,3547.125,1399.125,3561.375,1366.625,3537.75" +
            "C1334.125,3514.125,1335.375,3457.75,1356.25,3388L1357.75,3382 " +
            "1663.875,2356.5 828.75,1724.875 800.25,1703.125C739.5,1656.25,713.375,1619," +
            "726.625,1581C740,1543.125,794.375,1528,867.125,1528L873.375,1527.75 " +
            "1945.75,1526.5 2011.5,1332.25 2269.875,563.75 2281.375,530" +
            "C2306.625,457.375,2333.625,420.75,2373.875,421.375z");
        var starPath = new Path { Data = starGeometry };
        starPath.SetResourceReference(Shape.FillProperty, "TaskSucceededFill");
        return new Viewbox { Width = 16.8, Height = 16.8, Stretch = Stretch.Uniform, Child = starPath, Opacity = opacity };
    }

    private void PopulateVerificationDetail(string taskTitle, string taskId, string verificationLevel, bool verificationOnly, PlanTask? durableTask = null)
    {
        if (_detailDocument is null)
            return;

        _detailDocument.Blocks.Clear();

        var titlePara = new Paragraph(new Run("Verification") { FontWeight = FontWeights.Bold });
        titlePara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
        titlePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeLarge");
        titlePara.Margin = new Thickness(0, 0, 0, 4);
        _detailDocument.Blocks.Add(titlePara);

        var taskPara = new Paragraph(new Run($"Step: {taskTitle}"));
        taskPara.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
        taskPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
        taskPara.Margin = new Thickness(0, 0, 0, 8);
        _detailDocument.Blocks.Add(taskPara);

        if (verificationOnly)
        {
            var voPara = new Paragraph(new Run(
                "This step is a verification-only task. It evaluates prior work and does not produce repository commits. " +
                "It does not receive an additional independent verification pass."));
            voPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            voPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            voPara.Margin = new Thickness(0, 0, 0, 12);
            _detailDocument.Blocks.Add(voPara);
            return;
        }

        var levelLabel = verificationLevel switch
        {
            PlanTaskVerificationLevel.Lightweight => "Lightweight",
            PlanTaskVerificationLevel.Enhanced => "Enhanced",
            _ => "Standard",
        };
        bool isComplete = durableTask?.Status == PlanTaskStatus.Complete;
        double iconOpacity = isComplete ? 1.0 : 0.50;
        FrameworkElement levelIcon = verificationLevel switch
        {
            PlanTaskVerificationLevel.Enhanced => BuildEnhancedInlineIcon(iconOpacity),
            PlanTaskVerificationLevel.Lightweight => BuildLightweightInlineIcon(iconOpacity),
            _ => BuildStandardInlineIcon(iconOpacity),
        };
        var levelPara = new Paragraph();
        levelPara.Inlines.Add(new Run("Verification level: ") { FontWeight = FontWeights.SemiBold });
        levelPara.Inlines.Add(new InlineUIContainer(levelIcon) { BaselineAlignment = BaselineAlignment.Center });
        levelPara.Inlines.Add(new Run(" " + levelLabel));
        levelPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
        levelPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        levelPara.Margin = new Thickness(0, 0, 0, 8);
        _detailDocument.Blocks.Add(levelPara);

        if (durableTask?.VerificationHistory is { Count: > 0 } history)
        {
            var reports = history.OrderByDescending(report => report.CompletedAt).ToArray();
            for (var reportIndex = 0; reportIndex < reports.Length; reportIndex++)
            {
                if (reportIndex == 1)
                    AddSectionHeader("Previous Verification Attempts");

                var report = reports[reportIndex];
                var verificationPassed = report.Verdict.Equals(
                    PlanTaskVerificationVerdict.Accepted,
                    StringComparison.OrdinalIgnoreCase);
                var reworkRequired = report.Verdict.Equals(
                    PlanTaskVerificationVerdict.ReworkRequired,
                    StringComparison.OrdinalIgnoreCase);
                var verdictColorKey = reworkRequired ? "PriorityCritical" : "PriorityHigh";
                var verdictLabel = report.Verdict switch
                {
                    PlanTaskVerificationVerdict.Accepted => "Verification passed",
                    PlanTaskVerificationVerdict.ReworkRequired => "Rework required",
                    PlanTaskVerificationVerdict.HumanReviewRequired => "Human review required",
                    _ => report.Verdict,
                };
                var verdictPara = new Paragraph();
                if (verificationPassed)
                    verdictPara.Inlines.Add(new Run(VerificationCheckLeadingSpace));
                verdictPara.Inlines.Add(new InlineUIContainer(
                    verificationPassed
                        ? CreateVerificationClaimIcon("supported")
                        : reworkRequired
                            ? CreateVerificationClaimIcon("missing")
                            : CreateVerificationIcon(VerificationIconKind.Unclear))
                {
                    BaselineAlignment = BaselineAlignment.Center,
                });
                var verdictRun = new Run($"  {verdictLabel}") { FontWeight = FontWeights.SemiBold };
                verdictRun.SetResourceReference(TextElement.ForegroundProperty, verdictColorKey);
                verdictPara.Inlines.Add(verdictRun);
                verdictPara.Inlines.Add(new Run($"  ({report.CompletedAt:yyyy-MM-dd HH:mm} UTC)"));
                verdictPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                verdictPara.Margin = new Thickness(0, 0, 0, 4);
                _detailDocument.Blocks.Add(verdictPara);

                if (!string.IsNullOrWhiteSpace(report.EvaluatedCommit))
                {
                    var commitPara = new Paragraph(new Run($"Evaluated at commit: {report.EvaluatedCommit}")
                    {
                        FontFamily = new FontFamily("Consolas"),
                    });
                    commitPara.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                    commitPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
                    commitPara.Margin = new Thickness(0, 0, 0, 4);
                    _detailDocument.Blocks.Add(commitPara);
                }

                if (!string.IsNullOrWhiteSpace(report.Summary))
                {
                    AddSectionHeader("Summary");
                    var summaryPara = new Paragraph(new Run(report.Summary));
                    summaryPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                    summaryPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                    summaryPara.Margin = new Thickness(0, 0, 0, 8);
                    _detailDocument.Blocks.Add(summaryPara);
                }

                if (report.ClaimFindings is { Count: > 0 })
                {
                    AddSectionHeader("Claim Findings");
                    var findingList = new System.Windows.Documents.List();
                    findingList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                    findingList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                    findingList.MarkerStyle = TextMarkerStyle.None;
                    findingList.Margin = new Thickness(0, 0, 0, 8);
                    foreach (var finding in report.ClaimFindings)
                    {
                        var fPara = new Paragraph();
                        if (IsSupportedVerificationDisposition(finding.Disposition))
                            fPara.Inlines.Add(new Run(VerificationCheckLeadingSpace));
                        fPara.Inlines.Add(new InlineUIContainer(CreateVerificationClaimIcon(finding.Disposition))
                        {
                            BaselineAlignment = BaselineAlignment.Center,
                        });
                        fPara.Inlines.Add(new Run("  "));
                        fPara.Inlines.Add(new Run(finding.Claim));
                        if (!string.IsNullOrWhiteSpace(finding.Evidence))
                        {
                            fPara.Inlines.Add(new LineBreak());
                            var evidenceRun = new Run("    " + finding.Evidence) { FontStyle = FontStyles.Italic };
                            evidenceRun.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                            evidenceRun.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
                            fPara.Inlines.Add(evidenceRun);
                        }
                        findingList.ListItems.Add(new ListItem(fPara));
                    }
                    _detailDocument.Blocks.Add(findingList);
                }

                if (report.MissingOrOverstatedWork is { Count: > 0 })
                {
                    AddSectionHeader("Missing or Overstated Work");
                    var missingList = new System.Windows.Documents.List();
                    missingList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                    missingList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                    missingList.MarkerStyle = TextMarkerStyle.None;
                    missingList.Margin = new Thickness(0, 0, 0, 8);
                    foreach (var item in report.MissingOrOverstatedWork)
                    {
                        var mPara = new Paragraph();
                        var bulletRun = new Run("• ");
                        bulletRun.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                        mPara.Inlines.Add(bulletRun);
                        mPara.Inlines.Add(new Run(item));
                        missingList.ListItems.Add(new ListItem(mPara));
                    }
                    _detailDocument.Blocks.Add(missingList);
                }

                if (!string.IsNullOrWhiteSpace(report.TestAssessment))
                {
                    AddSectionHeader("Test Assessment");
                    var testPara = new Paragraph(new Run(report.TestAssessment));
                    testPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                    testPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                    testPara.Margin = new Thickness(0, 0, 0, 8);
                    _detailDocument.Blocks.Add(testPara);
                }

                if (report.ReworkInstructions is { Count: > 0 })
                {
                    AddSectionHeader("Rework Instructions");
                    var reworkList = new System.Windows.Documents.List();
                    reworkList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                    reworkList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                    reworkList.MarkerStyle = TextMarkerStyle.None;
                    reworkList.Margin = new Thickness(0, 0, 0, 8);
                    foreach (var rw in report.ReworkInstructions)
                    {
                        var rPara = new Paragraph();
                        var bulletRun = new Run("• ");
                        bulletRun.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                        rPara.Inlines.Add(bulletRun);
                        rPara.Inlines.Add(new Run(rw));
                        reworkList.ListItems.Add(new ListItem(rPara));
                    }
                    _detailDocument.Blocks.Add(reworkList);
                }
            }
        }
        else
        {
            AddSectionHeader("Assigned To");

            bool isLightweightLevel = verificationLevel == PlanTaskVerificationLevel.Lightweight;
            string reviewerHandle;
            string reviewerDisplayName;
            string reviewerRole;

            if (isLightweightLevel)
            {
                var primaryAssignment = durableTask?.AgentAssignments?.FirstOrDefault();
                reviewerHandle = primaryAssignment?.AgentHandle ?? "coordinator";
                reviewerDisplayName = HumanizeAgentHandle(reviewerHandle);
                reviewerRole = primaryAssignment?.Role ?? "Implementing agent";
            }
            else
            {
                reviewerHandle = VerityCrossHandle;
                reviewerDisplayName = "Verity Cross";
                reviewerRole = "Fact Checker — independent review agent";
            }

            if (_resolveAgentAvatar is not null)
            {
                var avatarInfo = _resolveAgentAvatar(reviewerHandle);
                var chipSize = 40.0 * _scaleFactor;
                var chip = CreateAgentAvatarChip(avatarInfo, chipSize, reviewerHandle);
                chip.HorizontalAlignment = HorizontalAlignment.Left;
                _detailDocument.Blocks.Add(new BlockUIContainer(chip) { Margin = new Thickness(0, 4, 0, 4) });
            }

            var namePara = new Paragraph(CreateAgentLink(reviewerHandle));
            namePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            namePara.Margin = new Thickness(0, 0, 0, 2);
            _detailDocument.Blocks.Add(namePara);

            var rolePara = new Paragraph(new Run(reviewerRole));
            rolePara.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
            rolePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
            rolePara.Margin = new Thickness(0, 0, 0, 12);
            _detailDocument.Blocks.Add(rolePara);

            string instructions = verificationLevel switch
            {
                PlanTaskVerificationLevel.Enhanced =>
                    PlanTaskVerificationInstructions.EnhancedIndependentReview,
                _ =>
                    "Perform the standard independent review: validate the task-owned claims against the actual diff, " +
                    "production wiring, and tests.",
            };

            if (verificationLevel == PlanTaskVerificationLevel.Lightweight)
            {
                AddSectionHeader("Lightweight Verification");

                var instrPara1 = new Paragraph(new Run(
                    "The implementing agent completes the task's required checks and returns supporting evidence. " +
                    "SquadDash validates the result, evidence, commit scope, and changed files before accepting it."));
                instrPara1.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                instrPara1.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                instrPara1.Margin = new Thickness(0, 0, 0, 6);
                _detailDocument.Blocks.Add(instrPara1);

                var instrPara2 = new Paragraph();
                instrPara2.Inlines.Add(new Run(
                    "If the task or changed files trigger a higher-risk rule, SquadDash automatically upgrades it " +
                    "to a Standard independent review by "));
                instrPara2.Inlines.Add(new Run("Verity Cross") { FontWeight = FontWeights.Bold });
                instrPara2.Inlines.Add(new Run("."));
                instrPara2.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                instrPara2.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                instrPara2.Margin = new Thickness(0, 0, 0, 12);
                _detailDocument.Blocks.Add(instrPara2);
            }
            else
            {
                AddSectionHeader("Instructions Sent to Verifier");
                var instrPara = new Paragraph(new Run(instructions));
                instrPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                instrPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                instrPara.Margin = new Thickness(0, 0, 0, 12);
                _detailDocument.Blocks.Add(instrPara);
            }

            AddSectionHeader("Possible Outcomes");
            var verdictList = new System.Windows.Documents.List();
            verdictList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            verdictList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            verdictList.MarkerStyle = TextMarkerStyle.None;
            verdictList.Margin = new Thickness(0, 0, 0, 8);
            foreach (var (verdict, desc) in new (string, string)[]
            {
                ("accepted", "Every material claim is supported. Task moves to complete."),
                ("rework-required", "A clear, bounded defect was found. Task is sent back."),
                ("human-review-required", "Evidence is incomplete or intent is ambiguous. Flagged for human judgment."),
            })
            {
                var p = new Paragraph();
                var bulletRun = new Run("• ");
                bulletRun.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                p.Inlines.Add(bulletRun);
                p.Inlines.Add(new Run($"{verdict}: ") { FontWeight = FontWeights.SemiBold });
                p.Inlines.Add(new Run(desc));
                verdictList.ListItems.Add(new ListItem(p));
            }
            _detailDocument.Blocks.Add(verdictList);
        }

    }

    private void PopulateGateDetail(PlanApprovalGate gate, Plan? plan)
    {
        if (_detailDocument is null)
            return;

        _detailDocument.Blocks.Clear();

        // Title
        var titlePara = new Paragraph(new Run("Approval Gate") { FontWeight = FontWeights.Bold });
        titlePara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
        titlePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeLarge");
        titlePara.Margin = new Thickness(0, 0, 0, 4);
        _detailDocument.Blocks.Add(titlePara);

        if (_isInspectorEditing)
        {
            var idPara = new Paragraph(new Run(gate.GateId) { FontFamily = new FontFamily("Consolas") });
            idPara.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
            idPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
            idPara.Margin = new Thickness(0, 0, 0, 4);
            _detailDocument.Blocks.Add(idPara);
        }

        // Message
        if (!string.IsNullOrWhiteSpace(gate.Message))
        {
            var msgPara = new Paragraph(new Run(gate.Message));
            msgPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            msgPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            msgPara.Margin = new Thickness(0, 0, 0, 8);
            _detailDocument.Blocks.Add(msgPara);
        }

        AppendGateReviewRequirements(gate);

        // Status (color-coded)
        var gateStatusColor = gate.Status switch
        {
            PlanGateStatus.Approved or PlanGateStatus.Skipped => "PriorityLow",
            "rework-requested" => "PriorityHigh",
            _ => "SubtleText",
        };
        var gateStatusText = gate.Status switch
        {
            PlanGateStatus.Pending => "Pending",
            PlanGateStatus.AwaitingApproval => "Awaiting Approval",
            PlanGateStatus.Approved => "Approved",
            PlanGateStatus.Skipped => "Skipped",
            _ => gate.Status ?? "Unknown",
        };
        var statusRun = new Run($"● {gateStatusText}");
        statusRun.SetResourceReference(TextElement.ForegroundProperty, gateStatusColor);
        var statusPara = new Paragraph(statusRun);
        statusPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        statusPara.Margin = new Thickness(0, 0, 0, 8);
        _detailDocument.Blocks.Add(statusPara);

        // After Task IDs
        AddTaskIdList("Starts After These:", gate.AfterTaskIds, plan);

        // Before Task IDs
        AddTaskIdList("Starts Before These:", gate.BeforeTaskIds, plan);

        // Resolution note
        if (!string.IsNullOrWhiteSpace(gate.ResolutionNote))
        {
            AddSectionHeader("Resolution Note");
            var notePara = new Paragraph(new Run(gate.ResolutionNote));
            notePara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            notePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            notePara.Margin = new Thickness(0, 0, 0, 12);
            _detailDocument.Blocks.Add(notePara);
        }

        // Resolved By
        if (!string.IsNullOrWhiteSpace(gate.ResolvedBy))
        {
            AddSectionHeader("Resolved By");
            var resolvedByPara = new Paragraph(new Run(gate.ResolvedBy));
            resolvedByPara.SetResourceReference(TextElement.ForegroundProperty, "LabelText");
            resolvedByPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            resolvedByPara.Margin = new Thickness(0, 0, 0, 12);
            _detailDocument.Blocks.Add(resolvedByPara);
        }

        // Rework Count
        if (gate.ReworkCount > 0)
        {
            AddSectionHeader("Rework Count");
            var reworkPara = new Paragraph(new Run(gate.ReworkCount.ToString()));
            reworkPara.SetResourceReference(TextElement.ForegroundProperty, "PriorityMid");
            reworkPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            reworkPara.Margin = new Thickness(0, 0, 0, 12);
            _detailDocument.Blocks.Add(reworkPara);
        }

        // Last Rework Instructions
        if (!string.IsNullOrWhiteSpace(gate.LastReworkInstructions))
        {
            AddSectionHeader("Last Rework Instructions");
            var instrPara = new Paragraph(new Run(gate.LastReworkInstructions));
            instrPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            instrPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            instrPara.Margin = new Thickness(0, 0, 0, 12);
            _detailDocument.Blocks.Add(instrPara);
        }

        AppendApprovalContractEditor(gate);
    }

    private void PopulateHumanApprovalDetail(string gateId)
    {
        if (_detailDocument is null)
            return;

        _detailDocument.Blocks.Clear();

        var gate = _durablePlan?.ApprovalGates.FirstOrDefault(g =>
            string.Equals(g.GateId, gateId, StringComparison.Ordinal));
        gate ??= _plan is null ? null
            : PendingDecomposePlanAdapter.MapApprovalGates(_plan.Group, _plan.Revision)
                .FirstOrDefault(g => string.Equals(g.GateId, gateId, StringComparison.Ordinal));
        if (gate is null)
        {
            var fallback = new Paragraph(new Run("Approval gate not found."));
            fallback.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
            fallback.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            _detailDocument.Blocks.Add(fallback);
            return;
        }

        // Title
        var titlePara = new Paragraph(new Run("Approval Required") { FontWeight = FontWeights.Bold });
        titlePara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
        titlePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeLarge");
        titlePara.Margin = new Thickness(0, 0, 0, 4);
        _detailDocument.Blocks.Add(titlePara);

        // Message
        if (!string.IsNullOrWhiteSpace(gate.Message))
        {
            var msgPara = new Paragraph(new Run(gate.Message));
            msgPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            msgPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            msgPara.Margin = new Thickness(0, 0, 0, 8);
            _detailDocument.Blocks.Add(msgPara);
        }

        // Status (color-coded)
        var gateStatusColor = gate.Status switch
        {
            PlanGateStatus.Approved or PlanGateStatus.Skipped => "PriorityLow",
            "rework-requested" => "PriorityHigh",
            _ => "SubtleText",
        };
        var gateStatusText = gate.Status switch
        {
            PlanGateStatus.Pending => "Pending",
            PlanGateStatus.AwaitingApproval => "Awaiting Approval",
            PlanGateStatus.Approved => "Approved",
            PlanGateStatus.Skipped => "Skipped",
            _ => gate.Status ?? "Unknown",
        };
        var statusRun = new Run($"● {gateStatusText}");
        statusRun.SetResourceReference(TextElement.ForegroundProperty, gateStatusColor);
        var statusPara = new Paragraph(statusRun);
        statusPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        statusPara.Margin = new Thickness(0, 0, 0, 8);
        _detailDocument.Blocks.Add(statusPara);

        AppendGateReviewRequirements(gate);

        // After Tasks
        AddTaskIdList("Starts After:", gate.AfterTaskIds, _durablePlan);

        // Before Tasks
        AddTaskIdList("Starts Before:", gate.BeforeTaskIds, _durablePlan);

        // Resolution Note
        if (!string.IsNullOrWhiteSpace(gate.ResolutionNote))
        {
            AddSectionHeader("Resolution Note");
            var notePara = new Paragraph(new Run(gate.ResolutionNote));
            notePara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            notePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            notePara.Margin = new Thickness(0, 0, 0, 12);
            _detailDocument.Blocks.Add(notePara);
        }

        // Resolved By
        if (!string.IsNullOrWhiteSpace(gate.ResolvedBy))
        {
            AddSectionHeader("Resolved By");
            var resolvedByPara = new Paragraph(new Run(gate.ResolvedBy));
            resolvedByPara.SetResourceReference(TextElement.ForegroundProperty, "LabelText");
            resolvedByPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            resolvedByPara.Margin = new Thickness(0, 0, 0, 12);
            _detailDocument.Blocks.Add(resolvedByPara);
        }

        AppendApprovalContractEditor(gate);
    }

    private void PopulateMilestoneDetail(int columnIndex)
    {
        if (_detailDocument is null)
            return;

        _detailDocument.Blocks.Clear();

        // Title
        var titlePara = new Paragraph(new Run("Milestone Boundary") { FontWeight = FontWeights.Bold });
        titlePara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
        titlePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeLarge");
        titlePara.Margin = new Thickness(0, 0, 0, 4);
        _detailDocument.Blocks.Add(titlePara);

        // Position
        var posPara = new Paragraph();
        posPara.Inlines.Add(new Run("Position: ") { FontWeight = FontWeights.SemiBold });
        posPara.Inlines.Add(new Run($"Between Stage {columnIndex + 1} and Stage {columnIndex + 2}"));
        posPara.SetResourceReference(TextElement.ForegroundProperty, "LabelText");
        posPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        posPara.Margin = new Thickness(0, 0, 0, 12);
        _detailDocument.Blocks.Add(posPara);

        // Resolve tasks and levels
        var (planTasks, levels) = ResolveTasksAndLevels();
        if (planTasks is null)
            return;

        var columns = planTasks.GroupBy(t => levels[t.TaskId]).OrderBy(g => g.Key).ToArray();

        if (_durablePlan is not null && columnIndex >= 0 && columnIndex + 1 < columns.Length)
        {
            var afterIds = columns[columnIndex].Select(task => task.TaskId).ToArray();
            var beforeIds = columns[columnIndex + 1].Select(task => task.TaskId).ToArray();
            var approvalGate = PlanGateManager.FindEquivalentGate(_durablePlan, afterIds, beforeIds) ??
                _durablePlan.ApprovalGates.FirstOrDefault(gate =>
                    PlanGateVisualizationPolicy.GraphEquivalent(
                        _durablePlan.Tasks,
                        gate.AfterTaskIds,
                        gate.BeforeTaskIds,
                        afterIds,
                        beforeIds));
            if (approvalGate is not null)
                AppendApprovalGuidance([approvalGate]);
        }

        // Tasks before milestone (columns 0..columnIndex)
        var tasksBefore = columns.Where(g => g.Key <= columnIndex).SelectMany(g => g).ToArray();
        if (tasksBefore.Length > 0)
        {
            AddSectionHeader($"Steps Before (Stage {columnIndex + 1} and earlier)");
            AddTaskStatusList(tasksBefore);
        }

        // Tasks after milestone (columns > columnIndex)
        var tasksAfter = columns.Where(g => g.Key > columnIndex).SelectMany(g => g).ToArray();
        if (tasksAfter.Length > 0)
        {
            AddSectionHeader($"Steps After (Stage {columnIndex + 2} and later)");
            AddTaskStatusList(tasksAfter);
        }
    }

    private void PopulateStageDetail(int stageIndex)
    {
        if (_detailDocument is null)
            return;

        _detailDocument.Blocks.Clear();

        // Title
        var stageTitle = $"Stage {stageIndex + 1}";
        var titlePara = new Paragraph(new Run(stageTitle) { FontWeight = FontWeights.Bold });
        titlePara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
        titlePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeLarge");
        titlePara.Margin = new Thickness(0, 0, 0, 8);
        _detailDocument.Blocks.Add(titlePara);

        // Resolve tasks and levels
        var (planTasks, levels) = ResolveTasksAndLevels();
        if (planTasks is null)
            return;

        var stageTasks = planTasks.Where(t => levels[t.TaskId] == stageIndex).ToArray();
        if (stageTasks.Length == 0)
            return;

        AddSectionHeader($"Steps ({stageTasks.Length})");
        AddTaskStatusList(stageTasks);
    }

    private void PopulateAllJoinDetail(int gateIndex)
    {
        if (_detailDocument is null)
            return;

        _detailDocument.Blocks.Clear();

        // Title
        var titlePara = new Paragraph(new Run("ALL Join") { FontWeight = FontWeights.Bold });
        titlePara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
        titlePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeLarge");
        titlePara.Margin = new Thickness(0, 0, 0, 4);
        _detailDocument.Blocks.Add(titlePara);

        // Description
        var descPara = new Paragraph(new Run("Waits for the following steps to complete before continuing:"));
        descPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
        descPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        descPara.Margin = new Thickness(0, 0, 0, 8);
        _detailDocument.Blocks.Add(descPara);

        // Resolve dependency tasks from stored visualization gates
        if (_visualizationGates is null || gateIndex < 0 || gateIndex >= _visualizationGates.Count)
            return;

        var gate = _visualizationGates[gateIndex];
        var dependencyIds = gate.Dependencies;

        var list = new List();
        list.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
        list.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        list.MarkerStyle = TextMarkerStyle.None;
        list.Margin = new Thickness(0, 0, 0, 12);

        foreach (var depId in dependencyIds)
        {
            var durableTask = _durablePlan?.Tasks?.FirstOrDefault(t =>
                string.Equals(t.TaskId, depId, StringComparison.Ordinal));
            var decomposedTask = _plan?.Group.Tasks?.FirstOrDefault(t =>
                string.Equals(t.Id, depId, StringComparison.Ordinal));

            var title = durableTask?.Title ?? decomposedTask?.Title ?? depId;
            var status = durableTask?.Status;

            var para = new Paragraph();
            var bulletRun = new Run("● ");
            para.Inlines.Add(bulletRun);
            para.Inlines.Add(new Run(title));

            if (status is not null)
            {
                var statusColorKey = GetTaskStatusColorKey(status);
                var statusSuffix = new Run($" — {FormatTaskStatus(status)}");
                statusSuffix.SetResourceReference(TextElement.ForegroundProperty, statusColorKey);
                para.Inlines.Add(statusSuffix);
            }

            list.ListItems.Add(new ListItem(para));
        }

        _detailDocument.Blocks.Add(list);

        if (_durablePlan is not null)
        {
            var targetIds = gate.Targets.Select(target => target.Id).ToArray();
            var approvalGate = PlanGateManager.FindEquivalentGate(
                _durablePlan, dependencyIds, targetIds) ??
                _durablePlan.ApprovalGates.FirstOrDefault(candidate =>
                    PlanGateVisualizationPolicy.GraphEquivalent(
                        _durablePlan.Tasks,
                        candidate.AfterTaskIds,
                        candidate.BeforeTaskIds,
                        dependencyIds,
                        targetIds));
            if (approvalGate is not null)
                AppendApprovalGuidance([approvalGate]);
        }
    }

    private void AppendApprovalGuidance(IEnumerable<PlanApprovalGate> gates)
    {
        if (_detailDocument is null) return;
        var distinct = gates.DistinctBy(gate => gate.GateId).ToArray();
        if (distinct.Length == 0) return;

        AddSectionHeader(distinct.Length == 1 ? "Human Approval" : "Human Approvals");
        foreach (var gate in distinct)
        {
            var message = new Paragraph(new Run(gate.Message) { FontWeight = FontWeights.SemiBold });
            message.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            message.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            message.Margin = new Thickness(0, 0, 0, 4);
            _detailDocument.Blocks.Add(message);

            var questions = ResolvePresentedHumanReviewQuestions(gate);
            if (questions.Count > 0)
            {
                var questionParagraph = new Paragraph();
                questionParagraph.Inlines.Add(new Run("Verify the Following: ") { FontWeight = FontWeights.SemiBold });
                questionParagraph.Inlines.Add(new Run(string.Join("  • ", questions)));
                questionParagraph.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
                questionParagraph.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                questionParagraph.Margin = new Thickness(0, 0, 0, 4);
                _detailDocument.Blocks.Add(questionParagraph);
            }

            var status = new Paragraph(new Run($"Status: {gate.Status}"));
            status.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
            status.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
            status.Margin = new Thickness(0, 0, 0, 10);
            _detailDocument.Blocks.Add(status);

            AppendApprovalContractEditor(gate);
        }
    }

    private void AppendGateReviewRequirements(PlanApprovalGate gate)
    {
        if (_detailDocument is null)
            return;

        var hasClarifiedReview = _durablePlan is not null && gate.HumanReviewDispositions is { Count: > 0 };
        // With full proofs, only retain an authored gate question, not a generated restatement.
        IReadOnlyList<string> approvalQuestions = hasClarifiedReview || gate.ProofRequirements is not { Count: > 0 }
            ? ResolvePresentedHumanReviewQuestions(gate)
            : string.IsNullOrWhiteSpace(gate.Question) ? [] : [gate.Question];
        var blocks = PlanGateReviewRenderer.CreateReviewBlocks(gate, approvalQuestions, hasClarifiedReview);
        if (blocks.Count > 0)
        {
            AddSectionHeader("Verify:");
            foreach (var block in blocks)
                _detailDocument.Blocks.Add(block);
        }
    }

    private IReadOnlyList<string> ResolvePresentedHumanReviewQuestions(PlanApprovalGate gate)
    {
        if (_durablePlan is not null && gate.HumanReviewDispositions is { Count: > 0 })
            return PlanHumanReviewChecklist.Build(_durablePlan, [gate])
                .Select(item => item.Text)
                .ToArray();

        var question = PlanProofCapabilityPolicy.ResolveHumanQuestion(gate);
        return string.IsNullOrWhiteSpace(question) ? [] : [question];
    }

    private void AppendApprovalContractEditor(PlanApprovalGate gate)
    {
        if (_detailDocument is null || _durablePlan is null || _onGatesChanged is null ||
            !_isInspectorEditing || !PlanGateManager.CanEditReviewContract(gate))
            return;

        var editor = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        void AddLabel(string text)
        {
            var label = new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 3),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            label.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            editor.Children.Add(label);
        }

        TextBox AddEditor(string label, string? value, double minHeight)
        {
            AddLabel(label);
            var textBox = new TextBox
            {
                Text = value ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                MinHeight = minHeight,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(6, 4, 6, 4),
            };
            textBox.SetResourceReference(TextBox.ForegroundProperty, "BodyText");
            textBox.SetResourceReference(TextBox.BackgroundProperty, "PanelBackground");
            textBox.SetResourceReference(TextBox.BorderBrushProperty, "LineColor");
            textBox.SetResourceReference(TextBox.FontSizeProperty, "FontSizeBody");
            textBox.SetResourceReference(TextBox.CaretBrushProperty, "BodyText");
            editor.Children.Add(textBox);
            return textBox;
        }

        AddSectionHeader("Edit Review Guidance");
        var messageEditor = AddEditor("Message", gate.Message, 54);
        var questionEditor = AddEditor("Approval question", gate.Question, 54);

        var proofEditors = new System.Collections.Generic.List<(
            PlanTaskProofRequirement Requirement,
            TextBox Title,
            TextBox Description,
            TextBox Question)>();
        var proofRequirements = gate.ProofRequirements?.ToArray() ?? [];
        if (proofRequirements.Length == 0)
        {
            proofRequirements =
            [
                new PlanTaskProofRequirement(
                    "human-review", "human-observation", string.Empty),
            ];
        }

        for (var index = 0; index < proofRequirements.Length; index++)
        {
            var requirement = proofRequirements[index];
            var suffix = proofRequirements.Length == 1 ? string.Empty : $" {index + 1}";
            var proofTitle = AddEditor(
                $"Scenario title{suffix}",
                PlanHumanReviewChecklist.ResolveDisplayTitle(
                    requirement.Title,
                    requirement.RequirementId),
                36);
            var description = AddEditor(
                $"Human proof requirement{suffix} ({requirement.ProofType})",
                requirement.Description,
                48);
            var proofQuestion = AddEditor(
                $"Proof-specific question{suffix} (optional)",
                requirement.Question,
                42);
            proofEditors.Add((requirement, proofTitle, description, proofQuestion));
        }

        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        status.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        status.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");

        var save = TranscriptQuickReplyFactory.CreateButton("Save review guidance", _quickReplyFontSize);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        save.Margin = new Thickness(0, 10, 0, 0);
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(messageEditor.Text) ||
                string.IsNullOrWhiteSpace(questionEditor.Text))
            {
                status.Text = "Message and approval question are required.";
                status.SetResourceReference(TextBlock.ForegroundProperty, "PriorityHigh");
                return;
            }

            var latestPlan = _durablePlan;
            var latestGate = latestPlan?.ApprovalGates.FirstOrDefault(candidate =>
                string.Equals(candidate.GateId, gate.GateId, StringComparison.Ordinal));
            if (latestPlan is null || !PlanGateManager.CanEditReviewContract(latestGate))
            {
                status.Text = "This checkpoint was resolved while you were editing it; guidance was not changed.";
                status.SetResourceReference(TextBlock.ForegroundProperty, "PriorityHigh");
                return;
            }

            var proofs = proofEditors
                .Where(item => !string.IsNullOrWhiteSpace(item.Description.Text))
                .Select(item => item.Requirement with
                {
                    Title = item.Title.Text,
                    Description = item.Description.Text,
                    Question = string.IsNullOrWhiteSpace(item.Question.Text)
                        ? null
                        : item.Question.Text,
                })
                .ToArray();
            var updated = PlanGateManager.UpdateReviewContract(
                latestPlan,
                gate.GateId,
                messageEditor.Text,
                questionEditor.Text,
                proofs);
            if (ReferenceEquals(updated, latestPlan))
            {
                status.Text = "No review-guidance changes to save.";
                status.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                return;
            }

            _onGatesChanged(updated);
            status.Text = "Review guidance saved.";
            status.SetResourceReference(TextBlock.ForegroundProperty, "PriorityLow");
        };
        editor.Children.Add(save);
        editor.Children.Add(status);

        _detailDocument.Blocks.Add(new BlockUIContainer(editor) { Margin = new Thickness(0) });
    }

    private (IReadOnlyList<PlanTask>? Tasks, Dictionary<string, int> Levels) ResolveTasksAndLevels()
    {
        var sourceTasks = _plan?.Group.Tasks;
        if (_durablePlan?.Tasks is { Count: > 0 } durableTasks)
        {
            var decomposed = durableTasks.Select(t =>
                new DecomposedSubTask(t.TaskId, t.Description ?? "", t.DependsOn ?? [], t.Priority ?? "normal", t.Title)).ToArray();
            var byId = decomposed.ToDictionary(t => t.Id, StringComparer.Ordinal);
            var levels = CalculateLevels(decomposed, byId);
            return (durableTasks, levels);
        }
        if (sourceTasks is { Count: > 0 })
        {
            var byId = sourceTasks.ToDictionary(t => t.Id, StringComparer.Ordinal);
            var levels = CalculateLevels(sourceTasks, byId);
            var planTasks = sourceTasks.Select(t => new PlanTask(
                t.Id, t.Title, t.Description, t.DependsOn, t.Priority, PlanTaskStatus.Pending)).ToArray();
            return (planTasks, levels);
        }
        return (null, new Dictionary<string, int>());
    }

    private void AddTaskStatusList(IReadOnlyList<PlanTask> tasks)
    {
        if (_detailDocument is null) return;
        var list = new List();
        list.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
        list.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        list.MarkerStyle = TextMarkerStyle.Disc;
        list.Margin = new Thickness(0, 0, 0, 12);
        foreach (var task in tasks)
        {
            var statusColor = GetTaskStatusColorKey(task.Status);
            var para = new Paragraph();
            para.Inlines.Add(new Run(task.Title ?? task.TaskId));
            var statusSuffix = new Run($" ({FormatTaskStatus(task.Status)})");
            statusSuffix.SetResourceReference(TextElement.ForegroundProperty, statusColor);
            para.Inlines.Add(statusSuffix);
            list.ListItems.Add(new ListItem(para));
        }
        _detailDocument.Blocks.Add(list);
    }

    private void AddTaskIdList(string header, IReadOnlyList<string>? taskIds, Plan? plan)
    {
        if (taskIds is not { Count: > 0 } || _detailDocument is null)
            return;

        AddSectionHeader(header);
        var list = new List();
        list.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        list.MarkerStyle = TextMarkerStyle.Disc;
        list.Margin = new Thickness(0, 0, 0, 12);
        foreach (var taskId in taskIds)
        {
            var task = plan?.Tasks?.FirstOrDefault(t => t.TaskId == taskId);
            var title = task?.Title ?? taskId;
            var para = new Paragraph(CreateTaskLink(title, taskId));
            list.ListItems.Add(new ListItem(para));
        }
        _detailDocument.Blocks.Add(list);
    }

    private void PopulateAgentDetail(string agentHandle)
    {
        if (_detailDocument is null)
            return;

        _detailDocument.Blocks.Clear();
        var isVerityCross = string.Equals(agentHandle, VerityCrossHandle, StringComparison.OrdinalIgnoreCase);

        // Agent image (upper-left)
        var avatarInfo = _resolveAgentAvatar?.Invoke(agentHandle);
        var avatarSize = 64.0 * _scaleFactor;
        var avatarElement = CreateAgentAvatarChip(avatarInfo, avatarSize, agentHandle);
        avatarElement.HorizontalAlignment = HorizontalAlignment.Left;
        _detailDocument.Blocks.Add(new BlockUIContainer(avatarElement) { Margin = new Thickness(0, 0, 0, 8) });

        PlanTask? currentVerifyingTask = null;
        var hasActiveVerifyingTask = false;
        var activeValidation = isVerityCross
            ? _durablePlan?.Validations?.FirstOrDefault(v =>
                string.Equals(v.Status, PlanValidationStatus.Validating, StringComparison.Ordinal))
            : null;

        // Agent name heading (below image)
        var titleText = HumanizeAgentHandle(agentHandle);
        var titlePara = new Paragraph(new Run(titleText) { FontWeight = FontWeights.Bold });
        titlePara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
        titlePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeLarge");
        titlePara.Margin = new Thickness(0, 0, 0, 8);
        _detailDocument.Blocks.Add(titlePara);

        var taskList = new List();
        taskList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
        taskList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        taskList.MarkerStyle = TextMarkerStyle.Disc;
        taskList.Margin = new Thickness(0, 0, 0, 12);

        var durableTasks = _durablePlan?.Tasks;
        if (durableTasks is not null)
        {
            var stepNumber = 0;
            foreach (var task in durableTasks)
            {
                stepNumber++;
                var assignment = task.AgentAssignments?.FirstOrDefault(a =>
                    string.Equals(a.AgentHandle, agentHandle, StringComparison.OrdinalIgnoreCase));
                if (assignment is null) continue;

                var isVerifying = PlanTaskStatus.IsVerifying(task.Status);
                var isVerificationPending = string.Equals(task.Status, PlanTaskStatus.VerificationPending, StringComparison.Ordinal);
                if ((isVerifying || isVerificationPending) &&
                    (currentVerifyingTask is null || (!hasActiveVerifyingTask && isVerifying)))
                {
                    currentVerifyingTask = task;
                    hasActiveVerifyingTask = isVerifying;
                }

                var stepRun = new Run($"Step {stepNumber}. ");
                stepRun.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                var titleLink = CreateTaskLink(task.Title ?? task.TaskId, task.TaskId);
                var isActiveTask = string.Equals(task.Status, PlanTaskStatus.Executing, StringComparison.Ordinal)
                    || PlanTaskStatus.IsVerifying(task.Status);
                var statusColorKey = isActiveTask ? "ImportantText" : "SubtleText";
                var statusText = FormatTaskStatus(task.Status)?.ToLowerInvariant();
                var roleRun = new Run($"\n{assignment.Role}");
                roleRun.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");

                var para = new Paragraph();
                para.Inlines.Add(stepRun);
                para.Inlines.Add(titleLink);
                if (!string.IsNullOrWhiteSpace(statusText))
                {
                    var statusRun = new Run($" ({statusText})");
                    statusRun.SetResourceReference(TextElement.ForegroundProperty, statusColorKey);
                    para.Inlines.Add(statusRun);
                }
                para.Inlines.Add(roleRun);
                para.Margin = new Thickness(0, 6, 0, 10);
                taskList.ListItems.Add(new ListItem(para));
            }
        }
        else
        {
            // Fallback to decomposed plan tasks
            var decomposedTasks = _plan?.Group.Tasks;
            if (decomposedTasks is not null)
            {
                var stepNumber = 0;
                foreach (var task in decomposedTasks)
                {
                    stepNumber++;
                    var assignment = task.AgentAssignments?.FirstOrDefault(a =>
                        string.Equals(a.AgentHandle, agentHandle, StringComparison.OrdinalIgnoreCase));
                    if (assignment is null) continue;

                    var stepRun = new Run($"Step {stepNumber}. ");
                    stepRun.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                    var titleLink = CreateTaskLink(task.Title ?? task.Id, task.Id);
                    var roleRun = new Run($"\n{assignment.Role}");
                    roleRun.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");

                    var para = new Paragraph();
                    para.Inlines.Add(stepRun);
                    para.Inlines.Add(titleLink);
                    para.Inlines.Add(roleRun);
                    para.Margin = new Thickness(0, 6, 0, 10);
                    taskList.ListItems.Add(new ListItem(para));
                }
            }
        }

        if (isVerityCross && hasActiveVerifyingTask && currentVerifyingTask is not null)
        {
            _detailDocument.Blocks.Remove(titlePara);
            var verityTitlePara = new Paragraph(new Run(HumanizeAgentHandle(agentHandle)) { FontWeight = FontWeights.Bold });
            verityTitlePara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
            verityTitlePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeLarge");
            verityTitlePara.Margin = new Thickness(0, 0, 0, 8);
            _detailDocument.Blocks.Add(verityTitlePara);

            AddSectionHeader("Currently verifying");

            var verifyingTaskTitle = new Paragraph(new Run(currentVerifyingTask.Title ?? currentVerifyingTask.TaskId)
            {
                FontWeight = FontWeights.Bold,
            });
            verifyingTaskTitle.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
            verifyingTaskTitle.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            verifyingTaskTitle.Margin = new Thickness(0, 0, 0, 4);
            _detailDocument.Blocks.Add(verifyingTaskTitle);

            if (!string.IsNullOrWhiteSpace(currentVerifyingTask.Description))
                AppendTaskDescription(currentVerifyingTask.Description, currentVerifyingTask.TaskId);

            if (!string.IsNullOrWhiteSpace(currentVerifyingTask.VerificationLevel))
            {
                var verificationLevelPara = new Paragraph();
                verificationLevelPara.Inlines.Add(new Run("Verification level: ") { FontWeight = FontWeights.SemiBold });
                verificationLevelPara.Inlines.Add(new Run(currentVerifyingTask.VerificationLevel));
                verificationLevelPara.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                verificationLevelPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                verificationLevelPara.Margin = new Thickness(0, 0, 0, 8);
                _detailDocument.Blocks.Add(verificationLevelPara);
            }

            AddSectionHeader("What she's checking");
            if (currentVerifyingTask.ProofRequirements is { Count: > 0 } proofRequirements)
            {
                var proofList = new List();
                proofList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                proofList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                proofList.MarkerStyle = TextMarkerStyle.Disc;
                proofList.Margin = new Thickness(0, 0, 0, 12);
                foreach (var proofRequirement in proofRequirements)
                {
                    var proofPara = new Paragraph();
                    if (!string.IsNullOrWhiteSpace(proofRequirement.ProofType) && proofRequirement.ProofType.Length <= 20)
                    {
                        var proofTypeRun = new Run($"{proofRequirement.ProofType}: ");
                        proofTypeRun.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                        proofPara.Inlines.Add(proofTypeRun);
                    }

                    proofPara.Inlines.Add(new Run(proofRequirement.Description));
                    proofList.ListItems.Add(new ListItem(proofPara));
                }
                _detailDocument.Blocks.Add(proofList);
            }
            else
            {
                var defaultGuidancePara = new Paragraph(new Run(PlanTaskVerificationInstructions.EnhancedIndependentReview));
                defaultGuidancePara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                defaultGuidancePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                defaultGuidancePara.Margin = new Thickness(0, 0, 0, 12);
                _detailDocument.Blocks.Add(defaultGuidancePara);
            }

            if (currentVerifyingTask.VerificationHistory is { Count: > 0 } verificationHistory)
            {
                var historyPara = new Paragraph(new Run($"Previous verification rounds: {verificationHistory.Count}"));
                historyPara.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
                historyPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                historyPara.Margin = new Thickness(0, 0, 0, 8);
                _detailDocument.Blocks.Add(historyPara);
            }
        }
        else if (isVerityCross && activeValidation is not null)
        {
            _detailDocument.Blocks.Remove(titlePara);

            var verityTitlePara = new Paragraph(new Run(HumanizeAgentHandle(agentHandle)) { FontWeight = FontWeights.Bold });
            verityTitlePara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
            verityTitlePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeLarge");
            verityTitlePara.Margin = new Thickness(0, 0, 0, 8);
            _detailDocument.Blocks.Add(verityTitlePara);

            AddSectionHeader("Currently validating");

            var validationTitlePara = new Paragraph(new Run(activeValidation.Title) { FontWeight = FontWeights.Bold });
            validationTitlePara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
            validationTitlePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            validationTitlePara.Margin = new Thickness(0, 0, 0, 4);
            _detailDocument.Blocks.Add(validationTitlePara);

            if (!string.IsNullOrWhiteSpace(activeValidation.Description))
            {
                var descriptionPara = new Paragraph(new Run(activeValidation.Description));
                descriptionPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                descriptionPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                descriptionPara.Margin = new Thickness(0, 0, 0, 6);
                _detailDocument.Blocks.Add(descriptionPara);
            }

            if (activeValidation.Assertions is { Count: > 0 } assertions)
            {
                AddSectionHeader("What she's checking");
                var assertionList = new List();
                assertionList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                assertionList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                assertionList.MarkerStyle = TextMarkerStyle.Disc;
                assertionList.Margin = new Thickness(0, 0, 0, 12);
                foreach (var assertion in assertions)
                {
                    assertionList.ListItems.Add(new ListItem(new Paragraph(new Run(assertion))));
                }
                _detailDocument.Blocks.Add(assertionList);
            }
        }
        else if (!string.IsNullOrWhiteSpace(currentVerifyingTask?.Title ?? currentVerifyingTask?.TaskId))
        {
            var verifyingPara = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 8),
            };
            verifyingPara.Inlines.Add(new Run("Currently verifying: "));
            verifyingPara.Inlines.Add(new Run(currentVerifyingTask?.Title ?? currentVerifyingTask?.TaskId) { FontWeight = FontWeights.SemiBold });
            verifyingPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            verifyingPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            _detailDocument.Blocks.Add(verifyingPara);
        }

        if (taskList.ListItems.Count > 0)
        {
            AddSectionHeader("Responsibilities");
            _detailDocument.Blocks.Add(taskList);
        }
    }

    private void AddSectionHeader(string text)
    {
        if (_detailDocument is null) return;
        var header = new Paragraph(new Run(text) { FontWeight = FontWeights.SemiBold });
        header.SetResourceReference(TextElement.ForegroundProperty, "SectionHeader");
        header.SetResourceReference(TextElement.FontSizeProperty, "FontSizeLargePlus");
        header.Margin = new Thickness(0, 13, 0, 2);
        _detailDocument.Blocks.Add(header);
    }

    private static string GetTaskStatusColorKey(string? status) => status switch
    {
        PlanTaskStatus.Complete or PlanTaskStatus.Superseded => "PriorityLow",
        PlanTaskStatus.Executing => "ActivePanelBorder",
        PlanTaskStatus.Reworking => "PriorityMid",
        PlanTaskStatus.VerificationPending => "PriorityMid",
        PlanTaskStatus.HumanReviewRequired or PlanTaskStatus.Failed => "PriorityHigh",
        PlanTaskStatus.Partial => "PriorityMid",
        _ when PlanTaskStatus.IsVerifying(status) => "ActivePanelBorder",
        _ => "SubtleText",
    };

    private static string FormatTaskStatus(string? status) => status switch
    {
        PlanTaskStatus.Pending => "Pending",
        PlanTaskStatus.Executing => "Executing",
        PlanTaskStatus.Complete => "Complete",
        PlanTaskStatus.Failed => "Failed",
        PlanTaskStatus.Partial => "Partial",
        PlanTaskStatus.Superseded => "Superseded",
        PlanTaskStatus.Reworking => "Reworking",
        PlanTaskStatus.VerificationPending => "Verification Pending",
        PlanTaskStatus.HumanReviewRequired => "Review Required",
        _ when PlanTaskStatus.IsVerifying(status) => "Verifying",
        _ => status ?? "Unknown",
    };

    private static string HumanizeAgentHandle(string handle)
    {
        var parts = handle.Split('-');
        return string.Join(" ", parts.Select(p =>
            p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..] : p));
    }

    private void WireSelectionClick(FrameworkElement element)
    {
        void SelectTaggedElement(object sender)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string tag) return;
            var parts = tag.Split(':', 2);
            if (parts.Length == 2)
                SelectElement(parts[0], parts[1]);
        }

        element.MouseLeftButtonDown += (sender, e) =>
        {
            if (e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase
                || (e.OriginalSource is FrameworkElement src && src.TemplatedParent is System.Windows.Controls.Primitives.ButtonBase))
                return;
            SelectTaggedElement(sender);
            e.Handled = true;
        };

        // Select the same tagged graph element before WPF opens its context menu. Do not mark the
        // event handled: the right-click still needs to reach the element and open that menu.
        element.PreviewMouseRightButtonDown += (sender, _) => SelectTaggedElement(sender);
    }

    private void WireTaskCardDrag(FrameworkElement element, string taskId)
    {
        element.PreviewMouseLeftButtonDown += (_, e) => OnTaskCardPreviewMouseLeftButtonDown(element, taskId, e);
        element.MouseLeftButtonUp += OnTaskCardMouseLeftButtonUp;
        element.LostMouseCapture += OnTaskCardLostMouseCapture;
        // Right-click selects the card before the context menu opens (same as WireSelectionClick does for other elements).
        element.PreviewMouseRightButtonDown += (_, _) => SelectElement("task", taskId);
    }

    private static IReadOnlyList<(UIElement Connector, Effect? OriginalEffect)> CaptureConnectorEffects(
        IReadOnlyList<DragConnectorState> connectorStates) =>
        connectorStates
            .SelectMany(state => state.Group.MainElements)
            .Distinct()
            .Select(element => (Connector: element, OriginalEffect: (Effect?)element.Effect))
            .ToArray();

    private static string EdgeKey(string sourceTaskId, string targetTaskId) =>
        $"{sourceTaskId}\u001f{targetTaskId}";

    private HashSet<string> ResolveDashedTaskEdgeKeys()
    {
        var sourceGroup = _plan?.Group;
        if (sourceGroup is null)
            return [];

        var (tasks, _) = ResolveTasksAndLevels();
        if (tasks is null || tasks.Count == 0)
            return [];

        var visualizationGates = _durablePlan?.ApprovalGates ?? (sourceGroup.ApprovalGates ?? [])
            .Select(gate => new PlanApprovalGate(
                gate.GateId,
                gate.Message,
                gate.AfterTaskIds ?? [],
                gate.BeforeTaskIds ?? [],
                PlanGateStatus.Pending,
                Question: gate.Question))
            .ToArray();
        var dashedTaskEdges = PlanGateVisualizationPolicy.DashedEdges(
            tasks,
            visualizationGates,
            requireEveryIncomingAtConvergence: true);
        return dashedTaskEdges
            .Select(edge => EdgeKey(edge.Item1, edge.Item2))
            .ToHashSet(StringComparer.Ordinal);
    }

    private void RegisterTaskDragOverlay(string taskId, UIElement overlay)
    {
        if (!_taskDragOverlaysByTask.TryGetValue(taskId, out var overlays))
            _taskDragOverlaysByTask[taskId] = overlays = [];
        if (!overlays.Contains(overlay))
            overlays.Add(overlay);
    }

    private IReadOnlyList<(UIElement Element, double OriginalTop)> CaptureTaskDragOverlays(string taskId)
    {
        if (!_taskDragOverlaysByTask.TryGetValue(taskId, out var overlays) || overlays.Count == 0)
            return [];

        var captured = new List<(UIElement Element, double OriginalTop)>();
        foreach (var overlay in overlays.Distinct())
        {
            var top = Canvas.GetTop(overlay);
            if (double.IsNaN(top))
                continue;
            captured.Add((overlay, top));
        }

        return captured;
    }

    private static bool TryGetTaskIdTag(FrameworkElement element, out string taskId)
    {
        taskId = string.Empty;
        if (element.Tag is not string tag || !tag.StartsWith("task:", StringComparison.Ordinal))
            return false;
        taskId = tag["task:".Length..];
        return !string.IsNullOrWhiteSpace(taskId);
    }

    private static bool IsSameColumn(double left, double columnX) =>
        double.IsFinite(left) && Math.Abs(left - columnX) <= 0.5;

    private string ResolveColumnIdentifier(double columnX)
    {
        if (_graphCanvas is null)
            return "stage:unknown";

        var nearestDistance = double.MaxValue;
        var nearestStage = "stage:unknown";
        foreach (var child in _graphCanvas.Children.OfType<FrameworkElement>())
        {
            if (child.Tag is not string tag || !tag.StartsWith("stage:", StringComparison.Ordinal))
                continue;
            var left = Canvas.GetLeft(child);
            if (!double.IsFinite(left))
                continue;
            var distance = Math.Abs(left - columnX);
            if (distance >= nearestDistance)
                continue;
            nearestDistance = distance;
            nearestStage = tag;
        }
        return nearestStage;
    }

    private static bool TryParseStageIndex(string columnIdentifier, out int stageIndex)
    {
        const string stagePrefix = "stage:";
        if (columnIdentifier.StartsWith(stagePrefix, StringComparison.Ordinal) &&
            int.TryParse(columnIdentifier[stagePrefix.Length..], out var parsed) &&
            parsed >= 0)
        {
            stageIndex = parsed;
            return true;
        }

        stageIndex = -1;
        return false;
    }

    private static IReadOnlyList<string> BuildReorderedStageTaskIds(
        IReadOnlyList<string> existingStageTaskIds,
        IReadOnlyList<string> requestedStageOrder)
    {
        var stageTaskIdSet = existingStageTaskIds.ToHashSet(StringComparer.Ordinal);
        var reordered = requestedStageOrder
            .Where(taskId => !string.IsNullOrWhiteSpace(taskId) && stageTaskIdSet.Contains(taskId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var taskId in existingStageTaskIds)
        {
            if (!reordered.Contains(taskId, StringComparer.Ordinal))
                reordered.Add(taskId);
        }
        return reordered;
    }

    private Plan ApplyCommittedColumnReorder(Plan plan)
    {
        if (_lastCommittedColumnOrder is not { } committed)
            return plan;
        return TryApplyCommittedColumnReorder(plan, committed, out var reorderedPlan)
            ? reorderedPlan
            : plan;
    }

    private bool TryApplyCommittedColumnReorder(Plan plan, CommittedColumnReorder committed, out Plan reorderedPlan)
    {
        reorderedPlan = plan;
        if (committed.NewTaskOrder.Count == 0)
            return false;
        if (!TryParseStageIndex(committed.ColumnTaskId, out var stageIndex))
        {
            SquadDashTrace.Write(
                TraceCategory.General,
                $"Plan viewer drag reorder skipped: unresolved column identifier '{committed.ColumnTaskId}'.");
            return false;
        }
        if (plan.Tasks.Count == 0)
            return false;

        var tasksById = plan.Tasks.ToDictionary(task => task.TaskId, StringComparer.Ordinal);
        var decomposedTasks = plan.Tasks
            .Select(task => new DecomposedSubTask(
                task.TaskId,
                task.Description ?? string.Empty,
                task.DependsOn ?? [],
                task.Priority ?? "normal",
                task.Title))
            .ToArray();
        var levels = CalculateLevels(
            decomposedTasks,
            decomposedTasks.ToDictionary(task => task.Id, StringComparer.Ordinal));
        var stageTaskIds = plan.Tasks
            .Where(task => levels.TryGetValue(task.TaskId, out var level) && level == stageIndex)
            .Select(task => task.TaskId)
            .ToArray();
        if (stageTaskIds.Length == 0)
        {
            SquadDashTrace.Write(
                TraceCategory.General,
                $"Plan viewer drag reorder skipped: no stage {stageIndex} found for '{committed.ColumnTaskId}'.");
            return false;
        }

        var reorderedStageTaskIds = BuildReorderedStageTaskIds(stageTaskIds, committed.NewTaskOrder);
        if (stageTaskIds.SequenceEqual(reorderedStageTaskIds, StringComparer.Ordinal))
            return false;

        var reorderedStageTasks = new Queue<PlanTask>(
            reorderedStageTaskIds
                .Where(tasksById.ContainsKey)
                .Select(taskId => tasksById[taskId]));
        reorderedPlan = plan with
        {
            Tasks = plan.Tasks
                .Select(task =>
                    levels.TryGetValue(task.TaskId, out var level) && level == stageIndex
                        ? reorderedStageTasks.Dequeue()
                        : task)
                .ToArray(),
        };
        return true;
    }

    private bool TryApplyCommittedColumnReorder(
        DecomposedTaskGroup group,
        CommittedColumnReorder committed,
        out DecomposedTaskGroup reorderedGroup)
    {
        reorderedGroup = group;
        if (committed.NewTaskOrder.Count == 0)
            return false;
        if (!TryParseStageIndex(committed.ColumnTaskId, out var stageIndex))
        {
            SquadDashTrace.Write(
                TraceCategory.General,
                $"Plan viewer drag reorder skipped: unresolved column identifier '{committed.ColumnTaskId}'.");
            return false;
        }
        if (group.Tasks.Count == 0)
            return false;

        var tasksById = group.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        var levels = CalculateLevels(group.Tasks, tasksById);
        var stageTaskIds = group.Tasks
            .Where(task => levels.TryGetValue(task.Id, out var level) && level == stageIndex)
            .Select(task => task.Id)
            .ToArray();
        if (stageTaskIds.Length == 0)
        {
            SquadDashTrace.Write(
                TraceCategory.General,
                $"Plan viewer drag reorder skipped: no stage {stageIndex} found for '{committed.ColumnTaskId}'.");
            return false;
        }

        var reorderedStageTaskIds = BuildReorderedStageTaskIds(stageTaskIds, committed.NewTaskOrder);
        if (stageTaskIds.SequenceEqual(reorderedStageTaskIds, StringComparer.Ordinal))
            return false;

        var reorderedStageTasks = new Queue<DecomposedSubTask>(
            reorderedStageTaskIds
                .Where(tasksById.ContainsKey)
                .Select(taskId => tasksById[taskId]));
        reorderedGroup = group with
        {
            Tasks = group.Tasks
                .Select(task =>
                    levels.TryGetValue(task.Id, out var level) && level == stageIndex
                        ? reorderedStageTasks.Dequeue()
                        : task)
                .ToArray(),
        };
        return true;
    }

    private bool PersistCommittedTaskCardReorder()
    {
        if (_lastCommittedColumnOrder is not { } committed)
            return false;

        if (_saveDefinitionEdit is not null &&
            _plan is not null &&
            TryApplyCommittedColumnReorder(_plan.Group, committed, out var reorderedGroup))
        {
            return ApplyStagedMutation(
                reorderedGroup,
                preserveReview: true,
                failurePrefix: "Order wasn’t saved. ");
        }

        if (_durablePlan is not null &&
            TryApplyCommittedColumnReorder(_durablePlan, committed, out var reorderedDurablePlan))
        {
            _durablePlan = reorderedDurablePlan;
            _plan = PendingDecomposePlanAdapter.FromPlan(reorderedDurablePlan);
            return true;
        }

        if (_plan is null)
            return false;
        if (TryApplyCommittedColumnReorder(_plan.Group, committed, out var reorderedPendingGroup))
        {
            _plan = _plan with { Group = reorderedPendingGroup };
            return true;
        }
        return false;
    }

    private static double ResolveSlotTop(TaskCardDragState dragState, int orderIndex)
    {
        if (orderIndex >= 0 && orderIndex < dragState.ColumnSlotTops.Count)
            return dragState.ColumnSlotTops[orderIndex];
        return dragState.OriginalTop;
    }

    private static double ResolveSiblingCurrentTop(
        TaskCardDragState dragState,
        (FrameworkElement Card, double OriginalTop, string TaskId) sibling)
    {
        if (dragState.DisplacedSiblingTops.TryGetValue(sibling.TaskId, out var displacedTop))
            return displacedTop;
        return sibling.OriginalTop;
    }

    private void AnimateCanvasTop(
        UIElement element,
        double toTop,
        TimeSpan duration,
        Action<double>? onCompleted = null)
    {
        element.BeginAnimation(Canvas.TopProperty, null);
        var animation = new DoubleAnimation(toTop, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        animation.Completed += (_, _) =>
        {
            element.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetTop(element, toTop);
            onCompleted?.Invoke(toTop);
        };
        element.BeginAnimation(Canvas.TopProperty, animation);
    }

    private void ApplyTaskCardTopAndConnectors(string taskId, double left, double top)
    {
        var priorTop = _taskCardPositions.TryGetValue(taskId, out var priorPosition)
            ? priorPosition.Y
            : top;
        _taskCardPositions[taskId] = new Point(left, top);
        if (!_connectorsByTask.TryGetValue(taskId, out var groups) || groups.Count == 0)
            return;
        var connectedConnectorStates = BuildDragConnectorStates(taskId, priorTop);
        var siblingDragState = new TaskCardDragState
        {
            TaskId = taskId,
            ColumnX = left,
            ConnectedConnectorStates = connectedConnectorStates,
        };
        UpdateTaskCardDragConnectors(siblingDragState, top);
    }

    private bool TryPerformDragSwap(TaskCardDragState dragState, double draggedMidpoint)
    {
        var draggedIndex = dragState.CurrentColumnOrder.IndexOf(dragState.TaskId);
        if (draggedIndex < 0)
            return false;

        if (draggedIndex > 0)
        {
            var siblingTaskId = dragState.CurrentColumnOrder[draggedIndex - 1];
            var sibling = dragState.SiblingCards.FirstOrDefault(value =>
                string.Equals(value.TaskId, siblingTaskId, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(sibling.TaskId))
            {
                var siblingCurrentTop = ResolveSiblingCurrentTop(dragState, sibling);
                var siblingMidpoint = siblingCurrentTop + sibling.Card.ActualHeight / 2.0;
                if (draggedMidpoint < siblingMidpoint)
                {
                    var siblingTargetTop = ResolveSlotTop(dragState, draggedIndex);
                    PerformVisualSwap(dragState, sibling, siblingTargetTop, draggedIndex, draggedIndex - 1);
                    return true;
                }
            }
        }

        if (draggedIndex + 1 < dragState.CurrentColumnOrder.Count)
        {
            var siblingTaskId = dragState.CurrentColumnOrder[draggedIndex + 1];
            var sibling = dragState.SiblingCards.FirstOrDefault(value =>
                string.Equals(value.TaskId, siblingTaskId, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(sibling.TaskId))
            {
                var siblingCurrentTop = ResolveSiblingCurrentTop(dragState, sibling);
                var siblingMidpoint = siblingCurrentTop + sibling.Card.ActualHeight / 2.0;
                if (draggedMidpoint > siblingMidpoint)
                {
                    var siblingTargetTop = ResolveSlotTop(dragState, draggedIndex);
                    PerformVisualSwap(dragState, sibling, siblingTargetTop, draggedIndex, draggedIndex + 1);
                    return true;
                }
            }
        }

        return false;
    }

    private void PerformVisualSwap(
        TaskCardDragState dragState,
        (FrameworkElement Card, double OriginalTop, string TaskId) sibling,
        double newSiblingTop,
        int draggedIndex,
        int siblingIndex)
    {
        var transitionDuration = TimeSpan.FromMilliseconds(180);
        AnimateCanvasTop(
            sibling.Card,
            newSiblingTop,
            transitionDuration,
            finalTop => ApplyTaskCardTopAndConnectors(sibling.TaskId, dragState.ColumnX, finalTop));
        dragState.DisplacedSiblingTops[sibling.TaskId] = newSiblingTop;
        ApplyTaskCardTopAndConnectors(sibling.TaskId, dragState.ColumnX, newSiblingTop);

        if (dragState.SiblingOverlayElements.TryGetValue(sibling.TaskId, out var overlays))
        {
            var siblingDelta = newSiblingTop - sibling.OriginalTop;
            foreach (var (overlay, originalTop) in overlays)
                AnimateCanvasTop(overlay, originalTop + siblingDelta, transitionDuration);
        }

        if (draggedIndex >= 0 &&
            draggedIndex < dragState.CurrentColumnOrder.Count &&
            siblingIndex >= 0 &&
            siblingIndex < dragState.CurrentColumnOrder.Count)
        {
            (dragState.CurrentColumnOrder[draggedIndex], dragState.CurrentColumnOrder[siblingIndex]) =
                (dragState.CurrentColumnOrder[siblingIndex], dragState.CurrentColumnOrder[draggedIndex]);
        }
    }

    private IReadOnlyList<DragConnectorState> BuildDragConnectorStates(string taskId, double originalTop)
    {
        if (!_connectorsByTask.TryGetValue(taskId, out var groups))
            return [];

        var (_, levels) = ResolveTasksAndLevels();
        var dashedTaskEdges = ResolveDashedTaskEdgeKeys();
        var states = new List<DragConnectorState>();
        foreach (var group in groups.Distinct())
        {
            var role = group.GetEndpointRole(taskId);
            if (role == PlanGraphConnectorEndpointRole.None)
                continue;

            var sourceTaskId = group.TaskIds.FirstOrDefault(id =>
                group.GetEndpointRole(id).HasFlag(PlanGraphConnectorEndpointRole.Start));
            var targetTaskId = group.TaskIds.FirstOrDefault(id =>
                group.GetEndpointRole(id).HasFlag(PlanGraphConnectorEndpointRole.End));
            var hasDirectTaskPair =
                !string.IsNullOrWhiteSpace(sourceTaskId) &&
                !string.IsNullOrWhiteSpace(targetTaskId) &&
                !string.Equals(sourceTaskId, targetTaskId, StringComparison.Ordinal);
            var baseSkipCount = hasDirectTaskPair &&
                                levels.TryGetValue(sourceTaskId!, out var sourceLevel) &&
                                levels.TryGetValue(targetTaskId!, out var targetLevel)
                ? Math.Max(0, targetLevel - sourceLevel - 1)
                : group.SkipCount;
            var state = new DragConnectorState
            {
                Group = group,
                Role = role,
                StartOffsetWithinCard = role.HasFlag(PlanGraphConnectorEndpointRole.Start)
                    ? group.CurrentFrom.Y - originalTop
                    : null,
                EndOffsetWithinCard = role.HasFlag(PlanGraphConnectorEndpointRole.End)
                    ? group.CurrentTo.Y - originalTop
                    : null,
                SourceTaskId = sourceTaskId,
                TargetTaskId = targetTaskId,
                BaseSkipCount = baseSkipCount,
                IsDashed = hasDirectTaskPair &&
                           dashedTaskEdges.Contains(EdgeKey(sourceTaskId!, targetTaskId!)),
            };
            states.Add(state);
        }

        return states;
    }

    private void ApplyTaskCardDragGlow(TaskCardDragState dragState)
    {
        dragState.Element.Effect = TaskNodeGlowEffect();
        foreach (var connectorState in dragState.ConnectedConnectorStates)
        {
            var connectorGroup = connectorState.Group;
            connectorGroup.RefreshGlowBrushes();
            foreach (var element in connectorGroup.GlowElements)
            {
                element.Visibility = Visibility.Visible;
                Panel.SetZIndex(element, 3);
            }
            foreach (var element in connectorGroup.MainElements)
                Panel.SetZIndex(element, 4);
        }
        foreach (var (connector, _) in dragState.ConnectedConnectors)
            connector.Effect = TaskNodeGlowEffect();
    }

    /// <summary>
    /// Computes the Y position for a connector's exit point on the source card's right edge,
    /// distributing exits vertically in sorted target-Y order to minimize crossings.
    /// </summary>
    private double ComputeSourceExitY(
        string sourceTaskId,
        PlanGraphConnectorGroup thisConnectorGroup,
        double thisDraggedTargetMidY,
        double sourceCardTop)
    {
        if (!_connectorsByTask.TryGetValue(sourceTaskId, out var groups))
            return sourceCardTop + NodeHeight / 2.0;

        var targetYs = new List<double>();
        foreach (var g in groups.Distinct())
        {
            if (!g.GetEndpointRole(sourceTaskId).HasFlag(PlanGraphConnectorEndpointRole.Start))
                continue;

            if (ReferenceEquals(g, thisConnectorGroup))
            {
                targetYs.Add(thisDraggedTargetMidY);
            }
            else
            {
                var targetId = g.TaskIds.FirstOrDefault(id =>
                    g.GetEndpointRole(id).HasFlag(PlanGraphConnectorEndpointRole.End));
                if (targetId is not null && _taskCardPositions.TryGetValue(targetId, out var targetPos))
                    targetYs.Add(targetPos.Y + NodeHeight / 2.0);
                else
                    targetYs.Add(g.CurrentTo.Y);
            }
        }

        if (targetYs.Count <= 1)
            return sourceCardTop + NodeHeight / 2.0;

        targetYs.Sort();

        var rank = targetYs.BinarySearch(thisDraggedTargetMidY);
        if (rank < 0)
            rank = ~rank;
        rank = Math.Min(rank, targetYs.Count - 1);
        return sourceCardTop + NodeHeight * (rank + 1.0) / (targetYs.Count + 1.0);
    }

    private void UpdateTaskCardDragConnectors(TaskCardDragState dragState, double top)
    {
        if (_graphCanvas is null)
            return;
        var left = dragState.ColumnX;
        _taskCardPositions[dragState.TaskId] = new Point(left, top);
        foreach (var connectorState in dragState.ConnectedConnectorStates)
        {
            var group = connectorState.Group;
            var updatedFrom = group.CurrentFrom;
            var updatedTo = group.CurrentTo;
            if (connectorState.Role.HasFlag(PlanGraphConnectorEndpointRole.Start) &&
                connectorState.StartOffsetWithinCard.HasValue)
            {
                updatedFrom = new Point(left + NodeWidth, top + connectorState.StartOffsetWithinCard.Value);
            }
            if (connectorState.Role.HasFlag(PlanGraphConnectorEndpointRole.End) &&
                connectorState.EndOffsetWithinCard.HasValue)
            {
                updatedTo = new Point(left, top + connectorState.EndOffsetWithinCard.Value);
            }

            // When the dragged task is the target (End) but NOT the source of this connector,
            // recalculate the upstream exit Y so arrows adjust during drag to minimize crossings.
            if (connectorState.Role == PlanGraphConnectorEndpointRole.End &&
                !string.IsNullOrWhiteSpace(connectorState.SourceTaskId) &&
                _taskCardPositions.TryGetValue(connectorState.SourceTaskId, out var sourcePos))
            {
                var cardHeight = dragState.Element is not null && dragState.Element.ActualHeight > 0
                    ? dragState.Element.ActualHeight
                    : NodeHeight;
                var draggedMidY = top + cardHeight / 2.0;
                var newExitY = ComputeSourceExitY(connectorState.SourceTaskId, group, draggedMidY, sourcePos.Y);
                updatedFrom = new Point(updatedFrom.X, newExitY);
            }

            if (!string.IsNullOrWhiteSpace(connectorState.SourceTaskId) &&
                !string.IsNullOrWhiteSpace(connectorState.TargetTaskId) &&
                group.TaskIds.Count >= 2)
            {
                RemoveConnectorGroupElements(_graphCanvas, group);
                var replacementGroup = AddDirectTaskConnector(
                    _graphCanvas,
                    updatedFrom,
                    updatedTo,
                    connectorState.BaseSkipCount,
                    connectorState.IsDashed);
                ReplaceConnectorGroupForDrag(connectorState, replacementGroup);
                replacementGroup.RefreshGlowBrushes();
                foreach (var element in replacementGroup.GlowElements)
                {
                    element.Visibility = Visibility.Visible;
                    Panel.SetZIndex(element, 3);
                }
                foreach (var element in replacementGroup.MainElements)
                    Panel.SetZIndex(element, 4);
            }
            else
            {
                group.UpdateEndpoints(updatedFrom, updatedTo);
            }
        }

        // Reposition ALL gate badges whose connected tasks moved.
        if (_visualizationGates is { Count: > 0 } gates && _graphCanvas is not null)
        {
            for (var gi = 0; gi < gates.Count; gi++)
            {
                var gate = gates[gi];
                var gateAffected =
                    gate.Dependencies.Any(depId =>
                        string.Equals(depId, dragState.TaskId, StringComparison.Ordinal) ||
                        dragState.DisplacedSiblingTops.ContainsKey(depId)) ||
                    gate.Targets.Any(target =>
                        string.Equals(target.Id, dragState.TaskId, StringComparison.Ordinal) ||
                        dragState.DisplacedSiblingTops.ContainsKey(target.Id));

                if (!gateAffected)
                    continue;

                var connectedMidpointYSum = 0.0;
                var connectedTaskCount = 0;
                foreach (var depId in gate.Dependencies)
                {
                    if (_taskCardPositions.TryGetValue(depId, out var depPosition))
                    {
                        connectedMidpointYSum += depPosition.Y + NodeHeight / 2.0;
                        connectedTaskCount++;
                    }
                }

                foreach (var target in gate.Targets)
                {
                    if (_taskCardPositions.TryGetValue(target.Id, out var targetPosition))
                    {
                        connectedMidpointYSum += targetPosition.Y + NodeHeight / 2.0;
                        connectedTaskCount++;
                    }
                }

                if (connectedTaskCount == 0)
                    continue;

                var liveCenterY = connectedMidpointYSum / connectedTaskCount;

                // Move the badge element.
                if (_allGateBadgeElements is not null &&
                    gi < _allGateBadgeElements.Length &&
                    _allGateBadgeElements[gi] is FrameworkElement badge)
                {
                    Canvas.SetTop(badge, liveCenterY - 17 * _scaleFactor);
                }

                // Update connector endpoints so arrows follow the badge.
                if (_visualizationGateConnectorGroups is not null && gi < _visualizationGateConnectorGroups.Count)
                {
                    foreach (var cg in _visualizationGateConnectorGroups[gi])
                    {
                        var currentFrom = cg.CurrentFrom;
                        var currentTo   = cg.CurrentTo;

                        // Incoming connectors have their To endpoint at the badge's left face (X = gateCenter.X - 29).
                        // Outgoing connectors have their From endpoint at the badge's right face (X = gateCenter.X + 29).
                        // Distinguish by comparing endpoint X to gate center offsets.
                        if (Math.Abs(currentTo.X - (gate.Center.X - 29)) < 1.0)
                        {
                            // Incoming: update To to new Y
                            cg.UpdateEndpoints(currentFrom, new Point(currentTo.X, liveCenterY));
                        }
                        else if (Math.Abs(currentFrom.X - (gate.Center.X + 29)) < 1.0)
                        {
                            // Outgoing: update From to new Y
                            cg.UpdateEndpoints(new Point(currentFrom.X, liveCenterY), currentTo);
                        }
                    }
                }
            }
        }
    }

    private void RestoreTaskCardDragGlow(TaskCardDragState dragState)
    {
        dragState.Element.Effect = dragState.OriginalCardEffect;
        foreach (var connectorState in dragState.ConnectedConnectorStates)
        {
            var connectorGroup = connectorState.Group;
            foreach (var element in connectorGroup.GlowElements)
            {
                element.Visibility = Visibility.Hidden;
                Panel.SetZIndex(element, 0);
            }
            foreach (var element in connectorGroup.MainElements)
            {
                Panel.SetZIndex(element, 0);
                element.Effect = null;
            }
        }

        foreach (var (connector, originalEffect) in dragState.ConnectedConnectors)
            connector.Effect = originalEffect;
    }

    private double FindLockedMilestoneSplitX(double fromX, double toX) =>
        _lockedMilestoneBoundaryXs
            .Where(boundaryX => boundaryX > fromX + 1.0 && boundaryX < toX - 1.0)
            .OrderBy(boundaryX => boundaryX)
            .Cast<double?>()
            .FirstOrDefault() ?? double.NaN;

    private PlanGraphConnectorGroup AddDirectTaskConnector(
        Canvas canvas,
        Point fromPoint,
        Point toPoint,
        int skipCount,
        bool dashed)
    {
        var detour = _allClusterFootprints.Count > 0
            ? ValidationShieldPresenter.ComputeConnectorDetour(
                (fromPoint.X, fromPoint.Y),
                (toPoint.X, toPoint.Y),
                _allClusterFootprints,
                _scaleFactor)
            : null;

        if (detour is { Count: 0 })
        {
            return _graphEdgeRenderingContract.AddConnector(
                canvas,
                fromPoint,
                toPoint,
                arrowHead: true,
                skipCount: Math.Max(1, skipCount),
                dashed: dashed,
                toolTip: "Alternate connector color: no clear route around an ALL group was available.",
                splitAtX: FindLockedMilestoneSplitX(fromPoint.X, toPoint.X));
        }

        if (detour is null || detour.Count <= 2)
        {
            return _graphEdgeRenderingContract.AddConnector(
                canvas,
                fromPoint,
                toPoint,
                arrowHead: true,
                skipCount: skipCount,
                dashed: dashed,
                splitAtX: FindLockedMilestoneSplitX(fromPoint.X, toPoint.X));
        }

        // Render the complete collision-safe route as one rounded path. Each corner
        // cuts back by half its shorter adjacent segment, producing proportional
        // curves without allowing a long segment to overwhelm a nearby short turn.
        return _graphEdgeRenderingContract.AddRoundedConnectorRoute(
            canvas,
            detour,
            arrowHead: true,
            skipCount: skipCount,
            dashed: dashed);
    }

    private void RemoveConnectorGroupElements(Canvas canvas, PlanGraphConnectorGroup group)
    {
        _graphEdgeRenderingContract.RemoveConnectorGroupElements(canvas, group);
    }

    private void ReplaceConnectorGroupForDrag(DragConnectorState state, PlanGraphConnectorGroup replacementGroup)
    {
        var priorGroup = state.Group;
        foreach (var taskId in priorGroup.TaskIds.Distinct(StringComparer.Ordinal))
        {
            if (!replacementGroup.TaskIds.Contains(taskId, StringComparer.Ordinal))
                replacementGroup.TaskIds.Add(taskId);
            var role = priorGroup.GetEndpointRole(taskId);
            if (role != PlanGraphConnectorEndpointRole.None)
                replacementGroup.RegisterEndpointRole(taskId, role);

            if (!_connectorsByTask.TryGetValue(taskId, out var groups))
                continue;
            groups.RemoveAll(group => ReferenceEquals(group, priorGroup));
            if (!groups.Contains(replacementGroup))
                groups.Add(replacementGroup);
        }

        foreach (var badge in priorGroup.GateBadges)
        {
            if (!replacementGroup.GateBadges.Contains(badge))
                replacementGroup.GateBadges.Add(badge);
        }

        state.Group = replacementGroup;
    }

    private void OnTaskCardPreviewMouseLeftButtonDown(
        FrameworkElement element,
        string taskId,
        MouseButtonEventArgs e)
    {
        if (_graphCanvas is null)
            return;

        var originalTop = Canvas.GetTop(element);
        if (double.IsNaN(originalTop))
            originalTop = 0;

        var columnX = Canvas.GetLeft(element);
        if (double.IsNaN(columnX))
            columnX = 0;

        var milestoneTop = double.MinValue;
        var milestoneBottom = double.MaxValue;
        if (TryResolveMilestoneVerticalBoundsForColumn(columnX, element.ActualWidth, out var resolvedTop, out var resolvedBottom))
        {
            milestoneTop = resolvedTop;
            milestoneBottom = resolvedBottom;
        }

        var connectedConnectorStates = BuildDragConnectorStates(taskId, originalTop);
        var connectedConnectors = CaptureConnectorEffects(connectedConnectorStates);
        var dragOverlayElements = CaptureTaskDragOverlays(taskId);
        var sameColumnCards = (_graphCanvas.Children.OfType<FrameworkElement>())
            .Select(child => new
            {
                Element = child,
                Left = Canvas.GetLeft(child),
                Top = Canvas.GetTop(child),
            })
            .Where(entry =>
                IsSameColumn(entry.Left, columnX) &&
                double.IsFinite(entry.Top) &&
                TryGetTaskIdTag(entry.Element, out _))
            .Select(entry => (entry.Element, entry.Top))
            .OrderBy(entry => entry.Top)
            .ToArray();
        var preDragColumnOrder = sameColumnCards
            .Select(entry =>
            {
                TryGetTaskIdTag(entry.Element, out var id);
                return id;
            })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();
        var siblingCards = sameColumnCards
            .Select(entry =>
            {
                TryGetTaskIdTag(entry.Element, out var id);
                return (Card: entry.Element, OriginalTop: entry.Top, TaskId: id);
            })
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.TaskId) &&
                !string.Equals(entry.TaskId, taskId, StringComparison.Ordinal))
            .ToArray();
        var siblingOverlayElements = siblingCards.ToDictionary(
            sibling => sibling.TaskId,
            sibling => CaptureTaskDragOverlays(sibling.TaskId),
            StringComparer.Ordinal);
        // Use natural slot tops (not nudged visual Canvas.Top values) so sibling
        // animation targets and nudge baselines are relative to clean slot geometry.
        // If a task had a prior nudge, its visual top is offset; using that offset as the
        // swap target would bake that prior nudge into the displaced sibling's position,
        // causing a jump when the rebuild snaps the sibling back to its natural Y.
        var columnSlotTops = preDragColumnOrder
            .Select(id => _naturalTaskSlotTops.TryGetValue(id, out var naturalY) ? naturalY : 0.0)
            .ToArray();

        _taskCardDragState = new TaskCardDragState
        {
            Element = element,
            TaskId = taskId,
            MouseOffsetWithinCard = e.GetPosition(element),
            DragStartCanvasPoint = e.GetPosition(_graphCanvas),
            OriginalTop = originalTop,
            ColumnX = columnX,
            MilestoneTop = milestoneTop,
            MilestoneBottom = milestoneBottom,
            OriginalCardEffect = element.Effect,
            ConnectedConnectorStates = connectedConnectorStates,
            ConnectedConnectors = connectedConnectors,
            DragOverlayElements = dragOverlayElements,
            SiblingCards = siblingCards,
            CurrentColumnOrder = preDragColumnOrder.ToList(),
            PreDragColumnOrder = preDragColumnOrder,
            PreDragTaskNudges = new Dictionary<string, double>(_taskNudges, StringComparer.Ordinal),
            DisplacedSiblingTops = new Dictionary<string, double>(StringComparer.Ordinal),
            SiblingOverlayElements = siblingOverlayElements,
            ColumnSlotTops = columnSlotTops,
            ColumnIdentifier = ResolveColumnIdentifier(columnX),
            IsActive = false,
        };
        ApplyTaskCardDragGlow(_taskCardDragState);
        _suppressTaskCardCancelOnLostCapture = false;

        if (!element.CaptureMouse())
        {
            RestoreTaskCardDragGlow(_taskCardDragState);
            _taskCardDragState = null;
        }
    }

    private void OnGraphCanvasMouseMove(object? sender, MouseEventArgs e)
    {
        if (_graphCanvas is null || _taskCardDragState is not { } dragState)
            return;

        var pointer = e.GetPosition(_graphCanvas);
        if (!dragState.IsActive)
        {
            var deltaX = Math.Abs(pointer.X - dragState.DragStartCanvasPoint.X);
            var deltaY = Math.Abs(pointer.Y - dragState.DragStartCanvasPoint.Y);
            if (deltaX < SystemParameters.MinimumHorizontalDragDistance &&
                deltaY < SystemParameters.MinimumVerticalDragDistance)
                return;

            dragState.IsActive = true;
        }

        var rawTop = pointer.Y - dragState.MouseOffsetWithinCard.Y;
        var cardHeight = dragState.Element.ActualHeight;
        if (cardHeight <= 0 || double.IsNaN(cardHeight))
            cardHeight = dragState.Element.Height;
        if (cardHeight < 0 || double.IsNaN(cardHeight))
            cardHeight = 0;

        var freeTop = dragState.MilestoneTop;
        var freeBottom = dragState.MilestoneBottom - cardHeight;

        var constrainedTop = rawTop;
        if (double.IsFinite(freeTop) &&
            double.IsFinite(freeBottom) &&
            freeTop <= freeBottom)
        {
            if (rawTop >= freeTop && rawTop <= freeBottom)
            {
                constrainedTop = rawTop;
            }
            else if (rawTop < freeTop)
            {
                var excess = freeTop - rawTop;
                constrainedTop = freeTop - RubberBandDistance(excess);
            }
            else
            {
                var excess = rawTop - freeBottom;
                constrainedTop = freeBottom + RubberBandDistance(excess);
            }
        }

        Canvas.SetTop(dragState.Element, constrainedTop);
        var dragDeltaY = constrainedTop - dragState.OriginalTop;
        foreach (var (element, originalTop) in dragState.DragOverlayElements)
            Canvas.SetTop(element, originalTop + dragDeltaY);
        UpdateTaskCardDragConnectors(dragState, constrainedTop);

        var draggedMidpoint = constrainedTop + dragState.Element.ActualHeight / 2.0;
        while (TryPerformDragSwap(dragState, draggedMidpoint))
        {
        }
    }

    private void OnGraphCanvasMouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
    {
        CommitTaskCardDrag();
    }

    private void OnTaskCardMouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
    {
        var wasDragActive = _taskCardDragState?.IsActive ?? false;
        var taskId = _taskCardDragState?.TaskId;
        CommitTaskCardDrag();
        // Treat as a click (selection toggle) only when no real drag occurred.
        if (!wasDragActive && taskId is not null)
            SelectElement("task", taskId);
    }

    private void OnTaskCardLostMouseCapture(object? sender, MouseEventArgs e)
    {
        if (_suppressTaskCardCancelOnLostCapture || _taskCardDragState is not { } dragState)
            return;

        if (!ReferenceEquals(sender, dragState.Element))
            return;

        CancelTaskCardDrag();
    }

    private void OnPlanViewerPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !_isInspectorEditing)
        {
            ShowDescriptionFind();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Space && _graphScroll?.IsMouseOver == true &&
            !IsGraphTextInput(Keyboard.FocusedElement) && _taskCardDragState is null)
        {
            _spacePanHeld = true;
            UpdateGraphPanCursor();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && _spacePanHeld)
        {
            ReleaseGraphPan();
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Escape || _taskCardDragState is null)
            return;

        CancelTaskCardDrag();
        e.Handled = true;
    }

    private void CommitTaskCardDrag()
    {
        if (_taskCardDragState is not { } dragState)
            return;

        foreach (var sibling in dragState.SiblingCards)
        {
            if (!dragState.DisplacedSiblingTops.TryGetValue(sibling.TaskId, out var displacedTop))
                continue;
            sibling.Card.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetTop(sibling.Card, displacedTop);

            if (dragState.SiblingOverlayElements.TryGetValue(sibling.TaskId, out var overlays))
            {
                var siblingDelta = displacedTop - sibling.OriginalTop;
                foreach (var (overlay, originalTop) in overlays)
                {
                    overlay.BeginAnimation(Canvas.TopProperty, null);
                    Canvas.SetTop(overlay, originalTop + siblingDelta);
                }
            }
        }

        var top = Canvas.GetTop(dragState.Element);
        if (double.IsNaN(top))
            top = dragState.OriginalTop;
        UpdateTaskCardDragConnectors(dragState, top);
        foreach (var sibling in dragState.SiblingCards)
        {
            if (!dragState.DisplacedSiblingTops.TryGetValue(sibling.TaskId, out var displacedTop))
                continue;
            ApplyTaskCardTopAndConnectors(sibling.TaskId, dragState.ColumnX, displacedTop);
        }

        // Snapshot committedTop before any persistence. For staged plans,
        // PersistCommittedTaskCardReorder calls ApplyStagedMutation which synchronously
        // rebuilds the canvas, repositioning all elements. Reading Canvas.GetTop after
        // that rebuild would return the rebuilt position, not where the user released.
        var committedTop = Canvas.GetTop(dragState.Element);
        if (double.IsNaN(committedTop))
            committedTop = dragState.OriginalTop;

        var reorderOccurred = dragState.CurrentColumnOrder.Count > 0 &&
            !dragState.CurrentColumnOrder.SequenceEqual(dragState.PreDragColumnOrder, StringComparer.Ordinal);

        if (reorderOccurred)
        {
            // Clear stale nudges for displaced siblings before any rebuild fires.
            // Each nudge is a pixel offset relative to a specific natural slot Y;
            // after a swap that slot belongs to a different task, so the old nudge
            // would shift the sibling in its new position.
            for (var i = 0; i < dragState.CurrentColumnOrder.Count; i++)
            {
                var siblingId = dragState.CurrentColumnOrder[i];
                if (siblingId == dragState.TaskId) continue;
                var oldIndex = dragState.PreDragColumnOrder.ToList().IndexOf(siblingId);
                if (oldIndex != i)
                    _taskNudges.Remove(siblingId);
            }
        }

        // Compute and store the dragged task's nudge before persistence so that
        // staged-plan rebuilds (which happen synchronously inside PersistCommittedTaskCardReorder)
        // use the correct final nudge dictionary. ColumnSlotTops now holds natural slot Ys
        // (not nudged visual positions), so this baseline is consistent with the rebuild.
        {
            double? nudgeBaseTop = null;
            if (reorderOccurred)
            {
                var newIndex = dragState.CurrentColumnOrder.IndexOf(dragState.TaskId);
                if (newIndex >= 0 && newIndex < dragState.ColumnSlotTops.Count)
                    nudgeBaseTop = dragState.ColumnSlotTops[newIndex];
            }
            if (nudgeBaseTop is null && _naturalTaskSlotTops.TryGetValue(dragState.TaskId, out var naturalTop))
                nudgeBaseTop = naturalTop;

            if (nudgeBaseTop.HasValue)
            {
                var fontSizeBody = _scaleFactor * 12.0;
                var nudgeLineHeights = (committedTop - nudgeBaseTop.Value) / fontSizeBody;
                if (Math.Abs(nudgeLineHeights) < 0.05)
                    nudgeLineHeights = 0.0;

                if (nudgeLineHeights == 0.0)
                    _taskNudges.Remove(dragState.TaskId);
                else
                    _taskNudges[dragState.TaskId] = nudgeLineHeights;
            }
        }

        // Persist the reorder after nudges are finalised. For staged plans this
        // triggers a synchronous rebuild via ApplyStagedMutation — nudges are correct.
        if (reorderOccurred)
        {
            _lastCommittedColumnOrder = new CommittedColumnReorder(
                dragState.ColumnIdentifier,
                dragState.CurrentColumnOrder.ToArray());
            if (!PersistCommittedTaskCardReorder())
            {
                _lastCommittedColumnOrder = null;
                _taskNudges = new Dictionary<string, double>(
                    dragState.PreDragTaskNudges,
                    StringComparer.Ordinal);
                Canvas.SetTop(dragState.Element, dragState.OriginalTop);
                Canvas.SetLeft(dragState.Element, dragState.ColumnX);
                foreach (var (overlay, originalTop) in dragState.DragOverlayElements)
                    Canvas.SetTop(overlay, originalTop);
                foreach (var sibling in dragState.SiblingCards)
                {
                    ApplyTaskCardTopAndConnectors(
                        sibling.TaskId,
                        dragState.ColumnX,
                        sibling.OriginalTop);
                    if (!dragState.SiblingOverlayElements.TryGetValue(sibling.TaskId, out var overlays))
                        continue;
                    foreach (var (overlay, originalTop) in overlays)
                        Canvas.SetTop(overlay, originalTop);
                }
                UpdateTaskCardDragConnectors(dragState, dragState.OriginalTop);
            }
        }

        // Save nudge state and trigger a rebuild for durable plans (staged plans
        // already rebuilt inside PersistCommittedTaskCardReorder above).
        _saveLayoutNudges?.Invoke(new Dictionary<string, double>(_taskNudges, StringComparer.Ordinal));
        RestoreTaskCardDragGlow(dragState);
        _taskCardDragState = null;
        _suppressTaskCardCancelOnLostCapture = true;
        if (dragState.Element.IsMouseCaptured)
            dragState.Element.ReleaseMouseCapture();
        _suppressTaskCardCancelOnLostCapture = false;
    }

    private void CancelTaskCardDrag()
    {
        if (_taskCardDragState is not { } dragState)
            return;

        var transitionDuration = TimeSpan.FromMilliseconds(180);
        foreach (var sibling in dragState.SiblingCards)
        {
            AnimateCanvasTop(
                sibling.Card,
                sibling.OriginalTop,
                transitionDuration,
                finalTop => ApplyTaskCardTopAndConnectors(sibling.TaskId, dragState.ColumnX, finalTop));
            if (dragState.SiblingOverlayElements.TryGetValue(sibling.TaskId, out var overlays))
            {
                foreach (var (overlay, originalTop) in overlays)
                    AnimateCanvasTop(overlay, originalTop, transitionDuration);
            }
        }

        Canvas.SetTop(dragState.Element, dragState.OriginalTop);
        Canvas.SetLeft(dragState.Element, dragState.ColumnX);
        foreach (var (element, originalTop) in dragState.DragOverlayElements)
            Canvas.SetTop(element, originalTop);
        UpdateTaskCardDragConnectors(dragState, dragState.OriginalTop);
        foreach (var sibling in dragState.SiblingCards)
            ApplyTaskCardTopAndConnectors(sibling.TaskId, dragState.ColumnX, sibling.OriginalTop);
        RestoreTaskCardDragGlow(dragState);

        _taskCardDragState = null;
        _suppressTaskCardCancelOnLostCapture = true;
        if (dragState.Element.IsMouseCaptured)
            dragState.Element.ReleaseMouseCapture();
        _suppressTaskCardCancelOnLostCapture = false;
    }

    private bool TryResolveMilestoneVerticalBoundsForColumn(
        double columnX,
        double cardWidth,
        out double milestoneTop,
        out double milestoneBottom)
    {
        milestoneTop = 0;
        milestoneBottom = 0;
        if (_graphCanvas is null)
            return false;

        var columnCenterX = columnX + (cardWidth > 0 ? cardWidth / 2.0 : 0);
        Border? nearestMilestone = null;
        var nearestDistance = double.MaxValue;

        foreach (var milestoneBand in _graphCanvas.Children.OfType<Border>())
        {
            if (milestoneBand.Tag is not string tag ||
                !tag.StartsWith("milestone:", StringComparison.Ordinal))
                continue;

            var bandLeft = Canvas.GetLeft(milestoneBand);
            var bandTop = Canvas.GetTop(milestoneBand);
            if (double.IsNaN(bandLeft) || double.IsNaN(bandTop))
                continue;

            var bandHeight = milestoneBand.ActualHeight > 0 ? milestoneBand.ActualHeight : milestoneBand.Height;
            if (bandHeight <= 0 || double.IsNaN(bandHeight))
                continue;

            var bandCenterX = bandLeft + (milestoneBand.ActualWidth > 0 ? milestoneBand.ActualWidth / 2.0 : 0);
            var distance = Math.Abs(columnCenterX - bandCenterX);
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearestMilestone = milestoneBand;
        }

        if (nearestMilestone is null)
            return false;

        var resolvedTop = Canvas.GetTop(nearestMilestone);
        var resolvedHeight = nearestMilestone.ActualHeight > 0 ? nearestMilestone.ActualHeight : nearestMilestone.Height;
        if (!double.IsFinite(resolvedTop) || !double.IsFinite(resolvedHeight) || resolvedHeight <= 0)
            return false;

        milestoneTop = resolvedTop;
        milestoneBottom = resolvedTop + resolvedHeight;
        return true;
    }

    private static double RubberBandDistance(double excess)
    {
        const double MaxPull = 40.0;
        return MaxPull * (1.0 - Math.Exp(-Math.Max(0, excess) / MaxPull));
    }
}
