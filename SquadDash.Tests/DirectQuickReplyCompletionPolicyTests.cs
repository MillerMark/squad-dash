namespace SquadDash.Tests;

[TestFixture]
internal sealed class DirectQuickReplyCompletionPolicyTests {
    [Test]
    public void Decide_RegistersSilentWatchdog_ForDirectQuickReplyWhilePromptRunning() {
        var action = DirectQuickReplyCompletionPolicy.Decide(
            needsDirectQuickReplyFollowUp: true,
            isPromptRunning: true,
            reportPromoted: false);

        Assert.That(action, Is.EqualTo(DirectQuickReplyCompletionAction.RegisterSilentWatchdog));
    }

    [Test]
    public void Decide_EnqueuesFollowUp_ForDirectQuickReplyWhenCoordinatorIsIdle() {
        var action = DirectQuickReplyCompletionPolicy.Decide(
            needsDirectQuickReplyFollowUp: true,
            isPromptRunning: false,
            reportPromoted: false);

        Assert.That(action, Is.EqualTo(DirectQuickReplyCompletionAction.EnqueueCoordinatorFollowUp));
    }

    [Test]
    public void Decide_RegistersSilentWatchdog_ForPromotedNonDirectReportDuringPrompt() {
        var action = DirectQuickReplyCompletionPolicy.Decide(
            needsDirectQuickReplyFollowUp: false,
            isPromptRunning: true,
            reportPromoted: true);

        Assert.That(action, Is.EqualTo(DirectQuickReplyCompletionAction.RegisterSilentWatchdog));
    }

    [Test]
    public void Decide_DoesNotQueueGenericRecovery_WhenHostCollectedPlanPayload() {
        var action = DirectQuickReplyCompletionPolicy.Decide(
            needsDirectQuickReplyFollowUp: true,
            isPromptRunning: true,
            reportPromoted: true,
            hostPayloadHandled: true);

        Assert.That(action, Is.EqualTo(DirectQuickReplyCompletionAction.None));
    }
}
