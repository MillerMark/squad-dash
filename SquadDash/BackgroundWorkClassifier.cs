using System;
using System.Linq;

namespace SquadDash;

internal static class BackgroundWorkClassifier {
    public static bool IsPlanningWork(
        string? prompt,
        string? latestResponse,
        string? latestIntent,
        string? detailText) {
        var searchText = BuildSearchText(prompt, latestResponse, latestIntent, detailText);
        if (string.IsNullOrWhiteSpace(searchText))
            return false;

        return searchText.Contains("plan saved", StringComparison.Ordinal) ||
               searchText.Contains("revised plan", StringComparison.Ordinal) ||
               searchText.Contains("revise the full plan", StringComparison.Ordinal) ||
               searchText.Contains("write revised plan", StringComparison.Ordinal) ||
               searchText.Contains("implementation plan", StringComparison.Ordinal) ||
               searchText.Contains("create a comprehensive plan", StringComparison.Ordinal) ||
               searchText.Contains("changes made to the plan", StringComparison.Ordinal) ||
               searchText.Contains("plan update", StringComparison.Ordinal) ||
               searchText.Contains("planning work", StringComparison.Ordinal) ||
               (searchText.Contains("architectur", StringComparison.Ordinal) &&
                searchText.Contains("plan", StringComparison.Ordinal));
    }

    public static string BuildAnnouncementHeader(
        string label,
        bool isContinuation,
        string? prompt,
        string? latestResponse,
        string? latestIntent,
        string? detailText) {
        if (IsPlanningWork(prompt, latestResponse, latestIntent, detailText)) {
            return isContinuation
                ? $"{label} added more plan detail:"
                : $"{label} shared a plan update:";
        }

        return isContinuation
            ? $"{label} added more detail:"
            : $"{label} reported back:";
    }

    public static string BuildCompletionSummary(
        string label,
        string? prompt,
        string? latestResponse,
        string? latestIntent,
        string? detailText) {
        return IsPlanningWork(prompt, latestResponse, latestIntent, detailText)
            ? label + " finished the plan update."
            : label + " completed.";
    }

    private static string BuildSearchText(params string?[] values) {
        return string.Join(
                "\n",
                values.Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!.Trim()))
            .ToLowerInvariant();
    }
}
