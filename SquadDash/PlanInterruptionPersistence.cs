using System;

namespace SquadDash;

internal enum PlanInterruptionPersistenceOutcome
{
    NotNeeded,
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
