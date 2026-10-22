using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash;

[JsonConverter(typeof(ButtonCommandTypeJsonConverter))]
internal enum ButtonCommandType {
    PowerShell = 0,
    RawCommand = 1,
    Url = 2
}

internal sealed class ButtonCommandTypeJsonConverter : JsonConverter<ButtonCommandType> {
    public override ButtonCommandType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numeric))
            return numeric switch {
                0 => ButtonCommandType.PowerShell,
                1 => ButtonCommandType.RawCommand,
                2 => ButtonCommandType.Url,
                _ => throw new JsonException("Unsupported button command type value.")
            };

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Button command type must be a number or string.");

        var value = reader.GetString();
        return value switch {
            "PowerShell" => ButtonCommandType.PowerShell,
            "RawCommand" or "raw-cmd" => ButtonCommandType.RawCommand,
            "Url" or "URL" => ButtonCommandType.Url,
            "0" => ButtonCommandType.PowerShell,
            "1" => ButtonCommandType.RawCommand,
            "2" => ButtonCommandType.Url,
            _ => throw new JsonException("Unsupported button command type value.")
        };
    }

    public override void Write(Utf8JsonWriter writer, ButtonCommandType value, JsonSerializerOptions options) {
        if (!Enum.IsDefined(value))
            throw new JsonException("Unsupported button command type value.");
        writer.WriteNumberValue((int)value);
    }
}

/// <summary>Editor-owned fields. Caption and CommandText are stored verbatim.</summary>
internal sealed record ButtonContent(
    [property: JsonRequired] string Caption,
    [property: JsonRequired] string Tooltip,
    [property: JsonRequired] string CommandText,
    [property: JsonRequired] ButtonCommandType CommandType,
    string? WorkingDirectory = null,
    // Missing in old saved JSON deserializes to false, preserving pre-elevation button behavior.
    bool RunAsAdministrator = false);

/// <summary>ContentRevision changes on edits, not on reorders.</summary>
internal sealed record ButtonDefinition(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] ButtonContent Content,
    [property: JsonRequired] int SortOrder,
    [property: JsonRequired] long ContentRevision);
