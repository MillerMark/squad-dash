using System.Text.RegularExpressions;

namespace SquadDash;

internal sealed record DecomposeDecision(string GroupId, string Revision, string Action, string? Branch);

internal static class DecomposeDecisionParser
{
    internal const string Marker = "DECOMPOSE_DECISION_JSON:";
    private static readonly Regex GroupIdPattern = new(@"^[A-Z]+-\d{8}$", RegexOptions.Compiled);

    internal static bool TryParse(string? text, out DecomposeDecision? decision)
    {
        decision = null;
        if (!StructuredJsonBlockParser.TryExtractObject<DecomposeDecision>(text, Marker, out var extraction) ||
            extraction is null)
            return false;

        decision = extraction.Payload with
        {
            Action = extraction.Payload.Action?.Trim().ToLowerInvariant() ?? string.Empty,
        };
        return decision is not null && GroupIdPattern.IsMatch(decision.GroupId ?? "") &&
               !string.IsNullOrWhiteSpace(decision.Revision) &&
               decision.Action is "add-to-backlog" or "collect" or "execute-new-branch" or "execute-active-branch";
    }
}
