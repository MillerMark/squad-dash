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
                var disposition = gate.HumanReviewDispositions?
                    .Where(candidateDisposition =>
                        string.Equals(candidateDisposition.PlanRevision, plan.Revision, StringComparison.Ordinal) &&
                        candidateDisposition.TargetItemIds.Contains(definition.ItemId, StringComparer.Ordinal))
                    .OrderByDescending(candidateDisposition => candidateDisposition.RecordedAt)
                    .FirstOrDefault();
                if (disposition?.Action is PlanHumanReviewClarificationAction.Remove or
                    PlanHumanReviewClarificationAction.Automate)
                    continue;

                if (disposition?.Action == PlanHumanReviewClarificationAction.Merge &&
                    !string.Equals(disposition.ItemId, definition.ItemId, StringComparison.Ordinal))
                    continue;

                var presentedDefinitions = disposition?.Action switch
                {
                    PlanHumanReviewClarificationAction.Reword or
                    PlanHumanReviewClarificationAction.Merge when
                        !string.IsNullOrWhiteSpace(disposition.ReplacementText) =>
                        [(definition.ItemId, disposition.ReplacementText!)],
                    PlanHumanReviewClarificationAction.Split when
                        disposition.ReplacementItems is { Count: > 0 } replacements =>
                        replacements.Select((text, index) =>
                            ($"{definition.ItemId}::clarified#{index + 1}", text)).ToArray(),
                    _ => [(definition.ItemId, definition.Text)],
                };
                foreach (var presented in presentedDefinitions)
                    AddChecklistItem(result, gate, definition.RequirementId,
                        presented.Item1, presented.Item2, candidate);
            }
        }
        return result;
    }

    private static void AddChecklistItem(
        List<PlanHumanReviewChecklistItem> result,
        PlanApprovalGate gate,
        string requirementId,
        string itemId,
        string text,
        string candidate)
    {
                var current = gate.HumanReviewSelections?
                    .LastOrDefault(selection =>
                        string.Equals(selection.ItemId, itemId, StringComparison.Ordinal) &&
                        CandidateCommitsMatch(selection.CandidateCommit, candidate));
                var previous = gate.HumanReviewSelections?
                    .Where(selection =>
                        selection.IsChecked &&
                        string.Equals(selection.ItemId, itemId, StringComparison.Ordinal) &&
                        !CandidateCommitsMatch(selection.CandidateCommit, candidate))
                    .OrderByDescending(selection => selection.UpdatedAt)
                    .FirstOrDefault();
                result.Add(new PlanHumanReviewChecklistItem(
                    gate.GateId,
                    itemId,
                    requirementId,
                    text,
                    candidate,
                    current?.IsChecked == true,
                    previous is not null,
                    previous?.CandidateCommit));
    }

    /// <summary>
    /// Compares the commit-scoped identity of a human-review selection. Multi-step gates retain
    /// their ordered '+'-joined identity while each component may be abbreviated or full-length.
    /// </summary>
    internal static bool CandidateCommitsMatch(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        if (string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        var leftParts = left.Split('+', StringSplitOptions.TrimEntries);
        var rightParts = right.Split('+', StringSplitOptions.TrimEntries);
        return leftParts.Length == rightParts.Length &&
               leftParts.Zip(rightParts).All(pair =>
                   RecoveryCommitValidator.CommitIdentifiersMatch(pair.First, pair.Second));
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
                var source = !string.IsNullOrWhiteSpace(requirement.Question)
                    ? requirement.Question!
                    : gate.ProofRequirements.Count == 1 && !string.IsNullOrWhiteSpace(gate.Question)
                        ? gate.Question!
                        : requirement.Description;
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
