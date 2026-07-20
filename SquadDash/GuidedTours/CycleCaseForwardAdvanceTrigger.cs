using System;

namespace SquadDash.GuidedTours;

internal sealed class CycleCaseForwardAdvanceTrigger : IGuidedTourAdvanceTrigger
{
    private readonly Action<EventHandler> _addHandler;
    private readonly Action<EventHandler> _removeHandler;

    public CycleCaseForwardAdvanceTrigger(Action<EventHandler> addHandler, Action<EventHandler> removeHandler)
    {
        _addHandler    = addHandler;
        _removeHandler = removeHandler;
    }

    /// <inheritdoc/>
    public IDisposable? Subscribe(string parameter, Action onAdvance)
    {
        int required = 1;
        if (!string.IsNullOrWhiteSpace(parameter) &&
            int.TryParse(parameter.Trim(), out int parsed) && parsed > 0)
            required = parsed;

        int count = 0;
        void Handler(object? s, EventArgs e)
        {
            if (++count >= required)
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
