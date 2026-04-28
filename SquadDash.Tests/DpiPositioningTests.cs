using System.Windows;
using System.Windows.Media;

namespace SquadDash.Tests;

/// <summary>
/// Validates the physical-to-logical DPI coordinate conversion algorithm used by
/// DpiHelper.PhysicalToLogical when positioning the PushToTalkWindow.
///
/// DpiHelper.PhysicalToLogical itself cannot be exercised directly in headless tests
/// because it calls PresentationSource.FromVisual, which requires a live WPF visual
/// tree connected to a real HwndSource.  These tests instead verify the underlying
/// matrix transform maths — the same computation that
/// CompositionTarget.TransformFromDevice.Transform performs at runtime.
/// </summary>
[TestFixture]
internal sealed class DpiPositioningTests
{
    [Test]
    public void PhysicalToLogical_AtHundredPercent_IsIdentity()
    {
        var matrix = new Matrix(1, 0, 0, 1, 0, 0); // 100 % DPI — scale factor 1.0
        var physical = new Point(500, 800);
        var logical = matrix.Transform(physical);
        Assert.That(logical.X, Is.EqualTo(500).Within(0.01));
        Assert.That(logical.Y, Is.EqualTo(800).Within(0.01));
    }

    [Test]
    public void PhysicalToLogical_AtHundredFiftyPercent_ScalesDown()
    {
        var scale = 1.0 / 1.5; // 150 % DPI — physical px 900 → logical px 600
        var matrix = new Matrix(scale, 0, 0, scale, 0, 0);
        var physical = new Point(900, 600);
        var logical = matrix.Transform(physical);
        Assert.That(logical.X, Is.EqualTo(600).Within(0.01));
        Assert.That(logical.Y, Is.EqualTo(400).Within(0.01));
    }

    [Test]
    public void PhysicalToLogical_AtTwoHundredPercent_ScalesDown()
    {
        var scale = 1.0 / 2.0; // 200 % DPI — physical px 1200 → logical px 600
        var matrix = new Matrix(scale, 0, 0, scale, 0, 0);
        var physical = new Point(1200, 800);
        var logical = matrix.Transform(physical);
        Assert.That(logical.X, Is.EqualTo(600).Within(0.01));
        Assert.That(logical.Y, Is.EqualTo(400).Within(0.01));
    }
}
