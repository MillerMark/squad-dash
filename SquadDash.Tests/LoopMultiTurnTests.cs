using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Tests for the LoopController multi-turn iteration pattern.
///
/// The <c>executePromptAsync</c> delegate can internally simulate multi-turn
/// exchanges (AI produces quick replies → waits for user input → continues)
/// because <see cref="LoopController"/> simply awaits whatever the delegate
/// returns.  All behaviour is modelled with fake async delegates — no WPF
/// dependency.
///
/// ── WPF integration gap (cannot be unit-tested here) ──────────────────────
/// The full queue-abort → loop-resume flow spans MainWindow.xaml.cs and
/// requires live WPF infrastructure that cannot be exercised in a headless
/// test runner:
///
///   1. Loop is running; its iteration ends.
///   2. <c>DrainQueueBeforeLoopIterationAsync</c> dispatches a queued item.
///   3. User presses Ctrl+Break: <c>setIsPromptRunning(false)</c> fires.
///   4. <c>setIsPromptRunning</c> sees <c>_promptQueue.Count == 0</c> and
///      calls <c>MaybeFireQueuedLoopAsync()</c>.
///   5. <c>MaybeFireQueuedLoopAsync</c> checks <c>_loopQueued</c> /
///      <c>_loopInterruptedByQueue</c> and restarts the loop via
///      <c>StartLoopImmediateAsync</c>.
///
/// All five steps depend on <c>MainWindow</c> state fields
/// (<c>_loopQueued</c>, <c>_loopInterruptedByQueue</c>, <c>_isPromptRunning</c>,
/// <c>_promptQueue</c>), the WPF <c>Dispatcher</c>, and
/// <c>PromptExecutionCoordinator</c>.  No seam exists to inject fakes without
/// a full WPF harness.  The tests below exercise only the
/// <see cref="LoopController"/> half of this contract.
/// </summary>
[TestFixture]
internal sealed class LoopMultiTurnTests {

    private static LoopMdConfig MakeConfig(
        double intervalMinutes = 0.0001,
        double timeoutMinutes  = 5)
        => new(intervalMinutes, TimeoutMinutes: timeoutMinutes,
               Description: "", Instructions: "test prompt");

    // ── Scenario 1: Clean iteration (no quick replies) ────────────────────────

    /// <summary>
    /// When executePromptAsync returns immediately (no multi-turn follow-up),
    /// onIterationCompleted fires exactly once per execution and the loop
    /// continues into the next iteration.
    /// </summary>
    [Test]
    public async Task CleanIteration_NoFollowUp_CompletedFiredOncePerExecution() {
        var iter2Tcs    = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedTcs  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int execCount      = 0;
        int completedCount = 0;

        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync:   (_, __) => {
                execCount++;
                if (execCount == 2) iter2Tcs.TrySetResult();
                return Task.CompletedTask;
            },
            abortPrompt:          () => { },
            onIterationStarted:   _ => { },
            onStopped:            () => stoppedTcs.TrySetResult(),
            onError:              _ => stoppedTcs.TrySetResult(),
            onIterationCompleted: _ => completedCount++,
            onWaiting:            _ => { });

        _ = controller!.StartAsync(MakeConfig(), continuousContext: true);
        await iter2Tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        controller.RequestStop();
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(execCount,       Is.GreaterThanOrEqualTo(2), "loop must run multiple iterations");
        Assert.That(completedCount,  Is.EqualTo(execCount),      "onIterationCompleted fires once per execution, no extras");
        Assert.That(controller.IsRunning, Is.False);
    }

    // ── Scenario 2: Single quick-reply follow-up ──────────────────────────────

    /// <summary>
    /// When the delegate parks waiting for user input (one follow-up round) and
    /// the input arrives, onIterationCompleted fires exactly once — after the
    /// clean follow-up response, not after the initial sub-turn.
    /// </summary>
    [Test]
    public async Task SingleFollowUp_IterationCountedOnceAfterCleanResponse() {
        var delegateParkedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var userInputTcs      = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedTcs        = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int completedCount    = 0;
        bool needsInput       = true;

        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync: async (_, __) => {
                if (needsInput) {
                    needsInput = false;
                    delegateParkedTcs.TrySetResult();        // signal: parked at follow-up wait
                    bool got = await userInputTcs.Task;
                    if (!got) return;                        // aborted — bail out
                    // second sub-turn: clean response (fall through, return normally)
                }
            },
            abortPrompt:          () => { },
            onIterationStarted:   _ => { },
            onStopped:            () => stoppedTcs.TrySetResult(),
            onError:              _ => stoppedTcs.TrySetResult(),
            onIterationCompleted: _ => { completedCount++; controller!.RequestStop(); },
            onWaiting:            _ => { });

        _ = controller!.StartAsync(MakeConfig(), continuousContext: true);
        await delegateParkedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        userInputTcs.TrySetResult(true);    // simulate user input arriving

        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(completedCount, Is.EqualTo(1),
            "onIterationCompleted fires once (after follow-up completes), never mid-sub-turn");
        Assert.That(controller.IsRunning, Is.False);
    }

    // ── Scenario 3: Multiple follow-up exchanges ──────────────────────────────

    /// <summary>
    /// Two rounds of quick replies followed by a clean response still count as
    /// a single iteration.
    /// </summary>
    [Test]
    public async Task TwoFollowUps_IterationCountedOnce() {
        var parked1Tcs     = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parked2Tcs     = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var userInput1Tcs  = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var userInput2Tcs  = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedTcs     = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int completedCount = 0;
        int roundsReached  = 0;

        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync: async (_, __) => {
                // Round 1: AI produces quick replies; park until user responds.
                roundsReached = 1;
                parked1Tcs.TrySetResult();
                bool got1 = await userInput1Tcs.Task;
                if (!got1) return;

                // Round 2: AI produces quick replies again; park until user responds.
                roundsReached = 2;
                parked2Tcs.TrySetResult();
                bool got2 = await userInput2Tcs.Task;
                if (!got2) return;

                // Clean response — fall through and return normally.
            },
            abortPrompt:          () => { },
            onIterationStarted:   _ => { },
            onStopped:            () => stoppedTcs.TrySetResult(),
            onError:              _ => stoppedTcs.TrySetResult(),
            onIterationCompleted: _ => { completedCount++; controller!.RequestStop(); },
            onWaiting:            _ => { });

        _ = controller!.StartAsync(MakeConfig(), continuousContext: true);

        await parked1Tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        userInput1Tcs.TrySetResult(true);

        await parked2Tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        userInput2Tcs.TrySetResult(true);

        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(completedCount, Is.EqualTo(1),  "two follow-up rounds still count as a single iteration");
        Assert.That(roundsReached,  Is.EqualTo(2),  "both follow-up rounds must have been exercised");
        Assert.That(controller.IsRunning, Is.False);
    }

    // ── Scenario 4: Queue drained before next iteration ───────────────────────

    /// <summary>
    /// After a multi-turn iteration completes, onBeforeIteration is called
    /// before the next iteration's executePromptAsync fires — verifying that
    /// the queue-drain hook always precedes the prompt regardless of how long
    /// the previous iteration took.
    /// </summary>
    [Test]
    public async Task QueueDrainedBeforeNextIteration_AfterMultiTurnExchange() {
        var callOrder         = new List<string>();
        var delegateParkedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var userInputTcs      = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondIterTcs     = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedTcs        = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int execCount         = 0;

        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync: async (_, __) => {
                execCount++;
                lock (callOrder) callOrder.Add($"exec{execCount}");

                if (execCount == 1) {
                    // Multi-turn: park waiting for user input.
                    delegateParkedTcs.TrySetResult();
                    bool got = await userInputTcs.Task;
                    if (!got) return;
                    // clean follow-up (fall through)
                }
                if (execCount == 2) {
                    secondIterTcs.TrySetResult();
                    controller!.RequestStop();
                }
            },
            abortPrompt:      () => { },
            onIterationStarted: _ => { },
            onStopped:        () => stoppedTcs.TrySetResult(),
            onError:          _ => stoppedTcs.TrySetResult(),
            onIterationCompleted: _ => { },
            onWaiting:        _ => { },
            onBeforeIteration: () => {
                lock (callOrder) callOrder.Add("before");
                return Task.CompletedTask;
            });

        _ = controller!.StartAsync(MakeConfig(), continuousContext: true);

        await delegateParkedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        userInputTcs.TrySetResult(true);    // complete the multi-turn iteration

        await secondIterTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // "before" must appear immediately before "exec2" in the call log.
        int exec2Index = callOrder.LastIndexOf("exec2");
        Assert.That(exec2Index, Is.GreaterThan(0), "exec2 must appear in the call log");
        Assert.That(callOrder[exec2Index - 1], Is.EqualTo("before"),
            "onBeforeIteration must fire immediately before the second iteration's executePromptAsync");
    }

    // ── Scenario 5: Abort during follow-up wait ───────────────────────────────

    /// <summary>
    /// When RequestAbort is called while the delegate is parked at the
    /// multi-turn wait, abortPrompt cancels the delegate's work, the delegate
    /// throws OperationCanceledException, onIterationCompleted is NOT called,
    /// and onError fires with the abort message.
    /// </summary>
    [Test]
    public async Task AbortDuringFollowUpWait_OnErrorCalledAndIterationNotCompleted() {
        var delegateParkedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishedTcs       = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var promptCts         = new CancellationTokenSource();
        int completedCount    = 0;
        string? errorMsg      = null;
        bool stoppedCalled    = false;

        var controller = new LoopController(
            executePromptAsync: async (_, __) => {
                delegateParkedTcs.TrySetResult();
                // Park simulating the multi-turn wait.
                // abortPrompt cancels promptCts, causing this to throw OperationCanceledException,
                // which propagates out of executePromptAsync and prevents onIterationCompleted.
                await Task.Delay(Timeout.InfiniteTimeSpan, promptCts.Token);
            },
            abortPrompt:          () => promptCts.Cancel(),
            onIterationStarted:   _ => { },
            onStopped:            () => { stoppedCalled = true; finishedTcs.TrySetResult(); },
            onError:              msg => { errorMsg = msg; finishedTcs.TrySetResult(); },
            onIterationCompleted: _ => completedCount++,
            onWaiting:            _ => { });

        _ = controller.StartAsync(MakeConfig(), continuousContext: true);
        await delegateParkedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        controller.RequestAbort();
        await finishedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(errorMsg,       Is.EqualTo("Loop aborted."), "onError must receive the abort message");
        Assert.That(completedCount, Is.EqualTo(0),               "onIterationCompleted must NOT fire when delegate throws");
        Assert.That(stoppedCalled,  Is.False,                    "onStopped must NOT fire on abort");
        Assert.That(controller.IsRunning, Is.False);
    }

    // ── Scenario 6: Stop request during follow-up ─────────────────────────────

    /// <summary>
    /// When RequestStop is called while the delegate is parked at the
    /// multi-turn wait, the current iteration still completes once user input
    /// arrives, onIterationCompleted fires, and then the loop stops gracefully.
    /// </summary>
    [Test]
    public async Task StopDuringFollowUpWait_CompletesCurrentIterationThenStops() {
        var delegateParkedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var userInputTcs      = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishedTcs       = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int completedCount    = 0;
        string? errorMsg      = null;
        bool stoppedCalled    = false;

        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync: async (_, __) => {
                delegateParkedTcs.TrySetResult();
                bool got = await userInputTcs.Task;
                if (!got) return;
                // Follow-up turn: clean response (fall through).
            },
            abortPrompt:          () => { },
            onIterationStarted:   _ => { },
            onStopped:            () => { stoppedCalled = true; finishedTcs.TrySetResult(); },
            onError:              msg => { errorMsg = msg; finishedTcs.TrySetResult(); },
            onIterationCompleted: _ => completedCount++,
            onWaiting:            _ => { });

        _ = controller!.StartAsync(MakeConfig(), continuousContext: true);
        await delegateParkedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        controller.RequestStop();           // graceful stop while delegate is parked
        userInputTcs.TrySetResult(true);    // user input arrives — delegate completes cleanly

        await finishedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(stoppedCalled,  Is.True,  "onStopped must be called on graceful stop");
        Assert.That(errorMsg,       Is.Null,  "onError must NOT be called on graceful stop");
        Assert.That(completedCount, Is.EqualTo(1), "in-progress iteration must complete before the loop exits");
        Assert.That(controller.IsRunning,  Is.False);
        Assert.That(controller.StopState,  Is.EqualTo(LoopStopState.StopRequested));
    }

    // ── Scenario 7: Timeout spans the whole multi-turn exchange ───────────────

    /// <summary>
    /// When the delegate stalls (simulating a hung multi-turn exchange) and the
    /// per-iteration timeout fires, abortPrompt is called, onError reports the
    /// timeout, and onStopped does not also fire and race the error recovery path.
    /// </summary>
    [Test]
    public async Task Timeout_DuringMultiTurnWait_ReportsTimeoutErrorAndStops() {
        var promptStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorTcs         = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var promptCts        = new CancellationTokenSource();
        bool abortCalled     = false;
        string? errorMsg     = null;
        bool stoppedCalled   = false;

        var controller = new LoopController(
            executePromptAsync: async (_, __) => {
                promptStartedTcs.TrySetResult();
                // Stalled multi-turn exchange — never returns until externally aborted.
                await Task.Delay(Timeout.InfiniteTimeSpan, promptCts.Token);
            },
            abortPrompt: () => {
                abortCalled = true;
                promptCts.Cancel();     // unblocks the stalled delegate
            },
            onIterationStarted:   _ => { },
            onStopped:            () => { stoppedCalled = true; },
            onError:              msg => { errorMsg = msg; errorTcs.TrySetResult(); },
            onIterationCompleted: _ => { },
            onWaiting:            _ => { });

        // Very short timeout (≈ 60 ms) so the test completes quickly.
        var config = new LoopMdConfig(
            IntervalMinutes: 0.0001,
            TimeoutMinutes:  1.0 / 1000,
            Description:     "",
            Instructions:    "test prompt");

        _ = controller.StartAsync(config, continuousContext: true);
        await promptStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(abortCalled,   Is.True,                   "abortPrompt must be called when timeout fires");
        Assert.That(errorMsg,      Does.Contain("timed out"), "onError must report the timeout message");
        Assert.That(stoppedCalled, Is.False,                  "onStopped must not also fire after timeout");
        Assert.That(controller.IsRunning, Is.False);
    }

    // ── Scenario 8: Queue item completes normally — loop continues ────────────

    /// <summary>
    /// When <c>onBeforeIteration</c> succeeds (simulating a queued item that
    /// drains and finishes without error), the loop fires
    /// <c>executePromptAsync</c> for the next iteration normally.
    /// </summary>
    [Test]
    public async Task OnBeforeIteration_ItemCompletesNormally_LoopContinues() {
        var beforeCalledTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondExecTcs   = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedTcs      = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int execCount   = 0;
        int beforeCount = 0;

        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync: (_, __) => {
                execCount++;
                if (execCount == 2) { secondExecTcs.TrySetResult(); controller!.RequestStop(); }
                return Task.CompletedTask;
            },
            abortPrompt:          () => { },
            onIterationStarted:   _ => { },
            onStopped:            () => stoppedTcs.TrySetResult(),
            onError:              _ => stoppedTcs.TrySetResult(),
            onIterationCompleted: _ => { },
            onWaiting:            _ => { },
            onBeforeIteration: () => {
                beforeCount++;
                if (beforeCount == 1) beforeCalledTcs.TrySetResult();
                return Task.CompletedTask; // queue item finishes without error
            });

        _ = controller!.StartAsync(MakeConfig(), continuousContext: true);
        await beforeCalledTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await secondExecTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(execCount,   Is.GreaterThanOrEqualTo(2), "loop must reach a second iteration");
        Assert.That(beforeCount, Is.GreaterThanOrEqualTo(2), "onBeforeIteration fires before every iteration");
        Assert.That(controller.IsRunning, Is.False);
    }

    // ── Scenario 9: Exception escapes onBeforeIteration — loop must survive ──

    /// <summary>
    /// If an exception escapes <c>onBeforeIteration</c> (e.g. an
    /// <see cref="OperationCanceledException"/> from an aborted queue item whose
    /// cancellation was not swallowed inside the delegate), the loop must catch
    /// it, skip the current iteration, and continue to the next one rather than
    /// stopping.
    ///
    /// <c>LoopController.RunLoopAsync</c> wraps the
    /// <c>await _onBeforeIteration()</c> call in a try/catch that swallows the
    /// exception and calls <c>continue</c> — unless a stop/cancel was already
    /// requested, in which case it breaks cleanly.
    /// </summary>
    [Test]
    public async Task OnBeforeIteration_ItemAbortedMidDrain_LoopContinuesToNextIteration() {
        var firstExecTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedTcs   = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int execCount    = 0;
        int beforeCount  = 0;

        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync: (_, __) => {
                execCount++;
                firstExecTcs.TrySetResult();
                controller!.RequestStop();
                return Task.CompletedTask;
            },
            abortPrompt:          () => { },
            onIterationStarted:   _ => { },
            onStopped:            () => stoppedTcs.TrySetResult(),
            onError:              _ => stoppedTcs.TrySetResult(),
            onIterationCompleted: _ => { },
            onWaiting:            _ => { },
            // First call: exception escapes (simulates missing inner catch).
            // Subsequent calls: return normally (queue is empty).
            onBeforeIteration: () => {
                beforeCount++;
                if (beforeCount == 1)
                    return Task.FromException(new OperationCanceledException("queue item aborted"));
                return Task.CompletedTask;
            });

        _ = controller!.StartAsync(MakeConfig(), continuousContext: true);
        await firstExecTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(execCount,   Is.GreaterThanOrEqualTo(1), "loop resumes and fires executePromptAsync after exception");
        Assert.That(beforeCount, Is.GreaterThanOrEqualTo(2), "onBeforeIteration retried after the exception call");
        Assert.That(controller.IsRunning, Is.False);
    }

    // ── Scenario 10: Queue drain with internal abort swallow — loop resumes ───

    /// <summary>
    /// Mirrors what <c>DrainQueueBeforeLoopIterationAsync</c> actually does:
    /// exceptions from dispatching queue items are caught <b>inside</b> the
    /// delegate and never propagate to <c>LoopController</c>.  From the
    /// controller's perspective <c>onBeforeIteration</c> always returns
    /// normally, so the loop continues to fire <c>executePromptAsync</c> for
    /// every iteration regardless of whether individual queue items complete
    /// cleanly or are aborted.
    ///
    /// Covers Case 1 (single queued item aborted) and Case 2 (multiple queued
    /// items aborted one-by-one) from the original scenario description.
    /// </summary>
    [Test]
    public async Task QueueDrain_ThenAbortQueueItem_LoopResumesAfterQueueEmpty() {
        var thirdIterTcs  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedTcs    = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int beforeCount   = 0;
        int execCount     = 0;

        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync: (_, __) => {
                execCount++;
                if (execCount == 3) { thirdIterTcs.TrySetResult(); controller!.RequestStop(); }
                return Task.CompletedTask;
            },
            abortPrompt:          () => { },
            onIterationStarted:   _ => { },
            onStopped:            () => stoppedTcs.TrySetResult(),
            onError:              _ => stoppedTcs.TrySetResult(),
            onIterationCompleted: _ => { },
            onWaiting:            _ => { },
            // Mirrors DrainQueueBeforeLoopIterationAsync: for the first two calls
            // a queued item is "dispatched" and then aborted — the exception is
            // caught internally so it never reaches LoopController.  The third
            // call represents an empty queue and returns immediately.
            onBeforeIteration: async () => {
                beforeCount++;
                if (beforeCount <= 2) {
                    try {
                        // Simulate dispatching a queued item that gets aborted.
                        await Task.FromException(
                            new OperationCanceledException("queue item aborted by user"));
                    }
                    catch {
                        // Swallow — mirrors the catch in DrainQueueBeforeLoopIterationAsync.
                    }
                }
                // beforeCount >= 3: queue is empty, return immediately.
            });

        _ = controller!.StartAsync(MakeConfig(), continuousContext: true);
        await thirdIterTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(execCount,   Is.GreaterThanOrEqualTo(3),
            "loop fires executePromptAsync for every iteration even when queue items are aborted");
        Assert.That(beforeCount, Is.GreaterThanOrEqualTo(3),
            "onBeforeIteration fires before every iteration");
        Assert.That(controller.IsRunning, Is.False);
    }
}
