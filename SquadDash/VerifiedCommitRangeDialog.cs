using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SquadDash;

/// <summary>
/// Lets the user identify the terminal commit of preserved task work. SquadDash validates
/// the complete baseline-to-terminal range after selection; this window makes no trust decision.
/// </summary>
internal sealed class VerifiedCommitRangeDialog : ChromedWindow
{
    private readonly ListBox _commits;

    private VerifiedCommitRangeDialog(
        string taskId,
        string baselineCommit,
        IReadOnlyList<RecoveryCommitRangeEntry> entries)
        : base(captionHeight: 34, resizeMode: ResizeMode.CanResize, resizeBorderThickness: 6)
    {
        Title = "Adopt Verified Commit Range";
        Width = 720;
        Height = 480;
        MinWidth = 560;
        MinHeight = 360;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var contentArea = ApplyOuterBorder("AppSurface", Title);
        var root = new Grid { Margin = new Thickness(16, 12, 16, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var instructions = new TextBlock
        {
            Text = $"Select the last commit that belongs to {taskId}. SquadDash will adopt every commit after {Short(baselineCommit)} through the selected commit, validate the changed paths, and leave later commits outside the task range.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        instructions.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        instructions.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        root.Children.Add(instructions);

        _commits = new ListBox { Margin = new Thickness(0, 0, 0, 12) };
        _commits.SetResourceReference(Control.BackgroundProperty, "TextBoxBackground");
        _commits.SetResourceReference(Control.BorderBrushProperty, "InputBorder");
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var row = new TextBlock
            {
                Text = $"{index + 1} commit{(index == 0 ? string.Empty : "s")} · {Short(entry.Commit)}  {entry.Subject}",
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(6, 4, 6, 4),
            };
            row.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            row.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
            _commits.Items.Add(new ListBoxItem { Content = row, Tag = entry });
        }
        // Deliberately leave the range unselected. Later unrelated commits may follow the
        // interrupted task, so defaulting to HEAD could silently attribute too much work.
        _commits.SelectedIndex = -1;
        Grid.SetRow(_commits, 1);
        root.Children.Add(_commits);

        var adopt = new Button
        {
            Content = "Review Selected Range",
            MinWidth = 150,
            Height = 28,
            Margin = new Thickness(0, 0, 8, 0),
        };
        adopt.SetResourceReference(StyleProperty, "ThemedButtonStyle");
        adopt.Click += (_, _) => AcceptSelection();

        var cancel = new Button { Content = "Cancel", Width = 80, Height = 28 };
        cancel.SetResourceReference(StyleProperty, "ThemedButtonStyle");
        cancel.Click += (_, _) => { DialogResult = false; Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(adopt);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        contentArea.Child = root;
        _commits.MouseDoubleClick += (_, _) => AcceptSelection();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) AcceptSelection();
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        };
    }

    internal RecoveryCommitRangeEntry? SelectedEntry { get; private set; }

    internal static RecoveryCommitRangeEntry? Show(
        Window owner,
        string taskId,
        string baselineCommit,
        IReadOnlyList<RecoveryCommitRangeEntry> entries)
    {
        var dialog = new VerifiedCommitRangeDialog(taskId, baselineCommit, entries) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.SelectedEntry : null;
    }

    private void AcceptSelection()
    {
        if (_commits.SelectedItem is not ListBoxItem { Tag: RecoveryCommitRangeEntry entry })
            return;
        SelectedEntry = entry;
        DialogResult = true;
        Close();
    }

    private static string Short(string commit) =>
        commit.Length <= 7 ? commit : commit[..7];
}
