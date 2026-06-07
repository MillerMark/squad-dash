namespace SquadDash.Tests;

[TestFixture]
internal sealed class RestartDeferralPolicyTests {
    [Test]
    public void GetDeferralReason_BlocksRestart_WhenBackgroundWorkIsActive() {
        var reason = RestartDeferralPolicy.GetDeferralReason(
            isPromptRunning: false,
            isLoopRunning: false,
            hasBackgroundWork: true,
            hasPendingDirectQuickReplyHandoff: false,
            isVoiceInputActiveOrDraining: false,
            hasDocRevisionInFlight: false);

        Assert.That(reason, Is.EqualTo(RestartDeferralReason.BackgroundWork));
    }

    [Test]
    public void GetDeferralReason_AllowsRestart_WhenPromptAppearsStalledAndBackgroundWorkIsActive() {
        var reason = RestartDeferralPolicy.GetDeferralReason(
            isPromptRunning: true,
            isLoopRunning: false,
            hasBackgroundWork: true,
            hasPendingDirectQuickReplyHandoff: false,
            isVoiceInputActiveOrDraining: false,
            hasDocRevisionInFlight: false,
            promptAppearsStalled: true);

        Assert.That(reason, Is.EqualTo(RestartDeferralReason.None));
    }

    [Test]
    public void GetDeferralReason_BlocksRestart_WhenDirectQuickReplyHandoffIsPending() {
        var reason = RestartDeferralPolicy.GetDeferralReason(
            isPromptRunning: false,
            isLoopRunning: false,
            hasBackgroundWork: false,
            hasPendingDirectQuickReplyHandoff: true,
            isVoiceInputActiveOrDraining: false,
            hasDocRevisionInFlight: false);

        Assert.That(reason, Is.EqualTo(RestartDeferralReason.DirectQuickReplyHandoff));
    }

    [Test]
    public void GetDeferralReason_AllowsRestart_WhenNoBlockersRemain() {
        var reason = RestartDeferralPolicy.GetDeferralReason(
            isPromptRunning: false,
            isLoopRunning: false,
            hasBackgroundWork: false,
            hasPendingDirectQuickReplyHandoff: false,
            isVoiceInputActiveOrDraining: false,
            hasDocRevisionInFlight: false);

        Assert.That(reason, Is.EqualTo(RestartDeferralReason.None));
    }

    [Test]
    public void GetDeferralReason_AllowsRestart_EvenWhenClipboardEditorWouldHaveBlocked() {
        // Clipboard editor open no longer blocks restart — state is persisted async instead.
        var reason = RestartDeferralPolicy.GetDeferralReason(
            isPromptRunning: false,
            isLoopRunning: false,
            hasBackgroundWork: false,
            hasPendingDirectQuickReplyHandoff: false,
            isVoiceInputActiveOrDraining: false,
            hasDocRevisionInFlight: false);

        Assert.That(reason, Is.EqualTo(RestartDeferralReason.None));
    }
}
