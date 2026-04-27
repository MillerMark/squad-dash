using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SquadDash;

internal static class AgentImagePathResolver {
    public static string? ResolveBundledPath(AgentStatusCard card, string agentImageAssetsDirectory) {
        ArgumentNullException.ThrowIfNull(card);
        return ResolveBundledPath(agentImageAssetsDirectory, card.AccentStorageKey, card.Name, card.RoleText);
    }

    /// <summary>
    /// Returns the full path to the best-matching role icon PNG for the given agent.
    /// Priority: keyword match on role/handle → <c>GenericAgent.png</c>.
    /// The returned path is always non-null; callers should still guard against
    /// the file not existing on disk.
    /// </summary>
    public static string ResolveRoleIconPath(AgentStatusCard card, string roleIconAssetsDirectory) {
        ArgumentNullException.ThrowIfNull(card);
        return ResolveRoleIconPath(roleIconAssetsDirectory, card.AccentStorageKey, card.RoleText);
    }

    public static string ResolveRoleIconPath(string roleIconAssetsDirectory, string handle, string? roleText) {
        var fileName = MatchRoleIconFileName(handle, roleText);
        return Path.Combine(roleIconAssetsDirectory, fileName);
    }

    private static string MatchRoleIconFileName(string handle, string? roleText) {
        // Build a single search corpus from handle + role text.
        var corpus = string.Concat(handle, " ", roleText ?? string.Empty);

        // Special named agents — check first so they always win.
        if (ContainsAny(corpus, "scribe", "session log"))
            return "scribe.png";
        if (ContainsAny(corpus, "ralph", "work monitor"))
            return "ralph.png";
        if (ContainsAny(corpus, "copilot", "github copilot"))
            return "copilot.png";

        // Role-based matching — ordered from most-distinctive to least.
        if (ContainsAny(corpus, "architect", "lead", "architecture", "system design", "api contract"))
            return "LeadArchitectTechLead.png";
        if (ContainsAny(corpus, "frontend", "ui", "ux", "wpf", "xaml", "design", "layout", "visual"))
            return "FrontendUiDesign.png";
        if (ContainsAny(corpus, "backend", "api", "server", "services", "persistence"))
            return "BackendApiServer.png";
        if (ContainsAny(corpus, "test", "testing", "qa", "quality", "nunit", "coverage"))
            return "TestQaQuality.png";
        if (ContainsAny(corpus, "security", "auth", "compliance", "vulnerability"))
            return "SecurityAuthCompliance.png";
        if (ContainsAny(corpus, "devops", "infra", "infrastructure", "platform", "deployment", "launcher", "ci/cd"))
            return "DevOpsInfraPlatform.png";
        if (ContainsAny(corpus, "data", "database", "analytics", "sql", "storage"))
            return "DataDatabaseAnalytics.png";
        if (ContainsAny(corpus, "docs", "documentation", "devrel", "technical writer", "memory", "institutional"))
            return "DocsDevRelTechnicalWriter.png";

        return "GenericAgent.png";
    }

    private static bool ContainsAny(string corpus, params string[] keywords) {
        foreach (var keyword in keywords) {
            if (corpus.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static string? ResolveBundledPath(string agentImageAssetsDirectory, string accentStorageKey, string agentName, string? roleText) {
        foreach (var candidate in EnumerateCandidateKeys(accentStorageKey, agentName, roleText)) {
            var bundledPath = Path.Combine(agentImageAssetsDirectory, candidate + ".png");
            if (File.Exists(bundledPath))
                return bundledPath;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidateKeys(
        string accentStorageKey,
        string agentName,
        string? roleText) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawCandidate in new[] { accentStorageKey, agentName, roleText }) {
            if (TryAddCandidate(seen, rawCandidate, out var candidate))
                yield return candidate;

            var normalizedCandidate = NormalizeAssetKey(rawCandidate);
            if (TryAddCandidate(seen, normalizedCandidate, out candidate))
                yield return candidate;
        }
    }

    private static bool TryAddCandidate(HashSet<string> seen, string? rawCandidate, out string candidate) {
        candidate = string.Empty;
        if (string.IsNullOrWhiteSpace(rawCandidate))
            return false;

        var trimmed = rawCandidate.Trim();
        if (!seen.Add(trimmed))
            return false;

        candidate = trimmed;
        return true;
    }

    private static string? NormalizeAssetKey(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var builder = new StringBuilder(value.Length);
        var lastCharacterWasSeparator = false;

        foreach (var character in value.Trim()) {
            if (char.IsLetterOrDigit(character)) {
                builder.Append(char.ToLowerInvariant(character));
                lastCharacterWasSeparator = false;
                continue;
            }

            if (builder.Length == 0 || lastCharacterWasSeparator)
                continue;

            builder.Append('-');
            lastCharacterWasSeparator = true;
        }

        while (builder.Length > 0 && builder[^1] == '-')
            builder.Length--;

        return builder.Length == 0 ? null : builder.ToString();
    }

    internal static string? NormalizeAssetKeyForLookup(string? value) => NormalizeAssetKey(value);
}
