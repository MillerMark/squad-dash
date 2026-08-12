namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanLoopTranscriptPresentationTests
{
    [Test]
    public void BuildPhasePrompt_Executing_IsPlanFirstAndLinksVisualizerBeforeLoopDetails()
    {
        var plan = new Plan(
            "PLAN-1",
            "revision-1",
            PlanSource.Inbox,
            PlanLifecycleStatus.Executing,
            "Plan title",
            "feature/plan",
            "Summary",
            [
                new PlanTask("PLAN-1-001", "First task", "First", [], "high", PlanTaskStatus.Complete,
                    DisplayStepLabel: "1"),
                new PlanTask("PLAN-1-002", "Second task", "Second", ["PLAN-1-001"], "high",
                    PlanTaskStatus.Executing, DisplayStepLabel: "2"),
            ],
            [],
            new PlanProgress(1, 9, "PLAN-1-002"),
            new PlanTimestamps(DateTimeOffset.UtcNow));

        var text = PlanLoopTranscriptPresentation.BuildPhasePrompt(
            plan,
            "PLAN-1-002",
            "Second task",
            "D:/repo/.squad/loop-executing-plan.md");

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("Executing plan · Step 2 of 9 · Second task"));
            Assert.That(text, Does.Contain("[View Plan](app://open-plan:PLAN-1)"));
            Assert.That(text.IndexOf("View Plan", StringComparison.Ordinal),
                Is.LessThan(text.IndexOf("Loop details", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void BuildPhasePrompt_UsesActualNonOrdinalDisplayLabelInsteadOfCalculatedStep()
    {
        var task = new PlanTask(
            "PLAN-1-AMD-001", "Amended cleanup", "Cleanup", [], "high", PlanTaskStatus.Executing,
            DisplayStepLabel: "4A");
        var plan = new Plan(
            "PLAN-1", "revision-1", PlanSource.Inbox, PlanLifecycleStatus.Executing,
            "Plan title", "feature/plan", "Summary", [task], [],
            new PlanProgress(4, 8, task.TaskId), new PlanTimestamps(DateTimeOffset.UtcNow));

        var text = PlanLoopTranscriptPresentation.BuildPhasePrompt(
            plan, task.TaskId, task.Title, "D:/repo/.squad/loop-executing-plan.md");

        Assert.That(text, Does.StartWith(
            "Executing plan · 4 of 8 complete (Step \"4A\") · Amended cleanup"));
        Assert.That(text, Does.Not.Contain("Step 5 of 8"));
    }

    [Test]
    public void BuildVerifyingCompletedWorkMessage_UsesDurableDisplayLabel()
    {
        var task = new PlanTask(
            "PLAN-1-AMD-001", "Amended cleanup", "Cleanup", [], "high", PlanTaskStatus.Verifying,
            DisplayStepLabel: "4A");
        var plan = new Plan(
            "PLAN-1", "revision-1", PlanSource.Inbox, PlanLifecycleStatus.Executing,
            "Plan title", "feature/plan", "Summary", [task], [],
            new PlanProgress(4, 8), new PlanTimestamps(DateTimeOffset.UtcNow));

        var text = PlanLoopTranscriptPresentation.BuildVerifyingCompletedWorkMessage(plan, task.TaskId, task.Title);

        Assert.That(text, Is.EqualTo(
            "Reviewing the completed Step 4A. No code changes will occur during this review."));
    }

    [TestCase(PlanTranscriptPhase.VerifyingWork, "Verifying work · Step 2 of 2 · Second task")]
    [TestCase(PlanTranscriptPhase.ReworkingTask, "Reworking task · Step 2 of 2 · Second task")]
    public void BuildPhasePrompt_UsesExplicitTaskPhase(PlanTranscriptPhase phase, string expected)
    {
        var task = new PlanTask("PLAN-1-002", "Second task", "Second", [], "high",
            phase == PlanTranscriptPhase.VerifyingWork ? PlanTaskStatus.Verifying : PlanTaskStatus.Reworking,
            DisplayStepLabel: "2");
        var plan = new Plan(
            "PLAN-1", "revision-1", PlanSource.Inbox, PlanLifecycleStatus.Executing,
            "Plan title", "feature/plan", "Summary", [task], [],
            new PlanProgress(1, 2, task.TaskId), new PlanTimestamps(DateTimeOffset.UtcNow));

        var text = PlanLoopTranscriptPresentation.BuildPhasePrompt(
            plan, task.TaskId, task.Title, "D:/repo/.squad/loop-executing-plan.md", phase);

        Assert.That(text, Does.StartWith(expected));
    }

    [Test]
    public void BuildPhasePrompt_ValidationUsesValidationTitleWithoutTaskProgress()
    {
        var plan = new Plan(
            "PLAN-1", "revision-1", PlanSource.Inbox, PlanLifecycleStatus.Executing,
            "Plan title", "feature/plan", "Summary", [], [],
            new PlanProgress(2, 2), new PlanTimestamps(DateTimeOffset.UtcNow));

        var text = PlanLoopTranscriptPresentation.BuildPhasePrompt(
            plan, null, "Live behavior verified", "D:/repo/.squad/loop-executing-plan.md",
            PlanTranscriptPhase.ValidatingPlan);

        Assert.That(text, Does.StartWith("Validating plan · Live behavior verified"));
        Assert.That(text, Does.Not.Contain("complete ·"));
    }

    [Test]
    public void PhaseHeading_IsEmittedOnlyWhenPhaseOrWorkItemChanges()
    {
        var first = PlanLoopTranscriptPresentation.BuildPhaseKey(
            "PLAN-1", PlanTranscriptPhase.VerifyingWork, "PLAN-1-002");
        var same = PlanLoopTranscriptPresentation.BuildPhaseKey(
            "PLAN-1", PlanTranscriptPhase.VerifyingWork, "PLAN-1-002");
        var repair = PlanLoopTranscriptPresentation.BuildPhaseKey(
            "PLAN-1", PlanTranscriptPhase.ReworkingTask, "PLAN-1-002");

        Assert.Multiple(() =>
        {
            Assert.That(PlanLoopTranscriptPresentation.ShouldEmitPhaseHeading(null, first), Is.True);
            Assert.That(PlanLoopTranscriptPresentation.ShouldEmitPhaseHeading(first, same), Is.False);
            Assert.That(PlanLoopTranscriptPresentation.ShouldEmitPhaseHeading(first, repair), Is.True);
        });
    }

    [Test]
    public void LoopBookkeeping_IsHiddenForPlanLoopsButRetainedForOrdinaryLoops()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PlanLoopTranscriptPresentation.ShouldShowLoopBookkeeping(isPlanLoop: true), Is.False);
            Assert.That(PlanLoopTranscriptPresentation.ShouldShowLoopBookkeeping(isPlanLoop: false), Is.True);
        });
    }

    [Test]
    public void BuildPlanCompleteMessage_UsesPlanProgressWithoutLoopTerminology()
    {
        var plan = new Plan(
            "PLAN-1", "revision-1", PlanSource.Inbox, PlanLifecycleStatus.Completed,
            "Plan title", "feature/plan", "Summary", [], [],
            new PlanProgress(8, 8), new PlanTimestamps(DateTimeOffset.UtcNow));

        Assert.That(
            PlanLoopTranscriptPresentation.BuildPlanCompleteMessage(plan),
            Is.EqualTo("Plan complete · 8 of 8"));
    }
}
