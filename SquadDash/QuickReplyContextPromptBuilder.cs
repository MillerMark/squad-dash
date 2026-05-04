using System.Text;
using System.Text.RegularExpressions;

namespace SquadDash;

internal sealed record QuickReplyHandoffTurnContext(
    string ThreadTitle,
    string UserPrompt,
    string AssistantResponse,
    DateTimeOffset StartedAt,
    bool IsSourceTurn);

internal sealed record QuickReplyHandoffAgentContext(
    string AgentLabel,
    string? UserPrompt,
    string? AssistantResponse,
    IReadOnlyList<string> RecentActivity,
    DateTimeOffset? LastActivityAt);

internal static class QuickReplyContextPromptBuilder
{
    private const int MaxContextChars = 12000;
    private const int MaxFieldChars = 1800;

    private static readonly Regex SystemNotificationRegex = new(
        @"<system_notification>[\s\S]*?</system_notification>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string BuildHandoffContext(
        string selectedOption,
        string? targetAgentLabel,
        string? routeMode,
        string? targetAgentHandle,
        IReadOnlyList<QuickReplyHandoffTurnContext> recentTurns,
        IReadOnlyList<QuickReplyHandoffAgentContext> recentAgentContexts)
    {
        var trimmedOption = selectedOption.Trim();
        var target = string.IsNullOrWhiteSpace(targetAgentLabel)
            ? "the named agent"
            : targetAgentLabel.Trim();

        var builder = new StringBuilder();
        builder.AppendLine("SquadDash quick-reply handoff context.");
        builder.AppendLine();
        builder.AppendLine($"Target agent: {target}");
        if (!string.IsNullOrWhiteSpace(targetAgentHandle))
            builder.AppendLine($"Target handle: @{targetAgentHandle.Trim().TrimStart('@')}");
        if (!string.IsNullOrWhiteSpace(routeMode))
            builder.AppendLine($"Route mode: {routeMode.Trim()}");
        builder.AppendLine($"Clicked quick reply: \"{trimmedOption}\"");
        builder.AppendLine("Use this handoff to resolve references, pronouns, and intended scope. If the clicked reply or source context asks for a full sweep, honor that; otherwise keep the work scoped to the source task.");

        var sourceTurns = recentTurns
            .Where(turn => turn.IsSourceTurn)
            .ToArray();
        var priorTurns = recentTurns
            .Where(turn => !turn.IsSourceTurn)
            .ToArray();

        if (sourceTurns.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Source transcript context:");
            foreach (var turn in sourceTurns)
                AppendTurn(builder, turn);
        }

        if (recentAgentContexts.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Recent named-agent context:");
            foreach (var agent in recentAgentContexts)
                AppendAgentContext(builder, agent);
        }

        if (priorTurns.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Earlier transcript context:");
            foreach (var turn in priorTurns)
                AppendTurn(builder, turn);
        }

        builder.AppendLine();
        builder.AppendLine("The selected quick reply is the visible user action. The context above is authoritative task scope for the named agent and must be used to resolve vague labels or references.");
        return TrimToMaxContext(builder.ToString().TrimEnd());
    }

    private static void AppendTurn(StringBuilder builder, QuickReplyHandoffTurnContext turn)
    {
        builder.AppendLine();
        builder.AppendLine(turn.IsSourceTurn
            ? $"--- Source turn ({turn.ThreadTitle}, {turn.StartedAt:u}) ---"
            : $"--- Prior turn ({turn.ThreadTitle}, {turn.StartedAt:u}) ---");

        if (NormalizeContext(turn.UserPrompt) is { Length: > 0 } prompt)
        {
            builder.AppendLine("User:");
            builder.AppendLine(prompt);
        }

        if (NormalizeCoordinatorResponse(turn.AssistantResponse) is { Length: > 0 } response)
        {
            builder.AppendLine("Assistant:");
            builder.AppendLine(response);
        }
    }

    private static void AppendAgentContext(StringBuilder builder, QuickReplyHandoffAgentContext agent)
    {
        builder.AppendLine();
        builder.AppendLine($"--- {agent.AgentLabel}" +
                           (agent.LastActivityAt is { } lastActivity ? $" ({lastActivity:u})" : string.Empty) +
                           " ---");

        if (NormalizeContext(agent.UserPrompt) is { Length: > 0 } prompt)
        {
            builder.AppendLine("Prompt:");
            builder.AppendLine(prompt);
        }

        if (NormalizeCoordinatorResponse(agent.AssistantResponse) is { Length: > 0 } response)
        {
            builder.AppendLine("Latest response:");
            builder.AppendLine(response);
        }

        var activity = agent.RecentActivity
            .Select(NormalizeContext)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(4)
            .ToArray();
        if (activity.Length > 0)
        {
            builder.AppendLine("Recent activity:");
            foreach (var item in activity)
                builder.AppendLine("- " + item);
        }
    }

    private static string NormalizeCoordinatorResponse(string? text)
    {
        var normalized = NormalizeContext(text);
        if (normalized.Length == 0)
            return string.Empty;

        if (QuickReplyOptionParser.TryExtractWithMetadata(normalized, out var cleaned, out _))
            normalized = cleaned;
        else if (QuickReplyOptionParser.TryExtract(normalized, out cleaned, out _))
            normalized = cleaned;

        return NormalizeContext(normalized);
    }

    private static string NormalizeContext(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = SystemNotificationRegex.Replace(text, string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Trim();

        if (normalized.Length <= MaxFieldChars)
            return normalized;

        return normalized[..MaxFieldChars].TrimEnd() + "\n[truncated]";
    }

    private static string TrimToMaxContext(string text)
    {
        if (text.Length <= MaxContextChars)
            return text;

        return text[..MaxContextChars].TrimEnd() + "\n[handoff context truncated]";
    }
}
