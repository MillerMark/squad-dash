namespace SquadDash;

/// <summary>
/// Matches a task's declarative proof contract against the structured evidence returned for the
/// execution attempt. This intentionally performs identity matching, not semantic guessing from
/// filenames, commit subjects, or test names. Semantic cross-step review belongs to the plan's
/// explicit completion-audit validation.
/// </summary>
internal static class PlanStepProofPolicy
{
    internal static string? Validate(
        DecomposedSubTask? task,
        DecomposeStepResult? result)
    {
        if (task?.ProofRequirements is not { Count: > 0 } requirements)
            return null;
        if (result is null)
            return "The task has proof requirements but returned no step result.";
        if (result.Status != "complete")
            return null;
        if (result.ProofEvidence is not { Count: > 0 } evidence)
            return $"Task {task.Id} requires structured proof evidence before it can be completed.";

        var duplicate = evidence
            .GroupBy(item => item.RequirementId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            return $"Task {task.Id} returned duplicate proof evidence for {duplicate.Key}.";

        foreach (var requirement in requirements)
        {
            var match = evidence.FirstOrDefault(item =>
                string.Equals(item.RequirementId, requirement.RequirementId, StringComparison.Ordinal));
            if (match is null)
                return $"Task {task.Id} did not satisfy proof requirement {requirement.RequirementId}.";
            if (!string.Equals(match.ProofType, requirement.ProofType, StringComparison.Ordinal))
                return $"Task {task.Id} returned {match.ProofType} evidence for {requirement.RequirementId}; " +
                       $"the approved plan requires {requirement.ProofType} evidence.";
            if (string.IsNullOrWhiteSpace(match.Summary))
                return $"Task {task.Id} returned empty evidence for {requirement.RequirementId}.";
            if (RequiresArtifact(requirement.ProofType) &&
                match.Artifacts?.Any(artifact => !string.IsNullOrWhiteSpace(artifact)) != true)
                return $"Task {task.Id} requires a durable artifact for {requirement.ProofType} proof " +
                       $"{requirement.RequirementId}.";
        }

        var unknown = evidence.FirstOrDefault(item => !requirements.Any(requirement =>
            string.Equals(requirement.RequirementId, item.RequirementId, StringComparison.Ordinal)));
        return unknown is null
            ? null
            : $"Task {task.Id} returned undeclared proof evidence {unknown.RequirementId}.";
    }

    private static bool RequiresArtifact(string proofType) => proofType is
        "live-ui-observation" or "restart-observation" or "human-observation";
}
