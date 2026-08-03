using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
public class DurableApprovalRequestManagerTests
{
    private string _tempDir = null!;
    private InboxStore _inbox = null!;
    private DurableApprovalRequestManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"squad-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _inbox = new InboxStore(_tempDir);
        _manager = new DurableApprovalRequestManager(_inbox);
    }

    [TearDown]
    public void TearDown()
    {
        _manager.ClearLocks();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Plan MakePlan(string planId = "PLAN-001", string title = "Test Plan",
        IReadOnlyList<PlanApprovalGate>? gates = null, IReadOnlyList<PlanTask>? tasks = null)
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
            planId, "rev1", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.AwaitingApproval, title, "main", "Summary",
            tasks, gates,
            new PlanProgress(1, 3),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    private static ApprovalReviewSnapshot MakeSnapshot(string planId = "PLAN-001", string gateId = "GATE-001") =>
        new(planId, "Test Plan", 1, 3, PlanLifecycleStatus.AwaitingApproval,
            gateId, "Review after T1", ["T1"], ["T2", "T3"],
            [], [], [], [], DateTimeOffset.UtcNow);

    // ── Tests ────────────────────────────────────────────────────────────────

    [Test]
    public async Task AppendCheckpoint_CreatesNewMessage()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();

        var messageId = await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);

        Assert.That(messageId, Is.EqualTo("approval-gate-PLAN-001"));
        var msg = _inbox.GetById(messageId);
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg!.Subject, Does.Contain("Test Plan"));
        Assert.That(msg.Read, Is.False);
        Assert.That(msg.Priority, Is.EqualTo("high"));
        Assert.That(msg.Actions, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AppendCheckpoint_ExposesPlanLinkButKeepsPersistenceAttachmentsInternal()
    {
        var plan = MakePlan();
        var messageId = await _manager.AppendCheckpointAsync(
            plan, plan.ApprovalGates[0], MakeSnapshot());

        var msg = _inbox.GetById(messageId)!;
        var visible = msg.Attachments
            .Where(DurableApprovalRequestManager.IsPresentationAttachment)
            .ToArray();

        Assert.That(visible, Has.Length.EqualTo(1));
        Assert.That(visible[0].Type, Is.EqualTo(DecomposePlanInbox.AttachmentType));
        Assert.That(visible[0].Label, Is.EqualTo("View plan and dependencies"));
        Assert.That(visible[0].PlanGroupId, Is.EqualTo(plan.PlanId));
        Assert.That(DecomposePlanInbox.TryReadSnapshot(visible[0], out var pending), Is.True);
        Assert.That(pending!.Group.GroupId, Is.EqualTo(plan.PlanId));

        Assert.That(msg.Attachments.Any(a => a.Type == DurableApprovalRequestManager.AttachmentType), Is.True);
        Assert.That(msg.Attachments.Any(a => a.Type == "approval-snapshot"), Is.True);
    }

    [Test]
    public async Task AppendCheckpoint_IsIdempotent()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();

        var id1 = await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);
        var id2 = await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);

        Assert.That(id1, Is.EqualTo(id2));
        var state = _manager.GetState("PLAN-001");
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.ActiveGateIds, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AppendCheckpoint_AppendsSecondGate()
    {
        var gate2 = new PlanApprovalGate("GATE-002", "Review after T2", ["T2"], ["T3"], PlanGateStatus.AwaitingApproval);
        var plan = MakePlan(gates: [
            new PlanApprovalGate("GATE-001", "Review after T1", ["T1"], ["T2", "T3"], PlanGateStatus.AwaitingApproval),
            gate2,
        ]);
        var snapshot1 = MakeSnapshot();
        var snapshot2 = MakeSnapshot(gateId: "GATE-002");

        await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot1);
        await _manager.AppendCheckpointAsync(plan, gate2, snapshot2);

        var state = _manager.GetState("PLAN-001");
        Assert.That(state!.ActiveGateIds, Has.Count.EqualTo(2));
        Assert.That(state.ActiveGateIds, Does.Contain("GATE-001"));
        Assert.That(state.ActiveGateIds, Does.Contain("GATE-002"));
    }

    [Test]
    public async Task AppendCheckpoint_RefreshesAggregatedMessageTimestamp()
    {
        var plan = MakePlan();
        await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var messageId = DurableApprovalRequestManager.BuildMessageId(plan.PlanId);
        var oldTimestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        _inbox.Save(_inbox.GetById(messageId)! with { Timestamp = oldTimestamp });

        var gate2 = new PlanApprovalGate(
            "GATE-002", "Review after T2", ["T2"], ["T3"], PlanGateStatus.AwaitingApproval);
        var expanded = MakePlan(gates: [plan.ApprovalGates[0], gate2]);
        await _manager.AppendCheckpointAsync(expanded, gate2, MakeSnapshot(gateId: "GATE-002"));

        Assert.That(_inbox.GetById(messageId)!.Timestamp, Is.GreaterThan(oldTimestamp));
    }

    [Test]
    public async Task ResolveCheckpoint_MovesToHistory()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();
        await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);

        await _manager.ResolveCheckpointAsync(plan, "GATE-001", "Looks good");

        var state = _manager.GetState("PLAN-001");
        Assert.That(state!.ActiveGateIds, Is.Empty);
        Assert.That(state.ResolvedCheckpoints, Has.Count.EqualTo(1));
        Assert.That(state.ResolvedCheckpoints[0].GateId, Is.EqualTo("GATE-001"));
        Assert.That(state.ResolvedCheckpoints[0].ResolutionNote, Is.EqualTo("Looks good"));
        Assert.That(state.Archived, Is.True);
    }

    [Test]
    public async Task ResolveCheckpoint_ArchivesWhenNoActiveGates()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();
        await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);

        await _manager.ResolveCheckpointAsync(plan, "GATE-001");

        var msg = _inbox.GetById("approval-gate-PLAN-001");
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg!.Read, Is.True);
        Assert.That(msg.Actions, Is.Empty);
        Assert.That(msg.Body, Does.Contain("archived"));
    }

    [Test]
    public async Task AppendCheckpoint_UnarchivesResolvedMessage()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();
        await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);
        await _manager.ResolveCheckpointAsync(plan, "GATE-001");

        // New gate arrives — unarchive
        var gate2 = new PlanApprovalGate("GATE-002", "Review after T2", ["T2"], ["T3"], PlanGateStatus.AwaitingApproval);
        var planWithGate2 = MakePlan(gates: [plan.ApprovalGates[0], gate2]);
        var snapshot2 = MakeSnapshot(gateId: "GATE-002");
        await _manager.AppendCheckpointAsync(planWithGate2, gate2, snapshot2);

        var state = _manager.GetState("PLAN-001");
        Assert.That(state!.Archived, Is.False);
        Assert.That(state.ActiveGateIds, Does.Contain("GATE-002"));
        Assert.That(state.ResolvedCheckpoints, Has.Count.EqualTo(1));

        var msg = _inbox.GetById("approval-gate-PLAN-001");
        Assert.That(msg!.Read, Is.False);
        Assert.That(msg.Actions, Has.Count.GreaterThan(0));
    }

    [Test]
    public async Task RefreshEvidence_UpdatesBody()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();
        await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);

        var updatedPlan = plan with { Progress = new PlanProgress(2, 3) };
        var updatedSnapshot = MakeSnapshot() with { CompletedTaskCount = 2 };
        await _manager.RefreshEvidenceAsync(updatedPlan, updatedSnapshot);

        var msg = _inbox.GetById("approval-gate-PLAN-001");
        Assert.That(msg!.Body, Does.Contain("2/3"));
    }

    [Test]
    public async Task TryMarkNotified_ReturnsTrueOnce()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();
        await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);

        var first = await _manager.TryMarkNotifiedAsync("PLAN-001");
        var second = await _manager.TryMarkNotifiedAsync("PLAN-001");

        Assert.That(first, Is.True);
        Assert.That(second, Is.False);
    }

    [Test]
    public async Task AppendLaterCheckpoint_AdvancesVersionAndAllowsOneNewNotification()
    {
        var firstGate = new PlanApprovalGate(
            "GATE-001", "First review", ["T1"], ["T2"], PlanGateStatus.AwaitingApproval);
        var secondGate = new PlanApprovalGate(
            "GATE-002", "Second review", ["T2"], ["T3"], PlanGateStatus.AwaitingApproval);
        var plan = MakePlan(gates: [firstGate, secondGate]);
        await _manager.AppendCheckpointAsync(plan, firstGate, MakeSnapshot());
        Assert.That(await _manager.TryMarkNotifiedAsync(plan.PlanId), Is.True);
        var version1 = _manager.GetState(plan.PlanId)!.Version;

        await _manager.AppendCheckpointAsync(
            plan,
            secondGate,
            MakeSnapshot() with { GateId = secondGate.GateId, GateReason = secondGate.Message });

        var state = _manager.GetState(plan.PlanId)!;
        Assert.Multiple(() =>
        {
            Assert.That(state.Version, Is.GreaterThan(version1));
            Assert.That(state.ActiveGateIds, Is.EqualTo(new[] { "GATE-001", "GATE-002" }));
        });
        Assert.That(await _manager.TryMarkNotifiedAsync(plan.PlanId), Is.True);
        Assert.That(await _manager.TryMarkNotifiedAsync(plan.PlanId), Is.False);
    }

    [Test]
    public async Task RestoreActivePlanIds_FindsActiveMessages()
    {
        var plan1 = MakePlan("PLAN-A", "Plan A");
        var plan2 = MakePlan("PLAN-B", "Plan B");
        var snap1 = MakeSnapshot("PLAN-A");
        var snap2 = MakeSnapshot("PLAN-B");

        await _manager.AppendCheckpointAsync(plan1, plan1.ApprovalGates[0], snap1);
        await _manager.AppendCheckpointAsync(plan2, plan2.ApprovalGates[0], snap2);
        await _manager.ResolveCheckpointAsync(plan2, "GATE-001");

        var active = _manager.RestoreActivePlanIds();
        Assert.That(active, Does.Contain("PLAN-A"));
        Assert.That(active, Does.Not.Contain("PLAN-B"));
    }

    [Test]
    public async Task StableIdentity_SameIdAcrossLifecycle()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();

        var id1 = await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);
        await _manager.ResolveCheckpointAsync(plan, "GATE-001");

        var gate2 = new PlanApprovalGate("GATE-002", "Review after T2", ["T2"], ["T3"], PlanGateStatus.AwaitingApproval);
        var planWithGate2 = MakePlan(gates: [plan.ApprovalGates[0], gate2]);
        var id2 = await _manager.AppendCheckpointAsync(planWithGate2, gate2, MakeSnapshot(gateId: "GATE-002"));

        Assert.That(id1, Is.EqualTo(id2), "Message ID must be stable across archive/unarchive");
    }

    [Test]
    public void BuildMessageId_IsDeterministic()
    {
        var id1 = DurableApprovalRequestManager.BuildMessageId("PLAN-001");
        var id2 = DurableApprovalRequestManager.BuildMessageId("PLAN-001");
        Assert.That(id1, Is.EqualTo(id2));
        Assert.That(id1, Is.EqualTo("approval-gate-PLAN-001"));
    }

    [Test]
    public async Task GetState_ReturnsNullForUnknownPlan()
    {
        var state = _manager.GetState("UNKNOWN");
        Assert.That(state, Is.Null);
    }

    [Test]
    public async Task IsArchived_ReturnsCorrectState()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();
        await _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot);

        Assert.That(_manager.IsArchived("PLAN-001"), Is.False);

        await _manager.ResolveCheckpointAsync(plan, "GATE-001");
        Assert.That(_manager.IsArchived("PLAN-001"), Is.True);
    }

    [Test]
    public async Task ConcurrentAppends_AreSerialized()
    {
        var plan = MakePlan();
        var snapshot = MakeSnapshot();

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _manager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], snapshot))
            .ToArray();

        await Task.WhenAll(tasks);

        // All should return the same ID; state should be consistent
        var ids = tasks.Select(t => t.Result).Distinct().ToList();
        Assert.That(ids, Has.Count.EqualTo(1));
        var state = _manager.GetState("PLAN-001");
        Assert.That(state!.ActiveGateIds, Has.Count.EqualTo(1));
    }

    [Test]
    public void BuildBody_ShowsActiveAndResolved()
    {
        var plan = MakePlan();
        var resolved = new List<ResolvedCheckpointEntry>
        {
            new("GATE-000", DateTimeOffset.UtcNow, "LGTM"),
        };
        var body = DurableApprovalRequestManager.BuildBody(plan, ["GATE-001"], resolved);

        Assert.That(body, Does.Contain("GATE-001"));
        Assert.That(body, Does.Contain("GATE-000"));
        Assert.That(body, Does.Contain("LGTM"));
        Assert.That(body, Does.Contain("1 checkpoint(s) awaiting approval"));
        Assert.That(body, Does.Contain("1 resolved checkpoint(s)"));
    }

    [Test]
    public void BuildActions_ReturnsEmptyWhenNoActiveGates()
    {
        var plan = MakePlan();
        var actions = DurableApprovalRequestManager.BuildActions(plan, []);
        Assert.That(actions, Is.Empty);
    }

    [Test]
    public void BuildActions_ReturnsOneVersionedAggregateAction()
    {
        var plan = MakePlan();
        var actions = DurableApprovalRequestManager.BuildActions(plan, ["GATE-001", "GATE-002"]);
        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].RouteMode, Is.EqualTo(DurableApprovalRequestManager.ApprovalRouteMode));
        Assert.That(ApprovalInboxActionPayload.TryParse(actions[0].Prompt, out var payload), Is.True);
        Assert.That(payload!.GateIds, Is.EqualTo(new[] { "GATE-001", "GATE-002" }));
    }
}
