using System.Text;
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
        @"(?<=[A-Za-z]{4,})\s+(?=(?:ize|ized|ization|ise|ised|ises|ing|ed|er|ers|ly|ment|ments|ation|ations|tion|tions|able|ible|ality|ities|ity)\b)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex SpaceBeforePunctRegex = new(
        @"\s+([,.;:!?%\)\]\}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SpaceAfterOpenRegex = new(
        @"([\(\[\{])\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static string SanitizeResponseText(string? text) =>
        RepairFusedProseBoundaries(StripHostOwnedJsonBlocks(StripTasksJsonBlock(StripInboxMessageBlock(StripInboxMessageFileBlock(StripHostCommandBlock(StripApprovalGroupBlock(StripAwaitInputSentinel(ToolTranscriptFormatter.StripSystemNotifications(text))))))))).TrimEnd();

    internal static string? SanitizeResponseTextOrNull(string? text)
    {
        var sanitized = SanitizeResponseText(text);
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    internal static string GetSanitizedTurnResponseText(TranscriptTurnView? turn)
    {
        if (turn is null)
            return string.Empty;

        var responseSegments = turn.ResponseEntries
            .Select(entry => SanitizeResponseText(entry.RawTextBuilder.ToString()).TrimEnd())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        if (responseSegments.Length > 0)
            return string.Join("\n\n", responseSegments);

        return SanitizeResponseText(turn.ResponseTextBuilder.ToString());
    }

    internal static void EnsureResponseParagraphBreak(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Length == 0)
            return;

        var lineFeedCount = CountTrailingLineFeeds(builder);
        if (lineFeedCount >= 2)
            return;

        builder.Append(lineFeedCount == 1 ? "\n" : "\n\n");
    }

    internal static string RepairFusedProseBoundaries(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        StringBuilder? builder = null;
        var inFence = false;
        var inlineCodeTicks = 0;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '`')
            {
                var tickCount = CountBacktickRun(text, index);
                if (tickCount >= 3 && IsFenceDelimiterAt(text, index))
                {
                    AppendRange(ref builder, text, index, tickCount);
                    inFence = !inFence;
                    index += tickCount - 1;
                    continue;
                }

                if (!inFence)
                    inlineCodeTicks = inlineCodeTicks == tickCount ? 0 : inlineCodeTicks == 0 ? tickCount : inlineCodeTicks;

                AppendRange(ref builder, text, index, tickCount);
                index += tickCount - 1;
                continue;
            }

            builder?.Append(text[index]);

            if (inFence || inlineCodeTicks > 0 || !IsFusionPunctuation(text[index]))
                continue;

            if (!ShouldRepairFusedBoundary(text, index))
                continue;

            builder ??= new StringBuilder(text.Length + 8).Append(text, 0, index + 1);
            builder.Append(' ');
        }

        return builder?.ToString() ?? text;
    }

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
        // Strip any top-level APPROVAL_GROUP_JSON block.
        // The AgentThreadRegistry parses these from the raw response before sanitization, so
        // stripping here only affects display; parsing is unaffected.
        var sentinelIdx = FindTopLevelSentinelIndex(text, "APPROVAL_GROUP_JSON:");
        if (sentinelIdx >= 0)
        {
            // Approval metadata is not necessarily the final machine-readable footer. Preserve
            // a following quick-reply block so the renderer can still turn it into buttons.
            var quickRepliesIdx = FindTopLevelSentinelIndex(text, "QUICK_REPLIES_JSON:");
            if (quickRepliesIdx > sentinelIdx)
                return $"{text[..sentinelIdx].TrimEnd()}\n\n{text[quickRepliesIdx..].TrimStart()}";

            return text[..sentinelIdx].TrimEnd();
        }
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
        var sentinelIdx = FindTopLevelSentinelIndex(text, sentinel);
        if (sentinelIdx >= 0)
            return text[..sentinelIdx].TrimEnd();

        return text;
    }

    private static string StripInboxMessageFileBlock(string text)
    {
        const string sentinel = AgentArtifactStore.InboxMessageFileMarker;

        if (InboxMessageFileReferenceParser.TryExtract(text, out var extraction) &&
            extraction is not null)
            return extraction.VisibleText;

        var sentinelIdx = FindTopLevelSentinelIndex(text, sentinel);
        if (sentinelIdx >= 0)
            return text[..sentinelIdx].TrimEnd();

        return text;
    }

    private static string StripTasksJsonBlock(string text)
        => StripTopLevelJsonBlock(text, "TASKS_JSON:");

    private static string StripHostOwnedJsonBlocks(string text)
    {
        foreach (var marker in new[]
                 {
                     DecomposeStepResultParser.Marker,
                     DecomposeRecoveryDecisionParser.Marker,
                     PlanValidationResultParser.Marker,
                     "DECOMPOSE_DECISION_JSON:",
                 })
            text = StripTopLevelJsonBlock(text, marker);
        return text;
    }

    private static string StripTopLevelJsonBlock(string text, string sentinel)
    {
        var sentinelIdx = FindTopLevelSentinelIndex(text, sentinel);
        if (sentinelIdx < 0)
            return text;

        // Hide an incomplete payload while it streams. Once the object closes, preserve any
        // accidental visible prose that follows it and consume an optional Markdown fence.
        var braceStart = text.IndexOf('{', sentinelIdx + sentinel.Length);
        if (braceStart < 0 || !TryFindBalancedJsonObjectEnd(text, braceStart, out var braceEnd))
            return text[..sentinelIdx].TrimEnd();

        var payloadEnd = braceEnd + 1;
        var cursor = payloadEnd;
        while (cursor < text.Length && text[cursor] is ' ' or '\t' or '\r' or '\n')
            cursor++;

        var betweenMarkerAndJson = text[(sentinelIdx + sentinel.Length)..braceStart];
        var hasOpeningFence = betweenMarkerAndJson
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.TrimStart().StartsWith("```", StringComparison.Ordinal));
        if (hasOpeningFence && cursor < text.Length &&
            text.AsSpan(cursor).StartsWith("```".AsSpan(), StringComparison.Ordinal))
        {
            var fenceLineEnd = text.IndexOf('\n', cursor);
            payloadEnd = fenceLineEnd < 0 ? text.Length : fenceLineEnd + 1;
        }

        var before = text[..sentinelIdx].TrimEnd();
        var after = text[payloadEnd..].TrimStart();
        if (before.Length == 0) return after;
        if (after.Length == 0) return before;
        return before + "\n\n" + after;
    }

    private static bool TryFindBalancedJsonObjectEnd(string text, int braceStart, out int braceEnd)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = braceStart; index < text.Length; index++)
        {
            var ch = text[index];
            if (escaped) { escaped = false; continue; }
            if (ch == '\\' && inString) { escaped = true; continue; }
            if (ch == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (ch == '{') depth++;
            else if (ch == '}' && --depth == 0)
            {
                braceEnd = index;
                return true;
            }
        }

        braceEnd = -1;
        return false;
    }

    private static int FindTopLevelSentinelIndex(string text, string sentinel)
    {
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

    private static bool ShouldRepairFusedBoundary(string text, int punctuationIndex)
    {
        if (punctuationIndex <= 0 || punctuationIndex >= text.Length - 1)
            return false;

        var punctuation = text[punctuationIndex];
        var previous = text[punctuationIndex - 1];
        var next = text[punctuationIndex + 1];

        if (char.IsWhiteSpace(next))
            return false;

        if (!char.IsLetterOrDigit(previous) && previous is not ')' and not ']' and not '}' and not '"' and not '\'' and not '`')
            return false;

        if ((punctuation == '.' || punctuation == ':') &&
            (char.IsDigit(previous) || char.IsDigit(next)))
            return false;

        if (punctuation == '.' && (previous == '.' || next == '.'))
            return false;

        if (punctuation == ':' && (next == '/' || next == '\\'))
            return false;

        if (IsInsideUrlToken(text, punctuationIndex))
            return false;

        var nextLetter = FindNextBoundaryLetter(text, punctuationIndex + 1);
        if (nextLetter is null)
            return false;

        return char.IsUpper(nextLetter.Value);
    }

    private static bool IsInsideUrlToken(string text, int index)
    {
        var tokenStart = index;
        while (tokenStart > 0 && !char.IsWhiteSpace(text[tokenStart - 1]))
            tokenStart--;

        var tokenEnd = index + 1;
        while (tokenEnd < text.Length && !char.IsWhiteSpace(text[tokenEnd]))
            tokenEnd++;

        var token = text.AsSpan(tokenStart, tokenEnd - tokenStart);
        return token.Contains("://".AsSpan(), StringComparison.Ordinal);
    }

    private static char? FindNextBoundaryLetter(string text, int start)
    {
        for (var index = start; index < text.Length; index++)
        {
            var ch = text[index];
            if (ch is '*' or '_' or '~' or '`')
                continue;

            return char.IsLetter(ch) ? ch : null;
        }

        return null;
    }

    private static bool IsFusionPunctuation(char ch) =>
        ch is '.' or ':' or '!' or '?';

    private static int CountBacktickRun(string text, int start)
    {
        var index = start;
        while (index < text.Length && text[index] == '`')
            index++;

        return index - start;
    }

    private static bool IsFenceDelimiterAt(string text, int index)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        for (var cursor = lineStart; cursor < index; cursor++)
            if (!char.IsWhiteSpace(text[cursor]))
                return false;

        return true;
    }

    private static void AppendRange(ref StringBuilder? builder, string text, int start, int length)
    {
        if (builder is null)
            return;

        builder.Append(text, start, length);
    }

    private static int CountTrailingLineFeeds(StringBuilder builder)
    {
        var count = 0;
        for (var index = builder.Length - 1; index >= 0; index--)
        {
            var ch = builder[index];
            if (ch == '\n')
            {
                count++;
                continue;
            }

            if (ch == '\r')
                continue;

            break;
        }

        return count;
    }

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
