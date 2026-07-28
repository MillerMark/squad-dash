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
        Action<Plan>? onEndPlan      = null)
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
                    toolTip: ToolTipHelper.MakeThemedToolTip("Mark this plan as stopped, preserving its history. No further recovery reminders will be shown."));
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
        var gates = new List<(Point Center, IReadOnlyList<DecomposedSubTask> Targets, IReadOnlyList<string> Dependencies)>();

        foreach (var gateGroup in gatedGroups)
        {
            var targets = gateGroup.ToArray();
            var dependencies = targets[0].DependsOn.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var sourceRight = dependencies.Where(positions.ContainsKey).Max(id => positions[id].X + NodeWidth);
            var targetLeft = targets.Min(task => positions[task.Id].X);
            var centers = dependencies.Where(positions.ContainsKey).Select(id => positions[id].Y + NodeHeight / 2)
                .Concat(targets.Select(task => positions[task.Id].Y + NodeHeight / 2));
            var gateCenter = new Point((sourceRight + targetLeft) / 2, centers.Average());
            gates.Add((gateCenter, targets, dependencies));

            // Each dep leg gets its own skip: how many stages does THIS dep span to reach the targets?
            var minTargetLevel = targets.Min(t => levels[t.Id]);
            var maxDepLevel    = dependencies.Where(positions.ContainsKey).Max(id => levels[id]);

            foreach (var dependency in dependencies.Where(positions.ContainsKey))
            {
                var source  = positions[dependency];
                var depSkip = minTargetLevel - levels[dependency] - 1;
                AddConnector(canvas,
                    new Point(source.X + NodeWidth, source.Y + NodeHeight / 2),
                    new Point(gateCenter.X - 20, gateCenter.Y),
                    arrowHead: false,
                    skipCount: Math.Max(0, depSkip));
            }
            foreach (var target in targets)
            {
                var targetPoint  = positions[target.Id];
                var targetSkip   = levels[target.Id] - maxDepLevel - 1;
                AddConnector(canvas,
                    new Point(gateCenter.X + 20, gateCenter.Y),
                    new Point(targetPoint.X, targetPoint.Y + NodeHeight / 2),
                    arrowHead: true,
                    skipCount: Math.Max(0, targetSkip));
            }
        }

        foreach (var task in group.Tasks.Where(task => !gatedTaskIds.Contains(task.Id)))
        {
            foreach (var dependency in task.DependsOn.Where(positions.ContainsKey))
            {
                var source    = positions[dependency];
                var target    = positions[task.Id];
                var skipCount = Math.Max(0, levels[task.Id] - levels[dependency] - 1);
                AddConnector(canvas,
                    new Point(source.X + NodeWidth, source.Y + NodeHeight / 2),
                    new Point(target.X, target.Y + NodeHeight / 2),
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

        // Render human approval gate badges.
        if (group.ApprovalGates is { Count: > 0 })
        {
            foreach (var approvalGate in group.ApprovalGates)
            {
                var afterPositions = (approvalGate.AfterTaskIds ?? [])
                    .Where(positions.ContainsKey)
                    .Select(id => positions[id])
                    .ToList();
                var beforePositions = (approvalGate.BeforeTaskIds ?? [])
                    .Where(positions.ContainsKey)
                    .Select(id => positions[id])
                    .ToList();

                if (afterPositions.Count == 0 && beforePositions.Count == 0)
                    continue;

                double gateCenterX;
                if (afterPositions.Count > 0 && beforePositions.Count > 0)
                {
                    var sourceRight = afterPositions.Max(p => p.X + NodeWidth);
                    var targetLeft  = beforePositions.Min(p => p.X);
                    gateCenterX = (sourceRight + targetLeft) / 2;
                }
                else if (afterPositions.Count > 0)
                    gateCenterX = afterPositions.Max(p => p.X + NodeWidth) + ColumnSpacing / 4;
                else
                    gateCenterX = beforePositions.Min(p => p.X) - ColumnSpacing / 4;

                var allYCenters = afterPositions.Select(p => p.Y + NodeHeight / 2)
                    .Concat(beforePositions.Select(p => p.Y + NodeHeight / 2))
                    .ToList();
                var gateCenterY = allYCenters.Average();
                var gateCenter  = new Point(gateCenterX, gateCenterY);

                foreach (var taskId in approvalGate.AfterTaskIds ?? [])
                {
                    if (!positions.TryGetValue(taskId, out var pos)) continue;
                    AddConnector(canvas,
                        new Point(pos.X + NodeWidth, pos.Y + NodeHeight / 2),
                        new Point(gateCenter.X - 30, gateCenter.Y),
                        arrowHead: false);
                }
                foreach (var taskId in approvalGate.BeforeTaskIds ?? [])
                {
                    if (!positions.TryGetValue(taskId, out var pos)) continue;
                    AddConnector(canvas,
                        new Point(gateCenter.X + 30, gateCenter.Y),
                        new Point(pos.X, pos.Y + NodeHeight / 2),
                        arrowHead: true);
                }

                var gateBadgeText = new TextBlock
                {
                    Text                = "🔒",
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
                    ToolTip         = approvalGate.Message,
                    Child           = gateBadgeText,
                };
                gateBadge.SetResourceReference(Border.BorderBrushProperty, "PriorityHigh");
                gateBadge.SetResourceReference(Border.BackgroundProperty,  "CardSurface");
                Canvas.SetLeft(gateBadge, gateCenter.X - 30);
                Canvas.SetTop(gateBadge, gateCenter.Y - 14);
                canvas.Children.Add(gateBadge);
            }
        }

        foreach (var task in group.Tasks)
        {
            var position = positions[task.Id];
            var prereqLines = task.DependsOn.Count == 0
                ? ["None — this task can start immediately."]
                : task.DependsOn.Select(id =>
                {
                    if (!tasksById.TryGetValue(id, out var dep)) return $"• {id}";
                    var label = dep.Title ?? dep.Description;
                    return "• " + (label.Length > 60 ? label[..60] + "…" : label);
                }).ToArray();
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
            var nodeDescription = new TextBlock
            {
                Text         = task.Description,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight    = 34,
                Margin = new Thickness(0, 5, 0, 0),
            };
            nodeDescription.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            nodeDescription.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            var content = new StackPanel();
            content.Children.Add(nodeTitle);
            content.Children.Add(nodeDescription);
            var border = new Border
            {
                Width           = NodeWidth,
                Height          = NodeHeight,
                Padding         = new Thickness(11, 8, 11, 8),
                CornerRadius    = new CornerRadius(7),
                BorderThickness = new Thickness(1.25),
                ToolTip         = BuildTaskToolTip(task.Description, prereqLines),
                Child           = content,
            };
            border.SetResourceReference(Border.BackgroundProperty,  "CardSurface");
            border.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
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

    private static ToolTip BuildTaskToolTip(string description, string[] prereqLines)
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
        var line = new Line
        {
            X1 = from.X,
            Y1 = from.Y,
            X2 = to.X,
            Y2 = to.Y,
            StrokeThickness = 2,
            Stroke = brush,
        };
        canvas.Children.Add(line);
        if (!arrowHead) return;

        var vector = from - to;
        if (vector.Length < 0.1) return;
        vector.Normalize();
        var perpendicular = new Vector(-vector.Y, vector.X);
        const double length = 11;
        const double halfWidth = 5;
        var basePoint = to + vector * length;
        var arrow = new Polygon
        {
            Fill   = brush,
            Points =
            [
                to,
                basePoint + perpendicular * halfWidth,
                basePoint - perpendicular * halfWidth,
            ],
        };
        canvas.Children.Add(arrow);
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
