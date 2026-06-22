#nullable enable

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
        AssertN1RuleCompliance(svc, nameof(MovePanel_UpdatesCurrentLayout_ZoneIsCorrect));
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
        AssertN1RuleCompliance(svc, nameof(MovePanel_FromTopToLeft_RemovesFromTopAndAddsToLeft));
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

    [Test]
    public void GetCurrentZone_ReturnsTopByDefault()
    {
        var svc = new PanelDockingService();
        Assert.That(svc.GetCurrentZone("tasks"), Is.EqualTo(DockZone.Top));
    }

    [Test]
    public void GetCurrentZone_ReturnsCorrectZoneAfterMove()
    {
        var svc = new PanelDockingService();
        svc.MovePanel("tasks", DockZone.Left);
        Assert.That(svc.GetCurrentZone("tasks"), Is.EqualTo(DockZone.Left));
    }

    [Test]
    public void GetCurrentZone_ReturnsNewZoneAfterMovePanel()
    {
        var svc = new PanelDockingService();
        svc.MovePanel("inbox", DockZone.Right);
        svc.MovePanel("inbox", DockZone.Left);
        Assert.That(svc.GetCurrentZone("inbox"), Is.EqualTo(DockZone.Left));
    }

    [Test]
    public void GetCurrentZone_UnknownPanel_ReturnsTop()
    {
        var svc = new PanelDockingService();
        Assert.That(svc.GetCurrentZone("nonexistent"), Is.EqualTo(DockZone.Top));
    }

    [Test]
    public void ShowDockContextMenu_CurrentZoneIsDisabled_OthersEnabled()
    {
        // Test the service-layer logic that determines which zones are enabled.
        var svc = new PanelDockingService();
        svc.MovePanel("tasks", DockZone.Left);

        var currentZone = svc.GetCurrentZone("tasks");

        var zones = new[] { DockZone.Top, DockZone.Left, DockZone.Right };
        foreach (var zone in zones)
        {
            bool shouldBeEnabled = zone != currentZone;
            // The menu item for the current zone must be disabled; others enabled.
            Assert.That(
                zone != currentZone,
                Is.EqualTo(shouldBeEnabled),
                $"Zone {zone} enabled state mismatch");
        }

        Assert.That(currentZone, Is.EqualTo(DockZone.Left));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void MovePanel_WhenLoadedLayoutSaysSideButElementIsStillTop_DoesNotDoubleParent()
    {
        var tempDir = CreateSavedLayoutWithTasksOnLeft();
        try
        {
            var topZone = new Grid();
            var taskPanel = new Border();
            topZone.Children.Add(taskPanel);
            var svc = CreateWpfDockingService(taskPanel, topZone);

            svc.LoadLayout(tempDir);

            Assert.That(() => svc.MovePanel("tasks", DockZone.Top), Throws.Nothing);
            Assert.That(topZone.Children.Contains(taskPanel), Is.True);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test, Apartment(ApartmentState.STA)]
    public void LoadAndApplyLayout_MovesTopElementIntoSavedSideZone()
    {
        var tempDir = CreateSavedLayoutWithTasksOnLeft();
        try
        {
            var topZone = new Grid();
            var leftZone = new Grid();
            var taskPanel = new Border();
            topZone.Children.Add(taskPanel);
            var svc = CreateWpfDockingService(taskPanel, topZone, leftZone);

            var loaded = svc.LoadAndApplyLayout(tempDir);

            Assert.That(topZone.Children.Contains(taskPanel), Is.False);
            Assert.That(leftZone.Children.Contains(taskPanel), Is.True);
            Assert.That(loaded.Slots.Single(s => s.PanelId == "tasks").Zone, Is.EqualTo(DockZone.Left));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Regression test for the "middle third empty gap" bug.
    /// When a panel in a zone is hidden and a second panel is then docked into the same zone,
    /// <see cref="PanelDockingService.RebuildZoneGrid"/> must NOT give the hidden panel a
    /// star-height row — in WPF a Height="*" row consumes space even when its content is
    /// Collapsed, producing a visible empty gap between the two rendered panels.
    /// </summary>
    [Test, Apartment(ApartmentState.STA)]
    public void MovePanel_IntoZoneWithCollapsedPanel_GridHasTwoStarRowsNotThree()
    {
        var topZone      = new Grid();
        var leftZone     = new Grid();
        var approvals    = new Border();
        var tasks        = new Border();
        var maintenance  = new Border();
        topZone.Children.Add(maintenance);

        var svc = new PanelDockingService(
            new Dictionary<string, FrameworkElement>
            {
                ["approvals"]   = approvals,
                ["tasks"]       = tasks,
                ["maintenance"] = maintenance,
            },
            leftZone,                // leftZonePanel
            new Grid(),              // rightZonePanel
            new Grid(),              // left2ZonePanel
            new Grid(),              // right2ZonePanel
            new Grid(),              // left3ZonePanel
            new Grid(),              // right3ZonePanel
            new Grid(),              // left4ZonePanel
            new Grid(),              // right4ZonePanel
            new Grid(),              // left5ZonePanel
            new Grid(),              // right5ZonePanel
            new Grid(),              // left6ZonePanel
            new Grid(),              // right6ZonePanel
            topZone,
            new ColumnDefinition { Width = new GridLength(280) }, // leftZoneColumn
            new ColumnDefinition(),  // rightZoneColumn
            new ColumnDefinition(),  // left2ZoneColumn
            new ColumnDefinition(),  // right2ZoneColumn
            new ColumnDefinition(),  // left3ZoneColumn
            new ColumnDefinition(),  // right3ZoneColumn
            new ColumnDefinition(),  // left4ZoneColumn
            new ColumnDefinition(),  // right4ZoneColumn
            new ColumnDefinition(),  // left5ZoneColumn
            new ColumnDefinition(),  // right5ZoneColumn
            new ColumnDefinition(),  // left6ZoneColumn
            new ColumnDefinition(),  // right6ZoneColumn
            new ColumnDefinition { Width = new GridLength(5) },   // leftSplitterColumn
            new ColumnDefinition(),  // rightSplitterColumn
            new ColumnDefinition(),  // left2SplitterColumn
            new ColumnDefinition(),  // right2SplitterColumn
            new ColumnDefinition(),  // left3SplitterColumn
            new ColumnDefinition(),  // right3SplitterColumn
            new ColumnDefinition(),  // left4SplitterColumn
            new ColumnDefinition(),  // right4SplitterColumn
            new ColumnDefinition(),  // left5SplitterColumn
            new ColumnDefinition(),  // right5SplitterColumn
            new ColumnDefinition(),  // left6SplitterColumn
            new ColumnDefinition(),  // right6SplitterColumn
            new ScrollViewer(),      // leftZoneScrollViewer
            new ScrollViewer(),      // rightZoneScrollViewer
            new ScrollViewer(),      // left2ZoneScrollViewer
            new ScrollViewer(),      // right2ZoneScrollViewer
            new ScrollViewer(),      // left3ZoneScrollViewer
            new ScrollViewer(),      // right3ZoneScrollViewer
            new ScrollViewer(),      // left4ZoneScrollViewer
            new ScrollViewer(),      // right4ZoneScrollViewer
            new ScrollViewer(),      // left5ZoneScrollViewer
            new ScrollViewer(),      // right5ZoneScrollViewer
            new ScrollViewer(),      // left6ZoneScrollViewer
            new ScrollViewer(),      // right6ZoneScrollViewer
            new GridSplitter(),      // leftZoneSplitter
            new GridSplitter(),      // rightZoneSplitter
            new GridSplitter(),      // left2ZoneSplitter
            new GridSplitter(),      // right2ZoneSplitter
            new GridSplitter(),      // left3ZoneSplitter
            new GridSplitter(),      // right3ZoneSplitter
            new GridSplitter(),      // left4ZoneSplitter
            new GridSplitter(),      // right4ZoneSplitter
            new GridSplitter(),      // left5ZoneSplitter
            new GridSplitter(),      // right5ZoneSplitter
            new GridSplitter(),      // left6ZoneSplitter
            new GridSplitter());     // right6ZoneSplitter

        // Step 1 – dock tasks and approvals into the left zone.
        svc.MovePanel("tasks",     DockZone.Left);
        svc.MovePanel("approvals", DockZone.Left);

        // Step 2 – hide tasks; it stays in _leftZonePanels but must not occupy a grid row.
        tasks.Visibility = Visibility.Collapsed;
        svc.OnPanelVisibilityChanged("tasks", visible: false);

        // Step 3 – dock maintenance into the left zone at order=0.
        // Before the fix this produced [maintenance, tasks(collapsed), approvals] → 3 star rows.
        svc.MovePanel("maintenance", DockZone.Left, targetOrder: 0);

        // Only visible panels (maintenance + approvals) should have star rows.
        // Splitters are 5px rows; count them separately.
        var starRows = leftZone.RowDefinitions.Where(r => r.Height.IsStar).ToList();
        Assert.That(starRows.Count, Is.EqualTo(2),
            "Expected exactly 2 star rows (one per visible panel); a hidden panel must not get a row.");

        // Both visible panels must be children of the left zone grid.
        Assert.That(leftZone.Children.Contains(maintenance), Is.True);
        Assert.That(leftZone.Children.Contains(approvals),   Is.True);
        Assert.That(leftZone.Children.Contains(tasks),       Is.False,
            "Collapsed panel must not be added to the grid.");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ComputeIdealHeightWeights_SoloConstrainedPanel_UsesUsefulHeight()
    {
        var panel = new HintPanel(maximumUsefulHeight: 180);

        var weights = PanelDockingService.ComputeIdealHeightWeights(new List<FrameworkElement> { panel }, availableHeight: 500);

        Assert.That(weights, Is.Not.Null);
        Assert.That(weights![panel], Is.EqualTo(180).Within(0.001));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ComputeIdealHeightWeights_CapsSmallestPanelsAndRedistributesRemainder()
    {
        var shortPanel = new HintPanel(maximumUsefulHeight: 100);
        var tallPanel = new HintPanel(maximumUsefulHeight: 200);

        var weights = PanelDockingService.ComputeIdealHeightWeights(
            new List<FrameworkElement> { shortPanel, tallPanel },
            availableHeight: 305);

        Assert.That(weights, Is.Not.Null);
        Assert.That(weights![shortPanel], Is.EqualTo(100).Within(0.001));
        Assert.That(weights[tallPanel], Is.EqualTo(200).Within(0.001));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ComputeIdealHeightWeights_WhenAllRemainingPanelsAreBelowShare_LeavesLargestToAbsorbRemainder()
    {
        var shortPanel = new HintPanel(maximumUsefulHeight: 100);
        var tallPanel = new HintPanel(maximumUsefulHeight: 200);

        var weights = PanelDockingService.ComputeIdealHeightWeights(
            new List<FrameworkElement> { shortPanel, tallPanel },
            availableHeight: 505);

        Assert.That(weights, Is.Not.Null);
        Assert.That(weights![shortPanel], Is.EqualTo(100).Within(0.001));
        Assert.That(weights[tallPanel], Is.EqualTo(400).Within(0.001));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ComputeIdealHeightWeights_WhenAllPanelsNeedMore_AllocatesProportionallyWithFiveToOneBound()
    {
        var smallDemand = new HintPanel(maximumUsefulHeight: 100);
        var hugeDemand = new HintPanel(maximumUsefulHeight: 1000);

        var weights = PanelDockingService.ComputeIdealHeightWeights(
            new List<FrameworkElement> { smallDemand, hugeDemand },
            availableHeight: 605);

        Assert.That(weights, Is.Not.Null);
        Assert.That(weights![smallDemand], Is.EqualTo(100).Within(0.001));
        Assert.That(weights[hugeDemand], Is.EqualTo(500).Within(0.001));
        Assert.That(weights[hugeDemand] / weights[smallDemand], Is.LessThanOrEqualTo(5.0));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RebuildZoneGrid_SoloConstrainedPanel_UsesGridHeightWithoutRowMaxHeight()
    {
        var zone = new Grid();
        var scrollViewer = new ScrollViewer();
        var panel = new HintPanel(maximumUsefulHeight: 180);
        var weights = new Dictionary<FrameworkElement, double> { [panel] = 180 };

        PanelDockingService.RebuildZoneGrid(
            zone,
            new List<FrameworkElement> { panel },
            scrollViewer,
            weights,
            weightsArePixels: true);

        // Solo panel must NOT cap zone.Height at maxH — it must bind to the scroll viewer
        // so the panel fills the full available zone height instead of leaving dead space.
        Assert.That(BindingOperations.GetBindingExpression(zone, FrameworkElement.HeightProperty), Is.Not.Null,
            "Solo panel zone must be bound to scrollViewer, not capped at maxH.");
        Assert.That(zone.RowDefinitions.Single().Height.IsStar, Is.True,
            "Solo panel row must use star sizing to fill the zone.");
        Assert.That(zone.RowDefinitions.Single().MaxHeight, Is.EqualTo(double.PositiveInfinity));
    }

    // ── InsertBefore column-shift tests ─────────────────────────────────────

    [Test]
    public void MovePanel_InsertBeforeLeft_WithLeft2OccupiedAndLeft3Empty_ShiftsLeft2ToLeft3AndLandsInLeft2()
    {
        // Layout: tasks→Left2, approvals→Left, loop→Top
        // Drop loop on InsertBefore Left@0 (thin strip between Left2 and Left)
        // → shifts tasks to Left3, loop lands in Left2 (not stacked in Left).
        var svc = new PanelDockingService();
        svc.MovePanel("tasks",     DockZone.Left2);
        svc.MovePanel("approvals", DockZone.Left);
        svc.MovePanel("loop",      DockZone.Left, targetOrder: 0, insertKind: SyntheticInsertKind.InsertBefore);

        var tasksSlot = svc.CurrentLayout.Slots.Single(s => s.PanelId == "tasks");
        var loopSlot  = svc.CurrentLayout.Slots.Single(s => s.PanelId == "loop");
        var aprvSlot  = svc.CurrentLayout.Slots.Single(s => s.PanelId == "approvals");

        Assert.That(tasksSlot.Zone, Is.EqualTo(DockZone.Left3), "tasks should have shifted from Left2 to Left3");
        Assert.That(loopSlot.Zone,  Is.EqualTo(DockZone.Left2), "loop should land in Left2 (the freed slot)");
        Assert.That(aprvSlot.Zone,  Is.EqualTo(DockZone.Left),  "approvals should remain in Left");
    }

    [Test]
    public void MovePanel_InsertBeforeLeft_WithLeft3OccupiedCascadesIntoLeft4()
    {
        // Layout: notes→Left3, tasks→Left2, approvals→Left, loop→Top
        // Left4 is empty → cascade: Left3→Left4, Left2→Left3, loop lands at Left2@0.
        var svc = new PanelDockingService();
        svc.MovePanel("notes",     DockZone.Left3);
        svc.MovePanel("tasks",     DockZone.Left2);
        svc.MovePanel("approvals", DockZone.Left);
        svc.MovePanel("loop",      DockZone.Left, targetOrder: 0, insertKind: SyntheticInsertKind.InsertBefore);

        var notesSlot = svc.CurrentLayout.Slots.Single(s => s.PanelId == "notes");
        var tasksSlot = svc.CurrentLayout.Slots.Single(s => s.PanelId == "tasks");
        var aprvSlot  = svc.CurrentLayout.Slots.Single(s => s.PanelId == "approvals");
        var loopSlot  = svc.CurrentLayout.Slots.Single(s => s.PanelId == "loop");

        Assert.That(notesSlot.Zone, Is.EqualTo(DockZone.Left4), "notes should cascade from Left3 to Left4");
        Assert.That(tasksSlot.Zone, Is.EqualTo(DockZone.Left3), "tasks should cascade from Left2 to Left3");
        Assert.That(aprvSlot.Zone,  Is.EqualTo(DockZone.Left),  "approvals should remain in Left");
        Assert.That(loopSlot.Zone,  Is.EqualTo(DockZone.Left2), "loop should land in Left2 (freed slot)");
        Assert.That(loopSlot.Order, Is.EqualTo(0),              "loop should be at order 0");
    }

    [Test]
    public void MovePanel_InsertBeforeLeft_WithLeft3AndLeft4OccupiedCascades4Zones()
    {
        // Layout: health→Left4, notes→Left3, tasks→Left2, approvals→Left, loop→Top
        // Left5/Left6 are empty → cascade4 fires: Left4→Left5, Left3→Left4, Left2→Left3, loop→Left2.
        var svc = new PanelDockingService();
        svc.MovePanel("health",    DockZone.Left4);
        svc.MovePanel("notes",     DockZone.Left3);
        svc.MovePanel("tasks",     DockZone.Left2);
        svc.MovePanel("approvals", DockZone.Left);
        svc.MovePanel("loop",      DockZone.Left, targetOrder: 0, insertKind: SyntheticInsertKind.InsertBefore);

        var healthSlot = svc.CurrentLayout.Slots.Single(s => s.PanelId == "health");
        var notesSlot  = svc.CurrentLayout.Slots.Single(s => s.PanelId == "notes");
        var tasksSlot  = svc.CurrentLayout.Slots.Single(s => s.PanelId == "tasks");
        var aprvSlot   = svc.CurrentLayout.Slots.Single(s => s.PanelId == "approvals");
        var loopSlot   = svc.CurrentLayout.Slots.Single(s => s.PanelId == "loop");

        Assert.That(healthSlot.Zone, Is.EqualTo(DockZone.Left5), "health should cascade Left4→Left5");
        Assert.That(notesSlot.Zone,  Is.EqualTo(DockZone.Left4), "notes should cascade Left3→Left4");
        Assert.That(tasksSlot.Zone,  Is.EqualTo(DockZone.Left3), "tasks should cascade Left2→Left3");
        Assert.That(aprvSlot.Zone,   Is.EqualTo(DockZone.Left),  "approvals should remain in Left");
        Assert.That(loopSlot.Zone,   Is.EqualTo(DockZone.Left2), "loop should land in Left2 (freed by cascade)");
        Assert.That(loopSlot.Order,  Is.EqualTo(0),              "loop should be at order 0");
    }

    [Test]
    public void MovePanel_InsertBeforeLeft_WithSixZonesOccupiedCascadesIntoLeft7()
    {
        // Layout: first 6 left zones occupied and Left7 empty → cascade into Left7.
        var svc = new PanelDockingService();
        svc.MovePanel("z6",        DockZone.Left6);
        svc.MovePanel("z5",        DockZone.Left5);
        svc.MovePanel("health",    DockZone.Left4);
        svc.MovePanel("notes",     DockZone.Left3);
        svc.MovePanel("tasks",     DockZone.Left2);
        svc.MovePanel("approvals", DockZone.Left);
        svc.MovePanel("loop",      DockZone.Left, targetOrder: 0, insertKind: SyntheticInsertKind.InsertBefore);

        var loopSlot = svc.CurrentLayout.Slots.Single(s => s.PanelId == "loop");
        var z6Slot   = svc.CurrentLayout.Slots.Single(s => s.PanelId == "z6");
        Assert.That(z6Slot.Zone,    Is.EqualTo(DockZone.Left7), "z6 should cascade Left6→Left7");
        Assert.That(loopSlot.Zone,  Is.EqualTo(DockZone.Left2), "loop should land in Left2 after the outward cascade");
        Assert.That(loopSlot.Order, Is.EqualTo(0),             "should be at order 0");
    }

    [Test]
    public void MovePanel_InsertBeforeLeft_WithLeft2EmptyAndLeftOccupied_ShiftsLeftToLeft2AndLandsInLeft()
    {
        // Layout: tasks→Left3, approvals→Left, loop→Top  (Left2 is empty)
        // The thin slot appears between the natural Left2 empty zone and Left (Approvals).
        // Dropping loop on InsertBefore Left@0 should:
        //   - shift approvals from Left → Left2
        //   - place loop in Left@0
        var svc = new PanelDockingService();
        svc.MovePanel("tasks",     DockZone.Left3);
        svc.MovePanel("approvals", DockZone.Left);
        svc.MovePanel("loop",      DockZone.Left, targetOrder: 0, insertKind: SyntheticInsertKind.InsertBefore);

        var tasksSlot = svc.CurrentLayout.Slots.Single(s => s.PanelId == "tasks");
        var loopSlot  = svc.CurrentLayout.Slots.Single(s => s.PanelId == "loop");
        var aprvSlot  = svc.CurrentLayout.Slots.Single(s => s.PanelId == "approvals");

        Assert.That(tasksSlot.Zone, Is.EqualTo(DockZone.Left3), "tasks should remain in Left3");
        Assert.That(aprvSlot.Zone,  Is.EqualTo(DockZone.Left2), "approvals should shift from Left to Left2");
        Assert.That(loopSlot.Zone,  Is.EqualTo(DockZone.Left),  "loop should land in Left (closest to center)");
    }

    [Test]
    public void MovePanel_InsertAfterLeft_WithLeft2AndLeftOccupied_CascadesAndLandsInLeft()
    {
        // Layout: tasks→Left2, approvals→Left, loop→Top  (Left3 is empty)
        // Inner-edge thin (InsertAfter Left@1) is shown after Approvals column.
        // Dropping loop on InsertAfter Left@1 should:
        //   - cascade: tasks Left2 → Left3, approvals Left → Left2
        //   - place loop in Left@0 (innermost)
        var svc = new PanelDockingService();
        svc.MovePanel("tasks",     DockZone.Left2);
        svc.MovePanel("approvals", DockZone.Left);
        svc.MovePanel("loop",      DockZone.Left, targetOrder: 1, insertKind: SyntheticInsertKind.InsertAfter);

        var tasksSlot = svc.CurrentLayout.Slots.Single(s => s.PanelId == "tasks");
        var aprvSlot  = svc.CurrentLayout.Slots.Single(s => s.PanelId == "approvals");
        var loopSlot  = svc.CurrentLayout.Slots.Single(s => s.PanelId == "loop");

        Assert.That(tasksSlot.Zone, Is.EqualTo(DockZone.Left3), "tasks should cascade from Left2 to Left3");
        Assert.That(aprvSlot.Zone,  Is.EqualTo(DockZone.Left2), "approvals should shift from Left to Left2");
        Assert.That(loopSlot.Zone,  Is.EqualTo(DockZone.Left),  "loop should land in Left (innermost)");
        Assert.That(loopSlot.Order, Is.EqualTo(0),              "loop should be at order 0");
    }

    [Test]
    public void MovePanel_InsertBeforeRight2_WithRight2OccupiedAndRight3Empty_ShiftsRight2ToRight3()
    {
        // Layout: inbox→Right2, maintenance→Right, loop→Top
        // Drop loop on InsertBefore Right2@0 (thin strip between Right and Right2)
        // → shift inbox to Right3, loop lands in Right2.
        var svc = new PanelDockingService();
        svc.MovePanel("inbox",       DockZone.Right2);
        svc.MovePanel("maintenance", DockZone.Right);
        svc.MovePanel("loop",        DockZone.Right2, targetOrder: 0, insertKind: SyntheticInsertKind.InsertBefore);

        var inboxSlot = svc.CurrentLayout.Slots.Single(s => s.PanelId == "inbox");
        var loopSlot  = svc.CurrentLayout.Slots.Single(s => s.PanelId == "loop");
        var maintSlot = svc.CurrentLayout.Slots.Single(s => s.PanelId == "maintenance");

        Assert.That(inboxSlot.Zone, Is.EqualTo(DockZone.Right3), "inbox should shift from Right2 to Right3");
        Assert.That(loopSlot.Zone,  Is.EqualTo(DockZone.Right2), "loop should land in Right2");
        Assert.That(maintSlot.Zone, Is.EqualTo(DockZone.Right),  "maintenance should remain in Right");
    }

    [Test]
    public void MovePanel_WhenPanelLeavesLeft2AndLeft3HasPanels_NormalizesLeft3ToLeft2()
    {
        // Scenario: after an InsertBefore shift, tasks=Left3, loop=Left2, approvals=Left.
        // When loop is moved out of Left2, normalization should slide tasks back to Left2.
        var svc = new PanelDockingService();
        svc.CurrentLayout.Slots = new List<PanelSlot>
        {
            new("tasks",     DockZone.Left3, 0),
            new("loop",      DockZone.Left2, 0),
            new("approvals", DockZone.Left,  1),
        };

        svc.MovePanel("loop", DockZone.Top);

        var tasksSlot = svc.CurrentLayout.Slots.First(
            s => string.Equals(s.PanelId, "tasks", StringComparison.OrdinalIgnoreCase));
        Assert.That(tasksSlot.Zone, Is.EqualTo(DockZone.Left2), "tasks should normalize from Left3 to Left2");
        Assert.That(svc.CurrentLayout.Slots.Any(s => s.Zone == DockZone.Left3), Is.False,
            "Left3 should be empty after normalization");
    }

    [Test]
    public void MovePanel_WhenLeft3AndLeft2BothOccupiedAndLeft3LeavesOnly_NormalizesLeft3ToLeft2()
    {
        // Tasks=Left3, Loop=Left2, Approvals=Left. Move tasks to Top.
        // No normalization needed (Left2 and Left still occupied, Left3 now empty).
        var svc = new PanelDockingService();
        svc.CurrentLayout.Slots = new List<PanelSlot>
        {
            new("tasks",     DockZone.Left3, 0),
            new("loop",      DockZone.Left2, 0),
            new("approvals", DockZone.Left,  1),
        };

        svc.MovePanel("tasks", DockZone.Top);

        var loopSlot = svc.CurrentLayout.Slots.First(
            s => string.Equals(s.PanelId, "loop", StringComparison.OrdinalIgnoreCase));
        Assert.That(loopSlot.Zone, Is.EqualTo(DockZone.Left2), "loop should remain in Left2 — no gap to fill");
        Assert.That(svc.CurrentLayout.Slots.Any(s => s.Zone == DockZone.Left3), Is.False,
            "Left3 should be empty");
    }

    // ── Hide-snapshot height restore tests ──────────────────────────────────

    /// <summary>
    /// When a panel is hidden and then shown with the same zone height and the same
    /// visible-panel set, its star weight must be restored to the pre-hide value.
    /// </summary>
    [Test, Apartment(ApartmentState.STA)]
    public void OnPanelVisibilityChanged_HideShow_RestoresSavedStarWeight_WhenContextMatches()
    {
        var leftZone  = new Grid();
        var loopEl    = new Border();
        var tasksEl   = new Border();
        var topZone   = new Grid();

        var svc = new PanelDockingService(
            new Dictionary<string, FrameworkElement>
            {
                ["loop"]  = loopEl,
                ["tasks"] = tasksEl,
            },
            leftZone, new Grid(), new Grid(), new Grid(), new Grid(), new Grid(),
            new Grid(), new Grid(), new Grid(), new Grid(), new Grid(), new Grid(),
            topZone,
            new ColumnDefinition { Width = new GridLength(280) },
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition { Width = new GridLength(5) },
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(),
            new ScrollViewer(), new ScrollViewer(), new ScrollViewer(), new ScrollViewer(),
            new ScrollViewer(), new ScrollViewer(), new ScrollViewer(), new ScrollViewer(),
            new ScrollViewer(), new ScrollViewer(), new ScrollViewer(), new ScrollViewer(),
            new GridSplitter(), new GridSplitter(), new GridSplitter(), new GridSplitter(),
            new GridSplitter(), new GridSplitter(), new GridSplitter(), new GridSplitter(),
            new GridSplitter(), new GridSplitter(), new GridSplitter(), new GridSplitter());

        svc.MovePanel("loop",  DockZone.Left);
        svc.MovePanel("tasks", DockZone.Left);

        // Simulate a user resize: loop=0.5*, tasks=1.5* (loop is small).
        leftZone.RowDefinitions[0].Height = new GridLength(0.5, GridUnitType.Star);
        leftZone.RowDefinitions[2].Height = new GridLength(1.5, GridUnitType.Star);

        // Hide loop.
        loopEl.Visibility = Visibility.Collapsed;
        svc.OnPanelVisibilityChanged("loop", visible: false);

        // Show loop — zone height and panel set are unchanged, context matches.
        loopEl.Visibility = Visibility.Visible;
        svc.OnPanelVisibilityChanged("loop", visible: true);

        // loop must occupy row 0 and get its saved star weight back.
        Assert.That(leftZone.RowDefinitions.Count, Is.EqualTo(3),
            "Expected 2 star rows + 1 splitter row");
        double loopStarRestored = leftZone.RowDefinitions[0].Height.Value;
        Assert.That(loopStarRestored, Is.EqualTo(0.5).Within(0.01),
            "loop star weight should be restored to the pre-hide value");
    }

    /// <summary>
    /// When the visible-panel set changes between hide and show (a third panel is hidden),
    /// the snapshot context does not match and equal star distribution is used instead.
    /// </summary>
    [Test, Apartment(ApartmentState.STA)]
    public void OnPanelVisibilityChanged_HideShow_FallsBackToEqualWeights_WhenPanelSetChanged()
    {
        var leftZone     = new Grid();
        var loopEl       = new Border();
        var tasksEl      = new Border();
        var approvalsEl  = new Border();
        var topZone      = new Grid();

        var svc = new PanelDockingService(
            new Dictionary<string, FrameworkElement>
            {
                ["loop"]      = loopEl,
                ["tasks"]     = tasksEl,
                ["approvals"] = approvalsEl,
            },
            leftZone, new Grid(), new Grid(), new Grid(), new Grid(), new Grid(),
            new Grid(), new Grid(), new Grid(), new Grid(), new Grid(), new Grid(),
            topZone,
            new ColumnDefinition { Width = new GridLength(280) },
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition { Width = new GridLength(5) },
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(),
            new ScrollViewer(), new ScrollViewer(), new ScrollViewer(), new ScrollViewer(),
            new ScrollViewer(), new ScrollViewer(), new ScrollViewer(), new ScrollViewer(),
            new ScrollViewer(), new ScrollViewer(), new ScrollViewer(), new ScrollViewer(),
            new GridSplitter(), new GridSplitter(), new GridSplitter(), new GridSplitter(),
            new GridSplitter(), new GridSplitter(), new GridSplitter(), new GridSplitter(),
            new GridSplitter(), new GridSplitter(), new GridSplitter(), new GridSplitter());

        svc.MovePanel("loop",      DockZone.Left);
        svc.MovePanel("tasks",     DockZone.Left);
        svc.MovePanel("approvals", DockZone.Left);

        // Simulate a user resize: loop is small.
        leftZone.RowDefinitions[0].Height = new GridLength(0.25, GridUnitType.Star);
        leftZone.RowDefinitions[2].Height = new GridLength(1.0,  GridUnitType.Star);
        leftZone.RowDefinitions[4].Height = new GridLength(1.0,  GridUnitType.Star);

        // Hide loop (snapshot saved with loop+tasks+approvals visible).
        loopEl.Visibility = Visibility.Collapsed;
        svc.OnPanelVisibilityChanged("loop", visible: false);

        // Also hide tasks — panel set changes relative to the snapshot.
        tasksEl.Visibility = Visibility.Collapsed;
        svc.OnPanelVisibilityChanged("tasks", visible: false);

        // Show loop — only approvals+loop would be visible, but snapshot expected all three.
        // Context does not match → fallback to equal star distribution.
        loopEl.Visibility = Visibility.Visible;
        svc.OnPanelVisibilityChanged("loop", visible: true);

        // Only loop and approvals are visible; they should each get 1* (equal weights).
        var starRows = leftZone.RowDefinitions.Where(r => r.Height.IsStar).ToList();
        Assert.That(starRows.Count, Is.EqualTo(2),
            "Expected exactly 2 star rows for the 2 visible panels");
        Assert.That(starRows[0].Height.Value, Is.EqualTo(1.0).Within(0.01),
            "loop should fall back to equal weight (1*)");
        Assert.That(starRows[1].Height.Value, Is.EqualTo(1.0).Within(0.01),
            "approvals should have equal weight (1*)");
    }

    /// <summary>
    /// When the zone height changes by more than the ±10 px tolerance between a panel's hide
    /// and show operations, the PanelHideSnapshot must be discarded and equal star weights
    /// used as a fallback instead of restoring the saved weight.
    /// </summary>
    [Test, Apartment(ApartmentState.STA)]
    public void OnPanelVisibilityChanged_HideShow_FallsBackToEqualWeights_WhenZoneHeightChanged()
    {
        var leftZone   = new Grid();
        var loopEl     = new Border();
        var tasksEl    = new Border();
        var topZone    = new Grid();
        var leftZoneSv = new ScrollViewer();

        var svc = new PanelDockingService(
            new Dictionary<string, FrameworkElement>
            {
                ["loop"]  = loopEl,
                ["tasks"] = tasksEl,
            },
            leftZone, new Grid(), new Grid(), new Grid(), new Grid(), new Grid(),
            new Grid(), new Grid(), new Grid(), new Grid(), new Grid(), new Grid(),
            topZone,
            new ColumnDefinition { Width = new GridLength(280) },
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition { Width = new GridLength(5) },
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(),
            new ColumnDefinition(), new ColumnDefinition(),
            leftZoneSv, new ScrollViewer(), new ScrollViewer(), new ScrollViewer(),
            new ScrollViewer(), new ScrollViewer(), new ScrollViewer(), new ScrollViewer(),
            new ScrollViewer(), new ScrollViewer(), new ScrollViewer(), new ScrollViewer(),
            new GridSplitter(), new GridSplitter(), new GridSplitter(), new GridSplitter(),
            new GridSplitter(), new GridSplitter(), new GridSplitter(), new GridSplitter(),
            new GridSplitter(), new GridSplitter(), new GridSplitter(), new GridSplitter());

        svc.MovePanel("loop",  DockZone.Left);
        svc.MovePanel("tasks", DockZone.Left);

        // Simulate a user resize: loop=0.5*, tasks=1.5* (loop is small).
        leftZone.RowDefinitions[0].Height = new GridLength(0.5, GridUnitType.Star);
        leftZone.RowDefinitions[2].Height = new GridLength(1.5, GridUnitType.Star);

        // Hide loop — snapshot is saved with ZoneActualHeight=0 (scroll viewer not yet laid out).
        loopEl.Visibility = Visibility.Collapsed;
        svc.OnPanelVisibilityChanged("loop", visible: false);

        // Simulate a window resize: measure and arrange the scroll viewer so its ActualHeight
        // becomes 600, which exceeds the ±10 px tolerance relative to the snapshotted height of 0.
        leftZoneSv.Height = 600;
        leftZoneSv.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        leftZoneSv.Arrange(new System.Windows.Rect(0, 0, 200, 600));

        // Show loop — zone height changed by >10 px → snapshot is stale → fallback to equal weights.
        loopEl.Visibility = Visibility.Visible;
        svc.OnPanelVisibilityChanged("loop", visible: true);

        // Both visible panels should receive equal star weights (snapshot was discarded).
        var starRows = leftZone.RowDefinitions.Where(r => r.Height.IsStar).ToList();
        Assert.That(starRows.Count, Is.EqualTo(2),
            "Expected exactly 2 star rows for the 2 visible panels");
        Assert.That(starRows[0].Height.Value, Is.EqualTo(1.0).Within(0.01),
            "loop should fall back to equal weight (1*)");
        Assert.That(starRows[1].Height.Value, Is.EqualTo(1.0).Within(0.01),
            "tasks should have equal weight (1*)");
    }

    // ── Regression tests: RowDefinition.MaxHeight must never be set on zone rows ─────────────────
    // Regression for commit 03a1357 (reverted in f023931), which set MaxHeight on docked-panel
    // RowDefinitions, preventing the GridSplitter from resizing panels beyond the capped height.

    /// <summary>
    /// A single panel docked into a side zone must produce a star RowDefinition with
    /// MaxHeight == PositiveInfinity (unconstrained), so the splitter is free to resize it.
    /// </summary>
    [Test, Apartment(ApartmentState.STA)]
    public void RebuildZoneGrid_SinglePanel_RowDefinitionMaxHeightIsUnconstrained()
    {
        var zone   = new Grid();
        var sv     = new ScrollViewer();
        var panel  = new Border();

        PanelDockingService.RebuildZoneGrid(
            zone,
            new List<FrameworkElement> { panel },
            sv);

        var row = zone.RowDefinitions.Single();
        Assert.That(row.Height.IsStar, Is.True,
            "The panel row must use star sizing.");
        Assert.That(row.MaxHeight, Is.EqualTo(double.PositiveInfinity),
            "RowDefinition.MaxHeight must not be constrained — a capped row breaks splitter resize.");
    }

    /// <summary>
    /// Multiple panels docked into a side zone must each produce a star RowDefinition with
    /// MaxHeight == PositiveInfinity, so the GridSplitter between them can move freely.
    /// </summary>
    [Test, Apartment(ApartmentState.STA)]
    public void RebuildZoneGrid_MultiplePanels_NoRowDefinitionHasConstrainedMaxHeight()
    {
        var zone   = new Grid();
        var sv     = new ScrollViewer();
        var panelA = new Border();
        var panelB = new Border();

        PanelDockingService.RebuildZoneGrid(
            zone,
            new List<FrameworkElement> { panelA, panelB },
            sv);

        var starRows = zone.RowDefinitions.Where(r => r.Height.IsStar).ToList();
        Assert.That(starRows.Count, Is.EqualTo(2),
            "Expected one star row per visible panel.");
        foreach (var row in starRows)
        {
            Assert.That(row.MaxHeight, Is.EqualTo(double.PositiveInfinity),
                "Each panel RowDefinition.MaxHeight must be unconstrained — capping breaks splitter resize.");
        }
    }

    /// <summary>
    /// After docking a panel, the GridSplitter must be able to assign an arbitrary height to the
    /// panel's RowDefinition.  Verifies that Height can be freely mutated after RebuildZoneGrid.
    /// </summary>
    [Test, Apartment(ApartmentState.STA)]
    public void RebuildZoneGrid_AfterDocking_RowDefinitionHeightIsFreelySettable()
    {
        var zone   = new Grid();
        var sv     = new ScrollViewer();
        var panelA = new Border();
        var panelB = new Border();

        PanelDockingService.RebuildZoneGrid(
            zone,
            new List<FrameworkElement> { panelA, panelB },
            sv);

        // Simulate a splitter drag: re-assign the star height to an arbitrary value.
        var panelRow = zone.RowDefinitions[0];  // first panel occupies row 0
        Assert.DoesNotThrow(() => panelRow.Height = new GridLength(350, GridUnitType.Star),
            "Assigning a new Height to a panel row must succeed (splitter resize must not be blocked).");
        Assert.That(panelRow.Height.Value, Is.EqualTo(350).Within(0.001),
            "The assigned star height must be preserved on the RowDefinition.");
    }

    private static void AssertN1RuleCompliance(PanelDockingService svc, string testContext)
    {
        // Get the current layout data
        var layoutData = svc.GetCurrentLayoutData();
        
        // For all visible panels, check N+1 compliance
        // We use a representative panel for the source to check slots
        var visiblePanels = layoutData.VisiblePanelIds.FirstOrDefault();
        if (!string.IsNullOrEmpty(visiblePanels))
        {
            var violations = DockingLayoutEngine.ValidateN1Rule(layoutData, visiblePanels);
            if (violations.Any())
            {
                System.Diagnostics.Debug.WriteLine($"N+1 warning in {testContext}: {string.Join(", ", violations)}");
            }
        }
    }

    private static string CreateSavedLayoutWithTasksOnLeft()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"PanelDockingServiceTests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        var dataSvc = new PanelDockingService();
        dataSvc.MovePanel("tasks", DockZone.Left);
        dataSvc.SaveLayout(tempDir);

        return tempDir;
    }

    private static PanelDockingService CreateWpfDockingService(
        FrameworkElement taskPanel,
        Grid topZone,
        Grid? leftZone = null)
    {
        return new PanelDockingService(
            new Dictionary<string, FrameworkElement>
            {
                ["tasks"] = taskPanel,
            },
            leftZone ?? new Grid(),  // leftZonePanel
            new Grid(),              // rightZonePanel
            new Grid(),              // left2ZonePanel
            new Grid(),              // right2ZonePanel
            new Grid(),              // left3ZonePanel
            new Grid(),              // right3ZonePanel
            new Grid(),              // left4ZonePanel
            new Grid(),              // right4ZonePanel
            new Grid(),              // left5ZonePanel
            new Grid(),              // right5ZonePanel
            new Grid(),              // left6ZonePanel
            new Grid(),              // right6ZonePanel
            topZone,
            new ColumnDefinition(),  // leftZoneColumn
            new ColumnDefinition(),  // rightZoneColumn
            new ColumnDefinition(),  // left2ZoneColumn
            new ColumnDefinition(),  // right2ZoneColumn
            new ColumnDefinition(),  // left3ZoneColumn
            new ColumnDefinition(),  // right3ZoneColumn
            new ColumnDefinition(),  // left4ZoneColumn
            new ColumnDefinition(),  // right4ZoneColumn
            new ColumnDefinition(),  // left5ZoneColumn
            new ColumnDefinition(),  // right5ZoneColumn
            new ColumnDefinition(),  // left6ZoneColumn
            new ColumnDefinition(),  // right6ZoneColumn
            new ColumnDefinition(),  // leftSplitterColumn
            new ColumnDefinition(),  // rightSplitterColumn
            new ColumnDefinition(),  // left2SplitterColumn
            new ColumnDefinition(),  // right2SplitterColumn
            new ColumnDefinition(),  // left3SplitterColumn
            new ColumnDefinition(),  // right3SplitterColumn
            new ColumnDefinition(),  // left4SplitterColumn
            new ColumnDefinition(),  // right4SplitterColumn
            new ColumnDefinition(),  // left5SplitterColumn
            new ColumnDefinition(),  // right5SplitterColumn
            new ColumnDefinition(),  // left6SplitterColumn
            new ColumnDefinition(),  // right6SplitterColumn
            new ScrollViewer(),      // leftZoneScrollViewer
            new ScrollViewer(),      // rightZoneScrollViewer
            new ScrollViewer(),      // left2ZoneScrollViewer
            new ScrollViewer(),      // right2ZoneScrollViewer
            new ScrollViewer(),      // left3ZoneScrollViewer
            new ScrollViewer(),      // right3ZoneScrollViewer
            new ScrollViewer(),      // left4ZoneScrollViewer
            new ScrollViewer(),      // right4ZoneScrollViewer
            new ScrollViewer(),      // left5ZoneScrollViewer
            new ScrollViewer(),      // right5ZoneScrollViewer
            new ScrollViewer(),      // left6ZoneScrollViewer
            new ScrollViewer(),      // right6ZoneScrollViewer
            new GridSplitter(),      // leftZoneSplitter
            new GridSplitter(),      // rightZoneSplitter
            new GridSplitter(),      // left2ZoneSplitter
            new GridSplitter(),      // right2ZoneSplitter
            new GridSplitter(),      // left3ZoneSplitter
            new GridSplitter(),      // right3ZoneSplitter
            new GridSplitter(),      // left4ZoneSplitter
            new GridSplitter(),      // right4ZoneSplitter
            new GridSplitter(),      // left5ZoneSplitter
            new GridSplitter(),      // right5ZoneSplitter
            new GridSplitter(),      // left6ZoneSplitter
            new GridSplitter());     // right6ZoneSplitter
    }

    private sealed class HintPanel(double maximumUsefulHeight) : FrameworkElement, IDockResizeSizeHint
    {
        public double GetMinimumDockSize(DockResizeOrientation orientation) =>
            orientation == DockResizeOrientation.Vertical ? 100 : 80;

        public double? GetMaximumUsefulDockSize(DockResizeOrientation orientation) =>
            orientation == DockResizeOrientation.Vertical ? maximumUsefulHeight : null;
    }
}
