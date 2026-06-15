using System;
using System.Threading;

namespace SquadDash;

/// <summary>
/// Detects when the application has been idle for a configurable threshold.
/// Idle = no prompt running AND no loop running AND threshold time elapsed since last activity.
/// </summary>
internal sealed class IdleDetectionService {

    // ── Events ─────────────────────────────────────────────────────────────────

    public event Action? IdleThresholdReached;
    public event Action? ActivityDetected;

    // ── State (using int/long for Interlocked) ────────────────────────────────

    private long     _lastActivityTicks;
    private volatile bool _isPromptActive;
    private volatile bool _isLoopActive;
    private volatile bool _isRunnerActive;
    private volatile bool _forcedIdle;
    private int      _firedThisIdlePeriod;   // 0 = not fired, 1 = fired (Interlocked)

    private Timer?   _timer;
    private double   _thresholdMinutes;

    // ── Constructor ────────────────────────────────────────────────────────────

    public IdleDetectionService() { }

    // ── Control ────────────────────────────────────────────────────────────────

    /// <summary>Starts the idle detection timer with the given threshold.</summary>
    public void Start(double thresholdMinutes) {
        _thresholdMinutes = thresholdMinutes;
        Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _firedThisIdlePeriod, 0);

        // Use a short interval so tests with tiny thresholds fire promptly.
        var thresholdMs  = thresholdMinutes * 60_000.0;
        var intervalMs   = (int)Math.Max(50, Math.Min(30_000, thresholdMs / 3));
        _timer = new Timer(_ => CheckAndFireIfIdle(), null, intervalMs, intervalMs);
    }

    /// <summary>Stops the idle detection timer.</summary>
    public void Stop() {
        _timer?.Dispose();
        _timer = null;
    }

    // ── Activity signalling ────────────────────────────────────────────────────

    /// <summary>Resets the idle timer. Call when user or system activity occurs.</summary>
    public void RecordActivity() {
        Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _firedThisIdlePeriod, 0);
        ActivityDetected?.Invoke();
    }

    // ── State setters ──────────────────────────────────────────────────────────

    /// <summary>
    /// Mark whether a prompt is currently executing.
    /// Setting false triggers an immediate idle check.
    /// </summary>
    public void SetPromptActive(bool active) {
        _isPromptActive = active;
        if (!active) CheckAndFireIfIdle();
    }

    /// <summary>
    /// Mark whether the loop controller is currently running.
    /// Setting false triggers an immediate idle check (releases deferred ForceIdle).
    /// </summary>
    public void SetLoopActive(bool active) {
        _isLoopActive = active;
        if (!active) CheckAndFireIfIdle();
    }

    /// <summary>
    /// Mark whether the maintenance runner is active.
    /// Setting false re-arms idle detection and triggers an immediate check.
    /// </summary>
    public void SetRunnerActive(bool active) {
        _isRunnerActive = active;
        if (!active) {
            _forcedIdle = false;
            Interlocked.Exchange(ref _firedThisIdlePeriod, 0);
            Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
            CheckAndFireIfIdle();
        }
    }

    /// <summary>
    /// Forces an idle event immediately if both prompt and loop are inactive;
    /// otherwise defers the signal until they become inactive.
    /// </summary>
    public void ForceIdle() {
        _forcedIdle = true;
        CheckAndFireIfIdle();
    }

    // ── Core check ────────────────────────────────────────────────────────────

    private void CheckAndFireIfIdle() {
        // Suppress while any active process is running
        if (_isPromptActive || _isLoopActive || _isRunnerActive)
            return;

        // Determine eligibility
        bool eligible = _forcedIdle || IsThresholdElapsed();
        if (!eligible)
            return;

        // Atomically claim the right to fire (prevent duplicate fires in the same idle period)
        if (Interlocked.CompareExchange(ref _firedThisIdlePeriod, 1, 0) != 0)
            return;

        _forcedIdle = false;
        IdleThresholdReached?.Invoke();
    }

    private bool IsThresholdElapsed() {
        var lastTicks   = Interlocked.Read(ref _lastActivityTicks);
        var elapsedMs   = (DateTime.UtcNow.Ticks - lastTicks) / (double)TimeSpan.TicksPerMillisecond;
        var thresholdMs = _thresholdMinutes * 60_000.0;
        return elapsedMs >= thresholdMs;
    }
}

