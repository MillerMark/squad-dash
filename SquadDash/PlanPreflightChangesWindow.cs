using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadDash;

/// <summary>Modeless, themed viewer for the Git evidence behind a plan preflight block.</summary>
internal sealed class PlanPreflightChangesWindow : ChromedWindow
{
    internal PlanPreflightChangesWindow(
        PlanPreflightBlockedException exception,
        string gitStatus,
        string gitDiff,
        double fontSize)
        : base(captionHeight: 28, resizeMode: ResizeMode.CanResize)
    {
        Title = "Plan Preflight Changes";
        Width = 1050;
        Height = 720;
        MinWidth = 600;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = true;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var title = new TextBlock
        {
            Text = "Uncommitted changes blocking plan execution",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeNormal");
        root.Children.Add(title);

        var target = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(exception.TargetBranch)
                ? "Review these changes, resolve them in Git, then return to the Inbox and retry."
                : $"Target branch: {exception.TargetBranch}. Resolve these changes in Git, then return to the Inbox and retry.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        target.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        target.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        Grid.SetRow(target, 1);
        root.Children.Add(target);

        var evidence = new TextBox
        {
            Text = "GIT STATUS\n" + (string.IsNullOrWhiteSpace(gitStatus) ? "(clean)" : gitStatus.TrimEnd()) +
                   "\n\nGIT DIFF\n" + (string.IsNullOrWhiteSpace(gitDiff)
                       ? "(No tracked diff. Untracked files, if any, are listed in Git status.)"
                       : gitDiff.TrimEnd()),
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = Math.Max(11, fontSize - 1),
            Padding = new Thickness(10),
        };
        evidence.SetResourceReference(TextBox.BackgroundProperty, "InboxBodySurface");
        evidence.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
        evidence.SetResourceReference(TextBox.BorderBrushProperty, "PanelBorder");
        Grid.SetRow(evidence, 2);
        root.Children.Add(evidence);

        var border = ApplyOuterBorder();
        border.Child = root;
    }
}
