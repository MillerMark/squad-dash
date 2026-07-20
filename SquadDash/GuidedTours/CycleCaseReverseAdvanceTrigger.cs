using System;

namespace SquadDash.GuidedTours;

internal sealed class CycleCaseReverseAdvanceTrigger : IGuidedTourAdvanceTrigger
{
    private readonly Action<EventHandler> _addHandler;
    private readonly Action<EventHandler> _removeHandler;

    public CycleCaseReverseAdvanceTrigger(Action<EventHandler> addHandler, Action<EventHandler> removeHandler)
    {
        _addHandler    = addHandler;
        _removeHandler = removeHandler;
    }

    /// <inheritdoc/>
    public IDisposable? Subscribe(string parameter, Action onAdvance)
    {
        void Handler(object? s, EventArgs e) => onAdvance();
        _addHandler(Handler);
        return new Subscription(() => _removeHandler(Handler));
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
