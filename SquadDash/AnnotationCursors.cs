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
internal static class AnnotationCursors {
    // ── Backing stores ────────────────────────────────────────────────────────

    private static Cursor? _openHand;
    private static Cursor? _closedHand;
    private static Cursor? _arrowTool;
    private static Cursor? _rectTool;
    private static Cursor? _dropCursorTool;
    private static Cursor? _cropTool;
    private static Cursor? _eyedropperTool;
    private static Cursor? _textTool;
    private static Cursor? _rotateEndpoint;
    private static Cursor? _measureLineTool;
    private static Cursor? _xTool;

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

    /// <summary>
    /// Rotate-endpoint cursor: a ~270° circular arc with arrowheads at each end,
    /// indicating that the arrow endpoint can be orbited around the other end.
    /// Hotspot = (16, 16) — centre of the 32×32 bitmap.
    /// </summary>
    public static Cursor RotateEndpoint
        => _rotateEndpoint ??= CreateCursorFromDrawing(CreateRotateEndpointDrawing(), 32, 32, hotX: 16, hotY: 16);

    /// <summary>
    /// Measure-line-tool cursor: precision crosshair with a horizontal double-headed arrow
    /// at centre, indicating that you are about to drag a dimension line.
    /// Hotspot = (16, 16).
    /// </summary>
    public static Cursor MeasureLineTool
        => _measureLineTool ??= CreateCursorFromDrawing(CreateMeasureLineToolDrawing(), 32, 32, hotX: 16, hotY: 16);

    /// <summary>
    /// X-tool cursor: a small red X (two crossing lines) at upper-left corner plus a precision crosshair.
    /// The crosshair centre is the hotspot (16, 16).
    /// </summary>
    public static Cursor XTool
        => _xTool ??= CreateCursorFromDrawing(CreateXToolDrawing(), 32, 32, hotX: 16, hotY: 16);

    // ── Cursor factory ────────────────────────────────────────────────────────

    /// <summary>
    /// Renders <paramref name="drawing"/> into a <see cref="Cursor"/> of the given
    /// pixel dimensions with the specified hot-spot.  Uses the .CUR file format
    /// with an embedded PNG image — supported on Windows Vista and later.
    /// </summary>
    private static Cursor CreateCursorFromDrawing(
        Drawing drawing, int widthPx, int heightPx, int hotX, int hotY) {
        var rtb = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
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
        using var bw = new BinaryWriter(cur);
        bw.Write((short)0);          // reserved
        bw.Write((short)2);          // type: cursor
        bw.Write((short)1);          // image count
        bw.Write((byte)widthPx);
        bw.Write((byte)heightPx);
        bw.Write((byte)0);           // colour count
        bw.Write((byte)0);           // reserved
        bw.Write((short)hotX);
        bw.Write((short)hotY);
        bw.Write(png.Length);
        bw.Write(22);           // byte offset to the PNG data (= header size)
        bw.Write(png);
        cur.Position = 0;
        return new Cursor(cur);
    }

    // ── Hand cursor drawings ──────────────────────────────────────────────────

    private static Drawing CreateOpenHandDrawing() {
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            // Cartoon white-glove style: pure white fill, bold 2px outline.
            var fill   = Brushes.White;
            var stroke = new Pen(new SolidColorBrush(Color.FromRgb(25, 25, 25)), 2.0) {
                LineJoin  = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap   = PenLineCap.Round
            };

            // Palm — wide, generously rounded cartoon-glove shape.
            dc.DrawRoundedRectangle(fill, stroke, new Rect(4.5, 17, 21.5, 12), 5.5, 5.5);

            // Thumb — chubby, round stub protruding to the lower-left.
            var thumbGeo = new PathGeometry();
            var tf = new PathFigure { StartPoint = new Point(5.5, 21), IsClosed = true };
            tf.Segments.Add(new BezierSegment(new Point(0.5, 20), new Point(0, 13.5), new Point(2.5, 10), true));
            tf.Segments.Add(new BezierSegment(new Point(4,   7.5), new Point(8.5, 8.5), new Point(8.5, 13), true));
            tf.Segments.Add(new LineSegment(new Point(7, 18), true));
            thumbGeo.Figures.Add(tf);
            dc.DrawGeometry(fill, stroke, thumbGeo);

            // Four fingers: index, middle (tallest), ring, pinky.
            // Wider and rounder than realistic — exaggerated cartoon proportions.
            double[] fx    = { 7,    12,   16.5, 21   };
            double[] fw    = { 4.5,  4.5,  4.5,  3.5  };
            double[] ftopY = { 5.5,  2.5,  4.0,  7.5  };
            for (int i = 0; i < 4; i++)
                dc.DrawRoundedRectangle(fill, stroke,
                    new Rect(fx[i], ftopY[i], fw[i], 15.5 - ftopY[i]),
                    3.0, 3.0);
        }
        return dg;
    }

    private static Drawing CreateClosedHandDrawing() {
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            var fill   = Brushes.White;
            var stroke = new Pen(new SolidColorBrush(Color.FromRgb(25, 25, 25)), 2.0) {
                LineJoin  = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap   = PenLineCap.Round
            };

            // Knuckle bumps drawn first so the main body covers their lower halves,
            // making them appear to protrude from the top of the fist.
            double[] kx = { 7.5, 12.0, 16.5, 21.0 };
            double[] kr = { 3.0,  3.0,  2.8,  2.3 };
            var bumpPen = new Pen(new SolidColorBrush(Color.FromRgb(25, 25, 25)), 1.7);
            for (int i = 0; i < 4; i++)
                dc.DrawEllipse(fill, bumpPen, new Point(kx[i], 14.5), kr[i], kr[i]);

            // Main fist body — white fill covers the lower halves of the knuckle bumps.
            dc.DrawRoundedRectangle(fill, stroke, new Rect(3.5, 14.5, 23, 13.5), 4.5, 4.5);

            // Thumb — round stub on the left side.
            dc.DrawRoundedRectangle(fill, stroke, new Rect(0.5, 16.5, 5.5, 7), 2.8, 2.8);

            // Finger separation lines running down the main body.
            var sep = new Pen(new SolidColorBrush(Color.FromRgb(25, 25, 25)), 1.0);
            dc.DrawLine(sep, new Point(11.5, 14.5), new Point(11.5, 28));
            dc.DrawLine(sep, new Point(16,   14.5), new Point(16,   28));
            dc.DrawLine(sep, new Point(20.5, 14.5), new Point(20.5, 28));
        }
        return dg;
    }

    // ── Tool cursor drawings ──────────────────────────────────────────────────

    /// <summary>
    /// Arrow-tool cursor (32×32).
    /// Upper-left: a small orange annotation arrow with its tip at (3, 3).
    /// Centre: a precision crosshair with a 3 px gap.  Hotspot = (16, 16).
    /// </summary>
    private static Drawing CreateArrowToolDrawing() {
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            // ── Arrow icon (upper-left quadrant) ─────────────────────────────

            // Colours matching the default annotation arrow
            var orange = Color.FromRgb(255, 120, 20);
            var shadowClr = Color.FromArgb(110, 0, 0, 0);

            var arrowPen = new Pen(new SolidColorBrush(orange), 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var shadowPen = new Pen(new SolidColorBrush(shadowClr), 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            // Shaft: tail at (11, 11) → tip at (4, 4)
            dc.DrawLine(shadowPen, new Point(12.0, 12.0), new Point(5.0, 5.0));
            dc.DrawLine(arrowPen, new Point(11.0, 11.0), new Point(4.0, 4.0));

            // Arrowhead triangle — tip at (3, 3), pointing upper-left
            var shadowHead = new PathGeometry();
            var shFig = new PathFigure { StartPoint = new Point(4, 4), IsClosed = true };
            shFig.Segments.Add(new LineSegment(new Point(10, 5), true));
            shFig.Segments.Add(new LineSegment(new Point(5, 10), true));
            shadowHead.Figures.Add(shFig);
            dc.DrawGeometry(new SolidColorBrush(shadowClr), null, shadowHead);

            var head = new PathGeometry();
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
    private static Drawing CreateRectToolDrawing() {
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            // ── Rectangle icon (upper-left quadrant) ─────────────────────────

            var redClr = Color.FromRgb(255, 80, 80);
            var shadowClr = Color.FromArgb(110, 0, 0, 0);

            var rectPen = new Pen(new SolidColorBrush(redClr), 1.5) { LineJoin = PenLineJoin.Round };
            var shadowPen = new Pen(new SolidColorBrush(shadowClr), 1.5) { LineJoin = PenLineJoin.Round };

            // Shadow shifted 1 px down-right
            dc.DrawRoundedRectangle(null, shadowPen, new Rect(3, 3, 12, 8), 1.5, 1.5);
            // Rectangle outline
            dc.DrawRoundedRectangle(null, rectPen, new Rect(2, 2, 12, 8), 1.5, 1.5);

            // ── Crosshair at (16, 16) ─────────────────────────────────────────
            DrawCrosshair(dc, cx: 16, cy: 16);
        }
        return dg;
    }

    /// <summary>
    /// Draws a four-armed precision crosshair centered at (<paramref name="cx"/>,
    /// <paramref name="cy"/>).  A 3 px gap in each arm keeps the hotspot visible.
    /// Each arm is rendered first in white (2 px wide) then in dark grey (1.2 px)
    /// so it is legible on both light and dark canvas content.
    /// </summary>
    private static Drawing CreateDropCursorToolDrawing() {
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            var fill = new SolidColorBrush(Color.FromRgb(255, 252, 242));
            var outlinePen = new Pen(new SolidColorBrush(Color.FromRgb(30, 30, 30)), 1.2) {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            var pointer = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(2.5, 2), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(2.5, 12.5), true));
            fig.Segments.Add(new LineSegment(new Point(5.5, 9.5), true));
            fig.Segments.Add(new LineSegment(new Point(7.5, 13.5), true));
            fig.Segments.Add(new LineSegment(new Point(9.0, 12.8), true));
            fig.Segments.Add(new LineSegment(new Point(7.0, 8.5), true));
            fig.Segments.Add(new LineSegment(new Point(9.5, 8.5), true));
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
    private static Drawing CreateEyedropperToolDrawing() {
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            var fill = new SolidColorBrush(Color.FromRgb(255, 252, 242));
            var outline = new Pen(new SolidColorBrush(Color.FromRgb(30, 30, 30)), 1.3) {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
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

    private static Drawing CreateCropToolDrawing() {
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            const double M = 2.0;
            const double E = 30.0;
            const double B = 6.0;
            var whitePen = new Pen(Brushes.White, 2.5) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square };
            var darkPen = new Pen(new SolidColorBrush(Color.FromRgb(30, 30, 30)), 1.5) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square };
            // White backing for legibility
            dc.DrawLine(whitePen, new Point(M, M + B), new Point(M, M));
            dc.DrawLine(whitePen, new Point(M, M), new Point(M + B, M));
            dc.DrawLine(whitePen, new Point(E - B, M), new Point(E, M));
            dc.DrawLine(whitePen, new Point(E, M), new Point(E, M + B));
            dc.DrawLine(whitePen, new Point(M, E - B), new Point(M, E));
            dc.DrawLine(whitePen, new Point(M, E), new Point(M + B, E));
            dc.DrawLine(whitePen, new Point(E - B, E), new Point(E, E));
            dc.DrawLine(whitePen, new Point(E, E), new Point(E, E - B));
            // Dark bracket arms
            dc.DrawLine(darkPen, new Point(M, M + B), new Point(M, M));
            dc.DrawLine(darkPen, new Point(M, M), new Point(M + B, M));
            dc.DrawLine(darkPen, new Point(E - B, M), new Point(E, M));
            dc.DrawLine(darkPen, new Point(E, M), new Point(E, M + B));
            dc.DrawLine(darkPen, new Point(M, E - B), new Point(M, E));
            dc.DrawLine(darkPen, new Point(M, E), new Point(M + B, E));
            dc.DrawLine(darkPen, new Point(E - B, E), new Point(E, E));
            dc.DrawLine(darkPen, new Point(E, E), new Point(E, E - B));
            DrawCrosshair(dc, cx: 16, cy: 16);
        }
        return dg;
    }

    private static Drawing CreateTextToolDrawing() {
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            const double cx = 16;
            const double cy = 16;
            const double serifY1 = 5;
            const double serifY2 = 27;
            const double serifHW = 4;  // half-width → 8 px total serif
            const double armGap = 3;
            const double armLen = 7;

            var whitePen = new Pen(Brushes.White, 2.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var darkPen = new Pen(new SolidColorBrush(Color.FromRgb(30, 30, 30)), 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            // White outline first for legibility
            dc.DrawLine(whitePen, new Point(cx - serifHW, serifY1), new Point(cx + serifHW, serifY1));
            dc.DrawLine(whitePen, new Point(cx - serifHW, serifY2), new Point(cx + serifHW, serifY2));
            dc.DrawLine(whitePen, new Point(cx, serifY1), new Point(cx, serifY2));
            dc.DrawLine(whitePen, new Point(cx - armLen, cy), new Point(cx - armGap, cy));
            dc.DrawLine(whitePen, new Point(cx + armGap, cy), new Point(cx + armLen, cy));

            // Dark grey on top
            dc.DrawLine(darkPen, new Point(cx - serifHW, serifY1), new Point(cx + serifHW, serifY1));
            dc.DrawLine(darkPen, new Point(cx - serifHW, serifY2), new Point(cx + serifHW, serifY2));
            dc.DrawLine(darkPen, new Point(cx, serifY1), new Point(cx, serifY2));
            dc.DrawLine(darkPen, new Point(cx - armLen, cy), new Point(cx - armGap, cy));
            dc.DrawLine(darkPen, new Point(cx + armGap, cy), new Point(cx + armLen, cy));
        }
        return dg;
    }

    /// <summary>
    /// Rotate-endpoint cursor (32×32): a ~270° circular arc centred at (16, 16) with
    /// a radius of 11 px, rendered white-outline then dark, with a small arrowhead
    /// at each end of the arc to indicate bi-directional rotation.
    /// </summary>
    private static Drawing CreateRotateEndpointDrawing() {
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            const double Cx = 16, Cy = 16, R = 11;
            // Arc spans 270° starting at 45° (gap in upper-right quadrant).
            const double StartDeg = 45;
            const double SweepDeg = 270;

            var whitePen = new Pen(Brushes.White, 3.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var darkPen = new Pen(new SolidColorBrush(Color.FromRgb(30, 30, 30)), 2.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            // Build an arc geometry: a PathFigure with an ArcSegment.
            // WPF ArcSegment needs a start point and end point on the circle.
            static (double x, double y) CirclePt(double deg) {
                double rad = deg * Math.PI / 180.0;
                return (Cx + R * Math.Cos(rad), Cy + R * Math.Sin(rad));
            }

            var (sx, sy) = CirclePt(StartDeg);
            var (ex, ey) = CirclePt(StartDeg + SweepDeg);

            var arcFig = new PathFigure {
                StartPoint = new Point(sx, sy),
                IsClosed = false
            };
            // isLargeArc = true because 270° > 180°; sweep direction = clockwise.
            arcFig.Segments.Add(new ArcSegment(
                new Point(ex, ey),
                new Size(R, R),
                rotationAngle: 0,
                isLargeArc: true,
                sweepDirection: SweepDirection.Clockwise,
                isStroked: true));

            var arcGeo = new PathGeometry();
            arcGeo.Figures.Add(arcFig);

            dc.DrawGeometry(null, whitePen, arcGeo);
            dc.DrawGeometry(null, darkPen, arcGeo);

            // Arrowheads — tangent direction at each arc end.
            // At angle θ on a CW arc the tangent (CW) is perpendicular: (−sin θ, cos θ) → rotated +90°.
            // At start (45°): tangent points towards increasing angle → (−sin45, cos45) = (−√2/2, √2/2).
            // We want the arrowhead to face *backward* along that tangent (i.e. as if the arc arrives there
            // from outside), so flip the direction at the start end.
            DrawArcArrowhead(dc, darkPen, whitePen, sx, sy, 45 + 90 + 180);   // tip end (arc "leaves" here going CW)
            DrawArcArrowhead(dc, darkPen, whitePen, ex, ey, (StartDeg + SweepDeg) + 90); // tail end
        }
        return dg;
    }

    /// <summary>
    /// Draws a small two-line arrowhead at (<paramref name="tipX"/>, <paramref name="tipY"/>)
    /// pointing in <paramref name="directionDeg"/> degrees.
    /// </summary>
    private static void DrawArcArrowhead(
        DrawingContext dc, Pen darkPen, Pen whitePen,
        double tipX, double tipY, double directionDeg) {
        const double Size = 5.5;
        const double HalfSpread = 0.45; // radians ≈ 26°
        double rad = directionDeg * Math.PI / 180.0;
        double ax = tipX + Size * Math.Cos(rad + HalfSpread);
        double ay = tipY + Size * Math.Sin(rad + HalfSpread);
        double bx = tipX + Size * Math.Cos(rad - HalfSpread);
        double by = tipY + Size * Math.Sin(rad - HalfSpread);
        var tip = new Point(tipX, tipY);
        var pa = new Point(ax, ay);
        var pb = new Point(bx, by);
        dc.DrawLine(whitePen, tip, pa);
        dc.DrawLine(whitePen, tip, pb);
        dc.DrawLine(darkPen, tip, pa);
        dc.DrawLine(darkPen, tip, pb);
    }

    /// <summary>Measure-line-tool cursor (32×32): crosshair + horizontal double-headed arrow.</summary>
    private static Drawing CreateMeasureLineToolDrawing() {
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            // Precision crosshair (fine, light grey lines with gap around centre)
            var crossPen = new Pen(new SolidColorBrush(Color.FromArgb(180, 80, 80, 80)), 1.0);
            const double gap = 4.0;
            dc.DrawLine(crossPen, new Point(0, 16), new Point(16 - gap, 16));   // left arm
            dc.DrawLine(crossPen, new Point(16 + gap, 16), new Point(32, 16));  // right arm
            dc.DrawLine(crossPen, new Point(16, 0), new Point(16, 16 - gap));   // top arm
            dc.DrawLine(crossPen, new Point(16, 16 + gap), new Point(16, 32));  // bottom arm

            // Horizontal double-headed arrow (←→) centred at (16, 16)
            var arrowPen = new Pen(new SolidColorBrush(Color.FromRgb(30, 30, 30)), 1.5) {
                StartLineCap = PenLineCap.Round,
                EndLineCap   = PenLineCap.Round,
            };
            const double ax1 = 8.0, ax2 = 24.0, ay = 16.0;
            const double headLen = 4.0, headH = 3.0;

            // Shaft
            dc.DrawLine(arrowPen, new Point(ax1, ay), new Point(ax2, ay));

            // Left arrowhead
            var leftHead = new StreamGeometry();
            using (var sgc = leftHead.Open()) {
                sgc.BeginFigure(new Point(ax1, ay), true, true);
                sgc.LineTo(new Point(ax1 + headLen, ay - headH), true, false);
                sgc.LineTo(new Point(ax1 + headLen, ay + headH), true, false);
            }
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(30, 30, 30)), null, leftHead);

            // Right arrowhead
            var rightHead = new StreamGeometry();
            using (var sgc = rightHead.Open()) {
                sgc.BeginFigure(new Point(ax2, ay), true, true);
                sgc.LineTo(new Point(ax2 - headLen, ay - headH), true, false);
                sgc.LineTo(new Point(ax2 - headLen, ay + headH), true, false);
            }
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(30, 30, 30)), null, rightHead);
        }
        return dg;
    }

    /// <summary>
    /// X-tool cursor (32×32).
    /// Upper-left: a small red X (two crossing diagonal lines).
    /// Centre: a precision crosshair with a 3 px gap.  Hotspot = (16, 16).
    /// </summary>
    private static Drawing CreateXToolDrawing() {
        var dg = new DrawingGroup();
        using (var dc = dg.Open()) {
            // ── X icon (upper-left quadrant) ──────────────────────────────────

            var redClr = Color.FromRgb(255, 80, 80);
            var shadowClr = Color.FromArgb(110, 0, 0, 0);

            var xPen = new Pen(new SolidColorBrush(redClr), 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var shadowPen = new Pen(new SolidColorBrush(shadowClr), 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            // Shadow shifted 1 px down-right
            dc.DrawLine(shadowPen, new Point(3, 3), new Point(13, 13));
            dc.DrawLine(shadowPen, new Point(14, 3), new Point(4, 13));
            // X lines
            dc.DrawLine(xPen, new Point(2, 2), new Point(12, 12));
            dc.DrawLine(xPen, new Point(13, 2), new Point(3, 12));

            // ── Crosshair at (16, 16) ─────────────────────────────────────────
            DrawCrosshair(dc, cx: 16, cy: 16);
        }
        return dg;
    }

    private static void DrawCrosshair(DrawingContext dc, double cx, double cy) {
        const double Arm = 7.0;   // arm length from centre tip to end
        const double Gap = 3.0;   // half-gap distance from centre

        var whitePen = new Pen(Brushes.White, 2.0);
        var darkPen = new Pen(new SolidColorBrush(Color.FromRgb(30, 30, 30)), 1.2);

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
