namespace SquadDash.Tests;

[TestFixture]
internal sealed class QueuedPlanStartTests
{
    [Test]
    public void EncodeDecode_PreservesPlanIdentityAndDisplayName()
    {
        var request = new QueuedPlanStart("plan-42", "Ship queue-safe plan starts");

        var decoded = QueuedPlanStart.TryDecode(request.Encode(), out var restored);

        Assert.That(decoded, Is.True);
        Assert.That(restored?.PlanId, Is.EqualTo(request.PlanId));
        Assert.That(restored?.DisplayName, Is.EqualTo(request.DisplayName));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("SQUADDASH_QUEUED_PLAN_START_V1:not-base64")]
    public void TryDecode_InvalidPayload_IsRejected(string? payload)
    {
        Assert.That(QueuedPlanStart.TryDecode(payload, out _), Is.False);
    }

    [Test]
    public void ShouldQueue_WhenCoordinatorTurnIsActive()
    {
        Assert.That(PlanStartQueuePolicy.ShouldQueue(
            coordinatorTurnActive: true,
            loopActive: false,
            queueDrainActive: false,
            backgroundWorkActive: false,
            directFollowUpPending: false,
            queueDispatchHeld: false,
            queuedItemCount: 0), Is.True);
    }

    [TestCase(false, true, false, false, false, false)]
    [TestCase(false, false, true, false, false, false)]
    [TestCase(false, false, false, true, false, false)]
    [TestCase(false, false, false, false, true, false)]
    [TestCase(false, false, false, false, false, true)]
    public void ShouldQueue_WhenAnotherWorkOwnerOrQueueHoldIsActive(
        bool coordinatorTurnActive,
        bool loopActive,
        bool queueDrainActive,
        bool backgroundWorkActive,
        bool directFollowUpPending,
        bool queueDispatchHeld)
    {
        Assert.That(PlanStartQueuePolicy.ShouldQueue(
            coordinatorTurnActive,
            loopActive,
            queueDrainActive,
            backgroundWorkActive,
            directFollowUpPending,
            queueDispatchHeld,
            queuedItemCount: 0), Is.True);
    }

    [Test]
    public void ShouldQueue_WhenEarlierQueueItemMustRetainOrdering()
    {
        Assert.That(PlanStartQueuePolicy.ShouldQueue(
            coordinatorTurnActive: false,
            loopActive: false,
            queueDrainActive: false,
            backgroundWorkActive: false,
            directFollowUpPending: false,
            queueDispatchHeld: false,
            queuedItemCount: 1), Is.True);
    }

    [Test]
    public void ShouldStartImmediately_WhenCoordinatorAndQueueAreIdle()
    {
        Assert.That(PlanStartQueuePolicy.ShouldQueue(
            coordinatorTurnActive: false,
            loopActive: false,
            queueDrainActive: false,
            backgroundWorkActive: false,
            directFollowUpPending: false,
            queueDispatchHeld: false,
            queuedItemCount: 0), Is.False);
    }
}
