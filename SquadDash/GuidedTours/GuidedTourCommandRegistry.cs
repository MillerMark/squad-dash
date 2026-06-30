using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SquadDash.GuidedTours;

/// <summary>
/// Central registry of named commands that guided tour steps can invoke
/// before or after the step is displayed.
/// Supports both synchronous (<see cref="Action"/>/<see cref="Action{T}"/>) and
/// asynchronous (<see cref="Func{Task}"/>/<see cref="Func{T, Task}"/>) commands.
/// </summary>
internal sealed class GuidedTourCommandRegistry
{
    private readonly Dictionary<string, Action>             _commands =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Action<string>>     _paramCommands =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Func<Task>>         _asyncCommands =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Func<string, Task>> _asyncParamCommands =
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
    /// Registers a named no-argument async command. Replaces any existing registration with the same name.
    /// </summary>
    public void RegisterAsync(string name, Func<Task> action) =>
        _asyncCommands[name] = action;

    /// <summary>
    /// Registers a named parameterized async command. The argument is the string after the first '|'.
    /// Replaces any existing registration with the same name.
    /// </summary>
    public void RegisterParameterizedAsync(string name, Func<string, Task> action) =>
        _asyncParamCommands[name] = action;

    /// <summary>
    /// Executes the named command synchronously if registered; silently does nothing if not found.
    /// Async commands registered via <see cref="RegisterAsync"/> or <see cref="RegisterParameterizedAsync"/>
    /// are not reachable through this overload — use <see cref="ExecuteAsync"/> instead.
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
    /// Executes the named command and returns a <see cref="Task"/> that completes when the command
    /// finishes. Async commands are awaited; synchronous commands complete immediately.
    /// Silently does nothing if the command name is not registered.
    /// If <paramref name="name"/> contains '|', the part before the first '|' is the command name
    /// and the remainder is passed as the argument to a parameterized command.
    /// </summary>
    public async Task ExecuteAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var sep = name.IndexOf('|');
        if (sep >= 0)
        {
            var cmdName = name[..sep];
            var arg     = name[(sep + 1)..];
            if (_asyncParamCommands.TryGetValue(cmdName, out var asyncParamAction))
                await asyncParamAction(arg);
            else if (_paramCommands.TryGetValue(cmdName, out var paramAction))
                paramAction(arg);
        }
        else
        {
            if (_asyncCommands.TryGetValue(name, out var asyncAction))
                await asyncAction();
            else if (_commands.TryGetValue(name, out var action))
                action();
        }
    }

    /// <summary>
    /// All registered command names (plain, parameterized, and async variants).
    /// </summary>
    public IReadOnlyList<string> CommandNames =>
        _commands.Keys
            .Concat(_paramCommands.Keys)
            .Concat(_asyncCommands.Keys)
            .Concat(_asyncParamCommands.Keys)
            .ToList();
}
