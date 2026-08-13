using System.Text.RegularExpressions;

namespace SquadDash;

internal sealed record PlanHumanReviewChecklistItem(
    string GateId,
    string ItemId,
    string RequirementId,
    string Text,
    string CandidateCommit,
    bool IsChecked,
    bool WasPreviouslyChecked,
    string? PreviouslyCheckedCommit);

/// <summary>Builds atomic, commit-scoped human review items for current and legacy plans.</summary>
internal static class PlanHumanReviewChecklist
{
    internal const string UncommittedCandidate = "uncommitted-candidate";

    internal static IReadOnlyList<PlanHumanReviewChecklistItem> Build(
        Plan plan,
        IEnumerable<PlanApprovalGate> gates)
    {
        var result = new List<PlanHumanReviewChecklistItem>();
        foreach (var gate in gates)
        {
            var candidate = ResolveCandidateCommit(plan, gate);
            var definitions = BuildDefinitions(gate);
            foreach (var definition in definitions)
            {
                var current = gate.HumanReviewSelections?
                    .LastOrDefault(selection =>
                        string.Equals(selection.ItemId, definition.ItemId, StringComparison.Ordinal) &&
                        string.Equals(selection.CandidateCommit, candidate, StringComparison.OrdinalIgnoreCase));
                var previous = gate.HumanReviewSelections?
                    .Where(selection =>
                        selection.IsChecked &&
                        string.Equals(selection.ItemId, definition.ItemId, StringComparison.Ordinal) &&
                        !string.Equals(selection.CandidateCommit, candidate, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(selection => selection.UpdatedAt)
                    .FirstOrDefault();
                result.Add(new PlanHumanReviewChecklistItem(
                    gate.GateId,
                    definition.ItemId,
                    definition.RequirementId,
                    definition.Text,
                    candidate,
                    current?.IsChecked == true,
                    previous is not null,
                    previous?.CandidateCommit));
            }
        }
        return result;
    }

    internal static string ResolveCandidateCommit(Plan plan, PlanApprovalGate gate)
    {
        var commits = gate.AfterTaskIds
            .Select(taskId => plan.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, taskId, StringComparison.Ordinal)))
            .Where(task => task is not null)
            .Select(task => task!.Commit ?? task.Handoff?.Commit)
            .Where(commit => !string.IsNullOrWhiteSpace(commit))
            .Select(commit => commit!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (commits.Length > 0)
            return string.Join("+", commits);

        var interruptionCommit = plan.InterruptionData?.TaskCommitEvidence?.Commit
            ?? plan.InterruptionData?.LastCommit;
        return string.IsNullOrWhiteSpace(interruptionCommit)
            ? UncommittedCandidate
            : interruptionCommit.Trim();
    }

    private static IReadOnlyList<(string ItemId, string RequirementId, string Text)> BuildDefinitions(
        PlanApprovalGate gate)
    {
        var definitions = new List<(string, string, string)>();
        if (gate.ProofRequirements is { Count: > 0 })
        {
            foreach (var requirement in gate.ProofRequirements)
            {
                var source = string.IsNullOrWhiteSpace(requirement.Question)
                    ? requirement.Description
                    : requirement.Question!;
                var parts = SplitLegacyQuestions(source);
                for (var index = 0; index < parts.Count; index++)
                {
                    var itemId = parts.Count == 1
                        ? requirement.RequirementId
                        : $"{requirement.RequirementId}#{index + 1}";
                    definitions.Add((itemId, requirement.RequirementId, parts[index]));
                }
            }
        }

        if (definitions.Count == 0)
        {
            var source = string.IsNullOrWhiteSpace(gate.Question) ? gate.Message : gate.Question!;
            var parts = SplitLegacyQuestions(source);
            for (var index = 0; index < parts.Count; index++)
                definitions.Add(($"legacy#{index + 1}", "legacy", parts[index]));
        }
        return definitions;
    }

    internal static IReadOnlyList<string> SplitLegacyQuestions(string text)
    {
        var normalized = string.Join(' ', text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (normalized.Length == 0) return ["The requested human review succeeds."];

        var matches = Regex.Matches(
            normalized,
            @"[^?]+(?:\?|$)",
            RegexOptions.CultureInvariant);
        var parts = matches
            .Select(match => match.Value.Trim())
            .Where(part => part.Length > 0)
            .ToArray();
        return parts.Length == 0 ? [normalized] : parts;
    }
}
