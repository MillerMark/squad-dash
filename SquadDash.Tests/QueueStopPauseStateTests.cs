namespace SquadDash.Tests;

[TestFixture]
internal sealed class QueueStopPauseStateTests
{
    [Test]
    public void StopPause_ResumesWhenCorrectiveResponseIsDispatched()
    {
        var state = new QueueStopPauseState();

        state.RecordStopPause(queueWasAlreadyPaused: false);

        Assert.That(state.ConsumeResponseResume(queueIsPausedOrPending: true), Is.True);
        Assert.That(state.IsAwaitingResponse, Is.False);
    }

    [Test]
    public void ExistingUserPause_IsNotReleasedByCorrectiveResponse()
    {
        var state = new QueueStopPauseState();

        state.RecordStopPause(queueWasAlreadyPaused: true);

        Assert.That(state.ConsumeResponseResume(queueIsPausedOrPending: true), Is.False);
    }

    [Test]
    public void ExplicitResume_ClearsPendingStopPauseOwnership()
    {
        var state = new QueueStopPauseState();
        state.RecordStopPause(queueWasAlreadyPaused: false);

        state.RecordQueueResumed();

        Assert.That(state.ConsumeResponseResume(queueIsPausedOrPending: true), Is.False);
    }

    [Test]
    public void RepeatedStop_DoesNotLoseExistingStopPauseOwnership()
    {
        var state = new QueueStopPauseState();
        state.RecordStopPause(queueWasAlreadyPaused: false);

        state.RecordStopPause(queueWasAlreadyPaused: true);

        Assert.That(state.ConsumeResponseResume(queueIsPausedOrPending: true), Is.True);
    }
}
