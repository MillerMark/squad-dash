using System;
using System.Linq;
using System.Collections.Generic;

namespace SquadDash;

internal sealed record AgentThreadIdentitySnapshot(
    string? Title,
    string? AgentId,
    string? AgentName,
    string? AgentDisplayName,
    string? AgentCardKey,
    bool IsPlaceholderThread);

internal static class AgentThreadIdentityPolicy {
    private static readonly HashSet<string> GenericIdentityKeys = new(StringComparer.Ordinal) {
        "agent",
        "backgroundagent",
        "default",
        "general",
        "generalpurpose",
        "purpose",
        "squad",
        "task",
        "worker"
    };

    public static string? ResolveExpectedAgentCardKey(
        string? agentId,
        string? agentName,
        string? agentDisplayName,
        IReadOnlyList<TeamAgentDescriptor> roster) {
        foreach (var card in roster) {
            if (MatchesRosterCardValue(agentDisplayName, card, humanizeValue: false))
                return card.AccentKey;

            if (MatchesRosterCardValue(agentName, card, humanizeValue: true))
                return card.AccentKey;

            if (MatchesRosterCardValue(agentId, card, humanizeValue: true))
                return card.AccentKey;
        }

        var prefixToken =
            TryExtractSpecificPrefixToken(agentId) ??
            TryExtractSpecificPrefixToken(agentName) ??
            TryExtractSpecificPrefixToken(agentDisplayName);
        if (!string.IsNullOrWhiteSpace(prefixToken)) {
            foreach (var card in roster) {
                if (MatchesRosterCardPrefix(prefixToken!, card))
                    return card.AccentKey;
            }
        }

        return null;
    }

    public static bool ThreadMatchesExpectedAgent(
        string? actualAgentCardKey,
        bool isPlaceholderThread,
        string? expectedAgentCardKey) {
        if (string.IsNullOrWhiteSpace(expectedAgentCardKey))
            return true;

        return string.Equals(actualAgentCardKey, expectedAgentCardKey, StringComparison.OrdinalIgnoreCase) ||
               (isPlaceholderThread &&
                string.Equals(actualAgentCardKey, expectedAgentCardKey, StringComparison.OrdinalIgnoreCase)) ||
               !HasRosterBackedIdentity(actualAgentCardKey);
    }

    public static bool CanReuseByAgentName(string? agentName, string? expectedAgentCardKey) =>
        !string.IsNullOrWhiteSpace(agentName) &&
        !string.IsNullOrWhiteSpace(expectedAgentCardKey);

    public static bool CanReuseByDisplayName(string? agentDisplayName, string? expectedAgentCardKey) =>
        !string.IsNullOrWhiteSpace(agentDisplayName) &&
        !string.IsNullOrWhiteSpace(expectedAgentCardKey);

    public static bool CanAliasByAgentName(string? agentName, string? agentCardKey) =>
        !string.IsNullOrWhiteSpace(agentName) &&
        (HasRosterBackedIdentity(agentCardKey) || !IsGenericLooseIdentity(agentName));

    public static bool CanAliasByDisplayName(string? agentDisplayName, string? agentCardKey) =>
        !string.IsNullOrWhiteSpace(agentDisplayName) &&
        HasRosterBackedIdentity(agentCardKey);

    public static string? NormalizeAgentCardKey(
        AgentThreadIdentitySnapshot snapshot,
        IReadOnlyList<TeamAgentDescriptor> roster) {
        var expectedAgentCardKey = ResolveExpectedAgentCardKey(
            snapshot.AgentId,
            snapshot.AgentName,
            snapshot.AgentDisplayName,
            roster);
        if (!string.IsNullOrWhiteSpace(expectedAgentCardKey))
            return expectedAgentCardKey;

        if (!HasRosterBackedIdentity(snapshot.AgentCardKey))
            return Normalize(snapshot.AgentCardKey);

        if (snapshot.IsPlaceholderThread)
            return Normalize(snapshot.AgentCardKey);

        var assignedCard = roster.FirstOrDefault(card =>
            string.Equals(card.AccentKey, snapshot.AgentCardKey?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (assignedCard is null)
            return null;

        return MatchesAssignedRosterCard(snapshot, assignedCard)
            ? Normalize(snapshot.AgentCardKey)
            : null;
    }

    public static bool HasRosterBackedIdentity(string? agentCardKey) =>
        !string.IsNullOrWhiteSpace(agentCardKey) &&
        !agentCardKey.StartsWith("dynamic:", StringComparison.OrdinalIgnoreCase) &&
        !agentCardKey.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesAssignedRosterCard(
        AgentThreadIdentitySnapshot snapshot,
        TeamAgentDescriptor card) {
        return MatchesRosterCardValue(snapshot.AgentDisplayName, card, humanizeValue: false) ||
               MatchesRosterCardValue(snapshot.AgentName, card, humanizeValue: true) ||
               MatchesRosterCardValue(snapshot.AgentId, card, humanizeValue: true) ||
               MatchesRosterCardValue(snapshot.Title, card, humanizeValue: true);
    }

    private static bool MatchesRosterCardValue(
        string? value,
        TeamAgentDescriptor card,
        bool humanizeValue) {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (string.Equals(card.AccentKey, trimmed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(card.DisplayName, trimmed, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (!humanizeValue)
            return false;

        return string.Equals(
            card.DisplayName,
            AgentNameHumanizer.Humanize(trimmed),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesRosterCardPrefix(string prefixToken, TeamAgentDescriptor card) {
        if (string.IsNullOrWhiteSpace(prefixToken))
            return false;

        var normalizedPrefix = NormalizeKey(prefixToken);
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
            return false;

        return NormalizeKey(card.AccentKey).StartsWith(normalizedPrefix, StringComparison.Ordinal) ||
               NormalizeKey(card.DisplayName).StartsWith(normalizedPrefix, StringComparison.Ordinal);
    }

    public static bool IsGenericLooseIdentity(string? value) {
        var normalized = NormalizeKey(value);
        return !string.IsNullOrWhiteSpace(normalized) &&
               GenericIdentityKeys.Contains(normalized);
    }

    private static string? TryExtractSpecificPrefixToken(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var token = value.Trim()
            .Split(new[] { '-', '_', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return IsGenericLooseIdentity(token) ? null : token;
    }

    private static string NormalizeKey(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new System.Text.StringBuilder(value.Length);
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
}
