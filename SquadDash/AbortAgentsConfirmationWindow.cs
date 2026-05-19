using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Collections.Generic;

namespace SquadDash;

internal sealed record AbortAgentsConfirmationTarget(
    string TaskId,
    string TaskKind,
    string DisplayLabel,
    DateTimeOffset StartedAt,
    bool IsCoordinator,
    string? AgentId = null,
    string? ToolCallId = null,
    string TaskIdSource = "unknown");

internal sealed class AbortAgentsConfirmationWindow : Window {
    private readonly List<AbortAgentsConfirmationTarget> _items = [];
    private readonly Func<IReadOnlyList<AbortAgentsConfirmationTarget>> _getTargets;
    private readonly DispatcherTimer _refreshTimer;
    private readonly StackPanel _listPanel;
    private readonly TextBlock _emptyText;
    private readonly Button _confirmButton;

    public IReadOnlyList<AbortAgentsConfirmationTarget> ConfirmedTargets { get; private set; }
        = Array.Empty<AbortAgentsConfirmationTarget>();

    public IReadOnlyList<AbortAgentsConfirmationTarget> SelectedTargets => ConfirmedTargets;

    public AbortAgentsConfirmationWindow(
        IReadOnlyList<AbortAgentsConfirmationTarget> targets,
        Func<IReadOnlyList<AbortAgentsConfirmationTarget>>? getTargets = null,
        Rect anchorScreenRect = default) {
        ArgumentNullException.ThrowIfNull(targets);

        _getTargets = getTargets ?? (() => targets);

        Title = "Confirm Stop";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        MinWidth = 420;
        MaxHeight = 560;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        this.SetResourceReference(BackgroundProperty, "AppSurface");

        if (anchorScreenRect == default)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            // Position right-bottom corner once actual height is known after render.
            ContentRendered += (_, _) =>
            {
                // anchorScreenRect is in physical pixels (from PointToScreen).
                // Left/Top are WPF logical units (DIPs). On high-DPI screens these
                // diverge — convert using the device-to-logical transform.
                var source = PresentationSource.FromVisual(this);
                var tfm = source?.CompositionTarget?.TransformFromDevice
                          ?? System.Windows.Media.Matrix.Identity;

                Left = anchorScreenRect.Right  * tfm.M11 - ActualWidth;
                Top  = anchorScreenRect.Top    * tfm.M22 - ActualHeight - 6;
            };
        }

        var root = new Grid {
            Margin = new Thickness(18)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var headerPanel = new StackPanel {
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(headerPanel);

        var title = new TextBlock {
            Text = "Stop all running work?",
            FontSize = (double)Application.Current.Resources["FontSizeTitle"],
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        headerPanel.Children.Add(title);

        var description = new TextBlock {
            Text = "SquadDash will stop the coordinator and every running agent listed below by stopping the local Squad bridge.",
            TextWrapping = TextWrapping.Wrap
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        headerPanel.Children.Add(description);

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

        _listPanel = new StackPanel();
        scroll.Content = _listPanel;

        _emptyText = new TextBlock {
            Text = "No running work.",
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        _emptyText.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");

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
            Content = "Stop All",
            Width = 96,
            Height = 32,
            IsDefault = true
        };
        _confirmButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        _confirmButton.Click += ConfirmButton_Click;
        buttonRow.Children.Add(_confirmButton);

        _refreshTimer = new DispatcherTimer {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += (_, _) => RefreshTargets(_getTargets());

        PreviewKeyDown += AbortAgentsConfirmationWindow_PreviewKeyDown;
        Closed += (_, _) => _refreshTimer.Stop();

        RefreshTargets(targets);
        _refreshTimer.Start();
    }

    private Border BuildTargetRow(AbortAgentsConfirmationTarget target) {
        var label = string.IsNullOrWhiteSpace(target.DisplayLabel)
            ? "Agent"
            : target.DisplayLabel.Trim();
        var timedLabel = $"{label} - {StatusTimingPresentation.FormatRelativeTimestamp(target.StartedAt)}";

        var secondaryText = target.IsCoordinator
            ? "Coordinator thread"
            : target.TaskKind.Equals("shell", StringComparison.OrdinalIgnoreCase)
                ? "Shell task"
                : "Agent thread";

        var textPanel = new StackPanel();

        var primary = new TextBlock {
            Text = timedLabel,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold
        };
        primary.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        textPanel.Children.Add(primary);

        var secondary = new TextBlock {
            Text = secondaryText,
            TextWrapping = TextWrapping.Wrap,
            FontSize = (double)Application.Current.Resources["FontSizeBody"],
            Margin = new Thickness(0, 2, 0, 0)
        };
        secondary.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        textPanel.Children.Add(secondary);

        var border = new Border {
            Child = textPanel,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 10),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1)
        };
        border.SetResourceReference(Border.BackgroundProperty, "AppSurface");
        border.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");

        return border;
    }

    private void RefreshTargets(IReadOnlyList<AbortAgentsConfirmationTarget> targets) {
        _items.Clear();
        _listPanel.Children.Clear();

        foreach (var target in targets) {
            var row = BuildTargetRow(target);
            _items.Add(target);
            _listPanel.Children.Add(row);
        }

        if (_items.Count == 0)
            _listPanel.Children.Add(_emptyText);

        _emptyText.Visibility = _items.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateConfirmButtonState();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) {
        ConfirmedTargets = _items.ToArray();

        DialogResult = true;
        Close();
    }

    private void UpdateConfirmButtonState() {
        if (_confirmButton is null)
            return;

        _confirmButton.IsEnabled = _items.Count > 0;
    }

    private void AbortAgentsConfirmationWindow_PreviewKeyDown(object sender, KeyEventArgs e) {
        if (e.Key != Key.Escape)
            return;

        DialogResult = false;
        Close();
    }
}
