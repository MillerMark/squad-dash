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
}
