namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanPauseTransitionCoordinatorTests
{
    [Test]
    public void TryApply_PersistenceFailureLeavesRuntimeAndPresentationUnchanged()
    {
        var current = BuildPlan(isPaused: false);
        var refreshCount = 0;

        var success = PlanPauseTransitionCoordinator.TryApply(
            current,
            pause: true,
            _ => (Success: false, Error: "disk unavailable"),
            _ => refreshCount++,
            out var persistedPlan,
            out var error);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(persistedPlan, Is.SameAs(current));
            Assert.That(persistedPlan.IsPaused, Is.False);
            Assert.That(refreshCount, Is.Zero);
            Assert.That(error, Is.EqualTo("disk unavailable"));
        });
    }

    [Test]
    public void TryApply_RefreshesPresentationOnlyAfterPersistenceSucceeds()
    {
        var current = BuildPlan(isPaused: false);
        var events = new List<string>();
        Plan? refreshed = null;

        var success = PlanPauseTransitionCoordinator.TryApply(
            current,
            pause: true,
            candidate =>
            {
                events.Add("persist");
                Assert.That(candidate.IsPaused, Is.True);
                return (Success: true, Error: null);
            },
            candidate =>
            {
                events.Add("refresh");
                refreshed = candidate;
            },
            out var persistedPlan,
            out var error);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(events, Is.EqualTo(new[] { "persist", "refresh" }));
            Assert.That(persistedPlan.IsPaused, Is.True);
            Assert.That(refreshed, Is.SameAs(persistedPlan));
            Assert.That(error, Is.Null);
        });
    }

    [Test]
    public void TryApply_NoOpStillSynchronizesPresentationWithoutWriting()
    {
        var current = BuildPlan(isPaused: true);
        var persistCount = 0;
        var refreshCount = 0;

        var success = PlanPauseTransitionCoordinator.TryApply(
            current,
            pause: true,
            _ =>
            {
                persistCount++;
                return (Success: true, Error: null);
            },
            _ => refreshCount++,
            out var persistedPlan,
            out var error);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(persistedPlan, Is.SameAs(current));
            Assert.That(persistCount, Is.Zero);
            Assert.That(refreshCount, Is.EqualTo(1));
            Assert.That(error, Is.Null);
        });
    }

    private static Plan BuildPlan(bool isPaused)
    {
        var task = new PlanTask(
            "PLAN-1-001",
            "Implement",
            "Implement",
            [],
            "high",
            PlanTaskStatus.Executing,
            DisplayStepLabel: "1");
        return new Plan(
            "PLAN-1",
            "revision-1",
            PlanSource.Inbox,
            PlanLifecycleStatus.Executing,
            "Plan",
            "feature/plan",
            "Summary",
            [task],
            [],
            new PlanProgress(0, 1, task.TaskId),
            new PlanTimestamps(DateTimeOffset.UtcNow),
            IsPaused: isPaused);
    }
}
