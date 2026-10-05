namespace SquadDash;

internal static class AgentModelPresentation {
    internal static string? GetOverrideHandle(TranscriptThreadState thread, string? agentCardHandle) =>
        NormalizeHandle(thread.RequestedAgentHandle)
        ?? NormalizeHandle(thread.AgentCardKey)
        ?? NormalizeHandle(agentCardHandle)
        ?? NormalizeHandle(thread.AgentName);

    internal static string? NormalizeHandle(string? handle) {
        if (string.IsNullOrWhiteSpace(handle))
            return null;

        return handle.Trim().TrimStart('@').ToLowerInvariant();
    }
}
