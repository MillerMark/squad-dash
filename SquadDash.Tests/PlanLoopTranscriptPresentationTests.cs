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
            [],
            [],
            new PlanProgress(1, 9, "PLAN-1-002"),
            new PlanTimestamps(DateTimeOffset.UtcNow));

        var text = PlanLoopTranscriptPresentation.BuildExecutingPrompt(
            plan,
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
}
