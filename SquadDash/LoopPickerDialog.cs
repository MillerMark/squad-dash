using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Dialog shown when the user clicks "Do These" while the loop panel is visible and the
/// currently selected loop is not the filtered-tasks loop.  Lets the user choose which
/// loop to run before proceeding.
/// </summary>
internal sealed class LoopPickerDialog : Window
{
    private readonly RadioButton _radioCurrentLoop;
    private readonly RadioButton _radioFilteredTasks;
    private readonly RadioButton? _radioOtherLoop;
    private readonly ComboBox?   _otherLoopCombo;

    private readonly string _currentLoopPath;
    private readonly string _filteredTasksPath;
    private readonly IReadOnlyList<LoopFileEntry> _otherLoops;

    /// <summary>The loop file path chosen by the user. Valid only when <see cref="ShowDialog"/> returned <c>true</c>.</summary>
    public string SelectedLoopPath { get; private set; }

    public LoopPickerDialog(
        string currentLoopPath,
        string currentLoopLabel,
        string filteredTasksPath,
        IReadOnlyList<LoopFileEntry> otherLoops,
        Window owner)
    {
        _currentLoopPath    = currentLoopPath;
        _filteredTasksPath  = filteredTasksPath;
        _otherLoops         = otherLoops;
        SelectedLoopPath    = filteredTasksPath; // default selection

        Owner                    = owner;
        Width                    = 460;
        SizeToContent            = SizeToContent.Height;
        MinWidth                 = 360;
        ResizeMode               = ResizeMode.NoResize;
        ShowInTaskbar            = false;
        WindowStyle              = WindowStyle.None;
        WindowStartupLocation    = WindowStartupLocation.CenterOwner;
        this.SetResourceReference(BackgroundProperty, "AppSurface");

        var outer = new DockPanel();
        Content = outer;

        // ── Custom title bar ─────────────────────────────────────────────────
        var titleBar = new Grid { Height = 36 };
        titleBar.SetResourceReference(BackgroundProperty, "ChromeSurface");
        DockPanel.SetDock(titleBar, Dock.Top);
        outer.Children.Add(titleBar);

        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text              = "Starting Loop for Filtered Tasks",
            FontSize = (double)Application.Current.Resources["FontSizeBody"],
            FontWeight        = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(14, 0, 0, 0),
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        Grid.SetColumn(titleText, 0);
        titleBar.Children.Add(titleText);

        var closeBtn = new Button
        {
            Content           = "✕",
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
            Width             = 28,
            Height            = 28,
            VerticalAlignment = VerticalAlignment.Top,
            Margin            = new Thickness(0, 4, 4, 0),
            BorderThickness   = new Thickness(0),
            Background        = Brushes.Transparent,
            Cursor            = Cursors.Hand,
        };
        closeBtn.SetResourceReference(Button.ForegroundProperty, "LabelText");
        closeBtn.Click += (_, _) => { DialogResult = false; };
        Grid.SetColumn(closeBtn, 1);
        titleBar.Children.Add(closeBtn);

        titleBar.MouseLeftButtonDown += (_, _) => DragMove();

        // ── Body ─────────────────────────────────────────────────────────────
        var body = new StackPanel { Margin = new Thickness(18, 14, 18, 18) };
        outer.Children.Add(body);

        var prompt = new TextBlock
        {
            Text         = "Which loop should run for the filtered tasks?",
            FontSize = (double)Application.Current.Resources["FontSizeNormal"],
            FontWeight   = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 14),
        };
        prompt.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        body.Children.Add(prompt);

        // Radio 1 — current loop
        _radioCurrentLoop = new RadioButton
        {
            Content   = $"Use the currently selected loop: \"{currentLoopLabel}\"",
            IsChecked = false,
            Margin    = new Thickness(0, 0, 0, 8),
            GroupName = "LoopChoice",
        };
        _radioCurrentLoop.SetResourceReference(RadioButton.ForegroundProperty, "LabelText");
        body.Children.Add(_radioCurrentLoop);

        // Radio 2 — filtered-tasks loop (default)
        _radioFilteredTasks = new RadioButton
        {
            Content   = "Use the Filtered Tasks loop",
            IsChecked = true,
            Margin    = new Thickness(0, 0, 0, otherLoops.Count > 0 ? 8 : 16),
            GroupName = "LoopChoice",
        };
        _radioFilteredTasks.SetResourceReference(RadioButton.ForegroundProperty, "LabelText");
        body.Children.Add(_radioFilteredTasks);

        // Radio 3 + ComboBox — only when there are additional loops beyond current + filtered-tasks
        if (otherLoops.Count > 0)
        {
            var row3 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 0, 0, 16),
            };
            body.Children.Add(row3);

            _radioOtherLoop = new RadioButton
            {
                Content       = "Use another loop:",
                IsChecked     = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin        = new Thickness(0, 0, 8, 0),
                GroupName     = "LoopChoice",
            };
            _radioOtherLoop.SetResourceReference(RadioButton.ForegroundProperty, "LabelText");
            row3.Children.Add(_radioOtherLoop);

            _otherLoopCombo = new ComboBox
            {
                Height    = 26,
                MinWidth  = 160,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _otherLoopCombo.SetResourceReference(ComboBox.StyleProperty, "ThemedComboBoxStyle");
            foreach (var entry in otherLoops)
                _otherLoopCombo.Items.Add(entry.DisplayName);
            _otherLoopCombo.SelectedIndex = 0;

            // Selecting the combo auto-selects the third radio
            _otherLoopCombo.SelectionChanged += (_, _) =>
            {
                if (_radioOtherLoop is not null)
                    _radioOtherLoop.IsChecked = true;
            };
            row3.Children.Add(_otherLoopCombo);
        }
        else
        {
            _radioFilteredTasks.Margin = new Thickness(0, 0, 0, 16);
        }

        // ── Button row: spacer | Cancel | OK ─────────────────────────────────
        var buttonRow = new Grid();
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.Children.Add(buttonRow);

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width   = 80,
            Height  = 30,
            Margin  = new Thickness(0, 0, 8, 0),
        };
        cancelBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        cancelBtn.Click += (_, _) => { DialogResult = false; };
        Grid.SetColumn(cancelBtn, 1);
        buttonRow.Children.Add(cancelBtn);

        var okBtn = new Button
        {
            Content   = "OK",
            Width     = 80,
            Height    = 30,
            IsDefault = true,
        };
        okBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        okBtn.Click += OkButton_Click;
        Grid.SetColumn(okBtn, 2);
        buttonRow.Children.Add(okBtn);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        };
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_radioCurrentLoop.IsChecked == true)
        {
            SelectedLoopPath = _currentLoopPath;
        }
        else if (_radioFilteredTasks.IsChecked == true)
        {
            SelectedLoopPath = _filteredTasksPath;
        }
        else if (_radioOtherLoop?.IsChecked == true && _otherLoopCombo is not null)
        {
            int idx = _otherLoopCombo.SelectedIndex;
            if (idx >= 0 && idx < _otherLoops.Count)
                SelectedLoopPath = _otherLoops[idx].FilePath;
            else
                SelectedLoopPath = _filteredTasksPath;
        }

        DialogResult = true;
    }
}
