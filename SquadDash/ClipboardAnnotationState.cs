using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SquadDash;

// ─────────────────────────────────────────────────────────────────────────────
//  Annotation state for pasted-image prompt attachments
//
//  Written as  {imagePath}.annotation.json  alongside the rendered PNG.
//  A matching  {imagePath}.source.png  holds the un-annotated source image so
//  the annotation editor can re-open with all edits fully intact.
//
//  Both sidecar files are deleted when the prompt is dispatched to the
//  transcript (the flat PNG is kept for the LLM context).
//
//  Version history
//  ───────────────
//  1 — initial (crop, arrows, rects, texts, measure lines, cursor)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Serialisable snapshot of the <see cref="ClipboardImageEditorWindow"/> annotation
/// state, persisted as a <c>.annotation.json</c> sidecar file while the attachment
/// is still in the prompt queue or draft.
/// </summary>
internal sealed class ClipboardAnnotationState
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>Canvas-to-pixel X scale at save time (always 1.0 for pasted images).</summary>
    [JsonPropertyName("canvasScaleX")]
    public double CanvasScaleX { get; set; } = 1.0;

    /// <summary>Canvas-to-pixel Y scale at save time.</summary>
    [JsonPropertyName("canvasScaleY")]
    public double CanvasScaleY { get; set; } = 1.0;

    // ── Crop selection ────────────────────────────────────────────────────────

    /// <summary>True when a pending crop selection existed at save time.</summary>
    [JsonPropertyName("hasCrop")]
    public bool HasCrop { get; set; }

    [JsonPropertyName("cropX")]
    public double CropX { get; set; }

    [JsonPropertyName("cropY")]
    public double CropY { get; set; }

    [JsonPropertyName("cropW")]
    public double CropW { get; set; }

    [JsonPropertyName("cropH")]
    public double CropH { get; set; }

    // ── Applied (destructive) crop within the original image ──────────────────
    //
    // When the user crops in-place, _workingImage is replaced with the cropped bitmap.
    // HasAppliedCrop=true means the sidecar .source.png is the *full* original image and
    // annotation coordinates are in cropped-image space (i.e. relative to AppliedCropX/Y).
    // On re-open, the editor shifts all annotation coordinates by +AppliedCropX/Y so they
    // land in the correct position on the full original image.

    [JsonPropertyName("hasAppliedCrop")]
    public bool HasAppliedCrop { get; set; }

    /// <summary>Canvas X of the top-left of the previously applied crop within the original image.</summary>
    [JsonPropertyName("appliedCropX")]
    public double AppliedCropX { get; set; }

    /// <summary>Canvas Y of the top-left of the previously applied crop within the original image.</summary>
    [JsonPropertyName("appliedCropY")]
    public double AppliedCropY { get; set; }

    /// <summary>Canvas width of the previously applied crop region.</summary>
    [JsonPropertyName("appliedCropW")]
    public double AppliedCropW { get; set; }

    /// <summary>Canvas height of the previously applied crop region.</summary>
    [JsonPropertyName("appliedCropH")]
    public double AppliedCropH { get; set; }

    // ── Cursor overlay ────────────────────────────────────────────────────────

    [JsonPropertyName("cursorEnabled")]
    public bool CursorEnabled { get; set; }

    [JsonPropertyName("cursorX")]
    public double CursorX { get; set; }

    [JsonPropertyName("cursorY")]
    public double CursorY { get; set; }

    // ── Annotation collections ────────────────────────────────────────────────

    [JsonPropertyName("arrows")]
    public List<ClipboardAnnotationArrowState> Arrows { get; set; } = new();

    [JsonPropertyName("rects")]
    public List<ClipboardAnnotationRectState> Rects { get; set; } = new();

    [JsonPropertyName("texts")]
    public List<ClipboardAnnotationTextState> Texts { get; set; } = new();

    [JsonPropertyName("measureLines")]
    public List<ClipboardAnnotationMeasureLineState> MeasureLines { get; set; } = new();
}

/// <summary>Serialisable state for a single annotation arrow.</summary>
internal sealed class ClipboardAnnotationArrowState
{
    [JsonPropertyName("targetName")]
    public string TargetElementName { get; set; } = "";

    [JsonPropertyName("targetX")]
    public double TargetBoundsX { get; set; }

    [JsonPropertyName("targetY")]
    public double TargetBoundsY { get; set; }

    [JsonPropertyName("targetW")]
    public double TargetBoundsW { get; set; }

    [JsonPropertyName("targetH")]
    public double TargetBoundsH { get; set; }

    [JsonPropertyName("angleDeg")]
    public double ArrowheadAngleDeg { get; set; }

    [JsonPropertyName("arrowLength")]
    public double ArrowLength { get; set; }

    [JsonPropertyName("tailLength")]
    public double TailLength { get; set; }

    [JsonPropertyName("userTailLength")]
    public double UserTailLength { get; set; }

    /// <summary>Arrow colour as <c>#RRGGBB</c>.</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FF7814";

    [JsonPropertyName("centerX")]
    public double TargetCenterX { get; set; }

    [JsonPropertyName("centerY")]
    public double TargetCenterY { get; set; }

    [JsonPropertyName("offsetX")]
    public double OffsetX { get; set; }

    [JsonPropertyName("offsetY")]
    public double OffsetY { get; set; }
}

/// <summary>Serialisable state for a single annotation rectangle.</summary>
internal sealed class ClipboardAnnotationRectState
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("w")]
    public double W { get; set; }

    [JsonPropertyName("h")]
    public double H { get; set; }

    /// <summary>Border colour as <c>#RRGGBB</c>.</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FF5050";
}

/// <summary>Serialisable state for a single text-label annotation.</summary>
internal sealed class ClipboardAnnotationTextState
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("w")]
    public double W { get; set; }

    [JsonPropertyName("h")]
    public double H { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; }

    /// <summary>Text foreground colour as <c>#RRGGBB</c>.</summary>
    [JsonPropertyName("fgColor")]
    public string FgColor { get; set; } = "#FFFFFF";

    /// <summary>
    /// Background fill colour.  Use <c>#00000000</c> for transparent (no background).
    /// Otherwise <c>#AARRGGBB</c>.
    /// </summary>
    [JsonPropertyName("bgColor")]
    public string BgColor { get; set; } = "#FF000000";
}

/// <summary>Serialisable state for a single measurement-line annotation.</summary>
internal sealed class ClipboardAnnotationMeasureLineState
{
    [JsonPropertyName("x1")]
    public double X1 { get; set; }

    [JsonPropertyName("y1")]
    public double Y1 { get; set; }

    [JsonPropertyName("x2")]
    public double X2 { get; set; }

    [JsonPropertyName("y2")]
    public double Y2 { get; set; }

    [JsonPropertyName("isHorizontal")]
    public bool IsHorizontal { get; set; }

    /// <summary>Line colour as <c>#RRGGBB</c>.</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FF7814";
}
