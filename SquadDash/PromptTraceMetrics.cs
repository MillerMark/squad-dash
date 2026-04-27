using System;

namespace SquadDash;

internal static class PromptTraceMetrics {
    internal static string FormatCharsPerSecond(int characterCount, DateTimeOffset? firstAt, DateTimeOffset? lastAt) {
        if (characterCount <= 0 || firstAt is not { } first || lastAt is not { } last)
            return "n/a";

        var seconds = (last - first).TotalSeconds;
        if (seconds <= 0)
            return "n/a";

        return (characterCount / seconds).ToString("0.0");
    }

    internal static string FormatAverageChunkSize(int characterCount, int chunkCount) =>
        characterCount > 0 && chunkCount > 0
            ? (characterCount / (double)chunkCount).ToString("0.0")
            : "n/a";
}
