using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// End-to-end proof that a guided tour creates one static simulation session,
/// overlays Plan, Notes, Tasks, Approvals, Inbox, and Loop fixtures on all 6 surfaces,
/// and removes exactly those artifacts on cleanup or restart recovery — without
/// touching real customer data, Git state, or existing simulators.
/// </summary>
[TestFixture]
internal sealed class StaticSimulationLifecycleTests
{
    private SimulationSessionManager _manager = null!;
    private Dictionary<SimulationSurfaceKind, TrackingAdapter> _adapters = null!;

    [SetUp]
    public void SetUp()
    {
        _manager = new SimulationSessionManager();
        _adapters = new Dictionary<SimulationSurfaceKind, TrackingAdapter>();

        foreach (SimulationSurfaceKind kind in Enum.GetValues(typeof(SimulationSurfaceKind)))
        {
            var adapter = new TrackingAdapter(kind);
            _adapters[kind] = adapter;
            _manager.RegisterAdapter(adapter);
        }
    }

    #region End-to-End Lifecycle

    [Test]
    public async Task SingleSession_OverlayAllSixSurfaces_AllTrackedAndCleanedOnEnd()
    {
        var session = _manager.CreateSession("Full Tour", "guide-user");

        // Overlay one artifact on each surface
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Plan, "plan-1", "Demo Plan", "plan-data");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Notes, "notes-1", "Demo Note", "notes-data");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Tasks, "tasks-1", "Demo Task", "tasks-data");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Approvals, "approvals-1", "Demo Approval", "approvals-data");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Inbox, "inbox-1", "Demo Inbox", "inbox-data");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Loop, "loop-1", "Demo Loop", "loop-data");

        // All 6 artifacts are tracked
        var artifacts = _manager.GetArtifactsForSession(session.SessionId);
        Assert.That(artifacts, Has.Count.EqualTo(6));

        // Each adapter has one overlay
        foreach (var (kind, adapter) in _adapters)
        {
            Assert.That(adapter.OverlaidArtifactIds, Has.Count.EqualTo(1),
                $"Adapter for {kind} should have exactly 1 overlaid artifact");
        }

        // End session → cleanup removes all
        await _manager.EndSessionAsync(session.SessionId);

        foreach (var (kind, adapter) in _adapters)
        {
            Assert.That(adapter.RemoveAllSessionIds, Does.Contain(session.SessionId),
                $"RemoveAllForSessionAsync should have been called on {kind} adapter");
            Assert.That(adapter.Contains($"{kind.ToString().ToLowerInvariant()}-1"), Is.False,
                $"Artifact should be removed from {kind} adapter after cleanup");
        }

        Assert.That(_manager.TryGetSession(session.SessionId, out _), Is.False,
            "Session should be fully disposed");
        Assert.That(_manager.GetActiveSessionIds(), Is.Empty);
    }

    [Test]
    public async Task SingleSession_MultipleArtifactsPerSurface_AllCleaned()
    {
        var session = _manager.CreateSession("Dense Tour", "guide-user");

        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Plan, "p1", "Plan A", "d");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Plan, "p2", "Plan B", "d");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Notes, "n1", "Note A", "d");

        var artifacts = _manager.GetArtifactsForSession(session.SessionId);
        Assert.That(artifacts, Has.Count.EqualTo(3));

        await _manager.EndSessionAsync(session.SessionId);

        Assert.That(_adapters[SimulationSurfaceKind.Plan].Contains("p1"), Is.False);
        Assert.That(_adapters[SimulationSurfaceKind.Plan].Contains("p2"), Is.False);
        Assert.That(_adapters[SimulationSurfaceKind.Notes].Contains("n1"), Is.False);
    }

    #endregion

    #region Cross-Session Isolation

    [Test]
    public async Task TwoSessions_EndOne_OtherArtifactsSurvive()
    {
        var session1 = _manager.CreateSession("Tour A", "user-1");
        var session2 = _manager.CreateSession("Tour B", "user-2");

        await _manager.OverlayArtifactAsync(session1.SessionId, SimulationSurfaceKind.Plan, "s1-plan", "S1 Plan", "d");
        await _manager.OverlayArtifactAsync(session1.SessionId, SimulationSurfaceKind.Notes, "s1-notes", "S1 Notes", "d");
        await _manager.OverlayArtifactAsync(session2.SessionId, SimulationSurfaceKind.Plan, "s2-plan", "S2 Plan", "d");
        await _manager.OverlayArtifactAsync(session2.SessionId, SimulationSurfaceKind.Tasks, "s2-tasks", "S2 Tasks", "d");

        // End session 1 only
        await _manager.EndSessionAsync(session1.SessionId);

        // Session 1 artifacts gone
        Assert.That(_manager.TryGetSession(session1.SessionId, out _), Is.False);
        Assert.That(_adapters[SimulationSurfaceKind.Plan].Contains("s1-plan"), Is.False);
        Assert.That(_adapters[SimulationSurfaceKind.Notes].Contains("s1-notes"), Is.False);

        // Session 2 artifacts survive
        Assert.That(_manager.TryGetSession(session2.SessionId, out var s2), Is.True);
        Assert.That(s2!.LifecycleState, Is.EqualTo(SimulationLifecycleState.Active));
        Assert.That(_adapters[SimulationSurfaceKind.Plan].Contains("s2-plan"), Is.True);
        Assert.That(_adapters[SimulationSurfaceKind.Tasks].Contains("s2-tasks"), Is.True);

        var s2Artifacts = _manager.GetArtifactsForSession(session2.SessionId);
        Assert.That(s2Artifacts, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task CrossSessionRemoval_IsBlocked_BySideEffectBarrier()
    {
        var session1 = _manager.CreateSession("Owner A", "user-1");
        var session2 = _manager.CreateSession("Owner B", "user-2");

        await _manager.OverlayArtifactAsync(session1.SessionId, SimulationSurfaceKind.Plan, "owned-by-a", "A's Plan", "d");

        // Session 2 attempting to remove session 1's artifact should throw
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _manager.RemoveArtifactAsync(session2.SessionId, "owned-by-a"));

        // Original artifact remains intact
        Assert.That(_adapters[SimulationSurfaceKind.Plan].Contains("owned-by-a"), Is.True);
    }

    [Test]
    public async Task RealArtifacts_NotTouched_DuringSimCleanup()
    {
        // Add "real" artifacts to multiple adapters
        _adapters[SimulationSurfaceKind.Plan].AddRealArtifact("real-plan-prod");
        _adapters[SimulationSurfaceKind.Notes].AddRealArtifact("real-notes-prod");
        _adapters[SimulationSurfaceKind.Tasks].AddRealArtifact("real-tasks-prod");

        var session = _manager.CreateSession("Tour", "user-1");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Plan, "sim-plan", "Sim", "d");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Notes, "sim-notes", "Sim", "d");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Tasks, "sim-tasks", "Sim", "d");

        await _manager.EndSessionAsync(session.SessionId);

        // All real artifacts remain
        Assert.That(_adapters[SimulationSurfaceKind.Plan].Contains("real-plan-prod"), Is.True);
        Assert.That(_adapters[SimulationSurfaceKind.Notes].Contains("real-notes-prod"), Is.True);
        Assert.That(_adapters[SimulationSurfaceKind.Tasks].Contains("real-tasks-prod"), Is.True);

        // All sim artifacts removed
        Assert.That(_adapters[SimulationSurfaceKind.Plan].Contains("sim-plan"), Is.False);
        Assert.That(_adapters[SimulationSurfaceKind.Notes].Contains("sim-notes"), Is.False);
        Assert.That(_adapters[SimulationSurfaceKind.Tasks].Contains("sim-tasks"), Is.False);
    }

    #endregion

    #region Restart / Orphan Recovery

    [Test]
    public async Task RecoverOrphanedSessions_CleansAllSurfaces()
    {
        var session = _manager.CreateSession("Orphan Tour", "user-1");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Plan, "o-plan", "Plan", "d");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Notes, "o-notes", "Notes", "d");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Tasks, "o-tasks", "Tasks", "d");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Approvals, "o-approvals", "Approvals", "d");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Inbox, "o-inbox", "Inbox", "d");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Loop, "o-loop", "Loop", "d");

        // Simulate restart recovery
        await _manager.RecoverOrphanedSessionsAsync();

        Assert.That(_manager.GetActiveSessionIds(), Is.Empty);
        Assert.That(_manager.TryGetSession(session.SessionId, out _), Is.False);

        foreach (var (kind, adapter) in _adapters)
        {
            Assert.That(adapter.RemoveAllSessionIds, Does.Contain(session.SessionId),
                $"Recovery should have cleaned {kind} adapter");
        }
    }

    [Test]
    public async Task RecoverOrphanedSessions_MultipleOrphans_AllCleaned()
    {
        var s1 = _manager.CreateSession("Orphan 1", "user-1");
        var s2 = _manager.CreateSession("Orphan 2", "user-2");
        var s3 = _manager.CreateSession("Orphan 3", "user-3");

        await _manager.OverlayArtifactAsync(s1.SessionId, SimulationSurfaceKind.Plan, "o1-p", "P1", "d");
        await _manager.OverlayArtifactAsync(s2.SessionId, SimulationSurfaceKind.Notes, "o2-n", "N2", "d");
        await _manager.OverlayArtifactAsync(s3.SessionId, SimulationSurfaceKind.Loop, "o3-l", "L3", "d");

        await _manager.RecoverOrphanedSessionsAsync();

        Assert.That(_manager.GetActiveSessionIds(), Is.Empty);
        Assert.That(_adapters[SimulationSurfaceKind.Plan].Contains("o1-p"), Is.False);
        Assert.That(_adapters[SimulationSurfaceKind.Notes].Contains("o2-n"), Is.False);
        Assert.That(_adapters[SimulationSurfaceKind.Loop].Contains("o3-l"), Is.False);
    }

    [Test]
    public async Task RecoverOrphanedSessions_IsNoOp_WhenNoSessions()
    {
        // No sessions exist — recovery should be a safe no-op
        Assert.DoesNotThrowAsync(async () =>
            await _manager.RecoverOrphanedSessionsAsync());

        Assert.That(_manager.GetActiveSessionIds(), Is.Empty);
    }

    #endregion

    #region LoopSimulationSurfaceAdapter Unit Tests

    [Test]
    [Apartment(ApartmentState.STA)]
    public void LoopAdapter_OverlayAsync_InvokesApplyStateCallback()
    {
        SimulationLoopState? receivedState = null;
        bool clearCalled = false;
        var dispatcher = Dispatcher.CurrentDispatcher;

        var adapter = new LoopSimulationSurfaceAdapter(
            state => receivedState = state,
            () => clearCalled = true,
            dispatcher);

        var loopState = new SimulationLoopState(3, "● Running · Round 3", true, false);
        var artifact = new SimulationArtifact("loop-art-1", "session-1",
            SimulationSurfaceKind.Loop, "Running Loop", DateTimeOffset.UtcNow);

        adapter.OverlayAsync(artifact, loopState).GetAwaiter().GetResult();

        Assert.That(receivedState, Is.Not.Null);
        Assert.That(receivedState!.Iteration, Is.EqualTo(3));
        Assert.That(receivedState.StatusText, Is.EqualTo("● Running · Round 3"));
        Assert.That(receivedState.IsRunning, Is.True);
        Assert.That(receivedState.IsWaiting, Is.False);
        Assert.That(adapter.Contains("loop-art-1"), Is.True);
        Assert.That(adapter.IsSimulationActive, Is.True);
        Assert.That(clearCalled, Is.False);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void LoopAdapter_RemoveAsync_InvokesClearWhenLastRemoved()
    {
        bool clearCalled = false;
        var dispatcher = Dispatcher.CurrentDispatcher;

        var adapter = new LoopSimulationSurfaceAdapter(
            _ => { },
            () => clearCalled = true,
            dispatcher);

        var state = new SimulationLoopState(1, "Status", true, false);
        var art1 = new SimulationArtifact("l1", "s1", SimulationSurfaceKind.Loop, "L1", DateTimeOffset.UtcNow);
        var art2 = new SimulationArtifact("l2", "s1", SimulationSurfaceKind.Loop, "L2", DateTimeOffset.UtcNow);

        adapter.OverlayAsync(art1, state).GetAwaiter().GetResult();
        adapter.OverlayAsync(art2, state).GetAwaiter().GetResult();

        // Remove first — clear should NOT fire (one still remains)
        adapter.RemoveAsync(art1).GetAwaiter().GetResult();
        Assert.That(clearCalled, Is.False);
        Assert.That(adapter.IsSimulationActive, Is.True);

        // Remove last — clear fires
        adapter.RemoveAsync(art2).GetAwaiter().GetResult();
        Assert.That(clearCalled, Is.True);
        Assert.That(adapter.IsSimulationActive, Is.False);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void LoopAdapter_RemoveAllForSession_OnlyRemovesOwnedArtifacts()
    {
        bool clearCalled = false;
        var dispatcher = Dispatcher.CurrentDispatcher;

        var adapter = new LoopSimulationSurfaceAdapter(
            _ => { },
            () => clearCalled = true,
            dispatcher);

        var state = new SimulationLoopState(1, "Status", true, false);
        var art1 = new SimulationArtifact("la", "session-A", SimulationSurfaceKind.Loop, "A", DateTimeOffset.UtcNow);
        var art2 = new SimulationArtifact("lb", "session-B", SimulationSurfaceKind.Loop, "B", DateTimeOffset.UtcNow);

        adapter.OverlayAsync(art1, state).GetAwaiter().GetResult();
        adapter.OverlayAsync(art2, state).GetAwaiter().GetResult();

        // Remove session A — session B remains
        adapter.RemoveAllForSessionAsync("session-A").GetAwaiter().GetResult();
        Assert.That(adapter.Contains("la"), Is.False);
        Assert.That(adapter.Contains("lb"), Is.True);
        Assert.That(adapter.IsSimulationActive, Is.True);
        Assert.That(clearCalled, Is.False);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void LoopAdapter_OverlayAsync_ThrowsOnInvalidFixtureData()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var adapter = new LoopSimulationSurfaceAdapter(_ => { }, () => { }, dispatcher);

        var artifact = new SimulationArtifact("loop-bad", "s1",
            SimulationSurfaceKind.Loop, "Bad", DateTimeOffset.UtcNow);

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await adapter.OverlayAsync(artifact, "not-a-loop-state"));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void LoopAdapter_WaitingState_IsRepresentedCorrectly()
    {
        SimulationLoopState? received = null;
        var dispatcher = Dispatcher.CurrentDispatcher;

        var adapter = new LoopSimulationSurfaceAdapter(
            s => received = s, () => { }, dispatcher);

        var waitingState = SimulationLoopFixtureBuilder.BuildWaitingLoopState();
        var artifact = new SimulationArtifact("loop-wait", "s1",
            SimulationSurfaceKind.Loop, "Waiting", DateTimeOffset.UtcNow);

        adapter.OverlayAsync(artifact, waitingState).GetAwaiter().GetResult();

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.IsWaiting, Is.True);
        Assert.That(received.IsRunning, Is.True);
        Assert.That(received.Iteration, Is.EqualTo(2));
    }

    #endregion

    #region Adapter Registration & Routing

    [Test]
    public void OverlayArtifact_ThrowsWhenNoAdapterRegistered()
    {
        // Create a fresh manager with NO adapters
        var bareManager = new SimulationSessionManager();
        var session = bareManager.CreateSession("No Adapters", "user-1");

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await bareManager.OverlayArtifactAsync(
                session.SessionId, SimulationSurfaceKind.Plan, "x", "X", "data"));
    }

    [Test]
    public async Task ArtifactsRouteToCorrectAdapter()
    {
        var session = _manager.CreateSession("Routing Test", "user-1");

        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Approvals, "a1", "Approval", "d");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Inbox, "i1", "Inbox Msg", "d");

        // Approvals adapter got the approval artifact, not the inbox one
        Assert.That(_adapters[SimulationSurfaceKind.Approvals].OverlaidArtifactIds, Does.Contain("a1"));
        Assert.That(_adapters[SimulationSurfaceKind.Approvals].OverlaidArtifactIds, Does.Not.Contain("i1"));

        // Inbox adapter got the inbox artifact, not the approval one
        Assert.That(_adapters[SimulationSurfaceKind.Inbox].OverlaidArtifactIds, Does.Contain("i1"));
        Assert.That(_adapters[SimulationSurfaceKind.Inbox].OverlaidArtifactIds, Does.Not.Contain("a1"));
    }

    #endregion

    #region Session State Transitions

    [Test]
    public async Task Session_TransitionsToDisposed_AfterEnd()
    {
        var session = _manager.CreateSession("Transition", "user-1");
        Assert.That(session.LifecycleState, Is.EqualTo(SimulationLifecycleState.Active));

        await _manager.EndSessionAsync(session.SessionId);

        // Session no longer accessible (Disposed and removed)
        Assert.That(_manager.TryGetSession(session.SessionId, out _), Is.False);
        Assert.That(_manager.GetActiveSessionIds(), Does.Not.Contain(session.SessionId));
    }

    [Test]
    public async Task EndSession_Idempotent_NoDuplicateCleanupCalls()
    {
        var session = _manager.CreateSession("Idempotent", "user-1");
        await _manager.OverlayArtifactAsync(session.SessionId, SimulationSurfaceKind.Plan, "idem-1", "P", "d");

        await _manager.EndSessionAsync(session.SessionId);
        var firstCallCount = _adapters[SimulationSurfaceKind.Plan].RemoveAllSessionIds.Count;

        // Second call is a no-op
        await _manager.EndSessionAsync(session.SessionId);
        var secondCallCount = _adapters[SimulationSurfaceKind.Plan].RemoveAllSessionIds.Count;

        Assert.That(secondCallCount, Is.EqualTo(firstCallCount),
            "Second EndSessionAsync should not trigger additional adapter cleanup");
    }

    #endregion

    #region Test Infrastructure

    /// <summary>
    /// In-memory adapter test double that tracks all calls per surface kind.
    /// Reusable across all 6 surfaces without requiring WPF dependencies.
    /// </summary>
    private sealed class TrackingAdapter : ISimulationSurfaceAdapter
    {
        private readonly HashSet<string> _overlaid = new(StringComparer.Ordinal);
        private readonly HashSet<string> _realArtifacts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _artifactToSession = new(StringComparer.Ordinal);

        public TrackingAdapter(SimulationSurfaceKind surface) => SupportedSurface = surface;

        public SimulationSurfaceKind SupportedSurface { get; }
        public List<string> OverlaidArtifactIds { get; } = new();
        public List<string> RemovedArtifactIds { get; } = new();
        public List<string> RemoveAllSessionIds { get; } = new();

        public void AddRealArtifact(string artifactId) => _realArtifacts.Add(artifactId);

        public Task OverlayAsync(SimulationArtifact artifact, object fixtureData)
        {
            _overlaid.Add(artifact.ArtifactId);
            _artifactToSession[artifact.ArtifactId] = artifact.SessionId;
            OverlaidArtifactIds.Add(artifact.ArtifactId);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(SimulationArtifact artifact)
        {
            _overlaid.Remove(artifact.ArtifactId);
            _artifactToSession.Remove(artifact.ArtifactId);
            RemovedArtifactIds.Add(artifact.ArtifactId);
            return Task.CompletedTask;
        }

        public Task RemoveAllForSessionAsync(string sessionId)
        {
            RemoveAllSessionIds.Add(sessionId);
            var toRemove = _artifactToSession
                .Where(kvp => string.Equals(kvp.Value, sessionId, StringComparison.Ordinal))
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var id in toRemove)
            {
                _overlaid.Remove(id);
                _artifactToSession.Remove(id);
            }
            return Task.CompletedTask;
        }

        public bool Contains(string artifactId) =>
            _overlaid.Contains(artifactId) || _realArtifacts.Contains(artifactId);
    }

    #endregion
}
