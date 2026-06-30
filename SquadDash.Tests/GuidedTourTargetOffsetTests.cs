namespace SquadDash.Tests;

/// <summary>
/// Tests for the offset coordinate mapping between the 0–1 user space
/// (stored in GuidedTourStep) and the -1..+1 percent-offset space used
/// by FrmUltimateCallout.
/// </summary>
[TestFixture]
internal sealed class GuidedTourTargetOffsetTests
{
    // Mapping formula: percentOffset = (userOffset - 0.5) * 2

    [TestCase(0.5,  0.0)]   // center  → no shift
    [TestCase(0.0, -1.0)]   // left/top edge → -1
    [TestCase(1.0,  1.0)]   // right/bottom edge → +1
    [TestCase(0.75, 0.5)]   // three-quarters → +0.5
    [TestCase(0.25,-0.5)]   // one-quarter → -0.5
    public void TargetOffsetToPercentOffset_MapsCorrectly(double userOffset, double expected)
    {
        double percentOffset = (userOffset - 0.5) * 2;
        Assert.That(percentOffset, Is.EqualTo(expected).Within(1e-10));
    }

    [TestCase(0.0,  0.5)]   // percentOffset 0 → userOffset 0.5
    [TestCase(1.0,  1.0)]   // percentOffset +1 → userOffset 1.0
    [TestCase(-1.0, 0.0)]   // percentOffset -1 → userOffset 0.0
    [TestCase(0.5,  0.75)]  // percentOffset +0.5 → userOffset 0.75
    [TestCase(-0.5, 0.25)]  // percentOffset -0.5 → userOffset 0.25
    public void PercentOffsetToTargetOffset_InverseFormula_IsCorrect(double percentOffset, double expected)
    {
        double userOffset = percentOffset / 2.0 + 0.5;
        Assert.That(userOffset, Is.EqualTo(expected).Within(1e-10));
    }

    [TestCase(0.5)]
    [TestCase(0.0)]
    [TestCase(1.0)]
    [TestCase(0.123)]
    [TestCase(0.987)]
    public void RoundTrip_UserToPercentAndBack_ReturnsOriginalValue(double originalUserOffset)
    {
        double percentOffset = (originalUserOffset - 0.5) * 2;
        double restored      = percentOffset / 2.0 + 0.5;
        Assert.That(restored, Is.EqualTo(originalUserOffset).Within(1e-10));
    }

    [Test]
    public void DefaultOffset_MapsToZeroPercentOffset()
    {
        const double defaultOffset = 0.5;
        double hPct = (defaultOffset - 0.5) * 2;
        double vPct = (defaultOffset - 0.5) * 2;
        Assert.Multiple(() =>
        {
            Assert.That(hPct, Is.EqualTo(0.0).Within(1e-10));
            Assert.That(vPct, Is.EqualTo(0.0).Within(1e-10));
        });
    }
}
