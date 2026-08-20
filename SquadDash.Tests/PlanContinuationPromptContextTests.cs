namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanContinuationPromptContextTests
{
    [Test]
    public void ExplicitResumeWithPlanId_RequiresExactRecoveryPayloadWithoutExecutingInstructions()
    {
        var plan = MakePlan();

        var context = PlanContinuationPromptContext.Build(
            "Resume CLIDETECT-20260814 after bringing the current work into its branch.",
            [plan]);

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("Host-owned plan continuation required"));
            Assert.That(context, Does.Contain(DecomposeRecoveryDecisionParser.Marker));
            Assert.That(context, Does.Contain("\"groupId\":\"CLIDETECT-20260814\""));
            Assert.That(context, Does.Contain("\"revision\":\"revision-5\""));
            Assert.That(context, Does.Contain("\"action\":\"continue-working\""));
            Assert.That(context, Does.Contain("Do not independently switch or merge branches"));
        });
    }

    [Test]
    public void ExplicitResumeWithUniqueTitle_RequiresRecoveryPayload()
    {
        var context = PlanContinuationPromptContext.Build(
            "Please continue CLI Detection and Selection.",
            [MakePlan()]);

        Assert.That(context, Does.Contain("CLIDETECT-20260814")
            .And.Contain("continue-working"));
    }

    [Test]
    public void MentionWithoutContinuationIntent_DoesNotChangeThePrompt()
    {
        var context = PlanContinuationPromptContext.Build(
            "Explain the status of CLIDETECT-20260814.",
            [MakePlan()]);

        Assert.That(context, Is.Empty);
    }

    private static Plan MakePlan() => new(
        "CLIDETECT-20260814",
        "revision-5",
        PlanSource.DecomposeDecision,
        PlanLifecycleStatus.WaitingForCleanWorktree,
        "CLI Detection and Selection",
        "feature/cli-detection-and-selection",
        "Resolve and select the CLI.",
        [new PlanTask("CLIDETECT-20260814-005", "Migrate callers", "Migrate callers", [], "high", PlanTaskStatus.Pending)],
        [],
        new PlanProgress(4, 7),
        new PlanTimestamps(DateTimeOffset.UtcNow));
}
