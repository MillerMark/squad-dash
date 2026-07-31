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

/// <summary>
/// Keeps response-envelope repair separate from terminal host-evidence contamination.
/// Immutable launch evidence can never be repaired in place; it requires a new attempt.
/// Both automatic paths are deliberately bounded and their counters are persisted in the
/// workspace execution envelope.
/// </summary>
internal static class PlanExecutionRecoveryPolicy
{
    internal const int MaxRepairRequestsPerAttempt = 1;
    internal const int MaxFreshAttemptsPerTask = 1;

    internal static PlanExecutionRecoveryAction Resolve(
        PlanExecutionAttemptState? attempt,
        IReadOnlyList<DecomposedAgentAssignment>? expectedAssignments,
        int repairRequestCount,
        int freshAttemptCount)
    {
        if (HasTerminalEvidenceContamination(attempt, expectedAssignments))
            return freshAttemptCount < MaxFreshAttemptsPerTask
                ? PlanExecutionRecoveryAction.StartFreshAttempt
                : PlanExecutionRecoveryAction.Block;

        return repairRequestCount < MaxRepairRequestsPerAttempt
            ? PlanExecutionRecoveryAction.RequestRepair
            : PlanExecutionRecoveryAction.Block;
    }

    internal static bool HasTerminalEvidenceContamination(
        PlanExecutionAttemptState? attempt,
        IReadOnlyList<DecomposedAgentAssignment>? expectedAssignments)
    {
        if (attempt is null)
            return false;
        if (attempt.UnexpectedPrimaryToolCallIds is { Count: > 0 })
            return true;
        if (attempt.GenericChildToolCallIds is { Count: > 0 })
            return true;
        if (!string.IsNullOrWhiteSpace(attempt.GenericPrimaryToolCallId) &&
            (attempt.GenericCompletedAt is null || attempt.GenericSucceeded != true))
            return true;

        return attempt.Assignments.Any(evidence =>
        {
            var assignment = expectedAssignments?.FirstOrDefault(expected =>
                string.Equals(expected.AgentHandle, evidence.AgentHandle, StringComparison.OrdinalIgnoreCase));
            if (evidence.ChildToolCallIds is { Count: > 0 } && assignment?.AllowGenericChildren != true)
                return true;
            if (string.IsNullOrWhiteSpace(evidence.PrimaryToolCallId))
                return false;
            if (evidence.CompletedAt is null || evidence.Succeeded != true)
                return true;
            return evidence.RequiredContextPaths.Any(required =>
                !(evidence.ObservedContextPaths ?? []).Any(observed =>
                    PlanExecutionAttemptState.PathsEqual(required, observed)));
        });
    }

    internal static IReadOnlyList<PlanExecutionAttemptState> ArchiveRejectedAttempt(
        IReadOnlyList<PlanExecutionAttemptState>? history,
        PlanExecutionAttemptState rejectedAttempt,
        PlanExecutionAttemptState freshAttempt)
    {
        if (string.Equals(rejectedAttempt.AttemptId, freshAttempt.AttemptId, StringComparison.Ordinal))
            throw new ArgumentException("A fresh execution attempt must have a new attempt ID.", nameof(freshAttempt));
        if (freshAttempt.UnexpectedPrimaryToolCallIds is { Count: > 0 })
            throw new ArgumentException("A fresh execution attempt cannot inherit unexpected launch evidence.", nameof(freshAttempt));

        return (history ?? [])
            .Append(rejectedAttempt with { Status = "rejected" })
            .GroupBy(attempt => attempt.AttemptId, StringComparer.Ordinal)
            .Select(attempts => attempts.Last())
            .TakeLast(20)
            .ToArray();
    }
}
