using System;

namespace SquadDash.GuidedTours;

/// <summary>
/// A named trigger type that can subscribe to an event and call back when it fires.
/// </summary>
internal interface IGuidedTourAdvanceTrigger
{
    /// <summary>
    /// Subscribes to the trigger for the given <paramref name="parameter"/>.
    /// Returns an <see cref="IDisposable"/> that, when disposed, cancels the subscription.
    /// Returns null if the trigger cannot attach (e.g. target element not found).
    /// </summary>
    IDisposable? Subscribe(string parameter, Action onAdvance);
}
