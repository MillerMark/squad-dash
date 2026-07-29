using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SquadDash;

internal sealed class PlanViewerWindow : ChromedWindow
{
    private const double NodeWidth = 220;
    private const double NodeHeight = 100;
    private const double ColumnSpacing = 360;
    private const double RowSpacing = 152;

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
        scroll.SetResourceReference(ScrollViewer.StyleProperty,      "RosterScrollViewerStyle");
        scroll.SetResourceReference(ScrollViewer.BackgroundProperty, "CardSurface");
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        ApplyOuterBorder(titleText: group.GroupTitle).Child = root;

        var tasksById = group.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        var levels = CalculateLevels(group.Tasks, tasksById);
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        var columns = group.Tasks.GroupBy(task => levels[task.Id]).OrderBy(column => column.Key).ToArray();
        foreach (var column in columns)
        {
            var tasks = column.ToArray();
            var x = 42 + column.Key * ColumnSpacing;
            var stageHeader = new TextBlock
            {
                Text       = tasks.Length == 1
                    ? $"Stage {column.Key + 1}"
                    : $"Stage {column.Key + 1}  ·  {tasks.Length} independent tasks",
                FontWeight = FontWeights.SemiBold,
            };
            stageHeader.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            stageHeader.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            Canvas.SetLeft(stageHeader, x);
            Canvas.SetTop(stageHeader, 18);
            canvas.Children.Add(stageHeader);

            for (var row = 0; row < tasks.Length; row++)
                positions[tasks[row].Id] = new Point(x, 58 + row * RowSpacing);
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

        // Pass 2: pre-compute approval-gate centers.
        var approvalGateInfos = new List<(DecomposedGate Gate, Point Center)>();
        if (group.ApprovalGates is { Count: > 0 })
        {
            foreach (var approvalGate in group.ApprovalGates)
            {
                var afterPositions  = (approvalGate.AfterTaskIds  ?? []).Where(positions.ContainsKey).Select(id => positions[id]).ToList();
                var beforePositions = (approvalGate.BeforeTaskIds ?? []).Where(positions.ContainsKey).Select(id => positions[id]).ToList();
                if (afterPositions.Count == 0 && beforePositions.Count == 0) continue;
                double gateCenterX;
                if (afterPositions.Count > 0 && beforePositions.Count > 0)
                    gateCenterX = (afterPositions.Max(p => p.X + NodeWidth) + beforePositions.Min(p => p.X)) / 2;
                else if (afterPositions.Count > 0)
                    gateCenterX = afterPositions.Max(p => p.X + NodeWidth) + ColumnSpacing / 4;
                else
                    gateCenterX = beforePositions.Min(p => p.X) - ColumnSpacing / 4;
                var allYCenters = afterPositions.Select(p => p.Y + NodeHeight / 2.0)
                                      .Concat(beforePositions.Select(p => p.Y + NodeHeight / 2.0)).ToList();
                approvalGateInfos.Add((approvalGate, new Point(gateCenterX, allYCenters.Average())));
            }
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
        foreach (var (approvalGate, gateCenter) in approvalGateInfos)
        {
            foreach (var taskId in approvalGate.AfterTaskIds  ?? []) if (positions.ContainsKey(taskId)) RegisterExit(taskId, gateCenter.Y);
            foreach (var taskId in approvalGate.BeforeTaskIds ?? []) if (positions.ContainsKey(taskId)) RegisterEntry(taskId, gateCenter.Y);
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

        // Draw ALL-gate connectors.
        foreach (var (gateCenter, targets, dependencies, minTargetLevel, maxDepLevel) in gates)
        {
            foreach (var dependency in dependencies.Where(positions.ContainsKey))
            {
                var source  = positions[dependency];
                var depSkip = minTargetLevel - levels[dependency] - 1;
                AddConnector(canvas,
                    new Point(source.X + NodeWidth, SpreadExitY(dependency, gateCenter.Y)),
                    new Point(gateCenter.X - 20, gateCenter.Y),
                    arrowHead: false,
                    skipCount: Math.Max(0, depSkip));
            }
            foreach (var target in targets)
            {
                var targetPoint = positions[target.Id];
                var targetSkip  = levels[target.Id] - maxDepLevel - 1;
                AddConnector(canvas,
                    new Point(gateCenter.X + 20, gateCenter.Y),
                    new Point(targetPoint.X, SpreadEntryY(target.Id, gateCenter.Y)),
                    arrowHead: true,
                    skipCount: Math.Max(0, targetSkip));
            }
        }

        // Draw non-gated direct connectors.
        foreach (var task in group.Tasks.Where(task => !gatedTaskIds.Contains(task.Id)))
        {
            foreach (var dependency in task.DependsOn.Where(positions.ContainsKey))
            {
                var source    = positions[dependency];
                var target    = positions[task.Id];
                var skipCount = Math.Max(0, levels[task.Id] - levels[dependency] - 1);
                AddConnector(canvas,
                    new Point(source.X + NodeWidth, SpreadExitY(dependency, target.Y + NodeHeight / 2.0)),
                    new Point(target.X,             SpreadEntryY(task.Id,   source.Y + NodeHeight / 2.0)),
                    arrowHead: true,
                    skipCount: skipCount);
            }
        }

        foreach (var gate in gates)
        {
            var badgeText = new TextBlock
            {
                Text                = "ALL",
                FontWeight          = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, "ActivePanelTitle");
            badgeText.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            var badge = new Border
            {
                Width           = 40,
                Height          = 26,
                CornerRadius    = new CornerRadius(13),
                BorderThickness = new Thickness(1.5),
                ToolTip         = "ALL prerequisites entering this gate must finish before any outgoing task can begin.",
                Child           = badgeText,
            };
            badge.SetResourceReference(Border.BorderBrushProperty, "ActivePanelBorder");
            badge.SetResourceReference(Border.BackgroundProperty,  "CardSurface");
            Canvas.SetLeft(badge, gate.Center.X - 20);
            Canvas.SetTop(badge, gate.Center.Y - 13);
            canvas.Children.Add(badge);
        }

        // Render human approval gate connectors and badges.
        foreach (var (approvalGate, gateCenter) in approvalGateInfos)
        {
            foreach (var taskId in approvalGate.AfterTaskIds ?? [])
            {
                if (!positions.TryGetValue(taskId, out var pos)) continue;
                AddConnector(canvas,
                    new Point(pos.X + NodeWidth, SpreadExitY(taskId, gateCenter.Y)),
                    new Point(gateCenter.X - 30, gateCenter.Y),
                    arrowHead: false);
            }
            foreach (var taskId in approvalGate.BeforeTaskIds ?? [])
            {
                if (!positions.TryGetValue(taskId, out var pos)) continue;
                AddConnector(canvas,
                    new Point(gateCenter.X + 30, gateCenter.Y),
                    new Point(pos.X, SpreadEntryY(taskId, gateCenter.Y)),
                    arrowHead: true);
            }

            var durableGate = durablePlan?.ApprovalGates.FirstOrDefault(g =>
                string.Equals(g.GateId, approvalGate.GateId, StringComparison.Ordinal));
            var durableGateStatus = durableGate?.Status ?? PlanGateStatus.Pending;
            var (gateIcon, gateBorderKey) = durableGateStatus switch
            {
                PlanGateStatus.AwaitingApproval => ("⏸", "PriorityMid"),
                PlanGateStatus.Approved         => ("✓",  "PriorityLow"),
                PlanGateStatus.Skipped          => ("–",  "SubtleText"),
                _                               => ("🔒", "PanelBorder"),
            };
            var gateToolTipText = durableGate is not null
                ? $"{approvalGate.Message}\nStatus: {durableGateStatus}"
                : approvalGate.Message;
            var gateBadgeText = new TextBlock
            {
                Text                = gateIcon,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };
            gateBadgeText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            var gateBadge = new Border
            {
                Width           = 60,
                Height          = 28,
                CornerRadius    = new CornerRadius(14),
                BorderThickness = new Thickness(1.5),
                ToolTip         = gateToolTipText,
                Child           = gateBadgeText,
            };
            gateBadge.SetResourceReference(Border.BorderBrushProperty, gateBorderKey);
            gateBadge.SetResourceReference(Border.BackgroundProperty,  "CardSurface");
            Canvas.SetLeft(gateBadge, gateCenter.X - 30);
            Canvas.SetTop(gateBadge, gateCenter.Y - 14);
            canvas.Children.Add(gateBadge);
        }

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

            canvas.Children.Add(border);
        }

        canvas.Width = Math.Max(1080, positions.Values.Max(point => point.X) + NodeWidth + 70);
        canvas.Height = Math.Max(560, positions.Values.Max(point => point.Y) + NodeHeight + 70);
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

    private static void AddConnector(Canvas canvas, Point from, Point to, bool arrowHead, int skipCount = 0)
    {
        var brush = new SolidColorBrush(ConnectorColor(skipCount));
        const double arrowLength    = 11;
        const double arrowHalfWidth = 5;

        // When there is an arrowhead, the line/curve ends at the base-center of the arrowhead
        // (not at the tip), so the line visually enters the center of the arrow rather than
        // overshooting through it.  All connectors flow left-to-right, so the base is directly
        // to the left of the tip by arrowLength pixels.
        var lineEnd = arrowHead ? new Point(to.X - arrowLength, to.Y) : to;

        if (skipCount > 0 || Math.Abs(to.Y - from.Y) < 1.0)
        {
            // Straight line: skip (multi-stage) connectors always straight; same-Y also straight.
            canvas.Children.Add(new Line
            {
                X1 = from.X, Y1 = from.Y,
                X2 = lineEnd.X, Y2 = lineEnd.Y,
                StrokeThickness = 2,
                Stroke = brush,
            });
        }
        else
        {
            // S-curve Bézier with horizontal tangents at both endpoints.
            double dx        = lineEnd.X - from.X;
            double handleLen = Math.Max(dx * 0.5, 40.0);
            var cp1 = new Point(from.X    + handleLen, from.Y);
            var cp2 = new Point(lineEnd.X - handleLen, lineEnd.Y);

            var figure = new PathFigure { StartPoint = from };
            figure.Segments.Add(new BezierSegment(cp1, cp2, lineEnd, isStroked: true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            canvas.Children.Add(new Path
            {
                Data            = geometry,
                StrokeThickness = 2,
                Stroke          = brush,
                Fill            = Brushes.Transparent,
            });
        }

        if (!arrowHead) return;

        // Arrowhead: tip at `to`, wings spread from `lineEnd` (base-center).
        var perp = new Vector(0, 1);  // horizontal tangent → perpendicular is vertical
        canvas.Children.Add(new Polygon
        {
            Fill   = brush,
            Points = [to, lineEnd + perp * arrowHalfWidth, lineEnd - perp * arrowHalfWidth],
        });
    }

    // Base hue for adjacent-stage connectors. Each skipped stage rotates the hue by 45°.
    private const double ConnectorBaseHue = 210.0;
    private const double ConnectorSaturation = 0.70;
    private const double ConnectorLightness  = 0.45;

    private static Color ConnectorColor(int skipCount)
    {
        var hue = (ConnectorBaseHue + skipCount * 45.0) % 360.0;
        return HslToRgb(hue, ConnectorSaturation, ConnectorLightness);
    }

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
