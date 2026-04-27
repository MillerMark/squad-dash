using System;
using System.Collections.Generic;

namespace SquadDash.Screenshots;

/// <summary>
/// Central registry of all <see cref="IReplayableUiAction"/> instances available
/// in this session.  Actions are registered at application startup and remain
/// available for the lifetime of the process.
/// </summary>
/// <remarks>
/// The registry is instantiated once by <c>MainWindow</c> and passed by reference
/// to any component that needs to enumerate or invoke replay actions.  There is no
/// static singleton — the single instance is owned by the application shell.
/// </remarks>
public sealed class UiActionReplayRegistry
{
    private readonly Dictionary<string, IReplayableUiAction> _actions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers an action.  Throws <see cref="InvalidOperationException"/> if an
    /// action with the same <see cref="IReplayableUiAction.ActionId"/> is already
    /// registered — duplicate IDs indicate a programming error, not a runtime
    /// condition.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// An action with the same <see cref="IReplayableUiAction.ActionId"/> is already registered.
    /// </exception>
    public void Register(IReplayableUiAction action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (_actions.ContainsKey(action.ActionId))
            throw new InvalidOperationException(
                $"A replay action with ActionId '{action.ActionId}' is already registered.");
        _actions[action.ActionId] = action;
    }

    /// <summary>
    /// Attempts to retrieve a registered action by its stable <paramref name="actionId"/>.
    /// </summary>
    /// <returns><c>true</c> if found; <c>false</c> otherwise.</returns>
    public bool TryGet(string actionId, out IReplayableUiAction? action)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            action = null;
            return false;
        }
        return _actions.TryGetValue(actionId, out action);
    }

    /// <summary>
    /// All registered actions, in registration order.  Read-only snapshot —
    /// callers must not cast or modify the underlying collection.
    /// </summary>
    public IReadOnlyList<IReplayableUiAction> All => [.._actions.Values];
}
