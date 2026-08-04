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
    private readonly bool _hostRecordedRange;

    private VerifiedCommitRangeDialog(
        string taskId,
        string baselineCommit,
        IReadOnlyList<RecoveryCommitRangeEntry> entries,
        bool hostRecordedRange)
        : base(captionHeight: 34, resizeMode: ResizeMode.CanResize, resizeBorderThickness: 6)
    {
        _hostRecordedRange = hostRecordedRange;
        Title = "Review Completed Work";
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
            Text = hostRecordedRange
                ? $"SquadDash recorded this exact commit range for {taskId}. Review the commits after {Short(baselineCommit)} below, then continue only if the work satisfies the task. Unrelated later commits are excluded."
                : $"Select the last commit produced for {taskId}. SquadDash will review every commit after {Short(baselineCommit)} through your selection. No plan state changes until you review the range and accept it.",
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
        // A host-recorded range has already been scoped to the exact task attempt, so select its
        // terminal commit. Fallback discovery deliberately remains unselected because later
        // unrelated commits may follow the interrupted task.
        _commits.SelectedIndex = hostRecordedRange ? entries.Count - 1 : -1;
        Grid.SetRow(_commits, 1);
        root.Children.Add(_commits);

        var adopt = new Button
        {
            Content = hostRecordedRange ? "Review Recorded Work" : "Review Selected Range",
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
        IReadOnlyList<RecoveryCommitRangeEntry> entries,
        bool hostRecordedRange = false)
    {
        var dialog = new VerifiedCommitRangeDialog(taskId, baselineCommit, entries, hostRecordedRange) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.SelectedEntry : null;
    }

    private void AcceptSelection()
    {
        if (_hostRecordedRange && _commits.Items.Count > 0)
            _commits.SelectedIndex = _commits.Items.Count - 1;
        if (_commits.SelectedItem is not ListBoxItem { Tag: RecoveryCommitRangeEntry entry })
            return;
        SelectedEntry = entry;
        DialogResult = true;
        Close();
    }

    private static string Short(string commit) =>
        commit.Length <= 7 ? commit : commit[..7];
}
