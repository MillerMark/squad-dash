using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Pure-logic helpers for adding, removing, and querying human approval gates on a <see cref="Plan"/>.
/// No WPF or IO dependencies — fully unit-testable.
/// </summary>
internal static class PlanGateManager
{
    /// <summary>
    /// Returns the useful choices for an approval gate's blocking frontier.
    /// Descendants of an already-blocked task are omitted because the gate already
    /// holds them transitively. Existing redundant selections remain visible so a
    /// user can remove them.
    /// </summary>
    internal static IReadOnlyList<DecomposedSubTask> GetApprovalBlockingCandidates(
        DecomposedTaskGroup group,
        DecomposedGate gate)
    {
        var afterSet = (gate.AfterTaskIds ?? []).ToHashSet(StringComparer.Ordinal);
        var reachableFromGate = ExpandSuccessors(group.Tasks, afterSet);
        var beforeSet = (gate.BeforeTaskIds ?? []).ToHashSet(StringComparer.Ordinal);
        var transitivelyBlocked = ExpandSuccessors(group.Tasks, beforeSet);

        return group.Tasks
            .Where(task => reachableFromGate.Contains(task.Id) &&
                           !afterSet.Contains(task.Id) &&
                           (beforeSet.Contains(task.Id) || !transitivelyBlocked.Contains(task.Id)))
            .ToArray();
    }

    private static HashSet<string> ExpandSuccessors(
        IReadOnlyList<DecomposedSubTask> tasks,
        IEnumerable<string> roots)
    {
        var reachable = roots.ToHashSet(StringComparer.Ordinal);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var task in tasks)
            {
                if (!reachable.Contains(task.Id) && task.DependsOn.Any(reachable.Contains))
                {
                    reachable.Add(task.Id);
                    changed = true;
                }
            }
        }

        return reachable;
    }

    /// <summary>
    /// Only a pending gate is still a plan-design choice. Once execution has requested,
    /// approved, or skipped a gate, its boundary and presentation anchor are durable history.
    /// </summary>
    internal static bool CanEditGate(PlanApprovalGate? gate) =>
        gate?.Status == PlanGateStatus.Pending;

    /// <summary>
    /// A human-proof gate can be the only remaining proof contract for a verification task after
    /// capability routing removes that human-only requirement from the task itself. Removing that
    /// gate would leave an invalid, proofless verification task.
    /// </summary>
    internal static bool IsRequiredVerificationProofGate(Plan plan, PlanApprovalGate? gate)
    {
        if (gate?.ProofRequirements is not { Count: > 0 }) return false;

        return plan.Tasks.Any(task =>
            string.Equals(task.ExecutionMode, PlanTaskExecutionMode.Verification, StringComparison.Ordinal) &&
            gate.AfterTaskIds.Contains(task.TaskId, StringComparer.Ordinal) &&
            task.ProofRequirements is not { Count: > 0 } &&
            !plan.ApprovalGates.Any(other =>
                !string.Equals(other.GateId, gate.GateId, StringComparison.Ordinal) &&
                other.ProofRequirements is { Count: > 0 } &&
                other.AfterTaskIds.Contains(task.TaskId, StringComparer.Ordinal)));
    }

    internal static bool CanRemoveGate(Plan plan, PlanApprovalGate? gate) =>
        CanEditGate(gate) && !IsRequiredVerificationProofGate(plan, gate);

    /// <summary>
    /// Review guidance may be clarified until the human resolves the checkpoint. Editing these
    /// fields never changes the boundary, execution status, request timestamps, or gate identity.
    /// </summary>
    internal static bool CanEditReviewContract(PlanApprovalGate? gate) =>
        gate?.Status is PlanGateStatus.Pending or PlanGateStatus.AwaitingApproval;

    /// <summary>
    /// Returns true when an approved gate can still be reverted. Reversibility is scoped to the
    /// gate boundary rather than the whole plan: unrelated same-stage work may be executing, but
    /// none of the tasks controlled by this gate may have started.
    /// </summary>
    internal static bool CanUnapproveGate(Plan plan, string gateId)
    {
        if (PlanLifecycleStatus.IsTerminal(plan.LifecycleStatus)) return false;
        var gate = plan.ApprovalGates.FirstOrDefault(candidate =>
            string.Equals(candidate.GateId, gateId, StringComparison.Ordinal) &&
            candidate.Status == PlanGateStatus.Approved);
        if (gate is null) return false;

        return gate.BeforeTaskIds.All(taskId =>
            plan.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, taskId, StringComparison.Ordinal))?.Status ==
            PlanTaskStatus.Pending);
    }

    /// <summary>
    /// Reverts an approved gate back to AwaitingApproval when the plan has not yet advanced.
    /// Returns plan unchanged when the gate cannot be reverted (<see cref="CanUnapproveGate"/>).
    /// </summary>
    internal static Plan UnapproveGate(Plan plan, string gateId)
    {
        if (!CanUnapproveGate(plan, gateId)) return plan;
        var gates = plan.ApprovalGates.Select(gate =>
            string.Equals(gate.GateId, gateId, StringComparison.Ordinal)
                ? gate with
                {
                    Status        = PlanGateStatus.AwaitingApproval,
                    ResolvedAt    = null,
                    ResolutionNote = null,
                    ResolvedBy    = null,
                }
                : gate).ToArray();
        return plan with
        {
            LifecycleStatus = plan.LifecycleStatus == PlanLifecycleStatus.Approved
                ? PlanLifecycleStatus.AwaitingApproval
                : plan.LifecycleStatus,
            ApprovalGates = gates,
        };
    }

    internal static Plan UpdateReviewContract(
        Plan plan,
        string gateId,
        string message,
        string question,
        IReadOnlyList<PlanTaskProofRequirement>? proofRequirements)
    {
        var normalizedMessage = message.Trim();
        var normalizedQuestion = question.Trim();
        if (normalizedMessage.Length == 0 || normalizedQuestion.Length == 0)
            return plan;

        var changed = false;
        var gates = plan.ApprovalGates.Select(gate =>
        {
            if (!string.Equals(gate.GateId, gateId, StringComparison.Ordinal) ||
                !CanEditReviewContract(gate))
                return gate;

            var normalizedProofs = (proofRequirements ?? [])
                .Where(requirement => !string.IsNullOrWhiteSpace(requirement.Description))
                .Select(requirement => requirement with
                {
                    Description = requirement.Description.Trim(),
                    Question = string.IsNullOrWhiteSpace(requirement.Question)
                        ? null
                        : requirement.Question.Trim(),
                })
                .ToArray();
            IReadOnlyList<PlanTaskProofRequirement>? proofs = normalizedProofs.Length == 0
                ? null
                : normalizedProofs;
            if (string.Equals(gate.Message, normalizedMessage, StringComparison.Ordinal) &&
                string.Equals(gate.Question, normalizedQuestion, StringComparison.Ordinal) &&
                (gate.ProofRequirements ?? []).SequenceEqual(proofs ?? []))
                return gate;

            changed = true;
            return gate with
            {
                Message = normalizedMessage,
                Question = normalizedQuestion,
                ProofRequirements = proofs,
            };
        }).ToArray();
        return changed ? plan with { ApprovalGates = gates } : plan;
    }

    /// <summary>Returns true when <paramref name="taskId"/> has no DependsOn (root task).</summary>
    internal static bool IsRootTask(Plan plan, string taskId)
    {
        var task = plan.Tasks.FirstOrDefault(t => t.TaskId == taskId);
        return task is not null && (task.DependsOn is null || task.DependsOn.Count == 0);
    }

    /// <summary>Returns true when no task depends on <paramref name="taskId"/> (leaf task).</summary>
    internal static bool IsLeafTask(Plan plan, string taskId)
    {
        return !plan.Tasks.Any(t => t.DependsOn.Contains(taskId, StringComparer.Ordinal));
    }

    /// <summary>
    /// Returns true when a task feeds a terminal validation. Such a validation is a meaningful
    /// exit boundary even though no plan task depends on the task.
    /// </summary>
    internal static bool HasFinalValidationAfterTask(Plan plan, string taskId) =>
        (plan.Validations ?? []).Any(validation =>
            validation.BeforeTaskIds.Count == 0 &&
            validation.AfterTaskIds.Contains(taskId, StringComparer.Ordinal));

    /// <summary>Returns true when a gate already exists with the same boundary.</summary>
    internal static bool HasEquivalentGate(Plan plan,
        IReadOnlyList<string> afterIds, IReadOnlyList<string> beforeIds)
    {
        return FindEquivalentGate(plan, afterIds, beforeIds) is not null;
    }

    /// <summary>Returns the gate with the same boundary, or <c>null</c> when none exists.</summary>
    internal static PlanApprovalGate? FindEquivalentGate(Plan plan,
        IReadOnlyList<string> afterIds, IReadOnlyList<string> beforeIds)
    {
        return plan.ApprovalGates.FirstOrDefault(g => SameBoundary(g, afterIds, beforeIds));
    }

    private static bool SameBoundary(PlanApprovalGate gate,
        IReadOnlyList<string> afterIds, IReadOnlyList<string> beforeIds) =>
        gate.AfterTaskIds.OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(afterIds.OrderBy(x => x, StringComparer.Ordinal)) &&
        gate.BeforeTaskIds.OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(beforeIds.OrderBy(x => x, StringComparer.Ordinal));

    private static Plan? RestoreSuppressedEquivalentGate(
        Plan plan,
        IReadOnlyList<string> afterIds,
        IReadOnlyList<string> beforeIds,
        string? presentationAnchor)
    {
        var suppressed = plan.SuppressedApprovalGates ?? [];
        var gate = suppressed.FirstOrDefault(candidate => SameBoundary(candidate, afterIds, beforeIds));
        if (gate is null) return null;

        var restored = gate with
        {
            Status = PlanGateStatus.Pending,
            PlanRevision = plan.Revision,
            PresentationAnchor = presentationAnchor ?? gate.PresentationAnchor,
            Question = ResolveReviewQuestion(
                plan, afterIds, beforeIds, gate.Message, gate.Question),
        };
        var remaining = suppressed
            .Where(candidate => !string.Equals(candidate.GateId, gate.GateId, StringComparison.Ordinal))
            .ToArray();
        return plan with
        {
            ApprovalGates = [..plan.ApprovalGates, restored],
            SuppressedApprovalGates = remaining.Length == 0 ? null : remaining,
        };
    }

    /// <summary>
    /// Adds one approval boundary between arbitrary non-overlapping task sets. Used by graph
    /// affordances such as an ALL join or a milestone between two displayed stages.
    /// </summary>
    internal static Plan AddBoundaryGate(Plan plan,
        IReadOnlyList<string> afterIds, IReadOnlyList<string> beforeIds, string message,
        string? presentationAnchor = null, bool removeSubsumedTaskGates = false,
        string? question = null)
    {
        var knownIds = plan.Tasks.Select(task => task.TaskId).ToHashSet(StringComparer.Ordinal);
        var normalizedAfter = afterIds.Distinct(StringComparer.Ordinal).ToArray();
        var normalizedBefore = beforeIds.Distinct(StringComparer.Ordinal).ToArray();
        var normalizedMessage = message.Trim();
        if (normalizedMessage.Length == 0 ||
            normalizedAfter.Length == 0 || normalizedBefore.Length == 0 ||
            normalizedAfter.Any(id => !knownIds.Contains(id)) ||
            normalizedBefore.Any(id => !knownIds.Contains(id)) ||
            normalizedAfter.Intersect(normalizedBefore, StringComparer.Ordinal).Any() ||
            HasEquivalentGate(plan, normalizedAfter, normalizedBefore))
            return plan;

        var restoredPlan = RestoreSuppressedEquivalentGate(
            plan, normalizedAfter, normalizedBefore, presentationAnchor);
        if (restoredPlan is not null)
            return removeSubsumedTaskGates
                ? SuppressSubsumedTaskGates(restoredPlan, normalizedAfter, normalizedBefore,
                    restoredPlan.ApprovalGates.Last().GateId)
                : restoredPlan;

        var gate = new PlanApprovalGate(
            GateId:        NewGateId(plan),
            Message:       normalizedMessage,
            AfterTaskIds:  normalizedAfter,
            BeforeTaskIds: normalizedBefore,
            Status:        PlanGateStatus.Pending,
            PlanRevision:  plan.Revision,
            PresentationAnchor: presentationAnchor,
            AfterTaskIdsSpecified: true,
            BeforeTaskIdsSpecified: true,
            Question: ResolveReviewQuestion(
                plan, normalizedAfter, normalizedBefore, normalizedMessage, question));

        var withGate = plan with { ApprovalGates = [..plan.ApprovalGates, gate] };
        return removeSubsumedTaskGates
            ? SuppressSubsumedTaskGates(withGate, normalizedAfter, normalizedBefore, gate.GateId)
            : withGate;
    }

    /// <summary>
    /// Extends one pending gate that already controls the same upstream boundary. Entry octagons
    /// for sibling tasks are alternate views of one checkpoint, not independent approval requests.
    /// Ambiguous boundaries with multiple authored gates are deliberately left unchanged.
    /// </summary>
    private static Plan? ExpandCompatiblePendingGate(
        Plan plan,
        IReadOnlyList<string> afterIds,
        IReadOnlyList<string> beforeIds,
        IReadOnlySet<string> eligibleBeforeIds)
    {
        var candidates = plan.ApprovalGates
            .Where(CanEditGate)
            .Where(gate => gate.AfterTaskIds.ToHashSet(StringComparer.Ordinal)
                .SetEquals(afterIds))
            .Where(gate => gate.BeforeTaskIds.Count > 0 &&
                           gate.BeforeTaskIds.All(eligibleBeforeIds.Contains))
            .ToArray();
        if (candidates.Length != 1) return null;

        var candidate = candidates[0];
        var mergedBefore = candidate.BeforeTaskIds
            .Concat(beforeIds)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidate.BeforeTaskIds.ToHashSet(StringComparer.Ordinal).SetEquals(mergedBefore))
            return plan;

        var expanded = candidate with
        {
            BeforeTaskIds = mergedBefore,
            PlanRevision = plan.Revision,
            BeforeTaskIdsSpecified = true,
            Question = ResolveReviewQuestion(
                plan, candidate.AfterTaskIds, mergedBefore, candidate.Message, candidate.Question),
        };
        return plan with
        {
            ApprovalGates = plan.ApprovalGates
                .Select(gate => string.Equals(gate.GateId, candidate.GateId, StringComparison.Ordinal)
                    ? expanded
                    : gate)
                .ToArray(),
        };
    }

    private static string ResolveReviewQuestion(
        Plan plan,
        IReadOnlyList<string> afterIds,
        IReadOnlyList<string> beforeIds,
        string message,
        string? question)
    {
        if (!string.IsNullOrWhiteSpace(question)) return question.Trim();

        static string QuoteTitles(Plan source, IReadOnlyList<string> ids) => string.Join(
            ", ",
            ids.Select(id => $"\"{source.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, id, StringComparison.Ordinal))?.Title ?? id}\""));

        var completed = QuoteTitles(plan, afterIds);
        if (beforeIds.Count == 0)
            return $"Do you approve the completed work for {completed}?";

        var upcoming = QuoteTitles(plan, beforeIds);
        return $"Is the completed work for {completed} ready for work to begin on {upcoming}?";
    }

    private static Plan SuppressSubsumedTaskGates(
        Plan plan,
        IReadOnlyCollection<string> groupAfter,
        IReadOnlyCollection<string> groupBefore,
        string retainedGateId)
    {
        var removed = plan.ApprovalGates
            .Where(existing => !string.Equals(existing.GateId, retainedGateId, StringComparison.Ordinal) &&
                               CanEditGate(existing) &&
                               IsSubsumedTaskGate(plan, existing, groupAfter, groupBefore))
            .ToArray();
        if (removed.Length == 0) return plan;

        var removedIds = removed.Select(gate => gate.GateId).ToHashSet(StringComparer.Ordinal);
        var suppressed = (plan.SuppressedApprovalGates ?? [])
            .Concat(removed)
            .GroupBy(gate => gate.GateId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToArray();
        return plan with
        {
            ApprovalGates = plan.ApprovalGates
                .Where(gate => !removedIds.Contains(gate.GateId))
                .ToArray(),
            SuppressedApprovalGates = suppressed,
        };
    }

    private static bool IsSubsumedTaskGate(Plan plan, PlanApprovalGate existing,
        IReadOnlyCollection<string> groupAfter, IReadOnlyCollection<string> groupBefore)
    {
        // A task exit/before gate is redundant only when the larger boundary covers every
        // endpoint it controls. A task with an additional edge therefore keeps its own gate.
        var taskExit = existing.AfterTaskIds.Count == 1 &&
            plan.Tasks.Where(task => task.DependsOn.Contains(existing.AfterTaskIds[0], StringComparer.Ordinal))
                .Select(task => task.TaskId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(existing.BeforeTaskIds);
        var taskEntry = existing.BeforeTaskIds.Count == 1 &&
            plan.Tasks.FirstOrDefault(task => task.TaskId == existing.BeforeTaskIds[0]) is { } entryTask &&
            entryTask.DependsOn.ToHashSet(StringComparer.Ordinal).SetEquals(existing.AfterTaskIds);
        var taskScoped = taskExit || taskEntry;
        if (!taskScoped) return false;
        var groupGate = new PlanApprovalGate(
            "coverage", "coverage", groupAfter.ToArray(), groupBefore.ToArray(), PlanGateStatus.Pending);
        return PlanGateVisualizationPolicy.CompletelyCovers(
            plan.Tasks, groupGate, existing.AfterTaskIds, existing.BeforeTaskIds);
    }

    internal static Plan SetPresentationAnchor(Plan plan, string gateId, string presentationAnchor)
    {
        var changed = false;
        var gates = plan.ApprovalGates.Select(gate =>
        {
            if (!string.Equals(gate.GateId, gateId, StringComparison.Ordinal) ||
                !CanEditGate(gate) ||
                string.Equals(gate.PresentationAnchor, presentationAnchor, StringComparison.Ordinal))
                return gate;
            changed = true;
            return gate with { PresentationAnchor = presentationAnchor };
        }).ToArray();
        return changed ? plan with { ApprovalGates = gates } : plan;
    }

    /// <summary>Generates next stable gate ID: "{planId}-GATE-001", "...GATE-002", etc.</summary>
    internal static string NewGateId(Plan plan)
    {
        var prefix = $"{plan.PlanId}-GATE-";
        var existingNumbers = plan.ApprovalGates
            .Concat(plan.SuppressedApprovalGates ?? [])
            .Select(g => g.GateId)
            .Where(id => id.StartsWith(prefix, StringComparison.Ordinal))
            .Select(id => id[prefix.Length..])
            .Where(suffix => int.TryParse(suffix, out _))
            .Select(int.Parse)
            .ToHashSet();

        var next = 1;
        while (existingNumbers.Contains(next)) next++;
        return $"{prefix}{next:D3}";
    }

    /// <summary>
    /// Adds a gate that blocks <paramref name="taskId"/> until approved.
    /// Gate: AfterTaskIds = task.DependsOn, BeforeTaskIds = [taskId].
    /// Returns plan unchanged when taskId is a root task or an equivalent gate already exists.
    /// </summary>
    internal static Plan AddGateBefore(Plan plan, string taskId, string message, string? question = null)
    {
        if (IsRootTask(plan, taskId)) return plan;

        var task = plan.Tasks.First(t => t.TaskId == taskId);
        IReadOnlyList<string> afterIds  = task.DependsOn ?? [];
        IReadOnlyList<string> beforeIds = [taskId];

        if (HasEquivalentGate(plan, afterIds, beforeIds)) return plan;
        var eligibleBeforeIds = plan.Tasks
            .Where(candidate => candidate.DependsOn.ToHashSet(StringComparer.Ordinal)
                .SetEquals(afterIds))
            .Select(candidate => candidate.TaskId)
            .ToHashSet(StringComparer.Ordinal);
        var expanded = ExpandCompatiblePendingGate(
            plan, afterIds, beforeIds, eligibleBeforeIds);
        if (expanded is not null) return expanded;

        return AddBoundaryGate(plan, afterIds, beforeIds, message, $"task-before:{taskId}", question: question);
    }

    /// <summary>
    /// Adds a gate after <paramref name="taskId"/>, blocking tasks that directly depend on it.
    /// Gate: AfterTaskIds = [taskId], BeforeTaskIds = tasks where DependsOn.Contains(taskId).
    /// A leaf task may still receive a gate when it feeds a final validation. In that case the
    /// gate has an empty task frontier and guards the validation at the same boundary.
    /// Returns plan unchanged for a leaf with no final validation or an equivalent gate.
    /// </summary>
    internal static Plan AddGateAfter(Plan plan, string taskId, string message, string? question = null)
    {
        IReadOnlyList<string> afterIds  = [taskId];
        IReadOnlyList<string> beforeIds = plan.Tasks
            .Where(t => t.DependsOn.Contains(taskId, StringComparer.Ordinal))
            .Select(t => t.TaskId)
            .ToArray();

        if (beforeIds.Count == 0)
        {
            if (!HasFinalValidationAfterTask(plan, taskId) ||
                HasEquivalentGate(plan, afterIds, beforeIds))
                return plan;

            var restoredPlan = RestoreSuppressedEquivalentGate(
                plan, afterIds, beforeIds, $"task-after:{taskId}");
            if (restoredPlan is not null)
                return restoredPlan;

            var terminalGate = new PlanApprovalGate(
                GateId: NewGateId(plan),
                Message: message.Trim(),
                AfterTaskIds: afterIds,
                BeforeTaskIds: beforeIds,
                Status: PlanGateStatus.Pending,
                PlanRevision: plan.Revision,
                PresentationAnchor: $"task-after:{taskId}",
                AfterTaskIdsSpecified: true,
                BeforeTaskIdsSpecified: true,
                Question: ResolveReviewQuestion(plan, afterIds, beforeIds, message, question));
            return plan with { ApprovalGates = [..plan.ApprovalGates, terminalGate] };
        }

        if (HasEquivalentGate(plan, afterIds, beforeIds)) return plan;
        var eligibleBeforeIds = beforeIds.ToHashSet(StringComparer.Ordinal);
        var expanded = ExpandCompatiblePendingGate(
            plan, afterIds, beforeIds, eligibleBeforeIds);
        if (expanded is not null) return expanded;

        return AddBoundaryGate(plan, afterIds, beforeIds, message, $"task-after:{taskId}", question: question);
    }

    /// <summary>
    /// Removes an editable gate from execution while retaining its authored metadata in the
    /// suppressed-gate collection. Re-adding the same boundary restores the original gate.
    /// </summary>
    internal static Plan RemoveGate(Plan plan, string gateId)
    {
        var removed = plan.ApprovalGates.FirstOrDefault(g =>
            string.Equals(g.GateId, gateId, StringComparison.Ordinal));
        if (removed is null || !CanRemoveGate(plan, removed)) return plan;

        var remaining = plan.ApprovalGates
            .Where(g => !string.Equals(g.GateId, gateId, StringComparison.Ordinal))
            .ToArray();
        var suppressed = (plan.SuppressedApprovalGates ?? [])
            .Where(g => !string.Equals(g.GateId, gateId, StringComparison.Ordinal))
            .Append(removed)
            .ToArray();
        return plan with
        {
            ApprovalGates = remaining,
            SuppressedApprovalGates = suppressed,
        };
    }

    /// <summary>
    /// Applies gate-design changes from a viewer snapshot to the latest durable plan while
    /// preserving every gate that has already entered execution history. This is the final
    /// defense against a stale open viewer replacing an approved gate with an older snapshot.
    /// </summary>
    internal static Plan ApplyEditableGateChanges(Plan current, Plan proposed)
    {
        var currentById = current.ApprovalGates.ToDictionary(gate => gate.GateId, StringComparer.Ordinal);
        var proposedById = proposed.ApprovalGates.ToDictionary(gate => gate.GateId, StringComparer.Ordinal);
        var merged = new List<PlanApprovalGate>();

        foreach (var currentGate in current.ApprovalGates)
        {
            if (!CanEditGate(currentGate))
            {
                if (CanEditReviewContract(currentGate) &&
                    proposedById.TryGetValue(currentGate.GateId, out var proposedGuidanceGate))
                {
                    merged.Add(currentGate with
                    {
                        Message = proposedGuidanceGate.Message,
                        Question = proposedGuidanceGate.Question,
                        ProofRequirements = proposedGuidanceGate.ProofRequirements,
                    });
                }
                else
                {
                    merged.Add(currentGate);
                }
                continue;
            }

            // Omitting a still-pending gate is the viewer's supported remove operation.
            if (proposedById.TryGetValue(currentGate.GateId, out var proposedGate) &&
                proposedGate.Status == PlanGateStatus.Pending)
                merged.Add(proposedGate);
        }

        foreach (var proposedGate in proposed.ApprovalGates)
        {
            if (currentById.ContainsKey(proposedGate.GateId) ||
                proposedGate.Status != PlanGateStatus.Pending)
                continue;
            merged.Add(proposedGate);
        }

        var proposedActiveIds = proposed.ApprovalGates
            .Select(gate => gate.GateId)
            .ToHashSet(StringComparer.Ordinal);
        var mergedSuppressed = (current.SuppressedApprovalGates ?? [])
            .Where(gate => !proposedActiveIds.Contains(gate.GateId))
            .Concat(proposed.SuppressedApprovalGates ?? [])
            .GroupBy(gate => gate.GateId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .Where(gate => merged.All(active =>
                !string.Equals(active.GateId, gate.GateId, StringComparison.Ordinal)))
            .ToArray();
        var currentSuppressed = current.SuppressedApprovalGates ?? [];

        if (current.ApprovalGates.SequenceEqual(merged) &&
            currentSuppressed.SequenceEqual(mergedSuppressed))
            return current;
        return current with
        {
            ApprovalGates = merged,
            SuppressedApprovalGates = mergedSuppressed.Length == 0 ? null : mergedSuppressed,
        };
    }

    /// <summary>
    /// Returns true when the gate should trigger a notification — i.e. it has never been notified before.
    /// Guard is based on <see cref="PlanApprovalGate.NotifiedAt"/> being null.
    /// </summary>
    internal static bool ShouldNotifyGateActivation(PlanApprovalGate gate) => gate.NotifiedAt is null;

    // ── DecomposedTaskGroup gate consolidation ───────────────────────────────

    /// <summary>
    /// Returns the gate in <paramref name="group"/> whose <c>AfterTaskIds</c> set equals
    /// <paramref name="afterTaskIds"/> (order-insensitive), or <c>null</c> when none exists.
    /// </summary>
    private static DecomposedGate? FindEquivalentGate(
        DecomposedTaskGroup group,
        IReadOnlyList<string> afterTaskIds)
    {
        return (group.ApprovalGates ?? []).FirstOrDefault(gate =>
            (gate.AfterTaskIds ?? [])
                .OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(afterTaskIds.OrderBy(x => x, StringComparer.Ordinal)));
    }

    /// <summary>
    /// If an equivalent gate already exists in <paramref name="group"/> for the same
    /// approval boundary (same AfterTaskIds set), returns a new group with that gate's
    /// BeforeTaskIds extended or contracted to include/exclude <paramref name="targetTaskId"/>,
    /// preserving GateId, Message, Question, and ProofRequirements.
    /// Otherwise returns <c>null</c> (caller is responsible for adding a new gate).
    /// </summary>
    internal static DecomposedTaskGroup? TryConsolidateGate(
        DecomposedTaskGroup group,
        IReadOnlyList<string> afterTaskIds,
        string targetTaskId,
        bool include)
    {
        var existing = FindEquivalentGate(group, afterTaskIds);
        if (existing is null) return null;

        var currentBefore = existing.BeforeTaskIds ?? [];
        IReadOnlyList<string> newBefore;
        if (include)
        {
            if (currentBefore.Any(id => string.Equals(id, targetTaskId, StringComparison.Ordinal)))
                return group;
            newBefore = [..currentBefore, targetTaskId];
        }
        else
        {
            var contracted = currentBefore
                .Where(id => !string.Equals(id, targetTaskId, StringComparison.Ordinal))
                .ToArray();
            if (contracted.Length == currentBefore.Count)
                return group;
            newBefore = contracted;
        }

        var updated = existing with { BeforeTaskIds = newBefore };
        var gates = (group.ApprovalGates ?? [])
            .Select(gate => string.Equals(gate.GateId, existing.GateId, StringComparison.Ordinal)
                ? updated
                : gate)
            .ToArray();
        return group with { ApprovalGates = gates };
    }

    /// <summary>
    /// Returns the group with an extended or newly created gate for the given approval boundary.
    /// When a gate with the same <paramref name="afterTaskIds"/> boundary already exists, delegates
    /// to <see cref="TryConsolidateGate"/> to extend or contract its <c>BeforeTaskIds</c> while
    /// preserving <c>GateId</c>, <c>Message</c>, <c>Question</c>, and <c>ProofRequirements</c>.
    /// When no equivalent gate exists, adds a new gate using the supplied identity parameters.
    /// The caller always receives a valid group — no null-check branch required.
    /// </summary>
    internal static DecomposedTaskGroup ApplyGateConsolidation(
        DecomposedTaskGroup group,
        IReadOnlyList<string> afterTaskIds,
        string targetTaskId,
        bool include,
        string newGateId,
        string message,
        string question)
    {
        var consolidated = TryConsolidateGate(group, afterTaskIds, targetTaskId, include);
        if (consolidated is not null) return consolidated;

        var newGate = new DecomposedGate(
            GateId:   newGateId,
            Message:  message,
            AfterTaskIds:  afterTaskIds,
            BeforeTaskIds: [targetTaskId],
            Question: question);

        var gates = (group.ApprovalGates ?? []).Append(newGate).ToArray();
        return group with { ApprovalGates = gates };
    }
}
