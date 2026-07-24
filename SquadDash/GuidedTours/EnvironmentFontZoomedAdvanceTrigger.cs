using System;

namespace SquadDash.GuidedTours;

/// <summary>
/// Advance trigger that fires when the user zooms the environment font size (Ctrl+mousewheel).
/// The <paramref name="parameter"/> value in <see cref="Subscribe"/> is ignored.
/// </summary>
internal sealed class EnvironmentFontZoomedAdvanceTrigger : IGuidedTourAdvanceTrigger
{
    private readonly Action<Action> _addHandler;
    private readonly Action<Action> _removeHandler;

    public EnvironmentFontZoomedAdvanceTrigger(Action<Action> addHandler, Action<Action> removeHandler)
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
