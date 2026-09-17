namespace SquadDash;

/// <summary>
/// Enforces the AI-authored display-title contract for newly proposed plans. Durable legacy plans
/// remain loadable and use <see cref="PlanHumanReviewChecklist.ResolveDisplayTitle"/> instead.
/// </summary>
internal static class PlanHumanProofTitlePolicy
{
    internal const int MaximumWordCount = 8;
    internal const int MaximumCharacterCount = 80;

    internal static IReadOnlyList<string> ValidateAiAuthoredPlan(DecomposedTaskGroup group)
    {
        var diagnostics = new List<string>();
        foreach (var gate in group.ApprovalGates ?? [])
        foreach (var requirement in gate.ProofRequirements ?? [])
        {
            var identity = $"gate '{gate.GateId}' proof '{requirement.RequirementId}'";
            if (!TryValidateTitle(requirement.Title, requirement.Question, out var reason))
                diagnostics.Add($"{identity} {reason}");
        }
        return diagnostics;
    }

    internal static bool TryValidateTitle(string? value, string? question, out string reason)
    {
        var title = value?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            reason = "is missing title. Add a short, AI-authored scenario title distinct from its checkbox question.";
            return false;
        }

        if (title.EndsWith('?') ||
            (!string.IsNullOrWhiteSpace(question) &&
             string.Equals(Normalize(title), Normalize(question), StringComparison.OrdinalIgnoreCase)))
        {
            reason = "title must be a short scenario label, not its checkbox question.";
            return false;
        }

        var wordCount = title.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        if (wordCount > MaximumWordCount || title.Length > MaximumCharacterCount)
        {
            reason = $"title is too long. Use at most {MaximumWordCount} words and {MaximumCharacterCount} characters.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
