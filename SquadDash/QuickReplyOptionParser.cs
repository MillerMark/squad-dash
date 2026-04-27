using System.Text.Json;
using System.Text.RegularExpressions;

namespace SquadDash;

internal sealed record QuickReplyOptionMetadata(
    string Label,
    string? RouteMode = null,
    string? TargetAgent = null,
    string? Reason = null);

internal static partial class QuickReplyOptionParser {
    [GeneratedRegex(
        @"(?s)^(?<body>.*?)(?:\n|^)\s*(?<options>(?:(?:\*\*)?\[[^\[\]\r\n]+\](?:\*\*)?\s*){2,})\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex QuickReplySegmentRegex();

    [GeneratedRegex(@"(?:\*\*)?\[(?<option>[^\[\]\r\n]+)\](?:\*\*)?", RegexOptions.CultureInvariant)]
    private static partial Regex QuickReplyOptionRegex();

    [GeneratedRegex(
        @"(?s)^(?<body>.*?)(?:\n|^)\s*QUICK_REPLIES_JSON:\s*(?<json>\[[\s\S]*\])\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex QuickReplyJsonRegex();

    internal static bool TryExtract(
        string text,
        out string body,
        out string[] options) {
        var parsed = TryExtractWithMetadata(text, out body, out var metadataOptions);
        options = metadataOptions
            .Select(option => option.Label)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .ToArray();
        return parsed;
    }

    internal static bool TryExtractWithMetadata(
        string text,
        out string body,
        out QuickReplyOptionMetadata[] options) {
        body = text;
        options = Array.Empty<QuickReplyOptionMetadata>();

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');

        if (TryExtractJson(normalized, out body, out options))
            return true;

        // Strip any trailing incomplete bracket (e.g. "[Han Solo] [M" truncated mid-option)
        var stripped = StripTrailingPartialOption(normalized);

        var match = QuickReplySegmentRegex().Match(stripped);
        if (!match.Success)
            return false;

        options = QuickReplyOptionRegex()
            .Matches(match.Groups["options"].Value)
            .Select(candidate => candidate.Groups["option"].Value.Trim())
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Distinct(StringComparer.Ordinal)
            .Select(option => new QuickReplyOptionMetadata(option))
            .ToArray();
        if (options.Length < 2)
            return false;

        body = match.Groups["body"].Value.TrimEnd();
        return true;
    }

    private static string StripTrailingPartialOption(string text) {
        var trimmed = text.TrimEnd();
        // If the text ends with an unclosed '[', remove everything from that '[' onward
        var lastOpen = trimmed.LastIndexOf('[');
        if (lastOpen < 0)
            return text;
        var lastClose = trimmed.LastIndexOf(']');
        if (lastClose > lastOpen)
            return text; // last '[' is closed — no truncation
        return trimmed[..lastOpen].TrimEnd();
    }

    private static bool TryExtractJson(
        string text,
        out string body,
        out QuickReplyOptionMetadata[] options) {
        body = text;
        options = Array.Empty<QuickReplyOptionMetadata>();

        var match = QuickReplyJsonRegex().Match(text);
        if (!match.Success)
            return false;

        try {
            using var document = JsonDocument.Parse(match.Groups["json"].Value);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            options = document.RootElement
                .EnumerateArray()
                .Where(static element => element.ValueKind == JsonValueKind.Object)
                .Select(ParseOptionMetadata)
                .Where(static option => option is not null)
                .Cast<QuickReplyOptionMetadata>()
                .DistinctBy(static option => option.Label, StringComparer.Ordinal)
                .ToArray();
            if (options.Length == 0)
                return false;

            body = match.Groups["body"].Value.TrimEnd();
            return true;
        }
        catch (JsonException) {
            return false;
        }
    }

    private static QuickReplyOptionMetadata? ParseOptionMetadata(JsonElement element) {
        var label = TryGetString(element, "label");
        if (string.IsNullOrWhiteSpace(label))
            return null;

        return new QuickReplyOptionMetadata(
            label.Trim(),
            TryGetString(element, "routeMode"),
            TryGetString(element, "targetAgent"),
            TryGetString(element, "reason"));
    }

    private static string? TryGetString(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String) {
            return null;
        }

        return property.GetString();
    }
}
