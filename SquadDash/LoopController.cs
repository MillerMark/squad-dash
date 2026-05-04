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
    private readonly Action<DateTimeOffset>      _onWaiting;
    private readonly Func<Task>                  _onBeforeIteration;
    private readonly Func<Task>                  _onBeforeWait;

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
    /// <param name="onWaiting">Fired with the target start time of the next iteration when the loop enters the inter-iteration delay.</param>
    /// <param name="onBeforeIteration">Awaited at the top of each cycle, before the loop prompt fires. Use this to drain a prompt queue before each iteration.</param>
    /// <param name="onBeforeWait">Awaited after each iteration completes, before the inter-iteration delay. Use this to drain a prompt queue after each iteration.</param>
    internal LoopController(
        Func<string, string?, Task> executePromptAsync,
        Action                      abortPrompt,
        Action<int>                 onIterationStarted,
        Action                      onStopped,
        Action<string>              onError,
        Action<int>                 onIterationCompleted,
        Action<DateTimeOffset>      onWaiting,
        Func<Task>?                 onBeforeIteration = null,
        Func<Task>?                 onBeforeWait      = null) {

        _executePromptAsync   = executePromptAsync;
        _abortPrompt          = abortPrompt;
        _onIterationStarted   = onIterationStarted;
        _onStopped            = onStopped;
        _onError              = onError;
        _onIterationCompleted = onIterationCompleted;
        _onWaiting            = onWaiting;
        _onBeforeIteration    = onBeforeIteration ?? (() => Task.CompletedTask);
        _onBeforeWait         = onBeforeWait      ?? (() => Task.CompletedTask);
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
                // Drain any queued prompts before firing this iteration.
                await _onBeforeIteration();
                if (_stopRequested) break;

                iteration++;
                _onIterationStarted(iteration);

                // continuousContext=false → each iteration gets its own session so
                // agent state does not accumulate across rounds.
                var sessionId = continuousContext ? null : Guid.NewGuid().ToString("N");

                var prompt     = BuildAugmentedPrompt(config.Instructions, config.Commands);
                var promptTask = _executePromptAsync(prompt, sessionId);

                // Race the prompt against the per-iteration wall-clock timeout.
                // Task.Delay with a dedicated CTS fires independently of the outer ct,
                // so we can distinguish a true timeout from a user-requested abort.
                using var timeoutCts = new CancellationTokenSource(
                    TimeSpan.FromMinutes(config.TimeoutMinutes));
                var timeoutExpired = Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token);

                if (await Task.WhenAny(promptTask, timeoutExpired) != promptTask) {
                    // Timeout fired before the prompt finished — abort it, then report.
                    _abortPrompt();
                    try { await promptTask; } catch { /* swallow post-abort exception */ }
                    _onError(
                        $"Iteration {iteration} timed out after {config.TimeoutMinutes} min");
                    break;
                }

                await promptTask; // propagate any exception thrown by the prompt itself

                _onIterationCompleted(iteration);
                if (_stopRequested) break;

                await _onBeforeWait();
                if (_stopRequested) break;

                var nextAt = DateTimeOffset.Now + TimeSpan.FromMinutes(config.IntervalMinutes);
                _onWaiting(nextAt);
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

    /// <summary>
    /// Appends a HOST_COMMAND_JSON reference block to the loop instructions if the
    /// loop.md frontmatter declared any <c>commands</c>. AI invokes commands by
    /// appending a HOST_COMMAND_JSON block at the very end of its response.
    /// </summary>
    internal static string BuildAugmentedPrompt(string instructions, IReadOnlyList<string>? commands) {
        if (commands is null || commands.Count == 0)
            return instructions;

        var sb = new System.Text.StringBuilder(instructions);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("## SquadDash loop commands (available this iteration)");
        sb.AppendLine();
        sb.AppendLine("To invoke a SquadDash command, append a HOST_COMMAND_JSON block at the **very end** of your response (after all other content including <system_notification>):");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("HOST_COMMAND_JSON:");
        sb.AppendLine("[");
        sb.AppendLine("  { \"command\": \"command_name\" }");
        sb.AppendLine("]");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Available commands this iteration:");
        sb.AppendLine();

        foreach (var cmd in commands) {
            switch (cmd.Trim().ToLowerInvariant()) {
                case "stop_loop":
                    sb.AppendLine("- **stop_loop** — Stops the loop after this iteration completes.");
                    break;
                case "start_loop":
                    sb.AppendLine("- **start_loop** — Starts (or restarts) the SquadDash native loop.");
                    break;
                default:
                    sb.AppendLine($"- **{cmd}**");
                    break;
            }
        }

        sb.AppendLine();
        sb.AppendLine("Only emit HOST_COMMAND_JSON when the condition for that command has been met. Do not emit it on every iteration.");
        return sb.ToString().TrimEnd();
    }
}
