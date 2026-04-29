using System;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

internal enum LoopStopState { None, StopRequested, Aborted }

/// <summary>
/// Drives the Native-Loop-in-Bridge feature.
/// Runs iterations on a background Task; all WPF UI callbacks are marshalled
/// by the caller (MainWindow) via Dispatcher before being passed as delegates.
/// </summary>
internal sealed class LoopController {

    private readonly Func<string, string?, Task> _executePromptAsync;
    private readonly Action                      _abortPrompt;
    private readonly Action<int>                 _onIterationStarted;
    private readonly Action                      _onStopped;
    private readonly Action<string>              _onError;
    private readonly Action<int>                 _onIterationCompleted;

    private volatile bool         _stopRequested;
    private CancellationTokenSource? _cts;

    internal bool          IsRunning  { get; private set; }
    internal LoopStopState StopState  { get; private set; }

    /// <param name="executePromptAsync">
    ///   <c>(prompt, sessionId) =&gt; …</c> — second arg is an optional session-id
    ///   override (null = use the active conversation session).
    ///   Must be dispatched to the UI thread by MainWindow before passing.
    /// </param>
    /// <param name="abortPrompt">Calls <c>_bridge.AbortPrompt()</c>.</param>
    /// <param name="onIterationStarted">Fired with the 1-based iteration number.</param>
    /// <param name="onStopped">Fired when the loop exits normally or via RequestStop.</param>
    /// <param name="onError">Fired with a human-readable message on timeout or abort.</param>
    /// <param name="onIterationCompleted">Fired with the 1-based iteration number after success.</param>
    internal LoopController(
        Func<string, string?, Task> executePromptAsync,
        Action                      abortPrompt,
        Action<int>                 onIterationStarted,
        Action                      onStopped,
        Action<string>              onError,
        Action<int>                 onIterationCompleted) {

        _executePromptAsync   = executePromptAsync;
        _abortPrompt          = abortPrompt;
        _onIterationStarted   = onIterationStarted;
        _onStopped            = onStopped;
        _onError              = onError;
        _onIterationCompleted = onIterationCompleted;
    }

    /// <summary>
    /// Starts the loop on a background Task and returns immediately.
    /// Does nothing if already running.
    /// </summary>
    internal Task StartAsync(LoopMdConfig config, bool continuousContext) {
        if (IsRunning)
            return Task.CompletedTask;

        _stopRequested = false;
        _cts           = new CancellationTokenSource();
        // Fire-and-forget; the loop reports completion via callbacks.
        _ = Task.Run(() => RunLoopAsync(config, continuousContext, _cts.Token));
        return Task.CompletedTask;
    }

    /// <summary>Graceful stop: finishes the current iteration then halts.</summary>
    internal void RequestStop() {
        _stopRequested = true;
        if (StopState == LoopStopState.None)
            StopState = LoopStopState.StopRequested;
    }

    /// <summary>Immediate abort: calls <c>abortPrompt</c> and cancels the loop CTS.</summary>
    internal void RequestAbort() {
        StopState      = LoopStopState.Aborted;
        _stopRequested = true;
        _abortPrompt();
        _cts?.Cancel();
    }

    private async Task RunLoopAsync(
        LoopMdConfig      config,
        bool              continuousContext,
        CancellationToken ct) {

        IsRunning = true;
        StopState = LoopStopState.None;
        int iteration = 0;

        try {
            while (!_stopRequested && !ct.IsCancellationRequested) {
                iteration++;
                _onIterationStarted(iteration);

                // continuousContext=false → each iteration gets its own session so
                // agent state does not accumulate across rounds.
                var sessionId = continuousContext ? null : Guid.NewGuid().ToString("N");

                using var iterCts =
                    CancellationTokenSource.CreateLinkedTokenSource(ct);
                iterCts.CancelAfter(TimeSpan.FromMinutes(config.TimeoutMinutes));

                try {
                    await _executePromptAsync(config.Instructions, sessionId);
                }
                catch (OperationCanceledException)
                    when (iterCts.IsCancellationRequested && !ct.IsCancellationRequested) {
                    // Timeout on this iteration only — report and stop the loop.
                    _onError(
                        $"Iteration {iteration} timed out after {config.TimeoutMinutes} min");
                    break;
                }

                _onIterationCompleted(iteration);
                if (_stopRequested) break;

                await Task.Delay(TimeSpan.FromMinutes(config.IntervalMinutes), ct);
            }
        }
        finally {
            IsRunning = false;
            if (StopState == LoopStopState.Aborted)
                _onError("Loop aborted.");
            else
                _onStopped();
        }
    }
}
