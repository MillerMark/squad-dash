namespace SquadDash.Tests;

[TestFixture]
internal sealed class ColorUtilitiesTests {

    // ── RgbToHsl ─────────────────────────────────────────────────────────────

    [Test]
    public void RgbToHsl_Black_ProducesZeroHsl() {
        ColorUtilities.RgbToHsl(0, 0, 0, out var h, out var s, out var l);

        Assert.Multiple(() => {
            Assert.That(h, Is.EqualTo(0).Within(1e-9));
            Assert.That(s, Is.EqualTo(0).Within(1e-9));
            Assert.That(l, Is.EqualTo(0).Within(1e-9));
        });
    }

    [Test]
    public void RgbToHsl_White_ProducesFullLightness() {
        ColorUtilities.RgbToHsl(255, 255, 255, out var h, out var s, out var l);

        Assert.Multiple(() => {
            Assert.That(h, Is.EqualTo(0).Within(1e-9));
            Assert.That(s, Is.EqualTo(0).Within(1e-9));
            Assert.That(l, Is.EqualTo(1.0).Within(1e-9));
        });
    }

    [Test]
    public void RgbToHsl_PureRed_ProducesCorrectHsl() {
        ColorUtilities.RgbToHsl(255, 0, 0, out var h, out var s, out var l);

        Assert.Multiple(() => {
            Assert.That(h, Is.EqualTo(0).Within(1e-9));
            Assert.That(s, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(l, Is.EqualTo(0.5).Within(1e-9));
        });
    }

    [Test]
    public void RgbToHsl_PureGreen_ProducesCorrectHsl() {
        ColorUtilities.RgbToHsl(0, 255, 0, out var h, out var s, out var l);

        Assert.Multiple(() => {
            Assert.That(h, Is.EqualTo(1.0 / 3).Within(1e-9));
            Assert.That(s, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(l, Is.EqualTo(0.5).Within(1e-9));
        });
    }

    [Test]
    public void RgbToHsl_PureBlue_ProducesCorrectHsl() {
        ColorUtilities.RgbToHsl(0, 0, 255, out var h, out var s, out var l);

        Assert.Multiple(() => {
            Assert.That(h, Is.EqualTo(2.0 / 3).Within(1e-9));
            Assert.That(s, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(l, Is.EqualTo(0.5).Within(1e-9));
        });
    }

    // ── HslToRgb ─────────────────────────────────────────────────────────────

    [Test]
    public void HslToRgb_Achromatic_ProducesGray() {
        // s=0 → gray; l=0.5 → mid-gray
        ColorUtilities.HslToRgb(0.0, 0.0, 0.5, out var r, out var g, out var b);

        Assert.Multiple(() => {
            Assert.That(r, Is.EqualTo(g));
            Assert.That(g, Is.EqualTo(b));
            Assert.That(r, Is.EqualTo(127).Within(1));
        });
    }

    // ── Round-trip ───────────────────────────────────────────────────────────

    [TestCase(100, 150, 200)]
    [TestCase(255, 128,   0)]
    [TestCase( 10,  20,  30)]
    public void RoundTrip_KnownColor_ReturnsSameRgb(byte rIn, byte gIn, byte bIn) {
        ColorUtilities.RgbToHsl(rIn, gIn, bIn, out var h, out var s, out var l);
        ColorUtilities.HslToRgb(h, s, l, out var rOut, out var gOut, out var bOut);

        Assert.Multiple(() => {
            Assert.That(rOut, Is.EqualTo(rIn).Within(1));
            Assert.That(gOut, Is.EqualTo(gIn).Within(1));
            Assert.That(bOut, Is.EqualTo(bIn).Within(1));
        });
    }

    // ── HueToRgb ─────────────────────────────────────────────────────────────

    [Test]
    public void HueToRgb_NegativeT_WrapsAroundByOne() {
        var direct  = ColorUtilities.HueToRgb(0.1, 0.9, 0.1);
        var wrapped = ColorUtilities.HueToRgb(0.1, 0.9, -0.9);

        Assert.That(wrapped, Is.EqualTo(direct).Within(1e-9));
    }

    [Test]
    public void HueToRgb_TGreaterThanOne_WrapsAroundByOne() {
        var direct  = ColorUtilities.HueToRgb(0.1, 0.9, 0.3);
        var wrapped = ColorUtilities.HueToRgb(0.1, 0.9, 1.3);

        Assert.That(wrapped, Is.EqualTo(direct).Within(1e-9));
    }

    [Test]
    public void HueToRgb_TLessThanOneSixth_ReturnsLinearInterpolation() {
        // t < 1/6 → p + (q-p)*6*t
        const double p = 0.2, q = 0.8, t = 0.1;
        var expected = p + (q - p) * 6 * t;

        Assert.That(ColorUtilities.HueToRgb(p, q, t), Is.EqualTo(expected).Within(1e-9));
    }

    [Test]
    public void HueToRgb_TBetweenOneSixthAndHalf_ReturnsQ() {
        // 1/6 <= t < 1/2 → q
        Assert.That(ColorUtilities.HueToRgb(0.2, 0.8, 0.3), Is.EqualTo(0.8).Within(1e-9));
    }

    [Test]
    public void HueToRgb_TBetweenHalfAndTwoThirds_ReturnsInterpolation() {
        // 1/2 <= t < 2/3 → p + (q-p)*(2/3 - t)*6
        const double p = 0.2, q = 0.8, t = 0.6;
        var expected = p + (q - p) * (2.0 / 3 - t) * 6;

        Assert.That(ColorUtilities.HueToRgb(p, q, t), Is.EqualTo(expected).Within(1e-9));
    }

    [Test]
    public void HueToRgb_TGreaterThanTwoThirds_ReturnsP() {
        // t >= 2/3 → p
        Assert.That(ColorUtilities.HueToRgb(0.2, 0.8, 0.9), Is.EqualTo(0.2).Within(1e-9));
    }
}
