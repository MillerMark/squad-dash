using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

internal enum PlanExecutionRecoveryAction
{
    RequestRepair,
    StartFreshAttempt,
    Block
}

internal enum PlanExecutionFailureKind
{
    EnvelopeContract,
    HostEvidence,
}

/// <summary>
/// Keeps response-envelope repair separate from terminal host-evidence contamination.
/// Immutable launch evidence can never be repaired in place; it requires a new attempt.
/// Both automatic paths are deliberately bounded and their counters are persisted in the
/// workspace execution envelope.
/// </summary>
internal static class PlanExecutionRecoveryPolicy
{
    internal const int MaxEnvelopeRepairRequestsPerAttempt = 3;
    internal const int MaxRepairRequestsPerAttempt = MaxEnvelopeRepairRequestsPerAttempt;
    internal const int MaxFreshAttemptsPerTask = 1;

    internal static PlanExecutionRecoveryAction Resolve(
        PlanExecutionAttemptState? attempt,
        IReadOnlyList<DecomposedAgentAssignment>? expectedAssignments,
        int repairRequestCount,
        int freshAttemptCount,
        PlanExecutionFailureKind failureKind = PlanExecutionFailureKind.EnvelopeContract)
    {
        // A failed observed worker cannot be repaired by rewriting its result envelope. Start one
        // clean, bounded attempt with fresh launch evidence; only a repeated worker failure stops
        // automatic execution.
        if (HasTerminalWorkerFailure(attempt))
            return freshAttemptCount < MaxFreshAttemptsPerTask
                ? PlanExecutionRecoveryAction.StartFreshAttempt
                : PlanExecutionRecoveryAction.Block;

        if (HasTerminalEvidenceContamination(attempt, expectedAssignments))
            return freshAttemptCount < MaxFreshAttemptsPerTask
                ? PlanExecutionRecoveryAction.StartFreshAttempt
                : PlanExecutionRecoveryAction.Block;

        // Branch, commit ancestry, changed-file attribution, and other host-recorded evidence
        // are trust-boundary failures. Rewording the model envelope cannot make them true.
        if (failureKind == PlanExecutionFailureKind.HostEvidence)
            return PlanExecutionRecoveryAction.Block;

        return repairRequestCount < MaxEnvelopeRepairRequestsPerAttempt
            ? PlanExecutionRecoveryAction.RequestRepair
            : PlanExecutionRecoveryAction.Block;
    }

    internal static bool HasTerminalEvidenceContamination(
        PlanExecutionAttemptState? attempt,
        IReadOnlyList<DecomposedAgentAssignment>? expectedAssignments)
    {
        if (attempt is null)
            return false;
        if (attempt.GenericChildToolCallIds is { Count: > 0 })
            return true;
        return false;
    }

    internal static bool HasTerminalWorkerFailure(PlanExecutionAttemptState? attempt)
    {
        if (attempt is null)
            return false;
        if (!string.IsNullOrWhiteSpace(attempt.GenericPrimaryToolCallId) &&
            attempt.GenericCompletedAt is not null &&
            attempt.GenericSucceeded == false)
            return true;
        return attempt.Assignments.Any(evidence =>
            !string.IsNullOrWhiteSpace(evidence.PrimaryToolCallId) &&
            evidence.CompletedAt is not null &&
            evidence.Succeeded == false);
    }

    internal static IReadOnlyList<PlanExecutionAttemptState> ArchiveRejectedAttempt(
        IReadOnlyList<PlanExecutionAttemptState>? history,
        PlanExecutionAttemptState rejectedAttempt,
        PlanExecutionAttemptState freshAttempt)
        => ArchiveAttempt(history, rejectedAttempt, freshAttempt, "rejected");

    internal static ActiveLoopExecutionState ReplaceInterruptedAttempt(
        ActiveLoopExecutionState execution,
        PlanExecutionAttemptState freshAttempt)
    {
        var interruptedAttempt = execution.PlanExecutionAttempt
            ?? throw new ArgumentException(
                "An interrupted execution must have an active attempt to replace.",
                nameof(execution));
        if (!string.Equals(interruptedAttempt.PlanId, freshAttempt.PlanId, StringComparison.Ordinal) ||
            !string.Equals(interruptedAttempt.TaskId, freshAttempt.TaskId, StringComparison.Ordinal) ||
            !string.Equals(interruptedAttempt.Revision, freshAttempt.Revision, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A fresh execution attempt must target the same plan task and revision.",
                nameof(freshAttempt));
        }

        var history = ArchiveAttempt(
            execution.PreviousPlanExecutionAttempts,
            interruptedAttempt,
            freshAttempt,
            "interrupted");

        return execution with
        {
            PlanExecutionAttempt = freshAttempt,
            PreviousPlanExecutionAttempts = history,
            RecoveryTaskId = freshAttempt.TaskId,
            RecoveryAttemptId = freshAttempt.AttemptId,
            RepairRequestCount = 0,
            PendingRepairResult = null,
        };
    }

    private static IReadOnlyList<PlanExecutionAttemptState> ArchiveAttempt(
        IReadOnlyList<PlanExecutionAttemptState>? history,
        PlanExecutionAttemptState previousAttempt,
        PlanExecutionAttemptState freshAttempt,
        string status)
    {
        if (string.Equals(previousAttempt.AttemptId, freshAttempt.AttemptId, StringComparison.Ordinal))
            throw new ArgumentException("A fresh execution attempt must have a new attempt ID.", nameof(freshAttempt));
        if (freshAttempt.UnexpectedPrimaryToolCallIds is { Count: > 0 } ||
            !string.IsNullOrWhiteSpace(freshAttempt.GenericPrimaryToolCallId) ||
            freshAttempt.Assignments.Any(assignment => !string.IsNullOrWhiteSpace(assignment.PrimaryToolCallId)))
        {
            throw new ArgumentException(
                "A fresh execution attempt cannot inherit launch evidence.",
                nameof(freshAttempt));
        }

        return (history ?? [])
            .Append(previousAttempt with { Status = status })
            .GroupBy(attempt => attempt.AttemptId, StringComparer.Ordinal)
            .Select(attempts => attempts.Last())
            .TakeLast(20)
            .ToArray();
    }
}
