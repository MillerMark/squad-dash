using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows;

namespace SquadDash;

/// <summary>
/// Runtime state for a single annotation arrow drawn on a <see cref="Canvas"/>.
/// Shared by <see cref="ScreenshotOverlayWindow"/> and <see cref="ClipboardImageEditorWindow"/>.
/// </summary>
internal sealed class AnnotationArrow
{
    /// <summary><c>x:Name</c> of the targeted WPF element (empty if unnamed).</summary>
    public string  TargetElementName    { get; set; } = "";

    /// <summary>
    /// Bounding box of the targeted element in mainWindow logical coordinates,
    /// snapshotted when the arrow was created.
    /// </summary>
    public Rect    TargetElementBounds  { get; set; }

    /// <summary>
    /// Clockwise angle in degrees from 12 o'clock to the arrowhead tip.
    /// 0° = tip directly above target centre.
    /// </summary>
    public double  ArrowheadAngleDeg    { get; set; }

    /// <summary>Distance from target centre to arrowhead tip in logical pixels.</summary>
    public double  ArrowLength          { get; set; }

    /// <summary>Length of the shaft beyond the arrowhead tip, away from the target centre.</summary>
    public double  TailLength           { get; set; }

    /// <summary>
    /// User-adjusted tail length, preserved across tip-handle drags.
    /// Negative means "not set" — auto-fill to selection edge.
    /// </summary>
    public double  UserTailLength       { get; set; } = -1.0;

    /// <summary>Colour applied to the shaft line and arrowhead fill.</summary>
    public Color   ArrowColor           { get; set; } = Color.FromRgb(255, 120, 20);

    /// <summary>The shaft line (tail end → arrowhead tip).</summary>
    public Line    Line                 { get; set; } = null!;

    /// <summary>Filled triangle at the tip of the arrow.</summary>
    public Polygon Head                 { get; set; } = null!;

    /// <summary>8×8 drag handle centred on the arrowhead tip.</summary>
    public Ellipse TipHandle            { get; set; } = null!;

    /// <summary>8×8 drag handle centred on the target-centre end (tail) of the arrow.</summary>
    public Ellipse TailHandle           { get; set; } = null!;

    /// <summary>
    /// Target element centre in overlay canvas logical coordinates.
    /// Cached at creation time; does not change when the arrow is dragged.
    /// </summary>
    public Point   TargetCenterOnCanvas { get; set; }

    /// <summary>Horizontal translation of the arrow's effective pivot from <see cref="TargetCenterOnCanvas"/>.</summary>
    public double  OffsetX              { get; set; } = 0.0;

    /// <summary>Vertical translation of the arrow's effective pivot from <see cref="TargetCenterOnCanvas"/>.</summary>
    public double  OffsetY              { get; set; } = 0.0;

    /// <summary>Drop-shadow polyline drawn 2 px below and to the right of <see cref="Line"/>.</summary>
    public Polyline ShadowLine          { get; set; } = null!;

    /// <summary>Drop-shadow polygon drawn 2 px below and to the right of <see cref="Head"/>.</summary>
    public Polygon  ShadowHead          { get; set; } = null!;
}
