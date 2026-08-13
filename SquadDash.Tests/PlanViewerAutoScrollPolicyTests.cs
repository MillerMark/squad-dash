using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PlanViewerAutoScrollPolicyTests
{
    [Test]
    public void CalculateHorizontalOffset_TargetRangeAlreadyVisible_PreservesOffset()
    {
        var result = PlanViewerAutoScrollPolicy.CalculateHorizontalOffset(
            currentOffset: 200, viewportWidth: 900,
            visibilityStart: 360, visibilityEnd: 980, extentWidth: 2200);

        Assert.That(result, Is.EqualTo(200));
    }

    [Test]
    public void CalculateHorizontalOffset_FollowingStageOutsideViewport_AlignsItsRightEdge()
    {
        var result = PlanViewerAutoScrollPolicy.CalculateHorizontalOffset(
            currentOffset: 100, viewportWidth: 800,
            visibilityStart: 720, visibilityEnd: 1240, extentWidth: 2200);

        Assert.That(result, Is.EqualTo(440));
    }

    [Test]
    public void CalculateHorizontalOffset_ActiveStageWasPassed_ScrollsBackToTargetRange()
    {
        var result = PlanViewerAutoScrollPolicy.CalculateHorizontalOffset(
            currentOffset: 900, viewportWidth: 800,
            visibilityStart: 500, visibilityEnd: 1160, extentWidth: 2200);

        Assert.That(result, Is.EqualTo(360));
    }

    [Test]
    public void CalculateHorizontalOffset_AllContentFits_DoesNotScroll()
    {
        var result = PlanViewerAutoScrollPolicy.CalculateHorizontalOffset(
            currentOffset: 0, viewportWidth: 1800,
            visibilityStart: 500, visibilityEnd: 1160, extentWidth: 1600);

        Assert.That(result, Is.Zero);
    }

    [Test]
    public void IsInteractionQuiet_RequiresThirtySecondsWithoutInput()
    {
        var now = new DateTime(2026, 8, 13, 12, 0, 30, DateTimeKind.Utc);

        Assert.Multiple(() =>
        {
            Assert.That(PlanViewerAutoScrollPolicy.IsInteractionQuiet(now.AddSeconds(-29), now), Is.False);
            Assert.That(PlanViewerAutoScrollPolicy.IsInteractionQuiet(now.AddSeconds(-30), now), Is.True);
        });
    }
}
