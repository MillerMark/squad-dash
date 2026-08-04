namespace SquadDash;

/// <summary>
/// Production service that applies proof-provenance-preserving recovery transitions
/// to durable plans. Wraps <see cref="PlanStoreUpdater.ApplyRecoveryWithProvenance"/>
/// with persistence via <see cref="PlanStore"/>.
/// </summary>
internal sealed class PlanRecoveryProvenanceService
{
    private readonly PlanStore _store;

    internal PlanRecoveryProvenanceService(PlanStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Result of a provenance-aware recovery attempt.
    /// </summary>
    internal sealed record RecoveryResult(
        Plan? Plan,
        bool Applied,
        string? BlockReason = null);

    /// <summary>
    /// Applies a single bounded recovery (envelope repair) to the durable plan task.
    /// Captures the provenance of the prior attempt and resets the task to Pending.
    /// Returns <see cref="RecoveryResult.Applied"/> = true on success, or false
    /// when the plan/task was not found or recovery was already exhausted.
    /// </summary>
    internal RecoveryResult ApplyEnvelopeRepair(
        string planId,
        string taskId,
        string? previousAttemptCommit)
    {
        var plan = _store.Load(planId);
        if (plan is null)
            return new RecoveryResult(null, Applied: false, "Plan not found.");

        var task = plan.Tasks.FirstOrDefault(t =>
            string.Equals(t.TaskId, taskId, StringComparison.Ordinal));
        if (task is null)
            return new RecoveryResult(plan, Applied: false, $"Task '{taskId}' not found.");

        // Enforce bounded recovery: if the task already has a provenance chain entry
        // with RecoveryKind "envelope-repair", block further repairs.
        if (task.ProvenanceChain?.Entries.Any(e =>
            string.Equals(e.RecoveryKind, "envelope-repair", StringComparison.Ordinal)) == true)
        {
            return new RecoveryResult(plan, Applied: false,
                $"Task '{taskId}' has already exhausted its envelope-repair allowance. " +
                "The unmet proof requirement cannot be satisfied by repeating the same operation.");
        }

        var updated = PlanStoreUpdater.ApplyRecoveryWithProvenance(
            plan, taskId, previousAttemptCommit, recoveryKind: "envelope-repair");

        _store.Save(updated);
        SquadDashTrace.Write(TraceCategory.General,
            $"PlanRecoveryProvenanceService: applied envelope-repair recovery for task '{taskId}' in plan '{planId}'.");
        return new RecoveryResult(updated, Applied: true);
    }

    /// <summary>
    /// Applies a fresh-attempt recovery to the durable plan task.
    /// Captures the provenance of the contaminated execution and resets the task to Pending.
    /// </summary>
    internal RecoveryResult ApplyFreshAttemptRecovery(
        string planId,
        string taskId,
        string? previousAttemptCommit)
    {
        var plan = _store.Load(planId);
        if (plan is null)
            return new RecoveryResult(null, Applied: false, "Plan not found.");

        var task = plan.Tasks.FirstOrDefault(t =>
            string.Equals(t.TaskId, taskId, StringComparison.Ordinal));
        if (task is null)
            return new RecoveryResult(plan, Applied: false, $"Task '{taskId}' not found.");

        // Enforce bounded recovery: only one fresh-attempt per task
        if (task.ProvenanceChain?.Entries.Any(e =>
            string.Equals(e.RecoveryKind, "fresh-attempt", StringComparison.Ordinal)) == true)
        {
            return new RecoveryResult(plan, Applied: false,
                $"Task '{taskId}' has already exhausted its fresh-attempt allowance. " +
                "The unmet proof requirement cannot be satisfied by repeating the same operation.");
        }

        var updated = PlanStoreUpdater.ApplyRecoveryWithProvenance(
            plan, taskId, previousAttemptCommit, recoveryKind: "fresh-attempt");

        _store.Save(updated);
        SquadDashTrace.Write(TraceCategory.General,
            $"PlanRecoveryProvenanceService: applied fresh-attempt recovery for task '{taskId}' in plan '{planId}'.");
        return new RecoveryResult(updated, Applied: true);
    }
}
