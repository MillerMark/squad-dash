namespace SquadDash.Tests;

[TestFixture]
internal sealed class BackgroundAgentReportAnnouncementBuilderTests {
    [Test]
    public void TryBuild_ReturnsNull_WhileAgentIsStillLive() {
        var announcement = BackgroundAgentReportAnnouncementBuilder.TryBuild(
            title: "Wanda",
            agentId: "wanda-review-3",
            prompt: null,
            latestResponse: "Review complete.",
            lastAnnouncedResponse: null,
            wasObservedAsBackgroundTask: true,
            isLiveBackgroundTask: true,
            isTerminal: false);

        Assert.That(announcement, Is.Null);
    }

    [Test]
    public void TryBuild_ReturnsInitialReport_WhenAgentIsNoLongerLive() {
        var announcement = BackgroundAgentReportAnnouncementBuilder.TryBuild(
            title: "Wanda",
            agentId: "wanda-review-3",
            prompt: null,
            latestResponse: "Review complete.\nTwo findings.",
            lastAnnouncedResponse: null,
            wasObservedAsBackgroundTask: true,
            isLiveBackgroundTask: false,
            isTerminal: false);

        Assert.That(announcement, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(announcement!.Header, Is.EqualTo("Wanda (wanda-review-3) reported back:"));
            Assert.That(announcement.Body, Is.EqualTo("Review complete.\nTwo findings."));
            Assert.That(announcement.FullResponse, Is.EqualTo("Review complete.\nTwo findings."));
        });
    }

    [Test]
    public void TryBuild_ReturnsOnlyNewTail_WhenResponseExtended() {
        var announcement = BackgroundAgentReportAnnouncementBuilder.TryBuild(
            title: "Wanda",
            agentId: "wanda-review-3",
            prompt: null,
            latestResponse: "Review complete.\nTwo findings.\nOne more note.",
            lastAnnouncedResponse: "Review complete.\nTwo findings.",
            wasObservedAsBackgroundTask: true,
            isLiveBackgroundTask: false,
            isTerminal: false);

        Assert.That(announcement, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(announcement!.Header, Is.EqualTo("Wanda (wanda-review-3) added more detail:"));
            Assert.That(announcement.Body, Is.EqualTo("One more note."));
            Assert.That(announcement.FullResponse, Is.EqualTo("Review complete.\nTwo findings.\nOne more note."));
        });
    }

    [Test]
    public void TryBuild_ReturnsNull_WhenResponseAlreadyAnnounced() {
        var announcement = BackgroundAgentReportAnnouncementBuilder.TryBuild(
            title: "Wanda",
            agentId: "wanda-review-3",
            prompt: null,
            latestResponse: "Review complete.",
            lastAnnouncedResponse: "Review complete.",
            wasObservedAsBackgroundTask: true,
            isLiveBackgroundTask: false,
            isTerminal: true);

        Assert.That(announcement, Is.Null);
    }

    [Test]
    public void TryBuild_DoesNotDuplicateAgentId_WhenTitleAlreadyContainsIt() {
        var announcement = BackgroundAgentReportAnnouncementBuilder.TryBuild(
            title: "Review options page changes (wanda-review-3)",
            agentId: "wanda-review-3",
            prompt: null,
            latestResponse: "Review complete.",
            lastAnnouncedResponse: null,
            wasObservedAsBackgroundTask: true,
            isLiveBackgroundTask: false,
            isTerminal: true);

        Assert.That(announcement, Is.Not.Null);
        Assert.That(
            announcement!.Header,
            Is.EqualTo("Review options page changes (wanda-review-3) reported back:"));
    }

    [Test]
    public void TryBuild_UsesPlanUpdateHeader_WhenPromptIsPlanningWork() {
        var announcement = BackgroundAgentReportAnnouncementBuilder.TryBuild(
            title: "Lyra Morn",
            agentId: "lyra-theme-plan-revise",
            prompt: "Yes, revise the full plan",
            latestResponse: "Plan saved (64KB, up from 44KB). Here's a summary.",
            lastAnnouncedResponse: null,
            wasObservedAsBackgroundTask: true,
            isLiveBackgroundTask: false,
            isTerminal: true);

        Assert.That(announcement, Is.Not.Null);
        Assert.That(
            announcement!.Header,
            Is.EqualTo("Lyra Morn (lyra-theme-plan-revise) shared a plan update:"));
    }
}
