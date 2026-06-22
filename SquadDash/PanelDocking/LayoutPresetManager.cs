#nullable enable

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadDash.PanelDocking;

/// <summary>
/// Manages layout presets (3 slots) for quick save/restore of panel configurations.
/// Persists presets to <c>.squad/panel-layout-presets.json</c>.
/// </summary>
internal sealed class LayoutPresetManager
{
    private const int PresetSlotCount = 3;
    private string? _workspacePath;
    private LayoutPresetsFile _presetsFile = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Initialize the preset manager for a workspace.
    /// </summary>
    public void Initialize(string workspacePath)
    {
        _workspacePath = workspacePath;
        LoadPresetsFile();
    }

    /// <summary>
    /// Save the current layout to the specified slot (0, 1, or 2).
    /// </summary>
    public void SavePreset(int slotIndex, DockLayout layout)
    {
        if (slotIndex < 0 || slotIndex >= PresetSlotCount)
            throw new ArgumentOutOfRangeException(nameof(slotIndex), $"Slot must be 0-{PresetSlotCount - 1}");
        if (_workspacePath is null)
            throw new InvalidOperationException("LayoutPresetManager not initialized");

        // Clone the layout for storage
        var preset = new DockLayout
        {
            Name = $"Preset {slotIndex + 1}",
            Slots = layout.Slots.ToList(),
            LeftZoneWidth = layout.LeftZoneWidth,
            RightZoneWidth = layout.RightZoneWidth,
            Left2ZoneWidth = layout.Left2ZoneWidth,
            Right2ZoneWidth = layout.Right2ZoneWidth,
            Left3ZoneWidth = layout.Left3ZoneWidth,
            Right3ZoneWidth = layout.Right3ZoneWidth,
            Left4ZoneWidth = layout.Left4ZoneWidth,
            Right4ZoneWidth = layout.Right4ZoneWidth,
            Left5ZoneWidth = layout.Left5ZoneWidth,
            Right5ZoneWidth = layout.Right5ZoneWidth,
            Left6ZoneWidth = layout.Left6ZoneWidth,
            Right6ZoneWidth = layout.Right6ZoneWidth,
            Left7ZoneWidth = layout.Left7ZoneWidth,
            Right7ZoneWidth = layout.Right7ZoneWidth,
        };

        // Ensure we have enough slots
        while (_presetsFile.Presets.Count <= slotIndex)
            _presetsFile.Presets.Add(null);

        _presetsFile.Presets[slotIndex] = preset;
        SavePresetsFile();
    }

    /// <summary>
    /// Get the layout preset for the specified slot, or null if empty.
    /// </summary>
    public DockLayout? GetPreset(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= PresetSlotCount)
            return null;
        if (slotIndex < _presetsFile.Presets.Count)
            return _presetsFile.Presets[slotIndex];
        return null;
    }

    /// <summary>
    /// Check if a preset slot has been saved.
    /// </summary>
    public bool HasPreset(int slotIndex) => GetPreset(slotIndex) is not null;

    private void LoadPresetsFile()
    {
        if (_workspacePath is null) return;

        var filePath = PresetsFilePath(_workspacePath);
        if (!File.Exists(filePath))
        {
            _presetsFile = new();
            return;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            _presetsFile = JsonSerializer.Deserialize<LayoutPresetsFile>(json, JsonOptions) ?? new();
        }
        catch
        {
            // If file is corrupted, start fresh
            _presetsFile = new();
        }
    }

    private void SavePresetsFile()
    {
        if (_workspacePath is null) return;

        var filePath = PresetsFilePath(_workspacePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var json = JsonSerializer.Serialize(_presetsFile, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    private static string PresetsFilePath(string workspacePath) =>
        Path.Combine(workspacePath, ".squad", "panel-layout-presets.json");
}

/// <summary>
/// Root DTO for <c>.squad/panel-layout-presets.json</c>.
/// </summary>
internal sealed class LayoutPresetsFile
{
    /// <summary>Stores up to 3 layout presets; null entries mean empty slots.</summary>
    public List<DockLayout?> Presets { get; set; } = new();
}
