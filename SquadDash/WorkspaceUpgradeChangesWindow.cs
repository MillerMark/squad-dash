using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadDash;

/// <summary>A passive viewer for workspace-upgrade filenames and repository evidence.</summary>
internal sealed class WorkspaceUpgradeChangesWindow : ChromedWindow
{
    private readonly TextBlock _description;
    private readonly TextBox _evidence;

    internal WorkspaceUpgradeChangesWindow(double fontSize)
        : base(captionHeight: 28, resizeMode: ResizeMode.CanResize)
    {
        Title = "Workspace Upgrade Changes";
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
            Text = "Workspace files affected by the upgrade",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeNormal");
        root.Children.Add(title);

        _description = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        _description.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        _description.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        Grid.SetRow(_description, 1);
        root.Children.Add(_description);

        _evidence = new TextBox
        {
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
        _evidence.SetResourceReference(TextBox.BackgroundProperty, "InboxBodySurface");
        _evidence.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
        _evidence.SetResourceReference(TextBox.BorderBrushProperty, "PanelBorder");
        Grid.SetRow(_evidence, 2);
        root.Children.Add(_evidence);

        var border = ApplyOuterBorder();
        border.Child = root;
    }

    internal void UpdateEvidence(
        WorkspaceUpgradeDryRunResult result,
        string? gitStatus,
        string? gitDiff,
        bool simulated)
    {
        _description.Text = simulated
            ? "Safe simulation results. This viewer only displays representative filenames and never starts coordinator work."
            : "Reported upgrade filenames with the current passive Git status and diff. No coordinator prompt is started.";
        _evidence.Text = WorkspaceUpgradeChangesPresentation.Build(
            result,
            gitStatus,
            gitDiff,
            simulated);
        _evidence.ScrollToHome();
    }
}
