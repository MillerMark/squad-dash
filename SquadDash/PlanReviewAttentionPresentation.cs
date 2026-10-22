namespace SquadDash;

internal sealed record PlanReviewAttentionPresentation(
    PlanTask Task,
    PlanApprovalGate Gate,
    IReadOnlyList<PlanHumanReviewChecklistItem> Checklist,
    string Title,
    string Summary,
    IReadOnlyList<PlanApprovalGate>? RelatedGates = null)
{
    internal IReadOnlyList<PlanApprovalGate> Gates => RelatedGates is { Count: > 0 }
        ? RelatedGates
        : [Gate];
}

internal sealed record PlanHumanReviewWorkflowPresentation(
    PlanTask Task,
    IReadOnlyList<PlanApprovalGate> CurrentGates,
    IReadOnlyList<PlanHumanReviewChecklistItem> Checklist,
    int PhaseNumber,
    int TotalPhases,
    bool IsFinalPhase,
    bool HasActionableInstructions,
    string Title,
    string Question,
    string? InstructionProblem);

/// <summary>
/// Canonical resolver for the next human-review phase. Every recovery and approval surface consumes
/// this result instead of independently selecting a gate.
/// </summary>
internal static class PlanHumanReviewWorkflowResolver
{
    internal static PlanHumanReviewWorkflowPresentation? Resolve(Plan plan, string? taskId = null)
    {
        taskId ??= plan.InterruptionData?.InterruptedTaskId ?? plan.Progress.ExecutingTaskId;
        if (string.IsNullOrWhiteSpace(taskId))
            taskId = plan.Tasks.FirstOrDefault(task =>
                task.Status == PlanTaskStatus.HumanReviewRequired)?.TaskId;
        if (string.IsNullOrWhiteSpace(taskId)) return null;

        var task = plan.Tasks.FirstOrDefault(candidate =>
            string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal));
        if (task is null || task.Status != PlanTaskStatus.HumanReviewRequired) return null;

        var currentGates = PlanHumanReviewGatePolicy.FindWorkflowGates(
            plan, task.TaskId, allowPendingReviewSiblings: true);
        if (currentGates.Count == 0) return null;

        var allUnresolved = PlanHumanReviewGatePolicy.FindWorkflowCandidates(
            plan, task.TaskId, allowPendingReviewSiblings: true);
        var phaseNumber = 1 + currentGates
            .SelectMany(gate => gate.DependsOnGateIds ?? [])
            .Select(dependencyId => DependencyDepth(plan, dependencyId))
            .DefaultIfEmpty(0)
            .Max();
        var totalPhases = Math.Max(phaseNumber, allUnresolved
            .Select(gate => 1 + (gate.DependsOnGateIds ?? [])
                .Select(dependencyId => DependencyDepth(plan, dependencyId))
                .DefaultIfEmpty(0).Max())
            .DefaultIfEmpty(1).Max());
        var checklist = PlanHumanReviewChecklist.Build(plan, currentGates);
        var incomplete = currentGates.SelectMany(gate => gate.ProofRequirements ?? [])
            .FirstOrDefault(requirement => !HasActionableInstructions(requirement));
        var hasInstructions = checklist.Count > 0 && incomplete is null;
        var title = totalPhases > 1
            ? $"Human Verification — Phase {phaseNumber} of {totalPhases}"
            : "Human Verification Is Required";
        var question = currentGates.Count == 1
            ? PlanProofCapabilityPolicy.ResolveHumanQuestion(currentGates[0]) ??
              "Complete every check in this review phase before continuing."
            : "Complete every check in this review phase before continuing.";
        return new PlanHumanReviewWorkflowPresentation(
            task,
            currentGates,
            checklist,
            phaseNumber,
            totalPhases,
            currentGates.Count == allUnresolved.Count,
            hasInstructions,
            hasInstructions ? title : "Review Instructions Need Repair",
            question,
            incomplete is null
                ? null
                : $"The review requirement '{incomplete.RequirementId}' does not contain a complete actionable procedure.");
    }

    private static bool HasActionableInstructions(PlanTaskProofRequirement requirement)
    {
        // Plans written before accessMode was required used one atomic question as the complete
        // directly-reachable review instruction. Preserve that durable contract; new plans are
        // validated more strictly and must declare directly-reachable or recipe explicitly.
        if (string.IsNullOrWhiteSpace(requirement.AccessMode))
            return !string.IsNullOrWhiteSpace(requirement.Question ?? requirement.Description);
        if (string.Equals(
                requirement.AccessMode,
                HumanVerificationAccessMode.DirectlyReachable,
                StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(requirement.Question ?? requirement.Description);
        var recipe = requirement.VerificationRecipe;
        return recipe is not null &&
               !string.IsNullOrWhiteSpace(recipe.Setup) &&
               !string.IsNullOrWhiteSpace(recipe.Open) &&
               !string.IsNullOrWhiteSpace(recipe.Observe) &&
               !string.IsNullOrWhiteSpace(recipe.Cleanup);
    }

    private static int DependencyDepth(Plan plan, string gateId, HashSet<string>? visited = null)
    {
        visited ??= new HashSet<string>(StringComparer.Ordinal);
        if (!visited.Add(gateId)) return 0;
        var gate = plan.ApprovalGates.FirstOrDefault(candidate =>
            string.Equals(candidate.GateId, gateId, StringComparison.Ordinal));
        var depth = gate is null
            ? 0
            : 1 + (gate.DependsOnGateIds ?? [])
                .Select(dependencyId => DependencyDepth(plan, dependencyId, visited))
                .DefaultIfEmpty(0).Max();
        visited.Remove(gateId);
        return depth;
    }
}

internal enum PlanCheckpointPrimaryActionKind
{
    ApproveCheckpoint,
    CompleteHumanReviewTasks,
}

internal sealed record PlanCheckpointPrimaryActionPresentation(
    PlanCheckpointPrimaryActionKind Kind,
    string Label,
    string Hint,
    IReadOnlyList<PlanReviewAttentionPresentation> ReviewAttentions)
{
    internal PlanReviewAttentionPresentation? ReviewAttention =>
        ReviewAttentions.Count == 1 ? ReviewAttentions[0] : null;
}

internal static class PlanHumanReviewCheckpointCommitPolicy
{
    internal static bool OwnsCurrentHead(
        Plan plan,
        IReadOnlyCollection<string> representedGateIds,
        string? currentHead)
    {
        if (string.IsNullOrWhiteSpace(currentHead)) return false;

        return ResolveDurableCommits(plan, representedGateIds).Contains(
            currentHead, StringComparer.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string> ResolveDurableCommits(
        Plan plan,
        IReadOnlyCollection<string> representedGateIds)
    {
        var representedTaskIds = plan.ApprovalGates
            .Where(gate => representedGateIds.Contains(gate.GateId, StringComparer.Ordinal) &&
                           gate.Status == PlanGateStatus.AwaitingApproval)
            .SelectMany(gate => gate.AfterTaskIds)
            .ToHashSet(StringComparer.Ordinal);
        return plan.Tasks
            .Where(task => representedTaskIds.Contains(task.TaskId))
            .Select(ResolveDurableCommit)
            .OfType<string>()
            .Where(commit => !string.IsNullOrWhiteSpace(commit))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveDurableCommit(PlanTask task)
    {
        if (task.Status == PlanTaskStatus.HumanReviewRequired)
        {
            return string.Equals(
                task.Handoff?.CompletionKind,
                DecomposeStepCompletionKind.VerifiedNoOp,
                StringComparison.Ordinal)
                ? task.Handoff?.EvaluatedCommit
                : task.Handoff?.Commit;
        }

        if (task.Status is not (PlanTaskStatus.Complete or PlanTaskStatus.Superseded))
            return null;
        return string.Equals(
            task.CompletionKind,
            DecomposeStepCompletionKind.VerifiedNoOp,
            StringComparison.Ordinal)
            ? task.EvaluatedCommit
            : task.Commit;
    }
}

/// <summary>
/// Defines the single primary action represented by an approval boundary. UI surfaces must use
/// this presentation instead of inferring task completion from a gate's AfterTaskIds. A gate can
/// review an already-complete task without owning a task-completion transition.
/// </summary>
internal static class PlanCheckpointPrimaryActionPresentationResolver
{
    internal static PlanCheckpointPrimaryActionPresentation Resolve(
        Plan plan,
        IReadOnlyList<string> representedGateIds)
    {
        var representedGates = plan.ApprovalGates
            .Where(gate => representedGateIds.Contains(gate.GateId, StringComparer.Ordinal) &&
                           gate.Status == PlanGateStatus.AwaitingApproval)
            .ToArray();
        var reviewAttentions = representedGates.SelectMany(gate => gate.AfterTaskIds.Select(taskId =>
        {
            var reviewAttention = PlanReviewAttentionPresentationResolver.Resolve(plan, taskId);
            return reviewAttention is not null &&
                   reviewAttention.Gates.Any(candidate =>
                       string.Equals(candidate.GateId, gate.GateId, StringComparison.Ordinal))
                ? reviewAttention
                : null;
        })).OfType<PlanReviewAttentionPresentation>().DistinctBy(attention => attention.Task.TaskId).ToArray();
        if (reviewAttentions.Length > 0)
        {
            var singleReview = reviewAttentions.Length == 1 ? reviewAttentions[0] : null;
            var stepLabel = singleReview is null
                ? "Reviewed Steps"
                : PlanRecoveryPresentationBuilder.FormatStepLabel(
                    singleReview.Task.DisplayStepLabel,
                    singleReview.Task.Title ?? singleReview.Task.TaskId);
            var label = representedGates.Length == 1
                ? $"{stepLabel} Is Complete"
                : singleReview is null
                    ? "Complete Reviewed Steps & Approve Checkpoints"
                    : $"Complete {stepLabel} & Approve Checkpoints";
            if (!plan.IsPaused) label += " & Continue";
            return new PlanCheckpointPrimaryActionPresentation(
                PlanCheckpointPrimaryActionKind.CompleteHumanReviewTasks,
                label,
                plan.IsPaused
                    ? "Accept every represented human-reviewed step, approve all represented checkpoints, and keep the plan paused in one durable transition."
                    : "Accept every represented human-reviewed step, approve all represented checkpoints, and continue in one durable transition.",
                reviewAttentions);
        }

        var scope = representedGates.Length switch
        {
            2 => "Both Checkpoints",
            > 2 => "All Checkpoints",
            _ => "Checkpoint",
        };
        return new PlanCheckpointPrimaryActionPresentation(
            PlanCheckpointPrimaryActionKind.ApproveCheckpoint,
            plan.IsPaused ? $"Approve {scope}" : $"Approve {scope} & Continue",
            plan.IsPaused
                ? "Approve the represented checkpoint without changing task completion and keep the plan paused."
                : "Approve the represented checkpoint without changing task completion, then continue the plan.",
            []);
    }
}

internal static class PlanHumanReviewStepAcceptance
{
    internal static Plan ApplyTasks(
        Plan plan,
        IReadOnlyList<PlanReviewAttentionPresentation> reviewAttentions,
        string? currentHead = null,
        IReadOnlyDictionary<string, string>? reviewedTaskCommits = null)
    {
        var updated = plan;
        if (reviewedTaskCommits is not null && string.IsNullOrWhiteSpace(currentHead))
            throw new InvalidOperationException("Cannot validate reviewed evidence because the current HEAD is unavailable.");
        foreach (var reviewAttention in reviewAttentions)
        {
            var task = plan.Tasks.SingleOrDefault(candidate =>
                string.Equals(candidate.TaskId, reviewAttention.Task.TaskId, StringComparison.Ordinal));
            var handoff = task?.Handoff ??
                throw new InvalidOperationException("The reviewed task has no durable implementation handoff.");
            if (task!.Status != PlanTaskStatus.HumanReviewRequired)
                throw new InvalidOperationException("The reviewed task is no longer awaiting human review.");
            var isVerifiedNoOp = string.Equals(
                handoff.CompletionKind,
                DecomposeStepCompletionKind.VerifiedNoOp,
                StringComparison.Ordinal);
            if (!isVerifiedNoOp && string.IsNullOrWhiteSpace(handoff.Commit))
                throw new InvalidOperationException("The reviewed task handoff has no commit.");
            var taskCommit = isVerifiedNoOp ? handoff.EvaluatedCommit : handoff.Commit;
            var reviewedCommit = isVerifiedNoOp
                ? reviewAttention.Task.Handoff?.EvaluatedCommit
                : reviewAttention.Task.Handoff?.Commit;
            var isInterruptedTask = plan.LifecycleStatus is PlanLifecycleStatus.Interrupted or PlanLifecycleStatus.Blocked &&
                string.Equals(plan.InterruptionData?.InterruptedTaskId, task.TaskId, StringComparison.Ordinal);
            var evidence = isInterruptedTask ? plan.InterruptionData?.TaskCommitEvidence : null;
            var expectedCommit = currentHead;
            if (reviewedTaskCommits is not null)
                reviewedTaskCommits.TryGetValue(task.TaskId, out expectedCommit);
            if (!string.Equals(taskCommit, reviewedCommit, StringComparison.OrdinalIgnoreCase) ||
                (!isVerifiedNoOp && isInterruptedTask &&
                 (evidence is null ||
                  !string.Equals(evidence.TaskId, task.TaskId, StringComparison.Ordinal) ||
                  !string.Equals(taskCommit, evidence.Commit, StringComparison.OrdinalIgnoreCase))) ||
                ((!string.IsNullOrWhiteSpace(currentHead) || reviewedTaskCommits is not null) &&
                 (string.IsNullOrWhiteSpace(expectedCommit) ||
                  !string.Equals(taskCommit, expectedCommit, StringComparison.OrdinalIgnoreCase))))
            {
                throw new InvalidOperationException(
                    $"Reviewed evidence is stale for task {task.TaskId}: task tip has advanced past the reviewed commit. Refresh the review card.");
            }
            var commits = isVerifiedNoOp
                ? Array.Empty<PlanEvidenceCommit>()
                : task.Commits ??
                [new PlanEvidenceCommit(
                    handoff.Commit!,
                    PlanRecoveryCommitRelation.Task,
                    "Host-validated implementation handoff reviewed at the human checkpoint.")];
            var result = new DecomposeStepResult(
                updated.PlanId,
                reviewAttention.Task.TaskId,
                updated.Revision,
                "complete",
                isVerifiedNoOp ? null : handoff.Commit,
                handoff.Summary,
                [],
                handoff.Verification,
                CompletedWork: handoff.CompletedWork,
                CompletionKind: isVerifiedNoOp
                    ? DecomposeStepCompletionKind.VerifiedNoOp
                    : DecomposeStepCompletionKind.Changed,
                EvaluatedCommit: isVerifiedNoOp ? handoff.EvaluatedCommit : null,
                NoOpEvidence: isVerifiedNoOp ? handoff.NoOpEvidence : null)
            {
                Commits = commits,
            };
            updated = PlanStoreUpdater.ApplyStepAccepted(updated, null, result);
        }
        return updated with
        {
            InterruptionData = plan.LifecycleStatus is PlanLifecycleStatus.Interrupted or PlanLifecycleStatus.Blocked &&
                               plan.InterruptionData is { } interruption &&
                               !reviewAttentions.Any(attention => string.Equals(
                                   attention.Task.TaskId, interruption.InterruptedTaskId, StringComparison.Ordinal))
                ? interruption
                : null,
            Progress = updated.Progress with { ExecutingTaskId = null },
        };
    }
}

internal static class PlanReviewAttentionPresentationResolver
{
    internal static PlanReviewAttentionPresentation? Resolve(Plan plan, string? taskId = null)
    {
        taskId ??= plan.InterruptionData?.InterruptedTaskId ?? plan.Progress.ExecutingTaskId;
        if (string.IsNullOrWhiteSpace(taskId))
            taskId = plan.Tasks.FirstOrDefault(task =>
                task.Status == PlanTaskStatus.HumanReviewRequired)?.TaskId;
        if (string.IsNullOrWhiteSpace(taskId)) return null;

        var task = plan.Tasks.FirstOrDefault(candidate =>
            string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal));
        if (task is null || task.Status != PlanTaskStatus.HumanReviewRequired) return null;
        var workflow = PlanHumanReviewWorkflowResolver.Resolve(plan, task.TaskId);
        if (workflow is null || !workflow.IsFinalPhase || !workflow.HasActionableInstructions)
            return null;
        var gates = PlanHumanReviewGatePolicy.FindCombinableGates(
            plan, task.TaskId, allowPendingReviewSiblings: true);
        if (gates.Count == 0) return null;
        var gate = gates[0];

        var stepLabel = PlanRecoveryPresentationBuilder.FormatStepLabel(
            task.DisplayStepLabel,
            task.Title ?? task.TaskId);
        return new PlanReviewAttentionPresentation(
            task,
            gate,
            workflow.Checklist,
            workflow.Title,
            $"Implementation and automated verification for {stepLabel} are complete. Human observation remains.",
            gates);
    }

    internal static PlanTaskProofRequirement? FindRequirement(
        PlanReviewAttentionPresentation presentation,
        PlanHumanReviewChecklistItem item) =>
        presentation.Gates.SelectMany(gate => gate.ProofRequirements ?? [])
            .FirstOrDefault(requirement =>
                string.Equals(requirement.RequirementId, item.RequirementId, StringComparison.Ordinal));
}
