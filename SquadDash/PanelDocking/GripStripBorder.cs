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
public sealed class GripStripBorder : Border
{
    public GripStripBorder()
    {
        ToolTipService.SetToolTip(this, "Docking map\u2026");
    }

    public event EventHandler? GripStripClicked;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        DrawGripStrip(dc);
    }

    private void DrawGripStrip(DrawingContext dc)
    {
        double r = CornerRadius.TopLeft;
        if (r <= 0 || ActualWidth <= 0) return;

        // Use the panel-title color (warm gold / dark brown) for visible contrast.
        // Fall back to BorderBrush then Gray if the resource is not found.
        var baseBrush = (TryFindResource("RosterPanelTitle") as SolidColorBrush)
                        ?? (BorderBrush as SolidColorBrush)
                        ?? new SolidColorBrush(Colors.Gray);
        var color = baseBrush.Color;
        color.A = (byte)(255 * 0.50);
        var pen = new Pen(new SolidColorBrush(color), 1.0);
        pen.Freeze();

        // Draw a horizontal line every 4 pixels within the strip height.
        // Stride 4 gives ~4 clearly separated lines in a 16px strip.
        for (double y = 2; y < r; y += 4)
        {
            // Compute x_offset from corner arc: how far inward the arc extends at this Y
            double xOffset = r - Math.Sqrt(r * r - (r - y) * (r - y));
            double lineStart = xOffset;
            double lineEnd = ActualWidth - xOffset;
            if (lineEnd <= lineStart) continue;

            // Use GuidelineSet for pixel-snapped rendering
            var gl = new GuidelineSet(null, [y + 0.5]);
            dc.PushGuidelineSet(gl);
            dc.DrawLine(pen, new Point(lineStart, y + 0.5), new Point(lineEnd, y + 0.5));
            dc.Pop();
        }
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        double r = CornerRadius.TopLeft;
        var pos = e.GetPosition(this);
        if (pos.Y <= r)
        {
            e.Handled = true;
            GripStripClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    // Set cursor to Hand when over the grip strip area
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        double r = CornerRadius.TopLeft;
        var pos = e.GetPosition(this);
        Cursor = pos.Y <= r ? Cursors.Hand : Cursors.Arrow;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Cursor = Cursors.Arrow;
    }
}
