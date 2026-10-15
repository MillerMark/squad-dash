using System.Security.Cryptography;
using System.Text;
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
    string? PreviouslyCheckedCommit,
    string? Title = null,
    string? ReviewTarget = null,
    string? Answer = null,
    string? Note = null)
{
    internal bool IsExplicitNo =>
        string.Equals(Answer, PlanHumanReviewAnswer.No, StringComparison.Ordinal);

    internal bool IsUnanswered =>
        !IsChecked && !IsExplicitNo;
}

internal sealed record PlanHumanReviewItemResponse(bool? IsApproved, string? Note)
{
    internal static PlanHumanReviewItemResponse FromItem(PlanHumanReviewChecklistItem item) =>
        new(
            item.IsChecked
                ? true
                : item.IsExplicitNo
                    ? false
                    : null,
            item.Note);

    internal static string? NormalizeNote(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal PlanHumanReviewItemResponse Normalized() =>
        this with { Note = NormalizeNote(Note) };
}

internal static class PlanHumanReviewAnswer
{
    internal const string Yes = "yes";
    internal const string No = "no";

    internal static string? FromApproval(bool? isApproved) =>
        isApproved switch
        {
            true => Yes,
            false => No,
            _ => null,
        };

    internal static string Display(bool? isApproved) =>
        isApproved switch
        {
            true => "Yes",
            false => "No",
            _ => "Unanswered",
        };
}

/// <summary>Builds atomic, commit-scoped human review items for current and legacy plans.</summary>
internal static class PlanHumanReviewChecklist
{
    internal const string UncommittedCandidate = "uncommitted-candidate";
    internal const string ReconfirmationToolTip =
        "Previously confirmed, but related changes may affect this result. Confirm it again.";

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
                    AddChecklistItem(result, plan, gate, definition.RequirementId,
                        presented.Item1, presented.Item2, candidate, definition.Title);
            }
        }
        return result;
    }

    private static void AddChecklistItem(
        List<PlanHumanReviewChecklistItem> result,
        Plan plan,
        PlanApprovalGate gate,
        string requirementId,
        string itemId,
        string text,
        string candidate,
        string? title)
    {
        var reviewTarget = ResolveReviewTarget(plan, gate);
        var current = gate.HumanReviewSelections?
            .Where(selection =>
                string.Equals(selection.ItemId, itemId, StringComparison.Ordinal) &&
                SelectionApplies(plan, gate, selection, candidate, reviewTarget))
            .OrderByDescending(selection => selection.UpdatedAt)
            .FirstOrDefault();
        var previous = current is null
            ? gate.HumanReviewSelections?
                .Where(selection => string.Equals(selection.ItemId, itemId, StringComparison.Ordinal))
                .OrderByDescending(selection => selection.UpdatedAt)
                .FirstOrDefault()
            : null;
        result.Add(new PlanHumanReviewChecklistItem(
            gate.GateId,
            itemId,
            requirementId,
            text,
            candidate,
            current?.IsChecked == true,
            previous?.IsChecked == true,
            previous?.CandidateCommit,
            ResolveDisplayTitle(title, requirementId),
            reviewTarget,
            ResolveAnswer(current),
            PlanHumanReviewItemResponse.NormalizeNote(current?.Note)));
    }

    private static string? ResolveAnswer(PlanHumanReviewSelection? selection)
    {
        if (selection is null)
            return null;
        if (selection.IsChecked)
            return PlanHumanReviewAnswer.Yes;
        return string.Equals(selection.Answer, PlanHumanReviewAnswer.No, StringComparison.OrdinalIgnoreCase)
            ? PlanHumanReviewAnswer.No
            : null;
    }

    /// <summary>
    /// Identifies the exact review boundary a human confirmation applies to. Ordinary unrelated
    /// repository movement is intentionally excluded. A task correction, candidate change, or
    /// human-proof contract change creates a new target and requires conscious reconfirmation.
    /// </summary>
    internal static string ResolveReviewTarget(Plan plan, PlanApprovalGate gate)
    {
        var value = new StringBuilder("human-review-target-v1\n")
            .Append("candidate=").Append(ResolveCandidateCommit(plan, gate)).Append('\n')
            .Append("gate=").Append(gate.GateId).Append('\n')
            .Append("message=").Append(gate.Message).Append('\n')
            .Append("question=").Append(gate.Question).Append('\n');

        foreach (var requirement in gate.ProofRequirements ?? [])
        {
            value.Append("proof=")
                .Append(requirement.RequirementId).Append('|')
                .Append(requirement.ProofType).Append('|')
                .Append(requirement.Title).Append('|')
                .Append(requirement.Description).Append('|')
                .Append(requirement.Question).Append('|')
                .Append(requirement.AccessMode).Append('|')
                .Append(requirement.FixtureTaskId).Append('|')
                .Append(requirement.FixtureOutputId).Append('\n');
            if (requirement.VerificationRecipe is { } recipe)
            {
                value.Append("recipe=")
                    .Append(recipe.Setup).Append('|')
                    .Append(recipe.Open).Append('|')
                    .Append(recipe.Observe).Append('|')
                    .Append(recipe.Cleanup).Append('\n');
            }
        }

        foreach (var taskId in gate.AfterTaskIds)
        {
            var task = plan.Tasks.FirstOrDefault(candidateTask =>
                string.Equals(candidateTask.TaskId, taskId, StringComparison.Ordinal));
            foreach (var activity in (task?.ReviewActivity ?? [])
                         .Where(activity => activity.Kind == PlanReviewActivityKind.ManualCorrection)
                         .OrderBy(activity => activity.RecordedAt))
            {
                value.Append("correction=")
                    .Append(taskId).Append('|')
                    .Append(activity.RecordedAt.ToUniversalTime().Ticks).Append('|')
                    .Append(activity.Summary).Append('\n');
            }
        }

        return "v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }

    private static bool SelectionApplies(
        Plan plan,
        PlanApprovalGate gate,
        PlanHumanReviewSelection selection,
        string candidate,
        string reviewTarget)
    {
        if (!string.IsNullOrWhiteSpace(selection.ReviewTarget))
            return string.Equals(selection.ReviewTarget, reviewTarget, StringComparison.Ordinal);

        // Compatibility for selections written before review-target fingerprints existed. A
        // legacy confirmation remains current only when its candidate still matches and no later
        // recorded correction changed the reviewed boundary.
        if (!CandidateCommitsMatch(selection.CandidateCommit, candidate))
            return false;
        return gate.AfterTaskIds
            .Select(taskId => plan.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, taskId, StringComparison.Ordinal)))
            .Where(task => task is not null)
            .SelectMany(task => task!.ReviewActivity ?? [])
            .Where(activity => activity.Kind == PlanReviewActivityKind.ManualCorrection)
            .All(activity => activity.RecordedAt <= selection.UpdatedAt);
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

    private static IReadOnlyList<(string ItemId, string RequirementId, string Text, string? Title)> BuildDefinitions(
        PlanApprovalGate gate)
    {
        var definitions = new List<(string, string, string, string?)>();
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
                    definitions.Add((itemId, requirement.RequirementId, parts[index], requirement.Title));
                }
            }
        }

        if (definitions.Count == 0)
        {
            var source = string.IsNullOrWhiteSpace(gate.Question) ? gate.Message : gate.Question!;
            var parts = SplitLegacyQuestions(source);
            for (var index = 0; index < parts.Count; index++)
                definitions.Add(($"legacy#{index + 1}", "legacy", parts[index], null));
        }
        return definitions;
    }

    /// <summary>
    /// Uses the AI-authored title when present. Legacy proof contracts have no semantic title,
    /// so their stable identifier is only mechanically made readable; SquadDash does not pretend
    /// to summarize the question itself.
    /// </summary>
    internal static string ResolveDisplayTitle(string? title, string requirementId)
    {
        if (!string.IsNullOrWhiteSpace(title))
            return title.Trim();

        var words = (requirementId ?? string.Empty).Split(
            ['-', '_'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
            return "Review";

        var result = string.Join(' ', words).ToLowerInvariant();
        return char.ToUpperInvariant(result[0]) + result[1..];
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
