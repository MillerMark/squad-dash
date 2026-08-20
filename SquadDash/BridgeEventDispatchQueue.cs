namespace SquadDash;

/// <summary>
/// Owns the bridge-event queue and the single outstanding UI-dispatch claim.
/// A rejected dispatcher post must release its claim so a deferred close cannot
/// strand later bridge events behind a permanently "scheduled" drain.
/// </summary>
internal sealed class BridgeEventDispatchQueue {
    private readonly object _gate = new();
    private readonly Queue<SquadSdkEvent> _events = new();
    private bool _dispatchClaimed;

    public bool EnqueueAndTryClaimDispatch(SquadSdkEvent evt) {
        ArgumentNullException.ThrowIfNull(evt);

        lock (_gate) {
            _events.Enqueue(evt);
            if (_dispatchClaimed)
                return false;

            _dispatchClaimed = true;
            return true;
        }
    }

    public bool TryClaimDispatchForPendingEvents() {
        lock (_gate) {
            if (_dispatchClaimed || _events.Count == 0)
                return false;

            _dispatchClaimed = true;
            return true;
        }
    }

    public void ReleaseDispatchClaim() {
        lock (_gate)
            _dispatchClaimed = false;
    }

    public bool TryDequeue(out SquadSdkEvent evt) {
        lock (_gate) {
            if (_events.Count == 0) {
                evt = null!;
                return false;
            }

            evt = _events.Dequeue();
            return true;
        }
    }

    /// <summary>
    /// Completes one drain pass. Returns true when the current dispatch claim
    /// should be used to post another pass; otherwise releases the claim.
    /// </summary>
    public bool CompleteDrainPass(out int remaining) {
        lock (_gate) {
            remaining = _events.Count;
            if (remaining > 0)
                return true;

            _dispatchClaimed = false;
            return false;
        }
    }

    public int Count {
        get {
            lock (_gate)
                return _events.Count;
        }
    }
}
