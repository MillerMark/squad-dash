using System;

namespace SquadDash;

internal sealed record BackgroundAgentReportAnnouncement(
    string Header,
    string Body,
    string FullResponse);

internal static class BackgroundAgentReportAnnouncementBuilder {
    public static BackgroundAgentReportAnnouncement? TryBuild(
        string title,
        string? agentId,
        string? prompt,
        string? latestResponse,
        string? lastAnnouncedResponse,
        bool wasObservedAsBackgroundTask,
        bool isLiveBackgroundTask,
        bool isTerminal) {
        if (!wasObservedAsBackgroundTask)
            return null;

        var fullResponse = Normalize(latestResponse);
        if (string.IsNullOrWhiteSpace(fullResponse))
            return null;

        if (isLiveBackgroundTask && !isTerminal)
            return null;

        var previousResponse = Normalize(lastAnnouncedResponse);
        string body;
        bool isContinuation;

        if (string.IsNullOrEmpty(previousResponse)) {
            body = fullResponse;
            isContinuation = false;
        }
        else if (string.Equals(fullResponse, previousResponse, StringComparison.Ordinal)) {
            return null;
        }
        else if (fullResponse.StartsWith(previousResponse, StringComparison.Ordinal)) {
            body = fullResponse[previousResponse.Length..].TrimStart('\r', '\n');
            if (string.IsNullOrWhiteSpace(body))
                return null;

            isContinuation = true;
        }
        else {
            body = fullResponse;
            isContinuation = false;
        }

        var label = BuildAgentLabel(title, agentId);
        return new BackgroundAgentReportAnnouncement(
            BackgroundWorkClassifier.BuildAnnouncementHeader(
                label,
                isContinuation,
                prompt,
                fullResponse,
                latestIntent: null,
                detailText: null),
            body,
            fullResponse);
    }

    private static string BuildAgentLabel(string title, string? agentId) {
        var trimmedTitle = string.IsNullOrWhiteSpace(title)
            ? "Background agent"
            : title.Trim();
        var trimmedAgentId = Normalize(agentId);

        if (string.IsNullOrWhiteSpace(trimmedAgentId) ||
            trimmedTitle.Contains(trimmedAgentId, StringComparison.OrdinalIgnoreCase)) {
            return trimmedTitle;
        }

        return $"{trimmedTitle} ({trimmedAgentId})";
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.TrimEnd();
}
