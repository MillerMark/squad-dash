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
    /// rebuilds task statuses from the parsed task items, and sets
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
        var tasks    = MapTasks(group.Tasks, items);
        var progress = BuildProgress(items, executingTaskId);

        if (existing is not null)
        {
            return existing with
            {
                LifecycleStatus  = PlanLifecycleStatus.Executing,
                Tasks            = tasks,
                Progress         = progress,
                InterruptionData = null,
                Timestamps       = existing.Timestamps with
                {
                    StartedAt = existing.Timestamps.StartedAt ?? now,
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
            ApprovalGates:   [],
            Progress:        progress,
            Timestamps:      new PlanTimestamps(
                CreatedAt: now,
                StartedAt: now));
    }

    /// <summary>
    /// Updates progress after a single step result is accepted by SquadDash.
    /// Re-reads item statuses from <paramref name="items"/> and points
    /// <see cref="PlanProgress.ExecutingTaskId"/> at <paramref name="nextExecutingTaskId"/>.
    /// </summary>
    internal static Plan ApplyStepAccepted(
        Plan                    existing,
        IReadOnlyList<TaskItem> items,
        string?                 nextExecutingTaskId)
    {
        var updated = MapTasks(existing.Tasks, items);
        var progress = BuildProgress(items, nextExecutingTaskId);
        return existing with
        {
            Tasks    = updated,
            Progress = progress,
        };
    }

    /// <summary>
    /// Transitions a plan to <see cref="PlanLifecycleStatus.Blocked"/>.
    /// Clears <see cref="PlanProgress.ExecutingTaskId"/> so the panel does not show a stale step.
    /// </summary>
    internal static Plan ApplyBlocked(Plan existing, string? blockedTaskId)
    {
        return existing with
        {
            LifecycleStatus = PlanLifecycleStatus.Blocked,
            Progress        = existing.Progress with { ExecutingTaskId = null },
            Timestamps      = existing.Timestamps with
            {
                InterruptedAt = DateTimeOffset.UtcNow,
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
        string? partialWorkEvidence = null)
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
            PartialWorkEvidence: partialWorkEvidence);
        return existing with
        {
            LifecycleStatus  = PlanLifecycleStatus.Interrupted,
            InterruptionData = interruptionData,
            Progress         = existing.Progress with { ExecutingTaskId = null },
            Timestamps       = existing.Timestamps with { InterruptedAt = now },
        };
    }

    /// <summary>
    /// Transitions a plan to <see cref="PlanLifecycleStatus.Stopped"/>.
    /// Preserves the task history and any interruption context for audit purposes,
    /// but clears the recovery state so no further recovery reminders are shown.
    /// </summary>
    internal static Plan ApplyStopped(Plan existing)
    {
        return existing with
        {
            LifecycleStatus  = PlanLifecycleStatus.Stopped,
            InterruptionData = existing.InterruptionData is null ? null
                : existing.InterruptionData with { RecoveryState = PlanRecoveryState.Ended },
            Progress         = existing.Progress with { ExecutingTaskId = null },
            Timestamps       = existing.Timestamps with { StoppedAt = DateTimeOffset.UtcNow },
        };
    }

    /// <summary>
    /// Transitions a plan to <see cref="PlanLifecycleStatus.Completed"/>.
    /// Sets <see cref="PlanProgress.ExecutingTaskId"/> to null and records the timestamp.
    /// </summary>
    internal static Plan ApplyCompleted(Plan existing)
    {
        return existing with
        {
            LifecycleStatus = PlanLifecycleStatus.Completed,
            Progress        = existing.Progress with { ExecutingTaskId = null },
            Timestamps      = existing.Timestamps with
            {
                CompletedAt = DateTimeOffset.UtcNow,
            },
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

        var updatedGate  = gate with { Status = PlanGateStatus.AwaitingApproval, RequestedAt = DateTimeOffset.UtcNow };
        var updatedGates = existing.ApprovalGates
            .Select(g => string.Equals(g.GateId, gateId, StringComparison.Ordinal) ? updatedGate : g)
            .ToList<PlanApprovalGate>();
        return existing with
        {
            LifecycleStatus = PlanLifecycleStatus.AwaitingApproval,
            ApprovalGates   = updatedGates,
            Progress        = existing.Progress with { ExecutingTaskId = null },
        };
    }

    /// <summary>
    /// Marks the gate <see cref="PlanGateStatus.Approved"/>, sets <see cref="PlanApprovalGate.ResolvedAt"/>
    /// and <see cref="PlanApprovalGate.ResolutionNote"/>. Transitions the plan back to
    /// <see cref="PlanLifecycleStatus.Executing"/> when no other gates are still awaiting approval.
    /// Returns the plan unchanged if <paramref name="gateId"/> is not found or the gate is not
    /// in <see cref="PlanGateStatus.AwaitingApproval"/> status.
    /// </summary>
    internal static Plan ApplyGateApproved(Plan existing, string gateId, string? note)
    {
        var gate = existing.ApprovalGates.FirstOrDefault(g =>
            string.Equals(g.GateId, gateId, StringComparison.Ordinal));
        if (gate is null || gate.Status != PlanGateStatus.AwaitingApproval)
            return existing;

        var updatedGate  = gate with { Status = PlanGateStatus.Approved, ResolvedAt = DateTimeOffset.UtcNow, ResolutionNote = note };
        var updatedGates = existing.ApprovalGates
            .Select(g => string.Equals(g.GateId, gateId, StringComparison.Ordinal) ? updatedGate : g)
            .ToList<PlanApprovalGate>();
        var anyStillAwaiting = updatedGates.Any(g => g.Status == PlanGateStatus.AwaitingApproval);
        return existing with
        {
            LifecycleStatus = anyStillAwaiting ? PlanLifecycleStatus.AwaitingApproval : PlanLifecycleStatus.Executing,
            ApprovalGates   = updatedGates,
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

        return subtasks.Select(sub =>
        {
            byId.TryGetValue(sub.Id, out var item);
            return new PlanTask(
                TaskId:      sub.Id,
                Title:       sub.Title,
                Description: sub.Description,
                DependsOn:   sub.DependsOn,
                Priority:    sub.Priority,
                Status:      MapTaskStatus(item),
                ParentTaskId: sub.ParentTaskId);
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
            return pt with { Status = MapTaskStatus(item) };
        }).ToList();
    }

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
