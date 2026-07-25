using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SquadDash;

internal sealed class DecomposePlanWindow : Window
{
    internal DecomposePlanWindow(DecomposedTaskGroup group)
    {
        Title = group.GroupTitle;
        Width = 1000; Height = 650; MinWidth = 700; MinHeight = 450;
        var canvas = new Canvas { Background = Brushes.Transparent, Margin = new Thickness(24) };
        var scroll = new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Content = scroll;

        var levels = new Dictionary<string, int>();
        int Level(string id)
        {
            if (levels.TryGetValue(id, out var known)) return known;
            var task = group.Tasks.First(t => t.Id == id);
            return levels[id] = task.DependsOn.Count == 0 ? 0 : task.DependsOn.Max(Level) + 1;
        }
        foreach (var task in group.Tasks) Level(task.Id);
        var positions = new Dictionary<string, Point>();
        foreach (var column in group.Tasks.GroupBy(t => levels[t.Id]).OrderBy(g => g.Key))
        {
            int row = 0;
            foreach (var task in column)
                positions[task.Id] = new Point(40 + column.Key * 260, 40 + row++ * 110);
        }
        foreach (var task in group.Tasks)
            foreach (var dependency in task.DependsOn)
            {
                var a = positions[dependency]; var b = positions[task.Id];
                canvas.Children.Add(new Line { X1 = a.X + 190, Y1 = a.Y + 30, X2 = b.X, Y2 = b.Y + 30,
                    Stroke = Brushes.SlateGray, StrokeThickness = 2 });
            }
        foreach (var task in group.Tasks)
        {
            var p = positions[task.Id];
            var label = task.Description.Split(':', '—')[0].Trim();
            var border = new Border { Width = 190, MinHeight = 60, Padding = new Thickness(10), CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1), BorderBrush = Brushes.SlateGray, Background = Brushes.WhiteSmoke,
                ToolTip = task.Description, Child = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black } };
            Canvas.SetLeft(border, p.X); Canvas.SetTop(border, p.Y); canvas.Children.Add(border);
        }
        canvas.Width = Math.Max(900, positions.Values.Max(p => p.X) + 250);
        canvas.Height = Math.Max(550, positions.Values.Max(p => p.Y) + 120);
    }
}
