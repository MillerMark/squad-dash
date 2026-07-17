#nullable enable

using System.IO;
using System.Text.Json;
using SquadDash.PanelDocking;

namespace SquadDash.Tests;

/// <summary>
/// Regression tests for the View → Store Layout / Restore Layout feature
/// (implemented via <see cref="LayoutPresetManager"/>).
///
/// Regression: panels that were open when a layout was saved did not
/// reappear when Restore Layout was invoked.  These tests guard the
/// preset persistence layer — slot placements, zone widths and the
/// full save→reload round-trip — so that the underlying data contract
/// never silently regresses.
/// </summary>
[TestFixture]
internal sealed class LayoutPresetManagerTests
{
    private string _workspacePath = null!;

    [SetUp]
    public void SetUp()
    {
        _workspacePath = Path.Combine(
            Path.GetTempPath(),
            $"LayoutPresetTests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_workspacePath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_workspacePath))
            Directory.Delete(_workspacePath, recursive: true);
    }

    // ── initialization ────────────────────────────────────────────────────────

    [Test]
    public void HasPreset_BeforeSavingAnything_ReturnsFalse_ForAllSlots()
    {
        var mgr = CreateInitialized();

        for (int i = 0; i < 3; i++)
            Assert.That(mgr.HasPreset(i), Is.False, $"Slot {i} should be empty initially");
    }

    [Test]
    public void GetPreset_BeforeSavingAnything_ReturnsNull()
    {
        var mgr = CreateInitialized();

        for (int i = 0; i < 3; i++)
            Assert.That(mgr.GetPreset(i), Is.Null, $"Slot {i} should return null before any save");
    }

    [Test]
    public void Initialize_MissingPresetsFile_StartsWithNoPresets()
    {
        // workspace exists but .squad folder and presets file do not
        var mgr = new LayoutPresetManager();
        mgr.Initialize(_workspacePath);

        Assert.That(mgr.HasPreset(0), Is.False);
    }

    [Test]
    public void Initialize_CorruptedPresetsFile_StartsWithNoPresets()
    {
        var squadDir = Path.Combine(_workspacePath, ".squad");
        Directory.CreateDirectory(squadDir);
        File.WriteAllText(
            Path.Combine(squadDir, "panel-layout-presets.json"),
            "{ this is not valid JSON !!! }");

        var mgr = new LayoutPresetManager();
        mgr.Initialize(_workspacePath);

        // Should not throw; corrupted file is treated as empty
        Assert.That(mgr.HasPreset(0), Is.False);
    }

    // ── out-of-range guards ───────────────────────────────────────────────────

    [Test]
    public void GetPreset_NegativeIndex_ReturnsNull()
    {
        var mgr = CreateInitialized();
        Assert.That(mgr.GetPreset(-1), Is.Null);
    }

    [Test]
    public void GetPreset_IndexTooHigh_ReturnsNull()
    {
        var mgr = CreateInitialized();
        Assert.That(mgr.GetPreset(3), Is.Null);
    }

    [Test]
    public void HasPreset_NegativeIndex_ReturnsFalse()
    {
        var mgr = CreateInitialized();
        Assert.That(mgr.HasPreset(-1), Is.False);
    }

    [Test]
    public void SavePreset_NegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        var mgr = CreateInitialized();
        Assert.That(
            () => mgr.SavePreset(-1, DockLayout.CreateDefault()),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void SavePreset_IndexTooHigh_ThrowsArgumentOutOfRangeException()
    {
        var mgr = CreateInitialized();
        Assert.That(
            () => mgr.SavePreset(3, DockLayout.CreateDefault()),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void SavePreset_NotInitialized_ThrowsInvalidOperationException()
    {
        var mgr = new LayoutPresetManager(); // not initialized
        Assert.That(
            () => mgr.SavePreset(0, DockLayout.CreateDefault()),
            Throws.TypeOf<InvalidOperationException>());
    }

    // ── basic save / retrieve ─────────────────────────────────────────────────

    [Test]
    public void SavePreset_HasPreset_ReturnsTrueAfterSave()
    {
        var mgr = CreateInitialized();
        mgr.SavePreset(0, DockLayout.CreateDefault());
        Assert.That(mgr.HasPreset(0), Is.True);
    }

    [Test]
    public void SavePreset_Slot0_CanBeRetrievedInSameSession()
    {
        var mgr = CreateInitialized();
        var layout = BuildLayout("tasks", DockZone.Left, "inbox", DockZone.Right);

        mgr.SavePreset(0, layout);
        var retrieved = mgr.GetPreset(0);

        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.Slots.Select(s => s.PanelId),
            Is.SupersetOf(new[] { "tasks", "inbox" }));
    }

    [Test]
    public void SavePreset_AllThreeSlots_WorkIndependently()
    {
        var mgr = CreateInitialized();

        var layout0 = BuildLayout("tasks",       DockZone.Left);
        var layout1 = BuildLayout("inbox",       DockZone.Right);
        var layout2 = BuildLayout("maintenance", DockZone.Left2);

        mgr.SavePreset(0, layout0);
        mgr.SavePreset(1, layout1);
        mgr.SavePreset(2, layout2);

        Assert.That(SlotPanelZone(mgr.GetPreset(0), "tasks"),       Is.EqualTo(DockZone.Left));
        Assert.That(SlotPanelZone(mgr.GetPreset(1), "inbox"),       Is.EqualTo(DockZone.Right));
        Assert.That(SlotPanelZone(mgr.GetPreset(2), "maintenance"), Is.EqualTo(DockZone.Left2));
    }

    [Test]
    public void SavePreset_Overwrite_ReplacesExistingData()
    {
        var mgr = CreateInitialized();
        mgr.SavePreset(0, BuildLayout("tasks", DockZone.Left));

        // Overwrite slot 0 with a different layout
        mgr.SavePreset(0, BuildLayout("inbox", DockZone.Right));

        var retrieved = mgr.GetPreset(0);
        Assert.That(SlotPanelZone(retrieved, "inbox"), Is.EqualTo(DockZone.Right));
    }

    // ── slot data integrity ───────────────────────────────────────────────────

    [Test]
    public void SavePreset_WithMultiplePanelsInDifferentZones_AllSlotsPreserved()
    {
        // Regression probe: saving a layout with several open (side-zone) panels
        // must preserve every panel's placement so Restore Layout can re-open them.
        var mgr = CreateInitialized();

        var svc = new PanelDockingService();
        svc.MovePanel("tasks",       DockZone.Left);
        svc.MovePanel("inbox",       DockZone.Right);
        svc.MovePanel("maintenance", DockZone.Left2);

        mgr.SavePreset(0, svc.CurrentLayout);

        var preset = mgr.GetPreset(0)!;
        Assert.That(SlotPanelZone(preset, "tasks"),       Is.EqualTo(DockZone.Left));
        Assert.That(SlotPanelZone(preset, "inbox"),       Is.EqualTo(DockZone.Right));
        Assert.That(SlotPanelZone(preset, "maintenance"), Is.EqualTo(DockZone.Left2));
    }

    [Test]
    public void SavePreset_WithTopOnlyLayout_AllSlotsInTop()
    {
        var mgr = CreateInitialized();
        var layout = DockLayout.CreateDefault(); // all 7 panels in Top

        mgr.SavePreset(1, layout);

        var preset = mgr.GetPreset(1)!;
        Assert.That(preset.Slots, Has.Count.GreaterThan(0));
        Assert.That(preset.Slots.All(s => s.Zone == DockZone.Top), Is.True,
            "Default layout has every panel in Top zone");
    }

    [Test]
    public void SavePreset_SinglePanel_InSideZone_PreservedExactly()
    {
        var mgr = CreateInitialized();

        var svc = new PanelDockingService();
        svc.MovePanel("notes", DockZone.Right2);

        mgr.SavePreset(2, svc.CurrentLayout);

        var preset = mgr.GetPreset(2)!;
        var notesSlot = preset.Slots.Single(s => s.PanelId == "notes");
        Assert.That(notesSlot.Zone, Is.EqualTo(DockZone.Right2));
    }

    [Test]
    public void SavePreset_PanelOrder_IsPreserved()
    {
        var mgr = CreateInitialized();

        var svc = new PanelDockingService();
        svc.MovePanel("tasks",       DockZone.Left);
        svc.MovePanel("maintenance", DockZone.Left);

        var originalTasksOrder = svc.CurrentLayout.Slots
            .Single(s => s.PanelId == "tasks").Order;
        var originalMaintOrder = svc.CurrentLayout.Slots
            .Single(s => s.PanelId == "maintenance").Order;

        mgr.SavePreset(0, svc.CurrentLayout);

        var preset = mgr.GetPreset(0)!;
        Assert.That(preset.Slots.Single(s => s.PanelId == "tasks").Order,
            Is.EqualTo(originalTasksOrder), "tasks panel order should survive preset save");
        Assert.That(preset.Slots.Single(s => s.PanelId == "maintenance").Order,
            Is.EqualTo(originalMaintOrder), "maintenance panel order should survive preset save");
    }

    [Test]
    public void SavePreset_ZoneWidths_ArePreserved()
    {
        var mgr = CreateInitialized();

        var layout = DockLayout.CreateDefault();
        layout.LeftZoneWidth   = 280.0;
        layout.RightZoneWidth  = 340.0;
        layout.Left2ZoneWidth  = 260.0;
        layout.Right2ZoneWidth = 320.0;

        mgr.SavePreset(0, layout);

        var preset = mgr.GetPreset(0)!;
        Assert.That(preset.LeftZoneWidth,   Is.EqualTo(280.0));
        Assert.That(preset.RightZoneWidth,  Is.EqualTo(340.0));
        Assert.That(preset.Left2ZoneWidth,  Is.EqualTo(260.0));
        Assert.That(preset.Right2ZoneWidth, Is.EqualTo(320.0));
    }

    [Test]
    public void SavePreset_ClonesLayout_MutatingSourceDoesNotAffectStoredPreset()
    {
        var mgr = CreateInitialized();

        var layout = BuildLayout("tasks", DockZone.Left);
        mgr.SavePreset(0, layout);

        // Mutate the original layout after saving
        layout.Slots.Clear();

        var preset = mgr.GetPreset(0)!;
        Assert.That(preset.Slots, Is.Not.Empty,
            "Stored preset should not be affected by mutations to the source layout");
    }

    // ── file persistence round-trips ──────────────────────────────────────────

    [Test]
    public void SavePreset_WritesPresetsFile()
    {
        var mgr = CreateInitialized();
        mgr.SavePreset(0, DockLayout.CreateDefault());

        var expectedPath = Path.Combine(_workspacePath, ".squad", "panel-layout-presets.json");
        Assert.That(File.Exists(expectedPath), Is.True, "Presets file should be created on disk");
    }

    [Test]
    public void SavePreset_WritesValidJson()
    {
        var mgr = CreateInitialized();
        mgr.SavePreset(0, DockLayout.CreateDefault());

        var json = File.ReadAllText(
            Path.Combine(_workspacePath, ".squad", "panel-layout-presets.json"));
        Assert.That(() => JsonDocument.Parse(json), Throws.Nothing,
            "Presets file must be valid JSON");
    }

    [Test]
    public void RoundTrip_SaveThenReloadManager_PreservesSlotData()
    {
        // Regression probe: slot placements must survive a save→re-initialize cycle
        // (simulates the app being closed and reopened, or a Restore after restart).
        var svc = new PanelDockingService();
        svc.MovePanel("tasks",       DockZone.Left);
        svc.MovePanel("inbox",       DockZone.Right);
        svc.MovePanel("approvals",   DockZone.Left2);

        var mgr1 = CreateInitialized();
        mgr1.SavePreset(0, svc.CurrentLayout);

        // Second manager reads from the same workspace path (simulates reload).
        var mgr2 = new LayoutPresetManager();
        mgr2.Initialize(_workspacePath);

        Assert.That(mgr2.HasPreset(0), Is.True,
            "Preset saved by first manager should be visible to second manager");

        var preset = mgr2.GetPreset(0)!;
        Assert.That(SlotPanelZone(preset, "tasks"),     Is.EqualTo(DockZone.Left));
        Assert.That(SlotPanelZone(preset, "inbox"),     Is.EqualTo(DockZone.Right));
        Assert.That(SlotPanelZone(preset, "approvals"), Is.EqualTo(DockZone.Left2));
    }

    [Test]
    public void RoundTrip_SaveThenReloadManager_PreservesZoneWidths()
    {
        var layout = DockLayout.CreateDefault();
        layout.LeftZoneWidth  = 300.0;
        layout.RightZoneWidth = 360.0;

        var mgr1 = CreateInitialized();
        mgr1.SavePreset(1, layout);

        var mgr2 = new LayoutPresetManager();
        mgr2.Initialize(_workspacePath);

        var preset = mgr2.GetPreset(1)!;
        Assert.That(preset.LeftZoneWidth,  Is.EqualTo(300.0));
        Assert.That(preset.RightZoneWidth, Is.EqualTo(360.0));
    }

    [Test]
    public void RoundTrip_ThreeSlots_AllPreservedIndependently()
    {
        var mgr1 = CreateInitialized();

        var svc0 = new PanelDockingService();
        svc0.MovePanel("tasks", DockZone.Left);

        var svc1 = new PanelDockingService();
        svc1.MovePanel("inbox", DockZone.Right);

        var svc2 = new PanelDockingService();
        svc2.MovePanel("notes", DockZone.Left2);

        mgr1.SavePreset(0, svc0.CurrentLayout);
        mgr1.SavePreset(1, svc1.CurrentLayout);
        mgr1.SavePreset(2, svc2.CurrentLayout);

        var mgr2 = new LayoutPresetManager();
        mgr2.Initialize(_workspacePath);

        Assert.That(SlotPanelZone(mgr2.GetPreset(0), "tasks"), Is.EqualTo(DockZone.Left));
        Assert.That(SlotPanelZone(mgr2.GetPreset(1), "inbox"), Is.EqualTo(DockZone.Right));
        Assert.That(SlotPanelZone(mgr2.GetPreset(2), "notes"), Is.EqualTo(DockZone.Left2));
    }

    [Test]
    public void RoundTrip_EmptyLayout_RoundTripsWithoutErrors()
    {
        var mgr1 = CreateInitialized();
        var emptyLayout = new DockLayout { Name = "Empty" };

        mgr1.SavePreset(0, emptyLayout);

        var mgr2 = new LayoutPresetManager();
        mgr2.Initialize(_workspacePath);

        var preset = mgr2.GetPreset(0);
        Assert.That(preset, Is.Not.Null);
        Assert.That(preset!.Slots, Is.Empty);
    }

    // ── Restore-preset ↔ ApplyLayout contract ────────────────────────────────

    [Test]
    public void GetPreset_CanBePassedDirectlyToApplyLayout_WithoutError()
    {
        // Validates that the object returned by GetPreset is a valid DockLayout
        // accepted by PanelDockingService.ApplyLayout — the call chain used by
        // RestoreLayoutPreset in MainWindow.
        var mgr = CreateInitialized();

        var svc = new PanelDockingService();
        svc.MovePanel("tasks", DockZone.Left);
        svc.MovePanel("inbox", DockZone.Right);

        mgr.SavePreset(0, svc.CurrentLayout);

        var svc2 = new PanelDockingService();
        var preset = mgr.GetPreset(0)!;

        Assert.That(() => svc2.ApplyLayout(preset), Throws.Nothing,
            "GetPreset result must be directly usable by ApplyLayout");
    }

    [Test]
    public void ApplyLayout_AfterRestorePreset_CurrentLayoutMatchesSavedSlots()
    {
        // Regression guard: the full Store→Restore round-trip must result in
        // CurrentLayout reflecting the saved slot placements.
        var svc1 = new PanelDockingService();
        svc1.MovePanel("tasks",       DockZone.Left);
        svc1.MovePanel("inbox",       DockZone.Right);
        svc1.MovePanel("maintenance", DockZone.Left2);

        var mgr = CreateInitialized();
        mgr.SavePreset(0, svc1.CurrentLayout);

        // Simulate closing and reopening panels (move everything back to Top)
        var svc2 = new PanelDockingService();
        // svc2 starts in default (all Top) state

        // Restore the preset — mirrors RestoreLayoutPreset in MainWindow.xaml.cs
        var preset = mgr.GetPreset(0)!;
        svc2.ApplyLayout(preset);

        Assert.That(svc2.CurrentLayout.Slots.Single(s => s.PanelId == "tasks").Zone,
            Is.EqualTo(DockZone.Left),       "tasks should be restored to Left zone");
        Assert.That(svc2.CurrentLayout.Slots.Single(s => s.PanelId == "inbox").Zone,
            Is.EqualTo(DockZone.Right),      "inbox should be restored to Right zone");
        Assert.That(svc2.CurrentLayout.Slots.Single(s => s.PanelId == "maintenance").Zone,
            Is.EqualTo(DockZone.Left2),      "maintenance should be restored to Left2 zone");
    }

    [Test]
    public void SavePreset_NoPresetSaved_RestoreIsNoOp()
    {
        // When no preset has been saved for a slot, GetPreset returns null and
        // the UI can guard against calling ApplyLayout (HasPreset check).
        var mgr = CreateInitialized();

        Assert.That(mgr.GetPreset(0), Is.Null,
            "Retrieving an empty slot should return null — guard for graceful no-op restore");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private LayoutPresetManager CreateInitialized()
    {
        var mgr = new LayoutPresetManager();
        mgr.Initialize(_workspacePath);
        return mgr;
    }

    /// <summary>
    /// Builds a <see cref="DockLayout"/> that starts from the default and moves the
    /// specified (panelId, zone) pairs into their target zones.
    /// </summary>
    private static DockLayout BuildLayout(params (string panelId, DockZone zone)[] moves)
    {
        var svc = new PanelDockingService();
        foreach (var (panelId, zone) in moves)
            svc.MovePanel(panelId, zone);
        return svc.CurrentLayout;
    }

    private static DockLayout BuildLayout(string panelId1, DockZone zone1) =>
        BuildLayout((panelId1, zone1));

    private static DockLayout BuildLayout(
        string panelId1, DockZone zone1,
        string panelId2, DockZone zone2) =>
        BuildLayout((panelId1, zone1), (panelId2, zone2));

    /// <summary>
    /// Returns the <see cref="DockZone"/> of the named panel in the given layout,
    /// or throws if the panel is not found.
    /// </summary>
    private static DockZone SlotPanelZone(DockLayout? layout, string panelId)
    {
        Assert.That(layout, Is.Not.Null, $"Layout should not be null when looking up '{panelId}'");
        var slot = layout!.Slots.SingleOrDefault(s =>
            string.Equals(s.PanelId, panelId, StringComparison.OrdinalIgnoreCase));
        Assert.That(slot, Is.Not.Null, $"Panel '{panelId}' not found in layout slots");
        return slot!.Zone;
    }
}
