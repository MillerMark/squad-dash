using System;
using System.IO;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanExecutionTransitionServiceTests
{
    private TestWorkspace _workspace = null!;
    private PlanStore _store = null!;
    private PlanExecutionTransitionService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _workspace = new TestWorkspace();
        var squadFolder = _workspace.GetPath(".squad");
        Directory.CreateDirectory(squadFolder);
        _store = new PlanStore(squadFolder);
        _service = new PlanExecutionTransitionService(_store);
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    // ── Clean start: Approved → Executing ────────────────────────────────────

    [Test]
    public void Start_ApprovedPlan_TransitionsToExecuting()
    {
        var plan = MakePlan("PLAN-001", PlanLifecycleStatus.Approved);
        _store.Save(plan);
        var timestamp = DateTimeOffset.UtcNow;

        var result = _service.Start(plan, timestamp);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.Started));
            Assert.That(result.Plan, Is.Not.Null);
            Assert.That(result.Plan!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(result.Plan.Timestamps.StartedAt, Is.EqualTo(timestamp));
        });
    }

    [Test]
    public void Start_ApprovedPlan_PersistsToStore()
    {
        var plan = MakePlan("PLAN-002", PlanLifecycleStatus.Approved);
        _store.Save(plan);
        var timestamp = DateTimeOffset.UtcNow;

        _service.Start(plan, timestamp);

        var loaded = _store.Load("PLAN-002");
        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(loaded.Timestamps.StartedAt, Is.EqualTo(timestamp));
        });
    }

    // ── Blocked start: wrong status ──────────────────────────────────────────

    [Test]
    public void Start_InterruptedPlan_ReturnsInvalidStatus()
    {
        var plan = MakePlan("PLAN-003", PlanLifecycleStatus.Interrupted);

        var result = _service.Start(plan, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.InvalidStatus));
            Assert.That(result.Plan!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
            Assert.That(result.Message, Does.Contain("Approved"));
        });
    }

    [Test]
    public void Start_StagedPlan_ReturnsInvalidStatus()
    {
        var plan = MakePlan("PLAN-004", PlanLifecycleStatus.Staged);

        var result = _service.Start(plan, DateTimeOffset.UtcNow);

        Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.InvalidStatus));
    }

    // ── Duplicate start: already executing ───────────────────────────────────

    [Test]
    public void Start_AlreadyExecuting_ReturnsIdempotentGuard()
    {
        var plan = MakePlan("PLAN-005", PlanLifecycleStatus.Executing);

        var result = _service.Start(plan, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.AlreadyExecuting));
            Assert.That(result.Plan!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        });
    }

    // ── Terminal plan: completed/stopped → rejected ──────────────────────────

    [Test]
    public void Start_CompletedPlan_ReturnsTerminalPlan()
    {
        var plan = MakePlan("PLAN-006", PlanLifecycleStatus.Completed);

        var result = _service.Start(plan, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.TerminalPlan));
            Assert.That(result.Plan!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Completed));
            Assert.That(result.Message, Does.Contain("terminal"));
        });
    }

    [Test]
    public void Start_StoppedPlan_ReturnsTerminalPlan()
    {
        var plan = MakePlan("PLAN-007", PlanLifecycleStatus.Stopped);

        var result = _service.Start(plan, DateTimeOffset.UtcNow);

        Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.TerminalPlan));
    }

    [Test]
    public void Start_ArchivedPlan_ReturnsTerminalPlan()
    {
        var plan = MakePlan("PLAN-008", PlanLifecycleStatus.Archived);

        var result = _service.Start(plan, DateTimeOffset.UtcNow);

        Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.TerminalPlan));
    }

    // ── Resume: Interrupted → Executing ──────────────────────────────────────

    [Test]
    public void Resume_InterruptedPlan_TransitionsToExecuting()
    {
        var plan = MakePlan("PLAN-009", PlanLifecycleStatus.Interrupted) with
        {
            InterruptionData = new PlanInterruptionData(
                Reason: "Crash",
                RecoveryState: PlanRecoveryState.PendingRecovery,
                LoopIteration: 1),
        };
        _store.Save(plan);

        var result = _service.Resume(plan, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.Started));
            Assert.That(result.Plan, Is.Not.Null);
            Assert.That(result.Plan!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(result.Plan.InterruptionData!.RecoveryState, Is.EqualTo(PlanRecoveryState.Recovered));
        });
    }

    [Test]
    public void Resume_InterruptedPlan_PersistsToStore()
    {
        var plan = MakePlan("PLAN-010", PlanLifecycleStatus.Interrupted) with
        {
            InterruptionData = new PlanInterruptionData(
                Reason: "Network error",
                RecoveryState: PlanRecoveryState.PendingRecovery,
                LoopIteration: 3),
        };
        _store.Save(plan);

        _service.Resume(plan, DateTimeOffset.UtcNow);

        var loaded = _store.Load("PLAN-010");
        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
        });
    }

    // ── Resume non-interrupted: Approved plan → rejected ─────────────────────

    [Test]
    public void Resume_ApprovedPlan_ReturnsInvalidStatus()
    {
        var plan = MakePlan("PLAN-011", PlanLifecycleStatus.Approved);

        var result = _service.Resume(plan, DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.InvalidStatus));
            Assert.That(result.Message, Does.Contain("Interrupted"));
        });
    }

    [Test]
    public void Resume_CompletedPlan_ReturnsTerminalPlan()
    {
        var plan = MakePlan("PLAN-012", PlanLifecycleStatus.Completed);

        var result = _service.Resume(plan, DateTimeOffset.UtcNow);

        Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.TerminalPlan));
    }

    [Test]
    public void Resume_AlreadyExecuting_ReturnsIdempotentGuard()
    {
        var plan = MakePlan("PLAN-013", PlanLifecycleStatus.Executing);

        var result = _service.Resume(plan, DateTimeOffset.UtcNow);

        Assert.That(result.Outcome, Is.EqualTo(ExecutionTransitionOutcome.AlreadyExecuting));
    }

    // ── Restart: persisted Executing plan is loadable after restart ───────────

    [Test]
    public void Start_PersistedExecutingPlan_SurvivesReload()
    {
        var plan = MakePlan("PLAN-014", PlanLifecycleStatus.Approved);
        _store.Save(plan);
        var timestamp = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

        _service.Start(plan, timestamp);

        // Simulate restart: create a fresh store against the same folder
        var freshStore = new PlanStore(_workspace.GetPath(".squad"));
        var loaded = freshStore.Load("PLAN-014");

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Executing));
            Assert.That(loaded.Timestamps.StartedAt, Is.EqualTo(timestamp));
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Plan MakePlan(string planId, string lifecycleStatus) => new(
        PlanId:          planId,
        Revision:        "abc123def456789a",
        Source:          PlanSource.DecomposeDecision,
        LifecycleStatus: lifecycleStatus,
        Title:           $"Test plan {planId}",
        Branch:          "feature/test",
        Summary:         "Created for transition service tests",
        Tasks:
        [
            new PlanTask(
                TaskId:      $"{planId}-001",
                Title:       "First step",
                Description: "Initial work",
                DependsOn:   [],
                Priority:    "high",
                Status:      PlanTaskStatus.Pending),
        ],
        ApprovalGates: [],
        Progress:       new PlanProgress(CompletedCount: 0, TotalCount: 1),
        Timestamps:     new PlanTimestamps(CreatedAt: DateTimeOffset.UtcNow));
}
