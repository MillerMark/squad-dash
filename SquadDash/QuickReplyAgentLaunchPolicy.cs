namespace SquadDash;

internal static class QuickReplyAgentLaunchPolicy {
    public static bool RequiresObservedNamedAgentLaunch(string? routeMode, string? targetAgentHandle) =>
        IsDirectNamedAgentRoute(routeMode) &&
        !string.IsNullOrWhiteSpace(targetAgentHandle);

    private static bool IsDirectNamedAgentRoute(string? routeMode) =>
        string.Equals(routeMode?.Trim(), "start_named_agent", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(routeMode?.Trim(), "continue_current_agent", StringComparison.OrdinalIgnoreCase);

    public static bool MatchesExpectedAgent(string? expectedAgentHandle, string? expectedAgentLabel, SquadSdkEvent evt) {
        var normalizedExpectedHandle = NormalizeHandle(expectedAgentHandle);
        var normalizedExpectedLabel  = NormalizeLabel(expectedAgentLabel);

        if (!string.IsNullOrWhiteSpace(normalizedExpectedHandle)) {
            if (string.Equals(normalizedExpectedHandle, NormalizeHandle(evt.AgentName), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedExpectedHandle, NormalizeHandle(evt.AgentId), StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedExpectedLabel) &&
            string.Equals(normalizedExpectedLabel, NormalizeLabel(evt.AgentDisplayName), StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return !string.IsNullOrWhiteSpace(normalizedExpectedHandle) &&
               string.Equals(
                   NormalizeLabel(AgentNameHumanizer.Humanize(normalizedExpectedHandle)),
                   NormalizeLabel(evt.AgentDisplayName),
                   StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildLaunchFailureMessage(string selectedOption, string? targetAgentLabel, string? targetAgentHandle) {
        var displayName = string.IsNullOrWhiteSpace(targetAgentLabel)
            ? AgentNameHumanizer.Humanize(targetAgentHandle?.Trim().TrimStart('@') ?? "the requested agent")
            : targetAgentLabel.Trim();
        var trimmedOption = string.IsNullOrWhiteSpace(selectedOption)
            ? "(unspecified quick reply)"
            : selectedOption.Trim();

        return $"[routing] SquadDash expected {displayName} to start for quick reply \"{trimmedOption}\", but no matching agent launch was observed. The Coordinator should not promise a named-agent handoff unless that specialist actually starts.";
    }

    private static string? NormalizeHandle(string? handle) =>
        string.IsNullOrWhiteSpace(handle)
            ? null
            : handle.Trim().TrimStart('@').ToLowerInvariant();

    private static string? NormalizeLabel(string? label) {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var buffer = new char[label.Length];
        var length = 0;
        foreach (var character in label.Trim()) {
            if (!char.IsLetterOrDigit(character))
                continue;

            buffer[length++] = char.ToLowerInvariant(character);
        }

        return length == 0 ? null : new string(buffer, 0, length);
    }
}
