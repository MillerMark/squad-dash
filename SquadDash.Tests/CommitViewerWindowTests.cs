namespace SquadDash.Tests;

[TestFixture]
internal sealed class CommitViewerWindowTests
{
    [TestCase(1920, 1440)]
    [TestCase(2560, 1600)]
    [TestCase(3840, 1600)]
    [TestCase(800, 720)]
    public void CalculateWindowWidth_UsesThreeQuartersWithSensibleLimits(
        double workingWidth,
        double expected)
    {
        Assert.That(CommitViewerLayout.CalculateWindowWidth(workingWidth), Is.EqualTo(expected));
    }

    [Test]
    public void UncertainCommitToolTip_IsConciseAndDoesNotRepeatCommitDetails()
    {
        var text = CommitViewerLayout.BuildUncertainCommitToolTip(
            "The commit predates the captured task baseline.");

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo(
                "SquadDash included this commit as evidence, but could not confirm that it belongs to this step."));
            Assert.That(text, Does.Not.Contain("Why it is uncertain"));
            Assert.That(text, Does.Not.Contain("predates"));
        });
    }

    [TestCase(PlanRecoveryCommitRelation.Unknown, "Unclear.", true)]
    [TestCase(PlanRecoveryCommitRelation.Task, "This task commit predates the baseline.", false)]
    [TestCase(PlanRecoveryCommitRelation.Task, "This task commit predates baseline.", false)]
    [TestCase(PlanRecoveryCommitRelation.Task, "Confirmed inside the captured range.", false)]
    [TestCase(PlanRecoveryCommitRelation.Unrelated, "Confirmed recovery infrastructure.", true)]
    public void AttributionUncertainty_MarksCommitsNotConfirmedAsPartOfTheStep(
        string relation,
        string explanation,
        bool expected)
    {
        Assert.That(
            CommitViewerLayout.IsCommitAttributionUncertain(relation, explanation),
            Is.EqualTo(expected));
    }

    [Test]
    public void TaskCommitBeforeRecoveryBaseline_UsesPlainLanguageHint()
    {
        var hint = CommitViewerLayout.BuildCommitToolTip(
            false,
            "This is the primary implementation commit; it predates the baseline.");

        Assert.That(hint, Is.EqualTo(
            "This commit appears to implement this step, but an interruption prevented SquadDash " +
            "from automatically confirming the step as complete."));
    }
}
