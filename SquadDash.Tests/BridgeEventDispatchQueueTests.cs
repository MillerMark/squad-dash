namespace SquadDash.Tests;

[TestFixture]
internal sealed class BridgeEventDispatchQueueTests {
    [Test]
    public void RejectedDispatch_ReleasesClaimSoPendingEventCanBeRescheduled() {
        var queue = new BridgeEventDispatchQueue();
        var evt = new SquadSdkEvent { Type = "tool_complete" };

        Assert.That(queue.EnqueueAndTryClaimDispatch(evt), Is.True);

        queue.ReleaseDispatchClaim();

        Assert.That(queue.TryClaimDispatchForPendingEvents(), Is.True);
        Assert.That(queue.TryDequeue(out var dequeued), Is.True);
        Assert.That(dequeued, Is.SameAs(evt));
        Assert.That(queue.CompleteDrainPass(out var remaining), Is.False);
        Assert.That(remaining, Is.Zero);
    }

    [Test]
    public void EventArrivingDuringClaim_IsRetainedForContinuationPass() {
        var queue = new BridgeEventDispatchQueue();
        var first = new SquadSdkEvent { Type = "response_delta" };
        var second = new SquadSdkEvent { Type = "done" };

        Assert.That(queue.EnqueueAndTryClaimDispatch(first), Is.True);
        Assert.That(queue.EnqueueAndTryClaimDispatch(second), Is.False);
        Assert.That(queue.TryDequeue(out _), Is.True);

        Assert.That(queue.CompleteDrainPass(out var remaining), Is.True);
        Assert.That(remaining, Is.EqualTo(1));
        Assert.That(queue.TryDequeue(out var dequeued), Is.True);
        Assert.That(dequeued, Is.SameAs(second));
        Assert.That(queue.CompleteDrainPass(out remaining), Is.False);
        Assert.That(remaining, Is.Zero);
    }

    [Test]
    public void EmptyDrain_ReleasesClaimForNextBridgeEvent() {
        var queue = new BridgeEventDispatchQueue();

        Assert.That(queue.EnqueueAndTryClaimDispatch(new SquadSdkEvent { Type = "done" }), Is.True);
        Assert.That(queue.TryDequeue(out _), Is.True);
        Assert.That(queue.CompleteDrainPass(out _), Is.False);

        Assert.That(queue.EnqueueAndTryClaimDispatch(new SquadSdkEvent { Type = "tool_start" }), Is.True);
    }
}
