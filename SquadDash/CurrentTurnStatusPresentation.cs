namespace SquadDash;

internal sealed record CurrentTurnStatusSnapshot(
    bool IsRunning,
    bool NoActivityWarningShown,
    bool StallWarningShown,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? LastActivityAt = null,
    string? LastActivityName = null,
    DateTimeOffset? SessionReadyAt = null,
    DateTimeOffset? FirstToolAt = null,
    DateTimeOffset? FirstResponseAt = null,
    DateTimeOffset? LastResponseAt = null,
    int ResponseDeltaCount = 0,
    int ResponseCharacterCount = 0,
    TimeSpan? LongestResponseGap = null,
    TimeSpan? AverageResponseGap = null,
    DateTimeOffset? FirstThinkingTextAt = null,
    DateTimeOffset? LastThinkingTextAt = null,
    int ThinkingDeltaCount = 0,
    int ThinkingTextDeltaCount = 0,
    int ThinkingCharacterCount = 0,
    TimeSpan? LongestThinkingGap = null,
    TimeSpan? AverageThinkingGap = null,
    int ToolStartCount = 0,
    int ToolCompleteCount = 0);

internal static class CurrentTurnStatusPresentation {
    private const double SlowUpstreamAverageChunkCharThreshold = 12;
    private static readonly TimeSpan SlowUpstreamAverageGapThreshold = TimeSpan.FromMilliseconds(800);
    private const int ThinkingLoopThreshold = 8;
    private const int ToolLoopThreshold = 3;

    public static string? BuildReportLine(CurrentTurnStatusSnapshot snapshot, DateTimeOffset? now = null) {
        if (!snapshot.IsRunning)
            return null;

        var effectiveNow = now ?? DateTimeOffset.Now;
        var status = snapshot.StallWarningShown || snapshot.NoActivityWarningShown
            ? "Waiting"
            : "Working";
        var decoratedStatus = snapshot.StartedAt is { } startedAt
            ? StatusTimingPresentation.BuildStatus(status, startedAt, completedAt: null, effectiveNow)
            : status;

        return $"Coordinator [{decoratedStatus}] - {BuildDetail(snapshot, effectiveNow)}";
    }

    private static string BuildDetail(CurrentTurnStatusSnapshot snapshot, DateTimeOffset now) {
        if (snapshot.LastResponseAt is { } lastResponseAt) {
            var prefix = BuildStreamingPrefix(snapshot, now, lastResponseAt);

            var metrics = BuildStreamingMetrics(snapshot);
            var thinkingMetrics = BuildThinkingMetrics(snapshot, onlyWhenCurrent: true);
            if (!string.IsNullOrWhiteSpace(thinkingMetrics))
                metrics = string.IsNullOrWhiteSpace(metrics) ? thinkingMetrics : $"{metrics} {thinkingMetrics}";
            return string.IsNullOrWhiteSpace(metrics)
                ? prefix
                : $"{prefix} {metrics}";
        }

        if (snapshot.SessionReadyAt is { } sessionReadyAt &&
            snapshot.StartedAt is { } startedAt) {
            var detail = $"Session ready in {StatusTimingPresentation.FormatDuration(sessionReadyAt - startedAt)}, but no response text has arrived yet.";
            if (snapshot.FirstToolAt is { } firstToolAt)
                detail += $" First tool activity started {StatusTimingPresentation.FormatDuration(firstToolAt - startedAt)} after launch.";
            detail += BuildPreResponseLoopSummary(snapshot);
            detail += BuildThinkingMetricsSuffix(snapshot);
            return detail;
        }

        if (snapshot.FirstToolAt is { } toolAt &&
            snapshot.StartedAt is { } promptStartedAt) {
            return $"Tool work started {StatusTimingPresentation.FormatDuration(toolAt - promptStartedAt)} after launch, but no response text has arrived yet.{BuildPreResponseLoopSummary(snapshot)}{BuildThinkingMetricsSuffix(snapshot)}";
        }

        return snapshot.StallWarningShown
            ? "Still responding, but no new output has arrived for a while."
            : "Still responding to the current prompt." + BuildPreResponseLoopSummary(snapshot) + BuildThinkingMetricsSuffix(snapshot);
    }

    private static string BuildStreamingMetrics(CurrentTurnStatusSnapshot snapshot) {
        if (snapshot.FirstResponseAt is null || snapshot.ResponseDeltaCount <= 0)
            return string.Empty;

        var metrics = $"First text in {FormatFromStart(snapshot.StartedAt, snapshot.FirstResponseAt)}; {snapshot.ResponseDeltaCount} {Pluralize("chunk", snapshot.ResponseDeltaCount)} / {snapshot.ResponseCharacterCount} {Pluralize("char", snapshot.ResponseCharacterCount)} so far.";
        if (snapshot.AverageResponseGap is { } averageGap && averageGap >= SlowUpstreamAverageGapThreshold)
            metrics += $" Average chunk gap {FormatCadenceDuration(averageGap)}.";

        var averageChunkSize = CalculateAverageChunkSize(snapshot);
        if (averageChunkSize is not null &&
            (averageChunkSize <= SlowUpstreamAverageChunkCharThreshold || snapshot.AverageResponseGap is { } interestingGap && interestingGap >= SlowUpstreamAverageGapThreshold)) {
            metrics += $" Average chunk size {averageChunkSize:0.#} chars.";
        }

        if (snapshot.LongestResponseGap is { } longestGap && longestGap >= TimeSpan.FromSeconds(3))
            metrics += $" Longest chunk gap {StatusTimingPresentation.FormatDuration(longestGap)}.";

        if (BuildStreamingThroughput(snapshot) is { } throughput)
            metrics += $" {throughput}";

        return metrics;
    }

    private static string BuildStreamingPrefix(CurrentTurnStatusSnapshot snapshot, DateTimeOffset now, DateTimeOffset lastResponseAt) {
        if (snapshot.StallWarningShown)
            return $"Last response chunk {StatusTimingPresentation.FormatAgo(now - lastResponseAt)}.";

        if (snapshot.NoActivityWarningShown)
            return $"Response has gone quiet for {StatusTimingPresentation.FormatDuration(now - lastResponseAt)}.";

        if (LooksLikeSlowUpstreamTokenArrival(snapshot))
            return "Upstream response text is arriving slowly.";

        return "Still responding to the current prompt.";
    }

    private static bool LooksLikeSlowUpstreamTokenArrival(CurrentTurnStatusSnapshot snapshot) {
        if (snapshot.ResponseDeltaCount < 6 || snapshot.AverageResponseGap is not { } averageGap)
            return false;

        var averageChunkSize = CalculateAverageChunkSize(snapshot);
        if (averageChunkSize is null)
            return false;

        return averageGap >= SlowUpstreamAverageGapThreshold &&
               averageChunkSize <= SlowUpstreamAverageChunkCharThreshold;
    }

    private static string BuildPreResponseLoopSummary(CurrentTurnStatusSnapshot snapshot) {
        if (snapshot.ThinkingDeltaCount >= ThinkingLoopThreshold && snapshot.ToolCompleteCount >= ToolLoopThreshold)
            return $" Model is still in a thinking/tool loop: {snapshot.ThinkingDeltaCount} thinking updates, {snapshot.ToolCompleteCount} completed {Pluralize("tool", snapshot.ToolCompleteCount)} so far.";

        if (snapshot.ThinkingDeltaCount >= ThinkingLoopThreshold && snapshot.ToolStartCount >= ToolLoopThreshold)
            return $" Model is still in a thinking/tool loop: {snapshot.ThinkingDeltaCount} thinking updates, {snapshot.ToolStartCount} started {Pluralize("tool", snapshot.ToolStartCount)} so far.";

        if (snapshot.ThinkingDeltaCount >= ThinkingLoopThreshold)
            return $" Model is still thinking internally: {snapshot.ThinkingDeltaCount} thinking updates so far.";

        if (snapshot.ToolCompleteCount >= ToolLoopThreshold)
            return $" Tool work is active: {snapshot.ToolCompleteCount} completed {Pluralize("tool", snapshot.ToolCompleteCount)} so far.";

        if (snapshot.ToolStartCount >= ToolLoopThreshold)
            return $" Tool work is active: {snapshot.ToolStartCount} started {Pluralize("tool", snapshot.ToolStartCount)} so far.";

        return string.Empty;
    }

    private static string BuildThinkingMetricsSuffix(CurrentTurnStatusSnapshot snapshot) {
        var thinkingMetrics = BuildThinkingMetrics(snapshot, onlyWhenCurrent: false);
        return string.IsNullOrWhiteSpace(thinkingMetrics)
            ? string.Empty
            : $" {thinkingMetrics}";
    }

    private static string FormatFromStart(DateTimeOffset? startedAt, DateTimeOffset? milestoneAt) {
        if (startedAt is null || milestoneAt is null)
            return "unknown";

        return StatusTimingPresentation.FormatDuration(milestoneAt.Value - startedAt.Value);
    }

    private static double? CalculateAverageChunkSize(CurrentTurnStatusSnapshot snapshot) {
        if (snapshot.ResponseDeltaCount <= 0)
            return null;

        return (double)snapshot.ResponseCharacterCount / snapshot.ResponseDeltaCount;
    }

    private static string? BuildStreamingThroughput(CurrentTurnStatusSnapshot snapshot) {
        if (snapshot.FirstResponseAt is not { } firstResponseAt ||
            snapshot.LastResponseAt is not { } lastResponseAt ||
            snapshot.ResponseDeltaCount < 2 ||
            snapshot.ResponseCharacterCount <= 0) {
            return null;
        }

        var responseWindow = lastResponseAt - firstResponseAt;
        if (responseWindow <= TimeSpan.Zero)
            return null;

        var charsPerSecond = snapshot.ResponseCharacterCount / responseWindow.TotalSeconds;
        var estimatedTokensPerSecond = charsPerSecond / 4.0;
        return $"Throughput {charsPerSecond:0.0} chars/s (~{estimatedTokensPerSecond:0.0} tok/s est.).";
    }

    private static string Pluralize(string singular, int count) =>
        count == 1 ? singular : singular + "s";

    private static string? BuildThinkingMetrics(CurrentTurnStatusSnapshot snapshot, bool onlyWhenCurrent) {
        if (snapshot.FirstThinkingTextAt is not { } firstThinkingTextAt ||
            snapshot.LastThinkingTextAt is not { } lastThinkingTextAt ||
            snapshot.ThinkingTextDeltaCount <= 0 ||
            snapshot.ThinkingCharacterCount <= 0) {
            return null;
        }

        if (onlyWhenCurrent && snapshot.LastResponseAt is { } lastResponseAt && lastThinkingTextAt < lastResponseAt)
            return null;

        var metrics = $"Thought stream {snapshot.ThinkingCharacterCount} chars / {snapshot.ThinkingTextDeltaCount} {Pluralize("chunk", snapshot.ThinkingTextDeltaCount)}.";
        if (BuildThinkingThroughput(snapshot, firstThinkingTextAt, lastThinkingTextAt) is { } throughput)
            metrics += $" {throughput}";

        if (snapshot.AverageThinkingGap is { } averageThinkingGap && averageThinkingGap >= TimeSpan.FromMilliseconds(800))
            metrics += $" Average thought gap {FormatCadenceDuration(averageThinkingGap)}.";

        if (snapshot.LongestThinkingGap is { } longestThinkingGap && longestThinkingGap >= TimeSpan.FromSeconds(3))
            metrics += $" Longest thought gap {StatusTimingPresentation.FormatDuration(longestThinkingGap)}.";

        return metrics;
    }

    private static string? BuildThinkingThroughput(
        CurrentTurnStatusSnapshot snapshot,
        DateTimeOffset firstThinkingTextAt,
        DateTimeOffset lastThinkingTextAt) {
        if (snapshot.ThinkingTextDeltaCount < 2)
            return null;

        var thinkingWindow = lastThinkingTextAt - firstThinkingTextAt;
        if (thinkingWindow <= TimeSpan.Zero)
            return null;

        var charsPerSecond = snapshot.ThinkingCharacterCount / thinkingWindow.TotalSeconds;
        var estimatedTokensPerSecond = charsPerSecond / 4.0;
        return $"Thinking throughput {charsPerSecond:0.0} chars/s (~{estimatedTokensPerSecond:0.0} tok/s est.).";
    }

    private static string FormatCadenceDuration(TimeSpan duration) {
        if (duration < TimeSpan.FromSeconds(1))
            return $"{Math.Round(duration.TotalMilliseconds):0}ms";

        return StatusTimingPresentation.FormatDuration(duration);
    }
}
