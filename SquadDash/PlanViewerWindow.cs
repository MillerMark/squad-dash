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
    private const double NodeWidth = 220;
    private const double NodeHeight = 100;
    private const double ColumnSpacing = 360;
    private const double RowSpacing = 152;

    private readonly string? _activeBranch;
    private readonly double _quickReplyFontSize;
    private readonly Func<DecomposePlanActionDefinition, Task<bool>>? _applyAction;
    private readonly Action<Plan>? _onGatesChanged;
    private readonly Action<Plan>? _onResumePlan;
    private readonly Action<Plan>? _onEndPlan;
    private readonly Action<Plan, string>? _onApproveGate;
    private Border? _contentHolder;
    private ScrollViewer? _graphScroll;

    internal PlanViewerWindow(
        PendingDecomposePlan plan,
        string? activeBranch,
        double quickReplyFontSize,
        Func<DecomposePlanActionDefinition, Task<bool>>? applyAction = null,
        Plan? durablePlan = null,
        Action<Plan>? onGatesChanged = null,
        Action<Plan>? onResumePlan   = null,
        Action<Plan>? onEndPlan      = null,
        Action<Plan, string>? onApproveGate = null)
        : base(captionHeight: CloseButtonHeight)
    {
        _activeBranch       = activeBranch;
        _quickReplyFontSize = quickReplyFontSize;
        _applyAction        = applyAction;
        _onGatesChanged     = onGatesChanged;
        _onResumePlan       = onResumePlan;
        _onEndPlan          = onEndPlan;
        _onApproveGate      = onApproveGate;

        Title     = plan.Group.GroupTitle;
        Width     = 1200;
        Height    = 720;
        MinWidth  = 760;
        MinHeight = 480;

        BuildContent(plan, durablePlan);
    }

    private void BuildContent(PendingDecomposePlan plan, Plan? durablePlan)
    {
        var activeBranch       = _activeBranch;
        var quickReplyFontSize = _quickReplyFontSize;
        var applyAction        = _applyAction;
        var onGatesChanged     = _onGatesChanged;
        var onResumePlan       = _onResumePlan;
        var onEndPlan          = _onEndPlan;
        var onApproveGate      = _onApproveGate;
        var group = plan.Group;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(22, 16, 22, 10) };

        if (applyAction is not null)
        {
            var actionsPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
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
            header.Children.Add(actionsPanel);
        }

        if (durablePlan is not null &&
            durablePlan.LifecycleStatus == PlanLifecycleStatus.Interrupted &&
            (onResumePlan is not null || onEndPlan is not null))
        {
            var interruptedPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 0, 0, 8),
            };
            if (onResumePlan is not null)
            {
                var capturedPlan   = durablePlan;
                var capturedAction = onResumePlan;
                var resumeButton   = TranscriptQuickReplyFactory.CreateButton(
                    "Resume Plan",
                    quickReplyFontSize,
                    toolTip: ToolTipHelper.MakeThemedToolTip("Resume executing this interrupted plan from where it left off."));
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

        var summaryBlock = new TextBlock
        {
            Text         = group.Summary,
            TextWrapping = TextWrapping.Wrap,
            FontWeight   = FontWeights.SemiBold,
            Margin       = new Thickness(0, 0, 0, 6),
        };
        summaryBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        summaryBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        header.Children.Add(summaryBlock);

        var hintBlock = new TextBlock
        {
            Text         = "Arrows point from prerequisite → dependent.  ALL means every incoming task must finish.  Tasks in the same stage with no arrow between them are independent and may run in any order.",
            TextWrapping = TextWrapping.Wrap,
        };
        hintBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        hintBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        header.Children.Add(hintBlock);

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

        var canvas = new Canvas { Background = Brushes.Transparent, Margin = new Thickness(18) };
        var scroll = new ScrollViewer
        {
            Content = canvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
        };
        _graphScroll = scroll;
        scroll.SetResourceReference(ScrollViewer.StyleProperty,      "RosterScrollViewerStyle");
        scroll.SetResourceReference(ScrollViewer.BackgroundProperty, "CardSurface");
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        _contentHolder ??= ApplyOuterBorder(titleText: group.GroupTitle);
        _contentHolder.Child = root;

        var tasksById = group.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        var levels = CalculateLevels(group.Tasks, tasksById);
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        var columns = group.Tasks.GroupBy(task => levels[task.Id]).OrderBy(column => column.Key).ToArray();
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
            (string.IsNullOrWhiteSpace(gate.PresentationAnchor) ||
             string.Equals(gate.PresentationAnchor, anchor, StringComparison.Ordinal));

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
                $"Covered by this {label} approval requirement. Click here to **clear** it.",
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
            var x = 42 + column.Key * ColumnSpacing;

            var mainTitle    = $"Stage {column.Key + 1}";

            var titleBlock = new TextBlock
            {
                Text       = mainTitle,
                FontWeight = FontWeights.SemiBold,
            };
            titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TitleText");
            titleBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeHeading");

            UIElement headerElement = titleBlock;

            Canvas.SetLeft(headerElement, x);
            Canvas.SetTop(headerElement, 10);
            canvas.Children.Add(headerElement);

            for (var row = 0; row < tasks.Length; row++)
                positions[tasks[row].Id] = new Point(x, 68 + row * RowSpacing);
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
            var gateCenter       = new Point((sourceRight + targetLeft) / 2, centers.Average());
            var minTargetLevel   = targets.Min(t => levels[t.Id]);
            var maxDepLevel      = dependencies.Where(positions.ContainsKey).Max(id => levels[id]);
            gates.Add((gateCenter, targets, dependencies, minTargetLevel, maxDepLevel));
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

        // Compute a uniform band height from the tallest stage (most tasks), with 10px padding.
        var globalBandTop = columns.SelectMany(col => col).Min(task => positions[task.Id].Y) - 10;
        var globalBandBottom = columns.SelectMany(col => col).Max(task => positions[task.Id].Y + NodeHeight) + 10;

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

            var leftTasks = leftColumn.ToArray();
            var leftX = positions[leftTasks[0].Id].X;
            var nextX = positions[columns[columnIndex + 1].First().Id].X;
            var boundaryX = (leftX + NodeWidth + nextX) / 2.0;
            if (isLocked) lockedMilestoneBoundaryXs.Add(boundaryX);
            var milestoneBand = new Border
            {
                Width        = 24,
                Height       = Math.Max(1, globalBandBottom - globalBandTop),
                CornerRadius = new CornerRadius(4),
                Opacity      = isLocked ? 0.90 : 0.56,
                ToolTip      = "Stage milestone boundary",
            };
            milestoneBand.SetResourceReference(Border.BackgroundProperty, "ActivePanelBorder");
            Canvas.SetLeft(milestoneBand, boundaryX - 12);
            Canvas.SetTop(milestoneBand, globalBandTop);
            Panel.SetZIndex(milestoneBand, -2);
            canvas.Children.Add(milestoneBand);

            var milestoneStop = CreateApprovalStop(
                isLocked,
                onGatesChanged is null
                    ? isLocked
                        ? "Preview: human approval is required at this stage milestone."
                        : "Preview: this stop controls approval at the stage milestone."
                    : isLocked
                        ? milestoneIsPrimary
                            ? "Human approval is required after the stage to the left completes and before the next stage begins. Click to remove."
                            : "This is an equivalent view of the approval boundary. Click to make this the primary control."
                        : "Require human approval after the stage to the left completes and before the next stage begins.",
                onGatesChanged is null
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
                isLocked && !milestoneIsPrimary ? 0.5 : 1.0);
            Canvas.SetLeft(milestoneStop, boundaryX - 8);
            Canvas.SetTop(milestoneStop, 10);
            Panel.SetZIndex(milestoneStop, 25);
            canvas.Children.Add(milestoneStop);
            approvalControlsByAnchor[milestoneAnchor] = milestoneStop;
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
            if (!string.IsNullOrWhiteSpace(gate.PresentationAnchor)) return gate.PresentationAnchor;
            for (var i = 0; i < columns.Length - 1; i++)
            {
                var after = columns[i].Select(task => task.Id).ToArray();
                var before = columns[i + 1].Select(task => task.Id).ToArray();
                if (SameBoundary(gate.AfterTaskIds, gate.BeforeTaskIds, after, before))
                    return StageAnchor(i + 1);
            }
            foreach (var allGate in gates)
            {
                var before = allGate.Targets.Select(task => task.Id).ToArray();
                if (SameBoundary(gate.AfterTaskIds, gate.BeforeTaskIds, allGate.Dependencies, before))
                    return AllAnchor(before);
            }
            if (gate.AfterTaskIds.Count == 1) return TaskAfterAnchor(gate.AfterTaskIds[0]);
            if (gate.BeforeTaskIds.Count == 1) return TaskBeforeAnchor(gate.BeforeTaskIds[0]);
            return null;
        }

        var visualizationTasks = durablePlan?.Tasks ?? group.Tasks.Select(task => new PlanTask(
            task.Id, task.Title, task.Description, task.DependsOn, task.Priority,
            PlanTaskStatus.Pending)).ToArray();
        var visualizationGates = durablePlan?.ApprovalGates ?? (group.ApprovalGates ?? [])
            .Select(gate => new PlanApprovalGate(
                gate.GateId, gate.Message, gate.AfterTaskIds ?? [], gate.BeforeTaskIds ?? [],
                PlanGateStatus.Pending)).ToArray();
        var dashedTaskEdges = PlanGateVisualizationPolicy.DashedEdges(
            visualizationTasks,
            visualizationGates,
            requireEveryIncomingAtConvergence: true);

        // Pass 3: scan every edge to build sorted per-task exit/entry Y lists for spread rendering.
        // When a task has N connectors leaving its right edge, they are spread at heights
        // NodeHeight * k/(N+1) for k = 1..N (sorted top-to-bottom by destination Y).
        var rightExitYs  = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var leftEntryYs  = new Dictionary<string, List<double>>(StringComparer.Ordinal);

        void RegisterExit(string taskId, double otherY)
        {
            if (!rightExitYs.TryGetValue(taskId, out var list)) rightExitYs[taskId] = list = [];
            list.Add(otherY);
        }
        void RegisterEntry(string taskId, double otherY)
        {
            if (!leftEntryYs.TryGetValue(taskId, out var list)) leftEntryYs[taskId] = list = [];
            list.Add(otherY);
        }

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
        foreach (var list in rightExitYs.Values)  list.Sort();
        foreach (var list in leftEntryYs.Values)   list.Sort();

        double SpreadExitY(string taskId, double otherY)
        {
            if (!rightExitYs.TryGetValue(taskId, out var list) || list.Count <= 1)
                return positions[taskId].Y + NodeHeight / 2.0;
            var idx = list.IndexOf(otherY);
            return positions[taskId].Y + NodeHeight * (Math.Max(0, idx) + 1.0) / (list.Count + 1);
        }
        double SpreadEntryY(string taskId, double otherY)
        {
            if (!leftEntryYs.TryGetValue(taskId, out var list) || list.Count <= 1)
                return positions[taskId].Y + NodeHeight / 2.0;
            var idx = list.IndexOf(otherY);
            return positions[taskId].Y + NodeHeight * (Math.Max(0, idx) + 1.0) / (list.Count + 1);
        }

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

        // Draw non-gated direct connectors.
        foreach (var task in group.Tasks.Where(task => !gatedTaskIds.Contains(task.Id)))
        {
            foreach (var dependency in task.DependsOn.Where(positions.ContainsKey))
            {
                var source    = positions[dependency];
                var target    = positions[task.Id];
                var skipCount = Math.Max(0, levels[task.Id] - levels[dependency] - 1);
                var fromPt    = new Point(source.X + NodeWidth, SpreadExitY(dependency, target.Y + NodeHeight / 2.0));
                var toPt      = new Point(target.X,             SpreadEntryY(task.Id,   source.Y + NodeHeight / 2.0));
                var cg = AddConnector(canvas,
                    fromPt, toPt,
                    arrowHead: true,
                    skipCount: skipCount,
                    dashed: dashedTaskEdges.Contains((dependency, task.Id)),
                    splitAtX: FindSplitX(fromPt.X, toPt.X));
                RegisterConnector(dependency, cg);
                RegisterConnector(task.Id,   cg);
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
            var badgeText = new TextBlock
            {
                Text                = "ALL",
                FontWeight          = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                Margin              = new Thickness(0, 0, 10, 0),
            };
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, "ActivePanelTitle");
            badgeText.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            var badgeContent = new Grid();
            badgeContent.Children.Add(badgeText);

            {
                var joinStop = CreateApprovalStop(
                    joinIsLocked,
                    onGatesChanged is null
                        ? joinIsLocked
                            ? "Preview: human approval is required at this ALL join."
                            : "Preview: this stop controls approval at the ALL join."
                        : collectivelyCoveredJoin
                            ? "Every incoming path is approved separately. Click to consolidate them into this ALL approval requirement."
                        : coveringJoinGate is not null
                            ? "This ALL join is covered by a larger approval requirement."
                        : joinIsLocked
                            ? joinIsPrimary
                                ? "Human approval is required after every incoming task completes and before joined work begins. Click to remove."
                                : "This is an equivalent view of the approval boundary. Click to make this the primary control."
                            : "Require human approval after every incoming task completes and before joined work begins.",
                    onGatesChanged is null
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
                                     collectivelyCoveredJoin) ? 0.5 : 1.0);
                joinStop.HorizontalAlignment = HorizontalAlignment.Right;
                joinStop.VerticalAlignment = VerticalAlignment.Center;
                joinStop.Margin = new Thickness(0, 0, 4, 0);
                badgeContent.Children.Add(joinStop);
                approvalControlsByAnchor[joinAnchor] = joinStop;
            }

            var badge = new Border
            {
                Width           = 58,
                Height          = 34,
                CornerRadius    = new CornerRadius(17),
                BorderThickness = new Thickness(1.5),
                ToolTip         = "ALL prerequisites entering this gate must finish before any outgoing task can begin.",
                Child           = badgeContent,
            };
            badge.SetResourceReference(Border.BorderBrushProperty, "ActivePanelBorder");
            badge.SetResourceReference(Border.BackgroundProperty,  "CardSurface");
            Canvas.SetLeft(badge, gate.Center.X - 29);
            Canvas.SetTop(badge, gate.Center.Y - 17);
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

        foreach (var task in group.Tasks)
        {
            var position = positions[task.Id];
            var durableTask = durablePlan?.Tasks.FirstOrDefault(t =>
                string.Equals(t.TaskId, task.Id, StringComparison.Ordinal));
            var prereqLines = task.DependsOn.Count == 0
                ? ["None — this task can start immediately."]
                : task.DependsOn.Select(id =>
                {
                    if (!tasksById.TryGetValue(id, out var dep)) return $"• {id}";
                    var label = dep.Title ?? dep.Description;
                    return "• " + (label.Length > 60 ? label[..60] + "…" : label);
                }).ToArray();

            string? statusChipText = durableTask?.Status switch
            {
                PlanTaskStatus.Complete   or
                PlanTaskStatus.Superseded => "✓ ",
                PlanTaskStatus.Executing  => "▶ ",
                PlanTaskStatus.Failed     => "✖ ",
                PlanTaskStatus.Partial    => "~ ",
                _                        => null,
            };
            string? statusChipFgKey = durableTask?.Status switch
            {
                PlanTaskStatus.Complete   or
                PlanTaskStatus.Superseded => "PriorityLow",
                PlanTaskStatus.Executing  => "ActivePanelTitle",
                PlanTaskStatus.Failed     => "PriorityHigh",
                PlanTaskStatus.Partial    => "PriorityMid",
                _                        => null,
            };
            string borderColorKey = durableTask?.Status switch
            {
                PlanTaskStatus.Complete   or
                PlanTaskStatus.Superseded => "PriorityLow",
                PlanTaskStatus.Executing  => "ActivePanelBorder",
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

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            if (statusChipText is not null && statusChipFgKey is not null)
            {
                var chip = new TextBlock
                {
                    Text              = statusChipText,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(0, 0, 2, 0),
                    FontWeight        = FontWeights.SemiBold,
                };
                chip.SetResourceReference(TextBlock.ForegroundProperty, statusChipFgKey);
                chip.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
                titleRow.Children.Add(chip);
            }
            titleRow.Children.Add(nodeTitle);

            var nodeDescription = new TextBlock
            {
                Text         = task.Description,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight    = 34,
                Margin       = new Thickness(0, 5, 0, 0),
            };
            nodeDescription.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            nodeDescription.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            var content = new StackPanel();
            content.Children.Add(titleRow);
            content.Children.Add(nodeDescription);

            if (durableTask?.Commit is { } commitSha && commitSha.Length > 0)
            {
                var shortSha = commitSha.Length >= 7 ? commitSha[..7] : commitSha;
                var commitBlock = new TextBlock
                {
                    Text       = $"[{shortSha}]",
                    Margin     = new Thickness(0, 2, 0, 0),
                    FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                };
                commitBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                commitBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
                content.Children.Add(commitBlock);
            }

            var border = new Border
            {
                Width           = NodeWidth,
                Height          = NodeHeight,
                Padding         = new Thickness(11, 8, 11, 8),
                CornerRadius    = new CornerRadius(7),
                BorderThickness = new Thickness(1.25),
                ToolTip         = BuildTaskToolTip(task.Description, prereqLines, durableTask?.CompletionSummary, durableTask?.Commit),
                Child           = content,
            };
            border.SetResourceReference(Border.BackgroundProperty,  "CardSurface");
            border.SetResourceReference(Border.BorderBrushProperty, borderColorKey);
            Canvas.SetLeft(border, position.X);
            Canvas.SetTop(border, position.Y);

            if (durablePlan is not null && onGatesChanged is not null)
            {
                var capturedTask = task;
                var addBeforeItem = new MenuItem { Header = "Require approval before this task" };
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

                var addAfterItem = new MenuItem { Header = "Require approval after this task" };
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
                        var removeItem = new MenuItem
                        {
                            Header = $"Remove approval gate: {capturedGate.Message}",
                        };
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

            borderByTask[task.Id] = border;

            // Task entry/exit approval stops. Root tasks have no meaningful entry boundary;
            // leaf tasks have no meaningful exit boundary, so those controls are omitted.
            if (durablePlan is not null && onGatesChanged is not null)
            {
                var capturedTaskForStop = task;
                var isRoot = PlanGateManager.IsRootTask(durablePlan, capturedTaskForStop.Id);
                var isLeaf = PlanGateManager.IsLeafTask(durablePlan, capturedTaskForStop.Id);

                if (!isRoot)
                {
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
                    var beforeStop = CreateApprovalStop(
                        beforeEngaged,
                        collectivelyCoveredEntry
                            ? "This task entry is covered by every incoming approval requirement."
                        : coveringBeforeGate is not null
                            ? "This task entry is covered by a larger approval requirement."
                            : beforeEngaged
                            ? beforeIsPrimary
                                ? "Human approval is required before this task begins. Click to remove."
                                : "This is an equivalent view of the approval boundary. Click to make this the primary control."
                            : "Require human approval before this task begins.",
                        () =>
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
                                          collectivelyCoveredEntry) ? 0.5 : 1.0);
                    Canvas.SetLeft(beforeStop, position.X + 6);
                    Canvas.SetTop(beforeStop, position.Y + NodeHeight - 20);
                    Panel.SetZIndex(beforeStop, 25);
                    canvas.Children.Add(beforeStop);
                    approvalControlsByAnchor[beforeAnchor] = beforeStop;
                }

                if (!isLeaf)
                {
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
                    var afterStop = CreateApprovalStop(
                        afterEngaged,
                        collectivelyCoveredByAllJoins
                            ? "This task exit is covered by its enabled ALL approval requirements."
                        : coveringAfterGate is not null
                            ? "This task exit is covered by a larger approval requirement."
                            : afterEngaged
                            ? afterIsPrimary
                                ? "Human approval is required after this task completes. Click to remove."
                                : "This is an equivalent view of the approval boundary. Click to make this the primary control."
                            : "Require human approval after this task completes.",
                        () =>
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
                                         collectivelyCoveredByAllJoins) ? 0.5 : 1.0);
                    Canvas.SetLeft(afterStop, position.X + NodeWidth - 22);
                    Canvas.SetTop(afterStop, position.Y + NodeHeight - 20);
                    Panel.SetZIndex(afterStop, 25);
                    canvas.Children.Add(afterStop);
                    approvalControlsByAnchor[afterAnchor] = afterStop;
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
                    Canvas.SetLeft(beforeStop, position.X + 6);
                    Canvas.SetTop(beforeStop, position.Y + NodeHeight - 20);
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
                    Canvas.SetLeft(afterStop, position.X + NodeWidth - 22);
                    Canvas.SetTop(afterStop, position.Y + NodeHeight - 20);
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

        canvas.Width  = positions.Values.Max(point => point.X) + NodeWidth + 70;
        canvas.Height = positions.Values.Max(point => point.Y) + NodeHeight + 70;

        if (durablePlan is not null)
        {
            var approvalSummary = BuildApprovalSummaryPanel(durablePlan, levels);
            Grid.SetRow(approvalSummary, 2);
            root.Children.Add(approvalSummary);
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
        Title = plan.Group.GroupTitle;
    }

    private static FrameworkElement BuildApprovalSummaryPanel(
        Plan plan, IReadOnlyDictionary<string, int> levels)
    {
        var title = new TextBlock
        {
            Text = "Human approval requirements",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 7, 10, 3),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");

        var document = new FlowDocument
        {
            PagePadding = new Thickness(10, 2, 10, 8),
            ColumnWidth = double.PositiveInfinity,
            FontFamily  = new FontFamily("Segoe UI, Segoe UI Emoji"),
        };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "LabelText");
        document.SetResourceReference(FlowDocument.BackgroundProperty, "CardSurface");
        document.SetResourceReference(FlowDocument.FontSizeProperty, "FontSizeBody");

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
            MaxHeight = 170,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
        };
        viewer.SetResourceReference(FlowDocumentScrollViewer.BackgroundProperty, "CardSurface");
        viewer.SetResourceReference(FlowDocumentScrollViewer.ForegroundProperty, "LabelText");
        viewer.SetResourceReference(FlowDocumentScrollViewer.FontSizeProperty, "FontSizeBody");

        var stack = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        stack.Children.Add(title);
        stack.Children.Add(viewer);

        var border = new Border
        {
            Child = stack,
            BorderThickness = new Thickness(1, 1, 0, 0),
            Margin = new Thickness(0),
        };
        border.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        border.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        return border;
    }

    private static FrameworkElement CreateApprovalStop(
        bool engaged, string toolTip, Action? toggle, double engagedOpacity = 1.0)
    {
        var stop = new Polygon
        {
            Points =
            [
                new Point(5, 1), new Point(11, 1), new Point(15, 5), new Point(15, 11),
                new Point(11, 15), new Point(5, 15), new Point(1, 11), new Point(1, 5),
            ],
            StrokeThickness = 1.6,
            Fill = engaged ? new SolidColorBrush(Color.FromRgb(0xC9, 0x4B, 0x4B)) : Brushes.Transparent,
            StrokeLineJoin = PenLineJoin.Round,
            Stretch = Stretch.None,
        };
        stop.SetResourceReference(Shape.StrokeProperty, "LineColor");

        var hitTarget = new Grid
        {
            Width = 16,
            Height = 16,
            Background = Brushes.Transparent,
            Cursor = toggle is null ? Cursors.Arrow : Cursors.Hand,
            ToolTip = ToolTipHelper.MakeThemedToolTip(toolTip),
            Opacity = engaged ? engagedOpacity : 1.0,
        };
        hitTarget.Children.Add(stop);
        if (toggle is not null)
        {
            hitTarget.MouseEnter += (_, _) => stop.StrokeThickness = 2.2;
            hitTarget.MouseLeave += (_, _) => stop.StrokeThickness = 1.6;
            hitTarget.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                toggle();
            };
        }
        return hitTarget;
    }

    private static ToolTip BuildTaskToolTip(string description, string[] prereqLines, string? completionSummary = null, string? commit = null)
    {
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
    }

    private static ConnectorGroup AddConnector(Canvas canvas, Point from, Point to, bool arrowHead, int skipCount = 0, bool dashed = false, string? toolTip = null, double splitAtX = double.NaN)
    {
        var group = new ConnectorGroup();

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

        if (skipCount > 0 || Math.Abs(to.Y - from.Y) < 1.0)
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
            var hitLine = new Line
            {
                X1 = from.X, Y1 = from.Y, X2 = lineEnd.X, Y2 = lineEnd.Y,
                StrokeThickness = 12, Stroke = Brushes.White, Opacity = 0.01,
            };
            if (toolTip is not null) hitLine.ToolTip = toolTip;
            canvas.Children.Add(hitLine);
            group.MainElements.Add(hitLine);
        }
        else
        {
            // S-curve Bézier with horizontal tangents at both endpoints.
            double dx        = lineEnd.X - from.X;
            double handleLen = Math.Max(dx * 0.5, 40.0);
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
            var hitPath = new Path
            {
                Data = MakeBezierGeometry(), StrokeThickness = 12, Stroke = Brushes.White,
                Fill = Brushes.Transparent, Opacity = 0.01,
            };
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

    // Base hue for adjacent-stage connectors. Each skipped stage rotates the hue by 45°.
    private const double ConnectorBaseHue   = 210.0;
    private const double ConnectorSaturation = 0.70;
    private const double ConnectorLightness  = 0.45;

    private static Color ConnectorColor(int skipCount)
    {
        var hue = (ConnectorBaseHue + skipCount * 45.0) % 360.0;
        return HslToRgb(hue, ConnectorSaturation, ConnectorLightness);
    }

    // Very light variant of the connector color — the glow halo shown on hover.
    private static Color ConnectorGlowColor(int skipCount)
    {
        var hue = (ConnectorBaseHue + skipCount * 45.0) % 360.0;
        return HslToRgb(hue, 0.95, 0.88);
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
}
