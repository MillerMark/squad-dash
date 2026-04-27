using System;

namespace SquadDash;

internal static class SilentBackgroundAgentPolicy {
    public static bool IsSilentAgentHandle(string? handle) =>
        string.Equals(Normalize(handle), "scribe", StringComparison.Ordinal);

    public static bool ShouldSuppressThread(string? agentId, string? agentName, string? agentDisplayName) =>
        IsSilentAgentHandle(agentId) ||
        IsSilentAgentHandle(agentName) ||
        string.Equals(Normalize(agentDisplayName), "scribe", StringComparison.Ordinal);

    public static string BuildSilentScribeLaunchPrompt(string selectedOption) {
        var trimmedOption = string.IsNullOrWhiteSpace(selectedOption)
            ? "(unspecified quick reply)"
            : selectedOption.Trim();

        return
            "The user selected a quick reply that routes the next step to Scribe. " +
            "Scribe is a silent background specialist. Do not perform this work in the Coordinator turn. " +
            "Launch exactly one background agent now with these settings:\n" +
            "agent_type: \"general-purpose\"\n" +
            "mode: \"background\"\n" +
            "description: \"Scribe: Log session & merge decisions\"\n" +
            "prompt: |\n" +
            "  You are the Scribe. Read .squad/agents/scribe/charter.md.\n" +
            "  TEAM ROOT: use the current repo root.\n" +
            "  Selected quick reply: " + trimmedOption + "\n\n" +
            "After the background Scribe agent starts, emit no further response text unless the launch fails. " +
            "Do not narrate Scribe's internal steps and do not answer as the Coordinator.";
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().TrimStart('@').ToLowerInvariant();
}
