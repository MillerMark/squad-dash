using System.Text.Json;
using System.Text.RegularExpressions;

namespace SquadDash;

internal static partial class HostCommandParser {
    [GeneratedRegex(
        @"(?s)^(?<body>.*?)(?:\n|^)\s*HOST_COMMAND_JSON:\s*(?<json>\[[\s\S]*\])\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HostCommandJsonRegex();

    internal static bool TryExtract(
        string text,
        out string body,
        out HostCommandInvocation[] commands) {
        body = text;
        commands = Array.Empty<HostCommandInvocation>();

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');

        var match = HostCommandJsonRegex().Match(normalized);
        if (!match.Success)
            return false;

        try {
            using var document = JsonDocument.Parse(match.Groups["json"].Value);
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
            body = match.Groups["body"].Value.TrimEnd();
            return true;
        }
        catch (JsonException) {
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
            }
            if (dict.Count > 0)
                parameters = dict;
        }

        return new HostCommandInvocation(command.Trim(), parameters);
    }
}
