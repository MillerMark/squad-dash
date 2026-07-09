namespace SquadDash.Tests;

[TestFixture]
internal sealed class LoopStartupResumePolicyTests
{
    [Test]
    public void Resolve_QueuesLoopBehindRestoredQueue_WhenLoopWasActiveAndQueueHasItems()
    {
        var action = LoopStartupResumePolicy.Resolve(
            loopActiveOnExit: true,
            loopAlreadyQueued: false,
            queueHasReadyItems: true,
            startupShiftHeld: false,
            hasPendingQuickReplies: false);

        Assert.That(action, Is.EqualTo(LoopStartupResumeAction.QueueLoopBehindRestoredQueue));
    }

    [Test]
    public void Resolve_KeepsQueuedLoop_WhenLoopWasAlreadyQueuedToDequeue()
    {
        var action = LoopStartupResumePolicy.Resolve(
            loopActiveOnExit: true,
            loopAlreadyQueued: true,
            queueHasReadyItems: true,
            startupShiftHeld: false,
            hasPendingQuickReplies: false);

        Assert.That(action, Is.EqualTo(LoopStartupResumeAction.KeepQueuedLoop));
    }

    [Test]
    public void Resolve_PausesForShiftBeforeRestoredQueue()
    {
        var action = LoopStartupResumePolicy.Resolve(
            loopActiveOnExit: true,
            loopAlreadyQueued: false,
            queueHasReadyItems: true,
            startupShiftHeld: true,
            hasPendingQuickReplies: false);

        Assert.That(action, Is.EqualTo(LoopStartupResumeAction.PauseForShift));
    }

    [Test]
    public void Resolve_PausesForQuickRepliesBeforeRestoredQueue()
    {
        var action = LoopStartupResumePolicy.Resolve(
            loopActiveOnExit: true,
            loopAlreadyQueued: false,
            queueHasReadyItems: true,
            startupShiftHeld: false,
            hasPendingQuickReplies: true);

        Assert.That(action, Is.EqualTo(LoopStartupResumeAction.PauseForQuickReplies));
    }

    [Test]
    public void Resolve_StartsImmediately_WhenLoopWasActiveAndNothingBlocksResume()
    {
        var action = LoopStartupResumePolicy.Resolve(
            loopActiveOnExit: true,
            loopAlreadyQueued: false,
            queueHasReadyItems: false,
            startupShiftHeld: false,
            hasPendingQuickReplies: false);

        Assert.That(action, Is.EqualTo(LoopStartupResumeAction.StartImmediately));
    }
}
