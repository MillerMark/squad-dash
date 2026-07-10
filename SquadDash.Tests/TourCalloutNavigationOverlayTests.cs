using System.Windows;

namespace SquadDash.Tests;

/// <summary>
/// Locks in the placement logic of <see cref="TourCalloutNavigationOverlay.ComputePosition"/>.
///
/// Strategy: <c>ComputePosition</c> is a static helper extracted from <c>PositionNear</c> that
/// takes explicit <c>visibleBounds</c> and <c>screenBounds</c> parameters, so no WPF window
/// needs to be instantiated.  The default fallback visible bounds used here mirror what
/// <c>GetVisibleButtonBounds()</c> returns before a layout pass:
///   PanelMargin=4, PrevButtonWidth=32, ButtonGap=6, NextButtonWidth=58, ButtonHeight=36
///   → visibleBounds = Rect(4, 4, 96, 36)  (Left=4, Top=4, Right=100, Bottom=40)
///
/// Nav-overlay drag exclusion (<c>IsCursorOverDraggableCalloutSurface</c>) is private and
/// exercised only through UI interaction — not easily unit-testable without a running WPF
/// message loop.  That path is flagged here as a known gap.
/// </summary>
[TestFixture]
internal sealed class TourCalloutNavigationOverlayTests
{
    // ── shared fixtures ────────────────────────────────────────────────────────

    // Default visible button bounds (matches pre-layout fallback in GetVisibleButtonBounds).
    static readonly Rect DefaultVisible = new Rect(4, 4, 96, 36);

    // A generous screen that will never clip any candidate in the "primary fits" tests.
    static readonly Rect LargeScreen = new Rect(0, 0, 2560, 1440);

    // A typical mid-screen callout body rect.
    static readonly Rect MidCallout = new Rect(300, 500, 190, 54);
    //   Left=300, Top=500, Right=490, Bottom=554

    // ── helper ────────────────────────────────────────────────────────────────

    static Point Compute(Rect callout, CalloutSide side, Rect? visible = null, Rect? screen = null) =>
        TourCalloutNavigationOverlay.ComputePosition(
            callout,
            side,
            visible ?? DefaultVisible,
            screen ?? LargeScreen);

    // ══════════════════════════════════════════════════════════════════════════
    // 1.  PositionNear — candidate placement by dangle direction
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Dangle=Top: pointer exits the top of the callout → target is above the callout.
    /// Buttons must land below the callout body so they don't overlap the pointer.
    /// Primary candidate is (rightAlignX, belowY).
    ///   belowY = callout.Bottom + 10 − visibleBounds.Top = 554 + 10 − 4 = 560
    /// Result.Y = 560 ≥ callout.Bottom = 554.
    /// </summary>
    [Test]
    public void ComputePosition_DangleTop_OverlayLandsBelow()
    {
        Point result = Compute(MidCallout, CalloutSide.Top);

        Assert.That(result.Y, Is.GreaterThanOrEqualTo(MidCallout.Bottom),
            "Top-dangle: nav overlay window origin Y must be at or below the callout bottom.");
    }

    /// <summary>
    /// Dangle=Bottom: pointer exits the bottom → target is below the callout.
    /// Buttons must land above the callout body.
    /// Primary candidate is (rightAlignX, aboveY).
    ///   aboveY = callout.Top − 10 − visibleBounds.Bottom = 500 − 10 − 40 = 450
    /// result.Y + visibleBounds.Bottom = 450 + 40 = 490 ≤ callout.Top = 500.
    /// </summary>
    [Test]
    public void ComputePosition_DangleBottom_OverlayLandsAbove()
    {
        Point result = Compute(MidCallout, CalloutSide.Bottom);

        double visibleBottom = result.Y + DefaultVisible.Bottom;
        Assert.That(visibleBottom, Is.LessThanOrEqualTo(MidCallout.Top),
            "Bottom-dangle: bottom of visible buttons must be at or above the callout top.");
    }

    /// <summary>
    /// Dangle=Left: pointer exits the left side → primary candidate is below the callout.
    /// Same primary Y as the Top case.
    ///   belowY = 554 + 10 − 4 = 560 ≥ callout.Bottom = 554.
    /// </summary>
    [Test]
    public void ComputePosition_DangleLeft_OverlayLandsBelow()
    {
        Point result = Compute(MidCallout, CalloutSide.Left);

        Assert.That(result.Y, Is.GreaterThanOrEqualTo(MidCallout.Bottom),
            "Left-dangle: nav overlay window origin Y must be at or below the callout bottom.");
    }

    /// <summary>
    /// Dangle=Right: pointer exits the right side → buttons must land to the LEFT.
    /// Primary candidate is (leftSideX, bottomAlignY).
    ///   leftSideX = callout.Left − 10 − visibleBounds.Right = 300 − 10 − 100 = 190
    /// result.X + visibleBounds.Right = 190 + 100 = 290 ≤ callout.Left = 300.
    /// </summary>
    [Test]
    public void ComputePosition_DangleRight_OverlayLandsLeftOfCallout()
    {
        Point result = Compute(MidCallout, CalloutSide.Right);

        double visibleRight = result.X + DefaultVisible.Right;
        Assert.That(visibleRight, Is.LessThanOrEqualTo(MidCallout.Left),
            "Right-dangle: right edge of visible buttons must be at or left of the callout left edge.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2.  Fallback when primary candidate is off-screen
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Callout is near the bottom of the screen; dangle=Top makes the primary "below"
    /// candidates fall outside the screen boundary, so the algorithm falls back to a
    /// right-side placement that fits.
    ///
    /// Callout: Rect(300, 1400, 190, 38) → Bottom=1438 on a 2560×1440 screen.
    ///   belowY = 1438 + 10 − 4 = 1444  → visible bottom = 1484 > 1440 ❌ off-screen
    ///   rightSideX = 490 + 10 − 4 = 496, bottomAlignY = 1438 − 40 = 1398
    ///   → visible rect right = 596, bottom = 1438 ≤ 1440 ✓ chosen
    ///
    /// Assert: the chosen placement is to the right of the callout, not below it.
    /// </summary>
    [Test]
    public void ComputePosition_DangleTop_PrimaryOffScreenBottom_FallsBackToRightSide()
    {
        var callout = new Rect(300, 1400, 190, 38);  // Bottom=1438, near screen edge
        Point result = Compute(callout, CalloutSide.Top, screen: LargeScreen);

        // The primary "below" candidates place the visible area starting at Y≥1444 which
        // overflows the 1440-high screen.  The fallback lands to the right of the callout.
        Assert.That(result.X + DefaultVisible.Left, Is.GreaterThanOrEqualTo(callout.Right),
            "When 'below' placements are clipped, fallback must place overlay to the right.");

        // Entire overlay must be on screen.
        var screenRect = new Rect(result.X + DefaultVisible.Left,
                                  result.Y + DefaultVisible.Top,
                                  DefaultVisible.Width,
                                  DefaultVisible.Height);
        Assert.That(LargeScreen.Contains(screenRect),
            "Fallback candidate must fit entirely within screen bounds.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3.  MathEx.IsBetween
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A point at the centre of the bounding box returns true.
    /// </summary>
    [Test]
    public void IsBetween_CentrePoint_ReturnsTrue()
    {
        var centre = new Point(150, 150);
        var b1 = new Point(100, 100);
        var b2 = new Point(200, 200);

        Assert.That(MathEx.IsBetween(centre, b1, b2), Is.True,
            "Centre point should be inside the bounding box.");
    }

    /// <summary>
    /// A point clearly outside the bounding box returns false.
    /// </summary>
    [Test]
    public void IsBetween_OutsidePoint_ReturnsFalse()
    {
        var outside = new Point(50, 50);
        var b1 = new Point(100, 100);
        var b2 = new Point(200, 200);

        Assert.That(MathEx.IsBetween(outside, b1, b2), Is.False,
            "Point outside the bounding box should return false.");
    }

    /// <summary>
    /// A point that is geometrically inside the bounding box but within the
    /// innerMargin shrink zone returns false.
    ///   bounds X: [100, 200], innerMargin=10 → valid zone: [110, 190]
    ///   test X=105 → 105 &lt; 110 → false.
    /// </summary>
    [Test]
    public void IsBetween_InsideInnerMargin_ReturnsFalse()
    {
        var nearEdge = new Point(105, 150);   // X is 5 px inside the left edge — inside margin band
        var b1 = new Point(100, 100);
        var b2 = new Point(200, 200);

        Assert.That(MathEx.IsBetween(nearEdge, b1, b2, innerMargin: 10), Is.False,
            "Point within innerMargin of the bounding box edge should return false.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GAP NOTE — nav overlay drag exclusion
    // ══════════════════════════════════════════════════════════════════════════
    // IsCursorOverDraggableCalloutSurface() is private and only reachable through
    // Window_MouseDown on a live WPF window.  Testing it requires spinning up a
    // full WPF message loop and synthesising mouse events — out of scope for
    // unit tests.  Consider an integration/UI-automation test if regression
    // coverage is needed.
}
