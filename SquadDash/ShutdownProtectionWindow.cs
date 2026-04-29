using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SquadDash;

internal enum DeferredShutdownMode { None, AfterCurrentTurn, AfterAllQueued }
internal enum ShutdownChoice { None, CloseNow, AfterCurrentTurn, AfterAllQueued }

/// <summary>
/// Custom shutdown-protection dialog shown when the user tries to close SquadDash
/// while the coordinator is busy, a loop is running, or the prompt queue has items.
/// </summary>
internal sealed class ShutdownProtectionWindow : Window {
    public ShutdownChoice Choice { get; private set; } = ShutdownChoice.None;

    public ShutdownProtectionWindow(bool isRunning, bool hasQueue, bool isLoopRunning) {
        Title                 = "Close SquadDash?";
        Width                 = 440;
        SizeToContent         = SizeToContent.Height;
        MinWidth              = 380;
        ResizeMode            = ResizeMode.NoResize;
        ShowInTaskbar         = false;
        WindowStyle           = WindowStyle.ToolWindow;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.SetResourceReference(BackgroundProperty, "AppSurface");

        var root = new StackPanel { Margin = new Thickness(20) };
        Content = root;

        // Header
        root.Children.Add(new TextBlock
        {
            Text       = "SquadDash is busy",
            FontSize   = 18,
            FontWeight = FontWeights.SemiBold,
            Margin     = new Thickness(0, 0, 0, 10),
        });

        // Status lines
        if (isLoopRunning)
            AddStatus(root, "A loop is currently running");
        else if (isRunning)
            AddStatus(root, "The coordinator is working on a turn");
        if (hasQueue)
            AddStatus(root, $"There are items waiting in the prompt queue");

        root.Children.Add(new Border { Height = 16 });

        // "Wait and close automatically" section (only when relevant)
        bool canDefer = isRunning || isLoopRunning || hasQueue;
        RadioButton? afterTurnRadio   = null;
        RadioButton? afterQueuedRadio = null;

        if (canDefer)
        {
            root.Children.Add(new TextBlock
            {
                Text       = "Or close automatically after:",
                FontSize   = 13,
                FontWeight = FontWeights.SemiBold,
                Margin     = new Thickness(0, 0, 0, 8),
            });

            if (isRunning || isLoopRunning)
            {
                string afterTurnText = isLoopRunning
                    ? "This loop iteration finishes"
                    : "This turn completes";
                afterTurnRadio = new RadioButton
                {
                    Content   = afterTurnText,
                    GroupName = "DeferredMode",
                    FontSize  = 13,
                    Margin    = new Thickness(4, 0, 0, 6),
                    IsChecked = true,
                };
                root.Children.Add(afterTurnRadio);
            }

            if (hasQueue)
            {
                afterQueuedRadio = new RadioButton
                {
                    Content   = "All queued items complete",
                    GroupName = "DeferredMode",
                    FontSize  = 13,
                    Margin    = new Thickness(4, 0, 0, 6),
                    IsChecked = afterTurnRadio is null, // default if no turn option
                };
                root.Children.Add(afterQueuedRadio);
            }

            root.Children.Add(new Border { Height = 16 });
        }

        // Button row: [Cancel] ... [Schedule Shutdown] [⚠ Close Now]
        var buttonRow = new Grid();
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });       // Cancel
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // spacer
        if (canDefer)
            buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Schedule
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // Close Now
        root.Children.Add(buttonRow);

        // Cancel
        var cancelBtn = new Button { Content = "Cancel", Width = 80, Height = 30, Margin = new Thickness(0, 0, 0, 0) };
        cancelBtn.Click += (_, _) => { Choice = ShutdownChoice.None; DialogResult = false; };
        Grid.SetColumn(cancelBtn, 0);
        buttonRow.Children.Add(cancelBtn);

        // Schedule Shutdown (only when deferred options exist)
        if (canDefer)
        {
            var scheduleBtn = new Button
            {
                Content = "Schedule Shutdown",
                Height  = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin  = new Thickness(0, 0, 8, 0),
            };
            scheduleBtn.Click += (_, _) =>
            {
                Choice = (afterQueuedRadio?.IsChecked == true)
                    ? ShutdownChoice.AfterAllQueued
                    : ShutdownChoice.AfterCurrentTurn;
                DialogResult = true;
            };
            Grid.SetColumn(scheduleBtn, 2);
            buttonRow.Children.Add(scheduleBtn);
        }

        // ⚠ Close Now (danger)
        var closeNowBtn = new Button
        {
            Content    = BuildCloseNowContent(),
            Height     = 30,
            Padding    = new Thickness(10, 0, 12, 0),
            Background = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
            Foreground = Brushes.White,
        };
        closeNowBtn.Click += (_, _) => { Choice = ShutdownChoice.CloseNow; DialogResult = true; };
        Grid.SetColumn(closeNowBtn, canDefer ? 3 : 2);
        buttonRow.Children.Add(closeNowBtn);

        // Escape = cancel
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Choice = ShutdownChoice.None;
                DialogResult = false;
                e.Handled = true;
            }
        };
    }

    private static void AddStatus(StackPanel root, string text)
    {
        root.Children.Add(new TextBlock
        {
            Text       = "• " + text,
            FontSize   = 13,
            Margin     = new Thickness(0, 0, 0, 4),
        });
    }

    /// <summary>Builds the content panel for the Close Now button: red circle with white ! + label.</summary>
    private static StackPanel BuildCloseNowContent()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        // Red circle with white exclamation mark
        var canvas = new Canvas { Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) };
        canvas.Children.Add(new Ellipse
        {
            Width  = 16,
            Height = 16,
            Fill   = Brushes.White,
        });
        canvas.Children.Add(new TextBlock
        {
            Text       = "!",
            FontSize   = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
            Width      = 16,
            TextAlignment = TextAlignment.Center,
        });
        panel.Children.Add(canvas);

        panel.Children.Add(new TextBlock
        {
            Text              = "Close Now",
            VerticalAlignment = VerticalAlignment.Center,
        });

        return panel;
    }
}
