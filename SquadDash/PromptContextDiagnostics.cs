using System;
using System.Collections.Generic;

namespace SquadDash;

internal sealed record PromptContextDiagnostics(
    string? SessionId,
    DateTimeOffset? SessionUpdatedAt,
    DateTimeOffset? TranscriptStartedAt,
    int CoordinatorTurnCount,
    int AgentThreadCount,
    int AgentThreadTurnCount,
    int PromptHistoryCount,
    int RecentSessionCount,
    int CoordinatorPromptChars,
    int CoordinatorResponseChars,
    int CoordinatorThinkingChars,
    int AgentPromptChars,
    int AgentResponseChars,
    int AgentThinkingChars) {

    public int TotalChars =>
        CoordinatorPromptChars +
        CoordinatorResponseChars +
        CoordinatorThinkingChars +
        AgentPromptChars +
        AgentResponseChars +
        AgentThinkingChars;
}

internal static class PromptContextDiagnosticsPresentation {
    public static string BuildTraceSummary(PromptContextDiagnostics diagnostics, DateTimeOffset now) {
        var riskBand = GetRiskBand(diagnostics, now);
        var parts = new List<string> {
            $"riskBand={riskBand}",
            $"sessionId={diagnostics.SessionId ?? "(none)"}",
            $"coordinatorTurns={diagnostics.CoordinatorTurnCount}",
            $"agentThreads={diagnostics.AgentThreadCount}",
            $"agentTurns={diagnostics.AgentThreadTurnCount}",
            $"promptHistory={diagnostics.PromptHistoryCount}",
            $"recentSessions={diagnostics.RecentSessionCount}",
            $"totalChars={diagnostics.TotalChars}",
            $"coordinatorPromptChars={diagnostics.CoordinatorPromptChars}",
            $"coordinatorResponseChars={diagnostics.CoordinatorResponseChars}",
            $"coordinatorThinkingChars={diagnostics.CoordinatorThinkingChars}",
            $"agentPromptChars={diagnostics.AgentPromptChars}",
            $"agentResponseChars={diagnostics.AgentResponseChars}",
            $"agentThinkingChars={diagnostics.AgentThinkingChars}"
        };

        if (diagnostics.TranscriptStartedAt is { } transcriptStartedAt) {
            parts.Add(
                $"transcriptAgeMs={(int)Math.Max(0, (now - transcriptStartedAt).TotalMilliseconds)}");
        }

        if (diagnostics.SessionUpdatedAt is { } sessionUpdatedAt) {
            parts.Add(
                $"lastPersistedUpdateMs={(int)Math.Max(0, (now - sessionUpdatedAt).TotalMilliseconds)}");
        }

        return string.Join(" ", parts);
    }

    public static string GetRiskBand(PromptContextDiagnostics diagnostics, DateTimeOffset now) {
        var score = 0;

        if (diagnostics.TotalChars >= 120_000)
            score += 3;
        else if (diagnostics.TotalChars >= 50_000)
            score += 2;
        else if (diagnostics.TotalChars >= 20_000)
            score += 1;

        var totalTurns = diagnostics.CoordinatorTurnCount + diagnostics.AgentThreadTurnCount;
        if (totalTurns >= 30)
            score += 2;
        else if (totalTurns >= 15)
            score += 1;

        if (diagnostics.TranscriptStartedAt is { } transcriptStartedAt) {
            var transcriptAge = now - transcriptStartedAt;
            if (transcriptAge >= TimeSpan.FromMinutes(90))
                score += 2;
            else if (transcriptAge >= TimeSpan.FromMinutes(30))
                score += 1;
        }

        if (diagnostics.PromptHistoryCount >= 20)
            score += 1;

        return score >= 5
            ? "high"
            : score >= 2
                ? "medium"
                : "low";
    }
}
