namespace SquadDash;

using System;

/// <summary>
/// Owns the current <see cref="ApplicationSettingsSnapshot"/> and mediates all reads, writes,
/// and persistence calls. Extracted from MainWindow to reduce its responsibility surface.
/// </summary>
internal sealed class SettingsSnapshotManager
{
    private readonly ApplicationSettingsStore _store;
    private ApplicationSettingsSnapshot _snapshot;

    public SettingsSnapshotManager(ApplicationSettingsStore store, ApplicationSettingsSnapshot initialSnapshot)
    {
        _store    = store;
        _snapshot = initialSnapshot;
    }

    /// <summary>Returns the current snapshot (read-only access for callers).</summary>
    public ApplicationSettingsSnapshot Current => _snapshot;

    /// <summary>Replaces the snapshot directly (used by external injection from PreferencesWindow).</summary>
    public void Replace(ApplicationSettingsSnapshot snapshot) => _snapshot = snapshot;

    /// <summary>Applies a with-expression mutation without persisting.</summary>
    public void Mutate(Func<ApplicationSettingsSnapshot, ApplicationSettingsSnapshot> mutate)
        => _snapshot = mutate(_snapshot);

    /// <summary>Applies a mutation and immediately persists the changed state.</summary>
    public void MutateAndSave(
        Func<ApplicationSettingsSnapshot, ApplicationSettingsSnapshot> mutate,
        Action<ApplicationSettingsStore, ApplicationSettingsSnapshot> save)
    {
        _snapshot = mutate(_snapshot);
        save(_store, _snapshot);
    }
}
