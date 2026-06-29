using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SquadDash;

/// <summary>
/// A floating, chrome-less navigation overlay that sits near the active tour callout.
/// Fires <see cref="PrevClicked"/> and <see cref="NextClicked"/> so the controller can advance the tour.
/// </summary>
internal sealed class TourCalloutNavigationOverlay : Window
{
    public event EventHandler? PrevClicked;
    public event EventHandler? NextClicked;

    // NavRight arrow path — fits a 822×882 viewbox (right-pointing chevron/arrow).
    private const string NavRightPath =
        "M50,88.0625L50.125,86.625C51.375,71.875,56.5,61.8125,68.0625,55.3125" +
        "C77.9375,49.6875,89.0625,49.9375,100.4375,54.0625L104.875,55.9375 " +
        "773,441.9375 119.25,817.8125 111.8125,822.0625" +
        "C95.625,831.0625,82.8125,832.875,69.75,825.1875" +
        "C56.6875,817.5,51.5,802.6875,51.5,785.3125L51.4375,783.75 50,88.0625z";

    private static readonly SolidColorBrush ButtonNormal =
        new SolidColorBrush(Color.FromArgb(0xCC, 0x1A, 0x2A, 0x3F));
    private static readonly SolidColorBrush ButtonHover =
        new SolidColorBrush(Color.FromArgb(0xCC, 0x28, 0x42, 0x62));

    public TourCalloutNavigationOverlay()
    {
        WindowStyle        = WindowStyle.None;
        AllowsTransparency = true;
        Background         = Brushes.Transparent;
        ShowInTaskbar      = false;
        Topmost            = true;
        ShowActivated      = false;
        ResizeMode         = ResizeMode.NoResize;
        SizeToContent      = SizeToContent.WidthAndHeight;
        Opacity            = 0;
        Title              = string.Empty;

        BuildContent();
    }

    private void BuildContent()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(4),
        };

        panel.Children.Add(BuildButton(isPrev: true));
        panel.Children.Add(new FrameworkElement { Width = 6 });
        panel.Children.Add(BuildButton(isPrev: false));

        Content = panel;
    }

    // Width of the panel margin on each side — used by PositionNear to flush the
    // visible button edge to the callout boundary instead of the window edge.
    private const double PanelMargin = 4;

    private UIElement BuildButton(bool isPrev)
    {
        var border = new Border
        {
            Width            = isPrev ? 32 : 58,
            Height           = 36,
            CornerRadius     = new CornerRadius(4),
            Background       = ButtonNormal,
            IsHitTestVisible = true,
            Cursor           = Cursors.Hand,
        };

        border.MouseEnter        += (_, _) => border.Background = ButtonHover;
        border.MouseLeave        += (_, _) => border.Background = ButtonNormal;
        border.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (isPrev) PrevClicked?.Invoke(this, EventArgs.Empty);
            else        NextClicked?.Invoke(this, EventArgs.Empty);
        };

        var inner = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            IsHitTestVisible    = false,
            Margin              = isPrev ? new Thickness(0) : new Thickness(3, 0, 3, 0),
        };

        inner.Children.Add(BuildArrowIcon(flipHorizontal: isPrev));

        if (!isPrev)
        {
            inner.Children.Add(new TextBlock
            {
                Text              = "Next",
                Foreground        = Brushes.White,
                FontSize          = 12,
                Margin            = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible  = false,
            });
        }

        border.Child = inner;
        return border;
    }

    private static UIElement BuildArrowIcon(bool flipHorizontal)
    {
        var geometry = Geometry.Parse(NavRightPath);
        var path = new System.Windows.Shapes.Path
        {
            Data             = geometry,
            Fill             = Brushes.White,
            Stretch          = Stretch.Uniform,
            Width            = 12,
            Height           = 14,
            IsHitTestVisible = false,
        };

        if (flipHorizontal)
        {
            path.RenderTransformOrigin = new Point(0.5, 0.5);
            path.RenderTransform       = new ScaleTransform(-1, 1);
        }

        return path;
    }

    /// <summary>
    /// Shows the window off-screen (opacity 0) and forces a layout pass so that
    /// <see cref="ActualWidth"/> and <see cref="ActualHeight"/> are accurate before
    /// <see cref="PositionNear"/> is called.  Safe to call multiple times.
    /// </summary>
    public void EnsureLayout()
    {
        if (!IsVisible)
        {
            Show();
            UpdateLayout();
        }
    }

    /// <summary>
    /// Positions the overlay near the callout's screen rectangle, choosing the first
    /// candidate that fits entirely on-screen. Falls back to screen-clamping.
    /// Priority: opposite side, edge-aligned → same side → other edges.
    /// </summary>
    public void PositionNear(Rect calloutScreenRect, CalloutSide dangleSide = CalloutSide.Bottom)
    {
        double w = ActualWidth  > 0 ? ActualWidth  : 120;
        double h = ActualHeight > 0 ? ActualHeight : 44;

        const double gap = 6;

        // The panel has a PanelMargin on each side, so the visible button right edge is
        // PanelMargin pixels inset from the window right edge.  Add PanelMargin to the
        // right-aligned X so the button face (not the transparent window edge) is flush
        // with the callout boundary.
        double rightAlignX = calloutScreenRect.Right - w + PanelMargin;
        double leftAlignX  = calloutScreenRect.Left  - PanelMargin;

        var screenBounds = NativeMethods.GetMonitorBoundsForPhysicalPoint(
            (int)calloutScreenRect.X, (int)calloutScreenRect.Y);

        // Build candidate list: opposite side of dangle first, then fallbacks.
        // For top/bottom dangle: buttons go on the opposite horizontal edge, right- then left-aligned.
        // For left/right dangle: buttons go on the opposite vertical side, bottom- then top-aligned.
        Point[] candidates = dangleSide switch {
            // Pointer exits bottom → buttons go above, right- then left-aligned
            CalloutSide.Bottom => new[] {
                new Point(rightAlignX,                                   calloutScreenRect.Top - h - gap),   // right-aligned, above
                new Point(leftAlignX,                                    calloutScreenRect.Top - h - gap),   // left-aligned, above
                new Point(rightAlignX,                                   calloutScreenRect.Bottom + gap),    // right-aligned, below
                new Point(leftAlignX,                                    calloutScreenRect.Bottom + gap),    // left-aligned, below
                new Point(calloutScreenRect.Right + gap,                 calloutScreenRect.Bottom - h),      // right side, bottom-aligned
                new Point(calloutScreenRect.Left - w - gap,              calloutScreenRect.Bottom - h),      // left side, bottom-aligned
            },
            // Pointer exits top → buttons go below, right- then left-aligned
            CalloutSide.Top => new[] {
                new Point(rightAlignX,                                   calloutScreenRect.Bottom + gap),    // right-aligned, below
                new Point(leftAlignX,                                    calloutScreenRect.Bottom + gap),    // left-aligned, below
                new Point(rightAlignX,                                   calloutScreenRect.Top - h - gap),   // right-aligned, above
                new Point(leftAlignX,                                    calloutScreenRect.Top - h - gap),   // left-aligned, above
                new Point(calloutScreenRect.Right + gap,                 calloutScreenRect.Bottom - h),      // right side, bottom-aligned
                new Point(calloutScreenRect.Left - w - gap,              calloutScreenRect.Bottom - h),      // left side, bottom-aligned
            },
            // Pointer exits right → buttons go to the left, bottom- then top-aligned
            CalloutSide.Right => new[] {
                new Point(calloutScreenRect.Left - w - gap,              calloutScreenRect.Bottom - h),      // left side, bottom-aligned
                new Point(calloutScreenRect.Left - w - gap,              calloutScreenRect.Top),             // left side, top-aligned
                new Point(calloutScreenRect.Right + gap,                 calloutScreenRect.Bottom - h),      // right side, bottom-aligned
                new Point(calloutScreenRect.Right + gap,                 calloutScreenRect.Top),             // right side, top-aligned
                new Point(rightAlignX,                                   calloutScreenRect.Bottom + gap),    // right-aligned, below
                new Point(leftAlignX,                                    calloutScreenRect.Bottom + gap),    // left-aligned, below
            },
            // Pointer exits left → buttons go to the right, bottom- then top-aligned
            _ => new[] {
                new Point(calloutScreenRect.Right + gap,                 calloutScreenRect.Bottom - h),      // right side, bottom-aligned
                new Point(calloutScreenRect.Right + gap,                 calloutScreenRect.Top),             // right side, top-aligned
                new Point(calloutScreenRect.Left - w - gap,              calloutScreenRect.Bottom - h),      // left side, bottom-aligned
                new Point(calloutScreenRect.Left - w - gap,              calloutScreenRect.Top),             // left side, top-aligned
                new Point(rightAlignX,                                   calloutScreenRect.Bottom + gap),    // right-aligned, below
                new Point(leftAlignX,                                    calloutScreenRect.Bottom + gap),    // left-aligned, below
            },
        };

        var chosen = candidates[candidates.Length - 1]; // fallback: last candidate
        foreach (var c in candidates)
        {
            if (screenBounds.Contains(new Rect(c.X, c.Y, w, h)))
            {
                chosen = c;
                break;
            }
        }

        Left = Math.Max(screenBounds.Left, Math.Min(chosen.X, screenBounds.Right  - w));
        Top  = Math.Max(screenBounds.Top,  Math.Min(chosen.Y, screenBounds.Bottom - h));
    }

    /// <summary>Shows the overlay and fades it in over 250 ms.</summary>
    public void FadeIn()
    {
        // EnsureLayout() should already have been called before PositionNear().
        // Show() here is a safe no-op if the window is already visible.
        Show();
        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }

    /// <summary>Instantly hides the overlay without animation.</summary>
    public void HideImmediate()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity    = 0;
        Visibility = Visibility.Hidden;
    }
}
