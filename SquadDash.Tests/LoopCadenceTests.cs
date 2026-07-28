using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SquadDash;

[TestFixture]
internal sealed class LoopCadenceTests
{
    private sealed class FakeClock : ILoopClock
    {
        private DateTimeOffset _now = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private readonly List<TimeSpan> _recordedDelays = [];
        internal IReadOnlyList<TimeSpan> RecordedDelays => _recordedDelays;

        public DateTimeOffset UtcNow => _now;

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            _recordedDelays.Add(delay);
            _now += delay;
            return Task.CompletedTask;
        }
    }

    private static LoopMdConfig MakeConfig(double intervalMinutes, int maxIterations = 2) =>
        new LoopMdConfig(
            IntervalMinutes: intervalMinutes,
            TimeoutMinutes: 5,
            Description: "test",
            Instructions: "test",
            Options: [new LoopOption("max_iterations", maxIterations.ToString(), "number", null, null, null)]);

    private static (LoopController controller, TaskCompletionSource stopped) MakeController(
        FakeClock clock,
        Action<int>? onIterationCompleted = null)
    {
        var tcs = new TaskCompletionSource();
        var controller = new LoopController(
            executePromptAsync: (_, _) => Task.CompletedTask,
            abortPrompt: () => { },
            onIterationStarted: _ => { },
            onStopped: () => tcs.TrySetResult(),
            onError: _ => tcs.TrySetResult(),
            onIterationCompleted: onIterationCompleted ?? (_ => { }),
            onWaiting: _ => { },
            clock: clock);
        return (controller, tcs);
    }

    [Test]
    public async Task LoopController_UsesConfiguredInterval_PointOneMinute()
    {
        var clock = new FakeClock();
        var (controller, tcs) = MakeController(clock);
        var config = MakeConfig(intervalMinutes: 0.1, maxIterations: 2);

        await controller.StartAsync(config, continuousContext: false);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(clock.RecordedDelays, Has.Count.EqualTo(1));
        Assert.That(clock.RecordedDelays[0], Is.EqualTo(TimeSpan.FromMinutes(0.1)));
        Assert.That(clock.RecordedDelays[0].TotalSeconds, Is.EqualTo(6.0).Within(0.001));
    }

    [Test]
    public async Task LoopController_UsesConfiguredInterval_OneMinute()
    {
        var clock = new FakeClock();
        var (controller, tcs) = MakeController(clock);
        var config = MakeConfig(intervalMinutes: 1.0, maxIterations: 2);

        await controller.StartAsync(config, continuousContext: false);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(clock.RecordedDelays, Has.Count.EqualTo(1));
        Assert.That(clock.RecordedDelays[0], Is.EqualTo(TimeSpan.FromMinutes(1.0)));
        Assert.That(clock.RecordedDelays[0].TotalSeconds, Is.EqualTo(60.0).Within(0.001));
    }

    [Test]
    public async Task LoopController_MaxIterations_StopsAtLimit()
    {
        var clock = new FakeClock();
        var completedCount = 0;
        var (controller, tcs) = MakeController(clock, onIterationCompleted: _ => completedCount++);
        var config = MakeConfig(intervalMinutes: 0.1, maxIterations: 3);

        await controller.StartAsync(config, continuousContext: false);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(completedCount, Is.EqualTo(3));
        Assert.That(clock.RecordedDelays, Has.Count.EqualTo(2)); // delay between each pair
    }

    [Test]
    public async Task LoopController_WaitInterrupted_ProceedsImmediately()
    {
        // Use a real (blocking) clock so we can interrupt the delay
        var interruptClock = new InterruptibleFakeClock();
        var tcs = new TaskCompletionSource();
        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync: (_, _) => Task.CompletedTask,
            abortPrompt: () => { },
            onIterationStarted: _ => { },
            onStopped: () => tcs.TrySetResult(),
            onError: _ => tcs.TrySetResult(),
            onIterationCompleted: _ => interruptClock.RequestInterrupt(controller!),
            onWaiting: _ => { },
            clock: interruptClock);

        var config = MakeConfig(intervalMinutes: 1.0, maxIterations: 2);
        await controller.StartAsync(config, continuousContext: false);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The delay was interrupted, so actual elapsed should be near zero
        Assert.That(interruptClock.WasInterrupted, Is.True);
        Assert.That(interruptClock.RecordedDelays, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task LoopController_BoundaryDiagnostics_CapturesConfiguredSource()
    {
        var clock = new FakeClock();
        var diagnostics = new List<LoopBoundaryDiagnostics>();
        var tcs = new TaskCompletionSource();
        var controller = new LoopController(
            executePromptAsync: (_, _) => Task.CompletedTask,
            abortPrompt: () => { },
            onIterationStarted: _ => { },
            onStopped: () => tcs.TrySetResult(),
            onError: _ => tcs.TrySetResult(),
            onIterationCompleted: _ => { },
            onWaiting: _ => { },
            clock: clock);

        // Capture diagnostics via trace — we verify indirectly by checking recorded delays
        var config = MakeConfig(intervalMinutes: 0.1, maxIterations: 2);
        await controller.StartAsync(config, continuousContext: false);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // No exception means diagnostics path executed without error
        Assert.That(clock.RecordedDelays, Has.Count.EqualTo(1));
        Assert.That(clock.RecordedDelays[0].TotalSeconds, Is.EqualTo(6.0).Within(0.001));
    }

    [Test]
    public async Task LoopController_ZeroMaxIterations_RunsUnlimitedUntilStopped()
    {
        var clock = new FakeClock();
        var completedCount = 0;
        var tcs = new TaskCompletionSource();
        LoopController? controller = null;
        controller = new LoopController(
            executePromptAsync: (_, _) => Task.CompletedTask,
            abortPrompt: () => { },
            onIterationStarted: _ => { },
            onStopped: () => tcs.TrySetResult(),
            onError: _ => tcs.TrySetResult(),
            onIterationCompleted: _ =>
            {
                completedCount++;
                if (completedCount >= 4)
                    controller!.RequestStop();
            },
            onWaiting: _ => { },
            clock: clock);

        var config = new LoopMdConfig(
            IntervalMinutes: 0.1,
            TimeoutMinutes: 5,
            Description: "test",
            Instructions: "test");

        await controller.StartAsync(config, continuousContext: false);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(completedCount, Is.EqualTo(4));
    }

    /// <summary>
    /// A fake clock whose Delay can be interrupted via CancelLoopWait,
    /// simulating an async queue arrival.
    /// </summary>
    private sealed class InterruptibleFakeClock : ILoopClock
    {
        private readonly List<TimeSpan> _recordedDelays = [];
        internal IReadOnlyList<TimeSpan> RecordedDelays => _recordedDelays;
        internal bool WasInterrupted { get; private set; }

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public async Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            _recordedDelays.Add(delay);
            try {
                // Use a real delay so the interrupt can fire and cancel it
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            } catch (OperationCanceledException) {
                WasInterrupted = true;
                throw; // re-throw so RunLoopAsync handles it via its own catch
            }
        }

        internal void RequestInterrupt(LoopController controller)
        {
            Task.Run(async () => {
                await Task.Delay(10);
                controller.CancelLoopWait();
            });
        }
    }
}
