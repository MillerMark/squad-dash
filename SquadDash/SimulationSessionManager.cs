using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

/// <summary>
/// Central runtime for static simulation sessions. Manages session lifecycle,
/// adapter registration, artifact overlay/removal, and orphan recovery.
/// All mutable operations are serialised via <see cref="SemaphoreSlim"/>.
/// </summary>
internal sealed class SimulationSessionManager
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly Dictionary<string, SimulationSession> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SimulationSideEffectBarrier> _barriers = new(StringComparer.Ordinal);
    private readonly Dictionary<(string SessionId, string ArtifactId), SimulationArtifact> _artifacts = new();
    private readonly Dictionary<SimulationSurfaceKind, ISimulationSurfaceAdapter> _adapters = new();

    /// <summary>Register an adapter for a specific surface kind. Throws on duplicate.</summary>
    internal void RegisterAdapter(ISimulationSurfaceAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        _gate.Wait();
        try
        {
            if (_adapters.ContainsKey(adapter.SupportedSurface))
                throw new InvalidOperationException(
                    $"An adapter for surface '{adapter.SupportedSurface}' is already registered.");

            _adapters[adapter.SupportedSurface] = adapter;
            SquadDashTrace.Write("Simulation",
                $"Adapter registered for surface '{adapter.SupportedSurface}'.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Create a new simulation session with Active state.</summary>
    internal SimulationSession CreateSession(string displayName, string ownerId)
    {
        var sessionId = Guid.NewGuid().ToString("D");
        var session = new SimulationSession(sessionId, displayName, DateTimeOffset.UtcNow,
            SimulationLifecycleState.Active, ownerId);
        var barrier = new SimulationSideEffectBarrier(session);

        _gate.Wait();
        try
        {
            _sessions[sessionId] = session;
            _barriers[sessionId] = barrier;
        }
        finally
        {
            _gate.Release();
        }

        SquadDashTrace.Write("Simulation",
            $"Session created: {sessionId} ('{displayName}', owner='{ownerId}').");
        return session;
    }

    /// <summary>
    /// End a session: transition to CleaningUp, remove all artifacts via adapters,
    /// transition to Disposed, then remove from registry.
    /// Idempotent if already Disposed.
    /// </summary>
    internal async Task EndSessionAsync(string sessionId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return;

            if (session.LifecycleState == SimulationLifecycleState.Disposed)
                return;

            // Transition to CleaningUp
            var cleaningUp = session with { LifecycleState = SimulationLifecycleState.CleaningUp };
            _sessions[sessionId] = cleaningUp;

            SquadDashTrace.Write("Simulation",
                $"Session '{sessionId}' transitioning to CleaningUp.");
        }
        finally
        {
            _gate.Release();
        }

        // Call RemoveAllForSessionAsync on every registered adapter
        foreach (var adapter in _adapters.Values)
        {
            try
            {
                await adapter.RemoveAllForSessionAsync(sessionId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SquadDashTrace.Write("Simulation",
                    $"Adapter cleanup failed for surface '{adapter.SupportedSurface}' " +
                    $"in session '{sessionId}': {ex.Message}");
            }
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Remove artifacts belonging to this session
            var keysToRemove = _artifacts.Keys
                .Where(k => string.Equals(k.SessionId, sessionId, StringComparison.Ordinal))
                .ToList();
            foreach (var key in keysToRemove)
                _artifacts.Remove(key);

            // Transition to Disposed and remove from registry
            if (_sessions.TryGetValue(sessionId, out _))
            {
                _sessions.Remove(sessionId);
                _barriers.Remove(sessionId);
            }

            SquadDashTrace.Write("Simulation",
                $"Session '{sessionId}' disposed and removed from registry.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Overlay a simulation artifact into its target surface.</summary>
    internal async Task OverlayArtifactAsync(
        string sessionId,
        SimulationSurfaceKind surfaceKind,
        string artifactId,
        string displayLabel,
        object fixtureData)
    {
        SimulationArtifact artifact;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                throw new InvalidOperationException(
                    $"Session '{sessionId}' not found.");

            if (session.LifecycleState != SimulationLifecycleState.Active)
                throw new InvalidOperationException(
                    $"Session '{sessionId}' is not Active (state={session.LifecycleState}).");

            artifact = new SimulationArtifact(artifactId, sessionId, surfaceKind,
                displayLabel, DateTimeOffset.UtcNow);
            _artifacts[(sessionId, artifactId)] = artifact;
        }
        finally
        {
            _gate.Release();
        }

        if (!_adapters.TryGetValue(surfaceKind, out var adapter))
            throw new InvalidOperationException(
                $"No adapter registered for surface '{surfaceKind}'.");

        await adapter.OverlayAsync(artifact, fixtureData).ConfigureAwait(false);

        SquadDashTrace.Write("Simulation",
            $"Artifact '{artifactId}' overlaid on '{surfaceKind}' in session '{sessionId}'.");
    }

    /// <summary>Remove a single artifact from its surface. Guards cross-session ownership.</summary>
    internal async Task RemoveArtifactAsync(string sessionId, string artifactId)
    {
        SimulationArtifact artifact;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_artifacts.TryGetValue((sessionId, artifactId), out artifact!))
                throw new InvalidOperationException(
                    $"Artifact '{artifactId}' not found in session '{sessionId}'.");

            if (!_barriers.TryGetValue(sessionId, out var barrier))
                throw new InvalidOperationException(
                    $"No barrier found for session '{sessionId}'.");

            barrier.GuardCleanupOwnership(sessionId, artifact.SessionId);

            _artifacts.Remove((sessionId, artifactId));
        }
        finally
        {
            _gate.Release();
        }

        if (_adapters.TryGetValue(artifact.SurfaceKind, out var adapter))
        {
            await adapter.RemoveAsync(artifact).ConfigureAwait(false);
        }

        SquadDashTrace.Write("Simulation",
            $"Artifact '{artifactId}' removed from '{artifact.SurfaceKind}' in session '{sessionId}'.");
    }

    /// <summary>Try to retrieve a session by ID.</summary>
    internal bool TryGetSession(string sessionId, out SimulationSession? session)
    {
        _gate.Wait();
        try
        {
            return _sessions.TryGetValue(sessionId, out session);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Get all active session IDs.</summary>
    internal IReadOnlyList<string> GetActiveSessionIds()
    {
        _gate.Wait();
        try
        {
            return _sessions
                .Where(kv => kv.Value.LifecycleState == SimulationLifecycleState.Active)
                .Select(kv => kv.Key)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Get all artifacts belonging to a specific session.</summary>
    internal IReadOnlyList<SimulationArtifact> GetArtifactsForSession(string sessionId)
    {
        _gate.Wait();
        try
        {
            return _artifacts
                .Where(kv => string.Equals(kv.Key.SessionId, sessionId, StringComparison.Ordinal))
                .Select(kv => kv.Value)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// On restart, find any sessions still Active or CleaningUp and clean them up.
    /// </summary>
    internal async Task RecoverOrphanedSessionsAsync()
    {
        List<string> orphanIds;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            orphanIds = _sessions
                .Where(kv => kv.Value.LifecycleState is SimulationLifecycleState.Active
                          or SimulationLifecycleState.CleaningUp)
                .Select(kv => kv.Key)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }

        if (orphanIds.Count == 0)
            return;

        SquadDashTrace.Write("Simulation",
            $"Recovering {orphanIds.Count} orphaned simulation session(s).");

        foreach (var id in orphanIds)
        {
            try
            {
                await EndSessionAsync(id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SquadDashTrace.Write("Simulation",
                    $"Orphan recovery failed for session '{id}': {ex.Message}");
            }
        }
    }
}
