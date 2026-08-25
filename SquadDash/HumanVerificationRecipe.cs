using System.Text.Json.Serialization;

namespace SquadDash;

/// <summary>Human-facing instructions for reproducing and safely removing review state.</summary>
internal sealed record HumanVerificationRecipe(
    [property: JsonPropertyName("setup")] string Setup,
    [property: JsonPropertyName("open")] string Open,
    [property: JsonPropertyName("observe")] string Observe,
    [property: JsonPropertyName("cleanup")] string Cleanup);

internal static class HumanVerificationAccessMode
{
    internal const string DirectlyReachable = "directly-reachable";
    internal const string Recipe = "recipe";
    internal const string Fixture = "fixture";
}
