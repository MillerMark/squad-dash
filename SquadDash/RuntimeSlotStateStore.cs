using System;
using System.IO;
using System.Text.Json;

namespace SquadDash;

internal static class RuntimeSlotNames {
    public const string SlotA = "A";
    public const string SlotB = "B";
    public const string PayloadFileName = "SquadDash.App.exe";

    public static string Normalize(string? slotName) {
        return string.Equals(slotName, SlotB, StringComparison.OrdinalIgnoreCase)
            ? SlotB
            : SlotA;
    }

    public static string Toggle(string? activeSlot) {
        return string.Equals(activeSlot, SlotA, StringComparison.OrdinalIgnoreCase)
            ? SlotB
            : SlotA;
    }
}

internal sealed class RuntimeSlotStateStore {
    private readonly string _runRootDirectory;

    public RuntimeSlotStateStore(string runRootDirectory) {
        if (string.IsNullOrWhiteSpace(runRootDirectory))
            throw new ArgumentException("Run root directory cannot be empty.", nameof(runRootDirectory));

        _runRootDirectory = Path.GetFullPath(runRootDirectory);
    }

    public RuntimeSlotState Load() {
        var statePath = GetStatePath();
        if (!File.Exists(statePath))
            return RuntimeSlotState.Empty;

        try {
            var json = File.ReadAllText(statePath);
            var state = JsonSerializer.Deserialize<RuntimeSlotState>(json);
            return Normalize(state ?? RuntimeSlotState.Empty);
        }
        catch {
            return RuntimeSlotState.Empty;
        }
    }

    public RuntimeSlotState Save(RuntimeSlotState state) {
        var normalized = Normalize(state);
        Directory.CreateDirectory(_runRootDirectory);

        var statePath = GetStatePath();
        JsonFileStorage.AtomicWrite(statePath, normalized);

        return normalized;
    }

    public string GetSlotDirectory(string slotName) {
        return Path.Combine(_runRootDirectory, RuntimeSlotNames.Normalize(slotName));
    }

    public string GetPayloadPath(string slotName) {
        return Path.Combine(GetSlotDirectory(slotName), RuntimeSlotNames.PayloadFileName);
    }

    private RuntimeSlotState Normalize(RuntimeSlotState state) {
        var activeSlot = string.IsNullOrWhiteSpace(state.ActiveSlot)
            ? null
            : RuntimeSlotNames.Normalize(state.ActiveSlot);

        return new RuntimeSlotState(
            activeSlot,
            state.UpdatedAt?.ToUniversalTime());
    }

    private string GetStatePath() {
        return Path.Combine(_runRootDirectory, "active-slot.json");
    }
}

internal sealed record RuntimeSlotState(
    string? ActiveSlot,
    DateTimeOffset? UpdatedAt) {

    public static RuntimeSlotState Empty { get; } = new(null, null);
}
