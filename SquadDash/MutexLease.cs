using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SquadDash;

internal sealed class MutexLease : IDisposable {
    // Tracks mutex names currently held in this process to prevent re-entrant acquisition.
    private static readonly ConcurrentDictionary<string, byte> s_heldNames =
        new(StringComparer.OrdinalIgnoreCase);

    private Mutex? _mutex;
    private readonly string _name;

    private MutexLease(Mutex mutex, string name) {
        _mutex = mutex;
        _name = name;
    }

    public static MutexLease Acquire(string name) {
        if (!TryAcquire(name, Timeout.InfiniteTimeSpan, out var lease) || lease is null)
            throw new TimeoutException($"Timed out waiting to acquire mutex '{name}'.");

        return lease;
    }

    public static bool TryAcquire(string name, out MutexLease? lease) {
        return TryAcquire(name, TimeSpan.Zero, out lease);
    }

    public static bool TryAcquire(string name, TimeSpan timeout, out MutexLease? lease) {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Mutex name cannot be empty.", nameof(name));

        if (timeout < Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var normalizedName = name.ToLowerInvariant();

        // Reject re-entrant acquisition within the same process.
        if (!s_heldNames.TryAdd(normalizedName, 0)) {
            lease = null;
            return false;
        }

        var mutex = new Mutex(false, name);
        var acquired = false;

        try {
            acquired = mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException) {
            // Ownership transfers to this thread when the previous owner exits unexpectedly.
            acquired = true;
        }

        if (!acquired) {
            s_heldNames.TryRemove(normalizedName, out _);
            mutex.Dispose();
            lease = null;
            return false;
        }

        lease = new MutexLease(mutex, normalizedName);
        return true;
    }

    public void Dispose() {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
            return;

        s_heldNames.TryRemove(_name, out _);

        try {
            mutex.ReleaseMutex();
        }
        finally {
            mutex.Dispose();
        }
    }
}
