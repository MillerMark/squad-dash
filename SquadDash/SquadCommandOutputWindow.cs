using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash;

internal sealed class SquadCommandOutputWindow : ChromedWindow {
    // Conventional xterm/VS Code terminal palette. These are deliberately fixed (not theme
    // resources) so command output keeps the same colors a user would see in a real terminal,
    // regardless of the app's light/dark theme.
    private static readonly Color[] AnsiPalette = {
        Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0xCD, 0x31, 0x31), Color.FromRgb(0x0D, 0xBC, 0x79), Color.FromRgb(0xE5, 0xE5, 0x10),
        Color.FromRgb(0x24, 0x72, 0xC8), Color.FromRgb(0xBC, 0x3F, 0xBC), Color.FromRgb(0x11, 0xA8, 0xCD), Color.FromRgb(0xE5, 0xE5, 0xE5),
        Color.FromRgb(0x66, 0x66, 0x66), Color.FromRgb(0xF1, 0x4C, 0x4C), Color.FromRgb(0x23, 0xD1, 0x8B), Color.FromRgb(0xF5, 0xF5, 0x43),
        Color.FromRgb(0x3B, 0x8E, 0xEA), Color.FromRgb(0xD6, 0x70, 0xD6), Color.FromRgb(0x29, 0xB8, 0xDB), Color.FromRgb(0xFF, 0xFF, 0xFF)
    };

    private readonly ThrottledScrollRichTextBox _outputBox;
    private readonly ProgressBar _busyIndicator;
    private readonly TextBlock _busyText;
    private readonly DispatcherTimer _elapsedTimer;
    private readonly Stopwatch _elapsed = new();
    private string _busyMessage = string.Empty;
    private bool _hasLiveOutput;
    private bool _isClosed;
    private Paragraph? _liveParagraph;

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

        _outputBox = new ThrottledScrollRichTextBox {
            IsReadOnly = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
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
        var text = string.IsNullOrWhiteSpace(output) ? "(no output)" : output.TrimEnd();
        _outputBox.Document = BuildDocument(text);
        _liveParagraph = null;
        _outputBox.ScrollToHome();
    }

    public void AppendOutputLine(string line) {
        // Queued progress must not overwrite the completed summary or resurrect a closed window.
        if (!_elapsed.IsRunning || _isClosed) return;
        if (!_hasLiveOutput || _liveParagraph is null) {
            _liveParagraph = new Paragraph { Margin = new Thickness(0) };
            var document = new FlowDocument { PagePadding = new Thickness(0) };
            document.Blocks.Add(_liveParagraph);
            _outputBox.Document = document;
            _hasLiveOutput = true;
        }
        AppendAnsiRuns(_liveParagraph.Inlines, line);
        _liveParagraph.Inlines.Add(new LineBreak());
        _outputBox.ScrollToEnd();
    }

    /// <summary>
    /// Parses <paramref name="text"/> for ANSI/VT100 SGR escape sequences and builds a
    /// <see cref="FlowDocument"/> containing a single wrapped paragraph of styled runs, so colored
    /// or bold error/warning output (e.g. from PowerShell) renders instead of showing raw escape bytes.
    /// </summary>
    private static FlowDocument BuildDocument(string text) {
        var document = new FlowDocument { PagePadding = new Thickness(0) };
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        AppendAnsiRuns(paragraph.Inlines, text);
        document.Blocks.Add(paragraph);
        return document;
    }

    private static void AppendAnsiRuns(InlineCollection inlines, string text) {
        foreach (var span in AnsiTextParser.Parse(text)) {
            var lines = span.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++) {
                var lineText = lines[i].TrimEnd('\r');
                if (lineText.Length > 0) inlines.Add(CreateRun(lineText, span.Style));
                if (i < lines.Length - 1) inlines.Add(new LineBreak());
            }
        }
    }

    private static Run CreateRun(string text, AnsiStyle style) {
        var run = new Run(text);
        if (style.ForegroundRgb is { } foregroundRgb)
            run.Foreground = new SolidColorBrush(Color.FromRgb(foregroundRgb.R, foregroundRgb.G, foregroundRgb.B));
        else if (style.Foreground is { } foreground)
            run.Foreground = new SolidColorBrush(AnsiPalette[(int)foreground]);
        // Otherwise leave Foreground unset so it inherits the box's themed default color.

        if (style.BackgroundRgb is { } backgroundRgb)
            run.Background = new SolidColorBrush(Color.FromRgb(backgroundRgb.R, backgroundRgb.G, backgroundRgb.B));
        else if (style.Background is { } background)
            run.Background = new SolidColorBrush(AnsiPalette[(int)background]);

        if (style.Bold) run.FontWeight = FontWeights.Bold;
        if (style.Underline) run.TextDecorations = TextDecorations.Underline;
        return run;
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
