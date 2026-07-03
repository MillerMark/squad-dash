namespace SquadDash;

using System.Text.Json;
using System.Text.RegularExpressions;

internal sealed record ApprovalGroupAssignment(string Sha, string Group);

internal static partial class ApprovalGroupParser {
    [GeneratedRegex(
        @"APPROVAL_GROUP_JSON:\s*(?:```(?:json)?\s*)?(?<json>\{[\s\S]*?\})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApprovalGroupBlockRegex();

    internal static IReadOnlyList<ApprovalGroupAssignment> Parse(string? text) {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var result = new List<ApprovalGroupAssignment>();
        foreach (Match match in ApprovalGroupBlockRegex().Matches(text)) {
            if (TryParseAssignment(match.Groups["json"].Value, out var assignment))
                result.Add(assignment);
        }

        return result;
    }

    private static bool TryParseAssignment(string json, out ApprovalGroupAssignment assignment) {
        if (TryParseAssignmentCore(json, out assignment))
            return true;

        if (json.Contains("\\\"", StringComparison.Ordinal) &&
            TryParseAssignmentCore(json.Replace("\\\"", "\"", StringComparison.Ordinal), out assignment))
            return true;

        assignment = null!;
        return false;
    }

    private static bool TryParseAssignmentCore(string json, out ApprovalGroupAssignment assignment) {
        assignment = null!;
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var sha = doc.RootElement.TryGetProperty("sha", out var shaProp)
                ? shaProp.GetString()
                : null;
            var group = doc.RootElement.TryGetProperty("group", out var groupProp)
                ? groupProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(sha) || string.IsNullOrWhiteSpace(group))
                return false;

            assignment = new ApprovalGroupAssignment(sha.Trim(), group.Trim());
            return true;
        }
        catch (JsonException) {
            return false;
        }
    }
}
