using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash.GuidedTours;

/// <summary>
/// Registry of named boolean context functions that guided tour steps can require.
/// If a step declares a required context, the controller evaluates it before showing
/// the step and silently skips the step if the context does not match.
/// </summary>
internal sealed class GuidedTourContextRegistry
{
    private readonly Dictionary<string, Func<bool>> _contexts =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a named boolean context. Replaces any existing registration with the same name.
    /// </summary>
    public void Register(string name, Func<bool> evaluate) =>
        _contexts[name] = evaluate;

    /// <summary>
    /// Evaluates the named context. Returns <see langword="null"/> if the name is not registered
    /// (treated as "no condition" — step proceeds normally).
    /// </summary>
    public bool? Evaluate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _contexts.TryGetValue(name, out var fn) ? fn() : null;
    }

    /// <summary>All registered context names, sorted alphabetically.</summary>
    public IReadOnlyList<string> ContextNames =>
        [.. _contexts.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)];
}
