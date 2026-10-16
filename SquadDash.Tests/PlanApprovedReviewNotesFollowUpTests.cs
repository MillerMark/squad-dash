using System;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanApprovedReviewNotesFollowUpTests
{
    [Test]
    public void BuildPrompt_IncludesOnlyApprovedReviewItemsWithNotes()
    {
        var plan = MakeReviewPlan();
        var gate = plan.ApprovalGates[0];
        var items = PlanHumanReviewChecklist.Build(plan, [gate]);
        var approvedItem = items[0];
        var unapprovedItem = items[1];
        var withApprovedNote = PlanStoreUpdater.ApplyGateHumanReviewResponse(
            plan,
            gate.GateId,
            approvedItem.ItemId,
            true,
            "Selection is hard to read in region text.",
            approvedItem.CandidateCommit,
            approvedItem.ReviewTarget);
        var withNoNote = PlanStoreUpdater.ApplyGateHumanReviewResponse(
            withApprovedNote,
            gate.GateId,
            unapprovedItem.ItemId,
            false,
            "This belongs to correction, not final follow-up.",
            unapprovedItem.CandidateCommit,
            unapprovedItem.ReviewTarget);
        var completed = withNoNote with
        {
            LifecycleStatus = PlanLifecycleStatus.Completed,
            ApprovalGates = withNoNote.ApprovalGates
                .Select(candidate => candidate with { Status = PlanGateStatus.Approved })
                .ToArray(),
            Timestamps = withNoNote.Timestamps with { CompletedAt = DateTimeOffset.UtcNow },
        };

        var prompt = PlanApprovedReviewNotesFollowUp.BuildPrompt(completed);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Is.Not.Null);
            Assert.That(prompt, Does.Contain("Answer: Yes"));
            Assert.That(prompt, Does.Contain("Selection is hard to read in region text."));
            Assert.That(prompt, Does.Not.Contain("This belongs to correction, not final follow-up."));
        });
    }

    [Test]
    public void BuildPrompt_ReturnsNullUntilPlanIsCompleted()
    {
        var plan = MakeCompletedPlan() with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
        };

        Assert.That(PlanApprovedReviewNotesFollowUp.BuildPrompt(plan), Is.Null);
    }

    [Test]
    public void BuildCompletionKey_ChangesWithPlanRevision()
    {
        var plan = MakeCompletedPlan();

        Assert.That(
            PlanApprovedReviewNotesFollowUp.BuildCompletionKey(plan with { Revision = "rev-2" }),
            Is.Not.EqualTo(PlanApprovedReviewNotesFollowUp.BuildCompletionKey(plan)));
    }

    private static Plan MakeCompletedPlan()
    {
        var plan = MakeReviewPlan();
        return plan with
        {
            LifecycleStatus = PlanLifecycleStatus.Completed,
            ApprovalGates = plan.ApprovalGates
                .Select(candidate => candidate with { Status = PlanGateStatus.Approved })
                .ToArray(),
            Timestamps = plan.Timestamps with { CompletedAt = DateTimeOffset.UtcNow },
        };
    }

    private static Plan MakeReviewPlan()
    {
        var tasks = new[]
        {
            new PlanTask(
                "task-1",
                "Task 1",
                "Done",
                [],
                "high",
                PlanTaskStatus.Complete,
                Commit: "abc1234567890",
                CompletedAt: DateTimeOffset.UtcNow),
        };
        var gate = new PlanApprovalGate(
            "gate-1",
            "Review completed task",
            ["task-1"],
            [],
            PlanGateStatus.AwaitingApproval,
            Question: "Is the selection readable in region text? Is the contrast acceptable?");
        return new Plan(
            "PLAN-001",
            "rev-1",
            PlanSource.DecomposeDecision,
            PlanLifecycleStatus.AwaitingApproval,
            "Test Plan",
            "main",
            "Summary",
            tasks,
            [gate],
            new PlanProgress(1, 1),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }
}
