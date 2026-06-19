using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SquadDash;

internal enum ModelProbeCheckStatus {
    NotRun,
    Passed,
    Failed,
    TimedOut
}

internal sealed record ModelProviderProbeResult(
    string ModelId,
    string ProviderEndpointRoot,
    string? ParentModel = null,
    string? Owner = null,
    bool? CatalogSupportsToolCalling = null,
    ModelProbeCheckStatus ChatStatus = ModelProbeCheckStatus.NotRun,
    ModelProbeCheckStatus ToolStatus = ModelProbeCheckStatus.NotRun,
    string? Notes = null) {

    public string CatalogToolCallingText => CatalogSupportsToolCalling switch {
        true => "Supported",
        false => "Not advertised",
        _ => "Unknown"
    };

    public string ChatStatusText => StatusText(ChatStatus);
    public string ToolStatusText => StatusText(ToolStatus);

    private static string StatusText(ModelProbeCheckStatus status) => status switch {
        ModelProbeCheckStatus.Passed => "Passed",
        ModelProbeCheckStatus.Failed => "Failed",
        ModelProbeCheckStatus.TimedOut => "Timed out",
        _ => "Not run"
    };
}

internal sealed class ModelProviderProbeService : IDisposable {
    private readonly HttpClient _http;
    private readonly bool _disposeHttp;

    public ModelProviderProbeService()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(12) }, disposeHttp: true) {
    }

    internal ModelProviderProbeService(HttpClient httpClient, bool disposeHttp = false) {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _disposeHttp = disposeHttp;
    }

    public async Task<IReadOnlyList<ModelProviderProbeResult>> DiscoverModelsAsync(
        string providerUrl,
        string? apiKey,
        CancellationToken cancellationToken = default) {
        var errors = new List<string>();

        foreach (var endpointRoot in BuildOpenAiEndpointCandidates(providerUrl)) {
            try {
                using var request = CreateRequest(HttpMethod.Get, $"{endpointRoot}/models", apiKey);
                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode) {
                    errors.Add($"{endpointRoot}/models returned {(int)response.StatusCode} {response.ReasonPhrase}");
                    continue;
                }

                return ParseModelsResponse(body, endpointRoot);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) {
                errors.Add($"{endpointRoot}/models failed: {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            errors.Count == 0
                ? "No provider endpoint candidates were available."
                : string.Join("; ", errors));
    }

    public async Task<ModelProviderProbeResult> RunLiveProbeAsync(
        string providerUrl,
        string? apiKey,
        ModelProviderProbeResult model,
        CancellationToken cancellationToken = default) {
        var endpointRoot = string.IsNullOrWhiteSpace(model.ProviderEndpointRoot)
            ? BuildOpenAiEndpointCandidates(providerUrl).First()
            : model.ProviderEndpointRoot.TrimEnd('/');

        var notes = new List<string>();
        var chatStatus = await ProbeChatAsync(endpointRoot, apiKey, model.ModelId, notes, cancellationToken).ConfigureAwait(false);
        var toolStatus = await ProbeToolCallingAsync(endpointRoot, apiKey, model.ModelId, notes, cancellationToken).ConfigureAwait(false);

        return model with {
            ChatStatus = chatStatus,
            ToolStatus = toolStatus,
            Notes = notes.Count == 0 ? model.Notes : string.Join(" ", notes)
        };
    }

    internal static IReadOnlyList<string> BuildOpenAiEndpointCandidates(string providerUrl) {
        if (string.IsNullOrWhiteSpace(providerUrl))
            return Array.Empty<string>();

        var normalized = providerUrl.Trim().TrimEnd('/');
        if (normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return [normalized];

        return [$"{normalized}/v1", normalized];
    }

    internal static IReadOnlyList<ModelProviderProbeResult> ParseModelsResponse(string json, string endpointRoot) {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Array.Empty<ModelProviderProbeResult>();

        var results = new List<ModelProviderProbeResult>();
        foreach (var item in data.EnumerateArray()) {
            var id = GetString(item, "id");
            if (string.IsNullOrWhiteSpace(id))
                continue;

            results.Add(new ModelProviderProbeResult(
                id,
                endpointRoot.TrimEnd('/'),
                ParentModel: GetString(item, "parent") ?? GetString(item, "base_model"),
                Owner: GetString(item, "owned_by") ?? GetString(item, "publisher"),
                CatalogSupportsToolCalling: TryGetToolCallingSupport(item),
                Notes: BuildCatalogNotes(item)));
        }

        return results
            .OrderBy(r => r.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<ModelProbeCheckStatus> ProbeChatAsync(
        string endpointRoot,
        string? apiKey,
        string modelId,
        List<string> notes,
        CancellationToken cancellationToken) {
        var body = new {
            model = modelId,
            messages = new[] {
                new { role = "user", content = "Reply with OK only." }
            },
            max_tokens = 16,
            stream = false
        };

        try {
            using var request = CreateJsonRequest($"{endpointRoot}/chat/completions", apiKey, body);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                notes.Add($"Chat probe returned {(int)response.StatusCode} {response.ReasonPhrase}.");
                return ModelProbeCheckStatus.Failed;
            }

            var content = ExtractFirstAssistantContent(json);
            if (!string.IsNullOrWhiteSpace(content))
                return ModelProbeCheckStatus.Passed;

            notes.Add("Chat probe returned no assistant text.");
            return ModelProbeCheckStatus.Failed;
        }
        catch (TaskCanceledException ex) {
            notes.Add($"Chat probe timed out: {ex.Message}");
            return ModelProbeCheckStatus.TimedOut;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException) {
            notes.Add($"Chat probe failed: {ex.Message}");
            return ModelProbeCheckStatus.Failed;
        }
    }

    private async Task<ModelProbeCheckStatus> ProbeToolCallingAsync(
        string endpointRoot,
        string? apiKey,
        string modelId,
        List<string> notes,
        CancellationToken cancellationToken) {
        var body = new {
            model = modelId,
            messages = new[] {
                new { role = "user", content = "Use the report_probe tool with result set to OK." }
            },
            tools = new[] {
                new {
                    type = "function",
                    function = new {
                        name = "report_probe",
                        description = "Report the model provider probe result.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                result = new { type = "string" }
                            },
                            required = new[] { "result" }
                        }
                    }
                }
            },
            tool_choice = "auto",
            max_tokens = 64,
            stream = false
        };

        try {
            using var request = CreateJsonRequest($"{endpointRoot}/chat/completions", apiKey, body);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                notes.Add($"Tool probe returned {(int)response.StatusCode} {response.ReasonPhrase}.");
                return ModelProbeCheckStatus.Failed;
            }

            if (ResponseContainsStructuredToolCall(json))
                return ModelProbeCheckStatus.Passed;

            var content = ExtractFirstAssistantContent(json);
            if (!string.IsNullOrWhiteSpace(content)) {
                notes.Add("Tool probe returned assistant text instead of a structured tool call.");
                return ModelProbeCheckStatus.Failed;
            }

            notes.Add("Tool probe returned no structured tool call.");
            return ModelProbeCheckStatus.Failed;
        }
        catch (TaskCanceledException ex) {
            notes.Add($"Tool probe timed out: {ex.Message}");
            return ModelProbeCheckStatus.TimedOut;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException) {
            notes.Add($"Tool probe failed: {ex.Message}");
            return ModelProbeCheckStatus.Failed;
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string uri, string? apiKey) {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        return request;
    }

    private static HttpRequestMessage CreateJsonRequest(string uri, string? apiKey, object body) {
        var request = CreateRequest(HttpMethod.Post, uri, apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return request;
    }

    private static string? GetString(JsonElement item, string propertyName) {
        return item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? TryGetToolCallingSupport(JsonElement item) {
        foreach (var name in new[] {
            "supportsToolCalling",
            "supports_tool_calling",
            "toolCalling",
            "tool_calling",
            "supports_tools"
        }) {
            if (TryReadBool(item, name, out var value))
                return value;
        }

        if (item.TryGetProperty("capabilities", out var capabilities) &&
            capabilities.ValueKind == JsonValueKind.Object) {
            foreach (var name in new[] { "toolCalling", "tool_calling", "tools", "functionCalling", "function_calling" }) {
                if (TryReadBool(capabilities, name, out var value))
                    return value;
            }
        }

        return null;
    }

    private static bool TryReadBool(JsonElement item, string propertyName, out bool value) {
        value = false;
        if (!item.TryGetProperty(propertyName, out var property))
            return false;

        if (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False) {
            value = property.GetBoolean();
            return true;
        }

        if (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out value))
            return true;

        return false;
    }

    private static string? BuildCatalogNotes(JsonElement item) {
        var parts = new List<string>();
        if (GetString(item, "object") is { Length: > 0 } objectType)
            parts.Add($"object={objectType}");
        if (item.TryGetProperty("Successful", out var success) &&
            (success.ValueKind == JsonValueKind.True || success.ValueKind == JsonValueKind.False))
            parts.Add($"catalogSuccess={success.GetBoolean()}");
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private static string? ExtractFirstAssistantContent(string json) {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var choice in choices.EnumerateArray()) {
            if (choice.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
                return content.GetString();

            if (choice.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("content", out var deltaContent) &&
                deltaContent.ValueKind == JsonValueKind.String)
                return deltaContent.GetString();
        }

        return null;
    }

    private static bool ResponseContainsStructuredToolCall(string json) {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var choice in choices.EnumerateArray()) {
            if (ElementHasStructuredToolCall(choice, "message") ||
                ElementHasStructuredToolCall(choice, "delta"))
                return true;

            if (choice.TryGetProperty("function_call", out var functionCall) &&
                functionCall.ValueKind == JsonValueKind.Object)
                return true;
        }

        return false;
    }

    private static bool ElementHasStructuredToolCall(JsonElement choice, string propertyName) {
        if (!choice.TryGetProperty(propertyName, out var message) || message.ValueKind != JsonValueKind.Object)
            return false;

        if (message.TryGetProperty("tool_calls", out var toolCalls) &&
            toolCalls.ValueKind == JsonValueKind.Array &&
            toolCalls.GetArrayLength() > 0)
            return true;

        return message.TryGetProperty("function_call", out var functionCall) &&
               functionCall.ValueKind == JsonValueKind.Object;
    }

    public void Dispose() {
        if (_disposeHttp)
            _http.Dispose();
    }
}
