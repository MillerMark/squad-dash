using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SquadDash.GuidedTours;

internal sealed class GuidedTour
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("steps")]
    public List<GuidedTourStep> Steps { get; set; } = new();
}
