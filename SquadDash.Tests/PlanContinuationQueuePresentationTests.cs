namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanContinuationQueuePresentationTests
{
    [Test]
    public void Build_LabelsTheStepAfterTheCurrentlyExecutingStep()
    {
        var display = PlanContinuationQueuePresentation.Build(BuildPlan(completed: 1, total: 8));

        Assert.Multiple(() =>
        {
            Assert.That(display, Is.Not.Null);
            Assert.That(display!.StepNumber, Is.EqualTo(3));
            Assert.That(display.Label, Is.EqualTo("Plan Step 3: Task 3"));
            Assert.That(display.Description, Does.Contain("locked continuation"));
            Assert.That(display.Description, Does.Contain("Plan: Plan"));
            Assert.That(display.Description, Does.Contain("Next task: Task 3"));
            Assert.That(display.Description, Does.Contain("Release:"));
            Assert.That(display.Description, Does.Contain("cannot be edited or sent manually"));
        });
    }

    [Test]
    public void Build_ReturnsNullWhenNoLaterStepExists()
    {
        Assert.That(PlanContinuationQueuePresentation.Build(BuildPlan(completed: 7, total: 8)), Is.Null);
    }

    private static Plan BuildPlan(int completed, int total)
    {
        var tasks = Enumerable.Range(1, total)
            .Select(index => new PlanTask(
                $"P-{index}", $"Task {index}", "Description", [], "normal",
                index <= completed ? PlanTaskStatus.Complete :
                index == completed + 1 ? PlanTaskStatus.Executing : PlanTaskStatus.Pending))
            .ToArray();
        return new Plan(
            "P", "revision", PlanSource.DecomposeDecision, PlanLifecycleStatus.Executing,
            "Plan", "feature/plan", "Summary", tasks, [],
            new PlanProgress(completed, total, tasks.ElementAtOrDefault(completed)?.TaskId),
            new PlanTimestamps(DateTimeOffset.UtcNow));
    }
}
