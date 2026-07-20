using System;

namespace SquadDash.GuidedTours;

/// <summary>
/// Fires when a printable key is pressed while in full-screen transcript mode,
/// causing the prompt text box to peek into view.
/// </summary>
internal sealed class FullScreenPromptPeekAdvanceTrigger : IGuidedTourAdvanceTrigger
{
    private readonly Action<EventHandler> _addHandler;
    private readonly Action<EventHandler> _removeHandler;

    public FullScreenPromptPeekAdvanceTrigger(Action<EventHandler> addHandler, Action<EventHandler> removeHandler)
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
