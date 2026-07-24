using System;

namespace SquadDash.GuidedTours;

/// <summary>
/// Advance trigger that fires when the user clicks the active workspace label
/// in the title bar to reveal the workspace folder in Explorer.
/// </summary>
internal sealed class WorkspaceOpenedInExplorerAdvanceTrigger : IGuidedTourAdvanceTrigger
{
    private readonly Action<Action> _addHandler;
    private readonly Action<Action> _removeHandler;

    public WorkspaceOpenedInExplorerAdvanceTrigger(Action<Action> addHandler, Action<Action> removeHandler)
    {
        _addHandler    = addHandler;
        _removeHandler = removeHandler;
    }

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
