namespace SquadDash;

/// <summary>
/// Bring Your Own Key (BYOK) provider configuration for the Copilot CLI bridge process.
/// When <see cref="ProviderUrl"/> is set, GitHub auth is bypassed and the custom provider is used.
/// </summary>
internal sealed record ByokProviderSettings(
    string ProviderUrl,
    string? Model,
    string? ProviderType,
    string? ApiKey,
    bool OfflineMode = false) {

    internal static string? NormalizeProviderUrl(string? providerUrl) {
        if (string.IsNullOrWhiteSpace(providerUrl))
            return null;

        var normalized = providerUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            return normalized;

        var path = uri.AbsolutePath.TrimEnd('/');
        var strippedPath = StripKnownEndpointSuffix(path);
        if (strippedPath != path)
            normalized = BuildUriWithPath(uri, strippedPath);

        if (IsLikelyOllamaProvider(normalized) &&
            !normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) {
            return $"{normalized.TrimEnd('/')}/v1";
        }

        return normalized;
    }

    internal static string? DetectWireApiFromProviderUrl(string? providerUrl, string? providerType) {
        if (string.IsNullOrWhiteSpace(providerUrl) ||
            !(string.Equals(providerType, "openai", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(providerType, "azure", StringComparison.OrdinalIgnoreCase))) {
            return null;
        }

        if (!Uri.TryCreate(providerUrl.Trim(), UriKind.Absolute, out var uri))
            return null;

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            return "responses";

        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/completions", StringComparison.OrdinalIgnoreCase)) {
            return "completions";
        }

        return null;
    }

    internal static string GetProviderRoot(string providerUrl) {
        var normalized = NormalizeProviderUrl(providerUrl) ?? providerUrl.Trim().TrimEnd('/');
        return normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^"/v1".Length]
            : normalized;
    }

    private static string StripKnownEndpointSuffix(string path) {
        foreach (var suffix in new[] {
            "/api/version",
            "/api/tags",
            "/models",
            "/chat/completions",
            "/completions",
            "/responses"
        }) {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return path[..^suffix.Length];
        }

        return path;
    }

    private static string BuildUriWithPath(Uri uri, string path) {
        var builder = new UriBuilder(uri) {
            Path = path.TrimStart('/'),
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private static bool IsLikelyOllamaProvider(string providerUrl) {
        if (!Uri.TryCreate(providerUrl, UriKind.Absolute, out var uri))
            return providerUrl.Contains("ollama", StringComparison.OrdinalIgnoreCase);

        return uri.Port == 11434 ||
               uri.Host.Contains("ollama", StringComparison.OrdinalIgnoreCase);
    }
}
