using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Keyboard-first, single-recipient picker shown when more than one live agent
/// session can receive an immediate steering message.
/// </summary>
internal sealed class SteeringTargetDialog : ChromedWindow {
    private readonly IReadOnlyList<SteeringTargetCandidate> _allTargets;
    private readonly List<(RadioButton Radio, Border Row, SteeringTargetCandidate Target)> _choices = [];
    private readonly Button _steerButton;
    private readonly Button _cancelButton;
    private RadioButton? _focusedRadio;

    internal SteeringTargetCandidate? SelectedTarget { get; private set; }

    internal SteeringTargetDialog(
        IReadOnlyList<SteeringTargetCandidate> targets,
        SteeringTargetCandidate defaultTarget)
        : base(captionHeight: CloseButtonHeight, resizeMode: ResizeMode.NoResize,
            resizeBorderThickness: 0) {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(defaultTarget);
        if (targets.Count == 0)
            throw new ArgumentException("At least one steering target is required.", nameof(targets));

        _allTargets = SteeringTargetResolver.OrderForDisplay(targets);
        Title = "Steer active work";
        Width = 560;
        MinWidth = 460;
        MaxWidth = 720;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 680;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(18, 16, 18, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var guidance = new TextBlock {
            Text = "Choose the agent that should receive this update. Quiet owners remain available while they wait for child work.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        guidance.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        guidance.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        Grid.SetRow(guidance, 0);
        root.Children.Add(guidance);

        var targetPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var target in _allTargets)
            targetPanel.Children.Add(BuildTargetRow(target, defaultTarget));

        var scroller = new ScrollViewer {
            Content = targetPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 390,
            Focusable = false,
        };
        scroller.SetResourceReference(Control.BackgroundProperty, "InputSurface");
        var listBorder = new Border {
            Child = scroller,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 14),
        };
        listBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        listBorder.SetResourceReference(Border.BackgroundProperty, "InputSurface");
        Grid.SetRow(listBorder, 1);
        root.Children.Add(listBorder);

        var buttonRow = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _cancelButton = new Button {
            Content = "Cancel",
            Width = 96,
            Height = 32,
            Margin = new Thickness(0, 0, 10, 0),
            IsCancel = true,
        };
        _cancelButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        _cancelButton.Click += (_, _) => Cancel();
        buttonRow.Children.Add(_cancelButton);

        _steerButton = new Button {
            Content = "Steer",
            Width = 96,
            Height = 32,
            IsDefault = true,
        };
        _steerButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        _steerButton.Click += (_, _) => Submit();
        buttonRow.Children.Add(_steerButton);
        Grid.SetRow(buttonRow, 2);
        root.Children.Add(buttonRow);

        var holder = ApplyOuterBorder(titleText: Title);
        holder.Child = root;
        KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);
        PreviewKeyDown += HandlePreviewKeyDown;
        Loaded += (_, _) => {
            var selected = _choices.FirstOrDefault(choice => choice.Radio.IsChecked == true);
            (selected.Radio ?? _choices.FirstOrDefault().Radio)?.Focus();
            RefreshChoiceVisuals();
        };
    }

    private UIElement BuildTargetRow(
        SteeringTargetCandidate target,
        SteeringTargetCandidate defaultTarget) {
        var depth = SteeringTargetResolver.ResolveDepth(target, _allTargets);
        var row = new Border {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 7, 8, 7),
            Margin = new Thickness(depth * 24, 1, 0, 1),
            Background = Brushes.Transparent,
        };

        var label = new StackPanel { Orientation = Orientation.Vertical };
        var name = new TextBlock {
            Text = target.DisplayName,
            FontWeight = target.ParentId is null ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        name.SetResourceReference(TextBlock.ForegroundProperty,
            target.CanSteer ? "LabelText" : "SubtleText");
        label.Children.Add(name);

        if (!string.IsNullOrWhiteSpace(target.StatusText) || !target.CanSteer) {
            var status = new TextBlock {
                Text = target.CanSteer
                    ? target.StatusText
                    : "Steering unavailable for this session",
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            status.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            status.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            label.Children.Add(status);
        }

        if (!target.CanSteer) {
            row.Child = label;
            row.Opacity = 0.72;
            return row;
        }

        var radio = new RadioButton {
            Content = label,
            GroupName = "SteeringTarget",
            IsChecked = string.Equals(target.Id, defaultTarget.Id, StringComparison.Ordinal),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = target,
            IsTabStop = true,
        };
        radio.SetResourceReference(Control.StyleProperty, "ThemedRadioButtonStyle");
        radio.GotKeyboardFocus += (_, _) => {
            _focusedRadio = radio;
            RefreshChoiceVisuals();
        };
        radio.LostKeyboardFocus += (_, _) => RefreshChoiceVisuals();
        radio.Checked += (_, _) => RefreshChoiceVisuals();
        row.MouseLeftButtonDown += (_, _) => {
            radio.IsChecked = true;
            radio.Focus();
        };
        row.Child = radio;
        _choices.Add((radio, row, target));
        return row;
    }

    private void HandlePreviewKeyDown(object sender, KeyEventArgs e) {
        switch (e.Key) {
            case Key.Tab:
                MoveRadioFocus(e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveRadioFocus(-1);
                e.Handled = true;
                break;
            case Key.Down:
                MoveRadioFocus(1);
                e.Handled = true;
                break;
            case Key.Space when _focusedRadio is not null:
                _focusedRadio.IsChecked = true;
                e.Handled = true;
                break;
            case Key.Enter:
                Submit();
                e.Handled = true;
                break;
            case Key.Escape:
                Cancel();
                e.Handled = true;
                break;
        }
    }

    private void MoveRadioFocus(int delta) {
        if (_choices.Count == 0)
            return;

        var currentIndex = _focusedRadio is null
            ? _choices.FindIndex(choice => choice.Radio.IsChecked == true)
            : _choices.FindIndex(choice => ReferenceEquals(choice.Radio, _focusedRadio));
        if (currentIndex < 0)
            currentIndex = 0;
        var nextIndex = (currentIndex + delta + _choices.Count) % _choices.Count;
        _choices[nextIndex].Radio.Focus();
    }

    private void RefreshChoiceVisuals() {
        foreach (var choice in _choices) {
            if (choice.Radio.IsKeyboardFocusWithin) {
                choice.Row.SetResourceReference(Border.BackgroundProperty, "FocusedSelectedItem");
            }
            else if (choice.Radio.IsChecked == true) {
                choice.Row.SetResourceReference(Border.BackgroundProperty, "UnfocusedSelectedItem");
            }
            else {
                choice.Row.Background = Brushes.Transparent;
            }
        }
    }

    private void Submit() {
        var selected = _choices.FirstOrDefault(choice => choice.Radio.IsChecked == true);
        if (selected.Target is null)
            return;

        SelectedTarget = selected.Target;
        DialogResult = true;
        Close();
    }

    private void Cancel() {
        DialogResult = false;
        Close();
    }
}
