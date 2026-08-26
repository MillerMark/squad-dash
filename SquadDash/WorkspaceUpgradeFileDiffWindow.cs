using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadDash;

internal sealed class WorkspaceUpgradeFileDiffWindow : ChromedWindow
{
    internal WorkspaceUpgradeFileDiffWindow(WorkspaceUpgradeFilePreview preview)
        : base(captionHeight: 28, resizeMode: ResizeMode.CanResize)
    {
        Title = $"Workspace Upgrade Preview — {Path.GetFileName(preview.DisplayPath)}";
        Width = 1120;
        Height = 760;
        MinWidth = 680;
        MinHeight = 440;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new TextBlock
        {
            Text = preview.DisplayPath,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        heading.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeNormal");
        root.Children.Add(heading);

        var description = new TextBlock
        {
            Text = BuildDescription(preview),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        description.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        Grid.SetRow(description, 1);
        root.Children.Add(description);

        var diffPanel = new StackPanel { Orientation = Orientation.Vertical };
        RenderPreview(diffPanel, preview);
        var scroll = new ScrollViewer
        {
            Content = diffPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8)
        };
        scroll.SetResourceReference(ScrollViewer.BackgroundProperty, "CardSurface");
        scroll.SetResourceReference(ScrollViewer.StyleProperty, "RosterScrollViewerStyle");
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        ApplyOuterBorder().Child = root;
    }

    private static string BuildDescription(WorkspaceUpgradeFilePreview preview)
    {
        if (!preview.IsAvailable)
            return preview.UnavailableReason ?? "A proposed-content preview is not available for this file.";

        var meaning = preview.Category switch
        {
            WorkspaceUpgradeFileCategory.TrackedLocalChange =>
                "The current customized file is compared with the incoming Squad template.",
            WorkspaceUpgradeFileCategory.GeneratedFile =>
                "This generated file would be replaced; manual edits in the current file may be lost.",
            _ => "The current file is compared with the incoming Squad template."
        };
        return preview.IsExactTemplateCopy
            ? meaning
            : meaning + " This is a template-level preview; Squad CLI may additionally render workspace-specific generated sections when applying the upgrade.";
    }

    private static void RenderPreview(Panel panel, WorkspaceUpgradeFilePreview preview)
    {
        if (!preview.IsAvailable)
        {
            AddMessage(panel, preview.UnavailableReason ?? "Preview unavailable.");
            return;
        }

        if (string.IsNullOrWhiteSpace(preview.DiffText))
        {
            AddMessage(panel, "The current file already matches the incoming template.");
            return;
        }

        foreach (var rawLine in preview.DiffText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine;
            if (rawLine.StartsWith("--- ", StringComparison.Ordinal))
                line = $"--- Current: {preview.DisplayPath}";
            else if (rawLine.StartsWith("+++ ", StringComparison.Ordinal))
                line = $"+++ Proposed: {preview.DisplayPath}";

            var kind = line.StartsWith('+') && !line.StartsWith("+++") ? DiffLineKind.Added
                : line.StartsWith('-') && !line.StartsWith("---") ? DiffLineKind.Removed
                : line.StartsWith("@@") || line.StartsWith("diff ") || line.StartsWith("index ") ||
                  line.StartsWith("---") || line.StartsWith("+++") ? DiffLineKind.Header
                : DiffLineKind.Context;
            panel.Children.Add(DiffLinePresenter.Create(line.Length == 0 ? " " : line, kind));
        }
    }

    private static void AddMessage(Panel panel, string message)
    {
        var text = new TextBlock
        {
            Text = message,
            Margin = new Thickness(10),
            TextWrapping = TextWrapping.Wrap
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        panel.Children.Add(text);
    }
}
