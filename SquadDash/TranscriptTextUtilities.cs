using System.Text.Json;
using System.Text.RegularExpressions;

namespace SquadDash;

internal static class TranscriptTextUtilities
{
    private static readonly Regex WhitespaceNormRegex = new(
        @"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WordApostropheRegex = new(
        @"(?<=\w)\s+'(?=\w)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SuffixSplitRegex = new(
        @"(?<=[A-Za-z]{4,})\s+(?=(?:ize|ized|ization|ise|ised|ises|ing|ed|er|ers|ly|ment|ments|tion|tions|able|ible|ality|ality|ities|ity)\b)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex SpaceBeforePunctRegex = new(
        @"\s+([,.;:!?%\)\]\}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SpaceAfterOpenRegex = new(
        @"([\(\[\{])\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static string SanitizeResponseText(string? text) =>
        StripApprovalGroupBlock(StripInboxMessageBlock(StripHostCommandBlock(StripAwaitInputSentinel(ToolTranscriptFormatter.StripSystemNotifications(text))))).TrimEnd();

    internal static string? SanitizeResponseTextOrNull(string? text)
    {
        var sanitized = SanitizeResponseText(text);
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    internal static string GetSanitizedTurnResponseText(TranscriptTurnView? turn) =>
        SanitizeResponseText(turn?.ResponseTextBuilder.ToString());

    internal static string FormatThinkingText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        normalized = WhitespaceNormRegex.Replace(normalized, " ").Trim();
        normalized = WordApostropheRegex.Replace(normalized, "'");
        normalized = SuffixSplitRegex.Replace(normalized, string.Empty);
        normalized = SpaceBeforePunctRegex.Replace(normalized, "$1");
        normalized = SpaceAfterOpenRegex.Replace(normalized, "$1");
        return normalized;
    }

    internal static string BuildThreadPreview(string text)
    {
        var collapsed = CollapseWhitespace(RemoveQuickReplySuffix(SanitizeResponseText(text)));
        if (collapsed.Length <= 120)
            return collapsed;

        return collapsed[..117] + "...";
    }

    internal static string MergeStreamingAndFinalResponse(
        string? streamingResponse,
        string? finalResponse,
        out string? tailToAppend)
    {
        tailToAppend = null;

        var current = streamingResponse ?? string.Empty;
        if (string.IsNullOrWhiteSpace(finalResponse))
            return current;

        var final = finalResponse!;
        if (string.IsNullOrEmpty(current))
        {
            tailToAppend = final;
            return final;
        }

        if (string.Equals(current, final, StringComparison.Ordinal))
            return current;

        if (final.StartsWith(current, StringComparison.Ordinal))
        {
            tailToAppend = final[current.Length..];
            return final;
        }

        var trimmedCurrent = current.TrimEnd();
        if (trimmedCurrent.Length > 0 &&
            final.StartsWith(trimmedCurrent, StringComparison.Ordinal))
        {
            tailToAppend = final[trimmedCurrent.Length..];
            return final;
        }

        if (current.StartsWith(final, StringComparison.Ordinal))
            return current;

        var overlap = FindSuffixPrefixOverlap(current, final);
        if (overlap >= Math.Min(64, current.Length / 2) &&
            overlap < final.Length)
        {
            tailToAppend = final[overlap..];
            return current + tailToAppend;
        }

        return final.Length > current.Length ? final : current;
    }

    internal static string BuildTimedStatusText(
        string? statusText,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        DateTimeOffset now)
    {
        var status = AgentThreadRegistry.HumanizeThreadStatus(statusText);
        if (string.IsNullOrWhiteSpace(status))
            status = completedAt is null ? "Running" : "Completed";

        var effectiveStartedAt = startedAt ?? completedAt ?? now;
        return StatusTimingPresentation.BuildStatus(status, effectiveStartedAt, completedAt, now);
    }

    internal static string CollapseWhitespace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    internal static string FormatJson(JsonElement element)
    {
        return JsonSerializer.Serialize(element, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static string StripApprovalGroupBlock(string text)
    {
        // Strip any top-level APPROVAL_GROUP_JSON block (label + JSON object on following line).
        // The AgentThreadRegistry parses these from the raw response before sanitization, so
        // stripping here only affects display; parsing is unaffected.
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var match = Regex.Match(normalized,
            @"(?s)^(?<body>.*?)(?:\n|^)\s*APPROVAL_GROUP_JSON:\s*\{[^\}]*\}\s*$",
            RegexOptions.CultureInvariant);
        if (match.Success)
            return match.Groups["body"].Value.TrimEnd();
        return text;
    }

    private static string StripHostCommandBlock(string text)
    {
        if (HostCommandParser.TryExtract(text, out var body, out _))
            return body;
        return text;
    }

    private static string StripInboxMessageBlock(string text)
    {
        // Do not strip when the last occurrence of the sentinel is inside a backtick inline
        // code span (i.e. the same line has a backtick before the sentinel).  A real
        // INBOX_MESSAGE_JSON block always starts on a bare line — top-level or inside a code
        // fence — never immediately after a backtick character.
        const string sentinel = "INBOX_MESSAGE_JSON:";
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        int lastMarkerIdx = normalized.LastIndexOf(sentinel, StringComparison.Ordinal);
        if (lastMarkerIdx >= 0)
        {
            int prevNewline = lastMarkerIdx > 0
                ? normalized.LastIndexOf('\n', lastMarkerIdx - 1)
                : -1;
            int lineStart = prevNewline < 0 ? 0 : prevNewline + 1;
            if (normalized[lineStart..lastMarkerIdx].Contains('`'))
                return text;
        }

        // Strip complete block (already handled by parser).
        if (InboxMessageParser.TryExtract(text, out var body, out _))
            return body;

        // Strip partial block (still streaming), but only when the sentinel is on its
        // own top-level line. Inline references and code-fenced examples must remain visible.
        var sentinelIdx = FindTopLevelInboxSentinelIndex(text);
        if (sentinelIdx >= 0)
            return text[..sentinelIdx].TrimEnd();

        return text;
    }

    private static int FindTopLevelInboxSentinelIndex(string text)
    {
        const string sentinel = "INBOX_MESSAGE_JSON:";

        var inFence = false;
        var offset  = 0;

        while (offset < text.Length)
        {
            var lineEnd = text.IndexOf('\n', offset);
            if (lineEnd < 0)
                lineEnd = text.Length;

            var lineLength = lineEnd - offset;
            if (lineLength > 0 && text[offset + lineLength - 1] == '\r')
                lineLength--;

            var line = text.Substring(offset, lineLength);
            var leadingWhitespace = line.Length - line.TrimStart().Length;
            var trimmed = line[leadingWhitespace..];

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
                inFence = !inFence;
            else if (!inFence && trimmed.StartsWith(sentinel, StringComparison.Ordinal))
                return offset + leadingWhitespace;

            offset = lineEnd == text.Length ? text.Length : lineEnd + 1;
        }

        return -1;
    }

    private static string StripAwaitInputSentinel(string text) =>
        text.Replace(PromptExecutionController.QueueAwaitInputSentinel, string.Empty,
                     StringComparison.Ordinal);

    private static string RemoveQuickReplySuffix(string text) =>
        QuickReplyOptionParser.TryExtract(text, out var body, out _) ? body : text;

    private static int FindSuffixPrefixOverlap(string left, string right)
    {
        var max = Math.Min(left.Length, right.Length);
        for (var length = max; length > 0; length--)
            if (left.AsSpan(left.Length - length, length)
                    .SequenceEqual(right.AsSpan(0, length)))
                return length;

        return 0;
    }
}
