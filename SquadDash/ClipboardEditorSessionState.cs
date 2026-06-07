using System;
using System.Text.Json.Serialization;

namespace SquadDash;

internal sealed class ClipboardEditorSessionState {
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("editorId")]
    public string EditorId { get; set; } = "";

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("workspaceFolder")]
    public string? WorkspaceFolder { get; set; }

    [JsonPropertyName("windowGeometry")]
    public WindowGeometryState? WindowGeometry { get; set; }

    [JsonPropertyName("attachedPrompt")]
    public AttachedPromptInfo? AttachedPrompt { get; set; }

    [JsonPropertyName("toolState")]
    public ToolStateInfo? ToolState { get; set; }

    [JsonPropertyName("annotationState")]
    public ClipboardAnnotationState? AnnotationState { get; set; }

    [JsonPropertyName("imageMetadata")]
    public ImageMetadataInfo? ImageMetadata { get; set; }

    [JsonPropertyName("savedAt")]
    public string? SavedAt { get; set; }

    [JsonPropertyName("appVersion")]
    public string? AppVersion { get; set; }

    /// <summary>Throws <see cref="InvalidOperationException"/> if required fields are missing or invalid.</summary>
    public void Validate() {
        if (string.IsNullOrWhiteSpace(EditorId))
            throw new InvalidOperationException("ClipboardEditorSessionState: EditorId is required.");
        if (Version < 1)
            throw new InvalidOperationException($"ClipboardEditorSessionState: unsupported Version {Version}.");
    }
}

internal sealed class WindowGeometryState {
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("isMaximized")]
    public bool IsMaximized { get; set; }
}

internal sealed class AttachedPromptInfo {
    /// <summary>"draft" or "queued"</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("draftId")]
    public string? DraftId { get; set; }

    [JsonPropertyName("queueIndex")]
    public int? QueueIndex { get; set; }

    [JsonPropertyName("promptText")]
    public string? PromptText { get; set; }
}

internal sealed class ToolStateInfo {
    [JsonPropertyName("selectedTool")]
    public string? SelectedTool { get; set; }

    [JsonPropertyName("zoomLevel")]
    public double ZoomLevel { get; set; } = 1.0;
}

internal sealed class ImageMetadataInfo {
    [JsonPropertyName("sourceImagePath")]
    public string? SourceImagePath { get; set; }

    [JsonPropertyName("originalImageHash")]
    public string? OriginalImageHash { get; set; }

    [JsonPropertyName("originalImageWidth")]
    public int OriginalImageWidth { get; set; }

    [JsonPropertyName("originalImageHeight")]
    public int OriginalImageHeight { get; set; }
}
