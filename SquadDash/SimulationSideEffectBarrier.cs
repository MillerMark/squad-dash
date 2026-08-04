using System;

namespace SquadDash;

/// <summary>
/// Production safety barrier that guards against forbidden side effects during simulation.
/// Each guard method logs the violation via <see cref="SquadDashTrace"/> before throwing
/// <see cref="InvalidOperationException"/>.
/// </summary>
internal sealed class SimulationSideEffectBarrier
{
    private readonly SimulationSession _session;

    internal SimulationSideEffectBarrier(SimulationSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>Whether the owning simulation session is currently active.</summary>
    internal bool IsSimulationActive =>
        _session.LifecycleState == SimulationLifecycleState.Active;

    /// <summary>Throws if AI calls are attempted during simulation.</summary>
    internal void GuardNoAiCalls()
    {
        if (!IsSimulationActive) return;
        const string message = "AI calls are forbidden during simulation.";
        SquadDashTrace.Write("Simulation", $"Guard violation ({_session.SessionId}): {message}");
        throw new InvalidOperationException(message);
    }

    /// <summary>Throws if Git mutation is attempted during simulation.</summary>
    internal void GuardNoGitMutation()
    {
        if (!IsSimulationActive) return;
        const string message = "Git mutation is forbidden during simulation.";
        SquadDashTrace.Write("Simulation", $"Guard violation ({_session.SessionId}): {message}");
        throw new InvalidOperationException(message);
    }

    /// <summary>Throws if real plan execution is attempted during simulation.</summary>
    internal void GuardNoPlanExecution()
    {
        if (!IsSimulationActive) return;
        const string message = "Real plan execution is forbidden during simulation.";
        SquadDashTrace.Write("Simulation", $"Guard violation ({_session.SessionId}): {message}");
        throw new InvalidOperationException(message);
    }

    /// <summary>Throws if external notifications are attempted during simulation.</summary>
    internal void GuardNoExternalNotifications()
    {
        if (!IsSimulationActive) return;
        const string message = "External notifications are forbidden during simulation.";
        SquadDashTrace.Write("Simulation", $"Guard violation ({_session.SessionId}): {message}");
        throw new InvalidOperationException(message);
    }

    /// <summary>
    /// Throws if the caller attempts to clean up artifacts not owned by this session.
    /// </summary>
    internal void GuardCleanupOwnership(string sessionId, string artifactSessionId)
    {
        if (string.Equals(sessionId, artifactSessionId, StringComparison.Ordinal))
            return;

        var message = $"Session '{sessionId}' cannot clean up artifact owned by session '{artifactSessionId}'.";
        SquadDashTrace.Write("Simulation", $"Guard violation ({_session.SessionId}): {message}");
        throw new InvalidOperationException(message);
    }
}
