using System;

namespace SquadDash.GuidedTours;

/// <summary>
/// Advance trigger that fires when a new empty prompt slot is created at the front of the queue
/// (e.g. via Ctrl+Q or the equivalent menu item).
/// The <paramref name="parameter"/> value in <see cref="Subscribe"/> is ignored.
/// </summary>
internal sealed class NewQueueSlotAdvanceTrigger : IGuidedTourAdvanceTrigger
{
    private readonly Action<Action> _addHandler;
    private readonly Action<Action> _removeHandler;

    public NewQueueSlotAdvanceTrigger(Action<Action> addHandler, Action<Action> removeHandler)
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
