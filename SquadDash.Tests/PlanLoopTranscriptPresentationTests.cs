namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanLoopTranscriptPresentationTests
{
    [Test]
    public void BuildExecutingPrompt_IsPlanFirstAndLinksVisualizerBeforeLoopDetails()
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

        var text = PlanLoopTranscriptPresentation.BuildExecutingPrompt(
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
    public void BuildExecutingPrompt_UsesActualNonOrdinalDisplayLabelInsteadOfCalculatedStep()
    {
        var task = new PlanTask(
            "PLAN-1-AMD-001", "Amended cleanup", "Cleanup", [], "high", PlanTaskStatus.Executing,
            DisplayStepLabel: "4A");
        var plan = new Plan(
            "PLAN-1", "revision-1", PlanSource.Inbox, PlanLifecycleStatus.Executing,
            "Plan title", "feature/plan", "Summary", [task], [],
            new PlanProgress(4, 8, task.TaskId), new PlanTimestamps(DateTimeOffset.UtcNow));

        var text = PlanLoopTranscriptPresentation.BuildExecutingPrompt(
            plan, task.TaskId, task.Title, "D:/repo/.squad/loop-executing-plan.md");

        Assert.That(text, Does.StartWith(
            "Executing plan · 4 of 8 complete (Step \"4A\") · Amended cleanup"));
        Assert.That(text, Does.Not.Contain("Step 5 of 8"));
    }

    [Test]
    public void BuildValidatingMessage_UsesDurableDisplayLabel()
    {
        var task = new PlanTask(
            "PLAN-1-AMD-001", "Amended cleanup", "Cleanup", [], "high", PlanTaskStatus.Scrutinizing,
            DisplayStepLabel: "4A");
        var plan = new Plan(
            "PLAN-1", "revision-1", PlanSource.Inbox, PlanLifecycleStatus.Executing,
            "Plan title", "feature/plan", "Summary", [task], [],
            new PlanProgress(4, 8), new PlanTimestamps(DateTimeOffset.UtcNow));

        var text = PlanLoopTranscriptPresentation.BuildValidatingMessage(plan, task.TaskId, task.Title);

        Assert.That(text, Does.StartWith(
            "Validating completed work for 4 of 8 complete (Step \"4A\")"));
    }
}
