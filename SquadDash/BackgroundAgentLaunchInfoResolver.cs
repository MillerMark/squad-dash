using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;

namespace SquadDash;

internal sealed record TeamAgentDescriptor(
    string DisplayName,
    string AccentKey,
    string RoleText);

internal sealed record BackgroundAgentLaunchInfo(
    string ToolCallId,
    string? TaskName,
    string DisplayName,
    string? AccentKey,
    string? RoleText,
    string? Description,
    string? AgentType,
    string? Prompt);

internal static class BackgroundAgentLaunchInfoResolver {
    private static readonly HashSet<string> GenericTaskTokens = new(StringComparer.OrdinalIgnoreCase) {
        "agent",
        "code",
        "explore",
        "fix",
        "general",
        "layout",
        "purpose",
        "review",
        "task",
        "worker"
    };

    public static BackgroundAgentLaunchInfo? TryResolve(
        string? toolCallId,
        JsonElement args,
        IReadOnlyList<TeamAgentDescriptor> roster) {
        if (string.IsNullOrWhiteSpace(toolCallId) || args.ValueKind != JsonValueKind.Object)
            return null;

        var taskName = TryGetString(args, "name");
        var description = TryGetString(args, "description");
        var agentType = TryGetString(args, "agent_type");
        var prompt = TryGetString(args, "prompt");

        if (string.IsNullOrWhiteSpace(taskName) &&
            string.IsNullOrWhiteSpace(description) &&
            string.IsNullOrWhiteSpace(agentType) &&
            string.IsNullOrWhiteSpace(prompt)) {
            return null;
        }

        var rosterMatch = FindRosterMatch(taskName, description, prompt, roster);
        var displayName = rosterMatch?.DisplayName ?? InferDisplayName(taskName, description, agentType);
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        return new BackgroundAgentLaunchInfo(
            toolCallId.Trim(),
            Normalize(taskName),
            displayName.Trim(),
            Normalize(rosterMatch?.AccentKey),
            Normalize(rosterMatch?.RoleText),
            Normalize(description),
            Normalize(agentType),
            Normalize(prompt));
    }

    public static TeamAgentDescriptor? FindRosterMatch(
        string? taskName,
        string? description,
        string? prompt,
        IReadOnlyList<TeamAgentDescriptor> roster) {
        if (roster.Count == 0)
            return null;

        var fullTaskName = NormalizeKey(taskName);
        var strippedTaskName = NormalizeKey(StripTrailingNumericSuffix(taskName));
        var prefixToken = NormalizeKey(TryExtractAgentPrefix(taskName, agentType: null));
        var descriptionKey = NormalizeKey(description);
        var promptKey = NormalizeKey(prompt);

        var matches = roster
            .Select(candidate => new {
                Candidate = candidate,
                Score = ScoreCandidate(
                    candidate,
                    fullTaskName,
                    strippedTaskName,
                    prefixToken,
                    descriptionKey,
                    promptKey)
            })
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .ToArray();

        if (matches.Length == 0)
            return null;

        if (matches.Length > 1 && matches[0].Score == matches[1].Score)
            return null;

        return matches[0].Candidate;
    }

    private static int ScoreCandidate(
        TeamAgentDescriptor candidate,
        string fullTaskName,
        string strippedTaskName,
        string prefixToken,
        string descriptionKey,
        string promptKey) {
        var nameKey = NormalizeKey(candidate.DisplayName);
        var accentKey = NormalizeKey(candidate.AccentKey);

        if (string.IsNullOrWhiteSpace(nameKey) && string.IsNullOrWhiteSpace(accentKey))
            return 0;

        if (MatchesExactly(fullTaskName, nameKey, accentKey))
            return 120;

        if (MatchesExactly(strippedTaskName, nameKey, accentKey))
            return 110;

        if (MatchesExactly(prefixToken, nameKey, accentKey))
            return 100;

        if (StartsWith(prefixToken, nameKey, accentKey))
            return 90;

        if (Contains(descriptionKey, nameKey, accentKey))
            return 70;

        if (Contains(promptKey, nameKey, accentKey))
            return 60;

        return 0;
    }

    private static bool MatchesExactly(string value, string candidateName, string candidateAccent) {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return string.Equals(value, candidateName, StringComparison.Ordinal) ||
               string.Equals(value, candidateAccent, StringComparison.Ordinal);
    }

    private static bool StartsWith(string value, string candidateName, string candidateAccent) {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return (!string.IsNullOrWhiteSpace(candidateName) && candidateName.StartsWith(value, StringComparison.Ordinal)) ||
               (!string.IsNullOrWhiteSpace(candidateAccent) && candidateAccent.StartsWith(value, StringComparison.Ordinal));
    }

    private static bool Contains(string haystack, string candidateName, string candidateAccent) {
        if (string.IsNullOrWhiteSpace(haystack))
            return false;

        return (!string.IsNullOrWhiteSpace(candidateName) && haystack.Contains(candidateName, StringComparison.Ordinal)) ||
               (!string.IsNullOrWhiteSpace(candidateAccent) && haystack.Contains(candidateAccent, StringComparison.Ordinal));
    }

    private static string InferDisplayName(string? taskName, string? description, string? agentType) {
        var prefix = TryExtractAgentPrefix(taskName, agentType);
        if (!string.IsNullOrWhiteSpace(prefix))
            return Humanize(prefix);

        var normalizedTaskName = StripTrailingNumericSuffix(taskName);
        if (!string.IsNullOrWhiteSpace(normalizedTaskName))
            return Humanize(normalizedTaskName!);

        if (!string.IsNullOrWhiteSpace(description))
            return description!.Trim();

        if (!string.IsNullOrWhiteSpace(agentType))
            return Humanize(agentType!);

        return "Background Agent";
    }

    private static string? TryExtractAgentPrefix(string? taskName, string? agentType) {
        if (string.IsNullOrWhiteSpace(taskName))
            return null;

        var cleaned = StripTrailingNumericSuffix(taskName);
        var segments = cleaned?
            .Split(new[] { '-', '_', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments is not { Length: > 0 })
            return null;

        var first = segments[0];
        var firstKey = NormalizeKey(first);
        if (string.IsNullOrWhiteSpace(firstKey))
            return null;

        var agentTypeKey = NormalizeKey(agentType);
        if (!string.IsNullOrWhiteSpace(agentTypeKey) &&
            (agentTypeKey.Contains(firstKey, StringComparison.Ordinal) ||
             firstKey.Contains(agentTypeKey, StringComparison.Ordinal))) {
            return null;
        }

        if (GenericTaskTokens.Contains(firstKey))
            return null;

        return first;
    }

    private static string? StripTrailingNumericSuffix(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        var index = trimmed.Length;
        while (index > 0 && char.IsDigit(trimmed[index - 1]))
            index--;

        if (index < trimmed.Length) {
            while (index > 0 && (trimmed[index - 1] == '-' || trimmed[index - 1] == '_' || trimmed[index - 1] == '.'))
                index--;
        }

        return trimmed[..index].TrimEnd('-', '_', '.', ' ');
    }

    private static string Humanize(string value) {
        var words = value
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Replace('.', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
            return "Background Agent";

        var builder = new StringBuilder();
        for (var index = 0; index < words.Length; index++) {
            if (index > 0)
                builder.Append(' ');

            var word = words[index];
            if (word.Length == 0)
                continue;

            builder.Append(char.ToUpperInvariant(word[0]));
            if (word.Length > 1)
                builder.Append(word[1..].ToLowerInvariant());
        }

        return builder.ToString();
    }

    private static string NormalizeKey(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim()) {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string? TryGetString(JsonElement element, string propertyName) {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
