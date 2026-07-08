using System;

namespace SquadDash.GuidedTours;

/// <summary>
/// Advance trigger that fires when a specific Preferences page is selected.
/// Parameter is the page label (e.g. "Hints", "Model") — case-insensitive.
/// The event source fires with the selected page label; the trigger only calls
/// <paramref name="onAdvance"/> when the label matches the subscribed parameter.
/// </summary>
internal sealed class PreferencePageSelectedAdvanceTrigger : IGuidedTourAdvanceTrigger
{
    private readonly Action<Action<string>> _addHandler;
    private readonly Action<Action<string>> _removeHandler;

    public PreferencePageSelectedAdvanceTrigger(
        Action<Action<string>> addHandler,
        Action<Action<string>> removeHandler)
    {
        _addHandler    = addHandler;
        _removeHandler = removeHandler;
    }

    /// <inheritdoc/>
    public IDisposable? Subscribe(string parameter, Action onAdvance)
    {
        void Handler(string label)
        {
            if (string.Equals(label, parameter, StringComparison.OrdinalIgnoreCase))
                onAdvance();
        }
        _addHandler(Handler);
        return new Subscription(() => _removeHandler(Handler));
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
