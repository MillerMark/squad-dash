using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Extended integration tests for the approval failure matrix.
/// Covers scenarios not exercised by the base <see cref="ApprovalIntegrationTests"/>:
/// evidence refresh, partial multi-gate resolution, lifecycle state transitions,
/// coordinator-manager synchronization races, and PlanStoreUpdater gate lifecycle.
/// </summary>
[TestFixture]
internal sealed class ApprovalIntegrationMatrixTests
{
    private string _tempDir = null!;
    private InboxStore _inbox = null!;
    private ApprovalActionCoordinator _coordinator = null!;
    private DurableApprovalRequestManager _durableManager = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"squad-matrix-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _inbox = new InboxStore(_tempDir);
        _coordinator = new ApprovalActionCoordinator();
        _durableManager = new DurableApprovalRequestManager(_inbox);
    }

    [TearDown]
    public void TearDown()
    {
        _coordinator.ClearAll();
        _durableManager.ClearLocks();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Plan MakePlan(
        string planId = "PLAN-001",
        string revision = "rev1",
        string t1Status = PlanTaskStatus.Pending,
        string t2Status = PlanTaskStatus.Pending,
        string t3Status = PlanTaskStatus.Pending,
        string t4Status = PlanTaskStatus.Pending,
        string t5Status = PlanTaskStatus.Pending,
        string gateAStatus = PlanGateStatus.Pending,
        string? t1Commit = null,
        string? t2Commit = null,
        IReadOnlyList<PlanApprovalGate>? extraGates = null,
        string lifecycleStatus = PlanLifecycleStatus.Executing)
    {
        var tasks = new List<PlanTask>
        {
            new("T1", "Task 1", "desc", [], "high", t1Status, Commit: t1Commit,
                CompletedAt: t1Status == PlanTaskStatus.Complete ? DateTimeOffset.UtcNow.AddMinutes(-10) : null),
            new("T2", "Task 2", "desc", [], "high", t2Status, Commit: t2Commit,
                CompletedAt: t2Status == PlanTaskStatus.Complete ? DateTimeOffset.UtcNow.AddMinutes(-5) : null),
            new("T3", "Task 3", "desc", ["T1", "T2"], "high", t3Status),
            new("T4", "Task 4", "desc", ["T3"], "high", t4Status),
            new("T5", "Task 5", "desc", ["T1"], "mid", t5Status),
        };
        var gates = new List<PlanApprovalGate>
        {
            new("GATE-A", "Review T1+T2 before T3", ["T1", "T2"], ["T3"], gateAStatus),
        };
        if (extraGates is not null)
            gates.AddRange(extraGates);

        var completed = tasks.Count(t => t.Status == PlanTaskStatus.Complete);
        return new Plan(
            planId, revision, PlanSource.DecomposeDecision,
            lifecycleStatus, "Integration Test Plan", "main", "Summary",
            tasks, gates,
            new PlanProgress(completed, tasks.Count),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    private static ApprovalReviewSnapshot MakeSnapshot(
        string planId = "PLAN-001",
        string gateId = "GATE-A",
        int completedTaskCount = 2,
        int totalTaskCount = 5) =>
        new(planId, "Integration Test Plan", completedTaskCount, totalTaskCount,
            PlanLifecycleStatus.Executing,
            gateId, "Review T1+T2 before T3", ["T1", "T2"], ["T3"],
            [], [], [], [], DateTimeOffset.UtcNow);

    // ═══════════════════════════════════════════════════════════════════════
    // RefreshEvidenceAsync — evidence update without gate changes
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task RefreshEvidence_UpdatesBodyWithoutChangingGateState()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var snap = MakeSnapshot();

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snap);

        // Progress changes — more tasks complete
        var updatedPlan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            t5Status: PlanTaskStatus.Complete);
        var updatedSnap = MakeSnapshot(completedTaskCount: 3);

        await _durableManager.RefreshEvidenceAsync(updatedPlan, updatedSnap);

        var state = _durableManager.GetState("PLAN-001");
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.ActiveGateIds, Does.Contain("GATE-A"),
            "Gate state must not change during evidence refresh");
        Assert.That(state.Archived, Is.False);

        var msg = _inbox.GetById("approval-gate-PLAN-001");
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg!.Body, Does.Contain("3/5 tasks"),
            "Body should reflect updated progress");
    }

    [Test]
    public async Task RefreshEvidence_NoOpWhenMessageDoesNotExist()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete);
        var snap = MakeSnapshot();

        // No AppendCheckpointAsync called first
        await _durableManager.RefreshEvidenceAsync(plan, snap);

        var state = _durableManager.GetState("PLAN-001");
        Assert.That(state, Is.Null, "Refresh on nonexistent message must be a no-op");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Partial multi-gate approval — approve subset, get fresh token, approve rest
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task PartialMultiGateApproval_FreshTokenApprovesRemainingGates()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1",
            ["GATE-A", "GATE-B", "GATE-C"]);

        // Approve only GATE-A
        var result = await _coordinator.TryApproveAsync(token, ["GATE-A"]);
        Assert.That(result, Is.EqualTo(ApprovalClickResult.Approved));

        // Get fresh token — should reflect reduced active gates
        var freshToken = _coordinator.GetCurrentToken("PLAN-001")!;
        Assert.That(freshToken.GateIds, Does.Not.Contain("GATE-A"),
            "GATE-A should be removed from active gates after approval");
        Assert.That(freshToken.GateIds, Does.Contain("GATE-B"));
        Assert.That(freshToken.GateIds, Does.Contain("GATE-C"));

        // Approve remaining with fresh token
        var result2 = await _coordinator.TryApproveAsync(freshToken, ["GATE-B", "GATE-C"]);
        Assert.That(result2, Is.EqualTo(ApprovalClickResult.Approved));
        Assert.That(_coordinator.HasActiveGates("PLAN-001"), Is.False,
            "All gates should be resolved after approving remaining");
    }

    [Test]
    public async Task PartialApproval_OldTokenCannotApproveRemaining()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A", "GATE-B"]);

        // Approve GATE-A — this bumps version
        await _coordinator.TryApproveAsync(token, ["GATE-A"]);

        // Old token tries to approve GATE-B — must fail (version stale)
        var result = await _coordinator.TryApproveAsync(token, ["GATE-B"]);
        Assert.That(result, Is.EqualTo(ApprovalClickResult.StaleRejected),
            "Old token must not approve remaining gates after partial approval");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Cross-surface event data integrity
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task CrossSurfaceEvent_ResolutionNotePreservedInEvent()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);

        ApprovalResolvedEventArgs? eventArgs = null;
        _coordinator.ApprovalResolved += (_, args) => eventArgs = args;

        await _coordinator.TryApproveAsync(token, ["GATE-A"], "Ship it! LGTM");

        Assert.That(eventArgs, Is.Not.Null);
        Assert.That(eventArgs!.ResolutionNote, Is.EqualTo("Ship it! LGTM"),
            "Resolution note must propagate through the event");
        Assert.That(eventArgs.ResolvedGateIds, Is.EqualTo(new[] { "GATE-A" }));
    }

    [Test]
    public async Task CrossSurfaceEvent_PartialApproval_FiredPerApprovalAction()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1",
            ["GATE-A", "GATE-B", "GATE-C"]);

        var events = new List<ApprovalResolvedEventArgs>();
        _coordinator.ApprovalResolved += (_, args) => events.Add(args);

        // Approve GATE-A only
        await _coordinator.TryApproveAsync(token, ["GATE-A"]);

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].AllGatesResolved, Is.False,
            "GATE-B and GATE-C still active");
        Assert.That(events[0].ResolvedGateIds, Has.Count.EqualTo(1));

        // Approve remaining with fresh token
        var freshToken = _coordinator.GetCurrentToken("PLAN-001")!;
        await _coordinator.TryApproveAsync(freshToken, ["GATE-B", "GATE-C"]);

        Assert.That(events, Has.Count.EqualTo(2));
        Assert.That(events[1].AllGatesResolved, Is.True,
            "All gates now resolved");
        Assert.That(events[1].ResolvedGateIds, Has.Count.EqualTo(2));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PlanStoreUpdater gate lifecycle transitions
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void PlanStoreUpdater_ApplyGateReady_KeepsExecutingLifecycle()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete);

        var updated = PlanStoreUpdater.ApplyGateReady(plan, "GATE-A");

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing),
            "ApplyGateReady must keep lifecycle as Executing for ungated work to continue");
        Assert.That(updated.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.AwaitingApproval));
        Assert.That(updated.ApprovalGates[0].RequestedAt, Is.Not.Null);
    }

    [Test]
    public void PlanStoreUpdater_ApplyGateActivated_TransitionsToAwaiting()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete);

        var updated = PlanStoreUpdater.ApplyGateActivated(plan, "GATE-A");

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));
        Assert.That(updated.Progress.ExecutingTaskId, Is.Null,
            "Activation must clear ExecutingTaskId");
    }

    [Test]
    public void PlanStoreUpdater_ApplyGateApproved_WithMultipleGates_StaysAwaitingIfOthersRemain()
    {
        var gateB = new PlanApprovalGate("GATE-B", "Second", ["T3"], ["T4"],
            PlanGateStatus.AwaitingApproval);
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            extraGates: [gateB],
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        var updated = PlanStoreUpdater.ApplyGateApproved(plan, "GATE-A", "OK");

        Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval),
            "Must stay AwaitingApproval while GATE-B is still awaiting");
        Assert.That(updated.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.Approved));
        Assert.That(updated.ApprovalGates[0].ResolutionNote, Is.EqualTo("OK"));
        Assert.That(updated.ApprovalGates[1].Status, Is.EqualTo(PlanGateStatus.AwaitingApproval));
    }

    [Test]
    public void PlanStoreUpdater_ApplyGateApproved_NonAwaitingGate_ReturnsUnchanged()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.Pending);

        var updated = PlanStoreUpdater.ApplyGateApproved(plan, "GATE-A", "OK");

        Assert.That(updated, Is.SameAs(plan),
            "Approving a gate that isn't AwaitingApproval must be a no-op");
    }

    [Test]
    public void PlanStoreUpdater_ApplyGateReady_AlreadyApproved_ReturnsUnchanged()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.Approved);

        var updated = PlanStoreUpdater.ApplyGateReady(plan, "GATE-A");

        Assert.That(updated, Is.SameAs(plan),
            "Cannot mark an already-approved gate as ready");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Full lifecycle: gate ready → activated → approved → back to executing
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void PlanStoreUpdater_FullGateLifecycle_ReadyActivatedApproved()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            t5Status: PlanTaskStatus.Complete);

        // Step 1: Gate becomes ready (ungated work still possible if any)
        var ready = PlanStoreUpdater.ApplyGateReady(plan, "GATE-A");
        Assert.That(ready.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));

        // Step 2: Full stop — no ungated work remains
        var stopped = PlanStoreUpdater.ApplyFullStopAtGates(plan, ["GATE-A"]);
        Assert.That(stopped.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));

        // Step 3: Gate approved — back to executing
        var approved = PlanStoreUpdater.ApplyGateApproved(stopped, "GATE-A", "LGTM");
        Assert.That(approved.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        Assert.That(approved.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.Approved));
        Assert.That(approved.ApprovalGates[0].ResolvedAt, Is.Not.Null);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BuildActions: output correctness for various gate configurations
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void BuildActions_MultipleActiveGates_GeneratesOneVersionedAggregateAction()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var actions = DurableApprovalRequestManager.BuildActions(plan, ["GATE-A", "GATE-B", "GATE-C"]);

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].Label, Does.Contain("3 Ready Checkpoints"));
        Assert.That(actions[0].RouteMode, Is.EqualTo(DurableApprovalRequestManager.ApprovalRouteMode));
        Assert.That(ApprovalInboxActionPayload.TryParse(actions[0].Prompt, out var payload), Is.True);
        Assert.That(payload!.GateIds, Is.EqualTo(new[] { "GATE-A", "GATE-B", "GATE-C" }));
    }

    [Test]
    public void BuildActions_EmptyGateList_ReturnsEmptyActions()
    {
        var plan = MakePlan();
        var actions = DurableApprovalRequestManager.BuildActions(plan, []);

        Assert.That(actions, Is.Empty,
            "No actions should be generated when no gates are active");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Coordinator ClearAll resets everything
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ClearAll_RemovesAllTrackedPlans()
    {
        await _coordinator.RegisterAsync("PLAN-A", "rev1", ["GATE-A"]);
        await _coordinator.RegisterAsync("PLAN-B", "rev1", ["GATE-X"]);

        Assert.That(_coordinator.HasActiveGates("PLAN-A"), Is.True);
        Assert.That(_coordinator.HasActiveGates("PLAN-B"), Is.True);

        _coordinator.ClearAll();

        Assert.That(_coordinator.HasActiveGates("PLAN-A"), Is.False);
        Assert.That(_coordinator.HasActiveGates("PLAN-B"), Is.False);
        Assert.That(_coordinator.GetCurrentToken("PLAN-A"), Is.Null);
        Assert.That(_coordinator.GetCurrentToken("PLAN-B"), Is.Null);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Concurrent DurableManager: parallel AppendCheckpoint for different gates
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ConcurrentAppendCheckpoint_DifferentGates_AllTracked()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var gate1 = plan.ApprovalGates[0]; // GATE-A

        // First checkpoint
        await _durableManager.AppendCheckpointAsync(plan, gate1, MakeSnapshot());

        // Add several new gates concurrently
        var gateNames = Enumerable.Range(1, 5).Select(i => $"GATE-NEW-{i}").ToList();
        var tasks = gateNames.Select(gateId =>
        {
            var gate = new PlanApprovalGate(gateId, $"Review {gateId}", ["T1"], ["T3"],
                PlanGateStatus.AwaitingApproval);
            var planWithGate = MakePlan(extraGates: [gate],
                t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
            return _durableManager.AppendCheckpointAsync(planWithGate, gate,
                MakeSnapshot(gateId: gateId));
        }).ToArray();

        await Task.WhenAll(tasks);

        var state = _durableManager.GetState("PLAN-001");
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.ActiveGateIds, Does.Contain("GATE-A"));
        foreach (var gateName in gateNames)
            Assert.That(state.ActiveGateIds, Does.Contain(gateName),
                $"{gateName} must be tracked after concurrent append");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Concurrent resolve + append race on DurableManager
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ConcurrentResolveAndAppend_NeitherLost()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());

        // Race: resolve GATE-A while appending GATE-B
        var resolveTask = _durableManager.ResolveCheckpointAsync(plan, "GATE-A", "OK");
        var appendGate = new PlanApprovalGate("GATE-B", "New", ["T3"], ["T4"],
            PlanGateStatus.AwaitingApproval);
        var appendPlan = MakePlan(extraGates: [appendGate],
            t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var appendTask = _durableManager.AppendCheckpointAsync(appendPlan, appendGate,
            MakeSnapshot(gateId: "GATE-B"));

        await Task.WhenAll(resolveTask, appendTask);

        var state = _durableManager.GetState("PLAN-001");
        Assert.That(state, Is.Not.Null);
        // GATE-A should be resolved regardless of ordering
        Assert.That(state!.ResolvedCheckpoints.Any(r => r.GateId == "GATE-A"), Is.True,
            "GATE-A must be resolved even when racing with append");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Stable message identity: BuildMessageId determinism
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void BuildMessageId_Deterministic_SamePlanId()
    {
        var id1 = DurableApprovalRequestManager.BuildMessageId("MY-PLAN");
        var id2 = DurableApprovalRequestManager.BuildMessageId("MY-PLAN");
        Assert.That(id1, Is.EqualTo(id2));
        Assert.That(id1, Is.EqualTo("approval-gate-MY-PLAN"));
    }

    [Test]
    public void BuildMessageId_Different_PlanIds()
    {
        var id1 = DurableApprovalRequestManager.BuildMessageId("PLAN-A");
        var id2 = DurableApprovalRequestManager.BuildMessageId("PLAN-B");
        Assert.That(id1, Is.Not.EqualTo(id2));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Token match semantics: Matches method edge cases
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ApprovalClickToken_Matches_IdenticalTokens_ReturnsTrue()
    {
        var token = new ApprovalClickToken("P1", "rev1", 1, ["GATE-A", "GATE-B"]);
        var clone = new ApprovalClickToken("P1", "rev1", 1, ["GATE-A", "GATE-B"]);
        Assert.That(token.Matches(clone), Is.True);
    }

    [Test]
    public void ApprovalClickToken_Matches_DifferentGateOrder_ReturnsFalse()
    {
        var token1 = new ApprovalClickToken("P1", "rev1", 1, ["GATE-A", "GATE-B"]);
        var token2 = new ApprovalClickToken("P1", "rev1", 1, ["GATE-B", "GATE-A"]);
        Assert.That(token1.Matches(token2), Is.False,
            "Gate order matters — reordered gates indicate a different snapshot");
    }

    [Test]
    public void ApprovalClickToken_Matches_DifferentVersion_ReturnsFalse()
    {
        var token1 = new ApprovalClickToken("P1", "rev1", 1, ["GATE-A"]);
        var token2 = new ApprovalClickToken("P1", "rev1", 2, ["GATE-A"]);
        Assert.That(token1.Matches(token2), Is.False);
    }

    [Test]
    public void ApprovalClickToken_Matches_DifferentRevision_ReturnsFalse()
    {
        var token1 = new ApprovalClickToken("P1", "rev1", 1, ["GATE-A"]);
        var token2 = new ApprovalClickToken("P1", "rev2", 1, ["GATE-A"]);
        Assert.That(token1.Matches(token2), Is.False);
    }

    [Test]
    public void ApprovalClickToken_Matches_EmptyGateLists_ReturnsTrue()
    {
        var token1 = new ApprovalClickToken("P1", "rev1", 1, []);
        var token2 = new ApprovalClickToken("P1", "rev1", 1, []);
        Assert.That(token1.Matches(token2), Is.True);
    }

    [Test]
    public void ApprovalClickToken_Matches_OneEmptyOneNot_ReturnsFalse()
    {
        var token1 = new ApprovalClickToken("P1", "rev1", 1, []);
        var token2 = new ApprovalClickToken("P1", "rev1", 1, ["GATE-A"]);
        Assert.That(token1.Matches(token2), Is.False);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Downstream frontier with diamond dependencies
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void DownstreamFrontier_DiamondDependency_AllPathsBlocked()
    {
        //   T1 ──► [GATE-A] ──► T2 ──┐
        //                              ├──► T4
        //                    T3 ──────┘
        var tasks = new[]
        {
            new PlanTask("T1", "Task 1", "desc", [], "high", PlanTaskStatus.Complete),
            new PlanTask("T2", "Task 2", "desc", ["T1"], "high", PlanTaskStatus.Pending),
            new PlanTask("T3", "Task 3", "desc", [], "high", PlanTaskStatus.Complete),
            new PlanTask("T4", "Task 4", "desc", ["T2", "T3"], "high", PlanTaskStatus.Pending),
        };
        var gate = new PlanApprovalGate("GATE-A", "Review", ["T1"], ["T2"], PlanGateStatus.Pending);
        var plan = new Plan("P-DIA", "rev1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "Diamond", "main", "",
            tasks, [gate], new PlanProgress(2, 4), new PlanTimestamps(DateTimeOffset.UtcNow));

        var frontier = ApprovalGateReadinessEvaluator.ComputeDownstreamFrontier(plan, gate);

        Assert.That(frontier, Does.Contain("T2"), "T2 is directly gated");
        Assert.That(frontier, Does.Contain("T4"),
            "T4 depends on T2 (gated) — must be transitively blocked");
        Assert.That(frontier, Does.Not.Contain("T1"));
        Assert.That(frontier, Does.Not.Contain("T3"),
            "T3 is independent of the gate, must not be in frontier");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Multiple plans' notifications are independent
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task NotificationDedup_IndependentAcrossPlans()
    {
        var planA = MakePlan("PLAN-A", t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var planB = MakePlan("PLAN-B", t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);

        await _durableManager.AppendCheckpointAsync(planA, planA.ApprovalGates[0], MakeSnapshot("PLAN-A"));
        await _durableManager.AppendCheckpointAsync(planB, planB.ApprovalGates[0], MakeSnapshot("PLAN-B"));

        // Notify PLAN-A
        Assert.That(await _durableManager.TryMarkNotifiedAsync("PLAN-A"), Is.True);
        Assert.That(await _durableManager.TryMarkNotifiedAsync("PLAN-A"), Is.False,
            "Second notification for PLAN-A must be deduped");

        // PLAN-B should be independently notifiable
        Assert.That(await _durableManager.TryMarkNotifiedAsync("PLAN-B"), Is.True,
            "PLAN-B notification must not be affected by PLAN-A's dedup");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Unseen gate invariant: re-registration after full resolution
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task UnseenGate_ReRegistrationAfterFullResolution_OldTokenFails()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);
        await _coordinator.TryApproveAsync(token, ["GATE-A"]);

        // Plan fully resolved. Now re-register with new gates (plan revision 2)
        var newToken = await _coordinator.RegisterAsync("PLAN-001", "rev2", ["GATE-NEW"]);

        // Old token must fail for the new gate
        var result = await _coordinator.TryApproveAsync(token, ["GATE-NEW"]);
        Assert.That(result, Is.EqualTo(ApprovalClickResult.StaleRejected),
            "Old token from rev1 must never approve gates registered under rev2");

        // New token must succeed
        var result2 = await _coordinator.TryApproveAsync(newToken, ["GATE-NEW"]);
        Assert.That(result2, Is.EqualTo(ApprovalClickResult.Approved));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Coordinator: approve gate not in token's gate list
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task TryApprove_GateNotInActiveList_RejectsAsStale()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);

        // Try to approve a gate that doesn't exist in the registered set
        var result = await _coordinator.TryApproveAsync(token, ["GATE-X"]);
        Assert.That(result, Is.EqualTo(ApprovalClickResult.StaleRejected),
            "Approving a gate not in the active list must be rejected");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Review snapshot model: edge cases
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ReviewTaskEntry_NoCommits_EmptyList()
    {
        var entry = new ReviewTaskEntry("T1", "Task with no commits", "Done", []);
        Assert.That(entry.Commits, Is.Empty);
        Assert.That(entry.TaskId, Is.EqualTo("T1"));
    }

    [Test]
    public void ChangedFileEntry_RenamedAndCopied_TrackedCorrectly()
    {
        var renamed = new ChangedFileEntry("new-name.cs", FileChangeStatus.Renamed,
            5, 2, "sha1", new FileLink("new-name.cs", "sha1"));
        var copied = new ChangedFileEntry("copy.cs", FileChangeStatus.Copied,
            10, 0, "sha1", new FileLink("copy.cs", "sha1"));

        Assert.That(renamed.Status, Is.EqualTo(FileChangeStatus.Renamed));
        Assert.That(copied.Status, Is.EqualTo(FileChangeStatus.Copied));
        Assert.That(renamed.Link.WorkspaceFileUri, Does.Contain("new-name.cs"));
    }

    [Test]
    public void DownstreamTaskEntry_ModelIntegrity()
    {
        var entry = new DownstreamTaskEntry("T3", "Implement API", PlanTaskStatus.Pending);
        Assert.That(entry.TaskId, Is.EqualTo("T3"));
        Assert.That(entry.Title, Is.EqualTo("Implement API"));
        Assert.That(entry.Status, Is.EqualTo(PlanTaskStatus.Pending));
    }

    [Test]
    public void IndependentWorkEntry_ModelIntegrity()
    {
        var commit = new CommitLink("abc1234", "abc1234full", "Fix bug");
        var entry = new IndependentWorkEntry("T5", "Independent task",
            "Completed independently", [new ReviewCommitEntry(commit, true, [])]);

        Assert.That(entry.TaskId, Is.EqualTo("T5"));
        Assert.That(entry.Commits, Has.Count.EqualTo(1));
        Assert.That(entry.CompletionSummary, Is.EqualTo("Completed independently"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // End-to-end: coordinator + evaluator + durable manager — full stop path
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task EndToEnd_FullStopPath_EvaluateRegisterPublishApproveResume()
    {
        // T1, T2, T5 all complete — only gated T3, T4 remain
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            t5Status: PlanTaskStatus.Complete);

        // Step 1: Evaluate — should stop
        Assert.That(ApprovalGateReadinessEvaluator.ShouldStopForApproval(plan), Is.True);

        // Step 2: Apply full stop
        var stoppedPlan = PlanStoreUpdater.ApplyFullStopAtGates(plan, ["GATE-A"]);
        Assert.That(stoppedPlan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));

        // Step 3: Register and publish
        var gateStates = ApprovalGateReadinessEvaluator.EvaluateGates(plan);
        var readyIds = ApprovalGateReadinessEvaluator.GetReadyGateIds(gateStates);
        var token = await _coordinator.RegisterAsync(plan.PlanId, plan.Revision, readyIds);
        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());

        // Step 4: Approve
        var clickResult = await _coordinator.TryApproveAsync(token, ["GATE-A"], "Approved all");
        Assert.That(clickResult, Is.EqualTo(ApprovalClickResult.Approved));

        // Step 5: Resolve durable state
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-A", "Approved all");
        Assert.That(_durableManager.IsArchived("PLAN-001"), Is.True);

        // Step 6: Resume — apply gate approved in plan
        var approvedPlan = PlanStoreUpdater.ApplyGateApproved(stoppedPlan, "GATE-A", "Approved all");
        Assert.That(approvedPlan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));

        // Step 7: Verify T3 is now eligible
        var released = ApprovalGateReadinessEvaluator.GetReleasedTaskIds(approvedPlan, "GATE-A");
        Assert.That(released, Does.Contain("T3"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Resolved gate IDs preserved in coordinator state
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ResolvedGateIds_TrackedInCoordinatorState()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A", "GATE-B"]);

        await _coordinator.TryApproveAsync(token, ["GATE-A"]);

        var freshToken = _coordinator.GetCurrentToken("PLAN-001")!;

        // Try re-approving GATE-A with fresh token — should be AlreadyResolved
        var result = await _coordinator.TryApproveAsync(freshToken, ["GATE-A"]);
        Assert.That(result, Is.EqualTo(ApprovalClickResult.AlreadyResolved));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Unregister then re-register: fresh start
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Unregister_ThenReRegister_StartsFresh()
    {
        var token1 = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);
        await _coordinator.TryApproveAsync(token1, ["GATE-A"]);
        _coordinator.Unregister("PLAN-001");

        // Re-register same plan — new state
        var token2 = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);
        Assert.That(token2.RequestVersion, Is.EqualTo(1),
            "Re-registration after unregister should start at version 1");
        Assert.That(_coordinator.HasActiveGates("PLAN-001"), Is.True);

        // Old resolved gates should not carry over
        var result = await _coordinator.TryApproveAsync(token2, ["GATE-A"]);
        Assert.That(result, Is.EqualTo(ApprovalClickResult.Approved),
            "GATE-A should be approvable again after fresh registration");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DurableManager: inbox message subject and priority
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task InboxMessage_SubjectAndPriority_SetCorrectly()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());

        var msg = _inbox.GetById("approval-gate-PLAN-001");
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg!.Subject, Does.Contain("Approval needed"));
        Assert.That(msg.Subject, Does.Contain("Integration Test Plan"));
        Assert.That(msg.Priority, Is.EqualTo("high"));
        Assert.That(msg.Read, Is.False);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DurableManager: resolve nonexistent gate is no-op
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ResolveCheckpoint_NonexistentGate_NoError()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());

        // Resolve a gate that doesn't exist in active list — should not throw
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-NONEXISTENT", "oops");

        var state = _durableManager.GetState("PLAN-001");
        Assert.That(state!.ActiveGateIds, Does.Contain("GATE-A"),
            "Original gate must remain active");
        Assert.That(state.Archived, Is.False);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TryMarkNotified on nonexistent plan returns false
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task TryMarkNotified_NonexistentPlan_ReturnsFalse()
    {
        var result = await _durableManager.TryMarkNotifiedAsync("NO-SUCH-PLAN");
        Assert.That(result, Is.False);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RestoreActivePlanIds: multiple plans, mixed states
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task RestoreActivePlanIds_MixedStates_OnlyActiveReturned()
    {
        var planA = MakePlan("PLAN-A", t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var planB = MakePlan("PLAN-B", t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        var planC = MakePlan("PLAN-C", t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);

        await _durableManager.AppendCheckpointAsync(planA, planA.ApprovalGates[0], MakeSnapshot("PLAN-A"));
        await _durableManager.AppendCheckpointAsync(planB, planB.ApprovalGates[0], MakeSnapshot("PLAN-B"));
        await _durableManager.AppendCheckpointAsync(planC, planC.ApprovalGates[0], MakeSnapshot("PLAN-C"));

        // Resolve B and C
        await _durableManager.ResolveCheckpointAsync(planB, "GATE-A");
        await _durableManager.ResolveCheckpointAsync(planC, "GATE-A");

        // Fresh manager to simulate restart
        var fresh = new DurableApprovalRequestManager(_inbox);
        var activeIds = fresh.RestoreActivePlanIds();

        Assert.That(activeIds, Has.Count.EqualTo(1));
        Assert.That(activeIds, Does.Contain("PLAN-A"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Evaluator: executing task excluded from next-task selection
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SelectNextUngatedTask_ExecutingTaskExcluded()
    {
        var tasks = new[]
        {
            new PlanTask("T1", "Task 1", "desc", [], "high", PlanTaskStatus.Complete),
            new PlanTask("T2", "Task 2", "desc", [], "high", PlanTaskStatus.Executing),
            new PlanTask("T3", "Task 3", "desc", ["T1"], "high", PlanTaskStatus.Pending),
        };
        var plan = new Plan("P-EX", "rev1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.Executing, "Test", "main", "",
            tasks, [], new PlanProgress(1, 3), new PlanTimestamps(DateTimeOffset.UtcNow));

        var next = ApprovalGateReadinessEvaluator.SelectNextUngatedTask(plan);

        Assert.That(next, Is.EqualTo("T3"),
            "Executing task T2 must not be selected; T3 is next eligible");
        Assert.That(next, Is.Not.EqualTo("T2"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DurableApprovalState: version increments on each mutation
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task DurableState_VersionIncrements_AcrossMutations()
    {
        var plan = MakePlan(t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var v1 = _durableManager.GetState("PLAN-001")!.Version;

        // Second append (idempotent) — should NOT bump version
        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var v2 = _durableManager.GetState("PLAN-001")!.Version;
        Assert.That(v2, Is.EqualTo(v1), "Idempotent append should not bump version");

        // New gate — SHOULD bump version
        var gate2 = new PlanApprovalGate("GATE-B", "New", ["T3"], ["T4"],
            PlanGateStatus.AwaitingApproval);
        var planWithGate = MakePlan(extraGates: [gate2],
            t1Status: PlanTaskStatus.Complete, t2Status: PlanTaskStatus.Complete);
        await _durableManager.AppendCheckpointAsync(planWithGate, gate2, MakeSnapshot(gateId: "GATE-B"));
        var v3 = _durableManager.GetState("PLAN-001")!.Version;
        Assert.That(v3, Is.GreaterThan(v1), "Adding a new gate must bump version");
    }
}
