#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash.PanelDocking;

/// <summary>
/// Identifies where a panel is docked within the main window.
/// </summary>
/// <remarks>
/// Top is the default zone — the existing horizontal panel strip (Row 2 of MainGrid).
/// Left and Right are vertical stacks added on either side of the main content area.
/// Future zones (Left2, Right2, …) can be appended without breaking existing serialized layouts,
/// provided unknown values are gracefully handled as Top during deserialization.
/// </remarks>
[JsonConverter(typeof(DockZoneJsonConverter))]
public enum DockZone
{
    Top,
    Left,
    Right,
    Left2,
    Right2,
    Left3,
    Right3,
    Left4,
    Right4,
    Left5,
    Right5,
    Left6,
    Right6,
    Left7,
    Right7
    // Future: Bottom, …
}

/// <summary>
/// Tolerant JSON converter for <see cref="DockZone"/>: falls back to
/// <see cref="DockZone.Top"/> for any unrecognised string instead of throwing.
/// </summary>
public class DockZoneJsonConverter : JsonConverter<DockZone>
{
    public override DockZone Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return Enum.TryParse<DockZone>(value, ignoreCase: true, out var zone) ? zone : DockZone.Top;
    }

    public override void Write(Utf8JsonWriter writer, DockZone value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
