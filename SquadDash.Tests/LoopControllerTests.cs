using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Unit tests for <see cref="LoopController"/> stop/abort state machine.
/// No WPF dependency — all callbacks are synchronous in-process delegates.
/// </summary>
[TestFixture]
internal sealed class LoopControllerTests {

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Config with a near-zero interval so the delay between iterations
    /// completes in milliseconds during tests.
    /// </summary>
    private static LoopMdConfig MakeConfig(double intervalMinutes = 0.0001)
        => new(intervalMinutes, TimeoutMinutes: 5, Description: "", Instructions: "test prompt");

    // ── RequestStop ───────────────────────────────────────────────────────────

    [Test]
    public async Task RequestStop_WhileIterationRunning_ExitsAfterIterationAndCallsOnStopped() {
        // Arrange
        var iterStartedTcs  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var promptTcs       = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedTcs      = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int completedCount  = 0;
        bool stoppedCalled  = false;
        string? errorMsg    = null;

        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync:   (_, __) => { iterStartedTcs.TrySetResult(); return promptTcs.Task; },
            abortPrompt:          () => { },
            onIterationStarted:   _ => { },
            onStopped:            () => { stoppedCalled = true; stoppedTcs.TrySetResult(); },
            onError:              msg => { errorMsg = msg; stoppedTcs.TrySetResult(); },
            onIterationCompleted: _ => completedCount++);

        // Act
        _ = controller!.StartAsync(MakeConfig(), continuousContext: true);
        await iterStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(controller.IsRunning, Is.True);
        Assert.That(controller.StopState, Is.EqualTo(LoopStopState.None));

        controller.RequestStop();

        Assert.That(controller.StopState, Is.EqualTo(LoopStopState.StopRequested));

        promptTcs.SetResult(); // release the running iteration

        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.That(stoppedCalled,  Is.True,  "onStopped should have been called");
        Assert.That(errorMsg,       Is.Null,  "onError should NOT be called on a graceful stop");
        Assert.That(completedCount, Is.EqualTo(1), "exactly one iteration should have completed");
        Assert.That(controller.IsRunning, Is.False);
    }

    // ── RequestAbort ──────────────────────────────────────────────────────────

    [Test]
    public async Task RequestAbort_CallsAbortPromptAndOnErrorWithAbortedMessage() {
        // Arrange
        var iterStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishedTcs    = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var promptCts      = new CancellationTokenSource();
        bool abortCalled   = false;
        string? errorMsg   = null;
        bool stoppedCalled = false;

        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync: async (_, __) => {
                iterStartedTcs.TrySetResult();
                // Simulate a long-running prompt that respects external cancellation.
                await Task.Delay(Timeout.InfiniteTimeSpan, promptCts.Token);
            },
            abortPrompt: () => {
                abortCalled = true;
                promptCts.Cancel(); // makes executePromptAsync throw OperationCanceledException
            },
            onIterationStarted:   _ => { },
            onStopped:            () => { stoppedCalled = true; finishedTcs.TrySetResult(); },
            onError:              msg => { errorMsg = msg; finishedTcs.TrySetResult(); },
            onIterationCompleted: _ => { });

        // Act
        _ = controller!.StartAsync(MakeConfig(), continuousContext: true);
        await iterStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        controller.RequestAbort();

        await finishedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.That(abortCalled,   Is.True,                          "abortPrompt delegate must be called");
        Assert.That(errorMsg,      Is.EqualTo("Loop aborted."),      "onError must receive the abort message");
        Assert.That(stoppedCalled, Is.False,                          "onStopped must NOT be called on abort");
        Assert.That(controller.IsRunning, Is.False);
    }

    // ── State transitions ─────────────────────────────────────────────────────

    [Test]
    public async Task StopState_TransitionsNoneToStopRequestedThenIdle() {
        var iterStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var promptTcs      = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedTcs     = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync:   (_, __) => { iterStartedTcs.TrySetResult(); return promptTcs.Task; },
            abortPrompt:          () => { },
            onIterationStarted:   _ => { },
            onStopped:            () => stoppedTcs.TrySetResult(),
            onError:              _ => stoppedTcs.TrySetResult(),
            onIterationCompleted: _ => { });

        _ = controller!.StartAsync(MakeConfig(), continuousContext: true);
        await iterStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(controller.StopState, Is.EqualTo(LoopStopState.None));

        controller.RequestStop();
        Assert.That(controller.StopState, Is.EqualTo(LoopStopState.StopRequested));

        promptTcs.SetResult();
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(controller.IsRunning, Is.False);
    }

    [Test]
    public async Task StopState_TransitionsNoneToAborted() {
        var iterStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishedTcs    = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var promptCts      = new CancellationTokenSource();

        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync: async (_, __) => {
                iterStartedTcs.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, promptCts.Token);
            },
            abortPrompt:          () => promptCts.Cancel(),
            onIterationStarted:   _ => { },
            onStopped:            () => finishedTcs.TrySetResult(),
            onError:              _ => finishedTcs.TrySetResult(),
            onIterationCompleted: _ => { });

        Assert.That(controller!.StopState, Is.EqualTo(LoopStopState.None));

        _ = controller.StartAsync(MakeConfig(), continuousContext: true);
        await iterStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        controller.RequestAbort();
        Assert.That(controller.StopState, Is.EqualTo(LoopStopState.Aborted));

        await finishedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(controller.IsRunning, Is.False);
    }

    // ── IsRunning lifecycle ───────────────────────────────────────────────────

    [Test]
    public async Task IsRunning_FalseBeforeStart_TrueDuringLoop_FalseAfterStop() {
        var iterStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var promptTcs      = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedTcs     = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync:   (_, __) => { iterStartedTcs.TrySetResult(); return promptTcs.Task; },
            abortPrompt:          () => { },
            onIterationStarted:   _ => { },
            onStopped:            () => stoppedTcs.TrySetResult(),
            onError:              _ => stoppedTcs.TrySetResult(),
            onIterationCompleted: _ => { });

        Assert.That(controller!.IsRunning, Is.False, "must not be running before start");

        _ = controller.StartAsync(MakeConfig(), continuousContext: false);
        await iterStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(controller.IsRunning, Is.True, "must be running during iteration");

        controller.RequestStop();
        promptTcs.SetResult();
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(controller.IsRunning, Is.False, "must not be running after stop");
    }
}
