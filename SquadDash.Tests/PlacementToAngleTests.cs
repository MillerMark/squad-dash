namespace SquadDash.Tests;

/// <summary>
/// Locks in the angle values returned by <see cref="FrmUltimateCallout.PlacementToAngle"/>.
/// Angle = direction the callout tail points (toward the target).
/// </summary>
[TestFixture]
internal sealed class PlacementToAngleTests
{
    [TestCase(CalloutPlacement.North,     0.0)]
    [TestCase(CalloutPlacement.NorthEast, 45.0)]
    [TestCase(CalloutPlacement.East,      90.0)]
    [TestCase(CalloutPlacement.SouthEast, 135.0)]
    [TestCase(CalloutPlacement.South,     180.0)]
    [TestCase(CalloutPlacement.SouthWest, 225.0)]
    [TestCase(CalloutPlacement.West,      270.0)]
    [TestCase(CalloutPlacement.NorthWest, 315.0)]
    public void PlacementToAngle_CardinalAndOrdinalPlacements_ReturnExpectedDegrees(
        CalloutPlacement placement, double expectedAngle)
    {
        Assert.That(FrmUltimateCallout.PlacementToAngle(placement),
                    Is.EqualTo(expectedAngle));
    }

    [Test]
    public void PlacementToAngle_Auto_ReturnsDoubleMinValue()
    {
        Assert.That(FrmUltimateCallout.PlacementToAngle(CalloutPlacement.Auto),
                    Is.EqualTo(double.MinValue));
    }
}
