using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanTaskInspectorPresentationTests
{
    [Test]
    public void ApprovedBeforeGate_DoesNotShowAsTaskGuidanceAfterBoundary()
    {
        var selectedTask = MakeTask("BLAZORAIGEN-20260316-007", PlanTaskStatus.Executing);
        var plan = MakePlan(
            tasks:
            [
                MakeTask("BLAZORAIGEN-20260316-005", PlanTaskStatus.Complete),
                MakeTask("BLAZORAIGEN-20260316-006", PlanTaskStatus.Complete),
                selectedTask,
            ],
            gates:
            [
                MakeGate(
                    "BLAZORAIGEN-20260316-VAL-001-HUMAN-PROOF",
                    PlanGateStatus.Approved,
                    ["BLAZORAIGEN-20260316-005", "BLAZORAIGEN-20260316-006"],
                    ["BLAZORAIGEN-20260316-007"]),
            ]) with
        {
            Progress = new PlanProgress(2, 3, selectedTask.TaskId),
        };

        var guidance = PlanTaskInspectorPresentation.SelectApprovalGuidanceGates(selectedTask, plan);

        Assert.That(guidance, Is.Empty);
    }

    [Test]
    public void AwaitingBeforeGate_RemainsTaskGuidanceWhileTaskIsBlocked()
    {
        var selectedTask = MakeTask("task-002");
        var gate = MakeGate("gate-001", PlanGateStatus.AwaitingApproval, ["task-001"], ["task-002"]);
        var plan = MakePlan(
            lifecycleStatus: PlanLifecycleStatus.AwaitingApproval,
            tasks: [MakeTask("task-001", PlanTaskStatus.Complete), selectedTask],
            gates: [gate]);

        var guidance = PlanTaskInspectorPresentation.SelectApprovalGuidanceGates(selectedTask, plan);

        Assert.That(guidance.Select(item => item.GateId), Is.EqualTo(new[] { gate.GateId }));
    }

    [Test]
    public void AfterGate_RemainsTaskGuidanceForTaskOwnedProof()
    {
        var selectedTask = MakeTask("task-008", PlanTaskStatus.Complete);
        var gate = MakeGate("task-008-human-proof", PlanGateStatus.Approved, ["task-008"], []);
        var plan = MakePlan(tasks: [selectedTask], gates: [gate]);

        var guidance = PlanTaskInspectorPresentation.SelectApprovalGuidanceGates(selectedTask, plan);

        Assert.That(guidance.Select(item => item.GateId), Is.EqualTo(new[] { gate.GateId }));
    }

    [Test]
    public void ExecutingTask_BuildsCurrentActivityCallout()
    {
        var selectedTask = MakeTask("BLAZORAIGEN-20260316-007", PlanTaskStatus.Executing);
        var plan = MakePlan(tasks: [selectedTask]) with
        {
            Progress = new PlanProgress(6, 8, selectedTask.TaskId),
        };

        var summary = PlanTaskInspectorPresentation.BuildActivitySummary(selectedTask, plan);

        Assert.Multiple(() =>
        {
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary!.State, Is.EqualTo(PlanTaskActivityState.Executing));
            Assert.That(summary.Label, Is.EqualTo("Working now"));
            Assert.That(summary.ShouldShowCallout, Is.True);
        });
    }

    private static PlanTask MakeTask(
        string taskId,
        string status = PlanTaskStatus.Pending,
        IReadOnlyList<string>? dependsOn = null) =>
        new(
            TaskId: taskId,
            Title: $"Task {taskId}",
            Description: $"Description for {taskId}",
            DependsOn: dependsOn ?? [],
            Priority: "high",
            Status: status);

    private static PlanApprovalGate MakeGate(
        string gateId,
        string status,
        IReadOnlyList<string> afterTaskIds,
        IReadOnlyList<string> beforeTaskIds) =>
        new(
            GateId: gateId,
            Message: $"Approve {gateId}",
            AfterTaskIds: afterTaskIds,
            BeforeTaskIds: beforeTaskIds,
            Status: status);

    private static Plan MakePlan(
        string lifecycleStatus = PlanLifecycleStatus.Executing,
        IReadOnlyList<PlanTask>? tasks = null,
        IReadOnlyList<PlanApprovalGate>? gates = null) =>
        new(
            PlanId: "PLAN-TEST",
            Revision: "rev1",
            Source: PlanSource.DecomposeDecision,
            LifecycleStatus: lifecycleStatus,
            Title: "Test Plan",
            Branch: "feature/test",
            Summary: "A test plan",
            Tasks: tasks ?? [],
            ApprovalGates: gates ?? [],
            Progress: new PlanProgress(0, tasks?.Count ?? 0),
            Timestamps: new PlanTimestamps(CreatedAt: DateTimeOffset.UtcNow));
}
