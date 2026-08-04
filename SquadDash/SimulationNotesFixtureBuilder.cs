using System;

namespace SquadDash;

/// <summary>
/// Builds static NoteItem fixtures for guided-tour demonstrations.
/// The notes are display-only: they showcase the Notes panel UI without persisting to disk.
/// </summary>
internal static class SimulationNotesFixtureBuilder
{
    /// <summary>
    /// Builds a welcome note introducing SquadDash Notes.
    /// </summary>
    internal static NoteItem BuildWelcomeNote() => new(
        Id: Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
        Title: "Welcome to SquadDash Notes",
        CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeMilliseconds(),
        Scope: DataScope.Local);

    /// <summary>
    /// Builds a note about architecture decisions.
    /// </summary>
    internal static NoteItem BuildArchitectureNote() => new(
        Id: Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901"),
        Title: "Architecture Decisions",
        CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-15).ToUnixTimeMilliseconds(),
        Scope: DataScope.Local);
}
