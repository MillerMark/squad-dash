using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SquadDash.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class SquadSdkProcessTests {
    private TestWorkspace _workspace = null!;

    [SetUp]
    public void SetUp() => _workspace = new TestWorkspace();

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    [Test]
    public void DefaultBridgeInactivityCheck_IsFifteenMinutes() {
        var options = new SquadSdkProcessOptions();

        Assert.That(options.PromptInactivityTimeout, Is.EqualTo(TimeSpan.FromMinutes(15)));
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
    public async Task RunLoopAsync_EmptyLoopMdPath_ThrowsArgumentException() {
        await using var sut = new SquadSdkProcess(BuildStartInfo("@echo off"));

        Assert.That(
            async () => await sut.RunLoopAsync("   ", "C:\\some\\dir"),
            Throws.TypeOf<ArgumentException>().With.Message.Contains("Loop markdown path"));
    }

    [Test]
    public async Task RunLoopAsync_EmptyWorkingDirectory_ThrowsArgumentException() {
        await using var sut = new SquadSdkProcess(BuildStartInfo("@echo off"));

        Assert.That(
            async () => await sut.RunLoopAsync("C:\\workspace\\loop.md", "   "),
            Throws.TypeOf<ArgumentException>().With.Message.Contains("Working directory"));
    }

    /// <summary>
    /// Regression test for the Fix 1 change: RunLoopAsync must call
    /// SendBridgeRequestWithRestartAsync (which calls EnsureProcessStarted) rather than
    /// SendBridgeRequestAsync (which threw "The Squad bridge is not running." when no
    /// process had been started yet).
    /// </summary>
    [Test]
    public async Task RunLoopAsync_ValidArgs_DoesNotThrowBridgeNotRunning() {
        // Process reads one line from stdin then exits — enough for EnsureProcessStarted
        // to succeed and the write to complete before the process dies.
        await using var sut = new SquadSdkProcess(BuildStartInfo("set /p x="));

        // The call may succeed or fail with a "process exited" error — both are fine.
        // What must NOT happen is InvalidOperationException("The Squad bridge is not running.").
        InvalidOperationException? bridgeNotRunningEx = null;
        try {
            await sut.RunLoopAsync("C:\\workspace\\.squad\\loop.md", "C:\\workspace");
        } catch (InvalidOperationException ioe) when (ioe.Message.Contains("Squad bridge is not running")) {
            bridgeNotRunningEx = ioe;
        } catch {
            // Any other exception (e.g. "process exited before prompt completed") is acceptable.
        }

        Assert.That(bridgeNotRunningEx, Is.Null,
            "RunLoopAsync must not throw 'bridge not running' — it should auto-start the process.");
    }

    [Test]
    public async Task StartRemoteAsync_EmptyRepo_ThrowsArgumentException() {
        await using var sut = new SquadSdkProcess(BuildStartInfo("@echo off"));

        Assert.That(
            async () => await sut.StartRemoteAsync("   ", "main", "my-machine", "C:\\workspace\\.squad", "C:\\workspace"),
            Throws.TypeOf<ArgumentException>().With.Message.Contains("Repo"));
    }

    [Test]
    public async Task StartRemoteAsync_EmptyWorkingDirectory_ThrowsArgumentException() {
        await using var sut = new SquadSdkProcess(BuildStartInfo("@echo off"));

        Assert.That(
            async () => await sut.StartRemoteAsync("my-repo", "main", "my-machine", "C:\\workspace\\.squad", "   "),
            Throws.TypeOf<ArgumentException>().With.Message.Contains("Working directory"));
    }

    [Test]
    public void ResolveByokWireApi_LocalOpenAiProvider_UsesCompletionsForOllamaCompatibility() {
        var settings = new ByokProviderSettings(
            "http://127.0.0.1:11435/v1",
            "lfm2.5:8b",
            "openai",
            ApiKey: null,
            OfflineMode: true);

        Assert.That(SquadSdkProcess.ResolveByokWireApi(settings), Is.EqualTo("completions"));
    }

    [Test]
    public void ResolveByokWireApi_LanOllamaProvider_UsesCompletionsForOllamaCompatibility() {
        var settings = new ByokProviderSettings(
            "http://192.168.1.61:11434/v1",
            "qwen3.6:latest",
            "openai",
            ApiKey: null);

        Assert.That(SquadSdkProcess.ResolveByokWireApi(settings), Is.EqualTo("completions"));
    }

    [Test]
    public void ResolveByokWireApi_RemoteOpenAiProvider_LeavesSdkDefault() {
        var settings = new ByokProviderSettings(
            "https://api.openai.example/v1",
            "some-model",
            "openai",
            ApiKey: null);

        Assert.That(SquadSdkProcess.ResolveByokWireApi(settings), Is.Null);
    }

    [Test]
    public void BuildNamedAgentRoute_CustomProfileCarriesProviderAndCopilotProfileSuppressesIt() {
        var custom = new ModelProfile(
            "openai-small", "OpenAI Small", "openai", "https://api.openai.example/v1",
            "gpt-5-mini", "test-key", ReasoningEffort: "high");
        var copilot = new ModelProfile(
            "copilot", "GitHub Copilot", "copilot", null, "auto", null);

        var customRoute = SquadSdkProcess.BuildNamedAgentRoute(
            "lyra-morn", "Lyra Morn", "Model Profiles", "# Charter", custom);
        var copilotRoute = SquadSdkProcess.BuildNamedAgentRoute(
            "vesper-knox", "Vesper Knox", "Testing", null, copilot);

        Assert.Multiple(() => {
            Assert.That(customRoute.Model, Is.EqualTo("gpt-5-mini"));
            Assert.That(customRoute.Provider, Is.Not.Null);
            Assert.That(customRoute.Provider!.BaseUrl, Is.EqualTo("https://api.openai.example/v1"));
            Assert.That(customRoute.Provider.Type, Is.EqualTo("openai"));
            Assert.That(customRoute.Provider.ApiKey, Is.EqualTo("test-key"));
            Assert.That(customRoute.ReasoningEffort, Is.EqualTo("high"));
            Assert.That(copilotRoute.Model, Is.Null);
            Assert.That(copilotRoute.Provider, Is.Null);
            Assert.That(copilotRoute.ReasoningEffort, Is.Null);
        });
    }

    [Test]
    public void BuildTemporaryAgentRoute_NullProfile_ReturnsNull() {
        Assert.That(SquadSdkProcess.BuildTemporaryAgentRoute(null), Is.Null);
    }

    [Test]
    public void BuildTemporaryAgentRoute_ResolverKeepsTemporaryAssignment_WhenMatchingCoordinatorProfile() {
        var profiles = new[] {
            new ModelProfile("prof-a", "Profile A", "copilot", null, "claude-sonnet-4", null),
            new ModelProfile("prof-b", "Profile B", "copilot", null, "gpt-5", null)
        };
        var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [ModelProfileCategory.Coordinator] = "prof-a",
            [ModelProfileCategory.SpawnedNamedAgents] = "prof-b",
            [ModelProfileCategory.TemporaryAgents] = "prof-a"
        };

        var coordinatorProfile = ModelProfileResolver.Resolve(
            profiles, assignments, perAgentOverrideProfileId: null, agentCategory: ModelProfileCategory.Coordinator);
        var namedProfile = ModelProfileResolver.Resolve(
            profiles, assignments, perAgentOverrideProfileId: null, agentCategory: ModelProfileCategory.SpawnedNamedAgents);
        var temporaryProfile = ModelProfileResolver.Resolve(
            profiles, assignments, perAgentOverrideProfileId: null, agentCategory: ModelProfileCategory.TemporaryAgents);

        var namedRoute = SquadSdkProcess.BuildNamedAgentRoute("lyra-morn", "Lyra Morn", "Model Profiles", null, namedProfile);
        var temporaryRoute = SquadSdkProcess.BuildTemporaryAgentRoute(temporaryProfile);
        var json = JsonSerializer.Serialize(new SquadSdkPromptRequest(
            "route check",
            @"D:\repo",
            NamedAgentRoutes: [namedRoute],
            TemporaryAgentRoute: temporaryRoute,
            ProfileId: coordinatorProfile?.Id));

        Assert.Multiple(() => {
            Assert.That(temporaryRoute, Is.Not.Null);
            Assert.That(temporaryRoute!.ProfileId, Is.EqualTo("prof-a"));
            Assert.That(namedRoute.ProfileId, Is.EqualTo("prof-b"));
            Assert.That(json, Does.Contain("\"profileId\":\"prof-a\""));
            Assert.That(json, Does.Contain("\"namedAgentRoutes\""));
            Assert.That(json, Does.Contain("\"temporaryAgentRoute\""));
            Assert.That(json, Does.Contain("\"handle\":\"temporary-agent\""));
        });
    }

    [Test]
    public async Task RunPromptAsync_WithNamedAgentRoutes_SerializesHostRosterAndProfile() {
        var requestLogPath = Path.Combine(_workspace.RootPath, "requests.jsonl");
        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo($$"""
            $requestLog = {{PowerShellSingleQuoted(requestLogPath)}}
            $line = [Console]::In.ReadLine()
            Add-Content -LiteralPath $requestLog -Value $line -Encoding UTF8
            $request = $line | ConvertFrom-Json
            Write-Output ('{"type":"session_ready","sessionId":"route-session","sessionResumed":false,"requestId":"' + $request.requestId + '"}')
            Write-Output ('{"type":"done","requestId":"' + $request.requestId + '"}')
            """));
        sut.NamedAgentRoutes = [SquadSdkProcess.BuildNamedAgentRoute(
            "lyra-morn",
            "Lyra Morn",
            "Model Profiles",
            "# Lyra charter",
            new ModelProfile(
                "openai-small", "OpenAI Small", "openai", "https://api.openai.example/v1",
                "gpt-5-mini", "test-key"))];

        await sut.RunPromptAsync("Delegate this work", _workspace.RootPath);

        using var request = JsonDocument.Parse(FindLoggedRequestLine(requestLogPath, "prompt"));
        var route = request.RootElement.GetProperty("namedAgentRoutes")[0];
        Assert.Multiple(() => {
            Assert.That(route.GetProperty("handle").GetString(), Is.EqualTo("lyra-morn"));
            Assert.That(route.GetProperty("displayName").GetString(), Is.EqualTo("Lyra Morn"));
            Assert.That(route.GetProperty("model").GetString(), Is.EqualTo("gpt-5-mini"));
            Assert.That(route.GetProperty("provider").GetProperty("baseUrl").GetString(),
                Is.EqualTo("https://api.openai.example/v1"));
            Assert.That(route.GetProperty("profileAlias").GetString(), Is.EqualTo("OpenAI Small"));
        });
    }

    [Test]
    public async Task RunPromptAsync_ActiveProfileWithMaxReasoningEffort_PreservesMaxInOutboundPromptRequest() {
        var requestLogPath = Path.Combine(_workspace.RootPath, "requests.jsonl");
        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo($$"""
            $requestLog = {{PowerShellSingleQuoted(requestLogPath)}}
            $line = [Console]::In.ReadLine()
            Add-Content -LiteralPath $requestLog -Value $line -Encoding UTF8
            $request = $line | ConvertFrom-Json
            Write-Output ('{"type":"session_ready","sessionId":"reasoning-max-session","sessionResumed":false,"requestId":"' + $request.requestId + '"}')
            Write-Output ('{"type":"done","requestId":"' + $request.requestId + '"}')
            """));

        sut.ActiveProfile = new ModelProfile(
            Id: "copilot-max",
            Alias: "Copilot Max",
            ProviderType: "copilot",
            ProviderUrl: null,
            Model: "claude-sonnet-4",
            ApiKey: null,
            ReasoningEffort: "max");

        await sut.RunPromptAsync("Use max reasoning effort", _workspace.RootPath);

        using var request = JsonDocument.Parse(FindLoggedRequestLine(requestLogPath, "prompt"));
        Assert.That(request.RootElement.GetProperty("reasoningEffort").GetString(), Is.EqualTo("max"));
    }

    [Test]
    public async Task RunPromptAsync_ProfileId_FollowsActiveProfileAndOmitsWhenUnknown() {
        var requestLogPath = Path.Combine(_workspace.RootPath, "requests.jsonl");
        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo($$"""
            $requestLog = {{PowerShellSingleQuoted(requestLogPath)}}
            for ($i = 0; $i -lt 2; $i++) {
                $line = [Console]::In.ReadLine()
                Add-Content -LiteralPath $requestLog -Value $line -Encoding UTF8
                $request = $line | ConvertFrom-Json
                Write-Output ('{"type":"session_ready","sessionId":"profile-id-session","sessionResumed":false,"requestId":"' + $request.requestId + '"}')
                Write-Output ('{"type":"done","requestId":"' + $request.requestId + '"}')
            }
            """));

        sut.ActiveProfile = new ModelProfile(
            Id: "coord-a",
            Alias: "Coordinator A",
            ProviderType: "copilot",
            ProviderUrl: null,
            Model: "claude-sonnet-4",
            ApiKey: null);

        await sut.RunPromptAsync("first", _workspace.RootPath);
        sut.ActiveProfile = null;
        await sut.RunPromptAsync("second", _workspace.RootPath);

        var promptLines = File.ReadLines(requestLogPath, Encoding.UTF8)
            .Select(line => JsonDocument.Parse(line))
            .Where(doc => doc.RootElement.TryGetProperty("type", out var type) && type.GetString() == "prompt")
            .ToList();

        Assert.That(promptLines, Has.Count.EqualTo(2));
        Assert.Multiple(() => {
            Assert.That(promptLines[0].RootElement.GetProperty("profileId").GetString(), Is.EqualTo("coord-a"));
            Assert.That(promptLines[1].RootElement.TryGetProperty("profileId", out _), Is.False);
        });

        foreach (var doc in promptLines)
            doc.Dispose();
    }

    [Test]
    public async Task RunPromptAsync_LocalByokProvider_DoesNotResumeSavedSession() {
        var requestLogPath = Path.Combine(_workspace.RootPath, "requests.jsonl");

        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo($$"""
            $requestLog = {{PowerShellSingleQuoted(requestLogPath)}}
            $line = [Console]::In.ReadLine()
            Add-Content -LiteralPath $requestLog -Value $line -Encoding UTF8
            $request = $line | ConvertFrom-Json
            Write-Output ('{"type":"session_ready","sessionId":"fresh-local-session","sessionResumed":false,"requestId":"' + $request.requestId + '"}')
            Write-Output ('{"type":"done","requestId":"' + $request.requestId + '"}')
            """));
        sut.ByokProviderSettings = new ByokProviderSettings(
            "http://127.0.0.1:11437/v1",
            "qwen2.5-coder-14b-instruct-cuda-gpu:4",
            "openai",
            ApiKey: null,
            OfflineMode: true);

        await sut.RunPromptAsync(
            "hello",
            _workspace.RootPath,
            sessionId: "saved-session",
            configDirectory: Path.Combine(_workspace.RootPath, "sdk-config"));

        var promptRequestLine = FindLoggedRequestLine(requestLogPath, "prompt");
        using var promptRequest = JsonDocument.Parse(promptRequestLine);

        Assert.That(
            promptRequest.RootElement.TryGetProperty("sessionId", out _),
            Is.False,
            "Local BYOK prompts should start fresh rather than taking the SDK provider-resume path.");
    }

    [Test]
    public async Task RunPromptAsync_LanOllamaByokProvider_DoesNotResumeSavedSession() {
        var requestLogPath = Path.Combine(_workspace.RootPath, "requests.jsonl");

        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo($$"""
            $requestLog = {{PowerShellSingleQuoted(requestLogPath)}}
            $line = [Console]::In.ReadLine()
            Add-Content -LiteralPath $requestLog -Value $line -Encoding UTF8
            $request = $line | ConvertFrom-Json
            Write-Output ('{"type":"session_ready","sessionId":"fresh-local-session","sessionResumed":false,"requestId":"' + $request.requestId + '"}')
            Write-Output ('{"type":"done","requestId":"' + $request.requestId + '"}')
            """));
        sut.ByokProviderSettings = new ByokProviderSettings(
            "http://192.168.1.61:11434/v1",
            "qwen3.6:latest",
            "openai",
            ApiKey: null,
            OfflineMode: true);

        await sut.RunPromptAsync(
            "hello",
            _workspace.RootPath,
            sessionId: "saved-session",
            configDirectory: Path.Combine(_workspace.RootPath, "sdk-config"));

        var promptRequestLine = FindLoggedRequestLine(requestLogPath, "prompt");
        using var promptRequest = JsonDocument.Parse(promptRequestLine);

        Assert.That(
            promptRequest.RootElement.TryGetProperty("sessionId", out _),
            Is.False,
            "LAN Ollama BYOK prompts should start fresh rather than taking the SDK provider-resume path.");
    }

    [Test]
    public async Task RunPromptAsync_RemoteByokProvider_StillResumesSavedSession() {
        var requestLogPath = Path.Combine(_workspace.RootPath, "requests.jsonl");

        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo($$"""
            $requestLog = {{PowerShellSingleQuoted(requestLogPath)}}
            $line = [Console]::In.ReadLine()
            Add-Content -LiteralPath $requestLog -Value $line -Encoding UTF8
            $request = $line | ConvertFrom-Json
            Write-Output ('{"type":"session_ready","sessionId":"' + $request.sessionId + '","sessionResumed":true,"requestId":"' + $request.requestId + '"}')
            Write-Output ('{"type":"done","requestId":"' + $request.requestId + '"}')
            """));
        sut.ByokProviderSettings = new ByokProviderSettings(
            "https://provider.example.com/v1",
            "remote-model",
            "openai",
            ApiKey: "sk-test");

        await sut.RunPromptAsync(
            "hello",
            _workspace.RootPath,
            sessionId: "saved-session",
            configDirectory: Path.Combine(_workspace.RootPath, "sdk-config"));

        var promptRequestLine = FindLoggedRequestLine(requestLogPath, "prompt");
        using var promptRequest = JsonDocument.Parse(promptRequestLine);

        Assert.That(
            promptRequest.RootElement.GetProperty("sessionId").GetString(),
            Is.EqualTo("saved-session"));
    }

    [Test]
    public async Task RunNamedAgentDelegationAsync_EmptySessionId_ThrowsArgumentException() {
        await using var sut = new SquadSdkProcess(BuildStartInfo("@echo off"));

        Assert.That(
            async () => await sut.RunNamedAgentDelegationAsync("Hand off to Lyra", "lyra-morn", _workspace.RootPath, "   "),
            Throws.TypeOf<ArgumentException>().With.Message.Contains("active Squad session"));
    }

    [Test]
    public async Task RunNamedAgentDelegationAsync_WithApprovalGroupContext_SendsContext() {
        var requestLogPath = Path.Combine(_workspace.RootPath, "requests.jsonl");

        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo($$"""
            $requestLog = {{PowerShellSingleQuoted(requestLogPath)}}
            $line = [Console]::In.ReadLine()
            Add-Content -LiteralPath $requestLog -Value $line -Encoding UTF8
            $request = $line | ConvertFrom-Json
            Write-Output ('{"type":"done","requestId":"' + $request.requestId + '"}')
            """));

        await sut.RunNamedAgentDelegationAsync(
            "Hand off to Lyra",
            "lyra-morn",
            _workspace.RootPath,
            "session-123",
            approvalGroupContext: "Use canonical groups: Guided Tour");

        var requestLine = FindLoggedRequestLine(requestLogPath, "delegate");
        using var request = JsonDocument.Parse(requestLine);

        Assert.That(
            request.RootElement.GetProperty("approvalGroupContext").GetString(),
            Is.EqualTo("Use canonical groups: Guided Tour"));
    }

    [Test]
    public async Task RunNamedAgentDelegationAsync_ProfileId_FollowsActiveProfileAndOmitsWhenUnknown() {
        var requestLogPath = Path.Combine(_workspace.RootPath, "requests.jsonl");
        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo($$"""
            $requestLog = {{PowerShellSingleQuoted(requestLogPath)}}
            for ($i = 0; $i -lt 2; $i++) {
                $line = [Console]::In.ReadLine()
                Add-Content -LiteralPath $requestLog -Value $line -Encoding UTF8
                $request = $line | ConvertFrom-Json
                Write-Output ('{"type":"done","requestId":"' + $request.requestId + '"}')
            }
            """));

        sut.ActiveProfile = new ModelProfile(
            Id: "coord-a",
            Alias: "Coordinator A",
            ProviderType: "copilot",
            ProviderUrl: null,
            Model: "claude-sonnet-4",
            ApiKey: null);

        await sut.RunNamedAgentDelegationAsync("first handoff", "lyra-morn", _workspace.RootPath, "session-123");
        sut.ActiveProfile = null;
        await sut.RunNamedAgentDelegationAsync("second handoff", "lyra-morn", _workspace.RootPath, "session-123");

        var delegateLines = File.ReadLines(requestLogPath, Encoding.UTF8)
            .Select(line => JsonDocument.Parse(line))
            .Where(doc => doc.RootElement.TryGetProperty("type", out var type) && type.GetString() == "delegate")
            .ToList();

        Assert.That(delegateLines, Has.Count.EqualTo(2));
        Assert.Multiple(() => {
            Assert.That(delegateLines[0].RootElement.GetProperty("profileId").GetString(), Is.EqualTo("coord-a"));
            Assert.That(delegateLines[1].RootElement.TryGetProperty("profileId", out _), Is.False);
        });

        foreach (var doc in delegateLines)
            doc.Dispose();
    }

    [Test]
    public async Task RunNamedAgentDirectAsync_WithApprovalGroupContext_SendsContext() {
        var requestLogPath = Path.Combine(_workspace.RootPath, "requests.jsonl");

        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo($$"""
            $requestLog = {{PowerShellSingleQuoted(requestLogPath)}}
            $line = [Console]::In.ReadLine()
            Add-Content -LiteralPath $requestLog -Value $line -Encoding UTF8
            $request = $line | ConvertFrom-Json
            Write-Output ('{"type":"done","requestId":"' + $request.requestId + '"}')
            """));

        await sut.RunNamedAgentDirectAsync(
            "lyra-morn",
            "Run verification",
            null,
            _workspace.RootPath,
            "session-123",
            approvalGroupContext: "Use canonical groups: Guided Tour");

        var requestLine = FindLoggedRequestLine(requestLogPath, "named_agent");
        using var request = JsonDocument.Parse(requestLine);

        Assert.That(
            request.RootElement.GetProperty("approvalGroupContext").GetString(),
            Is.EqualTo("Use canonical groups: Guided Tour"));
    }

    // ------------------------------------------------------------------
    // Happy path
    // ------------------------------------------------------------------

    [Test]
    public async Task RunPromptAsync_DoneEvent_CompletesWithoutException() {
        await using var sut = new SquadSdkProcess(BuildStartInfo(
            """echo {"type":"done","message":""}"""));

        Assert.That(
            async () => await sut.RunPromptAsync("hello", _workspace.RootPath),
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

        await sut.RunPromptAsync("hello", _workspace.RootPath);

        Assert.That(events.Select(e => e.Type), Contains.Item("response"));
    }

    [Test]
    public async Task RunPromptAsync_NonControlEvent_TagsBridgeProcessGeneration() {
        var events = new List<SquadSdkEvent>();

        await using var sut = new SquadSdkProcess(BuildStartInfo("""
            echo {"type":"response","message":"hello back"}
            echo {"type":"done","message":""}
            """));
        sut.EventReceived += (_, e) => events.Add(e);

        await sut.RunPromptAsync("hello", _workspace.RootPath);

        Assert.That(events.Where(e => e.Type == "response").Select(e => e.BridgeProcessGeneration), Is.All.GreaterThan(0));
    }

    [Test]
    public async Task RunPromptAsync_DoneEvent_AlsoFiresEventReceived() {
        var types = new List<string?>();

        await using var sut = new SquadSdkProcess(BuildStartInfo("""
            echo {"type":"response","message":"hi"}
            echo {"type":"done","message":""}
            """));
        sut.EventReceived += (_, e) => types.Add(e.Type);

        await sut.RunPromptAsync("hello", _workspace.RootPath);

        Assert.That(types, Is.EquivalentTo(new[] { "response", "done" }));
    }

    [Test]
    public async Task RunPromptAsync_SubagentCompletedEvent_FiresEventReceivedWithLifecycleFields() {
        var events = new List<SquadSdkEvent>();

        await using var sut = new SquadSdkProcess(BuildStartInfo("""
            echo {"type":"subagent_completed","toolCallId":"tool-wanda","agentName":"wanda-review","agentDisplayName":"WandaMaximoff","agentDescription":"Review options page changes","latestResponse":"done","hostProtocolVerified":true,"hostProtocol":"staged_plan_revision","hostProtocolGroupId":"PLAN-1","hostProtocolBaseRevision":"rev-1","hostProtocolCorrelationSource":"tool_metadata"}
            echo {"type":"done","message":""}
            """));
        sut.EventReceived += (_, e) => events.Add(e);

        await sut.RunPromptAsync("hello", _workspace.RootPath);

        var completionEvent = events.Single(e => e.Type == "subagent_completed");
        Assert.Multiple(() => {
            Assert.That(completionEvent.AgentName, Is.EqualTo("wanda-review"));
            Assert.That(completionEvent.AgentDisplayName, Is.EqualTo("WandaMaximoff"));
            Assert.That(completionEvent.AgentDescription, Is.EqualTo("Review options page changes"));
            Assert.That(completionEvent.LatestResponse, Is.EqualTo("done"));
            Assert.That(completionEvent.HostProtocolVerified, Is.True);
            Assert.That(completionEvent.HostProtocol, Is.EqualTo("staged_plan_revision"));
            Assert.That(completionEvent.HostProtocolGroupId, Is.EqualTo("PLAN-1"));
            Assert.That(completionEvent.HostProtocolBaseRevision, Is.EqualTo("rev-1"));
            Assert.That(completionEvent.HostProtocolCorrelationSource, Is.EqualTo("tool_metadata"));
        });
    }

    [Test]
    public async Task CancelBackgroundTaskAsync_SendsRequestIdAndReturnsAcknowledgedResult() {
        var requestLogPath = Path.Combine(_workspace.RootPath, "requests.jsonl");
        var promptStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<SquadSdkEvent>();

        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo($$"""
            $requestLog = {{PowerShellSingleQuoted(requestLogPath)}}
            $promptRequestId = $null
            while (($line = [Console]::In.ReadLine()) -ne $null) {
                if ([string]::IsNullOrWhiteSpace($line)) {
                    continue
                }

                Add-Content -LiteralPath $requestLog -Value $line -Encoding UTF8
                $request = $line | ConvertFrom-Json
                if ($request.type -eq 'prompt') {
                    $promptRequestId = $request.requestId
                    Write-Output ('{"type":"response_delta","requestId":"' + $request.requestId + '","chunk":"waiting"}')
                    continue
                }

                if ($request.type -eq 'cancel_background_task') {
                    Write-Output ('{"type":"background_task_cancelled","requestId":"' + $request.requestId + '","taskId":"' + $request.taskId + '","cancelled":true}')
                    Write-Output ('{"type":"done","requestId":"' + $promptRequestId + '"}')
                    continue
                }
            }
            """));
        sut.EventReceived += (_, e) => {
            lock (events)
                events.Add(e);
            if (e.Type == "response_delta")
                promptStarted.TrySetResult(true);
        };

        var promptTask = sut.RunPromptAsync("hello", _workspace.RootPath);
        Assert.That(
            await Task.WhenAny(promptStarted.Task, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(promptStarted.Task));

        var cancelled = await sut.CancelBackgroundTaskAsync("agent-1");
        await promptTask;

        var cancelRequestLine = FindLoggedRequestLine(requestLogPath, "cancel_background_task");
        using var cancelRequest = JsonDocument.Parse(cancelRequestLine);
        var root = cancelRequest.RootElement;

        Assert.Multiple(() => {
            Assert.That(cancelled, Is.True);
            Assert.That(root.GetProperty("taskId").GetString(), Is.EqualTo("agent-1"));
            Assert.That(root.GetProperty("requestId").GetString(), Is.Not.Null.And.Not.Empty);
            Assert.That(root.TryGetProperty("sessionId", out _), Is.False);
            Assert.That(events.Any(e =>
                e.Type == "background_task_cancelled" &&
                e.TaskId == "agent-1" &&
                e.Cancelled == true), Is.True);
        });
    }

    [Test]
    public async Task CancelBackgroundTaskAsync_ErrorAcknowledgement_DoesNotFailActivePrompt() {
        var promptStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo("""
            $promptRequestId = $null
            while (($line = [Console]::In.ReadLine()) -ne $null) {
                if ([string]::IsNullOrWhiteSpace($line)) {
                    continue
                }

                $request = $line | ConvertFrom-Json
                if ($request.type -eq 'prompt') {
                    $promptRequestId = $request.requestId
                    Write-Output ('{"type":"response_delta","requestId":"' + $request.requestId + '","chunk":"waiting"}')
                    continue
                }

                if ($request.type -eq 'cancel_background_task') {
                    Write-Output ('{"type":"error","requestId":"' + $request.requestId + '","message":"not cancelled"}')
                    Write-Output ('{"type":"done","requestId":"' + $promptRequestId + '"}')
                    continue
                }
            }
            """));
        sut.EventReceived += (_, e) => {
            if (e.Type == "response_delta")
                promptStarted.TrySetResult(true);
        };

        var promptTask = sut.RunPromptAsync("hello", _workspace.RootPath);
        Assert.That(
            await Task.WhenAny(promptStarted.Task, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(promptStarted.Task));

        var cancelled = await sut.CancelBackgroundTaskAsync("agent-1");

        Assert.That(cancelled, Is.False);
        Assert.That(async () => await promptTask, Throws.Nothing);
    }

    [Test]
    public async Task SteerAsync_WhilePromptIsActive_SerializesAndCorrelatesReversedAcknowledgements() {
        var requestLogPath = Path.Combine(_workspace.RootPath, "requests.jsonl");
        var promptStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo($$"""
            $requestLog = {{PowerShellSingleQuoted(requestLogPath)}}
            $promptRequestId = $null
            $steers = @()
            while (($line = [Console]::In.ReadLine()) -ne $null) {
                if ([string]::IsNullOrWhiteSpace($line)) {
                    continue
                }

                Add-Content -LiteralPath $requestLog -Value $line -Encoding UTF8
                $request = $line | ConvertFrom-Json
                if ($request.type -eq 'prompt') {
                    $promptRequestId = $request.requestId
                    Write-Output ('{"type":"response_delta","requestId":"' + $request.requestId + '","chunk":"waiting"}')
                    continue
                }

                if ($request.type -eq 'steer') {
                    $steers += $request
                    if ($steers.Count -eq 2) {
                        for ($index = 1; $index -ge 0; $index--) {
                            $steer = $steers[$index]
                            Write-Output ('{"type":"steer_result","requestId":"' + $steer.requestId + '","sessionId":"' + $steer.sessionId + '","accepted":true,"message":"accepted:' + $steer.prompt + '"}')
                        }
                        Write-Output ('{"type":"done","requestId":"' + $promptRequestId + '"}')
                    }
                    continue
                }
            }
            """));
        sut.EventReceived += (_, e) => {
            if (e.Type == "response_delta")
                promptStarted.TrySetResult(true);
        };

        var promptTask = sut.RunPromptAsync("keep working", _workspace.RootPath);
        Assert.That(
            await Task.WhenAny(promptStarted.Task, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(promptStarted.Task));

        var firstSteerTask = sut.SteerAsync(
            " provider-session-one ", " turn-one ", "First correction");
        var secondSteerTask = sut.SteerAsync(
            "provider-session-two", "turn-two", "Second correction");
        var results = await Task.WhenAll(firstSteerTask, secondSteerTask);
        await promptTask;

        var loggedRequests = File.ReadLines(requestLogPath, Encoding.UTF8)
            .Select(line => JsonDocument.Parse(line))
            .ToArray();
        try {
            var promptRequestId = loggedRequests
                .Single(document => document.RootElement.GetProperty("type").GetString() == "prompt")
                .RootElement.GetProperty("requestId").GetString();
            var steerRequests = loggedRequests
                .Where(document => document.RootElement.GetProperty("type").GetString() == "steer")
                .Select(document => document.RootElement)
                .OrderBy(element => element.GetProperty("prompt").GetString(), StringComparer.Ordinal)
                .ToArray();
            var steerRequestIds = steerRequests
                .Select(element => element.GetProperty("requestId").GetString())
                .ToArray();

            Assert.Multiple(() => {
                Assert.That(results[0], Is.EqualTo(new SquadSdkSteerResult(
                    true, "provider-session-one", "accepted:First correction")));
                Assert.That(results[1], Is.EqualTo(new SquadSdkSteerResult(
                    true, "provider-session-two", "accepted:Second correction")));
                Assert.That(steerRequests, Has.Length.EqualTo(2));
                Assert.That(steerRequests[0].GetProperty("sessionId").GetString(),
                    Is.EqualTo("provider-session-one"));
                Assert.That(steerRequests[0].GetProperty("prompt").GetString(),
                    Is.EqualTo("First correction"));
                Assert.That(steerRequests[0].GetProperty("steeringTurnId").GetString(),
                    Is.EqualTo("turn-one"));
                Assert.That(steerRequests[1].GetProperty("sessionId").GetString(),
                    Is.EqualTo("provider-session-two"));
                Assert.That(steerRequests[1].GetProperty("prompt").GetString(),
                    Is.EqualTo("Second correction"));
                Assert.That(steerRequests[1].GetProperty("steeringTurnId").GetString(),
                    Is.EqualTo("turn-two"));
                Assert.That(steerRequestIds.All(requestId => !string.IsNullOrEmpty(requestId)), Is.True);
                Assert.That(steerRequestIds.Distinct(StringComparer.Ordinal).ToArray(), Has.Length.EqualTo(2));
                Assert.That(steerRequestIds, Does.Not.Contain(promptRequestId));
            });
        }
        finally {
            foreach (var request in loggedRequests)
                request.Dispose();
        }
    }

    [Test]
    public async Task SteerAsync_RejectionAcknowledgement_DoesNotFailActivePrompt() {
        var promptStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activePromptEvents = new List<string?>();

        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo("""
            $promptRequestId = $null
            while (($line = [Console]::In.ReadLine()) -ne $null) {
                if ([string]::IsNullOrWhiteSpace($line)) {
                    continue
                }

                $request = $line | ConvertFrom-Json
                if ($request.type -eq 'prompt') {
                    $promptRequestId = $request.requestId
                    Write-Output ('{"type":"response_delta","requestId":"' + $request.requestId + '","chunk":"waiting"}')
                    continue
                }

                if ($request.type -eq 'steer') {
                    Write-Output ('{"type":"steer_result","requestId":"' + $request.requestId + '","sessionId":"' + $request.sessionId + '","accepted":false,"message":"target finished"}')
                    Write-Output ('{"type":"response_delta","requestId":"' + $promptRequestId + '","chunk":"still working"}')
                    Write-Output ('{"type":"done","requestId":"' + $promptRequestId + '"}')
                    continue
                }
            }
            """));
        sut.EventReceived += (_, e) => {
            lock (activePromptEvents)
                activePromptEvents.Add(e.Type);
            if (e.Type == "response_delta")
                promptStarted.TrySetResult(true);
        };

        var promptTask = sut.RunPromptAsync("keep working", _workspace.RootPath);
        Assert.That(
            await Task.WhenAny(promptStarted.Task, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(promptStarted.Task));

        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sut.SteerAsync("provider-session", "active-turn", "Too late"));
        await promptTask;

        Assert.Multiple(() => {
            Assert.That(error!.Message, Is.EqualTo("target finished"));
            Assert.That(activePromptEvents.Count(type => type == "response_delta"), Is.EqualTo(2));
            Assert.That(activePromptEvents, Does.Contain("done"));
            Assert.That(activePromptEvents, Does.Not.Contain("steer_result"),
                "The request-correlated steering rejection must not leak into the active prompt event stream.");
        });
    }

    [Test]
    public async Task SteerAsync_LateRejectionAfterAcceptance_IsConsumedAsControlTraffic() {
        var promptStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activePromptEvents = new List<string?>();

        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo("""
            $promptRequestId = $null
            while (($line = [Console]::In.ReadLine()) -ne $null) {
                if ([string]::IsNullOrWhiteSpace($line)) {
                    continue
                }

                $request = $line | ConvertFrom-Json
                if ($request.type -eq 'prompt') {
                    $promptRequestId = $request.requestId
                    Write-Output ('{"type":"response_delta","requestId":"' + $request.requestId + '","chunk":"waiting"}')
                    continue
                }

                if ($request.type -eq 'steer') {
                    Write-Output ('{"type":"steer_result","requestId":"' + $request.requestId + '","sessionId":"' + $request.sessionId + '","accepted":true}')
                    Start-Sleep -Milliseconds 200
                    Write-Output ('{"type":"steer_result","requestId":"' + $request.requestId + '","sessionId":"' + $request.sessionId + '","accepted":false,"message":"late transport rejection"}')
                    Write-Output ('{"type":"done","requestId":"' + $promptRequestId + '"}')
                    continue
                }
            }
            """));
        sut.EventReceived += (_, e) => {
            lock (activePromptEvents)
                activePromptEvents.Add(e.Type);
            if (e.Type == "response_delta")
                promptStarted.TrySetResult(true);
        };

        var promptTask = sut.RunPromptAsync("keep working", _workspace.RootPath);
        Assert.That(
            await Task.WhenAny(promptStarted.Task, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(promptStarted.Task));

        var result = await sut.SteerAsync("provider-session", "active-turn", "Change direction");
        await promptTask;

        Assert.Multiple(() => {
            Assert.That(result.Accepted, Is.True);
            Assert.That(activePromptEvents, Does.Contain("done"));
            Assert.That(activePromptEvents, Does.Not.Contain("steer_result"),
                "Late steering responses must stay isolated from the active prompt event stream.");
        });
    }

    [Test]
    public async Task SteerAsync_DelayedAcknowledgementBeyondFormerAdmissionWindow_WaitsForCorrelatedResult() {
        var promptStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo("""
            $promptRequestId = $null
            while (($line = [Console]::In.ReadLine()) -ne $null) {
                if ([string]::IsNullOrWhiteSpace($line)) {
                    continue
                }

                $request = $line | ConvertFrom-Json
                if ($request.type -eq 'prompt') {
                    $promptRequestId = $request.requestId
                    Write-Output ('{"type":"response_delta","requestId":"' + $request.requestId + '","chunk":"waiting"}')
                    continue
                }

                if ($request.type -eq 'steer') {
                    Start-Sleep -Milliseconds 8250
                    Write-Output ('{"type":"steer_result","requestId":"' + $request.requestId + '","sessionId":"' + $request.sessionId + '","accepted":true,"message":"accepted after provider delay"}')
                    Write-Output ('{"type":"done","requestId":"' + $promptRequestId + '"}')
                    continue
                }
            }
            """));
        sut.EventReceived += (_, e) => {
            if (e.Type == "response_delta")
                promptStarted.TrySetResult(true);
        };

        var promptTask = sut.RunPromptAsync("keep working", _workspace.RootPath);
        Assert.That(
            await Task.WhenAny(promptStarted.Task, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(promptStarted.Task));

        var stopwatch = Stopwatch.StartNew();
        var result = await sut.SteerAsync(
            "provider-session",
            "active-turn",
            "Change direction after the slow provider responds");
        stopwatch.Stop();
        await promptTask;

        Assert.Multiple(() => {
            Assert.That(stopwatch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(8)));
            Assert.That(result, Is.EqualTo(new SquadSdkSteerResult(
                true, "provider-session", "accepted after provider delay")));
        });
    }

    [Test]
    public async Task SteerAsync_ProcessExitsBeforeAcknowledgement_CompletesWithFailure() {
        var promptStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo("""
            while (($line = [Console]::In.ReadLine()) -ne $null) {
                if ([string]::IsNullOrWhiteSpace($line)) {
                    continue
                }

                $request = $line | ConvertFrom-Json
                if ($request.type -eq 'prompt') {
                    Write-Output ('{"type":"response_delta","requestId":"' + $request.requestId + '","chunk":"waiting"}')
                    continue
                }

                if ($request.type -eq 'steer') {
                    break
                }
            }
            """));
        sut.EventReceived += (_, e) => {
            if (e.Type == "response_delta")
                promptStarted.TrySetResult(true);
        };

        var promptTask = sut.RunPromptAsync("keep working", _workspace.RootPath);
        Assert.That(
            await Task.WhenAny(promptStarted.Task, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(promptStarted.Task));

        var steerTask = sut.SteerAsync("provider-session", "active-turn", "Change direction");

        Assert.That(
            await Task.WhenAny(steerTask, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(steerTask));
        Assert.That(
            async () => await steerTask,
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("process exited"));
        Assert.That(
            async () => await promptTask,
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("process exited"));
    }

    [Test]
    public async Task InterruptPrompt_SupersedesAnEarlierGenericAbortForSameRequest() {
        var promptStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo("""
            while (($line = [Console]::In.ReadLine()) -ne $null) {
                if ([string]::IsNullOrWhiteSpace($line)) {
                    continue
                }

                $request = $line | ConvertFrom-Json
                if ($request.type -eq 'prompt') {
                    Write-Output ('{"type":"response_delta","requestId":"' + $request.requestId + '","chunk":"waiting"}')
                    continue
                }

                if ($request.type -eq 'abort') {
                    Start-Sleep -Milliseconds 150
                    Write-Output ('{"type":"aborted","requestId":"' + $request.requestId + '"}')
                }
            }
            """));
        sut.EventReceived += (_, e) => {
            if (e.Type == "response_delta")
                promptStarted.TrySetResult(true);
        };

        var promptTask = sut.RunPromptAsync("hello", _workspace.RootPath);
        Assert.That(
            await Task.WhenAny(promptStarted.Task, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(promptStarted.Task));

        sut.AbortPrompt();
        sut.InterruptPrompt("Required provider configuration failed.");

        var ex = Assert.ThrowsAsync<OperationCanceledException>(async () => await promptTask);
        Assert.That(ex!.Message, Is.EqualTo("Required provider configuration failed."));
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
            async () => await sut.RunPromptAsync("hello", _workspace.RootPath),
            Throws.Nothing);
    }

    [Test]
    public async Task RunPromptAsync_TimesOutAfterConfiguredInactivityWindow() {
        // Use a script that sleeps for much longer than the timeout and never emits a done
        // event, so the inactivity timeout fires deterministically regardless of how long
        // powershell.exe takes to start on the host (which can exceed 1 second on CI).
        // The 500 ms poll/restart wait in SendBridgeRequestWithRestartAsync must not be able
        // to let a stale done event sneak through before WaitForPromptOutcomeAsync runs.
        await using var sut = new SquadSdkProcess(
            () => BuildPowerShellScriptStartInfo("Start-Sleep -Seconds 60"),
            new SquadSdkProcessOptions {
                PromptInactivityTimeout = TimeSpan.FromMilliseconds(500),
                PromptTimeoutPollInterval = TimeSpan.FromMilliseconds(50),
                BridgeHealthProbeTimeout = TimeSpan.FromMilliseconds(100)
            });

        var ex = Assert.ThrowsAsync<TimeoutException>(
            async () => await sut.RunPromptAsync("hello", _workspace.RootPath));

        Assert.That(ex!.Message,
            Does.Contain("without bridge activity").And.Contain("health check did not respond"));
    }

    [Test]
    public async Task RunPromptAsync_HealthySilentBridgeContinuesPastHostInactivityCheck() {
        await using var sut = new SquadSdkProcess(
            () => BuildPowerShellScriptStartInfo("""
                $promptRequestId = $null
                while (($line = [Console]::In.ReadLine()) -ne $null) {
                    if ([string]::IsNullOrWhiteSpace($line)) { continue }
                    $request = $line | ConvertFrom-Json
                    if ($request.type -eq 'prompt') {
                        $promptRequestId = $request.requestId
                        continue
                    }
                    if ($request.type -eq 'health_check') {
                        Write-Output ('{"type":"bridge_health","requestId":"' + $request.requestId + '","message":"ok"}')
                        Write-Output ('{"type":"done","requestId":"' + $promptRequestId + '"}')
                        break
                    }
                }
                """),
            new SquadSdkProcessOptions {
                PromptInactivityTimeout = TimeSpan.FromMilliseconds(500),
                PromptTimeoutPollInterval = TimeSpan.FromMilliseconds(50),
                BridgeHealthProbeTimeout = TimeSpan.FromSeconds(2)
            });

        Assert.That(
            async () => await sut.RunPromptAsync("hello", _workspace.RootPath),
            Throws.Nothing);
    }

    [Test]
    public async Task RunPromptAsync_Timeout_DoesNotAppendBenignNodeSQLiteWarning() {
        await using var sut = new SquadSdkProcess(
            BuildStartInfo("""
                echo (node:43416) ExperimentalWarning: SQLite is an experimental feature and might change at any time 1>&2
                echo (Use `node --trace-warnings ...` to show where the warning was created) 1>&2
                ping.exe -n 61 127.0.0.1 >nul
                """),
            new SquadSdkProcessOptions {
                PromptInactivityTimeout = TimeSpan.FromMilliseconds(500),
                PromptTimeoutPollInterval = TimeSpan.FromMilliseconds(50),
                BridgeHealthProbeTimeout = TimeSpan.FromMilliseconds(100)
            });

        var ex = Assert.ThrowsAsync<TimeoutException>(
            async () => await sut.RunPromptAsync("hello", _workspace.RootPath));

        Assert.Multiple(() => {
            Assert.That(ex!.Message, Does.Contain("without bridge activity"));
            Assert.That(ex.Message, Does.Not.Contain("ExperimentalWarning: SQLite"));
            Assert.That(ex.Message, Does.Not.Contain("trace-warnings"));
        });
    }

    [Test]
    public async Task RunPromptAsync_Timeout_AppendsActionableStderr() {
        await using var sut = new SquadSdkProcess(
            BuildStartInfo("""
                echo real provider failure 1>&2
                ping.exe -n 61 127.0.0.1 >nul
                """),
            new SquadSdkProcessOptions {
                PromptInactivityTimeout = TimeSpan.FromMilliseconds(500),
                PromptTimeoutPollInterval = TimeSpan.FromMilliseconds(50),
                BridgeHealthProbeTimeout = TimeSpan.FromMilliseconds(100)
            });

        var ex = Assert.ThrowsAsync<TimeoutException>(
            async () => await sut.RunPromptAsync("hello", _workspace.RootPath));

        Assert.That(ex!.Message, Does.Contain("real provider failure"));
    }

    [Test]
    public async Task DisposeAsync_ActivePrompt_CompletesPromptImmediately() {
        var promptStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = new SquadSdkProcess(
            () => BuildPowerShellScriptStartInfo("""
                Write-Output '{"type":"session_ready","sessionId":"session-1"}'
                Start-Sleep -Seconds 60
                """),
            new SquadSdkProcessOptions {
                PromptInactivityTimeout = TimeSpan.FromSeconds(60),
                PromptTimeoutPollInterval = TimeSpan.FromMilliseconds(50)
            });
        sut.EventReceived += (_, e) => {
            if (e.Type == "session_ready")
                promptStarted.TrySetResult(true);
        };

        var promptTask = sut.RunPromptAsync("hello", _workspace.RootPath);
        Assert.That(
            await Task.WhenAny(promptStarted.Task, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(promptStarted.Task));

        await sut.DisposeAsync();

        Assert.That(
            await Task.WhenAny(promptTask, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(promptTask));
        Assert.That(
            async () => await promptTask,
            Throws.TypeOf<OperationCanceledException>());
    }

    [Test]
    public async Task DisposeAsync_PendingSteer_CompletesSteerImmediately() {
        var promptStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var steerObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo("""
            $promptRequestId = $null
            while (($line = [Console]::In.ReadLine()) -ne $null) {
                if ([string]::IsNullOrWhiteSpace($line)) {
                    continue
                }

                $request = $line | ConvertFrom-Json
                if ($request.type -eq 'prompt') {
                    $promptRequestId = $request.requestId
                    Write-Output ('{"type":"response_delta","requestId":"' + $request.requestId + '","chunk":"waiting"}')
                    continue
                }

                if ($request.type -eq 'steer') {
                    Write-Output ('{"type":"response_delta","requestId":"' + $promptRequestId + '","chunk":"steer-observed"}')
                    continue
                }
            }
            """));
        sut.EventReceived += (_, e) => {
            if (e.Type != "response_delta")
                return;
            if (e.Chunk == "waiting")
                promptStarted.TrySetResult(true);
            else if (e.Chunk == "steer-observed")
                steerObserved.TrySetResult(true);
        };

        var promptTask = sut.RunPromptAsync("keep working", _workspace.RootPath);
        Assert.That(
            await Task.WhenAny(promptStarted.Task, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(promptStarted.Task));

        var steerTask = sut.SteerAsync("provider-session", "active-turn", "Change direction");
        Assert.That(
            await Task.WhenAny(steerObserved.Task, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(steerObserved.Task));
        Assert.That(steerTask.IsCompleted, Is.False);

        await sut.DisposeAsync();

        Assert.That(
            await Task.WhenAny(steerTask, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(steerTask));
        Assert.That(
            async () => await steerTask,
            Throws.TypeOf<OperationCanceledException>()
                .With.Message.Contains("disposed"));
        Assert.That(
            async () => await promptTask,
            Throws.TypeOf<OperationCanceledException>()
                .With.Message.Contains("disposed"));
    }

    [Test]
    public async Task ForceStopRunningWork_ActivePrompt_CompletesPromptImmediately() {
        var promptStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = new SquadSdkProcess(
            () => BuildPowerShellScriptStartInfo("""
                Write-Output '{"type":"session_ready","sessionId":"session-1"}'
                Start-Sleep -Seconds 60
                """),
            new SquadSdkProcessOptions {
                PromptInactivityTimeout = TimeSpan.FromSeconds(60),
                PromptTimeoutPollInterval = TimeSpan.FromMilliseconds(50)
            });
        sut.EventReceived += (_, e) => {
            if (e.Type == "session_ready")
                promptStarted.TrySetResult(true);
        };

        var promptTask = sut.RunPromptAsync("hello", _workspace.RootPath);
        Assert.That(
            await Task.WhenAny(promptStarted.Task, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(promptStarted.Task));

        var result = sut.ForceStopRunningWork("unit-test");

        Assert.Multiple(() => {
            Assert.That(result.HadActiveProcess, Is.True);
            Assert.That(result.ProcessGeneration, Is.GreaterThan(0));
            Assert.That(result.PendingPromptCount, Is.EqualTo(1));
        });
        Assert.That(
            await Task.WhenAny(promptTask, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(promptTask));
        Assert.That(
            async () => await promptTask,
            Throws.TypeOf<OperationCanceledException>()
                .With.Message.Contains("force-stopped"));

        await sut.DisposeAsync();
    }

    [Test]
    public async Task ForceStopRunningWork_PendingSteer_CompletesSteerImmediately() {
        var promptStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var steerObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo("""
            $promptRequestId = $null
            while (($line = [Console]::In.ReadLine()) -ne $null) {
                if ([string]::IsNullOrWhiteSpace($line)) {
                    continue
                }

                $request = $line | ConvertFrom-Json
                if ($request.type -eq 'prompt') {
                    $promptRequestId = $request.requestId
                    Write-Output ('{"type":"response_delta","requestId":"' + $request.requestId + '","chunk":"waiting"}')
                    continue
                }

                if ($request.type -eq 'steer') {
                    Write-Output ('{"type":"response_delta","requestId":"' + $promptRequestId + '","chunk":"steer-observed"}')
                    continue
                }
            }
            """));
        sut.EventReceived += (_, e) => {
            if (e.Type != "response_delta")
                return;
            if (e.Chunk == "waiting")
                promptStarted.TrySetResult(true);
            else if (e.Chunk == "steer-observed")
                steerObserved.TrySetResult(true);
        };

        var promptTask = sut.RunPromptAsync("keep working", _workspace.RootPath);
        Assert.That(
            await Task.WhenAny(promptStarted.Task, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(promptStarted.Task));

        var steerTask = sut.SteerAsync("provider-session", "active-turn", "Change direction");
        Assert.That(
            await Task.WhenAny(steerObserved.Task, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(steerObserved.Task));
        Assert.That(steerTask.IsCompleted, Is.False);

        var result = sut.ForceStopRunningWork("unit-test");

        Assert.That(result.HadActiveProcess, Is.True);
        Assert.That(
            await Task.WhenAny(steerTask, Task.Delay(TimeSpan.FromSeconds(5))),
            Is.SameAs(steerTask));
        Assert.That(
            async () => await steerTask,
            Throws.TypeOf<OperationCanceledException>()
                .With.Message.Contains("force-stopped"));
        Assert.That(
            async () => await promptTask,
            Throws.TypeOf<OperationCanceledException>()
                .With.Message.Contains("force-stopped"));

        await sut.DisposeAsync();
    }

    [Test]
    public async Task ForceStopRunningWork_NoActiveProcess_ReturnsHadActiveFalse() {
        // No process started — ForceStop should report nothing was running.
        await using var sut = new SquadSdkProcess(BuildStartInfo("@echo off"));

        var result = sut.ForceStopRunningWork("unit-test");

        Assert.Multiple(() => {
            Assert.That(result.HadActiveProcess,   Is.False);
            Assert.That(result.PendingPromptCount, Is.EqualTo(0));
            Assert.That(result.PendingCancelCount, Is.EqualTo(0));
        });
    }

    // ------------------------------------------------------------------
    // Error event
    // ------------------------------------------------------------------

    [Test]
    public async Task RunPromptAsync_ErrorEvent_ThrowsInvalidOperationException() {
        await using var sut = new SquadSdkProcess(BuildStartInfo(
            """echo {"type":"error","message":"something exploded"}"""));

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sut.RunPromptAsync("hello", _workspace.RootPath));

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
            _workspace.RootPath,
            sessionId: "stale-session",
            configDirectory: Path.Combine(_workspace.RootPath, "sdk-config"));

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
            _workspace.RootPath,
            sessionId: "poisoned-session",
            configDirectory: Path.Combine(_workspace.RootPath, "sdk-config"));

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
    public async Task RunPromptAsync_ResumedSessionMissingAuthOrCustomProvider_RetriesWithFreshSession() {
        var events = new List<SquadSdkEvent>();
        var scriptQueue = new Queue<string>(new[] {
            """
            echo {"type":"session_ready","sessionId":"copilot-born-session","sessionResumed":true}
            echo {"type":"error","message":"Execution failed: Error: Session was not created with authentication info or custom provider"}
            """,
            """
            echo {"type":"session_ready","sessionId":"fresh-local-session","sessionResumed":false}
            echo {"type":"response","message":"hello from local"}
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
            _workspace.RootPath,
            sessionId: "copilot-born-session",
            configDirectory: Path.Combine(_workspace.RootPath, "sdk-config"));

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
                _workspace.RootPath,
                sessionId: "poisoned-session",
                configDirectory: Path.Combine(_workspace.RootPath, "sdk-config")));

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
            async () => await sut.RunPromptAsync("hello", _workspace.RootPath),
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

        await sut.RunPromptAsync("hello", _workspace.RootPath);

        Assert.That(errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public async Task RunPromptAsync_NodePtyAttachConsoleStderr_IsSuppressed() {
        var errors = new List<string>();

        await using var sut = new SquadSdkProcess(() => BuildPowerShellScriptStartInfo("""
            [Console]::Error.WriteLine('[CLI subprocess] D:\Drive\Source\SquadDash-public\node_modules\node-pty\lib\conpty_console_list_agent.js:13')
            [Console]::Error.WriteLine('[CLI subprocess] var consoleProcessList = getConsoleProcessList(shellPid);')
            [Console]::Error.WriteLine('[CLI subprocess]                          ^')
            [Console]::Error.WriteLine('[CLI subprocess] Error: AttachConsole failed')
            [Console]::Error.WriteLine('[CLI subprocess]     at Object.<anonymous> (D:\Drive\Source\SquadDash-public\node_modules\node-pty\lib\conpty_console_list_agent.js:13:26)')
            [Console]::Error.WriteLine('[CLI subprocess]     at Module._compile (node:internal/modules/cjs/loader:1705:14)')
            [Console]::Error.WriteLine('[CLI subprocess]     at Object..js (node:internal/modules/cjs/loader:1838:10)')
            [Console]::Error.WriteLine('[CLI subprocess]     at Module.load (node:internal/modules/cjs/loader:1441:32)')
            [Console]::Error.WriteLine('[CLI subprocess]     at Function._load (node:internal/modules/cjs/loader:1263:12)')
            [Console]::Error.WriteLine('[CLI subprocess]     at TracingChannel.traceSync (node:diagnostics_channel:328:14)')
            [Console]::Error.WriteLine('[CLI subprocess]     at wrapModuleLoad (node:internal/modules/cjs/loader:237:24)')
            [Console]::Error.WriteLine('[CLI subprocess]     at Function.executeUserEntryPoint [as runMain] (node:internal/modules/run_main:171:5)')
            [Console]::Error.WriteLine('[CLI subprocess]     at node:internal/main/run_main_module:36:49')
            [Console]::Error.WriteLine('[CLI subprocess] Node.js v22.22.1')
            [Console]::Out.WriteLine('{"type":"done","message":""}')
            """));
        sut.ErrorReceived += (_, msg) => errors.Add(msg);

        await sut.RunPromptAsync("hello", _workspace.RootPath);

        Assert.That(errors, Is.Empty);
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
        var t1 = sut.RunPromptAsync("first", _workspace.RootPath);
        var t2 = sut.RunPromptAsync("second", _workspace.RootPath);

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
            async () => await sut.RunPromptAsync("hello", _workspace.RootPath),
            Throws.Nothing);

        Assert.That(errors.Any(e => e.StartsWith("[non-json]")), Is.True);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string PowerShellSingleQuoted(string value) =>
        "'" + value.Replace("'", "''") + "'";

    private static string FindLoggedRequestLine(string requestLogPath, string requestType) {
        foreach (var line in File.ReadLines(requestLogPath, Encoding.UTF8)) {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("type", out var type) &&
                string.Equals(type.GetString(), requestType, StringComparison.Ordinal)) {
                return line;
            }
        }

        throw new InvalidOperationException($"No logged request with type '{requestType}' was found.");
    }

    /// <summary>Creates a ProcessStartInfo that runs a cmd.exe script from inline lines.</summary>
    private Func<ProcessStartInfo> BuildStartInfo(string scriptLines) => () => BuildScriptStartInfo(scriptLines);

    private ProcessStartInfo BuildScriptStartInfo(string scriptLines) {
        var scriptPath = Path.Combine(_workspace.RootPath, $"fake_{Path.GetRandomFileName()}.cmd");
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
        var scriptPath = Path.Combine(_workspace.RootPath, $"fake_{Path.GetRandomFileName()}.ps1");
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
