using System;
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
    private double _diameter;           // currently rendered diameter, interpolates 12..18
    private double _targetDiameter;
    private double _readColorBlend;     // 0=yellow(thinking), 1=blue(reading)
    private double _writeColorBlend;    // 0=blue(reading), 1=red(writing); also drives diameter
    private double _spinnerOpacity;     // 0..1
    private double _targetOpacity;      // lerp target for opacity
    private double _satLightPhase;      // radians, for max-speed pulse oscillation

    // Timers
    private readonly DispatcherTimer _physicsTimer;
    private DateTime _lastTick;
    private DateTime _lastReadEventTime  = DateTime.MinValue;
    private DateTime _lastWriteEventTime = DateTime.MinValue;

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
    private const double ThinkingDiameter = 12.0;
    private const double WritingDiameter = 18.0;
    private const double WriteColorDecaySeconds = 7.5;
    private const double FadeOutThreshold = 0.18;    // rad/s — start fade below this
    private const double OpacityLerpRate = 2.0;      // units/sec — reaches target in ~0.5s
    private const double DiameterLerpRate = 2.5;     // interpolation speed (1/s)
    private const double PulseFrequency = 3.0;       // Hz for max-speed oscillation
    private const double PulseAmplitude = 0.22;      // ±22% brightness

    // Opacity targets per state
    private const double ThinkingTargetOpacity = 0.60;
    private const double ReadingTargetOpacity  = 0.80;
    private const double WritingTargetOpacity  = 1.00;

    // Colors: Thinking=yellow, Reading=blue, Writing=red
    private static readonly Color ThinkingColor = Color.FromRgb(0xFF, 0xDD, 0x00);
    private static readonly Color ReadingColor  = Color.FromRgb(0x22, 0x88, 0xFF);
    private static readonly Color WritingColor  = Color.FromRgb(0xFF, 0x44, 0x22);

    public ActivitySpinner()
    {
        _diameter = ThinkingDiameter;
        _targetDiameter = ThinkingDiameter;
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
            _subscribedCard = null;
        }
        if (e.NewValue is AgentStatusCard card)
        {
            _subscribedCard = card;
            card.ActivityPulsed += OnActivityPulsed;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _physicsTimer.Stop();
        if (_subscribedCard is not null)
        {
            _subscribedCard.ActivityPulsed -= OnActivityPulsed;
            _subscribedCard = null;
        }
        AgentStatusCard.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // Re-subscribe to theme change; just invalidate visual so next render
        // picks up the correct pulse direction.
        if (_physicsTimer.IsEnabled)
            InvalidateVisual();
    }

    // ── Activity pulse entry point ──────────────────────────────────────────

    private void OnActivityPulsed(object? sender, SpinnerActivityKind kind)
    {
        var impulse = kind == SpinnerActivityKind.Writing ? WritingImpulse : ThinkingImpulse;
        _angularVelocity = Math.Min(_angularVelocity + impulse, MaxAngularVelocity);

        var now = DateTime.UtcNow;
        if (kind == SpinnerActivityKind.Reading)
            _lastReadEventTime = now;
        else if (kind == SpinnerActivityKind.Writing)
            _lastWriteEventTime = now;

        // Snap target opacity immediately; _spinnerOpacity lerps toward it each frame
        _targetOpacity = kind switch
        {
            SpinnerActivityKind.Writing  => WritingTargetOpacity,
            SpinnerActivityKind.Reading  => ReadingTargetOpacity,
            _                            => ThinkingTargetOpacity
        };

        Visibility = Visibility.Visible;

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

        // Friction: exponential decay
        _angularVelocity *= Math.Exp(-FrictionK * dt);
        if (_angularVelocity < 1e-4) _angularVelocity = 0;

        // Advance rotation angle
        _angle += _angularVelocity * dt;
        if (_angle >= Math.PI * 2) _angle -= Math.PI * 2;

        // Read color blend — decays 1→0 after last reading event
        if (_lastReadEventTime != DateTime.MinValue)
        {
            var secondsSinceRead = (now - _lastReadEventTime).TotalSeconds;
            _readColorBlend = Math.Clamp(1.0 - secondsSinceRead / WriteColorDecaySeconds, 0.0, 1.0);
        }
        else
        {
            _readColorBlend = 0;
        }

        // Write color blend — decays 1→0 after last writing event
        if (_lastWriteEventTime != DateTime.MinValue)
        {
            var secondsSinceWrite = (now - _lastWriteEventTime).TotalSeconds;
            _writeColorBlend = Math.Clamp(1.0 - secondsSinceWrite / WriteColorDecaySeconds, 0.0, 1.0);
        }
        else
        {
            _writeColorBlend = 0;
        }

        // Interpolate diameter smoothly (driven by write blend for size variation)
        _targetDiameter = ThinkingDiameter + (WritingDiameter - ThinkingDiameter) * _writeColorBlend;
        var diameterDelta = (_targetDiameter - _diameter) * Math.Min(1.0, DiameterLerpRate * dt);
        _diameter += diameterDelta;

        // Max-speed saturation/lightness pulse
        var speedRatio = _angularVelocity / MaxAngularVelocity;
        if (speedRatio >= 0.85)
            _satLightPhase += 2.0 * Math.PI * PulseFrequency * dt;
        else
            _satLightPhase = 0;

        // Opacity: drive target toward 0 when coasting to a stop
        if (_angularVelocity < FadeOutThreshold)
            _targetOpacity = 0.0;

        // Lerp opacity toward target at OpacityLerpRate units/sec
        var opacityDiff = _targetOpacity - _spinnerOpacity;
        var opacityStep = Math.Min(Math.Abs(opacityDiff), OpacityLerpRate * dt) * Math.Sign(opacityDiff);
        _spinnerOpacity = Math.Clamp(_spinnerOpacity + opacityStep, 0.0, 1.0);

        if (_spinnerOpacity <= 0.0 && _targetOpacity <= 0.0)
        {
            _spinnerOpacity = 0;
            _physicsTimer.Stop();
            Visibility = Visibility.Collapsed;
            return;
        }

        InvalidateVisual();
    }

    // ── Layout ──────────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize) => new(18, 18);

    protected override Size ArrangeOverride(Size finalSize) => new(18, 18);

    // ── Rendering ───────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        if (_spinnerOpacity <= 0)
            return;

        // Three-way color blend: Thinking(yellow) → Reading(blue) → Writing(red)
        var midColor = LerpColor(ThinkingColor, ReadingColor, _readColorBlend);
        var color    = LerpColor(midColor, WritingColor, _writeColorBlend);

        // Apply max-speed pulse
        if (_angularVelocity / MaxAngularVelocity >= 0.85)
        {
            var isDark = AgentStatusCard.IsDarkTheme;
            var pulse = Math.Sin(_satLightPhase) * PulseAmplitude;
            if (!isDark) pulse = -pulse;
            color = AdjustBrightness(color, pulse);
        }

        // Bake opacity into alpha channel
        color.A = (byte)Math.Round(255 * _spinnerOpacity);

        var brush = new SolidColorBrush(color);
        brush.Freeze();

        var geo = GetShapeGeometry();

        // Scale so the 1957-unit shape fits in _diameter × _diameter, centred in 18×18
        var scale  = _diameter / OriginalCanvasSize;
        var offset = (18.0 - _diameter) / 2.0; // centres smaller shape in 18×18 box

        dc.PushTransform(new TranslateTransform(offset, offset));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.PushTransform(new RotateTransform(_angle * (180.0 / Math.PI), OriginalCenter, OriginalCenter));
        dc.DrawGeometry(brush, null, geo);
        dc.Pop();
        dc.Pop();
        dc.Pop();
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

    // ── Color helpers ────────────────────────────────────────────────────────

    private static Color LerpColor(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private static Color AdjustBrightness(Color c, double delta)
    {
        static byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
        var factor = 1.0 + delta;
        return Color.FromArgb(c.A, Clamp(c.R * factor), Clamp(c.G * factor), Clamp(c.B * factor));
    }
}
