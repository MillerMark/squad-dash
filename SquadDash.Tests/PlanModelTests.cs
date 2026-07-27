using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanModelTests
{
    // ─── Lifecycle status helpers ────────────────────────────────────────────

    [TestCase(PlanLifecycleStatus.Staged)]
    [TestCase(PlanLifecycleStatus.Approved)]
    [TestCase(PlanLifecycleStatus.Executing)]
    [TestCase(PlanLifecycleStatus.AwaitingApproval)]
    [TestCase(PlanLifecycleStatus.Interrupted)]
    [TestCase(PlanLifecycleStatus.Stopped)]
    [TestCase(PlanLifecycleStatus.Completed)]
    [TestCase(PlanLifecycleStatus.Archived)]
    [TestCase(PlanLifecycleStatus.Blocked)]
    public void AllKnownStatuses_ArePresentInAllSet(string status)
    {
        Assert.That(PlanLifecycleStatus.All, Does.Contain(status));
    }

    [TestCase(PlanLifecycleStatus.Stopped,    ExpectedResult = true)]
    [TestCase(PlanLifecycleStatus.Completed,  ExpectedResult = true)]
    [TestCase(PlanLifecycleStatus.Archived,   ExpectedResult = true)]
    [TestCase(PlanLifecycleStatus.Executing,  ExpectedResult = false)]
    [TestCase(PlanLifecycleStatus.Staged,     ExpectedResult = false)]
    [TestCase(PlanLifecycleStatus.Interrupted,ExpectedResult = false)]
    public bool IsTerminal_ReturnsCorrectValue(string status) =>
        PlanLifecycleStatus.IsTerminal(status);

    // ─── Plan record construction ────────────────────────────────────────────

    [Test]
    public void Plan_CanBeConstructed_WithMinimalRequiredFields()
    {
        var plan = MakeMinimalPlan();

        Assert.Multiple(() =>
        {
            Assert.That(plan.PlanId,          Is.EqualTo("PROJ-20260101"));
            Assert.That(plan.Revision,        Is.Not.Empty);
            Assert.That(plan.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Staged));
            Assert.That(plan.ApprovalGates,   Is.Empty);
            Assert.That(plan.InterruptionData, Is.Null);
            Assert.That(plan.HostRevision,    Is.Null);
        });
    }

    [Test]
    public void Plan_WithInterruptionData_RoundTripsCorrectly()
    {
        var interruption = new PlanInterruptionData(
            Reason:              "Loop timeout",
            RecoveryState:       PlanRecoveryState.PendingRecovery,
            LoopIteration:       3,
            InterruptedTaskId:   "PROJ-20260101-002",
            LastCompletedTaskId: "PROJ-20260101-001",
            LastCommit:          "abc1234",
            AffectedPaths:       ["src/Foo.cs", "src/Bar.cs"],
            PartialWorkEvidence: "Unit tests written; integration test pending");

        var plan = MakeMinimalPlan() with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            InterruptionData = interruption,
        };

        var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
        var restored = JsonSerializer.Deserialize<Plan>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.Multiple(() =>
        {
            Assert.That(restored!.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
            Assert.That(restored.InterruptionData!.Reason, Is.EqualTo("Loop timeout"));
            Assert.That(restored.InterruptionData.LastCommit, Is.EqualTo("abc1234"));
            Assert.That(restored.InterruptionData.AffectedPaths, Has.Count.EqualTo(2));
            Assert.That(restored.InterruptionData.RecoveryState,
                Is.EqualTo(PlanRecoveryState.PendingRecovery));
        });
    }

    [Test]
    public void Plan_WithApprovalGate_RoundTripsCorrectly()
    {
        var gate = new PlanApprovalGate(
            GateId:       "gate-1",
            Message:      "Human review required before deployment",
            AfterTaskIds: ["PROJ-20260101-002"],
            BeforeTaskIds:["PROJ-20260101-003"],
            Status:       PlanGateStatus.AwaitingApproval,
            RequestedAt:  DateTimeOffset.UtcNow);

        var plan = MakeMinimalPlan() with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            ApprovalGates   = [gate],
        };

        var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
        var restored = JsonSerializer.Deserialize<Plan>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.Multiple(() =>
        {
            Assert.That(restored!.ApprovalGates, Has.Count.EqualTo(1));
            Assert.That(restored.ApprovalGates[0].GateId,  Is.EqualTo("gate-1"));
            Assert.That(restored.ApprovalGates[0].Status,  Is.EqualTo(PlanGateStatus.AwaitingApproval));
            Assert.That(restored.ApprovalGates[0].AfterTaskIds, Is.EquivalentTo(new[] { "PROJ-20260101-002" }));
        });
    }

    [Test]
    public void Plan_OptionalFields_OmittedInJson_WhenNull()
    {
        var plan = MakeMinimalPlan();
        var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("\"interruptionData\""));
            Assert.That(json, Does.Not.Contain("\"hostRevision\""));
            Assert.That(json, Does.Not.Contain("\"acceptedAt\""));
        });
    }

    // ─── PlanTask record ─────────────────────────────────────────────────────

    [Test]
    public void PlanTask_WithCompletionEvidence_RoundTrips()
    {
        var task = new PlanTask(
            TaskId:            "PROJ-20260101-001",
            Title:             "Bootstrap project",
            Description:       "Set up scaffolding",
            DependsOn:         [],
            Priority:          "critical",
            Status:            PlanTaskStatus.Complete,
            Commit:            "deadbeef",
            CompletedAt:       DateTimeOffset.UtcNow,
            CompletionSummary: "All tests pass");

        var json = JsonSerializer.Serialize(task, new JsonSerializerOptions { WriteIndented = true });
        var restored = JsonSerializer.Deserialize<PlanTask>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.Multiple(() =>
        {
            Assert.That(restored!.Commit,            Is.EqualTo("deadbeef"));
            Assert.That(restored.CompletionSummary,  Is.EqualTo("All tests pass"));
            Assert.That(restored.Status,             Is.EqualTo(PlanTaskStatus.Complete));
        });
    }

    // ─── Progress calculation ────────────────────────────────────────────────

    [Test]
    public void PlanProgress_TracksCompletedAndTotal()
    {
        var progress = new PlanProgress(CompletedCount: 2, TotalCount: 5, ExecutingTaskId: "PROJ-001-003");

        Assert.Multiple(() =>
        {
            Assert.That(progress.CompletedCount,   Is.EqualTo(2));
            Assert.That(progress.TotalCount,        Is.EqualTo(5));
            Assert.That(progress.ExecutingTaskId,   Is.EqualTo("PROJ-001-003"));
        });
    }

    // ─── Factory helper ──────────────────────────────────────────────────────

    private static Plan MakeMinimalPlan() => new(
        PlanId:          "PROJ-20260101",
        Revision:        "abcdef1234567890",
        Source:          PlanSource.TasksJson,
        LifecycleStatus: PlanLifecycleStatus.Staged,
        Title:           "Test plan",
        Branch:          "feature/test",
        Summary:         "A minimal plan for unit tests",
        Tasks:
        [
            new PlanTask(
                TaskId:      "PROJ-20260101-001",
                Title:       "First task",
                Description: "Do the thing",
                DependsOn:   [],
                Priority:    "high",
                Status:      PlanTaskStatus.Pending),
            new PlanTask(
                TaskId:      "PROJ-20260101-002",
                Title:       "Second task",
                Description: "Do more things",
                DependsOn:   ["PROJ-20260101-001"],
                Priority:    "high",
                Status:      PlanTaskStatus.Pending),
        ],
        ApprovalGates: [],
        Progress:       new PlanProgress(CompletedCount: 0, TotalCount: 2),
        Timestamps:     new PlanTimestamps(CreatedAt: DateTimeOffset.UtcNow));
}
