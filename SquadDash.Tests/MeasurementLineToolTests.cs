using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace SquadDash.Tests;

/// <summary>
/// NUnit tests for the measurement/dimension-line annotation tool.
///
/// Tests are written against the expected interface defined in
/// <c>MeasurementLineStubs.cs</c>.  Once Lyra's implementation lands, swap
/// the stub file for real <c>&lt;Compile Include&gt;</c> links and the tests
/// should continue to pass without modification.
/// </summary>
[TestFixture]
internal sealed class MeasurementLineToolTests
{
    // ── SnapOrientation ───────────────────────────────────────────────────────

    [Test]
    public void SnapOrientation_WiderDrag_SnapsHorizontal()
    {
        var start = new Point(0, 0);
        var end   = new Point(200, 50);   // |dx|=200 > |dy|=50

        Assert.That(MeasurementLineTool.SnapOrientation(start, end),
            Is.EqualTo(LineOrientation.Horizontal));
    }

    [Test]
    public void SnapOrientation_TallerDrag_SnapsVertical()
    {
        var start = new Point(0, 0);
        var end   = new Point(50, 200);   // |dy|=200 > |dx|=50

        Assert.That(MeasurementLineTool.SnapOrientation(start, end),
            Is.EqualTo(LineOrientation.Vertical));
    }

    [Test]
    public void SnapOrientation_EqualDeltas_SnapsHorizontalDeterministically()
    {
        var start = new Point(0, 0);
        var end   = new Point(100, 100);  // |dx| == |dy|

        // Tie must always resolve to Horizontal — no randomness allowed.
        Assert.That(MeasurementLineTool.SnapOrientation(start, end),
            Is.EqualTo(LineOrientation.Horizontal));
    }

    [Test]
    public void SnapOrientation_NegativeDirection_SnapsByMagnitudeNotSign()
    {
        // Drag left-and-slightly-down: |dx|=200 > |dy|=50
        var start = new Point(300, 200);
        var end   = new Point(100, 250);

        Assert.That(MeasurementLineTool.SnapOrientation(start, end),
            Is.EqualTo(LineOrientation.Horizontal));
    }

    [Test]
    public void SnapOrientation_PureVerticalDrag_SnapsVertical()
    {
        var start = new Point(50, 0);
        var end   = new Point(50, 300);   // dx=0, dy=300

        Assert.That(MeasurementLineTool.SnapOrientation(start, end),
            Is.EqualTo(LineOrientation.Vertical));
    }

    [Test]
    public void SnapOrientation_PureHorizontalDrag_SnapsHorizontal()
    {
        var start = new Point(0, 100);
        var end   = new Point(256, 100);  // dy=0, dx=256

        Assert.That(MeasurementLineTool.SnapOrientation(start, end),
            Is.EqualTo(LineOrientation.Horizontal));
    }

    // ── FormatLabel ───────────────────────────────────────────────────────────

    [TestCase(128.0,  "128 px", TestName = "FormatLabel_128px")]
    [TestCase(  0.0,    "0 px", TestName = "FormatLabel_0px")]
    [TestCase(255.7,  "256 px", TestName = "FormatLabel_RoundsUp")]
    [TestCase(255.4,  "255 px", TestName = "FormatLabel_RoundsDown")]
    [TestCase(  1.0,    "1 px", TestName = "FormatLabel_1px")]
    [TestCase( 64.0,   "64 px", TestName = "FormatLabel_64px")]
    public void FormatLabel_GivenDistance_ReturnsExpectedText(double distance, string expected)
    {
        Assert.That(MeasurementLineTool.FormatLabel(distance), Is.EqualTo(expected));
    }

    [Test]
    public void FormatLabel_HalfPixel_RoundsToNearest()
    {
        // 0.5 rounds to 0 or 1 depending on MidpointRounding; either is valid
        // as long as it is consistent.  Assert no exception and non-null result.
        var label = MeasurementLineTool.FormatLabel(0.5);
        Assert.That(label, Does.EndWith(" px"));
    }

    // ── Label placement ───────────────────────────────────────────────────────

    [Test]
    public void ComputeLabelPlacement_LongLine_PlacesLabelInside()
    {
        // Line 300 px, label ~60 px wide → plenty of room
        Assert.That(
            MeasurementLineTool.ComputeLabelPlacement(lineLength: 300, labelWidth: 60),
            Is.EqualTo(LabelPlacement.InsideLine));
    }

    [Test]
    public void ComputeLabelPlacement_ShortLine_PlacesLabelAdjacent()
    {
        // Line 40 px, label ~60 px wide → no room
        Assert.That(
            MeasurementLineTool.ComputeLabelPlacement(lineLength: 40, labelWidth: 60),
            Is.EqualTo(LabelPlacement.AdjacentToLine));
    }

    [Test]
    public void ComputeLabelPlacement_LineLengthExactlyAtThreshold_PlacesLabelInside()
    {
        // lineLength == labelWidth + threshold → fits (boundary is inclusive)
        var threshold = MeasurementLineTool.LabelPaddingThreshold;
        Assert.That(
            MeasurementLineTool.ComputeLabelPlacement(lineLength: 60 + threshold, labelWidth: 60),
            Is.EqualTo(LabelPlacement.InsideLine));
    }

    [Test]
    public void ComputeLabelPlacement_OneBelowThreshold_PlacesLabelAdjacent()
    {
        var threshold = MeasurementLineTool.LabelPaddingThreshold;
        Assert.That(
            MeasurementLineTool.ComputeLabelPlacement(lineLength: 60 + threshold - 1, labelWidth: 60),
            Is.EqualTo(LabelPlacement.AdjacentToLine));
    }

    [Test]
    public void ComputeLabelPlacement_ZeroLengthLine_PlacesLabelAdjacent()
    {
        Assert.That(
            MeasurementLineTool.ComputeLabelPlacement(lineLength: 0, labelWidth: 40),
            Is.EqualTo(LabelPlacement.AdjacentToLine));
    }

    // ── Color contract ────────────────────────────────────────────────────────

    [Test]
    public void BadgeBackground_IsAlwaysBlack()
    {
        Assert.That(MeasurementLineAnnotation.BadgeBackground, Is.EqualTo(Colors.Black));
    }

    [Test]
    public void BadgeForeground_IsAlwaysWhite()
    {
        Assert.That(MeasurementLineAnnotation.BadgeForeground, Is.EqualTo(Colors.White));
    }

    [Test]
    public void LineColor_IsPreservedInAnnotation()
    {
        var annotation = new MeasurementLineAnnotation(
            StartX: 0, StartY: 0, EndX: 128, EndY: 0,
            Orientation: "Horizontal",
            Color: "#00FF00");

        Assert.That(annotation.Color, Is.EqualTo("#00FF00"));
    }

    [Test]
    public void BadgeColor_DoesNotChangeWhenLineColorChanges()
    {
        // Badge is always black/white regardless of what colour the line uses.
        var blueAnnotation = new MeasurementLineAnnotation(
            StartX: 0, StartY: 0, EndX: 100, EndY: 0,
            Orientation: "Horizontal", Color: "#0000FF");
        var redAnnotation = new MeasurementLineAnnotation(
            StartX: 0, StartY: 0, EndX: 100, EndY: 0,
            Orientation: "Horizontal", Color: "#FF0000");

        Assert.Multiple(() => {
            Assert.That(MeasurementLineAnnotation.BadgeBackground, Is.EqualTo(Colors.Black),
                "badge background must always be black");
            Assert.That(MeasurementLineAnnotation.BadgeForeground, Is.EqualTo(Colors.White),
                "badge foreground must always be white");
            // Verify the two annotations share identical badge colors even though
            // their line colors differ.
            _ = blueAnnotation; // suppress unused-variable warning
            _ = redAnnotation;
        });
    }

    // ── Serialisation round-trip ──────────────────────────────────────────────

    [Test]
    public void MeasurementLineAnnotation_RoundTrip_PreservesAllFields()
    {
        var original = new MeasurementLineAnnotation(
            StartX: 10.5, StartY: 20.0,
            EndX:  138.5, EndY: 20.0,
            Orientation: "Horizontal",
            Color: "#FF0000");

        var json         = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<MeasurementLineAnnotation>(json);

        Assert.Multiple(() => {
            Assert.That(deserialized!.StartX,      Is.EqualTo(original.StartX).Within(1e-9));
            Assert.That(deserialized.StartY,        Is.EqualTo(original.StartY).Within(1e-9));
            Assert.That(deserialized.EndX,          Is.EqualTo(original.EndX).Within(1e-9));
            Assert.That(deserialized.EndY,          Is.EqualTo(original.EndY).Within(1e-9));
            Assert.That(deserialized.Orientation,   Is.EqualTo(original.Orientation));
            Assert.That(deserialized.Color,         Is.EqualTo(original.Color));
        });
    }

    [Test]
    public void MeasurementLineAnnotation_SerializedJson_UsesExpectedPropertyNames()
    {
        var annotation = new MeasurementLineAnnotation(
            StartX: 0, StartY: 0, EndX: 100, EndY: 0,
            Orientation: "Horizontal",
            Color: "#0000FF");

        var json = JsonSerializer.Serialize(annotation);

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"startX\""));
            Assert.That(json, Does.Contain("\"startY\""));
            Assert.That(json, Does.Contain("\"endX\""));
            Assert.That(json, Does.Contain("\"endY\""));
            Assert.That(json, Does.Contain("\"orientation\""));
            Assert.That(json, Does.Contain("\"color\""));
        });
    }

    [Test]
    public void MeasurementLineAnnotation_VerticalLine_RoundTrips()
    {
        var original = new MeasurementLineAnnotation(
            StartX: 50, StartY: 10,
            EndX:   50, EndY: 74,
            Orientation: "Vertical",
            Color: "#FF7814");

        var json         = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<MeasurementLineAnnotation>(json);

        Assert.That(deserialized!.Orientation, Is.EqualTo("Vertical"));
    }

    // ── Integration: label text computed from endpoint coordinates ────────────

    [Test]
    public void FormatLabel_HorizontalSpanOf128Px_Returns128Px()
    {
        var start    = new Point(10, 50);
        var end      = new Point(138, 50);  // horizontal span of exactly 128 px
        var distance = Math.Abs(end.X - start.X);

        Assert.That(MeasurementLineTool.FormatLabel(distance), Is.EqualTo("128 px"));
    }

    [Test]
    public void FormatLabel_VerticalSpanOf64Px_Returns64Px()
    {
        var start    = new Point(50, 10);
        var end      = new Point(50, 74);   // vertical span of exactly 64 px
        var distance = Math.Abs(end.Y - start.Y);

        Assert.That(MeasurementLineTool.FormatLabel(distance), Is.EqualTo("64 px"));
    }
}
