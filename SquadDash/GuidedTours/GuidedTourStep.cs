using System.Text.Json.Serialization;

namespace SquadDash.GuidedTours;

internal sealed class GuidedTourStep
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("markdownText")]
    public string MarkdownText { get; set; } = string.Empty;

    /// <summary>The x:Name of the WPF element this step points at.</summary>
    [JsonPropertyName("targetControlId")]
    public string TargetControlId { get; set; } = string.Empty;

    /// <summary>Callout placement: "North", "South", "East", "West", or "Auto".</summary>
    [JsonPropertyName("calloutPlacement")]
    public string CalloutPlacement { get; set; } = "Auto";

    /// <summary>Raw preAction string, e.g. "None", "SaveLayout", "LoadLayout:myLayout".</summary>
    [JsonPropertyName("preAction")]
    public string PreAction { get; set; } = "None";

    [JsonIgnore]
    public GuidedTourPreActionDescriptor ParsedPreAction =>
        GuidedTourPreActionDescriptor.Parse(PreAction);

    [JsonIgnore]
    public global::SquadDash.CalloutPlacement ParsedCalloutPlacement =>
        Enum.TryParse<global::SquadDash.CalloutPlacement>(CalloutPlacement, ignoreCase: true, out var r)
            ? r
            : global::SquadDash.CalloutPlacement.Auto;
}
