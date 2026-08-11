namespace SquadDash.Tests;

[TestFixture]
internal sealed class QueueStartupHydrationTests
{
    [TestCase(-1, 3, null)]
    [TestCase(3, 3, null)]
    [TestCase(0, 0, null)]
    [TestCase(0, 3, 0)]
    [TestCase(2, 3, 2)]
    public void ActiveTabRestore_NormalizesSavedIndex(
        int savedIndex,
        int itemCount,
        int? expected)
    {
        Assert.That(
            QueueRestorePolicy.NormalizeActiveTabIndex(savedIndex, itemCount),
            Is.EqualTo(expected));
    }

    [Test]
    public void ActiveTabRestore_PreservesNullDraftSelection()
    {
        Assert.That(
            QueueRestorePolicy.NormalizeActiveTabIndex(null, 3),
            Is.Null);
    }

    [TestCase(false, false, LoopCloseRecoveryAction.OfferManualResume)]
    [TestCase(false, true, LoopCloseRecoveryAction.PreserveAutomaticResume)]
    [TestCase(true, false, LoopCloseRecoveryAction.UsePlanRecovery)]
    [TestCase(true, true, LoopCloseRecoveryAction.PreserveAutomaticResume)]
    public void CloseRecovery_ChoosesExplicitRuntimeBehavior(
        bool isPlan,
        bool restartPending,
        LoopCloseRecoveryAction expected)
    {
        var execution = new ActiveLoopExecutionState(
            @"D:\repo\.squad\loop.md",
            "scope",
            DecomposeGroupId: isPlan ? "PLAN-1" : null);

        Assert.That(
            LoopCloseRecoveryPolicy.Resolve(execution, restartPending),
            Is.EqualTo(expected));
    }

    [Test]
    public void CloseRecovery_WithNoExecution_DoesNothing()
    {
        Assert.That(
            LoopCloseRecoveryPolicy.Resolve(null, restartPending: false),
            Is.EqualTo(LoopCloseRecoveryAction.None));
    }
}
