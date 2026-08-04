using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SimulationSessionManagerTests
{
    private SimulationSessionManager _manager = null!;
    private TestSimulationSurfaceAdapter _adapter = null!;

    [SetUp]
    public void SetUp()
    {
        _manager = new SimulationSessionManager();
        _adapter = new TestSimulationSurfaceAdapter(SimulationSurfaceKind.Plan);
        _manager.RegisterAdapter(_adapter);
    }

    [Test]
    public void CreateSession_ReturnsActiveSession()
    {
        var session = _manager.CreateSession("Tour 1", "user-42");

        Assert.That(session, Is.Not.Null);
        Assert.That(session.SessionId, Is.Not.Null.And.Not.Empty);
        Assert.That(Guid.TryParse(session.SessionId, out _), Is.True,
            "SessionId should be a valid GUID");
        Assert.That(session.LifecycleState, Is.EqualTo(SimulationLifecycleState.Active));
        Assert.That(session.DisplayName, Is.EqualTo("Tour 1"));
        Assert.That(session.OwnerId, Is.EqualTo("user-42"));
    }

    [Test]
    public async Task EndSession_CleansUpAndDisposes()
    {
        var session = _manager.CreateSession("Tour 2", "user-1");
        await _manager.OverlayArtifactAsync(
            session.SessionId, SimulationSurfaceKind.Plan, "art-1", "Plan A", "data");

        await _manager.EndSessionAsync(session.SessionId);

        Assert.That(_manager.TryGetSession(session.SessionId, out _), Is.False,
            "Disposed session should be removed from registry");
        Assert.That(_adapter.RemoveAllSessionIds, Does.Contain(session.SessionId),
            "Adapter's RemoveAllForSessionAsync should have been called");
    }

    [Test]
    public async Task EndSession_IdempotentOnDisposed()
    {
        var session = _manager.CreateSession("Tour 3", "user-1");
        await _manager.EndSessionAsync(session.SessionId);

        // Second call should be a no-op (no exception)
        Assert.DoesNotThrowAsync(async () =>
            await _manager.EndSessionAsync(session.SessionId));
    }

    [Test]
    public async Task OverlayArtifact_StoresAndCallsAdapter()
    {
        var session = _manager.CreateSession("Tour 4", "user-1");

        await _manager.OverlayArtifactAsync(
            session.SessionId, SimulationSurfaceKind.Plan, "art-2", "My Plan", "fixture");

        var artifacts = _manager.GetArtifactsForSession(session.SessionId);
        Assert.That(artifacts, Has.Count.EqualTo(1));
        Assert.That(artifacts[0].ArtifactId, Is.EqualTo("art-2"));
        Assert.That(artifacts[0].SurfaceKind, Is.EqualTo(SimulationSurfaceKind.Plan));
        Assert.That(artifacts[0].DisplayLabel, Is.EqualTo("My Plan"));

        Assert.That(_adapter.OverlaidArtifactIds, Does.Contain("art-2"),
            "Adapter's OverlayAsync should have been called");
    }

    [Test]
    public void OverlayArtifact_ThrowsForInactiveSession()
    {
        var session = _manager.CreateSession("Tour 5", "user-1");
        _manager.EndSessionAsync(session.SessionId).GetAwaiter().GetResult();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _manager.OverlayArtifactAsync(
                session.SessionId, SimulationSurfaceKind.Plan, "art-3", "X", "data"));
    }

    [Test]
    public async Task RemoveArtifact_CallsAdapterAndRemovesFromLedger()
    {
        var session = _manager.CreateSession("Tour 6", "user-1");
        await _manager.OverlayArtifactAsync(
            session.SessionId, SimulationSurfaceKind.Plan, "art-4", "Plan B", "data");

        await _manager.RemoveArtifactAsync(session.SessionId, "art-4");

        var artifacts = _manager.GetArtifactsForSession(session.SessionId);
        Assert.That(artifacts, Is.Empty);
        Assert.That(_adapter.RemovedArtifactIds, Does.Contain("art-4"),
            "Adapter's RemoveAsync should have been called");
    }

    [Test]
    public async Task RemoveArtifact_RejectsCrossSessionCleanup()
    {
        var session1 = _manager.CreateSession("Session A", "user-1");
        var session2 = _manager.CreateSession("Session B", "user-2");
        await _manager.OverlayArtifactAsync(
            session1.SessionId, SimulationSurfaceKind.Plan, "art-5", "Owned by A", "data");

        // Session 2 tries to remove session 1's artifact — should throw
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _manager.RemoveArtifactAsync(session2.SessionId, "art-5"));
    }

    [Test]
    public void RegisterAdapter_ThrowsOnDuplicate()
    {
        var duplicate = new TestSimulationSurfaceAdapter(SimulationSurfaceKind.Plan);

        Assert.Throws<InvalidOperationException>(() =>
            _manager.RegisterAdapter(duplicate));
    }

    [Test]
    public async Task GetArtifactsForSession_ReturnsOnlyOwnedArtifacts()
    {
        var notesAdapter = new TestSimulationSurfaceAdapter(SimulationSurfaceKind.Notes);
        _manager.RegisterAdapter(notesAdapter);

        var session1 = _manager.CreateSession("Session X", "user-1");
        var session2 = _manager.CreateSession("Session Y", "user-2");

        await _manager.OverlayArtifactAsync(
            session1.SessionId, SimulationSurfaceKind.Plan, "art-s1", "S1 Plan", "data");
        await _manager.OverlayArtifactAsync(
            session2.SessionId, SimulationSurfaceKind.Notes, "art-s2", "S2 Notes", "data");

        var s1Artifacts = _manager.GetArtifactsForSession(session1.SessionId);
        var s2Artifacts = _manager.GetArtifactsForSession(session2.SessionId);

        Assert.That(s1Artifacts, Has.Count.EqualTo(1));
        Assert.That(s1Artifacts[0].ArtifactId, Is.EqualTo("art-s1"));

        Assert.That(s2Artifacts, Has.Count.EqualTo(1));
        Assert.That(s2Artifacts[0].ArtifactId, Is.EqualTo("art-s2"));
    }

    [Test]
    public async Task RecoverOrphanedSessions_CleansUpActiveSessions()
    {
        var session1 = _manager.CreateSession("Orphan 1", "user-1");
        var session2 = _manager.CreateSession("Orphan 2", "user-1");
        await _manager.OverlayArtifactAsync(
            session1.SessionId, SimulationSurfaceKind.Plan, "art-o1", "Plan", "data");

        // Simulate restart: sessions are still Active → recovery should clean them up
        await _manager.RecoverOrphanedSessionsAsync();

        Assert.That(_manager.GetActiveSessionIds(), Is.Empty,
            "All orphaned sessions should be cleaned up");
        Assert.That(_manager.TryGetSession(session1.SessionId, out _), Is.False);
        Assert.That(_manager.TryGetSession(session2.SessionId, out _), Is.False);
    }

    [Test]
    public async Task RealArtifactsRemainAfterSimCleanup()
    {
        // Pre-populate the adapter with a "real" artifact
        _adapter.AddRealArtifact("real-plan-001");

        var session = _manager.CreateSession("Cleanup Test", "user-1");
        await _manager.OverlayArtifactAsync(
            session.SessionId, SimulationSurfaceKind.Plan, "sim-plan-001", "Sim Plan", "data");

        // Verify both exist before cleanup
        Assert.That(_adapter.Contains("real-plan-001"), Is.True);
        Assert.That(_adapter.Contains("sim-plan-001"), Is.True);

        // End session → cleanup removes only sim artifacts
        await _manager.EndSessionAsync(session.SessionId);

        Assert.That(_adapter.Contains("real-plan-001"), Is.True,
            "Real artifact must remain after simulation cleanup");
        Assert.That(_adapter.Contains("sim-plan-001"), Is.False,
            "Simulated artifact should be removed after cleanup");
    }

    /// <summary>
    /// In-memory test double for <see cref="ISimulationSurfaceAdapter"/>.
    /// Tracks overlay, remove, and removeAll calls for assertion.
    /// </summary>
    private sealed class TestSimulationSurfaceAdapter : ISimulationSurfaceAdapter
    {
        private readonly HashSet<string> _overlaid = new(StringComparer.Ordinal);
        private readonly HashSet<string> _realArtifacts = new(StringComparer.Ordinal);

        public TestSimulationSurfaceAdapter(SimulationSurfaceKind surface)
        {
            SupportedSurface = surface;
        }

        public SimulationSurfaceKind SupportedSurface { get; }

        public List<string> OverlaidArtifactIds { get; } = new();
        public List<string> RemovedArtifactIds { get; } = new();
        public List<string> RemoveAllSessionIds { get; } = new();

        /// <summary>Add a "real" artifact that is not simulation-owned.</summary>
        public void AddRealArtifact(string artifactId) => _realArtifacts.Add(artifactId);

        public Task OverlayAsync(SimulationArtifact artifact, object fixtureData)
        {
            _overlaid.Add(artifact.ArtifactId);
            OverlaidArtifactIds.Add(artifact.ArtifactId);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(SimulationArtifact artifact)
        {
            _overlaid.Remove(artifact.ArtifactId);
            RemovedArtifactIds.Add(artifact.ArtifactId);
            return Task.CompletedTask;
        }

        public Task RemoveAllForSessionAsync(string sessionId)
        {
            RemoveAllSessionIds.Add(sessionId);
            // Remove only sim-overlaid artifacts, keep real ones
            var simIds = _overlaid.ToList();
            foreach (var id in simIds)
                _overlaid.Remove(id);
            return Task.CompletedTask;
        }

        public bool Contains(string artifactId) =>
            _overlaid.Contains(artifactId) || _realArtifacts.Contains(artifactId);
    }
}
