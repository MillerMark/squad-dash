namespace SquadDash.Tests;

[TestFixture]
internal sealed class BackgroundWorkClassifierTests {
    [Test]
    public void IsPlanningWork_ReturnsTrue_ForPlanRevisionPrompt() {
        var result = BackgroundWorkClassifier.IsPlanningWork(
            prompt: "Yes, revise the full plan",
            latestResponse: "Plan saved (64KB, up from 44KB). Here's a summary of every change made.",
            latestIntent: null,
            detailText: null);

        Assert.That(result, Is.True);
    }

    [Test]
    public void BuildCompletionSummary_UsesPlanLanguage_ForPlanningWork() {
        var summary = BackgroundWorkClassifier.BuildCompletionSummary(
            label: "Lyra Morn",
            prompt: "create a comprehensive plan for dark theme support",
            latestResponse: "Plan saved.",
            latestIntent: null,
            detailText: null);

        Assert.That(summary, Is.EqualTo("Lyra Morn finished the plan update."));
    }

    [TestCase("Fix the null reference bug")]
    [TestCase("add a unit test")]
    [TestCase("deploy the app")]
    public void IsPlanningWork_ReturnsFalse_ForNonPlanningPrompts(string prompt) {
        var result = BackgroundWorkClassifier.IsPlanningWork(
            prompt: prompt,
            latestResponse: null,
            latestIntent: null,
            detailText: null);

        Assert.That(result, Is.False);
    }

    [Test]
    public void BuildAnnouncementHeader_PlanningWork_NotContinuation_SharesPlanUpdate() {
        var header = BackgroundWorkClassifier.BuildAnnouncementHeader(
            label: "Lyra Morn",
            isContinuation: false,
            prompt: "create a comprehensive plan for dark theme support",
            latestResponse: "Plan saved.",
            latestIntent: null,
            detailText: null);

        Assert.That(header, Is.EqualTo("Lyra Morn shared a plan update:"));
    }

    [Test]
    public void BuildAnnouncementHeader_PlanningWork_IsContinuation_AddsMorePlanDetail() {
        var header = BackgroundWorkClassifier.BuildAnnouncementHeader(
            label: "Lyra Morn",
            isContinuation: true,
            prompt: "revise the full plan",
            latestResponse: "Plan saved.",
            latestIntent: null,
            detailText: null);

        Assert.That(header, Is.EqualTo("Lyra Morn added more plan detail:"));
    }

    [Test]
    public void BuildAnnouncementHeader_NonPlanningWork_NotContinuation_ReportedBack() {
        var header = BackgroundWorkClassifier.BuildAnnouncementHeader(
            label: "Lyra Morn",
            isContinuation: false,
            prompt: "Fix the null reference bug",
            latestResponse: null,
            latestIntent: null,
            detailText: null);

        Assert.That(header, Is.EqualTo("Lyra Morn reported back:"));
    }

    [Test]
    public void BuildAnnouncementHeader_NonPlanningWork_IsContinuation_AddsMoreDetail() {
        var header = BackgroundWorkClassifier.BuildAnnouncementHeader(
            label: "Lyra Morn",
            isContinuation: true,
            prompt: "deploy the app",
            latestResponse: null,
            latestIntent: null,
            detailText: null);

        Assert.That(header, Is.EqualTo("Lyra Morn added more detail:"));
    }
}
