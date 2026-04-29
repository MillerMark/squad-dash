using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Small confirmation dialog anchored above a queue tab — asks the user to confirm
/// deletion of a queued prompt item.
/// </summary>
internal sealed class QueueItemDeleteConfirmWindow : Window {
    public QueueItemDeleteConfirmWindow(string itemLabel, string previewText, Rect anchorScreenRect) {
        Title             = "Delete queued item?";
        Width             = 380;
        SizeToContent     = SizeToContent.Height;
        MinWidth          = 320;
        ResizeMode        = ResizeMode.NoResize;
        ShowInTaskbar     = false;
        WindowStyle       = WindowStyle.ToolWindow;
        this.SetResourceReference(BackgroundProperty, "AppSurface");

        if (anchorScreenRect == default)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            ContentRendered += (_, _) =>
            {
                Left = anchorScreenRect.Right - ActualWidth;
                Top  = anchorScreenRect.Top - ActualHeight - 6;
            };
        }

        var root = new StackPanel { Margin = new Thickness(18) };
        Content = root;

        root.Children.Add(new TextBlock
        {
            Text         = $"Delete queued item {itemLabel}?",
            FontSize     = 16,
            FontWeight   = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 8),
        });

        if (!string.IsNullOrWhiteSpace(previewText))
        {
            root.Children.Add(new TextBlock
            {
                Text         = previewText,
                FontSize     = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground   = SystemColors.GrayTextBrush,
                Margin       = new Thickness(0, 0, 0, 16),
            });
        }
        else
        {
            root.Children.Add(new Border { Height = 16 });
        }

        var buttons = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        root.Children.Add(buttons);

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width   = 80,
            Height  = 30,
            Margin  = new Thickness(0, 0, 8, 0),
        };
        cancelBtn.Click += (_, _) => { DialogResult = false; };
        buttons.Children.Add(cancelBtn);

        var deleteBtn = new Button
        {
            Content    = "Delete",
            Width      = 80,
            Height     = 30,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
        };
        deleteBtn.Click += (_, _) => { DialogResult = true; };
        buttons.Children.Add(deleteBtn);

        // Escape = cancel, Enter = delete.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) { DialogResult = false; e.Handled = true; }
            if (e.Key == System.Windows.Input.Key.Enter)  { DialogResult = true;  e.Handled = true; }
        };
    }
}
