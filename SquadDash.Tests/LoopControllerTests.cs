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
            onIterationCompleted: _ => completedCount++,
            onWaiting:            _ => { });

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
            onIterationCompleted: _ => { },
            onWaiting:            _ => { });

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
            onIterationCompleted: _ => { },
            onWaiting:            _ => { });

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
            onIterationCompleted: _ => { },
            onWaiting:            _ => { });

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
            onIterationCompleted: _ => { },
            onWaiting:            _ => { });

        Assert.That(controller!.IsRunning, Is.False, "must not be running before start");

        _ = controller.StartAsync(MakeConfig(), continuousContext: false);
        await iterStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(controller.IsRunning, Is.True, "must be running during iteration");

        controller.RequestStop();
        promptTcs.SetResult();
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(controller.IsRunning, Is.False, "must not be running after stop");
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Test]
    public async Task Timeout_ExceedingIterationLimit_AbortsPromptAndReportsError() {
        // Arrange
        var promptStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorTcs         = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedTcs       = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var promptCts        = new CancellationTokenSource();
        bool abortCalled     = false;
        string? errorMsg     = null;
        bool stoppedCalled   = false;

        var controller = new LoopController(
            executePromptAsync: async (_, __) => {
                promptStartedTcs.TrySetResult();
                // Simulate a prompt that only ends when externally aborted.
                await Task.Delay(Timeout.InfiniteTimeSpan, promptCts.Token);
            },
            abortPrompt: () => {
                abortCalled = true;
                promptCts.Cancel(); // unblocks executePromptAsync
            },
            onIterationStarted:   _ => { },
            onStopped:            () => { stoppedCalled = true; stoppedTcs.TrySetResult(); },
            onError:              msg => { errorMsg = msg; errorTcs.TrySetResult(); },
            onIterationCompleted: _ => { },
            onWaiting:            _ => { });

        // Use a very short timeout (≈ 60 ms) so the test completes quickly.
        var config = new LoopMdConfig(
            IntervalMinutes: 0.0001,
            TimeoutMinutes:  1.0 / 1000,   // ≈ 60 ms
            Description:     "",
            Instructions:    "test prompt");

        // Act
        _ = controller.StartAsync(config, continuousContext: true);
        await promptStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Wait for both onError (timeout message) and onStopped (loop reset) to fire.
        await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.That(abortCalled,   Is.True,                       "abortPrompt must be called on timeout");
        Assert.That(errorMsg,      Does.Contain("timed out"),     "onError must report timeout");
        Assert.That(stoppedCalled, Is.True,                       "onStopped fires in finally to reset loop state");
        Assert.That(controller.IsRunning, Is.False);
    }

    [Test]
    public async Task Timeout_PromptCompletesBeforeDeadline_LoopContinues() {
        // Arrange — prompt completes instantly; timeout is generous; loop should run 2+ iterations
        var stoppedTcs  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int execCount   = 0;

        var controller = new LoopController(
            executePromptAsync:   (_, __) => { execCount++; return Task.CompletedTask; },
            abortPrompt:          () => { },
            onIterationStarted:   _ => { },
            onStopped:            () => stoppedTcs.TrySetResult(),
            onError:              _  => stoppedTcs.TrySetResult(),
            onIterationCompleted: _  => { },
            onWaiting:            _  => { });

        var config = new LoopMdConfig(
            IntervalMinutes: 0.0001,
            TimeoutMinutes:  5,
            Description:     "",
            Instructions:    "test prompt");

        // Act
        _ = controller.StartAsync(config, continuousContext: true);
        await Task.Delay(150); // let it run a few fast iterations
        controller.RequestStop();
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — must have run at least 2 iterations with no abort
        Assert.That(execCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(controller.IsRunning, Is.False);
    }

    // ── onBeforeIteration ─────────────────────────────────────────────────────

    [Test]
    public async Task OnBeforeIteration_IsCalledBeforeEachIteration() {
        var callOrder       = new System.Collections.Generic.List<string>();
        var stoppedTcs      = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int iterCount       = 0;

        var controller = new LoopController(
            executePromptAsync:   (_, __) => { callOrder.Add($"exec{++iterCount}"); return Task.CompletedTask; },
            abortPrompt:          () => { },
            onIterationStarted:   n  => callOrder.Add($"started{n}"),
            onStopped:            () => stoppedTcs.TrySetResult(),
            onError:              _  => stoppedTcs.TrySetResult(),
            onIterationCompleted: _  => { },
            onWaiting:            _  => { },
            onBeforeIteration:    () => { callOrder.Add("before"); return Task.CompletedTask; });

        _ = controller.StartAsync(MakeConfig(), continuousContext: true);
        // Let it run one iteration then stop.
        await Task.Delay(100);
        controller.RequestStop();
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 'before' must appear immediately before each 'started' in the call log.
        for (int i = 0; i < callOrder.Count - 1; i++) {
            if (callOrder[i] == "before")
                Assert.That(callOrder[i + 1], Does.StartWith("started"),
                    "onBeforeIteration must be followed immediately by onIterationStarted");
        }
        Assert.That(callOrder, Does.Contain("before"), "onBeforeIteration must fire at least once");
    }

    [Test]
    public async Task OnBeforeIteration_StopRequestedDuringHook_LoopExitsWithoutExecutingIteration() {
        var stoppedTcs      = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int execCount       = 0;

        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync:   (_, __) => { execCount++; return Task.CompletedTask; },
            abortPrompt:          () => { },
            onIterationStarted:   _  => { },
            onStopped:            () => stoppedTcs.TrySetResult(),
            onError:              _  => stoppedTcs.TrySetResult(),
            onIterationCompleted: _  => { },
            onWaiting:            _  => { },
            onBeforeIteration: () => {
                controller!.RequestStop(); // stop during pre-iteration hook
                return Task.CompletedTask;
            });

        _ = controller!.StartAsync(MakeConfig(), continuousContext: true);
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(execCount, Is.EqualTo(0), "loop should exit before executing the iteration prompt");
        Assert.That(controller.IsRunning, Is.False);
    }

    // ── onBeforeWait ──────────────────────────────────────────────────────────

    [Test]
    public async Task OnBeforeWait_IsCalledAfterEachIterationAndBeforeDelay() {
        var callOrder  = new System.Collections.Generic.List<string>();
        var stoppedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int iterCount  = 0;

        var controller = new LoopController(
            executePromptAsync:   (_, __) => { callOrder.Add($"exec{++iterCount}"); return Task.CompletedTask; },
            abortPrompt:          () => { },
            onIterationStarted:   n  => callOrder.Add($"started{n}"),
            onStopped:            () => stoppedTcs.TrySetResult(),
            onError:              _  => stoppedTcs.TrySetResult(),
            onIterationCompleted: n  => callOrder.Add($"completed{n}"),
            onWaiting:            _  => callOrder.Add("waiting"),
            onBeforeWait:         () => { callOrder.Add("beforeWait"); return Task.CompletedTask; });

        _ = controller.StartAsync(MakeConfig(), continuousContext: true);
        await Task.Delay(100); // allow at least one full iteration
        controller.RequestStop();
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // After each completed iteration: beforeWait must appear before waiting
        for (int i = 0; i < callOrder.Count - 1; i++) {
            if (callOrder[i] == "beforeWait")
                Assert.That(callOrder[i + 1], Is.EqualTo("waiting"),
                    "onBeforeWait must be immediately followed by onWaiting");
        }
        Assert.That(callOrder, Does.Contain("beforeWait"), "onBeforeWait must fire at least once");
    }

    [Test]
    public async Task OnBeforeWait_StopRequestedDuringHook_LoopExitsBeforeDelay() {
        var stoppedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool waitingFired = false;

        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync:   (_, __) => Task.CompletedTask,
            abortPrompt:          () => { },
            onIterationStarted:   _  => { },
            onStopped:            () => stoppedTcs.TrySetResult(),
            onError:              _  => stoppedTcs.TrySetResult(),
            onIterationCompleted: _  => { },
            onWaiting:            _  => { waitingFired = true; },
            onBeforeWait: () => {
                controller!.RequestStop();
                return Task.CompletedTask;
            });

        _ = controller!.StartAsync(MakeConfig(), continuousContext: true);
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(waitingFired,          Is.False, "onWaiting must not fire when stopped in onBeforeWait");
        Assert.That(controller.IsRunning,  Is.False);
    }

    // ── StartAsync no-op guard ────────────────────────────────────────────────

    [Test]
    public async Task StartAsync_WhenAlreadyRunning_DoesNotStartSecondLoop() {
        var iterStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var promptTcs      = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedTcs     = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int execCount      = 0;

        var controller = new LoopController(
            executePromptAsync:   (_, __) => { execCount++; iterStartedTcs.TrySetResult(); return promptTcs.Task; },
            abortPrompt:          () => { },
            onIterationStarted:   _  => { },
            onStopped:            () => stoppedTcs.TrySetResult(),
            onError:              _  => stoppedTcs.TrySetResult(),
            onIterationCompleted: _  => { },
            onWaiting:            _  => { });

        _ = controller.StartAsync(MakeConfig(), continuousContext: true);
        await iterStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Second call while loop is running — must be a no-op
        _ = controller.StartAsync(MakeConfig(), continuousContext: true);

        Assert.That(controller.IsRunning, Is.True);

        controller.RequestStop();
        promptTcs.SetResult();
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(execCount, Is.EqualTo(1), "only one iteration should have been started");
        Assert.That(controller.IsRunning, Is.False);
    }

    // ── continuousContext=false ───────────────────────────────────────────────

    [Test]
    public async Task ContinuousContext_False_EachIterationReceivesDistinctSessionId() {
        var sessionIds = new System.Collections.Generic.List<string?>();
        var stoppedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var controller = new LoopController(
            executePromptAsync:   (_, sessionId) => { lock (sessionIds) sessionIds.Add(sessionId); return Task.CompletedTask; },
            abortPrompt:          () => { },
            onIterationStarted:   _  => { },
            onStopped:            () => stoppedTcs.TrySetResult(),
            onError:              _  => stoppedTcs.TrySetResult(),
            onIterationCompleted: _  => { },
            onWaiting:            _  => { });

        _ = controller.StartAsync(MakeConfig(), continuousContext: false);
        await Task.Delay(150); // let 2+ iterations run
        controller.RequestStop();
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(sessionIds.Count,        Is.GreaterThanOrEqualTo(2), "need ≥2 iterations to compare");
        Assert.That(sessionIds,              Has.None.Null,              "each session id must be non-null");
        Assert.That(sessionIds.Distinct().Count(), Is.EqualTo(sessionIds.Count), "all session ids must be unique");
    }

    [Test]
    public async Task ContinuousContext_True_AllIterationsReceiveNullSessionId() {
        var sessionIds = new System.Collections.Generic.List<string?>();
        var stoppedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var controller = new LoopController(
            executePromptAsync:   (_, sessionId) => { lock (sessionIds) sessionIds.Add(sessionId); return Task.CompletedTask; },
            abortPrompt:          () => { },
            onIterationStarted:   _  => { },
            onStopped:            () => stoppedTcs.TrySetResult(),
            onError:              _  => stoppedTcs.TrySetResult(),
            onIterationCompleted: _  => { },
            onWaiting:            _  => { });

        _ = controller.StartAsync(MakeConfig(), continuousContext: true);
        await Task.Delay(150);
        controller.RequestStop();
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(sessionIds.Count,   Is.GreaterThanOrEqualTo(2));
        Assert.That(sessionIds,         Has.All.Null, "continuousContext=true must pass null session id every time");
    }

    // ── BuildAugmentedPrompt ──────────────────────────────────────────────────

    [Test]
    public void BuildAugmentedPrompt_NullCommands_ReturnsInstructionsUnchanged() {
        const string instructions = "Run the test suite.";

        var result = LoopController.BuildAugmentedPrompt(instructions, null);

        Assert.That(result, Is.EqualTo(instructions));
    }

    [Test]
    public void BuildAugmentedPrompt_EmptyCommands_ReturnsInstructionsUnchanged() {
        const string instructions = "Run the test suite.";

        var result = LoopController.BuildAugmentedPrompt(instructions, System.Array.Empty<string>());

        Assert.That(result, Is.EqualTo(instructions));
    }

    [Test]
    public void BuildAugmentedPrompt_StopLoopCommand_AppendsSectionWithDescription() {
        var result = LoopController.BuildAugmentedPrompt("Do work.", new[] { "stop_loop" });

        Assert.That(result, Does.Contain("HOST_COMMAND_JSON"));
        Assert.That(result, Does.Contain("stop_loop"));
        Assert.That(result, Does.Contain("Stops the loop after this iteration completes."));
    }

    [Test]
    public void BuildAugmentedPrompt_StartLoopCommand_AppendsSectionWithDescription() {
        var result = LoopController.BuildAugmentedPrompt("Do work.", new[] { "start_loop" });

        Assert.That(result, Does.Contain("start_loop"));
        Assert.That(result, Does.Contain("Starts (or restarts) the SquadDash native loop."));
    }

    [Test]
    public void BuildAugmentedPrompt_UnknownCommand_AppendsCommandNameLiteral() {
        var result = LoopController.BuildAugmentedPrompt("Do work.", new[] { "custom_cmd" });

        Assert.That(result, Does.Contain("**custom_cmd**"));
    }

    [Test]
    public void BuildAugmentedPrompt_MultipleCommands_IncludesAll() {
        var result = LoopController.BuildAugmentedPrompt(
            "Do work.", new[] { "stop_loop", "start_loop", "custom_cmd" });

        Assert.That(result, Does.Contain("stop_loop"));
        Assert.That(result, Does.Contain("start_loop"));
        Assert.That(result, Does.Contain("custom_cmd"));
    }

    [Test]
    public void BuildAugmentedPrompt_CommandsCaseInsensitive_StopLoopUpperCase_StillDescribed() {
        var result = LoopController.BuildAugmentedPrompt("Do work.", new[] { "STOP_LOOP" });

        Assert.That(result, Does.Contain("Stops the loop after this iteration completes."));
    }

    [Test]
    public void BuildAugmentedPrompt_WithCommands_InstructionsAppearBeforeCommandSection() {
        const string instructions = "Do work.";
        var result = LoopController.BuildAugmentedPrompt(instructions, new[] { "stop_loop" });

        int instrIdx   = result.IndexOf(instructions,      StringComparison.Ordinal);
        int headerIdx  = result.IndexOf("HOST_COMMAND_JSON", StringComparison.Ordinal);

        Assert.That(instrIdx,  Is.GreaterThanOrEqualTo(0));
        Assert.That(headerIdx, Is.GreaterThan(instrIdx));
    }
}
