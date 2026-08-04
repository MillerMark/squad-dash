using System;

namespace SquadDash;

/// <summary>
/// Immutable identity and lifecycle snapshot for a single simulation session.
/// Each session owns zero or more <see cref="SimulationArtifact"/> instances and
/// is tracked by the simulation runtime from <see cref="SimulationLifecycleState.Active"/>
/// through <see cref="SimulationLifecycleState.Disposed"/>.
/// </summary>
internal sealed record SimulationSession(
    string SessionId,
    string DisplayName,
    DateTimeOffset CreatedAt,
    SimulationLifecycleState LifecycleState,
    string OwnerId);
