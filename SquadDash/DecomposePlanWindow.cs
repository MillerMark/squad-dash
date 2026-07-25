using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SquadDash;

internal sealed class DecomposePlanWindow : ChromedWindow
{
    private const double NodeWidth = 220;
    private const double NodeHeight = 76;
    private const double ColumnSpacing = 360;
    private const double RowSpacing = 128;

    internal DecomposePlanWindow(DecomposedTaskGroup group) : base(captionHeight: CloseButtonHeight)
    {
        Title     = group.GroupTitle;
        Width     = 1200;
        Height    = 720;
        MinWidth  = 760;
        MinHeight = 480;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Margin = new Thickness(22, 16, 22, 10) };

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

            foreach (var dependency in dependencies.Where(positions.ContainsKey))
            {
                var source = positions[dependency];
                AddConnector(canvas,
                    new Point(source.X + NodeWidth, source.Y + NodeHeight / 2),
                    new Point(gateCenter.X - 20, gateCenter.Y),
                    arrowHead: false);
            }
            foreach (var target in targets)
            {
                var targetPoint = positions[target.Id];
                AddConnector(canvas,
                    new Point(gateCenter.X + 20, gateCenter.Y),
                    new Point(targetPoint.X, targetPoint.Y + NodeHeight / 2),
                    arrowHead: true);
            }
        }

        foreach (var task in group.Tasks.Where(task => !gatedTaskIds.Contains(task.Id)))
        {
            foreach (var dependency in task.DependsOn.Where(positions.ContainsKey))
            {
                var source = positions[dependency];
                var target = positions[task.Id];
                AddConnector(canvas,
                    new Point(source.X + NodeWidth, source.Y + NodeHeight / 2),
                    new Point(target.X, target.Y + NodeHeight / 2),
                    arrowHead: true);
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

        foreach (var task in group.Tasks)
        {
            var position = positions[task.Id];
            var shortName = task.Description.Split(':', '—')[0].Trim();
            var dependencyText = task.DependsOn.Count == 0
                ? "None — this task can start immediately."
                : string.Join("\n", task.DependsOn.Select(id => "• " + id));
            var nodeTitle = new TextBlock
            {
                Text         = shortName,
                TextWrapping = TextWrapping.Wrap,
                FontWeight   = FontWeights.SemiBold,
            };
            nodeTitle.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            nodeTitle.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
            var nodeId = new TextBlock
            {
                Text   = task.Id,
                Margin = new Thickness(0, 5, 0, 0),
            };
            nodeId.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            nodeId.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeSmall");
            var content = new StackPanel();
            content.Children.Add(nodeTitle);
            content.Children.Add(nodeId);
            var border = new Border
            {
                Width           = NodeWidth,
                Height          = NodeHeight,
                Padding         = new Thickness(11, 8, 11, 8),
                CornerRadius    = new CornerRadius(7),
                BorderThickness = new Thickness(1.25),
                ToolTip         = $"{task.Description}\n\nPrerequisites:\n{dependencyText}",
                Child           = content,
            };
            border.SetResourceReference(Border.BackgroundProperty,  "CardSurface");
            border.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
            Canvas.SetLeft(border, position.X);
            Canvas.SetTop(border, position.Y);
            canvas.Children.Add(border);
        }

        canvas.Width = Math.Max(1080, positions.Values.Max(point => point.X) + NodeWidth + 70);
        canvas.Height = Math.Max(560, positions.Values.Max(point => point.Y) + NodeHeight + 70);
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

    private static void AddConnector(Canvas canvas, Point from, Point to, bool arrowHead)
    {
        var line = new Line
        {
            X1 = from.X,
            Y1 = from.Y,
            X2 = to.X,
            Y2 = to.Y,
            StrokeThickness = 2,
        };
        line.SetResourceReference(Shape.StrokeProperty, "ActivePanelTitle");
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
            Points =
            [
                to,
                basePoint + perpendicular * halfWidth,
                basePoint - perpendicular * halfWidth,
            ],
        };
        arrow.SetResourceReference(Shape.FillProperty, "ActivePanelTitle");
        canvas.Children.Add(arrow);
    }
}
