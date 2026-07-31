namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanInterruptionPersistenceTests
{
    [Test]
    public void Apply_PersistenceFailureDoesNotClaimInterruptedPlanWasSaved()
    {
        var result = PlanInterruptionPersistence.Apply(
            PlanFactory.CreateExecuting("PLAN-1", "PLAN-1-002"),
            "PLAN-1",
            "PLAN-1-001",
            "loop failed",
            7,
            "abc1234",
            preferDurableTaskId: false,
            persist: _ => (false, "disk full"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanInterruptionPersistenceOutcome.Failed));
            Assert.That(result.Plan, Is.Null);
            Assert.That(result.Error, Is.EqualTo("disk full"));
        });
    }

    [Test]
    public void Apply_UsesDurableExecutingTaskAheadOfStaleRoundIdentity()
    {
        Plan? persisted = null;
        var result = PlanInterruptionPersistence.Apply(
            PlanFactory.CreateExecuting("PLAN-1", "PLAN-1-002"),
            "PLAN-1",
            "PLAN-1-001",
            "user stopped",
            7,
            "abc1234",
            preferDurableTaskId: true,
            persist: plan =>
            {
                persisted = plan;
                return (true, null);
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanInterruptionPersistenceOutcome.Persisted));
            Assert.That(persisted?.InterruptionData?.InterruptedTaskId, Is.EqualTo("PLAN-1-002"));
            Assert.That(result.Plan, Is.SameAs(persisted));
        });
    }

    [Test]
    public void Apply_NonExecutingPlanDoesNotRewriteOrPersist()
    {
        var persistCalled = false;
        var completed = PlanFactory.CreateExecuting("PLAN-1", null) with
        {
            LifecycleStatus = PlanLifecycleStatus.Completed
        };

        var result = PlanInterruptionPersistence.Apply(
            completed,
            "PLAN-1",
            null,
            "late callback",
            7,
            null,
            preferDurableTaskId: false,
            persist: _ =>
            {
                persistCalled = true;
                return (true, null);
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanInterruptionPersistenceOutcome.NotNeeded));
            Assert.That(persistCalled, Is.False);
        });
    }

    [Test]
    public void Apply_ErrorKeepsCapturedRoundTaskAheadOfAdvancedDurableProgress()
    {
        Plan? persisted = null;
        var result = PlanInterruptionPersistence.Apply(
            PlanFactory.CreateExecuting("PLAN-1", "PLAN-1-002"),
            "PLAN-1",
            "PLAN-1-001",
            "round failed",
            7,
            "abc1234",
            preferDurableTaskId: false,
            persist: plan =>
            {
                persisted = plan;
                return (true, null);
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(PlanInterruptionPersistenceOutcome.Persisted));
            Assert.That(persisted?.InterruptionData?.InterruptedTaskId, Is.EqualTo("PLAN-1-001"));
        });
    }

    private static class PlanFactory
    {
        internal static Plan CreateExecuting(string planId, string? executingTaskId)
        {
            var now = DateTimeOffset.UtcNow;
            return new Plan(
                PlanId: planId,
                Revision: "revision-1",
                Source: PlanSource.Manual,
                LifecycleStatus: PlanLifecycleStatus.Executing,
                Title: "Plan",
                Branch: "feature/plan",
                Summary: "Summary",
                Tasks: [],
                ApprovalGates: [],
                Progress: new PlanProgress(0, 2, executingTaskId),
                Timestamps: new PlanTimestamps(now, StartedAt: now));
        }
    }
}
