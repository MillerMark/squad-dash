using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Integration tests exercising <see cref="LoopController"/> and <see cref="PromptQueue"/>
/// together — no WPF dependency.  All coordination uses in-process delegates and
/// <see cref="TaskCompletionSource"/>.
/// </summary>
[TestFixture]
internal sealed class LoopQueueIntegrationTests {

    // ── Test Harness ──────────────────────────────────────────────────────────

    /// <summary>
    /// Self-contained coordinator that wires a <see cref="PromptQueue"/> to a
    /// <see cref="LoopController"/> with drain hooks, a shared execution log, and
    /// configurable knobs for each scenario.
    /// </summary>
    private sealed class Harness {

        private readonly PromptQueue                    _queue      = new();
        private          LoopController?               _controller;
        private readonly List<string>                  _log        = new();
        private readonly object                        _logLock    = new();
        private          int                           _maxIterations;
        private          int                           _iterationsStarted;
        private readonly CancellationTokenSource       _abortCts   = new();

        // ── knobs ─────────────────────────────────────────────────────────────

        /// <summary>
        /// If non-null, <see cref="ExecuteLoopIterationAsync"/> blocks until
        /// this TCS is resolved (or an abort fires).
        /// </summary>
        public volatile TaskCompletionSource? IterationBlockTcs;

        /// <summary>
        /// Invoked after each queue item is logged; lets a test inject actions
        /// mid-drain (e.g. enqueue another item, call RequestStop).
        /// </summary>
        public Func<PromptQueueItem, Task>? OnQueueItemDispatch { get; set; }

        /// <summary>
        /// Invoked inside <c>onIterationCompleted</c> before the stop-after-N check.
        /// </summary>
        public Action<int>? OnIterationCompleted { get; set; }

        /// <summary>When true, <c>onBeforeWait</c> also drains the queue.</summary>
        public bool DrainOnBeforeWait { get; set; }

        /// <summary>
        /// When true, remaining queue items are drained inside <c>onStopped</c>
        /// (models the AfterAllQueued shutdown mode).
        /// </summary>
        public bool DrainOnStopped { get; set; }

        /// <summary>
        /// When true, <see cref="DrainQueueAsync"/> aborts immediately at the top
        /// of its loop without dequeuing further items (models a restart-pending
        /// flag that halts mid-drain).
        /// </summary>
        public volatile bool RestartPending;

        // ── observables ───────────────────────────────────────────────────────

        public LoopController                      Controller => _controller!;
        public readonly TaskCompletionSource       DoneTcs    =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // ── log accessors ─────────────────────────────────────────────────────

        public bool ContainsLog(string entry) {
            lock (_logLock) return _log.Contains(entry);
        }

        /// <summary>Returns a snapshot of the log at this point in time.</summary>
        public List<string> GetLog() {
            lock (_logLock) return new List<string>(_log);
        }

        // ── setup ─────────────────────────────────────────────────────────────

        public void Setup(int maxIterations = 3) {
            _maxIterations = maxIterations;

            _controller = new LoopController(
                executePromptAsync:   ExecuteLoopIterationAsync,
                abortPrompt:          () => _abortCts.Cancel(),
                onIterationStarted:   _ => { },
                onStopped:            OnLoopStopped,
                onError:              msg => { AddLog($"error:{msg}"); DoneTcs.TrySetResult(); },
                onIterationCompleted: n => {
                    OnIterationCompleted?.Invoke(n);
                    if (n >= _maxIterations)
                        _controller!.RequestStop();
                },
                onWaiting:            _ => { },
                onBeforeIteration:    DrainQueueAsync,
                onBeforeWait:         () => DrainOnBeforeWait
                                               ? DrainQueueAsync()
                                               : Task.CompletedTask);
        }

        // ── callbacks ─────────────────────────────────────────────────────────

        private void OnLoopStopped() {
            AddLog("stopped");
            // AfterAllQueued model: drain remaining items after the loop halts.
            if (DrainOnStopped) {
                while (_queue.HasReadyItems) {
                    var item = _queue.DequeueFirstReady();
                    if (item is null) break;
                    AddLog($"queue:{item.Text}");
                }
            }
            DoneTcs.TrySetResult();
        }

        private async Task ExecuteLoopIterationAsync(string _, string? __) {
            var n = Interlocked.Increment(ref _iterationsStarted);
            AddLog($"loop:{n}");

            var tcs = IterationBlockTcs;
            if (tcs is not null) {
                try {
                    await Task.WhenAny(
                        tcs.Task,
                        Task.Delay(Timeout.InfiniteTimeSpan, _abortCts.Token));
                }
                catch (OperationCanceledException) { /* abort path — fall through */ }

                // Propagate as OCE so LoopController's abort path fires.
                _abortCts.Token.ThrowIfCancellationRequested();
            }
        }

        private async Task DrainQueueAsync() {
            while (_queue.HasReadyItems) {
                if (RestartPending) break;
                var item = _queue.DequeueFirstReady();
                if (item is null) break;
                AddLog($"queue:{item.Text}");
                if (OnQueueItemDispatch is { } hook)
                    await hook(item);
            }
        }

        private void AddLog(string entry) { lock (_logLock) _log.Add(entry); }

        // ── convenience ───────────────────────────────────────────────────────

        public void Enqueue(string text, int seqNum = 1) =>
            _queue.Enqueue(text, seqNum);

        public Task StartAsync() =>
            _controller!.StartAsync(
                new LoopMdConfig(
                    IntervalMinutes: 0.0001,
                    TimeoutMinutes:  5,
                    Description:     "",
                    Instructions:    "loop-prompt"),
                continuousContext: true);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static async Task WaitUntilLogContains(
        Harness harness,
        string  entry,
        int     timeoutMs = 5_000) {

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline) {
            if (harness.ContainsLog(entry)) return;
            await Task.Delay(10);
        }
        Assert.Fail(
            $"Timed out waiting for log entry '{entry}'. " +
            $"Current log: [{string.Join(", ", harness.GetLog())}]");
    }

    private static async Task WaitUntilLogContains(
        ResumeHarness harness,
        string        entry,
        int           timeoutMs = 5_000) {

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline) {
            if (harness.ContainsLog(entry)) return;
            await Task.Delay(10);
        }
        Assert.Fail(
            $"Timed out waiting for log entry '{entry}'. " +
            $"Current log: [{string.Join(", ", harness.GetLog())}]");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Basic ordering
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task BasicOrdering_QueueItemsDrainBeforeFirstIteration() {
        var h = new Harness();
        h.Setup(maxIterations: 1);
        h.Enqueue("a", 1);
        h.Enqueue("b", 2);

        await h.StartAsync();
        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log = h.GetLog();
        Assert.That(log.IndexOf("queue:a"), Is.LessThan(log.IndexOf("loop:1")),
            "'queue:a' must appear before 'loop:1'");
        Assert.That(log.IndexOf("queue:b"), Is.LessThan(log.IndexOf("loop:1")),
            "'queue:b' must appear before 'loop:1'");
    }

    [Test]
    public async Task BasicOrdering_QueueItemAddedMidLoop_DrainsBeforeNextIteration() {
        var h        = new Harness();
        h.Setup(maxIterations: 2);
        var blockTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.IterationBlockTcs = blockTcs;

        await h.StartAsync();
        await WaitUntilLogContains(h, "loop:1");

        h.Enqueue("mid", 1);
        // Clear the block so iteration 2 runs freely, then release iteration 1.
        h.IterationBlockTcs = null;
        blockTcs.SetResult();

        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log = h.GetLog();
        Assert.That(log.IndexOf("queue:mid"), Is.GreaterThan(log.IndexOf("loop:1")),
            "'queue:mid' must appear after 'loop:1'");
        Assert.That(log.IndexOf("queue:mid"), Is.LessThan(log.IndexOf("loop:2")),
            "'queue:mid' must appear before 'loop:2'");
    }

    [Test]
    public async Task BasicOrdering_NoQueueItems_LoopRunsUninterrupted() {
        var h = new Harness();
        h.Setup(maxIterations: 3);

        await h.StartAsync();
        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log = h.GetLog();
        Assert.That(log.Where(e => e.StartsWith("queue:")), Is.Empty,
            "no queue entries should appear");
        Assert.That(log, Contains.Item("loop:1")
                            .And.Contains("loop:2")
                            .And.Contains("loop:3"),
            "all three iterations must fire");
    }

    [Test]
    public async Task BasicOrdering_QueueItemsDrainInFifoOrder() {
        var h = new Harness();
        h.Setup(maxIterations: 1);
        h.Enqueue("first",  1);
        h.Enqueue("second", 2);
        h.Enqueue("third",  3);

        await h.StartAsync();
        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log    = h.GetLog();
        var qFirst  = log.IndexOf("queue:first");
        var qSecond = log.IndexOf("queue:second");
        var qThird  = log.IndexOf("queue:third");

        Assert.That(qFirst,  Is.GreaterThanOrEqualTo(0), "'queue:first' must appear");
        Assert.That(qSecond, Is.GreaterThan(qFirst),     "'queue:second' must follow 'queue:first'");
        Assert.That(qThird,  Is.GreaterThan(qSecond),    "'queue:third' must follow 'queue:second'");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Loop + queue interleaving
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Interleaving_ItemAddedWhileIterationRunning_DrainsBeforeNextIteration() {
        var h        = new Harness();
        h.Setup(maxIterations: 2);
        var blockTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.IterationBlockTcs = blockTcs;

        await h.StartAsync();
        await WaitUntilLogContains(h, "loop:1");

        h.Enqueue("x", 1);
        h.IterationBlockTcs = null;
        blockTcs.SetResult();

        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log = h.GetLog();
        Assert.That(log.IndexOf("queue:x"), Is.GreaterThan(log.IndexOf("loop:1")),
            "'queue:x' must be after 'loop:1'");
        Assert.That(log.IndexOf("queue:x"), Is.LessThan(log.IndexOf("loop:2")),
            "'queue:x' must be before 'loop:2'");
    }

    [Test]
    public async Task Interleaving_MultipleItemsAddedDuringIteration_AllDrainBeforeNextIteration() {
        var h        = new Harness();
        h.Setup(maxIterations: 2);
        var blockTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.IterationBlockTcs = blockTcs;

        await h.StartAsync();
        await WaitUntilLogContains(h, "loop:1");

        h.Enqueue("p", 1);
        h.Enqueue("q", 2);
        h.Enqueue("r", 3);
        h.IterationBlockTcs = null;
        blockTcs.SetResult();

        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log   = h.GetLog();
        int loop2 = log.IndexOf("loop:2");
        Assert.That(loop2, Is.GreaterThan(0));
        Assert.That(log.IndexOf("queue:p"), Is.LessThan(loop2), "'queue:p' before loop:2");
        Assert.That(log.IndexOf("queue:q"), Is.LessThan(loop2), "'queue:q' before loop:2");
        Assert.That(log.IndexOf("queue:r"), Is.LessThan(loop2), "'queue:r' before loop:2");
    }

    [Test]
    public async Task Interleaving_NewItemArrivesEveryIteration_QueueThenLoopPattern() {
        // Enqueue item1 before start; enqueue item(n+1) in onIterationCompleted for each n.
        // Expected pattern: queue:item1, loop:1, queue:item2, loop:2, queue:item3, loop:3.
        var h          = new Harness();
        h.Setup(maxIterations: 3);
        h.Enqueue("item1", 1);
        int addedCount = 1;
        h.OnIterationCompleted = n => {
            if (n < 3) {
                addedCount++;
                h.Enqueue($"item{addedCount}", addedCount);
            }
        };

        await h.StartAsync();
        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log = h.GetLog();
        Assert.That(log.IndexOf("queue:item1"), Is.LessThan(log.IndexOf("loop:1")),
            "item1 must drain before loop:1");
        Assert.That(log.IndexOf("queue:item2"), Is.GreaterThan(log.IndexOf("loop:1")),
            "item2 must appear after loop:1");
        Assert.That(log.IndexOf("queue:item2"), Is.LessThan(log.IndexOf("loop:2")),
            "item2 must drain before loop:2");
        Assert.That(log.IndexOf("queue:item3"), Is.GreaterThan(log.IndexOf("loop:2")),
            "item3 must appear after loop:2");
        Assert.That(log.IndexOf("queue:item3"), Is.LessThan(log.IndexOf("loop:3")),
            "item3 must drain before loop:3");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Stop / abort during queue drain
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task StopDuringDrain_LoopExitsWithoutFiringAnyIteration() {
        // RequestStop() is called inside the drain of item "a".
        // The drain function does not short-circuit on _stopRequested, so "b" also
        // drains; but after onBeforeIteration returns the loop checks the flag and exits.
        var h = new Harness();
        h.Setup(maxIterations: 99);
        h.Enqueue("a", 1);
        h.Enqueue("b", 2);
        h.OnQueueItemDispatch = item => {
            if (item.Text == "a")
                h.Controller.RequestStop();
            return Task.CompletedTask;
        };

        await h.StartAsync();
        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log = h.GetLog();
        Assert.That(log, Does.Not.Contain("loop:1"),
            "no loop iteration must fire after stop is requested during pre-iteration drain");
        Assert.That(log, Contains.Item("stopped"));
    }

    [Test]
    public async Task StopAfterIteration_EnqueuedItemDrainsThenLoopExits() {
        // After iteration 1 completes, enqueue "x".
        // onBeforeIteration for iteration 2 drains "x" and calls RequestStop inside
        // OnQueueItemDispatch — so the loop exits after draining without firing loop:2.
        var h = new Harness();
        h.Setup(maxIterations: 99);
        h.OnIterationCompleted = n => {
            if (n == 1) h.Enqueue("x", 1);
        };
        h.OnQueueItemDispatch = _ => {
            h.Controller.RequestStop();
            return Task.CompletedTask;
        };

        await h.StartAsync();
        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log = h.GetLog();
        Assert.That(log, Contains.Item("queue:x"),  "'queue:x' must be dispatched");
        Assert.That(log, Does.Not.Contain("loop:2"), "loop:2 must not fire");
        Assert.That(log, Contains.Item("stopped"));
        Assert.That(log.IndexOf("queue:x"), Is.GreaterThan(log.IndexOf("loop:1")),
            "'queue:x' must appear after 'loop:1'");
        Assert.That(log.IndexOf("queue:x"), Is.LessThan(log.IndexOf("stopped")),
            "'queue:x' must appear before 'stopped'");
    }

    [Test]
    public async Task Abort_DuringLoopIteration_QueueItemsNotDispatched() {
        // Items are added while iteration 1 is in-flight; aborting mid-iteration
        // means onBeforeIteration for iteration 2 never runs — items stay undispatched.
        var h        = new Harness();
        h.Setup(maxIterations: 99);
        var blockTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.IterationBlockTcs = blockTcs;

        await h.StartAsync();
        await WaitUntilLogContains(h, "loop:1");

        h.Enqueue("a", 1);
        h.Enqueue("b", 2);
        h.Controller.RequestAbort();

        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log = h.GetLog();
        Assert.That(log, Does.Not.Contain("queue:a"), "'queue:a' must not be dispatched");
        Assert.That(log, Does.Not.Contain("queue:b"), "'queue:b' must not be dispatched");
        Assert.That(log, Contains.Item("error:Loop aborted."));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Shutdown-like behaviours
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Shutdown_AfterCurrentTurn_QueueItemsNotExecuted() {
        // Simulate DeferredShutdownMode.AfterCurrentTurn: stop loop after the
        // current iteration without draining remaining queued items.
        var h        = new Harness();
        h.Setup(maxIterations: 99);
        var blockTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.IterationBlockTcs = blockTcs;

        await h.StartAsync();
        await WaitUntilLogContains(h, "loop:1");

        // Enqueue items while iteration 1 is running.
        h.Enqueue("a", 1);
        h.Enqueue("b", 2);

        // AfterCurrentTurn: stop the loop as soon as iteration 1 completes.
        h.OnIterationCompleted = _ => h.Controller.RequestStop();

        h.IterationBlockTcs = null;
        blockTcs.SetResult();

        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log = h.GetLog();
        Assert.That(log, Does.Not.Contain("queue:a"),
            "AfterCurrentTurn: 'queue:a' must not execute after graceful stop");
        Assert.That(log, Does.Not.Contain("queue:b"),
            "AfterCurrentTurn: 'queue:b' must not execute after graceful stop");
        Assert.That(log, Contains.Item("stopped"));
    }

    [Test]
    public async Task Shutdown_AfterAllQueued_RemainingItemsDrainAfterLoopStops() {
        // Simulate DeferredShutdownMode.AfterAllQueued: drain remaining items in
        // onStopped after the loop exits — items execute after "stopped" is logged.
        var h        = new Harness();
        h.Setup(maxIterations: 99);
        h.DrainOnStopped = true;
        var blockTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.IterationBlockTcs = blockTcs;

        await h.StartAsync();
        await WaitUntilLogContains(h, "loop:1");

        h.Enqueue("a", 1);
        h.Enqueue("b", 2);
        h.OnIterationCompleted = _ => h.Controller.RequestStop();

        h.IterationBlockTcs = null;
        blockTcs.SetResult();

        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log        = h.GetLog();
        int stoppedIdx = log.IndexOf("stopped");
        Assert.That(stoppedIdx, Is.GreaterThanOrEqualTo(0), "'stopped' must appear");
        Assert.That(log.IndexOf("queue:a"), Is.GreaterThan(stoppedIdx),
            "'queue:a' must drain after loop stops");
        Assert.That(log.IndexOf("queue:b"), Is.GreaterThan(stoppedIdx),
            "'queue:b' must drain after loop stops");
    }

    [Test]
    public async Task Shutdown_RestartPending_CurrentItemFinishes_SubsequentNotDispatched() {
        // Simulate a rebuild-triggered restart: RestartPending is set mid-drain,
        // halting further dispatch while allowing the in-progress item to complete.
        var h = new Harness();
        h.Setup(maxIterations: 1);
        h.Enqueue("a", 1);
        h.Enqueue("b", 2);

        h.OnQueueItemDispatch = item => {
            if (item.Text == "a")
                h.RestartPending = true;
            return Task.CompletedTask;
        };

        await h.StartAsync();
        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log = h.GetLog();
        Assert.That(log, Contains.Item("queue:a"), "'a' must complete dispatch");
        Assert.That(log, Does.Not.Contain("queue:b"),
            "'b' must not dispatch when restart is pending");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Edge cases
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task EdgeCase_EmptyQueue_NoQueueEntriesInLog() {
        var h = new Harness();
        h.Setup(maxIterations: 3);

        await h.StartAsync();
        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log = h.GetLog();
        Assert.That(log.Where(e => e.StartsWith("queue:")), Is.Empty,
            "no queue entries should appear when queue is always empty");
        Assert.That(log, Contains.Item("loop:1"), "loop must still run");
    }

    [Test]
    public async Task EdgeCase_ItemEnqueuedWhileDraining_AlsoDispatchedBeforeIteration() {
        // During drain of "first", "second" is enqueued.  Because the drain while-loop
        // re-checks HasReadyItems after each item, "second" is also drained in the
        // same pre-iteration pass.
        var h = new Harness();
        h.Setup(maxIterations: 1);
        h.Enqueue("first", 1);
        h.OnQueueItemDispatch = item => {
            if (item.Text == "first")
                h.Enqueue("second", 2);
            return Task.CompletedTask;
        };

        await h.StartAsync();
        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log = h.GetLog();
        Assert.That(log.IndexOf("queue:first"),  Is.GreaterThanOrEqualTo(0));
        Assert.That(log.IndexOf("queue:second"), Is.GreaterThanOrEqualTo(0));
        Assert.That(log.IndexOf("queue:first"),  Is.LessThan(log.IndexOf("loop:1")),
            "'queue:first' must precede loop:1");
        Assert.That(log.IndexOf("queue:second"), Is.LessThan(log.IndexOf("loop:1")),
            "'queue:second' must precede loop:1 (drained in same pass)");
        Assert.That(log.IndexOf("queue:second"), Is.GreaterThan(log.IndexOf("queue:first")),
            "'queue:second' must follow 'queue:first'");
    }

    [Test]
    public async Task EdgeCase_StopRequestedBeforeStart_LoopStartsCleanly() {
        // StartAsync resets _stopRequested = false, so a pre-start RequestStop is
        // a no-op for the loop's execution — the loop must still run.
        var h = new Harness();
        h.Setup(maxIterations: 1);

        h.Controller.RequestStop();
        Assert.That(h.Controller.StopState, Is.EqualTo(LoopStopState.StopRequested),
            "StopState should reflect the pre-start request");

        await h.StartAsync(); // resets _stopRequested internally
        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log = h.GetLog();
        Assert.That(log, Contains.Item("loop:1"),
            "loop must execute despite pre-start RequestStop (StartAsync resets the flag)");
        Assert.That(log, Contains.Item("stopped"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Loop auto-resume after queue interrupt (mirrors MainWindow._loopInterruptedByQueue)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Models the MainWindow coordinator pattern introduced by the
    /// _loopInterruptedByQueue fix: when a user enqueues a prompt while the
    /// native loop is running, the loop is expected to resume automatically
    /// after those queued items have been drained.
    /// </summary>
    private sealed class ResumeHarness {
        private readonly PromptQueue                    _queue  = new();
        private readonly LoopController                 _controller;
        private readonly List<string>                   _log    = new();
        private readonly object                         _logLock = new();
        private readonly CancellationTokenSource        _abortCts = new();

        // Mirrors MainWindow._loopInterruptedByQueue / _loopQueued
        private bool _loopInterruptedByQueue;
        private bool _loopQueued;

        private readonly int _maxTotalIterations;
        private          int _totalIterationsRun;

        public readonly TaskCompletionSource DoneTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public volatile TaskCompletionSource? IterationBlockTcs;

        public LoopController Controller => _controller;

        public ResumeHarness(int maxTotalIterations = 3) {
            _maxTotalIterations = maxTotalIterations;
            _controller = new LoopController(
                executePromptAsync:   ExecuteAsync,
                abortPrompt:          () => _abortCts.Cancel(),
                onIterationStarted:   _ => { },
                onStopped:            OnStopped,
                onError:              msg => {
                    AddLog($"error:{msg}");
                    _loopInterruptedByQueue = false;
                    DoneTcs.TrySetResult();
                },
                onIterationCompleted: _ => {
                    // Stop the loop once the total iteration cap is reached.
                    if (_totalIterationsRun >= _maxTotalIterations)
                        _controller!.RequestStop();
                },
                onWaiting:            _ => { },
                onBeforeIteration:    DrainAsync,
                onBeforeWait:         () => Task.CompletedTask);
        }

        /// <summary>
        /// Models MainWindow: enqueueing while the loop is running sets
        /// <c>_loopInterruptedByQueue</c> in addition to adding the item.
        /// </summary>
        public void EnqueueWhileRunning(string text, int seqNum) {
            _loopInterruptedByQueue = true;
            _queue.Enqueue(text, seqNum);
        }

        public void RequestStop()  => _controller.RequestStop();
        public void RequestAbort() => _controller.RequestAbort();

        private async Task ExecuteAsync(string _, string? __) {
            var n = Interlocked.Increment(ref _totalIterationsRun);
            AddLog($"loop:{n}");
            var tcs = IterationBlockTcs;
            if (tcs != null) {
                try {
                    await Task.WhenAny(
                        tcs.Task,
                        Task.Delay(Timeout.InfiniteTimeSpan, _abortCts.Token));
                }
                catch (OperationCanceledException) { /* abort path — fall through */ }
                _abortCts.Token.ThrowIfCancellationRequested();
            }
        }

        // Models MainWindow.OnNativeLoopStopped
        private void OnStopped() {
            AddLog("stopped");
            bool hasInterrupt = _loopInterruptedByQueue;
            if ((hasInterrupt || _queue.HasReadyItems) && !_loopQueued)
                _loopQueued = true;
            _ = MaybeFireQueuedLoopAsync();
        }

        // Models MainWindow.MaybeFireQueuedLoopAsync
        private async Task MaybeFireQueuedLoopAsync() {
            if (!_loopQueued) { DoneTcs.TrySetResult(); return; }
            _loopQueued              = false;
            _loopInterruptedByQueue  = false;
            await DrainAsync();
            AddLog("resume");
            if (_totalIterationsRun >= _maxTotalIterations) { DoneTcs.TrySetResult(); return; }
            _ = _controller.StartAsync(MakeConfig(), continuousContext: true);
        }

        private async Task DrainAsync() {
            while (_queue.HasReadyItems) {
                var item = _queue.DequeueFirstReady();
                if (item is null) break;
                AddLog($"queue:{item.Text}");
                await Task.Yield();
            }
        }

        public Task StartAsync() => _controller.StartAsync(MakeConfig(), continuousContext: true);

        private static LoopMdConfig MakeConfig() => new(0.0001, 5, "", "loop-prompt");

        private void AddLog(string e) { lock (_logLock) _log.Add(e); }
        public List<string> GetLog() { lock (_logLock) return new List<string>(_log); }
        public bool ContainsLog(string e) { lock (_logLock) return _log.Contains(e); }
    }

    [Test]
    public async Task LoopResume_QueueItemEnqueuedMidRun_LoopRestartAfterQueueDrains() {
        // Scenario: loop is running, user enqueues a prompt mid-iteration
        // (sets _loopInterruptedByQueue = true).  The loop is then stopped
        // gracefully.  Expected: queue item drains, then loop restarts once.
        var h        = new ResumeHarness(maxTotalIterations: 2);
        var blockTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.IterationBlockTcs = blockTcs;

        await h.StartAsync();
        await WaitUntilLogContains(h, "loop:1");

        h.EnqueueWhileRunning("interrupt", 1);
        h.RequestStop();

        h.IterationBlockTcs = null;
        blockTcs.SetResult(); // release iteration 1

        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log = h.GetLog();

        Assert.That(log, Contains.Item("loop:1"),       "first iteration must run");
        Assert.That(log, Contains.Item("stopped"),      "loop must stop cleanly");
        Assert.That(log, Contains.Item("queue:interrupt"), "queued item must drain");
        Assert.That(log, Contains.Item("resume"),       "resume signal must fire");
        Assert.That(log, Contains.Item("loop:2"),       "loop must restart after resume");

        int stoppedIdx   = log.IndexOf("stopped");
        int queueIdx     = log.IndexOf("queue:interrupt");
        int resumeIdx    = log.IndexOf("resume");
        int loop2Idx     = log.IndexOf("loop:2");

        Assert.That(queueIdx,  Is.GreaterThan(stoppedIdx),   "queue drain must follow stop");
        Assert.That(resumeIdx, Is.GreaterThan(queueIdx),     "resume must follow queue drain");
        Assert.That(loop2Idx,  Is.GreaterThan(resumeIdx),    "loop:2 must follow resume signal");
    }

    [Test]
    public async Task LoopResume_MultipleItemsQueuedMidRun_AllDrainBeforeRestart() {
        // Three items queued while iteration 1 is blocked.  All must drain before
        // the loop restarts as loop:2.
        var h        = new ResumeHarness(maxTotalIterations: 2);
        var blockTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.IterationBlockTcs = blockTcs;

        await h.StartAsync();
        await WaitUntilLogContains(h, "loop:1");

        h.EnqueueWhileRunning("a", 1);
        h.EnqueueWhileRunning("b", 2);
        h.EnqueueWhileRunning("c", 3);
        h.RequestStop();

        h.IterationBlockTcs = null;
        blockTcs.SetResult();

        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log    = h.GetLog();
        int loop2  = log.IndexOf("loop:2");

        Assert.That(loop2, Is.GreaterThan(0), "loop:2 must appear");
        Assert.That(log.IndexOf("queue:a"), Is.LessThan(loop2), "'a' must drain before loop:2");
        Assert.That(log.IndexOf("queue:b"), Is.LessThan(loop2), "'b' must drain before loop:2");
        Assert.That(log.IndexOf("queue:c"), Is.LessThan(loop2), "'c' must drain before loop:2");
    }

    [Test]
    public async Task LoopResume_AbortWithInterruptFlag_DoesNotResume() {
        // When the loop is aborted (not stopped), _loopInterruptedByQueue must be
        // cleared by onError, and the loop must NOT restart.
        var h        = new ResumeHarness(maxTotalIterations: 99);
        var blockTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.IterationBlockTcs = blockTcs;

        await h.StartAsync();
        await WaitUntilLogContains(h, "loop:1");

        h.EnqueueWhileRunning("interrupt", 1);
        h.RequestAbort(); // abort, not graceful stop

        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log = h.GetLog();
        Assert.That(log, Contains.Item("error:Loop aborted."), "abort error must fire");
        Assert.That(log, Does.Not.Contain("resume"),  "no resume must happen after abort");
        Assert.That(log, Does.Not.Contain("loop:2"),  "loop:2 must not fire after abort");
    }

    [Test]
    public async Task LoopResume_InterruptFlagSetButQueueAlreadyDrainedByLoop_LoopStillResumes() {
        // The interrupt item was enqueued, then the loop's onBeforeIteration already
        // drained it.  _loopInterruptedByQueue stays true.  When the loop subsequently
        // stops, MaybeFireQueuedLoopAsync must still trigger a resume (even though
        // HasReadyItems is now false) because the flag signals user intent.
        var h        = new ResumeHarness(maxTotalIterations: 2);
        var blockTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.IterationBlockTcs = blockTcs;

        await h.StartAsync();
        await WaitUntilLogContains(h, "loop:1");

        // Enqueue while loop is running (sets _loopInterruptedByQueue = true)
        h.EnqueueWhileRunning("interrupt", 1);
        // Release iteration 1 so onBeforeIteration for iteration 2 runs,
        // draining "interrupt" before the stop fires.
        h.IterationBlockTcs = null;
        blockTcs.SetResult();

        // Wait for the queue item to drain inside the loop's pre-iteration pass.
        await WaitUntilLogContains(h, "queue:interrupt");

        // Now stop the loop — the queue is empty but _loopInterruptedByQueue was set.
        h.RequestStop();

        await h.DoneTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var log = h.GetLog();
        // loop:2 ran during the first loop run (before the stop), not after resume;
        // the key assertion is that the resume signal fires because _loopInterruptedByQueue
        // persisted even though the queue was already empty when the loop stopped.
        Assert.That(log, Contains.Item("stopped"),         "loop must stop cleanly");
        Assert.That(log, Contains.Item("queue:interrupt"), "interrupt item must have been drained inside the loop");
        Assert.That(log, Contains.Item("resume"),          "resume must fire even though queue was already empty — the interrupt flag triggered it");
        Assert.That(log.IndexOf("queue:interrupt"), Is.LessThan(log.IndexOf("stopped")),
            "'queue:interrupt' must be drained before the stop");
    }
}
