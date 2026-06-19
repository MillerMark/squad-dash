using System.Net;
using System.Net.Http;
using System.Text;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ModelProviderProbeServiceTests {
    [Test]
    public void BuildOpenAiEndpointCandidates_AppendsV1WhenMissing() {
        var candidates = ModelProviderProbeService.BuildOpenAiEndpointCandidates("http://127.0.0.1:5273");

        Assert.That(candidates, Is.EqualTo(new[] {
            "http://127.0.0.1:5273/v1",
            "http://127.0.0.1:5273"
        }));
    }

    [Test]
    public void BuildOpenAiEndpointCandidates_KeepsExistingV1Root() {
        var candidates = ModelProviderProbeService.BuildOpenAiEndpointCandidates("http://127.0.0.1:11437/v1/");

        Assert.That(candidates, Is.EqualTo(new[] { "http://127.0.0.1:11437/v1" }));
    }

    [Test]
    public void ParseModelsResponse_ReadsCatalogToolCallingMetadata() {
        var results = ModelProviderProbeService.ParseModelsResponse(
            """
            {
              "data": [
                {
                  "id": "tool-model",
                  "owned_by": "Microsoft",
                  "parent": "base-model",
                  "supportsToolCalling": true,
                  "object": "model"
                }
              ]
            }
            """,
            "http://localhost:5273/v1");

        Assert.Multiple(() => {
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].ModelId, Is.EqualTo("tool-model"));
            Assert.That(results[0].ParentModel, Is.EqualTo("base-model"));
            Assert.That(results[0].Owner, Is.EqualTo("Microsoft"));
            Assert.That(results[0].CatalogSupportsToolCalling, Is.True);
            Assert.That(results[0].CatalogNotes, Does.Contain("object=model"));
            Assert.That(results[0].Notes, Is.Null);
        });
    }

    [Test]
    public void NoteSummary_CondensesLongMultilineNotes() {
        var result = new ModelProviderProbeResult(
            "model",
            "http://provider/v1",
            Notes: "Load failed:\r\n\r\nFirst line\r\nSecond line " + new string('x', 220));

        Assert.Multiple(() => {
            Assert.That(result.HasNotes, Is.True);
            Assert.That(result.NoteSummary, Does.Not.Contain("\r"));
            Assert.That(result.NoteSummary, Does.Not.Contain("\n"));
            Assert.That(result.NoteSummary.Length, Is.LessThanOrEqualTo(150));
            Assert.That(result.NoteSummary, Does.EndWith("..."));
        });
    }

    [Test]
    public void NoteSummary_ShowsConciseInstructionForUnloadedModels() {
        var result = new ModelProviderProbeResult(
            "model",
            "http://provider/v1",
            ChatStatus: ModelProbeCheckStatus.NotLoaded,
            ToolStatus: ModelProbeCheckStatus.NotLoaded,
            Notes: "Chat probe returned 400 Bad Request: Failed to handle OpenAI completion: Model 'model' is not loaded. Please load the model before getting a ChatClient.",
            CanLoadLocally: true);

        Assert.That(result.NoteSummary, Is.EqualTo("Not loaded. Click Load to load this model."));
    }

    [Test]
    public void NoteSummary_DoesNotOfferLoadForRemoteUnloadedModels() {
        var result = new ModelProviderProbeResult(
            "model",
            "http://provider/v1",
            ChatStatus: ModelProbeCheckStatus.NotLoaded,
            ToolStatus: ModelProbeCheckStatus.NotLoaded,
            Notes: "Model 'model' is not loaded. Please load the model before getting a ChatClient.");

        Assert.Multiple(() => {
            Assert.That(result.NoteSummary, Is.EqualTo("Not loaded by provider. Load it on the provider host, then probe again."));
            Assert.That(result.RowActionText, Is.EqualTo("Details..."));
        });
    }

    [Test]
    public void NoteSummary_ShowsTransientLoadAndProbeStatus() {
        var loading = new ModelProviderProbeResult(
            "model",
            "http://provider/v1",
            ChatStatus: ModelProbeCheckStatus.NotLoaded,
            ToolStatus: ModelProbeCheckStatus.NotLoaded,
            Notes: "Loading...",
            CanLoadLocally: true);
        var probing = loading with { Notes = "Probing..." };

        Assert.Multiple(() => {
            Assert.That(loading.NoteSummary, Is.EqualTo("Loading..."));
            Assert.That(probing.NoteSummary, Is.EqualTo("Probing..."));
        });
    }

    [Test]
    public void RowActionText_LoadsUnloadedModelsBeforeNotesExist() {
        var result = new ModelProviderProbeResult(
            "model",
            "http://provider/v1",
            CanLoadLocally: true);

        Assert.Multiple(() => {
            Assert.That(result.RowActionText, Is.EqualTo("Load"));
            Assert.That(result.NoteSummary, Is.EqualTo("Not loaded. Click Load to load this model."));
        });
    }

    [Test]
    public void RowActionText_ProbesLoadedLocalModelsBeforeNotesExist() {
        var result = new ModelProviderProbeResult(
            "model",
            "http://provider/v1",
            CanLoadLocally: true,
            IsLoadedLocally: true);

        Assert.Multiple(() => {
            Assert.That(result.RowActionText, Is.EqualTo("Probe"));
            Assert.That(result.NoteSummary, Is.EqualTo("Loaded. Click Probe to test this model."));
        });
    }

    [Test]
    public void WithoutStaleNotLoadedNotes_RemovesProviderLoadPromptAfterSuccessfulLoad() {
        var result = new ModelProviderProbeResult(
            "model",
            "http://provider/v1",
            Notes: "Chat probe returned 400 Bad Request: Failed to handle OpenAI completion: Model 'model' is not loaded. Please load the model before getting a ChatClient..",
            DiagnosticNotes: "Tool probe returned 400 Bad Request: Failed to handle OpenAI completion: Model 'model' is not loaded. Please load the model before getting a ChatClient..");

        var cleaned = result.WithoutStaleNotLoadedNotes();

        Assert.Multiple(() => {
            Assert.That(cleaned.Notes, Is.Null);
            Assert.That(cleaned.DiagnosticNotes, Is.Null);
        });
    }

    [Test]
    public void RowActionText_ProbesModelsAfterSuccessfulLoad() {
        var result = new ModelProviderProbeResult(
            "model",
            "http://provider/v1",
            ChatStatus: ModelProbeCheckStatus.NotRun,
            ToolStatus: ModelProbeCheckStatus.NotRun,
            Notes: "Chat probe returned 400 Bad Request: Model is not loaded. Load succeeded: loaded exit=0");

        Assert.Multiple(() => {
            Assert.That(result.RowActionText, Is.EqualTo("Probe"));
            Assert.That(result.NoteSummary, Is.EqualTo("Loaded. Click Probe to test this model."));
        });
    }

    [Test]
    public void RowActionText_ProbesUnprobedModels() {
        var result = new ModelProviderProbeResult(
            "model",
            "http://provider/v1",
            CatalogNotes: "object=model; catalogSuccess=True");

        Assert.That(result.RowActionText, Is.EqualTo("Probe"));
    }

    [Test]
    public void ParseFoundryLoadedModels_ReadsStructuredJson() {
        var loaded = ModelProviderProbeService.ParseFoundryLoadedModels(
            """
            {
              "models": [
                {
                  "alias": "qwen2.5-coder-14b",
                  "id": "qwen2.5-coder-14b-instruct-cuda-gpu:4",
                  "displayName": "qwen2.5-coder-14b-instruct-cuda-gpu",
                  "device": "Gpu",
                  "fileSizeMb": 9000,
                  "supportsToolCalling": true
                }
              ]
            }
            """);

        Assert.Multiple(() => {
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0].ModelId, Is.EqualTo("qwen2.5-coder-14b-instruct-cuda-gpu:4"));
            Assert.That(loaded[0].Alias, Is.EqualTo("qwen2.5-coder-14b"));
            Assert.That(loaded[0].DisplayName, Is.EqualTo("qwen2.5-coder-14b-instruct-cuda-gpu"));
            Assert.That(loaded[0].Device, Is.EqualTo("Gpu"));
            Assert.That(loaded[0].FileSizeMb, Is.EqualTo(9000));
            Assert.That(loaded[0].SupportsToolCalling, Is.True);
        });
    }

    [Test]
    public void ParseNvidiaGpuMemory_ReadsCsvOutput() {
        var gpus = ModelProviderProbeService.ParseNvidiaGpuMemory(
            """
            0, NVIDIA GeForce RTX 3090, 24576, 24067, 256
            1, NVIDIA GeForce RTX 2080 Ti, 11264, 712, 10316
            """);

        Assert.Multiple(() => {
            Assert.That(gpus, Has.Count.EqualTo(2));
            Assert.That(gpus[0].Index, Is.EqualTo(0));
            Assert.That(gpus[0].Name, Is.EqualTo("NVIDIA GeForce RTX 3090"));
            Assert.That(gpus[0].TotalMiB, Is.EqualTo(24576));
            Assert.That(gpus[0].UsedMiB, Is.EqualTo(24067));
            Assert.That(gpus[0].FreeMiB, Is.EqualTo(256));
            Assert.That(gpus[1].Index, Is.EqualTo(1));
        });
    }

    [Test]
    public void StatusDisplay_AddsPassAndFailGlyphs() {
        var result = new ModelProviderProbeResult(
            "model",
            "http://provider/v1",
            ChatStatus: ModelProbeCheckStatus.Passed,
            ToolStatus: ModelProbeCheckStatus.Failed);

        Assert.Multiple(() => {
            Assert.That(result.ChatStatusText, Is.EqualTo("Passed"));
            Assert.That(result.ToolStatusText, Is.EqualTo("Failed"));
            Assert.That(result.ChatStatusDisplay, Is.EqualTo("\u2611 Passed"));
            Assert.That(result.ToolStatusDisplay, Is.EqualTo("\u2715 Failed"));
        });
    }

    [Test]
    public void RowActionText_ShowsDetailsForProbedOrLoadFailedModels() {
        var probed = new ModelProviderProbeResult(
            "model",
            "http://provider/v1",
            ChatStatus: ModelProbeCheckStatus.Passed,
            ToolStatus: ModelProbeCheckStatus.Failed);
        var loadFailed = new ModelProviderProbeResult(
            "model",
            "http://provider/v1",
            Notes: "Load failed.");

        Assert.Multiple(() => {
            Assert.That(probed.RowActionText, Is.EqualTo("Details..."));
            Assert.That(loadFailed.RowActionText, Is.EqualTo("Details..."));
        });
    }

    [Test]
    public async Task DiscoverModelsAsync_FallsBackToRawProviderUrlWhenV1CandidateFails() {
        var handler = new StubHttpHandler(request => {
            if (request.RequestUri!.AbsoluteUri == "http://provider/v1/models")
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            return JsonResponse("""
                {
                  "data": [
                    { "id": "fallback-model" }
                  ]
                }
                """);
        });
        using var http = new HttpClient(handler);
        using var service = new ModelProviderProbeService(http);

        var results = await service.DiscoverModelsAsync("http://provider", apiKey: null);

        Assert.Multiple(() => {
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].ModelId, Is.EqualTo("fallback-model"));
            Assert.That(results[0].ProviderEndpointRoot, Is.EqualTo("http://provider"));
        });
    }

    [Test]
    public void DiscoverModelsAsync_IncludesProviderErrorBodyWhenDiscoveryFails() {
        var handler = new StubHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError) {
                Content = new StringContent(
                    """
                    { "error": { "message": "Foundry daemon is not ready." } }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        using var http = new HttpClient(handler);
        using var service = new ModelProviderProbeService(http);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DiscoverModelsAsync("http://provider/v1", apiKey: null));

        Assert.That(ex!.Message, Does.Contain("Foundry daemon is not ready."));
    }

    [Test]
    public async Task RunLiveProbeAsync_DetectsStructuredToolCall() {
        string? toolProbeBody = null;
        var handler = new StubHttpHandler(request => {
            if (request.RequestUri!.AbsolutePath.EndsWith("/models", StringComparison.Ordinal))
                return JsonResponse("""{ "data": [ { "id": "tool-model" } ] }""");

            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("\"tools\"", StringComparison.Ordinal)) {
                toolProbeBody = body;
                return JsonResponse("""
                    {
                      "choices": [
                        {
                          "message": {
                            "role": "assistant",
                            "content": null,
                            "tool_calls": [
                              {
                                "id": "call_1",
                                "type": "function",
                                "function": { "name": "report_probe", "arguments": "{\"result\":\"OK\"}" }
                              }
                            ]
                          }
                        }
                      ]
                    }
                    """);
            }

            return JsonResponse("""
                {
                  "choices": [
                    { "message": { "role": "assistant", "content": "OK" } }
                  ]
                }
                """);
        });
        using var http = new HttpClient(handler);
        using var service = new ModelProviderProbeService(http);
        var model = new ModelProviderProbeResult("tool-model", "http://provider/v1");

        var probed = await service.RunLiveProbeAsync("http://provider/v1", null, model);

        Assert.Multiple(() => {
            Assert.That(probed.ChatStatus, Is.EqualTo(ModelProbeCheckStatus.Passed));
            Assert.That(probed.ToolStatus, Is.EqualTo(ModelProbeCheckStatus.Passed));
            Assert.That(probed.Notes, Is.EqualTo("Probe succeeded."));
            Assert.That(toolProbeBody, Does.Contain("\"tool_choice\""));
            Assert.That(toolProbeBody, Does.Contain("\"name\":\"report_probe\""));
        });
    }

    [Test]
    public async Task RunLiveProbeAsync_MarksTextImitationAsToolFailure() {
        var handler = new StubHttpHandler(request => {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("\"tools\"", StringComparison.Ordinal))
                return JsonResponse("""
                    {
                      "choices": [
                        { "message": { "role": "assistant", "content": "report_probe{\"result\":\"OK\"}", "tool_calls": [] } }
                      ]
                    }
                    """);

            return JsonResponse("""
                {
                  "choices": [
                    { "message": { "role": "assistant", "content": "OK" } }
                  ]
                }
                """);
        });
        using var http = new HttpClient(handler);
        using var service = new ModelProviderProbeService(http);
        var model = new ModelProviderProbeResult("text-model", "http://provider/v1");

        var probed = await service.RunLiveProbeAsync("http://provider/v1", null, model);

        Assert.Multiple(() => {
            Assert.That(probed.ChatStatus, Is.EqualTo(ModelProbeCheckStatus.Passed));
            Assert.That(probed.ToolStatus, Is.EqualTo(ModelProbeCheckStatus.Failed));
            Assert.That(probed.Notes, Does.Contain("structured tool call"));
        });
    }

    [Test]
    public async Task RunLiveProbeAsync_SkipsToolProbeWhenChatTimesOut() {
        var handler = new DelayingHttpHandler();
        using var http = new HttpClient(handler) {
            Timeout = TimeSpan.FromMilliseconds(20)
        };
        using var service = new ModelProviderProbeService(http);
        var model = new ModelProviderProbeResult("slow-model", "http://provider/v1");

        var probed = await service.RunLiveProbeAsync("http://provider/v1", null, model);

        Assert.Multiple(() => {
            Assert.That(handler.RequestCount, Is.EqualTo(1));
            Assert.That(probed.ChatStatus, Is.EqualTo(ModelProbeCheckStatus.TimedOut));
            Assert.That(probed.ToolStatus, Is.EqualTo(ModelProbeCheckStatus.TimedOut));
            Assert.That(probed.Notes, Does.Contain("Tool probe skipped"));
        });
    }

    [Test]
    public async Task RunLiveProbeAsync_MarksFoundryNotLoadedError() {
        var handler = new StubHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest) {
                Content = new StringContent(
                    """
                    {
                      "error": {
                        "message": "Failed to handle OpenAI completion: Model 'qwen3-8b-cuda-gpu' is not loaded. Please load the model before getting a ChatClient.",
                        "type": "invalid_request_error",
                        "code": null
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        using var http = new HttpClient(handler);
        using var service = new ModelProviderProbeService(http);
        var model = new ModelProviderProbeResult("qwen3-8b-cuda-gpu", "http://provider/v1");

        var probed = await service.RunLiveProbeAsync("http://provider/v1", null, model);

        Assert.Multiple(() => {
            Assert.That(probed.ChatStatus, Is.EqualTo(ModelProbeCheckStatus.NotLoaded));
            Assert.That(probed.ToolStatus, Is.EqualTo(ModelProbeCheckStatus.NotLoaded));
            Assert.That(probed.Notes, Does.Contain("400 Bad Request"));
            Assert.That(probed.DiagnosticNotes, Does.Contain("Please load the model"));
        });
    }

    [Test]
    public async Task RunLiveProbeAsync_ExplainsFoundryToolGrammarFailure() {
        var requestIndex = 0;
        var handler = new StubHttpHandler(_ => {
            requestIndex++;
            if (requestIndex == 1)
                return JsonResponse("""
                    {
                      "choices": [
                        { "message": { "role": "assistant", "content": "OK" } }
                      ]
                    }
                    """);

            return new HttpResponseMessage(HttpStatusCode.InternalServerError) {
                Content = new StringContent(
                    """
                    {
                      "error": {
                        "message": "Failed to handle OpenAI completion: Error creating grammar: unknown name: \"TOOL_CALLS\"",
                        "type": "server_error",
                        "code": null
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var http = new HttpClient(handler);
        using var service = new ModelProviderProbeService(http);
        var model = new ModelProviderProbeResult("ministral", "http://provider/v1");

        var probed = await service.RunLiveProbeAsync("http://provider/v1", null, model);

        Assert.Multiple(() => {
            Assert.That(probed.ChatStatus, Is.EqualTo(ModelProbeCheckStatus.Passed));
            Assert.That(probed.ToolStatus, Is.EqualTo(ModelProbeCheckStatus.Failed));
            Assert.That(probed.Notes, Does.Contain("Provider rejected the structured tool-call request"));
            Assert.That(probed.DiagnosticNotes, Does.Contain("likely does not currently support OpenAI tool calls correctly"));
        });
    }

    [Test]
    public async Task LoadFoundryModelAsync_InvokesFoundryModelLoad() {
        string? observedFileName = null;
        IReadOnlyList<string>? observedArguments = null;
        using var http = new HttpClient(new StubHttpHandler(_ => JsonResponse("{}")));
        using var service = new ModelProviderProbeService(
            http,
            commandRunner: (fileName, arguments, _) => {
                if (arguments.SequenceEqual(new[] { "--version" }))
                    return Task.FromResult(new ModelProviderCommandResult(true, 0, "0.8.119", ""));
                if (arguments.SequenceEqual(new[] { "--help" }))
                    return Task.FromResult(new ModelProviderCommandResult(true, 0, "Commands:\n  model\n  service", ""));

                observedFileName = fileName;
                observedArguments = arguments.ToArray();
                return Task.FromResult(new ModelProviderCommandResult(true, 0, "loaded", ""));
            },
            foundryCliCandidates: () => new[] {
                new FoundryCliCandidate("foundry", "PATH alias")
            });

        var result = await service.LoadFoundryModelAsync("qwen3-8b-cuda-gpu");

        Assert.Multiple(() => {
            Assert.That(result.Success, Is.True);
            Assert.That(observedFileName, Is.EqualTo("foundry"));
            Assert.That(observedArguments, Is.EqualTo(new[] { "model", "load", "qwen3-8b-cuda-gpu" }));
            Assert.That(result.Output, Does.Contain("Foundry CLI version: 0.8.119"));
        });
    }

    [Test]
    public async Task UnloadFoundryModelAsync_InvokesFoundryModelUnload() {
        IReadOnlyList<string>? observedArguments = null;
        using var http = new HttpClient(new StubHttpHandler(_ => JsonResponse("{}")));
        using var service = new ModelProviderProbeService(
            http,
            commandRunner: (_, arguments, _) => {
                if (arguments.SequenceEqual(new[] { "--version" }))
                    return Task.FromResult(new ModelProviderCommandResult(true, 0, "0.10.0", ""));
                if (arguments.SequenceEqual(new[] { "--help" }))
                    return Task.FromResult(new ModelProviderCommandResult(true, 0, "Commands:\n  model\n  server", ""));

                observedArguments = arguments.ToArray();
                return Task.FromResult(new ModelProviderCommandResult(true, 0, "unloaded", ""));
            },
            foundryCliCandidates: () => new[] {
                new FoundryCliCandidate("foundry", "PATH alias")
            });

        var result = await service.UnloadFoundryModelAsync("qwen3-8b-cuda-gpu");

        Assert.Multiple(() => {
            Assert.That(result.Success, Is.True);
            Assert.That(observedArguments, Is.EqualTo(new[] { "model", "unload", "qwen3-8b-cuda-gpu" }));
        });
    }

    [Test]
    public async Task LoadFoundryModelAsync_PrefersNewFoundryCliWhenPathAliasIsOld() {
        const string newFoundry = @"C:\Program Files\WindowsApps\Microsoft.FoundryLocalCLI_0.10.0.0_x64__8wekyb3d8bbwe\foundry.exe";
        string? observedFileName = null;
        using var http = new HttpClient(new StubHttpHandler(_ => JsonResponse("{}")));
        using var service = new ModelProviderProbeService(
            http,
            commandRunner: (fileName, arguments, _) => {
                if (arguments.SequenceEqual(new[] { "--version" })) {
                    var version = string.Equals(fileName, "foundry", StringComparison.OrdinalIgnoreCase)
                        ? "0.8.119"
                        : "0.10.0+abc";
                    return Task.FromResult(new ModelProviderCommandResult(true, 0, version, ""));
                }

                if (arguments.SequenceEqual(new[] { "--help" })) {
                    var help = string.Equals(fileName, "foundry", StringComparison.OrdinalIgnoreCase)
                        ? "Commands:\n  model\n  service"
                        : "Commands:\n  model\n  server";
                    return Task.FromResult(new ModelProviderCommandResult(true, 0, help, ""));
                }

                observedFileName = fileName;
                return Task.FromResult(new ModelProviderCommandResult(true, 0, "loaded", ""));
            },
            foundryCliCandidates: () => new[] {
                new FoundryCliCandidate("foundry", "PATH alias"),
                new FoundryCliCandidate(newFoundry, "Foundry Local CLI package")
            });

        var result = await service.LoadFoundryModelAsync("qwen3-8b-cuda-gpu");

        Assert.Multiple(() => {
            Assert.That(result.Success, Is.True);
            Assert.That(observedFileName, Is.EqualTo(newFoundry));
            Assert.That(result.Output, Does.Contain("Foundry CLI version: 0.10.0"));
            Assert.That(result.Output, Does.Contain("Foundry CLI conflict"));
            Assert.That(result.Output, Does.Contain("PATH alias resolves to 0.8.119"));
        });
    }

    [Test]
    public async Task LoadFoundryModelAsync_PrefersHighestServerCapableFoundryCli() {
        const string foundry010 = @"C:\Foundry\0.10\foundry.exe";
        const string foundry020 = @"C:\Foundry\0.20\foundry.exe";
        string? observedFileName = null;
        using var http = new HttpClient(new StubHttpHandler(_ => JsonResponse("{}")));
        using var service = new ModelProviderProbeService(
            http,
            commandRunner: (fileName, arguments, _) => {
                if (arguments.SequenceEqual(new[] { "--version" })) {
                    var version = string.Equals(fileName, foundry020, StringComparison.OrdinalIgnoreCase)
                        ? "0.20.0"
                        : "0.10.0";
                    return Task.FromResult(new ModelProviderCommandResult(true, 0, version, ""));
                }

                if (arguments.SequenceEqual(new[] { "--help" }))
                    return Task.FromResult(new ModelProviderCommandResult(true, 0, "Commands:\n  model\n  server", ""));

                observedFileName = fileName;
                return Task.FromResult(new ModelProviderCommandResult(true, 0, "loaded", ""));
            },
            foundryCliCandidates: () => new[] {
                new FoundryCliCandidate(foundry010, "Foundry Local CLI package alias"),
                new FoundryCliCandidate(foundry020, "Foundry Local CLI package alias")
            });

        var result = await service.LoadFoundryModelAsync("qwen3-8b-cuda-gpu");

        Assert.Multiple(() => {
            Assert.That(result.Success, Is.True);
            Assert.That(observedFileName, Is.EqualTo(foundry020));
            Assert.That(result.Output, Does.Contain("Foundry CLI version: 0.20.0"));
        });
    }

    [Test]
    public void ParseFoundryVersion_ReadsSemverPrefix() {
        Assert.Multiple(() => {
            Assert.That(ModelProviderProbeService.ParseFoundryVersion("0.10.0+174be11"), Is.EqualTo(new Version(0, 10, 0)));
            Assert.That(ModelProviderProbeService.ParseFoundryVersion("0.8.119"), Is.EqualTo(new Version(0, 8, 119)));
            Assert.That(ModelProviderProbeService.ParseFoundryVersion("not a version"), Is.Null);
        });
    }

    [Test]
    public void LoadFoundryModelAsync_RejectsEmptyModelId() {
        using var http = new HttpClient(new StubHttpHandler(_ => JsonResponse("{}")));
        using var service = new ModelProviderProbeService(http);

        Assert.ThrowsAsync<ArgumentException>(() => service.LoadFoundryModelAsync(" "));
    }

    private static HttpResponseMessage JsonResponse(string json) {
        return new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            return Task.FromResult(handler(request));
        }
    }

    private sealed class DelayingHttpHandler : HttpMessageHandler {
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            RequestCount++;
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return JsonResponse("""{ "choices": [ { "message": { "role": "assistant", "content": "OK" } } ] }""");
        }
    }
}
