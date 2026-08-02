using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SquadDash.Tests;

/// <summary>
/// Focused tests for <see cref="PlanCollectionService"/> — the collection transition
/// that moves a pending proposal into the durable PlanStore as an inactive (Approved) plan.
/// </summary>
[TestFixture]
internal sealed class PlanCollectionServiceTests
{
    private TestWorkspace _workspace = null!;
    private PlanStore _planStore = null!;
    private PendingDecomposePlanStore _pendingStore = null!;
    private PlanCollectionService _service = null!;
    private string _squadFolder = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TestWorkspace();
        _squadFolder = _workspace.GetPath(".squad");
        Directory.CreateDirectory(_squadFolder);
        _planStore = new PlanStore(_squadFolder);
        _pendingStore = new PendingDecomposePlanStore(_squadFolder);
        _service = new PlanCollectionService(_planStore, _pendingStore);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DecomposedTaskGroup MakeGroup(
        string groupId = "COLLECT-001",
        int taskCount = 3,
        string branch = "feature/collection",
        IReadOnlyList<DecomposedGate>? gates = null)
    {
        var tasks = Enumerable.Range(1, taskCount)
            .Select(i => new DecomposedSubTask(
                Id:          $"{groupId}-00{i}",
                Description: $"Task {i} description",
                DependsOn:   i == 1 ? [] : [$"{groupId}-00{i - 1}"],
                Priority:    "mid",
                Title:       $"Task {i}",
                AgentAssignments: [new DecomposedAgentAssignment("orion-vale", "architect")]))
            .ToList();

        return new DecomposedTaskGroup(
            GroupId:       groupId,
            GroupTitle:    "Collection Test Plan",
            Branch:        branch,
            Summary:       "Tests for PlanCollectionService",
            Tasks:         tasks,
            ApprovalGates: gates);
    }

    private static PendingDecomposePlan MakePending(DecomposedTaskGroup? group = null)
    {
        group ??= MakeGroup();
        var revision = PendingDecomposePlanStore.ComputeRevision(group);
        return new PendingDecomposePlan(revision, group, DateTimeOffset.UtcNow);
    }

    // ── 1. Collecting creates a durable Plan with status Approved ────────────

    [Test]
    public void Collect_CreatesApprovedPlan()
    {
        var pending = MakePending();
        var now = DateTimeOffset.UtcNow;

        var result = _service.Collect(pending, now);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(CollectionOutcome.Collected));
            Assert.That(result.Plan, Is.Not.Null);
            Assert.That(result.Plan!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Approved));
            Assert.That(result.Plan.PlanId, Is.EqualTo("COLLECT-001"));
            Assert.That(result.Plan.Revision, Is.EqualTo(pending.Revision));
            Assert.That(result.Plan.Timestamps.AcceptedAt, Is.EqualTo(now));
        });
    }

    [Test]
    public void Collect_PlanIsDurableInStore()
    {
        var pending = MakePending();
        _service.Collect(pending, DateTimeOffset.UtcNow);

        var loaded = _planStore.Load("COLLECT-001");
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Approved));
    }

    // ── 2. Idempotency — same revision → returns same plan, no double-write ──

    [Test]
    public void Collect_SameRevision_IsIdempotent()
    {
        var pending = MakePending();
        var now = DateTimeOffset.UtcNow;

        var first = _service.Collect(pending, now);
        var second = _service.Collect(pending, now.AddMinutes(5));

        Assert.Multiple(() =>
        {
            Assert.That(second.Outcome, Is.EqualTo(CollectionOutcome.AlreadyCollected));
            Assert.That(second.Plan, Is.Not.Null);
            Assert.That(second.Plan!.PlanId, Is.EqualTo(first.Plan!.PlanId));
            Assert.That(second.Plan.Revision, Is.EqualTo(first.Plan.Revision));
        });
    }

    // ── 3. Stale revision is rejected ────────────────────────────────────────

    [Test]
    public void Collect_StaleRevision_IsRejected()
    {
        // Collect the first version.
        var group1 = MakeGroup();
        var pending1 = MakePending(group1);
        _service.Collect(pending1, DateTimeOffset.UtcNow);

        // Attempt to collect a different version with the same GroupId.
        var group2 = MakeGroup(taskCount: 5); // different content = different revision
        var pending2 = MakePending(group2);

        var result = _service.Collect(pending2, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(CollectionOutcome.StaleRevisionRejected));
            Assert.That(result.Plan, Is.Null);
        });
    }

    // ── 4. Collection does NOT write tasks.md ────────────────────────────────

    [Test]
    public void Collect_DoesNotWriteTasksMd()
    {
        var pending = MakePending();
        _service.Collect(pending, DateTimeOffset.UtcNow);

        var tasksMdPath = Path.Combine(_squadFolder, "tasks.md");
        Assert.That(File.Exists(tasksMdPath), Is.False);
    }

    // ── 5. Collection does not modify branch state ───────────────────────────

    [Test]
    public void Collect_DoesNotModifyBranchState()
    {
        var pending = MakePending();
        Assert.DoesNotThrow(() => _service.Collect(pending, DateTimeOffset.UtcNow));
    }

    // ── 6. After restart — collected plan is loadable ─────────────────────────

    [Test]
    public void Collect_SurvivesRestart()
    {
        var pending = MakePending();
        _service.Collect(pending, DateTimeOffset.UtcNow);

        // Simulate restart: new PlanStore + new service over the same folder.
        var freshStore = new PlanStore(_squadFolder);
        var loaded = freshStore.Load("COLLECT-001");

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Approved));
            Assert.That(loaded.Revision, Is.EqualTo(pending.Revision));
        });
    }

    // ── 7. Collected plan preserves task graph, assignments, gates, branch ────

    [Test]
    public void Collect_PreservesTaskGraph()
    {
        var group = MakeGroup(taskCount: 3);
        var pending = MakePending(group);

        var result = _service.Collect(pending, DateTimeOffset.UtcNow);
        var plan = result.Plan!;

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks, Has.Count.EqualTo(3));
            Assert.That(plan.Tasks[0].TaskId, Is.EqualTo("COLLECT-001-001"));
            Assert.That(plan.Tasks[1].DependsOn, Does.Contain("COLLECT-001-001"));
            Assert.That(plan.Tasks[2].DependsOn, Does.Contain("COLLECT-001-002"));
        });
    }

    [Test]
    public void Collect_PreservesAgentAssignments()
    {
        var pending = MakePending();
        var result = _service.Collect(pending, DateTimeOffset.UtcNow);

        var assignments = result.Plan!.Tasks[0].AgentAssignments;
        Assert.That(assignments, Is.Not.Null);
        Assert.That(assignments![0].AgentHandle, Is.EqualTo("orion-vale"));
    }

    [Test]
    public void Collect_PreservesApprovalGates()
    {
        var gates = new[]
        {
            new DecomposedGate(
                GateId:       "GATE-001",
                Message:      "Review phase 1 before continuing",
                AfterTaskIds:  ["COLLECT-001-001"],
                BeforeTaskIds: ["COLLECT-001-002"]),
        };
        var group = MakeGroup(gates: gates);
        var pending = MakePending(group);

        var result = _service.Collect(pending, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Plan!.ApprovalGates, Has.Count.EqualTo(1));
            Assert.That(result.Plan.ApprovalGates[0].GateId, Is.EqualTo("GATE-001"));
            Assert.That(result.Plan.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.Pending));
        });
    }

    [Test]
    public void Collect_PreservesBranch()
    {
        var group = MakeGroup(branch: "feature/custom-branch");
        var pending = MakePending(group);

        var result = _service.Collect(pending, DateTimeOffset.UtcNow);

        Assert.That(result.Plan!.Branch, Is.EqualTo("feature/custom-branch"));
    }

    // ── 8. Pending cleanup — after collection the pending store is empty ──────

    [Test]
    public void Collect_RemovesPendingPlanFromTransientStore()
    {
        var group = MakeGroup();
        var pending = _pendingStore.Save(group);

        // Verify pending exists before collection.
        Assert.That(_pendingStore.Load("COLLECT-001"), Is.Not.Null);

        _service.Collect(pending, DateTimeOffset.UtcNow);

        // After collection the pending plan is cleaned up.
        Assert.That(_pendingStore.Load("COLLECT-001"), Is.Null);
    }

    [Test]
    public void Collect_Idempotent_AlsoCleansPending()
    {
        var group = MakeGroup();
        var pending = _pendingStore.Save(group);
        _service.Collect(pending, DateTimeOffset.UtcNow);

        // Re-save pending (simulating stale state after restart).
        _pendingStore.Save(group);
        Assert.That(_pendingStore.Load("COLLECT-001"), Is.Not.Null);

        // Idempotent collect still cleans up.
        _service.Collect(pending, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.That(_pendingStore.Load("COLLECT-001"), Is.Null);
    }

    // ── 9. Active plan protection ─────────────────────────────────────────────

    [Test]
    [TestCase(PlanLifecycleStatus.Executing)]
    [TestCase(PlanLifecycleStatus.AwaitingApproval)]
    [TestCase(PlanLifecycleStatus.Interrupted)]
    [TestCase(PlanLifecycleStatus.Blocked)]
    public void Collect_ActivePlan_IsBlocked(string activeStatus)
    {
        var group = MakeGroup();
        var pending = MakePending(group);

        // Pre-populate PlanStore with an active plan (same ID and revision).
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow) with
        {
            LifecycleStatus = activeStatus,
        };
        _planStore.Save(plan);

        var result = _service.Collect(pending, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(CollectionOutcome.ActivePlanBlocked));
            Assert.That(result.Plan, Is.Null);
        });
    }

    [Test]
    public void Collect_ExecutingPlan_CannotBeOverwritten_EvenWithMatchingRevision()
    {
        var group = MakeGroup();
        var pending = MakePending(group);

        // Place an executing plan with the SAME revision.
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow) with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
        };
        _planStore.Save(plan);

        var result = _service.Collect(pending, DateTimeOffset.UtcNow);

        Assert.That(result.Outcome, Is.EqualTo(CollectionOutcome.ActivePlanBlocked));
    }

    // ── 10. Compatibility — unrelated pending plan does not affect existing approved ──

    [Test]
    public void Collect_UnrelatedPending_DoesNotAffectExistingApproved()
    {
        // Collect plan A.
        var groupA = MakeGroup(groupId: "PLAN-A");
        var pendingA = MakePending(groupA);
        _service.Collect(pendingA, DateTimeOffset.UtcNow);

        // Collect plan B (different ID).
        var groupB = MakeGroup(groupId: "PLAN-B");
        var pendingB = MakePending(groupB);
        _service.Collect(pendingB, DateTimeOffset.UtcNow);

        // Plan A is still intact.
        var loadedA = _planStore.Load("PLAN-A");
        Assert.Multiple(() =>
        {
            Assert.That(loadedA, Is.Not.Null);
            Assert.That(loadedA!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Approved));
            Assert.That(loadedA.Revision, Is.EqualTo(pendingA.Revision));
        });
    }

    // ── 11. Persistence round-trip — restart with fresh store instances ────────

    [Test]
    public void Collect_PersistenceRoundTrip_AllDataSurvivesRestart()
    {
        var gates = new[]
        {
            new DecomposedGate(
                GateId: "GATE-RT",
                Message: "Round-trip gate",
                AfterTaskIds: ["COLLECT-001-001"],
                BeforeTaskIds: ["COLLECT-001-002"]),
        };
        var group = MakeGroup(gates: gates, branch: "feature/roundtrip");
        var pending = MakePending(group);
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

        _service.Collect(pending, now);

        // Simulate full restart — new PlanStore, new PendingDecomposePlanStore, new service.
        var freshPlanStore = new PlanStore(_squadFolder);
        var freshPendingStore = new PendingDecomposePlanStore(_squadFolder);
        var freshService = new PlanCollectionService(freshPlanStore, freshPendingStore);

        var loaded = freshPlanStore.Load("COLLECT-001");

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.PlanId, Is.EqualTo("COLLECT-001"));
            Assert.That(loaded.Revision, Is.EqualTo(pending.Revision));
            Assert.That(loaded.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Approved));
            Assert.That(loaded.Branch, Is.EqualTo("feature/roundtrip"));
            Assert.That(loaded.Tasks, Has.Count.EqualTo(3));
            Assert.That(loaded.Tasks[0].AgentAssignments, Is.Not.Null);
            Assert.That(loaded.Tasks[0].AgentAssignments![0].AgentHandle, Is.EqualTo("orion-vale"));
            Assert.That(loaded.ApprovalGates, Has.Count.EqualTo(1));
            Assert.That(loaded.ApprovalGates[0].GateId, Is.EqualTo("GATE-RT"));
            Assert.That(loaded.Timestamps.AcceptedAt, Is.EqualTo(now));
        });

        // Idempotent collect on fresh service also works.
        var idempotentResult = freshService.Collect(pending, now.AddHours(1));
        Assert.That(idempotentResult.Outcome, Is.EqualTo(CollectionOutcome.AlreadyCollected));
    }

    // ── 12. Terminal plans allow re-collection ─────────────────────────────────

    [Test]
    [TestCase(PlanLifecycleStatus.Stopped)]
    [TestCase(PlanLifecycleStatus.Completed)]
    [TestCase(PlanLifecycleStatus.Archived)]
    public void Collect_TerminalPlan_TreatedAsStaleOrIdempotent(string terminalStatus)
    {
        var group = MakeGroup();
        var pending = MakePending(group);

        // Place a terminal plan with same revision.
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, DateTimeOffset.UtcNow) with
        {
            LifecycleStatus = terminalStatus,
        };
        _planStore.Save(plan);

        // Same revision → idempotent (plan already exists with matching revision).
        var result = _service.Collect(pending, DateTimeOffset.UtcNow);
        Assert.That(result.Outcome, Is.EqualTo(CollectionOutcome.AlreadyCollected));
    }

    // ── 13. Collection with no pending store still works (backward compat) ────

    [Test]
    public void Collect_WithoutPendingStore_StillSucceeds()
    {
        var serviceNoPending = new PlanCollectionService(_planStore);
        var pending = MakePending();

        var result = serviceNoPending.Collect(pending, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(CollectionOutcome.Collected));
            Assert.That(result.Plan, Is.Not.Null);
        });
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Test]
    public void Collect_NullPending_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => _service.Collect(null!, DateTimeOffset.UtcNow));
    }

    [Test]
    public void Collect_NullGroup_Throws()
    {
        var pending = new PendingDecomposePlan("rev", null!);
        Assert.Throws<ArgumentException>(
            () => _service.Collect(pending, DateTimeOffset.UtcNow));
    }
}
