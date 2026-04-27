using System.IO.Pipes;
using System.Threading;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class InstanceActivationChannelTests {
    [Test]
    public async Task TryRequestActivation_RoundTripsToListeningChannel() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("app-root");
        Directory.CreateDirectory(appRoot);
        using var activated = new ManualResetEventSlim(false);
        await using var channel = new InstanceActivationChannel(
            appRoot,
            Environment.ProcessId,
            424242,
            activated.Set);
        channel.Start();

        var owner = new RunningInstanceRecord(
            appRoot,
            appRoot,
            Environment.ProcessId,
            424242,
            DateTimeOffset.UtcNow.Ticks) {
            ActiveWorkspaceFolder = appRoot
        };

        var requested = InstanceActivationChannel.TryRequestActivation(
            appRoot,
            owner,
            TimeSpan.FromSeconds(1));

        Assert.Multiple(() => {
            Assert.That(requested, Is.True);
            Assert.That(activated.Wait(TimeSpan.FromSeconds(2)), Is.True);
        });
    }

    // ── GetPipeName ───────────────────────────────────────────────────────────

    [Test]
    public void GetPipeName_IsDeterministic_SameInputsProduceSameName() {
        var name1 = InstanceActivationChannel.GetPipeName(@"C:\App", 1234, 999L);
        var name2 = InstanceActivationChannel.GetPipeName(@"C:\App", 1234, 999L);

        Assert.That(name1, Is.EqualTo(name2));
    }

    [Test]
    public void GetPipeName_DifferentApplicationRoots_ProduceDifferentNames() {
        var a = InstanceActivationChannel.GetPipeName(@"C:\AppA", 1, 0L);
        var b = InstanceActivationChannel.GetPipeName(@"C:\AppB", 1, 0L);

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void GetPipeName_DifferentProcessIds_ProduceDifferentNames() {
        var a = InstanceActivationChannel.GetPipeName(@"C:\App", 100, 0L);
        var b = InstanceActivationChannel.GetPipeName(@"C:\App", 200, 0L);

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void GetPipeName_DifferentStartTicks_ProduceDifferentNames() {
        var a = InstanceActivationChannel.GetPipeName(@"C:\App", 1, 100L);
        var b = InstanceActivationChannel.GetPipeName(@"C:\App", 1, 200L);

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void GetPipeName_HasExpectedPrefixAndHashLength() {
        const string prefix = "SquadDash.Activate.";
        var name = InstanceActivationChannel.GetPipeName(@"C:\App", 1, 0L);

        Assert.Multiple(() => {
            Assert.That(name, Does.StartWith(prefix));
            Assert.That(name.Length, Is.EqualTo(prefix.Length + 24));
            Assert.That(name[prefix.Length..], Does.Match("^[0-9a-f]{24}$"));
        });
    }

    // ── Null guard ────────────────────────────────────────────────────────────

    [Test]
    public void TryRequestActivation_NullOwner_ThrowsArgumentNullException() {
        Assert.Throws<ArgumentNullException>(() =>
            InstanceActivationChannel.TryRequestActivation(
                @"C:\App",
                null!,
                TimeSpan.FromMilliseconds(50)));
    }

    // ── Timeout / no server ───────────────────────────────────────────────────

    [Test]
    public void TryRequestActivation_WhenNoServerListening_ReturnsFalse() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("no-server");
        Directory.CreateDirectory(appRoot);

        // int.MaxValue exceeds the valid Windows PID range, so Process.GetProcessById
        // throws, causing the native window-activation fallback to also return false.
        var owner = new RunningInstanceRecord(
            appRoot,
            appRoot,
            int.MaxValue,
            DateTimeOffset.UtcNow.AddHours(-1).Ticks,
            DateTimeOffset.UtcNow.Ticks);

        var result = InstanceActivationChannel.TryRequestActivation(
            appRoot,
            owner,
            TimeSpan.FromMilliseconds(50));

        Assert.That(result, Is.False);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [Test]
    public async Task Start_IsIdempotent_SecondCallDoesNotThrow() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("app-root");
        Directory.CreateDirectory(appRoot);

        await using var channel = new InstanceActivationChannel(
            appRoot,
            Environment.ProcessId,
            12345678L,
            () => { });

        Assert.DoesNotThrow(() => {
            channel.Start();
            channel.Start(); // second call must be a no-op
        });
    }

    [Test]
    public async Task DisposeAsync_BeforeStart_CompletesWithoutError() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("app-root");
        Directory.CreateDirectory(appRoot);

        var channel = new InstanceActivationChannel(
            appRoot,
            Environment.ProcessId,
            23456789L,
            () => { });

        // Dispose without ever calling Start — must not throw.
        Assert.DoesNotThrowAsync(async () => await channel.DisposeAsync());
    }

    [Test]
    public async Task DisposeAsync_AfterStart_PipeNoLongerAcceptsConnections() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("app-root");
        Directory.CreateDirectory(appRoot);

        var channel = new InstanceActivationChannel(
            appRoot,
            Environment.ProcessId,
            34567890L,
            () => { });

        channel.Start();
        await channel.DisposeAsync(); // awaits _listenTask — server is fully torn down

        var pipeName = InstanceActivationChannel.GetPipeName(
            appRoot,
            Environment.ProcessId,
            34567890L);

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);

        Assert.Throws<TimeoutException>(() => client.Connect(50));
    }

    // ── Unknown command ───────────────────────────────────────────────────────

    [Test]
    public async Task Channel_UnknownCommand_DoesNotFireActivationCallback() {
        using var workspace = new TestWorkspace();
        var appRoot = workspace.GetPath("app-root");
        Directory.CreateDirectory(appRoot);

        var activationCount = 0;

        await using var channel = new InstanceActivationChannel(
            appRoot,
            Environment.ProcessId,
            45678901L,
            () => Interlocked.Increment(ref activationCount));

        channel.Start();

        // Connect to the channel's pipe and send a command that is NOT "activate".
        var pipeName = InstanceActivationChannel.GetPipeName(
            appRoot,
            Environment.ProcessId,
            45678901L);

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
        client.Connect(2000);

        using var writer = new StreamWriter(
            client,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: false) {
            AutoFlush = true
        };
        writer.WriteLine("not-activate");

        // Give the channel time to process the command.
        await Task.Delay(500);

        Assert.That(activationCount, Is.EqualTo(0));
    }
}
