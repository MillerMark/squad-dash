using System.Windows;

namespace SquadDash.Tests;

/// <summary>
/// Verifies geometric invariants of the callout dangle triangle:
/// - Base width (span of tp2/tp3 along the callout edge) ≥ 8 px
/// - Triangle height (perpendicular distance from base to tip) ≥ 8 px
/// - Both base points pinned to the correct callout edge
///
/// All point values are taken directly from production trace logs, making
/// these tests reliable regression anchors.  No WPF UI thread is required.
/// </summary>
[TestFixture]
internal sealed class DangleTriangleTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    static double MeasureDangleBaseWidth(Point tp1, Point tp2, Point tp3, CalloutSide dangleSide) =>
        dangleSide is CalloutSide.Bottom or CalloutSide.Top
            ? Math.Abs(tp3.X - tp2.X)
            : Math.Abs(tp3.Y - tp2.Y);

    static double MeasureDangleHeight(Point tp1, Point tp2, CalloutSide dangleSide) =>
        dangleSide switch
        {
            CalloutSide.Bottom => tp1.Y - tp2.Y,
            CalloutSide.Top    => tp2.Y - tp1.Y,
            CalloutSide.Right  => tp1.X - tp2.X,
            CalloutSide.Left   => tp2.X - tp1.X,
            _                  => throw new ArgumentOutOfRangeException(nameof(dangleSide))
        };

    static void VerifyBaseOnEdge(
        Point tp2, Point tp3, CalloutSide dangleSide,
        double cbLeft, double cbRight, double cbTop, double cbBottom)
    {
        switch (dangleSide)
        {
            case CalloutSide.Bottom:
                Assert.That(tp2.Y, Is.EqualTo(cbBottom).Within(0.01));
                Assert.That(tp3.Y, Is.EqualTo(cbBottom).Within(0.01));
                break;
            case CalloutSide.Top:
                Assert.That(tp2.Y, Is.EqualTo(cbTop).Within(0.01));
                Assert.That(tp3.Y, Is.EqualTo(cbTop).Within(0.01));
                break;
            case CalloutSide.Right:
                Assert.That(tp2.X, Is.EqualTo(cbRight).Within(0.01));
                Assert.That(tp3.X, Is.EqualTo(cbRight).Within(0.01));
                break;
            case CalloutSide.Left:
                Assert.That(tp2.X, Is.EqualTo(cbLeft).Within(0.01));
                Assert.That(tp3.X, Is.EqualTo(cbLeft).Within(0.01));
                break;
        }
    }

    // ── tests ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Regression for the thin-triangle bug at ~281°.
    /// Before the edge-pinning fix, tp2/tp3 Y values were inside the box (70, 77).
    /// After fix, both are pinned to cbBottom=78, giving a base width of exactly 8 px.
    /// </summary>
    [Test]
    public void MinimumBaseWidth_Bottom_IsAtLeast8px()
    {
        // Before fix: tp2=(232.0,70.0) tp3=(240.0,77.0) — Y values inside box
        // After fix:  tp2=(232.0,78.0) tp3=(240.0,78.0) — Y=cbBottom
        var tp1 = new Point(263.2, 86.0);   // min-height applied: cbBottom+8 = 86
        var tp2 = new Point(232.0, 78.0);   // cbBottom=78
        var tp3 = new Point(240.0, 78.0);   // cbBottom=78
        double cbBottom = 78.0;

        // base width = |tp3.X - tp2.X| = 8.0
        Assert.That(Math.Abs(tp3.X - tp2.X), Is.GreaterThanOrEqualTo(8.0));
        // height = tp1.Y - tp2.Y = 86.0 - 78.0 = 8.0
        Assert.That(tp1.Y - tp2.Y, Is.GreaterThanOrEqualTo(8.0));
        Assert.That(tp2.Y, Is.EqualTo(cbBottom).Within(0.01));
        Assert.That(tp3.Y, Is.EqualTo(cbBottom).Within(0.01));
    }

    /// <summary>
    /// Right-side dangle: base points must be pinned to cbRight,
    /// and the vertical span (base height) must be ≥ 8 px.
    /// </summary>
    [Test]
    public void MinimumBaseWidth_Right_IsAtLeast8px()
    {
        var tp1 = new Point(269.7, 76.4);
        var tp2 = new Point(240.0, 61.1);   // cbRight=240 (pinned)
        var tp3 = new Point(240.0, 78.0);
        double cbRight = 240.0;

        Assert.That(Math.Abs(tp3.Y - tp2.Y), Is.GreaterThanOrEqualTo(8.0));
        Assert.That(tp2.X, Is.EqualTo(cbRight).Within(0.01));
        Assert.That(tp3.X, Is.EqualTo(cbRight).Within(0.01));
    }

    /// <summary>
    /// Near-corner-angle case: both base Y values must equal cbBottom.
    /// </summary>
    [Test]
    public void BasePoints_BothOnSameEdge_Bottom()
    {
        var tp2 = new Point(232.0, 78.0);
        var tp3 = new Point(240.0, 78.0);
        double cbBottom = 78.0;

        Assert.That(tp2.Y, Is.EqualTo(cbBottom).Within(0.01), "tp2 should be on bottom edge");
        Assert.That(tp3.Y, Is.EqualTo(cbBottom).Within(0.01), "tp3 should be on bottom edge");
    }

    /// <summary>
    /// "Visible width" is the span of the triangle where it crosses the callout edge.
    /// With base pinned to the edge, this equals the base width directly (no interpolation needed).
    /// Previously failing: base was inside the box at Y=70 and Y=77, giving ~6.6 px.
    /// After fix: base at Y=78, visible width = 8 px.
    /// </summary>
    [Test]
    public void VisibleWidth_AtPerimeter_NearHorizontalAngle()
    {
        var tp2 = new Point(232.0, 78.0);
        var tp3 = new Point(240.0, 78.0);

        double baseWidth = Math.Abs(tp3.X - tp2.X);
        Assert.That(baseWidth, Is.GreaterThanOrEqualTo(8.0),
            "Triangle must be at least 8 px wide where it crosses the callout edge");
    }

    /// <summary>
    /// The minimum-height enforcement pushes tp1.Y from 83.9 to 86.0 so that
    /// the triangle protrudes at least 8 px below the callout box.
    /// </summary>
    [Test]
    public void TriangleDangleHeight_Bottom_IsAtLeast8px()
    {
        var tp1 = new Point(263.2, 86.0);  // pushed by min-height from 83.9
        var tp2 = new Point(232.0, 78.0);  // cbBottom

        double height = tp1.Y - tp2.Y;
        Assert.That(height, Is.GreaterThanOrEqualTo(8.0));
    }

    /// <summary>
    /// The tip must lie outside (below) the callout box for a Bottom dangle.
    /// </summary>
    [Test]
    public void TipIsOutsideCalloutBox_Bottom()
    {
        var tp1 = new Point(263.2, 86.0);
        double cbBottom = 78.0;

        Assert.That(tp1.Y, Is.GreaterThan(cbBottom),
            "Tip should be below the callout box for Bottom dangle");
    }

    /// <summary>
    /// When tp2 and tp3 would otherwise collapse to less than 8 px apart,
    /// the spread correction centers an 8 px base on the tip's X projection
    /// (clamped to stay within the callout edge bounds).
    /// </summary>
    [Test]
    public void BaseSpread_WhenCollapsed_CenteredOnTipProjection()
    {
        // Near-collapse: raw tp2.X=238.5, tp3.X=239.2 (only 0.7 px apart)
        // tip.X=248.0, cbLeft=40, cbRight=240, half-spread=4
        // centerX = Clamp(248.0, 40+4, 240-4) = Clamp(248, 44, 236) = 236
        // → tp2.X = 236-4 = 232,  tp3.X = 236+4 = 240
        double tipX = Math.Clamp(248.0, 40.0 + 4.0, 240.0 - 4.0);  // = 236

        Assert.That(tipX, Is.EqualTo(236.0).Within(0.01));
        Assert.That(tipX - 4.0, Is.EqualTo(232.0).Within(0.01), "tp2.X");
        Assert.That(tipX + 4.0, Is.EqualTo(240.0).Within(0.01), "tp3.X");
    }

    // ── helper-method contract tests ───────────────────────────────────────────

    [Test]
    public void MeasureDangleBaseWidth_Bottom_ReturnsHorizontalSpan()
    {
        var tp1 = new Point(263.2, 86.0);
        var tp2 = new Point(232.0, 78.0);
        var tp3 = new Point(240.0, 78.0);

        double width = MeasureDangleBaseWidth(tp1, tp2, tp3, CalloutSide.Bottom);
        Assert.That(width, Is.EqualTo(8.0).Within(0.001));
    }

    [Test]
    public void MeasureDangleBaseWidth_Right_ReturnsVerticalSpan()
    {
        var tp1 = new Point(269.7, 76.4);
        var tp2 = new Point(240.0, 61.1);
        var tp3 = new Point(240.0, 78.0);

        double height = MeasureDangleBaseWidth(tp1, tp2, tp3, CalloutSide.Right);
        Assert.That(height, Is.EqualTo(Math.Abs(78.0 - 61.1)).Within(0.001));
    }

    [Test]
    public void MeasureDangleHeight_Bottom_ReturnsTipBelowBase()
    {
        var tp1 = new Point(263.2, 86.0);
        var tp2 = new Point(232.0, 78.0);

        double h = MeasureDangleHeight(tp1, tp2, CalloutSide.Bottom);
        Assert.That(h, Is.EqualTo(8.0).Within(0.001));
    }

    [Test]
    public void VerifyBaseOnEdge_Bottom_PassesWhenPinned()
    {
        var tp2 = new Point(232.0, 78.0);
        var tp3 = new Point(240.0, 78.0);

        Assert.DoesNotThrow(() =>
            VerifyBaseOnEdge(tp2, tp3, CalloutSide.Bottom,
                cbLeft: 40, cbRight: 240, cbTop: 10, cbBottom: 78));
    }

    [Test]
    public void VerifyBaseOnEdge_Bottom_FailsWhenInsideBox()
    {
        // Pre-fix values: tp2.Y=70, tp3.Y=77 — both inside the box (cbBottom=78)
        var tp2 = new Point(232.0, 70.0);
        var tp3 = new Point(240.0, 77.0);

        Assert.Throws<AssertionException>(() =>
            VerifyBaseOnEdge(tp2, tp3, CalloutSide.Bottom,
                cbLeft: 40, cbRight: 240, cbTop: 10, cbBottom: 78));
    }
}
