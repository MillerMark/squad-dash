namespace SquadDash.Tests;

[TestFixture]
internal sealed class LoopStartupResumePolicyTests
{
    [Test]
    public void Resolve_QueuesLoopBehindRestoredQueue_WhenLoopWasActiveAndQueueHasItems()
    {
        var action = LoopStartupResumePolicy.Resolve(
            workspaceExecution: Execution(),
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
            workspaceExecution: Execution(),
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
            workspaceExecution: Execution(),
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
            workspaceExecution: Execution(),
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
            workspaceExecution: Execution(),
            loopAlreadyQueued: false,
            queueHasReadyItems: false,
            startupShiftHeld: false,
            hasPendingQuickReplies: false);

        Assert.That(action, Is.EqualTo(LoopStartupResumeAction.StartImmediately));
    }

    [Test]
    public void Resolve_DoesNotResumeWorkspaceWithoutItsOwnExecutionEnvelope()
    {
        var action = LoopStartupResumePolicy.Resolve(
            workspaceExecution: null,
            loopAlreadyQueued: false,
            queueHasReadyItems: false,
            startupShiftHeld: false,
            hasPendingQuickReplies: false);

        Assert.That(action, Is.EqualTo(LoopStartupResumeAction.None));
    }

    private static ActiveLoopExecutionState Execution() =>
        new("D:/repo/.squad/loop-executing-plan.md", "PLAN-1", "PLAN-1", "revision-1");
}
