using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

public sealed class SquadSdkProcess : IAsyncDisposable {
    private static readonly TimeSpan DefaultPromptInactivityTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultPromptTimeoutPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DefaultBackgroundCancelResponseTimeout = TimeSpan.FromSeconds(5);
    private const string SessionResetEventType = "session_reset";

    public event EventHandler<SquadSdkEvent>? EventReceived;
    public event EventHandler<string>? ErrorReceived;

    private readonly SemaphoreSlim _promptLock = new(1, 1);
    private readonly SemaphoreSlim _stdinWriteLock = new(1, 1);
    private readonly Func<ProcessStartInfo>? _processStartInfoFactory;
    private readonly object _stateLock = new();
    private readonly Queue<string> _recentErrorLines = new();
    private readonly Dictionary<string, PendingBridgeRequest> _pendingRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingBackgroundCancelRequest> _pendingBackgroundCancels = new(StringComparer.Ordinal);
    private readonly TimeSpan _promptInactivityTimeout;
    private readonly TimeSpan _promptTimeoutPollInterval;
    private readonly IWorkspacePaths? _workspacePaths;

    private Process? _activeProcess;
    private StreamWriter? _activeProcessInput;
    private Task? _outputReaderTask;
    private string? _activePromptRequestId;

    /// <summary>
    /// Optional BYOK provider settings. When set (and <see cref="ByokProviderSettings.ProviderUrl"/> is
    /// non-empty), the corresponding <c>COPILOT_PROVIDER_*</c> environment variables are injected into
    /// the bridge child process, bypassing GitHub authentication entirely.
    /// Must be assigned before the bridge is first started.
    /// </summary>
    internal ByokProviderSettings? ByokProviderSettings { get; set; }

    internal SquadSdkProcess(IWorkspacePaths workspacePaths)
        : this(processStartInfoFactory: null, options: null, workspacePaths: workspacePaths) {
    }

    internal SquadSdkProcess(Func<ProcessStartInfo> processStartInfoFactory) {
        _processStartInfoFactory = processStartInfoFactory;
        _promptInactivityTimeout = DefaultPromptInactivityTimeout;
        _promptTimeoutPollInterval = DefaultPromptTimeoutPollInterval;
    }

    internal SquadSdkProcess(
        Func<ProcessStartInfo>? processStartInfoFactory,
        SquadSdkProcessOptions? options,
        IWorkspacePaths? workspacePaths = null) {
        _processStartInfoFactory = processStartInfoFactory;
        _workspacePaths = workspacePaths;
        _promptInactivityTimeout = options?.PromptInactivityTimeout > TimeSpan.Zero
            ? options.PromptInactivityTimeout
            : DefaultPromptInactivityTimeout;
        _promptTimeoutPollInterval = options?.PromptTimeoutPollInterval > TimeSpan.Zero
            ? options.PromptTimeoutPollInterval
            : DefaultPromptTimeoutPollInterval;
    }

    public async Task RunPromptAsync(
        string prompt,
        string workingDirectory,
        string? sessionId = null,
        string? configDirectory = null) {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt cannot be empty.", nameof(prompt));
        if (string.IsNullOrWhiteSpace(workingDirectory))
            throw new ArgumentException("Working directory cannot be empty.", nameof(workingDirectory));

        SquadDashTrace.Write(
            "Bridge",
            $"RunPromptAsync requested prompt={prompt} cwd={workingDirectory} sessionId={sessionId ?? "(new)"}");

        await _promptLock.WaitAsync().ConfigureAwait(false);
        try {
            await RunPromptWithSessionRecoveryAsync(
                prompt,
                workingDirectory,
                sessionId,
                configDirectory).ConfigureAwait(false);
        }
        finally {
            _promptLock.Release();
        }
    }

    public async Task RunNamedAgentDelegationAsync(
        string selectedOption,
        string targetAgentHandle,
        string workingDirectory,
        string? sessionId,
        string? configDirectory = null) {
        if (string.IsNullOrWhiteSpace(selectedOption))
            throw new ArgumentException("Selected option cannot be empty.", nameof(selectedOption));
        if (string.IsNullOrWhiteSpace(targetAgentHandle))
            throw new ArgumentException("Target agent handle cannot be empty.", nameof(targetAgentHandle));
        if (string.IsNullOrWhiteSpace(workingDirectory))
            throw new ArgumentException("Working directory cannot be empty.", nameof(workingDirectory));
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Named-agent delegation requires an active Squad session.", nameof(sessionId));

        SquadDashTrace.Write(
            "Bridge",
            $"RunNamedAgentDelegationAsync requested option={selectedOption} targetAgent={targetAgentHandle} cwd={workingDirectory} sessionId={sessionId}");

        await _promptLock.WaitAsync().ConfigureAwait(false);
        try {
            var requestId = Guid.NewGuid().ToString("N");
            var request = new SquadSdkDelegateRequest(
                selectedOption,
                targetAgentHandle,
                workingDirectory,
                sessionId,
                configDirectory,
                requestId);
            await RunBridgeRequestOnceAsync(
                request,
                requestId,
                sessionId,
                allowRecoverableSessionReset: false).ConfigureAwait(false);
        }
        finally {
            _promptLock.Release();
        }
    }

    public async Task RunNamedAgentDirectAsync(
        string targetAgentHandle,
        string selectedOption,
        string? handoffContext,
        string workingDirectory,
        string? coordinatorSessionId,
        string? configDirectory = null) {
        if (string.IsNullOrWhiteSpace(targetAgentHandle))
            throw new ArgumentException("Target agent handle cannot be empty.", nameof(targetAgentHandle));
        if (string.IsNullOrWhiteSpace(selectedOption))
            throw new ArgumentException("Selected option cannot be empty.", nameof(selectedOption));
        if (string.IsNullOrWhiteSpace(workingDirectory))
            throw new ArgumentException("Working directory cannot be empty.", nameof(workingDirectory));

        SquadDashTrace.Write(
            "Bridge",
            $"RunNamedAgentDirectAsync targetAgent={targetAgentHandle} option={selectedOption} cwd={workingDirectory}");

        await _promptLock.WaitAsync().ConfigureAwait(false);
        try {
            var requestId = Guid.NewGuid().ToString("N");
            var request = new SquadSdkNamedAgentRequest(
                targetAgentHandle,
                selectedOption,
                handoffContext,
                workingDirectory,
                coordinatorSessionId,
                configDirectory,
                requestId);
            await RunBridgeRequestOnceAsync(
                request,
                requestId,
                sessionId: coordinatorSessionId,
                allowRecoverableSessionReset: false).ConfigureAwait(false);
        }
        finally {
            _promptLock.Release();
        }
    }

    private async Task RunPromptWithSessionRecoveryAsync(
        string prompt,
        string workingDirectory,
        string? sessionId,
        string? configDirectory) {
        try {
            await RunPromptOnceAsync(
                prompt,
                workingDirectory,
                sessionId,
                configDirectory,
                allowRecoverableSessionReset: !string.IsNullOrWhiteSpace(sessionId)).ConfigureAwait(false);
        }
        catch (RecoverableSessionResetException ex) when (!string.IsNullOrWhiteSpace(sessionId)) {
            SquadDashTrace.Write(
                "Bridge",
                $"Resetting resumed session {sessionId} after recoverable provider error: {ex.Message}");
            ResetProcess();

            EventReceived?.Invoke(this, new SquadSdkEvent {
                Type = SessionResetEventType,
                Message = "Squad reset the previous session after the provider rejected its saved context. Retrying this prompt in a fresh session, so you may need to restate earlier context."
            });

            await RunPromptOnceAsync(
                prompt,
                workingDirectory,
                sessionId: null,
                configDirectory,
                allowRecoverableSessionReset: false).ConfigureAwait(false);
        }
    }

    private async Task RunPromptOnceAsync(
        string prompt,
        string workingDirectory,
        string? sessionId,
        string? configDirectory,
        bool allowRecoverableSessionReset) {
        var requestId = Guid.NewGuid().ToString("N");
        var request = new SquadSdkPromptRequest(
            prompt,
            workingDirectory,
            sessionId,
            configDirectory,
            requestId);
        await RunBridgeRequestOnceAsync(
            request,
            requestId,
            sessionId,
            allowRecoverableSessionReset).ConfigureAwait(false);
    }

    private async Task RunBridgeRequestOnceAsync<TRequest>(
        TRequest request,
        string requestId,
        string? sessionId,
        bool allowRecoverableSessionReset) {
        ClearRecentErrors();

        var completion = new TaskCompletionSource<BridgeRequestOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingRequest = new PendingBridgeRequest(
            completion,
            sessionId,
            allowRecoverableSessionReset);

        lock (_stateLock) {
            _pendingRequests[requestId] = pendingRequest;
            _activePromptRequestId = requestId;
        }

        try {
            await SendBridgeRequestWithRestartAsync(request, pendingRequest).ConfigureAwait(false);
            pendingRequest.MarkActivity();
            var outcome = await WaitForPromptOutcomeAsync(pendingRequest).ConfigureAwait(false);
            if (outcome == BridgeRequestOutcome.Aborted)
                throw new OperationCanceledException("Prompt aborted by user.");

            SquadDashTrace.Write("Bridge", "Prompt completed.");
            await WaitForInjectedBridgeToSettleAsync().ConfigureAwait(false);
        }
        finally {
            lock (_stateLock) {
                _pendingRequests.Remove(requestId);
                if (string.Equals(_activePromptRequestId, requestId, StringComparison.Ordinal))
                    _activePromptRequestId = null;
            }
        }
    }

    private async Task SendBridgeRequestWithRestartAsync<TRequest>(TRequest request, PendingBridgeRequest? pendingRequest = null) {
        Task? outputReaderTask = null;
        try {
            EnsureProcessStarted();
            lock (_stateLock) {
                outputReaderTask = _outputReaderTask;
            }
            await SendBridgeRequestAsync(request).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecoverableWriteFailure(ex)) {
            // If the process exited before we could write (race condition with a fast-exiting
            // process), wait briefly for the output reader to flush any in-flight events before
            // deciding whether to restart. If the pending request is already resolved the process
            // handled it — no restart needed.
            if (outputReaderTask is not null) {
                try { await Task.WhenAny(outputReaderTask, Task.Delay(500)).ConfigureAwait(false); }
                catch { }
            }

            if (pendingRequest?.Completion.Task.IsCompleted == true) {
                SquadDashTrace.Write("Bridge", $"Prompt write failed but request already resolved; not restarting: {ex.Message}");
                return;
            }

            SquadDashTrace.Write("Bridge", $"Prompt write failed. Restarting bridge and retrying once: {ex.Message}");
            ResetProcess();
            EnsureProcessStarted();
            await SendBridgeRequestAsync(request).ConfigureAwait(false);
        }
    }

    private async Task<BridgeRequestOutcome> WaitForPromptOutcomeAsync(PendingBridgeRequest pendingRequest) {
        while (true) {
            var completedTask = await Task
                .WhenAny(pendingRequest.Completion.Task, Task.Delay(_promptTimeoutPollInterval))
                .ConfigureAwait(false);
            if (completedTask == pendingRequest.Completion.Task)
                return await pendingRequest.Completion.Task.ConfigureAwait(false);

            var idleFor = pendingRequest.GetInactivityDuration(DateTimeOffset.UtcNow);
            if (idleFor < _promptInactivityTimeout)
                continue;

            var lastActivityAt = pendingRequest.GetLastActivityAt();
            SquadDashTrace.Write(
                "Bridge",
                $"Prompt inactivity timeout reached after {idleFor.TotalSeconds:0}s. lastActivityAt={lastActivityAt:O}. Restarting bridge process.");
            ResetProcess();
            throw new TimeoutException(BuildFailureMessage(
                $"Timed out waiting for Squad to finish responding after {_promptInactivityTimeout.TotalSeconds:0} seconds without bridge activity."));
        }
    }

    private void EnsureProcessStarted() {
        Process? staleProcess = null;

        lock (_stateLock) {
            if (_activeProcess is not null) {
                try {
                    if (!_activeProcess.HasExited && _activeProcessInput is not null)
                        return;
                }
                catch {
                }

                staleProcess = _activeProcess;
                _activeProcess = null;
                _activeProcessInput = null;
                _outputReaderTask = null;
                if (_activePromptRequestId is null)
                    _pendingRequests.Clear();
            }
        }

        if (staleProcess is not null)
            TryKill(staleProcess);

        if (_processStartInfoFactory is null) {
            var compatibilityResult = SquadRuntimeCompatibility.Repair(
                _workspacePaths?.SquadSdkDirectory ?? throw new InvalidOperationException("WorkspacePaths not configured"),
                "Bundled Squad SDK");
            if (!compatibilityResult.Success)
                throw new InvalidOperationException(compatibilityResult.ToDisplayText());
        }

        var startInfo = _processStartInfoFactory?.Invoke() ?? BuildDefaultStartInfo();
        var process = new Process {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.ErrorDataReceived += (_, e) => {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            EnqueueError(e.Data);
            SquadDashTrace.Write("Bridge", $"stderr: {e.Data}");
            ErrorReceived?.Invoke(this, e.Data);
        };

        process.Exited += (_, _) => HandleProcessExited(process);

        var startSw = Stopwatch.StartNew();
        if (!process.Start())
            throw new InvalidOperationException("Failed to start Squad SDK process.");
        startSw.Stop();

        process.BeginErrorReadLine();
        var readerTask = Task.Run(() => ReadOutputLoopAsync(process));

        lock (_stateLock) {
            _activeProcess = process;
            _activeProcessInput = process.StandardInput;
            _outputReaderTask = readerTask;
        }

        SquadDashTrace.Write(
            "Bridge",
            $"Started persistent bridge process pid={process.Id} in {startSw.ElapsedMilliseconds}ms. {SquadDashRuntimeStamp.BuildBridgeStamp()}");
    }

    private ProcessStartInfo BuildDefaultStartInfo() {
        var psi = new ProcessStartInfo {
            FileName = "node",
            Arguments = "runPrompt.js",
            WorkingDirectory = _workspacePaths?.SquadSdkDirectory ?? throw new InvalidOperationException("WorkspacePaths not configured"),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (ByokProviderSettings is { ProviderUrl: { Length: > 0 } providerUrl } byok) {
            psi.EnvironmentVariables["COPILOT_PROVIDER_BASE_URL"] = providerUrl;

            if (!string.IsNullOrEmpty(byok.Model))
                psi.EnvironmentVariables["COPILOT_PROVIDER_MODEL_ID"] = byok.Model;

            if (!string.IsNullOrEmpty(byok.ProviderType))
                psi.EnvironmentVariables["COPILOT_PROVIDER_TYPE"] = byok.ProviderType;

            if (!string.IsNullOrEmpty(byok.ApiKey))
                psi.EnvironmentVariables["COPILOT_PROVIDER_API_KEY"] = byok.ApiKey;

            SquadDashTrace.Write("Bridge",
                $"BYOK active — url={providerUrl} model={byok.Model ?? "(none)"} type={byok.ProviderType ?? "(default)"} apiKey={(string.IsNullOrEmpty(byok.ApiKey) ? "not set" : "set")}");
        } else {
            SquadDashTrace.Write("Bridge", "BYOK not configured — using default GitHub Copilot provider.");
        }

        return psi;
    }

    private async Task SendBridgeRequestAsync<TRequest>(TRequest request) {
        var payload = JsonSerializer.Serialize(request);

        await _stdinWriteLock.WaitAsync().ConfigureAwait(false);
        try {
            StreamWriter? input;
            lock (_stateLock) {
                input = _activeProcessInput;
            }

            if (input is null)
                throw new InvalidOperationException("The Squad bridge is not running.");

            await input.WriteLineAsync(payload).ConfigureAwait(false);
            await input.FlushAsync().ConfigureAwait(false);
            SquadDashTrace.Write("Bridge", $"Sent request: {payload}");
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) {
            throw new InvalidOperationException("Failed to write to the Squad bridge.", ex);
        }
        finally {
            _stdinWriteLock.Release();
        }
    }

    private async Task ReadOutputLoopAsync(Process process) {
        try {
            while (true) {
                string? line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                    break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try {
                    var evt = JsonSerializer.Deserialize<SquadSdkEvent>(
                        line,
                        new JsonSerializerOptions {
                            PropertyNameCaseInsensitive = true
                        });

                    if (evt is null)
                        continue;

                    SquadDashTrace.Write(
                        "Bridge",
                        $"Received event type={evt.Type ?? "(null)"} requestId={evt.RequestId ?? "(none)"}");
                    HandleBridgeEvent(evt);
                }
                catch {
                    SquadDashTrace.Write("Bridge", $"Received non-json stdout line: {line}");
                    ErrorReceived?.Invoke(this, "[non-json] " + line);
                }
            }
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Bridge", $"stdout reader failed: {ex}");
            ErrorReceived?.Invoke(this, ex.Message);
            TryKill(process);
        }
    }

    private void HandleBridgeEvent(SquadSdkEvent evt) {
        var backgroundCancelHandled = TryCompleteBackgroundCancel(evt);

        var pendingRequest = ResolvePendingRequest(evt);
        var isRecoverableSessionReset =
            pendingRequest is not null &&
            string.Equals(evt.Type, "error", StringComparison.Ordinal) &&
            pendingRequest.AllowRecoverableSessionReset &&
            ShouldResetSessionAndRetry(pendingRequest.SessionId, evt.Message);

        if ((!backgroundCancelHandled || string.Equals(evt.Type, "background_task_cancelled", StringComparison.Ordinal)) &&
            !isRecoverableSessionReset &&
            !string.Equals(evt.Type, "aborted", StringComparison.Ordinal))
            EventReceived?.Invoke(this, evt);

        if (pendingRequest is null)
            return;

        pendingRequest.MarkActivity();

        switch (evt.Type) {
            case "done":
                pendingRequest.Completion.TrySetResult(BridgeRequestOutcome.Completed);
                break;

            case "aborted":
                pendingRequest.Completion.TrySetResult(BridgeRequestOutcome.Aborted);
                break;

            case "error":
                if (isRecoverableSessionReset) {
                    pendingRequest.Completion.TrySetException(
                        new RecoverableSessionResetException(evt.Message ?? "Unknown error"));
                }
                else {
                    pendingRequest.Completion.TrySetException(
                        new InvalidOperationException(evt.Message ?? "Unknown error"));
                }
                break;
        }
    }

    private PendingBridgeRequest? ResolvePendingRequest(SquadSdkEvent evt) {
        lock (_stateLock) {
            if (!string.IsNullOrWhiteSpace(evt.RequestId))
                return _pendingRequests.TryGetValue(evt.RequestId, out var matched)
                    ? matched
                    : null;

            if (_pendingRequests.Count == 1)
                return _pendingRequests.Values.Single();

            return null;
        }
    }

    private bool TryCompleteBackgroundCancel(SquadSdkEvent evt) {
        if (string.IsNullOrWhiteSpace(evt.RequestId))
            return false;

        PendingBackgroundCancelRequest? pendingCancel;
        lock (_stateLock) {
            if (!_pendingBackgroundCancels.TryGetValue(evt.RequestId, out pendingCancel))
                return false;
        }

        if (string.Equals(evt.Type, "background_task_cancelled", StringComparison.Ordinal)) {
            var cancelled = evt.Cancelled == true;
            SquadDashTrace.Write(
                "Bridge",
                $"Background cancel acknowledgement requestId={evt.RequestId} taskId={evt.TaskId ?? pendingCancel.TaskId} sessionId={evt.SessionId ?? pendingCancel.SessionId ?? "(auto)"} cancelled={cancelled}");
            pendingCancel.Completion.TrySetResult(cancelled);
            return true;
        }

        if (string.Equals(evt.Type, "error", StringComparison.Ordinal)) {
            SquadDashTrace.Write(
                "Bridge",
                $"Background cancel failed requestId={evt.RequestId} taskId={pendingCancel.TaskId} message={evt.Message ?? "(none)"}");
            pendingCancel.Completion.TrySetResult(false);
            return true;
        }

        return false;
    }

    private void HandleProcessExited(Process process) {
        string[] pendingRequestIds;
        PendingBridgeRequest[] pendingRequests;
        PendingBackgroundCancelRequest[] pendingCancels;
        Task? outputReaderTask;

        lock (_stateLock) {
            if (!ReferenceEquals(_activeProcess, process))
                return;

            pendingRequestIds = _pendingRequests.Keys.ToArray();
            pendingRequests = _pendingRequests.Values.ToArray();
            pendingCancels = _pendingBackgroundCancels.Values.ToArray();
            _pendingBackgroundCancels.Clear();
            outputReaderTask = _outputReaderTask;
            _activeProcess = null;
            _activeProcessInput = null;
            _outputReaderTask = null;
            _activePromptRequestId = null;
        }

        foreach (var pendingCancel in pendingCancels)
            pendingCancel.Completion.TrySetResult(false);

        if (pendingRequests.Length == 0)
            return;

        _ = CompleteExitedProcessRequestsAsync(pendingRequestIds, pendingRequests, outputReaderTask);
    }

    private async Task CompleteExitedProcessRequestsAsync(
        string[] pendingRequestIds,
        PendingBridgeRequest[] pendingRequests,
        Task? outputReaderTask) {
        if (outputReaderTask is not null) {
            try {
                await outputReaderTask.ConfigureAwait(false);
            }
            catch {
            }
        }

        lock (_stateLock) {
            foreach (var requestId in pendingRequestIds)
                _pendingRequests.Remove(requestId);
        }

        var message = BuildFailureMessage("The Squad SDK process exited before the prompt completed.");
        foreach (var pendingRequest in pendingRequests.Where(request => !request.Completion.Task.IsCompleted))
            pendingRequest.Completion.TrySetException(new InvalidOperationException(message));
    }

    private void EnqueueError(string text) {
        lock (_recentErrorLines) {
            if (_recentErrorLines.Count >= 12)
                _recentErrorLines.Dequeue();

            _recentErrorLines.Enqueue(text);
        }
    }

    private void ClearRecentErrors() {
        lock (_recentErrorLines) {
            _recentErrorLines.Clear();
        }
    }

    private string BuildFailureMessage(string fallbackMessage) {
        string[] recentErrors;
        lock (_recentErrorLines) {
            recentErrors = _recentErrorLines.ToArray();
        }

        if (recentErrors.Length == 0)
            return fallbackMessage;

        return $"{fallbackMessage}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, recentErrors)}";
    }

    private static bool ShouldResetSessionAndRetry(string? sessionId, string? message) {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("CAPIError: 400", StringComparison.OrdinalIgnoreCase) ||
               (message.Contains("CAPIError", StringComparison.OrdinalIgnoreCase) &&
                message.Contains("Bad Request", StringComparison.OrdinalIgnoreCase)) ||
               message.Contains("Session not found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("session.send failed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecoverableWriteFailure(Exception ex) {
        if (ex is IOException or ObjectDisposedException)
            return true;

        return ex is InvalidOperationException;
    }

    private async Task WaitForInjectedBridgeToSettleAsync() {
        if (_processStartInfoFactory is null)
            return;

        Process? process;
        lock (_stateLock) {
            process = _activeProcess;
        }

        if (process is null)
            return;

        try {
            await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(75)).ConfigureAwait(false);
        }
        catch {
        }
    }

    /// <summary>
    /// Kills the active bridge process (if any) so it will restart with fresh env vars on the next request.
    /// Call this after updating <see cref="ByokProviderSettings"/> to ensure the new settings take effect.
    /// </summary>
    internal void RestartBridgeForNewSettings() => ResetProcess();

    private void ResetProcess() {
        Process? processToKill;
        PendingBackgroundCancelRequest[] pendingCancels;

        lock (_stateLock) {
            processToKill = _activeProcess;
            pendingCancels = _pendingBackgroundCancels.Values.ToArray();
            _pendingBackgroundCancels.Clear();
            _activeProcess = null;
            _activeProcessInput = null;
            _outputReaderTask = null;
            _activePromptRequestId = null;
        }

        foreach (var pendingCancel in pendingCancels)
            pendingCancel.Completion.TrySetResult(false);

        if (processToKill is not null)
            TryKill(processToKill);
    }

    private static void TryKill(Process process) {
        try {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch {
        }
    }

    public async ValueTask DisposeAsync() {
        var sw = Stopwatch.StartNew();
        ResetProcess();
        SquadDashTrace.Write(TraceCategory.Shutdown, $"SDK DisposeAsync: ResetProcess (kill) {sw.ElapsedMilliseconds}ms.");
        _stdinWriteLock.Dispose();
        _promptLock.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public void AbortPrompt() {
        SquadDashTrace.Write("Bridge", "AbortPrompt requested.");

        string? requestId;
        lock (_stateLock) {
            requestId = _activePromptRequestId;
        }

        if (string.IsNullOrWhiteSpace(requestId))
            return;

        _ = SendAbortRequestAsync(requestId);
    }

    public async Task RunLoopAsync(string loopMdPath, string cwd, string? sessionId = null) {
        if (string.IsNullOrWhiteSpace(loopMdPath))
            throw new ArgumentException("Loop markdown path cannot be empty.", nameof(loopMdPath));
        if (string.IsNullOrWhiteSpace(cwd))
            throw new ArgumentException("Working directory cannot be empty.", nameof(cwd));

        var requestId = Guid.NewGuid().ToString("N");
        SquadDashTrace.Write("Bridge", $"RunLoopAsync loopMdPath={loopMdPath} cwd={cwd} sessionId={sessionId ?? "(auto)"}");

        await SendBridgeRequestWithRestartAsync(new SquadSdkRunLoopRequest(loopMdPath.Trim(), cwd.Trim(), requestId, sessionId?.Trim()))
            .ConfigureAwait(false);
    }

    public async Task<bool> CancelBackgroundTaskAsync(string taskId, string? sessionId = null) {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("Background task id cannot be empty.", nameof(taskId));

        var normalizedTaskId = taskId.Trim();
        var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim();
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingCancel = new PendingBackgroundCancelRequest(
            normalizedTaskId,
            normalizedSessionId,
            completion);

        lock (_stateLock) {
            _pendingBackgroundCancels[requestId] = pendingCancel;
        }

        SquadDashTrace.Write(
            "Bridge",
            $"CancelBackgroundTaskAsync requested requestId={requestId} taskId={normalizedTaskId} sessionId={normalizedSessionId ?? "(auto)"}");

        try {
            await SendBridgeRequestAsync(new SquadSdkCancelBackgroundTaskRequest(
                    normalizedTaskId,
                    normalizedSessionId,
                    requestId))
                .ConfigureAwait(false);

            var completedTask = await Task
                .WhenAny(completion.Task, Task.Delay(DefaultBackgroundCancelResponseTimeout))
                .ConfigureAwait(false);
            if (completedTask == completion.Task) {
                var cancelled = await completion.Task.ConfigureAwait(false);
                SquadDashTrace.Write(
                    "Bridge",
                    $"CancelBackgroundTaskAsync completed requestId={requestId} taskId={normalizedTaskId} cancelled={cancelled}");
                return cancelled;
            }

            SquadDashTrace.Write(
                "Bridge",
                $"CancelBackgroundTaskAsync timed out waiting for acknowledgement requestId={requestId} taskId={normalizedTaskId}");
            return false;
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Bridge", $"Failed to send background task cancel request for {normalizedTaskId}: {ex.Message}");
            ErrorReceived?.Invoke(this, ex.Message);
            throw;
        }
        finally {
            lock (_stateLock) {
                _pendingBackgroundCancels.Remove(requestId);
            }
        }
    }

    private async Task SendAbortRequestAsync(string requestId) {
        try {
            await SendBridgeRequestAsync(new SquadSdkAbortRequest(requestId)).ConfigureAwait(false);
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Bridge", $"Failed to send abort request for {requestId}: {ex.Message}");
            ErrorReceived?.Invoke(this, ex.Message);
        }
    }

    public async Task StopLoopAsync() {
        var requestId = Guid.NewGuid().ToString("N");
        SquadDashTrace.Write("Bridge", "StopLoopAsync");
        await SendBridgeRequestAsync(new SquadSdkRunLoopStopRequest(requestId))
            .ConfigureAwait(false);
    }

    public async Task StartRemoteAsync(
        string repo,
        string branch,
        string machine,
        string squadDir,
        string cwd,
        int port = 0,
        string? sessionId = null,
        string? tunnelMode = null,
        string? tunnelToken = null,
        string? rcToken = null) {
        if (string.IsNullOrWhiteSpace(repo))
            throw new ArgumentException("Repo name cannot be empty.", nameof(repo));
        if (string.IsNullOrWhiteSpace(cwd))
            throw new ArgumentException("Working directory cannot be empty.", nameof(cwd));

        var requestId = Guid.NewGuid().ToString("N");
        SquadDashTrace.Write("Bridge", $"StartRemoteAsync repo={repo} branch={branch} port={port} tunnelMode={tunnelMode ?? "none"}");

        await SendBridgeRequestWithRestartAsync(
            new SquadSdkRcStartRequest(port, repo, branch, machine, squadDir, cwd, requestId, sessionId,
                string.IsNullOrWhiteSpace(tunnelMode) ? null : tunnelMode,
                string.IsNullOrWhiteSpace(tunnelToken) ? null : tunnelToken,
                string.IsNullOrWhiteSpace(rcToken) ? null : rcToken))
            .ConfigureAwait(false);
    }

    public async Task StopRemoteAsync() {
        SquadDashTrace.Write("Bridge", "StopRemoteAsync");
        var requestId = Guid.NewGuid().ToString("N");
        try {
            await SendBridgeRequestAsync(new SquadSdkRcStopRequest(requestId)).ConfigureAwait(false);
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Bridge", $"Failed to send rc_stop request: {ex.Message}");
        }
    }

    public async Task BroadcastRcStatusAsync(bool busy) {
        try {
            await SendBridgeRequestAsync(new SquadSdkRcStatusBroadcastRequest(busy ? "busy" : "idle"))
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Bridge", $"Failed to broadcast rc_status: {ex.Message}");
        }
    }

    public async Task BroadcastRcCommitAsync(string sha, string? commitUrl) {
        try {
            await SendBridgeRequestAsync(new SquadSdkRcCommitBroadcastRequest(sha, commitUrl))
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Bridge", $"Failed to broadcast rc_commit: {ex.Message}");
        }
    }

    public async Task BroadcastRcAgentRosterAsync(IReadOnlyList<(string Handle, string DisplayName, string AccentHex)> agents) {
        try {
            var agentDtos = agents
                .Select(a => new RcAgentRosterEntry(a.Handle, a.DisplayName, a.AccentHex))
                .ToList();
            await SendBridgeRequestAsync(new SquadSdkRcAgentRosterBroadcastRequest(agentDtos))
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Bridge", $"Failed to broadcast rc_agent_roster: {ex.Message}");
        }
    }

    public async Task BroadcastRcPromptAsync(string text) {
        try {
            await SendBridgeRequestAsync(new SquadSdkRcPromptBroadcastRequest(text))
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Bridge", $"Failed to broadcast rc_prompt: {ex.Message}");
        }
    }

    public async Task ListSubSquadsAsync(string cwd) {
        SquadDashTrace.Write("Bridge", $"ListSubSquadsAsync cwd={cwd}");
        var requestId = Guid.NewGuid().ToString("N");
        try {
            await SendBridgeRequestAsync(new SquadSdkSubSquadsListRequest(cwd, requestId))
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Bridge", $"Failed to send subsquads_list request: {ex.Message}");
        }
    }

    public async Task ActivateSubSquadAsync(string cwd, string subSquadName) {
        SquadDashTrace.Write("Bridge", $"ActivateSubSquadAsync cwd={cwd} subSquadName={subSquadName}");
        var requestId = Guid.NewGuid().ToString("N");
        try {
            await SendBridgeRequestAsync(new SquadSdkSubSquadsActivateRequest(subSquadName, cwd, requestId))
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Bridge", $"Failed to send subsquads_activate request: {ex.Message}");
        }
    }

    public async Task ListPersonalAgentsAsync() {
        SquadDashTrace.Write("Bridge", "ListPersonalAgentsAsync");
        var requestId = Guid.NewGuid().ToString("N");
        try {
            await SendBridgeRequestAsync(new SquadSdkPersonalListRequest(requestId))
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Bridge", $"Failed to send personal_list request: {ex.Message}");
        }
    }

    public async Task InitPersonalSquadAsync() {
        SquadDashTrace.Write("Bridge", "InitPersonalSquadAsync");
        var requestId = Guid.NewGuid().ToString("N");
        try {
            await SendBridgeRequestAsync(new SquadSdkPersonalInitRequest(requestId))
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            SquadDashTrace.Write("Bridge", $"Failed to send personal_init request: {ex.Message}");
        }
    }
}

internal enum BridgeRequestOutcome {
    Completed,
    Aborted
}

internal sealed class PendingBridgeRequest {
    private long _lastActivityAtUtcTicks;

    public PendingBridgeRequest(
        TaskCompletionSource<BridgeRequestOutcome> completion,
        string? sessionId,
        bool allowRecoverableSessionReset) {
        Completion = completion;
        SessionId = sessionId;
        AllowRecoverableSessionReset = allowRecoverableSessionReset;
        MarkActivity();
    }

    public TaskCompletionSource<BridgeRequestOutcome> Completion { get; }
    public string? SessionId { get; }
    public bool AllowRecoverableSessionReset { get; }

    public void MarkActivity() =>
        Interlocked.Exchange(ref _lastActivityAtUtcTicks, DateTimeOffset.UtcNow.Ticks);

    public DateTimeOffset GetLastActivityAt() =>
        new(Interlocked.Read(ref _lastActivityAtUtcTicks), TimeSpan.Zero);

    public TimeSpan GetInactivityDuration(DateTimeOffset now) =>
        now - GetLastActivityAt();
}

internal sealed class PendingBackgroundCancelRequest {
    public PendingBackgroundCancelRequest(
        string taskId,
        string? sessionId,
        TaskCompletionSource<bool> completion) {
        TaskId = taskId;
        SessionId = sessionId;
        Completion = completion;
    }

    public string TaskId { get; }
    public string? SessionId { get; }
    public TaskCompletionSource<bool> Completion { get; }
}

internal sealed class SquadSdkProcessOptions {
    public TimeSpan PromptInactivityTimeout { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan PromptTimeoutPollInterval { get; init; } = TimeSpan.FromMilliseconds(250);
}

internal sealed record SquadSdkPromptRequest(
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("cwd")] string Cwd,
    [property: JsonPropertyName("sessionId")] string? SessionId = null,
    [property: JsonPropertyName("configDir")] string? ConfigDirectory = null,
    [property: JsonPropertyName("requestId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequestId = null,
    [property: JsonPropertyName("type")] string Type = "prompt");

internal sealed record SquadSdkDelegateRequest(
    [property: JsonPropertyName("selectedOption")] string SelectedOption,
    [property: JsonPropertyName("targetAgent")] string TargetAgent,
    [property: JsonPropertyName("cwd")] string Cwd,
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("configDir")] string? ConfigDirectory = null,
    [property: JsonPropertyName("requestId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequestId = null,
    [property: JsonPropertyName("type")] string Type = "delegate");

internal sealed record SquadSdkNamedAgentRequest(
    [property: JsonPropertyName("targetAgent")] string TargetAgent,
    [property: JsonPropertyName("selectedOption")] string SelectedOption,
    [property: JsonPropertyName("handoffContext"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HandoffContext,
    [property: JsonPropertyName("cwd")] string Cwd,
    [property: JsonPropertyName("sessionId")] string? SessionId = null,
    [property: JsonPropertyName("configDir")] string? ConfigDirectory = null,
    [property: JsonPropertyName("requestId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequestId = null,
    [property: JsonPropertyName("type")] string Type = "named_agent");

internal sealed record SquadSdkAbortRequest(
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("type")] string Type = "abort");

internal sealed record SquadSdkCancelBackgroundTaskRequest(
    [property: JsonPropertyName("taskId")] string TaskId,
    [property: JsonPropertyName("sessionId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SessionId = null,
    [property: JsonPropertyName("requestId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequestId = null,
    [property: JsonPropertyName("type")] string Type = "cancel_background_task");

internal sealed record SquadSdkRunLoopRequest(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("loopMdPath")] string LoopMdPath,
    [property: JsonPropertyName("cwd")] string Cwd,
    [property: JsonPropertyName("requestId")] string? RequestId,
    [property: JsonPropertyName("sessionId")] string? SessionId)
{
    public SquadSdkRunLoopRequest(string loopMdPath, string cwd, string? requestId, string? sessionId)
        : this("run_loop", loopMdPath, cwd, requestId, sessionId) { }
}

internal sealed record SquadSdkRunLoopStopRequest(
    [property: JsonPropertyName("requestId")] string? RequestId,
    [property: JsonPropertyName("type")] string Type = "run_loop_stop");

internal sealed record SquadSdkRcStartRequest(
    [property: JsonPropertyName("port")] int Port,
    [property: JsonPropertyName("repo")] string Repo,
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("machine")] string Machine,
    [property: JsonPropertyName("squadDir")] string SquadDir,
    [property: JsonPropertyName("cwd")] string Cwd,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("sessionId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SessionId = null,
    [property: JsonPropertyName("tunnelMode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TunnelMode = null,
    [property: JsonPropertyName("tunnelToken"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TunnelToken = null,
    [property: JsonPropertyName("rcToken"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RcToken = null,
    [property: JsonPropertyName("type")] string Type = "rc_start");

internal sealed record SquadSdkRcStopRequest(
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("type")] string Type = "rc_stop");

internal sealed record SquadSdkRcCommitBroadcastRequest(
    [property: JsonPropertyName("sha")] string Sha,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("type")] string Type = "rc_commit_broadcast");

internal sealed record SquadSdkRcStatusBroadcastRequest(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("type")] string Type = "rc_status_broadcast");

internal sealed record RcAgentRosterEntry(
    [property: JsonPropertyName("handle")] string Handle,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("accentHex")] string AccentHex);

internal sealed record SquadSdkRcAgentRosterBroadcastRequest(
    [property: JsonPropertyName("agents")] List<RcAgentRosterEntry> Agents,
    [property: JsonPropertyName("type")] string Type = "rc_agent_roster_broadcast");

internal sealed record SquadSdkRcPromptBroadcastRequest(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("type")] string Type = "rc_prompt_broadcast");

internal sealed record SquadSdkSubSquadsListRequest(
    [property: JsonPropertyName("cwd")] string Cwd,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("type")] string Type = "subsquads_list");

internal sealed record SquadSdkSubSquadsActivateRequest(
    [property: JsonPropertyName("subSquadName")] string SubSquadName,
    [property: JsonPropertyName("cwd")] string Cwd,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("type")] string Type = "subsquads_activate");

internal sealed record SquadSdkPersonalListRequest(
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("type")] string Type = "personal_list");

internal sealed record SquadSdkPersonalInitRequest(
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("type")] string Type = "personal_init");

internal sealed class RecoverableSessionResetException : InvalidOperationException {
    public RecoverableSessionResetException(string message)
        : base(message) {
    }
}
