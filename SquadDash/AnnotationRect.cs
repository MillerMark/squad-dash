using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SquadDash;

/// <summary>
/// Runtime state for a single annotation rectangle drawn on a <see cref="System.Windows.Controls.Canvas"/>.
/// Used by <see cref="ClipboardImageEditorWindow"/>.
/// </summary>
internal sealed class AnnotationRect
{
    /// <summary>Position and size in canvas logical coordinates.</summary>
    public Rect Bounds { get; set; }

    /// <summary>Stroke colour applied to the rect border and handles.</summary>
    public Color RectColor { get; set; } = Color.FromRgb(255, 80, 80);

    /// <summary>The visible rounded-rectangle stroke shape.</summary>
    public Rectangle Border { get; set; } = null!;

    /// <summary>Drop-shadow copy of <see cref="Border"/> rendered 2 px below-right.</summary>
    public Rectangle Shadow { get; set; } = null!;

    /// <summary>
    /// Resize handles: [0]=NW, [1]=NE, [2]=SW, [3]=SE, [4]=N, [5]=S, [6]=W, [7]=E.
    /// Hidden unless the rect is selected.
    /// </summary>
    public Ellipse[] Handles { get; set; } = null!;

    /// <summary>Transparent rectangle inflated 3 px on each side — widens the clickable area.</summary>
    public Rectangle HitZoneRect { get; set; } = null!;
}
