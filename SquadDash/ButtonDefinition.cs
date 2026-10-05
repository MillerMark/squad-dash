using System;
using System.Text.Json.Serialization;

namespace SquadDash;

internal enum ButtonCommandType {
    PowerShell,
    RawCommand
}

/// <summary>Editor-owned fields. Caption and CommandText are stored verbatim.</summary>
internal sealed record ButtonContent(
    [property: JsonRequired] string Caption,
    [property: JsonRequired] string Tooltip,
    [property: JsonRequired] string CommandText,
    [property: JsonRequired] ButtonCommandType CommandType,
    string? WorkingDirectory = null);

/// <summary>ContentRevision changes on edits, not on reorders.</summary>
internal sealed record ButtonDefinition(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] ButtonContent Content,
    [property: JsonRequired] int SortOrder,
    [property: JsonRequired] long ContentRevision);
