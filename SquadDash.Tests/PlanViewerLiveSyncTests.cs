using System.Collections.Generic;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanViewerLiveSyncTests
{
    private static Plan MakePlan(
        string planId,
        int completedCount,
        int totalCount = 5,
        string revision = "rev1",
        int revisionNumber = 1) => new(
        PlanId:          planId,
        Revision:        revision,
        Source:          PlanSource.DecomposeDecision,
        LifecycleStatus: PlanLifecycleStatus.Executing,
        Title:           "Test Plan",
        Branch:          "main",
        Summary:         "A test plan",
        Tasks:           [],
        ApprovalGates:   [],
        Progress:        new PlanProgress(CompletedCount: completedCount, TotalCount: totalCount),
        Timestamps:      new PlanTimestamps(CreatedAt: System.DateTimeOffset.UtcNow),
        RevisionNumber:  revisionNumber);

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
    public void HumanReviewRework_LowerCompletedCountRefreshesCompletedTaskAsExecuting()
    {
        var broker = new WeakEventBroker();
        var completedAt = System.DateTimeOffset.UtcNow.AddMinutes(-2);
        var completedTask = new PlanTask(
            "PLAN-001-TASK-006",
            "Bundle custom grammars",
            "Bundle and register editor-local grammars.",
            [],
            "mid",
            PlanTaskStatus.Complete,
            Commit: "abc1234",
            CompletedAt: completedAt,
            CompletionSummary: "Bundled the initial grammars.");
        var initial = MakePlan("PLAN-001", completedCount: 8, totalCount: 9) with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            Tasks = [completedTask],
        };
        Plan? received = null;
        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-001", initial, broker,
            plan => received = plan);

        var archivedAttempt = new PlanTaskAttempt(
            PlanTaskStatus.Complete,
            completedTask.Commit,
            completedAt,
            completedTask.CompletionSummary,
            "changes-requested",
            "Correct the auto-detect behavior.");
        var reopened = initial with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            Tasks =
            [
                completedTask with
                {
                    Status = PlanTaskStatus.Executing,
                    Commit = null,
                    CompletedAt = null,
                    CompletionSummary = null,
                    AttemptHistory = [archivedAttempt],
                },
            ],
            Progress = new PlanProgress(7, 9, completedTask.TaskId),
        };

        handler.HandleEventDirect(new PlanProgressEvent("PLAN-001", reopened));

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.SameAs(reopened));
            Assert.That(handler.CurrentPlan, Is.SameAs(reopened));
            Assert.That(handler.AppliedCount, Is.EqualTo(1));
            Assert.That(handler.RejectedCount, Is.Zero);
            Assert.That(
                PlanTaskActivityResolver.Resolve(handler.CurrentPlan!)[completedTask.TaskId],
                Is.EqualTo(PlanTaskActivityState.Executing));
        });

        handler.Detach();
    }

    [Test]
    public void LatePreReworkSnapshot_CannotReplaceCompletedReplacementAttempt()
    {
        var broker = new WeakEventBroker();
        var archivedAttempt = new PlanTaskAttempt(
            PlanTaskStatus.Complete,
            "abc1234",
            System.DateTimeOffset.UtcNow.AddMinutes(-4),
            "Initial attempt.",
            "changes-requested",
            "Correct the auto-detect behavior.");
        var replacement = new PlanTask(
            "PLAN-001-TASK-006",
            "Bundle custom grammars",
            "Bundle and register editor-local grammars.",
            [],
            "mid",
            PlanTaskStatus.Complete,
            Commit: "def5678",
            CompletedAt: System.DateTimeOffset.UtcNow,
            CompletionSummary: "Corrected auto-detect.",
            AttemptHistory: [archivedAttempt]);
        var current = MakePlan("PLAN-001", completedCount: 8, totalCount: 9) with
        {
            Tasks = [replacement],
        };
        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-001", current, broker,
            _ => Assert.Fail("A late rework snapshot must not replace the completed replacement attempt."));
        var lateReworkSnapshot = current with
        {
            Tasks = [replacement with { Status = PlanTaskStatus.Executing, Commit = null, CompletedAt = null }],
            Progress = new PlanProgress(7, 9, replacement.TaskId),
        };

        handler.HandleEventDirect(new PlanProgressEvent("PLAN-001", lateReworkSnapshot));

        Assert.Multiple(() =>
        {
            Assert.That(handler.CurrentPlan, Is.SameAs(current));
            Assert.That(handler.AppliedCount, Is.Zero);
            Assert.That(handler.RejectedCount, Is.EqualTo(1));
        });

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
    public void RevisionFiltering_SamePlanIdDifferentRevision_IsRejected()
    {
        var broker = new WeakEventBroker();
        var initial = MakePlan("PLAN-001", completedCount: 1, revision: "old-revision");
        Plan? received = null;
        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-001", initial, broker,
            plan => received = plan);

        var proposedRevision = MakePlan("PLAN-001", completedCount: 0, revision: "new-revision");
        handler.HandleEventDirect(new PlanProgressEvent("PLAN-001", proposedRevision));

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.Null);
            Assert.That(handler.CurrentPlan, Is.SameAs(initial));
            Assert.That(handler.RejectedCount, Is.EqualTo(1));
        });
        handler.Detach();
    }

    [Test]
    public void NewerApprovedRevision_ReopenedStepRefreshesOpenViewerImmediately()
    {
        var broker = new WeakEventBroker();
        var initial = MakePlan(
            "PLAN-001", completedCount: 7, totalCount: 8,
            revision: "old-revision", revisionNumber: 1);
        Plan? received = null;
        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-001", initial, broker,
            plan => received = plan);

        var revised = MakePlan(
            "PLAN-001", completedCount: 6, totalCount: 8,
            revision: "new-revision", revisionNumber: 2) with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
        };
        handler.HandleEventDirect(new PlanProgressEvent("PLAN-001", revised));

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.SameAs(revised));
            Assert.That(handler.CurrentPlan, Is.SameAs(revised));
            Assert.That(handler.AppliedCount, Is.EqualTo(1));
            Assert.That(handler.RejectedCount, Is.Zero);
        });
        handler.Detach();
    }

    [Test]
    public void OlderRevisionEvent_IsRejectedEvenWhenItsCompletionCountIsHigher()
    {
        var broker = new WeakEventBroker();
        var initial = MakePlan(
            "PLAN-001", completedCount: 6, totalCount: 8,
            revision: "new-revision", revisionNumber: 2);
        Plan? received = null;
        var handler = new PlanViewerLiveSyncHandler(
            "PLAN-001", initial, broker,
            plan => received = plan);

        var lateOldRevision = MakePlan(
            "PLAN-001", completedCount: 7, totalCount: 8,
            revision: "old-revision", revisionNumber: 1);
        handler.HandleEventDirect(new PlanProgressEvent("PLAN-001", lateOldRevision));

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.Null);
            Assert.That(handler.CurrentPlan, Is.SameAs(initial));
            Assert.That(handler.RejectedCount, Is.EqualTo(1));
        });
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
