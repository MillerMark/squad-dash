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
    AgentRuntime,
    Cancelled,
    Unknown
}

internal sealed record ProviderFailureContext(
    string? Model = null,
    string? ProfileAlias = null,
    string? ProviderBaseUrl = null,
    string? ProviderType = null,
    string? WireApi = null,
    string? RequestedModel = null,
    string? ProfileId = null,
    IReadOnlyList<ModelProfile>? AvailableProfiles = null)
{
    internal bool HasDifferentModelAlternate =>
        IsConcreteModel(Model) &&
        AvailableProfiles?.Any(profile =>
            profile.IsEnabled &&
            !string.Equals(profile.Id, ProfileId, StringComparison.OrdinalIgnoreCase) &&
            IsConcreteModel(profile.Model) &&
            !string.Equals(profile.Model?.Trim(), Model?.Trim(), StringComparison.OrdinalIgnoreCase)) == true;

    internal static ProviderFailureContext FromEvent(
        SquadSdkEvent evt,
        ModelProfile? activeProfile,
        IReadOnlyList<ModelProfile>? profiles)
    {
        var profile = profiles?.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, evt.ProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null &&
            (string.IsNullOrWhiteSpace(evt.ProfileId) ||
             string.Equals(evt.ProfileId, activeProfile?.Id, StringComparison.OrdinalIgnoreCase)))
            profile = activeProfile;
        return new ProviderFailureContext(
            evt.SessionModel ?? evt.Model ?? profile?.Model,
            evt.ProfileAlias ?? profile?.Alias,
            evt.ProviderBaseUrl ?? profile?.ProviderUrl,
            evt.ProviderType ?? profile?.ProviderType,
            evt.WireApi ?? profile?.WireApi,
            evt.RequestedModel,
            evt.ProfileId ?? profile?.Id,
            profiles);
    }

    private static bool IsConcreteModel(string? model) =>
        !string.IsNullOrWhiteSpace(model) &&
        !string.Equals(model.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
}

internal sealed record ProviderFailurePresentation(
    ProviderFailureCategory Category,
    string Title,
    string Explanation,
    string Guidance,
    string RawError,
    string? ContextLine)
{
    internal bool IsQuotaEntitlementFailure { get; init; }

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
        var isTerminalAccountQuota = ContainsAny(
            normalized,
            "insufficient_quota", "payment required", "credits exhausted",
            "credit exhausted", "account quota", "hard limit", "monthly usage limit",
            "billing_hard_limit", "billing_not_active", "quota exhausted", "quota is exhausted",
            "quota entitlement") ||
            (HasHttpStatus(normalized, 429) && normalized.Contains("billing", StringComparison.Ordinal));
        var isAzure = ContainsAny(
            (context.ProviderBaseUrl ?? string.Empty).ToLowerInvariant(),
            "azure.com", "azure.net", "services.ai.azure.com") ||
            ContainsAny(normalized, "azure openai", "deploymentnotfound");
        var isTransientManagedDeploymentFailure =
            normalized.Contains("api deployment for this resource does not exist", StringComparison.Ordinal) &&
            !isAzure;

        ProviderFailureCategory category;
        string title;
        string explanation;
        string guidance;

        if (normalized.Contains("github copilot authentication lookup failed", StringComparison.Ordinal) &&
            HasAnyServerStatus(normalized))
        {
            category = ProviderFailureCategory.ProviderService;
            title = "GitHub Copilot authentication service is temporarily unavailable";
            explanation = "GitHub could not verify the saved Copilot sign-in because its authentication service returned a server error.";
            guidance = "Wait briefly and retry. The saved sign-in may still be valid; do not sign in again or restart SquadDash unless a later check reports that no credential is available.";
        }
        else if (ContainsAny(normalized,
                "failed to connect to copilot cli",
                "cli server exited with code",
                "agent runtime exited with code"))
        {
            category = ProviderFailureCategory.AgentRuntime;
            title = "Agent runtime stopped during startup";
            explanation = "The local agent process exited before it established a provider session. Warning text on stderr does not by itself identify the cause.";
            guidance = "Retry once. If it repeats, run Squad Doctor and use the complete trace entry to diagnose the local CLI startup failure.";
        }
        else if (ContainsAny(normalized, "operation was canceled", "operation was cancelled", "request canceled", "request cancelled"))
        {
            category = ProviderFailureCategory.Cancelled;
            title = "Provider request cancelled";
            explanation = "The request ended before the provider returned a response.";
            guidance = "Retry the operation. If cancellation repeats, check whether a restart, timeout, or user cancellation is interrupting the request.";
        }
        else if (isTerminalAccountQuota)
        {
            category = ProviderFailureCategory.RateLimitOrQuota;
            title = "Provider quota or billing requires action";
            explanation = "The provider rejected the request because the account's usable quota or billing allowance is exhausted.";
            guidance = BuildQuotaGuidance(context);
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
            title = "Provider rate limit persisted after retries";
            explanation = "The provider continued throttling the request after SquadDash used its bounded wait-and-resume recovery.";
            guidance = "Wait for the provider's rate window to reset, reduce concurrent model work, or review the deployment's rate allocation before retrying.";
        }
        else if (isTransientManagedDeploymentFailure)
        {
            category = ProviderFailureCategory.ProviderService;
            title = "Provider model deployment is temporarily unavailable";
            explanation = BuildManagedDeploymentExplanation(context);
            guidance = "Retry the operation. If the provider continues returning this error, temporarily select another available model and try again later.";
        }
        else if (normalized.Contains("deploymentnotfound", StringComparison.Ordinal) ||
                 (isAzure && HasHttpStatus(normalized, 404) &&
                  ContainsAny(normalized, "resource not found", "model", "deployment", "not found")))
        {
            category = ProviderFailureCategory.DeploymentOrModel;
            title = "Azure deployment not found";
            explanation = BuildDeploymentExplanation(context);
            guidance = "Open SquadDash Options > AI > Model, select this profile, and set Model to the exact Azure deployment name that exists on this resource.";
        }
        else if (ContainsAny(normalized, "model_not_found", "model not found", "model does not exist", "no such model", "unsupported model", "unknown model") ||
                 (normalized.Contains("model", StringComparison.Ordinal) &&
                  normalized.Contains("is not available", StringComparison.Ordinal)))
        {
            category = ProviderFailureCategory.DeploymentOrModel;
            title = "Model not found";
            explanation = BuildModelExplanation(context);
            guidance = "Open SquadDash Options > AI > Model, select this profile, and choose a model that is available for this provider account.";
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
            explanation = "The provider or agent session did not complete the request within the allowed time. This error does not indicate token or budget exhaustion unless the provider reports that separately.";
            guidance = "Resume from the preserved work. If it repeats, check provider health, network latency, model availability, and the configured request timeout.";
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
            guidance = "Review the raw error shown above, then correct the profile protocol/model or reduce unsupported parameters and context.";
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
            guidance = "Review the complete raw error shown above, then open SquadDash Options > AI > Model and select or verify an available model for this profile before retrying.";
        }

        return new ProviderFailurePresentation(
            category,
            title,
            explanation,
            guidance,
            raw,
            BuildContextLine(context))
        {
            IsQuotaEntitlementFailure = category == ProviderFailureCategory.RateLimitOrQuota && isTerminalAccountQuota
        };
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

    private static string BuildQuotaGuidance(ProviderFailureContext context)
    {
        var providerName = !string.IsNullOrWhiteSpace(context.ProfileAlias)
            ? context.ProfileAlias.Trim()
            : context.ProviderType?.Trim().ToLowerInvariant() switch
            {
                "copilot" => "GitHub Copilot",
                "openai" => "OpenAI",
                "azure" => "Azure OpenAI",
                "anthropic" => "Anthropic",
                _ => string.IsNullOrWhiteSpace(context.ProviderType) ? "The provider" : context.ProviderType.Trim()
            };
        providerName = RedactSecrets(providerName);
        return context.HasDifferentModelAlternate
            ? $"{providerName} quota is exhausted. You can disable this provider to try an alternate. To re-enable {providerName}, increase quota/spend settings or wait for the quota reset."
            : $"{providerName} quota is exhausted. You can add an alternate provider in Settings. You can also increase quota/spend settings (or wait for quota reset) to continue.";
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

    private static string BuildManagedDeploymentExplanation(ProviderFailureContext context)
    {
        var model = Quote(context.Model);
        return model is null
            ? "The provider could not route this request to its managed model deployment. This is usually a transient provider-side availability problem."
            : $"The provider could not route this request to the managed deployment for model {model}. This is usually a transient provider-side availability problem.";
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
        if (!string.Equals(context.RequestedModel?.Trim(), context.Model?.Trim(), StringComparison.OrdinalIgnoreCase))
            AddContext(parts, "Requested model", context.RequestedModel);
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

internal static class ProviderFailureRecoveryPolicy
{
    internal static bool IsRecoverableInterruption(ProviderFailureCategory category) =>
        category == ProviderFailureCategory.Timeout;

    internal static string BuildThreadStatus(ProviderFailureCategory category) =>
        IsRecoverableInterruption(category) ? "Interrupted" : "Failed";
}
