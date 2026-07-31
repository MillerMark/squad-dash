using System;
using System.Linq;

namespace SquadDash;

internal static class PlanExecutionResumeEnvelope
{
    internal static ActiveLoopExecutionState Create(
        string loopPath,
        string groupId,
        string revision,
        int resumeFromIteration,
        ActiveLoopExecutionState? prior,
        bool reclaimPersistedExecution)
    {
        var canReclaim = reclaimPersistedExecution &&
            prior is not null &&
            string.Equals(prior.DecomposeGroupId, groupId, StringComparison.Ordinal) &&
            string.Equals(prior.DecomposeRevision, revision, StringComparison.Ordinal);
        if (canReclaim)
        {
            return prior! with
            {
                LoopPath = loopPath,
                FilterText = groupId,
                DecomposeGroupId = groupId,
                DecomposeRevision = revision,
                LastCompletedIteration = Math.Max(
                    prior!.LastCompletedIteration,
                    Math.Max(0, resumeFromIteration))
            };
        }

        var history = (prior?.PreviousPlanExecutionAttempts ?? []).ToList();
        if (prior?.PlanExecutionAttempt is { } priorAttempt &&
            string.Equals(priorAttempt.PlanId, groupId, StringComparison.Ordinal))
        {
            history.Add(priorAttempt with
            {
                Status = string.Equals(priorAttempt.Status, "active", StringComparison.Ordinal)
                    ? "interrupted"
                    : priorAttempt.Status
            });
        }

        return new ActiveLoopExecutionState(
            loopPath,
            groupId,
            groupId,
            revision,
            PreviousPlanExecutionAttempts: history,
            LastCompletedIteration: Math.Max(0, resumeFromIteration));
    }
}
