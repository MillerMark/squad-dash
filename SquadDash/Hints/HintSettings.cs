namespace SquadDash.Hints;

/// <summary>
/// User-configurable knobs for the Hints (Discoverability) system.
/// Stored as flat properties on <see cref="ApplicationSettingsSnapshot"/> and
/// retrieved via <see cref="FromSnapshot"/>.
/// </summary>
public class HintSettings {
    public bool HintsEnabled  { get; set; } = true;
    public int  MinGapMinutes { get; set; } = 10;

    internal static HintSettings FromSnapshot(ApplicationSettingsSnapshot snapshot) =>
        new() {
            HintsEnabled  = snapshot.HintsEnabled,
            MinGapMinutes = snapshot.HintMinGapMinutes
        };
}
