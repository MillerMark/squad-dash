using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SquadDash;

/// <summary>
/// Central registry of all custom WPF cursors used by the annotation editor.
/// Every cursor is generated programmatically at first access and cached for the
/// lifetime of the application (static lazy-init pattern).
///
/// Adding a new cursor: add a private nullable backing field, a public property
/// that calls <see cref="CreateCursorFromDrawing"/>, and a private factory method
/// that builds the <see cref="Drawing"/>.
/// </summary>
internal static class AnnotationCursors
{
    // ── Backing stores ────────────────────────────────────────────────────────

    private static Cursor? _openHand;
    private static Cursor? _closedHand;
    private static Cursor? _arrowTool;
    private static Cursor? _rectTool;
    private static Cursor? _dropCursorTool;
    private static Cursor? _cropTool;
    private static Cursor? _eyedropperTool;
    private static Cursor? _textTool;

    // ── Public properties ─────────────────────────────────────────────────────

    /// <summary>Open hand — shown while Space is held (pan-ready mode).</summary>
    public static Cursor OpenHand
        => _openHand ??= CreateCursorFromDrawing(CreateOpenHandDrawing(), 32, 32, hotX: 10, hotY: 3);

    /// <summary>Closed hand — shown while actively panning (Space + drag).</summary>
    public static Cursor ClosedHand
        => _closedHand ??= CreateCursorFromDrawing(CreateClosedHandDrawing(), 32, 32, hotX: 14, hotY: 14);

    /// <summary>
    /// Arrow-tool cursor: a small orange annotation-arrow icon at the upper-left
    /// corner plus a precision crosshair. The crosshair centre is the hotspot (16, 16).
    /// </summary>
    public static Cursor ArrowTool
        => _arrowTool ??= CreateCursorFromDrawing(CreateArrowToolDrawing(), 32, 32, hotX: 16, hotY: 16);

    /// <summary>
    /// Rectangle-tool cursor: a small red rectangle outline at the upper-left corner
    /// plus a precision crosshair. The crosshair centre is the hotspot (16, 16).
    /// </summary>
    public static Cursor RectTool
        => _rectTool ??= CreateCursorFromDrawing(CreateRectToolDrawing(), 32, 32, hotX: 16, hotY: 16);

    /// <summary>
    /// Drop-cursor-tool cursor: a small pointer-arrow icon at the upper-left corner
    /// plus a precision crosshair. Shown while in cursor-placement mode.
    /// The crosshair centre is the hotspot (16, 16).
    /// </summary>
    public static Cursor DropCursorTool
        => _dropCursorTool ??= CreateCursorFromDrawing(CreateDropCursorToolDrawing(), 32, 32, hotX: 16, hotY: 16);

    /// <summary>
    /// Crop-tool cursor: corner bracket marks in each quadrant with a crosshair at
    /// centre. Shown when the canvas is in crop-draw state (no tool active).
    /// The crosshair centre is the hotspot (16, 16).
    /// </summary>
    public static Cursor CropTool
        => _cropTool ??= CreateCursorFromDrawing(CreateCropToolDrawing(), 32, 32, hotX: 16, hotY: 16);

    /// <summary>
    /// Eyedropper-tool cursor: a classic pipette icon oriented diagonally with the
    /// sampling tip at lower-left.  The tip of the nozzle is the hotspot (2, 28).
    /// </summary>
    public static Cursor EyedropperTool
        => _eyedropperTool ??= CreateCursorFromDrawing(CreateEyedropperToolDrawing(), 32, 32, hotX: 2, hotY: 28);

    /// <summary>
    /// Text-tool cursor: an I-beam with horizontal crosshair arms at centre.
    /// The I-beam's vertical bar serves as the crosshair vertical arm. Hotspot = (16, 16).
    /// </summary>
    public static Cursor TextTool
        => _textTool ??= CreateCursorFromDrawing(CreateTextToolDrawing(), 32, 32, hotX: 16, hotY: 16);

    // ── Cursor factory ────────────────────────────────────────────────────────

    /// <summary>
    /// Renders <paramref name="drawing"/> into a <see cref="Cursor"/> of the given
    /// pixel dimensions with the specified hot-spot.  Uses the .CUR file format
    /// with an embedded PNG image — supported on Windows Vista and later.
    /// </summary>
    private static Cursor CreateCursorFromDrawing(
        Drawing drawing, int widthPx, int heightPx, int hotX, int hotY)
    {
        var rtb = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
        var dv  = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawDrawing(drawing);
        rtb.Render(dv);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var pngStream = new MemoryStream();
        encoder.Save(pngStream);
        var png = pngStream.ToArray();

        // Build a minimal .CUR file with one PNG-compressed entry.
        using var cur = new MemoryStream();
        using var bw  = new BinaryWriter(cur);
        bw.Write((short)0);          // reserved
        bw.Write((short)2);          // type: cursor
        bw.Write((short)1);          // image count
        bw.Write((byte)widthPx);
        bw.Write((byte)heightPx);
        bw.Write((byte)0);           // colour count
        bw.Write((byte)0);           // reserved
        bw.Write((short)hotX);
        bw.Write((short)hotY);
        bw.Write((int)png.Length);
        bw.Write((int)22);           // byte offset to the PNG data (= header size)
        bw.Write(png);
        cur.Position = 0;
        return new Cursor(cur);
    }

    // ── Hand cursor drawings ──────────────────────────────────────────────────

    private static Drawing CreateOpenHandDrawing()
    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            var fill   = new SolidColorBrush(Color.FromRgb(255, 252, 242));
            var stroke = new Pen(new SolidColorBrush(Color.FromRgb(35, 35, 35)), 1.3)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap   = PenLineCap.Round
            };

            // Palm
            dc.DrawRoundedRectangle(fill, stroke, new Rect(5, 16, 18, 13), 3, 3);

            // Thumb (left side)
            var thumbGeo = new PathGeometry();
            var thumbFig = new PathFigure { StartPoint = new Point(5, 20), IsClosed = true };
            thumbFig.Segments.Add(new BezierSegment(new Point(2, 19), new Point(1, 15), new Point(3, 12), true));
            thumbFig.Segments.Add(new BezierSegment(new Point(4, 9),  new Point(6, 10), new Point(6, 13), true));
            thumbFig.Segments.Add(new LineSegment(new Point(5, 16), true));
            thumbGeo.Figures.Add(thumbFig);
            dc.DrawGeometry(fill, stroke, thumbGeo);

            // Four fingers: index, middle, ring, pinky
            double[] fingerX    = { 7,   10,  13,  17   };
            double[] fingerW    = { 3,    3,   3,   2.5  };
            double[] fingerTopY = { 5,    3,   4,   7    };
            for (int i = 0; i < 4; i++)
                dc.DrawRoundedRectangle(fill, stroke,
                    new Rect(fingerX[i], fingerTopY[i], fingerW[i], 16 - fingerTopY[i] + 1),
                    1.5, 1.5);
        }
        return dg;
    }

    private static Drawing CreateClosedHandDrawing()
    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            var fill   = new SolidColorBrush(Color.FromRgb(255, 252, 242));
            var stroke = new Pen(new SolidColorBrush(Color.FromRgb(35, 35, 35)), 1.3)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap   = PenLineCap.Round
            };

            // Knuckle row
            dc.DrawRoundedRectangle(fill, stroke, new Rect(5, 11, 19, 8), 3, 3);

            // Palm
            dc.DrawRoundedRectangle(fill, stroke, new Rect(5, 17, 19, 11), 3, 3);

            // Thumb tucked left
            dc.DrawRoundedRectangle(fill, stroke, new Rect(2, 14, 5, 7), 2, 2);

            // Finger separation lines on knuckle row
            var linePen = new Pen(new SolidColorBrush(Color.FromRgb(35, 35, 35)), 0.8);
            dc.DrawLine(linePen, new Point(10, 11), new Point(10, 19));
            dc.DrawLine(linePen, new Point(14, 11), new Point(14, 19));
            dc.DrawLine(linePen, new Point(18, 11), new Point(18, 19));
        }
        return dg;
    }

    // ── Tool cursor drawings ──────────────────────────────────────────────────

    /// <summary>
    /// Arrow-tool cursor (32×32).
    /// Upper-left: a small orange annotation arrow with its tip at (3, 3).
    /// Centre: a precision crosshair with a 3 px gap.  Hotspot = (16, 16).
    /// </summary>
    private static Drawing CreateArrowToolDrawing()
    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            // ── Arrow icon (upper-left quadrant) ─────────────────────────────

            // Colours matching the default annotation arrow
            var orange    = Color.FromRgb(255, 120, 20);
            var shadowClr = Color.FromArgb(110, 0, 0, 0);

            var arrowPen  = new Pen(new SolidColorBrush(orange),    1.5)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var shadowPen = new Pen(new SolidColorBrush(shadowClr), 1.5)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            // Shaft: tail at (11, 11) → tip at (4, 4)
            dc.DrawLine(shadowPen, new Point(12.0, 12.0), new Point(5.0, 5.0));
            dc.DrawLine(arrowPen,  new Point(11.0, 11.0), new Point(4.0, 4.0));

            // Arrowhead triangle — tip at (3, 3), pointing upper-left
            var shadowHead = new PathGeometry();
            var shFig      = new PathFigure { StartPoint = new Point(4, 4), IsClosed = true };
            shFig.Segments.Add(new LineSegment(new Point(10, 5), true));
            shFig.Segments.Add(new LineSegment(new Point(5, 10), true));
            shadowHead.Figures.Add(shFig);
            dc.DrawGeometry(new SolidColorBrush(shadowClr), null, shadowHead);

            var head    = new PathGeometry();
            var headFig = new PathFigure { StartPoint = new Point(3, 3), IsClosed = true };
            headFig.Segments.Add(new LineSegment(new Point(9, 4), true));
            headFig.Segments.Add(new LineSegment(new Point(4, 9), true));
            head.Figures.Add(headFig);
            dc.DrawGeometry(new SolidColorBrush(orange), null, head);

            // ── Crosshair at (16, 16) ─────────────────────────────────────────
            DrawCrosshair(dc, cx: 16, cy: 16);
        }
        return dg;
    }

    /// <summary>
    /// Rectangle-tool cursor (32×32).
    /// Upper-left: a small red rectangle outline.
    /// Centre: a precision crosshair with a 3 px gap.  Hotspot = (16, 16).
    /// </summary>
    private static Drawing CreateRectToolDrawing()
    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            // ── Rectangle icon (upper-left quadrant) ─────────────────────────

            var redClr    = Color.FromRgb(255, 80, 80);
            var shadowClr = Color.FromArgb(110, 0, 0, 0);

            var rectPen   = new Pen(new SolidColorBrush(redClr),    1.5) { LineJoin = PenLineJoin.Round };
            var shadowPen = new Pen(new SolidColorBrush(shadowClr), 1.5) { LineJoin = PenLineJoin.Round };

            // Shadow shifted 1 px down-right
            dc.DrawRoundedRectangle(null, shadowPen, new Rect(3, 3, 12, 8), 1.5, 1.5);
            // Rectangle outline
            dc.DrawRoundedRectangle(null, rectPen,   new Rect(2, 2, 12, 8), 1.5, 1.5);

            // ── Crosshair at (16, 16) ─────────────────────────────────────────
            DrawCrosshair(dc, cx: 16, cy: 16);
        }
        return dg;
    }

    /// <summary>
    /// Draws a four-armed precision crosshair centred at (<paramref name="cx"/>,
    /// <paramref name="cy"/>).  A 3 px gap in each arm keeps the hotspot visible.
    /// Each arm is rendered first in white (2 px wide) then in dark grey (1.2 px)
    /// so it is legible on both light and dark canvas content.
    /// </summary>
    private static Drawing CreateDropCursorToolDrawing()
    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            var fill      = new SolidColorBrush(Color.FromRgb(255, 252, 242));
            var outlinePen = new Pen(new SolidColorBrush(Color.FromRgb(30, 30, 30)), 1.2)
            {
                LineJoin     = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap   = PenLineCap.Round
            };
            var pointer = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(2.5, 2), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(2.5, 12.5), true));
            fig.Segments.Add(new LineSegment(new Point(5.5, 9.5),  true));
            fig.Segments.Add(new LineSegment(new Point(7.5, 13.5), true));
            fig.Segments.Add(new LineSegment(new Point(9.0, 12.8), true));
            fig.Segments.Add(new LineSegment(new Point(7.0, 8.5),  true));
            fig.Segments.Add(new LineSegment(new Point(9.5, 8.5),  true));
            pointer.Figures.Add(fig);
            dc.DrawGeometry(fill, outlinePen, pointer);
            DrawCrosshair(dc, cx: 16, cy: 16);
        }
        return dg;
    }

    /// <summary>
    /// Eyedropper cursor (32×32).
    /// Body: diagonal tube from upper-right (~cap at 22–28, 5–9) to lower-left
    /// (~nozzle end at 5–9, 22–27), with a cubic-bezier rounded cap at the top.
    /// Nozzle: narrow parallelogram continuing to tip at lower-left.
    /// Tip dot: filled ellipse centred at (4, 28); hotspot aligns with its left edge (2, 28).
    /// </summary>
    private static Drawing CreateEyedropperToolDrawing()
    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            var fill = new SolidColorBrush(Color.FromRgb(255, 252, 242));
            var outline = new Pen(new SolidColorBrush(Color.FromRgb(30, 30, 30)), 1.3)
            {
                LineJoin     = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap   = PenLineCap.Round,
            };

            // Body: elongated parallelogram with a bezier-rounded cap at upper-right.
            // Clockwise: start at upper-left cap corner → arc around cap → right edge
            // down to nozzle end → across nozzle end → left edge back up (auto-close).
            var bodyGeo = new PathGeometry();
            var bodyFig = new PathFigure { StartPoint = new Point(22, 5), IsClosed = true };
            // Rounded cap arc from (22,5) around upper-right to (28,9)
            bodyFig.Segments.Add(new BezierSegment(new Point(24, 1), new Point(30, 5), new Point(28, 9), true));
            bodyFig.Segments.Add(new LineSegment(new Point(9, 27), true));   // right edge →lower-left
            bodyFig.Segments.Add(new LineSegment(new Point(5, 22), true));   // across nozzle end
            bodyGeo.Figures.Add(bodyFig);
            dc.DrawGeometry(fill, outline, bodyGeo);

            // Nozzle: narrow parallelogram continuing from body end toward the tip.
            // Width ≈ 3 px (half-width 1.5 along the 45° normal).
            var nozzleGeo = new PathGeometry();
            var nozzleFig = new PathFigure { StartPoint = new Point(6, 24), IsClosed = true };
            nozzleFig.Segments.Add(new LineSegment(new Point(8, 26), true));
            nozzleFig.Segments.Add(new LineSegment(new Point(5, 29), true));
            nozzleFig.Segments.Add(new LineSegment(new Point(3, 27), true));
            nozzleGeo.Figures.Add(nozzleFig);
            dc.DrawGeometry(fill, outline, nozzleGeo);

            // Tip sampling dot — hotspot (2,28) aligns with its left edge.
            dc.DrawEllipse(fill, outline, new Point(4, 28), 2.5, 2.5);
        }
        return dg;
    }

    private static Drawing CreateCropToolDrawing()    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            const double M = 2.0;
            const double E = 30.0;
            const double B = 6.0;
            var whitePen = new Pen(Brushes.White, 2.5)
                { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square };
            var darkPen = new Pen(new SolidColorBrush(Color.FromRgb(30, 30, 30)), 1.5)
                { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square };
            // White backing for legibility
            dc.DrawLine(whitePen, new Point(M, M + B), new Point(M, M));
            dc.DrawLine(whitePen, new Point(M, M),     new Point(M + B, M));
            dc.DrawLine(whitePen, new Point(E - B, M), new Point(E, M));
            dc.DrawLine(whitePen, new Point(E, M),     new Point(E, M + B));
            dc.DrawLine(whitePen, new Point(M, E - B), new Point(M, E));
            dc.DrawLine(whitePen, new Point(M, E),     new Point(M + B, E));
            dc.DrawLine(whitePen, new Point(E - B, E), new Point(E, E));
            dc.DrawLine(whitePen, new Point(E, E),     new Point(E, E - B));
            // Dark bracket arms
            dc.DrawLine(darkPen, new Point(M, M + B), new Point(M, M));
            dc.DrawLine(darkPen, new Point(M, M),     new Point(M + B, M));
            dc.DrawLine(darkPen, new Point(E - B, M), new Point(E, M));
            dc.DrawLine(darkPen, new Point(E, M),     new Point(E, M + B));
            dc.DrawLine(darkPen, new Point(M, E - B), new Point(M, E));
            dc.DrawLine(darkPen, new Point(M, E),     new Point(M + B, E));
            dc.DrawLine(darkPen, new Point(E - B, E), new Point(E, E));
            dc.DrawLine(darkPen, new Point(E, E),     new Point(E, E - B));
            DrawCrosshair(dc, cx: 16, cy: 16);
        }
        return dg;
    }

    private static Drawing CreateTextToolDrawing()
    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            const double cx       = 16;
            const double cy       = 16;
            const double serifY1  = 5;
            const double serifY2  = 27;
            const double serifHW  = 4;  // half-width → 8 px total serif
            const double armGap   = 3;
            const double armLen   = 7;

            var whitePen = new Pen(Brushes.White, 2.0)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var darkPen = new Pen(new SolidColorBrush(Color.FromRgb(30, 30, 30)), 1.2)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            // White outline first for legibility
            dc.DrawLine(whitePen, new Point(cx - serifHW, serifY1), new Point(cx + serifHW, serifY1));
            dc.DrawLine(whitePen, new Point(cx - serifHW, serifY2), new Point(cx + serifHW, serifY2));
            dc.DrawLine(whitePen, new Point(cx, serifY1),           new Point(cx, serifY2));
            dc.DrawLine(whitePen, new Point(cx - armLen, cy),       new Point(cx - armGap, cy));
            dc.DrawLine(whitePen, new Point(cx + armGap, cy),       new Point(cx + armLen, cy));

            // Dark grey on top
            dc.DrawLine(darkPen, new Point(cx - serifHW, serifY1), new Point(cx + serifHW, serifY1));
            dc.DrawLine(darkPen, new Point(cx - serifHW, serifY2), new Point(cx + serifHW, serifY2));
            dc.DrawLine(darkPen, new Point(cx, serifY1),           new Point(cx, serifY2));
            dc.DrawLine(darkPen, new Point(cx - armLen, cy),       new Point(cx - armGap, cy));
            dc.DrawLine(darkPen, new Point(cx + armGap, cy),       new Point(cx + armLen, cy));
        }
        return dg;
    }

    private static void DrawCrosshair(DrawingContext dc, double cx, double cy)
    {
        const double Arm = 7.0;   // arm length from centre tip to end
        const double Gap = 3.0;   // half-gap distance from centre

        var whitePen = new Pen(Brushes.White, 2.0);
        var darkPen  = new Pen(new SolidColorBrush(Color.FromRgb(30, 30, 30)), 1.2);

        // White outline first (slightly thicker — creates legibility border)
        dc.DrawLine(whitePen, new Point(cx - Arm, cy), new Point(cx - Gap, cy));
        dc.DrawLine(whitePen, new Point(cx + Gap, cy), new Point(cx + Arm, cy));
        dc.DrawLine(whitePen, new Point(cx, cy - Arm), new Point(cx, cy - Gap));
        dc.DrawLine(whitePen, new Point(cx, cy + Gap), new Point(cx, cy + Arm));

        // Dark lines on top
        dc.DrawLine(darkPen, new Point(cx - Arm, cy), new Point(cx - Gap, cy));
        dc.DrawLine(darkPen, new Point(cx + Gap, cy), new Point(cx + Arm, cy));
        dc.DrawLine(darkPen, new Point(cx, cy - Arm), new Point(cx, cy - Gap));
        dc.DrawLine(darkPen, new Point(cx, cy + Gap), new Point(cx, cy + Arm));
    }
}
