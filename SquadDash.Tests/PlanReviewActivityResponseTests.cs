using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanReviewActivityResponseTests
{
    [TestCase(PlanReviewActivityKind.ManualCorrection)]
    [TestCase(PlanReviewActivityKind.ReviewDiscussion)]
    public void TryParse_AcceptsActionableReviewActivity(string activity)
    {
        var text = $$"""
            Visible response.
            PLAN_REVIEW_ACTIVITY_JSON:
            {"planId":"PLAN-1","revision":"rev","activity":"{{activity}}","taskIds":["TASK-1"],"summary":"Footer attribution was investigated."}
            """;

        Assert.That(PlanReviewActivityResponseParser.TryParse(text, out var response), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(response!.Activity, Is.EqualTo(activity));
            Assert.That(response.TaskIds, Is.EqualTo(new[] { "TASK-1" }));
        });
    }

    [Test]
    public void TryParse_RejectsPassiveOrUnknownActivity()
    {
        const string text = """
            PLAN_REVIEW_ACTIVITY_JSON:
            {"planId":"PLAN-1","revision":"rev","activity":"feedback","taskIds":["TASK-1"],"summary":"Missing footer."}
            """;

        Assert.That(PlanReviewActivityResponseParser.TryParse(text, out _), Is.False);
    }

    [Test]
    public void ProtocolContext_InterruptedHumanReview_AllowsManualCorrectionWithoutPlanResume()
    {
        var plan = MakeInterruptedPlan();

        var context = PlanReviewActivityResponseParser.BuildProtocolContext([plan]);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("mode=interrupted-human-review"));
            Assert.That(context, Does.Contain("A bare \"fix it\""));
            Assert.That(context, Does.Contain("manual-correction"));
            Assert.That(context, Does.Contain("assess-and-continue"));
            Assert.That(context, Does.Contain("Do not reopen the task"));
        });
    }

    [Test]
    public void ApplyManualReviewActivity_PersistsEvidenceWithoutAdvancingPlan()
    {
        var plan = MakeInterruptedPlan();
        var recordedAt = DateTimeOffset.Parse("2026-08-13T12:00:00Z");

        var updated = PlanStoreUpdater.ApplyManualReviewActivity(
            plan,
            ["TASK-1"],
            "Moved model attribution into the completion footer.",
            recordedAt);

        Assert.Multiple(() =>
        {
            Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.Interrupted));
            Assert.That(updated.Tasks[0].Status, Is.EqualTo(PlanTaskStatus.HumanReviewRequired));
            Assert.That(updated.Progress, Is.EqualTo(plan.Progress));
            Assert.That(updated.Tasks[0].ReviewActivity, Has.Count.EqualTo(1));
            Assert.That(updated.Tasks[0].ReviewActivity![0].Summary,
                Is.EqualTo("Moved model attribution into the completion footer."));
            Assert.That(updated.Tasks[0].ReviewActivity![0].RecordedAt, Is.EqualTo(recordedAt));
        });
    }

    [Test]
    public void ApplyManualReviewActivity_AwaitingGate_KeepsGateAwaitingApproval()
    {
        var plan = MakeInterruptedPlan() with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            InterruptionData = null,
            Tasks =
            [
                new PlanTask("TASK-1", "Footer", "Work", [], "high", PlanTaskStatus.Complete),
            ],
            ApprovalGates =
            [
                new PlanApprovalGate(
                    "GATE-1", "Review footer", ["TASK-1"], [], PlanGateStatus.AwaitingApproval),
            ],
            Progress = new PlanProgress(1, 1),
        };

        var updated = PlanStoreUpdater.ApplyManualReviewActivity(
            plan, ["TASK-1"], "Adjusted the footer interactively.");

        Assert.Multiple(() =>
        {
            Assert.That(updated.LifecycleStatus, Is.EqualTo(PlanLifecycleStatus.AwaitingApproval));
            Assert.That(updated.ApprovalGates[0].Status, Is.EqualTo(PlanGateStatus.AwaitingApproval));
            Assert.That(updated.Tasks[0].Status, Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(updated.Tasks[0].ReviewActivity, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ProtocolContext_MultipleReviewBoundaries_RequiresExplicitPlanSelection()
    {
        var first = MakeInterruptedPlan();
        var second = MakeInterruptedPlan() with
        {
            PlanId = "PLAN-2",
            Revision = "rev-2",
            Title = "Second plan",
        };

        var context = PlanReviewActivityResponseParser.BuildProtocolContext([first, second]);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("planId=PLAN-1"));
            Assert.That(context, Does.Contain("planId=PLAN-2"));
            Assert.That(context, Does.Contain("ask which plan or task"));
            Assert.That(context, Does.Contain("Do not modify plan state or guess"));
        });
    }

    [Test]
    public void ProtocolContext_IncludesDurableManualCorrectionForLaterContinuation()
    {
        var plan = PlanStoreUpdater.ApplyManualReviewActivity(
            MakeInterruptedPlan(), ["TASK-1"], "Corrected footer placement.");

        var context = PlanReviewActivityResponseParser.BuildProtocolContext([plan]);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("manualCorrectionCount=1"));
            Assert.That(context, Does.Contain("Corrected footer placement."));
        });
    }

    [Test]
    public void RepairInstruction_DoesNotRepeatManualWorkOrAdvancePlan()
    {
        var instruction = PlanReviewActivityResponseParser.BuildRepairInstruction([MakeInterruptedPlan()]);

        Assert.Multiple(() =>
        {
            Assert.That(instruction, Does.Contain("Do not repeat repository work"));
            Assert.That(instruction, Does.Contain("do not continue any plan"));
            Assert.That(instruction, Does.Contain("PLAN_REVIEW_ACTIVITY_JSON"));
        });
    }

    private static Plan MakeInterruptedPlan()
    {
        var now = DateTimeOffset.Parse("2026-08-13T11:00:00Z");
        return new Plan(
            "PLAN-1",
            "rev",
            PlanSource.Manual,
            PlanLifecycleStatus.Interrupted,
            "Plan",
            "feature/plan",
            "Summary",
            [new PlanTask("TASK-1", "Footer", "Work", [], "high", PlanTaskStatus.HumanReviewRequired)],
            [],
            new PlanProgress(0, 1),
            new PlanTimestamps(now),
            new PlanInterruptionData(
                "Human review required",
                PlanRecoveryState.PendingRecovery,
                1,
                InterruptedTaskId: "TASK-1"));
    }
}
