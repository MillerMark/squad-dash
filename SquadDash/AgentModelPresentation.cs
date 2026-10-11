namespace SquadDash;

internal static class AgentModelPresentation {
    internal static string? GetOverrideHandle(
        TranscriptThreadState? thread, string? agentCardHandle, bool isLeadAgent = false) =>
        isLeadAgent || thread?.Kind == TranscriptThreadKind.Coordinator
            ? "coordinator"
            : NormalizeHandle(thread?.RequestedAgentHandle)
              ?? NormalizeHandle(thread?.AgentCardKey)
              ?? NormalizeHandle(agentCardHandle)
              ?? NormalizeHandle(thread?.AgentName);

    internal static string GetProfileCategory(string handle) => NormalizeHandle(handle) switch {
        "coordinator" => ModelProfileCategory.Coordinator,
        "rai" => ModelProfileCategory.RAI,
        "scribe" => ModelProfileCategory.Scribe,
        "ralph" => ModelProfileCategory.Ralph,
        "fact-checker" => ModelProfileCategory.FactChecker,
        _ => ModelProfileCategory.SpawnedNamedAgents
    };

    internal static string? NormalizeHandle(string? handle) {
        if (string.IsNullOrWhiteSpace(handle))
            return null;

        return handle.Trim().TrimStart('@').ToLowerInvariant();
    }
}
