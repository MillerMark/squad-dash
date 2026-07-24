using System;

namespace SquadDash.GuidedTours;

/// <summary>
/// Advance trigger that fires when the number of open secondary agent transcripts drops
/// from two or more to exactly one — i.e. the user clicked an active agent card to focus
/// on a single secondary transcript while others were visible.
/// The <paramref name="parameter"/> value in <see cref="Subscribe"/> is ignored.
/// </summary>
internal sealed class SecondaryTranscriptCollapsedToOneAdvanceTrigger : IGuidedTourAdvanceTrigger
{
    private readonly Action<Action> _addHandler;
    private readonly Action<Action> _removeHandler;

    public SecondaryTranscriptCollapsedToOneAdvanceTrigger(Action<Action> addHandler, Action<Action> removeHandler)
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
