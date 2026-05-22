using System;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace SquadDash;

// ─────────────────────────────────────────────────────────────────────────────
//  Stub definitions that establish the expected public contract for the
//  MeasurementLineTool feature being implemented by Lyra Morn.
//
//  Replace these with <Compile Include> links pointing at Lyra's real source
//  files once her feature branch is merged.  The tests in
//  MeasurementLineToolTests.cs are written against this interface and should
//  continue to compile and pass without modification after the swap.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Whether the measurement line is drawn horizontally or vertically.</summary>
internal enum LineOrientation { Horizontal, Vertical }

/// <summary>Whether the pixel-distance label is rendered inside the line span or beside it.</summary>
internal enum LabelPlacement { InsideLine, AdjacentToLine }

/// <summary>
/// Pure-logic helpers for the measurement/dimension-line annotation tool.
/// Handles orientation snapping, label text formatting, and label placement decisions.
/// </summary>
internal static class MeasurementLineTool
{
    /// <summary>
    /// Minimum extra space (in logical pixels) required on each side of the label
    /// text for it to be considered "inside" the line span.
    /// </summary>
    public const double LabelPaddingThreshold = 20.0;

    /// <summary>
    /// Snaps the drag vector to horizontal or vertical based on which axis has
    /// the larger magnitude.  When the magnitudes are equal the result is
    /// <see cref="LineOrientation.Horizontal"/> (deterministic tie-break).
    /// </summary>
    public static LineOrientation SnapOrientation(Point start, Point end)
    {
        var dx = Math.Abs(end.X - start.X);
        var dy = Math.Abs(end.Y - start.Y);
        return dx >= dy ? LineOrientation.Horizontal : LineOrientation.Vertical;
    }

    /// <summary>
    /// Returns the human-readable pixel-distance label, e.g. <c>"128 px"</c>.
    /// The distance is rounded to the nearest whole pixel.
    /// </summary>
    public static string FormatLabel(double pixelDistance)
        => $"{(int)Math.Round(pixelDistance)} px";

    /// <summary>
    /// Decides whether the label fits inside the line span or must be placed
    /// adjacent to it.  The label is considered to fit when
    /// <paramref name="lineLength"/> ≥ <paramref name="labelWidth"/> + <see cref="LabelPaddingThreshold"/>.
    /// </summary>
    public static LabelPlacement ComputeLabelPlacement(double lineLength, double labelWidth)
        => lineLength >= labelWidth + LabelPaddingThreshold
            ? LabelPlacement.InsideLine
            : LabelPlacement.AdjacentToLine;
}

/// <summary>
/// Serialisable record representing a saved measurement-line annotation.
/// Mirrors the pattern of <see cref="ArrowAnnotation"/> in the sidecar schema.
/// </summary>
internal sealed record MeasurementLineAnnotation(
    [property: JsonPropertyName("startX")]      double StartX,
    [property: JsonPropertyName("startY")]      double StartY,
    [property: JsonPropertyName("endX")]        double EndX,
    [property: JsonPropertyName("endY")]        double EndY,
    [property: JsonPropertyName("orientation")] string Orientation,
    [property: JsonPropertyName("color")]       string Color = "#FF0000")
{
    /// <summary>Badge background is always black — not user-configurable.</summary>
    public static readonly Color BadgeBackground = Colors.Black;

    /// <summary>Badge label text is always white — not user-configurable.</summary>
    public static readonly Color BadgeForeground = Colors.White;
}
