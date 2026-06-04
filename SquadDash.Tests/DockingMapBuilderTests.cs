#nullable enable

using NUnit.Framework;
using SquadDash.PanelDocking;

namespace SquadDash.Tests;

[TestFixture]
public class DockingMapBuilderTests
{
    private static readonly DockZone[] LeftZones =
    [
        DockZone.Left, DockZone.Left2, DockZone.Left3,
        DockZone.Left4, DockZone.Left5, DockZone.Left6
    ];

    private static readonly DockZone[] RightZones =
    [
        DockZone.Right, DockZone.Right2, DockZone.Right3,
        DockZone.Right4, DockZone.Right5, DockZone.Right6
    ];

    [Test]
    public void BuildDockingMap_WithTwoOccupiedLeftZones_EmitsOuterMiddleAndInnerThinTargets()
    {
        var map = Build(
            sourcePanelId: "loop",
            ("loop", DockZone.Top),
            ("approvals", DockZone.Left),
            ("tasks", DockZone.Left2));

        var thins = ThinSlots(map, LeftZones);

        Assert.That(thins.Count, Is.EqualTo(3));
        Assert.That(thins.Select(s => (s.TargetZone, s.InsertKind)), Is.EqualTo(new[]
        {
            (DockZone.Left3, SyntheticInsertKind.None),
            (DockZone.Left, SyntheticInsertKind.InsertBefore),
            (DockZone.Left, SyntheticInsertKind.InsertAfter),
        }));
        Assert.That(DockingMapBuilder.FindAdjacentThinViolations(map.Slots), Is.Empty);
    }

    [Test]
    public void BuildDockingMap_WithSingleOccupiedRightZone_EmitsInnerAndOuterThinTargets()
    {
        var map = Build(
            sourcePanelId: "loop",
            ("loop", DockZone.Top),
            ("approvals", DockZone.Right));

        var thins = ThinSlots(map, RightZones);

        Assert.That(thins.Count, Is.EqualTo(2));
        Assert.That(thins.Select(s => (s.TargetZone, s.InsertKind)), Is.EqualTo(new[]
        {
            (DockZone.Right, SyntheticInsertKind.InsertBefore),
            (DockZone.Right2, SyntheticInsertKind.None),
        }));
        Assert.That(DockingMapBuilder.FindAdjacentThinViolations(map.Slots), Is.Empty);
    }

    [Test]
    public void BuildDockingMap_WithAllSixLeftZonesOccupied_StillEmitsNPlusOneThinTargets()
    {
        var map = Build(
            sourcePanelId: "loop",
            ("loop", DockZone.Top),
            ("l1", DockZone.Left),
            ("l2", DockZone.Left2),
            ("l3", DockZone.Left3),
            ("l4", DockZone.Left4),
            ("l5", DockZone.Left5),
            ("l6", DockZone.Left6));

        var thins = ThinSlots(map, LeftZones);

        Assert.That(thins.Count, Is.EqualTo(7));
        Assert.That(thins.First().TargetZone, Is.EqualTo(DockZone.Left6));
        Assert.That(thins.First().InsertKind, Is.EqualTo(SyntheticInsertKind.InsertBefore));
        Assert.That(thins.Last().TargetZone, Is.EqualTo(DockZone.Left));
        Assert.That(thins.Last().InsertKind, Is.EqualTo(SyntheticInsertKind.InsertAfter));
        Assert.That(DockingMapBuilder.FindAdjacentThinViolations(map.Slots), Is.Empty);
    }

    [Test]
    public void BuildDockingMap_WithAllSixRightZonesOccupied_StillEmitsNPlusOneThinTargets()
    {
        var map = Build(
            sourcePanelId: "loop",
            ("loop", DockZone.Top),
            ("r1", DockZone.Right),
            ("r2", DockZone.Right2),
            ("r3", DockZone.Right3),
            ("r4", DockZone.Right4),
            ("r5", DockZone.Right5),
            ("r6", DockZone.Right6));

        var thins = ThinSlots(map, RightZones);

        Assert.That(thins.Count, Is.EqualTo(7));
        Assert.That(thins.First().TargetZone, Is.EqualTo(DockZone.Right));
        Assert.That(thins.First().InsertKind, Is.EqualTo(SyntheticInsertKind.InsertBefore));
        Assert.That(thins.Last().TargetZone, Is.EqualTo(DockZone.Right6));
        Assert.That(thins.Last().InsertKind, Is.EqualTo(SyntheticInsertKind.InsertAfter));
        Assert.That(DockingMapBuilder.FindAdjacentThinViolations(map.Slots), Is.Empty);
    }

    [Test]
    public void BuildDockingMap_WithSourceAsOnlySidePanel_DoesNotOfferNoopLateralThin()
    {
        var map = Build(
            sourcePanelId: "tasks",
            ("tasks", DockZone.Left2));

        Assert.That(ThinSlots(map, LeftZones), Is.Empty);
        Assert.That(DockingMapBuilder.FindAdjacentThinViolations(map.Slots), Is.Empty);
    }

    private static DockingMapViewModel Build(
        string sourcePanelId,
        params (string PanelId, DockZone Zone)[] placements)
    {
        var layout = new DockLayout
        {
            Slots = placements
                .Select((p, index) => new PanelSlot(p.PanelId, p.Zone, index))
                .ToList(),
        };

        return DockingMapBuilder.BuildDockingMap(sourcePanelId, layout);
    }

    private static List<SlotButtonViewModel> ThinSlots(
        DockingMapViewModel map,
        IReadOnlyCollection<DockZone> sideZones) =>
        map.Slots
            .Where(s => !s.IsSeparator && sideZones.Contains(s.TargetZone) && s.Width < 48)
            .OrderBy(s => s.X)
            .ToList();
}
