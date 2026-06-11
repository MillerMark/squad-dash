using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SquadDash;

/// <summary>
/// Runtime state for a single "X" annotation drawn on a <see cref="System.Windows.Controls.Canvas"/>.
/// The X is drawn as two diagonal lines crossing within <see cref="Bounds"/>.
/// Used by <see cref="ClipboardImageEditorWindow"/>.
/// </summary>
internal sealed class AnnotationX
{
    /// <summary>Bounding box in canvas logical coordinates.</summary>
    public Rect Bounds { get; set; }

    /// <summary>Stroke colour applied to both diagonal lines.</summary>
    public Color XColor { get; set; } = Color.FromRgb(255, 80, 80);

    /// <summary>NW→SE diagonal (primary line).</summary>
    public Line Line1 { get; set; } = null!;

    /// <summary>NE→SW diagonal (secondary line).</summary>
    public Line Line2 { get; set; } = null!;

    /// <summary>Transparent rectangle inflated 3 px on each side — widens the clickable area.</summary>
    public Rectangle HitZoneRect { get; set; } = null!;

    /// <summary>
    /// Resize handles: [0]=NW, [1]=NE, [2]=SW, [3]=SE, [4]=N, [5]=S, [6]=W, [7]=E.
    /// Hidden unless the X is selected.
    /// </summary>
    public Rectangle[] Handles { get; set; } = null!;
}
