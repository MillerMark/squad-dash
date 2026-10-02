using System.Windows;
using System.Windows.Controls;

namespace SquadDash;

internal sealed class BranchPullDialog : ChromedWindow
{
    internal bool UseRebase { get; private set; }

    internal BranchPullDialog(string branch, GitUpstreamCounts counts, Window owner)
        : base(captionHeight: 36, resizeMode: ResizeMode.NoResize)
    {
        Title = "Combine Diverged Branch Histories";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = owner;

        var root = new StackPanel { Margin = new Thickness(24, 40, 24, 24) };
        ApplyOuterBorder().Child = root;

        var heading = new TextBlock
        {
            Text = "Your branch and the remote have diverged",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        root.Children.Add(heading);

        var explanation = new TextBlock
        {
            Text = $"Your local '{branch}' has {counts.Ahead} commit(s) that aren't on the remote, "
                + $"and the remote has {counts.Behind} commit(s) you don't have. "
                + "These histories have diverged, so a simple fast-forward isn't possible.\n\n"
                + "How would you like to combine them?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18),
        };
        explanation.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        root.Children.Add(explanation);

        var merge = BuildChoiceButton(
            "Merge (recommended)",
            "Creates a merge commit without rewriting your existing commits. "
                + "This is the safe default; no force-push is needed.");
        merge.IsDefault = true;
        merge.Click += (_, _) => { UseRebase = false; DialogResult = true; };
        root.Children.Add(merge);

        var rebase = BuildChoiceButton(
            "Rebase",
            "Replays your local commits on top of the remote for a cleaner, linear history, "
                + "but rewrites those local commits. Prefer Merge if you've already shared them.");
        rebase.Click += (_, _) => { UseRebase = true; DialogResult = true; };
        root.Children.Add(rebase);

        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            Width = 100,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        cancel.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        cancel.Click += (_, _) => DialogResult = false;
        root.Children.Add(cancel);
        Loaded += (_, _) => merge.Focus();
    }

    private static Button BuildChoiceButton(string label, string detail)
    {
        var content = new StackPanel();
        var title = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        content.Children.Add(title);
        var explanation = new TextBlock
        {
            Text = detail,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        explanation.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        content.Children.Add(explanation);

        var button = new Button
        {
            Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 10),
        };
        button.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        return button;
    }
}
