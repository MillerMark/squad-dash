using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash.GuidedTours;

/// <summary>
/// Central registry of named commands that guided tour steps can invoke
/// before or after the step is displayed.
/// </summary>
internal sealed class GuidedTourCommandRegistry
{
    private readonly Dictionary<string, Action>        _commands =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Action<string>> _paramCommands =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a named no-argument command. Replaces any existing registration with the same name.
    /// </summary>
    public void Register(string name, Action action) =>
        _commands[name] = action;

    /// <summary>
    /// Registers a named parameterized command. The argument is the string after the first '|'.
    /// Replaces any existing registration with the same name.
    /// </summary>
    public void RegisterParameterized(string name, Action<string> action) =>
        _paramCommands[name] = action;

    /// <summary>
    /// Executes the named command if registered; silently does nothing if not found.
    /// If <paramref name="name"/> contains '|', the part before the first '|' is the command name
    /// and the remainder is passed as the argument to a parameterized command.
    /// </summary>
    public void Execute(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var sep = name.IndexOf('|');
        if (sep >= 0)
        {
            var cmdName = name[..sep];
            var arg     = name[(sep + 1)..];
            if (_paramCommands.TryGetValue(cmdName, out var paramAction))
                paramAction(arg);
        }
        else if (_commands.TryGetValue(name, out var action))
            action();
    }

    /// <summary>
    /// All registered command names (both plain and parameterized).
    /// </summary>
    public IReadOnlyList<string> CommandNames =>
        _commands.Keys.Concat(_paramCommands.Keys).ToList();
}
