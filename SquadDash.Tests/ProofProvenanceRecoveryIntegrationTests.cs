using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Integration tests exercising proof-provenance recovery through the production
/// <see cref="PlanRecoveryProvenanceService"/> coordinator, verifying bounded recovery,
/// provenance capture, and block-on-exhaustion semantics.
/// </summary>
[TestFixture]
internal sealed class ProofProvenanceRecoveryIntegrationTests
{
    private string _squadFolder = null!;
    private PlanStore _planStore = null!;
    private PlanRecoveryProvenanceService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _squadFolder = Path.Combine(Path.GetTempPath(), $"squad-provenance-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_squadFolder);
        _planStore = new PlanStore(_squadFolder);
        _service = new PlanRecoveryProvenanceService(_planStore);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_squadFolder, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Plan MakeInterruptedPlan(
        string planId = "PLAN-INT-001",
        string taskId = "TASK-001",
        string? taskCommit = "abc1234def5678901234567890abcdef12345678",
        ProofProvenanceChain? existingChain = null)
    {
        var task = new PlanTask(
            TaskId: taskId,
            Title: "Implement feature",
            Description: "A task that requires proof evidence",
            DependsOn: [],
            Priority: "high",
            Status: PlanTaskStatus.Complete,
            Commit: taskCommit,
            CompletedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletionSummary: "Implemented and committed",
            ProofRequirements:
            [
                new PlanTaskProofRequirement("req-1", "ai-assessed", "Unit tests pass"),
            ],
            ProofEvidence:
            [
                new PlanTaskProofEvidence("req-1", "ai-assessed", "Tests verified"),
            ],
            ProvenanceChain: existingChain);

        return new Plan(
            PlanId: planId,
            Revision: "rev-integration-1",
            Source: PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Interrupted,
            Title: "Integration Test Plan",
            Branch: "feature/integration-test",
            Summary: "Tests proof-provenance recovery through production service.",
            Tasks: [task],
            ApprovalGates: [],
            Progress: new PlanProgress(0, 1),
            Timestamps: new PlanTimestamps(CreatedAt: DateTimeOffset.UtcNow.AddHours(-1)),
            InterruptionData: new PlanInterruptionData(
                Reason: "Invalid proof result",
                RecoveryState: PlanRecoveryState.PendingRecovery,
                LoopIteration: 1,
                InterruptedTaskId: taskId));
    }

    // ── First envelope repair triggers exactly one provenance capture ─────────

    [Test]
    public void EnvelopeRepair_FirstInvalid_CapturesProvenanceAndResetsToPending()
    {
        var plan = MakeInterruptedPlan();
        _planStore.Save(plan);

        var result = _service.ApplyEnvelopeRepair(
            plan.PlanId, "TASK-001", plan.Tasks[0].Commit);

        Assert.That(result.Applied, Is.True);
        Assert.That(result.Plan, Is.Not.Null);

        // Verify durable state was persisted
        var reloaded = _planStore.Load(plan.PlanId);
        Assert.That(reloaded, Is.Not.Null);
        var task = reloaded!.Tasks[0];

        // Task was reset to Pending
        Assert.That(task.Status, Is.EqualTo(PlanTaskStatus.Pending));
        Assert.That(task.Commit, Is.Null);
        Assert.That(task.CompletedAt, Is.Null);

        // Provenance chain has exactly one entry
        Assert.That(task.ProvenanceChain, Is.Not.Null);
        Assert.That(task.ProvenanceChain!.Entries, Has.Count.EqualTo(1));
        var entry = task.ProvenanceChain.Entries[0];
        Assert.That(entry.TaskId, Is.EqualTo("TASK-001"));
        Assert.That(entry.RecoveryKind, Is.EqualTo("envelope-repair"));
        Assert.That(entry.CommitShortSha, Is.EqualTo("abc1234"));
    }

    // ── Second invalid result is blocked without re-reset ────────────────────

    [Test]
    public void EnvelopeRepair_SecondInvalid_BlocksWithoutRerunningRecovery()
    {
        var plan = MakeInterruptedPlan();
        _planStore.Save(plan);

        // First repair succeeds
        var first = _service.ApplyEnvelopeRepair(
            plan.PlanId, "TASK-001", plan.Tasks[0].Commit);
        Assert.That(first.Applied, Is.True);

        // Simulate: the repaired task was re-executed and again returned invalid proof
        // The task is now back in some completed state with the provenance chain
        var reloaded = _planStore.Load(plan.PlanId)!;
        var taskAfterFirstRecovery = reloaded.Tasks[0];
        // Confirm provenance exists from first recovery
        Assert.That(taskAfterFirstRecovery.ProvenanceChain!.Entries, Has.Count.EqualTo(1));

        // Second repair attempt — should be BLOCKED because allowance is exhausted
        var second = _service.ApplyEnvelopeRepair(
            plan.PlanId, "TASK-001", "newcommit1234567890abcdef12345678901234567");

        Assert.That(second.Applied, Is.False);
        Assert.That(second.BlockReason, Does.Contain("exhausted"));
        Assert.That(second.BlockReason, Does.Contain("envelope-repair"));

        // Verify the plan was NOT modified by the second attempt
        var finalPlan = _planStore.Load(plan.PlanId)!;
        var finalTask = finalPlan.Tasks[0];
        Assert.That(finalTask.ProvenanceChain!.Entries, Has.Count.EqualTo(1),
            "Provenance chain must not grow beyond the bounded single repair entry.");
    }

    // ── Fresh-attempt recovery captures provenance ───────────────────────────

    [Test]
    public void FreshAttemptRecovery_FirstAttempt_CapturesProvenanceAndResets()
    {
        var plan = MakeInterruptedPlan();
        _planStore.Save(plan);

        var result = _service.ApplyFreshAttemptRecovery(
            plan.PlanId, "TASK-001", plan.Tasks[0].Commit);

        Assert.That(result.Applied, Is.True);

        var reloaded = _planStore.Load(plan.PlanId)!;
        var task = reloaded.Tasks[0];
        Assert.That(task.Status, Is.EqualTo(PlanTaskStatus.Pending));
        Assert.That(task.ProvenanceChain, Is.Not.Null);
        Assert.That(task.ProvenanceChain!.Entries, Has.Count.EqualTo(1));
        Assert.That(task.ProvenanceChain.Entries[0].RecoveryKind, Is.EqualTo("fresh-attempt"));
    }

    // ── Fresh-attempt is also bounded to one ─────────────────────────────────

    [Test]
    public void FreshAttemptRecovery_SecondAttempt_BlocksAdvancement()
    {
        var plan = MakeInterruptedPlan();
        _planStore.Save(plan);

        var first = _service.ApplyFreshAttemptRecovery(
            plan.PlanId, "TASK-001", plan.Tasks[0].Commit);
        Assert.That(first.Applied, Is.True);

        var second = _service.ApplyFreshAttemptRecovery(
            plan.PlanId, "TASK-001", "newcommit1234567890abcdef12345678901234567");

        Assert.That(second.Applied, Is.False);
        Assert.That(second.BlockReason, Does.Contain("exhausted"));
        Assert.That(second.BlockReason, Does.Contain("fresh-attempt"));
    }

    // ── Provenance chain preserves existing entries from prior recovery kinds ─

    [Test]
    public void EnvelopeRepair_AfterFreshAttempt_AllowsOncePerKind()
    {
        // A task that already had a fresh-attempt recovery can still get one envelope-repair
        var existingChain = new ProofProvenanceChain([
            new ProofProvenanceEntry(
                TaskId: "TASK-001",
                SourceLabel: "Host-recorded commit",
                SourceKind: "HostRecorded",
                CommitShortSha: "abc1234",
                RecoveryKind: "fresh-attempt",
                RecordedAt: DateTimeOffset.UtcNow.AddMinutes(-10))
        ]);

        var plan = MakeInterruptedPlan(existingChain: existingChain);
        _planStore.Save(plan);

        var result = _service.ApplyEnvelopeRepair(
            plan.PlanId, "TASK-001", "def5678901234567890abcdef1234567890123456");

        Assert.That(result.Applied, Is.True);

        var reloaded = _planStore.Load(plan.PlanId)!;
        var task = reloaded.Tasks[0];
        Assert.That(task.ProvenanceChain!.Entries, Has.Count.EqualTo(2));
        Assert.That(task.ProvenanceChain.Entries[0].RecoveryKind, Is.EqualTo("fresh-attempt"));
        Assert.That(task.ProvenanceChain.Entries[1].RecoveryKind, Is.EqualTo("envelope-repair"));
    }

    // ── Missing plan returns not-applied ─────────────────────────────────────

    [Test]
    public void EnvelopeRepair_MissingPlan_ReturnsNotApplied()
    {
        var result = _service.ApplyEnvelopeRepair(
            "NONEXISTENT-PLAN", "TASK-001", null);

        Assert.That(result.Applied, Is.False);
        Assert.That(result.Plan, Is.Null);
        Assert.That(result.BlockReason, Does.Contain("not found"));
    }

    // ── Missing task returns not-applied ─────────────────────────────────────

    [Test]
    public void FreshAttemptRecovery_MissingTask_ReturnsNotApplied()
    {
        var plan = MakeInterruptedPlan();
        _planStore.Save(plan);

        var result = _service.ApplyFreshAttemptRecovery(
            plan.PlanId, "NONEXISTENT-TASK", null);

        Assert.That(result.Applied, Is.False);
        Assert.That(result.BlockReason, Does.Contain("not found"));
    }
}
