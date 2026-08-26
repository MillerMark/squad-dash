using System;

namespace SquadDash;

internal enum PlanInterruptionPersistenceOutcome
{
    NotNeeded,
    Completed,
    Persisted,
    Failed
}

internal sealed record PlanInterruptionPersistenceResult(
    PlanInterruptionPersistenceOutcome Outcome,
    Plan? Plan = null,
    string? Error = null)
{
    internal static readonly PlanInterruptionPersistenceResult NotNeeded =
        new(PlanInterruptionPersistenceOutcome.NotNeeded);

    internal static PlanInterruptionPersistenceResult Persisted(Plan plan) =>
        new(PlanInterruptionPersistenceOutcome.Persisted, plan);

    internal static PlanInterruptionPersistenceResult Completed(Plan plan) =>
        new(PlanInterruptionPersistenceOutcome.Completed, plan);

    internal static PlanInterruptionPersistenceResult Failed(string error) =>
        new(PlanInterruptionPersistenceOutcome.Failed, Error: error);
}

internal static class PlanInterruptionPersistence
{
    internal static PlanInterruptionPersistenceResult Apply(
        Plan? existing,
        string groupId,
        string? requestedTaskId,
        string reason,
        int loopIteration,
        string? lastCommit,
        bool preferDurableTaskId,
        Func<Plan, (bool Succeeded, string? Error)> persist)
    {
        if (existing is null)
            return PlanInterruptionPersistenceResult.Failed(
                $"Plan {groupId} could not be loaded from the durable plan store.");
        if (existing.LifecycleStatus != PlanLifecycleStatus.Executing)
            return PlanInterruptionPersistenceResult.NotNeeded;

        // A native-loop stop is a transport event, not evidence that accepted plan work is
        // incomplete. Final acceptance can race the stop callback, so terminal readiness wins.
        if (PlanExecutionBoundaryPolicy.IsReadyToComplete(existing))
        {
            var completed = PlanStoreUpdater.ApplyCompleted(existing with { InterruptionData = null });
            try
            {
                var result = persist(completed);
                return result.Succeeded
                    ? PlanInterruptionPersistenceResult.Completed(completed)
                    : PlanInterruptionPersistenceResult.Failed(
                        result.Error ?? "The completed plan could not be saved.");
            }
            catch (Exception ex)
            {
                return PlanInterruptionPersistenceResult.Failed(ex.Message);
            }
        }

        var interruptedTaskId = preferDurableTaskId
            ? existing.Progress.ExecutingTaskId ?? requestedTaskId
            : requestedTaskId ?? existing.Progress.ExecutingTaskId;
        var interrupted = PlanStoreUpdater.ApplyInterrupted(
            existing,
            reason,
            Math.Max(0, loopIteration),
            interruptedTaskId,
            lastCommit: lastCommit);
        try
        {
            var result = persist(interrupted);
            return result.Succeeded
                ? PlanInterruptionPersistenceResult.Persisted(interrupted)
                : PlanInterruptionPersistenceResult.Failed(
                    result.Error ?? "The interrupted plan could not be saved.");
        }
        catch (Exception ex)
        {
            return PlanInterruptionPersistenceResult.Failed(ex.Message);
        }
    }
}
