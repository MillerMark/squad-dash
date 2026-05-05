using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// A small non-interactive floating hint that appears near an insertion point
/// after a Revise with AI replacement, then fades out automatically.
/// </summary>
internal sealed class RevisionHintOverlay : Window
{
    private const double DisplayMs  = 1600;
    private const double FadeInMs   = 150;
    private const double FadeOutMs  = 400;

    internal RevisionHintOverlay(string message)
    {
        WindowStyle        = WindowStyle.None;
        AllowsTransparency = true;
        Background         = Brushes.Transparent;
        ResizeMode         = ResizeMode.NoResize;
        SizeToContent      = SizeToContent.WidthAndHeight;
        ShowInTaskbar      = false;
        Topmost            = true;
        IsHitTestVisible   = false;
        Opacity            = 0;

        var border = new Border {
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(10, 6, 10, 6),
            Background      = new SolidColorBrush(Color.FromArgb(0xE8, 0x1A, 0x7A, 0x2C)),
        };

        var tb = new TextBlock {
            Text       = message,
            FontSize   = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        };
        border.Child = tb;
        Content = border;

        Loaded += (_, _) => BeginLifecycle();
    }

    private void BeginLifecycle()
    {
        // Fade in
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(FadeInMs));
        fadeIn.Completed += (_, _) => ScheduleFadeOut();
        BeginAnimation(OpacityProperty, fadeIn);
    }

    private void ScheduleFadeOut()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DisplayMs) };
        timer.Tick += (_, _) => {
            timer.Stop();
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(FadeOutMs));
            fadeOut.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
        };
        timer.Start();
    }
}
