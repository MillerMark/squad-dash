using System;
using System.Linq;
using System.Collections.Generic;

namespace SquadDash;

internal sealed record BackgroundTaskThreadSnapshot(
    string ThreadId,
    string Title,
    string? ToolCallId,
    string? AgentId,
    string? AgentCardKey,
    string StatusText,
    bool WasObservedAsBackgroundTask,
    bool IsPlaceholderThread,
    DateTimeOffset StartedAt,
    DateTimeOffset? LastObservedActivityAt,
    DateTimeOffset? CompletedAt);

internal static class BackgroundTaskStateResolver {
    public static bool IsThreadBackedBySnapshot(
        BackgroundTaskThreadSnapshot thread,
        IReadOnlyList<SquadBackgroundAgentInfo> snapshotAgents,
        Func<SquadBackgroundAgentInfo, string> resolveSnapshotLabel,
        Func<BackgroundTaskThreadSnapshot, string> resolveThreadLabel) {
        if (snapshotAgents.Count == 0)
            return false;

        if (!string.IsNullOrWhiteSpace(thread.ToolCallId) &&
            snapshotAgents.Any(agent =>
                string.Equals(agent.ToolCallId, thread.ToolCallId, StringComparison.OrdinalIgnoreCase))) {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(thread.AgentId) &&
            snapshotAgents.Any(agent =>
                string.Equals(agent.AgentId, thread.AgentId, StringComparison.OrdinalIgnoreCase))) {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(thread.AgentCardKey) &&
            snapshotAgents.Any(agent =>
                string.Equals(agent.AgentId, thread.AgentCardKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(agent.AgentName, thread.AgentCardKey, StringComparison.OrdinalIgnoreCase))) {
            return true;
        }

        var threadLabel = resolveThreadLabel(thread);
        return snapshotAgents.Any(agent =>
            string.Equals(resolveSnapshotLabel(agent), threadLabel, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsFallbackLiveThread(
        BackgroundTaskThreadSnapshot thread,
        IReadOnlyList<SquadBackgroundAgentInfo> snapshotAgents,
        bool isPromptRunning,
        DateTimeOffset now,
        TimeSpan recentActivityLinger,
        Func<SquadBackgroundAgentInfo, string> resolveSnapshotLabel,
        Func<BackgroundTaskThreadSnapshot, string> resolveThreadLabel) {
        if (thread.IsPlaceholderThread || !thread.WasObservedAsBackgroundTask || IsTerminalStatus(thread.StatusText))
            return false;

        if (IsThreadBackedBySnapshot(thread, snapshotAgents, resolveSnapshotLabel, resolveThreadLabel))
            return false;

        return isPromptRunning || now - GetLastActivityAt(thread) <= recentActivityLinger;
    }

    public static IReadOnlyList<BackgroundTaskThreadSnapshot> GetFallbackLiveThreads(
        IReadOnlyList<SquadBackgroundAgentInfo> snapshotAgents,
        IReadOnlyList<BackgroundTaskThreadSnapshot> threads,
        bool isPromptRunning,
        DateTimeOffset now,
        TimeSpan recentActivityLinger,
        Func<SquadBackgroundAgentInfo, string> resolveSnapshotLabel,
        Func<BackgroundTaskThreadSnapshot, string> resolveThreadLabel) {
        return threads
            .Where(thread => IsFallbackLiveThread(
                thread,
                snapshotAgents,
                isPromptRunning,
                now,
                recentActivityLinger,
                resolveSnapshotLabel,
                resolveThreadLabel))
            .OrderByDescending(GetLastActivityAt)
            .ThenBy(thread => resolveThreadLabel(thread), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DateTimeOffset GetLastActivityAt(BackgroundTaskThreadSnapshot thread) {
        if (thread.LastObservedActivityAt is { } lastObservedActivityAt)
            return lastObservedActivityAt;

        if (thread.CompletedAt is { } completedAt)
            return completedAt;

        return thread.StartedAt;
    }

    private static bool IsTerminalStatus(string? statusText) {
        if (string.IsNullOrWhiteSpace(statusText))
            return false;

        return statusText.Trim() switch {
            "Completed" => true,
            "Failed" => true,
            "Cancelled" => true,
            _ => false
        };
    }
}
