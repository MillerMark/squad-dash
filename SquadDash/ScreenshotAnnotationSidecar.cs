using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SquadDash;

// ─────────────────────────────────────────────────────────────────────────────
//  Annotation sidecar schema  — written as  {name}-{theme}.annotations.json
//  alongside every PNG saved through the interactive annotation editor.
//
//  Version history
//  ───────────────
//  1  — initial  (cursor, arrows, description)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Root record for the <c>.annotations.json</c> sidecar written by
/// <see cref="ScreenshotOverlayWindow"/> after the user annotates a capture.
/// </summary>
internal sealed record AnnotationSidecar(
    [property: JsonPropertyName("version")]        int                   Version,
    [property: JsonPropertyName("description")]    string?               Description,
    [property: JsonPropertyName("rawDescription")] string?               RawDescription,
    [property: JsonPropertyName("cursor")]         CursorAnnotation?     Cursor,
    [property: JsonPropertyName("arrows")]         List<ArrowAnnotation> Arrows);

/// <summary>Serialised position of the cursor overlay image.</summary>
internal sealed record CursorAnnotation(
    /// <summary>
    /// Logical-px offset from the anchor element's top-left (when <see cref="AnchorName"/> is
    /// non-null), or from the left edge of the capture selection (legacy / no anchor).
    /// </summary>
    [property: JsonPropertyName("x")]            double        X,
    /// <summary>
    /// Logical-px offset from the anchor element's top (when <see cref="AnchorName"/> is
    /// non-null), or from the top edge of the capture selection (legacy / no anchor).
    /// </summary>
    [property: JsonPropertyName("y")]            double        Y,
    /// <summary>Cursor shape type — currently always <c>"arrow"</c>.</summary>
    [property: JsonPropertyName("type")]         string        Type,
    /// <summary>
    /// Name of the closest named WPF element that contains the cursor position, or
    /// <c>null</c> when the cursor is not over any named element.  When present,
    /// <see cref="X"/>/<see cref="Y"/> are relative to this element's top-left corner.
    /// </summary>
    [property: JsonPropertyName("anchorName")]   string?       AnchorName   = null,
    /// <summary>
    /// Bounding box of the anchor element in main-window logical coordinates, or
    /// <c>null</c> when <see cref="AnchorName"/> is <c>null</c>.
    /// </summary>
    [property: JsonPropertyName("anchorBounds")] BoundsRecord? AnchorBounds = null);

/// <summary>Serialised state of a single annotation arrow.</summary>
internal sealed record ArrowAnnotation(
    /// <summary>
    /// <c>x:Name</c> of the WPF element the arrow points at.
    /// Empty string when the element is unnamed.
    /// </summary>
    [property: JsonPropertyName("targetElementName")]   string       TargetElementName,
    /// <summary>
    /// Bounding box of the target element in logical pixels relative to
    /// the <c>MainWindow</c> top-left, recorded at annotation time.
    /// </summary>
    [property: JsonPropertyName("targetElementBounds")] BoundsRecord TargetElementBounds,
    /// <summary>
    /// Clockwise angle in degrees from 12 o'clock (up) to the arrowhead tip.
    /// 0 = tip directly above target, 90 = tip to the right, etc.
    /// </summary>
    [property: JsonPropertyName("arrowheadAngleDeg")]   double       ArrowheadAngleDeg,
    /// <summary>
    /// Distance in logical pixels from the target element centre to the
    /// arrowhead tip.
    /// </summary>
    [property: JsonPropertyName("arrowLength")]         double       ArrowLength,
    /// <summary>Extension of shaft beyond arrowhead tip toward the tail end, in logical pixels.</summary>
    [property: JsonPropertyName("tailLength")]          double       TailLength = 80.0,
    /// <summary>
    /// Arrow colour as a CSS hex string, e.g. <c>#FF7814</c>.
    /// Defaults to the original orange when not present in the JSON.
    /// </summary>
    [property: JsonPropertyName("color")]               string       Color = "#FF7814",
    /// <summary>Horizontal translation of the pivot from <c>targetElementBounds</c> centre, in logical pixels.</summary>
    [property: JsonPropertyName("offsetX")]             double       OffsetX = 0.0,
    /// <summary>Vertical translation of the pivot from <c>targetElementBounds</c> centre, in logical pixels.</summary>
    [property: JsonPropertyName("offsetY")]             double       OffsetY = 0.0);

/// <summary>Axis-aligned bounding rectangle, all values in logical pixels.</summary>
internal sealed record BoundsRecord(
    [property: JsonPropertyName("x")]      double X,
    [property: JsonPropertyName("y")]      double Y,
    [property: JsonPropertyName("width")]  double Width,
    [property: JsonPropertyName("height")] double Height);
