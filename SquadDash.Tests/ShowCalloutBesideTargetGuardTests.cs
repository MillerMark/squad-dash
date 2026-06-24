using System.Windows;

namespace SquadDash.Tests;

/// <summary>
/// Verifies the early-out guard in <see cref="FrmUltimateCallout.ShowCalloutBesideTarget"/>:
/// returns <c>null</c> when the target is invisible or has zero rendered size.
/// Tests use a plain unrendered <see cref="FrameworkElement"/> — no window is shown.
/// </summary>
[TestFixture]
internal sealed class ShowCalloutBesideTargetGuardTests
{
    [Test]
    public void ShowCalloutBesideTarget_TargetNotVisible_ReturnsNull()
    {
        FrmUltimateCallout? result = null;
        WpfTestContext.Run(() =>
        {
            // A FrameworkElement that has never been added to a visual tree has
            // IsVisible == false, ActualWidth == 0, ActualHeight == 0.
            var target = new FrameworkElement();
            result = FrmUltimateCallout.ShowCalloutBesideTarget("hello", target);
        });
        Assert.That(result, Is.Null, "Should return null when target.IsVisible is false");
    }

    [Test]
    public void ShowCalloutBesideTarget_TargetHasZeroActualWidth_ReturnsNull()
    {
        FrmUltimateCallout? result = null;
        WpfTestContext.Run(() =>
        {
            var target = new FrameworkElement();
            // ActualWidth is 0 for an unrendered element — guard should fire.
            Assert.That(target.ActualWidth, Is.LessThanOrEqualTo(0),
                        "Pre-condition: unrendered element has ActualWidth <= 0");
            result = FrmUltimateCallout.ShowCalloutBesideTarget("hello", target);
        });
        Assert.That(result, Is.Null, "Should return null when target.ActualWidth <= 0");
    }

    [Test]
    public void ShowCalloutBesideTarget_TargetHasZeroActualHeight_ReturnsNull()
    {
        FrmUltimateCallout? result = null;
        WpfTestContext.Run(() =>
        {
            var target = new FrameworkElement();
            // ActualHeight is 0 for an unrendered element — guard should fire.
            Assert.That(target.ActualHeight, Is.LessThanOrEqualTo(0),
                        "Pre-condition: unrendered element has ActualHeight <= 0");
            result = FrmUltimateCallout.ShowCalloutBesideTarget("hello", target);
        });
        Assert.That(result, Is.Null, "Should return null when target.ActualHeight <= 0");
    }
}
