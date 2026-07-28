using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Shows a concise, actionable message when plan execution is blocked by uncommitted working-tree
/// changes. Offers a scrollable file list and a Retry/Dismiss button; never offers automatic
/// commit or stash.
/// </summary>
internal sealed class PlanPreflightBlockedDialog : ChromedWindow
{
    public PlanPreflightBlockedDialog(
        PlanPreflightBlockedException blocked,
        Window? owner = null) : base(captionHeight: 36, resizeMode: ResizeMode.NoResize)
    {
        Title                 = "Changes Blocking Branch Switch";
        Width                 = 520;
        SizeToContent         = SizeToContent.Height;
        MinWidth              = 400;
        MaxHeight             = 640;
        ShowInTaskbar         = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (owner is not null) Owner = owner;

        var root = new StackPanel { Margin = new Thickness(20) };
        var outerBorder = ApplyOuterBorder();
        outerBorder.Child = root;

        // Summary sentence
        var branch = string.IsNullOrWhiteSpace(blocked.TargetBranch)
            ? string.Empty
            : $" to branch '{blocked.TargetBranch}'";
        var summary = new TextBlock
        {
            Text         = $"{blocked.Condition}: the working tree has uncommitted changes. "
                         + $"Commit or stash them, then retry the switch{branch}.",
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 14),
        };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        root.Children.Add(summary);

        // Scrollable changed-files list
        var fileLabel = new TextBlock
        {
            Text   = $"Changed files ({blocked.ChangedPaths.Count})",
            Margin = new Thickness(0, 0, 0, 6),
        };
        fileLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        root.Children.Add(fileLabel);

        var fileStack = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        foreach (var path in blocked.ChangedPaths)
        {
            var item = new TextBlock
            {
                Text         = $"• {path}",
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 1, 0, 1),
                FontFamily   = new FontFamily("Consolas, Courier New, monospace"),
            };
            item.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
            fileStack.Children.Add(item);
        }

        var scroll = new ScrollViewer
        {
            Content          = fileStack,
            MaxHeight        = 200,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin           = new Thickness(0, 0, 0, 18),
        };
        root.Children.Add(scroll);

        // Reminder note
        var note = new TextBlock
        {
            Text         = "Commit or stash the changes above, then click Retry.",
            TextWrapping = TextWrapping.Wrap,
            FontStyle    = System.Windows.FontStyles.Italic,
            Margin       = new Thickness(0, 0, 0, 16),
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        root.Children.Add(note);

        // Button row
        var buttonRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        root.Children.Add(buttonRow);

        var dismissButton = new Button
        {
            Content  = "Dismiss",
            Width    = 88,
            Height   = 32,
            Margin   = new Thickness(0, 0, 10, 0),
            IsCancel = true,
        };
        dismissButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        dismissButton.Click += (_, _) => Close();
        buttonRow.Children.Add(dismissButton);

        var retryButton = new Button
        {
            Content   = "Retry",
            Height    = 32,
            Padding   = new Thickness(14, 0, 14, 0),
            IsDefault = true,
            Tag       = "retry",
        };
        retryButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        retryButton.Click += (_, _) => { ShouldRetry = true; Close(); };
        buttonRow.Children.Add(retryButton);
    }

    /// <summary>True when the user clicked Retry; false when dismissed.</summary>
    public bool ShouldRetry { get; private set; }
}
