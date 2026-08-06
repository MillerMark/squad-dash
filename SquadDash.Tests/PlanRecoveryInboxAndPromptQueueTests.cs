using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Tests verifying:
/// (1) Recovery decisions publish provenance content through the Inbox store.
/// (2) Blocked decisions do not enqueue prompts (prompt-queue orchestration guard).
/// </summary>
[TestFixture]
internal sealed class PlanRecoveryInboxAndPromptQueueTests
{
    private string _squadFolder = null!;
    private PlanStore _planStore = null!;
    private InboxStore _inboxStore = null!;
    private PlanRecoveryProvenanceService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _squadFolder = Path.Combine(Path.GetTempPath(), $"squad-inbox-prompt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_squadFolder);
        _planStore = new PlanStore(_squadFolder);
        _inboxStore = new InboxStore(_squadFolder);
        _service = new PlanRecoveryProvenanceService(_planStore);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_squadFolder, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Plan MakePlanWithTask(
        string planId = "PLAN-INBOX-001",
        string taskId = "TASK-001",
        string? taskCommit = "abc1234def5678901234567890abcdef12345678")
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
            ]);

        return new Plan(
            PlanId: planId,
            Revision: "rev-inbox-1",
            Source: PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Interrupted,
            Title: "Inbox Integration Test Plan",
            Branch: "feature/inbox-test",
            Summary: "Tests inbox provenance publishing.",
            Tasks: [task],
            ApprovalGates: [],
            Progress: new PlanProgress(1, 1),
            Timestamps: new PlanTimestamps(CreatedAt: DateTimeOffset.UtcNow.AddHours(-1)),
            InterruptionData: new PlanInterruptionData(
                Reason: "Invalid proof result",
                RecoveryState: PlanRecoveryState.PendingRecovery,
                LoopIteration: 1,
                InterruptedTaskId: taskId));
    }

    // ── Successful internal recovery remains out of the actionable Inbox ─────

    [Test]
    public void HandleRepairDecision_Applied_DoesNotPublishInboxNoise()
    {
        var plan = MakePlanWithTask();
        _planStore.Save(plan);

        var handler = new PlanRecoveryDecisionHandler(_service, _inboxStore);
        var decision = handler.HandleRepairDecision(plan.PlanId, "TASK-001", plan.Tasks[0].Commit);

        Assert.That(decision.Allowed, Is.True);

        Assert.That(_inboxStore.LoadAll(), Is.Empty);
    }

    // ── Item 1: Inbox message published on blocked recovery ─────────────────

    [Test]
    public void HandleRepairDecision_Blocked_PublishesInboxMessageWithBlockReason()
    {
        var plan = MakePlanWithTask();
        _planStore.Save(plan);

        var handler = new PlanRecoveryDecisionHandler(_service, _inboxStore);

        // First repair succeeds
        handler.HandleRepairDecision(plan.PlanId, "TASK-001", plan.Tasks[0].Commit);

        // Second repair is blocked
        var blocked = handler.HandleRepairDecision(
            plan.PlanId, "TASK-001", "newcommit1234567890abcdef12345678901234567");

        Assert.That(blocked.Allowed, Is.False);

        var messages = _inboxStore.LoadAll();
        var blockedMsg = messages.FirstOrDefault(m => m.Subject.Contains("Recovery blocked"));
        Assert.That(blockedMsg, Is.Not.Null, "Inbox should contain a 'Recovery blocked' message.");
        Assert.That(blockedMsg!.Subject, Does.Contain("TASK-001"));
        Assert.That(blockedMsg.Body, Does.Contain("exhausted"));
        Assert.That(blockedMsg.Body, Does.Contain("Reason:"));
        Assert.That(blockedMsg.Priority, Is.EqualTo("high"));
    }

    // ── Fresh-attempt success is also internal bookkeeping ───────────────────

    [Test]
    public void HandleFreshAttemptDecision_Applied_DoesNotPublishInboxNoise()
    {
        var plan = MakePlanWithTask();
        _planStore.Save(plan);

        var handler = new PlanRecoveryDecisionHandler(_service, _inboxStore);
        var decision = handler.HandleFreshAttemptDecision(plan.PlanId, "TASK-001", plan.Tasks[0].Commit);

        Assert.That(decision.Allowed, Is.True);

        Assert.That(_inboxStore.LoadAll(), Is.Empty);
    }

    [Test]
    public void ArchiveObsoleteAppliedNotifications_RemovesOnlySuccessfulRecoveryNotices()
    {
        _inboxStore.Save(new InboxMessage
        {
            Id = "applied",
            Subject = "Recovery applied: TASK-001",
            From = "SquadDash Recovery",
            Timestamp = DateTimeOffset.UtcNow,
            Body = "Internal bookkeeping",
            Priority = "low",
        });
        _inboxStore.Save(new InboxMessage
        {
            Id = "blocked",
            Subject = "Recovery blocked: TASK-001",
            From = "SquadDash Recovery",
            Timestamp = DateTimeOffset.UtcNow,
            Body = "Action required",
            Priority = "high",
        });

        PlanRecoveryDecisionHandler.ArchiveObsoleteAppliedNotifications(_inboxStore);

        Assert.Multiple(() =>
        {
            Assert.That(_inboxStore.GetById("applied"), Is.Null);
            Assert.That(_inboxStore.GetById("blocked"), Is.Not.Null);
        });
    }

    // ── Item 2: Progress correction when reopening a task ───────────────────

    [Test]
    public void ApplyRecoveryWithProvenance_DecrementsCompletedCount()
    {
        var task1 = new PlanTask(
            TaskId: "T1", Title: "Task 1", Description: "First task",
            DependsOn: [], Priority: "high",
            Status: PlanTaskStatus.Complete, Commit: "abc1234",
            CompletedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletionSummary: "Done",
            ProofRequirements: [new PlanTaskProofRequirement("r1", "ai-assessed", "Passes")],
            ProofEvidence: [new PlanTaskProofEvidence("r1", "ai-assessed", "Verified")]);

        var task2 = new PlanTask(
            TaskId: "T2", Title: "Task 2", Description: "Second task",
            DependsOn: [], Priority: "mid",
            Status: PlanTaskStatus.Complete, Commit: "def5678",
            CompletedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletionSummary: "Done");

        var plan = new Plan(
            PlanId: "PLAN-PROG-001",
            Revision: "rev-prog-1",
            Source: PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Executing,
            Title: "Progress Test Plan",
            Branch: "feature/progress",
            Summary: "Tests progress correction.",
            Tasks: [task1, task2],
            ApprovalGates: [],
            Progress: new PlanProgress(2, 2),
            Timestamps: new PlanTimestamps(CreatedAt: DateTimeOffset.UtcNow.AddHours(-1)));

        var recovered = PlanStoreUpdater.ApplyRecoveryWithProvenance(
            plan, "T1", "abc1234", recoveryKind: "fresh-attempt");

        Assert.That(recovered.Progress.CompletedCount, Is.EqualTo(1),
            "CompletedCount must decrease by 1 when a completed task is recovered to Pending.");
        Assert.That(recovered.Progress.TotalCount, Is.EqualTo(2),
            "TotalCount must remain unchanged.");
    }

    // ── Item 3: Stale transition preserves evidence ─────────────────────────

    [Test]
    public void InvalidateDependentValidations_UsesStaleTransitionPreservingEvidence()
    {
        var validation = new PlanValidationNode(
            ValidationId: "V1",
            Title: "Check output",
            Description: "Validates task output",
            AfterTaskIds: ["TASK-001"],
            BeforeTaskIds: [],
            Assertions: ["Output exists"],
            OutputIds: null,
            Mode: "automated",
            Commands: ["dotnet test"],
            RevalidateAtCompletion: false,
            Status: PlanValidationStatus.Passed,
            CompletedAt: DateTimeOffset.UtcNow.AddMinutes(-3),
            ValidatedCommit: "abc1234def5678901234567890abcdef12345678",
            Summary: "All passed",
            Evidence: ["test output: 5 passed, 0 failed"]);

        var plan = new Plan(
            PlanId: "PLAN-STALE-001",
            Revision: "rev-stale-1",
            Source: PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Executing,
            Title: "Stale Test Plan",
            Branch: "feature/stale",
            Summary: "Tests stale transition.",
            Tasks: [new PlanTask(
                TaskId: "TASK-001", Title: "Task", Description: "Task",
                DependsOn: [], Priority: "high",
                Status: PlanTaskStatus.Pending)],
            ApprovalGates: [],
            Progress: new PlanProgress(0, 1),
            Timestamps: new PlanTimestamps(CreatedAt: DateTimeOffset.UtcNow.AddHours(-1)),
            Validations: [validation]);

        var result = PlanStoreUpdater.InvalidateDependentValidationsForRecovery(plan, "TASK-001");

        var v = result.Validations![0];
        Assert.That(v.Status, Is.EqualTo(PlanValidationStatus.Stale),
            "Dependent validation must transition to Stale (not Pending).");
        Assert.That(v.ValidatedCommit, Is.EqualTo("abc1234def5678901234567890abcdef12345678"),
            "ValidatedCommit must be preserved for audit trail.");
        Assert.That(v.Evidence, Is.Not.Null,
            "Evidence must be preserved (not nulled) for audit.");
        Assert.That(v.Evidence![0], Is.EqualTo("test output: 5 passed, 0 failed"),
            "Original evidence content must remain intact.");
        Assert.That(v.Summary, Does.Contain("Stale"),
            "Summary should indicate the stale reason.");
    }
}
