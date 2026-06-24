using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SquadDash.Hints;

namespace SquadDash;

internal partial class HintAuthoringWindow : Window
{
    private const string DefaultMarkdown = "**This text appears in the callout.**\n\nEdit this hint text.";

    private readonly DispatcherTimer _debounceTimer;
    private HintDefinition? _editingHint;
    private string _workspaceRoot = string.Empty;
    private FrmUltimateCallout? _previewCallout;

    public HintAuthoringWindow()
    {
        InitializeComponent();

        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _debounceTimer.Tick += DebounceTimer_Tick;

        Closed += (_, _) => { _previewCallout?.Close(); _previewCallout = null; };

        MarkdownEditor.PreviewKeyDown += MarkdownEditor_PreviewKeyDown;
    }

    /// <summary>
    /// Pre-fills the window with the target control ID and optionally an existing hint to edit.
    /// </summary>
    public void Initialize(string targetControlId, string workspaceRoot, HintDefinition? existing = null, FrameworkElement? target = null)
    {
        _workspaceRoot = workspaceRoot;
        TargetBox.Text = targetControlId;

        if (existing is not null)
        {
            _editingHint = existing;
            MarkdownEditor.Text = existing.MarkdownText;
            PriorityBox.Text = existing.Priority.ToString();
            MaxShowCountBox.Text = existing.MaxShowCount.ToString();

            foreach (ComboBoxItem item in TriggerComboBox.Items)
            {
                if (item.Tag?.ToString() == existing.Trigger.ToString())
                {
                    TriggerComboBox.SelectedItem = item;
                    break;
                }
            }

            ActionIdBox.Text = existing.ActionId ?? "";
            TriggerNotesBox.Text = existing.TriggerNotes ?? "";

            foreach (ComboBoxItem item in ConditionComboBox.Items)
            {
                if (item.Tag?.ToString() == existing.ConditionId)
                {
                    ConditionComboBox.SelectedItem = item;
                    break;
                }
            }
        }
        else
        {
            MarkdownEditor.Text = DefaultMarkdown;
            TriggerNotesBox.Text = string.Empty;
        }

        MarkdownPreview.Markdown = MarkdownEditor.Text;
        ShowPreviewCallout(target);
    }

    private void ShowPreviewCallout(FrameworkElement? target)
    {
        if (target is null) return;

        var isDark = Application.Current.Resources.Contains("IsDarkTheme")
                     && (bool)Application.Current.Resources["IsDarkTheme"];
        var theme = isDark ? CalloutTheme.Dark : CalloutTheme.Light;

        var fontSize = Application.Current.Resources.Contains("FontSizeBody")
            ? Convert.ToDouble(Application.Current.Resources["FontSizeBody"])
            : 13.0;

        _previewCallout = FrmUltimateCallout.ShowCalloutBesideTarget(
            MarkdownEditor.Text, target, theme: theme, fontSize: fontSize);
    }

    private void MarkdownEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
        {
            MarkdownEditorCommands.ApplyBold(MarkdownEditor);
            e.Handled = true;
        }
        else if (e.Key == Key.I && Keyboard.Modifiers == ModifierKeys.Control)
        {
            MarkdownEditorCommands.ApplyItalic(MarkdownEditor);
            e.Handled = true;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void MarkdownEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        MarkdownPreview.Markdown = MarkdownEditor.Text;

        if (_previewCallout is { IsLoaded: true } callout && callout.IsVisible)
            callout.UpdateMarkdown(MarkdownEditor.Text);
    }

    private void TriggerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActionIdBox is null) return;
        if (TriggerComboBox.SelectedItem is ComboBoxItem item)
        {
            bool isAction = item.Tag?.ToString() == "Action";
            var vis = isAction ? Visibility.Visible : Visibility.Collapsed;
            ActionIdBox.Visibility = vis;
            ActionIdLabel.Visibility = vis;
        }
    }

    private HintDefinition BuildHintDefinition()
    {
        var hint = _editingHint ?? new HintDefinition();

        hint.TargetControlId = TargetBox.Text.Trim();
        hint.MarkdownText    = MarkdownEditor.Text;

        if (string.IsNullOrWhiteSpace(hint.HintId))
        {
            // Auto-generate a stable ID from the target name + current timestamp
            var safe = hint.TargetControlId
                .Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
            hint.HintId = $"{safe}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        }

        hint.Priority = int.TryParse(PriorityBox.Text.Trim(), out int p) ? p : 100;
        hint.MaxShowCount = int.TryParse(MaxShowCountBox.Text.Trim(), out int m) ? m : 3;

        if (TriggerComboBox.SelectedItem is ComboBoxItem trigItem)
            hint.Trigger = trigItem.Tag?.ToString() == "Action" ? HintTrigger.Action : HintTrigger.Idle;

        hint.TriggerNotes = TriggerNotesBox.Text.Trim();
        hint.ActionId = ActionIdBox.Visibility == Visibility.Visible
            ? ActionIdBox.Text.Trim()
            : null;

        if (ConditionComboBox.SelectedItem is ComboBoxItem condItem)
            hint.ConditionId = condItem.Tag?.ToString();

        return hint;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var hint = BuildHintDefinition();

        if (string.IsNullOrWhiteSpace(hint.TargetControlId))
        {
            StatusText.Text = "Target control ID is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            StatusText.Text = "No workspace open — cannot save hint registry.";
            return;
        }

        try
        {
            var persistence = new HintPersistence();
            var registry = persistence.LoadRegistry(_workspaceRoot);
            var idx = registry.FindIndex(h => h.HintId == hint.HintId);
            if (idx >= 0)
                registry[idx] = hint;
            else
                registry.Add(hint);
            persistence.SaveRegistry(_workspaceRoot, registry);

            StatusText.Text = "Saved.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TestButton_Click(object sender, RoutedEventArgs e)
    {
        var hint = BuildHintDefinition();
        // Show the callout pointing at the Test button so the author can see the rendered markdown
        FrmUltimateCallout.ShowCallout(hint.MarkdownText, TestButton);
    }
}
