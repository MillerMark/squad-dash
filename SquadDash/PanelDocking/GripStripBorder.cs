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

    // GripHeight = 80% of corner radius: covers the arc but leaves the flat
    // content area untouched so panel controls can render without interference.
    private double GripHeight => CornerRadius.TopLeft * 0.8;

    private void DrawGripStrip(DrawingContext dc)
    {
        double r = CornerRadius.TopLeft;
        double h = GripHeight;
        if (r <= 0 || ActualWidth <= 0) return;

        // Use the GripStripLine theme color — a midpoint between RosterPanelTitle
        // and RosterPanelSurface — for subtle, low-contrast hatching.
        var baseBrush = (TryFindResource("GripStripLine") as SolidColorBrush)
                        ?? new SolidColorBrush(Color.FromRgb(0x72, 0x60, 0x4C));
        var pen = new Pen(baseBrush, 1.0);
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

    // Set cursor to Hand when over the grip strip area
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        Cursor = pos.Y <= GripHeight ? Cursors.Hand : Cursors.Arrow;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Cursor = Cursors.Arrow;
    }
}
