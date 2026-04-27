using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash.Screenshots;

/// <summary>
/// An immutable data bag that carries key-value state used to populate the
/// application before a screenshot is captured.
/// </summary>
/// <remarks>
/// <para>
/// Fixtures are resolved before <see cref="IReplayableUiAction.ExecuteAsync"/> is
/// called: the capture pipeline calls
/// <see cref="FixtureLoaderRegistry.ApplyAllAsync"/> with the fixture, then
/// executes the action, captures, and calls
/// <see cref="FixtureLoaderRegistry.RestoreAllAsync"/> in a <c>finally</c> block.
/// </para>
/// <para>
/// The <see cref="Data"/> bag is intentionally untyped (<c>JsonElement</c> values)
/// so that fixture definitions can be authored and stored as plain JSON without any
/// generated code or shared type contracts.  Each <see cref="IFixtureLoader"/>
/// implementation declares the keys it understands via
/// <see cref="IFixtureLoader.KnownKeys"/>; unknown keys are silently ignored by
/// convention.  Typed fixture interfaces and brittleness-detection tests are deferred
/// to a later phase.
/// </para>
/// </remarks>
/// <param name="FixtureId">
///   Stable, kebab-case identifier that matches the name of the fixture definition.
///   Example: <c>"agent-card-selected"</c>.
/// </param>
/// <param name="Data">
///   Generic key-value bag.  Values are <see cref="JsonElement"/> so that any
///   JSON-representable structure can be carried without a typed schema.
/// </param>
public record ScreenshotFixture(
    [property: JsonPropertyName("fixtureId")] string                              FixtureId,
    [property: JsonPropertyName("data")]      IReadOnlyDictionary<string, JsonElement> Data
)
{
    /// <summary>
    /// A fixture with an empty <see cref="Data"/> bag.
    /// Use when no state population is required before capture.
    /// </summary>
    public static readonly ScreenshotFixture Empty =
        new(string.Empty, new Dictionary<string, JsonElement>());
}
