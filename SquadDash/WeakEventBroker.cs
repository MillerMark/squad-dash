namespace SquadDash;

/// <summary>
/// Lightweight pub-sub event broker. Handlers are stored via <see cref="WeakReference{T}"/>
/// so the broker does not prevent subscriber GC. The subscriber must hold a strong reference
/// to the registered <see cref="Action{TEvent}"/> delegate to keep the subscription alive;
/// dead references are pruned automatically on the next <see cref="Publish{TEvent}"/> call.
/// All methods are thread-safe.
/// </summary>
public sealed class WeakEventBroker
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Type, List<WeakReference<Delegate>>> _subscriptions = new();

    /// <summary>
    /// Registers <paramref name="handler"/> for events of type <typeparamref name="TEvent"/>.
    /// The caller is responsible for retaining a strong reference to <paramref name="handler"/>;
    /// the broker holds only a <see cref="WeakReference{T}"/> and will not prevent GC.
    /// </summary>
    public void Subscribe<TEvent>(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock)
        {
            if (!_subscriptions.TryGetValue(typeof(TEvent), out var list))
                _subscriptions[typeof(TEvent)] = list = [];
            list.Add(new WeakReference<Delegate>(handler));
        }
    }

    /// <summary>
    /// Removes <paramref name="handler"/> from the handler list for <typeparamref name="TEvent"/>.
    /// Only the first matching occurrence is removed (mirrors C# event <c>-=</c> semantics).
    /// Dead references encountered during the search are also pruned.
    /// </summary>
    public void Unsubscribe<TEvent>(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock)
        {
            if (!_subscriptions.TryGetValue(typeof(TEvent), out var list)) return;
            var removed = false;
            list.RemoveAll(wr =>
            {
                if (!wr.TryGetTarget(out var d)) return true; // dead — prune regardless
                if (!removed && ReferenceEquals(d, handler))
                {
                    removed = true;
                    return true; // remove first match only
                }
                return false;
            });
        }
    }

    /// <summary>
    /// Delivers <paramref name="evt"/> to all live handlers registered for
    /// <typeparamref name="TEvent"/>. Dead <see cref="WeakReference{T}"/> entries are pruned
    /// before invocation; handlers are invoked outside the lock to avoid re-entrancy deadlocks.
    /// </summary>
    public void Publish<TEvent>(TEvent evt)
    {
        List<Action<TEvent>> snapshot;
        lock (_lock)
        {
            if (!_subscriptions.TryGetValue(typeof(TEvent), out var list)) return;
            snapshot = [];
            list.RemoveAll(wr =>
            {
                if (!wr.TryGetTarget(out var d)) return true; // dead — prune
                snapshot.Add((Action<TEvent>)d);
                return false;
            });
        }
        foreach (var h in snapshot)
            h(evt);
    }
}
