using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SquadDash.GuidedTours;

namespace SquadDash;

/// <summary>
/// A floating, chrome-less navigation overlay that sits near the active tour callout.
/// Fires <see cref="PrevClicked"/> and <see cref="NextClicked"/> so the controller can advance the tour.
/// </summary>
internal sealed class TourCalloutNavigationOverlay : Window
{
    public event EventHandler? PrevClicked;
    public event EventHandler? NextClicked;
    public event EventHandler? EditClicked;
    public event EventHandler? NewStepAfterClicked;
    public event EventHandler? NewStepBeforeClicked;

    private TextBlock? _nextLabel;

    private const double PrevButtonWidth = 32;
    private const double NextButtonWidth = 58;
    private const double ButtonHeight = 36;
    private const double ButtonGap = 6;

    private Border? _prevButton;
    private Border? _nextButton;
    private Border? _editButton;

    private bool _isDevModeVisible;
    public bool IsDevModeVisible
    {
        get => _isDevModeVisible;
        set
        {
            _isDevModeVisible = value;
            if (_editButton is not null)
                _editButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // NavRight arrow path — fits a 822×882 viewbox (right-pointing chevron/arrow).
    private const string NavRightPath =
        "M50,88.0625L50.125,86.625C51.375,71.875,56.5,61.8125,68.0625,55.3125" +
        "C77.9375,49.6875,89.0625,49.9375,100.4375,54.0625L104.875,55.9375 " +
        "773,441.9375 119.25,817.8125 111.8125,822.0625" +
        "C95.625,831.0625,82.8125,832.875,69.75,825.1875" +
        "C56.6875,817.5,51.5,802.6875,51.5,785.3125L51.4375,783.75 50,88.0625z";

    // Button appearance uses theme resources (InputSurface / HoverSurface / LabelText)
    // so it adapts to light/dark mode automatically.

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

        // When the user clicks Next, record the advance globally and hide the label
        // once they've clicked enough times to be considered familiar with the control.
        NextClicked += (_, _) =>
        {
            GuidedTourStateStore.Shared.RecordTourNavAdvance();
            if (_nextLabel is not null
                && GuidedTourStateStore.Shared.TourNavAdvanceCount >= 3)
            {
                _nextLabel.Visibility = Visibility.Collapsed;
            }
        };
    }

    private void BuildContent()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(4),
        };

        _editButton = BuildEditButton();
        _prevButton = BuildButton(isPrev: true);
        _nextButton = BuildButton(isPrev: false);

        panel.Children.Add(_editButton);
        panel.Children.Add(new FrameworkElement { Width = ButtonGap });
        panel.Children.Add(_prevButton);
        panel.Children.Add(new FrameworkElement { Width = ButtonGap });
        panel.Children.Add(_nextButton);

        Content = panel;
    }

    // Width of the panel margin on each side — used by PositionNear to flush the
    // visible button edge to the callout boundary instead of the window edge.
    private const double PanelMargin = 4;

    private Border BuildButton(bool isPrev)
    {
        var border = new Border
        {
            Width            = isPrev ? PrevButtonWidth : NextButtonWidth,
            Height           = ButtonHeight,
            CornerRadius     = new CornerRadius(4),
            BorderThickness  = new Thickness(1),
            IsHitTestVisible = true,
            Cursor           = Cursors.Hand,
            ToolTip          = isPrev
                ? "Click or press Backspace to go to the previous step."
                : "Click or press Enter to go to the next step.",
        };
        border.SetResourceReference(Border.BackgroundProperty,   "InputSurface");
        border.SetResourceReference(Border.BorderBrushProperty,  "CalloutBorder");

        border.MouseEnter        += (_, _) => border.SetResourceReference(Border.BackgroundProperty, "HoverSurface");
        border.MouseLeave        += (_, _) => border.SetResourceReference(Border.BackgroundProperty, "InputSurface");
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
            var label = new TextBlock
            {
                Text              = "Next",
                FontSize          = 12,
                Margin            = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible  = false,
                Visibility        = GuidedTourStateStore.Shared.TourNavAdvanceCount >= 3
                                        ? Visibility.Collapsed : Visibility.Visible,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            _nextLabel = label;
            inner.Children.Add(label);
        }

        border.Child = inner;
        return border;
    }

    private Border BuildEditButton()
    {
        var border = new Border
        {
            Width            = PrevButtonWidth,
            Height           = ButtonHeight,
            CornerRadius     = new CornerRadius(4),
            BorderThickness  = new Thickness(1),
            IsHitTestVisible = true,
            Cursor           = Cursors.Hand,
            Visibility       = Visibility.Collapsed,
            ToolTip          = "Click to edit step.\nAlt+Click to add a new step after this one.\nCtrl+Click to add a new step before this step.",
        };
        border.SetResourceReference(Border.BackgroundProperty,  "InputSurface");
        border.SetResourceReference(Border.BorderBrushProperty, "CalloutBorder");

        border.MouseEnter        += (_, _) => border.SetResourceReference(Border.BackgroundProperty, "HoverSurface");
        border.MouseLeave        += (_, _) => border.SetResourceReference(Border.BackgroundProperty, "InputSurface");
        border.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                NewStepAfterClicked?.Invoke(this, EventArgs.Empty);
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                NewStepBeforeClicked?.Invoke(this, EventArgs.Empty);
            else
                EditClicked?.Invoke(this, EventArgs.Empty);
        };

        var pencil = new TextBlock
        {
            Text                = "✎",
            FontSize            = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            IsHitTestVisible    = false,
        };
        pencil.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        border.Child = pencil;
        return border;
    }

    private static UIElement BuildArrowIcon(bool flipHorizontal)
    {
        var geometry = Geometry.Parse(NavRightPath);
        var path = new System.Windows.Shapes.Path
        {
            Data             = geometry,
            Stretch          = Stretch.Uniform,
            Width            = 12,
            Height           = 14,
            IsHitTestVisible = false,
        };
        path.SetResourceReference(System.Windows.Shapes.Path.FillProperty, "LabelText");

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
            if (ActualWidth <= 0)
            {
                // A second pass is sometimes needed for SizeToContent windows to fully measure.
                InvalidateMeasure();
                UpdateLayout();
            }
        }
    }

    /// <summary>
    /// Positions the overlay near the callout's screen rectangle, choosing the first
    /// candidate that fits entirely on-screen. Falls back to screen-clamping.
    /// Priority: opposite side, edge-aligned → same side → other edges.
    /// </summary>
    public void PositionNear(Rect calloutScreenRect, CalloutSide dangleSide = CalloutSide.Bottom)
    {
        Rect visibleBounds = GetVisibleButtonBounds();

        const double gap = 6;

        // Align the measured button faces, not the transparent top-level window bounds.
        // Some layered WPF windows can report extra non-visible width; using that width
        // here puts the Next button short of the callout edge by exactly that phantom space.
        double rightAlignX = calloutScreenRect.Right - visibleBounds.Right;
        double leftAlignX  = calloutScreenRect.Left  - visibleBounds.Left;
        double aboveY      = calloutScreenRect.Top    - gap - visibleBounds.Bottom;
        double belowY      = calloutScreenRect.Bottom + gap - visibleBounds.Top;
        double topAlignY   = calloutScreenRect.Top    - visibleBounds.Top;
        double bottomAlignY = calloutScreenRect.Bottom - visibleBounds.Bottom;
        double rightSideX  = calloutScreenRect.Right + gap - visibleBounds.Left;
        double leftSideX   = calloutScreenRect.Left  - gap - visibleBounds.Right;

        var screenBounds = GetMonitorBoundsForLogicalPoint(calloutScreenRect.TopLeft);

        // Build candidate list: opposite side of dangle first, then fallbacks.
        // For top/bottom dangle: buttons go on the opposite horizontal edge, right- then left-aligned.
        // For left/right dangle: buttons go on the opposite vertical side, bottom- then top-aligned.
        Point[] candidates = dangleSide switch {
            // Pointer exits bottom → buttons go above, right- then left-aligned
            CalloutSide.Bottom => new[] {
                new Point(rightAlignX,                                   aboveY),        // right-aligned, above
                new Point(leftAlignX,                                    aboveY),        // left-aligned, above
                new Point(rightAlignX,                                   belowY),        // right-aligned, below
                new Point(leftAlignX,                                    belowY),        // left-aligned, below
                new Point(rightSideX,                                    bottomAlignY),  // right side, bottom-aligned
                new Point(leftSideX,                                     bottomAlignY),  // left side, bottom-aligned
            },
            // Pointer exits top → buttons go below, right- then left-aligned
            CalloutSide.Top => new[] {
                new Point(rightAlignX,                                   belowY),        // right-aligned, below
                new Point(leftAlignX,                                    belowY),        // left-aligned, below
                new Point(rightAlignX,                                   aboveY),        // right-aligned, above
                new Point(leftAlignX,                                    aboveY),        // left-aligned, above
                new Point(rightSideX,                                    bottomAlignY),  // right side, bottom-aligned
                new Point(leftSideX,                                     bottomAlignY),  // left side, bottom-aligned
            },
            // Pointer exits right → buttons go to the left, bottom- then top-aligned
            CalloutSide.Right => new[] {
                new Point(leftSideX,                                     bottomAlignY),  // left side, bottom-aligned
                new Point(leftSideX,                                     topAlignY),     // left side, top-aligned
                new Point(rightSideX,                                    bottomAlignY),  // right side, bottom-aligned
                new Point(rightSideX,                                    topAlignY),     // right side, top-aligned
                new Point(rightAlignX,                                   belowY),        // right-aligned, below
                new Point(leftAlignX,                                    belowY),        // left-aligned, below
            },
            // Pointer exits left → buttons go to the right, bottom- then top-aligned
            _ => new[] {
                new Point(rightSideX,                                    bottomAlignY),  // right side, bottom-aligned
                new Point(rightSideX,                                    topAlignY),     // right side, top-aligned
                new Point(leftSideX,                                     bottomAlignY),  // left side, bottom-aligned
                new Point(leftSideX,                                     topAlignY),     // left side, top-aligned
                new Point(rightAlignX,                                   belowY),        // right-aligned, below
                new Point(leftAlignX,                                    belowY),        // left-aligned, below
            },
        };

        var chosen = candidates[candidates.Length - 1]; // fallback: last candidate
        foreach (var c in candidates)
        {
            if (screenBounds.Contains(GetVisibleScreenRect(c, visibleBounds)))
            {
                chosen = c;
                break;
            }
        }

        Left = ClampOriginToKeepVisibleBoundsOnScreen(
            chosen.X, visibleBounds.Left, visibleBounds.Right, screenBounds.Left, screenBounds.Right);
        Top = ClampOriginToKeepVisibleBoundsOnScreen(
            chosen.Y, visibleBounds.Top, visibleBounds.Bottom, screenBounds.Top, screenBounds.Bottom);
    }

    private Rect GetVisibleButtonBounds()
    {
        Rect? bounds = null;
        AddButtonBounds(_editButton, ref bounds);
        AddButtonBounds(_prevButton, ref bounds);
        AddButtonBounds(_nextButton, ref bounds);

        return bounds ?? new Rect(
            PanelMargin,
            PanelMargin,
            PrevButtonWidth + ButtonGap + NextButtonWidth,
            ButtonHeight);
    }

    private void AddButtonBounds(FrameworkElement? button, ref Rect? bounds)
    {
        if (button is null || button.ActualWidth <= 0 || button.ActualHeight <= 0)
            return;

        try
        {
            Point topLeft = button.TranslatePoint(new Point(0, 0), this);
            Point bottomRight = button.TranslatePoint(new Point(button.ActualWidth, button.ActualHeight), this);
            Rect buttonBounds = new Rect(topLeft, bottomRight);
            if (bounds is { } existing)
            {
                existing.Union(buttonBounds);
                bounds = existing;
            }
            else
            {
                bounds = buttonBounds;
            }
        }
        catch (InvalidOperationException)
        {
            // The fallback constants match BuildContent/BuildButton and cover pre-layout calls.
        }
    }

    private static Rect GetVisibleScreenRect(Point windowOrigin, Rect visibleBounds) =>
        new Rect(
            windowOrigin.X + visibleBounds.Left,
            windowOrigin.Y + visibleBounds.Top,
            visibleBounds.Width,
            visibleBounds.Height);

    private Rect GetMonitorBoundsForLogicalPoint(Point logicalPoint)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is { } ct)
        {
            Point physicalPoint = ct.TransformToDevice.Transform(logicalPoint);
            Rect physicalBounds = NativeMethods.GetMonitorBoundsForPhysicalPoint(
                (int)physicalPoint.X,
                (int)physicalPoint.Y);

            if (!physicalBounds.IsEmpty)
            {
                Point topLeft = ct.TransformFromDevice.Transform(
                    new Point(physicalBounds.Left, physicalBounds.Top));
                Point bottomRight = ct.TransformFromDevice.Transform(
                    new Point(physicalBounds.Right, physicalBounds.Bottom));
                return new Rect(topLeft, bottomRight);
            }
        }

        return new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
    }

    private static double ClampOriginToKeepVisibleBoundsOnScreen(
        double origin,
        double visibleStart,
        double visibleEnd,
        double screenStart,
        double screenEnd)
    {
        double min = screenStart - visibleStart;
        double max = screenEnd - visibleEnd;
        if (max < min)
            return min;

        return Math.Max(min, Math.Min(origin, max));
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
