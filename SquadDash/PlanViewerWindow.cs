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
    private readonly Func<PlanPreflightBlockedException, Task>? _viewPreflightChanges;
    private readonly Func<Task<bool>>? _isPreflightWorkspaceClean;
    private readonly Func<Task<bool>>? _initializeRepository;
    private readonly Action<string>? _onOpenCommit;
    private readonly Action<IReadOnlyList<PlanEvidenceCommit>>? _onOpenCommits;
    private readonly Action<Plan>? _onContinueInterruptedTask;
    private readonly Action<Plan>? _onReplanRemainingWork;
    private readonly Func<string, (ImageSource? Image, string Initial, Brush Accent)?>? _resolveAgentAvatar;
    private readonly Func<string, bool>? _isTaskActivityActive;
    private readonly Func<string, bool>? _isRecoveryAssessmentActive;
    private System.Windows.Threading.DispatcherTimer? _preflightPollTimer;
    private PlanViewerLiveSyncHandler? _liveSyncHandler;
    private Action<PlanTaskActivityPulseEvent>? _taskActivityPulseHandler;
    private Action<PlanValidationActivityPulseEvent>? _validationActivityPulseHandler;
    private Action<PlanRecoveryAssessmentActivityEvent>? _recoveryAssessmentActivityHandler;
    private readonly HashSet<string> _recoveryAssessmentTaskIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivitySpinner> _taskSpinnersById =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivitySpinner> _validationSpinnersById =
        new(StringComparer.Ordinal);
    private Border? _contentHolder;
    private ScrollViewer? _graphScroll;
    private Canvas? _graphCanvas;
    private FlowDocument? _detailDocument;

    private record SelectedElementIdentity(string Kind, string Id);
    private SelectedElementIdentity? _selectedElement;
    private (Border border, Thickness original)? _selectionBorderState;
    private FrameworkElement? _selectionGlowElement;

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
        Func<PlanPreflightBlockedException, Task>? viewPreflightChanges = null,
        Func<Task<bool>>? isPreflightWorkspaceClean = null,
        WeakEventBroker? broker = null,
        Action<string>? onOpenCommit = null,
        Func<string, (ImageSource? Image, string Initial, Brush Accent)?>? resolveAgentAvatar = null,
        Func<string, bool>? isTaskActivityActive = null,
        Func<string, bool>? isRecoveryAssessmentActive = null,
        Func<Task<bool>>? initializeRepository = null,
        Action<IReadOnlyList<PlanEvidenceCommit>>? onOpenCommits = null,
        Action<Plan>? onContinueInterruptedTask = null,
        Action<Plan>? onReplanRemainingWork = null)
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
        _viewPreflightChanges = viewPreflightChanges;
        _isPreflightWorkspaceClean = isPreflightWorkspaceClean;
        _initializeRepository = initializeRepository;
        _onOpenCommit = onOpenCommit;
        _onOpenCommits = onOpenCommits;
        _onContinueInterruptedTask = onContinueInterruptedTask;
        _onReplanRemainingWork = onReplanRemainingWork;
        _resolveAgentAvatar = resolveAgentAvatar;
        _isTaskActivityActive = isTaskActivityActive;
        _isRecoveryAssessmentActive = isRecoveryAssessmentActive;

        Title     = $"Plan — {plan.Group.GroupTitle}";
        Width     = 1200;
        Height    = 720;
        MinWidth  = 760;
        MinHeight = 480;

        BuildContent(plan, durablePlan);
        Closed += (_, _) => _preflightPollTimer?.Stop();

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
        }
    }

    private void ApplyLiveUpdate(Plan updatedPlan)
    {
        if (_plan is null) return;
        var updatedDefinition = PendingDecomposePlanAdapter.FromPlan(updatedPlan);
        _plan = updatedDefinition;
        _durablePlan = updatedPlan;
        RebuildPreservingScroll(updatedDefinition, updatedPlan);
        Title = $"Plan — {updatedDefinition.Group.GroupTitle}";
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
        _preflightPollTimer?.Stop();
        _preflightPollTimer = null;
        _plan = plan;
        _durablePlan = durablePlan;
        var activeBranch       = _activeBranch;
        var quickReplyFontSize = _quickReplyFontSize;
        var applyAction        = _applyAction;
        var onGatesChanged     = _onGatesChanged;
        var onStartPlan        = _onStartPlan;
        var onResumePlan       = _onResumePlan;
        var onAdoptVerifiedCommitRange = _onAdoptVerifiedCommitRange;
        var onEndPlan          = _onEndPlan;
        var onContinueInterruptedTask = _onContinueInterruptedTask;
        var onReplanRemainingWork = _onReplanRemainingWork;
        var onApproveGate      = _onApproveGate;
        var group = plan.Group;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(22, 16, 22, 2) };

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
                        UIErrorHelper.ShowError("Task Plan", ex.Message, this);
                    }
                };
                actionsPanel.Children.Add(button);
            }
            actionRegion.Children.Add(actionsPanel);
            actionRegion.Children.Add(recoveryHost);
            header.Children.Add(actionRegion);
        }

        if (durablePlan is not null &&
            durablePlan.LifecycleStatus == PlanLifecycleStatus.Interrupted &&
            durablePlan.InterruptionData?.RecoveryAssessment is not null)
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
                    $"Accept {stepLabel} as Complete", quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(
                        $"Review and explicitly accept the existing evidence for {stepLabel}, mark only this step complete, and continue the plan."));
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
                    "Replan Remaining Work", quickReplyFontSize,
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
            durablePlan.LifecycleStatus == PlanLifecycleStatus.Interrupted &&
            (onResumePlan is not null || onAdoptVerifiedCommitRange is not null || onEndPlan is not null))
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
                        ? _interruptedPrimaryActionLabel ?? "Assess & Continue"
                        : "Resume Validation",
                    quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip(
                        resumableValidation is null
                            ? _interruptedPrimaryActionHint ??
                              "AI will classify the task as complete, partial, or not started. SquadDash validates the assessment before changing or continuing the plan."
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
                        UIErrorHelper.ShowError("Assess & Continue", ex.Message, this);
                    }
                };
                interruptedPanel.Children.Add(adoptButton);
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
                                ? "Resume the added amendment; completed tasks remain accepted."
                                : PlanRecoveryResumePolicy.IsReworkPreflightPause(capturedPlan)
                                    ? "Resume the already-prepared rework. The change request will not be submitted again."
                                    : "Resume with the next runnable plan step."
                            : "Resume without assessing repository evidence. This may repeat work from the interrupted task."),
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
            Text         = "Arrows point from prerequisite → dependent.  ALL means every incoming task must finish.  Tasks in the same stage with no arrow between them are independent and may run in any order.",
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(22, 6, 22, 8),
        };
        hintBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        hintBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");

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
                PlanSource.TasksJson         => "Task plan",
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
                var intRow2 = new TextBlock { Text = $"Last task: {interruptedTaskId}" };
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

        var splitGrid = new Grid();
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Pixel) });
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300, GridUnitType.Pixel), MinWidth = 200 });

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

        var detailDocument = new FlowDocument();
        detailDocument.SetResourceReference(FlowDocument.FontSizeProperty, "FontSizeBody");
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
        var levels = CalculateLevels(group.Tasks, tasksById);
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        var columns = group.Tasks.GroupBy(task => levels[task.Id]).OrderBy(column => column.Key).ToArray();
        var displayedValidations = group.Validations ?? [];
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

            var beforeTask = validation.BeforeTaskIds.Count == 1
                ? group.Tasks.FirstOrDefault(task =>
                    string.Equals(task.Id, validation.BeforeTaskIds[0], StringComparison.Ordinal) &&
                    SameIds(task.DependsOn, validation.AfterTaskIds))
                : null;
            if (beforeTask is not null)
            {
                validationAnchors[validation.ValidationId] = ("before", beforeTask.Id, -1, null);
                continue;
            }

            var afterTask = validation.AfterTaskIds.Count == 1
                ? group.Tasks.FirstOrDefault(task =>
                {
                    if (!string.Equals(task.Id, validation.AfterTaskIds[0], StringComparison.Ordinal)) return false;
                    var dependents = group.Tasks
                        .Where(candidate => candidate.DependsOn.Contains(task.Id, StringComparer.Ordinal))
                        .Select(candidate => candidate.Id);
                    return SameIds(dependents, validation.BeforeTaskIds);
                })
                : null;
            if (afterTask is not null)
            {
                validationAnchors[validation.ValidationId] = ("after", afterTask.Id, -1, null);
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

        var validationRailHeight = ValidationShieldPresenter.ComputeValidationRailHeight(
            validationAnchors.Values.Select(PresenterAnchor).ToArray(), _scaleFactor);
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

            var milestoneVisual = PlanApprovalHistoricalPresentationPolicy.Resolve(
                milestoneExecutionLocked,
                existingGate?.Status,
                milestoneIsPrimary);
            var milestoneApproved = milestoneVisual == PlanApprovalControlVisualState.ApprovedCheck;
            var milestoneAwaiting = milestoneVisual == PlanApprovalControlVisualState.AwaitingQuestion;
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
                    var updated = isLocked && existingGate is not null
                        ? milestoneIsPrimary
                            ? PlanGateManager.RemoveGate(durablePlan!, existingGate.GateId)
                            : PlanGateManager.SetPresentationAnchor(durablePlan!, existingGate.GateId, milestoneAnchor)
                        : PlanGateManager.AddBoundaryGate(
                            durablePlan!,
                            afterIds,
                            beforeIds,
                            $"Review milestone before Stage {leftColumn.Key + 2}",
                            milestoneAnchor,
                            removeSubsumedTaskGates: true);
                    if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                    },
                    isLocked && !milestoneIsPrimary ? 0.5 : 1.0,
                    approved: milestoneApproved,
                    awaitingApproval: milestoneAwaiting,
                    selectionAnchor: existingGate?.GateId,
                    toggleActionLabel: milestoneIsPrimary
                        ? "Remove this approval requirement"
                        : "Make this the primary approval control");
                Canvas.SetLeft(milestoneStop, boundaryX - (milestoneApproved || milestoneAwaiting ? 10 : 8) * _scaleFactor);
                Canvas.SetTop(milestoneStop, globalBandTop - octagonSize - 4 * _scaleFactor);
                Panel.SetZIndex(milestoneStop, 25);
                canvas.Children.Add(milestoneStop);
                approvalControlsByAnchor[milestoneAnchor] = milestoneStop;
            }
        }

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

                var taskPositionMap = positions.ToDictionary(
                    entry => entry.Key,
                    entry => (entry.Value.X, entry.Value.Y),
                    StringComparer.Ordinal);
                var gateCenterMap = gates.Select(candidate => (
                    candidate.Center.X,
                    candidate.Center.Y,
                    string.Join("\u001f", candidate.Dependencies.OrderBy(id => id, StringComparer.Ordinal)))).ToArray();
                var layout = ValidationShieldPresenter.ComputeShieldPosition(
                    PresenterAnchor(anchor),
                    stackIndex,
                    _scaleFactor,
                    stageBoundaryXs,
                    taskPositionMap,
                    NodeWidth,
                    NodeHeight,
                    graphTop,
                    gateCenterMap,
                    ref fallbackNextLeft);
                var left = layout.Left;
                var top = layout.Top;
                if (isStageValidation)
                    left -= 72 * _scaleFactor; // re-center the wider (288px) visual over the stage boundary

                Canvas.SetLeft(visual, left);
                Canvas.SetTop(visual, top);
                Panel.SetZIndex(visual, 30);
                canvas.Children.Add(visual);
                _deferredShieldHovers.Add((visual, validation.AfterTaskIds, validation.BeforeTaskIds));

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
                    }
                }
                validationRailRight = Math.Max(validationRailRight, left + validationWidth);
                // Full visual height: shield (26) + title margin (3) + title maxHeight (32) + bottom padding (5)
                validationBottom = Math.Max(validationBottom, top + ValidationShieldPresenter.BaseShieldStackSpacing * _scaleFactor);
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
        var connectorsByTask = new Dictionary<string, List<ConnectorGroup>>(StringComparer.Ordinal);
        // Deferred badge hover wiring — populated during badge draw, executed after borderByTask is ready.
        var _deferredBadgeHovers = new List<(Border Badge, List<ConnectorGroup> Cgs)>();
        void RegisterConnector(string taskId, ConnectorGroup cg)
        {
            if (!connectorsByTask.TryGetValue(taskId, out var list))
                connectorsByTask[taskId] = list = [];
            if (!list.Contains(cg)) list.Add(cg);
            if (!cg.TaskIds.Contains(taskId, StringComparer.Ordinal)) cg.TaskIds.Add(taskId);
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
                RegisterConnector(dependency, cg);
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
                RegisterConnector(target.Id, cg);
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

                var detour = allClusterFootprints.Count > 0
                    ? ValidationShieldPresenter.ComputeConnectorDetour(
                        (fromPt.X, fromPt.Y), (toPt.X, toPt.Y), allClusterFootprints, _scaleFactor)
                    : null;

                if (detour is { Count: 0 })
                {
                    // No collision-free forward-only route was available. Preserve the dependency
                    // with the same alternate hue used for a stage-bypassing connector so the
                    // overlap is visibly distinct rather than appearing to enter the ALL badge.
                    var cg = AddConnector(canvas,
                        fromPt, toPt,
                        arrowHead: true,
                        skipCount: Math.Max(1, skipCount),
                        dashed: dashedTaskEdges.Contains((dependency, task.Id)),
                        toolTip: "Alternate connector color: no clear route around an ALL group was available.",
                        splitAtX: FindSplitX(fromPt.X, toPt.X));
                    RegisterConnector(dependency, cg);
                    RegisterConnector(task.Id, cg);
                }
                else if (detour is null || detour.Count <= 2)
                {
                    var cg = AddConnector(canvas,
                        fromPt, toPt,
                        arrowHead: true,
                        skipCount: skipCount,
                        dashed: dashedTaskEdges.Contains((dependency, task.Id)),
                        splitAtX: FindSplitX(fromPt.X, toPt.X));
                    RegisterConnector(dependency, cg);
                    RegisterConnector(task.Id,   cg);
                }
                else
                {
                    // Render the complete collision-safe route as one rounded path. Each corner
                    // cuts back by half its shorter adjacent segment, producing proportional
                    // curves without allowing a long segment to overwhelm a nearby short turn.
                    var routeGroup = AddRoundedConnectorRoute(
                        canvas,
                        detour,
                        arrowHead: true,
                        skipCount: skipCount,
                        dashed: dashedTaskEdges.Contains((dependency, task.Id)));
                    RegisterConnector(dependency, routeGroup);
                    RegisterConnector(task.Id, routeGroup);
                }
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

            var joinVisual = PlanApprovalHistoricalPresentationPolicy.Resolve(
                joinExecutionLocked,
                (existingJoinGate ?? coveringJoinGate)?.Status,
                IsPrimary(existingJoinGate ?? coveringJoinGate, joinAnchor),
                hasUnresolvedEquivalent: collectivelyCoveredJoin && durablePlan is not null &&
                    durablePlan.ApprovalGates.Any(IsUnresolvedApproval));
            var joinApproved = joinVisual == PlanApprovalControlVisualState.ApprovedCheck;
            var joinAwaiting = joinVisual == PlanApprovalControlVisualState.AwaitingQuestion;
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
                                ? "Human approval is required after every incoming task completes and before joined work begins. Click to review; right-click to remove."
                                : "This is an equivalent view of the approval boundary. Click to review; right-click to make this the primary control."
                            : "Require human approval after every incoming task completes and before joined work begins.",
                    joinExecutionLocked ? null
                    : onGatesChanged is null
                        ? null
                        : () =>
                    {
                        if (coveringJoinGate is not null)
                        {
                            if (joinController is not null)
                                ShowCoveredGuidance(joinController, ApprovalLabel(joinController));
                            return;
                        }
                        var updated = joinIsLocked && existingJoinGate is not null
                            ? joinIsPrimary
                                ? PlanGateManager.RemoveGate(durablePlan!, existingJoinGate.GateId)
                                : PlanGateManager.SetPresentationAnchor(durablePlan!, existingJoinGate.GateId, joinAnchor)
                            : PlanGateManager.AddBoundaryGate(
                                durablePlan!,
                                joinAfterIds,
                                joinBeforeIds,
                                $"Review joined work before: {string.Join(", ", gate.Targets.Select(task => task.Title ?? task.Id))}",
                                joinAnchor,
                                removeSubsumedTaskGates: true);
                        if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                    },
                    joinIsLocked && (!joinIsPrimary || coveringJoinGate is not null ||
                                     collectivelyCoveredJoin) ? 0.5 : 1.0,
                    approved: joinApproved,
                    awaitingApproval: joinAwaiting,
                    selectionAnchor: (existingJoinGate ?? coveringJoinGate)?.GateId,
                    toggleActionLabel: joinIsPrimary
                        ? "Remove this approval requirement"
                        : "Make this the primary approval control");
                joinStop.HorizontalAlignment = HorizontalAlignment.Right;
                joinStop.VerticalAlignment = VerticalAlignment.Center;
                joinStop.Margin = new Thickness(0, 0, 4, 0);
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
                ToolTip         = "ALL prerequisites entering this gate must finish before any outgoing task can begin.",
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
            if (_recoveryAssessmentTaskIds.Contains(task.Id) ||
                (_isRecoveryAssessmentActive?.Invoke(task.Id) ?? false))
                activityState = PlanTaskActivityState.Assessing;
            var isTaskExecuting = activityState is PlanTaskActivityState.Executing or
                PlanTaskActivityState.Assessing or
                PlanTaskActivityState.Verifying or
                PlanTaskActivityState.Reworking;
            var prereqLines = task.DependsOn.Count == 0
                ? ["None — this task can start immediately."]
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
                            "The task is receiving its one bounded automatic correction.",
                        _ => "This task is actively receiving work from one or more agents.",
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
                    Header = "Require approval before this task",
                    IsEnabled = !taskEntryLocked,
                };
                if (taskEntryLocked)
                    addBeforeItem.ToolTip = ToolTipHelper.MakeThemedToolTip(
                        PlanApprovalControlLockPolicy.LockedTooltip("Task entry"));
                addBeforeItem.Click += (_, _) =>
                {
                    var msg = SimpleInputDialog.Show(Window.GetWindow(border) ?? Application.Current.MainWindow,
                        "Enter a message for this approval gate:",
                        "Require Approval Before",
                        $"Review before: {capturedTask.Title ?? capturedTask.Id}");
                    if (msg is null) return;
                    var updated = PlanGateManager.AddGateBefore(durablePlan, capturedTask.Id, msg);
                    if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                };

                var addAfterItem = new MenuItem
                {
                    Header = "Require approval after this task",
                    IsEnabled = !taskExitLocked,
                };
                if (taskExitLocked)
                    addAfterItem.ToolTip = ToolTipHelper.MakeThemedToolTip(
                        PlanApprovalControlLockPolicy.LockedTooltip("Task exit"));
                addAfterItem.Click += (_, _) =>
                {
                    var msg = SimpleInputDialog.Show(Window.GetWindow(border) ?? Application.Current.MainWindow,
                        "Enter a message for this approval gate:",
                        "Require Approval After",
                        $"Review after: {capturedTask.Title ?? capturedTask.Id}");
                    if (msg is null) return;
                    var updated = PlanGateManager.AddGateAfter(durablePlan, capturedTask.Id, msg);
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
                        var removeItem = new MenuItem
                        {
                            Header = $"Remove approval gate: {capturedGate.Message}",
                            IsEnabled = !gateIsTraversed,
                        };
                        if (gateIsTraversed)
                            removeItem.ToolTip = ToolTipHelper.MakeThemedToolTip(
                                PlanApprovalControlLockPolicy.LockedTooltip("Traversed gate"));
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

                MenuItem CreateInsertTaskMenuItem(bool insertAfter, bool isEnabled)
                {
                    var relativePosition = insertAfter ? "after" : "before";
                    var item = new MenuItem
                    {
                        Header = $"Insert task {relativePosition}…",
                        IsEnabled = isEnabled,
                        ToolTip = isEnabled
                            ? ToolTipHelper.MakeThemedToolTip(
                                $"Add a new task {relativePosition} this still-pending task and update its future dependencies.")
                            : ToolTipHelper.MakeThemedToolTip(
                                "Only the unstarted downstream portion of a plan can be changed."),
                    };
                    item.Click += (_, _) =>
                    {
                        var owner = Window.GetWindow(border) ?? Application.Current.MainWindow;
                        var newTitle = SimpleInputDialog.Show(
                            owner,
                            $"Enter a short title for the task to insert {relativePosition} “{capturedTask.Title ?? capturedTask.Id}”:",
                            $"Insert Task {relativePosition}");
                        if (newTitle is null) return;
                        var newDescription = SimpleInputDialog.Show(
                            owner,
                            "Describe the result this task must produce:",
                            "Describe Inserted Task",
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
                }
            }

            // Assess & Continue is coordinator-owned. Mirror the fact-checker placement so
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
                    var beforeVisual = PlanApprovalHistoricalPresentationPolicy.Resolve(
                        entryExecutionLocked,
                        controllingBeforeGate?.Status,
                        IsPrimary(controllingBeforeGate, beforeAnchor),
                        collectivelyUnresolvedEntry);
                    var beforeApproved = beforeVisual == PlanApprovalControlVisualState.ApprovedCheck;
                    var beforeAwaiting = beforeVisual == PlanApprovalControlVisualState.AwaitingQuestion;
                    if (beforeVisual != PlanApprovalControlVisualState.Hidden)
                    {
                        var beforeStop = CreateApprovalStop(
                            beforeEngaged,
                            beforeApproved
                                ? BuildApprovalResolvedToolTip(controllingBeforeGate, "before this task began")
                            : beforeAwaiting
                                ? "Waiting for human approval before this task begins."
                            : entryExecutionLocked
                            ? PlanApprovalControlLockPolicy.LockedTooltip("Task entry")
                        : collectivelyCoveredEntry
                            ? "This task entry is covered by every incoming approval requirement."
                        : coveringBeforeGate is not null
                            ? "This task entry is covered by a larger approval requirement."
                            : beforeEngaged
                            ? beforeIsPrimary
                                ? "Human approval is required before this task begins. Click to review; right-click to remove."
                                : "This is an equivalent view of the approval boundary. Click to review; right-click to make this the primary control."
                            : "Require human approval before this task begins.",
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
                                if (beforeController is not null)
                                    ShowCoveredGuidance(beforeController, ApprovalLabel(beforeController));
                                return;
                            }
                            var updated = existingBeforeGate is not null
                                ? beforeIsPrimary
                                    ? PlanGateManager.RemoveGate(durablePlan, existingBeforeGate.GateId)
                                    : PlanGateManager.SetPresentationAnchor(durablePlan, existingBeforeGate.GateId, beforeAnchor)
                                : PlanGateManager.AddGateBefore(durablePlan, capturedTaskForStop.Id,
                                    $"Review before starting: {capturedTaskForStop.Title ?? capturedTaskForStop.Id}");
                            if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                        },
                            beforeEngaged && (!beforeIsPrimary || coveringBeforeGate is not null ||
                                              collectivelyCoveredEntry) ? 0.5 : 1.0,
                            approved: beforeApproved,
                            awaitingApproval: beforeAwaiting,
                            selectionAnchor: controllingBeforeGate?.GateId,
                            toggleActionLabel: beforeIsPrimary
                                ? "Remove this approval requirement"
                                : "Make this the primary approval control");
                        Canvas.SetLeft(beforeStop, position.X + (beforeApproved || beforeAwaiting ? 4 : 6) * _scaleFactor);
                        Canvas.SetTop(beforeStop, position.Y + NodeHeight - (beforeApproved || beforeAwaiting ? 22 : 20) * _scaleFactor);
                        Panel.SetZIndex(beforeStop, 25);
                        canvas.Children.Add(beforeStop);
                        approvalControlsByAnchor[beforeAnchor] = beforeStop;
                    }
                }

                var hasFinalValidationBoundary =
                    PlanGateManager.HasFinalValidationAfterTask(durablePlan, capturedTaskForStop.Id);
                if (!isLeaf || hasFinalValidationBoundary)
                {
                    var exitExecutionLocked = PlanApprovalControlLockPolicy.PlanHasExecutionContext(durablePlan) &&
                        PlanApprovalControlLockPolicy.IsTaskExitLocked(durablePlan, capturedTaskForStop.Id);
                    var existingAfterGate = FindTaskGateAfter(capturedTaskForStop.Id);
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
                    var afterIsPrimary = IsPrimary(existingAfterGate, afterAnchor);
                    var afterController = coveringAfterGate is null ? null : ResolvePresentationAnchor(coveringAfterGate);
                    var controllingAfterGate = existingAfterGate ?? coveringAfterGate;
                    var collectivelyUnresolvedExit = collectivelyCoveredByAllJoins &&
                        lockedAllJoinGates.Any(IsUnresolvedApproval);
                    var afterVisual = PlanApprovalHistoricalPresentationPolicy.Resolve(
                        exitExecutionLocked,
                        controllingAfterGate?.Status,
                        IsPrimary(controllingAfterGate, afterAnchor),
                        collectivelyUnresolvedExit);
                    var afterApproved = afterVisual == PlanApprovalControlVisualState.ApprovedCheck;
                    var afterAwaiting = afterVisual == PlanApprovalControlVisualState.AwaitingQuestion;
                    if (afterVisual != PlanApprovalControlVisualState.Hidden)
                    {
                        var afterStop = CreateApprovalStop(
                            afterEngaged,
                            afterApproved
                                ? BuildApprovalResolvedToolTip(controllingAfterGate, "after this task completed")
                            : afterAwaiting
                                ? "Waiting for human approval before the final validation begins."
                            : exitExecutionLocked
                            ? PlanApprovalControlLockPolicy.LockedTooltip("Task exit")
                        : collectivelyCoveredByAllJoins
                            ? "This task exit is covered by its enabled ALL approval requirements."
                        : coveringAfterGate is not null
                            ? "This task exit is covered by a larger approval requirement."
                            : afterEngaged
                            ? afterIsPrimary
                                ? "Human approval is required after this task completes. Click to review; right-click to remove."
                                : "This is an equivalent view of the approval boundary. Click to review; right-click to make this the primary control."
                            : "Require human approval after this task completes.",
                        exitExecutionLocked || onGatesChanged is null ? null : () =>
                        {
                            if (collectivelyCoveredByAllJoins)
                            {
                                if (collectiveController is not null)
                                    ShowCoveredGuidance(collectiveController, ApprovalLabel(collectiveController));
                                return;
                            }
                            if (coveringAfterGate is not null)
                            {
                                if (afterController is not null)
                                    ShowCoveredGuidance(afterController, ApprovalLabel(afterController));
                                return;
                            }
                            var updated = existingAfterGate is not null
                                ? afterIsPrimary
                                    ? PlanGateManager.RemoveGate(durablePlan, existingAfterGate.GateId)
                                    : PlanGateManager.SetPresentationAnchor(durablePlan, existingAfterGate.GateId, afterAnchor)
                                : PlanGateManager.AddGateAfter(durablePlan, capturedTaskForStop.Id,
                                    $"Review after completing: {capturedTaskForStop.Title ?? capturedTaskForStop.Id}");
                            if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                        },
                            afterEngaged && (!afterIsPrimary || coveringAfterGate is not null ||
                                             collectivelyCoveredByAllJoins) ? 0.5 : 1.0,
                            approved: afterApproved,
                            awaitingApproval: afterAwaiting,
                            selectionAnchor: controllingAfterGate?.GateId,
                            toggleActionLabel: afterIsPrimary
                                ? "Remove this approval requirement"
                                : "Make this the primary approval control");
                        Canvas.SetLeft(afterStop, position.X + NodeWidth - (afterApproved || afterAwaiting ? 24 : 22) * _scaleFactor);
                        Canvas.SetTop(afterStop, position.Y + NodeHeight - (afterApproved || afterAwaiting ? 22 : 20) * _scaleFactor);
                        Panel.SetZIndex(afterStop, 25);
                        canvas.Children.Add(afterStop);
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
                            ? "Preview: human approval is required before this task."
                            : "Preview: this stop controls approval before the task.",
                        null);
                    Canvas.SetLeft(beforeStop, position.X + 6 * _scaleFactor);
                    Canvas.SetTop(beforeStop, position.Y + NodeHeight - 20 * _scaleFactor);
                    Panel.SetZIndex(beforeStop, 25);
                    canvas.Children.Add(beforeStop);
                }

                if (!isLeaf)
                {
                    var afterEngaged = FindDisplayedGate([task.Id], directDependents) is not null;
                    var afterStop = CreateApprovalStop(
                        afterEngaged,
                        afterEngaged
                            ? "Preview: human approval is required after this task."
                            : "Preview: this stop controls approval after the task.",
                        null);
                    Canvas.SetLeft(afterStop, position.X + NodeWidth - 22 * _scaleFactor);
                    Canvas.SetTop(afterStop, position.Y + NodeHeight - 20 * _scaleFactor);
                    Panel.SetZIndex(afterStop, 25);
                    canvas.Children.Add(afterStop);
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

        canvas.Width  = Math.Max(positions.Values.Max(point => point.X) + NodeWidth, validationRailRight);
        canvas.Height = Math.Max(
            positions.Values.Max(point => point.Y) + NodeHeight,
            Math.Max(
                validationBottom,
                allClusterFootprints.Select(footprint => footprint.Bottom).DefaultIfEmpty(0).Max()));

        SizeWindowToContent(canvas.Width, canvas.Height);

        if (durablePlan is not null)
        {
            var approvalSummary = BuildApprovalSummaryPanel(
                durablePlan,
                levels,
                quickReplyFontSize);
            Grid.SetRow(approvalSummary, 3);
            approvalSummary.Visibility = Visibility.Collapsed;
            root.Children.Add(approvalSummary);

            // Defer expansion to after layout completes so we can measure without stealing graph space.
            Dispatcher.BeginInvoke(() => RevealApprovalSummary(approvalSummary),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
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
                UIErrorHelper.ShowError("Task Plan", ex.Message, this);
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
                    if (!await _isPreflightWorkspaceClean()) return;
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
    private void RebuildPreservingScroll(PendingDecomposePlan plan, Plan? durablePlan)
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
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }, System.Windows.Threading.DispatcherPriority.Normal);
    }

    internal void RefreshPlan(PendingDecomposePlan plan, Plan durablePlan)
    {
        RebuildPreservingScroll(plan, durablePlan);
        Title = $"Plan — {plan.Group.GroupTitle}";
    }

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
                        listItem.Blocks.Add(new Paragraph(new Run("After all the following tasks complete:"))
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

    private void SizeWindowToContent(double canvasWidth, double canvasHeight)
    {
        const double horizontalChrome = 18 * 2 + 20;
        const double verticalChrome = 18 * 2 + 60 + 40;
        const double approvalReserve = 180;

        var idealWidth = canvasWidth + horizontalChrome;
        var idealHeight = canvasHeight + verticalChrome + approvalReserve;

        var workArea = SystemParameters.WorkArea;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != nint.Zero)
        {
            var physicalWorkArea = NativeMethods.GetWorkAreaForWindow(hwnd);
            var source = PresentationSource.FromVisual(this);
            var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            workArea = new Rect(
                physicalWorkArea.X / dpiX, physicalWorkArea.Y / dpiY,
                physicalWorkArea.Width / dpiX, physicalWorkArea.Height / dpiY);
        }

        Width = Math.Max(MinWidth, Math.Min(idealWidth, workArea.Width));
        Height = Math.Max(MinHeight, Math.Min(idealHeight, workArea.Height));
    }

    private void RevealApprovalSummary(FrameworkElement approvalSummary)
    {
        // Measure how much space the summary needs while still collapsed.
        approvalSummary.Visibility = Visibility.Visible;
        approvalSummary.MaxHeight = 0;
        UpdateLayout();

        approvalSummary.Measure(new Size(ActualWidth, double.PositiveInfinity));
        var desiredHeight = approvalSummary.DesiredSize.Height;
        if (desiredHeight <= 0)
        {
            approvalSummary.MaxHeight = double.PositiveInfinity;
            return;
        }

        // Get the monitor work area in physical pixels via the window's HWND.
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero)
        {
            approvalSummary.MaxHeight = 170;
            return;
        }

        var workArea = NativeMethods.GetWorkAreaForWindow(hwnd);

        // Convert window bounds to physical pixels for comparison.
        var source = PresentationSource.FromVisual(this);
        var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        var windowBottomPhysical = (Top + ActualHeight) * dpiY;
        var windowTopPhysical = Top * dpiY;

        // Check that the window is fully contained within this monitor's work area.
        if (windowTopPhysical < workArea.Top || windowBottomPhysical > workArea.Bottom)
        {
            approvalSummary.MaxHeight = 170;
            return;
        }

        var availableBelowPhysical = workArea.Bottom - windowBottomPhysical;
        var availableBelow = availableBelowPhysical / dpiY;

        if (availableBelow >= desiredHeight)
        {
            // Plenty of room — grow window, then reveal at full size.
            Height = ActualHeight + desiredHeight;
            approvalSummary.MaxHeight = double.PositiveInfinity;
        }
        else if (availableBelow > 0)
        {
            // Partial room — grow what we can, constrain the summary.
            Height = ActualHeight + availableBelow;
            approvalSummary.MaxHeight = availableBelow;
        }
        else
        {
            // No room — use scrollbar fallback.
            approvalSummary.MaxHeight = 170;
        }
    }

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
            var spinnerSize = shieldWidth * 0.55;
            var spinner = new ActivitySpinner
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AccentColor = ResolvePlanActivityColor("ValidationValidatingSpinner", Colors.SteelBlue),
                FontSize = spinnerSize,
                MinimumOpacity = 1.0,
                ToolTip = ToolTipHelper.MakeThemedToolTip("Validation is actively evaluating its contract."),
            };
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
            AddText("• Repository evidence from the prerequisite tasks", "SubtleText");
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
        _ => "Waiting for prerequisite tasks",
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
        string? selectionAnchor = null,
        string? toggleActionLabel = null)
    {
        var s = _scaleFactor;
        FrameworkElement indicator;
        Polygon? stop = null;
        if (approved || awaitingApproval)
        {
            var bgSize = 18 * s;
            var bg = new Border
            {
                Width = bgSize,
                Height = bgSize,
                CornerRadius = new CornerRadius(3 * s),
                BorderThickness = new Thickness(awaitingApproval ? 1.1 * s : 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            bg.SetResourceReference(Border.BackgroundProperty, "PlanApprovalResolvedBg");
            if (awaitingApproval)
                bg.SetResourceReference(Border.BorderBrushProperty, "PlanApprovalResolved");

            FrameworkElement glyph;
            var resourceKey = awaitingApproval
                ? "TaskAwaitingHumanApproval"
                : "PlanApprovalResolvedCheck";
            if (Application.Current?.TryFindResource(resourceKey) is Viewbox resourceGlyph)
            {
                resourceGlyph.Width = (awaitingApproval ? 8 : 13) * s;
                resourceGlyph.Height = (awaitingApproval ? 13 : 13) * s;
                resourceGlyph.HorizontalAlignment = HorizontalAlignment.Center;
                resourceGlyph.VerticalAlignment = VerticalAlignment.Center;
                glyph = resourceGlyph;
            }
            else
            {
                var fallback = new TextBlock
                {
                    Text = awaitingApproval ? "?" : "✓",
                    FontSize = 14 * s,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                fallback.SetResourceReference(TextBlock.ForegroundProperty, "PlanApprovalResolved");
                glyph = fallback;
            }

            var approvedContainer = new Grid();
            approvedContainer.Children.Add(bg);
            approvedContainer.Children.Add(glyph);
            if (awaitingApproval)
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
            indicator = approvedContainer;
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
            Width = (approved || awaitingApproval ? 20 : 16) * s,
            Height = (approved || awaitingApproval ? 20 : 16) * s,
            Background = Brushes.Transparent,
            Cursor = toggle is null ? Cursors.Arrow : Cursors.Hand,
            ToolTip = ToolTipHelper.MakeThemedToolTip(toolTip),
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
                var menu = new ContextMenu();
                var toggleItem = new MenuItem
                {
                    Header = toggleActionLabel ?? "Change this approval control",
                };
                toggleItem.Click += (_, _) => toggle();
                menu.Items.Add(toggleItem);
                hitTarget.ContextMenu = menu;
            }

        }
        return hitTarget;
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

        if (skipCount > 0 || Math.Abs(to.Y - from.Y) < 1.0 || Math.Abs(to.X - from.X) < 1.0)
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
            bool doLineSplit = !double.IsNaN(splitAtX) && splitAtX > from.X + 1.0 && splitAtX < lineEnd.X - 1.0;
            if (doLineSplit)
            {
                double tSplit  = (splitAtX - from.X) / (lineEnd.X - from.X);
                double splitY  = from.Y + tSplit * (lineEnd.Y - from.Y);
                var    splitPt = new Point(splitAtX, splitY);

                var leftLine = new Line
                {
                    X1 = from.X, Y1 = from.Y, X2 = splitPt.X, Y2 = splitPt.Y,
                    StrokeThickness = 2, Stroke = mainBrush,
                };
                if (dashArray is not null) leftLine.StrokeDashArray = dashArray;
                canvas.Children.Add(leftLine);
                group.MainElements.Add(leftLine);

                var rightLine = new Line
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
                var mainLine = new Line
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
                var leftPath = new Path { Data = leftGeo, StrokeThickness = 2, Stroke = mainBrush, Fill = Brushes.Transparent };
                if (dashArray is not null) leftPath.StrokeDashArray = dashArray;
                canvas.Children.Add(leftPath);
                group.MainElements.Add(leftPath);

                var rightFig = new PathFigure { StartPoint = rp0 };
                rightFig.Segments.Add(new BezierSegment(rp1, rp2, rp3, isStroked: true));
                var rightGeo  = new PathGeometry(); rightGeo.Figures.Add(rightFig);
                var rightPath = new Path
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
                var mainPath = new Path
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
        }

        if (arrowHead)
        {
            // Glow arrowhead: same tip, wider wings.
            var perp = new Vector(0, 1);
            var glowArrow = new Polygon
            {
                Fill       = glowBrush,
                Visibility = Visibility.Hidden,
                Points     = [to, lineEnd + perp * glowArrowHalf, lineEnd - perp * glowArrowHalf],
            };
            canvas.Children.Add(glowArrow);
            group.GlowElements.Add(glowArrow);

            // Main arrowhead.
            var mainArrow = new Polygon
            {
                Fill   = mainBrush,
                Points = [to, lineEnd + perp * arrowHalfWidth, lineEnd - perp * arrowHalfWidth],
            };
            canvas.Children.Add(mainArrow);
            group.MainElements.Add(mainArrow);
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
        var corners = ValidationShieldPresenter.ComputeRoundedRouteCorners(drawableRoute)
            .ToDictionary(corner => corner.PointIndex);

        PathGeometry MakeGeometry()
        {
            var figure = new PathFigure
            {
                StartPoint = new Point(drawableRoute[0].X, drawableRoute[0].Y),
            };
            for (var index = 1; index < drawableRoute.Length - 1; index++)
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
                new Point(drawableRoute[^1].X, drawableRoute[^1].Y), isStroked: true));
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
            Data = MakeGeometry(),
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
            Data = MakeGeometry(),
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
            Data = MakeGeometry(),
            StrokeThickness = 12,
            Fill = Brushes.Transparent,
            Opacity = 0.01,
        };
        hitPath.SetResourceReference(Shape.StrokeProperty, "CardSurface");
        if (toolTip is not null) hitPath.ToolTip = toolTip;
        canvas.Children.Add(hitPath);
        group.MainElements.Add(hitPath);

        if (arrowHead)
        {
            var lineEnd = new Point(drawableRoute[^1].X, drawableRoute[^1].Y);
            var perpendicular = new Vector(0, 1);
            var glowArrow = new Polygon
            {
                Fill = glowBrush,
                Visibility = Visibility.Hidden,
                Points = [target, lineEnd + perpendicular * glowArrowHalf, lineEnd - perpendicular * glowArrowHalf],
            };
            canvas.Children.Add(glowArrow);
            group.GlowElements.Add(glowArrow);

            var mainArrow = new Polygon
            {
                Fill = mainBrush,
                Points = [target, lineEnd + perpendicular * arrowHalfWidth, lineEnd - perpendicular * arrowHalfWidth],
            };
            canvas.Children.Add(mainArrow);
            group.MainElements.Add(mainArrow);
        }

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

        if (_selectedElement is null)
        {
            _detailDocument.Blocks.Clear();
            var placeholder = new Paragraph(new Run("Select an element to view details."));
            placeholder.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
            placeholder.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            _detailDocument.Blocks.Add(placeholder);
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
        AddTaskIdList("After Tasks", validation.AfterTaskIds, plan);

        // Before Task IDs
        AddTaskIdList("Before Tasks", validation.BeforeTaskIds, plan);
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

        var approvalQuestion = PlanProofCapabilityPolicy.ResolveHumanQuestion(gate);
        if (!string.IsNullOrWhiteSpace(approvalQuestion))
        {
            AddSectionHeader("What to verify");
            var questionPara = new Paragraph(new Run(approvalQuestion)
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
        AddTaskIdList("After Tasks", gate.AfterTaskIds, plan);

        // Before Task IDs
        AddTaskIdList("Before Tasks", gate.BeforeTaskIds, plan);

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
        AddTaskIdList("After Tasks", gate.AfterTaskIds, _durablePlan);

        // Before Tasks
        AddTaskIdList("Before Tasks", gate.BeforeTaskIds, _durablePlan);

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
            AddSectionHeader($"Tasks Before (Stage {columnIndex + 1} and earlier)");
            AddTaskStatusList(tasksBefore);
        }

        // Tasks after milestone (columns > columnIndex)
        var tasksAfter = columns.Where(g => g.Key > columnIndex).SelectMany(g => g).ToArray();
        if (tasksAfter.Length > 0)
        {
            AddSectionHeader($"Tasks After (Stage {columnIndex + 2} and later)");
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

        AddSectionHeader($"Tasks ({stageTasks.Length})");
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
        var descPara = new Paragraph(new Run("Waits for the following tasks to complete before continuing:"));
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

            var question = PlanProofCapabilityPolicy.ResolveHumanQuestion(gate);
            if (!string.IsNullOrWhiteSpace(question))
            {
                var questionParagraph = new Paragraph();
                questionParagraph.Inlines.Add(new Run("What to verify: ") { FontWeight = FontWeights.SemiBold });
                questionParagraph.Inlines.Add(new Run(question));
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

    private void AppendApprovalContractEditor(PlanApprovalGate gate)
    {
        if (_detailDocument is null || _durablePlan is null || _onGatesChanged is null ||
            !PlanGateManager.CanEditReviewContract(gate))
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

        // Title
        var titlePara = new Paragraph(new Run(agentHandle) { FontWeight = FontWeights.Bold });
        titlePara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
        titlePara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeLarge");
        titlePara.Margin = new Thickness(0, 0, 0, 8);
        _detailDocument.Blocks.Add(titlePara);

        // Tasks in this plan
        AddSectionHeader("Tasks in this plan");

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

        if (taskList.ListItems.Count > 0)
            _detailDocument.Blocks.Add(taskList);
        else
        {
            var noPara = new Paragraph(new Run("No tasks assigned."));
            noPara.SetResourceReference(TextElement.ForegroundProperty, "SubtleText");
            noPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            _detailDocument.Blocks.Add(noPara);
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
        element.MouseLeftButtonDown += (sender, e) =>
        {
            if (e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase
                || (e.OriginalSource is FrameworkElement src && src.TemplatedParent is System.Windows.Controls.Primitives.ButtonBase))
                return;
            if (sender is FrameworkElement fe && fe.Tag is string tag)
            {
                var parts = tag.Split(':', 2);
                if (parts.Length == 2)
                    SelectElement(parts[0], parts[1]);
                e.Handled = true;
            }
        };
    }
}
