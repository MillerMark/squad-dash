using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Resolves the most qualified roster agent for a specific plan step,
/// based on the step description, routing rules, and active roster.
/// </summary>
internal sealed class PlanStepAgentResolver
{
    private readonly IReadOnlyList<RoutingRule> _rules;
    private readonly IReadOnlyList<RosterAgent> _activeAgents;

    public PlanStepAgentResolver(
        IReadOnlyList<RoutingRule> rules,
        IReadOnlyList<RosterAgent> activeAgents)
    {
        _rules        = rules;
        _activeAgents = activeAgents;
    }

    /// <summary>
    /// Resolves the best agent for a step given its title and description.
    /// Returns a resolution with <c>IsGenericFallback == true</c> if no qualified
    /// active roster member can be identified.
    /// </summary>
    public AgentRoutingResolution Resolve(string stepTitle, string stepDescription)
    {
        var searchText = (stepTitle + " " + stepDescription).ToLowerInvariant();

        int         bestScore = 0;
        RoutingRule? bestRule  = null;

        foreach (var rule in _rules)
        {
            int score = 0;

            if (searchText.Contains(rule.WorkType.ToLowerInvariant(), StringComparison.Ordinal))
                score += 2;

            var keywords = rule.Keywords.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var kw in keywords)
            {
                var clean = CleanKeyword(kw).ToLowerInvariant();
                if (clean.Length > 0 && searchText.Contains(clean, StringComparison.Ordinal))
                    score += 3;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestRule  = rule;
            }
        }

        if (bestScore < 1 || bestRule is null)
            return Fallback("No qualified active roster member matched this step type");

        var agent = _activeAgents.FirstOrDefault(a =>
            string.Equals(a.Name, bestRule.AgentName, StringComparison.OrdinalIgnoreCase) &&
            a.IsActive);

        if (agent is null)
            return Fallback("No qualified active roster member matched this step type");

        return new AgentRoutingResolution(
            agent.Name,
            agent.Handle,
            bestRule.WorkType,
            FallbackReason: null,
            IsGenericFallback: false);
    }

    /// <summary>
    /// Parses the routing table from the content of a routing.md file.
    /// Returns an empty list on parse failure or empty input.
    /// </summary>
    internal static IReadOnlyList<RoutingRule> ParseRoutingMd(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<RoutingRule>();

        try
        {
            var rules = new List<RoutingRule>();
            foreach (var rawLine in content.Split('\n'))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith('|') || line.Contains("---", StringComparison.Ordinal))
                    continue;

                var parts = line.Split('|');
                if (parts.Length < 5) continue;

                var workType  = parts[1].Trim();
                var agentName = parts[2].Trim();
                var keywords  = parts[3].Trim();

                if (string.IsNullOrWhiteSpace(workType) || string.IsNullOrWhiteSpace(agentName))
                    continue;
                // Skip header rows
                if (workType.Equals("Work Type", StringComparison.OrdinalIgnoreCase) ||
                    workType.Equals("Label", StringComparison.OrdinalIgnoreCase))
                    continue;

                rules.Add(new RoutingRule(workType, agentName, keywords));
            }
            return rules;
        }
        catch
        {
            return Array.Empty<RoutingRule>();
        }
    }

    /// <summary>
    /// Parses the Members table from the content of a team.md file.
    /// Only agents with a Status containing "active" (case-insensitive) are included.
    /// Returns an empty list on parse failure or empty input.
    /// </summary>
    internal static IReadOnlyList<RosterAgent> ParseTeamMd(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<RosterAgent>();

        try
        {
            var agents = new List<RosterAgent>();
            foreach (var rawLine in content.Split('\n'))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith('|') || line.Contains("---", StringComparison.Ordinal))
                    continue;

                var parts = line.Split('|');
                if (parts.Length < 6) continue;

                var name    = parts[1].Trim();
                var charter = parts[3].Trim();
                var status  = parts[4].Trim();

                if (string.IsNullOrWhiteSpace(name) ||
                    name.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    continue;

                var isActive    = status.Equals("active", StringComparison.OrdinalIgnoreCase);
                var charterPath = string.IsNullOrWhiteSpace(charter) || charter == "—" ? null : charter;
                var charterDirectory = charterPath is null
                    ? null
                    : Path.GetFileName(Path.GetDirectoryName(charterPath.Replace('/', Path.DirectorySeparatorChar)));
                var handle = string.IsNullOrWhiteSpace(charterDirectory)
                    ? name.ToLowerInvariant().Replace(' ', '-')
                    : charterDirectory.ToLowerInvariant();

                agents.Add(new RosterAgent(name, handle, charterPath, isActive));
            }
            return agents;
        }
        catch
        {
            return Array.Empty<RosterAgent>();
        }
    }

    private static AgentRoutingResolution Fallback(string reason) =>
        new(null, null, null, reason, IsGenericFallback: true);

    private static string CleanKeyword(string kw) =>
        new string(kw.Where(c => c != '`' && c != '*' && c != '\\').ToArray()).Trim();
}

internal sealed record RoutingRule(
    string WorkType,
    string AgentName,
    string Keywords);

internal sealed record RosterAgent(
    string  Name,
    string  Handle,
    string? CharterPath,
    bool    IsActive);

internal sealed record AgentRoutingResolution(
    string? AgentName,
    string? AgentHandle,
    string? MatchedWorkType,
    string? FallbackReason,
    bool    IsGenericFallback);
