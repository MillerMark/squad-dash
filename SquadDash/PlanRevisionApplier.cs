using System.Text.Json;

namespace SquadDash;

internal enum PlanRevisionApplyOutcome
{
    Applied,
    NoChanges,
    Stale,
    Invalid,
}

internal sealed record PlanRevisionApplyResult(
    PlanRevisionApplyOutcome Outcome,
    Plan? UpdatedPlan,
    DecomposedTaskGroup? EffectiveGroup,
    int AppliedChangeCount,
    int PreservedLockedTaskCount,
    string? Error = null);

/// <summary>Applies a complete AI-authored definition while preserving durable execution state.</summary>
internal static class PlanRevisionApplier
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    internal static PlanRevisionApplyResult Apply(
        Plan current,
        DecomposedTaskGroup proposal,
        string baseRevision,
        DateTimeOffset revisedAt,
        IReadOnlySet<string>? reopenTaskIds = null)
    {
        if (!string.Equals(current.PlanId, proposal.GroupId, StringComparison.Ordinal))
            return Invalid($"Revision returned plan ID '{proposal.GroupId}', expected '{current.PlanId}'.");
        if (!string.Equals(current.Revision, baseRevision, StringComparison.Ordinal))
            return new PlanRevisionApplyResult(
                PlanRevisionApplyOutcome.Stale, null, null, 0, 0,
                $"The revision was based on {baseRevision}, but the current plan is {current.Revision}.");

        var currentGroup = Canonicalize(PendingDecomposePlanAdapter.FromPlan(current).Group);
        proposal = Canonicalize(proposal);
        var currentDefinitions = currentGroup.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        var reopenedIds = reopenTaskIds is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(reopenTaskIds, StringComparer.Ordinal);
        var invalidReopenIds = reopenedIds.Where(id =>
            !current.Tasks.Any(task => string.Equals(task.TaskId, id, StringComparison.Ordinal) &&
                task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Partial or
                    PlanTaskStatus.Failed or PlanTaskStatus.HumanReviewRequired)).ToArray();
        if (invalidReopenIds.Length > 0)
            return Invalid("The revision cannot reopen these tasks at the current boundary: " +
                           string.Join(", ", invalidReopenIds));
        var lockedIds = current.Tasks
            .Where(task => !reopenedIds.Contains(task.TaskId) &&
                           (task.Status != PlanTaskStatus.Pending ||
                           string.Equals(task.TaskId, current.Progress.ExecutingTaskId, StringComparison.Ordinal))
            )
            .Select(task => task.TaskId)
            .ToHashSet(StringComparer.Ordinal);

        var effectiveTasks = new List<DecomposedSubTask>(proposal.Tasks.Count + lockedIds.Count);
        var effectiveIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var proposedTask in proposal.Tasks)
        {
            var effective = lockedIds.Contains(proposedTask.Id) &&
                            currentDefinitions.TryGetValue(proposedTask.Id, out var lockedDefinition)
                ? lockedDefinition
                : proposedTask;
            effectiveTasks.Add(effective);
            effectiveIds.Add(effective.Id);
        }

        foreach (var currentTask in currentGroup.Tasks.Where(task => lockedIds.Contains(task.Id)))
        {
            if (effectiveIds.Add(currentTask.Id)) effectiveTasks.Add(currentTask);
        }

        var effectiveGroup = proposal with { Tasks = effectiveTasks };
        var parserInput = "TASKS_JSON:\n" + JsonSerializer.Serialize(effectiveGroup, JsonOptions);
        if (!TasksJsonParser.TryParse(parserInput, out var validated, out var diagnostic) || validated is null)
            return Invalid(diagnostic?.Message ?? "The revised plan did not pass schema validation.", lockedIds.Count);
        if (!CodeHealthGroupRunner.HasNoDependencyCycle(validated, out var cycleIds))
            return Invalid("The revised plan contains a dependency cycle: " + string.Join(", ", cycleIds ?? []), lockedIds.Count);

        effectiveGroup = Canonicalize(validated);
        var currentDefinitionRevision = PendingDecomposePlanStore.ComputeRevision(currentGroup);
        var effectiveRevision = PendingDecomposePlanStore.ComputeRevision(effectiveGroup);
        if (string.Equals(currentDefinitionRevision, effectiveRevision, StringComparison.Ordinal))
            return new PlanRevisionApplyResult(
                PlanRevisionApplyOutcome.NoChanges, null, effectiveGroup, 0, lockedIds.Count);

        var changeCount = CountDefinitionChanges(currentGroup, effectiveGroup);
        var projected = PendingDecomposePlanAdapter.ToPlan(
            new PendingDecomposePlan(effectiveRevision, effectiveGroup),
            revisedAt);
        var existingTasks = current.Tasks.ToDictionary(task => task.TaskId, StringComparer.Ordinal);
        var mergedTasks = projected.Tasks.Select(task =>
        {
            if (!existingTasks.TryGetValue(task.TaskId, out var existing)) return task;
            if (lockedIds.Contains(task.TaskId)) return existing;
            if (reopenedIds.Contains(task.TaskId))
            {
                var priorAttempts = existing.AttemptHistory ?? [];
                if (existing.Status != PlanTaskStatus.Pending)
                {
                    priorAttempts = priorAttempts.Append(new PlanTaskAttempt(
                        existing.Status,
                        existing.Commit,
                        existing.CompletedAt,
                        existing.CompletionSummary,
                        "plan-revision",
                        "Task specification changed in an approved plan revision.")).ToArray();
                }
                return task with
                {
                    Status = PlanTaskStatus.Pending,
                    AttemptHistory = priorAttempts,
                };
            }
            return task with
            {
                Status = existing.Status,
                AttemptHistory = existing.AttemptHistory,
                ProofEvidence = existing.ProofEvidence,
                ProvenanceChain = existing.ProvenanceChain,
                Handoff = existing.Handoff,
                VerificationHistory = existing.VerificationHistory,
            };
        }).ToArray();

        var mergedGates = MergeGates(current.ApprovalGates, projected.ApprovalGates, reopenedIds);
        var mergedValidations = MergeValidations(current.Validations ?? [], projected.Validations ?? [], reopenedIds);
        if (reopenedIds.Count > 0)
        {
            mergedGates = mergedGates.Select(gate => gate.AfterTaskIds.Any(reopenedIds.Contains)
                ? gate with
                {
                    Status = PlanGateStatus.Pending,
                    RequestedAt = null,
                    ResolvedAt = null,
                    ResolutionNote = null,
                    NotifiedAt = null,
                    ResolvedBy = null,
                    ProofEvidence = null,
                }
                : gate).ToArray();
            mergedValidations = mergedValidations.Select(validation =>
                validation.AfterTaskIds.Any(reopenedIds.Contains)
                    ? validation with
                    {
                        Status = PlanValidationStatus.Pending,
                        StartedAt = null,
                        CompletedAt = null,
                        ValidatedCommit = null,
                        Summary = null,
                        Evidence = null,
                    }
                    : validation).ToArray();
        }
        var reactivateArchivedPlan = current.LifecycleStatus == PlanLifecycleStatus.Archived &&
                                    mergedTasks.Any(task => task.Status == PlanTaskStatus.Pending);
        var updated = current with
        {
            Revision = effectiveRevision,
            Title = effectiveGroup.GroupTitle,
            Branch = effectiveGroup.Branch,
            Summary = effectiveGroup.Summary,
            Tasks = mergedTasks,
            ApprovalGates = mergedGates,
            Validations = mergedValidations,
            Progress = current.Progress with
            {
                CompletedCount = mergedTasks.Count(task => task.Status == PlanTaskStatus.Complete),
                TotalCount = mergedTasks.Count(task => task.Status != PlanTaskStatus.Superseded),
                ExecutingTaskId = reopenedIds.Count > 0 ? null : current.Progress.ExecutingTaskId,
            },
            HostRevision = effectiveRevision,
            RevisionNumber = Math.Max(1, current.RevisionNumber) + 1,
            RevisedAt = revisedAt,
            LifecycleStatus = reactivateArchivedPlan
                ? PlanLifecycleStatus.Staged
                : reopenedIds.Count > 0
                    ? PlanLifecycleStatus.Interrupted
                    : current.LifecycleStatus,
            InterruptionData = reopenedIds.Count > 0
                ? current.InterruptionData is { } interruption
                    ? interruption with
                    {
                        Reason = "Plan content revision approved; revised work is ready to continue.",
                        RecoveryState = "plan-revision-approved",
                        InterruptedTaskId = reopenedIds.First(),
                    }
                    : new PlanInterruptionData(
                        "Plan content revision approved; revised work is ready to continue.",
                        "plan-revision-approved",
                        0,
                        reopenedIds.First())
                : current.InterruptionData,
            Timestamps = reactivateArchivedPlan
                ? current.Timestamps with { ArchivedAt = null }
                : current.Timestamps,
        };

        return new PlanRevisionApplyResult(
            PlanRevisionApplyOutcome.Applied,
            updated,
            effectiveGroup,
            Math.Max(1, changeCount),
            lockedIds.Count);
    }

    private static DecomposedTaskGroup Canonicalize(DecomposedTaskGroup group) => group with
    {
        Delivery = null,
        HostRevision = null,
    };

    private static int CountDefinitionChanges(DecomposedTaskGroup before, DecomposedTaskGroup after)
    {
        var beforeTasks = before.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        var afterTasks = after.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        var count = beforeTasks.Keys.Union(afterTasks.Keys, StringComparer.Ordinal).Count(id =>
            !beforeTasks.TryGetValue(id, out var left) ||
            !afterTasks.TryGetValue(id, out var right) ||
            !Same(left, right));
        if (!string.Equals(before.GroupTitle, after.GroupTitle, StringComparison.Ordinal)) count++;
        if (!string.Equals(before.Branch, after.Branch, StringComparison.Ordinal)) count++;
        if (!string.Equals(before.Summary, after.Summary, StringComparison.Ordinal)) count++;
        if (!Same(before.ApprovalGates ?? [], after.ApprovalGates ?? [])) count++;
        if (!Same(before.Validations ?? [], after.Validations ?? [])) count++;
        return count;
    }

    private static IReadOnlyList<PlanApprovalGate> MergeGates(
        IReadOnlyList<PlanApprovalGate> existing,
        IReadOnlyList<PlanApprovalGate> projected,
        IReadOnlySet<string> reopenedTaskIds)
    {
        var projectedIds = projected.Select(gate => gate.GateId).ToHashSet(StringComparer.Ordinal);
        var merged = projected.Select(gate =>
        {
            var current = existing.FirstOrDefault(candidate => string.Equals(candidate.GateId, gate.GateId, StringComparison.Ordinal));
            if (gate.AfterTaskIds.Any(reopenedTaskIds.Contains))
                return gate;
            return current is not null && current.Status != PlanGateStatus.Pending ? current : gate;
        }).ToList();
        merged.AddRange(existing.Where(gate =>
            gate.Status != PlanGateStatus.Pending &&
            !projectedIds.Contains(gate.GateId) &&
            !gate.AfterTaskIds.Any(reopenedTaskIds.Contains)));
        return merged;
    }

    private static IReadOnlyList<PlanValidationNode> MergeValidations(
        IReadOnlyList<PlanValidationNode> existing,
        IReadOnlyList<PlanValidationNode> projected,
        IReadOnlySet<string> reopenedTaskIds)
    {
        var projectedIds = projected.Select(node => node.ValidationId).ToHashSet(StringComparer.Ordinal);
        var merged = projected.Select(node =>
        {
            var current = existing.FirstOrDefault(candidate => string.Equals(candidate.ValidationId, node.ValidationId, StringComparison.Ordinal));
            if (node.AfterTaskIds.Any(reopenedTaskIds.Contains))
                return node;
            return current is not null && current.Status != PlanValidationStatus.Pending ? current : node;
        }).ToList();
        merged.AddRange(existing.Where(node =>
            node.Status != PlanValidationStatus.Pending &&
            !projectedIds.Contains(node.ValidationId) &&
            !node.AfterTaskIds.Any(reopenedTaskIds.Contains)));
        return merged;
    }

    private static bool Same<T>(T left, T right) =>
        string.Equals(JsonSerializer.Serialize(left, JsonOptions), JsonSerializer.Serialize(right, JsonOptions), StringComparison.Ordinal);

    private static PlanRevisionApplyResult Invalid(string error, int lockedCount = 0) =>
        new(PlanRevisionApplyOutcome.Invalid, null, null, 0, lockedCount, error);
}
