namespace SquadDash;

using System;

/// <summary>Metadata for a workspace note. Content is stored separately in a .md file.</summary>
internal sealed record NoteItem(
    Guid   Id,
    string Title,
    long   CreatedAt);
