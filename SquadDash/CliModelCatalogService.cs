using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

internal sealed class CliModelCatalogService : ICliModelCatalogService {
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultFetchTimeout = TimeSpan.FromSeconds(12);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Dictionary<string, CliModelMetadata> _cacheByModelId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<CancellationToken, Task<IReadOnlyList<CliModelMetadata>>> _fetchCatalogAsync;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly TimeSpan _cacheTtl;

    private DateTimeOffset _nextRefreshAtUtc = DateTimeOffset.MinValue;
    private bool _hasCache;
    private bool _cacheIsStale;
    internal bool IsCacheStale => _cacheIsStale;

    public CliModelCatalogService(IWorkspacePaths workspacePaths, TimeSpan? cacheTtl = null)
        : this(
            cancellationToken => FetchCatalogViaSdkBridgeAsync(workspacePaths, cancellationToken),
            cacheTtl,
            utcNow: null) {
    }

    internal CliModelCatalogService(
        Func<CancellationToken, Task<IReadOnlyList<CliModelMetadata>>> fetchCatalogAsync,
        TimeSpan? cacheTtl = null,
        Func<DateTimeOffset>? utcNow = null) {
        _fetchCatalogAsync = fetchCatalogAsync ?? throw new ArgumentNullException(nameof(fetchCatalogAsync));
        _cacheTtl = cacheTtl is { } ttl && ttl > TimeSpan.Zero
            ? ttl
            : DefaultCacheTtl;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<CliModelMetadata?> GetModelMetadataAsync(
        string modelId,
        AiProviderType providerType,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Model ID cannot be empty.", nameof(modelId));
        if (providerType != AiProviderType.Copilot)
            return null;

        await EnsureCatalogAsync(cancellationToken).ConfigureAwait(false);
        if (!_hasCache)
            return null;

        _cacheByModelId.TryGetValue(modelId.Trim(), out var metadata);
        return metadata;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        RefreshCoreAsync(force: true, cancellationToken);

    private Task EnsureCatalogAsync(CancellationToken cancellationToken) {
        var now = _utcNow();
        var shouldRefresh = !_hasCache || now >= _nextRefreshAtUtc;
        return shouldRefresh
            ? RefreshCoreAsync(force: false, cancellationToken)
            : Task.CompletedTask;
    }

    private async Task RefreshCoreAsync(bool force, CancellationToken cancellationToken) {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var now = _utcNow();
            if (!force && _hasCache && now < _nextRefreshAtUtc)
                return;

            try {
                var entries = await _fetchCatalogAsync(cancellationToken).ConfigureAwait(false);
                _cacheByModelId.Clear();
                foreach (var entry in entries) {
                    if (string.IsNullOrWhiteSpace(entry.ModelId))
                        continue;
                    _cacheByModelId[entry.ModelId] = entry with {
                        ModelId = entry.ModelId.Trim(),
                        SupportedReasoningEfforts = NormalizeReasoningEfforts(entry.SupportedReasoningEfforts)
                    };
                }

                _hasCache = true;
                _cacheIsStale = false;
                _nextRefreshAtUtc = now + _cacheTtl;
            }
            catch (Exception ex) {
                _cacheIsStale = true;
                _nextRefreshAtUtc = now + _cacheTtl;
                if (_hasCache)
                    SquadDashTrace.Write("CliModelCatalog", $"Model refresh failed; serving stale cache: {ex.Message}");
                else
                    SquadDashTrace.Write("CliModelCatalog", $"Model refresh failed on cold start: {ex.Message}");
            }
        }
        finally {
            _refreshLock.Release();
        }
    }

    private static IReadOnlyList<string> NormalizeReasoningEfforts(IReadOnlyList<string>? efforts) {
        if (efforts is null || efforts.Count == 0)
            return [];

        return efforts
            .Where(effort => !string.IsNullOrWhiteSpace(effort))
            .Select(effort => effort.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<CliModelMetadata>> FetchCatalogViaSdkBridgeAsync(
        IWorkspacePaths workspacePaths,
        CancellationToken cancellationToken) {
        if (workspacePaths is null)
            throw new ArgumentNullException(nameof(workspacePaths));

        var sdkDirectory = workspacePaths.SquadSdkDirectory;
        var scriptPath = Path.Combine(sdkDirectory, "listModels.js");
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("Bundled model discovery script was not found.", scriptPath);

        using var process = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = ResolveNodeExecutablePath(),
                Arguments = "listModels.js",
                WorkingDirectory = sdkDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultFetchTimeout);

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            TryKillProcess(process);
            throw new TimeoutException("Timed out while asking GitHub Copilot for models.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0) {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                ? $"Model discovery exited with code {process.ExitCode}."
                : stderr.Trim());
        }

        return ParseCatalog(stdout);
    }

    internal static IReadOnlyList<CliModelMetadata> ParseCatalog(string json) {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var metadata = new List<CliModelMetadata>();
        foreach (var element in document.RootElement.EnumerateArray()) {
            if (element.ValueKind != JsonValueKind.Object)
                continue;
            if (!TryGetStringProperty(element, "id", out var modelId) || string.IsNullOrWhiteSpace(modelId))
                continue;

            TryGetStringProperty(element, "category", out var category);
            TryGetStringProperty(element, "releaseStatus", out var releaseStatus);
            var inputPrice = TryGetDecimalProperty(element, "inputPricePerMillionTokens");
            var outputPrice = TryGetDecimalProperty(element, "outputPricePerMillionTokens");
            var efforts = TryGetReasoningEffortsProperty(element);

            metadata.Add(new CliModelMetadata(
                modelId.Trim(),
                category,
                inputPrice,
                outputPrice,
                releaseStatus,
                efforts));
        }

        return metadata;
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value) {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property))
            return false;

        if (property.ValueKind != JsonValueKind.String)
            return false;

        var raw = property.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        value = raw.Trim();
        return true;
    }

    private static decimal? TryGetDecimalProperty(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var numericValue))
            return numericValue;

        if (property.ValueKind != JsonValueKind.String)
            return null;

        var text = property.GetString();
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static IReadOnlyList<string> TryGetStringArrayProperty(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return [];

        var values = new List<string>();
        foreach (var item in property.EnumerateArray()) {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                values.Add(value.Trim());
        }

        return values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> TryGetReasoningEffortsProperty(JsonElement element) {
        var plural = TryGetStringArrayProperty(element, "supportedReasoningEfforts");
        if (plural.Count > 0)
            return plural;

        var singularArray = TryGetStringArrayProperty(element, "supportedReasoningEffort");
        if (singularArray.Count > 0)
            return singularArray;

        if (TryGetStringProperty(element, "supportedReasoningEffort", out var singularValue) &&
            singularValue is not null) {
            return [singularValue];
        }

        return [];
    }

    private static string ResolveNodeExecutablePath() {
        static IEnumerable<string> SplitPath(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? Enumerable.Empty<string>()
                : value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var candidates = new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe")
        };

        foreach (var candidate in candidates) {
            if (File.Exists(candidate))
                return candidate;
        }

        foreach (var scope in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine }) {
            foreach (var directory in SplitPath(Environment.GetEnvironmentVariable("PATH", scope))) {
                var nodePath = Path.Combine(directory.Trim('"'), "node.exe");
                if (File.Exists(nodePath))
                    return nodePath;
            }
        }

        return "node";
    }

    private static void TryKillProcess(Process process) {
        try {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) {
            SquadDashTrace.Write("CliModelCatalog", $"Failed to terminate model discovery process: {ex.Message}");
        }
    }
}
