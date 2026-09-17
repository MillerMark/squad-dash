namespace SquadDash;

internal sealed record HumanVerificationReadinessFinding(
    string TargetId,
    string RequirementId,
    string Severity,
    string Message);

/// <summary>
/// Performs structural checks only. It never guesses from prose: fixture ownership is accepted
/// exclusively through explicit task/output identifiers, while semantic feasibility remains an
/// advisory AI-review concern.
/// </summary>
internal static class HumanVerificationReadinessPolicy
{
    internal static IReadOnlyList<HumanVerificationReadinessFinding> Evaluate(DecomposedTaskGroup group)
    {
        var findings = new List<HumanVerificationReadinessFinding>();
        // The parser deliberately runs this policy while it is still collecting all independent
        // schema errors. Keep malformed duplicate IDs from short-circuiting that full report.
        var tasks = group.Tasks
            .Where(task => !string.IsNullOrWhiteSpace(task.Id))
            .GroupBy(task => task.Id, StringComparer.Ordinal)
            .ToDictionary(values => values.Key, values => values.First(), StringComparer.Ordinal);

        foreach (var task in group.Tasks)
            foreach (var proof in (task.ProofRequirements ?? []).Where(requirement =>
                         PlanProofCapabilityPolicy.IsHumanOnly(requirement.ProofType)))
                EvaluateRequirement(group, tasks, task.Id, proof, task.DependsOn ?? [], findings);

        foreach (var gate in group.ApprovalGates ?? [])
            foreach (var proof in gate.ProofRequirements ?? [])
                EvaluateRequirement(group, tasks, gate.GateId, proof, gate.AfterTaskIds ?? [], findings);

        return findings;
    }

    private static void EvaluateRequirement(
        DecomposedTaskGroup group,
        IReadOnlyDictionary<string, DecomposedSubTask> tasks,
        string targetId,
        DecomposedTaskProofRequirement proof,
        IReadOnlyList<string> frontier,
        ICollection<HumanVerificationReadinessFinding> findings)
    {
        var mode = proof.AccessMode?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(mode))
        {
            findings.Add(new(targetId, proof.RequirementId, "warning",
                "Human verification has no declared access mode or verification recipe."));
            return;
        }

        if (mode == HumanVerificationAccessMode.DirectlyReachable)
            return;

        if (mode == HumanVerificationAccessMode.Recipe)
        {
            if (!IsComplete(proof.VerificationRecipe))
                findings.Add(new(targetId, proof.RequirementId, "error",
                    "Recipe access requires nonempty Setup, Open, Observe, and Cleanup instructions."));
            else
                AddOperatorGuideFindings(targetId, proof, findings);
            return;
        }

        if (mode != HumanVerificationAccessMode.Fixture)
        {
            findings.Add(new(targetId, proof.RequirementId, "error",
                $"Unknown human-verification accessMode '{proof.AccessMode}'."));
            return;
        }

        if (string.IsNullOrWhiteSpace(proof.FixtureTaskId) ||
            string.IsNullOrWhiteSpace(proof.FixtureOutputId))
        {
            findings.Add(new(targetId, proof.RequirementId, "error",
                "Fixture access requires explicit fixtureTaskId and fixtureOutputId values."));
            return;
        }

        if (!tasks.TryGetValue(proof.FixtureTaskId, out var fixtureTask))
        {
            findings.Add(new(targetId, proof.RequirementId, "error",
                $"Fixture task '{proof.FixtureTaskId}' does not exist."));
            return;
        }
        if (!(fixtureTask.Outputs ?? []).Any(output =>
                string.Equals(output.OutputId, proof.FixtureOutputId, StringComparison.Ordinal)))
        {
            findings.Add(new(targetId, proof.RequirementId, "error",
                $"Fixture output '{proof.FixtureOutputId}' is not declared by task '{proof.FixtureTaskId}'."));
            return;
        }
        if (!frontier.Any(taskId => string.Equals(taskId, proof.FixtureTaskId, StringComparison.Ordinal) ||
                                    DependsTransitivelyOn(tasks, taskId, proof.FixtureTaskId)))
        {
            findings.Add(new(targetId, proof.RequirementId, "error",
                $"Fixture task '{proof.FixtureTaskId}' is not upstream of this human-review boundary."));
        }
        if (!IsComplete(proof.VerificationRecipe))
            findings.Add(new(targetId, proof.RequirementId, "warning",
                "The fixture reference is structurally valid, but human-facing Setup, Open, Observe, and Cleanup instructions are incomplete."));
        else
            AddOperatorGuideFindings(targetId, proof, findings);
    }

    private static void AddOperatorGuideFindings(
        string targetId,
        DecomposedTaskProofRequirement proof,
        ICollection<HumanVerificationReadinessFinding> findings)
    {
        var recipe = proof.VerificationRecipe!;
        var body = string.Join('\n', recipe.Setup, recipe.Open, recipe.Observe, recipe.Cleanup);
        foreach (var error in ApprovalOperatorGuidePolicy.Validate(body))
            findings.Add(new(targetId, proof.RequirementId, "error", error));
    }

    private static bool IsComplete(HumanVerificationRecipe? recipe) =>
        recipe is not null &&
        !string.IsNullOrWhiteSpace(recipe.Setup) &&
        !string.IsNullOrWhiteSpace(recipe.Open) &&
        !string.IsNullOrWhiteSpace(recipe.Observe) &&
        !string.IsNullOrWhiteSpace(recipe.Cleanup);

    private static bool DependsTransitivelyOn(
        IReadOnlyDictionary<string, DecomposedSubTask> tasks,
        string taskId,
        string dependencyId,
        HashSet<string>? visited = null)
    {
        if (!tasks.TryGetValue(taskId, out var task)) return false;
        visited ??= new HashSet<string>(StringComparer.Ordinal);
        if (!visited.Add(taskId)) return false;
        return (task.DependsOn ?? []).Any(candidate =>
            string.Equals(candidate, dependencyId, StringComparison.Ordinal) ||
            DependsTransitivelyOn(tasks, candidate, dependencyId, visited));
    }
}
