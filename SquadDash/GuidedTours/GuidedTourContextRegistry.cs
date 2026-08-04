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

    private readonly Dictionary<string, Func<string, bool>> _parameterizedContexts =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a named boolean context. Replaces any existing registration with the same name.
    /// </summary>
    public void Register(string name, Func<bool> evaluate) =>
        _contexts[name] = evaluate;

    /// <summary>
    /// Registers a parameterized context keyed by <paramref name="prefix"/>.
    /// When <see cref="Evaluate"/> encounters a name containing ':', it splits on the first ':'
    /// and invokes the matching registration with the remainder as the argument.
    /// </summary>
    public void RegisterParameterized(string prefix, Func<string, bool> evaluate) =>
        _parameterizedContexts[prefix] = evaluate;

    /// <summary>
    /// Evaluates the named context. Returns <see langword="null"/> if the name is not registered
    /// (treated as "no condition" — step proceeds normally).
    /// </summary>
    public bool? Evaluate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        // Exact match first.
        if (_contexts.TryGetValue(name, out var fn)) return fn();

        // Parameterized match: "Prefix:Argument".
        int colonIdx = name.IndexOf(':');
        if (colonIdx > 0)
        {
            var prefix = name.Substring(0, colonIdx);
            var argument = name.Substring(colonIdx + 1);
            if (_parameterizedContexts.TryGetValue(prefix, out var paramFn))
                return paramFn(argument);
        }

        return null;
    }

    /// <summary>All registered context names, sorted alphabetically.</summary>
    public IReadOnlyList<string> ContextNames =>
        [.. _contexts.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)];
}
