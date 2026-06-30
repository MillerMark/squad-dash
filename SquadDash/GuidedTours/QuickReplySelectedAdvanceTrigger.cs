using System;

namespace SquadDash.GuidedTours;

/// <summary>
/// Advance trigger that fires whenever a tour-injected quick reply button is clicked.
/// The <paramref name="parameter"/> value in <see cref="Subscribe"/> is ignored — any
/// quick reply selection advances the step.
/// </summary>
internal sealed class QuickReplySelectedAdvanceTrigger : IGuidedTourAdvanceTrigger
{
    private readonly Action<Action> _addHandler;
    private readonly Action<Action> _removeHandler;

    public QuickReplySelectedAdvanceTrigger(Action<Action> addHandler, Action<Action> removeHandler)
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
