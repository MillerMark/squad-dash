using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Host-orchestration integration tests that exercise the full recovery decision path
/// through <see cref="PlanRecoveryDecisionHandler"/>, verifying authoritative enforcement,
/// provenance rendering, dependent validation invalidation, and block-on-exhaustion semantics.
/// </summary>
[TestFixture]
internal sealed class PlanRecoveryDecisionHandlerTests
{
    private string _squadFolder = null!;
    private PlanStore _planStore = null!;
    private PlanRecoveryProvenanceService _service = null!;
    private PlanRecoveryDecisionHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _squadFolder = Path.Combine(Path.GetTempPath(), $"squad-decision-handler-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_squadFolder);
        _planStore = new PlanStore(_squadFolder);
        _service = new PlanRecoveryProvenanceService(_planStore);
        _handler = new PlanRecoveryDecisionHandler(_service);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_squadFolder, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Plan MakePlanWithTaskAndValidation(
        string planId = "PLAN-DH-001",
        string taskId = "TASK-001",
        string? taskCommit = "abc1234def5678901234567890abcdef12345678",
        ProofProvenanceChain? existingChain = null,
        IReadOnlyList<PlanValidationNode>? validations = null)
    {
        var task = new PlanTask(
            TaskId: taskId,
            Title: "Implement feature",
            Description: "A task requiring proof evidence",
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
            Revision: "rev-decision-1",
            Source: PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Interrupted,
            Title: "Decision Handler Test Plan",
            Branch: "feature/decision-test",
            Summary: "Tests recovery decision handler flow.",
            Tasks: [task],
            ApprovalGates: [],
            Progress: new PlanProgress(0, 1),
            Timestamps: new PlanTimestamps(CreatedAt: DateTimeOffset.UtcNow.AddHours(-1)),
            InterruptionData: new PlanInterruptionData(
                Reason: "Invalid proof result",
                RecoveryState: PlanRecoveryState.PendingRecovery,
                LoopIteration: 1,
                InterruptedTaskId: taskId),
            Validations: validations);
    }

    // ── Test: first repair succeeds, task reset, provenance captured ─────────

    [Test]
    public void HandleRepairDecision_FirstRepair_AllowedAndTaskReset()
    {
        var plan = MakePlanWithTaskAndValidation();
        _planStore.Save(plan);

        var decision = _handler.HandleRepairDecision(
            plan.PlanId, "TASK-001", plan.Tasks[0].Commit);

        Assert.That(decision.Allowed, Is.True, "First repair should be allowed.");
        Assert.That(decision.Result.Applied, Is.True);
        Assert.That(decision.UserMessage, Does.Contain("Recovery (envelope-repair) applied"));

        // Verify task was reset
        var reloaded = _planStore.Load(plan.PlanId)!;
        var task = reloaded.Tasks[0];
        Assert.That(task.Status, Is.EqualTo(PlanTaskStatus.Pending));
        Assert.That(task.Commit, Is.Null);

        // Verify provenance was captured
        Assert.That(task.ProvenanceChain, Is.Not.Null);
        Assert.That(task.ProvenanceChain!.Entries, Has.Count.EqualTo(1));
        Assert.That(task.ProvenanceChain.Entries[0].RecoveryKind, Is.EqualTo("envelope-repair"));
        Assert.That(task.ProvenanceChain.Entries[0].CommitShortSha, Is.EqualTo("abc1234"));
    }

    // ── Test: second repair blocked, no advancement ─────────────────────────

    [Test]
    public void HandleRepairDecision_SecondRepair_BlockedAndNoAdvancement()
    {
        var plan = MakePlanWithTaskAndValidation();
        _planStore.Save(plan);

        // First repair succeeds
        var first = _handler.HandleRepairDecision(
            plan.PlanId, "TASK-001", plan.Tasks[0].Commit);
        Assert.That(first.Allowed, Is.True);

        // Second repair on the same task — must be blocked
        var second = _handler.HandleRepairDecision(
            plan.PlanId, "TASK-001", "newcommit1234567890abcdef12345678901234567");

        Assert.That(second.Allowed, Is.False, "Second repair must be blocked.");
        Assert.That(second.Result.Applied, Is.False);
        Assert.That(second.UserMessage, Does.Contain("⛔ Recovery (envelope-repair) blocked"));
        Assert.That(second.UserMessage, Does.Contain("exhausted"));
        Assert.That(second.UserMessage, Does.Contain("Prior attempts:"));

        // Verify plan was not modified by the blocked attempt
        var finalPlan = _planStore.Load(plan.PlanId)!;
        Assert.That(finalPlan.Tasks[0].ProvenanceChain!.Entries, Has.Count.EqualTo(1),
            "Provenance chain must not grow beyond the bounded single repair entry.");
    }

    // ── Test: first fresh-attempt succeeds ───────────────────────────────────

    [Test]
    public void HandleFreshAttemptDecision_FirstAttempt_AllowedWithProvenance()
    {
        var plan = MakePlanWithTaskAndValidation();
        _planStore.Save(plan);

        var decision = _handler.HandleFreshAttemptDecision(
            plan.PlanId, "TASK-001", plan.Tasks[0].Commit);

        Assert.That(decision.Allowed, Is.True);
        Assert.That(decision.UserMessage, Does.Contain("Recovery (fresh-attempt) applied"));

        var reloaded = _planStore.Load(plan.PlanId)!;
        var task = reloaded.Tasks[0];
        Assert.That(task.Status, Is.EqualTo(PlanTaskStatus.Pending));
        Assert.That(task.ProvenanceChain!.Entries, Has.Count.EqualTo(1));
        Assert.That(task.ProvenanceChain.Entries[0].RecoveryKind, Is.EqualTo("fresh-attempt"));
    }

    // ── Test: second fresh-attempt blocked ───────────────────────────────────

    [Test]
    public void HandleFreshAttemptDecision_SecondAttempt_BlockedWithExplanation()
    {
        var plan = MakePlanWithTaskAndValidation();
        _planStore.Save(plan);

        var first = _handler.HandleFreshAttemptDecision(
            plan.PlanId, "TASK-001", plan.Tasks[0].Commit);
        Assert.That(first.Allowed, Is.True);

        var second = _handler.HandleFreshAttemptDecision(
            plan.PlanId, "TASK-001", "newcommit1234567890abcdef12345678901234567");

        Assert.That(second.Allowed, Is.False);
        Assert.That(second.UserMessage, Does.Contain("⛔ Recovery (fresh-attempt) blocked"));
        Assert.That(second.UserMessage, Does.Contain("exhausted"));
    }

    // ── Test: dependent validations invalidated on recovery ──────────────────

    [Test]
    public void HandleRepairDecision_InvalidatesDependentValidations()
    {
        var validations = new List<PlanValidationNode>
        {
            new PlanValidationNode(
                ValidationId: "VAL-001",
                Title: "Verify task output",
                Description: "Checks TASK-001 output",
                AfterTaskIds: ["TASK-001"],
                BeforeTaskIds: ["TASK-002"],
                Assertions: ["Output file exists"],
                OutputIds: null,
                Mode: "automated",
                Commands: ["dotnet test"],
                RevalidateAtCompletion: false,
                Status: PlanValidationStatus.Passed,
                CompletedAt: DateTimeOffset.UtcNow.AddMinutes(-3),
                ValidatedCommit: "abc1234def5678901234567890abcdef12345678",
                Summary: "All assertions passed",
                Evidence: ["test output verified"]),
            new PlanValidationNode(
                ValidationId: "VAL-002",
                Title: "Independent validation",
                Description: "Checks unrelated task output",
                AfterTaskIds: ["TASK-099"],
                BeforeTaskIds: [],
                Assertions: ["Something else"],
                OutputIds: null,
                Mode: "ai",
                Commands: null,
                RevalidateAtCompletion: false,
                Status: PlanValidationStatus.Passed,
                CompletedAt: DateTimeOffset.UtcNow.AddMinutes(-2),
                ValidatedCommit: "def5678",
                Summary: "Passed independently"),
        };

        var plan = MakePlanWithTaskAndValidation(validations: validations);
        _planStore.Save(plan);

        var decision = _handler.HandleRepairDecision(
            plan.PlanId, "TASK-001", plan.Tasks[0].Commit);

        Assert.That(decision.Allowed, Is.True);

        var reloaded = _planStore.Load(plan.PlanId)!;

        // VAL-001 depends on TASK-001 — should be reset to Pending
        var val1 = reloaded.Validations!.First(v => v.ValidationId == "VAL-001");
        Assert.That(val1.Status, Is.EqualTo(PlanValidationStatus.Pending),
            "Dependent validation must be reset to Pending when upstream task is recovered.");
        Assert.That(val1.ValidatedCommit, Is.Null,
            "Validated commit must be cleared on reset.");
        Assert.That(val1.CompletedAt, Is.Null,
            "CompletedAt must be cleared on reset.");

        // VAL-002 does NOT depend on TASK-001 — should remain Passed
        var val2 = reloaded.Validations!.First(v => v.ValidationId == "VAL-002");
        Assert.That(val2.Status, Is.EqualTo(PlanValidationStatus.Passed),
            "Independent validation must not be affected by recovery of an unrelated task.");
    }

    // ── Test: fresh-attempt also invalidates dependent validations ───────────

    [Test]
    public void HandleFreshAttemptDecision_InvalidatesDependentValidations()
    {
        var validations = new List<PlanValidationNode>
        {
            new PlanValidationNode(
                ValidationId: "VAL-FA-001",
                Title: "Verify fresh-attempt task",
                Description: "Checks TASK-001 output",
                AfterTaskIds: ["TASK-001"],
                BeforeTaskIds: [],
                Assertions: ["Build succeeds"],
                OutputIds: null,
                Mode: "automated",
                Commands: ["dotnet build"],
                RevalidateAtCompletion: false,
                Status: PlanValidationStatus.Failed,
                CompletedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
                Summary: "Build failed"),
        };

        var plan = MakePlanWithTaskAndValidation(validations: validations);
        _planStore.Save(plan);

        var decision = _handler.HandleFreshAttemptDecision(
            plan.PlanId, "TASK-001", plan.Tasks[0].Commit);

        Assert.That(decision.Allowed, Is.True);

        var reloaded = _planStore.Load(plan.PlanId)!;
        var val = reloaded.Validations!.First(v => v.ValidationId == "VAL-FA-001");
        Assert.That(val.Status, Is.EqualTo(PlanValidationStatus.Pending),
            "Failed dependent validation must be reset to Pending on upstream task recovery.");
    }

    // ── Test: user message contains provenance content on applied recovery ───

    [Test]
    public void HandleRepairDecision_Applied_UserMessageContainsProvenance()
    {
        var plan = MakePlanWithTaskAndValidation();
        _planStore.Save(plan);

        var decision = _handler.HandleRepairDecision(
            plan.PlanId, "TASK-001", plan.Tasks[0].Commit);

        Assert.That(decision.Allowed, Is.True);
        Assert.That(decision.UserMessage, Does.Contain("⚙ Recovery"));
        Assert.That(decision.UserMessage, Does.Contain("TASK-001"));
    }

    // ── Test: blocked recovery message includes full provenance chain ────────

    [Test]
    public void HandleRepairDecision_Blocked_UserMessageContainsChainSummary()
    {
        var plan = MakePlanWithTaskAndValidation();
        _planStore.Save(plan);

        // First succeeds
        _handler.HandleRepairDecision(plan.PlanId, "TASK-001", plan.Tasks[0].Commit);

        // Second is blocked — message should include provenance chain
        var blocked = _handler.HandleRepairDecision(
            plan.PlanId, "TASK-001", "newcommit1234567890abcdef12345678901234567");

        Assert.That(blocked.Allowed, Is.False);
        Assert.That(blocked.UserMessage, Does.Contain("⛔ Recovery"));
        Assert.That(blocked.UserMessage, Does.Contain("Prior attempts:"));
        Assert.That(blocked.UserMessage, Does.Contain("Attempt 1"));
    }

    // ── Test: missing plan returns not-allowed ───────────────────────────────

    [Test]
    public void HandleRepairDecision_MissingPlan_NotAllowed()
    {
        var decision = _handler.HandleRepairDecision(
            "NONEXISTENT", "TASK-001", null);

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.UserMessage, Does.Contain("not found"));
    }

    // ── Test: missing task returns not-allowed ───────────────────────────────

    [Test]
    public void HandleFreshAttemptDecision_MissingTask_NotAllowed()
    {
        var plan = MakePlanWithTaskAndValidation();
        _planStore.Save(plan);

        var decision = _handler.HandleFreshAttemptDecision(
            plan.PlanId, "NONEXISTENT-TASK", null);

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.UserMessage, Does.Contain("not found"));
    }
}
