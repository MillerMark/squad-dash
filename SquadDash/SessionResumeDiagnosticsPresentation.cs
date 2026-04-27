using System;
using System.Collections.Generic;

namespace SquadDash;

internal static class SessionResumeDiagnosticsPresentation {
    public static string? BuildSummary(SquadSdkEvent? evt) {
        if (evt is null)
            return null;

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(evt.SessionReuseKind))
            parts.Add($"path {FormatReuseKind(evt.SessionReuseKind)}");

        if (evt.SessionAcquireDurationMs is { } acquireMs && acquireMs >= 0)
            parts.Add($"acquire {StatusTimingPresentation.FormatDuration(TimeSpan.FromMilliseconds(acquireMs))}");

        if (evt.SessionResumeDurationMs is { } resumeMs && resumeMs >= 0)
            parts.Add($"provider resume {StatusTimingPresentation.FormatDuration(TimeSpan.FromMilliseconds(resumeMs))}");

        if (evt.SessionCreateDurationMs is { } createMs && createMs >= 0)
            parts.Add($"provider create {StatusTimingPresentation.FormatDuration(TimeSpan.FromMilliseconds(createMs))}");

        if (evt.SessionAgeMs is { } ageMs && ageMs > 0)
            parts.Add($"age {StatusTimingPresentation.FormatDuration(TimeSpan.FromMilliseconds(ageMs))}");

        if (evt.SessionPromptCountIncludingCurrent is { } promptCount && promptCount > 0) {
            var priorPromptCount = Math.Max(0, (evt.SessionPromptCountBeforeCurrent ?? promptCount - 1));
            parts.Add($"prompt #{promptCount} ({priorPromptCount} prior)");
        }

        if (evt.CachedAssistantChars is { } cachedAssistantChars && cachedAssistantChars > 0)
            parts.Add($"cached assistant {cachedAssistantChars} chars");

        var bridgeState = BuildBridgeStateSummary(evt);
        if (!string.IsNullOrWhiteSpace(bridgeState))
            parts.Add(bridgeState);

        if (!string.IsNullOrWhiteSpace(evt.RestoredContextSummary))
            parts.Add($"restored context {evt.RestoredContextSummary}");

        if (!string.IsNullOrWhiteSpace(evt.SessionResumeFailureMessage))
            parts.Add($"resume fallback \"{evt.SessionResumeFailureMessage.Trim()}\"");

        return parts.Count > 0
            ? string.Join("; ", parts) + "."
            : null;
    }

    private static string? BuildBridgeStateSummary(SquadSdkEvent evt) {
        var parts = new List<string>();

        if (evt.BackgroundAgentCount is { } backgroundAgents)
            parts.Add($"{backgroundAgents} bg agents");

        if (evt.BackgroundShellCount is { } backgroundShells)
            parts.Add($"{backgroundShells} bg shells");

        if (evt.KnownSubagentCount is { } knownSubagents)
            parts.Add($"{knownSubagents} known subagents");

        if (evt.ActiveToolCount is { } activeTools)
            parts.Add($"{activeTools} active tools");

        return parts.Count > 0
            ? "bridge state " + string.Join(", ", parts)
            : null;
    }

    private static string FormatReuseKind(string reuseKind) {
        return reuseKind.Trim().Replace('_', ' ');
    }
}
