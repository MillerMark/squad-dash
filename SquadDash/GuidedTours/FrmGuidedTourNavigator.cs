using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SquadDash.GuidedTours;

namespace SquadDash;

/// <summary>
/// Floating navigator window that shows the current step header and
/// Prev / Next / Close Tour controls during a guided tour.
/// </summary>
internal sealed class FrmGuidedTourNavigator : ChromedWindow
{
    private const string PositionKey = "GuidedTourNavigatorPosition";

    private readonly TextBlock _headerLabel;
    private readonly Button    _prevButton;
    private readonly Button    _nextButton;

    /// <summary>Fired when the user clicks "← Prev" or presses F2.</summary>
    public event EventHandler? PrevRequested;
    /// <summary>Fired when the user clicks "Next →" or presses F3.</summary>
    public event EventHandler? NextRequested;
    /// <summary>Fired when the user clicks "✕ Close Tour".</summary>
    public event EventHandler? CloseRequested;

    public FrmGuidedTourNavigator()
        : base(captionHeight: 34, resizeMode: ResizeMode.NoResize, resizeBorderThickness: 0)
    {
        Title         = "Guided Tour";
        Width         = 300;
        SizeToContent = SizeToContent.Height;
        ShowInTaskbar = false;
        Topmost       = true;

        // ── Content area ──────────────────────────────────────────────────────
        var contentArea = ApplyOuterBorder("AppSurface", "Guided Tour");

        _headerLabel = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(12, 10, 12, 8),
            FontWeight   = FontWeights.SemiBold,
        };
        _headerLabel.SetResourceReference(TextBlock.FontSizeProperty,   "FontSizeBody");
        _headerLabel.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        _prevButton = MakeButton("← Prev");
        _prevButton.Click += (_, _) => PrevRequested?.Invoke(this, EventArgs.Empty);

        _nextButton = MakeButton("Next →");
        _nextButton.Click += (_, _) => NextRequested?.Invoke(this, EventArgs.Empty);

        var closeButton = MakeButton("✕ Close Tour");
        closeButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(8, 0, 8, 10),
        };
        buttonRow.Children.Add(_prevButton);
        buttonRow.Children.Add(_nextButton);
        buttonRow.Children.Add(closeButton);

        var stack = new StackPanel();
        stack.Children.Add(_headerLabel);
        stack.Children.Add(buttonRow);

        contentArea.Child = stack;

        // ── Position restore ─────────────────────────────────────────────────
        WindowStartupLocation = WindowStartupLocation.Manual;
        Loaded  += OnLoaded;
        Closing += OnClosing;

        // ── Keyboard shortcuts ────────────────────────────────────────────────
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Updates the header to "Step N of N: {title}".</summary>
    public void UpdateStep(int stepIndex, int totalSteps, string title)
    {
        _headerLabel.Text = $"Step {stepIndex + 1} of {totalSteps}: {title}";
        _prevButton.IsEnabled = stepIndex > 0;
        _nextButton.IsEnabled = stepIndex < totalSteps - 1;
    }

    // ── Position save / restore ──────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (FloatingWindowPositionStore.Shared.TryRestore(PositionKey, this))
        {
            // Validate the restored position is fully on-screen
            if (!IsFullyOnScreen())
                PlaceAtDefaultPosition();
        }
        else
        {
            PlaceAtDefaultPosition();
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        FloatingWindowPositionStore.Shared.Save(PositionKey, this);
    }

    private void PlaceAtDefaultPosition()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right  - Width  - 20;
        Top  = workArea.Bottom - Height - 20;
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    private bool IsFullyOnScreen()
    {
        var workArea = SystemParameters.WorkArea;
        return Left >= workArea.Left
            && Top  >= workArea.Top
            && Left + Width  <= workArea.Right
            && Top  + Height <= workArea.Bottom;
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2) { PrevRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        if (e.Key == Key.F3) { NextRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private static Button MakeButton(string content)
    {
        var btn = new Button
        {
            Content = content,
            Height  = 26,
            Margin  = new Thickness(3, 0, 3, 0),
            Padding = new Thickness(8, 2, 8, 2),
        };
        btn.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        return btn;
    }
}
