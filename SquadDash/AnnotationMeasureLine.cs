using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Input;

namespace SquadDash;

/// <summary>
/// Data model for a dimension/measurement-line annotation.
/// Stores the two snapped endpoints plus references to all WPF visual elements
/// that make up the rendered annotation (shadow layer + main layer + label badge).
/// </summary>
internal sealed class AnnotationMeasureLine {
    /// <summary>Lower-coordinate endpoint (left for horizontal, top for vertical).</summary>
    public Point StartPt { get; set; }

    /// <summary>Higher-coordinate endpoint (right for horizontal, bottom for vertical).</summary>
    public Point EndPt { get; set; }

    public bool IsHorizontal { get; set; }

    public Color LineColor { get; set; }

    // ── Shadow elements (drawn behind the main elements, offset by +1.5 px) ──
    public Line    ShadowLine  { get; set; } = null!;
    public Polygon ShadowHead1 { get; set; } = null!;  // shadow of arrowhead at StartPt
    public Polygon ShadowHead2 { get; set; } = null!;  // shadow of arrowhead at EndPt
    public Line    ShadowCap1  { get; set; } = null!;
    public Line    ShadowCap2  { get; set; } = null!;

    // ── Main visual elements ──────────────────────────────────────────────────
    public Line    MainLine { get; set; } = null!;
    public Polygon Head1    { get; set; } = null!;  // outward arrowhead at StartPt
    public Polygon Head2    { get; set; } = null!;  // outward arrowhead at EndPt
    public Line    Cap1     { get; set; } = null!;  // perpendicular tick at StartPt
    public Line    Cap2     { get; set; } = null!;  // perpendicular tick at EndPt

    // ── Label badge (white text on dark pill, similar to crop-size overlay) ──
    public Border    LabelBadge { get; set; } = null!;
    public TextBlock LabelText  { get; set; } = null!;

    /// <summary>Wide transparent hit-test proxy line, enables click/drag anywhere along the shaft.</summary>
    public Line HitLine { get; set; } = null!;

    /// <summary>Drag handle at <see cref="StartPt"/> (left end for H, top end for V). Hidden unless selected.</summary>
    public Ellipse Handle1 { get; set; } = null!;

    /// <summary>Drag handle at <see cref="EndPt"/> (right end for H, bottom end for V). Hidden unless selected.</summary>
    public Ellipse Handle2 { get; set; } = null!;
}
