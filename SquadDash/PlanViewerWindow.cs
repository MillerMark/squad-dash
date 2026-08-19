using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

    private readonly string? _activeBranch;
    private readonly double _quickReplyFontSize;
    private readonly Func<DecomposePlanActionDefinition, Task<bool>>? _applyAction;
    private readonly Action<Plan>? _onGatesChanged;
    private readonly Action<Plan>? _onStartPlan;
    private readonly Action<Plan>? _onResumePlan;
    private readonly Func<Plan, Task<bool>>? _onAdoptVerifiedCommitRange;
    private readonly string? _interruptedPrimaryActionLabel;
    private readonly string? _interruptedPrimaryActionHint;
    private readonly Action<Plan>? _onEndPlan;
    private readonly Action<Plan, string>? _onApproveGate;
    private readonly Action<Plan, string>? _onUnapproveGate;
    private readonly Func<PlanPreflightBlockedException, Task>? _viewPreflightChanges;
    private readonly Func<Task<bool>>? _isPreflightWorkspaceClean;
    private readonly Func<Task<IReadOnlyList<string>?>>? _getPreflightDirtyPaths;
    private readonly Func<Task<bool>>? _initializeRepository;
    private readonly Action<string>? _onOpenCommit;
    private readonly Action<IReadOnlyList<PlanEvidenceCommit>>? _onOpenCommits;
    private readonly Action<Plan>? _onContinueInterruptedTask;
    private readonly Action<Plan>? _onReplanRemainingWork;
    private readonly Func<string, (ImageSource? Image, string Initial, Brush Accent)?>? _resolveAgentAvatar;
    private readonly Func<string, bool>? _isTaskActivityActive;
    private readonly Func<string, bool>? _isRecoveryAssessmentActive;
    private readonly Func<string, PlanTaskCorrectionActivityState>? _resolveTaskCorrectionActivity;
    private readonly Func<string, bool>? _isApprovalChangeActivityActive;
    private readonly Func<DecomposedTaskGroup, PlanDefinitionEditResult>? _saveDefinitionEdit;
    private readonly Action<PendingDecomposePlan>? _requestAiReview;
    private readonly Action<PlanVisualizerReviewPayload?>? _saveReviewState;
    private readonly Action<Plan, string>? _onRetryValidation;
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
    private readonly Dictionary<string, ActivitySpinner> _taskSpinnersById =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivitySpinner> _validationSpinnersById =
        new(StringComparer.Ordinal);
    private Border? _contentHolder;
    private ScrollViewer? _graphScroll;
    private Canvas? _graphCanvas;
    private FlowDocument? _detailDocument;
    private FrameworkElement? _approvalSummary;
    private FrameworkElement? _header;
    private FrameworkElement? _readingHint;
    private double _canvasContentWidth;
    private double _canvasContentHeight;
    private double _activeVisibilityStart = double.NaN;
    private double _activeVisibilityEnd = double.NaN;
    private AutomaticScrollTarget _automaticScrollTarget;
    private double _allocatedApprovalHeight;
    private DateTime _lastViewerInteractionUtc = DateTime.MinValue;
    private bool _initialLayoutApplied;
    private StagedPlanEditHistory? _editHistory;
    private bool _isInspectorEditing;
    private PlanVisualizerReviewPayload? _review;
    private int _reviewIndex = -1;
    private FrmUltimateCallout? _reviewCallout;
    private bool _isAiReviewPending;
    private string? _reviewStatusMessage;
    private DecomposedTaskGroup? _deferredDefinitionEdit;
    private (Dictionary<string, Point> OldPositions, IReadOnlyList<string> AffectedTaskIds)? _pendingAnimation;
    private int _buildSeq;

    private enum AutomaticScrollTarget
    {
        None,
        PlanStart,
        ActiveRange,
        PlanEnd,
    }

    private record SelectedElementIdentity(string Kind, string Id);
    private record CommittedColumnReorder(string ColumnTaskId, IReadOnlyList<string> NewTaskOrder);
    [Flags]
    private enum ConnectorEndpointRole
    {
        None = 0,
        Start = 1,
        End = 2,
    }
    private sealed class DragConnectorState
    {
        public ConnectorGroup Group { get; set; } = null!;
        public ConnectorEndpointRole Role { get; init; }
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
        public Dictionary<string, double> DisplacedSiblingTops { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, IReadOnlyList<(UIElement Element, double OriginalTop)>> SiblingOverlayElements { get; init; } =
            new(StringComparer.Ordinal);
        public IReadOnlyList<double> ColumnSlotTops { get; init; } = [];
        public string ColumnIdentifier { get; init; } = string.Empty;
        public bool IsActive { get; set; }
    }
    private SelectedElementIdentity? _selectedElement;
    private (Border border, Thickness original)? _selectionBorderState;
    private FrameworkElement? _selectionGlowElement;
    private TaskCardDragState? _taskCardDragState;
    private CommittedColumnReorder? _lastCommittedColumnOrder;
    private readonly Dictionary<string, List<ConnectorGroup>> _connectorsByTask = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Point> _taskCardPositions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<UIElement>> _taskDragOverlaysByTask = new(StringComparer.Ordinal);
    private readonly List<ValidationShieldPresenter.LayoutRect> _allClusterFootprints = [];
    private readonly List<double> _lockedMilestoneBoundaryXs = [];
    private bool _suppressTaskCardCancelOnLostCapture;

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
        Func<string, bool>? isRecoveryAssessmentActive = null,
        Func<string, PlanTaskCorrectionActivityState>? resolveTaskCorrectionActivity = null,
        Func<string, bool>? isApprovalChangeActivityActive = null,
        Func<Task<bool>>? initializeRepository = null,
        Action<IReadOnlyList<PlanEvidenceCommit>>? onOpenCommits = null,
        Action<Plan>? onContinueInterruptedTask = null,
        Action<Plan>? onReplanRemainingWork = null,
        Func<DecomposedTaskGroup, PlanDefinitionEditResult>? saveDefinitionEdit = null,
        Action<PendingDecomposePlan>? requestAiReview = null,
        Action<PlanVisualizerReviewPayload?>? saveReviewState = null,
        Action<Plan, string>? onRetryValidation = null)
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
        _onAdoptVerifiedCommitRange = onAdoptVerifiedCommitRange;
        _interruptedPrimaryActionLabel = interruptedPrimaryActionLabel;
        _interruptedPrimaryActionHint = interruptedPrimaryActionHint;
        _onEndPlan          = onEndPlan;
        _onApproveGate      = onApproveGate;
        _onUnapproveGate    = onUnapproveGate;
        _viewPreflightChanges = viewPreflightChanges;
        _isPreflightWorkspaceClean = isPreflightWorkspaceClean;
        _getPreflightDirtyPaths = getPreflightDirtyPaths;
        _initializeRepository = initializeRepository;
        _onOpenCommit = onOpenCommit;
        _onOpenCommits = onOpenCommits;
        _onContinueInterruptedTask = onContinueInterruptedTask;
        _onReplanRemainingWork = onReplanRemainingWork;
        _resolveAgentAvatar = resolveAgentAvatar;
        _isTaskActivityActive = isTaskActivityActive;
        _isRecoveryAssessmentActive = isRecoveryAssessmentActive;
        _resolveTaskCorrectionActivity = resolveTaskCorrectionActivity;
        _isApprovalChangeActivityActive = isApprovalChangeActivityActive;
        _saveDefinitionEdit = saveDefinitionEdit;
        _requestAiReview = requestAiReview;
        _saveReviewState = saveReviewState;
        _onRetryValidation = onRetryValidation;
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
        ContentRendered += OnInitialContentRendered;

        BuildContent(plan, durablePlan);
        Closed += (_, _) =>
        {
            _preflightPollTimer?.Stop();
            _reviewCallout?.Close();
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
                        _activeApprovalChangeGateIds.Remove(activity.GateId);
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

    private void OnGraphScrollPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Shift) return;
        if (_graphScroll is null) return;
        var delta = e.Delta > 0 ? -SystemParameters.WheelScrollLines * 16.0 : SystemParameters.WheelScrollLines * 16.0;
        _graphScroll.ScrollToHorizontalOffset(_graphScroll.HorizontalOffset + delta);
        e.Handled = true;
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
        if (_deferredDefinitionEdit is not null &&
            displayedPlan.LifecycleStatus != PlanLifecycleStatus.Executing)
        {
            var deferred = _deferredDefinitionEdit;
            _deferredDefinitionEdit = null;
            Dispatcher.BeginInvoke(() => ApplyStagedMutation(deferred));
        }
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
            BuildContent(_plan, _durablePlan);
    }

    private void BuildContent(PendingDecomposePlan plan, Plan? durablePlan)
    {
        var currentBuildSeq = ++_buildSeq;
        var previousApprovalHeight = _approvalSummary is { ActualHeight: > 0 } currentSummary
            ? currentSummary.ActualHeight
            : _allocatedApprovalHeight;
        _approvalSummary = null;
        _preflightPollTimer?.Stop();
        _preflightPollTimer = null;
        _plan = plan;
        _durablePlan = durablePlan;
        var activeBranch       = _activeBranch;
        var quickReplyFontSize = _quickReplyFontSize;
        var applyAction        = _applyAction;
        Action<Plan>? onGatesChanged = _saveDefinitionEdit is not null
            ? edited => ApplyStagedMutation(PendingDecomposePlanAdapter.FromPlan(edited).Group)
            : _onGatesChanged;
        var onStartPlan        = _onStartPlan;
        var onResumePlan       = _onResumePlan;
        var onAdoptVerifiedCommitRange = _onAdoptVerifiedCommitRange;
        var onEndPlan          = _onEndPlan;
        var onContinueInterruptedTask = _onContinueInterruptedTask;
        var onReplanRemainingWork = _onReplanRemainingWork;
        var onApproveGate      = _onApproveGate;
        var onUnapproveGate    = _onUnapproveGate;
        var onRetryValidation  = _onRetryValidation;
        var group = plan.Group;
        var currentInterruptedTaskId = durablePlan?.InterruptionData?.InterruptedTaskId;
        var interruptedCorrectionState = string.IsNullOrWhiteSpace(currentInterruptedTaskId)
            ? PlanTaskCorrectionActivityState.None
            : ResolveTaskCorrectionActivity(currentInterruptedTaskId!);
        var hasClaimedInterruptedCorrection =
            interruptedCorrectionState != PlanTaskCorrectionActivityState.None;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(22, 16, 22, 2) };
        _header = header;

        if (applyAction is not null)
        {
            var actionRegion = new StackPanel();
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
                    actionsPanel.IsEnabled = false;
                    try
                    {
                        if (await applyAction(capturedAction))
                            Close();
                        else
                            actionsPanel.IsEnabled = true;
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
                        UIErrorHelper.ShowError("Plan", ex.Message, this);
                    }
                };
                actionsPanel.Children.Add(button);
            }
            actionRegion.Children.Add(actionsPanel);
            actionRegion.Children.Add(recoveryHost);
            header.Children.Add(actionRegion);
        }

        var failedValidation = durablePlan?.Validations?.FirstOrDefault(candidate =>
            candidate.Status == PlanValidationStatus.Failed);
        if (durablePlan is not null && failedValidation is not null && onRetryValidation is not null)
        {
            var validationPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8),
            };
            var retryValidationButton = TranscriptQuickReplyFactory.CreateButton(
                "Retry Validation",
                quickReplyFontSize,
                toolTip: ToolTipHelper.MakeThemedToolTip(
                    $"Retry the non-mutating validation “{failedValidation.Title}.” Completed implementation steps and commits remain accepted."));
            retryValidationButton.Focusable = false;
            retryValidationButton.Click += (_, _) =>
            {
                validationPanel.IsEnabled = false;
                onRetryValidation(durablePlan, failedValidation.ValidationId);
                Close();
            };
            validationPanel.Children.Add(retryValidationButton);
            header.Children.Add(validationPanel);
        }

        if (durablePlan is not null &&
            durablePlan.LifecycleStatus == PlanLifecycleStatus.Interrupted &&
            durablePlan.InterruptionData?.RecoveryAssessment is not null &&
            !hasClaimedInterruptedCorrection)
        {
            var assessmentPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8),
            };
            var interruptedTaskId = durablePlan.InterruptionData.InterruptedTaskId;
            var interruptedTask = durablePlan.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, interruptedTaskId, StringComparison.Ordinal));
            var stepLabel = PlanRecoveryPresentationBuilder.FormatStepLabel(
                interruptedTask?.DisplayStepLabel,
                interruptedTask?.Title ?? interruptedTaskId ?? "Interrupted Step");

            if (onAdoptVerifiedCommitRange is not null)
            {
                var acceptButton = TranscriptQuickReplyFactory.CreateButton(
                    _interruptedPrimaryActionLabel ?? $"{stepLabel} Is Complete", quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(
                        _interruptedPrimaryActionHint ??
                        $"Use the evidence links in this viewer, then mark only {stepLabel} complete and continue the plan."));
                acceptButton.Focusable = false;
                acceptButton.Click += async (_, _) =>
                {
                    assessmentPanel.IsEnabled = false;
                    try
                    {
                        if (await onAdoptVerifiedCommitRange(durablePlan)) Close();
                        else assessmentPanel.IsEnabled = true;
                    }
                    catch (Exception ex)
                    {
                        assessmentPanel.IsEnabled = true;
                        UIErrorHelper.ShowError("Accept Completed Step", ex.Message, this);
                    }
                };
                assessmentPanel.Children.Add(acceptButton);
            }
            if (onContinueInterruptedTask is not null)
            {
                var continueButton = TranscriptQuickReplyFactory.CreateButton(
                    $"Continue Working on {stepLabel}", quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(
                        $"Keep {stepLabel} incomplete and continue from the existing repository state."));
                continueButton.Focusable = false;
                continueButton.Click += (_, _) => { onContinueInterruptedTask(durablePlan); Close(); };
                assessmentPanel.Children.Add(continueButton);
            }
            if (onReplanRemainingWork is not null)
            {
                var replanButton = TranscriptQuickReplyFactory.CreateButton(
                    "✎ Revise Remaining Plan…", quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(
                        "Replace this blocked step with smaller, dependency-aware steps."));
                replanButton.Focusable = false;
                replanButton.Click += (_, _) => { onReplanRemainingWork(durablePlan); Close(); };
                assessmentPanel.Children.Add(replanButton);
            }
            header.Children.Add(assessmentPanel);
        }

        if (durablePlan is not null &&
            durablePlan.InterruptionData?.RecoveryAssessment is null &&
            durablePlan.LifecycleStatus is PlanLifecycleStatus.Interrupted or
                PlanLifecycleStatus.WaitingForCleanWorktree &&
            (onResumePlan is not null || onAdoptVerifiedCommitRange is not null ||
             onContinueInterruptedTask is not null || onEndPlan is not null) &&
            !hasClaimedInterruptedCorrection)
        {
            var interruptedPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 0, 0, 8),
            };
            // Safe continuation owns the recovery choice. A ready validation is simply the next
            // scheduler boundary and must not create a duplicate "Resume Validation" action.
            if (onAdoptVerifiedCommitRange is not null && onResumePlan is null)
            {
                var capturedPlan = durablePlan;
                var capturedAction = onAdoptVerifiedCommitRange;
                var resumableValidation = PlanExecutionBoundaryPolicy.SelectValidation(capturedPlan);
                var adoptButton = TranscriptQuickReplyFactory.CreateButton(
                    resumableValidation is null
                        ? _interruptedPrimaryActionLabel ?? "Continue Plan Recovery"
                        : "Resume Validation",
                    quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(
                        resumableValidation is null
                            ? _interruptedPrimaryActionHint ??
                              "Continue from the preserved plan state without launching a preliminary repository assessment."
                            : $"Continue with the ready validation “{resumableValidation.Title}.” Completed implementation steps will not be repeated."));
                adoptButton.Focusable = false;
                adoptButton.Click += async (_, _) =>
                {
                    interruptedPanel.IsEnabled = false;
                    try
                    {
                        if (await capturedAction(capturedPlan))
                            Close();
                        else
                            interruptedPanel.IsEnabled = true;
                    }
                    catch (Exception ex)
                    {
                        interruptedPanel.IsEnabled = true;
                        SquadDashTrace.Write(TraceCategory.General,
                            $"Plan recovery assessment failed: {ex}");
                        UIErrorHelper.ShowError("Plan Recovery", ex.Message, this);
                    }
                };
                interruptedPanel.Children.Add(adoptButton);
            }
            if (onContinueInterruptedTask is not null && onResumePlan is null)
            {
                var capturedPlan = durablePlan;
                var continueButton = TranscriptQuickReplyFactory.CreateButton(
                    $"✎ Continue Working on {PlanRecoveryPresentationBuilder.FormatStepLabel(
                        capturedPlan.Tasks.FirstOrDefault(task => string.Equals(
                            task.TaskId,
                            capturedPlan.InterruptionData?.InterruptedTaskId,
                            StringComparison.Ordinal))?.DisplayStepLabel,
                        capturedPlan.Tasks.FirstOrDefault(task => string.Equals(
                            task.TaskId,
                            capturedPlan.InterruptionData?.InterruptedTaskId,
                            StringComparison.Ordinal))?.Title ?? "Step")}…",
                    quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(
                        "Open an editable correction prompt without rerunning the completed step from scratch."));
                continueButton.Focusable = false;
                continueButton.Click += (_, _) =>
                {
                    onContinueInterruptedTask(capturedPlan);
                    Close();
                };
                interruptedPanel.Children.Add(continueButton);
            }
            if (onResumePlan is not null)
            {
                var capturedPlan   = durablePlan;
                var capturedAction = onResumePlan;
                var safeResume = PlanRecoveryResumePolicy.IsSafelyResumable(capturedPlan);
                var resumeButton   = TranscriptQuickReplyFactory.CreateButton(
                    safeResume ? "Resume Plan" : "Resume Plan Anyway…",
                    quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(
                        safeResume
                            ? PlanRecoveryResumePolicy.IsAmendmentPreflightPause(capturedPlan)
                                ? "Resume the added amendment; completed steps remain accepted."
                                : PlanRecoveryResumePolicy.IsReworkPreflightPause(capturedPlan)
                                    ? "Resume the already-prepared rework. The change request will not be submitted again."
                                    : "Resume with the next runnable plan step."
                            : "Resume without assessing repository evidence. This may repeat work from the interrupted step."),
                    tone: safeResume ? QuickReplyTone.Default : QuickReplyTone.Warning);
                resumeButton.Focusable = false;
                resumeButton.Click += (_, _) =>
                {
                    capturedAction(capturedPlan);
                    Close();
                };
                interruptedPanel.Children.Add(resumeButton);
            }
            if (onEndPlan is not null)
            {
                var capturedPlan   = durablePlan;
                var capturedAction = onEndPlan;
                var endButton      = TranscriptQuickReplyFactory.CreateButton(
                    "End Plan",
                    quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip("Set this plan to Stopped. History is preserved but the plan cannot be resumed."));
                endButton.Focusable = false;
                endButton.Click += (_, _) =>
                {
                    capturedAction(capturedPlan);
                    Close();
                };
                interruptedPanel.Children.Add(endButton);
            }
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

        if (durablePlan is not null &&
            durablePlan.LifecycleStatus == PlanLifecycleStatus.Approved &&
            onStartPlan is not null)
        {
            var approvedPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 0, 0, 8),
            };
            var capturedPlan   = durablePlan;
            var capturedAction = onStartPlan;
            var startButton    = TranscriptQuickReplyFactory.CreateButton(
                "Start Plan",
                quickReplyFontSize,
                toolTip: ToolTipHelper.MakeThemedToolTip("Begin executing this approved plan."));
            startButton.Focusable = false;
            startButton.Click += (_, _) =>
            {
                capturedAction(capturedPlan);
                Close();
            };
            approvedPanel.Children.Add(startButton);
            header.Children.Add(approvedPanel);
        }

        var summaryBlock = new TextBlock
        {
            Text         = group.Summary,
            TextWrapping = TextWrapping.Wrap,
            FontWeight   = FontWeights.SemiBold,
            Margin       = new Thickness(0, 0, 0, 6),
        };
        summaryBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        summaryBlock.FontSize = quickReplyFontSize;
        header.Children.Add(summaryBlock);

        var hintBlock = new TextBlock
        {
            Text         = "Arrows point from prerequisite → dependent.  ALL means every incoming step must finish.  Steps in the same stage with no arrow between them are independent and may run in any order.",
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(22, 6, 22, 8),
        };
        hintBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        hintBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        _readingHint = hintBlock;

        if (durablePlan is not null)
        {
            var metaPanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

            TextBlock MkMeta(string text)
            {
                var tb = new TextBlock { Text = text };
                tb.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                tb.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
                return tb;
            }
            void AddMetaSep() => metaPanel.Children.Add(MkMeta(" · "));
            void AddMeta(string text) => metaPanel.Children.Add(MkMeta(text));

            var planIdBlock = new TextBlock
            {
                Text       = durablePlan.PlanId,
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            };
            planIdBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            planIdBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            metaPanel.Children.Add(planIdBlock);

            var sourceLabel = durablePlan.Source switch
            {
                PlanSource.TasksJson         => "Plan",
                PlanSource.DecomposeDecision => "Decomposition",
                PlanSource.Inbox             => "Inbox",
                PlanSource.Manual            => "Manual",
                _                            => durablePlan.Source,
            };
            AddMetaSep(); AddMeta(durablePlan.Branch);
            AddMetaSep(); AddMeta(sourceLabel);
            if (durablePlan.Timestamps.StartedAt is { } metaStartedAt)  { AddMetaSep(); AddMeta($"Started: {metaStartedAt:MMM d, yyyy}"); }
            if (durablePlan.Timestamps.CompletedAt is { } metaCompletedAt) { AddMetaSep(); AddMeta($"Completed: {metaCompletedAt:MMM d, yyyy}"); }

            header.Children.Add(metaPanel);
        }

        if (durablePlan?.InterruptionData is { } interruptionData)
        {
            var interruptionStack = new StackPanel();

            var intRow1 = new TextBlock
            {
                Text       = $"⚠ Interrupted · {interruptionData.RecoveryState}",
                FontWeight = FontWeights.SemiBold,
            };
            intRow1.SetResourceReference(TextBlock.ForegroundProperty, "PriorityHigh");
            intRow1.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            interruptionStack.Children.Add(intRow1);

            if (interruptionData.InterruptedTaskId is { } interruptedTaskId)
            {
                var intRow2 = new TextBlock { Text = $"Last step: {interruptedTaskId}" };
                intRow2.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                intRow2.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
                interruptionStack.Children.Add(intRow2);
            }
            if (interruptionData.LastCommit is { } lastCommit)
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
            if (interruptionData.PartialWorkEvidence is { } evidence && evidence.Length > 0)
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

        if (durablePlan?.LifecycleStatus == PlanLifecycleStatus.AwaitingApproval && onApproveGate is not null)
        {
            var awaitingGate = durablePlan.ApprovalGates.FirstOrDefault(g =>
                g.Status == PlanGateStatus.AwaitingApproval);
            if (awaitingGate is not null)
            {
                var capturedApprPlan = durablePlan;
                var capturedApprGate = awaitingGate;
                var capturedApprove  = onApproveGate;
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
                    "Approve & Continue",
                    quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip("Approve this gate and resume plan execution."));
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
            Background          = Brushes.Transparent,
            Margin              = new Thickness(18, 4, 18, 18),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment   = VerticalAlignment.Top,
        };
        _graphCanvas = canvas;
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
        var scroll = new ScrollViewer
        {
            Content = canvas,
            Margin  = new Thickness(22, 0, 22, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
        };
        _graphScroll = scroll;
        scroll.SetResourceReference(ScrollViewer.StyleProperty,      "RosterScrollViewerStyle");
        scroll.SetResourceReference(ScrollViewer.BackgroundProperty, "CardSurface");
        scroll.PreviewMouseWheel += OnGraphScrollPreviewMouseWheel;

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
        };
        detailViewer.SetResourceReference(FlowDocumentScrollViewer.BackgroundProperty, "CardSurface");
        detailViewer.SetResourceReference(FlowDocumentScrollViewer.ForegroundProperty, "BodyText");
        Grid.SetColumn(detailViewer, 2);
        splitGrid.Children.Add(detailViewer);

        _detailDocument = detailDocument;

        Grid.SetRow(splitGrid, 1);
        root.Children.Add(splitGrid);

        // Reading guidance belongs with the graph it explains, not in the plan's proposal
        // header. Keep it visible immediately below the graph and above approval details.
        Grid.SetRow(hintBlock, 2);
        root.Children.Add(hintBlock);

        _contentHolder ??= ApplyOuterBorder(titleText: group.GroupTitle);
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
        var stageHeaderReserve = 36 * _scaleFactor;
        var graphTop = Math.Max(stageHeaderReserve, 12 * _scaleFactor + validationRailHeight);
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
            Canvas.SetTop(headerElement, graphTop - 36 * _scaleFactor);
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
                positions[tasks[row].Id] = new Point(x, nextY);
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
        var octagonSize = 16 * _scaleFactor;
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
                        : null);
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
                    var removeMilestoneItem = new MenuItem { Header = "Remove this approval requirement" };
                    removeMilestoneItem.Style = TryFindResource("ThemedMenuItemStyle") as Style;
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
                    var menu = new ContextMenu();
                    var edit = new MenuItem
                    {
                        Header = "Edit validation",
                        IsEnabled = definitionEditable,
                    };
                    edit.Click += (_, _) =>
                    {
                        _selectedElement = new SelectedElementIdentity("validation", capturedValidationId);
                        _isInspectorEditing = true;
                        ApplySelectionVisual();
                        RefreshDetailPanel();
                    };
                    var remove = new MenuItem
                    {
                        Header = "Delete validation",
                        IsEnabled = definitionEditable,
                    };
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
        var _deferredBadgeHovers = new List<(Border Badge, List<ConnectorGroup> Cgs)>();
        void RegisterConnector(string taskId, ConnectorGroup cg, ConnectorEndpointRole role)
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
        var gateConnectorGroups = new List<List<ConnectorGroup>>(gates.Count);
        foreach (var (gateCenter, targets, dependencies, minTargetLevel, maxDepLevel) in gates)
        {
            var cgsForGate = new List<ConnectorGroup>();
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
                var cg = AddConnector(canvas,
                    fromPt, toPt,
                    arrowHead: false,
                    skipCount: Math.Max(0, depSkip),
                    dashed: targets.Any(target => dashedTaskEdges.Contains((dependency, target.Id))),
                    splitAtX: FindSplitX(fromPt.X, toPt.X));
                RegisterConnector(dependency, cg, ConnectorEndpointRole.Start);
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
                var cg = AddConnector(canvas,
                    fromPt, toPt,
                    arrowHead: true,
                    skipCount: Math.Max(0, targetSkip),
                    dashed: joinIsLocked || combinedDashed,
                    splitAtX: FindSplitX(fromPt.X, toPt.X));
                RegisterConnector(target.Id, cg, ConnectorEndpointRole.End);
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
                RegisterConnector(dependency, directConnector, ConnectorEndpointRole.Start);
                RegisterConnector(task.Id, directConnector, ConnectorEndpointRole.End);
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
            var joinIsPrimary = existingJoinGate is null || IsPrimary(existingJoinGate, joinAnchor);
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

            var joinGate = existingJoinGate ?? coveringJoinGate;
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
                            ? "This ALL join is covered by a larger approval requirement."
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
                            var coveringUpdated = PlanGateManager.SetPresentationAnchor(
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
                    joinIsLocked && (!joinIsPrimary || coveringJoinGate is not null ||
                                     collectivelyCoveredJoin) ? 0.5 : 1.0,
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
                        : null);
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
                    var removeJoinItem = new MenuItem { Header = "Remove this approval requirement" };
                    removeJoinItem.Style = TryFindResource("ThemedMenuItemStyle") as Style;
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

        var borderByTask = new Dictionary<string, Border>(StringComparer.Ordinal);
        var avatarChipsByAgent = new Dictionary<string, List<Border>>(StringComparer.OrdinalIgnoreCase);
        var taskActivityById = durablePlan is null
            ? new Dictionary<string, PlanTaskActivityState>(StringComparer.Ordinal)
            : PlanTaskActivityResolver.Resolve(durablePlan);
        var taskOrdinalById = group.Tasks
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
            var prereqLines = task.DependsOn.Count == 0
                ? ["None — this step can start immediately."]
                : task.DependsOn.Select(id =>
                {
                    if (!tasksById.TryGetValue(id, out var dep)) return $"• {id}";
                    var label = dep.Title ?? dep.Description;
                    return "• " + (label.Length > 60 ? label[..60] + "…" : label);
                }).ToArray();

            string? taskIconKey = isTaskExecuting ? null : durableTask?.Status switch
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
                Text         = task.Title ?? task.Description,
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
                            "The step is receiving its one bounded automatic correction.",
                        _ => "This step is actively receiving work from one or more agents.",
                    }),
                };
                spinner.SetResourceReference(ActivitySpinner.FontSizeProperty, "FontSizeSmall");
                Grid.SetColumn(spinner, spinnerOnRight ? 2 : 0);
                _taskSpinnersById[task.Id] = spinner;
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
                Text         = task.Description,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin       = new Thickness(0, 5, 0, 0),
            };
            nodeDescription.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            nodeDescription.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
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
                    taskOrdinalById[task.Id], activityState),
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
                ToolTip         = BuildTaskToolTip(
                    task.Title ?? task.Description,
                    task.Description,
                    prereqLines,
                    durableTask?.CompletionSummary,
                    durableTask?.Commit,
                    durableTask?.ProvenanceChain),
                Child           = nodeLayout,
            };
            border.SetResourceReference(Border.BackgroundProperty,  "CardSurface");
            border.SetResourceReference(Border.BorderBrushProperty, borderColorKey);
            border.Tag = $"task:{task.Id}";
            WireSelectionClick(border);
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

                var contextMenu = new ContextMenu();
                var canInsertBefore = PlanStoreUpdater.CanInsertTask(durablePlan, capturedTask.Id, insertAfter: false);
                var canInsertAfter = PlanStoreUpdater.CanInsertTask(durablePlan, capturedTask.Id, insertAfter: true);
                contextMenu.Items.Add(CreateInsertTaskMenuItem(insertAfter: false, canInsertBefore));
                contextMenu.Items.Add(CreateInsertTaskMenuItem(insertAfter: true, canInsertAfter));
                contextMenu.Items.Add(new Separator());
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
                    contextMenu.Items.Add(new Separator());
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
                        var removeItem = new MenuItem
                        {
                            Header = isRequiredVerificationProof
                                ? "Required verification checkpoint"
                                : $"Remove approval checkpoint ({relationship})",
                            IsEnabled = PlanGateManager.CanRemoveGate(durablePlan, durableGate),
                        };
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
                    contextMenu.Items.Add(new Separator());
                    var dependsOnSubmenu1 = CreateDependsOnSubmenu(capturedTask.Id, group, levels, positions);
                    if (dependsOnSubmenu1 is not null)
                    {
                        contextMenu.Items.Add(dependsOnSubmenu1);
                        contextMenu.Items.Add(new Separator());
                    }
                    var addValidation = new MenuItem
                    {
                        Header = "Add validation after this step",
                        IsEnabled = definitionEditable,
                    };
                    addValidation.Click += (_, _) => ApplyStagedMutation(
                        StagedPlanEditor.AddValidationAfterTask(_plan!.Group, capturedTask.Id),
                        selectKind: "validation",
                        beginEditingSelection: true);
                    contextMenu.Items.Add(addValidation);

                    var deleteTask = new MenuItem
                    {
                        Header = "Delete step",
                        IsEnabled = group.Tasks.Count > 1 && definitionEditable,
                    };
                    deleteTask.Click += (_, _) =>
                    {
                        if (!ConfirmDestructivePlanEdit(
                                $"Delete step “{capturedTask.Title ?? capturedTask.Id}” and reconnect its dependents?"))
                            return;
                        ApplyStagedMutation(StagedPlanEditor.DeleteTask(_plan!.Group, capturedTask.Id));
                    };
                    contextMenu.Items.Add(deleteTask);
                }

                MenuItem CreateInsertTaskMenuItem(bool insertAfter, bool isEnabled)
                {
                    var relativePosition = insertAfter ? "after" : "before";
                    var item = new MenuItem
                    {
                        Header = _saveDefinitionEdit is not null
                            ? $"Insert step {relativePosition}"
                            : $"Insert step {relativePosition}…",
                        IsEnabled = isEnabled,
                        ToolTip = isEnabled
                            ? ToolTipHelper.MakeThemedToolTip(
                                $"Add a new step {relativePosition} this still-pending step and update its future dependencies.")
                            : ToolTipHelper.MakeThemedToolTip(
                                "Only the unstarted downstream portion of a plan can be changed."),
                    };
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
                var contextMenu = new ContextMenu();
                var insertBefore = CreateStagedInsertMenuItem(capturedTaskId, before: true);
                insertBefore.IsEnabled = definitionEditable;
                contextMenu.Items.Add(insertBefore);
                var insertAfter = CreateStagedInsertMenuItem(capturedTaskId, before: false);
                insertAfter.IsEnabled = definitionEditable;
                contextMenu.Items.Add(insertAfter);
                contextMenu.Items.Add(new Separator());
                var dependsOnSubmenu2 = CreateDependsOnSubmenu(capturedTaskId, group, levels, positions);
                if (dependsOnSubmenu2 is not null)
                {
                    contextMenu.Items.Add(dependsOnSubmenu2);
                    contextMenu.Items.Add(new Separator());
                }
                var addValidation = new MenuItem
                {
                        Header = "Add validation after this step",
                    IsEnabled = definitionEditable,
                };
                addValidation.Click += (_, _) => ApplyStagedMutation(
                    StagedPlanEditor.AddValidationAfterTask(_plan!.Group, capturedTaskId),
                    selectKind: "validation",
                    beginEditingSelection: true);
                contextMenu.Items.Add(addValidation);
                var deleteTask = new MenuItem
                {
                    Header = "Delete step",
                    IsEnabled = group.Tasks.Count > 1 && definitionEditable,
                };
                deleteTask.Click += (_, _) =>
                {
                    if (!ConfirmDestructivePlanEdit($"Delete step {capturedTaskId} and reconnect its dependents?"))
                        return;
                    ApplyStagedMutation(StagedPlanEditor.DeleteTask(_plan!.Group, capturedTaskId));
                };
                contextMenu.Items.Add(deleteTask);
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

            if (hasAgentAvatars)
            {
                var avatarPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    MaxWidth = NodeWidth - 8 * _scaleFactor,
                    ClipToBounds = false,
                };
                foreach (var assignment in task.AgentAssignments!)
                {
                    var info = _resolveAgentAvatar!(assignment.AgentHandle);
                    var chip = CreateAgentAvatarChip(info, avatarChipSize, assignment.AgentHandle);
                    chip.Tag = $"agent:{assignment.AgentHandle}";
                    WireSelectionClick(chip);
                    chip.ToolTip = ToolTipHelper.MakeThemedToolTip(
                        $"{assignment.AgentHandle} — {assignment.Role}");

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
                    WireSelectionClick(coordinatorChip);
                    Canvas.SetLeft(coordinatorChip, position.X + NodeWidth - chipSize - 4 * _scaleFactor);
                    Canvas.SetTop(coordinatorChip, position.Y - chipSize);
                    Panel.SetZIndex(coordinatorChip, 35);
                    canvas.Children.Add(coordinatorChip);
                    RegisterTaskDragOverlay(task.Id, coordinatorChip);
                }
            }

            borderByTask[task.Id] = border;

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
                    var beforeIsPrimary = IsPrimary(existingBeforeGate, beforeAnchor);
                    var beforeController = coveringBeforeGate is null ? null : ResolvePresentationAnchor(coveringBeforeGate);
                    var controllingBeforeGate = existingBeforeGate ?? coveringBeforeGate;
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
                            ? "This step entry is covered by a larger approval requirement."
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
                                var coveringUpdated = PlanGateManager.SetPresentationAnchor(
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
                                var question = _saveDefinitionEdit is not null
                                    ? SimpleInputDialog.Show(this, "Enter a reviewer question for this approval gate:", "Add Approval Gate", defaultQ)
                                    : defaultQ;
                                if (string.IsNullOrWhiteSpace(question)) return;
                                updated = PlanGateManager.AddGateBefore(durablePlan, capturedTaskForStop.Id,
                                    $"Review before starting: {taskTitle}", question: question);
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
                            beforeEngaged && (!beforeIsPrimary || coveringBeforeGate is not null ||
                                              collectivelyCoveredEntry) ? 0.5 : 1.0,
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
                                : null);
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
                            var removeBeforeItem = new MenuItem { Header = "Remove this approval requirement" };
                            removeBeforeItem.Style = TryFindResource("ThemedMenuItemStyle") as Style;
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
                            ? "This step exit is covered by a larger approval requirement."
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
                                var coveringUpdated = PlanGateManager.SetPresentationAnchor(
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
                                var question = _saveDefinitionEdit is not null
                                    ? SimpleInputDialog.Show(this, "Enter a reviewer question for this approval gate:", "Add Approval Gate", defaultQ)
                                    : defaultQ;
                                if (string.IsNullOrWhiteSpace(question)) return;
                                updated = PlanGateManager.AddGateAfter(durablePlan, capturedTaskForStop.Id,
                                    $"Review after completing: {taskTitle}", question: question);
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
                            afterEngaged && (!afterIsPrimary ||
                                             collectivelyCoveredByAllJoins) ? 0.5 : 1.0,
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
                                : null);
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
                            var removeAfterItem = new MenuItem { Header = "Remove this approval requirement" };
                            removeAfterItem.Style = TryFindResource("ThemedMenuItemStyle") as Style;
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
                        if (borderByTask.TryGetValue(tid, out var b)) b.Effect = TaskNodeGlowEffect();
                    foreach (var gb in capturedCg.GateBadges) gb.Effect = TaskNodeGlowEffect();
                };
                el.MouseLeave += (_, _) =>
                {
                    foreach (var g in capturedCg.GlowElements) { g.Visibility = Visibility.Hidden; Panel.SetZIndex(g, 0); }
                    foreach (var m in capturedCg.MainElements) Panel.SetZIndex(m, 0);
                    foreach (var tid in capturedCg.TaskIds)
                        if (borderByTask.TryGetValue(tid, out var b)) b.Effect = null;
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
                        if (borderByTask.TryGetValue(tid, out var b)) b.Effect = TaskNodeGlowEffect();
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
                        if (borderByTask.TryGetValue(tid, out var b)) b.Effect = null;
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
                    if (borderByTask.TryGetValue(tid, out var b)) b.Effect = TaskNodeGlowEffect();
                foreach (var tid in capturedBefore)
                    if (borderByTask.TryGetValue(tid, out var b)) b.Effect = TaskNodeGlowEffect();
            };
            capturedRow.MouseLeave += (_, _) =>
            {
                foreach (var tid in capturedAfter)
                    if (borderByTask.TryGetValue(tid, out var b)) b.Effect = null;
                foreach (var tid in capturedBefore)
                    if (borderByTask.TryGetValue(tid, out var b)) b.Effect = null;
            };
        }

        // Kick off any pending dependency-edit animation after the new layout is applied.
        if (_pendingAnimation is { } animData)
        {
            var capturedBorderByTask = borderByTask;
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

        canvas.Width  = Math.Max(positions.Values.Max(point => point.X) + NodeWidth, validationRailRight);
        canvas.Height = Math.Max(
            positions.Values.Max(point => point.Y) + NodeHeight,
            Math.Max(
                validationBottom,
                allClusterFootprints.Select(footprint => footprint.Bottom).DefaultIfEmpty(0).Max()));
        _canvasContentWidth = canvas.Width;
        _canvasContentHeight = canvas.Height;
        ResolveAutomaticScrollTarget(durablePlan, levels, positions, canvas.Margin.Left);

        if (durablePlan is not null)
        {
            var approvalSummary = BuildApprovalSummaryPanel(
                durablePlan,
                levels,
                quickReplyFontSize);
            Grid.SetRow(approvalSummary, 3);
            approvalSummary.Visibility = Visibility.Visible;
            approvalSummary.MaxHeight = 0;
            root.Children.Add(approvalSummary);
            _approvalSummary = approvalSummary;

            if (_initialLayoutApplied)
            {
                Dispatcher.BeginInvoke(
                    () => ResizeForApprovalSummary(approvalSummary, previousApprovalHeight),
                    System.Windows.Threading.DispatcherPriority.Render);
            }
        }
        else if (_initialLayoutApplied && previousApprovalHeight > 0)
        {
            Height = Math.Max(MinHeight, ActualHeight - previousApprovalHeight);
            _allocatedApprovalHeight = 0;
        }
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
        var copyButton = TranscriptQuickReplyFactory.CreateButton("Copy Details", _quickReplyFontSize);
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
                UIErrorHelper.ShowError("Plan", ex.Message, this);
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
                            content = PlanPreflightRecoveryContent.From(
                                new PlanPreflightBlockedException(
                                    exception.Condition,
                                    currentDirtyPaths,
                                    exception.TargetBranch));
                            summary.Text = content.Summary;
                            files.Text = content.ChangedFilesSummary;
                            files.Visibility = string.IsNullOrWhiteSpace(content.ChangedFilesSummary)
                                ? Visibility.Collapsed
                                : Visibility.Visible;
                            detailText.Text = content.TechnicalDetails;
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
        var horizontalOffset = _graphScroll?.HorizontalOffset ?? 0;
        var verticalOffset   = _graphScroll?.VerticalOffset  ?? 0;
        // Defer the content rebuild to the next dispatcher cycle so any in-flight mouse/keyboard
        // event (e.g. ButtonBase.OnMouseLeftButtonDown calling Focus()) fully completes before
        // _contentHolder.Child is replaced. Replacing the child mid-event detaches elements from
        // the visual tree while WPF's input system still holds references to them, which causes
        // a NullReferenceException inside HwndKeyboardInputProvider.AcquireFocus.
        Dispatcher.BeginInvoke(() =>
        {
            BuildContent(plan, durablePlan);
            Dispatcher.BeginInvoke(() =>
            {
                _graphScroll?.ScrollToHorizontalOffset(horizontalOffset);
                _graphScroll?.ScrollToVerticalOffset(verticalOffset);
                if (autoScrollForPlanProgress &&
                    PlanViewerAutoScrollPolicy.IsInteractionQuiet(_lastViewerInteractionUtc, DateTime.UtcNow))
                {
                    Dispatcher.BeginInvoke(
                        EnsureAutomaticScrollTargetVisible,
                        System.Windows.Threading.DispatcherPriority.Render);
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
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
        bool preserveReview = false)
    {
        if (_saveDefinitionEdit is null || _plan is null) return false;
        if (!StagedPlanEditor.TryValidate(candidate, out var normalized, out var error) || normalized is null)
        {
            UIErrorHelper.ShowError("Edit Plan", error ?? "The edit would make the plan invalid.", this);
            return false;
        }
        if (_durablePlan is not null &&
            _durablePlan.LifecycleStatus != PlanLifecycleStatus.Staged &&
            !StagedPlanEditor.TryValidateUnlockedChanges(_durablePlan, normalized, out error))
        {
            UIErrorHelper.ShowError("Edit Plan", error ??
                "The edit would change work that has already started.", this);
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
            else if (_selectedElement is { } selected && !ElementStillExists(saved.Group, selected))
            {
                _selectedElement = null;
            }

            RebuildPreservingScroll(saved, _durablePlan);
            Title = $"Plan — {_durablePlan?.DisplayName ?? saved.Group.GroupTitle}";
            return true;
        }
        catch (PlanEditPauseRequiredException exception)
        {
            _deferredDefinitionEdit = normalized;
            MessageBox.Show(this, exception.Message, "Plan edit queued",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        catch (Exception exception)
        {
            UIErrorHelper.ShowError("Edit Plan", exception.Message, this);
            return false;
        }
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
        var item = new MenuItem { Header = before ? "Insert step before" : "Insert step after" };
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

        var submenu = new MenuItem { Header = "This approval blocks" };
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
            var item = new MenuItem
            {
                Header = capturedCandidate.Title ?? capturedCandidate.Id,
                IsCheckable = true,
                IsChecked = beforeSet.Contains(capturedCandidate.Id),
                IsEnabled = canEdit,
            };
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
        var submenu = new MenuItem { Header = "Depends On" };

        void AddGroupHeader(string label)
        {
            var headerItem = new MenuItem
            {
                Header    = label,
                IsEnabled = false,
                FontStyle = FontStyles.Italic,
            };
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
            var item = new MenuItem
            {
                Header      = capturedCandidate.Title ?? capturedCandidate.Id,
                IsCheckable = true,
                IsChecked   = capturedCurrentDep,
                IsEnabled   = itemEnabled,
            };
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
            if (earlier.Length > 0) submenu.Items.Add(new Separator());
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
        SelectedElementIdentity selected) => selected.Kind switch
    {
        "task" => group.Tasks.Any(task => task.Id == selected.Id),
        "validation" => (group.Validations ?? []).Any(value => value.ValidationId == selected.Id),
        "gate" or "humanapproval" => (group.ApprovalGates ?? []).Any(value => value.GateId == selected.Id),
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

    private static FrameworkElement BuildApprovalSummaryPanel(
        Plan plan,
        IReadOnlyDictionary<string, int> levels,
        double contentFontSize)
    {
        var title = new TextBlock
        {
            Text = "Human approval requirements",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 7, 10, 3),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        title.FontSize = contentFontSize;

        var document = new FlowDocument
        {
            PagePadding = new Thickness(10, 2, 10, 8),
            ColumnWidth = double.PositiveInfinity,
            FontFamily  = new FontFamily("Segoe UI, Segoe UI Emoji"),
        };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "LabelText");
        document.SetResourceReference(FlowDocument.BackgroundProperty, "CardSurface");
        document.FontSize = contentFontSize;

        string TaskName(string id) => plan.Tasks.FirstOrDefault(task =>
            string.Equals(task.TaskId, id, StringComparison.Ordinal)) is { } task
                ? task.Title ?? task.Description
                : id;

        static Paragraph Sentence(string prefix, string boldText, string suffix)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
            paragraph.Inlines.Add(new Run(prefix));
            paragraph.Inlines.Add(new Bold(new Run(boldText)));
            paragraph.Inlines.Add(new Run(suffix));
            return paragraph;
        }

        System.Windows.Documents.List TaskList(IEnumerable<string> taskIds)
        {
            var list = new System.Windows.Documents.List
            {
                MarkerStyle = TextMarkerStyle.Disc,
                Margin = new Thickness(20, 1, 0, 2),
            };
            foreach (var taskId in taskIds)
            {
                var paragraph = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                paragraph.Inlines.Add(new Bold(new Run(TaskName(taskId))));
                list.ListItems.Add(new ListItem(paragraph));
            }
            return list;
        }

        var summary = PlanApprovalSummaryBuilder.Build(plan, levels);
        if (plan.ApprovalGates.Count == 0)
        {
            document.Blocks.Add(new Paragraph(new Run("No human approval requirements."))
                { Margin = new Thickness(0) });
        }
        else if (summary.BetweenEveryStage)
        {
            document.Blocks.Add(new Paragraph(new Run(
                "Human approval will be required between every stage.")) { Margin = new Thickness(0) });
        }
        else
        {
            var list = new System.Windows.Documents.List
            {
                MarkerStyle = TextMarkerStyle.Disc,
                Margin = new Thickness(18, 0, 0, 0),
            };
            foreach (var item in summary.Items)
            {
                var listItem = new ListItem();
                switch (item.Kind)
                {
                    case ApprovalSummaryKind.TaskBefore:
                        listItem.Blocks.Add(Sentence("Before ", TaskName(item.TaskId!), " starts."));
                        break;
                    case ApprovalSummaryKind.TaskAfter:
                        listItem.Blocks.Add(Sentence("After ", TaskName(item.TaskId!), " completes."));
                        break;
                    case ApprovalSummaryKind.Stage:
                        listItem.Blocks.Add(new Paragraph(new Run(
                            $"After Stage {item.LeftStage} completes and before Stage {item.LeftStage + 1} begins."))
                            { Margin = new Thickness(0, 1, 0, 1) });
                        break;
                    case ApprovalSummaryKind.All:
                        listItem.Blocks.Add(new Paragraph(new Run("After all the following steps complete:"))
                            { Margin = new Thickness(0, 1, 0, 1) });
                        listItem.Blocks.Add(TaskList(item.AfterTaskIds));
                        break;
                    default:
                        listItem.Blocks.Add(new Paragraph(new Run("Approval after:"))
                            { Margin = new Thickness(0, 1, 0, 1) });
                        listItem.Blocks.Add(TaskList(item.AfterTaskIds));
                        listItem.Blocks.Add(new Paragraph(new Run("Before:"))
                            { Margin = new Thickness(0, 2, 0, 1) });
                        listItem.Blocks.Add(TaskList(item.BeforeTaskIds));
                        break;
                }
                list.ListItems.Add(listItem);
            }
            document.Blocks.Add(list);
        }

        var viewer = new FlowDocumentScrollViewer
        {
            Document = document,
            IsToolBarVisible = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
        };
        viewer.SetResourceReference(FlowDocumentScrollViewer.BackgroundProperty, "CardSurface");
        viewer.SetResourceReference(FlowDocumentScrollViewer.ForegroundProperty, "LabelText");
        viewer.FontSize = contentFontSize;

        var stack = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        stack.Children.Add(title);
        stack.Children.Add(viewer);

        var border = new Border
        {
            Child = stack,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(8, 0, 8, 8),
        };
        border.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        border.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        return border;
    }

    private void OnInitialContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnInitialContentRendered;
        ApplyInitialWindowLayout();
        _initialLayoutApplied = true;
        Dispatcher.BeginInvoke(EnsureAutomaticScrollTargetVisible,
            System.Windows.Threading.DispatcherPriority.Render);
    }

    private void ApplyInitialWindowLayout()
    {
        const double graphShare = 0.70;
        const double splitterWidth = 5;
        const double graphHorizontalInsets = 22 * 2 + 18 * 2;
        const double canvasVerticalInsets = 4 + 18;
        const double windowBorderInsets = 3;

        var workArea = GetLogicalWorkArea();
        var requiredGraphColumnWidth = _canvasContentWidth + graphHorizontalInsets;
        var idealWidth = requiredGraphColumnWidth / graphShare + splitterWidth + windowBorderInsets;
        Width = Math.Min(workArea.Width, Math.Max(MinWidth, idealWidth));
        UpdateLayout();

        var approvalHeight = _approvalSummary is null
            ? 0
            : MeasureApprovalSummaryHeight(_approvalSummary);
        var fixedHeight = CloseButtonHeight + windowBorderInsets +
                          (_header?.DesiredSize.Height ?? 0) +
                          _canvasContentHeight + canvasVerticalInsets +
                          (_readingHint?.DesiredSize.Height ?? 0);
        var idealHeight = fixedHeight + approvalHeight;
        Height = Math.Min(workArea.Height, Math.Max(MinHeight, idealHeight));

        var approvalSpace = Math.Max(0, Height - fixedHeight);
        var allocatedApprovalHeight = Math.Min(approvalHeight, approvalSpace);
        if (_approvalSummary is not null)
        {
            var showsFullSummary = allocatedApprovalHeight >= approvalHeight - 0.5;
            if (!showsFullSummary)
                allocatedApprovalHeight = Math.Min(170, allocatedApprovalHeight);
            _approvalSummary.MaxHeight = showsFullSummary
                ? double.PositiveInfinity
                : allocatedApprovalHeight;
        }
        _allocatedApprovalHeight = allocatedApprovalHeight;

        KeepWindowInside(workArea);
        UpdateLayout();
    }

    private void ResizeForApprovalSummary(FrameworkElement approvalSummary, double previousHeight)
    {
        if (!ReferenceEquals(_approvalSummary, approvalSummary))
            return;

        var desiredHeight = MeasureApprovalSummaryHeight(approvalSummary);
        var workArea = GetLogicalWorkArea();
        var availableBelow = Math.Max(0, workArea.Bottom - (Top + ActualHeight));
        var canAllocate = Math.Max(0, previousHeight + availableBelow);
        var showsFullSummary = desiredHeight <= canAllocate + 0.5;
        var targetHeight = showsFullSummary
            ? desiredHeight
            : Math.Min(170, canAllocate);

        var heightDelta = targetHeight - previousHeight;
        if (Math.Abs(heightDelta) > 0.5)
            Height = Math.Min(workArea.Height, Math.Max(MinHeight, ActualHeight + heightDelta));

        approvalSummary.MaxHeight = showsFullSummary
            ? double.PositiveInfinity
            : targetHeight;
        _allocatedApprovalHeight = targetHeight;
        UpdateLayout();
    }

    private double MeasureApprovalSummaryHeight(FrameworkElement approvalSummary)
    {
        approvalSummary.Visibility = Visibility.Visible;
        approvalSummary.MaxHeight = double.PositiveInfinity;
        approvalSummary.Measure(new Size(Math.Max(1, ActualWidth - 16), double.PositiveInfinity));
        var desiredHeight = approvalSummary.DesiredSize.Height;
        approvalSummary.MaxHeight = 0;
        return Math.Max(0, desiredHeight);
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
            ? $"{task.Title ?? task.Description} ({id})"
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
        Action? unapproveAction = null)
    {
        var s = _scaleFactor;
        FrameworkElement indicator;
        Polygon? stop = null;
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
            if (approvalChangeActive)
            {
                var activityStack = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                activityStack.Children.Add(approvedContainer);
                var spinner = new ActivitySpinner
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 1 * s, 0, 0),
                    AccentColor = ResolvePlanSpinnerColor(),
                    MinimumOpacity = 1.0,
                };
                spinner.SetResourceReference(ActivitySpinner.FontSizeProperty, "FontSizeSmall");
                spinner.SetContinuousActive(true);
                activityStack.Children.Add(spinner);
                indicator = activityStack;
            }
            else
            {
                indicator = approvedContainer;
            }
        }
        else
        {
            stop = new Polygon
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
            stop.SetResourceReference(Shape.StrokeProperty, "LineColor");
            indicator = stop;
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
        if (toggle is not null && stop is not null && selectionAnchor is null)
        {
            hitTarget.MouseEnter += (_, _) => stop.StrokeThickness = 2.2;
            hitTarget.MouseLeave += (_, _) => stop.StrokeThickness = 1.6;
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
            if (toggle is not null && stop is not null)
            {
                var menu = new ContextMenu
                {
                    Style = TryFindResource("ThemedContextMenuStyle") as Style,
                };
                var toggleItem = new MenuItem
                {
                    Header = toggleActionLabel ?? "Change this approval control",
                    Style = TryFindResource("ThemedMenuItemStyle") as Style,
                };
                toggleItem.Click += (_, _) => toggle();
                menu.Items.Add(toggleItem);
                hitTarget.ContextMenu = menu;
            }

        }
        // Approval/unapproval context menu for the human-gate icon.
        if (approveAction is not null || unapproveAction is not null)
        {
            var menu = new ContextMenu();
            menu.Style = TryFindResource("ThemedContextMenuStyle") as Style;
            if (approveAction is not null)
            {
                var approveItem = new MenuItem
                {
                    Header = "Approve this step",
                    Style  = TryFindResource("ThemedMenuItemStyle") as Style,
                };
                approveItem.Click += (_, _) => approveAction();
                menu.Items.Add(approveItem);
            }
            if (unapproveAction is not null)
            {
                var unapproveItem = new MenuItem
                {
                    Header = "Cancel approval",
                    Style  = TryFindResource("ThemedMenuItemStyle") as Style,
                };
                unapproveItem.Click += (_, _) => unapproveAction();
                menu.Items.Add(unapproveItem);
            }
            hitTarget.ContextMenu = menu;
        }
        return hitTarget;
    }

    private Separator CreateThemedMenuSeparator()
    {
        var separator = new Separator();
        separator.Style = TryFindResource("ThemedMenuSeparatorStyle") as Style;
        return separator;
    }

    private static ToolTip BuildTaskToolTip(
        string title,
        string description,
        string[] prereqLines,
        string? completionSummary = null,
        string? commit = null,
        ProofProvenanceChain? provenanceChain = null)
    {
        var titleBlock = new TextBlock
        {
            Text = title,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 500,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 5),
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        titleBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");

        var descBlock = new TextBlock
        {
            Text         = description,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth     = 500,
        };
        descBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        descBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");

        var prereqHeader = new TextBlock
        {
            Text       = "Prerequisites:",
            FontWeight = FontWeights.SemiBold,
            Margin     = new Thickness(0, 8, 0, 2),
        };
        prereqHeader.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        prereqHeader.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");

        var panel = new StackPanel { MaxWidth = 500 };
        panel.Children.Add(titleBlock);
        panel.Children.Add(descBlock);
        panel.Children.Add(prereqHeader);
        foreach (var line in prereqLines)
        {
            var lineBlock = new TextBlock
            {
                Text         = line,
                TextWrapping = TextWrapping.Wrap,
            };
            lineBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            lineBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
            panel.Children.Add(lineBlock);
        }

        if (completionSummary is not null)
        {
            var completionHeader = new TextBlock
            {
                Text       = "Completion:",
                FontWeight = FontWeights.SemiBold,
                Margin     = new Thickness(0, 8, 0, 2),
            };
            completionHeader.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            completionHeader.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
            panel.Children.Add(completionHeader);

            var summaryBlock = new TextBlock
            {
                Text         = completionSummary,
                TextWrapping = TextWrapping.Wrap,
            };
            summaryBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            summaryBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
            panel.Children.Add(summaryBlock);
        }

        if (commit is not null)
        {
            var shortCommit = commit.Length >= 7 ? commit[..7] : commit;
            var commitBlock = new TextBlock
            {
                Text         = $"Commit: [{shortCommit}]",
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 4, 0, 0),
                FontFamily   = new FontFamily("Consolas, Courier New, monospace"),
            };
            commitBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            commitBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
            panel.Children.Add(commitBlock);
        }

        if (provenanceChain is { Entries.Count: > 0 })
        {
            var provenanceHeader = new TextBlock
            {
                Text       = "Prior attempts:",
                FontWeight = FontWeights.SemiBold,
                Margin     = new Thickness(0, 8, 0, 2),
            };
            provenanceHeader.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            provenanceHeader.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
            panel.Children.Add(provenanceHeader);

            var provenanceSummary = new TextBlock
            {
                Text         = provenanceChain.BuildSummary(),
                TextWrapping = TextWrapping.Wrap,
            };
            provenanceSummary.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            provenanceSummary.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
            panel.Children.Add(provenanceSummary);
        }

        return new ToolTip { Content = panel };
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

    // A set of UIElements (glow + main strokes) making up one logical connector.
    private sealed class ConnectorGroup
    {
        public readonly List<UIElement> GlowElements = [];
        public readonly List<UIElement> MainElements = [];
        public readonly List<string>    TaskIds       = [];
        // ALL-gate badge Borders that this connector enters or exits; highlighted on hover.
        public readonly List<Border>    GateBadges    = [];
        public int SkipCount { get; set; }
        public Point OriginalFrom { get; private set; }
        public Point OriginalTo { get; private set; }
        public Point CurrentFrom { get; private set; }
        public Point CurrentTo { get; private set; }
        private readonly Dictionary<string, ConnectorEndpointRole> _endpointRoles = new(StringComparer.Ordinal);
        private Action<Point, Point>? _endpointUpdater;

        // Recompute the glow brush from the current theme and update all glow elements.
        public void RefreshGlowBrushes()
        {
            var brush = new SolidColorBrush(ConnectorGlowColor(SkipCount));
            foreach (var el in GlowElements)
            {
                if (el is Shape shape)
                {
                    if (shape is Polygon)
                        shape.Fill = brush;
                    else
                        shape.Stroke = brush;
                }
            }
        }

        public void InitializeEndpoints(Point from, Point to, Action<Point, Point> endpointUpdater)
        {
            OriginalFrom = from;
            OriginalTo = to;
            CurrentFrom = from;
            CurrentTo = to;
            _endpointUpdater = endpointUpdater;
        }

        public void RegisterEndpointRole(string taskId, ConnectorEndpointRole role)
        {
            _endpointRoles.TryGetValue(taskId, out var existing);
            _endpointRoles[taskId] = existing | role;
        }

        public ConnectorEndpointRole GetEndpointRole(string taskId) =>
            _endpointRoles.TryGetValue(taskId, out var role) ? role : ConnectorEndpointRole.None;

        public void UpdateEndpoints(Point from, Point to)
        {
            CurrentFrom = from;
            CurrentTo = to;
            _endpointUpdater?.Invoke(from, to);
        }
    }

    private static ConnectorGroup AddConnector(
        Canvas canvas,
        Point from,
        Point to,
        bool arrowHead,
        int skipCount = 0,
        bool dashed = false,
        string? toolTip = null,
        double splitAtX = double.NaN,
        ConnectorGroup? existingGroup = null)
    {
        var group = existingGroup ?? new ConnectorGroup();
        group.SkipCount = skipCount;

        const double arrowLength     = 11;
        const double arrowHalfWidth  = 5;
        const double glowThickness   = 8;
        const double glowArrowHalf   = 10;

        var color     = ConnectorColor(skipCount);
        var glowColor = ConnectorGlowColor(skipCount);
        var mainBrush = new SolidColorBrush(color);
        var glowBrush = new SolidColorBrush(glowColor);
        var dashArray = dashed ? new DoubleCollection { 7, 2 } : null;

        // Line/curve ends at the arrowhead base-center so it enters the triangle's middle.
        var lineEnd = arrowHead ? new Point(to.X - arrowLength, to.Y) : to;
        var renderStraight = skipCount > 0 || Math.Abs(to.Y - from.Y) < 1.0 || Math.Abs(to.X - from.X) < 1.0;
        var doLineSplit = !double.IsNaN(splitAtX) && splitAtX > from.X + 1.0 && splitAtX < lineEnd.X - 1.0;

        if (renderStraight)
        {
            // Straight line.
            var glowLine = new Line
            {
                X1 = from.X, Y1 = from.Y, X2 = lineEnd.X, Y2 = lineEnd.Y,
                StrokeThickness = glowThickness, Stroke = glowBrush,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Visibility = Visibility.Hidden,
            };
            if (dashArray is not null) glowLine.StrokeDashArray = null; // glow is always solid
            canvas.Children.Add(glowLine);
            group.GlowElements.Add(glowLine);

            // Main line — split at a locked milestone boundary if one crosses this segment.
            Line? leftLine = null;
            Line? rightLine = null;
            Line? mainLine = null;
            if (doLineSplit)
            {
                double tSplit  = (splitAtX - from.X) / (lineEnd.X - from.X);
                double splitY  = from.Y + tSplit * (lineEnd.Y - from.Y);
                var    splitPt = new Point(splitAtX, splitY);

                leftLine = new Line
                {
                    X1 = from.X, Y1 = from.Y, X2 = splitPt.X, Y2 = splitPt.Y,
                    StrokeThickness = 2, Stroke = mainBrush,
                };
                if (dashArray is not null) leftLine.StrokeDashArray = dashArray;
                canvas.Children.Add(leftLine);
                group.MainElements.Add(leftLine);

                rightLine = new Line
                {
                    X1 = splitPt.X, Y1 = splitPt.Y, X2 = lineEnd.X, Y2 = lineEnd.Y,
                    StrokeThickness = 2, Stroke = mainBrush,
                    StrokeDashArray = new DoubleCollection { 7, 2 },
                };
                if (toolTip is not null) rightLine.ToolTip = toolTip;
                canvas.Children.Add(rightLine);
                group.MainElements.Add(rightLine);
            }
            else
            {
                mainLine = new Line
                {
                    X1 = from.X, Y1 = from.Y, X2 = lineEnd.X, Y2 = lineEnd.Y,
                    StrokeThickness = 2, Stroke = mainBrush,
                };
                if (dashArray is not null) mainLine.StrokeDashArray = dashArray;
                if (toolTip is not null)   mainLine.ToolTip = toolTip;
                canvas.Children.Add(mainLine);
                group.MainElements.Add(mainLine);
            }

            // Wide invisible hit-target so hovering near (but not pixel-perfect on) the line
            // still triggers the hover. Opacity must be > 0 for WPF hit testing to work.
            // Use CardSurface so the hit-target blends with the canvas in both themes.
            var hitLine = new Line
            {
                X1 = from.X, Y1 = from.Y, X2 = lineEnd.X, Y2 = lineEnd.Y,
                StrokeThickness = 12, Opacity = 0.01,
            };
            hitLine.SetResourceReference(Shape.StrokeProperty, "CardSurface");
            if (toolTip is not null) hitLine.ToolTip = toolTip;
            canvas.Children.Add(hitLine);
            group.MainElements.Add(hitLine);

            Polygon? glowArrow = null;
            Polygon? mainArrow = null;
            if (arrowHead)
            {
                // Glow arrowhead: same tip, wider wings.
                var perp = new Vector(0, 1);
                glowArrow = new Polygon
                {
                    Fill       = glowBrush,
                    Visibility = Visibility.Hidden,
                    Points     = [to, lineEnd + perp * glowArrowHalf, lineEnd - perp * glowArrowHalf],
                };
                canvas.Children.Add(glowArrow);
                group.GlowElements.Add(glowArrow);

                // Main arrowhead.
                mainArrow = new Polygon
                {
                    Fill   = mainBrush,
                    Points = [to, lineEnd + perp * arrowHalfWidth, lineEnd - perp * arrowHalfWidth],
                };
                canvas.Children.Add(mainArrow);
                group.MainElements.Add(mainArrow);
            }

            group.InitializeEndpoints(from, to, (updatedFrom, updatedTo) =>
            {
                var updatedLineEnd = arrowHead
                    ? new Point(updatedTo.X - arrowLength, updatedTo.Y)
                    : updatedTo;
                glowLine.X1 = updatedFrom.X;
                glowLine.Y1 = updatedFrom.Y;
                glowLine.X2 = updatedLineEnd.X;
                glowLine.Y2 = updatedLineEnd.Y;
                if (doLineSplit && leftLine is not null && rightLine is not null)
                {
                    double tSplit = (splitAtX - updatedFrom.X) / (updatedLineEnd.X - updatedFrom.X);
                    double splitY = updatedFrom.Y + tSplit * (updatedLineEnd.Y - updatedFrom.Y);
                    var splitPt = new Point(splitAtX, splitY);
                    leftLine.X1 = updatedFrom.X;
                    leftLine.Y1 = updatedFrom.Y;
                    leftLine.X2 = splitPt.X;
                    leftLine.Y2 = splitPt.Y;
                    rightLine.X1 = splitPt.X;
                    rightLine.Y1 = splitPt.Y;
                    rightLine.X2 = updatedLineEnd.X;
                    rightLine.Y2 = updatedLineEnd.Y;
                }
                else if (mainLine is not null)
                {
                    mainLine.X1 = updatedFrom.X;
                    mainLine.Y1 = updatedFrom.Y;
                    mainLine.X2 = updatedLineEnd.X;
                    mainLine.Y2 = updatedLineEnd.Y;
                }

                hitLine.X1 = updatedFrom.X;
                hitLine.Y1 = updatedFrom.Y;
                hitLine.X2 = updatedLineEnd.X;
                hitLine.Y2 = updatedLineEnd.Y;

                if (arrowHead && glowArrow is not null && mainArrow is not null)
                {
                    var perp = new Vector(0, 1);
                    glowArrow.Points = [updatedTo, updatedLineEnd + perp * glowArrowHalf, updatedLineEnd - perp * glowArrowHalf];
                    mainArrow.Points = [updatedTo, updatedLineEnd + perp * arrowHalfWidth, updatedLineEnd - perp * arrowHalfWidth];
                }
            });
        }
        else
        {
            // S-curve Bézier with horizontal tangents at both endpoints.
            double dx        = lineEnd.X - from.X;
            // Keep both control points within the segment's horizontal bounds. The previous
            // minimum 40px handle made short detour segments overshoot and curl backward.
            double handleLen = Math.Max(0, dx * 0.5);
            var cp1 = new Point(from.X    + handleLen, from.Y);
            var cp2 = new Point(lineEnd.X - handleLen, lineEnd.Y);

            PathGeometry MakeBezierGeometry()
            {
                var fig = new PathFigure { StartPoint = from };
                fig.Segments.Add(new BezierSegment(cp1, cp2, lineEnd, isStroked: true));
                var geo = new PathGeometry();
                geo.Figures.Add(fig);
                return geo;
            }

            var glowPath = new Path
            {
                Data = MakeBezierGeometry(), StrokeThickness = glowThickness, Stroke = glowBrush,
                Fill = Brushes.Transparent,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Visibility = Visibility.Hidden,
            };
            if (dashArray is not null) glowPath.StrokeDashArray = null; // glow is always solid
            canvas.Children.Add(glowPath);
            group.GlowElements.Add(glowPath);

            // Main path — split at a locked milestone boundary if one crosses this segment.
            bool doBezSplit = !double.IsNaN(splitAtX) && splitAtX > from.X + 1.0 && splitAtX < lineEnd.X - 1.0;
            Path? leftPath = null;
            Path? rightPath = null;
            Path? mainPath = null;
            if (doBezSplit)
            {
                // Binary search for t where x(t) = splitAtX (monotone for left-to-right S-curves).
                double lo = 0.0, hi = 1.0;
                for (var i = 0; i < 50; i++)
                {
                    double mid = (lo + hi) * 0.5;
                    if (BezierX(from, cp1, cp2, lineEnd, mid) < splitAtX) lo = mid; else hi = mid;
                }
                SplitBezier(from, cp1, cp2, lineEnd, (lo + hi) * 0.5,
                    out var lp0, out var lp1, out var lp2, out var lp3,
                    out var rp0, out var rp1, out var rp2, out var rp3);

                var leftFig = new PathFigure { StartPoint = lp0 };
                leftFig.Segments.Add(new BezierSegment(lp1, lp2, lp3, isStroked: true));
                var leftGeo  = new PathGeometry(); leftGeo.Figures.Add(leftFig);
                leftPath = new Path { Data = leftGeo, StrokeThickness = 2, Stroke = mainBrush, Fill = Brushes.Transparent };
                if (dashArray is not null) leftPath.StrokeDashArray = dashArray;
                canvas.Children.Add(leftPath);
                group.MainElements.Add(leftPath);

                var rightFig = new PathFigure { StartPoint = rp0 };
                rightFig.Segments.Add(new BezierSegment(rp1, rp2, rp3, isStroked: true));
                var rightGeo  = new PathGeometry(); rightGeo.Figures.Add(rightFig);
                rightPath = new Path
                {
                    Data = rightGeo, StrokeThickness = 2, Stroke = mainBrush, Fill = Brushes.Transparent,
                    StrokeDashArray = new DoubleCollection { 7, 2 },
                };
                if (toolTip is not null) rightPath.ToolTip = toolTip;
                canvas.Children.Add(rightPath);
                group.MainElements.Add(rightPath);
            }
            else
            {
                mainPath = new Path
                {
                    Data = MakeBezierGeometry(), StrokeThickness = 2, Stroke = mainBrush,
                    Fill = Brushes.Transparent,
                };
                if (dashArray is not null) mainPath.StrokeDashArray = dashArray;
                if (toolTip is not null)   mainPath.ToolTip = toolTip;
                canvas.Children.Add(mainPath);
                group.MainElements.Add(mainPath);
            }

            // Wide invisible hit-target for the Bézier — same path, much thicker, nearly invisible.
            // Use CardSurface so the hit-target blends with the canvas in both themes.
            var hitPath = new Path
            {
                Data = MakeBezierGeometry(), StrokeThickness = 12,
                Fill = Brushes.Transparent, Opacity = 0.01,
            };
            hitPath.SetResourceReference(Shape.StrokeProperty, "CardSurface");
            if (toolTip is not null) hitPath.ToolTip = toolTip;
            canvas.Children.Add(hitPath);
            group.MainElements.Add(hitPath);

            Polygon? glowArrow = null;
            Polygon? mainArrow = null;
            if (arrowHead)
            {
                // Glow arrowhead: same tip, wider wings.
                var perp = new Vector(0, 1);
                glowArrow = new Polygon
                {
                    Fill       = glowBrush,
                    Visibility = Visibility.Hidden,
                    Points     = [to, lineEnd + perp * glowArrowHalf, lineEnd - perp * glowArrowHalf],
                };
                canvas.Children.Add(glowArrow);
                group.GlowElements.Add(glowArrow);

                // Main arrowhead.
                mainArrow = new Polygon
                {
                    Fill   = mainBrush,
                    Points = [to, lineEnd + perp * arrowHalfWidth, lineEnd - perp * arrowHalfWidth],
                };
                canvas.Children.Add(mainArrow);
                group.MainElements.Add(mainArrow);
            }

            group.InitializeEndpoints(from, to, (updatedFrom, updatedTo) =>
            {
                var updatedLineEnd = arrowHead
                    ? new Point(updatedTo.X - arrowLength, updatedTo.Y)
                    : updatedTo;
                var updatedDx = updatedLineEnd.X - updatedFrom.X;
                var updatedHandleLen = Math.Max(0, updatedDx * 0.5);
                var updatedCp1 = new Point(updatedFrom.X + updatedHandleLen, updatedFrom.Y);
                var updatedCp2 = new Point(updatedLineEnd.X - updatedHandleLen, updatedLineEnd.Y);
                PathGeometry MakeUpdatedBezierGeometry()
                {
                    var fig = new PathFigure { StartPoint = updatedFrom };
                    fig.Segments.Add(new BezierSegment(updatedCp1, updatedCp2, updatedLineEnd, isStroked: true));
                    var geo = new PathGeometry();
                    geo.Figures.Add(fig);
                    return geo;
                }

                glowPath.Data = MakeUpdatedBezierGeometry();
                if (doBezSplit && leftPath is not null && rightPath is not null)
                {
                    double lo = 0.0, hi = 1.0;
                    for (var index = 0; index < 50; index++)
                    {
                        var mid = (lo + hi) * 0.5;
                        if (BezierX(updatedFrom, updatedCp1, updatedCp2, updatedLineEnd, mid) < splitAtX) lo = mid; else hi = mid;
                    }
                    SplitBezier(updatedFrom, updatedCp1, updatedCp2, updatedLineEnd, (lo + hi) * 0.5,
                        out var lp0, out var lp1, out var lp2, out var lp3,
                        out var rp0, out var rp1, out var rp2, out var rp3);
                    var leftFig = new PathFigure { StartPoint = lp0 };
                    leftFig.Segments.Add(new BezierSegment(lp1, lp2, lp3, isStroked: true));
                    var leftGeo = new PathGeometry();
                    leftGeo.Figures.Add(leftFig);
                    leftPath.Data = leftGeo;
                    var rightFig = new PathFigure { StartPoint = rp0 };
                    rightFig.Segments.Add(new BezierSegment(rp1, rp2, rp3, isStroked: true));
                    var rightGeo = new PathGeometry();
                    rightGeo.Figures.Add(rightFig);
                    rightPath.Data = rightGeo;
                }
                else if (mainPath is not null)
                {
                    mainPath.Data = MakeUpdatedBezierGeometry();
                }

                hitPath.Data = MakeUpdatedBezierGeometry();
                if (arrowHead && glowArrow is not null && mainArrow is not null)
                {
                    var perp = new Vector(0, 1);
                    glowArrow.Points = [updatedTo, updatedLineEnd + perp * glowArrowHalf, updatedLineEnd - perp * glowArrowHalf];
                    mainArrow.Points = [updatedTo, updatedLineEnd + perp * arrowHalfWidth, updatedLineEnd - perp * arrowHalfWidth];
                }
            });
        }

        return group;
    }

    private static ConnectorGroup AddRoundedConnectorRoute(
        Canvas canvas,
        IReadOnlyList<(double X, double Y)> route,
        bool arrowHead,
        int skipCount = 0,
        bool dashed = false,
        string? toolTip = null)
    {
        var group = new ConnectorGroup { SkipCount = skipCount };
        if (route.Count < 2) return group;

        const double arrowLength = 11;
        const double arrowHalfWidth = 5;
        const double glowThickness = 8;
        const double glowArrowHalf = 10;

        var drawableRoute = route.ToArray();
        var target = new Point(drawableRoute[^1].X, drawableRoute[^1].Y);
        if (arrowHead)
            drawableRoute[^1] = (drawableRoute[^1].X - arrowLength, drawableRoute[^1].Y);

        PathGeometry MakeGeometry(IReadOnlyList<(double X, double Y)> geometryRoute)
        {
            var corners = ValidationShieldPresenter.ComputeRoundedRouteCorners(geometryRoute)
                .ToDictionary(corner => corner.PointIndex);
            var figure = new PathFigure
            {
                StartPoint = new Point(geometryRoute[0].X, geometryRoute[0].Y),
            };
            for (var index = 1; index < geometryRoute.Count - 1; index++)
            {
                if (!corners.TryGetValue(index, out var corner)) continue;
                figure.Segments.Add(new LineSegment(
                    new Point(corner.Entry.X, corner.Entry.Y), isStroked: true));
                figure.Segments.Add(new QuadraticBezierSegment(
                    new Point(corner.Control.X, corner.Control.Y),
                    new Point(corner.Exit.X, corner.Exit.Y),
                    isStroked: true));
            }
            figure.Segments.Add(new LineSegment(
                new Point(geometryRoute[^1].X, geometryRoute[^1].Y), isStroked: true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        var color = ConnectorColor(skipCount);
        var glowColor = ConnectorGlowColor(skipCount);
        var mainBrush = new SolidColorBrush(color);
        var glowBrush = new SolidColorBrush(glowColor);
        var glowPath = new Path
        {
            Data = MakeGeometry(drawableRoute),
            StrokeThickness = glowThickness,
            Stroke = glowBrush,
            Fill = Brushes.Transparent,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Visibility = Visibility.Hidden,
        };
        canvas.Children.Add(glowPath);
        group.GlowElements.Add(glowPath);

        var mainPath = new Path
        {
            Data = MakeGeometry(drawableRoute),
            StrokeThickness = 2,
            Stroke = mainBrush,
            Fill = Brushes.Transparent,
        };
        if (dashed) mainPath.StrokeDashArray = new DoubleCollection { 7, 2 };
        if (toolTip is not null) mainPath.ToolTip = toolTip;
        canvas.Children.Add(mainPath);
        group.MainElements.Add(mainPath);

        var hitPath = new Path
        {
            Data = MakeGeometry(drawableRoute),
            StrokeThickness = 12,
            Fill = Brushes.Transparent,
            Opacity = 0.01,
        };
        hitPath.SetResourceReference(Shape.StrokeProperty, "CardSurface");
        if (toolTip is not null) hitPath.ToolTip = toolTip;
        canvas.Children.Add(hitPath);
        group.MainElements.Add(hitPath);

        Polygon? glowArrow = null;
        Polygon? mainArrow = null;
        if (arrowHead)
        {
            var lineEnd = new Point(drawableRoute[^1].X, drawableRoute[^1].Y);
            var perpendicular = new Vector(0, 1);
            glowArrow = new Polygon
            {
                Fill = glowBrush,
                Visibility = Visibility.Hidden,
                Points = [target, lineEnd + perpendicular * glowArrowHalf, lineEnd - perpendicular * glowArrowHalf],
            };
            canvas.Children.Add(glowArrow);
            group.GlowElements.Add(glowArrow);

            mainArrow = new Polygon
            {
                Fill = mainBrush,
                Points = [target, lineEnd + perpendicular * arrowHalfWidth, lineEnd - perpendicular * arrowHalfWidth],
            };
            canvas.Children.Add(mainArrow);
            group.MainElements.Add(mainArrow);
        }

        var baseRoute = route.ToArray();
        group.InitializeEndpoints(
            new Point(baseRoute[0].X, baseRoute[0].Y),
            new Point(baseRoute[^1].X, baseRoute[^1].Y),
            (fromPoint, toPoint) =>
            {
                var adjustedRoute = baseRoute.ToArray();
                adjustedRoute[0] = (fromPoint.X, fromPoint.Y);
                adjustedRoute[^1] = (toPoint.X, toPoint.Y);
                var adjustedDrawableRoute = adjustedRoute.ToArray();
                var adjustedTarget = new Point(adjustedDrawableRoute[^1].X, adjustedDrawableRoute[^1].Y);
                if (arrowHead)
                    adjustedDrawableRoute[^1] = (adjustedDrawableRoute[^1].X - arrowLength, adjustedDrawableRoute[^1].Y);

                var geometry = MakeGeometry(adjustedDrawableRoute);
                glowPath.Data = geometry;
                mainPath.Data = geometry.Clone();
                hitPath.Data = geometry.Clone();
                if (arrowHead && glowArrow is not null && mainArrow is not null)
                {
                    var adjustedLineEnd = new Point(adjustedDrawableRoute[^1].X, adjustedDrawableRoute[^1].Y);
                    var perpendicular = new Vector(0, 1);
                    glowArrow.Points = [adjustedTarget, adjustedLineEnd + perpendicular * glowArrowHalf, adjustedLineEnd - perpendicular * glowArrowHalf];
                    mainArrow.Points = [adjustedTarget, adjustedLineEnd + perpendicular * arrowHalfWidth, adjustedLineEnd - perpendicular * arrowHalfWidth];
                }
            });

        return group;
    }

    // Base hue for adjacent-stage connectors. Each skipped stage rotates the hue by 45°.
    private const double ConnectorBaseHue   = 210.0;
    private const double ConnectorSaturation = 0.70;
    private const double ConnectorLightness  = 0.45;

    private static Color ConnectorColor(int skipCount)
    {
        var hue = (ConnectorBaseHue + skipCount * 45.0) % 360.0;
        return HslToRgb(hue, ConnectorSaturation, ConnectorLightness);
    }

    // Theme-polarity variant of the connector color — the glow halo shown on hover.
    // Light theme: brighten toward white. Dark theme: darken toward black.
    private static Color ConnectorGlowColor(int skipCount)
    {
        var hue = (ConnectorBaseHue + skipCount * 45.0) % 360.0;
        return AgentStatusCard.IsDarkTheme
            ? HslToRgb(hue, 0.95, 0.20)
            : HslToRgb(hue, 0.95, 0.88);
    }

    // Drop-shadow glow applied to a task node Border on hover.
    private static DropShadowEffect TaskNodeGlowEffect() => new()
    {
        Color       = HslToRgb(ConnectorBaseHue, 0.90, 0.70),
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

    // Evaluates the X coordinate of the cubic Bézier at parameter t.
    private static double BezierX(Point p0, Point p1, Point p2, Point p3, double t)
    {
        double mt = 1.0 - t;
        return mt * mt * mt * p0.X + 3 * mt * mt * t * p1.X + 3 * mt * t * t * p2.X + t * t * t * p3.X;
    }

    // De Casteljau split: divides the Bézier [p0,p1,p2,p3] at parameter t into two sub-curves.
    private static void SplitBezier(Point p0, Point p1, Point p2, Point p3, double t,
        out Point lp0, out Point lp1, out Point lp2, out Point lp3,
        out Point rp0, out Point rp1, out Point rp2, out Point rp3)
    {
        static Point Lerp(Point a, Point b, double f) => new(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f);
        var A = Lerp(p0, p1, t); var B = Lerp(p1, p2, t); var C = Lerp(p2, p3, t);
        var D = Lerp(A,  B,  t); var E = Lerp(B,  C,  t); var F = Lerp(D,  E,  t);
        lp0 = p0; lp1 = A; lp2 = D; lp3 = F;
        rp0 = F;  rp1 = E; rp2 = C; rp3 = p3;
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
            _selectedElement = identity;
            ApplySelectionVisual();
        }
        RefreshDetailPanel();
    }

    // Applies selection indicator: border doubling for task/gate, glow-behind for others.
    // Always clears previous selection first to ensure single-selection invariant.
    private void ApplySelectionVisual()
    {
        ClearSelectionVisual();
        if (_selectedElement is null || _graphCanvas is null)
            return;

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
            _selectionBorderState = (border, border.BorderThickness);
            border.BorderThickness = new Thickness(
                border.BorderThickness.Left * 2,
                border.BorderThickness.Top * 2,
                border.BorderThickness.Right * 2,
                border.BorderThickness.Bottom * 2);
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
        }
    }

    // Restores previous element to its default visual state.
    private void ClearSelectionVisual()
    {
        if (_selectionBorderState is var (border, original))
        {
            border.BorderThickness = original;
            _selectionBorderState = null;
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

        if (_isInspectorEditing && TryPopulateStagedEditor())
        {
            PrependInspectorToolbar();
            return;
        }

        if (_selectedElement is null)
        {
            _detailDocument.Blocks.Clear();
            var placeholder = new Paragraph(new Run("Select an element to view details."));
            placeholder.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
            placeholder.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            _detailDocument.Blocks.Add(placeholder);
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
                            new PlanAgentAssignment(a.AgentHandle, a.Role, a.AllowGenericChildren)).ToList());
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
                    var decomposedGates = _plan?.Group.ApprovalGates;
                    if (decomposedGates is not null && gateIndex >= 0 && gateIndex < decomposedGates.Count)
                    {
                        var dg = decomposedGates[gateIndex];
                        var minimalGate = new PlanApprovalGate(
                            dg.GateId, dg.Message, dg.AfterTaskIds ?? [], dg.BeforeTaskIds ?? [],
                            PlanGateStatus.Pending, Question: dg.Question);
                        PopulateGateDetail(minimalGate, null);
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
        if (_detailDocument is null) return;
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };

        Button AddButton(string label, Action click, string? toolTip = null)
        {
            var button = TranscriptQuickReplyFactory.CreateButton(
                label, _quickReplyFontSize,
                toolTip: toolTip is null ? null : ToolTipHelper.MakeThemedToolTip(toolTip));
            button.Margin = new Thickness(0, 0, 6, 4);
            button.Click += (_, _) => click();
            toolbar.Children.Add(button);
            return button;
        }

        if (CanEditSelectedElement())
        {
            AddButton(_isInspectorEditing ? "Cancel edit" : "Edit", () =>
            {
                _isInspectorEditing = !_isInspectorEditing;
                RefreshDetailPanel();
            }, "Edit this staged-plan element in the inspector.");
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
            toolbar.Children.Add(locked);
        }
        if (_editHistory is not null)
        {
            var undo = AddButton("Undo", UndoStagedEdit, "Undo the most recent plan edit.");
            undo.IsEnabled = CanApplyHistoryCandidate(_editHistory.PeekUndo, out var undoError);
            var redo = AddButton("Redo", RedoStagedEdit, "Redo the most recently undone plan edit.");
            redo.IsEnabled = CanApplyHistoryCandidate(_editHistory.PeekRedo, out var redoError);
            var historyError = undoError ?? redoError;
            if (!string.IsNullOrWhiteSpace(historyError))
            {
                var status = new TextBlock
                {
                    Text = "Undo/redo is blocked at the execution boundary. " + historyError,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 0, 4),
                };
                status.SetResourceReference(TextBlock.ForegroundProperty, "PriorityHigh");
                status.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
                toolbar.Children.Add(status);
            }
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
                toolbar.Children.Add(spinner);
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
                toolbar.Children.Add(status);
            }
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
            toolbar.Children.Add(count);
        }

        var container = new BlockUIContainer(toolbar) { Margin = new Thickness(0) };
        if (_detailDocument.Blocks.FirstBlock is { } first)
            _detailDocument.Blocks.InsertBefore(first, container);
        else
            _detailDocument.Blocks.Add(container);
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
        _review = review;
        _reviewIndex = review.Annotations.Count > 0 ? 0 : -1;
        if (_reviewIndex >= 0)
            NavigateReview(_reviewIndex);
        else
            RefreshDetailPanel();
    }

    internal void SetReviewRequestError(string error)
    {
        _isAiReviewPending = false;
        _reviewStatusMessage = error;
        RefreshDetailPanel();
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
        var branch = AddEditorField(panel, "Branch", _plan.Group.Branch, 34);
        var summary = AddEditorField(panel, "Summary", _plan.Group.Summary, 140);
        var delivery = AddEditorField(panel, "Delivery", _plan.Group.Delivery, 70);
        AddEditorActions(panel, status, () =>
        {
            if (string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(branch.Text) ||
                string.IsNullOrWhiteSpace(summary.Text))
                return "Plan title, branch, and summary are required.";
            ApplyStagedMutation(StagedPlanEditor.UpdatePlan(
                _plan.Group, title.Text, branch.Text, summary.Text, delivery.Text));
            return null;
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
        var description = AddEditorField(panel, "Description", task.Description, 170);
        var priority = AddEditorField(panel, "Priority", task.Priority, 34);
        var dependencies = AddEditorField(
            panel, "Dependencies (IDs separated by commas or lines)",
            string.Join(Environment.NewLine, task.DependsOn), 70);
        var routingMode = AddEditorField(panel, "Agent routing mode", task.AgentRoutingMode, 34);
        var assignments = AddEditorField(
            panel,
            "Agent assignments (handle | role | allow generic children)",
            string.Join(Environment.NewLine, (task.AgentAssignments ?? []).Select(value =>
                $"{value.AgentHandle} | {value.Role} | {value.AllowGenericChildren}")),
            80);
        var genericReason = AddEditorField(panel, "Generic-agent reason", task.GenericAgentReason, 55);
        var executionMode = AddEditorField(panel, "Execution mode", task.ExecutionMode, 34);
        var parallel = new CheckBox
        {
            Content = "Parallel eligible (indeterminate means unspecified)",
            IsThreeState = true,
            IsChecked = task.ParallelEligible,
            Margin = new Thickness(0, 8, 0, 0),
        };
        parallel.SetResourceReference(CheckBox.ForegroundProperty, "BodyText");
        panel.Children.Add(parallel);
        AddEditorActions(panel, status, () =>
        {
            if (string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(description.Text))
                return "Title and description are required.";
            if (!TryParseAgentAssignments(assignments.Text, out var parsedAssignments, out var assignmentError))
                return assignmentError;
            var ids = ParseEditorList(dependencies.Text);
            var updated = StagedPlanEditor.UpdateTask(
                _plan!.Group, taskId, title.Text, description.Text, priority.Text, ids);
            updated = StagedPlanEditor.UpdateTaskAdvanced(
                updated, taskId, parsedAssignments, routingMode.Text, parallel.IsChecked,
                genericReason.Text, executionMode.Text);
            ApplyStagedMutation(updated);
            return null;
        });
        _detailDocument.Blocks.Add(new BlockUIContainer(panel));
        return true;
    }

    private bool PopulateValidationEditor(string validationId)
    {
        var validation = _plan?.Group.Validations?.FirstOrDefault(value =>
            value.ValidationId == validationId);
        if (validation is null || _detailDocument is null) return false;
        _detailDocument.Blocks.Clear();
        var panel = CreateEditorPanel("Edit validation", out var status);
        var title = AddEditorField(panel, "Title", validation.Title, 34);
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
            ApplyStagedMutation(updatedGroup);
            return null;
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
        var question = AddEditorField(panel, "Approval question", gate.Question, 75);
        var after = AddEditorField(panel, "After step IDs", string.Join(Environment.NewLine, gate.AfterTaskIds ?? []), 64);
        var before = AddEditorField(panel, "Before step IDs", string.Join(Environment.NewLine, gate.BeforeTaskIds ?? []), 64);
        var proofs = AddEditorField(
            panel,
            "Proof requirements (ID | type | description | question)",
            string.Join(Environment.NewLine, (gate.ProofRequirements ?? []).Select(value =>
                $"{value.RequirementId} | {value.ProofType} | {value.Description} | {value.Question}")),
            100);
        var gateId = gate.GateId;
        AddEditorActions(panel, status, () =>
        {
            if (string.IsNullOrWhiteSpace(message.Text)) return "Message is required.";
            if (!TryParseProofRequirements(proofs.Text, out var parsedProofs, out var proofError))
                return proofError;
            ApplyStagedMutation(StagedPlanEditor.UpdateGate(
                _plan!.Group, gateId, message.Text, question.Text,
                ParseEditorList(after.Text), ParseEditorList(before.Text), parsedProofs));
            return null;
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
        editor.SetResourceReference(TextBox.ForegroundProperty, "BodyText");
        editor.SetResourceReference(TextBox.BackgroundProperty, "PanelBackground");
        editor.SetResourceReference(TextBox.BorderBrushProperty, "LineColor");
        editor.SetResourceReference(TextBox.FontSizeProperty, "FontSizeBody");
        panel.Children.Add(label);
        panel.Children.Add(editor);
        return editor;
    }

    private void AddEditorActions(StackPanel panel, TextBlock status, Func<string?> save)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var saveButton = TranscriptQuickReplyFactory.CreateButton("Save changes", _quickReplyFontSize);
        var cancelButton = TranscriptQuickReplyFactory.CreateButton("Cancel", _quickReplyFontSize);
        cancelButton.Margin = new Thickness(6, 0, 0, 0);
        saveButton.Click += (_, _) =>
        {
            var error = save();
            if (error is not null) status.Text = error;
        };
        cancelButton.Click += (_, _) =>
        {
            _isInspectorEditing = false;
            RefreshDetailPanel();
        };
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);
        panel.Children.Add(buttons);
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

    private static bool TryParseAgentAssignments(
        string value,
        out IReadOnlyList<DecomposedAgentAssignment> assignments,
        out string? error)
    {
        var result = new List<DecomposedAgentAssignment>();
        foreach (var line in ParseEditorLines(value))
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length is < 2 or > 3 || string.IsNullOrWhiteSpace(parts[0]) ||
                string.IsNullOrWhiteSpace(parts[1]) ||
                (parts.Length == 3 && !bool.TryParse(parts[2], out _)))
            {
                assignments = [];
                error = "Each assignment must be: handle | role | true-or-false.";
                return false;
            }
            result.Add(new DecomposedAgentAssignment(
                parts[0], parts[1], parts.Length < 3 || bool.Parse(parts[2])));
        }
        assignments = result;
        error = null;
        return true;
    }

    private static bool TryParseProofRequirements(
        string value,
        out IReadOnlyList<DecomposedTaskProofRequirement> requirements,
        out string? error)
    {
        var result = new List<DecomposedTaskProofRequirement>();
        foreach (var line in ParseEditorLines(value))
        {
            var parts = line.Split('|', 4, StringSplitOptions.TrimEntries);
            if (parts.Length < 3 || parts.Take(3).Any(string.IsNullOrWhiteSpace))
            {
                requirements = [];
                error = "Each proof requirement must be: ID | type | description | optional question.";
                return false;
            }
            result.Add(new DecomposedTaskProofRequirement(
                parts[0], parts[1], parts[2],
                parts.Length == 4 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3] : null));
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
        titlePara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
        titlePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeLarge");
        titlePara.Margin = new Thickness(0, 0, 0, 4);
        _detailDocument.Blocks.Add(titlePara);

        // 2. Status with color-coded indicator
        var statusColorKey = GetTaskStatusColorKey(task.Status);
        var statusRun = new Run($"● {FormatTaskStatus(task.Status)}");
        statusRun.SetResourceReference(TextElement.ForegroundProperty, statusColorKey);
        var statusPara = new Paragraph(statusRun);
        statusPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        statusPara.Margin = new Thickness(0, 0, 0, 8);
        _detailDocument.Blocks.Add(statusPara);

        // 3. Description
        if (!string.IsNullOrWhiteSpace(task.Description))
        {
            var descPara = new Paragraph(new Run(task.Description));
            descPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            descPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            descPara.Margin = new Thickness(0, 0, 0, 12);
            _detailDocument.Blocks.Add(descPara);
        }

        if (plan is not null)
        {
            AppendApprovalGuidance(plan.ApprovalGates.Where(gate =>
                gate.AfterTaskIds.Contains(task.TaskId, StringComparer.Ordinal) ||
                gate.BeforeTaskIds.Contains(task.TaskId, StringComparer.Ordinal)));
        }

        // 4. Dependencies
        if (task.DependsOn is { Count: > 0 })
        {
            AddSectionHeader("Dependencies");
            var depList = new List();
            depList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            depList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            depList.MarkerStyle = TextMarkerStyle.Disc;
            depList.Margin = new Thickness(0, 0, 0, 12);
            foreach (var depId in task.DependsOn)
            {
                var depTask = plan?.Tasks?.FirstOrDefault(t => t.TaskId == depId);
                var depTitle = depTask?.Title ?? depTask?.Description ?? depId;
                var depStatus = depTask is not null ? $" ({FormatTaskStatus(depTask.Status)})" : "";
                var item = new ListItem(new Paragraph(new Run($"{depTitle}{depStatus}")));
                depList.ListItems.Add(item);
            }
            _detailDocument.Blocks.Add(depList);
        }

        // 5. Agent Assignments
        if (task.AgentAssignments is { Count: > 0 })
        {
            AddSectionHeader("Agent Assignments");
            var agentList = new List();
            agentList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            agentList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            agentList.MarkerStyle = TextMarkerStyle.None;
            agentList.Margin = new Thickness(0, 0, 0, 12);
            foreach (var assignment in task.AgentAssignments)
            {
                var agentPara = new Paragraph();
                var handleRun = new Run(assignment.AgentHandle) { FontWeight = FontWeights.SemiBold };
                handleRun.SetResourceReference(TextElement.ForegroundProperty, "LabelText");
                agentPara.Inlines.Add(handleRun);
                agentPara.Inlines.Add(new Run($" — {assignment.Role}"));
                agentList.ListItems.Add(new ListItem(agentPara));
            }
            _detailDocument.Blocks.Add(agentList);
        }

        // 6. Completion Summary
        if (!string.IsNullOrWhiteSpace(task.CompletionSummary))
        {
            AddSectionHeader("Completion Summary");
            var summaryPara = new Paragraph(new Run(task.CompletionSummary));
            summaryPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            summaryPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            summaryPara.Margin = new Thickness(0, 0, 0, 12);
            _detailDocument.Blocks.Add(summaryPara);
        }

        // 7. Commit evidence
        var detailEvidence = _durablePlan is null
            ? Array.Empty<PlanEvidenceCommit>()
            : PlanRecoveryPresentationBuilder.ResolveTaskEvidence(_durablePlan, task).ToArray();
        if (detailEvidence.Length > 0)
        {
            var commits = detailEvidence;
            AddSectionHeader(commits.Length > 1 ? "Commits" : "Commit");
            var label = commits.Length > 1 ? $"{commits.Length} commits" : commits[0].Commit;
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

        // 8. Handoff
        if (task.Handoff is { } handoff)
        {
            AddSectionHeader("Handoff");

            var handoffCommitPara = new Paragraph();
            handoffCommitPara.Inlines.Add(new Run("Commit: ") { FontWeight = FontWeights.SemiBold });
            handoffCommitPara.Inlines.Add(new Run(handoff.Commit) { FontFamily = new FontFamily("Consolas") });
            handoffCommitPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            handoffCommitPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            _detailDocument.Blocks.Add(handoffCommitPara);

            if (!string.IsNullOrWhiteSpace(handoff.Summary))
            {
                var hSummaryPara = new Paragraph(new Run(handoff.Summary));
                hSummaryPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                hSummaryPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                _detailDocument.Blocks.Add(hSummaryPara);
            }

            if (handoff.ChangedFiles is { Count: > 0 })
            {
                var filesPara = new Paragraph(new Run("Changed Files:") { FontWeight = FontWeights.SemiBold });
                filesPara.SetResourceReference(TextElement.ForegroundProperty, "LabelText");
                filesPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
                _detailDocument.Blocks.Add(filesPara);

                var fileList = new List();
                fileList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                fileList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
                fileList.MarkerStyle = TextMarkerStyle.None;
                foreach (var file in handoff.ChangedFiles)
                    fileList.ListItems.Add(new ListItem(new Paragraph(new Run(file) { FontFamily = new FontFamily("Consolas") })));
                _detailDocument.Blocks.Add(fileList);
            }

            if (handoff.Verification is { } ver)
            {
                var verPara = new Paragraph();
                verPara.Inlines.Add(new Run("Verification: ") { FontWeight = FontWeights.SemiBold });
                verPara.Inlines.Add(new Run(ver.Status ?? "unknown"));
                verPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                verPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                _detailDocument.Blocks.Add(verPara);
            }

            if (handoff.DeferredWork is { Count: > 0 })
            {
                var deferHeader = new Paragraph(new Run("Deferred Work:") { FontWeight = FontWeights.SemiBold });
                deferHeader.SetResourceReference(TextElement.ForegroundProperty, "LabelText");
                deferHeader.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                _detailDocument.Blocks.Add(deferHeader);

                var deferList = new List();
                deferList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
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

            // Spacing after handoff section
            var handoffSpacer = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };
            _detailDocument.Blocks.Add(handoffSpacer);
        }

        // 9. Verification History
        if (task.VerificationHistory is { Count: > 0 })
        {
            AddSectionHeader("Verification History");
            foreach (var report in task.VerificationHistory)
            {
                var verdictPara = new Paragraph();
                var verdictColorKey = report.Verdict.Equals("accepted", StringComparison.OrdinalIgnoreCase)
                    ? "PriorityLow" : "PriorityHigh";
                var verdictRun = new Run($"● {report.Verdict}") { FontWeight = FontWeights.SemiBold };
                verdictRun.SetResourceReference(TextElement.ForegroundProperty, verdictColorKey);
                verdictPara.Inlines.Add(verdictRun);
                verdictPara.Inlines.Add(new Run($"  ({report.CompletedAt:yyyy-MM-dd HH:mm})"));
                verdictPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
                _detailDocument.Blocks.Add(verdictPara);

                if (!string.IsNullOrWhiteSpace(report.Summary))
                {
                    var rSummaryPara = new Paragraph(new Run(report.Summary));
                    rSummaryPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                    rSummaryPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
                    rSummaryPara.Margin = new Thickness(8, 0, 0, 4);
                    _detailDocument.Blocks.Add(rSummaryPara);
                }

                if (report.ClaimFindings is { Count: > 0 })
                {
                    var findingList = new List();
                    findingList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
                    findingList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
                    findingList.MarkerStyle = TextMarkerStyle.None;
                    findingList.Margin = new Thickness(8, 0, 0, 8);
                    foreach (var finding in report.ClaimFindings)
                    {
                        var fPara = new Paragraph();
                        var dispositionColor = finding.Disposition.Equals("confirmed", StringComparison.OrdinalIgnoreCase)
                            ? "PriorityLow" : "PriorityMid";
                        var dispRun = new Run($"[{finding.Disposition}] ");
                        dispRun.SetResourceReference(TextElement.ForegroundProperty, dispositionColor);
                        fPara.Inlines.Add(dispRun);
                        fPara.Inlines.Add(new Run(finding.Claim));
                        findingList.ListItems.Add(new ListItem(fPara));
                    }
                    _detailDocument.Blocks.Add(findingList);
                }
            }
        }

        // 10. Proof Evidence
        if (task.ProofEvidence is { Count: > 0 })
        {
            AddSectionHeader("Proof Evidence");
            var proofList = new List();
            proofList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
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
        var statusColorKey = validation.Status switch
        {
            "passed" or "complete" => "PriorityLow",
            "failed" => "PriorityHigh",
            "executing" or "running" => "ActivePanelBorder",
            _ => "SubtleText",
        };
        var statusRun = new Run($"● {validation.Status ?? "unknown"}");
        statusRun.SetResourceReference(TextElement.ForegroundProperty, statusColorKey);
        var statusPara = new Paragraph(statusRun);
        statusPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        statusPara.Margin = new Thickness(0, 0, 0, 4);
        _detailDocument.Blocks.Add(statusPara);

        // Mode
        if (!string.IsNullOrWhiteSpace(validation.Mode))
        {
            var modePara = new Paragraph();
            modePara.Inlines.Add(new Run("Mode: ") { FontWeight = FontWeights.SemiBold });
            modePara.Inlines.Add(new Run(validation.Mode));
            modePara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            modePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            modePara.Margin = new Thickness(0, 0, 0, 8);
            _detailDocument.Blocks.Add(modePara);
        }

        // Description
        if (!string.IsNullOrWhiteSpace(validation.Description))
        {
            var descPara = new Paragraph(new Run(validation.Description));
            descPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            descPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            descPara.Margin = new Thickness(0, 0, 0, 12);
            _detailDocument.Blocks.Add(descPara);
        }

        // Assertions list
        if (validation.Assertions is { Count: > 0 })
        {
            AddSectionHeader("Assertions");
            var assertList = new List();
            assertList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            assertList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            assertList.MarkerStyle = TextMarkerStyle.None;
            assertList.Margin = new Thickness(0, 0, 0, 12);
            for (int i = 0; i < validation.Assertions.Count; i++)
            {
                var assertion = validation.Assertions[i];
                var hasEvidence = validation.Evidence is not null && i < validation.Evidence.Count;
                var indicator = hasEvidence
                    ? (validation.Evidence![i].StartsWith("pass", StringComparison.OrdinalIgnoreCase) ? "✓ " : "✗ ")
                    : "• ";
                var indicatorColor = indicator == "✓ " ? "PriorityLow" : indicator == "✗ " ? "PriorityHigh" : "SubtleText";
                var para = new Paragraph();
                var indicatorRun = new Run(indicator);
                indicatorRun.SetResourceReference(TextElement.ForegroundProperty, indicatorColor);
                para.Inlines.Add(indicatorRun);
                para.Inlines.Add(new Run(assertion));
                assertList.ListItems.Add(new ListItem(para));
            }
            _detailDocument.Blocks.Add(assertList);
        }

        // After Task IDs
        AddTaskIdList("After Steps", validation.AfterTaskIds, plan);

        // Before Task IDs
        AddTaskIdList("Before Steps", validation.BeforeTaskIds, plan);
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

        // Gate ID (monospace)
        var idPara = new Paragraph(new Run(gate.GateId) { FontFamily = new FontFamily("Consolas") });
        idPara.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
        idPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeSmall");
        idPara.Margin = new Thickness(0, 0, 0, 4);
        _detailDocument.Blocks.Add(idPara);

        // Message
        if (!string.IsNullOrWhiteSpace(gate.Message))
        {
            var msgPara = new Paragraph(new Run(gate.Message));
            msgPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            msgPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            msgPara.Margin = new Thickness(0, 0, 0, 8);
            _detailDocument.Blocks.Add(msgPara);
        }

        var approvalQuestions = ResolvePresentedHumanReviewQuestions(gate);
        if (approvalQuestions.Count > 0)
        {
            AddSectionHeader("Verify the Following:");
            var questionPara = new Paragraph(new Run("• " + string.Join("\n• ", approvalQuestions))
            {
                FontWeight = FontWeights.SemiBold,
            });
            questionPara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
            questionPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            questionPara.Margin = new Thickness(0, 0, 0, 12);
            _detailDocument.Blocks.Add(questionPara);
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

        // After Task IDs
        AddTaskIdList("After Steps", gate.AfterTaskIds, plan);

        // Before Task IDs
        AddTaskIdList("Before Steps", gate.BeforeTaskIds, plan);

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
        if (gate is null)
        {
            var fallback = new Paragraph(new Run("Approval gate not found."));
            fallback.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
            fallback.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            _detailDocument.Blocks.Add(fallback);
            return;
        }

        // Title
        var titlePara = new Paragraph(new Run("Human Approval Required") { FontWeight = FontWeights.Bold });
        titlePara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
        titlePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeLarge");
        titlePara.Margin = new Thickness(0, 0, 0, 4);
        _detailDocument.Blocks.Add(titlePara);

        // Question
        var approvalQuestion = PlanProofCapabilityPolicy.ResolveHumanQuestion(gate);
        if (!string.IsNullOrWhiteSpace(approvalQuestion))
        {
            var questionPara = new Paragraph(new Run(approvalQuestion)
            {
                FontWeight = FontWeights.SemiBold,
            });
            questionPara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
            questionPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            questionPara.Margin = new Thickness(0, 0, 0, 12);
            _detailDocument.Blocks.Add(questionPara);
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

        // After Tasks
        AddTaskIdList("After Steps", gate.AfterTaskIds, _durablePlan);

        // Before Tasks
        AddTaskIdList("Before Steps", gate.BeforeTaskIds, _durablePlan);

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
            editor.Children.Add(textBox);
            return textBox;
        }

        AddSectionHeader("Edit Review Guidance");
        var messageEditor = AddEditor("Message", gate.Message, 54);
        var questionEditor = AddEditor("Approval question", gate.Question, 54);

        var proofEditors = new System.Collections.Generic.List<(
            PlanTaskProofRequirement Requirement,
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
            var description = AddEditor(
                $"Human proof requirement{suffix} ({requirement.ProofType})",
                requirement.Description,
                48);
            var proofQuestion = AddEditor(
                $"Proof-specific question{suffix} (optional)",
                requirement.Question,
                42);
            proofEditors.Add((requirement, description, proofQuestion));
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
        list.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
        list.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        list.MarkerStyle = TextMarkerStyle.Disc;
        list.Margin = new Thickness(0, 0, 0, 12);
        foreach (var taskId in taskIds)
        {
            var task = plan?.Tasks?.FirstOrDefault(t => t.TaskId == taskId);
            var title = task?.Title ?? taskId;
            list.ListItems.Add(new ListItem(new Paragraph(new Run(title))));
        }
        _detailDocument.Blocks.Add(list);
    }

    private void PopulateAgentDetail(string agentHandle)
    {
        if (_detailDocument is null)
            return;

        _detailDocument.Blocks.Clear();

        // Agent image (upper-left)
        var avatarInfo = _resolveAgentAvatar?.Invoke(agentHandle);
        var avatarSize = 64.0 * _scaleFactor;
        var avatarElement = CreateAgentAvatarChip(avatarInfo, avatarSize, agentHandle);
        avatarElement.HorizontalAlignment = HorizontalAlignment.Left;
        _detailDocument.Blocks.Add(new BlockUIContainer(avatarElement) { Margin = new Thickness(0, 0, 0, 8) });

        // Agent name heading (below image)
        var titlePara = new Paragraph(new Run(agentHandle) { FontWeight = FontWeights.Bold });
        titlePara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
        titlePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeLarge");
        titlePara.Margin = new Thickness(0, 0, 0, 8);
        _detailDocument.Blocks.Add(titlePara);

        string? currentVerifyingTaskTitle = null;
        var hasActiveVerifyingTask = false;
        var taskList = new List();
        taskList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
        taskList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        taskList.MarkerStyle = TextMarkerStyle.Disc;
        taskList.Margin = new Thickness(0, 0, 0, 12);

        var durableTasks = _durablePlan?.Tasks;
        if (durableTasks is not null)
        {
            foreach (var task in durableTasks)
            {
                var assignment = task.AgentAssignments?.FirstOrDefault(a =>
                    string.Equals(a.AgentHandle, agentHandle, StringComparison.OrdinalIgnoreCase));
                if (assignment is null) continue;

                var isVerifying = PlanTaskStatus.IsVerifying(task.Status);
                var isVerificationPending = string.Equals(task.Status, PlanTaskStatus.VerificationPending, StringComparison.Ordinal);
                if ((isVerifying || isVerificationPending) &&
                    (currentVerifyingTaskTitle is null || (!hasActiveVerifyingTask && isVerifying)))
                {
                    currentVerifyingTaskTitle = task.Title ?? task.TaskId;
                    hasActiveVerifyingTask = isVerifying;
                }

                var titleRun = new Run(task.Title ?? task.TaskId) { FontWeight = FontWeights.SemiBold };
                var statusColorKey = GetTaskStatusColorKey(task.Status);
                var statusRun = new Run($"  ● {FormatTaskStatus(task.Status)}");
                statusRun.SetResourceReference(TextElement.ForegroundProperty, statusColorKey);
                var roleRun = new Run($"\n{assignment.Role}");
                roleRun.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");

                var para = new Paragraph();
                para.Inlines.Add(titleRun);
                para.Inlines.Add(statusRun);
                para.Inlines.Add(roleRun);
                para.Margin = new Thickness(0, 0, 0, 4);
                taskList.ListItems.Add(new ListItem(para));
            }
        }
        else
        {
            // Fallback to decomposed plan tasks
            var decomposedTasks = _plan?.Group.Tasks;
            if (decomposedTasks is not null)
            {
                foreach (var task in decomposedTasks)
                {
                    var assignment = task.AgentAssignments?.FirstOrDefault(a =>
                        string.Equals(a.AgentHandle, agentHandle, StringComparison.OrdinalIgnoreCase));
                    if (assignment is null) continue;

                    var titleRun = new Run(task.Title ?? task.Id) { FontWeight = FontWeights.SemiBold };
                    var roleRun = new Run($"\n{assignment.Role}");
                    roleRun.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");

                    var para = new Paragraph();
                    para.Inlines.Add(titleRun);
                    para.Inlines.Add(roleRun);
                    para.Margin = new Thickness(0, 0, 0, 4);
                    taskList.ListItems.Add(new ListItem(para));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(currentVerifyingTaskTitle))
        {
            var verifyingPara = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 8),
            };
            verifyingPara.Inlines.Add(new Run("Currently verifying: "));
            verifyingPara.Inlines.Add(new Run(currentVerifyingTaskTitle) { FontWeight = FontWeights.SemiBold });
            verifyingPara.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
            verifyingPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            _detailDocument.Blocks.Add(verifyingPara);
        }

        if (taskList.ListItems.Count > 0)
        {
            AddSectionHeader("Steps in this plan");
            _detailDocument.Blocks.Add(taskList);
        }
    }

    private void AddSectionHeader(string text)
    {
        if (_detailDocument is null) return;
        var header = new Paragraph(new Run(text) { FontWeight = FontWeights.SemiBold });
        header.SetResourceReference(TextElement.ForegroundProperty, "LabelText");
        header.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        header.Margin = new Thickness(0, 4, 0, 2);
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
        PlanTaskStatus.HumanReviewRequired => "Human Review Required",
        _ when PlanTaskStatus.IsVerifying(status) => "Verifying",
        _ => status ?? "Unknown",
    };

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

    private void PersistCommittedTaskCardReorder()
    {
        if (_lastCommittedColumnOrder is not { } committed)
            return;

        if (_saveDefinitionEdit is not null &&
            _plan is not null &&
            TryApplyCommittedColumnReorder(_plan.Group, committed, out var reorderedGroup))
        {
            ApplyStagedMutation(reorderedGroup, preserveReview: true);
            return;
        }

        if (_durablePlan is not null &&
            TryApplyCommittedColumnReorder(_durablePlan, committed, out var reorderedDurablePlan))
        {
            _durablePlan = reorderedDurablePlan;
            _plan = PendingDecomposePlanAdapter.FromPlan(reorderedDurablePlan);
            return;
        }

        if (_plan is null)
            return;
        if (TryApplyCommittedColumnReorder(_plan.Group, committed, out var reorderedPendingGroup))
            _plan = _plan with { Group = reorderedPendingGroup };
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
            if (role == ConnectorEndpointRole.None)
                continue;

            var sourceTaskId = group.TaskIds.FirstOrDefault(id =>
                group.GetEndpointRole(id).HasFlag(ConnectorEndpointRole.Start));
            var targetTaskId = group.TaskIds.FirstOrDefault(id =>
                group.GetEndpointRole(id).HasFlag(ConnectorEndpointRole.End));
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
                StartOffsetWithinCard = role.HasFlag(ConnectorEndpointRole.Start)
                    ? group.CurrentFrom.Y - originalTop
                    : null,
                EndOffsetWithinCard = role.HasFlag(ConnectorEndpointRole.End)
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
            if (connectorState.Role.HasFlag(ConnectorEndpointRole.Start) &&
                connectorState.StartOffsetWithinCard.HasValue)
            {
                updatedFrom = new Point(left + NodeWidth, top + connectorState.StartOffsetWithinCard.Value);
            }
            if (connectorState.Role.HasFlag(ConnectorEndpointRole.End) &&
                connectorState.EndOffsetWithinCard.HasValue)
            {
                updatedTo = new Point(left, top + connectorState.EndOffsetWithinCard.Value);
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

    private ConnectorGroup AddDirectTaskConnector(
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
            return AddConnector(
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
            return AddConnector(
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
        return AddRoundedConnectorRoute(
            canvas,
            detour,
            arrowHead: true,
            skipCount: skipCount,
            dashed: dashed);
    }

    private static void RemoveConnectorGroupElements(Canvas canvas, ConnectorGroup group)
    {
        foreach (var element in group.GlowElements.Concat(group.MainElements).Distinct())
            canvas.Children.Remove(element);
    }

    private void ReplaceConnectorGroupForDrag(DragConnectorState state, ConnectorGroup replacementGroup)
    {
        var priorGroup = state.Group;
        foreach (var taskId in priorGroup.TaskIds.Distinct(StringComparer.Ordinal))
        {
            if (!replacementGroup.TaskIds.Contains(taskId, StringComparer.Ordinal))
                replacementGroup.TaskIds.Add(taskId);
            var role = priorGroup.GetEndpointRole(taskId);
            if (role != ConnectorEndpointRole.None)
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
        var columnSlotTops = sameColumnCards.Select(entry => entry.Top).ToArray();

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
        CommitTaskCardDrag();
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
        if (dragState.CurrentColumnOrder.Count > 0 &&
            !dragState.CurrentColumnOrder.SequenceEqual(dragState.PreDragColumnOrder, StringComparer.Ordinal))
        {
            _lastCommittedColumnOrder = new CommittedColumnReorder(
                dragState.ColumnIdentifier,
                dragState.CurrentColumnOrder.ToArray());
            PersistCommittedTaskCardReorder();
        }
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
