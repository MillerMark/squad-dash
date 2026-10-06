using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SquadDash;

internal sealed class SquadCommandOutputWindow : ChromedWindow {
    private readonly TextBox _outputBox;
    private readonly ProgressBar _busyIndicator;
    private readonly TextBlock _busyText;
    private readonly DispatcherTimer _elapsedTimer;
    private readonly Stopwatch _elapsed = new();
    private string _busyMessage = string.Empty;
    private bool _hasLiveOutput;
    private bool _isClosed;

    public SquadCommandOutputWindow(
        string title,
        string output,
        bool showDelegateButton = false,
        bool isBusy = false) : base(captionHeight: CloseButtonHeight) {
        Title = title;
        Width = 720;
        Height = 480;
        MinWidth = 520;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ApplyOuterBorder(titleText: title).Child = root;

        var busyPanel = new StackPanel {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        _busyIndicator = new ProgressBar {
            IsIndeterminate = true,
            Width = 110,
            Height = 12,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _busyIndicator.SetResourceReference(StyleProperty, "CommandOutputProgressBarStyle");
        _busyIndicator.SetResourceReference(Control.BackgroundProperty, "TrackSurface");
        _busyIndicator.SetResourceReference(Control.ForegroundProperty, "ActionLinkText");
        _busyText = new TextBlock {
            VerticalAlignment = VerticalAlignment.Center
        };
        _busyText.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        _elapsedTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += (_, _) => UpdateBusyText();
        Closed += (_, _) => {
            _isClosed = true;
            SetBusy(false);
        };
        busyPanel.Children.Add(_busyIndicator);
        busyPanel.Children.Add(_busyText);
        Grid.SetRow(busyPanel, 0);
        root.Children.Add(busyPanel);

        var outputBorder = new Border {
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10)
        };
        outputBorder.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        outputBorder.SetResourceReference(Border.BorderBrushProperty, "LineColor");
        Grid.SetRow(outputBorder, 1);
        root.Children.Add(outputBorder);

        _outputBox = new TextBox {
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0)
        };
        _outputBox.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        _outputBox.SetResourceReference(Control.FontSizeProperty, "FontSizeNormal");
        _outputBox.SetResourceReference(Control.BackgroundProperty, "CardSurface");
        _outputBox.SetResourceReference(Control.ForegroundProperty, "LabelText");
        outputBorder.Child = _outputBox;

        var buttons = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        if (showDelegateButton) {
            var delegateButton = new Button {
                Content = "Delegate...",
                MinWidth = 96,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            delegateButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
            delegateButton.Click += (_, _) => {
                DelegateRequested = true;
                DialogResult = true;
            };
            buttons.Children.Add(delegateButton);
        }

        var closeButton = new Button {
            Content = "Close",
            MinWidth = 80,
            IsCancel = true
        };
        closeButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        closeButton.Click += (_, _) => Close();
        buttons.Children.Add(closeButton);

        SetOutput(output);
        SetBusy(isBusy);
    }

    public bool DelegateRequested { get; private set; }

    public void SetTitle(string title) {
        Title = title;
        if (TitleLabel is not null) TitleLabel.Text = title;
    }

    public void SetOutput(string output) {
        _outputBox.Text = string.IsNullOrWhiteSpace(output) ? "(no output)" : output.TrimEnd();
        _outputBox.ScrollToHome();
    }

    public void AppendOutputLine(string line) {
        // Queued progress must not overwrite the completed summary or resurrect a closed window.
        if (!_elapsed.IsRunning || _isClosed) return;
        if (!_hasLiveOutput) {
            _outputBox.Clear();
            _hasLiveOutput = true;
        }
        _outputBox.AppendText(line + Environment.NewLine);
        _outputBox.ScrollToEnd();
    }

    public void SetBusy(bool isBusy, string message = "Executing command\u2026") {
        isBusy &= !_isClosed;
        _busyIndicator.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        _busyText.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        _busyMessage = message;
        if (isBusy) {
            if (!_elapsed.IsRunning) {
                _hasLiveOutput = false;
                _elapsed.Restart();
            }
            UpdateBusyText();
            _elapsedTimer.Start();
        }
        else {
            _elapsedTimer.Stop();
            _elapsed.Reset();
            _busyText.Text = string.Empty;
        }
    }

    private void UpdateBusyText() {
        var elapsed = _elapsed.Elapsed;
        _busyText.Text = $"{_busyMessage} {(long)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
    }
}
