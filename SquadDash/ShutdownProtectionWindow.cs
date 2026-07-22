using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;

namespace SquadDash;

internal enum DeferredShutdownMode { None, AfterCurrentTurn, AfterAllQueued }
internal enum ShutdownChoice { None, CloseNow, AfterCurrentTurn, AfterAllQueued }

/// <summary>
/// Custom shutdown-protection dialog shown when the user tries to close SquadDash
/// while the coordinator is busy, a loop is running, or the prompt queue has items.
/// </summary>
internal sealed class ShutdownProtectionWindow : ChromedWindow {
    public ShutdownChoice Choice { get; private set; } = ShutdownChoice.None;

    public ShutdownProtectionWindow(bool isRunning, bool hasQueue, bool isLoopRunning)
        : base(captionHeight: 28, resizeMode: ResizeMode.NoResize) {
        Title = "Close SquadDash?";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        MinWidth = 380;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // Cascade the themed foreground to all TextBlock descendants that don't
        // set their own Foreground (Foreground is an inheritable DP in WPF).
        this.SetResourceReference(ForegroundProperty, "LabelText");

        var root = new StackPanel { Margin = new Thickness(20) };
        var outerBorder = ApplyOuterBorder();
        outerBorder.Child = root;

        // Header
        root.Children.Add(new TextBlock {
            Text = "SquadDash is busy",
            FontSize = (double)Application.Current.Resources["FontSizeSubtitle"],
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        });

        // Status lines
        if (isLoopRunning)
            AddStatus(root, "A loop is currently running.");
        else if (isRunning)
            AddStatus(root, "The Coordinator is working on a turn.");
        if (hasQueue)
            AddStatus(root, "There are queued prompts waiting to run.");

        root.Children.Add(new Border { Height = 16 });

        // Shutdown options — always shown (at minimum "Right now" is available)
        root.Children.Add(new TextBlock {
            Text = "Shutdown SquadDash:",
            FontSize = (double)Application.Current.Resources["FontSizeNormal"],
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        RadioButton? afterTurnRadio   = null;
        RadioButton? afterQueuedRadio = null;

        if (isRunning || isLoopRunning) {
            string afterTurnText = isLoopRunning
                ? "After this loop iteration completes"
                : "After this turn completes";
            afterTurnRadio = new RadioButton {
                Content   = afterTurnText,
                GroupName = "DeferredMode",
                FontSize  = (double)Application.Current.Resources["FontSizeNormal"],
                Margin    = new Thickness(4, 0, 0, 6),
                IsChecked = true,
            };
            afterTurnRadio.SetResourceReference(Control.ForegroundProperty, "LabelText");
            root.Children.Add(afterTurnRadio);
        }

        if (hasQueue) {
            afterQueuedRadio = new RadioButton {
                Content   = "After the Queue is empty",
                GroupName = "DeferredMode",
                FontSize  = (double)Application.Current.Resources["FontSizeNormal"],
                Margin    = new Thickness(4, 0, 0, 6),
                IsChecked = afterTurnRadio is null,
            };
            afterQueuedRadio.SetResourceReference(Control.ForegroundProperty, "LabelText");
            root.Children.Add(afterQueuedRadio);
        }

        var rightNowRadio = new RadioButton {
            Content   = "Right now",
            GroupName = "DeferredMode",
            FontSize  = (double)Application.Current.Resources["FontSizeNormal"],
            Margin    = new Thickness(4, 0, 0, 6),
            IsChecked = afterTurnRadio is null && afterQueuedRadio is null,
        };
        rightNowRadio.SetResourceReference(Control.ForegroundProperty, "LabelText");
        root.Children.Add(rightNowRadio);

        root.Children.Add(new Border { Height = 16 });

        // Button row: [Cancel] ... [Schedule Shutdown -or- ⚠ Close Now]
        // Schedule Shutdown is shown when a deferred option is selected;
        // ⚠ Close Now is shown when "Right now" is selected.
        var buttonRow = new Grid();
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.Children.Add(buttonRow);

        // Cancel
        var cancelBtn = new Button { Content = "Cancel", Width = 80, Height = 30 };
        cancelBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        cancelBtn.Click += (_, _) => { Choice = ShutdownChoice.None; DialogResult = false; };
        Grid.SetColumn(cancelBtn, 0);
        buttonRow.Children.Add(cancelBtn);

        // Schedule Shutdown — hidden when "Right now" is selected
        var scheduleBtn = new Button {
            Content = "Schedule Shutdown",
            Height  = 30,
            Padding = new Thickness(12, 0, 12, 0),
        };
        scheduleBtn.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        scheduleBtn.Click += (_, _) => {
            Choice = (afterQueuedRadio?.IsChecked == true)
                ? ShutdownChoice.AfterAllQueued
                : ShutdownChoice.AfterCurrentTurn;
            DialogResult = true;
        };
        Grid.SetColumn(scheduleBtn, 2);
        buttonRow.Children.Add(scheduleBtn);

        // ⚠ Close Now (danger) — shown only when "Right now" is selected
        var closeNowBtn = new Button {
            Content    = BuildCloseNowContent(),
            Height     = 30,
            Padding    = new Thickness(10, 0, 12, 0),
            Visibility = Visibility.Collapsed,
        };
        closeNowBtn.SetResourceReference(Control.StyleProperty, "DangerButtonStyle");
        closeNowBtn.Click += (_, _) => { Choice = ShutdownChoice.CloseNow; DialogResult = true; };
        Grid.SetColumn(closeNowBtn, 2);
        buttonRow.Children.Add(closeNowBtn);

        // Swap action button based on radio selection
        void UpdateActionButton() {
            bool rightNow = rightNowRadio.IsChecked == true;
            scheduleBtn.Visibility  = rightNow ? Visibility.Collapsed : Visibility.Visible;
            closeNowBtn.Visibility  = rightNow ? Visibility.Visible   : Visibility.Collapsed;
        }
        rightNowRadio.Checked                                          += (_, _) => UpdateActionButton();
        if (afterTurnRadio   is not null) afterTurnRadio.Checked       += (_, _) => UpdateActionButton();
        if (afterQueuedRadio is not null) afterQueuedRadio.Checked     += (_, _) => UpdateActionButton();
        UpdateActionButton();

        // Escape = cancel
        PreviewKeyDown += (_, e) => {
            if (e.Key == System.Windows.Input.Key.Escape) {
                Choice = ShutdownChoice.None;
                DialogResult = false;
                e.Handled = true;
            }
        };
    }

    private static void AddStatus(StackPanel root, string text) {
        root.Children.Add(new TextBlock {
            Text = "• " + text,
            FontSize = (double)Application.Current.Resources["FontSizeNormal"],
            Margin = new Thickness(0, 0, 0, 4),
        });
    }

    /// <summary>Builds the content panel for the Close Now button: red circle with white ! + label.</summary>
    private static StackPanel BuildCloseNowContent() {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        // Red circle with white exclamation mark
        var canvas = new Canvas { Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) };
        canvas.Children.Add(new Ellipse {
            Width = 16,
            Height = 16,
            Fill = Brushes.White,
        });
        canvas.Children.Add(new TextBlock {
            Text = "!",
            FontSize = (double)Application.Current.Resources["FontSizeSmall"],
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
            Width = 16,
            TextAlignment = TextAlignment.Center,
        });
        panel.Children.Add(canvas);

        panel.Children.Add(new TextBlock {
            Text = "Close Now",
            VerticalAlignment = VerticalAlignment.Center,
        });

        return panel;
    }
}
