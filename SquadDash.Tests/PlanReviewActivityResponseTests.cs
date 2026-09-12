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
    public void ActivityPolicy_UsesReviewCorrectionGateWhenCorrectionPromptRuns()
    {
        var correction = new PendingPlanReviewCorrectionContext(
            "queue-1", "PLAN-1", "GATE-REVIEW", "TASK-1");

        var target = PlanReviewCorrectionActivityPolicy.Resolve(null, correction);

        Assert.That(target, Is.EqualTo(("PLAN-1", "GATE-REVIEW")));
    }

    [Test]
    public void ActivityPolicy_PrefersExplicitGateResponseContext()
    {
        var correction = new PendingPlanReviewCorrectionContext(
            "queue-1", "PLAN-1", "GATE-REVIEW", "TASK-1");

        var target = PlanReviewCorrectionActivityPolicy.Resolve(
            ("PLAN-1", "GATE-RESPONSE"), correction);

        Assert.That(target, Is.EqualTo(("PLAN-1", "GATE-RESPONSE")));
    }

    [Test]
    public void ActivityPolicy_ClaimsReviewCorrectionWhileEditablePromptIsDrafted()
    {
        var correction = new PendingPlanReviewCorrectionContext(
            "queue-1", "PLAN-1", "GATE-REVIEW", "TASK-1");

        var target = PlanReviewCorrectionActivityPolicy.ResolveActive(
            null, correction, isPromptRunning: false);

        Assert.That(target, Is.EqualTo(("PLAN-1", "GATE-REVIEW")));
    }

    [Test]
    public void ActivityPolicy_ClaimsOrdinaryGateResponseOnlyWhilePromptRuns()
    {
        var gateResponse = (PlanId: "PLAN-1", GateId: "GATE-RESPONSE");

        Assert.Multiple(() =>
        {
            Assert.That(
                PlanReviewCorrectionActivityPolicy.ResolveActive(
                    gateResponse, null, isPromptRunning: false),
                Is.Null);
            Assert.That(
                PlanReviewCorrectionActivityPolicy.ResolveActive(
                    gateResponse, null, isPromptRunning: true),
                Is.EqualTo(gateResponse));
        });
    }

    [TestCase(false, PlanTaskCorrectionActivityState.Drafting)]
    [TestCase(true, PlanTaskCorrectionActivityState.Running)]
    public void ReviewCorrectionTaskActivity_DrivesReviewedTaskSpinner(
        bool isPromptRunning,
        PlanTaskCorrectionActivityState expected)
    {
        var correction = new PendingPlanReviewCorrectionContext(
            "queue-1", "PLAN-1", "GATE-REVIEW", "TASK-1");

        var state = PlanReviewCorrectionTaskActivityPolicy.Resolve(
            correction,
            "PLAN-1",
            "TASK-1",
            isPromptRunning,
            dispatchedItemId: "queue-1",
            dispatchedSourceTag: "plan-review-correction-draft");

        Assert.That(state, Is.EqualTo(expected));
    }

    [Test]
    public void TaskCorrectionActivity_StartsWhenEditableQueueItemIsDequeued()
    {
        var correction = new PendingPlanTaskCorrectionContext(
            "queue-1", "PLAN-1", "TASK-1");

        var state = PlanTaskCorrectionActivityPolicy.Resolve(
            correction,
            "PLAN-1",
            "TASK-1",
            isPromptRunning: true,
            dispatchedItemId: "queue-1",
            dispatchedSourceTag: "plan-recovery-correction-draft");

        Assert.That(state, Is.EqualTo(PlanTaskCorrectionActivityState.Running));
    }

    [Test]
    public void ReviewCorrectionTaskActivity_RemainsRunningDuringUnifiedMetadataRepair()
    {
        var correction = new PendingPlanReviewCorrectionContext(
            "queue-1", "PLAN-1", "GATE-REVIEW", "TASK-1");

        var state = PlanReviewCorrectionTaskActivityPolicy.Resolve(
            correction,
            "PLAN-1",
            "TASK-1",
            isPromptRunning: true,
            dispatchedItemId: "repair-queue-item",
            dispatchedSourceTag: "plan-review-response-repair");

        Assert.That(state, Is.EqualTo(PlanTaskCorrectionActivityState.Running));
    }

    [TestCase(false, "queue-1", "plan-recovery-correction-draft", PlanTaskCorrectionActivityState.Drafting)]
    [TestCase(true, "different", "ordinary-prompt", PlanTaskCorrectionActivityState.Drafting)]
    [TestCase(true, "queue-1", "plan-recovery-correction-draft", PlanTaskCorrectionActivityState.Running)]
    public void TaskCorrectionActivity_DoesNotWaitForWorkerDelegation(
        bool isPromptRunning,
        string dispatchedItemId,
        string dispatchedSourceTag,
        PlanTaskCorrectionActivityState expected)
    {
        var correction = new PendingPlanTaskCorrectionContext(
            "queue-1", "PLAN-1", "TASK-1");

        Assert.That(
            PlanTaskCorrectionActivityPolicy.Resolve(
                correction,
                "PLAN-1",
                "TASK-1",
                isPromptRunning,
                dispatchedItemId,
                dispatchedSourceTag),
            Is.EqualTo(expected));
    }

    [Test]
    public void TaskCorrectionActivity_IsolatedFromOtherPlansAndTasks()
    {
        var correction = new PendingPlanTaskCorrectionContext(
            "queue-1", "PLAN-1", "TASK-1");

        Assert.Multiple(() =>
        {
            Assert.That(PlanTaskCorrectionActivityPolicy.Resolve(
                correction, "PLAN-2", "TASK-1", true, "queue-1", "plan-recovery-correction-draft"),
                Is.EqualTo(PlanTaskCorrectionActivityState.None));
            Assert.That(PlanTaskCorrectionActivityPolicy.Resolve(
                correction, "PLAN-1", "TASK-2", true, "queue-1", "plan-recovery-correction-draft"),
                Is.EqualTo(PlanTaskCorrectionActivityState.None));
        });
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
            Assert.That(context, Does.Contain("continue-working"));
            Assert.That(context, Does.Not.Contain("assess-and-continue"));
            Assert.That(context, Does.Contain("Do not reopen the task"));
        });
    }

    [Test]
    public void ProtocolContext_InterruptedRecordedWork_AllowsBoundedCorrection()
    {
        var plan = MakeInterruptedPlan() with
        {
            Tasks =
            [
                new PlanTask("TASK-1", "Footer", "Work", [], "high", PlanTaskStatus.Executing),
            ],
            InterruptionData = MakeInterruptedPlan().InterruptionData! with
            {
                TaskCommitEvidence = new PlanTaskCommitEvidence(
                    "TASK-1", "attempt-1", "aaaaaaa", "bbbbbbb", "Implemented footer."),
            },
        };

        var context = PlanReviewActivityResponseParser.BuildProtocolContext([plan]);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("mode=interrupted-completed-work-review"));
            Assert.That(context, Does.Contain("manual-correction"));
            Assert.That(PlanReviewActivityResponseParser.IsActiveTarget(plan, "TASK-1"), Is.True);
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
    public void StaleEvidenceAcceptanceIsRejected()
    {
        const string reviewedCommit = "ddc913";
        var plan = MakeInterruptedReviewPlan(reviewedCommit, reviewedCommit);
        var attention = PlanReviewAttentionPresentationResolver.Resolve(plan, "TASK-1");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PlanHumanReviewStepAcceptance.ApplyTasks(
                plan,
                [attention!],
                currentHead: "f8f3dd"));

        Assert.Multiple(() =>
        {
            Assert.That(attention, Is.Not.Null);
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Message, Does.Contain("stale"));
            Assert.That(plan.Tasks.Single(task => task.TaskId == "TASK-1").Status,
                Is.EqualTo(PlanTaskStatus.HumanReviewRequired));
            Assert.That(plan.InterruptionData, Is.Not.Null);
        });
    }

    [Test]
    public void StaleEvidenceAcceptance_AllMatchingCommitsCompletesNormally()
    {
        const string reviewedCommit = "ddc913";
        var plan = MakeInterruptedReviewPlan(reviewedCommit, reviewedCommit);
        var attention = PlanReviewAttentionPresentationResolver.Resolve(plan, "TASK-1");

        var updated = PlanHumanReviewStepAcceptance.ApplyTasks(
            plan,
            [attention!],
            currentHead: reviewedCommit);

        Assert.Multiple(() =>
        {
            Assert.That(attention, Is.Not.Null);
            Assert.That(updated.Tasks.Single(task => task.TaskId == "TASK-1").Status,
                Is.EqualTo(PlanTaskStatus.Complete));
            Assert.That(updated.InterruptionData, Is.Null);
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

    private static Plan MakeInterruptedReviewPlan(string handoffCommit, string evidenceCommit)
    {
        var verification = new DecomposeStepVerification("passed", "dotnet test", "93/93 tests passed");
        var task = new PlanTask(
            "TASK-1",
            "Footer",
            "Work",
            [],
            "high",
            PlanTaskStatus.HumanReviewRequired,
            Handoff: new PlanTaskHandoff(
                handoffCommit,
                "Implemented footer behavior.",
                ["MainWindow.xaml.cs"],
                verification,
                DateTimeOffset.Parse("2026-08-13T10:58:00Z")));
        var gate = new PlanApprovalGate(
            "GATE-1",
            "Review footer behavior",
            [task.TaskId],
            [],
            PlanGateStatus.AwaitingApproval,
            ProofRequirements:
            [
                new PlanTaskProofRequirement(
                    "proof-1",
                    "live-ui-observation",
                    "Observe footer behavior.",
                    "Does the footer render correctly?"),
            ]);
        var now = DateTimeOffset.Parse("2026-08-13T11:00:00Z");
        return new Plan(
            "PLAN-1",
            "rev",
            PlanSource.Manual,
            PlanLifecycleStatus.Interrupted,
            "Plan",
            "feature/plan",
            "Summary",
            [task],
            [gate],
            new PlanProgress(0, 1, "TASK-1"),
            new PlanTimestamps(now),
            new PlanInterruptionData(
                "Human review required",
                PlanRecoveryState.PendingRecovery,
                1,
                InterruptedTaskId: "TASK-1",
                TaskCommitEvidence: new PlanTaskCommitEvidence(
                    "TASK-1",
                    "attempt-1",
                    "baseline-1",
                    evidenceCommit,
                    "Implemented footer behavior.",
                    verification)));
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
