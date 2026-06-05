#nullable enable

using System.IO;
using System.Text.Json;

namespace SquadDash.PanelDocking;

/// <summary>
/// Records a single docking interaction as a JSON test case.
/// State machine: Idle → Recording → Idle (auto-resets after writing the file).
/// </summary>
internal sealed class DockingTestRecorder : IDockingMoveRecorder
{
    private enum RecorderState { Idle, Recording }

    private RecorderState   _state          = RecorderState.Idle;
    private string?         _sourcePanelId;
    private PanelLayoutData? _initialLayout;
    private List<SlotButtonInfo>? _slotButtons;
    private PanelLayoutData? _slotButtonLayout;
    private List<SlotButtonViewModel>? _dockingMapSlots; // NEW: capture docking map after move

    private readonly string _outputDirectory;

    public Action? RecordingCompleted { get; set; }
    public bool IsIdle => _state == RecorderState.Idle;

    public DockingTestRecorder(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
    }

    public void StartRecording(PanelLayoutData initialLayout)
    {
        _initialLayout    = initialLayout;
        _sourcePanelId    = null;
        _slotButtons      = null;
        _slotButtonLayout = null;
        _dockingMapSlots  = null;
        _state            = RecorderState.Recording;
    }

    public void OnSlotButtonsBuilt(string sourcePanelId, List<SlotButtonInfo> slots, PanelLayoutData layout)
    {
        if (_state != RecorderState.Recording) return;

        _sourcePanelId    = sourcePanelId;
        _slotButtons      = slots;
        _slotButtonLayout = layout;
    }

    /// <summary>
    /// Capture the docking map slots for verification in the test case.
    /// </summary>
    public void OnDockingMapBuilt(IReadOnlyList<SlotButtonViewModel> slots)
    {
        if (_state != RecorderState.Recording) return;
        SquadDashTrace.Write(TraceCategory.Docking, 
            $"[DockingTestRecorder] OnDockingMapBuilt called with {slots.Count} slots");
        _dockingMapSlots = slots.ToList();
        SquadDashTrace.Write(TraceCategory.Docking, 
            $"[DockingTestRecorder] _dockingMapSlots set to list with {_dockingMapSlots.Count} items");
    }

    public void OnMoveCompleted(string sourcePanelId, DockZone targetZone, int targetOrder, PanelLayoutData layoutAfter)
    {
        if (_state != RecorderState.Recording) return;
        if (_initialLayout is null) return;

        var sourceSlot = _initialLayout.Slots.FirstOrDefault(s =>
            string.Equals(s.PanelId, sourcePanelId, StringComparison.OrdinalIgnoreCase));
        var sourceZone    = sourceSlot?.Zone ?? targetZone;
        var sourceZoneTag = DockingLayoutEngine.GetZoneFileTag(sourceZone);
        var targetZoneTag = DockingLayoutEngine.GetZoneFileTag(targetZone);
        var timestamp     = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var filename      = $"{sourcePanelId}_{sourceZoneTag}_to_{targetZoneTag}_order{targetOrder}_{timestamp}.json";

        var layoutForPreview = _slotButtonLayout ?? _initialLayout;
        var chosenPreview    = DockingLayoutEngine.GetNormalizedPreviewDescription(targetZone, targetOrder, layoutForPreview);

        var slotButtonsForJson = (_slotButtons ?? new List<SlotButtonInfo>())
            .Select(s => new
            {
                zone    = DockingLayoutEngine.GetZoneDisplayName(s.Zone),
                order   = s.Order,
                preview = DockingLayoutEngine.GetNormalizedPreviewDescription(s.Zone, s.Order, layoutForPreview),
            })
            .ToList();

        var dockingMapForJson = (_dockingMapSlots ?? new List<SlotButtonViewModel>())
            .Select(slot => new
            {
                zone      = DockingLayoutEngine.GetZoneDisplayName(slot.TargetZone),
                order     = slot.TargetOrder,
                x         = (int)slot.X,
                y         = (int)slot.Y,
                width     = (int)slot.Width,
                height    = (int)slot.Height,
                panelId   = slot.Label,
                isSeparator = slot.IsSeparator,
                isVirtualThin = slot.Width < 48, // Heuristic: thins are narrow (<48px)
            })
            .OrderBy(s => s.zone)
            .ThenBy(s => s.order)
            .ToList();

        var testCase = new
        {
            name           = $"{sourcePanelId}_{sourceZoneTag}_to_{targetZoneTag}_order{targetOrder}",
            sourcePanelId  = sourcePanelId,
            initialLayout  = DockingLayoutEngine.LayoutToJson(_initialLayout),
            slotButtons    = slotButtonsForJson,
            action         = new
            {
                targetZone  = DockingLayoutEngine.GetZoneDisplayName(targetZone),
                targetOrder = targetOrder,
            },
            chosenPreview  = chosenPreview,
            expectedLayout = DockingLayoutEngine.LayoutToJson(layoutAfter),
            expectedDockingMap = dockingMapForJson,
        };

        Directory.CreateDirectory(_outputDirectory);
        var json = JsonSerializer.Serialize(testCase, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(_outputDirectory, filename), json);

        Reset();
        RecordingCompleted?.Invoke();
    }

    /// <summary>
    /// Cancels an in-progress recording without writing a file or invoking
    /// <see cref="RecordingCompleted"/>.
    /// </summary>
    internal void Cancel() => Reset();

    private void Reset()
    {
        _state            = RecorderState.Idle;
        _sourcePanelId    = null;
        _initialLayout    = null;
        _slotButtons      = null;
        _slotButtonLayout = null;
        _dockingMapSlots  = null;
    }
}
