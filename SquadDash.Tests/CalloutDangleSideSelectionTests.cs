using System.Windows;

namespace SquadDash.Tests;

/// <summary>
/// Verifies <see cref="FrmUltimateCallout.SelectCalloutDangleSide"/> chooses the correct
/// callout edge after the b0d2d85 fix that switched from window-wide InnerWindow* segments
/// to bounded Callout* segments.  The critical property: a test-line that crosses the callout
/// near a corner should choose the side whose bounded segment is actually intersected, not the
/// adjacent side whose segment has been trimmed back to the callout rectangle.
/// </summary>
[TestFixture]
internal sealed class CalloutDangleSideSelectionTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    static CalloutSide Select(
        MyLine testLine, Point target,
        double left, double top, double right, double bottom) =>
        FrmUltimateCallout.SelectCalloutDangleSide(
            testLine, target,
            calloutTop:    MyLine.Horizontal(left, right, top),
            calloutLeft:   MyLine.Vertical(left, top, bottom),
            calloutRight:  MyLine.Vertical(right, top, bottom),
            calloutBottom: MyLine.Horizontal(left, right, bottom));

    // ── tests ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Left-edge boundary (~113° scenario from bug report).
    /// Callout near the left screen edge (calloutLeft ≈ 2 px from left, calloutTop ≈ 30 px
    /// from top).  The test-line from the target enters the callout through the Top edge at
    /// x ≈ 148.  At x = calloutLeft the line's y-coordinate is far above calloutTop, so the
    /// bounded Left segment returns no intersection.  Expected: Top, not Left.
    /// </summary>
    [Test]
    public void SelectCalloutDangleSide_LeftEdgeBoundary_ReturnsTop()
    {
        // Target is below-right; test-line heads upper-left into the callout top edge.
        // At x=2 (calloutLeft) the line hits y ≈ -183 — outside [30,230] → Left NaN → MaxValue.
        var target   = new Point(200, 100);
        var testLine = new MyLine(target, new Point(-10, -200)); // long upper-left ray
        CalloutSide result = Select(testLine, target, left: 2, top: 30, right: 297, bottom: 230);
        Assert.That(result, Is.EqualTo(CalloutSide.Top),
            "Near the left screen edge the bounded Left segment should not win over Top.");
    }

    /// <summary>
    /// Stable Top selection: test-line goes straight down into the callout.
    /// Only Top and Bottom edges are ever intersected (Left/Right are parallel).
    /// Top is nearest → Top wins.
    /// </summary>
    [Test]
    public void SelectCalloutDangleSide_ClearTop_ReturnsTop()
    {
        var target   = new Point(150, 0);                                // directly above callout
        var testLine = new MyLine(target, new Point(150, 300));          // straight down
        CalloutSide result = Select(testLine, target, left: 50, top: 50, right: 250, bottom: 200);
        Assert.That(result, Is.EqualTo(CalloutSide.Top));
    }

    /// <summary>
    /// Stable Left selection: test-line goes straight right into the callout.
    /// Left edge (distance 50) is closer than Right edge (distance 250).
    /// </summary>
    [Test]
    public void SelectCalloutDangleSide_ClearLeft_ReturnsLeft()
    {
        var target   = new Point(0, 100);                                // directly to the left
        var testLine = new MyLine(target, new Point(400, 100));          // straight right
        CalloutSide result = Select(testLine, target, left: 50, top: 50, right: 250, bottom: 200);
        Assert.That(result, Is.EqualTo(CalloutSide.Left));
    }

    /// <summary>
    /// Symmetric right-edge boundary (~67° scenario).
    /// Callout near the right screen edge; test-line from the target enters through the Top
    /// edge at x ≈ 209.  At x = calloutRight the line's y-coordinate is above calloutTop,
    /// so the bounded Right segment returns no intersection.  Expected: Top, not Right.
    /// </summary>
    [Test]
    public void SelectCalloutDangleSide_RightEdgeBoundary_ReturnsTop()
    {
        // Target is below-left; test-line heads upper-right into the callout top edge.
        // At x=298 (calloutRight) the line hits y ≈ -34 — outside [30,180] → Right NaN → MaxValue.
        var target   = new Point(100, 100);
        var testLine = new MyLine(target, new Point(300, -100));         // upper-right ray
        CalloutSide result = Select(testLine, target, left: 5, top: 30, right: 298, bottom: 180);
        Assert.That(result, Is.EqualTo(CalloutSide.Top),
            "Near the right screen edge the bounded Right segment should not win over Top.");
    }
}
