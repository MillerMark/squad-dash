using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SquadDash;

/// <summary>
/// A floating, chrome-less navigation overlay that sits near the active tour callout.
/// Fires <see cref="PrevClicked"/> and <see cref="NextClicked"/> so the controller can advance the tour.
/// </summary>
internal sealed class TourCalloutNavigationOverlay : Window {
    public event EventHandler? PrevClicked;
    public event EventHandler? NextClicked;
    public event EventHandler? EditClicked;
    public event EventHandler? NewStepAfterClicked;
    public event EventHandler? NewStepBeforeClicked;
    public event EventHandler? DeleteClicked;

    private TextBlock? _nextLabel;

    private const double PrevButtonWidth = 32;
    private const double NextButtonWidth = 58;
    private const double ButtonHeight = 36;
    private const double ButtonGap = 6;

    private Border? _prevButton;
    private Border? _nextButton;
    private bool _glowActive;
    private Func<int>? _getNextAdvanceCount;
    private Action? _recordNextAdvance;

    private bool _isFirstStep;

    public bool IsFirstStep
    {
        get => _isFirstStep;
        set
        {
            _isFirstStep = value;
            if (_prevButton is not null)
                _prevButton.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            UpdateNextLabelVisibility();
        }
    }

    private bool _isDevModeVisible;
    public bool IsDevModeVisible {
        get => _isDevModeVisible;
        set => _isDevModeVisible = value;
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

    public TourCalloutNavigationOverlay() {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = 0;
        Title = string.Empty;

        BuildContent();

        // When the user clicks Next, record the advance globally and hide the label
        // once they've clicked enough times to be considered familiar with the control.
        NextClicked += (_, _) => {
            _recordNextAdvance?.Invoke();
            UpdateNextLabelVisibility();
        };
    }

    public void ConfigureNextLabelState(Func<int>? getNextAdvanceCount, Action? recordNextAdvance) {
        _getNextAdvanceCount = getNextAdvanceCount;
        _recordNextAdvance = recordNextAdvance;
        UpdateNextLabelVisibility();
    }

    private void BuildContent() {
        var panel = new StackPanel {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6),
        };

        _prevButton = BuildButton(isPrev: true);
        _nextButton = BuildButton(isPrev: false);

        panel.Children.Add(_prevButton);
        panel.Children.Add(new FrameworkElement { Width = ButtonGap });
        panel.Children.Add(_nextButton);

        var container = new Border {
            CornerRadius        = new CornerRadius(8),
            Padding             = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment   = VerticalAlignment.Top,
            Background          = new System.Windows.Media.SolidColorBrush(
                                      System.Windows.Media.Color.FromArgb(128, 0, 0, 0)),
            Child               = panel,
        };

        Content = container;
    }

    // Width of the panel margin on each side — used by PositionNear to flush the
    // visible button edge to the callout boundary instead of the window edge.
    private const double PanelMargin = 4;

    private Border BuildButton(bool isPrev) {
        var border = new Border {
            Width = isPrev ? PrevButtonWidth : NextButtonWidth,
            Height = ButtonHeight,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = true,
            Cursor = Cursors.Hand,
            ToolTip = isPrev
                ? "Click or press Backspace to go to the previous step."
                : "Click or press Enter to go to the next step.",
        };
        border.SetResourceReference(Border.BackgroundProperty, "CalloutButtonBackground");
        border.SetResourceReference(Border.BorderBrushProperty, "CalloutBorder");

        border.MouseEnter += (_, _) => border.SetResourceReference(Border.BackgroundProperty, "CalloutButtonHover");
        border.MouseLeave += (_, _) => border.SetResourceReference(Border.BackgroundProperty, "CalloutButtonBackground");
        border.MouseLeftButtonUp += (_, e) => {
            e.Handled = true;
            if (isPrev) PrevClicked?.Invoke(this, EventArgs.Empty);
            else NextClicked?.Invoke(this, EventArgs.Empty);
        };

        var inner = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Margin = isPrev ? new Thickness(0) : new Thickness(3, 0, 3, 0),
        };

        inner.Children.Add(BuildArrowIcon(flipHorizontal: isPrev));

        if (!isPrev) {
            var label = new TextBlock {
                Text = "Next",
                FontSize = 12,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "CalloutText");
            _nextLabel = label;
            UpdateNextLabelVisibility();
            inner.Children.Add(label);
        }

        border.Child = inner;
        return border;
    }

    private void UpdateNextLabelVisibility() {
        if (_nextLabel is null || _nextButton is null) return;

        bool showLabel = _isFirstStep || (_getNextAdvanceCount?.Invoke() ?? 0) < 3;
        _nextLabel.Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed;
        _nextButton.Width = showLabel ? NextButtonWidth : PrevButtonWidth;
    }

    /// <summary>Fades a blue glow in on the Next button and holds it until <see cref="StopNextButtonGlow"/> is called or Next is clicked.</summary>
    public void StartNextButtonGlow()
    {
        if (_nextButton is null || _glowActive) return;
        _glowActive = true;

        Color glowColor;
        try   { glowColor = ((SolidColorBrush)Application.Current.FindResource("WindowBorderGlow")).Color; }
        catch { glowColor = Color.FromRgb(0x18, 0xb1, 0xfc); }

        var ease     = new CubicEase { EasingMode = EasingMode.EaseIn };
        var duration = new Duration(TimeSpan.FromSeconds(0.7));

        // ── Drop-shadow fade IN, hold at target ─────────────────────────────
        var glowEffect = new DropShadowEffect {
            Color           = glowColor,
            ShadowDepth     = 0,
            BlurRadius      = 0,
            Opacity         = 1.0,
            RenderingBias   = RenderingBias.Performance,
        };
        _nextButton.Effect = glowEffect;
        glowEffect.BeginAnimation(DropShadowEffect.BlurRadiusProperty,
            new DoubleAnimation(0, 18, duration) { EasingFunction = ease });

        // ── Border thickness fade IN, hold ───────────────────────────────────
        _nextButton.BeginAnimation(Border.BorderThicknessProperty,
            new ThicknessAnimation(new Thickness(1), new Thickness(3), duration)
                { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });

        // ── Border color fade IN to glow color ───────────────────────────────
        Color restColor;
        try   { restColor = ((SolidColorBrush)Application.Current.FindResource("CalloutBorder")).Color; }
        catch { restColor = Colors.Gray; }
        var animBrush = new SolidColorBrush(restColor);
        _nextButton.BorderBrush = animBrush;
        animBrush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(restColor, glowColor, duration)
                { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });

        // Auto-cancel when Next is clicked
        EventHandler? stopOnClick = null;
        stopOnClick = (_, _) => {
            NextClicked -= stopOnClick;
            StopNextButtonGlow();
        };
        NextClicked += stopOnClick;
    }

    /// <summary>Immediately removes the Next-button glow and restores normal styling.</summary>
    public void StopNextButtonGlow()
    {
        if (_nextButton is null || !_glowActive) return;
        _glowActive = false;

        _nextButton.Effect = null;
        _nextButton.BeginAnimation(Border.BorderThicknessProperty, null);
        _nextButton.BorderThickness = new Thickness(1);
        _nextButton.SetResourceReference(Border.BorderBrushProperty, "CalloutBorder");
    }

    private static UIElement BuildArrowIcon(bool flipHorizontal) {
        var geometry = Geometry.Parse(NavRightPath);
        var path = new System.Windows.Shapes.Path {
            Data = geometry,
            Stretch = Stretch.Uniform,
            Width = 12,
            Height = 14,
            IsHitTestVisible = false,
        };
        path.SetResourceReference(System.Windows.Shapes.Path.FillProperty, "CalloutText");

        if (flipHorizontal) {
            path.RenderTransformOrigin = new Point(0.5, 0.5);
            path.RenderTransform = new ScaleTransform(-1, 1);
        }

        return path;
    }

    /// <summary>
    /// Shows the window off-screen (opacity 0) and forces a layout pass so that
    /// <see cref="ActualWidth"/> and <see cref="ActualHeight"/> are accurate before
    /// <see cref="PositionNear"/> is called.  Safe to call multiple times.
    /// </summary>
    public void EnsureLayout() {
        if (!IsVisible) {
            Show();
            UpdateLayout();
            if (ActualWidth <= 0) {
                // A second pass is sometimes needed for SizeToContent windows to fully measure.
                InvalidateMeasure();
                UpdateLayout();
            }
        }
    }

    /// <summary>
    /// Positions the overlay near the callout's screen rectangle, choosing the first
    /// candidate that fits entirely on-screen. Falls back to screen-clamping.
    /// Rule: buttons always go on the OPPOSITE side from the pointer (dangle).
    /// </summary>
    public void PositionNear(Rect calloutScreenRect, CalloutSide dangleSide = CalloutSide.Bottom) {
        Rect visibleBounds = GetVisibleButtonBounds();
        var screenBounds = GetMonitorBoundsForLogicalPoint(calloutScreenRect.TopLeft);

        SquadDashTrace.Write(TraceCategory.Callouts,
            $"[NavOverlay] PositionNear: dangleSide={dangleSide} " +
            $"callout=({calloutScreenRect.Left:F0},{calloutScreenRect.Top:F0},{calloutScreenRect.Right:F0},{calloutScreenRect.Bottom:F0}) " +
            $"screen=({screenBounds.Left:F0},{screenBounds.Top:F0},{screenBounds.Right:F0},{screenBounds.Bottom:F0})");

        var position = ComputePosition(calloutScreenRect, dangleSide, visibleBounds, screenBounds);
        Left = position.X;
        Top  = position.Y;

        SquadDashTrace.Write(TraceCategory.Callouts,
            $"[NavOverlay] Positioned: Left={Left:F0} Top={Top:F0}");
    }

    /// <summary>
    /// Pure placement computation — the testable core of <see cref="PositionNear"/>.
    /// Builds a candidate list (ordered: best side first) and returns the window (Left, Top)
    /// origin for the first candidate whose visible button rect fits entirely within
    /// <paramref name="screenBounds"/>, clamped to the screen as a final safety net.
    /// </summary>
    /// <param name="calloutScreenRect">Callout body rect in logical screen coordinates.</param>
    /// <param name="dangleSide">The side of the callout from which the pointer exits (toward the target).</param>
    /// <param name="visibleBounds">Bounding rect of the button faces relative to the window origin.</param>
    /// <param name="screenBounds">Available screen area in logical coordinates.</param>
    internal static Point ComputePosition(
        Rect calloutScreenRect,
        CalloutSide dangleSide,
        Rect visibleBounds,
        Rect screenBounds)
    {
        const double gap = 10;

        // Align the measured button faces, not the transparent top-level window bounds.
        // Some layered WPF windows can report extra non-visible width; using that width
        // here puts the Next button short of the callout edge by exactly that phantom space.
        double rightAlignX  = calloutScreenRect.Right  - visibleBounds.Right;
        double leftAlignX   = calloutScreenRect.Left   - visibleBounds.Left;
        double aboveY       = calloutScreenRect.Top    - gap - visibleBounds.Bottom;
        double belowY       = calloutScreenRect.Bottom + gap - visibleBounds.Top;
        double topAlignY    = calloutScreenRect.Top    - visibleBounds.Top;
        double bottomAlignY = calloutScreenRect.Bottom - visibleBounds.Bottom;
        double rightSideX   = calloutScreenRect.Right  + gap - visibleBounds.Left;
        double leftSideX    = calloutScreenRect.Left   - gap - visibleBounds.Right;

        // Build candidate list: ONLY the opposite side from the dangle pointer.
        // No same-side fallbacks — if nothing fits, clamping (below) keeps it on screen.
        Point[] candidates = dangleSide switch {
            // Pointer exits bottom → buttons go ABOVE first (right-aligned), right side as fallback
            CalloutSide.Bottom => new[] {
                new Point(rightAlignX, aboveY),        // above, right-aligned  ← primary
                new Point(leftAlignX,  aboveY),        // above, left-aligned
                new Point(rightSideX,  topAlignY),     // right side, top-aligned
                new Point(leftSideX,   topAlignY),     // left side, top-aligned
            },
            // Pointer exits top → buttons go below first (right-aligned), then right side
            CalloutSide.Top => new[] {
                new Point(rightAlignX, belowY),        // below, right-aligned  ← primary
                new Point(leftAlignX,  belowY),        // below, left-aligned
                new Point(rightSideX,  bottomAlignY),  // right side, bottom-aligned
                new Point(rightSideX,  topAlignY),     // right side, top-aligned
            },
            // Pointer exits right → buttons go LEFT only
            CalloutSide.Right => new[] {
                new Point(leftSideX,   bottomAlignY),  // left side, bottom-aligned
                new Point(leftSideX,   topAlignY),     // left side, top-aligned
                new Point(leftAlignX,  belowY),        // below, left-aligned fallback
                new Point(leftAlignX,  aboveY),        // above, left-aligned fallback
            },
            // Pointer exits left → buttons go below first, then right side, then above right
            _ => new[] {
                new Point(rightAlignX, belowY),        // below, right-aligned  ← primary
                new Point(leftAlignX,  belowY),        // below, left-aligned
                new Point(rightSideX,  bottomAlignY),  // right side, bottom-aligned
                new Point(rightSideX,  topAlignY),     // right side, top-aligned
                new Point(rightAlignX, aboveY),        // above, right-aligned (last resort)
            },
        };

        var chosen = candidates[candidates.Length - 1]; // fallback: last candidate
        foreach (var c in candidates) {
            bool fits = screenBounds.Contains(GetVisibleScreenRect(c, visibleBounds));
            SquadDashTrace.Write(TraceCategory.Callouts,
                $"[NavOverlay]   candidate ({c.X:F0},{c.Y:F0}) fits={fits}");
            if (fits) {
                chosen = c;
                break;
            }
        }

        return new Point(
            ClampOriginToKeepVisibleBoundsOnScreen(
                chosen.X, visibleBounds.Left, visibleBounds.Right, screenBounds.Left, screenBounds.Right),
            ClampOriginToKeepVisibleBoundsOnScreen(
                chosen.Y, visibleBounds.Top, visibleBounds.Bottom, screenBounds.Top, screenBounds.Bottom));
    }

    private Rect GetVisibleButtonBounds() {
        Rect? bounds = null;
        AddButtonBounds(_prevButton, ref bounds);
        AddButtonBounds(_nextButton, ref bounds);

        return bounds ?? new Rect(
            PanelMargin,
            PanelMargin,
            PrevButtonWidth + ButtonGap + NextButtonWidth,
            ButtonHeight);
    }

    private void AddButtonBounds(FrameworkElement? button, ref Rect? bounds) {
        if (button is null || button.ActualWidth <= 0 || button.ActualHeight <= 0)
            return;

        try {
            Point topLeft = button.TranslatePoint(new Point(0, 0), this);
            Point bottomRight = button.TranslatePoint(new Point(button.ActualWidth, button.ActualHeight), this);
            Rect buttonBounds = new Rect(topLeft, bottomRight);
            if (bounds is { } existing) {
                existing.Union(buttonBounds);
                bounds = existing;
            }
            else {
                bounds = buttonBounds;
            }
        }
        catch (InvalidOperationException) {
            // The fallback constants match BuildContent/BuildButton and cover pre-layout calls.
        }
    }

    private static Rect GetVisibleScreenRect(Point windowOrigin, Rect visibleBounds) =>
        new Rect(
            windowOrigin.X + visibleBounds.Left,
            windowOrigin.Y + visibleBounds.Top,
            visibleBounds.Width,
            visibleBounds.Height);

    private Rect GetMonitorBoundsForLogicalPoint(Point logicalPoint) {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is { } ct) {
            Point physicalPoint = ct.TransformToDevice.Transform(logicalPoint);
            Rect physicalBounds = NativeMethods.GetMonitorBoundsForPhysicalPoint(
                (int)physicalPoint.X,
                (int)physicalPoint.Y);

            if (!physicalBounds.IsEmpty) {
                Point topLeft = ct.TransformFromDevice.Transform(
                    new Point(physicalBounds.Left, physicalBounds.Top));
                Point bottomRight = ct.TransformFromDevice.Transform(
                    new Point(physicalBounds.Right, physicalBounds.Bottom));
                return new Rect(topLeft, bottomRight);
            }
        }

        // PresentationSource unavailable (overlay not yet shown/attached to visual tree).
        // Return a permissive rect so ALL candidates pass the Contains check and the first
        // (correct-side) candidate wins. Clamping in PositionNear keeps the final position on screen.
        return new Rect(-10000, -10000, 30000, 30000);
    }

    private static double ClampOriginToKeepVisibleBoundsOnScreen(
        double origin,
        double visibleStart,
        double visibleEnd,
        double screenStart,
        double screenEnd) {
        double min = screenStart - visibleStart;
        double max = screenEnd - visibleEnd;
        if (max < min)
            return min;

        return Math.Max(min, Math.Min(origin, max));
    }

    /// <summary>Shows the overlay and fades it in over 250 ms.</summary>
    public void FadeIn() {
        // EnsureLayout() should already have been called before PositionNear().
        // Show() here is a safe no-op if the window is already visible.
        Show();
        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }

    /// <summary>Instantly hides the overlay without animation.</summary>
    public void HideImmediate() {
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Visibility = Visibility.Hidden;
    }
}
