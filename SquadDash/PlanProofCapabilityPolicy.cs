namespace SquadDash;

internal enum PlanProofExecutorKind
{
    Worker,
    Host,
    Human,
    Unsupported,
}

/// <summary>
/// Defines who can truthfully produce each proof type and moves human-only task proofs to
/// explicit approval checkpoints before a plan is staged. This is schema-driven policy, not
/// inference from task prose, filenames, or test names.
/// </summary>
internal static class PlanProofCapabilityPolicy
{
    private static readonly IReadOnlyDictionary<string, PlanProofExecutorKind> Executors =
        new Dictionary<string, PlanProofExecutorKind>(StringComparer.Ordinal)
        {
            ["ai-assessed"] = PlanProofExecutorKind.Worker,
            ["automated-test"] = PlanProofExecutorKind.Worker,
            ["build"] = PlanProofExecutorKind.Worker,
            ["host-recorded"] = PlanProofExecutorKind.Host,
            ["live-ui-observation"] = PlanProofExecutorKind.Human,
            ["restart-observation"] = PlanProofExecutorKind.Human,
            ["human-observation"] = PlanProofExecutorKind.Human,
        };

    internal static PlanProofExecutorKind Classify(string? proofType) =>
        proofType is not null && Executors.TryGetValue(proofType, out var executor)
            ? executor
            : PlanProofExecutorKind.Unsupported;

    internal static bool IsHumanOnly(string? proofType) =>
        Classify(proofType) == PlanProofExecutorKind.Human;

    /// <summary>
    /// Returns the authored approval question, or a compatibility question derived from a
    /// legacy human-proof requirement when the stored gate predates the question field.
    /// </summary>
    internal static string? ResolveHumanQuestion(PlanApprovalGate gate)
    {
        if (!string.IsNullOrWhiteSpace(gate.Question))
            return gate.Question.Trim();

        var observations = (gate.ProofRequirements ?? [])
            .Where(requirement => IsHumanOnly(requirement.ProofType))
            .Select(requirement => requirement.Description.Trim().TrimEnd('.', '?', '!'))
            .Where(description => description.Length > 0)
            .ToArray();
        return observations.Length switch
        {
            0 => null,
            1 => $"Did you observe the following behavior: {observations[0]}?",
            _ => $"Did you observe each of these behaviors: {string.Join("; ", observations)}?",
        };
    }

    internal static IReadOnlyList<DecomposedTaskProofRequirement> ResultEnvelopeRequirements(
        IReadOnlyList<DecomposedTaskProofRequirement>? requirements) =>
        (requirements ?? [])
            .Where(requirement => Classify(requirement.ProofType) != PlanProofExecutorKind.Host)
            .ToArray();

    internal static DecomposeStepResult AttachHostRecordedEvidence(
        DecomposedSubTask? task,
        DecomposeStepResult result,
        PlanTaskCommitEvidence evidence)
    {
        var hostRequirements = (task?.ProofRequirements ?? [])
            .Where(requirement => Classify(requirement.ProofType) == PlanProofExecutorKind.Host)
            .ToArray();
        if (hostRequirements.Length == 0) return result;

        var hostIds = hostRequirements
            .Select(requirement => requirement.RequirementId)
            .ToHashSet(StringComparer.Ordinal);
        var returnedEvidence = (result.ProofEvidence ?? [])
            .Where(item => !hostIds.Contains(item.RequirementId));
        var hostEvidence = hostRequirements.Select(requirement =>
            new DecomposeStepProofEvidence(
                requirement.RequirementId,
                requirement.ProofType,
                $"SquadDash recorded task-owned commit {evidence.Commit} from baseline {evidence.BaselineCommit}. " +
                requirement.Description,
                [$"git:{evidence.Commit}"]));
        return result with { ProofEvidence = returnedEvidence.Concat(hostEvidence).ToArray() };
    }

    internal static DecomposedTaskGroup RouteHumanProofsToApprovalGates(DecomposedTaskGroup group)
    {
        var gates = (group.ApprovalGates ?? []).ToList();
        var existingGateIds = gates.Select(gate => gate.GateId).ToHashSet(StringComparer.Ordinal);
        var tasks = new List<DecomposedSubTask>(group.Tasks.Count);

        foreach (var task in group.Tasks)
        {
            var humanProofs = (task.ProofRequirements ?? [])
                .Where(requirement => IsHumanOnly(requirement.ProofType))
                .ToArray();
            if (humanProofs.Length == 0)
            {
                tasks.Add(task);
                continue;
            }

            var workerProofs = task.ProofRequirements!
                .Where(requirement => !IsHumanOnly(requirement.ProofType))
                .ToArray();
            tasks.Add(task with { ProofRequirements = workerProofs.Length == 0 ? null : workerProofs });

            // A plan revision may update both a task's human proof contract and its explicit
            // checkpoint. That proof is already routed; creating another generated checkpoint
            // would leave two approval requests for the same step.
            var alreadyRoutedIds = gates
                .Where(gate => (gate.AfterTaskIds ?? []).Contains(task.Id, StringComparer.Ordinal))
                .SelectMany(gate => gate.ProofRequirements ?? [])
                .Select(requirement => requirement.RequirementId)
                .ToHashSet(StringComparer.Ordinal);
            var unroutedHumanProofs = humanProofs
                .Where(requirement => !alreadyRoutedIds.Contains(requirement.RequirementId))
                .ToArray();
            if (unroutedHumanProofs.Length == 0)
                continue;

            var directDependents = group.Tasks
                .Where(candidate => candidate.DependsOn.Contains(task.Id, StringComparer.Ordinal))
                .Select(candidate => candidate.Id)
                .ToArray();
            var baseGateId = $"{task.Id}-HUMAN-PROOF";
            var gateId = baseGateId;
            for (var suffix = 2; !existingGateIds.Add(gateId); suffix++)
                gateId = $"{baseGateId}-{suffix}";

            var requirementSummary = string.Join("; ", unroutedHumanProofs.Select(requirement =>
                $"{requirement.Description} [{requirement.ProofType}]"));
            var question = BuildHumanQuestion(unroutedHumanProofs);
            gates.Add(new DecomposedGate(
                gateId,
                $"Confirm the human-observed proof for “{task.Title ?? task.Id}”: {requirementSummary}",
                [task.Id],
                directDependents,
                unroutedHumanProofs,
                question));
        }

        return group with
        {
            Tasks = tasks,
            ApprovalGates = gates.Count == 0 ? null : gates,
        };
    }

    private static string BuildHumanQuestion(
        IReadOnlyList<DecomposedTaskProofRequirement> humanProofs)
    {
        var supplied = humanProofs
            .Select(requirement => requirement.Question?.Trim())
            .Where(question => !string.IsNullOrWhiteSpace(question))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (supplied.Length > 0)
            return string.Join(Environment.NewLine, supplied!);

        var observations = humanProofs
            .Select(requirement => requirement.Description.Trim().TrimEnd('.', '?', '!'))
            .Where(description => description.Length > 0)
            .ToArray();
        return observations.Length == 1
            ? $"Did you observe the following behavior: {observations[0]}?"
            : $"Did you observe each of these behaviors: {string.Join("; ", observations)}?";
    }
}
