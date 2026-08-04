using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ProofProvenanceRecoveryTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Plan MakePlanWithTask(
        string taskId = "TASK-001",
        string status = PlanTaskStatus.Complete,
        string? commit = "abc1234def5678901234567890abcdef12345678",
        string? completionSummary = "Implemented feature X",
        IReadOnlyList<PlanTaskProofRequirement>? proofRequirements = null,
        IReadOnlyList<PlanTaskProofEvidence>? proofEvidence = null,
        ProofProvenanceChain? existingChain = null)
    {
        var task = new PlanTask(
            TaskId: taskId,
            Title: "Test task",
            Description: "A test task",
            DependsOn: [],
            Priority: "high",
            Status: status,
            Commit: commit,
            CompletedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletionSummary: completionSummary,
            ProofRequirements: proofRequirements,
            ProofEvidence: proofEvidence,
            ProvenanceChain: existingChain);

        return new Plan(
            PlanId: "PLAN-001",
            Revision: "rev1",
            Source: PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Interrupted,
            Title: "Test Plan",
            Branch: "feature/test",
            Summary: "A test plan",
            Tasks: [task],
            ApprovalGates: [],
            Progress: new PlanProgress(0, 1),
            Timestamps: new PlanTimestamps(CreatedAt: DateTimeOffset.UtcNow.AddHours(-1)),
            InterruptionData: new PlanInterruptionData(
                Reason: "Task failed",
                RecoveryState: PlanRecoveryState.PendingRecovery,
                LoopIteration: 1,
                InterruptedTaskId: taskId));
    }

    // ── Provenance survives retry ─────────────────────────────────────────────

    [Test]
    public void ApplyRecoveryWithProvenance_Retry_PreservesOriginalAttemptProvenance()
    {
        var plan = MakePlanWithTask(
            proofRequirements: [new PlanTaskProofRequirement("req-1", "host-recorded", "Build passes")],
            proofEvidence: [new PlanTaskProofEvidence("req-1", "host-recorded", "Build succeeded")]);

        var result = PlanStoreUpdater.ApplyRecoveryWithProvenance(
            plan, "TASK-001", "abc1234def5678901234567890abcdef12345678", "retry");

        var task = result.Tasks[0];
        Assert.That(task.ProvenanceChain, Is.Not.Null);
        Assert.That(task.ProvenanceChain!.Entries, Has.Count.EqualTo(1));

        var entry = task.ProvenanceChain.Entries[0];
        Assert.That(entry.TaskId, Is.EqualTo("TASK-001"));
        Assert.That(entry.CommitShortSha, Is.EqualTo("abc1234"));
        Assert.That(entry.RecoveryKind, Is.EqualTo("retry"));
        Assert.That(entry.SourceKind, Is.EqualTo(EvidenceSourceKind.HostRecorded.ToString()));
    }

    // ── Provenance survives replan — chain grows ──────────────────────────────

    [Test]
    public void ApplyRecoveryWithProvenance_Replan_ChainGrowsWithEachAttempt()
    {
        var existingEntry = new ProofProvenanceEntry(
            TaskId: "TASK-001",
            SourceLabel: "Host-recorded commit",
            SourceKind: EvidenceSourceKind.HostRecorded.ToString(),
            CommitShortSha: "1111111",
            CommitFullSha: "1111111222222233333334444444555555566666",
            RecoveryKind: "retry",
            RecordedAt: DateTimeOffset.UtcNow.AddMinutes(-10));

        var existingChain = new ProofProvenanceChain([existingEntry]);

        var plan = MakePlanWithTask(
            commit: "bbb2222ccc3333ddd4444eee5555fff6666aaa77",
            proofRequirements: [new PlanTaskProofRequirement("req-1", "automated-test", "Tests pass")],
            proofEvidence: [new PlanTaskProofEvidence("req-1", "automated-test", "All 42 tests passed")],
            existingChain: existingChain);

        var result = PlanStoreUpdater.ApplyRecoveryWithProvenance(
            plan, "TASK-001", "bbb2222ccc3333ddd4444eee5555fff6666aaa77", "replan");

        var task = result.Tasks[0];
        Assert.That(task.ProvenanceChain!.Entries, Has.Count.EqualTo(2));
        Assert.That(task.ProvenanceChain.Entries[0].RecoveryKind, Is.EqualTo("retry"));
        Assert.That(task.ProvenanceChain.Entries[1].RecoveryKind, Is.EqualTo("replan"));
        Assert.That(task.ProvenanceChain.Entries[1].CommitShortSha, Is.EqualTo("bbb2222"));
    }

    // ── JSON round-trip ───────────────────────────────────────────────────────

    [Test]
    public void ProofProvenanceChain_JsonRoundTrip_PreservesEquality()
    {
        var chain = new ProofProvenanceChain([
            new ProofProvenanceEntry(
                TaskId: "TASK-001",
                SourceLabel: "Host-recorded commit",
                SourceKind: EvidenceSourceKind.HostRecorded.ToString(),
                CommitShortSha: "abc1234",
                CommitFullSha: "abc1234def5678901234567890abcdef12345678",
                Summary: "Build passed",
                RecoveryKind: "retry",
                RecordedAt: DateTimeOffset.Parse("2026-08-03T10:00:00Z")),
            new ProofProvenanceEntry(
                TaskId: "TASK-001",
                SourceLabel: "Automated test evidence",
                SourceKind: EvidenceSourceKind.Automated.ToString(),
                CommitShortSha: "def5678",
                CommitFullSha: "def5678abc1234901234567890abcdef12345678",
                Summary: "Tests green",
                RecoveryKind: "replan",
                RecordedAt: DateTimeOffset.Parse("2026-08-03T11:00:00Z")),
        ]);

        var json = JsonSerializer.Serialize(chain);
        var deserialized = JsonSerializer.Deserialize<ProofProvenanceChain>(json);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Entries, Has.Count.EqualTo(2));
        Assert.That(deserialized.Entries[0].TaskId, Is.EqualTo("TASK-001"));
        Assert.That(deserialized.Entries[0].CommitShortSha, Is.EqualTo("abc1234"));
        Assert.That(deserialized.Entries[0].RecoveryKind, Is.EqualTo("retry"));
        Assert.That(deserialized.Entries[1].SourceKind, Is.EqualTo(EvidenceSourceKind.Automated.ToString()));
        Assert.That(deserialized.Entries[1].RecoveryKind, Is.EqualTo("replan"));
    }

    // ── Recovery with no prior provenance — graceful empty chain ──────────────

    [Test]
    public void ApplyRecoveryWithProvenance_NoPriorProvenance_CreatesChainWithSingleEntry()
    {
        var plan = MakePlanWithTask(proofRequirements: null, proofEvidence: null);

        var result = PlanStoreUpdater.ApplyRecoveryWithProvenance(
            plan, "TASK-001", "abc1234def5678901234567890abcdef12345678", "retry");

        var task = result.Tasks[0];
        Assert.That(task.ProvenanceChain, Is.Not.Null);
        Assert.That(task.ProvenanceChain!.Entries, Has.Count.EqualTo(1));

        var entry = task.ProvenanceChain.Entries[0];
        Assert.That(entry.TaskId, Is.EqualTo("TASK-001"));
        Assert.That(entry.SourceLabel, Is.EqualTo("Host-recorded commit"));
        Assert.That(entry.Summary, Is.EqualTo("Implemented feature X"));
    }

    // ── Multiple retries accumulate ordered entries ───────────────────────────

    [Test]
    public void ApplyRecoveryWithProvenance_MultipleRetries_AccumulatesOrderedEntries()
    {
        var plan = MakePlanWithTask(
            commit: "aaa1111bbb2222ccc3333ddd4444eee5555fff66",
            proofRequirements: [new PlanTaskProofRequirement("req-1", "host-recorded", "Build")],
            proofEvidence: [new PlanTaskProofEvidence("req-1", "host-recorded", "Attempt 1 evidence")]);

        // First recovery
        var after1 = PlanStoreUpdater.ApplyRecoveryWithProvenance(
            plan, "TASK-001", "aaa1111bbb2222ccc3333ddd4444eee5555fff66", "retry");

        // Simulate second attempt completing
        var task1 = after1.Tasks[0] with
        {
            Status = PlanTaskStatus.Complete,
            Commit = "bbb2222ccc3333ddd4444eee5555fff6666ggg77",
            CompletionSummary = "Second attempt done",
            ProofRequirements = [new PlanTaskProofRequirement("req-1", "host-recorded", "Build passes")],
            ProofEvidence = [new PlanTaskProofEvidence("req-1", "host-recorded", "Attempt 2 evidence")],
        };
        var planAfter1 = after1 with { Tasks = [task1] };

        // Second recovery
        var after2 = PlanStoreUpdater.ApplyRecoveryWithProvenance(
            planAfter1, "TASK-001", "bbb2222ccc3333ddd4444eee5555fff6666ggg77", "retry");

        var finalTask = after2.Tasks[0];
        Assert.That(finalTask.ProvenanceChain!.Entries, Has.Count.EqualTo(2));
        Assert.That(finalTask.ProvenanceChain.Entries[0].CommitShortSha, Is.EqualTo("aaa1111"));
        Assert.That(finalTask.ProvenanceChain.Entries[1].CommitShortSha, Is.EqualTo("bbb2222"));
        Assert.That(finalTask.Status, Is.EqualTo(PlanTaskStatus.Pending));
    }

    // ── Host result includes provenance summary ──────────────────────────────

    [Test]
    public void ProvenanceChain_BuildSummary_IncludesAllAttempts()
    {
        var chain = new ProofProvenanceChain([
            new ProofProvenanceEntry(
                TaskId: "TASK-001",
                SourceLabel: "Host-recorded commit",
                SourceKind: EvidenceSourceKind.HostRecorded.ToString(),
                CommitShortSha: "abc1234",
                Summary: "First try"),
            new ProofProvenanceEntry(
                TaskId: "TASK-001",
                SourceLabel: "Automated test evidence",
                SourceKind: EvidenceSourceKind.Automated.ToString(),
                CommitShortSha: "def5678",
                Summary: "Second try"),
        ]);

        var summary = chain.BuildSummary();

        Assert.That(summary, Does.Contain("Attempt 1"));
        Assert.That(summary, Does.Contain("abc1234"));
        Assert.That(summary, Does.Contain("First try"));
        Assert.That(summary, Does.Contain("Attempt 2"));
        Assert.That(summary, Does.Contain("def5678"));
        Assert.That(summary, Does.Contain("Second try"));
    }

    [Test]
    public void ProvenanceChain_BuildSummary_EmptyChain_ReturnsEmpty()
    {
        var chain = ProofProvenanceChain.Empty;
        Assert.That(chain.BuildSummary(), Is.EqualTo(string.Empty));
    }

    // ── Task not found returns plan unchanged ────────────────────────────────

    [Test]
    public void ApplyRecoveryWithProvenance_UnknownTaskId_ReturnsPlanUnchanged()
    {
        var plan = MakePlanWithTask();

        var result = PlanStoreUpdater.ApplyRecoveryWithProvenance(
            plan, "NONEXISTENT-TASK", "abc1234", "retry");

        Assert.That(result, Is.SameAs(plan));
    }

    // ── Recovery resets task status ───────────────────────────────────────────

    [Test]
    public void ApplyRecoveryWithProvenance_ResetsTaskToPending()
    {
        var plan = MakePlanWithTask(status: PlanTaskStatus.Failed);

        var result = PlanStoreUpdater.ApplyRecoveryWithProvenance(
            plan, "TASK-001", null, "replan");

        var task = result.Tasks[0];
        Assert.That(task.Status, Is.EqualTo(PlanTaskStatus.Pending));
        Assert.That(task.Commit, Is.Null);
        Assert.That(task.CompletedAt, Is.Null);
        Assert.That(task.CompletionSummary, Is.Null);
    }

    // ── Recovery updates interruption recovery state ─────────────────────────

    [Test]
    public void ApplyRecoveryWithProvenance_SetsRecoveryInProgress()
    {
        var plan = MakePlanWithTask();

        var result = PlanStoreUpdater.ApplyRecoveryWithProvenance(
            plan, "TASK-001", "abc1234", "retry");

        Assert.That(result.InterruptionData, Is.Not.Null);
        Assert.That(result.InterruptionData!.RecoveryState, Is.EqualTo(PlanRecoveryState.RecoveryInProgress));
    }
}
