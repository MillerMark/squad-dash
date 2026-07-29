using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        var group = plan.Group;
        Title     = group.GroupTitle;
        Width     = 1200;
        Height    = 720;
        MinWidth  = 760;
        MinHeight = 480;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

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

        _contentHolder = ApplyOuterBorder(titleText: group.GroupTitle);
        _contentHolder.Child = root;

        var tasksById = group.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        var levels = CalculateLevels(group.Tasks, tasksById);
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        var columns = group.Tasks.GroupBy(task => levels[task.Id]).OrderBy(column => column.Key).ToArray();

        PlanApprovalGate? FindDurableGate(
            IReadOnlyList<string> afterIds,
            IReadOnlyList<string> beforeIds) =>
            durablePlan is null
                ? null
                : PlanGateManager.FindEquivalentGate(durablePlan, afterIds, beforeIds);

        string[] DirectDependents(string taskId) => durablePlan?.Tasks
            .Where(task => task.DependsOn.Contains(taskId, StringComparer.Ordinal))
            .Select(task => task.TaskId)
            .ToArray() ?? [];

        PlanApprovalGate? FindTaskGateAfter(string taskId) =>
            FindDurableGate([taskId], DirectDependents(taskId));

        foreach (var column in columns)
        {
            var tasks = column.ToArray();
            var x = 42 + column.Key * ColumnSpacing;

            var mainTitle    = $"Stage {column.Key + 1}";
            var subtitle     = tasks.Length == 1 ? null : $"{tasks.Length} independent tasks";

            var titleBlock = new TextBlock
            {
                Text       = mainTitle,
                FontWeight = FontWeights.SemiBold,
            };
            titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            titleBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeHeading");

            UIElement headerElement;
            if (subtitle is not null)
            {
                var subtitleBlock = new TextBlock { Text = subtitle };
                subtitleBlock.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                subtitleBlock.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSubtitle");

                var stack = new StackPanel { Orientation = Orientation.Vertical };
                stack.Children.Add(titleBlock);
                stack.Children.Add(subtitleBlock);
                headerElement = stack;
            }
            else
            {
                headerElement = titleBlock;
            }

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

        // A stage milestone is one global boundary: every task in columns to its left must
        // complete before any task in columns to its right may proceed.
        var stageBoundaries = new List<(string[] AfterIds, string[] BeforeIds)>();
        for (var columnIndex = 0; columnIndex < columns.Length - 1; columnIndex++)
        {
            var leftColumn = columns[columnIndex];
            var afterIds = group.Tasks
                .Where(task => levels[task.Id] <= leftColumn.Key)
                .Select(task => task.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            var beforeIds = group.Tasks
                .Where(task => levels[task.Id] > leftColumn.Key)
                .Select(task => task.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            stageBoundaries.Add((afterIds, beforeIds));

            var existingGate = FindDurableGate(afterIds, beforeIds);
            var displayedGate = (group.ApprovalGates ?? []).FirstOrDefault(gate =>
                SameBoundary(gate.AfterTaskIds ?? [], gate.BeforeTaskIds ?? [], afterIds, beforeIds));
            var isLocked = existingGate is not null || displayedGate is not null;

            var leftTasks = leftColumn.ToArray();
            var leftX = positions[leftTasks[0].Id].X;
            var nextX = positions[columns[columnIndex + 1].First().Id].X;
            var boundaryX = (leftX + NodeWidth + nextX) / 2.0;
            var bandTop = leftTasks.Min(task => positions[task.Id].Y);
            var bandBottom = leftTasks.Max(task => positions[task.Id].Y + NodeHeight);
            var milestoneBand = new Border
            {
                Width        = 30,
                Height       = Math.Max(1, bandBottom - bandTop),
                CornerRadius = new CornerRadius(15),
                Opacity      = isLocked ? 0.45 : 0.28,
                ToolTip      = "Stage milestone boundary",
            };
            milestoneBand.SetResourceReference(Border.BackgroundProperty, "ActivePanelBorder");
            Canvas.SetLeft(milestoneBand, boundaryX - 15);
            Canvas.SetTop(milestoneBand, bandTop);
            Panel.SetZIndex(milestoneBand, -2);
            canvas.Children.Add(milestoneBand);

            var milestoneLock = new TextBlock
            {
                Text    = isLocked ? "🔒" : "🔓",
                Opacity = isLocked ? 1.0 : 0.25,
                Cursor  = onGatesChanged is null ? Cursors.Arrow : Cursors.Hand,
                ToolTip = onGatesChanged is null
                    ? isLocked
                        ? "Preview: human approval is required at this stage milestone."
                        : "Preview: this padlock controls approval at the stage milestone."
                    : isLocked
                        ? "Human approval is required after all work to the left completes and before any work to the right begins. Click to remove."
                        : "Require human approval after all work to the left completes and before any work to the right begins.",
            };
            milestoneLock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
            Canvas.SetLeft(milestoneLock, boundaryX - 10);
            Canvas.SetTop(milestoneLock, 10);
            Panel.SetZIndex(milestoneLock, 25);
            if (onGatesChanged is not null)
            {
                milestoneLock.MouseEnter += (_, _) => milestoneLock.Opacity = 1.0;
                milestoneLock.MouseLeave += (_, _) => milestoneLock.Opacity = isLocked ? 1.0 : 0.25;
                milestoneLock.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    var updated = isLocked && existingGate is not null
                        ? PlanGateManager.RemoveGate(durablePlan!, existingGate.GateId)
                        : PlanGateManager.AddBoundaryGate(
                            durablePlan!,
                            afterIds,
                            beforeIds,
                            $"Review milestone before Stage {leftColumn.Key + 2}");
                    if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                };
            }
            canvas.Children.Add(milestoneLock);
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

        bool IsTaskBoundary(DecomposedGate gate) => gate.AfterTaskIds?.Count == 1 &&
            SameBoundary(
                gate.AfterTaskIds ?? [],
                gate.BeforeTaskIds ?? [],
                gate.AfterTaskIds ?? [],
                group.Tasks
                    .Where(task => task.DependsOn.Contains(gate.AfterTaskIds![0], StringComparer.Ordinal))
                    .Select(task => task.Id)
                    .ToArray());

        // Pass 2: collect task-scoped and legacy approval edges. Stage and ALL-join boundaries
        // have dedicated visuals and must not produce an all-to-all dashed connector mesh.
        var lockedAfterTaskIds = new HashSet<string>(StringComparer.Ordinal);
        var approvalDirectPairs = new List<(string AfterId, string BeforeId, DecomposedGate Gate)>();
        if (group.ApprovalGates is { Count: > 0 })
        {
            foreach (var approvalGate in group.ApprovalGates)
            {
                if (IsStageBoundary(approvalGate) || IsAllJoinBoundary(approvalGate))
                    continue;
                var taskBoundary = IsTaskBoundary(approvalGate);
                foreach (var afterId in approvalGate.AfterTaskIds ?? [])
                {
                    if (taskBoundary) lockedAfterTaskIds.Add(afterId);
                    foreach (var beforeId in approvalGate.BeforeTaskIds ?? [])
                        approvalDirectPairs.Add((afterId, beforeId, approvalGate));
                }
            }
        }

        // Map each AfterTaskId to the icon it should display based on the most-restrictive gate status.
        var lockIconByTask = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var afterId in lockedAfterTaskIds)
        {
            var worstIcon = (group.ApprovalGates ?? [])
                .Where(g => g.AfterTaskIds?.Contains(afterId, StringComparer.Ordinal) ?? false)
                .Select(g =>
                {
                    var durableGate = durablePlan?.ApprovalGates.FirstOrDefault(dg =>
                        string.Equals(dg.GateId, g.GateId, StringComparison.Ordinal));
                    return durableGate?.Status ?? PlanGateStatus.Pending;
                })
                .OrderByDescending(s => s switch
                {
                    PlanGateStatus.AwaitingApproval => 2,
                    PlanGateStatus.Pending          => 1,
                    _                               => 0,
                })
                .Select(s => s switch
                {
                    PlanGateStatus.AwaitingApproval => "⏸",
                    PlanGateStatus.Approved         => null,
                    PlanGateStatus.Skipped          => null,
                    _                               => "🔒",
                })
                .FirstOrDefault();
            if (worstIcon is not null)
                lockIconByTask[afterId] = worstIcon;
        }

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
        foreach (var (afterId, beforeId, _) in approvalDirectPairs)
        {
            if (positions.ContainsKey(afterId) && positions.ContainsKey(beforeId))
            {
                RegisterExit(afterId,   positions[beforeId].Y + NodeHeight / 2.0);
                RegisterEntry(beforeId, positions[afterId].Y  + NodeHeight / 2.0);
            }
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

        // Draw ALL-gate connectors; collect per-gate groups so the badge can reference them later.
        var gateConnectorGroups = new List<List<ConnectorGroup>>(gates.Count);
        foreach (var (gateCenter, targets, dependencies, minTargetLevel, maxDepLevel) in gates)
        {
            var cgsForGate = new List<ConnectorGroup>();
            var joinBeforeIds = targets.Select(task => task.Id).ToArray();
            var joinIsLocked = FindDurableGate(dependencies, joinBeforeIds) is not null ||
                               FindDisplayedGate(dependencies, joinBeforeIds) is not null;
            // Outbound is dashed when the whole gate is locked OR any individual dep is locked.
            // Inbound connectors are never dashed — the approval barrier is AFTER the gate, not before it.
            var outboundDashed = joinIsLocked ||
                                 dependencies.Any(dep => lockedAfterTaskIds.Contains(dep));
            foreach (var dependency in dependencies.Where(positions.ContainsKey))
            {
                var source  = positions[dependency];
                var depSkip = minTargetLevel - levels[dependency] - 1;
                var cg = AddConnector(canvas,
                    new Point(source.X + NodeWidth, SpreadExitY(dependency, gateCenter.Y)),
                    new Point(gateCenter.X - 29, gateCenter.Y),
                    arrowHead: false,
                    skipCount: Math.Max(0, depSkip),
                    dashed: false);
                RegisterConnector(dependency, cg);
                cgsForGate.Add(cg);
            }
            foreach (var target in targets)
            {
                var targetPoint = positions[target.Id];
                var targetSkip  = levels[target.Id] - maxDepLevel - 1;
                var cg = AddConnector(canvas,
                    new Point(gateCenter.X + 29, gateCenter.Y),
                    new Point(targetPoint.X, SpreadEntryY(target.Id, gateCenter.Y)),
                    arrowHead: true,
                    skipCount: Math.Max(0, targetSkip),
                    dashed: outboundDashed);
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
                var cg = AddConnector(canvas,
                    new Point(source.X + NodeWidth, SpreadExitY(dependency, target.Y + NodeHeight / 2.0)),
                    new Point(target.X,             SpreadEntryY(task.Id,   source.Y + NodeHeight / 2.0)),
                    arrowHead: true,
                    skipCount: skipCount,
                    dashed: lockedAfterTaskIds.Contains(dependency));
                RegisterConnector(dependency, cg);
                RegisterConnector(task.Id,   cg);
            }
        }

        // Draw direct dashed approval connectors (replacing the old gate-node-mediated path).
        foreach (var (afterId, beforeId, approvalGate) in approvalDirectPairs)
        {
            if (!positions.ContainsKey(afterId) || !positions.ContainsKey(beforeId)) continue;
            var afterPos  = positions[afterId];
            var beforePos = positions[beforeId];
            var durableGate = durablePlan?.ApprovalGates.FirstOrDefault(g =>
                string.Equals(g.GateId, approvalGate.GateId, StringComparison.Ordinal));
            var gateToolTip = durableGate is not null
                ? $"{approvalGate.Message}\nStatus: {durableGate.Status}"
                : approvalGate.Message;
            var cg = AddConnector(canvas,
                new Point(afterPos.X + NodeWidth, SpreadExitY(afterId,   beforePos.Y + NodeHeight / 2.0)),
                new Point(beforePos.X,            SpreadEntryY(beforeId, afterPos.Y  + NodeHeight / 2.0)),
                arrowHead: true,
                skipCount: 0,
                dashed: true,
                toolTip: gateToolTip);
            RegisterConnector(afterId,  cg);
            RegisterConnector(beforeId, cg);
        }

        for (int gi = 0; gi < gates.Count; gi++)
        {
            var gate = gates[gi];
            var joinAfterIds = gate.Dependencies;
            var joinBeforeIds = gate.Targets.Select(task => task.Id).ToArray();
            var existingJoinGate = FindDurableGate(joinAfterIds, joinBeforeIds);
            var displayedJoinGate = FindDisplayedGate(joinAfterIds, joinBeforeIds);
            var joinIsLocked = existingJoinGate is not null || displayedJoinGate is not null;
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
                var joinLock = new TextBlock
                {
                    Text                = joinIsLocked ? "🔒" : "🔓",
                    Opacity             = joinIsLocked ? 1.0 : 0.25,
                    Cursor              = onGatesChanged is null ? Cursors.Arrow : Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment   = VerticalAlignment.Center,
                    Margin              = new Thickness(0, 0, 4, 0),
                    ToolTip             = onGatesChanged is null
                        ? joinIsLocked
                            ? "Preview: human approval is required at this ALL join."
                            : "Preview: this padlock controls approval at the ALL join."
                        : joinIsLocked
                            ? "Human approval is required after every incoming task completes and before joined work begins. Click to remove."
                            : "Require human approval after every incoming task completes and before joined work begins.",
                };
                joinLock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
                if (onGatesChanged is not null)
                {
                    joinLock.MouseEnter += (_, _) => joinLock.Opacity = 1.0;
                    joinLock.MouseLeave += (_, _) => joinLock.Opacity = joinIsLocked ? 1.0 : 0.25;
                    joinLock.MouseLeftButtonDown += (_, e) =>
                    {
                        e.Handled = true;
                        var updated = joinIsLocked && existingJoinGate is not null
                            ? PlanGateManager.RemoveGate(durablePlan!, existingJoinGate.GateId)
                            : PlanGateManager.AddBoundaryGate(
                                durablePlan!,
                                joinAfterIds,
                                joinBeforeIds,
                                $"Review joined work before: {string.Join(", ", gate.Targets.Select(task => task.Title ?? task.Id))}");
                        if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                    };
                }
                badgeContent.Children.Add(joinLock);
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
                addBeforeItem.IsEnabled = !PlanGateManager.IsRootTask(durablePlan, capturedTask.Id);
                addBeforeItem.Click += (_, _) =>
                {
                    var msg = SimpleInputDialog.Show(this,
                        "Enter a message for this approval gate:",
                        "Require Approval Before",
                        $"Review before: {capturedTask.Title ?? capturedTask.Id}");
                    if (msg is null) return;
                    var updated = PlanGateManager.AddGateBefore(durablePlan, capturedTask.Id, msg);
                    if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                };

                var addAfterItem = new MenuItem { Header = "Require approval after this task" };
                addAfterItem.IsEnabled = !PlanGateManager.IsLeafTask(durablePlan, capturedTask.Id);
                addAfterItem.Click += (_, _) =>
                {
                    var msg = SimpleInputDialog.Show(this,
                        "Enter a message for this approval gate:",
                        "Require Approval After",
                        $"Review after: {capturedTask.Title ?? capturedTask.Id}");
                    if (msg is null) return;
                    var updated = PlanGateManager.AddGateAfter(durablePlan, capturedTask.Id, msg);
                    if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                };

                var contextMenu = new ContextMenu();
                contextMenu.Items.Add(addBeforeItem);
                contextMenu.Items.Add(addAfterItem);

                var gatesForTask = (group.ApprovalGates ?? [])
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

            // Lock icon overlay: clickable toggle when editable; status badge otherwise.
            if (durablePlan is not null && onGatesChanged is not null)
            {
                var capturedTaskForLock = task;
                var isLeaf   = PlanGateManager.IsLeafTask(durablePlan, capturedTaskForLock.Id);
                var existingTaskGate = FindTaskGateAfter(capturedTaskForLock.Id);
                var isLocked = existingTaskGate is not null;
                var lockText = new TextBlock
                {
                    Text    = isLocked ? "🔒" : "🔓",
                    Opacity = isLocked ? 1.0 : 0.25,
                    Cursor  = isLeaf ? Cursors.Arrow : Cursors.Hand,
                    ToolTip = isLeaf
                        ? (isLocked ? "This task requires human approval." : null)
                        : isLocked
                            ? "This task requires human approval when it completes. Click to remove."
                            : "Click to require human approval when this task completes.",
                };
                lockText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
                Canvas.SetLeft(lockText, position.X + NodeWidth - 22);
                Canvas.SetTop(lockText,  position.Y + NodeHeight - 20);
                Panel.SetZIndex(lockText, 25);
                if (!isLeaf)
                {
                    lockText.MouseEnter += (_, _) => lockText.Opacity = 1.0;
                    lockText.MouseLeave += (_, _) => lockText.Opacity = isLocked ? 1.0 : 0.25;
                    lockText.MouseLeftButtonDown += (_, e) =>
                    {
                        e.Handled = true;
                        var updated = durablePlan;
                        if (isLocked && existingTaskGate is not null)
                            updated = PlanGateManager.RemoveGate(updated, existingTaskGate.GateId);
                        else
                            updated = PlanGateManager.AddGateAfter(updated, capturedTaskForLock.Id,
                                $"Review after completing: {capturedTaskForLock.Title ?? capturedTaskForLock.Id}");
                        if (!ReferenceEquals(updated, durablePlan)) onGatesChanged(updated);
                    };
                }
                canvas.Children.Add(lockText);
            }
            else
            {
                // Snapshot-only fixtures are intentionally non-editable, but still show the
                // approval affordance so the preview accurately represents the finished UI.
                var hasConfiguredGate = lockIconByTask.TryGetValue(task.Id, out var lockIcon);
                lockIcon ??= "🔓";
                var lockText = new TextBlock
                {
                    Text                = lockIcon,
                    Opacity             = hasConfiguredGate ? 1.0 : 0.25,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                    Cursor              = Cursors.Arrow,
                    ToolTip             = hasConfiguredGate
                        ? "Preview: this task has a human approval checkpoint."
                        : "Preview: this padlock controls approval after the task.",
                };
                lockText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
                Canvas.SetLeft(lockText, position.X + NodeWidth - 22);
                Canvas.SetTop(lockText,  position.Y + NodeHeight - 20);
                Panel.SetZIndex(lockText, 25);
                canvas.Children.Add(lockText);
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

        canvas.Width= Math.Max(1080, positions.Values.Max(point => point.X) + NodeWidth + 70);
        canvas.Height = Math.Max(560, positions.Values.Max(point => point.Y) + NodeHeight + 70);
    }

    /// <summary>
    /// Rebuilds the viewer content against a newly persisted immutable plan while preserving the
    /// existing window, location, size, focus, and owner. The temporary window is never shown; it
    /// is only used to construct a fresh visual tree with handlers bound to the new plan instance.
    /// </summary>
    internal void RefreshPlan(PendingDecomposePlan plan, Plan durablePlan)
    {
        var horizontalOffset = _graphScroll?.HorizontalOffset ?? 0;
        var verticalOffset = _graphScroll?.VerticalOffset ?? 0;
        var refreshed = new PlanViewerWindow(
            plan,
            _activeBranch,
            _quickReplyFontSize,
            _applyAction,
            durablePlan,
            _onGatesChanged,
            _onResumePlan,
            _onEndPlan,
            _onApproveGate);
        var refreshedContent = refreshed._contentHolder?.Child;
        if (refreshed._contentHolder is not null)
            refreshed._contentHolder.Child = null;
        if (_contentHolder is not null)
            _contentHolder.Child = refreshedContent;
        _graphScroll = refreshed._graphScroll;
        Title = refreshed.Title;
        Dispatcher.BeginInvoke(() =>
        {
            _graphScroll?.ScrollToHorizontalOffset(horizontalOffset);
            _graphScroll?.ScrollToVerticalOffset(verticalOffset);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
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

    private static ConnectorGroup AddConnector(Canvas canvas, Point from, Point to, bool arrowHead, int skipCount = 0, bool dashed = false, string? toolTip = null)
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

            var mainLine = new Line
            {
                X1 = from.X, Y1 = from.Y, X2 = lineEnd.X, Y2 = lineEnd.Y,
                StrokeThickness = 2, Stroke = mainBrush,
            };
            if (dashArray is not null) mainLine.StrokeDashArray = dashArray;
            if (toolTip is not null)   mainLine.ToolTip = toolTip;
            canvas.Children.Add(mainLine);
            group.MainElements.Add(mainLine);

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

            var mainPath = new Path
            {
                Data = MakeBezierGeometry(), StrokeThickness = 2, Stroke = mainBrush,
                Fill = Brushes.Transparent,
            };
            if (dashArray is not null) mainPath.StrokeDashArray = dashArray;
            if (toolTip is not null)   mainPath.ToolTip = toolTip;
            canvas.Children.Add(mainPath);
            group.MainElements.Add(mainPath);

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
}
