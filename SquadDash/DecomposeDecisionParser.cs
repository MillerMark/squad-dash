using System.Text.Json;
using System.Text.RegularExpressions;

namespace SquadDash;

internal sealed record DecomposeDecision(string GroupId, string Revision, string Action, string? Branch);

internal static class DecomposeDecisionParser
{
    private const string Marker = "DECOMPOSE_DECISION_JSON:";
    private static readonly Regex GroupIdPattern = new(@"^[A-Z]+-\d{8}$", RegexOptions.Compiled);

    internal static bool TryParse(string? text, out DecomposeDecision? decision)
    {
        decision = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var marker = text.LastIndexOf(Marker, StringComparison.Ordinal);
        if (marker < 0) return false;
        var start = text.IndexOf('{', marker + Marker.Length);
        if (start < 0) return false;
        int depth = 0, end = -1; bool quoted = false, escaped = false;
        for (int i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (escaped) { escaped = false; continue; }
            if (quoted && c == '\\') { escaped = true; continue; }
            if (c == '"') { quoted = !quoted; continue; }
            if (quoted) continue;
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) { end = i; break; }
        }
        if (end < 0) return false;
        try
        {
            decision = JsonSerializer.Deserialize<DecomposeDecision>(text[start..(end + 1)],
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            SquadDashTrace.Write(TraceCategory.General, $"Decompose decision JSON is invalid: {ex.Message}");
            return false;
        }
        return decision is not null && GroupIdPattern.IsMatch(decision.GroupId ?? "") &&
               !string.IsNullOrWhiteSpace(decision.Revision) &&
               decision.Action is "add-to-backlog" or "collect" or "execute-new-branch" or "execute-active-branch";
    }
}
