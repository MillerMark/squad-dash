using System.Windows;
using System.Windows.Media;

namespace SquadDash.Tests;

/// <summary>
/// Validates the physical-to-logical DPI coordinate conversion algorithm used by
/// DpiHelper.PhysicalToLogical when positioning the PushToTalkWindow and the callout windows.
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

    // ── Callout window-position arithmetic ──────────────────────────────────────
    //
    // FrmUltimateCallout computes windowLeft/windowTop as:
    //   screenDanglePoint - calloutDanglePoint
    // where screenDanglePoint derives from the logical target center and calloutDanglePoint
    // is in canvas (logical) space.  The result is assigned directly to Window.Left/Top
    // (also logical).  These tests verify that the arithmetic stays in logical units at all
    // DPI settings by simulating the key conversion step: physical PointToScreen → logical.

    [Test]
    public void CalloutWindowPosition_AtHundredPercent_EqualsLogicalPosition()
    {
        // Simulate: element is at physical px (400, 300) with logical size 120×40.
        // At 100 % DPI the element's logical top-left == physical top-left.
        double dpiScale      = 1.0;
        var fromDevice       = new Matrix(1.0 / dpiScale, 0, 0, 1.0 / dpiScale, 0, 0);
        var physTopLeft      = new Point(400, 300);
        var logicalTopLeft   = fromDevice.Transform(physTopLeft);
        double elementWidth  = 120;
        double elementHeight = 40;

        // Logical target centre (what TargetClientPointToScreen returns after the fix)
        var logicalCenter = new Point(logicalTopLeft.X + elementWidth / 2,
                                      logicalTopLeft.Y + elementHeight / 2);

        // calloutDanglePoint is a canvas offset (purely logical), e.g. right edge + spacing
        var calloutDanglePoint = new Point(320 + 5, 20);  // calloutWidth + spacing, midHeight

        double windowLeft = logicalCenter.X - calloutDanglePoint.X;
        double windowTop  = logicalCenter.Y - calloutDanglePoint.Y;

        // At 100 % the result equals the 100 %-DPI expected value
        Assert.That(windowLeft, Is.EqualTo(400 + 60 - calloutDanglePoint.X).Within(0.01));
        Assert.That(windowTop,  Is.EqualTo(300 + 20 - calloutDanglePoint.Y).Within(0.01));
    }

    [Test]
    public void CalloutWindowPosition_AtHundredFiftyPercent_StaysInLogicalUnits()
    {
        // At 150 % DPI the element's physical top-left is 1.5× the logical top-left.
        double dpiScale      = 1.5;
        var fromDevice       = new Matrix(1.0 / dpiScale, 0, 0, 1.0 / dpiScale, 0, 0);
        var physTopLeft      = new Point(600, 450);   // 400×1.5, 300×1.5 in physical pixels
        var logicalTopLeft   = fromDevice.Transform(physTopLeft);
        double elementWidth  = 120;   // logical DIP size (ActualWidth)
        double elementHeight = 40;

        // After the fix, TargetClientPointToScreen converts PointToScreen → logical
        var logicalCenter = new Point(logicalTopLeft.X + elementWidth / 2,
                                      logicalTopLeft.Y + elementHeight / 2);

        var calloutDanglePoint = new Point(325, 20);

        double windowLeft = logicalCenter.X - calloutDanglePoint.X;
        double windowTop  = logicalCenter.Y - calloutDanglePoint.Y;

        // Logical centre should equal the 100 %-DPI expected value (400+60=460, 300+20=320)
        Assert.That(logicalCenter.X, Is.EqualTo(460).Within(0.01),
            "Target centre X must be in logical units, not physical pixels");
        Assert.That(logicalCenter.Y, Is.EqualTo(320).Within(0.01),
            "Target centre Y must be in logical units, not physical pixels");

        // Window position is in logical DIPs — suitable for Window.Left / Window.Top
        Assert.That(windowLeft, Is.EqualTo(460 - calloutDanglePoint.X).Within(0.01));
        Assert.That(windowTop,  Is.EqualTo(320 - calloutDanglePoint.Y).Within(0.01));
    }

    [Test]
    public void LogicalToPhysical_RoundTrip_PreservesCoordinates()
    {
        // Verify that physical → logical → physical is a lossless round-trip.
        double dpiScale    = 1.25;
        var fromDevice     = new Matrix(1.0 / dpiScale, 0, 0, 1.0 / dpiScale, 0, 0);
        var toDevice       = new Matrix(dpiScale, 0, 0, dpiScale, 0, 0);
        var physicalInput  = new Point(750, 500);

        var logical        = fromDevice.Transform(physicalInput);
        var physicalOutput = toDevice.Transform(logical);

        Assert.That(physicalOutput.X, Is.EqualTo(physicalInput.X).Within(0.01));
        Assert.That(physicalOutput.Y, Is.EqualTo(physicalInput.Y).Within(0.01));
    }
}
