using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Asks the user whether to continue from preserved (uncommitted) work.
/// Replaces the verbose MessageBox.Show Yes/No dialog with a focused,
/// consequence-oriented prompt and a collapsible file list.
/// </summary>
internal sealed class ConfirmPreservedWorkDialog : ChromedWindow
{
    public bool Confirmed { get; private set; }

    public ConfirmPreservedWorkDialog(
        string taskId,
        IReadOnlyList<string> preservedPaths,
        Window? owner = null) : base(captionHeight: 36, resizeMode: ResizeMode.NoResize)
    {
        Title                  = "Continue From Preserved Work?";
        Width                  = 480;
        SizeToContent          = SizeToContent.Height;
        MinWidth               = 380;
        MaxHeight              = 600;
        ShowInTaskbar          = false;
        WindowStartupLocation  = WindowStartupLocation.CenterOwner;
        if (owner is not null) Owner = owner;

        var root = new StackPanel { Margin = new Thickness(20) };
        var outerBorder = ApplyOuterBorder();
        outerBorder.Child = root;

        // Body text
        var body = new TextBlock
        {
            Text = $"Task {taskId} left uncommitted changes. " +
                   "SquadDash will verify the files are unchanged before starting, " +
                   "and requires one clean commit before advancing the plan.",
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 14),
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        root.Children.Add(body);

        // Collapsible "Changed files (N)" disclosure
        var expander = new Expander
        {
            Header  = $"▶ Changed files ({preservedPaths.Count})",
            Margin  = new Thickness(0, 0, 0, 16),
        };
        expander.SetResourceReference(Expander.ForegroundProperty, "LabelText");

        var filePanel = new StackPanel { Margin = new Thickness(12, 6, 0, 0) };
        foreach (var path in preservedPaths)
        {
            var item = new TextBlock
            {
                Text         = $"• {path}",
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 1, 0, 1),
                FontFamily   = new FontFamily("Consolas, Courier New, monospace"),
            };
            item.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
            filePanel.Children.Add(item);
        }
        expander.Content = filePanel;
        root.Children.Add(expander);

        // Button row
        var buttonRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        root.Children.Add(buttonRow);

        var cancelButton = new Button
        {
            Content  = "Cancel",
            Width    = 96,
            Height   = 32,
            Margin   = new Thickness(0, 0, 10, 0),
            IsCancel = true,
        };
        cancelButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        cancelButton.Click += (_, _) => { Confirmed = false; Close(); };
        buttonRow.Children.Add(cancelButton);

        var continueButton = new Button
        {
            Content   = "Continue Preserved Work",
            Height    = 32,
            Padding   = new Thickness(14, 0, 14, 0),
            IsDefault = true,
        };
        continueButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        continueButton.Click += (_, _) => { Confirmed = true; Close(); };
        buttonRow.Children.Add(continueButton);
    }
}
