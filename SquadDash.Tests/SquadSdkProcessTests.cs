using System.Diagnostics;
using System.Text;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class SquadSdkProcessTests {
    private string _tempDir = null!;

    [SetUp]
    public void SetUp() {
        _tempDir = Path.Combine(Path.GetTempPath(), "SquadSdkProcessTests", Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ------------------------------------------------------------------
    // Argument validation (no process needed)
    // ------------------------------------------------------------------

    [Test]
    public async Task RunPromptAsync_EmptyPrompt_ThrowsArgumentException() {
        await using var sut = new SquadSdkProcess(BuildStartInfo("@echo off"));

        Assert.That(
            async () => await sut.RunPromptAsync("   ", "C:\\some\\dir"),
            Throws.TypeOf<ArgumentException>().With.Message.Contains("Prompt"));
    }

    [Test]
    public async Task RunPromptAsync_EmptyWorkingDirectory_ThrowsArgumentException() {
        await using var sut = new SquadSdkProcess(BuildStartInfo("@echo off"));

        Assert.That(
            async () => await sut.RunPromptAsync("hello", "   "),
            Throws.TypeOf<ArgumentException>().With.Message.Contains("Working directory"));
    }

    [Test]
    public async Task RunNamedAgentDelegationAsync_EmptySessionId_ThrowsArgumentException() {
        await using var sut = new SquadSdkProcess(BuildStartInfo("@echo off"));

        Assert.That(
            async () => await sut.RunNamedAgentDelegationAsync("Hand off to Lyra", "lyra-morn", _tempDir, "   "),
            Throws.TypeOf<ArgumentException>().With.Message.Contains("active Squad session"));
    }

    // ------------------------------------------------------------------
    // Happy path
    // ------------------------------------------------------------------

    [Test]
    public async Task RunPromptAsync_DoneEvent_CompletesWithoutException() {
        await using var sut = new SquadSdkProcess(BuildStartInfo(
            """echo {"type":"done","message":""}"""));

        Assert.That(
            async () => await sut.RunPromptAsync("hello", _tempDir),
            Throws.Nothing);
    }

    [Test]
    public async Task RunPromptAsync_NonControlEvent_FiresEventReceived() {
        var events = new List<SquadSdkEvent>();

        await using var sut = new SquadSdkProcess(BuildStartInfo("""
            echo {"type":"response","message":"hello back"}
            echo {"type":"done","message":""}
            """));
        sut.EventReceived += (_, e) => events.Add(e);

        await sut.RunPromptAsync("hello", _tempDir);

        Assert.That(events.Select(e => e.Type), Contains.Item("response"));
    }

    [Test]
    public async Task RunPromptAsync_DoneEvent_AlsoFiresEventReceived() {
        var types = new List<string?>();

        await using var sut = new SquadSdkProcess(BuildStartInfo("""
            echo {"type":"response","message":"hi"}
            echo {"type":"done","message":""}
            """));
        sut.EventReceived += (_, e) => types.Add(e.Type);

        await sut.RunPromptAsync("hello", _tempDir);

        Assert.That(types, Is.EquivalentTo(new[] { "response", "done" }));
    }

    [Test]
    public async Task RunPromptAsync_SubagentCompletedEvent_FiresEventReceivedWithLifecycleFields() {
        var events = new List<SquadSdkEvent>();

        await using var sut = new SquadSdkProcess(BuildStartInfo("""
            echo {"type":"subagent_completed","agentName":"wanda-review","agentDisplayName":"WandaMaximoff","agentDescription":"Review options page changes"}
            echo {"type":"done","message":""}
            """));
        sut.EventReceived += (_, e) => events.Add(e);

        await sut.RunPromptAsync("hello", _tempDir);

        var completionEvent = events.Single(e => e.Type == "subagent_completed");
        Assert.Multiple(() => {
            Assert.That(completionEvent.AgentName, Is.EqualTo("wanda-review"));
            Assert.That(completionEvent.AgentDisplayName, Is.EqualTo("WandaMaximoff"));
            Assert.That(completionEvent.AgentDescription, Is.EqualTo("Review options page changes"));
        });
    }

    [Test]
    public async Task RunPromptAsync_DoesNotTimeoutWhileBridgeKeepsSendingEvents() {
        await using var sut = new SquadSdkProcess(
            () => BuildPowerShellScriptStartInfo("""
                Write-Output '{"type":"session_ready","sessionId":"session-1"}'
                Start-Sleep -Milliseconds 250
                Write-Output '{"type":"response_delta","chunk":"a"}'
                Start-Sleep -Milliseconds 250
                Write-Output '{"type":"response_delta","chunk":"b"}'
                Start-Sleep -Milliseconds 250
                Write-Output '{"type":"done","message":""}'
                """),
            new SquadSdkProcessOptions {
                // Timeout must exceed powershell.exe startup time (which can be >1 second on some
                // machines) while still being comfortably longer than the 250 ms inter-event gap
                // tested here, so that events arriving every 250 ms reliably prevent the timeout.
                PromptInactivityTimeout = TimeSpan.FromSeconds(10),
                PromptTimeoutPollInterval = TimeSpan.FromMilliseconds(25)
            });

        Assert.That(
            async () => await sut.RunPromptAsync("hello", _tempDir),
            Throws.Nothing);
    }

    [Test]
    public async Task RunPromptAsync_TimesOutAfterConfiguredInactivityWindow() {
        await using var sut = new SquadSdkProcess(
            () => BuildPowerShellScriptStartInfo("""
                Start-Sleep -Milliseconds 250
                Write-Output '{"type":"done","message":""}'
                """),
            new SquadSdkProcessOptions {
                PromptInactivityTimeout = TimeSpan.FromMilliseconds(80),
                PromptTimeoutPollInterval = TimeSpan.FromMilliseconds(20)
            });

        var ex = Assert.ThrowsAsync<TimeoutException>(
            async () => await sut.RunPromptAsync("hello", _tempDir));

        Assert.That(ex!.Message, Does.Contain("without bridge activity"));
    }

    // ------------------------------------------------------------------
    // Error event
    // ------------------------------------------------------------------

    [Test]
    public async Task RunPromptAsync_ErrorEvent_ThrowsInvalidOperationException() {
        await using var sut = new SquadSdkProcess(BuildStartInfo(
            """echo {"type":"error","message":"something exploded"}"""));

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sut.RunPromptAsync("hello", _tempDir));

        Assert.That(ex!.Message, Does.Contain("something exploded"));
    }

    [Test]
    public async Task RunPromptAsync_ResumedSessionNotFound_RetriesWithFreshSession() {
        var events = new List<SquadSdkEvent>();
        var scriptQueue = new Queue<string>(new[] {
            """
            echo {"type":"session_ready","sessionId":"stale-session","sessionResumed":true}
            echo {"type":"error","message":"Request session.send failed with message: Session not found: 6f83aac8-484c-46d7-a85e-e6f727571855"}
            """,
            """
            echo {"type":"session_ready","sessionId":"fresh-session","sessionResumed":false}
            echo {"type":"response","message":"hello back"}
            echo {"type":"done","message":""}
            """
        });
        var processStarts = 0;

        ProcessStartInfo Factory() {
            processStarts++;
            return BuildScriptStartInfo(scriptQueue.Dequeue());
        }

        await using var sut = new SquadSdkProcess(Factory);
        sut.EventReceived += (_, e) => events.Add(e);

        await sut.RunPromptAsync(
            "hello",
            _tempDir,
            sessionId: "stale-session",
            configDirectory: Path.Combine(_tempDir, "sdk-config"));

        Assert.Multiple(() => {
            Assert.That(processStarts, Is.EqualTo(2));
            Assert.That(
                events.Select(e => e.Type).ToArray(),
                Is.EqualTo(new[] { "session_ready", "session_reset", "session_ready", "response", "done" }));
            Assert.That(events.Any(e => string.Equals(e.Type, "error", StringComparison.Ordinal)), Is.False);
            Assert.That(
                events.Single(e => string.Equals(e.Type, "session_reset", StringComparison.Ordinal)).Message,
                Does.Contain("fresh session"));
        });
    }

    [Test]
    public async Task RunPromptAsync_ResumedSession400_RetriesWithFreshSession() {
        var events = new List<SquadSdkEvent>();
        var scriptQueue = new Queue<string>(new[] {
            """
            echo {"type":"session_ready","sessionId":"poisoned-session","sessionResumed":true}
            echo {"type":"error","message":"Execution failed: CAPIError: 400 400 Bad Request (Request ID: TEST-123)"}
            """,
            """
            echo {"type":"session_ready","sessionId":"fresh-session","sessionResumed":false}
            echo {"type":"response","message":"hello back"}
            echo {"type":"done","message":""}
            """
        });
        var processStarts = 0;

        ProcessStartInfo Factory() {
            processStarts++;
            return BuildScriptStartInfo(scriptQueue.Dequeue());
        }

        await using var sut = new SquadSdkProcess(Factory);
        sut.EventReceived += (_, e) => events.Add(e);

        await sut.RunPromptAsync(
            "hello",
            _tempDir,
            sessionId: "poisoned-session",
            configDirectory: Path.Combine(_tempDir, "sdk-config"));

        Assert.Multiple(() => {
            Assert.That(processStarts, Is.EqualTo(2));
            Assert.That(
                events.Select(e => e.Type).ToArray(),
                Is.EqualTo(new[] { "session_ready", "session_reset", "session_ready", "response", "done" }));
            Assert.That(events.Any(e => string.Equals(e.Type, "error", StringComparison.Ordinal)), Is.False);
            Assert.That(
                events.Single(e => string.Equals(e.Type, "session_reset", StringComparison.Ordinal)).Message,
                Does.Contain("fresh session"));
        });
    }

    [Test]
    public async Task RunNamedAgentDelegationAsync_ResumedSession400_DoesNotRetryWithFreshSession() {
        var scriptQueue = new Queue<string>(new[] {
            """
            echo {"type":"session_ready","sessionId":"poisoned-session","sessionResumed":true}
            echo {"type":"error","message":"Execution failed: CAPIError: 400 400 Bad Request (Request ID: TEST-123)"}
            """,
            """
            echo {"type":"session_ready","sessionId":"fresh-session","sessionResumed":false}
            echo {"type":"done","message":""}
            """
        });
        var processStarts = 0;

        ProcessStartInfo Factory() {
            processStarts++;
            return BuildScriptStartInfo(scriptQueue.Dequeue());
        }

        await using var sut = new SquadSdkProcess(Factory);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sut.RunNamedAgentDelegationAsync(
                "Hand off to Lyra",
                "lyra-morn",
                _tempDir,
                sessionId: "poisoned-session",
                configDirectory: Path.Combine(_tempDir, "sdk-config")));

        Assert.Multiple(() => {
            Assert.That(processStarts, Is.EqualTo(1));
            Assert.That(ex!.Message, Does.Contain("CAPIError: 400"));
        });
    }

    // ------------------------------------------------------------------
    // Process exits early
    // ------------------------------------------------------------------

    [Test]
    public async Task RunPromptAsync_ProcessExitsWithoutDone_ThrowsInvalidOperationException() {
        // Script writes nothing to stdout, just exits
        await using var sut = new SquadSdkProcess(BuildStartInfo("@echo off"));

        Assert.That(
            async () => await sut.RunPromptAsync("hello", _tempDir),
            Throws.TypeOf<InvalidOperationException>()
                  .With.Message.Contains("exited before the prompt completed"));
    }

    // ------------------------------------------------------------------
    // Stderr → ErrorReceived
    // ------------------------------------------------------------------

    [Test]
    public async Task RunPromptAsync_StderrOutput_FiresErrorReceived() {
        var errors = new List<string>();

        // Write to stderr then complete normally
        await using var sut = new SquadSdkProcess(BuildStartInfo("""
            echo stderr line 1>&2
            echo {"type":"done","message":""}
            """));
        sut.ErrorReceived += (_, msg) => errors.Add(msg);

        await sut.RunPromptAsync("hello", _tempDir);

        Assert.That(errors, Has.Count.GreaterThan(0));
    }

    // ------------------------------------------------------------------
    // Concurrent calls are serialized
    // ------------------------------------------------------------------

    [Test]
    public async Task RunPromptAsync_ConcurrentCalls_RunSequentiallyNotConcurrently() {
        var completionOrder = new List<int>();
        var processStarts = 0;

        ProcessStartInfo Factory() {
            processStarts++;
            return BuildPowerShellScriptStartInfo("""
                while (($line = [Console]::In.ReadLine()) -ne $null) {
                    if ([string]::IsNullOrWhiteSpace($line)) {
                        continue
                    }

                    $request = $line | ConvertFrom-Json
                    if ($request.type -eq 'abort') {
                        continue
                    }

                    Write-Output ('{"type":"done","requestId":"' + $request.requestId + '"}')
                }
                """);
        }

        await using var sut = new SquadSdkProcess(Factory);
        sut.EventReceived += (_, e) => {
            if (e.Type == "done")
                lock (completionOrder)
                    completionOrder.Add(completionOrder.Count + 1);
        };

        // Launch two concurrent prompts
        var t1 = sut.RunPromptAsync("first", _tempDir);
        var t2 = sut.RunPromptAsync("second", _tempDir);

        await Task.WhenAll(t1, t2);

        // Both completed — the semaphore ensured sequential execution
        Assert.Multiple(() => {
            Assert.That(completionOrder, Has.Count.EqualTo(2));
            Assert.That(processStarts, Is.EqualTo(1));
        });
    }

    // ------------------------------------------------------------------
    // Non-JSON stdout lines are tolerated
    // ------------------------------------------------------------------

    [Test]
    public async Task RunPromptAsync_NonJsonStdoutLine_DoesNotThrow_AndFiresErrorReceived() {
        var errors = new List<string>();

        await using var sut = new SquadSdkProcess(BuildStartInfo("""
            echo this is not json at all
            echo {"type":"done","message":""}
            """));
        sut.ErrorReceived += (_, msg) => errors.Add(msg);

        Assert.That(
            async () => await sut.RunPromptAsync("hello", _tempDir),
            Throws.Nothing);

        Assert.That(errors.Any(e => e.StartsWith("[non-json]")), Is.True);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>Creates a ProcessStartInfo that runs a cmd.exe script from inline lines.</summary>
    private Func<ProcessStartInfo> BuildStartInfo(string scriptLines) => () => BuildScriptStartInfo(scriptLines);

    private ProcessStartInfo BuildScriptStartInfo(string scriptLines) {
        var scriptPath = Path.Combine(_tempDir, $"fake_{Path.GetRandomFileName()}.cmd");
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        foreach (var line in scriptLines.Split('\n'))
            sb.AppendLine(line.Trim('\r'));
        File.WriteAllText(scriptPath, sb.ToString(), Encoding.ASCII);

        return new ProcessStartInfo {
            FileName = "cmd.exe",
            Arguments = $"/c \"{scriptPath}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
    }

    private ProcessStartInfo BuildPowerShellScriptStartInfo(string scriptBody) {
        var scriptPath = Path.Combine(_tempDir, $"fake_{Path.GetRandomFileName()}.ps1");
        File.WriteAllText(scriptPath, scriptBody, Encoding.UTF8);

        return new ProcessStartInfo {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
    }
}
