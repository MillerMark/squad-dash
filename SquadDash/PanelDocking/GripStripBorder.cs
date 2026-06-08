#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SquadDash.PanelDocking;

/// <summary>
/// A Border subclass that draws a hatched grip strip at its top edge and
/// raises GripStripClicked when the user clicks within the strip area.
/// The strip height equals the border's CornerRadius.TopLeft value.
/// </summary>
public sealed class GripStripBorder : Border, IDockResizeSizeHint
{
    public static readonly DependencyProperty DockMinimumWidthProperty =
        DependencyProperty.Register(
            nameof(DockMinimumWidth),
            typeof(double),
            typeof(GripStripBorder),
            new FrameworkPropertyMetadata(double.NaN));

    public static readonly DependencyProperty DockMaximumUsefulWidthProperty =
        DependencyProperty.Register(
            nameof(DockMaximumUsefulWidth),
            typeof(double),
            typeof(GripStripBorder),
            new FrameworkPropertyMetadata(double.NaN));

    public static readonly DependencyProperty DockMinimumHeightProperty =
        DependencyProperty.Register(
            nameof(DockMinimumHeight),
            typeof(double),
            typeof(GripStripBorder),
            new FrameworkPropertyMetadata(double.NaN));

    public static readonly DependencyProperty DockMaximumUsefulHeightProperty =
        DependencyProperty.Register(
            nameof(DockMaximumUsefulHeight),
            typeof(double),
            typeof(GripStripBorder),
            new FrameworkPropertyMetadata(double.NaN));

    public GripStripBorder()
    {
        ToolTipService.SetToolTip(this, "Click for docking map");
        ToolTipService.SetIsEnabled(this, false);
    }

    public double DockMinimumWidth
    {
        get => (double)GetValue(DockMinimumWidthProperty);
        set => SetValue(DockMinimumWidthProperty, value);
    }

    public double DockMaximumUsefulWidth
    {
        get => (double)GetValue(DockMaximumUsefulWidthProperty);
        set => SetValue(DockMaximumUsefulWidthProperty, value);
    }

    public double DockMinimumHeight
    {
        get => (double)GetValue(DockMinimumHeightProperty);
        set => SetValue(DockMinimumHeightProperty, value);
    }

    public double DockMaximumUsefulHeight
    {
        get => (double)GetValue(DockMaximumUsefulHeightProperty);
        set => SetValue(DockMaximumUsefulHeightProperty, value);
    }

    public event EventHandler? GripStripClicked;

    /// <summary>
    /// When set, this delegate is called first in <see cref="GetMaximumUsefulDockSize"/>.
    /// A non-null return value takes priority over <see cref="DockMaximumUsefulWidth"/>.
    /// </summary>
    public Func<DockResizeOrientation, double?>? MaximumUsefulSizeProvider { get; set; }

    public double GetMinimumDockSize(DockResizeOrientation orientation)
    {
        var explicitValue = orientation == DockResizeOrientation.Horizontal
            ? DockMinimumWidth
            : DockMinimumHeight;
        if (IsUsefulSize(explicitValue))
            return explicitValue;

        var frameworkValue = orientation == DockResizeOrientation.Horizontal
            ? MinWidth
            : MinHeight;
        if (IsUsefulSize(frameworkValue))
            return frameworkValue;

        return orientation == DockResizeOrientation.Horizontal ? 80 : 100;
    }

    public double? GetMaximumUsefulDockSize(DockResizeOrientation orientation)
    {
        if (MaximumUsefulSizeProvider?.Invoke(orientation) is { } computed)
            return Math.Max(GetMinimumDockSize(orientation), computed);

        var explicitValue = orientation == DockResizeOrientation.Horizontal
            ? DockMaximumUsefulWidth
            : DockMaximumUsefulHeight;
        if (IsUsefulSize(explicitValue))
            return Math.Max(GetMinimumDockSize(orientation), explicitValue);

        var frameworkValue = orientation == DockResizeOrientation.Horizontal
            ? MaxWidth
            : MaxHeight;
        if (IsUsefulSize(frameworkValue))
            return Math.Max(GetMinimumDockSize(orientation), frameworkValue);

        return null;
    }

    private static bool IsUsefulSize(double value) =>
        value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        DrawGripStrip(dc);
    }

    // GripHeight is capped at 8 px: a thin decorative strip that stays well
    // inside the rounded-corner arc and doesn't push panel content down.
    private double GripHeight => Math.Min(CornerRadius.TopLeft * 0.5, 8.0);

    private void DrawGripStrip(DrawingContext dc)
    {
        double r = CornerRadius.TopLeft;
        double h = GripHeight;
        if (r <= 0 || ActualWidth <= 0) return;

        // Derive line color from the panel's own background: same hue, just a
        // touch lighter (dark theme) or darker (light theme) — no accent intrusion.
        var pen = new Pen(GetGripBrush(), 1.0);
        pen.Freeze();

        // Draw lines within the 80% strip height, stride 4 → 3 clear lines in 16px.
        for (double y = 2; y < h; y += 4)
        {
            double xOffset = r - Math.Sqrt(r * r - (r - y) * (r - y));
            double lineStart = xOffset;
            double lineEnd = ActualWidth - xOffset;
            if (lineEnd <= lineStart) continue;

            var gl = new GuidelineSet(null, [y + 0.5]);
            dc.PushGuidelineSet(gl);
            dc.DrawLine(pen, new Point(lineStart, y + 0.5), new Point(lineEnd, y + 0.5));
            dc.Pop();
        }
    }

    /// <summary>
    /// Returns a brush that is the same warm hue as the panel background but shifted
    /// slightly toward mid-gray — barely visible hatching that never reads as an accent.
    /// </summary>
    private Brush GetGripBrush()
    {
        if (Background is SolidColorBrush bg)
        {
            var c = bg.Color;
            double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            Color tinted;
            if (luminance < 0.5)
            {
                // Dark background: push ~14 steps toward white, preserving warm hue ratio.
                tinted = Color.FromRgb(
                    (byte)Math.Min(255, c.R + 22),
                    (byte)Math.Min(255, c.G + 18),
                    (byte)Math.Min(255, c.B + 12));
            }
            else
            {
                // Light background: pull ~12 steps toward black, same warm-hue ratio.
                tinted = Color.FromRgb(
                    (byte)Math.Max(0, c.R - 18),
                    (byte)Math.Max(0, c.G - 15),
                    (byte)Math.Max(0, c.B - 10));
            }
            return new SolidColorBrush(tinted);
        }

        // No solid background — fall back to theme token then hardcoded default.
        return (TryFindResource("GripStripLine") as SolidColorBrush)
               ?? new SolidColorBrush(Color.FromRgb(0x3E, 0x36, 0x30));
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        var pos = e.GetPosition(this);
        if (pos.Y <= GripHeight)
        {
            e.Handled = true;
            GripStripClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    // Set cursor to Hand when over the grip strip area; show tooltip only over grip strip
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        var overGrip = pos.Y <= GripHeight;
        Cursor = overGrip ? Cursors.Hand : Cursors.Arrow;
        ToolTipService.SetIsEnabled(this, overGrip);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Cursor = Cursors.Arrow;
        ToolTipService.SetIsEnabled(this, false);
    }
}
