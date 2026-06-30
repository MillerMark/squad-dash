using System;
using System.Collections.Generic;

namespace SquadDash.GuidedTours;

/// <summary>
/// Registry mapping trigger-type names to <see cref="IGuidedTourAdvanceTrigger"/> implementations.
/// Supports dependency injection and testability — callers register trigger types at startup
/// and the <see cref="GuidedTourController"/> subscribes/unsubscribes at step boundaries.
/// </summary>
internal sealed class GuidedTourAdvanceTriggerRegistry
{
    private readonly Dictionary<string, IGuidedTourAdvanceTrigger> _triggers =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a named trigger type. Replaces any existing registration.</summary>
    public void Register(string name, IGuidedTourAdvanceTrigger trigger) =>
        _triggers[name] = trigger;

    /// <summary>
    /// Parses <paramref name="spec"/> as "Name:Argument" (or just "Name"),
    /// looks up the trigger, and subscribes. Returns null if spec is empty or
    /// the trigger type is not registered.
    /// </summary>
    public IDisposable? Subscribe(string? spec, Action onAdvance)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;

        var sep  = spec.IndexOf(':');
        var name = sep >= 0 ? spec[..sep] : spec;
        var arg  = sep >= 0 ? spec[(sep + 1)..] : string.Empty;

        return _triggers.TryGetValue(name, out var trigger)
            ? trigger.Subscribe(arg, onAdvance)
            : null;
    }

    /// <summary>All registered trigger-type names.</summary>
    public IReadOnlyCollection<string> TriggerNames => _triggers.Keys;
}
