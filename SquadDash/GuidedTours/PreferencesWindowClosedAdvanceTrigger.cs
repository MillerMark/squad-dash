using System;

namespace SquadDash.GuidedTours;

/// <summary>
/// Advance trigger that fires when the Preferences window is closed (X button or programmatic close).
/// The <paramref name="parameter"/> value in <see cref="Subscribe"/> is ignored.
/// </summary>
internal sealed class PreferencesWindowClosedAdvanceTrigger : IGuidedTourAdvanceTrigger
{
    private readonly Action<Action> _addHandler;
    private readonly Action<Action> _removeHandler;

    public PreferencesWindowClosedAdvanceTrigger(Action<Action> addHandler, Action<Action> removeHandler)
    {
        _addHandler    = addHandler;
        _removeHandler = removeHandler;
    }

    /// <inheritdoc/>
    public IDisposable? Subscribe(string parameter, Action onAdvance)
    {
        _addHandler(onAdvance);
        return new Subscription(() => _removeHandler(onAdvance));
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
