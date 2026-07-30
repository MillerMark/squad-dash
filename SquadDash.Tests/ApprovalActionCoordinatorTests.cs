using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ApprovalActionCoordinatorTests
{
    private ApprovalActionCoordinator _coordinator = null!;

    [SetUp]
    public void SetUp()
    {
        _coordinator = new ApprovalActionCoordinator();
    }

    [TearDown]
    public void TearDown()
    {
        _coordinator.ClearAll();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Plan MakePlan(
        string planId = "PLAN-001",
        string revision = "rev1",
        string lifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
        IReadOnlyList<PlanApprovalGate>? gates = null,
        IReadOnlyList<PlanTask>? tasks = null)
    {
        tasks ??=
        [
            new PlanTask("T1", "Task 1", "Desc", [], "high", PlanTaskStatus.Complete),
            new PlanTask("T2", "Task 2", "Desc", ["T1"], "high", PlanTaskStatus.Pending),
            new PlanTask("T3", "Task 3", "Desc", ["T1"], "high", PlanTaskStatus.Pending),
        ];
        gates ??=
        [
            new PlanApprovalGate("GATE-001", "Review after T1", ["T1"], ["T2", "T3"], PlanGateStatus.AwaitingApproval),
        ];
        return new Plan(
            planId, revision, PlanSource.DecomposeDecision,
            lifecycleStatus, "Test Plan", "main", "Summary",
            tasks, gates,
            new PlanProgress(1, 3),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    // ── Registration tests ───────────────────────────────────────────────────

    [Test]
    public async Task Register_ReturnsTokenWithCorrectFields()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001"]);

        Assert.That(token.PlanId, Is.EqualTo("PLAN-001"));
        Assert.That(token.PlanRevision, Is.EqualTo("rev1"));
        Assert.That(token.RequestVersion, Is.EqualTo(1));
        Assert.That(token.GateIds, Is.EqualTo(new[] { "GATE-001" }));
    }

    [Test]
    public async Task Register_UpdateExisting_IncrementsVersion()
    {
        await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001"]);
        var token2 = await _coordinator.RegisterAsync("PLAN-001", "rev2", ["GATE-001", "GATE-002"]);

        Assert.That(token2.PlanRevision, Is.EqualTo("rev2"));
        Assert.That(token2.RequestVersion, Is.EqualTo(2));
        Assert.That(token2.GateIds, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetCurrentToken_ReturnsNull_WhenNotRegistered()
    {
        var token = _coordinator.GetCurrentToken("PLAN-UNKNOWN");
        Assert.That(token, Is.Null);
    }

    [Test]
    public async Task GetCurrentToken_ReturnsCurrentState()
    {
        var registered = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001"]);
        var current = _coordinator.GetCurrentToken("PLAN-001");

        Assert.That(current, Is.Not.Null);
        Assert.That(registered.Matches(current!), Is.True);
    }

    // ── Stale-click rejection ────────────────────────────────────────────────

    [Test]
    public async Task TryApprove_RejectsStale_WhenRevisionChanged()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001"]);

        // Simulate plan revision change
        await _coordinator.RegisterAsync("PLAN-001", "rev2", ["GATE-001"]);

        var result = await _coordinator.TryApproveAsync(token, ["GATE-001"]);

        Assert.That(result, Is.EqualTo(ApprovalClickResult.StaleRejected));
    }

    [Test]
    public async Task TryApprove_RejectsStale_WhenVersionChanged()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001"]);

        // Simulate a new gate arrival (increments version)
        await _coordinator.AppendGateAsync("PLAN-001", "rev1", "GATE-002");

        var result = await _coordinator.TryApproveAsync(token, ["GATE-001"]);

        Assert.That(result, Is.EqualTo(ApprovalClickResult.StaleRejected));
    }

    [Test]
    public async Task TryApprove_RejectsStale_WhenGateIdsChanged()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001", "GATE-002"]);

        // Simulate update that changes gate set
        await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001"]);

        var result = await _coordinator.TryApproveAsync(token, ["GATE-001"]);

        Assert.That(result, Is.EqualTo(ApprovalClickResult.StaleRejected));
    }

    [Test]
    public async Task TryApprove_RejectsStale_WhenPlanNotRegistered()
    {
        var token = new ApprovalClickToken("PLAN-UNKNOWN", "rev1", 1, ["GATE-001"]);

        var result = await _coordinator.TryApproveAsync(token, ["GATE-001"]);

        Assert.That(result, Is.EqualTo(ApprovalClickResult.StaleRejected));
    }

    // ── Successful approval ──────────────────────────────────────────────────

    [Test]
    public async Task TryApprove_Succeeds_WhenTokenMatches()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001"]);

        var result = await _coordinator.TryApproveAsync(token, ["GATE-001"], "LGTM");

        Assert.That(result, Is.EqualTo(ApprovalClickResult.Approved));
    }

    [Test]
    public async Task TryApprove_ResolvesGate_RemovesFromActive()
    {
        await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001", "GATE-002"]);
        var token = _coordinator.GetCurrentToken("PLAN-001")!;

        await _coordinator.TryApproveAsync(token, ["GATE-001"]);

        var activeGates = _coordinator.GetActiveGateIds("PLAN-001");
        Assert.That(activeGates, Is.EqualTo(new[] { "GATE-002" }));
    }

    [Test]
    public async Task TryApprove_AllGates_FullyResolved()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001"]);

        var result = await _coordinator.TryApproveAsync(token, ["GATE-001"]);

        Assert.That(result, Is.EqualTo(ApprovalClickResult.Approved));
        Assert.That(_coordinator.HasActiveGates("PLAN-001"), Is.False);
    }

    // ── Cross-surface invalidation ───────────────────────────────────────────

    [Test]
    public async Task TryApprove_RaisesResolvedEvent()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001"]);

        ApprovalResolvedEventArgs? receivedArgs = null;
        _coordinator.ApprovalResolved += (_, args) => receivedArgs = args;

        await _coordinator.TryApproveAsync(token, ["GATE-001"], "Looks good");

        Assert.That(receivedArgs, Is.Not.Null);
        Assert.That(receivedArgs!.PlanId, Is.EqualTo("PLAN-001"));
        Assert.That(receivedArgs.ResolvedGateIds, Is.EqualTo(new[] { "GATE-001" }));
        Assert.That(receivedArgs.AllGatesResolved, Is.True);
        Assert.That(receivedArgs.ResolutionNote, Is.EqualTo("Looks good"));
    }

    [Test]
    public async Task TryApprove_PartialResolve_EventShowsNotAllResolved()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001", "GATE-002"]);

        ApprovalResolvedEventArgs? receivedArgs = null;
        _coordinator.ApprovalResolved += (_, args) => receivedArgs = args;

        await _coordinator.TryApproveAsync(token, ["GATE-001"]);

        Assert.That(receivedArgs, Is.Not.Null);
        Assert.That(receivedArgs!.AllGatesResolved, Is.False);
    }

    [Test]
    public async Task TryApprove_AlreadyResolved_ReturnsAlreadyResolved()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001"]);

        // First approval succeeds
        await _coordinator.TryApproveAsync(token, ["GATE-001"]);

        // Re-register so we have a valid token, then try resolving same gate
        var token2 = await _coordinator.RegisterAsync("PLAN-001", "rev1", []);
        var result = await _coordinator.TryApproveAsync(token2, ["GATE-001"]);

        Assert.That(result, Is.EqualTo(ApprovalClickResult.AlreadyResolved));
    }

    // ── Concurrent gate arrival ──────────────────────────────────────────────

    [Test]
    public async Task AppendGate_RestoresActivePlan()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001"]);
        await _coordinator.TryApproveAsync(token, ["GATE-001"]);

        Assert.That(_coordinator.HasActiveGates("PLAN-001"), Is.False);

        // Concurrent gate arrival
        await _coordinator.AppendGateAsync("PLAN-001", "rev1", "GATE-002");

        Assert.That(_coordinator.HasActiveGates("PLAN-001"), Is.True);
        Assert.That(_coordinator.GetActiveGateIds("PLAN-001"), Is.EqualTo(new[] { "GATE-002" }));
    }

    [Test]
    public async Task AppendGate_RaisesRefreshEvent()
    {
        await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001"]);

        string? refreshedPlanId = null;
        _coordinator.ApprovalRefreshNeeded += (_, planId) => refreshedPlanId = planId;

        await _coordinator.AppendGateAsync("PLAN-001", "rev1", "GATE-002");

        Assert.That(refreshedPlanId, Is.EqualTo("PLAN-001"));
    }

    [Test]
    public async Task AppendGate_Idempotent_WhenGateAlreadyActive()
    {
        await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001"]);

        string? refreshedPlanId = null;
        _coordinator.ApprovalRefreshNeeded += (_, planId) => refreshedPlanId = planId;

        // Append same gate — should be no-op
        await _coordinator.AppendGateAsync("PLAN-001", "rev1", "GATE-001");

        // Refresh event is NOT raised for idempotent append
        Assert.That(refreshedPlanId, Is.Null);
        Assert.That(_coordinator.GetActiveGateIds("PLAN-001"), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AppendGate_CreatesNewState_WhenNotRegistered()
    {
        await _coordinator.AppendGateAsync("PLAN-NEW", "rev1", "GATE-001");

        Assert.That(_coordinator.HasActiveGates("PLAN-NEW"), Is.True);
        Assert.That(_coordinator.GetActiveGateIds("PLAN-NEW"), Is.EqualTo(new[] { "GATE-001" }));
    }

    // ── Unregister ───────────────────────────────────────────────────────────

    [Test]
    public async Task Unregister_RemovesAllState()
    {
        await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001"]);
        _coordinator.Unregister("PLAN-001");

        Assert.That(_coordinator.GetCurrentToken("PLAN-001"), Is.Null);
        Assert.That(_coordinator.HasActiveGates("PLAN-001"), Is.False);
    }

    // ── Serialization under concurrent access ────────────────────────────────

    [Test]
    public async Task ConcurrentApprovals_OnlyOneSucceeds()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001"]);

        var results = await Task.WhenAll(
            _coordinator.TryApproveAsync(token, ["GATE-001"]),
            _coordinator.TryApproveAsync(token, ["GATE-001"]));

        // One should succeed, one should be stale or already-resolved
        Assert.That(results.Count(r => r == ApprovalClickResult.Approved), Is.EqualTo(1));
        Assert.That(results.Count(r => r != ApprovalClickResult.Approved), Is.EqualTo(1));
    }

    // ── Token matching ───────────────────────────────────────────────────────

    [Test]
    public void ClickToken_Matches_IdenticalToken()
    {
        var t1 = new ApprovalClickToken("P1", "rev1", 1, ["G1", "G2"]);
        var t2 = new ApprovalClickToken("P1", "rev1", 1, ["G1", "G2"]);

        Assert.That(t1.Matches(t2), Is.True);
    }

    [Test]
    public void ClickToken_DoesNotMatch_DifferentRevision()
    {
        var t1 = new ApprovalClickToken("P1", "rev1", 1, ["G1"]);
        var t2 = new ApprovalClickToken("P1", "rev2", 1, ["G1"]);

        Assert.That(t1.Matches(t2), Is.False);
    }

    [Test]
    public void ClickToken_DoesNotMatch_DifferentVersion()
    {
        var t1 = new ApprovalClickToken("P1", "rev1", 1, ["G1"]);
        var t2 = new ApprovalClickToken("P1", "rev1", 2, ["G1"]);

        Assert.That(t1.Matches(t2), Is.False);
    }

    [Test]
    public void ClickToken_DoesNotMatch_DifferentGateIds()
    {
        var t1 = new ApprovalClickToken("P1", "rev1", 1, ["G1"]);
        var t2 = new ApprovalClickToken("P1", "rev1", 1, ["G1", "G2"]);

        Assert.That(t1.Matches(t2), Is.False);
    }

    [Test]
    public void ClickToken_DoesNotMatch_DifferentGateOrder()
    {
        var t1 = new ApprovalClickToken("P1", "rev1", 1, ["G1", "G2"]);
        var t2 = new ApprovalClickToken("P1", "rev1", 1, ["G2", "G1"]);

        Assert.That(t1.Matches(t2), Is.False);
    }

    // ── Resolution lifecycle ─────────────────────────────────────────────────

    [Test]
    public async Task FullLifecycle_RegisterApproveUnregister()
    {
        // Register
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-001", "GATE-002"]);

        // Approve first gate
        var r1 = await _coordinator.TryApproveAsync(token, ["GATE-001"]);
        Assert.That(r1, Is.EqualTo(ApprovalClickResult.Approved));
        Assert.That(_coordinator.HasActiveGates("PLAN-001"), Is.True);

        // Get fresh token and approve second
        var token2 = _coordinator.GetCurrentToken("PLAN-001")!;
        var r2 = await _coordinator.TryApproveAsync(token2, ["GATE-002"]);
        Assert.That(r2, Is.EqualTo(ApprovalClickResult.Approved));
        Assert.That(_coordinator.HasActiveGates("PLAN-001"), Is.False);

        // Unregister
        _coordinator.Unregister("PLAN-001");
        Assert.That(_coordinator.GetCurrentToken("PLAN-001"), Is.Null);
    }
}
