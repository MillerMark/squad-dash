using System;

namespace SquadDash.GuidedTours;

/// <summary>
/// Advance trigger that fires when a tool panel becomes visible or is closed.
/// Parameter is the panel identifier (e.g. "tasks", "approvals", "inbox") — case-insensitive.
/// An empty parameter matches any panel.
/// </summary>
internal sealed class ToolPanelVisibilityAdvanceTrigger : IGuidedTourAdvanceTrigger
{
    private readonly Action<Action<string>> _addHandler;
    private readonly Action<Action<string>> _removeHandler;

    public ToolPanelVisibilityAdvanceTrigger(
        Action<Action<string>> addHandler,
        Action<Action<string>> removeHandler)
    {
        _addHandler    = addHandler;
        _removeHandler = removeHandler;
    }

    /// <inheritdoc/>
    public IDisposable? Subscribe(string parameter, Action onAdvance)
    {
        void Handler(string panelId)
        {
            if (string.IsNullOrEmpty(parameter) ||
                string.Equals(panelId, parameter, StringComparison.OrdinalIgnoreCase))
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
