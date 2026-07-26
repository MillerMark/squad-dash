namespace SquadDash.Tests;

[TestFixture]
internal sealed class LoopStopResumePolicyTests
{
    [Test]
    public void ExplicitStop_WithRestartPending_DoesNotPreserveOrResume()
    {
        var result = LoopStopResumePolicy.Resolve(
            resumeSuppressed: true,
            restartPending: true,
            interruptedByQueue: false,
            queueHasReadyItems: false,
            loopAlreadyQueued: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.PreserveExecution, Is.False);
            Assert.That(result.PreserveForRestart, Is.False);
            Assert.That(result.ResumeAfterQueue, Is.False);
        });
    }

    [Test]
    public void ExplicitStop_WithQueuedWork_StillDoesNotResumeLoop()
    {
        var result = LoopStopResumePolicy.Resolve(
            resumeSuppressed: true,
            restartPending: false,
            interruptedByQueue: true,
            queueHasReadyItems: true,
            loopAlreadyQueued: true);

        Assert.That(result, Is.EqualTo(new LoopStopResumeDecision(false, false, false)));
    }

    [Test]
    public void QueueInterruption_PreservesExecutionAndResumesAfterDrain()
    {
        var result = LoopStopResumePolicy.Resolve(
            resumeSuppressed: false,
            restartPending: false,
            interruptedByQueue: true,
            queueHasReadyItems: true,
            loopAlreadyQueued: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.PreserveExecution, Is.True);
            Assert.That(result.ResumeAfterQueue, Is.True);
            Assert.That(result.PreserveForRestart, Is.False);
        });
    }

    [Test]
    public void RestartInterruption_PreservesExecutionWithoutQueueResume()
    {
        var result = LoopStopResumePolicy.Resolve(
            resumeSuppressed: false,
            restartPending: true,
            interruptedByQueue: false,
            queueHasReadyItems: false,
            loopAlreadyQueued: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.PreserveExecution, Is.True);
            Assert.That(result.PreserveForRestart, Is.True);
            Assert.That(result.ResumeAfterQueue, Is.False);
        });
    }

    [Test]
    public void TerminalPlanStop_WithRestartPending_DoesNotPreserveOrResume()
    {
        var result = LoopStopResumePolicy.Resolve(
            resumeSuppressed: true,
            restartPending: true,
            interruptedByQueue: false,
            queueHasReadyItems: false,
            loopAlreadyQueued: false);

        Assert.That(result, Is.EqualTo(new LoopStopResumeDecision(false, false, false)));
    }
}
