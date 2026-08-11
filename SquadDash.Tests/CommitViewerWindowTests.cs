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
    public void UncertainCommitToolTip_ExplainsMeaningAndSpecificReason()
    {
        var text = CommitViewerLayout.BuildUncertainCommitToolTip(
            "The commit predates the captured task baseline.");

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("could not confirm that it belongs to this step"));
            Assert.That(text, Does.Contain("Why it is uncertain"));
            Assert.That(text, Does.Contain("predates the captured task baseline"));
        });
    }

    [TestCase(PlanRecoveryCommitRelation.Unknown, "Unclear.", true)]
    [TestCase(PlanRecoveryCommitRelation.Task, "This task commit predates the baseline.", true)]
    [TestCase(PlanRecoveryCommitRelation.Task, "This task commit predates baseline.", true)]
    [TestCase(PlanRecoveryCommitRelation.Task, "Confirmed inside the captured range.", false)]
    [TestCase(PlanRecoveryCommitRelation.Unrelated, "Confirmed recovery infrastructure.", false)]
    public void AttributionUncertainty_IncludesTaskCommitsOutsideRecoveryBaseline(
        string relation,
        string explanation,
        bool expected)
    {
        Assert.That(
            CommitViewerLayout.IsCommitAttributionUncertain(relation, explanation),
            Is.EqualTo(expected));
    }
}
