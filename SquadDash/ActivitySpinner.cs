using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash;

public enum SpinnerActivityKind { Thinking, Reading, Writing }

/// <summary>
/// Physics-based activity spinner drawn directly via OnRender.
/// Subscribes to its DataContext (AgentStatusCard) for activity pulses.
/// </summary>
public sealed class ActivitySpinner : FrameworkElement
{
    // Physics state
    private double _angularVelocity;    // rad/s
    private double _angle;              // radians, current rotation
    private double _spinnerOpacity;     // 0..1
    private double _targetOpacity;      // lerp target for opacity
    private double _velocityTarget;      // what velocity we want to reach
    private double _velocityAccelPhase;  // seconds remaining in acceleration ramp (0 = at target)

    // Write-dot state
    private double _writeDotRadius;     // current rendered radius (px)
    private double _writeDotTarget;     // target radius the dot wants to reach (px)
    private double _dotGlowPhase;       // 0..2π oscillation phase
    private SpinnerActivityKind _currentKind = SpinnerActivityKind.Thinking;

    // Scale animation (0 = invisible, 1 = fully visible)
    private double _scaleTarget = 0.0;
    private double _currentScale = 0.0;

    // Forced fade-out state (triggered when a turn ends)
    private bool _forcedFadeOutActive;
    private double _forcedFadeOutDuration;
    private double _forcedFadeOutElapsed;

    // Animated layout width (drives smooth text slide)
    private double _animatedWidth = 0.0;
    private double _widthTarget = 0.0;

    // Dynamic sizing — driven by FontSize dependency property
    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(
            nameof(FontSize), typeof(double), typeof(ActivitySpinner),
            new FrameworkPropertyMetadata(
                12.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                OnFontSizeChanged));

    /// <summary>Body font size used to size the spinner. Diameter = FontSize × 1.2 × 0.85.</summary>
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    private static void OnFontSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var spinner = (ActivitySpinner)d;
        // Keep layout width target in sync when spinner is active
        if (spinner._scaleTarget > 0.0)
            spinner._widthTarget = spinner.Diameter + 4;
    }

    private double Diameter => FontSize * 1.2 * 0.85;

    // Timers
    private readonly DispatcherTimer _physicsTimer;
    private DateTime _lastTick;

    // Geometry cache (not diameter-dependent — scale is handled via transform)
    private Geometry? _cachedShapeGeo;

    // The cross/pinwheel path on a 1957×1957 canvas
    private const double OriginalCanvasSize = 1957.0;
    private const double OriginalCenter = 978.5;
    private const string ShapeFigures =
        "M978.5,-0.5C1113.6875,-0.5,1242.5,26.9375,1359.625,76.4375L1369.4375,81.1875 1367.9375,86.8125C1355.9375,118.875,1333.125,147,1285.6875,183C1202.1875,239.4375,1147.25,335,1147.25,443.375C1147.4375,445.5,1147.6875,447.625,1147.875,449.75L1147.1875,450.9375 1147.1875,794.5 1147.1875,794.5 1147.1875,809.875 1506.125,809.875 1507.3125,809.1875C1509.4375,809.375,1511.5625,809.625,1513.625,809.8125C1622.0625,809.8125,1717.625,754.875,1774.0625,671.375C1810.0625,623.9375,1838.1875,601.125,1870.1875,589.125L1875.875,587.625 1880.625,597.4375C1930.125,714.5625,1957.5,843.375,1957.5,978.5C1957.5,1113.6875,1930.125,1242.5,1880.625,1359.625L1875.875,1369.4375 1870.1875,1367.9375C1838.1875,1355.9375,1810.0625,1333.125,1774.0625,1285.6875C1717.625,1202.1875,1622.0625,1147.25,1513.625,1147.25C1511.5625,1147.4375,1509.4375,1147.6875,1507.3125,1147.875L1506.125,1147.1875 1162.5625,1147.1875 1162.5625,1147.1875 1147.1875,1147.1875 1147.1875,1506.125 1147.875,1507.3125C1147.6875,1509.4375,1147.4375,1511.5625,1147.25,1513.6875C1147.25,1622.0625,1202.1875,1717.625,1285.6875,1774.0625C1333.125,1810.0625,1355.9375,1838.1875,1367.9375,1870.1875L1369.4375,1875.875 1359.625,1880.625C1242.5,1930.125,1113.6875,1957.5,978.5,1957.5C843.375,1957.5,714.5625,1930.125,597.4375,1880.625L587.625,1875.875 589.125,1870.1875C601.125,1838.1875,623.9375,1810.0625,671.375,1774.0625C754.875,1717.625,809.8125,1622.0625,809.8125,1513.6875C809.625,1511.5625,809.375,1509.4375,809.1875,1507.3125L809.875,1506.125 809.875,1162.5625 809.875,1162.5625 809.875,1147.1875 450.9375,1147.1875 449.75,1147.875C447.625,1147.6875,445.5,1147.4375,443.375,1147.25C335,1147.25,239.4375,1202.1875,183,1285.6875C147,1333.125,118.875,1355.9375,86.875,1367.9375L81.1875,1369.4375 76.4375,1359.625C26.9375,1242.5,-0.5,1113.6875,-0.5,978.5C-0.5,843.375,26.9375,714.5625,76.4375,597.4375L81.1875,587.625 86.875,589.125C118.875,601.125,147,623.9375,183,671.375C239.4375,754.875,335,809.8125,443.375,809.8125C445.5,809.625,447.625,809.375,449.75,809.1875L450.9375,809.875 794.5,809.875 794.5,809.875 809.875,809.875 809.875,450.9375 809.1875,449.75C809.375,447.625,809.625,445.5,809.8125,443.375C809.8125,335,754.875,239.4375,671.375,183C623.9375,147,601.125,118.875,589.125,86.8125L587.625,81.1875 597.4375,76.4375C714.5625,26.9375,843.375,-0.5,978.5,-0.5z";

    // Physics constants
    private const double MaxAngularVelocity = 20.0;
    private const double FrictionK = 0.11;           // exp(-k*t) → ~6% at 25s
    private const double ThinkingImpulse = 3.5;
    private const double WritingImpulse = 6.0;

    private const double FadeOutThreshold = 0.18;    // rad/s — start fade below this
    private const double OpacityLerpRate = 2.0;      // units/sec — reaches target in ~0.5s

    private const double VelocityRampSeconds = 0.5;   // time to ramp from current to target

    // Opacity targets per state
    private const double ThinkingTargetOpacity = 0.72;
    private const double ReadingTargetOpacity  = 0.82;
    private const double WritingTargetOpacity  = 0.95;

    // Write-dot constants
    private const double MaxDotRadiusFraction = 0.425;  // 0.5 × 0.85 — dot max reduced to 85% of prior
    private const double DotGrowRate = 6.0;             // lerp speed when growing (units/sec)
    private const double DotShrinkRate = 2.0;           // lerp speed when shrinking (units/sec)
    private const double PulseIncrement = 1.5;          // px added per write pulse
    private const double DotGlowHz = 0.4165;            // glow oscillation frequency (~2× slower than before)
    private const double DotBorderFraction = 0.15;      // white border = radius * 0.15

    // Accent color — set from the agent's card; fallback to SteelBlue
    public Color AccentColor { get; set; } = Colors.SteelBlue;

    public ActivitySpinner()
    {

        _spinnerOpacity = 0;
        Visibility = Visibility.Collapsed;

        _physicsTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromSeconds(1.0 / 60.0)
        };
        _physicsTimer.Tick += OnPhysicsTick;
        _lastTick = DateTime.UtcNow;

        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;

        // Subscribe to static theme-changed notifications
        AgentStatusCard.ThemeChanged += OnThemeChanged;
    }

    // ── DataContext binding to AgentStatusCard ──────────────────────────────

    private AgentStatusCard? _subscribedCard;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedCard is not null)
        {
            _subscribedCard.ActivityPulsed -= OnActivityPulsed;
            _subscribedCard.PropertyChanged -= OnCardPropertyChanged;
            _subscribedCard.TurnEnded -= OnTurnEnded;
            _subscribedCard = null;
        }
        if (e.NewValue is AgentStatusCard card)
        {
            _subscribedCard = card;
            card.ActivityPulsed += OnActivityPulsed;
            card.PropertyChanged += OnCardPropertyChanged;
            card.TurnEnded += OnTurnEnded;
            SyncAccentColor();
        }
    }

    private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AgentStatusCard.AccentColorHex)
                           or nameof(AgentStatusCard.EffectiveAccentBrush)
                           or nameof(AgentStatusCard.EffectiveSpinnerAccentBrush))
            SyncAccentColor();
    }

    private void SyncAccentColor()
    {
        if (_subscribedCard?.EffectiveSpinnerAccentBrush is SolidColorBrush scb)
            AccentColor = scb.Color;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _physicsTimer.Stop();
        if (_subscribedCard is not null)
        {
            _subscribedCard.ActivityPulsed -= OnActivityPulsed;
            _subscribedCard.PropertyChanged -= OnCardPropertyChanged;
            _subscribedCard.TurnEnded -= OnTurnEnded;
            _subscribedCard = null;
        }
        AgentStatusCard.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        SyncAccentColor();
        if (_physicsTimer.IsEnabled)
            InvalidateVisual();
    }

    // ── Activity pulse entry point ──────────────────────────────────────────

    /// <summary>
    /// Begins a forced wind-down of the spinner over <paramref name="durationSeconds"/> seconds.
    /// No-op if the spinner is not currently running or already below the fade threshold.
    /// </summary>
    public void BeginForcedFadeOut(double durationSeconds = 3.0)
    {
        if (!_physicsTimer.IsEnabled || _angularVelocity < FadeOutThreshold)
            return;

        _forcedFadeOutActive = true;
        _forcedFadeOutDuration = durationSeconds;
        _forcedFadeOutElapsed = 0.0;
    }

    private void OnTurnEnded(object? sender, EventArgs e) => BeginForcedFadeOut(3.0);

    private void OnActivityPulsed(object? sender, SpinnerActivityKind kind)
    {
        // A new activity pulse cancels any in-progress forced fade-out
        _forcedFadeOutActive = false;

        _currentKind = kind;

        var impulse = kind == SpinnerActivityKind.Writing ? WritingImpulse : ThinkingImpulse;
        var rawTarget = _angularVelocity + impulse;
        _velocityTarget = Math.Min(Math.Max(_velocityTarget, rawTarget), MaxAngularVelocity);
        _velocityAccelPhase = VelocityRampSeconds;   // always restart ramp on any pulse

        // Snap target opacity immediately; _spinnerOpacity lerps toward it each frame
        _targetOpacity = kind switch
        {
            SpinnerActivityKind.Writing  => WritingTargetOpacity,
            SpinnerActivityKind.Reading  => ReadingTargetOpacity,
            _                            => ThinkingTargetOpacity
        };

        // Accumulate write dot on every Writing pulse
        if (kind == SpinnerActivityKind.Writing)
        {
            var maxDotRadius = Diameter / 2.0 * MaxDotRadiusFraction;
            _writeDotTarget = Math.Min(_writeDotTarget + PulseIncrement, maxDotRadius);
        }

        Visibility = Visibility.Visible;

        // Animate scale and layout width in on activation
        _scaleTarget = 1.0;
        _widthTarget = Diameter + 4;

        if (!_physicsTimer.IsEnabled)
        {
            _lastTick = DateTime.UtcNow;
            _physicsTimer.Start();
        }
    }

    // ── Physics tick (≈60 Hz) ───────────────────────────────────────────────

    private void OnPhysicsTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = Math.Clamp((now - _lastTick).TotalSeconds, 0.001, 0.2);
        _lastTick = now;

        // Smooth acceleration toward target velocity (ramp up), then apply friction
        if (_velocityAccelPhase > 0)
        {
            _velocityAccelPhase = Math.Max(0.0, _velocityAccelPhase - dt);
            var accelRate = (_velocityTarget - _angularVelocity) / VelocityRampSeconds;
            _angularVelocity = Math.Min(_angularVelocity + accelRate * dt, _velocityTarget);
        }

        // Forced fade-out: override normal coasting with accelerated deceleration
        if (_forcedFadeOutActive)
        {
            _forcedFadeOutElapsed += dt;
            var remaining = Math.Max(0.001, _forcedFadeOutDuration - _forcedFadeOutElapsed);
            _angularVelocity *= Math.Exp(-dt / (remaining / 3.0));
            _velocityTarget = 0;
            _velocityAccelPhase = 0;
            if (_forcedFadeOutElapsed >= _forcedFadeOutDuration)
            {
                _angularVelocity = 0;
                _forcedFadeOutActive = false;
            }
        }

        // Friction: exponential decay (always applied so spinner coasts to stop when idle)
        _angularVelocity *= Math.Exp(-FrictionK * dt);
        if (_angularVelocity < 1e-4) _angularVelocity = 0;
        if (_angularVelocity == 0) { _velocityTarget = 0; _velocityAccelPhase = 0; }

        // Advance rotation angle
        _angle += _angularVelocity * dt;
        if (_angle >= Math.PI * 2) _angle -= Math.PI * 2;

        // Opacity: drive target toward 0 when coasting to a stop
        if (_angularVelocity < FadeOutThreshold)
            _targetOpacity = 0.0;

        // Lerp opacity toward target at OpacityLerpRate units/sec
        var opacityDiff = _targetOpacity - _spinnerOpacity;
        var opacityStep = Math.Min(Math.Abs(opacityDiff), OpacityLerpRate * dt) * Math.Sign(opacityDiff);
        _spinnerOpacity = Math.Clamp(_spinnerOpacity + opacityStep, 0.0, 1.0);

        // Write-dot physics ──────────────────────────────────────────────────
        var maxDotRadius = Diameter / 2.0 * MaxDotRadiusFraction;

        // Decay target when writing has stopped (velocity below fade threshold or non-Writing kind)
        bool writingActive = _currentKind == SpinnerActivityKind.Writing
                             && _angularVelocity >= FadeOutThreshold;
        if (!writingActive)
            _writeDotTarget = Math.Max(0.0, _writeDotTarget - DotShrinkRate * dt);

        // Lerp radius toward target (fast grow, slow shrink)
        var dotDiff = _writeDotTarget - _writeDotRadius;
        if (Math.Abs(dotDiff) > 1e-4)
        {
            var dotRate = dotDiff > 0 ? DotGrowRate : DotShrinkRate;
            var dotStep = Math.Sign(dotDiff) * Math.Min(Math.Abs(dotDiff), dotRate * dt);
            _writeDotRadius = Math.Clamp(_writeDotRadius + dotStep, 0.0, maxDotRadius);
        }
        else
        {
            _writeDotRadius = Math.Clamp(_writeDotTarget, 0.0, maxDotRadius);
        }

        // Glow oscillation while dot is visible
        if (_writeDotRadius > 0.5)
            _dotGlowPhase += 2.0 * Math.PI * DotGlowHz * dt;

        // ── Scale animation (ease-out lerp, ~400 ms) ────────────────────────
        if (_angularVelocity < FadeOutThreshold)
            _scaleTarget = 0.0;

        _currentScale += (_scaleTarget - _currentScale) * Math.Min(1.0, dt / 0.4);

        // ── Animated layout width ────────────────────────────────────────────
        // Collapse the layout footprint only after the visual has nearly vanished
        // and the write-dot is also gone — prevents the dot from losing its space.
        if (_scaleTarget == 0.0 && _currentScale < 0.01 && _writeDotRadius < 0.5)
            _widthTarget = 0.0;

        var prevAnimatedWidth = _animatedWidth;
        _animatedWidth += (_widthTarget - _animatedWidth) * Math.Min(1.0, dt / 0.4);
        if (Math.Abs(_animatedWidth - prevAnimatedWidth) > 0.05)
            InvalidateMeasure();

        // ────────────────────────────────────────────────────────────────────

        if (_spinnerOpacity <= 0.0 && _targetOpacity <= 0.0 && _writeDotRadius <= 0.5
            && _currentScale < 0.01 && _animatedWidth < 0.5)
        {
            _spinnerOpacity = 0;
            _writeDotRadius = 0;
            _currentScale = 0.0;
            _animatedWidth = 0.0;
            _physicsTimer.Stop();
            Visibility = Visibility.Collapsed;
            InvalidateMeasure();
            return;
        }

        InvalidateVisual();
    }

    // ── Layout ──────────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        var h = Diameter + 4; // 2px padding each side
        return new(_animatedWidth, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        var renderSpinner = _spinnerOpacity > 0;
        var renderDot     = _writeDotRadius > 0.5;

        if (!renderSpinner && !renderDot)
            return;

        // Fixed drawing center — independent of AnimatedWidth so the spinner stays
        // centred in its full-size slot even while the layout width is animating.
        var fullSize = Diameter + 4;
        var centerX  = fullSize / 2.0;
        var centerY  = fullSize / 2.0;

        // Wrap all drawing in the scale-in/out transform (visual only, not layout).
        dc.PushTransform(new ScaleTransform(_currentScale, _currentScale, centerX, centerY));

        if (renderSpinner)
        {
            var color = AccentColor;

            // Bake opacity into alpha channel
            color.A = (byte)Math.Round(255 * _spinnerOpacity);

            var brush = new SolidColorBrush(color);
            brush.Freeze();

            var geo = GetShapeGeometry();

            // Scale so the 1957-unit shape fits in Diameter × Diameter, centred in element
            var diameter = Diameter;
            var scale    = diameter / OriginalCanvasSize;
            var offsetX  = (fullSize - diameter) / 2.0;
            var offsetY  = (fullSize - diameter) / 2.0;

            dc.PushTransform(new TranslateTransform(offsetX, offsetY));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new RotateTransform(_angle * (180.0 / Math.PI), OriginalCenter, OriginalCenter));
            dc.DrawGeometry(brush, null, geo);
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        if (renderDot)
        {
            var center      = new Point(centerX, centerY);
            var border      = Math.Max(_writeDotRadius * DotBorderFraction, 0.8);
            var glowOpacity = 0.75 + 0.25 * Math.Sin(_dotGlowPhase);

            // White border circle
            var whiteBrush = new SolidColorBrush(Colors.White);
            whiteBrush.Freeze();
            var outerR = _writeDotRadius + border;
            dc.DrawEllipse(whiteBrush, null, center, outerR, outerR);

            // Red fill circle with glow opacity
            var redAlpha = (byte)Math.Round(255 * glowOpacity);
            var redBrush = new SolidColorBrush(Color.FromArgb(redAlpha, 0xFF, 0x22, 0x22));
            redBrush.Freeze();
            dc.DrawEllipse(redBrush, null, center, _writeDotRadius, _writeDotRadius);
        }

        dc.Pop(); // pop scale transform
    }

    // ── Geometry ────────────────────────────────────────────────────────────

    private Geometry GetShapeGeometry()
    {
        if (_cachedShapeGeo is not null)
            return _cachedShapeGeo;

        var geo = Geometry.Parse(ShapeFigures);
        geo.Freeze();
        _cachedShapeGeo = geo;
        return geo;
    }

}
