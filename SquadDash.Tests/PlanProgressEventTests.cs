using NUnit.Framework;
using System.Collections.Generic;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanProgressEventTests
{
    private static Plan MakePlan(string planId, string status, int completed, int total)
    {
        var progress   = new PlanProgress(completed, total, null);
        var timestamps = new PlanTimestamps(System.DateTimeOffset.UtcNow);
        return new Plan(
            PlanId:          planId,
            Revision:        "rev1",
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: status,
            Title:           "Test Plan",
            Branch:          "feature/test",
            Summary:         "A test plan",
            Tasks:           [],
            ApprovalGates:   [],
            Progress:        progress,
            Timestamps:      timestamps);
    }

    [Test]
    public void PlanProgressEvent_StoresPlanIdAndPlan()
    {
        var plan = MakePlan("PLANS-001", PlanLifecycleStatus.Executing, 2, 5);
        var evt  = new PlanProgressEvent("PLANS-001", plan);

        Assert.That(evt.PlanId, Is.EqualTo("PLANS-001"));
        Assert.That(evt.UpdatedPlan, Is.SameAs(plan));
    }

    [Test]
    public void PlanProgressEvent_PlanIdMatchesUpdatedPlan()
    {
        var plan = MakePlan("PLANS-002", PlanLifecycleStatus.Blocked, 1, 3);
        var evt  = new PlanProgressEvent(plan.PlanId, plan);

        Assert.That(evt.PlanId, Is.EqualTo(evt.UpdatedPlan.PlanId));
    }

    [Test]
    public void PlanProgressEvent_EqualityByValue()
    {
        var plan = MakePlan("PLANS-003", PlanLifecycleStatus.Executing, 0, 4);
        var evt1 = new PlanProgressEvent("PLANS-003", plan);
        var evt2 = new PlanProgressEvent("PLANS-003", plan);

        Assert.That(evt1, Is.EqualTo(evt2));
    }

    [Test]
    public void PlanProgressEvent_InequalityWhenPlanDiffers()
    {
        var planA = MakePlan("PLANS-004", PlanLifecycleStatus.Executing,  1, 4);
        var planB = MakePlan("PLANS-004", PlanLifecycleStatus.Completed, 4, 4);
        var evt1  = new PlanProgressEvent("PLANS-004", planA);
        var evt2  = new PlanProgressEvent("PLANS-004", planB);

        Assert.That(evt1, Is.Not.EqualTo(evt2));
    }

    [Test]
    public void PlanProgressEvent_CarriesFullProgress()
    {
        var plan = MakePlan("PLANS-005", PlanLifecycleStatus.Executing, 3, 7);
        var evt  = new PlanProgressEvent("PLANS-005", plan);

        Assert.That(evt.UpdatedPlan.Progress.CompletedCount, Is.EqualTo(3));
        Assert.That(evt.UpdatedPlan.Progress.TotalCount,     Is.EqualTo(7));
    }
}
