using System.Collections.Generic;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanViewerLiveSyncTests
{
    private static Plan MakePlan(string planId, int completedCount, int totalCount = 5) => new(
        PlanId:          planId,
        Revision:        "rev1",
        Source:          PlanSource.DecomposeDecision,
        LifecycleStatus: PlanLifecycleStatus.Executing,
        Title:           "Test Plan",
        Branch:          "main",
        Summary:         "A test plan",
        Tasks:           [],
        ApprovalGates:   [],
        Progress:        new PlanProgress(CompletedCount: completedCount, TotalCount: totalCount),
        Timestamps:      new PlanTimestamps(CreatedAt: System.DateTimeOffset.UtcNow));

    [Test]
    public void ProgressOrdering_HigherCompletedCount_UpdatesPlan()
    {
        var broker = new WeakEventBroker();
        var initial = MakePlan("PLAN-001", completedCount: 1);
        Plan? received = null;

        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-001", initial, broker,
            plan => received = plan);

        var updated = MakePlan("PLAN-001", completedCount: 3);
        handler.HandleEventDirect(new PlanProgressEvent("PLAN-001", updated));

        Assert.That(received, Is.SameAs(updated));
        Assert.That(handler.CurrentPlan, Is.SameAs(updated));
        Assert.That(handler.AppliedCount, Is.EqualTo(1));

        handler.Detach();
    }

    [Test]
    public void StaleEventRejection_LowerCompletedCount_IsIgnored()
    {
        var broker = new WeakEventBroker();
        var initial = MakePlan("PLAN-001", completedCount: 3);
        Plan? received = null;

        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-001", initial, broker,
            plan => received = plan);

        var stale = MakePlan("PLAN-001", completedCount: 1);
        handler.HandleEventDirect(new PlanProgressEvent("PLAN-001", stale));

        Assert.That(received, Is.Null);
        Assert.That(handler.CurrentPlan, Is.SameAs(initial));
        Assert.That(handler.RejectedCount, Is.EqualTo(1));

        handler.Detach();
    }

    [Test]
    public void PlanIdFiltering_DifferentPlanId_IsIgnored()
    {
        var broker = new WeakEventBroker();
        var initial = MakePlan("PLAN-001", completedCount: 1);
        Plan? received = null;

        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-001", initial, broker,
            plan => received = plan);

        var otherPlan = MakePlan("PLAN-002", completedCount: 5);
        handler.HandleEventDirect(new PlanProgressEvent("PLAN-002", otherPlan));

        Assert.That(received, Is.Null);
        Assert.That(handler.AppliedCount, Is.EqualTo(0));

        handler.Detach();
    }

    [Test]
    public void SubscriptionDetachment_AfterDetach_EventsNoLongerReach()
    {
        var broker = new WeakEventBroker();
        var initial = MakePlan("PLAN-001", completedCount: 0);
        var updates = new List<Plan>();

        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-001", initial, broker,
            plan => updates.Add(plan));

        // First event gets through
        var evt1 = MakePlan("PLAN-001", completedCount: 1);
        broker.Publish(new PlanProgressEvent("PLAN-001", evt1));
        Assert.That(updates, Has.Count.EqualTo(1));

        // Detach simulates window close
        handler.Detach();

        // Second event should not get through
        var evt2 = MakePlan("PLAN-001", completedCount: 2);
        broker.Publish(new PlanProgressEvent("PLAN-001", evt2));
        Assert.That(updates, Has.Count.EqualTo(1),
            "No further events should be received after detach.");
    }

    [Test]
    public void RepeatedRefresh_MultipleSamePlanEvents_DoNotDuplicateState()
    {
        var broker = new WeakEventBroker();
        var initial = MakePlan("PLAN-001", completedCount: 0);
        var updates = new List<Plan>();

        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-001", initial, broker,
            plan => updates.Add(plan));

        // Send three sequential events with increasing progress
        handler.HandleEventDirect(new PlanProgressEvent("PLAN-001", MakePlan("PLAN-001", completedCount: 1)));
        handler.HandleEventDirect(new PlanProgressEvent("PLAN-001", MakePlan("PLAN-001", completedCount: 2)));
        handler.HandleEventDirect(new PlanProgressEvent("PLAN-001", MakePlan("PLAN-001", completedCount: 3)));

        Assert.That(updates, Has.Count.EqualTo(3));
        Assert.That(handler.CurrentPlan!.Progress.CompletedCount, Is.EqualTo(3));
        Assert.That(handler.AppliedCount, Is.EqualTo(3));

        handler.Detach();
    }

    [Test]
    public void EventCoalescence_RapidEvents_WithoutDispatcher_AllApplyDirectly()
    {
        // Without a dispatcher (null), coalescence is bypassed — events apply immediately.
        // This tests that without dispatcher, events still work correctly.
        var broker = new WeakEventBroker();
        var initial = MakePlan("PLAN-001", completedCount: 0);
        var updates = new List<Plan>();

        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-001", initial, broker,
            plan => updates.Add(plan),
            dispatcher: null);

        // Rapid events without dispatcher all apply
        handler.HandleEventDirect(new PlanProgressEvent("PLAN-001", MakePlan("PLAN-001", completedCount: 1)));
        handler.HandleEventDirect(new PlanProgressEvent("PLAN-001", MakePlan("PLAN-001", completedCount: 2)));
        handler.HandleEventDirect(new PlanProgressEvent("PLAN-001", MakePlan("PLAN-001", completedCount: 3)));

        Assert.That(handler.CurrentPlan!.Progress.CompletedCount, Is.EqualTo(3));

        handler.Detach();
    }

    [Test]
    public void EqualCompletedCount_IsNotStale_Applies()
    {
        var broker = new WeakEventBroker();
        var initial = MakePlan("PLAN-001", completedCount: 2);
        Plan? received = null;

        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-001", initial, broker,
            plan => received = plan);

        // Same completed count but different lifecycle (e.g., awaiting approval)
        var updated = MakePlan("PLAN-001", completedCount: 2) with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
        };
        handler.HandleEventDirect(new PlanProgressEvent("PLAN-001", updated));

        Assert.That(received, Is.SameAs(updated),
            "Events with equal CompletedCount should apply (lifecycle may have changed).");

        handler.Detach();
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void ValidationCompletion_WithEqualTaskCount_AppliesImmediatelyWithoutDispatcherTick()
    {
        var broker = new WeakEventBroker();
        var validation = new PlanValidationNode(
            "PLAN-001-VAL-001", "Live behavior", "Observe the running UI.",
            [], [], ["The open viewer refreshes."], null, "evidence", null, true,
            Status: PlanValidationStatus.Validating);
        var initial = MakePlan("PLAN-001", completedCount: 5) with { Validations = [validation] };
        Plan? received = null;
        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-001", initial, broker,
            plan => received = plan,
            System.Windows.Threading.Dispatcher.CurrentDispatcher);

        var updated = initial with
        {
            Validations = [validation with { Status = PlanValidationStatus.Passed }],
        };
        handler.HandleEventDirect(new PlanProgressEvent("PLAN-001", updated));

        Assert.That(received, Is.SameAs(updated),
            "A blue-to-green validation transition must not wait for the coalescing timer or a restart.");
        handler.Detach();
    }

    [Test]
    public void BrokerSubscription_PublishRoutesToHandler()
    {
        var broker = new WeakEventBroker();
        var initial = MakePlan("PLAN-001", completedCount: 0);
        Plan? received = null;

        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-001", initial, broker,
            plan => received = plan);

        var updated = MakePlan("PLAN-001", completedCount: 2);
        broker.Publish(new PlanProgressEvent("PLAN-001", updated));

        Assert.That(received, Is.SameAs(updated),
            "Publishing via broker should route to the handler.");

        handler.Detach();
    }
}
