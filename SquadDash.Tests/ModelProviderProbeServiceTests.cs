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
    public void RowActionText_LoadsUnloadedModelsBeforeNotesExist() {
        var result = new ModelProviderProbeResult(
            "model",
            "http://provider/v1",
            ChatStatus: ModelProbeCheckStatus.NotLoaded,
            ToolStatus: ModelProbeCheckStatus.NotLoaded);

        Assert.That(result.RowActionText, Is.EqualTo("Load"));
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
    public async Task RunLiveProbeAsync_DetectsStructuredToolCall() {
        var handler = new StubHttpHandler(request => {
            if (request.RequestUri!.AbsolutePath.EndsWith("/models", StringComparison.Ordinal))
                return JsonResponse("""{ "data": [ { "id": "tool-model" } ] }""");

            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("\"tools\"", StringComparison.Ordinal))
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
            Assert.That(probed.Notes, Does.Contain("Please load the model"));
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
                observedFileName = fileName;
                observedArguments = arguments.ToArray();
                return Task.FromResult(new ModelProviderCommandResult(true, 0, "loaded", ""));
            });

        var result = await service.LoadFoundryModelAsync("qwen3-8b-cuda-gpu");

        Assert.Multiple(() => {
            Assert.That(result.Success, Is.True);
            Assert.That(observedFileName, Is.EqualTo("foundry"));
            Assert.That(observedArguments, Is.EqualTo(new[] { "model", "load", "qwen3-8b-cuda-gpu" }));
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
}
