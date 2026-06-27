using System;
using System.Collections.Generic;

namespace SquadDash.GuidedTours;

/// <summary>
/// Central registry of named commands that guided tour steps can invoke
/// before or after the step is displayed.
/// </summary>
internal sealed class GuidedTourCommandRegistry
{
    private readonly Dictionary<string, Action> _commands =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a named command. Replaces any existing registration with the same name.
    /// </summary>
    public void Register(string name, Action action) =>
        _commands[name] = action;

    /// <summary>
    /// Executes the named command if registered; silently does nothing if not found.
    /// </summary>
    public void Execute(string name)
    {
        if (!string.IsNullOrWhiteSpace(name) && _commands.TryGetValue(name, out var action))
            action();
    }

    /// <summary>
    /// All registered command names in registration order.
    /// </summary>
    public IReadOnlyList<string> CommandNames => new List<string>(_commands.Keys);
}
