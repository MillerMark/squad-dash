using System;

namespace SquadDash.GuidedTours;

/// <summary>
/// Advance trigger that fires when a new prompt is added to the prompt queue (e.g. via Ctrl+Q).
/// </summary>
internal sealed class PromptQueuedAdvanceTrigger : IGuidedTourAdvanceTrigger
{
    private readonly PromptQueue _promptQueue;

    public PromptQueuedAdvanceTrigger(PromptQueue promptQueue)
    {
        _promptQueue = promptQueue;
    }

    /// <inheritdoc/>
    public IDisposable? Subscribe(string parameter, Action onAdvance)
    {
        void Handler(object? s, EventArgs e) => onAdvance();
        _promptQueue.ItemEnqueued += Handler;
        return new Subscription(() => _promptQueue.ItemEnqueued -= Handler);
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
