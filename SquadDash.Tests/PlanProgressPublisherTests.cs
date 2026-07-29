using System;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanProgressPublisherTests
{
    [Test]
    public void TryPublish_PersistenceFails_DoesNotNotifyOrReportSuccess()
    {
        var notified = false;

        var published = PlanProgressPublisher.TryPublish(
            MakePlan(),
            _ => throw new InvalidOperationException("disk unavailable"),
            _ => notified = true,
            out var persistenceError,
            out var notificationError);

        Assert.Multiple(() =>
        {
            Assert.That(published, Is.False);
            Assert.That(notified, Is.False);
            Assert.That(persistenceError, Is.EqualTo("disk unavailable"));
            Assert.That(notificationError, Is.Null);
        });
    }

    [Test]
    public void TryPublish_NotificationFails_PreservesDurableSuccess()
    {
        var persisted = false;

        var published = PlanProgressPublisher.TryPublish(
            MakePlan(),
            _ => persisted = true,
            _ => throw new InvalidOperationException("panel refresh failed"),
            out var persistenceError,
            out var notificationError);

        Assert.Multiple(() =>
        {
            Assert.That(published, Is.True);
            Assert.That(persisted, Is.True);
            Assert.That(persistenceError, Is.Null);
            Assert.That(notificationError, Is.EqualTo("panel refresh failed"));
        });
    }

    [Test]
    public void TryPublish_PersistsBeforeNotification()
    {
        var order = string.Empty;

        var published = PlanProgressPublisher.TryPublish(
            MakePlan(),
            _ => order += "persist ",
            _ => order += "notify",
            out var persistenceError,
            out var notificationError);

        Assert.Multiple(() =>
        {
            Assert.That(published, Is.True);
            Assert.That(order, Is.EqualTo("persist notify"));
            Assert.That(persistenceError, Is.Null);
            Assert.That(notificationError, Is.Null);
        });
    }

    private static Plan MakePlan() => new(
        PlanId: "PLAN-001",
        Revision: "rev1",
        Source: PlanSource.DecomposeDecision,
        LifecycleStatus: PlanLifecycleStatus.Executing,
        Title: "Plan",
        Branch: "feature/plan",
        Summary: "Summary",
        Tasks: [],
        ApprovalGates: [],
        Progress: new PlanProgress(0, 0),
        Timestamps: new PlanTimestamps(DateTimeOffset.UtcNow));
}
