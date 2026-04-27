using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Text;

namespace SquadDash;

internal static class SquadBridgePromptBuilder {
    private static readonly Regex MentionRegex = new(@"(?<![A-Za-z0-9_-])@(?<handle>[a-z0-9][a-z0-9_-]{1,63})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BacktickTokenRegex = new(@"`(?<token>[^`\r\n]{3,})`", RegexOptions.CultureInvariant);
    private static readonly Regex[] HiringIntentRegexes = [
        new(@"\b(?:please\s+)?(?:hire|recruit|bring\s+in|add)\b[\s\S]{0,80}?\b(?:teammate|team member|agent|specialist|reviewer|architect)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\b(?:need|want|looking\s+for|can\s+we\s+get|could\s+we\s+get|let'?s\s+get|we\s+need)\b[\s\S]{0,80}?\b(?:another|new)\s+(?:teammate|team member|agent|specialist|reviewer|architect)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
    ];
    private static readonly Regex[] HiringFalsePositiveRegexes = [
        new(@"\bhire\s+(?:a\s+)?new\s+agent\s+(?:window|dialog|screen|panel|view)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bhire\s+agent\s+(?:window|dialog|screen|panel|view)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\b(?:window|dialog|screen|panel|view)\b[\s\S]{0,40}?\bhire\s+(?:a\s+)?new\s+agent\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
    ];

    internal sealed record BuildResult(string PromptText, string RoutingSummary);

    private sealed record TeamRoutingMember(string Name, string Handle, string Role, string? CharterPath);

    private sealed record RoutingRule(string WorkType, string RouteTo, string Examples);

    private sealed record RoutingContext(
        string? GenericInstruction,
        string? ExplicitInstruction,
        string? StrongInstruction,
        string Summary);

    public static BuildResult Build(
        string prompt,
        string quickReplyInstruction,
        string? quickReplyRoutingInstruction,
        string? quickReplyRouteMode,
        string? supplementalInstruction,
        string? workspaceFolder) {
        var trimmedPrompt = string.IsNullOrWhiteSpace(prompt)
            ? null
            : prompt.TrimEnd();
        var builder = new StringBuilder();

        static void AppendSegment(StringBuilder builder, string? segment) {
            if (string.IsNullOrWhiteSpace(segment))
                return;

            if (builder.Length > 0) {
                builder.Append(Environment.NewLine);
                builder.Append(Environment.NewLine);
            }

            builder.Append(segment.Trim());
        }

        var routingContext = BuildRoutingContext(prompt, workspaceFolder, quickReplyRouteMode);

        AppendSegment(builder, trimmedPrompt);
        AppendSegment(builder, quickReplyRoutingInstruction);
        AppendSegment(builder, routingContext.ExplicitInstruction);
        AppendSegment(builder, routingContext.StrongInstruction);
        AppendSegment(builder, supplementalInstruction);
        AppendSegment(builder, routingContext.GenericInstruction);

        var customUniverseContext = BuildCustomUniverseContext(prompt, workspaceFolder);
        AppendSegment(builder, customUniverseContext);
        AppendSegment(builder, quickReplyInstruction);

        return new BuildResult(builder.ToString(), routingContext.Summary);
    }

    internal static string? BuildCustomUniverseContext(string prompt, string? workspaceFolder) {
        if (string.IsNullOrWhiteSpace(prompt) ||
            string.IsNullOrWhiteSpace(workspaceFolder) ||
            !LooksLikeHiringPrompt(prompt)) {
            return null;
        }

        var squadDirectory = Path.Combine(workspaceFolder, ".squad");
        var customUniversePath = Path.Combine(squadDirectory, "universes", "squaddash.md");
        if (!File.Exists(customUniversePath))
            return null;

        return "The authoritative roster is `.squad/team.md`. When adding or hiring a teammate in this workspace, consult `.squad/casting/policy.json`, `.squad/casting/history.json`, and `.squad/casting/registry.json` first. If the user's request explicitly names a preferred universe, treat that universe choice as authoritative for this hire as long as it is allowlisted; do not override it just because the workspace previously used the SquadDash Universe. Use `.squad/universes/squaddash.md` when the selected universe is the SquadDash Universe or when you need the SquadDash roster as a reference. Do not invent an ad hoc temporary role when a real team member should be recruited.";
    }

    private static bool LooksLikeHiringPrompt(string prompt) {
        var normalizedPrompt = prompt.Trim();
        if (normalizedPrompt.Length == 0)
            return false;

        if (HiringFalsePositiveRegexes.Any(regex => regex.IsMatch(normalizedPrompt)))
            return false;

        return HiringIntentRegexes.Any(regex => regex.IsMatch(normalizedPrompt));
    }

    private static RoutingContext BuildRoutingContext(string prompt, string? workspaceFolder, string? quickReplyRouteMode) {
        if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(workspaceFolder))
            return new RoutingContext(null, null, null, "routing=none");

        var squadDirectory = Path.Combine(workspaceFolder, ".squad");
        var teamPath = Path.Combine(squadDirectory, "team.md");
        var routingPath = Path.Combine(squadDirectory, "routing.md");

        var members = LoadTeamMembers(teamPath, workspaceFolder);
        var rules = LoadRoutingRules(routingPath);

        var genericInstruction = File.Exists(teamPath) && File.Exists(routingPath)
            ? "The active squad roster is defined in `.squad/team.md`, and the routing guide is in `.squad/routing.md`. Consult those files before deciding who should handle this request. If one specialist is the clear primary owner, route to that specialist instead of keeping the work in the Coordinator. If the task spans multiple specialties, keep the Coordinator responsible for delegation and orchestration. When you name owners in plans, delegated follow-up work, backlog breakdowns, reviews, or recommendations, keep those named owners aligned with `.squad/routing.md` instead of assigning by convenience. In particular, keep testing, QA, verification, and coverage work with the testing owner from `.squad/routing.md` unless that file explicitly says otherwise or the work is clearly described as collaboration under that testing owner."
            : null;

        var explicitInstruction = BuildMentionRoutingInstruction(prompt, members);
        var strongInstruction = explicitInstruction is null && !IsForcedNamedAgentQuickReply(quickReplyRouteMode)
            ? BuildStrongMatchRoutingInstruction(prompt, members, rules, workspaceFolder)
            : null;

        var summaryParts = new List<string>();
        if (genericInstruction is not null)
            summaryParts.Add("generic");
        if (explicitInstruction is not null)
            summaryParts.Add("explicit-mention");
        if (strongInstruction is not null)
            summaryParts.Add("strong-match");
        if (IsForcedNamedAgentQuickReply(quickReplyRouteMode))
            summaryParts.Add("quick-reply-start-named-agent");

        return new RoutingContext(
            genericInstruction,
            explicitInstruction,
            strongInstruction,
            summaryParts.Count == 0 ? "routing=none" : $"routing={string.Join(",", summaryParts)}");
    }

    private static string? BuildMentionRoutingInstruction(string prompt, IReadOnlyList<TeamRoutingMember> members) {
        if (members.Count == 0)
            return null;

        var matches = MentionRegex.Matches(prompt)
            .Select(match => match.Groups["handle"].Value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(handle => members.FirstOrDefault(member => string.Equals(member.Handle, handle, StringComparison.OrdinalIgnoreCase)))
            .Where(member => member is not null)
            .Cast<TeamRoutingMember>()
            .DistinctBy(member => member.Handle)
            .ToArray();

        if (matches.Length == 0)
            return null;

        if (matches.Length == 1) {
            var member = matches[0];
            return $"The user explicitly addressed @{member.Handle}. Route this request to {member.Name} and have that specialist follow their charter unless the user explicitly asked only for coordination.";
        }

        var handles = string.Join(", ", matches.Select(member => "@" + member.Handle));
        return $"The user explicitly addressed {handles}. Keep the Coordinator responsible for orchestration long enough to involve those named specialists according to `.squad/team.md` and `.squad/routing.md`.";
    }

    private static string? BuildStrongMatchRoutingInstruction(
        string prompt,
        IReadOnlyList<TeamRoutingMember> members,
        IReadOnlyList<RoutingRule> rules,
        string workspaceFolder) {
        if (members.Count == 0)
            return null;

        var matches = members
            .Select(member => new {
                Member = member,
                Matches = BuildOwnershipSignals(member, members, rules, workspaceFolder)
                    .Where(signal => PromptContainsSignal(prompt, signal))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .Where(candidate => candidate.Matches.Length > 0)
            .ToArray();

        if (matches.Length != 1)
            return null;

        var winner = matches[0];
        var signalList = string.Join(", ", winner.Matches.Take(3).Select(signal => $"`{signal}`"));
        return $"This request contains direct ownership clues for {winner.Member.Name} ({signalList}). Route it to {winner.Member.Name} as the primary specialist unless the user explicitly asked the Coordinator to keep the work.";
    }

    private static bool PromptContainsSignal(string prompt, string signal) {
        if (string.IsNullOrWhiteSpace(signal))
            return false;

        if (signal.Contains('.', StringComparison.Ordinal) ||
            signal.Contains('_', StringComparison.Ordinal) ||
            signal.Any(char.IsUpper)) {
            return prompt.Contains(signal, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static IReadOnlyList<string> BuildOwnershipSignals(
        TeamRoutingMember member,
        IReadOnlyList<TeamRoutingMember> members,
        IReadOnlyList<RoutingRule> rules,
        string workspaceFolder) {
        var signals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownMemberNames = members
            .Select(candidate => candidate.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules.Where(rule => string.Equals(rule.RouteTo, member.Name, StringComparison.OrdinalIgnoreCase))) {
            foreach (var token in ExtractOwnershipTokens(rule.WorkType, knownMemberNames))
                signals.Add(token);
            foreach (var token in ExtractOwnershipTokens(rule.Examples, knownMemberNames))
                signals.Add(token);
        }

        if (!string.IsNullOrWhiteSpace(member.CharterPath)) {
            var charterPath = Path.IsPathRooted(member.CharterPath)
                ? member.CharterPath
                : Path.Combine(workspaceFolder, member.CharterPath);

            if (File.Exists(charterPath)) {
                foreach (var token in ExtractOwnershipTokens(File.ReadAllText(charterPath), knownMemberNames))
                    signals.Add(token);
            }
        }

        return signals.ToArray();
    }

    private static IEnumerable<string> ExtractOwnershipTokens(string? text, IReadOnlySet<string> knownMemberNames) {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        foreach (Match match in BacktickTokenRegex.Matches(text)) {
            var token = match.Groups["token"].Value.Trim();
            if (LooksLikeOwnershipToken(token, knownMemberNames))
                yield return token;
        }

        foreach (var rawToken in text.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (LooksLikeOwnershipToken(rawToken, knownMemberNames))
                yield return rawToken.Trim();
        }
    }

    private static bool LooksLikeOwnershipToken(string token, IReadOnlySet<string> knownMemberNames) {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        token = token.Trim();
        if (knownMemberNames.Contains(token))
            return false;

        return token.Length >= 6 &&
               (token.Contains('.', StringComparison.Ordinal) ||
                token.Contains('_', StringComparison.Ordinal) ||
                token.Any(char.IsUpper));
    }

    private static bool IsForcedNamedAgentQuickReply(string? quickReplyRouteMode) =>
        string.Equals(quickReplyRouteMode?.Trim(), "start_named_agent", StringComparison.OrdinalIgnoreCase);

    private static TeamRoutingMember[] LoadTeamMembers(string teamPath, string workspaceFolder) {
        if (!File.Exists(teamPath))
            return [];

        var lines = File.ReadAllLines(teamPath);
        var membersHeader = Array.FindIndex(lines, line => line.Trim().Equals("## Members", StringComparison.OrdinalIgnoreCase));
        if (membersHeader < 0)
            return [];

        var rows = new List<TeamRoutingMember>();
        var headerIndex = -1;
        for (var i = membersHeader + 1; i < lines.Length; i++) {
            var line = lines[i].Trim();
            if (line.Length == 0)
                continue;
            if (!line.StartsWith("|", StringComparison.Ordinal))
                break;

            if (headerIndex < 0) {
                headerIndex = i;
                continue;
            }

            if (line.Contains("---", StringComparison.Ordinal))
                continue;

            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length < 5)
                continue;

            var name = cells[1].Trim();
            var role = cells[2].Trim();
            var charterPath = cells[3].Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var handle = DeriveHandle(name, charterPath);
            var resolvedCharter = string.IsNullOrWhiteSpace(charterPath)
                ? null
                : Path.Combine(workspaceFolder, ".squad", charterPath.Replace('/', Path.DirectorySeparatorChar));
            rows.Add(new TeamRoutingMember(name, handle, role, resolvedCharter));
        }

        return rows.ToArray();
    }

    private static RoutingRule[] LoadRoutingRules(string routingPath) {
        if (!File.Exists(routingPath))
            return [];

        return File.ReadAllLines(routingPath)
            .Select(ParseRoutingRule)
            .Where(rule => rule is not null)
            .Cast<RoutingRule>()
            .ToArray();
    }

    private static RoutingRule? ParseRoutingRule(string line) {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("|", StringComparison.Ordinal) ||
            trimmed.Contains("---", StringComparison.Ordinal)) {
            return null;
        }

        var cells = trimmed.Split('|', StringSplitOptions.TrimEntries);
        if (cells.Length < 5)
            return null;

        var workType = cells[1].Trim();
        var routeTo = cells[2].Trim();
        var examples = cells[3].Trim();
        if (string.IsNullOrWhiteSpace(workType) ||
            string.IsNullOrWhiteSpace(routeTo) ||
            workType.Equals("Work Type", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return new RoutingRule(workType, routeTo, examples);
    }

    private static string DeriveHandle(string name, string? charterPath) {
        if (!string.IsNullOrWhiteSpace(charterPath)) {
            var normalized = charterPath.Replace('\\', '/');
            const string marker = "agents/";
            var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0) {
                var afterMarker = normalized[(markerIndex + marker.Length)..];
                var slashIndex = afterMarker.IndexOf('/');
                if (slashIndex > 0)
                    return afterMarker[..slashIndex].Trim().ToLowerInvariant();
            }
        }

        var builder = new StringBuilder();
        var previousWasSeparator = false;
        foreach (var ch in name.Trim().ToLowerInvariant()) {
            if (char.IsLetterOrDigit(ch)) {
                builder.Append(ch);
                previousWasSeparator = false;
                continue;
            }

            if (previousWasSeparator)
                continue;

            builder.Append('-');
            previousWasSeparator = true;
        }

        return builder.ToString().Trim('-');
    }
}
