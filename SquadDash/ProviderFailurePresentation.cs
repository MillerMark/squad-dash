using System.Text.RegularExpressions;

namespace SquadDash;

internal enum ProviderFailureCategory
{
    Authentication,
    DeploymentOrModel,
    EndpointOrProtocol,
    RateLimitOrQuota,
    Timeout,
    Network,
    InvalidRequest,
    ContentSafety,
    ProviderService,
    Cancelled,
    Unknown
}

internal sealed record ProviderFailureContext(
    string? Model = null,
    string? ProfileAlias = null,
    string? ProviderBaseUrl = null,
    string? ProviderType = null,
    string? WireApi = null);

internal sealed record ProviderFailurePresentation(
    ProviderFailureCategory Category,
    string Title,
    string Explanation,
    string Guidance,
    string RawError,
    string? ContextLine)
{
    private static readonly Regex SecretAssignmentPattern = new(
        @"(?i)(api[_-]?key|authorization|token|secret|password|bearer)\s*[:=]\s*(?:bearer\s+)?[^\s,;]+",
        RegexOptions.Compiled);

    private static readonly Regex SecretQueryPattern = new(
        @"(?i)([?&](?:api[_-]?key|access_token|token)=)[^&#\s]+",
        RegexOptions.Compiled);

    private static readonly Regex OpenAiKeyPattern = new(
        @"\bsk-[A-Za-z0-9_-]{12,}\b",
        RegexOptions.Compiled);

    internal static ProviderFailurePresentation Analyze(string? error, ProviderFailureContext? context = null)
    {
        context ??= new ProviderFailureContext();
        var raw = RedactSecrets(string.IsNullOrWhiteSpace(error) ? "No provider error details were returned." : error.Trim());
        var normalized = raw.ToLowerInvariant();
        var isAzure = ContainsAny(
            (context.ProviderBaseUrl ?? string.Empty).ToLowerInvariant(),
            "azure.com", "azure.net", "services.ai.azure.com") ||
            ContainsAny(normalized, "azure openai", "deploymentnotfound");

        ProviderFailureCategory category;
        string title;
        string explanation;
        string guidance;

        if (ContainsAny(normalized, "operation was canceled", "operation was cancelled", "request canceled", "request cancelled"))
        {
            category = ProviderFailureCategory.Cancelled;
            title = "Provider request cancelled";
            explanation = "The request ended before the provider returned a response.";
            guidance = "Retry the operation. If cancellation repeats, check whether a restart, timeout, or user cancellation is interrupting the request.";
        }
        else if (HasHttpStatus(normalized, 401) || HasHttpStatus(normalized, 403) ||
                 ContainsAny(normalized, "unauthorized", "forbidden", "invalid api key", "incorrect api key", "authentication failed", "permission denied"))
        {
            category = ProviderFailureCategory.Authentication;
            title = "Provider authentication failed";
            explanation = "The provider rejected the configured credential or its permissions.";
            guidance = "Update the profile's API key or token and confirm that it can access the selected model or deployment.";
        }
        else if (HasHttpStatus(normalized, 429) ||
                 ContainsAny(normalized, "rate limit", "ratelimit", "too many requests", "insufficient_quota", "quota exceeded", "capacity exceeded"))
        {
            category = ProviderFailureCategory.RateLimitOrQuota;
            title = "Provider rate limit or quota reached";
            explanation = "The provider temporarily refused the request because capacity, rate, or account quota was exhausted.";
            guidance = "Wait and retry, reduce concurrency, or review the provider account's quota, billing, and deployment capacity.";
        }
        else if (normalized.Contains("deploymentnotfound", StringComparison.Ordinal) ||
                 (isAzure && HasHttpStatus(normalized, 404) &&
                  ContainsAny(normalized, "resource not found", "model", "deployment", "not found")))
        {
            category = ProviderFailureCategory.DeploymentOrModel;
            title = "Azure deployment not found";
            explanation = BuildDeploymentExplanation(context);
            guidance = "Set Model to the exact Azure deployment name for this resource, or create that deployment, then retry.";
        }
        else if (ContainsAny(normalized, "model_not_found", "model not found", "model does not exist", "no such model", "unsupported model", "unknown model"))
        {
            category = ProviderFailureCategory.DeploymentOrModel;
            title = "Model not found";
            explanation = BuildModelExplanation(context);
            guidance = "Choose a model available to this provider account, or correct the model identifier in the profile.";
        }
        else if (HasHttpStatus(normalized, 404) ||
                 ContainsAny(normalized, "cannot post", "unsupported protocol", "unsupported wire", "unknown endpoint", "invalid endpoint", "route not found"))
        {
            category = ProviderFailureCategory.EndpointOrProtocol;
            title = "Provider endpoint or protocol mismatch";
            explanation = "The configured endpoint did not expose the API route SquadDash tried to use.";
            guidance = BuildEndpointGuidance(context);
        }
        else if (ContainsAny(normalized, "timed out", "timeout", "etimedout", "deadline exceeded"))
        {
            category = ProviderFailureCategory.Timeout;
            title = "Provider request timed out";
            explanation = "The provider did not complete the request within the allowed time.";
            guidance = "Retry the operation. If it repeats, check provider health, network latency, and the configured timeout.";
        }
        else if (ContainsAny(normalized, "econnrefused", "econnreset", "enotfound", "dns", "name resolution", "network error", "connection refused", "connection reset", "tls", "ssl", "certificate"))
        {
            category = ProviderFailureCategory.Network;
            title = "Provider network connection failed";
            explanation = "SquadDash could not establish or maintain a valid connection to the provider.";
            guidance = "Verify the endpoint, internet or local-server availability, proxy/firewall settings, DNS, and TLS certificate.";
        }
        else if (ContainsAny(normalized, "content_filter", "content filter", "responsibleaipolicyviolation", "safety policy"))
        {
            category = ProviderFailureCategory.ContentSafety;
            title = "Provider safety policy blocked the request";
            explanation = "The provider declined the request under its content-safety policy.";
            guidance = "Review the provider's safety details and revise the prompt or supplied content before retrying.";
        }
        else if (HasHttpStatus(normalized, 400) ||
                 ContainsAny(normalized, "invalid_request_error", "bad request", "unsupported parameter", "context_length_exceeded", "maximum context length"))
        {
            category = ProviderFailureCategory.InvalidRequest;
            title = "Provider rejected the request";
            explanation = "The request format, parameters, or context were not accepted by the selected provider/model.";
            guidance = "Review the raw error below, then correct the profile protocol/model or reduce unsupported parameters and context.";
        }
        else if (HasAnyServerStatus(normalized) ||
                 ContainsAny(normalized, "service unavailable", "bad gateway", "internal server error", "overloaded"))
        {
            category = ProviderFailureCategory.ProviderService;
            title = "Provider service failure";
            explanation = "The provider returned a server-side or availability error.";
            guidance = "Retry after a short delay and check the provider status page if the failure continues.";
        }
        else
        {
            category = ProviderFailureCategory.Unknown;
            title = "Agent provider failed";
            explanation = "SquadDash received an unclassified provider or agent-launch error.";
            guidance = "Use the complete raw error below to verify the profile's endpoint, protocol, credential, and model, then retry.";
        }

        return new ProviderFailurePresentation(
            category,
            title,
            explanation,
            guidance,
            raw,
            BuildContextLine(context));
    }

    internal string BuildThreadSummary(string agentLabel) =>
        $"{NormalizeLabel(agentLabel)} failed: {Title}. {Explanation}";

    internal string BuildCopyText(string agentLabel)
    {
        var lines = new List<string>
        {
            $"{NormalizeLabel(agentLabel)} — {Title}",
            Explanation
        };
        if (!string.IsNullOrWhiteSpace(ContextLine))
            lines.Add(ContextLine);
        lines.Add("Provider error:");
        lines.Add(RawError);
        lines.Add("How to fix: " + Guidance);
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildDeploymentExplanation(ProviderFailureContext context)
    {
        var profile = Quote(context.ProfileAlias);
        var model = Quote(context.Model);
        if (profile is not null && model is not null)
            return $"Profile {profile} points to model/deployment {model}, but the Azure resource could not find that deployment.";
        if (model is not null)
            return $"The Azure resource could not find model/deployment {model}.";
        return "The Azure resource could not find the configured deployment.";
    }

    private static string BuildModelExplanation(ProviderFailureContext context)
    {
        var model = Quote(context.Model);
        return model is null
            ? "The provider could not find or access the configured model."
            : $"The provider could not find or access model {model}.";
    }

    private static string BuildEndpointGuidance(ProviderFailureContext context)
    {
        var wire = context.WireApi?.Trim().ToLowerInvariant();
        return wire switch
        {
            "responses" => "Verify the base URL supports the Responses API and that the profile protocol is set to Responses.",
            "completions" or "chat_completions" or "chat-completions" => "Verify the base URL supports Chat Completions and that the profile protocol is set to Chat Completions.",
            _ => "Verify the profile's base URL and select the protocol the provider supports (Responses or Chat Completions)."
        };
    }

    private static string? BuildContextLine(ProviderFailureContext context)
    {
        var parts = new List<string>();
        AddContext(parts, "Profile", context.ProfileAlias);
        AddContext(parts, "Model/deployment", context.Model);
        AddContext(parts, "Provider", context.ProviderType);
        AddContext(parts, "Protocol", context.WireApi);
        AddContext(parts, "Endpoint", context.ProviderBaseUrl);
        return parts.Count == 0 ? null : string.Join("  •  ", parts);
    }

    private static void AddContext(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parts.Add($"{name}: {RedactSecrets(value.Trim())}");
    }

    private static bool HasHttpStatus(string text, int status) =>
        Regex.IsMatch(text, $@"(?<!\d)(?:http\s*)?{status}(?!\d)", RegexOptions.IgnoreCase);

    private static bool HasAnyServerStatus(string text) =>
        Regex.IsMatch(text, @"(?<!\d)(?:http\s*)?5\d\d(?!\d)", RegexOptions.IgnoreCase);

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static string? Quote(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"“{RedactSecrets(value.Trim())}”";

    private static string NormalizeLabel(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Agent" : value.Trim();

    internal static string RedactSecrets(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var redacted = SecretAssignmentPattern.Replace(value, match => match.Groups[1].Value + ": [REDACTED]");
        redacted = SecretQueryPattern.Replace(redacted, match => match.Groups[1].Value + "[REDACTED]");
        return OpenAiKeyPattern.Replace(redacted, "[REDACTED API KEY]");
    }
}
