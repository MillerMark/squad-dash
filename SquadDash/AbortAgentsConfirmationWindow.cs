using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Collections.Generic;

namespace SquadDash;

internal sealed record AbortAgentsConfirmationTarget(
    string TaskId,
    string TaskKind,
    string DisplayLabel,
    bool IsCoordinator);

internal sealed class AbortAgentsConfirmationWindow : Window {
    private readonly List<(CheckBox CheckBox, AbortAgentsConfirmationTarget Target)> _items = [];
    private readonly Button _confirmButton;

    public IReadOnlyList<AbortAgentsConfirmationTarget> SelectedTargets { get; private set; }
        = Array.Empty<AbortAgentsConfirmationTarget>();

    public AbortAgentsConfirmationWindow(IReadOnlyList<AbortAgentsConfirmationTarget> targets) {
        ArgumentNullException.ThrowIfNull(targets);

        Title = "Confirm Abort";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        MinWidth = 420;
        MaxHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        this.SetResourceReference(BackgroundProperty, "AppSurface");

        var root = new Grid {
            Margin = new Thickness(18)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var title = new TextBlock {
            Text = "Abort these agents?",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 14)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        root.Children.Add(title);

        var listBorder = new Border {
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            MaxHeight = 360
        };
        listBorder.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        listBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        Grid.SetRow(listBorder, 1);
        root.Children.Add(listBorder);

        var scroll = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        listBorder.Child = scroll;

        var list = new StackPanel();
        scroll.Content = list;

        foreach (var target in targets) {
            var checkBox = BuildTargetCheckBox(target);
            checkBox.Checked += (_, _) => UpdateConfirmButtonState();
            checkBox.Unchecked += (_, _) => UpdateConfirmButtonState();
            _items.Add((checkBox, target));
            list.Children.Add(checkBox);
        }

        var buttonRow = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        Grid.SetRow(buttonRow, 2);
        root.Children.Add(buttonRow);

        var cancelButton = new Button {
            Content = "Cancel",
            Width = 96,
            Height = 32,
            Margin = new Thickness(0, 0, 10, 0),
            IsCancel = true
        };
        cancelButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        buttonRow.Children.Add(cancelButton);

        _confirmButton = new Button {
            Content = "Confirm",
            Width = 96,
            Height = 32,
            IsDefault = true
        };
        _confirmButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        _confirmButton.Click += ConfirmButton_Click;
        buttonRow.Children.Add(_confirmButton);

        PreviewKeyDown += AbortAgentsConfirmationWindow_PreviewKeyDown;
        UpdateConfirmButtonState();
    }

    private CheckBox BuildTargetCheckBox(AbortAgentsConfirmationTarget target) {
        var label = string.IsNullOrWhiteSpace(target.DisplayLabel)
            ? "Agent"
            : target.DisplayLabel.Trim();

        var secondaryText = target.IsCoordinator
            ? "Coordinator thread"
            : target.TaskKind.Equals("shell", StringComparison.OrdinalIgnoreCase)
                ? "Shell task"
                : "Agent thread";

        var textPanel = new StackPanel {
            Margin = new Thickness(8, 0, 0, 0)
        };

        var primary = new TextBlock {
            Text = label,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold
        };
        primary.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        textPanel.Children.Add(primary);

        var secondary = new TextBlock {
            Text = secondaryText,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0)
        };
        secondary.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        textPanel.Children.Add(secondary);

        var checkBox = new CheckBox {
            Content = textPanel,
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 10),
            VerticalContentAlignment = VerticalAlignment.Top
        };
        checkBox.SetResourceReference(Control.ForegroundProperty, "LabelText");

        return checkBox;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) {
        SelectedTargets = _items
            .Where(item => item.CheckBox.IsChecked == true)
            .Select(item => item.Target)
            .ToArray();

        DialogResult = true;
        Close();
    }

    private void UpdateConfirmButtonState() {
        if (_confirmButton is null)
            return;

        _confirmButton.IsEnabled = _items.Any(item => item.CheckBox.IsChecked == true);
    }

    private void AbortAgentsConfirmationWindow_PreviewKeyDown(object sender, KeyEventArgs e) {
        if (e.Key != Key.Escape)
            return;

        DialogResult = false;
        Close();
    }
}
