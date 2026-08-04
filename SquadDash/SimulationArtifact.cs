using System;

namespace SquadDash;

/// <summary>
/// Immutable identity and provenance record that binds a single simulated artifact
/// to its owning <see cref="SimulationSession"/>.
/// </summary>
internal sealed record SimulationArtifact(
    string ArtifactId,
    string SessionId,
    SimulationSurfaceKind SurfaceKind,
    string DisplayLabel,
    DateTimeOffset CreatedAt);
