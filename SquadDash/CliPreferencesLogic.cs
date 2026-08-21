using System;
using System.Threading.Tasks;

namespace SquadDash;

/// <summary>
/// Pure-logic helpers for the CLI selection section of the Preferences window.
/// Kept in a separate file so unit tests can include this file without pulling in
/// WPF assembly dependencies from PreferencesWindow.cs.
/// </summary>
internal static class CliPreferencesLogic
{
    /// <summary>
    /// Maps a <see cref="CliCapabilityStatus"/> to a human-readable label and a WPF theme
    /// resource key for the foreground colour.
    /// </summary>
    public static (string Label, string ResourceKey) BuildCapabilityIndicator(CliCapabilityStatus status) =>
        status switch
        {
            CliCapabilityStatus.Supported    => ("Supported",     "SuccessText"),
            CliCapabilityStatus.NotSupported => ("Not supported", "ErrorText"),
            _                                => ("Unknown",       "BodyText"),
        };

    /// <summary>
    /// Normalises the user's mode and custom-path input into the values that should be
    /// persisted: non-Custom modes always clear the custom path.
    /// </summary>
    public static (CliSelectionMode Mode, string? CustomPath) BuildCliSelectionForSave(
        CliSelectionMode selectedMode,
        string? customPathText)
    {
        var normalizedCustomPath = string.IsNullOrWhiteSpace(customPathText)
            ? null
            : customPathText.Trim();
        return selectedMode == CliSelectionMode.CustomPath
            ? (selectedMode, normalizedCustomPath)
            : (selectedMode, null);
    }

    /// <summary>
    /// Persists the user's CLI selection via <paramref name="selectionStore"/>.
    /// </summary>
    public static Task PersistCliSelectionAsync(
        ISquadCliSelectionStore selectionStore,
        string workspaceDirectory,
        CliSelectionMode selectedMode,
        string? customPathText)
    {
        ArgumentNullException.ThrowIfNull(selectionStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        var selection = BuildCliSelectionForSave(selectedMode, customPathText);
        return selectionStore.SaveSelectionAsync(workspaceDirectory, selection.Mode, selection.CustomPath);
    }

    /// <summary>Returns a display string for a <see cref="CliInstallSource"/>.</summary>
    public static string FormatCliInstallSource(CliInstallSource source) => source switch
    {
        CliInstallSource.WorkspaceNpm => "Workspace npm package",
        CliInstallSource.GlobalNpm    => "Global npm package",
        CliInstallSource.NativeOther  => "Native system install",
        CliInstallSource.Custom       => "Custom path",
        _                             => "Unknown",
    };
}
