using System;
using System.IO;
using System.Text;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace SquadDash;

internal sealed class InstanceActivationChannel : IAsyncDisposable {
    private const string ActivateCommand = "activate";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _pipeName;
    private readonly Action _onActivationRequested;
    private readonly Action<Exception>? _onError;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listenTask;

    public InstanceActivationChannel(
        string applicationRoot,
        int processId,
        long processStartedAtUtcTicks,
        Action onActivationRequested,
        Action<Exception>? onError = null) {
        _pipeName = GetPipeName(applicationRoot, processId, processStartedAtUtcTicks);
        _onActivationRequested = onActivationRequested ?? throw new ArgumentNullException(nameof(onActivationRequested));
        _onError = onError;
    }

    public void Start() {
        _listenTask ??= Task.Run(ListenLoopAsync);
    }

    public void Stop() {
        _shutdown.Cancel();
    }

    public async ValueTask DisposeAsync() {
        Stop();

        if (_listenTask is not null) {
            try {
                await _listenTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) {
            }
        }

        _shutdown.Dispose();
    }

    public static bool TryRequestActivation(
        string applicationRoot,
        RunningInstanceRecord owner,
        TimeSpan timeout) {
        if (owner is null)
            throw new ArgumentNullException(nameof(owner));

        NativeMethods.AllowSetForegroundWindow(owner.ProcessId);

        if (TryRequestActivationViaPipe(applicationRoot, owner.ProcessId, owner.ProcessStartedAtUtcTicks, timeout))
            return true;

        return NativeMethods.TryActivateProcessMainWindow(owner.ProcessId);
    }

    internal static string GetPipeName(
        string applicationRoot,
        int processId,
        long processStartedAtUtcTicks) {
        var normalizedRoot = WorkspaceOwnershipLease.NormalizePath(applicationRoot);
        var identity = $"{normalizedRoot}\n{processId}\n{processStartedAtUtcTicks}";
        var hash = Convert.ToHexString(SHA256.HashData(Utf8NoBom.GetBytes(identity))).ToLowerInvariant();
        return $"SquadDash.Activate.{hash[..24]}";
    }

    private async Task ListenLoopAsync() {
        while (!_shutdown.IsCancellationRequested) {
            using var server = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try {
                await server.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
                using var reader = new StreamReader(
                    server,
                    Utf8NoBom,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 1024,
                    leaveOpen: true);
                var command = await reader.ReadLineAsync().ConfigureAwait(false);

                if (string.Equals(command?.Trim(), ActivateCommand, StringComparison.OrdinalIgnoreCase))
                    _onActivationRequested();
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) {
                break;
            }
            catch (IOException) when (_shutdown.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                _onError?.Invoke(ex);

                try {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), _shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) {
                    break;
                }
            }
        }
    }

    private static bool TryRequestActivationViaPipe(
        string applicationRoot,
        int processId,
        long processStartedAtUtcTicks,
        TimeSpan timeout) {
        try {
            var timeoutMs = (int)Math.Clamp(timeout.TotalMilliseconds, 50, 5000);
            using var client = new NamedPipeClientStream(
                ".",
                GetPipeName(applicationRoot, processId, processStartedAtUtcTicks),
                PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(
                client,
                Utf8NoBom,
                bufferSize: 1024,
                leaveOpen: false) {
                AutoFlush = true
            };
            writer.WriteLine(ActivateCommand);
            return true;
        }
        catch {
            return false;
        }
    }
}
