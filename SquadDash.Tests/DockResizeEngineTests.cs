#nullable enable

using SquadDash.PanelDocking;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class DockResizeEngineTests
{
    [Test, Apartment(ApartmentState.STA)]
    public void GripStripBorder_ExposesDockResizeSizeHints()
    {
        var panel = new GripStripBorder
        {
            MinWidth = 200,
            DockMinimumWidth = 220,
            DockMaximumUsefulWidth = 620,
        };

        Assert.That(panel, Is.AssignableTo<IDockResizeSizeHint>());
        Assert.That(panel.GetMinimumDockSize(DockResizeOrientation.Horizontal), Is.EqualTo(220));
        Assert.That(panel.GetMaximumUsefulDockSize(DockResizeOrientation.Horizontal), Is.EqualTo(620));
    }

    [Test]
    public void NormalDrag_ResizesOnlyAdjacentParticipants()
    {
        var sizes = DockResizeEngine.Resize(
            Participants((300, 10, null), (240, 100, null), (260, 100, null)),
            splitterLeftParticipantIndex: 1,
            DockResizeMode.Normal,
            delta: 50);

        Assert.That(sizes, Is.EqualTo(new[] { 300, 290, 210 }).Within(0.001));
    }

    [Test]
    public void NormalDrag_ClampsAtShrinkingMinimum()
    {
        var sizes = DockResizeEngine.Resize(
            Participants((300, 10, null), (240, 100, null), (120, 100, null)),
            splitterLeftParticipantIndex: 1,
            DockResizeMode.Normal,
            delta: 80);

        Assert.That(sizes, Is.EqualTo(new[] { 300, 260, 100 }).Within(0.001));
    }

    [Test]
    public void NormalDrag_GrowingPanelBelowMaximumUsefulSize_StopsAtMaximum()
    {
        var sizes = DockResizeEngine.Resize(
            Participants((240, 100, 260), (240, 100, null)),
            splitterLeftParticipantIndex: 0,
            DockResizeMode.Normal,
            delta: 80);

        Assert.That(sizes, Is.EqualTo(new[] { 260, 220 }).Within(0.001));
    }

    [Test]
    public void NormalDrag_GrowingPanelAlreadyAboveMaximumUsefulSize_DoesNotFreeze()
    {
        var sizes = DockResizeEngine.Resize(
            Participants((320, 100, 260), (240, 100, null)),
            splitterLeftParticipantIndex: 0,
            DockResizeMode.Normal,
            delta: 80);

        Assert.That(sizes, Is.EqualTo(new[] { 400, 160 }).Within(0.001));
    }

    [Test]
    public void ProportionalDrag_ShrinksRightSideTogether()
    {
        var sizes = DockResizeEngine.Resize(
            Participants((300, 10, null), (200, 100, null), (300, 100, null)),
            splitterLeftParticipantIndex: 0,
            DockResizeMode.Proportional,
            delta: 100);

        Assert.That(sizes[0], Is.EqualTo(400).Within(0.001));
        Assert.That(sizes[1], Is.EqualTo(160).Within(0.001));
        Assert.That(sizes[2], Is.EqualTo(240).Within(0.001));
    }

    [Test]
    public void ChainDrag_GrowsNearestThenNextAtMaximumUsefulSize()
    {
        var sizes = DockResizeEngine.Resize(
            Participants((250, 100, 300), (250, 100, 280), (300, 100, 500)),
            splitterLeftParticipantIndex: 1,
            DockResizeMode.Chain,
            delta: 100);

        Assert.That(sizes, Is.EqualTo(new[] { 300, 280, 220 }).Within(0.001));
    }

    [Test]
    public void ChainDrag_ShrinksNearestThenNextAtMinimum()
    {
        var sizes = DockResizeEngine.Resize(
            Participants((220, 100, 500), (120, 100, 500), (360, 100, 500)),
            splitterLeftParticipantIndex: 1,
            DockResizeMode.Chain,
            delta: -140);

        Assert.That(sizes, Is.EqualTo(new[] { 100, 100, 500 }).Within(0.001));
    }

    [Test]
    public void ChainDragLeft_WhenReceiverAlreadyAboveMaximumUsefulSize_CompressesLeftBoundary()
    {
        var sizes = DockResizeEngine.Resize(
            Participants(
                (924, 260, null),
                (317, 200, 360),
                (320, 200, 320),
                (390, 220, 560),
                (1053, 220, 650)),
            splitterLeftParticipantIndex: 3,
            DockResizeMode.Chain,
            delta: -500);

        Assert.That(sizes, Is.EqualTo(new[] { 831, 200, 200, 220, 1553 }).Within(0.001));
    }

    [Test]
    public void ChainDragLeft_WhenReceiverBelowMaximumUsefulSize_StopsAtMaximum()
    {
        var sizes = DockResizeEngine.Resize(
            Participants(
                (924, 260, null),
                (317, 200, 360),
                (320, 200, 320),
                (390, 220, 560),
                (600, 220, 650)),
            splitterLeftParticipantIndex: 3,
            DockResizeMode.Chain,
            delta: -500);

        Assert.That(sizes, Is.EqualTo(new[] { 924, 317, 320, 340, 650 }).Within(0.001));
    }

    private static DockResizeParticipant[] Participants(params (double Size, double Min, double? Max)[] values) =>
        values.Select(v => new DockResizeParticipant(v.Size, v.Min, v.Max)).ToArray();
}
