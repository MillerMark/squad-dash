#nullable enable

using SquadDash.PanelDocking;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PanelDockingServiceTests
{
    [Test]
    public void MovePanel_UpdatesCurrentLayout_ZoneIsCorrect()
    {
        var svc = new PanelDockingService();
        svc.MovePanel("tasks", DockZone.Left);
        var slot = svc.CurrentLayout.Slots.Single(s => s.PanelId == "tasks");
        Assert.That(slot.Zone, Is.EqualTo(DockZone.Left));
    }

    [Test]
    public void MovePanel_FromTopToLeft_RemovesFromTopAndAddsToLeft()
    {
        var svc = new PanelDockingService();
        svc.MovePanel("tasks", DockZone.Left);
        var topSlots = svc.CurrentLayout.Slots.Where(s => s.Zone == DockZone.Top).Select(s => s.PanelId);
        var leftSlots = svc.CurrentLayout.Slots.Where(s => s.Zone == DockZone.Left).Select(s => s.PanelId);
        Assert.That(topSlots, Does.Not.Contain("tasks"));
        Assert.That(leftSlots, Contains.Item("tasks"));
    }

    [Test]
    public void MovePanel_FromLeftBackToTop_RestoresPanel()
    {
        var svc = new PanelDockingService();
        svc.MovePanel("tasks", DockZone.Left);
        svc.MovePanel("tasks", DockZone.Top);
        var topSlots = svc.CurrentLayout.Slots.Where(s => s.Zone == DockZone.Top).Select(s => s.PanelId);
        var leftSlots = svc.CurrentLayout.Slots.Where(s => s.Zone == DockZone.Left).Select(s => s.PanelId);
        Assert.That(topSlots, Contains.Item("tasks"));
        Assert.That(leftSlots, Does.Not.Contain("tasks"));
    }

    [Test]
    public void MovePanel_SameZone_NoChange()
    {
        var svc = new PanelDockingService();
        var before = svc.CurrentLayout.Slots.Single(s => s.PanelId == "tasks");
        svc.MovePanel("tasks", DockZone.Top); // already in Top
        var after = svc.CurrentLayout.Slots.Single(s => s.PanelId == "tasks");
        Assert.That(after.Zone, Is.EqualTo(before.Zone));
        Assert.That(after.Order, Is.EqualTo(before.Order));
    }

    [Test]
    public void MovePanel_LastPanelOutOfLeft_ZoneIsEmpty()
    {
        var svc = new PanelDockingService();
        svc.MovePanel("tasks", DockZone.Left);
        svc.MovePanel("tasks", DockZone.Top);
        Assert.That(svc.CurrentLayout.Slots.Any(s => s.Zone == DockZone.Left), Is.False);
    }

    [Test]
    public void MovePanel_LastPanelOutOfRight_ZoneIsEmpty()
    {
        var svc = new PanelDockingService();
        svc.MovePanel("inbox", DockZone.Right);
        svc.MovePanel("inbox", DockZone.Top);
        Assert.That(svc.CurrentLayout.Slots.Any(s => s.Zone == DockZone.Right), Is.False);
    }

    [Test]
    public void MovePanel_MultipleToLeft_OrderIsIncreasing()
    {
        var svc = new PanelDockingService();
        svc.MovePanel("tasks", DockZone.Left);
        svc.MovePanel("inbox", DockZone.Left);
        var leftSlots = svc.CurrentLayout.Slots.Where(s => s.Zone == DockZone.Left).OrderBy(s => s.Order).ToList();
        Assert.That(leftSlots.Count, Is.EqualTo(2));
        Assert.That(leftSlots[0].PanelId, Is.EqualTo("tasks"));
        Assert.That(leftSlots[1].PanelId, Is.EqualTo("inbox"));
        Assert.That(leftSlots[1].Order, Is.GreaterThan(leftSlots[0].Order));
    }
}
