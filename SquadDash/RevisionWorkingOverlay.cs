using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// A small non-interactive floating "Working…" overlay that appears at the
/// former position of a DocRevisePopup after it closes, then fades out
/// automatically without stealing focus.
/// </summary>
internal sealed class RevisionWorkingOverlay : Window
{
    private const double FadeInMs  = 200;
    private const double DisplayMs = 2000;
    private const double FadeOutMs = 800;

    internal RevisionWorkingOverlay()
    {
        WindowStyle        = WindowStyle.None;
        AllowsTransparency = true;
        Background         = Brushes.Transparent;
        ResizeMode         = ResizeMode.NoResize;
        SizeToContent      = SizeToContent.WidthAndHeight;
        ShowInTaskbar      = false;
        Topmost            = true;
        IsHitTestVisible   = false;
        ShowActivated      = false;
        Opacity            = 0;

        var border = new Border {
            CornerRadius    = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(12, 7, 12, 7),
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        border.SetResourceReference(Border.BorderBrushProperty, "LineColor");

        var tb = new TextBlock {
            Text       = "⟳  Working…",
            FontSize   = 12,
            FontWeight = FontWeights.SemiBold,
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        border.Child = tb;
        Content = border;

        Loaded += (_, _) => BeginLifecycle();
    }

    /// <summary>
    /// Creates and shows an overlay centred at <paramref name="center"/> (WPF logical coords).
    /// </summary>
    internal static void ShowAt(Point center, Window owner)
    {
        try
        {
            var overlay = new RevisionWorkingOverlay { Owner = owner };
            overlay.Loaded += (_, _) => {
                overlay.Left = center.X - overlay.ActualWidth  / 2;
                overlay.Top  = center.Y - overlay.ActualHeight / 2;
            };
            overlay.Show();
        }
        catch { /* overlay is cosmetic — swallow positioning errors */ }
    }

    private void BeginLifecycle()
    {
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
            fadeOut.Completed += (_, _) => { try { Close(); } catch { } };
            BeginAnimation(OpacityProperty, fadeOut);
        };
        timer.Start();
    }
}
