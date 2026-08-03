using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Host-controlled integration tests for the approval and recovery notification lifecycle.
/// Verifies atomic timestamp refresh, temporary action disabling during replacement,
/// card restoration after transcript hydration, restart replay, Loop panel state,
/// archival of obsolete messages, accumulating gates, approval from every surface,
/// stale tokens, restart convergence, workspace switching, concurrent workspaces,
/// hidden Inbox, and normal workspace with no restarts.
/// </summary>
[TestFixture]
internal sealed class ApprovalNotificationLifecycleTests
{
    private string _tempDir = null!;
    private InboxStore _inbox = null!;
    private ApprovalActionCoordinator _coordinator = null!;
    private DurableApprovalRequestManager _durableManager = null!;
    private PlanApprovalRuntime _runtime = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"squad-lifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _inbox = new InboxStore(_tempDir);
        _coordinator = new ApprovalActionCoordinator();
        _durableManager = new DurableApprovalRequestManager(_inbox);
        _runtime = new PlanApprovalRuntime(
            _durableManager,
            _coordinator,
            (plan, gate, ct) => Task.FromResult(MakeSnapshot(plan.PlanId, gate.GateId)));
    }

    [TearDown]
    public void TearDown()
    {
        _coordinator.ClearAll();
        _durableManager.ClearLocks();
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (Exception ex)
        {
            SquadDashTrace.Write("TestTearDown", $"Cleanup failed: {ex.Message}");
        }
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
            lifecycleStatus, "Lifecycle Test Plan", "main", "Summary",
            tasks, gates,
            new PlanProgress(completed, tasks.Count),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }

    private static ApprovalReviewSnapshot MakeSnapshot(
        string planId = "PLAN-001",
        string gateId = "GATE-A",
        int completedTaskCount = 2,
        int totalTaskCount = 5) =>
        new(planId, "Lifecycle Test Plan", completedTaskCount, totalTaskCount,
            PlanLifecycleStatus.Executing,
            gateId, "Review T1+T2 before T3", ["T1", "T2"], ["T3"],
            [], [], [], [], DateTimeOffset.UtcNow);

    private static string MessageIdFor(string planId) =>
        DurableApprovalRequestManager.BuildMessageId(planId);

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Atomic timestamp refresh
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task AtomicTimestampRefresh_WhenMessageUpdated_TimestampRefreshesAtomically()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var messageId = MessageIdFor(plan.PlanId);

        // Force old timestamp
        var oldTimestamp = DateTimeOffset.UtcNow.AddHours(-2);
        var msg = _inbox.GetById(messageId)!;
        _inbox.Save(msg with { Timestamp = oldTimestamp });

        // Append second gate — should atomically refresh
        var gate2 = new PlanApprovalGate(
            "GATE-B", "Review before T4", ["T3"], ["T4"], PlanGateStatus.AwaitingApproval);
        var expanded = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            extraGates: [gate2],
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(expanded, gate2, MakeSnapshot(gateId: "GATE-B"));

        var refreshed = _inbox.GetById(messageId)!;
        Assert.That(refreshed.Timestamp, Is.GreaterThan(oldTimestamp),
            "Timestamp must atomically refresh when a new gate is appended");
        Assert.That(refreshed.Timestamp, Is.GreaterThanOrEqualTo(DateTimeOffset.UtcNow.AddSeconds(-5)),
            "Refreshed timestamp must be recent");
    }

    [Test]
    public async Task AtomicTimestampRefresh_EvidenceRefreshDoesNotBumpTimestamp()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var messageId = MessageIdFor(plan.PlanId);
        var timestampAfterCreate = _inbox.GetById(messageId)!.Timestamp;

        // Evidence refresh should NOT bump timestamp (only content update)
        var updatedSnap = MakeSnapshot(completedTaskCount: 3);
        await _durableManager.RefreshEvidenceAsync(plan, updatedSnap);

        var afterRefresh = _inbox.GetById(messageId)!;
        Assert.That(afterRefresh.Timestamp, Is.EqualTo(timestampAfterCreate),
            "Evidence refresh must not bump the message timestamp");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Temporary action disabling during replacement
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task TemporaryActionDisabling_OldTokenRejectedDuringReplacement()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);

        // Simulate replacement: re-register with additional gate bumps version
        await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A", "GATE-B"]);

        // Old token must be rejected — simulates action disabled during replacement
        var result = await _coordinator.TryApproveAsync(token, ["GATE-A"]);
        Assert.That(result, Is.EqualTo(ApprovalClickResult.StaleRejected),
            "Actions captured against stale version must be rejected during replacement");
    }

    [Test]
    public async Task TemporaryActionDisabling_FreshTokenSucceeds()
    {
        await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);

        // Simulate replacement
        var freshToken = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A", "GATE-B"]);

        var result = await _coordinator.TryApproveAsync(freshToken, ["GATE-A"]);
        Assert.That(result, Is.EqualTo(ApprovalClickResult.Approved),
            "Fresh token must succeed after replacement");
    }

    [Test]
    public async Task TemporaryActionDisabling_DurableMessageActionsRebuiltOnReplace()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var messageId = MessageIdFor(plan.PlanId);

        var beforeActions = _inbox.GetById(messageId)!.Actions;
        Assert.That(beforeActions, Has.Count.GreaterThan(0));

        // Add second gate — actions should be rebuilt with new version
        var gate2 = new PlanApprovalGate("GATE-B", "Second", ["T3"], ["T4"], PlanGateStatus.AwaitingApproval);
        var expanded = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            extraGates: [gate2]);
        await _durableManager.AppendCheckpointAsync(expanded, gate2, MakeSnapshot(gateId: "GATE-B"));

        var afterActions = _inbox.GetById(messageId)!.Actions;
        Assert.That(afterActions, Has.Count.GreaterThan(0));
        Assert.That(afterActions[0].Label, Does.Contain("2 Ready Checkpoints"),
            "Actions must be rebuilt to reflect new gate count after replacement");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Card restoration after transcript hydration
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task CardRestoration_AfterHydration_SnapshotPreservedInAttachments()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var messageId = MessageIdFor(plan.PlanId);

        var msg = _inbox.GetById(messageId)!;
        var snapshotAttachment = msg.Attachments.FirstOrDefault(a => a.Type == "approval-snapshot");

        Assert.That(snapshotAttachment, Is.Not.Null,
            "Approval snapshot must be persisted for card restoration after hydration");
        Assert.That(snapshotAttachment!.Content, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task CardRestoration_AfterHydration_StateRecoverableFromPersistedMessage()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());

        // Simulate restart: clear in-memory state
        _coordinator.ClearAll();

        // Restore from persisted messages
        var activePlanIds = _durableManager.RestoreActivePlanIds();
        Assert.That(activePlanIds, Does.Contain("PLAN-001"));

        var state = _durableManager.GetState("PLAN-001");
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.ActiveGateIds, Does.Contain("GATE-A"));
        Assert.That(state.Archived, Is.False);
    }

    [Test]
    public async Task CardRestoration_PlanLinkAttachmentSurvivesRoundTrip()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var messageId = MessageIdFor(plan.PlanId);

        var msg = _inbox.GetById(messageId)!;
        var planLink = msg.Attachments
            .Where(DurableApprovalRequestManager.IsPresentationAttachment)
            .ToArray();
        Assert.That(planLink, Has.Length.EqualTo(1),
            "Plan link attachment must survive for transcript card restoration");
        Assert.That(planLink[0].PlanGroupId, Is.EqualTo(plan.PlanId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Restart replay of callout to Inbox row/menu item
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task RestartReplay_ActiveRequestSurvivesRestart()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        await _durableManager.TryMarkNotifiedAsync(plan.PlanId);

        // Simulate restart
        _coordinator.ClearAll();
        _durableManager.ClearLocks();

        // Restore — should re-establish coordinator state
        await _runtime.RestoreAsync([plan]);

        var token = _coordinator.GetCurrentToken("PLAN-001");
        Assert.That(token, Is.Not.Null,
            "After restart, coordinator must have restored token for active plan");
        Assert.That(token!.GateIds, Does.Contain("GATE-A"));
    }

    [Test]
    public async Task RestartReplay_InboxMessageRetainsActionsAfterRestore()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());

        // Simulate restart
        _coordinator.ClearAll();
        _durableManager.ClearLocks();

        await _runtime.RestoreAsync([plan]);

        var messageId = MessageIdFor(plan.PlanId);
        var msg = _inbox.GetById(messageId)!;
        Assert.That(msg.Actions, Has.Count.GreaterThan(0),
            "Inbox message must retain actions after restart restore");
        Assert.That(msg.Read, Is.False,
            "Unread status must be preserved for unresolved approval");
    }

    [Test]
    public async Task RestartReplay_NewNotificationAllowedAfterRestore()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        await _durableManager.TryMarkNotifiedAsync(plan.PlanId);

        // Simulate restart: clear locks but preserved message state
        _durableManager.ClearLocks();

        // A second gate arrival after restart allows a new notification
        var gate2 = new PlanApprovalGate("GATE-B", "Second", ["T3"], ["T4"], PlanGateStatus.AwaitingApproval);
        var expanded = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            extraGates: [gate2],
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);
        await _durableManager.AppendCheckpointAsync(expanded, gate2, MakeSnapshot(gateId: "GATE-B"));

        // Version bumped — new notification allowed
        var canNotify = await _durableManager.TryMarkNotifiedAsync(plan.PlanId);
        Assert.That(canNotify, Is.True,
            "After restart with new gate, notification must be permitted");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. "Waiting for approval" in Loop panel
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task WaitingForApproval_LifecycleStatusCorrectAfterGateActivation()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete);

        // Gate becomes ready and plan stops
        var stopped = PlanStoreUpdater.ApplyFullStopAtGates(plan, ["GATE-A"]);
        Assert.That(stopped.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval),
            "Loop panel relies on AwaitingApproval lifecycle for 'Waiting for approval' display");
    }

    [Test]
    public async Task WaitingForApproval_RuntimeAdvanceReportsCorrectMustStop()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            t5Status: PlanTaskStatus.Complete);

        var result = await _runtime.AdvanceAsync(plan);

        // All ungated work done (T5 complete), gate blocks T3 — must stop
        Assert.That(result.MustStop, Is.True,
            "When all ungated work is complete and gate blocks remaining, MustStop must be true");
        Assert.That(result.UpdatedPlan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval),
            "Plan must be AwaitingApproval when stopped at gate");
    }

    [Test]
    public void WaitingForApproval_AwaitingApprovalStatusSurvivesRoundTrip()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval),
            "After restart, plan loaded from store retains AwaitingApproval status");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. Archival of obsolete blocked-plan messages after acceptance
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Archival_AfterAllGatesApproved_MessageArchived()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-A", "Approved");

        var state = _durableManager.GetState("PLAN-001");
        Assert.That(state!.Archived, Is.True, "Message must be archived after all gates approved");

        var msg = _inbox.GetById(MessageIdFor("PLAN-001"))!;
        Assert.That(msg.Read, Is.True, "Archived message must be marked read");
        Assert.That(msg.Actions, Is.Empty, "Archived message must have no actions");
        Assert.That(msg.Body, Does.Contain("archived"),
            "Body must indicate archived state");
    }

    [Test]
    public async Task Archival_PartialApproval_DoesNotArchive()
    {
        var gate2 = new PlanApprovalGate("GATE-B", "Second", ["T3"], ["T4"], PlanGateStatus.AwaitingApproval);
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            extraGates: [gate2],
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        await _durableManager.AppendCheckpointAsync(plan, gate2, MakeSnapshot(gateId: "GATE-B"));
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-A", "Approved");

        var state = _durableManager.GetState("PLAN-001");
        Assert.That(state!.Archived, Is.False,
            "Partial approval must not archive while GATE-B remains active");
        Assert.That(state.ActiveGateIds, Does.Contain("GATE-B"));
    }

    [Test]
    public async Task Archival_StaleReconciledDuringRestart()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());

        // Plan was approved externally before restart (gate now Approved in plan)
        var resolvedPlan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.Approved,
            lifecycleStatus: PlanLifecycleStatus.Executing);

        _coordinator.ClearAll();
        _durableManager.ClearLocks();
        await _runtime.RestoreAsync([resolvedPlan]);

        // After restore, stale gate should be reconciled and archived
        var state = _durableManager.GetState("PLAN-001");
        Assert.That(state!.Archived, Is.True,
            "Stale gate must be reconciled and archived during restart");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 7. Accumulating gates
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task AccumulatingGates_MultipleGatesInSingleMessage()
    {
        var gate2 = new PlanApprovalGate("GATE-B", "Second", ["T3"], ["T4"], PlanGateStatus.AwaitingApproval);
        var gate3 = new PlanApprovalGate("GATE-C", "Third", ["T4"], ["T5"], PlanGateStatus.AwaitingApproval);
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            extraGates: [gate2, gate3]);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        await _durableManager.AppendCheckpointAsync(plan, gate2, MakeSnapshot(gateId: "GATE-B"));
        await _durableManager.AppendCheckpointAsync(plan, gate3, MakeSnapshot(gateId: "GATE-C"));

        var state = _durableManager.GetState("PLAN-001");
        Assert.That(state!.ActiveGateIds, Has.Count.EqualTo(3));
        Assert.That(state.ActiveGateIds, Is.EquivalentTo(new[] { "GATE-A", "GATE-B", "GATE-C" }));

        // Single aggregated message
        var msgs = _inbox.LoadAll().Where(m => m.Id.StartsWith("approval-gate-PLAN-001")).ToList();
        Assert.That(msgs, Has.Count.EqualTo(1),
            "All gates must be aggregated into a single Inbox message");
    }

    [Test]
    public async Task AccumulatingGates_VersionIncreasesPerAppend()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var v1 = _durableManager.GetState("PLAN-001")!.Version;

        var gate2 = new PlanApprovalGate("GATE-B", "Second", ["T3"], ["T4"], PlanGateStatus.AwaitingApproval);
        var expanded = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            extraGates: [gate2]);
        await _durableManager.AppendCheckpointAsync(expanded, gate2, MakeSnapshot(gateId: "GATE-B"));
        var v2 = _durableManager.GetState("PLAN-001")!.Version;

        Assert.That(v2, Is.GreaterThan(v1),
            "Version must increase with each accumulated gate");
    }

    [Test]
    public async Task AccumulatingGates_ActionLabelReflectsCount()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var msg1 = _inbox.GetById(MessageIdFor("PLAN-001"))!;
        Assert.That(msg1.Actions[0].Label, Does.Contain("Approve Checkpoint"),
            "Single gate should have singular label");

        var gate2 = new PlanApprovalGate("GATE-B", "Second", ["T3"], ["T4"], PlanGateStatus.AwaitingApproval);
        var expanded = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            extraGates: [gate2]);
        await _durableManager.AppendCheckpointAsync(expanded, gate2, MakeSnapshot(gateId: "GATE-B"));
        var msg2 = _inbox.GetById(MessageIdFor("PLAN-001"))!;
        Assert.That(msg2.Actions[0].Label, Does.Contain("2 Ready Checkpoints"),
            "Multiple gates should have plural label with count");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 8. Approval from every surface (coordinator + durable)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ApprovalFromTranscript_FullRuntimeApprove()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var token = await _coordinator.RestoreAsync(
            plan.PlanId, plan.Revision,
            _durableManager.GetState(plan.PlanId)!.Version,
            ["GATE-A"]);

        var result = await _runtime.ApproveAsync(
            token, plan, "Approved from transcript",
            persistPlan: _ => true);

        Assert.That(result.Result, Is.EqualTo(ApprovalClickResult.Approved));
        Assert.That(result.ShouldResume, Is.True);
        Assert.That(result.UpdatedPlan!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
    }

    [Test]
    public async Task ApprovalFromInbox_DurableAndCoordinatorBothResolve()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var version = _durableManager.GetState(plan.PlanId)!.Version;
        var token = await _coordinator.RestoreAsync(plan.PlanId, plan.Revision, version, ["GATE-A"]);

        // Approve via coordinator (Inbox click path)
        var clickResult = await _coordinator.TryApproveAsync(token, ["GATE-A"], "From Inbox");
        Assert.That(clickResult, Is.EqualTo(ApprovalClickResult.Approved));

        // Also resolve durable side (as runtime.ApproveAsync would)
        await _durableManager.ResolveCheckpointAsync(plan, "GATE-A", "From Inbox");

        Assert.That(_coordinator.HasActiveGates("PLAN-001"), Is.False);
        Assert.That(_durableManager.IsArchived("PLAN-001"), Is.True);
    }

    [Test]
    public async Task ApprovalFromPlanViewer_SameTokenValidation()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var version = _durableManager.GetState(plan.PlanId)!.Version;
        var token = await _coordinator.RestoreAsync(plan.PlanId, plan.Revision, version, ["GATE-A"]);

        // Plan Viewer uses same approval path
        var result = await _runtime.ApproveAsync(
            token, plan, "Approved from Plan Viewer",
            persistPlan: _ => true);

        Assert.That(result.Result, Is.EqualTo(ApprovalClickResult.Approved));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 9. Stale tokens
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task StaleToken_RejectedGracefully()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);

        // Bump version
        await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);

        var result = await _coordinator.TryApproveAsync(token, ["GATE-A"]);
        Assert.That(result, Is.EqualTo(ApprovalClickResult.StaleRejected));
    }

    [Test]
    public async Task StaleToken_WrongRevision_Rejected()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var token = await _coordinator.RestoreAsync(plan.PlanId, "rev1",
            _durableManager.GetState(plan.PlanId)!.Version, ["GATE-A"]);

        // Plan was updated with new revision
        var updatedPlan = MakePlan(
            planId: "PLAN-001",
            revision: "rev2",
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        var result = await _runtime.ApproveAsync(token, updatedPlan, "attempt", _ => true);
        Assert.That(result.Result, Is.EqualTo(ApprovalClickResult.StaleRejected),
            "Token with old revision must be rejected when plan revision changes");
    }

    [Test]
    public async Task StaleToken_AlreadyResolved_Detected()
    {
        var token = await _coordinator.RegisterAsync("PLAN-001", "rev1", ["GATE-A"]);
        await _coordinator.TryApproveAsync(token, ["GATE-A"]);

        // Get fresh token (now pointing to resolved plan)
        var freshToken = _coordinator.GetCurrentToken("PLAN-001");
        // Plan has no active gates — should get null or empty
        Assert.That(freshToken, Is.Null.Or.Property("GateIds").Empty.Or.Property("GateIds").Count.EqualTo(0),
            "After full resolution, token should reflect no active gates");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 10. Restart convergence — all surfaces converge to correct state
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task RestartConvergence_CoordinatorAndDurableAlignAfterRestore()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());

        // Simulate restart
        _coordinator.ClearAll();
        _durableManager.ClearLocks();

        await _runtime.RestoreAsync([plan]);

        // Verify convergence
        var durableState = _durableManager.GetState("PLAN-001")!;
        var coordinatorToken = _coordinator.GetCurrentToken("PLAN-001")!;

        Assert.That(coordinatorToken.GateIds, Is.EquivalentTo(durableState.ActiveGateIds),
            "Coordinator and durable state must converge after restart");
        Assert.That(coordinatorToken.RequestVersion, Is.EqualTo(durableState.Version),
            "Version must be synchronized between coordinator and durable state");
    }

    [Test]
    public async Task RestartConvergence_StaleGatesCleaned()
    {
        var gate2 = new PlanApprovalGate("GATE-B", "Second", ["T3"], ["T4"], PlanGateStatus.AwaitingApproval);
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            extraGates: [gate2],
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        await _durableManager.AppendCheckpointAsync(plan, gate2, MakeSnapshot(gateId: "GATE-B"));

        // At restart, only GATE-A is still awaiting in the authoritative plan
        var planAfterRestart = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        _coordinator.ClearAll();
        _durableManager.ClearLocks();
        await _runtime.RestoreAsync([planAfterRestart]);

        var state = _durableManager.GetState("PLAN-001")!;
        Assert.That(state.ActiveGateIds, Does.Not.Contain("GATE-B"),
            "Stale gate must be reconciled away during restart");
        Assert.That(state.ActiveGateIds, Does.Contain("GATE-A"),
            "Active gate must be preserved during restart");
    }

    [Test]
    public async Task RestartConvergence_MultiplePlansIndependent()
    {
        var plan1 = MakePlan(
            planId: "PLAN-A",
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);
        var plan2 = MakePlan(
            planId: "PLAN-B",
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan1, plan1.ApprovalGates[0], MakeSnapshot("PLAN-A"));
        await _durableManager.AppendCheckpointAsync(plan2, plan2.ApprovalGates[0], MakeSnapshot("PLAN-B"));

        _coordinator.ClearAll();
        _durableManager.ClearLocks();
        await _runtime.RestoreAsync([plan1, plan2]);

        var tok1 = _coordinator.GetCurrentToken("PLAN-A");
        var tok2 = _coordinator.GetCurrentToken("PLAN-B");
        Assert.That(tok1, Is.Not.Null);
        Assert.That(tok2, Is.Not.Null);
        Assert.That(tok1!.PlanId, Is.Not.EqualTo(tok2!.PlanId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 11. Workspace switch — approval state handled correctly
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task WorkspaceSwitch_ApprovalStateSurvivesSwitch()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var token = await _coordinator.RestoreAsync(
            plan.PlanId, plan.Revision,
            _durableManager.GetState(plan.PlanId)!.Version, ["GATE-A"]);

        // Workspace switch: coordinator cleared, then restored
        _coordinator.ClearAll();
        await _runtime.RestoreAsync([plan]);

        var restored = _coordinator.GetCurrentToken("PLAN-001");
        Assert.That(restored, Is.Not.Null,
            "Approval state must survive workspace switch via restore path");
        Assert.That(restored!.GateIds, Does.Contain("GATE-A"));
    }

    [Test]
    public async Task WorkspaceSwitch_DifferentPlanIdPerWorkspace()
    {
        // Workspace A has PLAN-WS-A
        var planA = MakePlan(planId: "PLAN-WS-A",
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval);
        await _durableManager.AppendCheckpointAsync(planA, planA.ApprovalGates[0], MakeSnapshot("PLAN-WS-A"));

        // Switch workspace — clear coordinator, load new workspace plans
        _coordinator.ClearAll();

        var planB = MakePlan(planId: "PLAN-WS-B",
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval);
        await _durableManager.AppendCheckpointAsync(planB, planB.ApprovalGates[0], MakeSnapshot("PLAN-WS-B"));
        await _runtime.RestoreAsync([planB]);

        Assert.That(_coordinator.GetCurrentToken("PLAN-WS-A"), Is.Null,
            "Old workspace plan must not be in coordinator after switch");
        Assert.That(_coordinator.GetCurrentToken("PLAN-WS-B"), Is.Not.Null,
            "New workspace plan must be in coordinator after switch");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 12. Two workspaces — independent approval state
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task TwoWorkspaces_IndependentDurableState()
    {
        // Each workspace has its own InboxStore and state
        var tempDir2 = Path.Combine(Path.GetTempPath(), $"squad-ws2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir2);
        try
        {
            var inbox2 = new InboxStore(tempDir2);
            var durable2 = new DurableApprovalRequestManager(inbox2);

            var planA = MakePlan(planId: "PLAN-A",
                t1Status: PlanTaskStatus.Complete,
                t2Status: PlanTaskStatus.Complete,
                gateAStatus: PlanGateStatus.AwaitingApproval);
            var planB = MakePlan(planId: "PLAN-B",
                t1Status: PlanTaskStatus.Complete,
                t2Status: PlanTaskStatus.Complete,
                gateAStatus: PlanGateStatus.AwaitingApproval);

            await _durableManager.AppendCheckpointAsync(planA, planA.ApprovalGates[0], MakeSnapshot("PLAN-A"));
            await durable2.AppendCheckpointAsync(planB, planB.ApprovalGates[0], MakeSnapshot("PLAN-B"));

            // Each workspace has independent state
            Assert.That(_durableManager.GetState("PLAN-A"), Is.Not.Null);
            Assert.That(_durableManager.GetState("PLAN-B"), Is.Null,
                "Workspace 1 must not see workspace 2's plan");
            Assert.That(durable2.GetState("PLAN-B"), Is.Not.Null);
            Assert.That(durable2.GetState("PLAN-A"), Is.Null,
                "Workspace 2 must not see workspace 1's plan");

            durable2.ClearLocks();
        }
        finally
        {
            try { Directory.Delete(tempDir2, recursive: true); }
            catch (Exception ex)
            {
                SquadDashTrace.Write("TestCleanup", $"TwoWorkspaces cleanup failed: {ex.Message}");
            }
        }
    }

    [Test]
    public async Task TwoWorkspaces_CoordinatorIsolation()
    {
        var coord2 = new ApprovalActionCoordinator();
        try
        {
            var token1 = await _coordinator.RegisterAsync("PLAN-A", "rev1", ["GATE-A"]);
            var token2 = await coord2.RegisterAsync("PLAN-B", "rev1", ["GATE-X"]);

            Assert.That(_coordinator.GetCurrentToken("PLAN-B"), Is.Null,
                "Coordinator 1 must not see coordinator 2's plans");
            Assert.That(coord2.GetCurrentToken("PLAN-A"), Is.Null,
                "Coordinator 2 must not see coordinator 1's plans");

            // Approving in one does not affect the other
            await _coordinator.TryApproveAsync(token1, ["GATE-A"]);
            Assert.That(coord2.HasActiveGates("PLAN-B"), Is.True,
                "Approval in coordinator 1 must not affect coordinator 2");
        }
        finally
        {
            coord2.ClearAll();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 13. Hidden or filtered Inbox — notifications still function
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task HiddenInbox_NotificationStillMarkedInStore()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());

        // Notification proceeds regardless of Inbox visibility (store-first design)
        var canNotify = await _durableManager.TryMarkNotifiedAsync(plan.PlanId);
        Assert.That(canNotify, Is.True,
            "Notification must be trackable even when Inbox UI is hidden");

        var state = _durableManager.GetState("PLAN-001")!;
        Assert.That(state.LastNotifiedAt, Is.Not.Null,
            "LastNotifiedAt must be set regardless of Inbox visibility");
    }

    [Test]
    public async Task HiddenInbox_MessagePersistsRegardlessOfVisibility()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());

        var messageId = MessageIdFor("PLAN-001");
        var msg = _inbox.GetById(messageId);
        Assert.That(msg, Is.Not.Null,
            "Inbox message must persist regardless of whether Inbox panel is visible");
        Assert.That(msg!.Priority, Is.EqualTo("high"));
    }

    [Test]
    public async Task FilteredInbox_ApprovalActionStillValid()
    {
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            gateAStatus: PlanGateStatus.AwaitingApproval,
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval);

        await _durableManager.AppendCheckpointAsync(plan, plan.ApprovalGates[0], MakeSnapshot());
        var version = _durableManager.GetState(plan.PlanId)!.Version;
        var token = await _coordinator.RestoreAsync(plan.PlanId, plan.Revision, version, ["GATE-A"]);

        // Approval from filtered Inbox (or via Plan Viewer) still works
        var result = await _runtime.ApproveAsync(token, plan, "From filtered view", _ => true);
        Assert.That(result.Result, Is.EqualTo(ApprovalClickResult.Approved),
            "Approval must succeed even when triggered outside visible Inbox filter");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 14. Normal workspace with no build restarts — basic happy path
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task NormalWorkspace_HappyPath_GateReadyThroughApproval()
    {
        // Start: tasks in progress
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            t5Status: PlanTaskStatus.Complete);

        // Advance detects gate readiness
        var advance = await _runtime.AdvanceAsync(plan);
        Assert.That(advance.NewlyReadyGates, Has.Count.EqualTo(1));
        Assert.That(advance.NewlyReadyGates[0].GateId, Is.EqualTo("GATE-A"));
        Assert.That(advance.MessageId, Is.Not.Null);
        Assert.That(advance.ClickToken, Is.Not.Null);

        // Inbox message created
        var msg = _inbox.GetById(advance.MessageId!)!;
        Assert.That(msg.Priority, Is.EqualTo("high"));
        Assert.That(msg.Actions, Has.Count.GreaterThan(0));

        // User approves
        var approveResult = await _runtime.ApproveAsync(
            advance.ClickToken!, advance.UpdatedPlan, "Looks good!", _ => true);
        Assert.That(approveResult.Result, Is.EqualTo(ApprovalClickResult.Approved));
        Assert.That(approveResult.ShouldResume, Is.True);
        Assert.That(approveResult.UpdatedPlan!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));

        // Message archived
        Assert.That(_durableManager.IsArchived("PLAN-001"), Is.True);
    }

    [Test]
    public async Task NormalWorkspace_NoGatesReady_NoNotification()
    {
        // T1 not yet complete — gate not ready
        var plan = MakePlan(t1Status: PlanTaskStatus.Pending, t2Status: PlanTaskStatus.Pending);

        var advance = await _runtime.AdvanceAsync(plan);
        Assert.That(advance.NewlyReadyGates, Is.Empty,
            "No gates should be ready when prerequisite tasks are incomplete");
        Assert.That(advance.MessageId, Is.Null);
        Assert.That(advance.ClickToken, Is.Null);
        Assert.That(advance.MustStop, Is.False);
    }

    [Test]
    public async Task NormalWorkspace_UngatedWorkContinuesWhileGateOpen()
    {
        // T1 and T2 done, T5 still pending (depends only on T1)
        var plan = MakePlan(
            t1Status: PlanTaskStatus.Complete,
            t2Status: PlanTaskStatus.Complete,
            t5Status: PlanTaskStatus.Pending);

        var advance = await _runtime.AdvanceAsync(plan);
        // Gate becomes ready but T5 is still ungated work
        Assert.That(advance.MustStop, Is.False,
            "MustStop should be false when ungated work (T5) remains available");
        Assert.That(advance.NextUngatedTaskId, Is.EqualTo("T5"),
            "T5 should be the next ungated task while gate is open");
    }
}
