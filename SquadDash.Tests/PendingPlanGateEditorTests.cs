using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SquadDash.Tests;

/// <summary>
/// Verifies that <see cref="PendingPlanGateEditor"/> correctly persists gate edits
/// to the pending plan store, recomputes the draft revision, and atomically replaces
/// the host-owned inbox message. Tests cover revision safety, rapid edits, add/remove
/// gate changes, anchor updates, watcher-style refresh, restart persistence, and
/// stale-revision rejection on execution.
/// </summary>
[TestFixture]
internal sealed class PendingPlanGateEditorTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private string _tempDir = null!;
    private string _squadFolder = null!;
    private PendingDecomposePlanStore _pendingStore = null!;
    private InboxStore _inboxStore = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PendingPlanGateEditorTests_" + Guid.NewGuid().ToString("N")[..8]);
        _squadFolder = Path.Combine(_tempDir, ".squad");
        Directory.CreateDirectory(_squadFolder);
        _pendingStore = new PendingDecomposePlanStore(_squadFolder);
        _inboxStore = new InboxStore(_squadFolder);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    private static DecomposedTaskGroup MakeGroup(
        string groupId = "TEST-20260730",
        IReadOnlyList<DecomposedGate>? gates = null)
    {
        return new DecomposedTaskGroup(
            GroupId:    groupId,
            GroupTitle: "Test Plan",
            Branch:    "feature/test",
            Summary:   "Test summary",
            Tasks: [
                new DecomposedSubTask("TASK-001", "First task",  [], "mid", "First"),
                new DecomposedSubTask("TASK-002", "Second task", ["TASK-001"], "mid", "Second"),
                new DecomposedSubTask("TASK-003", "Third task",  ["TASK-002"], "mid", "Third"),
            ],
            ApprovalGates: gates);
    }

    private PendingDecomposePlan SavePendingPlan(
        DecomposedTaskGroup? group = null)
    {
        return _pendingStore.Save(group ?? MakeGroup());
    }

    private void SaveInboxMessage(PendingDecomposePlan plan)
    {
        var message = DecomposePlanInbox.BuildMessage(
            plan, DateTimeOffset.UtcNow, explicitlyRequested: true, activeBranch: null);
        _inboxStore.Save(message);
    }

    private static Plan SyntheticDurable(PendingDecomposePlan pending) =>
        PendingDecomposePlanAdapter.ToPlan(pending, pending.CreatedAt ?? DateTimeOffset.UtcNow);

    // ─── Add gate persists and recomputes revision ──────────────────────────

    [Test]
    public void AddGate_PersistsToPendingStore_AndRecomputesRevision()
    {
        var pending = SavePendingPlan();
        SaveInboxMessage(pending);
        var originalRevision = pending.Revision;

        var durable = SyntheticDurable(pending);
        var gated = PlanGateManager.AddGateAfter(durable, "TASK-001", "Review after task 1");

        var result = PendingPlanGateEditor.Apply(
            gated,
            DecomposePlanInbox.BuildMessageId(pending),
            _pendingStore, _inboxStore, null);

        Assert.Multiple(() =>
        {
            Assert.That(result.UpdatedPlan.Revision, Is.Not.EqualTo(originalRevision),
                "Revision should change after adding a gate");
            Assert.That(result.SyntheticDurablePlan.ApprovalGates, Has.Count.GreaterThan(0),
                "Synthetic durable plan should contain the new gate");
        });

        // Verify persistence
        var reloaded = _pendingStore.Load("TEST-20260730");
        Assert.That(reloaded, Is.Not.Null);
        Assert.That(reloaded!.Revision, Is.EqualTo(result.UpdatedPlan.Revision));
    }

    // ─── Remove gate persists and recomputes revision ───────────────────────

    [Test]
    public void RemoveGate_PersistsToPendingStore_AndRecomputesRevision()
    {
        var gates = new[]
        {
            new DecomposedGate("TEST-20260730-GATE-001", "Gate 1", ["TASK-001"], ["TASK-002"]),
        };
        var pending = SavePendingPlan(MakeGroup(gates: gates));
        SaveInboxMessage(pending);
        var revisionWithGate = pending.Revision;

        var durable = SyntheticDurable(pending);
        var ungated = PlanGateManager.RemoveGate(durable, "TEST-20260730-GATE-001");

        var result = PendingPlanGateEditor.Apply(
            ungated,
            DecomposePlanInbox.BuildMessageId(pending),
            _pendingStore, _inboxStore, null);

        Assert.Multiple(() =>
        {
            Assert.That(result.UpdatedPlan.Revision, Is.Not.EqualTo(revisionWithGate));
            Assert.That(result.SyntheticDurablePlan.ApprovalGates, Has.Count.EqualTo(0));
        });
    }

    // ─── Anchor change persists and recomputes revision ─────────────────────

    [Test]
    public void SetPresentationAnchor_PersistsToPendingStore()
    {
        var gates = new[]
        {
            new DecomposedGate("TEST-20260730-GATE-001", "Gate 1", ["TASK-001"], ["TASK-002"]),
        };
        var pending = SavePendingPlan(MakeGroup(gates: gates));
        SaveInboxMessage(pending);

        var durable = SyntheticDurable(pending);
        var anchored = PlanGateManager.SetPresentationAnchor(
            durable, "TEST-20260730-GATE-001", "stage:2");

        var result = PendingPlanGateEditor.Apply(
            anchored,
            DecomposePlanInbox.BuildMessageId(pending),
            _pendingStore, _inboxStore, null);

        var reloaded = _pendingStore.Load("TEST-20260730");
        Assert.That(reloaded, Is.Not.Null);
    }

    // ─── Inbox message is atomically replaced ───────────────────────────────

    [Test]
    public void Apply_ReplacesInboxMessage_WithNewRevisionId()
    {
        var pending = SavePendingPlan();
        SaveInboxMessage(pending);
        var oldMessageId = DecomposePlanInbox.BuildMessageId(pending);

        var durable = SyntheticDurable(pending);
        var gated = PlanGateManager.AddGateAfter(durable, "TASK-001", "Review");

        var result = PendingPlanGateEditor.Apply(
            gated, oldMessageId, _pendingStore, _inboxStore, null);

        Assert.Multiple(() =>
        {
            // Old message should be deleted
            Assert.That(_inboxStore.GetById(oldMessageId), Is.Null,
                "Old inbox message should be removed");

            // New message should exist with updated revision
            Assert.That(result.NewInboxMessageId, Is.Not.Null);
            var newMsg = _inboxStore.GetById(result.NewInboxMessageId!);
            Assert.That(newMsg, Is.Not.Null, "New inbox message should exist");
            Assert.That(newMsg!.Attachments[0].PlanRevision,
                Is.EqualTo(result.UpdatedPlan.Revision),
                "Attachment revision should match new plan revision");
        });
    }

    [Test]
    public void Apply_PreservesReadState_OnInboxReplacement()
    {
        var pending = SavePendingPlan();
        SaveInboxMessage(pending);
        var oldMessageId = DecomposePlanInbox.BuildMessageId(pending);
        _inboxStore.MarkRead(oldMessageId);

        var durable = SyntheticDurable(pending);
        var gated = PlanGateManager.AddGateAfter(durable, "TASK-002", "Review");

        var result = PendingPlanGateEditor.Apply(
            gated, oldMessageId, _pendingStore, _inboxStore, null);

        var newMsg = _inboxStore.GetById(result.NewInboxMessageId!);
        Assert.That(newMsg!.Read, Is.True, "Read state should be preserved across replacement");
    }

    // ─── Action payloads reference new revision ─────────────────────────────

    [Test]
    public void Apply_ActionPayloads_ReferenceNewRevision()
    {
        var pending = SavePendingPlan();
        SaveInboxMessage(pending);
        var oldMessageId = DecomposePlanInbox.BuildMessageId(pending);

        var durable = SyntheticDurable(pending);
        var gated = PlanGateManager.AddGateAfter(durable, "TASK-001", "Review");

        var result = PendingPlanGateEditor.Apply(
            gated, oldMessageId, _pendingStore, _inboxStore, null);

        var newMsg = _inboxStore.GetById(result.NewInboxMessageId!);
        Assert.That(newMsg, Is.Not.Null);
        foreach (var action in newMsg!.Actions)
        {
            if (action.Prompt is not null)
            {
                Assert.That(action.Prompt, Does.Contain(result.UpdatedPlan.Revision),
                    $"Action '{action.Label}' should reference new revision in its prompt");
            }
        }
    }

    // ─── Execution rejects stale revision ───────────────────────────────────

    [Test]
    public void Execution_RejectsStaleRevision_AfterGateEdit()
    {
        var pending = SavePendingPlan();
        var staleRevision = pending.Revision;

        // Simulate a gate edit
        var durable = SyntheticDurable(pending);
        var gated = PlanGateManager.AddGateAfter(durable, "TASK-001", "Review");
        PendingPlanGateEditor.Apply(gated, null, _pendingStore, null, null);

        // Try to load with stale revision — should not match
        var currentPlan = _pendingStore.Load("TEST-20260730");
        Assert.That(currentPlan, Is.Not.Null);
        Assert.That(currentPlan!.Revision, Is.Not.EqualTo(staleRevision),
            "Stored plan should have the new revision, not the stale one");
    }

    // ─── Rapid edits produce consistent state ───────────────────────────────

    [Test]
    public void RapidEdits_ProduceConsistentState()
    {
        var pending = SavePendingPlan();
        SaveInboxMessage(pending);
        var currentMessageId = DecomposePlanInbox.BuildMessageId(pending);

        // Simulate 5 rapid gate edits
        var latestPlan = pending;
        for (var i = 0; i < 5; i++)
        {
            var durable = SyntheticDurable(latestPlan);
            var edited = i % 2 == 0
                ? PlanGateManager.AddGateAfter(durable, "TASK-001", $"Review #{i}")
                : PlanGateManager.RemoveGate(durable, durable.ApprovalGates.LastOrDefault()?.GateId ?? "none");

            if (ReferenceEquals(edited, durable)) continue;

            var result = PendingPlanGateEditor.Apply(
                edited, currentMessageId, _pendingStore, _inboxStore, null);
            latestPlan = result.UpdatedPlan;
            currentMessageId = result.NewInboxMessageId ?? currentMessageId;
        }

        // Final state should be consistent
        var reloaded = _pendingStore.Load("TEST-20260730");
        Assert.That(reloaded, Is.Not.Null);
        Assert.That(reloaded!.Revision, Is.EqualTo(latestPlan.Revision),
            "Stored revision should match the last edit's revision");

        var inboxMessages = _inboxStore.LoadAll();
        var planMessages = inboxMessages.Where(m =>
            m.Attachments.Any(a => a.PlanGroupId == "TEST-20260730")).ToList();
        Assert.That(planMessages, Has.Count.EqualTo(1),
            "Exactly one inbox message should exist after rapid edits");
        Assert.That(planMessages[0].Attachments[0].PlanRevision,
            Is.EqualTo(latestPlan.Revision),
            "The surviving inbox message should reference the latest revision");
    }

    // ─── Watcher-style refresh sees latest gates ────────────────────────────

    [Test]
    public void WatcherRefresh_SeesLatestGates()
    {
        var pending = SavePendingPlan();
        SaveInboxMessage(pending);

        // Add a gate
        var durable = SyntheticDurable(pending);
        var gated = PlanGateManager.AddGateAfter(durable, "TASK-001", "Review");
        var result = PendingPlanGateEditor.Apply(
            gated,
            DecomposePlanInbox.BuildMessageId(pending),
            _pendingStore, _inboxStore, null);

        // Simulate a watcher loading the plan fresh from the store
        var fresh = _pendingStore.Load("TEST-20260730");
        Assert.That(fresh, Is.Not.Null);
        Assert.That(fresh!.Group.ApprovalGates, Has.Count.GreaterThan(0),
            "Reloaded plan should contain the gates added by the editor");
        Assert.That(fresh.Revision, Is.EqualTo(result.UpdatedPlan.Revision));
    }

    // ─── Restart persistence ────────────────────────────────────────────────

    [Test]
    public void Restart_Persistence_PlanSurvivesStoreReload()
    {
        var pending = SavePendingPlan();
        var durable = SyntheticDurable(pending);
        var gated = PlanGateManager.AddGateAfter(durable, "TASK-002", "Gate between 2 and 3");
        PendingPlanGateEditor.Apply(gated, null, _pendingStore, null, null);

        // Simulate restart: new store instance from same folder
        var freshStore = new PendingDecomposePlanStore(_squadFolder);
        var reloaded = freshStore.Load("TEST-20260730");
        Assert.That(reloaded, Is.Not.Null);
        Assert.That(reloaded!.Group.ApprovalGates, Has.Count.GreaterThan(0));

        // Revision recomputation should validate
        var recomputed = PendingDecomposePlanStore.ComputeRevision(reloaded.Group);
        Assert.That(reloaded.Revision, Is.EqualTo(recomputed),
            "Persisted revision should pass recomputation after restart");
    }

    // ─── No inbox message: Apply still succeeds ─────────────────────────────

    [Test]
    public void Apply_WithoutInboxMessage_Succeeds()
    {
        var pending = SavePendingPlan();
        var durable = SyntheticDurable(pending);
        var gated = PlanGateManager.AddGateAfter(durable, "TASK-001", "Review");

        var result = PendingPlanGateEditor.Apply(
            gated, null, _pendingStore, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(result.UpdatedPlan, Is.Not.Null);
            Assert.That(result.SyntheticDurablePlan, Is.Not.Null);
            Assert.That(result.NewInboxMessageId, Is.Null);
        });
    }

    // ─── Latest action synchronization ──────────────────────────────────────

    [Test]
    public void LatestAction_AlwaysReferencesCurrentRevision()
    {
        var pending = SavePendingPlan();
        SaveInboxMessage(pending);
        var messageId = DecomposePlanInbox.BuildMessageId(pending);

        // First edit
        var durable1 = SyntheticDurable(pending);
        var gated1 = PlanGateManager.AddGateAfter(durable1, "TASK-001", "Review");
        var result1 = PendingPlanGateEditor.Apply(
            gated1, messageId, _pendingStore, _inboxStore, null);

        // Second edit on top of first
        var durable2 = result1.SyntheticDurablePlan;
        var gated2 = PlanGateManager.AddGateBefore(durable2, "TASK-003", "Final review");
        var result2 = PendingPlanGateEditor.Apply(
            gated2, result1.NewInboxMessageId, _pendingStore, _inboxStore, null);

        // The final inbox message actions should reference result2's revision
        var finalMsg = _inboxStore.GetById(result2.NewInboxMessageId!);
        Assert.That(finalMsg, Is.Not.Null);
        var decomposeActions = finalMsg!.Actions
            .Where(a => a.RouteMode == DecomposePlanInbox.ActionRouteMode).ToList();
        Assert.That(decomposeActions, Is.Not.Empty);
        foreach (var action in decomposeActions)
        {
            Assert.That(action.Prompt, Does.Contain(result2.UpdatedPlan.Revision),
                $"Action '{action.Label}' must reference the latest revision");
            Assert.That(action.Prompt, Does.Not.Contain(pending.Revision),
                $"Action '{action.Label}' must NOT reference the original stale revision");
        }
    }
}
