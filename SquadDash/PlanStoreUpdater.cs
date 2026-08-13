using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Pure-logic helper: transforms <see cref="Plan"/> objects in response to execution
/// lifecycle events.  No UI or I/O dependencies; fully testable without WPF.
/// </summary>
internal static class PlanStoreUpdater
{
    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new <see cref="Plan"/> or updates an existing one when a plan loop starts
    /// or resumes.  Sets lifecycle status to <see cref="PlanLifecycleStatus.Executing"/>,
    /// reconciles task definitions/statuses without discarding accepted-result provenance, and sets
    /// <see cref="PlanProgress.ExecutingTaskId"/> to <paramref name="executingTaskId"/>.
    /// </summary>
    internal static Plan ApplyExecutionStarted(
        Plan?                    existing,
        DecomposedTaskGroup      group,
        string                   revision,
        IReadOnlyList<TaskItem>  items,
        string?                  executingTaskId)
    {
        var now      = DateTimeOffset.UtcNow;
        var tasks    = existing is null
            ? MapTasks(group.Tasks, items)
            : MapTasks(existing.Tasks, group.Tasks, items);
        var progress = BuildProgress(items, executingTaskId);
        var projectedGates = PendingDecomposePlanAdapter.MapApprovalGates(group, revision);
        var projectedValidations = PendingDecomposePlanAdapter.MapValidations(group);

        if (existing is not null)
        {
            var approvalGates = string.Equals(existing.Revision, revision, StringComparison.Ordinal) &&
                                existing.ApprovalGates.Count > 0
                ? existing.ApprovalGates
                : projectedGates;
            var validations = string.Equals(existing.Revision, revision, StringComparison.Ordinal) &&
                              existing.Validations is { Count: > 0 }
                ? existing.Validations
                : projectedValidations;
            return existing with
            {
                Revision         = revision,
                Title            = group.GroupTitle,
                Branch           = group.Branch,
                Summary          = group.Summary,
                LifecycleStatus  = PlanLifecycleStatus.Executing,
                Tasks            = tasks,
                ApprovalGates    = approvalGates,
                Validations      = validations,
                Progress         = progress,
                InterruptionData = null,
                HostRevision     = group.HostRevision ?? revision,
                Timestamps       = existing.Timestamps with
                {
                    StartedAt = existing.Timestamps.StartedAt ?? now,
                    LastRunAt = now,
                },
            };
        }

        return new Plan(
            PlanId:          group.GroupId,
            Revision:        revision,
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: PlanLifecycleStatus.Executing,
            Title:           group.GroupTitle,
            Branch:          group.Branch,
            Summary:         group.Summary,
            Tasks:           tasks,
            ApprovalGates:   projectedGates,
            Progress:        progress,
            Timestamps:      new PlanTimestamps(
                CreatedAt: now,
                StartedAt: now,
                LastRunAt: now),
            HostRevision:    group.HostRevision ?? revision,
            Validations:     projectedValidations);
    }

    /// <summary>
    /// Marks an individual scheduled task as executing. This transition occurs at the start of
    /// every iteration, after the scheduler has selected the task, so live UI never depends on
    /// the earlier plan-start projection having guessed the next task correctly.
    /// </summary>
    internal static Plan ApplyTaskStarted(Plan existing, string taskId)
    {
        if (!existing.Tasks.Any(task =>
                string.Equals(task.TaskId, taskId, StringComparison.Ordinal) &&
                task.Status is not (PlanTaskStatus.Complete or PlanTaskStatus.Superseded)))
            return existing;

        var now = DateTimeOffset.UtcNow;
        return existing with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            Tasks = existing.Tasks.Select(task =>
                string.Equals(task.TaskId, taskId, StringComparison.Ordinal) &&
                task.Status is not (PlanTaskStatus.Complete or PlanTaskStatus.Superseded)
                    ? task with { Status = PlanTaskStatus.Executing }
                    : task).ToArray(),
            Progress = existing.Progress with { ExecutingTaskId = taskId },
            Timestamps = existing.Timestamps with { LastRunAt = now },
        };
    }

    internal static Plan ApplyTaskVerificationPending(
        Plan existing,
        string taskId,
        DecomposeStepResult candidate,
        IReadOnlyList<string> changedFiles)
    {
        var now = DateTimeOffset.UtcNow;
        return existing with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            Tasks = existing.Tasks.Select(task =>
                string.Equals(task.TaskId, taskId, StringComparison.Ordinal)
                    ? task with
                    {
                        Status = PlanTaskStatus.VerificationPending,
                        Handoff = new PlanTaskHandoff(
                            candidate.Commit ?? string.Empty,
                            candidate.Summary,
                            changedFiles,
                            candidate.Verification,
                            now,
                            candidate.DeferredWork),
                    }
                    : task).ToArray(),
            Progress = existing.Progress with { ExecutingTaskId = taskId },
            Timestamps = existing.Timestamps with { LastRunAt = now },
        };
    }

    internal static Plan ApplyTaskVerificationStarted(Plan existing, string taskId)
    {
        var now = DateTimeOffset.UtcNow;
        return existing with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            Tasks = existing.Tasks.Select(task =>
                string.Equals(task.TaskId, taskId, StringComparison.Ordinal)
                    ? task with { Status = PlanTaskStatus.Verifying }
                    : task).ToArray(),
            Progress = existing.Progress with { ExecutingTaskId = taskId },
            Timestamps = existing.Timestamps with { LastRunAt = now },
        };
    }

    internal static Plan ApplyTaskVerificationResult(
        Plan existing,
        string taskId,
        PlanTaskVerificationResult result,
        bool automaticReworkAvailable)
    {
        var now = DateTimeOffset.UtcNow;
        var report = new PlanTaskVerificationReport(
            result.Verdict,
            result.Summary,
            result.ClaimFindings,
            result.MissingOrOverstatedWork,
            result.TestAssessment,
            result.ReworkInstructions,
            result.EvaluatedCommit,
            now);
        var targetStatus = result.Verdict switch
        {
            PlanTaskVerificationVerdict.Accepted => PlanTaskStatus.Executing,
            PlanTaskVerificationVerdict.ReworkRequired when automaticReworkAvailable => PlanTaskStatus.Reworking,
            _ => PlanTaskStatus.HumanReviewRequired,
        };

        return existing with
        {
            LifecycleStatus = result.Verdict == PlanTaskVerificationVerdict.Accepted || automaticReworkAvailable
                ? PlanLifecycleStatus.Executing
                : PlanLifecycleStatus.AwaitingApproval,
            Tasks = existing.Tasks.Select(task =>
            {
                if (!string.Equals(task.TaskId, taskId, StringComparison.Ordinal)) return task;
                var history = (task.VerificationHistory ?? []).Append(report).ToArray();
                return task with { Status = targetStatus, VerificationHistory = history };
            }).ToArray(),
            Progress = existing.Progress with { ExecutingTaskId = taskId },
            Timestamps = existing.Timestamps with { LastRunAt = now },
        };
    }

    /// <summary>
    /// Updates progress and durable result provenance after a single step result is accepted by
    /// SquadDash. Re-reads item statuses from <paramref name="items"/> and points
    /// <see cref="PlanProgress.ExecutingTaskId"/> at <paramref name="nextExecutingTaskId"/>.
    /// </summary>
    internal static Plan ApplyStepAccepted(
        Plan                    existing,
        IReadOnlyList<TaskItem> items,
        string?                 nextExecutingTaskId,
        DecomposeStepResult?    acceptedResult = null)
    {
        var updated = MapTasks(existing.Tasks, items);
        if (acceptedResult is not null)
            updated = ApplyAcceptedResult(updated, acceptedResult);
        var progress = BuildProgress(items, nextExecutingTaskId);
        var plan = existing with
        {
            Tasks    = updated,
            Progress = progress,
            Timestamps = existing.Timestamps with { LastRunAt = DateTimeOffset.UtcNow },
        };

        // Detect tasks that transitioned to Complete in this acceptance and invalidate
        // any passed validations whose afterTaskIds reference them (output change scenario).
        var changedIds = DetectNewlyCompletedTaskIds(existing.Tasks, plan.Tasks);
        plan = InvalidateCoveredValidations(plan, changedIds);

        return ApplyReadyValidations(plan);
    }

    /// <summary>
    /// Accepts task work discovered during interrupted-plan assessment and returns the plan to
    /// the ordinary executing boundary. The caller must then schedule the same next boundary as
    /// a normally accepted task (validation, human approval, another task, or completion).
    /// </summary>
    internal static Plan ApplyAssessedStepAccepted(
        Plan                    existing,
        IReadOnlyList<TaskItem> items,
        string?                 nextExecutingTaskId,
        DecomposeStepResult     acceptedResult)
    {
        var accepted = ApplyStepAccepted(existing, items, nextExecutingTaskId, acceptedResult);
        return accepted with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            InterruptionData = null,
            Progress = accepted.Progress with { ExecutingTaskId = nextExecutingTaskId },
        };
    }

    /// <summary>
    /// Returns the set of task IDs that were already Complete before but now have a different
    /// commit (re-accepted with new work), indicating their outputs may have changed.
    /// </summary>
    private static IReadOnlySet<string>? DetectNewlyCompletedTaskIds(
        IReadOnlyList<PlanTask> before,
        IReadOnlyList<PlanTask> after)
    {
        HashSet<string>? changed = null;
        var beforeByTaskId = before
            .Where(t => t.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded)
            .ToDictionary(t => t.TaskId, t => t.Commit, StringComparer.Ordinal);

        foreach (var task in after)
        {
            if (task.Status is not (PlanTaskStatus.Complete or PlanTaskStatus.Superseded))
                continue;
            if (!beforeByTaskId.TryGetValue(task.TaskId, out var previousCommit))
                continue; // newly completed, not a re-acceptance
            if (string.Equals(previousCommit, task.Commit, StringComparison.Ordinal))
                continue; // same commit, no change
            changed ??= new HashSet<string>(StringComparer.Ordinal);
            changed.Add(task.TaskId);
        }
        return changed;
    }

    /// <summary>
    /// Transitions a plan to <see cref="PlanLifecycleStatus.Blocked"/>.
    /// Clears <see cref="PlanProgress.ExecutingTaskId"/> so the panel does not show a stale step.
    /// </summary>
    internal static Plan ApplyBlocked(Plan existing, string? blockedTaskId)
    {
        var now = DateTimeOffset.UtcNow;
        return existing with
        {
            LifecycleStatus = PlanLifecycleStatus.Blocked,
            Progress        = existing.Progress with { ExecutingTaskId = null },
            Timestamps      = existing.Timestamps with
            {
                InterruptedAt = now,
                LastRunAt = now,
            },
        };
    }

    /// <summary>
    /// Transitions a plan to <see cref="PlanLifecycleStatus.Interrupted"/>.
    /// Records interruption details for restart-safe recovery.
    /// </summary>
    internal static Plan ApplyInterrupted(
        Plan   existing,
        string reason,
        int    loopIteration,
        string? interruptedTaskId   = null,
        string? lastCompletedTaskId = null,
        string? lastCommit          = null,
        IReadOnlyList<string>? affectedPaths     = null,
        string? partialWorkEvidence = null,
        PlanTaskCommitEvidence? taskCommitEvidence = null)
    {
        var now = DateTimeOffset.UtcNow;
        var interruptionData = new PlanInterruptionData(
            Reason:              reason,
            RecoveryState:       PlanRecoveryState.PendingRecovery,
            LoopIteration:       loopIteration,
            InterruptedTaskId:   interruptedTaskId,
            LastCompletedTaskId: lastCompletedTaskId,
            LastCommit:          lastCommit,
            AffectedPaths:       affectedPaths,
            PartialWorkEvidence: partialWorkEvidence,
            TaskCommitEvidence:  taskCommitEvidence);
        return existing with
        {
            LifecycleStatus  = PlanLifecycleStatus.Interrupted,
            InterruptionData = interruptionData,
            Progress         = existing.Progress with { ExecutingTaskId = null },
            Timestamps       = existing.Timestamps with { InterruptedAt = now, LastRunAt = now },
        };
    }

    /// <summary>
    /// Transitions a plan to <see cref="PlanLifecycleStatus.Stopped"/>.
    /// Preserves the task history and any interruption context for audit purposes,
    /// but clears the recovery state so no further recovery reminders are shown.
    /// </summary>
    internal static Plan ApplyStopped(Plan existing)
    {
        var now = DateTimeOffset.UtcNow;
        return existing with
        {
            LifecycleStatus  = PlanLifecycleStatus.Stopped,
            InterruptionData = existing.InterruptionData is null ? null
                : existing.InterruptionData with { RecoveryState = PlanRecoveryState.Ended },
            Progress         = existing.Progress with { ExecutingTaskId = null },
            Timestamps       = existing.Timestamps with { StoppedAt = now, LastRunAt = now },
        };
    }

    /// <summary>
    /// Transitions a plan to <see cref="PlanLifecycleStatus.Completed"/>.
    /// Sets <see cref="PlanProgress.ExecutingTaskId"/> to null and records the timestamp.
    /// </summary>
    internal static Plan ApplyCompleted(Plan existing)
    {
        var now = DateTimeOffset.UtcNow;
        return existing with
        {
            LifecycleStatus = PlanLifecycleStatus.Completed,
            Progress        = existing.Progress with { ExecutingTaskId = null },
            Timestamps      = existing.Timestamps with
            {
                CompletedAt = now,
                LastRunAt = now,
            },
        };
    }

    /// <summary>
    /// Transitions a plan task through recovery (retry or replan), preserving the proof provenance
    /// from the prior attempt. Generates a <see cref="ProofProvenanceEntry"/> for the previous
    /// execution and appends it to the task's provenance chain. Resets the task status to pending.
    /// Returns the plan unchanged if <paramref name="taskId"/> is not found.
    /// </summary>
    internal static Plan ApplyRecoveryWithProvenance(
        Plan    plan,
        string  taskId,
        string? previousAttemptCommit,
        string  recoveryKind)
    {
        var taskIndex = -1;
        for (int i = 0; i < plan.Tasks.Count; i++)
        {
            if (string.Equals(plan.Tasks[i].TaskId, taskId, StringComparison.Ordinal))
            {
                taskIndex = i;
                break;
            }
        }

        if (taskIndex < 0)
            return plan;

        var task = plan.Tasks[taskIndex];

        // Build provenance from the prior attempt using ProofProvenancePresenter
        var provenanceContent = ProofProvenancePresenter.BuildForTask(task);

        var sourceKind = provenanceContent?.SourceKind ?? EvidenceSourceKind.HostRecorded;
        var sourceLabel = provenanceContent?.SourceLabel
            ?? ProofProvenancePresenter.FormatSourceLabel(EvidenceSourceKind.HostRecorded);

        var summary = provenanceContent?.ReturnedSummaries is { Count: > 0 } summaries
            ? string.Join("; ", summaries)
            : task.CompletionSummary;

        var entry = new ProofProvenanceEntry(
            TaskId: taskId,
            SourceLabel: sourceLabel,
            SourceKind: sourceKind.ToString(),
            CommitShortSha: ProofProvenancePresenter.FormatShortSha(previousAttemptCommit ?? task.Commit),
            CommitFullSha: previousAttemptCommit ?? task.Commit,
            Summary: summary,
            RecoveryKind: recoveryKind,
            RecordedAt: DateTimeOffset.UtcNow);

        var existingChain = task.ProvenanceChain ?? ProofProvenanceChain.Empty;
        var updatedChain = existingChain.Append(entry);

        var updatedTask = task with
        {
            Status = PlanTaskStatus.Pending,
            Commit = null,
            CompletedAt = null,
            CompletionSummary = null,
            ProvenanceChain = updatedChain,
        };

        var updatedTasks = plan.Tasks.ToList();
        updatedTasks[taskIndex] = updatedTask;

        var recovered = plan with
        {
            Tasks = updatedTasks,
            Progress = RecalculateProgressAfterRecovery(plan, updatedTasks),
            InterruptionData = plan.InterruptionData is null ? null
                : plan.InterruptionData with { RecoveryState = PlanRecoveryState.RecoveryInProgress },
        };

        // Atomically invalidate dependent validations: any validation node whose
        // AfterTaskIds includes the recovered task must be reset to Pending so it
        // re-runs against the new attempt's output.
        recovered = InvalidateDependentValidationsForRecovery(recovered, taskId);

        return recovered;
    }

    /// <summary>
    /// Transitions all validation nodes that depend on the recovered task to
    /// <see cref="PlanValidationStatus.Stale"/>, preserving their prior evidence for audit.
    /// Called atomically within <see cref="ApplyRecoveryWithProvenance"/>.
    /// </summary>
    internal static Plan InvalidateDependentValidationsForRecovery(Plan plan, string recoveredTaskId)
    {
        if (plan.Validations is not { Count: > 0 })
            return plan;

        var anyReset = false;
        var updated = plan.Validations.Select(validation =>
        {
            if (!validation.AfterTaskIds.Contains(recoveredTaskId))
                return validation;
            if (validation.Status is PlanValidationStatus.Pending or PlanValidationStatus.Stale)
                return validation;

            anyReset = true;
            return validation with
            {
                Status = PlanValidationStatus.Stale,
                Summary = $"Stale: upstream task '{recoveredTaskId}' was recovered.",
                CompletedAt = null,
            };
        }).ToArray();

        return anyReset ? plan with { Validations = updated } : plan;
    }

    /// <summary>
    /// Recalculates <see cref="PlanProgress.CompletedCount"/> after a recovery transition
    /// changes a task from Complete to Pending.
    /// </summary>
    private static PlanProgress RecalculateProgressAfterRecovery(Plan original, IReadOnlyList<PlanTask> updatedTasks)
    {
        var completed = updatedTasks.Count(t =>
            t.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded);
        return original.Progress with { CompletedCount = completed };
    }

    /// <summary>Archives a non-running plan without deleting its durable history.</summary>
    internal static Plan ApplyArchived(Plan existing)
    {
        if (existing.LifecycleStatus is PlanLifecycleStatus.Executing or PlanLifecycleStatus.AwaitingApproval)
            return existing;

        return existing with
        {
            LifecycleStatus = PlanLifecycleStatus.Archived,
            Progress = existing.Progress with { ExecutingTaskId = null },
            Timestamps = existing.Timestamps with { ArchivedAt = DateTimeOffset.UtcNow },
        };
    }

    /// <summary>
    /// Restores an archived plan so a newly proposed definition can enter the normal approval
    /// flow. The most specific durable terminal/recovery state is recovered when possible;
    /// otherwise a never-run collected plan returns to Approved.
    /// </summary>
    internal static Plan ApplyRestoredForRevision(Plan existing)
    {
        if (existing.LifecycleStatus != PlanLifecycleStatus.Archived)
            return existing;

        var restoredStatus = existing.Timestamps.CompletedAt is not null
            ? PlanLifecycleStatus.Completed
            : existing.Timestamps.StoppedAt is not null
                ? PlanLifecycleStatus.Stopped
                : existing.InterruptionData is not null
                    ? PlanLifecycleStatus.Interrupted
                    : PlanLifecycleStatus.Approved;

        return existing with
        {
            LifecycleStatus = restoredStatus,
            Timestamps = existing.Timestamps with { ArchivedAt = null },
        };
    }

    /// <summary>
    /// Transitions the gate to <see cref="PlanGateStatus.AwaitingApproval"/> and the plan to
    /// <see cref="PlanLifecycleStatus.AwaitingApproval"/>. Sets <see cref="PlanApprovalGate.RequestedAt"/>
    /// to now and clears <see cref="PlanProgress.ExecutingTaskId"/>.
    /// Returns the plan unchanged if <paramref name="gateId"/> is not found.
    /// </summary>
    internal static Plan ApplyGateActivated(Plan existing, string gateId)
    {
        var gate = existing.ApprovalGates.FirstOrDefault(g =>
            string.Equals(g.GateId, gateId, StringComparison.Ordinal));
        if (gate is null) return existing;

        var now         = DateTimeOffset.UtcNow;
        var updatedGate = gate with
        {
            Status      = PlanGateStatus.AwaitingApproval,
            RequestedAt = now,
            NotifiedAt  = gate.NotifiedAt ?? now,
        };
        var updatedGates = existing.ApprovalGates
            .Select(g => string.Equals(g.GateId, gateId, StringComparison.Ordinal) ? updatedGate : g)
            .ToList<PlanApprovalGate>();
        return existing with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            ApprovalGates   = updatedGates,
            Progress        = existing.Progress with { ExecutingTaskId = null },
            Timestamps      = existing.Timestamps with { LastRunAt = now },
        };
    }

    /// <summary>
    /// Marks the gate <see cref="PlanGateStatus.Approved"/>, sets <see cref="PlanApprovalGate.ResolvedAt"/>
    /// and <see cref="PlanApprovalGate.ResolutionNote"/>. A plan paused at the checkpoint moves
    /// to <see cref="PlanLifecycleStatus.Approved"/> when no other gates are still awaiting
    /// approval; the host changes it to <see cref="PlanLifecycleStatus.Executing"/> only after
    /// acquiring an execution slot. A plan already running independent work remains executing.
    /// Returns the plan unchanged if <paramref name="gateId"/> is not found or the gate is not
    /// in <see cref="PlanGateStatus.AwaitingApproval"/> status.
    /// </summary>
    internal static Plan ApplyGateApproved(
        Plan existing,
        string gateId,
        string? note,
        string? resolvedBy = null)
    {
        var gate = existing.ApprovalGates.FirstOrDefault(g =>
            string.Equals(g.GateId, gateId, StringComparison.Ordinal));
        if (gate is null || gate.Status != PlanGateStatus.AwaitingApproval)
            return existing;

        var now = DateTimeOffset.UtcNow;
        var updatedGate  = gate with
        {
            Status = PlanGateStatus.Approved,
            ResolvedAt = now,
            ResolutionNote = note,
            ResolvedBy = resolvedBy,
            ProofEvidence = gate.ProofRequirements?.Select(requirement =>
                new PlanTaskProofEvidence(
                    requirement.RequirementId,
                    requirement.ProofType,
                    BuildHumanProofSummary(requirement, resolvedBy, note),
                    [$"squaddash://approval/{existing.PlanId}/{gate.GateId}"])).ToArray(),
        };
        var updatedGates = existing.ApprovalGates
            .Select(g => string.Equals(g.GateId, gateId, StringComparison.Ordinal) ? updatedGate : g)
            .ToList<PlanApprovalGate>();
        var anyStillAwaiting = updatedGates.Any(g => g.Status == PlanGateStatus.AwaitingApproval);
        var lifecycleStatus = anyStillAwaiting
            ? PlanLifecycleStatus.AwaitingApproval
            : existing.LifecycleStatus == PlanLifecycleStatus.AwaitingApproval
                ? PlanLifecycleStatus.Approved
                : existing.LifecycleStatus;
        return existing with
        {
            LifecycleStatus = lifecycleStatus,
            ApprovalGates   = updatedGates,
            Timestamps      = existing.Timestamps with { LastRunAt = now },
        };
    }

    private static string BuildHumanProofSummary(
        PlanTaskProofRequirement requirement,
        string? resolvedBy,
        string? note)
    {
        var identity = string.IsNullOrWhiteSpace(resolvedBy) ? "the human reviewer" : resolvedBy;
        var attestation = string.IsNullOrWhiteSpace(note)
            ? "The checkpoint was explicitly approved."
            : note.Trim();
        return $"{identity} confirmed: {requirement.Description} {attestation}";
    }

    /// <summary>
    /// Auto-transitions pending or stale validation nodes to <see cref="PlanValidationStatus.Ready"/>
    /// when all of their prerequisite tasks (<see cref="PlanValidationNode.AfterTaskIds"/>) have
    /// reached a terminal status. Called automatically by <see cref="ApplyStepAccepted"/> so that
    /// validation readiness is re-evaluated every time a task completes.
    /// </summary>
    internal static Plan ApplyReadyValidations(Plan plan)
    {
        if (plan.Validations is not { Count: > 0 }) return plan;

        var readinessStates = PlanValidationReadinessEvaluator.Evaluate(plan);
        var readyIds = readinessStates
            .Where(state => state.IsReady)
            .Select(state => state.ValidationId)
            .ToHashSet(StringComparer.Ordinal);

        if (readyIds.Count == 0) return plan;

        var updated = plan.Validations.Select(validation =>
            readyIds.Contains(validation.ValidationId) &&
            validation.Status is PlanValidationStatus.Pending or PlanValidationStatus.Stale
                ? validation with { Status = PlanValidationStatus.Ready }
                : validation).ToArray();

        return ReferenceEquals(plan.Validations, updated) ? plan : plan with { Validations = updated };
    }

    internal static Plan ApplyValidationReady(Plan existing, string validationId)
    {
        var validations = existing.Validations ?? [];
        if (!validations.Any(validation =>
                string.Equals(validation.ValidationId, validationId, StringComparison.Ordinal) &&
                validation.Status is PlanValidationStatus.Pending or PlanValidationStatus.Stale))
            return existing;
        return existing with
        {
            Validations = validations.Select(validation =>
                string.Equals(validation.ValidationId, validationId, StringComparison.Ordinal)
                    ? validation with { Status = PlanValidationStatus.Ready }
                    : validation).ToArray(),
        };
    }

    internal static Plan ApplyValidationStarted(Plan existing, string validationId)
    {
        var validations = existing.Validations ?? [];
        if (!validations.Any(validation =>
                string.Equals(validation.ValidationId, validationId, StringComparison.Ordinal) &&
                validation.Status == PlanValidationStatus.Ready))
            return existing;
        var now = DateTimeOffset.UtcNow;
        return existing with
        {
            Validations = validations.Select(validation =>
                string.Equals(validation.ValidationId, validationId, StringComparison.Ordinal)
                    ? validation with
                    {
                        Status = PlanValidationStatus.Validating,
                        StartedAt = now,
                        CompletedAt = null,
                        Summary = null,
                        Evidence = null,
                    }
                    : validation).ToArray(),
            Timestamps = existing.Timestamps with { LastRunAt = now },
        };
    }

    internal static Plan ApplyValidationResult(
        Plan existing,
        string validationId,
        bool passed,
        string summary,
        IReadOnlyList<string> evidence,
        string? validatedCommit)
    {
        var validations = existing.Validations ?? [];
        if (!validations.Any(validation =>
                string.Equals(validation.ValidationId, validationId, StringComparison.Ordinal) &&
                validation.Status is PlanValidationStatus.Ready or PlanValidationStatus.Validating))
            return existing;
        var now = DateTimeOffset.UtcNow;
        var updated = existing with
        {
            Validations = validations.Select(validation =>
                string.Equals(validation.ValidationId, validationId, StringComparison.Ordinal)
                    ? validation with
                    {
                        Status = passed ? PlanValidationStatus.Passed : PlanValidationStatus.Failed,
                        CompletedAt = now,
                        ValidatedCommit = validatedCommit,
                        Summary = summary,
                        Evidence = evidence,
                    }
                    : validation).ToArray(),
            Timestamps = existing.Timestamps with { LastRunAt = now },
        };
        if (!passed)
            return ApplyBlocked(updated, blockedTaskId: null);
        return updated.Tasks.All(task =>
                   task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded) &&
               PlanValidationReadinessEvaluator.AllRequiredPassed(updated)
            ? ApplyCompleted(updated)
            : updated;
    }

    internal static Plan ApplyValidationStale(Plan existing, string validationId, string reason)
    {
        var validations = existing.Validations ?? [];
        if (!validations.Any(validation =>
                string.Equals(validation.ValidationId, validationId, StringComparison.Ordinal) &&
                validation.Status == PlanValidationStatus.Passed))
            return existing;
        return existing with
        {
            Validations = validations.Select(validation =>
                string.Equals(validation.ValidationId, validationId, StringComparison.Ordinal)
                    ? validation with
                    {
                        Status = PlanValidationStatus.Stale,
                        Summary = reason,
                    }
                    : validation).ToArray(),
        };
    }

    /// <summary>
    /// Transitions a failed validation back to <see cref="PlanValidationStatus.Ready"/> for retry.
    /// Distinguishes evidence repair (missing envelope, parse failure) from a genuinely failed
    /// contract. The commit produced by the prerequisite tasks is preserved; only the validation
    /// evidence is cleared for a fresh attempt.
    /// </summary>
    internal static Plan ApplyValidationRetry(Plan existing, string validationId)
    {
        var validations = existing.Validations ?? [];
        if (!validations.Any(validation =>
                string.Equals(validation.ValidationId, validationId, StringComparison.Ordinal) &&
                validation.Status == PlanValidationStatus.Failed))
            return existing;

        var now = DateTimeOffset.UtcNow;
        var updated = existing with
        {
            Validations = validations.Select(validation =>
                string.Equals(validation.ValidationId, validationId, StringComparison.Ordinal)
                    ? validation with
                    {
                        Status = PlanValidationStatus.Ready,
                        StartedAt = null,
                        CompletedAt = null,
                        Summary = null,
                        Evidence = null,
                    }
                    : validation).ToArray(),
            Timestamps = existing.Timestamps with { LastRunAt = now },
        };

        // Unblock the plan if it was blocked solely due to this failed validation.
        if (updated.LifecycleStatus == PlanLifecycleStatus.Blocked)
            updated = updated with { LifecycleStatus = PlanLifecycleStatus.Executing };

        return updated;
    }

    /// <summary>
    /// Detects completed validation verdicts whose covered outputs have changed (i.e., the task
    /// producing the output was reopened or re-accepted with new work) and marks them
    /// <see cref="PlanValidationStatus.Stale"/>. Called at both transitions so stale verdicts
    /// disappear immediately and remain restart-safe.
    /// </summary>
    internal static Plan InvalidateCoveredValidations(Plan plan, IReadOnlySet<string>? changedTaskIds = null)
    {
        if (plan.Validations is not { Count: > 0 } || changedTaskIds is not { Count: > 0 })
            return plan;

        var anyStale = false;
        var updated = plan.Validations.Select(validation =>
        {
            if (validation.Status is not (PlanValidationStatus.Passed or PlanValidationStatus.Failed))
                return validation;
            var covered = validation.AfterTaskIds.Any(changedTaskIds.Contains);
            if (!covered) return validation;
            anyStale = true;
            return validation with
            {
                Status = PlanValidationStatus.Stale,
                Summary = "Covered output changed after validation passed.",
            };
        }).ToArray();

        return anyStale ? plan with { Validations = updated } : plan;
    }

    /// <summary>
    /// Sends one or more completed tasks at an awaiting approval boundary back for another
    /// host-owned attempt. Previous accepted-result provenance is appended to the immutable
    /// attempt history; the current result fields are cleared so normal scheduling can select
    /// the task again. Approved boundaries are deliberately ineligible for this transition.
    /// </summary>
    internal static Plan ApplyGateReworkRequested(
        Plan existing,
        string gateId,
        IReadOnlyCollection<string> taskIds,
        string instructions)
    {
        if (taskIds.Count == 0 || string.IsNullOrWhiteSpace(instructions)) return existing;
        var gate = existing.ApprovalGates.FirstOrDefault(candidate =>
            string.Equals(candidate.GateId, gateId, StringComparison.Ordinal));
        if (gate is null || gate.Status != PlanGateStatus.AwaitingApproval) return existing;

        var targetIds = taskIds.ToHashSet(StringComparer.Ordinal);
        if (targetIds.Any(id => !gate.AfterTaskIds.Contains(id, StringComparer.Ordinal)))
            return existing;
        if (targetIds.Any(id => !existing.Tasks.Any(task =>
                string.Equals(task.TaskId, id, StringComparison.Ordinal) &&
                task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Partial)))
            return existing;

        var updatedTasks = existing.Tasks.Select(task =>
        {
            if (!targetIds.Contains(task.TaskId)) return task;
            var priorAttempt = new PlanTaskAttempt(
                task.Status,
                task.Commit,
                task.CompletedAt,
                task.CompletionSummary,
                "changes-requested",
                instructions.Trim());
            return task with
            {
                Status = PlanTaskStatus.Pending,
                Commit = null,
                CompletedAt = null,
                CompletionSummary = null,
                AttemptHistory = (task.AttemptHistory ?? []).Append(priorAttempt).ToArray(),
            };
        }).ToArray();

        var now = DateTimeOffset.UtcNow;
        var updatedGate = gate with
        {
            Status = PlanGateStatus.Pending,
            RequestedAt = null,
            NotifiedAt = null,
            ResolvedAt = null,
            ResolutionNote = null,
            ResolvedBy = null,
            ProofEvidence = null,
            ReworkCount = gate.ReworkCount + 1,
            LastReworkRequestedAt = now,
            LastReworkInstructions = instructions.Trim(),
        };
        var updatedGates = existing.ApprovalGates
            .Select(candidate => string.Equals(candidate.GateId, gateId, StringComparison.Ordinal)
                ? updatedGate
                : candidate)
            .ToArray();
        var completedCount = updatedTasks.Count(task =>
            task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded);

        var updated = existing with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            Tasks = updatedTasks,
            ApprovalGates = updatedGates,
            Progress = new PlanProgress(completedCount, updatedTasks.Length),
            Timestamps = existing.Timestamps with { LastRunAt = now },
        };

        // Reopening an accepted task immediately invalidates every verdict that was based on
        // that task's previous output. Do this in the same immutable transition so live viewers
        // can never observe a pending task beside a still-passed dependent validation.
        return InvalidateCoveredValidations(updated, targetIds);
    }

    /// <summary>
    /// Records direct conversational work performed while a plan remains paused for human
    /// review. This deliberately leaves task, gate, progress, and lifecycle state unchanged;
    /// the next host-owned assessment uses the activity as context and validates repository
    /// evidence before any plan work is accepted or resumed.
    /// </summary>
    internal static Plan ApplyManualReviewActivity(
        Plan existing,
        IReadOnlyCollection<string> taskIds,
        string summary,
        DateTimeOffset? recordedAt = null)
    {
        if (taskIds.Count == 0 || string.IsNullOrWhiteSpace(summary)) return existing;
        var targetIds = taskIds.ToHashSet(StringComparer.Ordinal);
        if (targetIds.Any(id => !PlanReviewActivityResponseParser.IsActiveTarget(existing, id)))
            return existing;

        var activity = new PlanTaskReviewActivity(
            PlanReviewActivityKind.ManualCorrection,
            summary.Trim(),
            recordedAt ?? DateTimeOffset.UtcNow);
        var changed = false;
        var tasks = existing.Tasks.Select(task =>
        {
            if (!targetIds.Contains(task.TaskId)) return task;
            changed = true;
            return task with
            {
                ReviewActivity = (task.ReviewActivity ?? []).Append(activity).ToArray(),
            };
        }).ToArray();
        return changed ? existing with { Tasks = tasks } : existing;
    }

    /// <summary>
    /// Adds bounded work discovered during human review without withdrawing any accepted task
    /// result. The amendment becomes part of the reviewed side of the same gate, and validations
    /// covering the affected joined result are rescheduled after the amendment.
    /// </summary>
    internal static Plan ApplyGateAmendmentRequested(
        Plan existing,
        string gateId,
        IReadOnlyCollection<string>? relatedTaskIds,
        string title,
        string instructions)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(instructions)) return existing;
        var gate = existing.ApprovalGates.FirstOrDefault(candidate =>
            string.Equals(candidate.GateId, gateId, StringComparison.Ordinal));
        if (gate is null || gate.Status != PlanGateStatus.AwaitingApproval) return existing;

        var related = relatedTaskIds is { Count: > 0 }
            ? relatedTaskIds.Distinct(StringComparer.Ordinal).ToArray()
            : gate.AfterTaskIds.Distinct(StringComparer.Ordinal).ToArray();
        if (related.Length == 0 || related.Any(id =>
                !gate.AfterTaskIds.Contains(id, StringComparer.Ordinal) ||
                !existing.Tasks.Any(task => string.Equals(task.TaskId, id, StringComparison.Ordinal) &&
                    task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Partial)))
            return existing;

        // An approval-boundary amendment is a graph insertion, not an independent sibling task.
        // Only the unstarted side of the boundary may be rewritten. Unrelated plan branches may
        // continue running, but accepted or active downstream history is immutable.
        var beforeIds = gate.BeforeTaskIds.ToHashSet(StringComparer.Ordinal);
        if (beforeIds.Count == 0 || existing.Tasks
                .Where(task => beforeIds.Contains(task.TaskId))
                .Any(task => task.Status != PlanTaskStatus.Pending))
            return existing;

        var taskById = existing.Tasks.ToDictionary(task => task.TaskId, StringComparer.Ordinal);
        if (beforeIds.Any(id => !taskById.ContainsKey(id))) return existing;

        // Gates retain previously reviewed task IDs as durable evidence. Repeated amendments need
        // only the latest frontier, otherwise every amendment accumulates redundant ancestor edges.
        var reviewedIds = gate.AfterTaskIds
            .Where(taskById.ContainsKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var reviewedSet = reviewedIds.ToHashSet(StringComparer.Ordinal);
        bool IsAncestorOf(string possibleAncestor, string taskId)
        {
            var pending = new Stack<string>(taskById[taskId].DependsOn);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                var candidate = pending.Pop();
                if (!visited.Add(candidate)) continue;
                if (string.Equals(candidate, possibleAncestor, StringComparison.Ordinal)) return true;
                if (!taskById.TryGetValue(candidate, out var prerequisite)) continue;
                foreach (var dependency in prerequisite.DependsOn) pending.Push(dependency);
            }
            return false;
        }
        var reviewedFrontier = reviewedIds
            .Where(candidate => !reviewedIds.Any(other =>
                !string.Equals(candidate, other, StringComparison.Ordinal) &&
                IsAncestorOf(candidate, other)))
            .ToArray();
        if (reviewedFrontier.Length == 0) return existing;

        var amendmentNumber = 1;
        string amendmentId;
        do
        {
            amendmentId = $"{existing.PlanId}-AMD-{amendmentNumber:000}";
            amendmentNumber++;
        } while (existing.Tasks.Any(task => string.Equals(task.TaskId, amendmentId, StringComparison.Ordinal)));

        var amendment = new PlanTask(
            amendmentId,
            title.Trim(),
            "This is additional work requested at a human approval boundary. Preserve the accepted " +
            "results of the tasks below; inspect the current repository because the user may already " +
            "have implemented part of this amendment during the approval pause. Complete only the " +
            "remaining bounded work, integrate it with the accumulated result, and return normal task " +
            $"handoff evidence. Requested amendment: {instructions.Trim().ReplaceLineEndings(" ")}",
            reviewedFrontier,
            "high",
            PlanTaskStatus.Pending,
            AgentRoutingMode: "generic",
            GenericAgentReason: "This user-authored boundary amendment may span accepted task contracts and has no preapproved roster assignment.",
            AmendmentGateId: gateId);

        var firstDownstreamIndex = existing.Tasks
            .Select((task, index) => (task, index))
            .Where(entry => beforeIds.Contains(entry.task.TaskId))
            .Min(entry => entry.index);
        var updatedTasks = existing.Tasks
            .Select(task => beforeIds.Contains(task.TaskId)
                ? task with
                {
                    DependsOn = task.DependsOn
                        .Where(id => !reviewedSet.Contains(id))
                        .Append(amendmentId)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                }
                : task)
            .ToList();
        updatedTasks.Insert(firstDownstreamIndex, amendment);
        updatedTasks = AssignMutableDisplayStepLabels(existing.Tasks, updatedTasks).ToList();
        var now = DateTimeOffset.UtcNow;
        var updatedGate = gate with
        {
            AfterTaskIds = gate.AfterTaskIds.Append(amendmentId).Distinct(StringComparer.Ordinal).ToArray(),
            PresentationAnchor = $"task-after:{amendmentId}",
            Status = PlanGateStatus.Pending,
            RequestedAt = null,
            NotifiedAt = null,
            ResolvedAt = null,
            ResolutionNote = null,
            ResolvedBy = null,
            ProofEvidence = null,
            ReworkCount = gate.ReworkCount + 1,
            LastReworkRequestedAt = now,
            LastReworkInstructions = instructions.Trim(),
        };
        var updatedGates = existing.ApprovalGates
            .Select(candidate => string.Equals(candidate.GateId, gateId, StringComparison.Ordinal)
                ? updatedGate
                : candidate)
            .ToArray();

        var affected = gate.AfterTaskIds.ToHashSet(StringComparer.Ordinal);
        var updatedValidations = existing.Validations?.Select(validation =>
        {
            if (!validation.AfterTaskIds.Any(affected.Contains)) return validation;
            return validation with
            {
                AfterTaskIds = validation.AfterTaskIds.Append(amendmentId)
                    .Distinct(StringComparer.Ordinal).ToArray(),
                Status = PlanValidationStatus.Pending,
                StartedAt = null,
                CompletedAt = null,
                ValidatedCommit = null,
                Summary = null,
                Evidence = null,
            };
        }).ToArray();

        var preliminary = existing with
        {
            LifecycleStatus = PlanLifecycleStatus.Executing,
            Tasks = updatedTasks,
            ApprovalGates = updatedGates,
            Validations = updatedValidations,
            Progress = new PlanProgress(
                updatedTasks.Count(task => task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded),
                updatedTasks.Count(task => task.Status != PlanTaskStatus.Superseded)),
            InterruptionData = null,
            Timestamps = existing.Timestamps with
            {
                InterruptedAt = null,
                CompletedAt = null,
                LastRunAt = now,
            },
        };
        var revisedGroup = PendingDecomposePlanAdapter.FromPlan(preliminary).Group;
        var revision = PendingDecomposePlanStore.ComputeRevision(revisedGroup);
        return preliminary with
        {
            Revision = revision,
            HostRevision = revision,
            ApprovalGates = preliminary.ApprovalGates
                .Select(candidate => candidate with { PlanRevision = revision })
                .ToArray(),
        };
    }

    /// <summary>
    /// Preserves labels that have entered execution history and freely renumbers the pending
    /// portions around them. Alphabetic suffixes are used only when there are more pending entries
    /// between two fixed numeric labels than the original numeric gap can hold.
    /// </summary>
    private static IReadOnlyList<PlanTask> AssignMutableDisplayStepLabels(
        IReadOnlyList<PlanTask> originalTasks,
        IReadOnlyList<PlanTask> reorderedTasks)
    {
        var originalById = originalTasks
            .Select((task, index) => (task.TaskId, Label: task.DisplayStepLabel ?? (index + 1).ToString()))
            .ToDictionary(entry => entry.TaskId, entry => entry.Label, StringComparer.Ordinal);
        var labels = new string?[reorderedTasks.Count];
        for (var index = 0; index < reorderedTasks.Count; index++)
        {
            var task = reorderedTasks[index];
            if (task.Status != PlanTaskStatus.Pending && originalById.TryGetValue(task.TaskId, out var fixedLabel))
                labels[index] = fixedLabel;
        }

        var segmentStart = 0;
        while (segmentStart < reorderedTasks.Count)
        {
            if (labels[segmentStart] is not null)
            {
                segmentStart++;
                continue;
            }

            var segmentEnd = segmentStart;
            while (segmentEnd + 1 < reorderedTasks.Count && labels[segmentEnd + 1] is null)
                segmentEnd++;

            var previousNumber = segmentStart > 0 &&
                                 int.TryParse(labels[segmentStart - 1], out var parsedPrevious)
                ? parsedPrevious
                : 0;
            var nextNumber = 0;
            var hasNextNumber = segmentEnd + 1 < reorderedTasks.Count &&
                                int.TryParse(labels[segmentEnd + 1], out nextNumber);
            var numericSlots = hasNextNumber
                ? Math.Max(0, nextNumber - previousNumber - 1)
                : int.MaxValue;

            for (var offset = 0; offset <= segmentEnd - segmentStart; offset++)
            {
                labels[segmentStart + offset] = offset < numericSlots
                    ? (previousNumber + offset + 1).ToString()
                    : $"{Math.Max(previousNumber, nextNumber - 1)}{ToAlphabeticSuffix(offset - numericSlots + 1)}";
            }
            segmentStart = segmentEnd + 1;
        }

        return reorderedTasks.Select((task, index) => task with
        {
            DisplayStepLabel = labels[index] ?? (index + 1).ToString(),
        }).ToArray();
    }

    private static string ToAlphabeticSuffix(int value)
    {
        var chars = new Stack<char>();
        do
        {
            value--;
            chars.Push((char)('A' + value % 26));
            value /= 26;
        } while (value > 0);
        return new string(chars.ToArray());
    }

    internal static bool CanInsertTask(Plan plan, string targetTaskId, bool insertAfter)
    {
        var target = plan.Tasks.FirstOrDefault(task =>
            string.Equals(task.TaskId, targetTaskId, StringComparison.Ordinal));
        if (target is null || target.Status != PlanTaskStatus.Pending) return false;
        if (!insertAfter) return true;

        return plan.Tasks
            .Where(task => task.DependsOn.Contains(targetTaskId, StringComparer.Ordinal))
            .All(task => task.Status == PlanTaskStatus.Pending);
    }

    /// <summary>
    /// Inserts user-authored work into the still-mutable plan graph. This operation is valid while
    /// a different task is executing, but it never rewrites a task that has started or completed.
    /// </summary>
    internal static Plan ApplyTaskInserted(
        Plan existing,
        string targetTaskId,
        bool insertAfter,
        string title,
        string description)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description) ||
            !CanInsertTask(existing, targetTaskId, insertAfter))
            return existing;

        var targetIndex = existing.Tasks.ToList().FindIndex(task =>
            string.Equals(task.TaskId, targetTaskId, StringComparison.Ordinal));
        if (targetIndex < 0) return existing;
        var target = existing.Tasks[targetIndex];
        var insertionNumber = 1;
        string insertionId;
        do
        {
            insertionId = $"{existing.PlanId}-INS-{insertionNumber:000}";
            insertionNumber++;
        } while (existing.Tasks.Any(task => string.Equals(task.TaskId, insertionId, StringComparison.Ordinal)));

        var insertion = new PlanTask(
            insertionId,
            title.Trim(),
            description.Trim(),
            insertAfter ? [targetTaskId] : target.DependsOn.ToArray(),
            "high",
            PlanTaskStatus.Pending,
            AgentRoutingMode: "generic",
            GenericAgentReason: "This task was added interactively after plan approval and has no preapproved roster assignment.");

        var immediateDependents = existing.Tasks
            .Where(task => task.DependsOn.Contains(targetTaskId, StringComparer.Ordinal))
            .Select(task => task.TaskId)
            .ToHashSet(StringComparer.Ordinal);
        var tasks = existing.Tasks.Select(task =>
        {
            if (!insertAfter && string.Equals(task.TaskId, targetTaskId, StringComparison.Ordinal))
                return task with { DependsOn = [insertionId] };
            if (insertAfter && immediateDependents.Contains(task.TaskId))
                return task with
                {
                    DependsOn = task.DependsOn
                        .Select(id => string.Equals(id, targetTaskId, StringComparison.Ordinal)
                            ? insertionId
                            : id)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                };
            return task;
        }).ToList();
        tasks.Insert(targetIndex + (insertAfter ? 1 : 0), insertion);
        tasks = AssignMutableDisplayStepLabels(existing.Tasks, tasks).ToList();

        IReadOnlyList<string> MoveBeforeBoundary(IReadOnlyList<string> ids) =>
            ids.Select(id => string.Equals(id, targetTaskId, StringComparison.Ordinal) ? insertionId : id)
                .Distinct(StringComparer.Ordinal).ToArray();
        var gates = existing.ApprovalGates.Select(gate =>
        {
            if (!insertAfter && gate.BeforeTaskIds.Contains(targetTaskId, StringComparer.Ordinal))
                return gate with { BeforeTaskIds = MoveBeforeBoundary(gate.BeforeTaskIds) };
            if (insertAfter && gate.AfterTaskIds.Contains(targetTaskId, StringComparer.Ordinal))
            {
                var anchor = string.Equals(gate.PresentationAnchor, $"task-after:{targetTaskId}", StringComparison.Ordinal)
                    ? $"task-after:{insertionId}"
                    : gate.PresentationAnchor;
                return gate with
                {
                    AfterTaskIds = gate.AfterTaskIds.Append(insertionId)
                        .Distinct(StringComparer.Ordinal).ToArray(),
                    PresentationAnchor = anchor,
                };
            }
            return gate;
        }).ToArray();

        var validations = existing.Validations?.Select(validation =>
        {
            if (!insertAfter && validation.BeforeTaskIds.Contains(targetTaskId, StringComparer.Ordinal))
                return validation with
                {
                    BeforeTaskIds = MoveBeforeBoundary(validation.BeforeTaskIds),
                    Status = PlanValidationStatus.Pending,
                    StartedAt = null,
                    CompletedAt = null,
                    ValidatedCommit = null,
                    Summary = null,
                    Evidence = null,
                };
            if (insertAfter && validation.AfterTaskIds.Contains(targetTaskId, StringComparer.Ordinal))
                return validation with
                {
                    AfterTaskIds = validation.AfterTaskIds.Append(insertionId)
                        .Distinct(StringComparer.Ordinal).ToArray(),
                    Status = PlanValidationStatus.Pending,
                    StartedAt = null,
                    CompletedAt = null,
                    ValidatedCommit = null,
                    Summary = null,
                    Evidence = null,
                };
            return validation;
        }).ToArray();

        var preliminary = existing with
        {
            Tasks = tasks,
            ApprovalGates = gates,
            Validations = validations,
            Progress = new PlanProgress(
                tasks.Count(task => task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded),
                tasks.Count(task => task.Status != PlanTaskStatus.Superseded),
                existing.Progress.ExecutingTaskId),
            Timestamps = existing.Timestamps with { LastRunAt = DateTimeOffset.UtcNow },
        };
        var revision = PendingDecomposePlanStore.ComputeRevision(
            PendingDecomposePlanAdapter.FromPlan(preliminary).Group);
        return preliminary with
        {
            Revision = revision,
            HostRevision = revision,
            ApprovalGates = preliminary.ApprovalGates
                .Select(gate => gate with { PlanRevision = revision }).ToArray(),
        };
    }

    /// <summary>
    /// Repairs a legacy gate response that reopened accepted tasks before the host could express
    /// boundary amendments. This is valid only before a replacement attempt starts.
    /// </summary>
    internal static Plan ConvertUnstartedGateReworkToAmendment(
        Plan existing,
        string gateId,
        IReadOnlyCollection<string> reopenedTaskIds,
        string title,
        string instructions)
    {
        if (reopenedTaskIds.Count == 0) return existing;
        var ids = reopenedTaskIds.ToHashSet(StringComparer.Ordinal);
        var gate = existing.ApprovalGates.FirstOrDefault(candidate =>
            string.Equals(candidate.GateId, gateId, StringComparison.Ordinal));
        if (gate is null || ids.Any(id => !gate.AfterTaskIds.Contains(id, StringComparer.Ordinal)))
            return existing;

        var invalid = existing.Tasks.Where(task => ids.Contains(task.TaskId)).Any(task =>
            task.Status != PlanTaskStatus.Pending ||
            task.AttemptHistory is not { Count: > 0 } history ||
            history[^1].Disposition != "changes-requested");
        if (invalid) return existing;

        var restoredTasks = existing.Tasks.Select(task =>
        {
            if (!ids.Contains(task.TaskId)) return task;
            var history = task.AttemptHistory!;
            var accepted = history[^1];
            return task with
            {
                Status = accepted.Status,
                Commit = accepted.Commit,
                CompletedAt = accepted.CompletedAt,
                CompletionSummary = accepted.Summary,
                AttemptHistory = history.Count == 1 ? null : history.Take(history.Count - 1).ToArray(),
            };
        }).ToArray();
        var restoredGate = gate with
        {
            Status = PlanGateStatus.AwaitingApproval,
            ReworkCount = Math.Max(0, gate.ReworkCount - 1),
        };
        var restored = existing with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            Tasks = restoredTasks,
            ApprovalGates = existing.ApprovalGates.Select(candidate =>
                string.Equals(candidate.GateId, gateId, StringComparison.Ordinal) ? restoredGate : candidate).ToArray(),
            Progress = new PlanProgress(
                restoredTasks.Count(task => task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded),
                restoredTasks.Count(task => task.Status != PlanTaskStatus.Superseded)),
            InterruptionData = null,
            Timestamps = existing.Timestamps with { InterruptedAt = null },
        };
        return ApplyGateAmendmentRequested(restored, gateId, null, title, instructions);
    }

    /// <summary>
    /// Marks a gate as ready for approval without transitioning the plan to awaiting-approval.
    /// Sets the gate status to <see cref="PlanGateStatus.AwaitingApproval"/> and records
    /// <see cref="PlanApprovalGate.RequestedAt"/>, but keeps the plan in
    /// <see cref="PlanLifecycleStatus.Executing"/> so ungated tasks continue running.
    /// </summary>
    internal static Plan ApplyGateReady(Plan existing, string gateId)
    {
        var gate = existing.ApprovalGates.FirstOrDefault(g =>
            string.Equals(g.GateId, gateId, StringComparison.Ordinal));
        if (gate is null || gate.Status != PlanGateStatus.Pending) return existing;

        var now = DateTimeOffset.UtcNow;
        var updatedGate = gate with
        {
            Status = PlanGateStatus.AwaitingApproval,
            RequestedAt = now,
            NotifiedAt = gate.NotifiedAt ?? now,
        };
        var updatedGates = existing.ApprovalGates
            .Select(g => string.Equals(g.GateId, gateId, StringComparison.Ordinal) ? updatedGate : g)
            .ToList<PlanApprovalGate>();

        // Keep lifecycle as Executing — the loop continues with ungated work
        return existing with
        {
            ApprovalGates = updatedGates,
            Timestamps = existing.Timestamps with { LastRunAt = now },
        };
    }

    /// <summary>
    /// Clears a generic interruption while handing accepted work or a passed validation off to
    /// a ready human-approval boundary. The approval runtime immediately follows this transition
    /// and owns activation of the gate itself.
    /// </summary>
    internal static Plan ApplyApprovalBoundaryRecovery(Plan existing) => existing with
    {
        LifecycleStatus = PlanLifecycleStatus.Executing,
        InterruptionData = null,
        Progress = existing.Progress with { ExecutingTaskId = null },
    };

    /// <summary>
    /// Resolves all provided ready gate IDs at once and transitions to
    /// <see cref="PlanLifecycleStatus.AwaitingApproval"/> only when used as a full-stop boundary.
    /// Each gate in <paramref name="readyGateIds"/> that is in
    /// <see cref="PlanGateStatus.Pending"/> is moved to <see cref="PlanGateStatus.AwaitingApproval"/>.
    /// The plan lifecycle transitions to AwaitingApproval and clears ExecutingTaskId.
    /// </summary>
    internal static Plan ApplyFullStopAtGates(Plan existing, IReadOnlyList<string> readyGateIds)
    {
        if (readyGateIds.Count == 0) return existing;

        var readySet = readyGateIds.ToHashSet(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var updatedGates = existing.ApprovalGates.Select(g =>
        {
            if (!readySet.Contains(g.GateId) || g.Status != PlanGateStatus.Pending) return g;
            return g with
            {
                Status = PlanGateStatus.AwaitingApproval,
                RequestedAt = now,
                NotifiedAt = g.NotifiedAt ?? now,
            };
        }).ToList<PlanApprovalGate>();

        return existing with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            ApprovalGates = updatedGates,
            Progress = existing.Progress with { ExecutingTaskId = null },
            InterruptionData = null,
            Timestamps = existing.Timestamps with { LastRunAt = now },
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Counts completed and total items to build a <see cref="PlanProgress"/>.</summary>
    internal static PlanProgress BuildProgress(
        IReadOnlyList<TaskItem> items,
        string?                 executingTaskId)
    {
        int completed = items.Count(i => i.IsChecked || i.IsSuperseded);
        return new PlanProgress(
            CompletedCount:  completed,
            TotalCount:      items.Count,
            ExecutingTaskId: executingTaskId);
    }

    /// <summary>
    /// Counts completed tasks from a <see cref="PlanTask"/> list to build a <see cref="PlanProgress"/>.
    /// Used by <see cref="RepairInconsistentState"/> where <see cref="TaskItem"/> data is unavailable.
    /// </summary>
    internal static PlanProgress BuildProgress(
        IReadOnlyList<PlanTask> tasks,
        string?                 executingTaskId)
    {
        var completed = tasks.Count(t =>
            t.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded);
        return new PlanProgress(
            CompletedCount:  completed,
            TotalCount:      tasks.Count,
            ExecutingTaskId: executingTaskId);
    }

    /// <summary>
    /// Detects and repairs impossible plan state combinations that can arise from
    /// interrupted writes. Called on load to ensure the PlanStore is self-consistent.
    /// Safe to call on already-consistent plans — returns the input unchanged when no repair is needed.
    /// </summary>
    /// <remarks>
    /// Repair cases are checked in priority order; the first match is repaired and returned.
    /// Interrupted and Blocked plans are never modified — they have their own recovery flows.
    /// </remarks>
    internal static Plan RepairInconsistentState(Plan plan, IReadOnlyList<TaskItem>? currentItems = null)
    {
        plan = RepairLegacyAmendmentTopology(plan);
        plan = RepairLegacyAmendmentDisplayLabels(plan);
        plan = RepairContradictoryRecoveryAcceptance(plan);
        plan = RepairLostPlanRevisionReopen(plan);

        // Never repair plans that have their own recovery flows.
        if (plan.LifecycleStatus is PlanLifecycleStatus.Interrupted or PlanLifecycleStatus.Blocked)
            return plan;

        // Case A — Completed lifecycle with unfinished tasks.
        // tasks.md was not yet written when ApplyCompleted was saved (or vice versa).
        if (plan.LifecycleStatus == PlanLifecycleStatus.Completed &&
            plan.Tasks.Any(t => t.Status is PlanTaskStatus.Pending or PlanTaskStatus.Executing))
        {
            var repairedTasks = plan.Tasks
                .Select(t => t.Status is PlanTaskStatus.Pending or PlanTaskStatus.Executing
                    ? t with { Status = PlanTaskStatus.Complete }
                    : t)
                .ToList<PlanTask>();
            return plan with
            {
                Tasks    = repairedTasks,
                Progress = plan.Progress with { ExecutingTaskId = null },
            };
        }

        // Case B — Executing lifecycle but all tasks are terminal.
        // ApplyStepResult updated tasks.md for the final step but the PlanStore was not saved.
        if (plan.LifecycleStatus == PlanLifecycleStatus.Executing &&
            plan.Tasks.Count > 0 &&
            plan.Tasks.All(t => t.Status is PlanTaskStatus.Complete or PlanTaskStatus.Failed
                                           or PlanTaskStatus.Partial  or PlanTaskStatus.Superseded))
        {
            if (plan.Tasks.Any(t => t.Status is PlanTaskStatus.Failed or PlanTaskStatus.Partial))
                return ApplyBlocked(plan, blockedTaskId: null);
            // Only complete if all mandatory validations have passed; otherwise stay Executing.
            return PlanValidationReadinessEvaluator.AllRequiredPassed(plan)
                ? ApplyCompleted(plan)
                : plan with { Progress = plan.Progress with { ExecutingTaskId = null } };
        }

        // Case C — Progress count does not match actual task statuses.
        var expectedCompleted = plan.Tasks.Count(t =>
            t.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded);
        if (plan.Progress.CompletedCount != expectedCompleted)
        {
            var repairedProgress = BuildProgress(plan.Tasks, plan.Progress.ExecutingTaskId);
            return plan with { Progress = repairedProgress };
        }

        // Case D — ExecutingTaskId points to a task that is no longer executing.
        if (plan.Progress.ExecutingTaskId is not null)
        {
            var pointedTask = plan.Tasks.FirstOrDefault(t =>
                string.Equals(t.TaskId, plan.Progress.ExecutingTaskId, StringComparison.Ordinal));
            if (pointedTask is null || !PlanTaskStatus.IsWorkInProgress(pointedTask.Status))
                return plan with { Progress = plan.Progress with { ExecutingTaskId = null } };
        }

        return plan;
    }

    /// <summary>
    /// Repairs the short-lived revision bug that rewrote an explicitly reopened task with its
    /// former completed Markdown marker. The audit entry is an exact host-authored signature:
    /// the current attempt has no completion evidence while the displaced attempt was retained
    /// with disposition <c>plan-revision</c>. Stale checkpoints from the preceding definition are
    /// removed and the checkpoint/validation owned by the current revision is reset with the task.
    /// </summary>
    private static Plan RepairLostPlanRevisionReopen(Plan plan)
    {
        if (plan.LifecycleStatus is PlanLifecycleStatus.Interrupted or PlanLifecycleStatus.Blocked ||
            PlanLifecycleStatus.IsTerminal(plan.LifecycleStatus))
            return plan;

        var reopenedIds = plan.Tasks
            .Where(task =>
                task.Status == PlanTaskStatus.Complete &&
                task.Commit is null &&
                task.CompletedAt is null &&
                task.CompletionSummary is null &&
                task.AttemptHistory?.LastOrDefault()?.Disposition == "plan-revision")
            .Select(task => task.TaskId)
            .ToHashSet(StringComparer.Ordinal);
        if (reopenedIds.Count == 0)
            return plan;

        var tasks = plan.Tasks.Select(task => reopenedIds.Contains(task.TaskId)
            ? task with { Status = PlanTaskStatus.Pending }
            : task).ToArray();
        var gates = plan.ApprovalGates
            .Where(gate =>
                !gate.AfterTaskIds.Any(reopenedIds.Contains) ||
                string.IsNullOrWhiteSpace(gate.PlanRevision) ||
                string.Equals(gate.PlanRevision, plan.Revision, StringComparison.Ordinal))
            .Select(gate => gate.AfterTaskIds.Any(reopenedIds.Contains)
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
                : gate)
            .ToArray();
        var validations = (plan.Validations ?? []).Select(validation =>
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
        var firstReopened = plan.Tasks.First(task => reopenedIds.Contains(task.TaskId)).TaskId;
        var now = DateTimeOffset.UtcNow;
        var interruption = (plan.InterruptionData ?? new PlanInterruptionData(
            "An approved plan revision reopened completed work.",
            "plan-revision-approved",
            0,
            firstReopened)) with
        {
            Reason = "An approved plan revision reopened completed work and is ready to continue.",
            RecoveryState = "plan-revision-approved",
            InterruptedTaskId = firstReopened,
        };

        return plan with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            Tasks = tasks,
            ApprovalGates = gates,
            Validations = validations,
            Progress = BuildProgress(tasks, executingTaskId: null),
            InterruptionData = interruption,
            Timestamps = plan.Timestamps with { InterruptedAt = now, LastRunAt = now },
        };
    }

    /// <summary>Records one human-review checkbox without resolving or reopening the gate.</summary>
    internal static Plan ApplyGateHumanReviewSelection(
        Plan existing,
        string gateId,
        string itemId,
        bool isChecked,
        string candidateCommit,
        DateTimeOffset? updatedAt = null)
    {
        if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(candidateCommit))
            return existing;
        var gate = existing.ApprovalGates.FirstOrDefault(candidate =>
            string.Equals(candidate.GateId, gateId, StringComparison.Ordinal));
        if (gate is null || gate.Status is not (PlanGateStatus.Pending or PlanGateStatus.AwaitingApproval))
            return existing;

        var selection = new PlanHumanReviewSelection(
            itemId.Trim(), isChecked, candidateCommit.Trim(), updatedAt ?? DateTimeOffset.UtcNow);
        var selections = (gate.HumanReviewSelections ?? [])
            .Where(existingSelection =>
                !string.Equals(existingSelection.ItemId, selection.ItemId, StringComparison.Ordinal) ||
                !string.Equals(existingSelection.CandidateCommit, selection.CandidateCommit,
                    StringComparison.OrdinalIgnoreCase))
            .Append(selection)
            .ToArray();
        var updatedGate = gate with { HumanReviewSelections = selections };
        return existing with
        {
            ApprovalGates = existing.ApprovalGates
                .Select(candidate => string.Equals(candidate.GateId, gateId, StringComparison.Ordinal)
                    ? updatedGate
                    : candidate)
                .ToArray(),
            Timestamps = existing.Timestamps with { LastRunAt = selection.UpdatedAt },
        };
    }

    /// <summary>
    /// Repairs builds that allowed a generic AI recovery assessment to mark work complete despite
    /// an unresolved independent-verification verdict. Human acceptance remains authoritative; this
    /// repair is limited to completion summaries explicitly produced by AI recovery assessment.
    /// </summary>
    private static Plan RepairContradictoryRecoveryAcceptance(Plan plan)
    {
        var target = plan.Tasks.FirstOrDefault(task =>
            task.Status == PlanTaskStatus.Complete &&
            task.CompletionSummary?.StartsWith("AI-assessed recovery:", StringComparison.Ordinal) == true &&
            task.VerificationHistory?.LastOrDefault() is { } report &&
             !string.Equals(report.Verdict, PlanTaskVerificationVerdict.Accepted, StringComparison.Ordinal));
        if (target is null) return plan;

        var tasks = plan.Tasks.Select(task => string.Equals(task.TaskId, target.TaskId, StringComparison.Ordinal)
            ? task with
            {
                Status = PlanTaskStatus.HumanReviewRequired,
                Commit = null,
                CompletedAt = null,
                CompletionSummary = null,
            }
            : task).ToArray();
        var targetIndex = Array.FindIndex(tasks, task =>
            string.Equals(task.TaskId, target.TaskId, StringComparison.Ordinal));
        var lastAccepted = tasks.Take(Math.Max(0, targetIndex)).LastOrDefault(task =>
            task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded);
        var now = DateTimeOffset.UtcNow;
        var interruption = (plan.InterruptionData ?? new PlanInterruptionData(
            "Recovery acceptance contradicted unresolved verification.",
            PlanRecoveryState.PendingRecovery,
            0)) with
        {
            Reason = $"AI recovery acceptance for {target.TaskId} was withdrawn because independent verification remains unresolved.",
            RecoveryState = PlanRecoveryState.PendingRecovery,
            InterruptedTaskId = target.TaskId,
            LastCompletedTaskId = lastAccepted?.TaskId,
            LastCommit = lastAccepted?.Commit,
            PartialWorkEvidence = target.Handoff?.Summary ?? plan.InterruptionData?.PartialWorkEvidence,
        };
        var repaired = plan with
        {
            LifecycleStatus = PlanLifecycleStatus.Interrupted,
            Tasks = tasks,
            Progress = BuildProgress(tasks, executingTaskId: null),
            InterruptionData = interruption,
            Timestamps = plan.Timestamps with { InterruptedAt = now, LastRunAt = now },
        };
        return InvalidateDependentValidationsForRecovery(repaired, target.TaskId);
    }

    /// <summary>
    /// Repairs labels produced by the original amendment implementation, which appended the new
    /// task number and then shifted the mutable suffix by that number. A repair is safe only when
    /// the prefix is the canonical accepted sequence and no non-amendment task after the insertion
    /// has been accepted. Once later work is accepted, its displayed identity is history.
    /// </summary>
    private static Plan RepairLegacyAmendmentDisplayLabels(Plan plan)
    {
        var firstAmendmentIndex = plan.Tasks.ToList().FindIndex(task =>
            !string.IsNullOrWhiteSpace(task.AmendmentGateId));
        if (firstAmendmentIndex < 0) return plan;

        for (var index = 0; index < firstAmendmentIndex; index++)
        {
            if (!string.Equals(plan.Tasks[index].DisplayStepLabel, (index + 1).ToString(),
                    StringComparison.Ordinal))
                return plan;
        }

        if (plan.Tasks.Skip(firstAmendmentIndex + 1).Any(task =>
                string.IsNullOrWhiteSpace(task.AmendmentGateId) &&
                task.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded))
            return plan;

        var numericLabels = new int[plan.Tasks.Count - firstAmendmentIndex];
        for (var offset = 0; offset < numericLabels.Length; offset++)
        {
            if (!int.TryParse(plan.Tasks[firstAmendmentIndex + offset].DisplayStepLabel,
                    out numericLabels[offset]))
                return plan;
            if (offset > 0 && numericLabels[offset] != numericLabels[offset - 1] + 1)
                return plan;
        }

        var expectedFirstLabel = firstAmendmentIndex + 1;
        if (numericLabels[0] <= expectedFirstLabel) return plan;

        var tasks = plan.Tasks.Select((task, index) => index < firstAmendmentIndex
            ? task
            : task with { DisplayStepLabel = (index + 1).ToString() }).ToArray();
        var preliminary = plan with { Tasks = tasks };
        var revision = PendingDecomposePlanStore.ComputeRevision(
            PendingDecomposePlanAdapter.FromPlan(preliminary).Group);
        return preliminary with
        {
            Revision = revision,
            HostRevision = revision,
            ApprovalGates = preliminary.ApprovalGates.Select(gate =>
                gate with { PlanRevision = revision }).ToArray(),
        };
    }

    /// <summary>
    /// Earlier builds appended approval amendments as sibling tasks. Repair that durable shape on
    /// load when its downstream boundary is still entirely unstarted. Interrupted amendment work
    /// is preserved; only the future graph, primary anchor, and display ordering are corrected.
    /// </summary>
    private static Plan RepairLegacyAmendmentTopology(Plan plan)
    {
        var tasks = plan.Tasks.ToList();
        var gates = plan.ApprovalGates.ToArray();
        var changed = false;

        for (var gateIndex = 0; gateIndex < gates.Length; gateIndex++)
        {
            var gate = gates[gateIndex];
            var amendmentTasks = tasks
                .Where(task => string.Equals(task.AmendmentGateId, gate.GateId, StringComparison.Ordinal))
                .ToArray();
            if (amendmentTasks.Length == 0) continue;

            var beforeIds = gate.BeforeTaskIds.ToHashSet(StringComparer.Ordinal);
            if (beforeIds.Count == 0 || beforeIds.Any(id => !tasks.Any(task =>
                    string.Equals(task.TaskId, id, StringComparison.Ordinal))) || tasks
                    .Where(task => beforeIds.Contains(task.TaskId))
                    .Any(task => task.Status != PlanTaskStatus.Pending))
                continue;

            var latestAmendment = amendmentTasks[^1];
            var topologyIsCurrent = tasks
                .Where(task => beforeIds.Contains(task.TaskId))
                .All(task => task.DependsOn.Contains(latestAmendment.TaskId, StringComparer.Ordinal)) &&
                string.Equals(gate.PresentationAnchor,
                    $"task-after:{latestAmendment.TaskId}", StringComparison.Ordinal) &&
                tasks.IndexOf(latestAmendment) < tasks.FindIndex(task => beforeIds.Contains(task.TaskId));
            if (topologyIsCurrent) continue;

            var amendmentIds = amendmentTasks.Select(task => task.TaskId)
                .ToHashSet(StringComparer.Ordinal);
            var reviewedIds = gate.AfterTaskIds.ToHashSet(StringComparer.Ordinal);
            for (var amendmentIndex = 0; amendmentIndex < amendmentTasks.Length; amendmentIndex++)
            {
                var amendment = amendmentTasks[amendmentIndex];
                var dependencies = amendmentIndex == 0
                    ? amendment.DependsOn.Where(id => !amendmentIds.Contains(id)).ToArray()
                    : [amendmentTasks[amendmentIndex - 1].TaskId];
                var position = tasks.FindIndex(task =>
                    string.Equals(task.TaskId, amendment.TaskId, StringComparison.Ordinal));
                tasks[position] = amendment with { DependsOn = dependencies };
            }

            tasks = tasks.Select(task => beforeIds.Contains(task.TaskId)
                ? task with
                {
                    DependsOn = task.DependsOn
                        .Where(id => !reviewedIds.Contains(id))
                        .Append(latestAmendment.TaskId)
                        .Distinct(StringComparer.Ordinal).ToArray(),
                }
                : task).ToList();
            var orderedAmendments = tasks.Where(task => amendmentIds.Contains(task.TaskId)).ToArray();
            tasks.RemoveAll(task => amendmentIds.Contains(task.TaskId));
            var insertionIndex = tasks.FindIndex(task => beforeIds.Contains(task.TaskId));
            tasks.InsertRange(insertionIndex, orderedAmendments);
            gates[gateIndex] = gate with
            {
                PresentationAnchor = $"task-after:{latestAmendment.TaskId}",
            };
            changed = true;
        }

        if (!changed) return plan;
        tasks = AssignMutableDisplayStepLabels(plan.Tasks, tasks).ToList();
        var preliminary = plan with { Tasks = tasks, ApprovalGates = gates };
        var revision = PendingDecomposePlanStore.ComputeRevision(
            PendingDecomposePlanAdapter.FromPlan(preliminary).Group);
        return preliminary with
        {
            Revision = revision,
            HostRevision = revision,
            ApprovalGates = gates.Select(gate => gate with { PlanRevision = revision }).ToArray(),
        };
    }

    /// <summary>
    /// Maps <paramref name="subtasks"/> to <see cref="PlanTask"/> records,
    /// reading each task's current status from the matching <see cref="TaskItem"/>.
    /// </summary>
    private static IReadOnlyList<PlanTask> MapTasks(
        IReadOnlyList<DecomposedSubTask> subtasks,
        IReadOnlyList<TaskItem>          items)
    {
        var byId = items
            .Where(i => i.TaskId is not null)
            .ToDictionary(i => i.TaskId!, StringComparer.Ordinal);

        return subtasks.Select((sub, index) =>
        {
            byId.TryGetValue(sub.Id, out var item);
            return new PlanTask(
                TaskId:      sub.Id,
                Title:       sub.Title,
                Description: sub.Description,
                DependsOn:   sub.DependsOn,
                Priority:    sub.Priority,
                Status:      MapTaskStatus(item),
                ParentTaskId: sub.ParentTaskId,
                AgentAssignments: MapAgentAssignments(sub.AgentAssignments),
                ParallelEligible: sub.ParallelEligible,
                AgentRoutingMode: sub.AgentRoutingMode,
                GenericAgentReason: sub.GenericAgentReason,
                Outputs: MapOutputs(sub.Outputs),
                Inputs: sub.Inputs,
                ProofRequirements: MapProofRequirements(sub.ProofRequirements),
                DisplayStepLabel: (index + 1).ToString());
        }).ToList();
    }

    /// <summary>
    /// Reconciles the latest plan definition and tasks.md projection into an existing durable
    /// plan without discarding accepted-result provenance already stored on its tasks.
    /// </summary>
    private static IReadOnlyList<PlanTask> MapTasks(
        IReadOnlyList<PlanTask>          existing,
        IReadOnlyList<DecomposedSubTask> subtasks,
        IReadOnlyList<TaskItem>          items)
    {
        var existingById = existing.ToDictionary(task => task.TaskId, StringComparer.Ordinal);
        var itemsById = items
            .Where(item => item.TaskId is not null)
            .ToDictionary(item => item.TaskId!, StringComparer.Ordinal);

        return subtasks.Select((sub, index) =>
        {
            itemsById.TryGetValue(sub.Id, out var item);
            if (!existingById.TryGetValue(sub.Id, out var durable))
                return CreatePlanTask(sub, item) with { DisplayStepLabel = (index + 1).ToString() };

            return durable with
            {
                Title              = sub.Title,
                Description        = sub.Description,
                DependsOn          = sub.DependsOn,
                Priority           = sub.Priority,
                Status             = ReconcileProjectedTaskStatus(durable.Status, item),
                ParentTaskId       = sub.ParentTaskId,
                AgentAssignments   = MapAgentAssignments(sub.AgentAssignments),
                ParallelEligible   = sub.ParallelEligible,
                AgentRoutingMode   = sub.AgentRoutingMode,
                GenericAgentReason = sub.GenericAgentReason,
                Outputs            = MapOutputs(sub.Outputs),
                Inputs             = sub.Inputs,
                ProofRequirements  = MapProofRequirements(sub.ProofRequirements),
                DisplayStepLabel   = durable.DisplayStepLabel ?? (index + 1).ToString(),
            };
        }).ToList();
    }

    /// <summary>
    /// Updates the status of existing <see cref="PlanTask"/> records from fresh item data
    /// without losing any fields (commit, completionSummary, etc.) already on the PlanTask.
    /// </summary>
    private static IReadOnlyList<PlanTask> MapTasks(
        IReadOnlyList<PlanTask>  existing,
        IReadOnlyList<TaskItem>  items)
    {
        var byId = items
            .Where(i => i.TaskId is not null)
            .ToDictionary(i => i.TaskId!, StringComparer.Ordinal);

        return existing.Select(pt =>
        {
            byId.TryGetValue(pt.TaskId, out var item);
            return pt with { Status = ReconcileProjectedTaskStatus(pt.Status, item) };
        }).ToList();
    }

    private static string ReconcileProjectedTaskStatus(string durableStatus, TaskItem? item)
    {
        var projectedStatus = MapTaskStatus(item);
        if (projectedStatus == PlanTaskStatus.Pending &&
            durableStatus is PlanTaskStatus.VerificationPending or PlanTaskStatus.Verifying)
            return durableStatus;
        return projectedStatus;
    }

    private static IReadOnlyList<PlanTask> ApplyAcceptedResult(
        IReadOnlyList<PlanTask> tasks,
        DecomposeStepResult result)
    {
        var completedAt = DateTimeOffset.UtcNow;
        return tasks.Select(task =>
        {
            if (!string.Equals(task.TaskId, result.TaskId, StringComparison.Ordinal))
                return task;

            return task with
            {
                Commit = string.IsNullOrWhiteSpace(result.Commit) ? task.Commit : result.Commit,
                Commits = result.Commits ?? task.Commits,
                CompletedAt = result.Status == "complete"
                    ? task.CompletedAt ?? completedAt
                    : task.CompletedAt,
                CompletionSummary = result.Summary,
                ProofEvidence = result.ProofEvidence?.Select(evidence => new PlanTaskProofEvidence(
                    evidence.RequirementId,
                    evidence.ProofType,
                    evidence.Summary,
                    evidence.Artifacts)).ToArray(),
            };
        }).ToList();
    }

    private static PlanTask CreatePlanTask(DecomposedSubTask sub, TaskItem? item) =>
        new(
            TaskId:             sub.Id,
            Title:              sub.Title,
            Description:        sub.Description,
            DependsOn:          sub.DependsOn,
            Priority:           sub.Priority,
            Status:             MapTaskStatus(item),
            ParentTaskId:       sub.ParentTaskId,
            AgentAssignments:   MapAgentAssignments(sub.AgentAssignments),
            ParallelEligible:   sub.ParallelEligible,
            AgentRoutingMode:   sub.AgentRoutingMode,
            GenericAgentReason: sub.GenericAgentReason,
            Outputs:            MapOutputs(sub.Outputs),
            Inputs:             sub.Inputs,
            ProofRequirements:  MapProofRequirements(sub.ProofRequirements));

    private static IReadOnlyList<PlanTaskOutput>? MapOutputs(
        IReadOnlyList<DecomposedTaskOutput>? outputs) =>
        outputs?.Select(output => new PlanTaskOutput(output.OutputId, output.Description)).ToArray();

    private static IReadOnlyList<PlanTaskProofRequirement>? MapProofRequirements(
        IReadOnlyList<DecomposedTaskProofRequirement>? requirements) =>
        requirements?.Select(requirement => new PlanTaskProofRequirement(
            requirement.RequirementId,
            requirement.ProofType,
            requirement.Description,
            requirement.Question)).ToArray();

    private static IReadOnlyList<PlanAgentAssignment>? MapAgentAssignments(
        IReadOnlyList<DecomposedAgentAssignment>? assignments) =>
        assignments?.Select(assignment => new PlanAgentAssignment(
            assignment.AgentHandle,
            assignment.Role,
            assignment.AllowGenericChildren)).ToArray();

    private static string MapTaskStatus(TaskItem? item)
    {
        if (item is null)          return PlanTaskStatus.Pending;
        if (item.IsChecked)        return PlanTaskStatus.Complete;
        if (item.IsSuperseded)     return PlanTaskStatus.Superseded;
        if (item.IsFailed)         return PlanTaskStatus.Failed;
        if (item.IsPartial)        return PlanTaskStatus.Partial;
        return PlanTaskStatus.Pending;
    }
}
