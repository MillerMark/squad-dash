using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SquadDash;

internal enum ModelProbeCheckStatus {
    NotRun,
    Passed,
    Failed,
    TimedOut,
    NotLoaded
}

internal sealed record ModelProviderProbeResult(
    string ModelId,
    string ProviderEndpointRoot,
    string? ParentModel = null,
    string? Owner = null,
    bool? CatalogSupportsToolCalling = null,
    string? CatalogNotes = null,
    ModelProbeCheckStatus ChatStatus = ModelProbeCheckStatus.NotRun,
    ModelProbeCheckStatus ToolStatus = ModelProbeCheckStatus.NotRun,
    string? Notes = null,
    string? DiagnosticNotes = null,
    bool CanLoadLocally = false,
    bool IsLoadedLocally = false) {

    public string CatalogToolCallingText => CatalogSupportsToolCalling switch {
        true => "Supported",
        false => "Not advertised",
        _ => "Unknown"
    };

    public string ChatStatusText => StatusText(ChatStatus);
    public string ToolStatusText => StatusText(ToolStatus);
    public string ChatStatusDisplay => StatusDisplay(ChatStatus);
    public string ToolStatusDisplay => StatusDisplay(ToolStatus);
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes) ||
                            !string.IsNullOrWhiteSpace(DiagnosticNotes);
    public string NoteSummary => BuildNoteSummary(ChatStatus, ToolStatus, Notes, CanLoadLocally, IsLoadedLocally);
    public string CatalogSummary => SummarizeNote(CatalogNotes);
    public bool HasProbeResult => ChatStatus != ModelProbeCheckStatus.NotRun ||
                                  ToolStatus != ModelProbeCheckStatus.NotRun;
    public string RowActionText =>
        IsLoadFailure
            ? "Details..."
            : CanLoadLocally && !IsLoadedLocally
            ? "Load"
            : IsLoadSuccess && !HasProbeResult
            ? "Probe"
            : HasProbeResult || HasNotes
            ? "Details..."
            : "Probe";
    private bool IsLoadFailure =>
        Notes?.Contains("Load failed", StringComparison.OrdinalIgnoreCase) == true;
    private bool IsLoadSuccess =>
        Notes?.Contains("Load succeeded", StringComparison.OrdinalIgnoreCase) == true;

    private static string StatusText(ModelProbeCheckStatus status) => status switch {
        ModelProbeCheckStatus.Passed => "Passed",
        ModelProbeCheckStatus.Failed => "Failed",
        ModelProbeCheckStatus.TimedOut => "Timed out",
        ModelProbeCheckStatus.NotLoaded => "Not loaded",
        _ => "Not run"
    };

    private static string StatusDisplay(ModelProbeCheckStatus status) => status switch {
        ModelProbeCheckStatus.Passed => "\u2611 Passed",
        ModelProbeCheckStatus.Failed => "\u2715 Failed",
        ModelProbeCheckStatus.TimedOut => "\u2715 Timed out",
        ModelProbeCheckStatus.NotLoaded => "Not loaded",
        _ => "Not run"
    };

    private static string BuildNoteSummary(
        ModelProbeCheckStatus chatStatus,
        ModelProbeCheckStatus toolStatus,
        string? note,
        bool canLoadLocally,
        bool isLoadedLocally) {
        if (IsTransientStatusNote(note))
            return note!.Trim();

        if (canLoadLocally &&
            chatStatus == ModelProbeCheckStatus.NotRun &&
            toolStatus == ModelProbeCheckStatus.NotRun &&
            string.IsNullOrWhiteSpace(note))
            return isLoadedLocally
                ? "Loaded. Click Probe to test this model."
                : "Not loaded. Click Load to load this model.";

        if (chatStatus == ModelProbeCheckStatus.NotLoaded || toolStatus == ModelProbeCheckStatus.NotLoaded) {
            return canLoadLocally
                ? "Not loaded. Click Load to load this model."
                : "Not loaded by provider. Load it on the provider host, then probe again.";
        }

        if (chatStatus == ModelProbeCheckStatus.NotRun &&
            toolStatus == ModelProbeCheckStatus.NotRun &&
            note?.Contains("Load succeeded", StringComparison.OrdinalIgnoreCase) == true)
            return "Loaded. Click Probe to test this model.";

        return SummarizeNote(note);
    }

    private static string SummarizeNote(string? note) {
        if (string.IsNullOrWhiteSpace(note))
            return string.Empty;

        var normalized = NormalizeWhitespace(note);
        const int maxChars = 150;
        return normalized.Length <= maxChars
            ? normalized
            : normalized[..(maxChars - 3)] + "...";
    }

    private static string NormalizeWhitespace(string value) {
        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;
        foreach (var ch in value) {
            if (char.IsWhiteSpace(ch)) {
                if (!previousWasWhitespace)
                    builder.Append(' ');
                previousWasWhitespace = true;
                continue;
            }

            builder.Append(ch);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static bool IsTransientStatusNote(string? note) {
        if (string.IsNullOrWhiteSpace(note))
            return false;

        var trimmed = note.Trim();
        return string.Equals(trimmed, "Loading...", StringComparison.Ordinal) ||
               string.Equals(trimmed, "Probing...", StringComparison.Ordinal);
    }

    public ModelProviderProbeResult WithoutStaleNotLoadedNotes() {
        if (string.IsNullOrWhiteSpace(Notes) &&
            string.IsNullOrWhiteSpace(DiagnosticNotes))
            return this;

        return IsStaleNotLoadedNote(Notes) || IsStaleNotLoadedNote(DiagnosticNotes)
            ? this with { Notes = null, DiagnosticNotes = null }
            : this;
    }

    private static bool IsStaleNotLoadedNote(string? note) {
        return !string.IsNullOrWhiteSpace(note) &&
               note.Contains("not loaded", StringComparison.OrdinalIgnoreCase) &&
               note.Contains("before getting a ChatClient", StringComparison.OrdinalIgnoreCase);
    }

    public static string? AppendDiagnosticNote(string? existing, string? note) {
        if (string.IsNullOrWhiteSpace(note))
            return existing;

        if (string.IsNullOrWhiteSpace(existing))
            return note.Trim();

        return $"{existing.TrimEnd()}{Environment.NewLine}{note.Trim()}";
    }
}

internal sealed record ModelProviderCommandResult(
    bool Success,
    int ExitCode,
    string Output,
    string Error);

internal sealed record FoundryCliCandidate(
    string FileName,
    string Source);

internal sealed record FoundryCliLocation(
    string FileName,
    string Source,
    Version? Version,
    bool IsSelected);

internal sealed record FoundryLoadedModel(
    string ModelId,
    string? Alias,
    string? DisplayName,
    string? Device,
    int? FileSizeMb,
    bool? SupportsToolCalling);

internal sealed record LocalGpuMemoryInfo(
    int Index,
    string Name,
    long TotalMiB,
    long UsedMiB,
    long FreeMiB);

internal sealed record ModelProviderLocalStatus(
    IReadOnlyList<FoundryLoadedModel> LoadedModels,
    IReadOnlyList<LocalGpuMemoryInfo> GpuMemory);

internal sealed record FoundryCliResolution(
    string FileName,
    string Diagnostic,
    bool HasConflict,
    bool IsLegacyVersion,
    Version? SelectedVersion,
    IReadOnlyList<FoundryCliLocation> Locations);

internal sealed record FoundryCliInfo(
    FoundryCliCandidate Candidate,
    bool Success,
    Version? Version,
    bool SupportsServerCommand,
    bool SupportsServiceCommand,
    string? Error) {

    public bool IsUsable => Success && Version is not null;
}

internal sealed class ModelProviderProbeService : IDisposable {
    private readonly HttpClient _http;
    private readonly bool _disposeHttp;
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<ModelProviderCommandResult>> _commandRunner;
    private readonly Func<IReadOnlyList<FoundryCliCandidate>> _foundryCliCandidates;

    public ModelProviderProbeService()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(35) }, disposeHttp: true) {
    }

    internal ModelProviderProbeService(
        HttpClient httpClient,
        bool disposeHttp = false,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<ModelProviderCommandResult>>? commandRunner = null,
        Func<IReadOnlyList<FoundryCliCandidate>>? foundryCliCandidates = null) {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _disposeHttp = disposeHttp;
        _commandRunner = commandRunner ?? RunCommandAsync;
        _foundryCliCandidates = foundryCliCandidates ?? BuildFoundryCliCandidates;
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
                    var errorMessage = ExtractErrorMessage(body);
                    errors.Add($"{endpointRoot}/models returned {(int)response.StatusCode} {response.ReasonPhrase}: {errorMessage ?? "no error body"}");
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
        var diagnosticNotes = new List<string>();
        var chatStatus = await ProbeChatAsync(endpointRoot, apiKey, model.ModelId, notes, diagnosticNotes, cancellationToken).ConfigureAwait(false);
        var toolStatus = chatStatus == ModelProbeCheckStatus.TimedOut
            ? ModelProbeCheckStatus.TimedOut
            : await ProbeToolCallingAsync(endpointRoot, apiKey, model.ModelId, notes, diagnosticNotes, cancellationToken).ConfigureAwait(false);
        if (chatStatus == ModelProbeCheckStatus.TimedOut)
            notes.Add("Tool probe skipped because chat probe timed out.");
        var probeSucceeded = chatStatus == ModelProbeCheckStatus.Passed &&
                             toolStatus == ModelProbeCheckStatus.Passed &&
                             notes.Count == 0;

        return model with {
            ChatStatus = chatStatus,
            ToolStatus = toolStatus,
            Notes = probeSucceeded
                ? "Probe succeeded."
                : notes.Count == 0
                    ? model.Notes
                    : string.Join(" ", notes),
            DiagnosticNotes = diagnosticNotes.Count == 0
                ? model.DiagnosticNotes
                : ModelProviderProbeResult.AppendDiagnosticNote(
                    model.DiagnosticNotes,
                    string.Join(Environment.NewLine, diagnosticNotes))
        };
    }

    public async Task<ModelProviderCommandResult> LoadFoundryModelAsync(
        string modelId,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Model id cannot be empty.", nameof(modelId));

        var foundry = await ResolveFoundryCliAsync(cancellationToken).ConfigureAwait(false);
        var result = await _commandRunner(
            foundry.FileName,
            ["model", "load", modelId.Trim()],
            cancellationToken).ConfigureAwait(false);
        return AttachDiagnostic(result, foundry.Diagnostic);
    }

    public async Task<ModelProviderCommandResult> UnloadFoundryModelAsync(
        string modelId,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Model id cannot be empty.", nameof(modelId));

        var foundry = await ResolveFoundryCliAsync(cancellationToken).ConfigureAwait(false);
        var result = await _commandRunner(
            foundry.FileName,
            ["model", "unload", modelId.Trim()],
            cancellationToken).ConfigureAwait(false);
        return AttachDiagnostic(result, foundry.Diagnostic);
    }

    public async Task<IReadOnlyList<FoundryLoadedModel>> ListLoadedFoundryModelsAsync(
        CancellationToken cancellationToken = default) {
        var foundry = await ResolveFoundryCliAsync(cancellationToken).ConfigureAwait(false);
        var result = await _commandRunner(
            foundry.FileName,
            ["model", "list", "--loaded", "-o", "json"],
            cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            return Array.Empty<FoundryLoadedModel>();

        return ParseFoundryLoadedModels(result.Output);
    }

    public async Task<IReadOnlyList<LocalGpuMemoryInfo>> GetNvidiaGpuMemoryAsync(
        CancellationToken cancellationToken = default) {
        var result = await _commandRunner(
            "nvidia-smi",
            [
                "--query-gpu=index,name,memory.total,memory.used,memory.free",
                "--format=csv,noheader,nounits"
            ],
            cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            return Array.Empty<LocalGpuMemoryInfo>();

        return ParseNvidiaGpuMemory(result.Output);
    }

    public async Task<FoundryCliResolution> DiagnoseFoundryCliAsync(
        CancellationToken cancellationToken = default) {
        return await ResolveFoundryCliAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static IReadOnlyList<string> BuildOpenAiEndpointCandidates(string providerUrl) {
        if (string.IsNullOrWhiteSpace(providerUrl))
            return Array.Empty<string>();

        var normalized = providerUrl.Trim().TrimEnd('/');
        if (normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return [normalized];

        return [$"{normalized}/v1", normalized];
    }

    internal static IReadOnlyList<FoundryCliCandidate> BuildFoundryCliCandidates() {
        var candidates = new List<FoundryCliCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? fileName, string source) {
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            var trimmed = fileName.Trim().Trim('"');
            if (seen.Add(trimmed))
                candidates.Add(new FoundryCliCandidate(trimmed, source));
        }

        Add(Environment.GetEnvironmentVariable("SQUADDASH_FOUNDRY_CLI"), "SQUADDASH_FOUNDRY_CLI");
        var localWindowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps");
        AddPackageAliasCandidates(localWindowsApps, "Microsoft.FoundryLocalCLI_*", "Foundry Local CLI package alias", Add);
        AddPackageAliasCandidates(localWindowsApps, "Microsoft.FoundryLocal_*", "Foundry Local package alias", Add);

        Add("foundry", "PATH alias");

        var windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "WindowsApps");
        AddPackageCandidates(windowsApps, "Microsoft.FoundryLocalCLI_*", "Foundry Local CLI package", Add);
        AddPackageCandidates(windowsApps, "Microsoft.FoundryLocal_*", "Foundry Local package", Add);

        return candidates;
    }

    private static void AddPackageAliasCandidates(
        string localWindowsApps,
        string pattern,
        string source,
        Action<string?, string> add) {
        try {
            if (!Directory.Exists(localWindowsApps))
                return;

            foreach (var directory in Directory.EnumerateDirectories(localWindowsApps, pattern)) {
                var foundry = Path.Combine(directory, "foundry.exe");
                if (File.Exists(foundry))
                    add(foundry, source);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
            // Package-specific aliases are a best-effort path before the global alias.
        }
    }

    private static void AddPackageCandidates(
        string windowsApps,
        string pattern,
        string source,
        Action<string?, string> add) {
        try {
            if (!Directory.Exists(windowsApps))
                return;

            foreach (var directory in Directory.EnumerateDirectories(windowsApps, pattern)) {
                var foundry = Path.Combine(directory, "foundry.exe");
                if (File.Exists(foundry))
                    add(foundry, source);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
            // WindowsApps can be locked down. The PATH alias remains as a fallback.
        }
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
                CatalogNotes: BuildCatalogNotes(item)));
        }

        return results
            .OrderBy(r => r.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static IReadOnlyList<FoundryLoadedModel> ParseFoundryLoadedModels(string json) {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<FoundryLoadedModel>();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            return Array.Empty<FoundryLoadedModel>();

        var results = new List<FoundryLoadedModel>();
        foreach (var model in models.EnumerateArray()) {
            var id = GetString(model, "id");
            if (string.IsNullOrWhiteSpace(id))
                continue;

            results.Add(new FoundryLoadedModel(
                id,
                GetString(model, "alias"),
                GetString(model, "displayName"),
                GetString(model, "device"),
                TryGetInt(model, "fileSizeMb"),
                TryGetBool(model, "supportsToolCalling")));
        }

        return results;
    }

    internal static IReadOnlyList<LocalGpuMemoryInfo> ParseNvidiaGpuMemory(string output) {
        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<LocalGpuMemoryInfo>();

        var results = new List<LocalGpuMemoryInfo>();
        foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
            var parts = rawLine.Split(',').Select(part => part.Trim()).ToArray();
            if (parts.Length < 5)
                continue;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ||
                !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) ||
                !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var used) ||
                !long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var free))
                continue;

            results.Add(new LocalGpuMemoryInfo(index, parts[1], total, used, free));
        }

        return results;
    }

    private async Task<ModelProbeCheckStatus> ProbeChatAsync(
        string endpointRoot,
        string? apiKey,
        string modelId,
        List<string> notes,
        List<string> diagnosticNotes,
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
                var errorMessage = ExtractErrorMessage(json);
                notes.Add($"Chat probe returned {(int)response.StatusCode} {response.ReasonPhrase}.");
                diagnosticNotes.Add($"Chat probe returned {(int)response.StatusCode} {response.ReasonPhrase}: {errorMessage ?? "no error body"}.");
                return IsModelNotLoadedError(errorMessage)
                    ? ModelProbeCheckStatus.NotLoaded
                    : ModelProbeCheckStatus.Failed;
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
        List<string> diagnosticNotes,
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
            tool_choice = new {
                type = "function",
                function = new {
                    name = "report_probe"
                }
            },
            max_tokens = 64,
            stream = false
        };

        try {
            using var request = CreateJsonRequest($"{endpointRoot}/chat/completions", apiKey, body);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                var errorMessage = ExtractErrorMessage(json);
                notes.Add(FormatToolProbeHttpSummary(response, errorMessage));
                diagnosticNotes.Add($"Tool probe returned {(int)response.StatusCode} {response.ReasonPhrase}: {FormatToolProbeError(errorMessage)}.");
                return IsModelNotLoadedError(errorMessage)
                    ? ModelProbeCheckStatus.NotLoaded
                    : ModelProbeCheckStatus.Failed;
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

    private static bool? TryGetBool(JsonElement item, string propertyName) =>
        TryReadBool(item, propertyName, out var value) ? value : null;

    private static int? TryGetInt(JsonElement item, string propertyName) {
        if (!item.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
            return value;

        return property.ValueKind == JsonValueKind.String &&
               int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            ? value
            : null;
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

    private static string? ExtractErrorMessage(string json) {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var error)) {
                if (error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.String)
                    return message.GetString();

                if (error.ValueKind == JsonValueKind.String)
                    return error.GetString();
            }

            if (doc.RootElement.TryGetProperty("message", out var rootMessage) &&
                rootMessage.ValueKind == JsonValueKind.String)
                return rootMessage.GetString();
        }
        catch (JsonException) {
            return json.Length <= 240 ? json : json[..240] + "...";
        }

        return null;
    }

    private static bool IsModelNotLoadedError(string? message) {
        return !string.IsNullOrWhiteSpace(message) &&
               message.Contains("not loaded", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatToolProbeError(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return "no error body";

        if (message.Contains("Error creating grammar", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("TOOL_CALLS", StringComparison.OrdinalIgnoreCase))
            return "Provider rejected the structured tool-call request while creating its tool grammar. The provider/model likely does not currently support OpenAI tool calls correctly.";

        return message;
    }

    private static string FormatToolProbeHttpSummary(HttpResponseMessage response, string? message) {
        var prefix = $"Tool probe returned {(int)response.StatusCode} {response.ReasonPhrase}.";
        if (string.IsNullOrWhiteSpace(message))
            return prefix;

        if (message.Contains("Error creating grammar", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("TOOL_CALLS", StringComparison.OrdinalIgnoreCase))
            return $"{prefix} Provider rejected the structured tool-call request while creating its tool grammar.";

        return prefix;
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

    private async Task<FoundryCliResolution> ResolveFoundryCliAsync(CancellationToken cancellationToken) {
        var candidates = _foundryCliCandidates();
        if (candidates.Count == 0)
            candidates = [new FoundryCliCandidate("foundry", "PATH alias")];

        var infos = new List<FoundryCliInfo>();
        foreach (var candidate in candidates) {
            infos.Add(await ProbeFoundryCliAsync(candidate, cancellationToken).ConfigureAwait(false));
        }

        var overrideInfo = infos.FirstOrDefault(info =>
            info.IsUsable &&
            string.Equals(info.Candidate.Source, "SQUADDASH_FOUNDRY_CLI", StringComparison.OrdinalIgnoreCase));
        var selected = overrideInfo ?? infos
            .Where(info => info.IsUsable)
            .OrderByDescending(info => info.SupportsServerCommand)
            .ThenByDescending(info => info.Version)
            .ThenBy(info => string.Equals(info.Candidate.Source, "PATH alias", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .FirstOrDefault();

        if (selected is null) {
            var failures = infos.Count == 0
                ? "No Foundry CLI candidates were found."
                : string.Join("; ", infos.Select(info => $"{info.Candidate.Source} ({info.Candidate.FileName}) failed: {info.Error ?? "unknown error"}"));
            return new FoundryCliResolution(
                "foundry",
                $"Foundry CLI discovery failed. Falling back to PATH alias. {failures}",
                HasConflict: false,
                IsLegacyVersion: true,
                SelectedVersion: null,
                Locations: infos
                    .Select(info => new FoundryCliLocation(
                        info.Candidate.FileName,
                        info.Candidate.Source,
                        info.Version,
                        IsSelected: false))
                    .ToArray());
        }

        var hasConflict = HasFoundryCliConflict(selected, infos);
        return new FoundryCliResolution(
            selected.Candidate.FileName,
            BuildFoundryCliDiagnostic(selected, infos),
            HasConflict: hasConflict,
            IsLegacyVersion: IsLegacyFoundryVersion(selected.Version),
            SelectedVersion: selected.Version,
            Locations: infos
                .Where(info => info.IsUsable)
                .Select(info => new FoundryCliLocation(
                    info.Candidate.FileName,
                    info.Candidate.Source,
                    info.Version,
                    IsSelected: string.Equals(info.Candidate.FileName, selected.Candidate.FileName, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(location => location.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray());
    }

    private async Task<FoundryCliInfo> ProbeFoundryCliAsync(
        FoundryCliCandidate candidate,
        CancellationToken cancellationToken) {
        var versionResult = await _commandRunner(
            candidate.FileName,
            ["--version"],
            cancellationToken).ConfigureAwait(false);
        if (!versionResult.Success) {
            return new FoundryCliInfo(
                candidate,
                Success: false,
                Version: null,
                SupportsServerCommand: false,
                SupportsServiceCommand: false,
                Error: FirstNonEmpty(versionResult.Error, versionResult.Output) ?? $"exit={versionResult.ExitCode}");
        }

        var version = ParseFoundryVersion(FirstNonEmpty(versionResult.Output, versionResult.Error));
        var helpResult = await _commandRunner(
            candidate.FileName,
            ["--help"],
            cancellationToken).ConfigureAwait(false);
        var help = $"{helpResult.Output}\n{helpResult.Error}";

        return new FoundryCliInfo(
            candidate,
            Success: true,
            Version: version,
            SupportsServerCommand: ContainsCommand(help, "server"),
            SupportsServiceCommand: ContainsCommand(help, "service"),
            Error: version is null ? "Could not parse Foundry CLI version." : null);
    }

    internal static Version? ParseFoundryVersion(string? text) {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = Regex.Match(text, @"\d+\.\d+(?:\.\d+)?(?:\.\d+)?");
        return match.Success && Version.TryParse(match.Value, out var version)
            ? version
            : null;
    }

    private static bool ContainsCommand(string text, string command) {
        return Regex.IsMatch(
            text,
            $@"(^|\s){Regex.Escape(command)}(\s|$)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
    }

    private static bool HasFoundryCliConflict(FoundryCliInfo selected, IReadOnlyList<FoundryCliInfo> infos) {
        return infos.Any(info =>
            info.IsUsable &&
            !string.Equals(info.Candidate.FileName, selected.Candidate.FileName, StringComparison.OrdinalIgnoreCase) &&
            (info.Version != selected.Version ||
             info.SupportsServerCommand != selected.SupportsServerCommand ||
             info.SupportsServiceCommand != selected.SupportsServiceCommand));
    }

    private static bool IsLegacyFoundryVersion(Version? version) {
        return version is null || version < new Version(0, 10, 0);
    }

    private static string BuildFoundryCliDiagnostic(FoundryCliInfo selected, IReadOnlyList<FoundryCliInfo> infos) {
        var lines = new List<string> {
            $"Foundry CLI: {selected.Candidate.FileName}",
            $"Foundry CLI source: {selected.Candidate.Source}",
            $"Foundry CLI version: {selected.Version}",
            $"Foundry CLI mode: {(selected.SupportsServerCommand ? "server" : selected.SupportsServiceCommand ? "service" : "unknown")}"
        };

        var alias = infos.FirstOrDefault(info =>
            info.IsUsable &&
            string.Equals(info.Candidate.Source, "PATH alias", StringComparison.OrdinalIgnoreCase));
        if (alias is not null &&
            !string.Equals(alias.Candidate.FileName, selected.Candidate.FileName, StringComparison.OrdinalIgnoreCase) &&
            (alias.Version != selected.Version ||
             alias.SupportsServerCommand != selected.SupportsServerCommand ||
             alias.SupportsServiceCommand != selected.SupportsServiceCommand)) {
            lines.Add(
                $"Foundry CLI conflict: PATH alias resolves to {alias.Version} at {alias.Candidate.FileName}, but SquadDash selected {selected.Version} from {selected.Candidate.Source}.");
        }

        var otherUsable = infos
            .Where(info => info.IsUsable && !string.Equals(info.Candidate.FileName, selected.Candidate.FileName, StringComparison.OrdinalIgnoreCase))
            .Select(info => $"{info.Candidate.Source}={info.Version}");
        var otherText = string.Join(", ", otherUsable);
        if (!string.IsNullOrWhiteSpace(otherText))
            lines.Add($"Other Foundry CLIs detected: {otherText}");

        return string.Join(Environment.NewLine, lines);
    }

    private static ModelProviderCommandResult AttachDiagnostic(ModelProviderCommandResult result, string diagnostic) {
        return result.Success
            ? result with { Output = AppendLines(diagnostic, result.Output) }
            : result with { Error = AppendLines(diagnostic, result.Error) };
    }

    private static string AppendLines(string first, string second) {
        if (string.IsNullOrWhiteSpace(first))
            return second.Trim();
        if (string.IsNullOrWhiteSpace(second))
            return first.Trim();
        return $"{first.Trim()}{Environment.NewLine}{second.Trim()}";
    }

    private static string? FirstNonEmpty(params string?[] values) {
        foreach (var value in values) {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static async Task<ModelProviderCommandResult> RunCommandAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) {
        var psi = new ProcessStartInfo {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = psi };
        try {
            process.Start();
        }
        catch (Exception ex) {
            return new ModelProviderCommandResult(false, -1, "", ex.Message);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        try {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            TryKill(process);
            throw;
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        return new ModelProviderCommandResult(
            process.ExitCode == 0,
            process.ExitCode,
            output.Trim(),
            error.Trim());
    }

    private static void TryKill(Process process) {
        try {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch {
            // Best-effort cleanup after cancellation.
        }
    }

    public void Dispose() {
        if (_disposeHttp)
            _http.Dispose();
    }
}
