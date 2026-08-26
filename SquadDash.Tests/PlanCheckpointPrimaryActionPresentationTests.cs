using System;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanCheckpointPrimaryActionPresentationTests
{
    [Test]
    public void Resolve_OrdinaryGateAfterCompletedTask_DoesNotClaimTaskCompletion()
    {
        var task = new PlanTask(
            "DONE", "Already done", "Done", [], "high", PlanTaskStatus.Complete,
            DisplayStepLabel: "4");
        var gate = new PlanApprovalGate(
            "GATE", "Review the completed boundary", [task.TaskId], [],
            PlanGateStatus.AwaitingApproval);
        var plan = BuildPlan(task, gate);

        var action = PlanCheckpointPrimaryActionPresentationResolver.Resolve(plan, [gate.GateId]);

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind, Is.EqualTo(PlanCheckpointPrimaryActionKind.ApproveCheckpoint));
            Assert.That(action.Label, Is.EqualTo("Approve Checkpoint & Continue"));
            Assert.That(action.Label, Does.Not.Contain("Step 4"));
            Assert.That(action.Label, Does.Not.Contain("Complete"));
            Assert.That(action.ReviewAttention, Is.Null);
        });
    }

    [Test]
    public void Resolve_HumanReviewGate_UsesOneAtomicCompletionAction()
    {
        const string commit = "abcdef1234567890";
        var task = new PlanTask(
            "REVIEW", "Review UI", "Review", [], "high",
            PlanTaskStatus.HumanReviewRequired,
            Handoff: new PlanTaskHandoff(
                commit, "Implemented", ["Feature.cs"], null, DateTimeOffset.UtcNow),
            DisplayStepLabel: "4");
        var gate = new PlanApprovalGate(
            "GATE", "Observe the UI", [task.TaskId], [],
            PlanGateStatus.AwaitingApproval,
            ProofRequirements:
            [new PlanTaskProofRequirement(
                "visual", "live-ui-observation", "Observe", "Does it work?")],
            HumanReviewSelections:
            [new PlanHumanReviewSelection("visual", true, commit, DateTimeOffset.UtcNow)]);
        var plan = BuildPlan(task, gate);

        var action = PlanCheckpointPrimaryActionPresentationResolver.Resolve(plan, [gate.GateId]);

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind,
                Is.EqualTo(PlanCheckpointPrimaryActionKind.CompleteHumanReviewTasks));
            Assert.That(action.Label, Is.EqualTo("Step 4 Is Complete & Continue"));
            Assert.That(action.Hint, Does.Contain("one durable transition"));
            Assert.That(action.ReviewAttention?.Task, Is.SameAs(task));
            Assert.That(action.ReviewAttention?.Gate, Is.SameAs(gate));
        });
    }

    [Test]
    public void Resolve_AggregatedBoundary_IncludesHumanReviewTaskInPrimaryAction()
    {
        const string commit = "abcdef1234567890";
        var reviewTask = new PlanTask(
            "REVIEW", "Review UI", "Review", [], "high",
            PlanTaskStatus.HumanReviewRequired,
            Handoff: new PlanTaskHandoff(
                commit, "Implemented", ["Feature.cs"], null, DateTimeOffset.UtcNow),
            DisplayStepLabel: "4");
        var completedTask = new PlanTask(
            "DONE", "Completed work", "Done", [], "high", PlanTaskStatus.Complete,
            DisplayStepLabel: "5");
        var reviewGate = new PlanApprovalGate(
            "GATE-REVIEW", "Observe the UI", [reviewTask.TaskId], [],
            PlanGateStatus.AwaitingApproval,
            ProofRequirements:
            [new PlanTaskProofRequirement(
                "visual", "live-ui-observation", "Observe", "Does it work?")],
            HumanReviewSelections:
            [new PlanHumanReviewSelection("visual", true, commit, DateTimeOffset.UtcNow)]);
        var ordinaryGate = new PlanApprovalGate(
            "GATE-ORDINARY", "Approve completed work", [completedTask.TaskId], [],
            PlanGateStatus.AwaitingApproval);
        var plan = new Plan(
            "PLAN", "revision", PlanSource.DecomposeDecision,
            PlanLifecycleStatus.AwaitingApproval, "Plan", "feature/plan", "Summary",
            [reviewTask, completedTask], [reviewGate, ordinaryGate],
            new PlanProgress(1, 2, reviewTask.TaskId),
            new PlanTimestamps(DateTimeOffset.UtcNow));

        var action = PlanCheckpointPrimaryActionPresentationResolver.Resolve(
            plan,
            [reviewGate.GateId, ordinaryGate.GateId]);

        Assert.Multiple(() =>
        {
            Assert.That(action.Kind,
                Is.EqualTo(PlanCheckpointPrimaryActionKind.CompleteHumanReviewTasks));
            Assert.That(action.Label,
                Is.EqualTo("Complete Step 4 & Approve Checkpoints & Continue"));
            Assert.That(action.ReviewAttentions, Has.Count.EqualTo(1));
            Assert.That(action.ReviewAttentions[0].Task, Is.SameAs(reviewTask));
        });
    }

    private static Plan BuildPlan(PlanTask task, PlanApprovalGate gate) => new(
        "PLAN", "revision", PlanSource.DecomposeDecision,
        PlanLifecycleStatus.AwaitingApproval, "Plan", "feature/plan", "Summary",
        [task], [gate], new PlanProgress(
            task.Status == PlanTaskStatus.Complete ? 1 : 0,
            1,
            task.Status == PlanTaskStatus.Complete ? null : task.TaskId),
        new PlanTimestamps(DateTimeOffset.UtcNow));
}
