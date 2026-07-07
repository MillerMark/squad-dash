using System.Text.Json;
using System.Text.RegularExpressions;

namespace SquadDash;

internal static partial class HostCommandParser {
    private const string Sentinel = "HOST_COMMAND_JSON:";

    [GeneratedRegex(@"""command""\s*:\s*""organize_approvals""", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OrganizeApprovalsCommandRegex();

    [GeneratedRegex(@"""assignments""\s*:\s*""\s*\[", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex QuotedAssignmentsArrayPrefixRegex();

    internal static bool TryExtract(
        string text,
        out string body,
        out HostCommandInvocation[] commands) {
        body = text;
        commands = Array.Empty<HostCommandInvocation>();

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var sentinelIdx = normalized.IndexOf(Sentinel, StringComparison.Ordinal);
        if (sentinelIdx < 0)
            return false;
        if (sentinelIdx != normalized.LastIndexOf(Sentinel, StringComparison.Ordinal))
            return false;

        var json = normalized[(sentinelIdx + Sentinel.Length)..].Trim();
        if (!json.StartsWith("[", StringComparison.Ordinal))
            return false;

        try {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            var parsed = document.RootElement
                .EnumerateArray()
                .Where(static e => e.ValueKind == JsonValueKind.Object)
                .Select(ParseInvocation)
                .Where(static inv => inv is not null)
                .Cast<HostCommandInvocation>()
                .ToArray();

            if (parsed.Length == 0)
                return false;

            commands = parsed;
            body = normalized[..sentinelIdx].TrimEnd();
            return true;
        }
        catch (JsonException) {
            if (TryParseMalformedOrganizeApprovals(json, out commands)) {
                body = normalized[..sentinelIdx].TrimEnd();
                return true;
            }
            return false;
        }
    }

    private static HostCommandInvocation? ParseInvocation(JsonElement element) {
        if (!element.TryGetProperty("command", out var commandProp) ||
            commandProp.ValueKind != JsonValueKind.String)
            return null;

        var command = commandProp.GetString();
        if (string.IsNullOrWhiteSpace(command))
            return null;

        IReadOnlyDictionary<string, string>? parameters = null;
        if (element.TryGetProperty("parameters", out var paramsProp) &&
            paramsProp.ValueKind == JsonValueKind.Object) {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var param in paramsProp.EnumerateObject()) {
                if (param.Value.ValueKind == JsonValueKind.String) {
                    var val = param.Value.GetString();
                    if (val is not null)
                        dict[param.Name] = val;
                }
                else if (param.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object or
                         JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) {
                    dict[param.Name] = param.Value.GetRawText();
                }
            }
            if (dict.Count > 0)
                parameters = dict;
        }

        return new HostCommandInvocation(command.Trim(), parameters);
    }

    private static bool TryParseMalformedOrganizeApprovals(string json, out HostCommandInvocation[] commands) {
        commands = Array.Empty<HostCommandInvocation>();

        if (!OrganizeApprovalsCommandRegex().IsMatch(json))
            return false;

        var prefixMatch = QuotedAssignmentsArrayPrefixRegex().Match(json);
        if (!prefixMatch.Success)
            return false;

        var arrayStart = json.IndexOf('[', prefixMatch.Index);
        if (arrayStart < 0 || !TryFindJsonArrayEnd(json, arrayStart, out var arrayEnd))
            return false;

        var assignmentsJson = json[arrayStart..(arrayEnd + 1)];
        try {
            using var document = JsonDocument.Parse(assignmentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return false;
        }
        catch (JsonException) {
            return false;
        }

        commands = [
            new HostCommandInvocation(
                "organize_approvals",
                new Dictionary<string, string>(StringComparer.Ordinal) {
                    ["assignments"] = assignmentsJson
                })
        ];
        return true;
    }

    private static bool TryFindJsonArrayEnd(string text, int arrayStart, out int arrayEnd) {
        arrayEnd = -1;
        var depth = 0;
        var inString = false;
        var escaping = false;

        for (var i = arrayStart; i < text.Length; i++) {
            var ch = text[i];
            if (inString) {
                if (escaping) {
                    escaping = false;
                    continue;
                }
                if (ch == '\\') {
                    escaping = true;
                    continue;
                }
                if (ch == '"')
                    inString = false;
                continue;
            }

            if (ch == '"') {
                inString = true;
                continue;
            }
            if (ch == '[') {
                depth++;
                continue;
            }
            if (ch != ']')
                continue;

            depth--;
            if (depth == 0) {
                arrayEnd = i;
                return true;
            }
            if (depth < 0)
                return false;
        }

        return false;
    }
}
