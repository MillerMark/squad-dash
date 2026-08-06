using System.Text.Json;

namespace SquadDash;

internal sealed record StructuredJsonBlockExtraction<T>(
    T Payload,
    string JsonText,
    string VisibleText,
    string TextBeforeBlock,
    string TrailingText,
    int MarkerIndex,
    int JsonStartIndex,
    int JsonEndIndex);

internal static class StructuredJsonBlockParser
{
    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    internal static bool TryExtractObject<T>(
        string? text,
        string marker,
        out StructuredJsonBlockExtraction<T>? extraction)
    {
        extraction = null;

        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(marker))
            return false;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var markerIdx = normalized.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIdx < 0)
            return false;

        var braceStart = normalized.IndexOf('{', markerIdx + marker.Length);
        if (braceStart < 0 || !TryFindJsonObjectEnd(normalized, braceStart, out var braceEnd))
            return false;

        var jsonText = normalized[braceStart..(braceEnd + 1)];
        try
        {
            var payload = JsonSerializer.Deserialize<T>(jsonText, ParseOptions);
            if (payload is null)
                return false;

            var before = StripTrailingCodeFence(normalized[..markerIdx].TrimEnd());
            var after = StripLeadingCodeFence(normalized[(braceEnd + 1)..]).Trim();
            extraction = new StructuredJsonBlockExtraction<T>(
                payload,
                jsonText,
                CombineVisibleText(before, after),
                before,
                after,
                markerIdx,
                braceStart,
                braceEnd);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Accepts a response whose entire meaningful content is one JSON object, optionally wrapped
    /// in a Markdown code fence. This is intentionally narrower than searching arbitrary prose so
    /// protocol parsers remain deterministic when a model omits only the requested marker.
    /// </summary>
    internal static bool TryExtractSingleObject<T>(
        string? text,
        out StructuredJsonBlockExtraction<T>? extraction)
    {
        extraction = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = normalized.IndexOf('\n');
            if (firstLineEnd < 0) return false;
            normalized = normalized[(firstLineEnd + 1)..].Trim();
            if (normalized.EndsWith("```", StringComparison.Ordinal))
                normalized = normalized[..^3].TrimEnd();
        }

        if (!normalized.StartsWith('{') || !TryFindJsonObjectEnd(normalized, 0, out var braceEnd) ||
            !string.IsNullOrWhiteSpace(normalized[(braceEnd + 1)..]))
            return false;

        var jsonText = normalized[..(braceEnd + 1)];
        try
        {
            var payload = JsonSerializer.Deserialize<T>(jsonText, ParseOptions);
            if (payload is null) return false;
            extraction = new StructuredJsonBlockExtraction<T>(
                payload, jsonText, string.Empty, string.Empty, string.Empty, -1, 0, braceEnd);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryFindJsonObjectEnd(string text, int braceStart, out int braceEnd)
    {
        braceEnd = -1;
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = braceStart; i < text.Length; i++)
        {
            var c = text[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inString)
            {
                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                    inString = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
            {
                depth++;
                continue;
            }

            if (c != '}')
                continue;

            depth--;
            if (depth == 0)
            {
                braceEnd = i;
                return true;
            }

            if (depth < 0)
                return false;
        }

        return false;
    }

    private static string CombineVisibleText(string before, string after)
    {
        if (string.IsNullOrWhiteSpace(before))
            return after.Trim();
        if (string.IsNullOrWhiteSpace(after))
            return before.TrimEnd();
        return before.TrimEnd() + "\n\n" + after.Trim();
    }

    private static string StripTrailingCodeFence(string text)
    {
        if (text.Length == 0)
            return text;

        var lastNewline = text.LastIndexOf('\n');
        var lastLine = lastNewline < 0 ? text : text[(lastNewline + 1)..];
        if (lastLine.TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            var before = lastNewline < 0 ? string.Empty : text[..lastNewline];
            return before.TrimEnd();
        }

        return text;
    }

    private static string StripLeadingCodeFence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return text;

        var lineEnd = trimmed.IndexOf('\n');
        return lineEnd < 0 ? string.Empty : trimmed[(lineEnd + 1)..];
    }
}
