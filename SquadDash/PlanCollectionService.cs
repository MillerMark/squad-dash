namespace SquadDash;

/// <summary>
/// Owns the "collection" transition — moving a <see cref="PendingDecomposePlan"/> from
/// transient host state into the durable <see cref="PlanStore"/> as an inactive
/// (Approved) plan.
/// <para>
/// Invariants:
/// <list type="bullet">
///   <item>Collecting is idempotent and revision-safe.</item>
///   <item>Collecting never starts a loop, switches branches, or writes tasks.md.</item>
///   <item>Execution remains a separate explicit transition.</item>
///   <item>Stale Inbox actions cannot mutate a newer revision.</item>
///   <item>Active (non-terminal, non-Approved) plans are never overwritten.</item>
///   <item>After successful collection the pending proposal is cleaned up (best-effort).</item>
/// </list>
/// </para>
/// </summary>
internal sealed class PlanCollectionService
{
    private readonly PlanStore _planStore;
    private readonly PendingDecomposePlanStore? _pendingStore;

    internal PlanCollectionService(PlanStore planStore)
        : this(planStore, pendingStore: null) { }

    internal PlanCollectionService(PlanStore planStore, PendingDecomposePlanStore? pendingStore)
    {
        _planStore = planStore;
        _pendingStore = pendingStore;
    }

    /// <summary>
    /// Result of a collection attempt.
    /// </summary>
    internal sealed record CollectionResult(
        Plan? Plan,
        CollectionOutcome Outcome);

    /// <summary>
    /// Attempts to collect a pending proposal into the durable PlanStore.
    /// Returns the persisted plan on success or idempotent hit, or null with an
    /// error outcome when the revision is stale or an active plan blocks collection.
    /// </summary>
    internal CollectionResult Collect(PendingDecomposePlan pending, DateTimeOffset timestamp)
    {
        if (pending is null)
            throw new ArgumentNullException(nameof(pending));
        if (pending.Group is null)
            throw new ArgumentException("Pending plan has no group.", nameof(pending));

        var planId = pending.Group.GroupId;
        var incomingRevision = pending.Revision;

        // Check for an existing plan with the same ID.
        var existing = _planStore.Load(planId);
        if (existing is not null)
        {
            // Active plan protection: an executing/interrupted/blocked/awaiting-approval plan
            // must never be overwritten by a collect action — even with matching revision.
            if (IsActivePlan(existing))
            {
                SquadDashTrace.Write(TraceCategory.General,
                    $"PlanCollectionService: active plan blocked collection for '{planId}' " +
                    $"(status={existing.LifecycleStatus}).");
                return new CollectionResult(null, CollectionOutcome.ActivePlanBlocked);
            }

            // Idempotent: same revision means already collected — return as-is.
            if (string.Equals(existing.Revision, incomingRevision, StringComparison.Ordinal))
            {
                SquadDashTrace.Write(TraceCategory.General,
                    $"PlanCollectionService: idempotent collect for '{planId}' (revision {incomingRevision}).");
                CleanupPending(planId);
                return new CollectionResult(existing, CollectionOutcome.AlreadyCollected);
            }

            // Stale: the existing plan has a different (newer) revision.
            SquadDashTrace.Write(TraceCategory.General,
                $"PlanCollectionService: stale revision rejected for '{planId}' " +
                $"(incoming={incomingRevision}, stored={existing.Revision}).");
            return new CollectionResult(null, CollectionOutcome.StaleRevisionRejected);
        }

        // Convert to durable Plan with Approved status.
        var plan = PendingDecomposePlanAdapter.ToPlan(pending, timestamp) with
        {
            LifecycleStatus = PlanLifecycleStatus.Approved,
            Timestamps = new PlanTimestamps(
                CreatedAt: timestamp,
                AcceptedAt: timestamp),
        };

        _planStore.Save(plan);

        SquadDashTrace.Write(TraceCategory.General,
            $"PlanCollectionService: collected '{planId}' (revision {incomingRevision}).");

        CleanupPending(planId);

        return new CollectionResult(plan, CollectionOutcome.Collected);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the plan is in an active non-terminal status
    /// that must not be overwritten by a collect action. Approved plans are allowed to be
    /// hit idempotently; terminal plans are no longer blocking.
    /// </summary>
    private static bool IsActivePlan(Plan plan) =>
        plan.LifecycleStatus is not PlanLifecycleStatus.Approved
            && !PlanLifecycleStatus.IsTerminal(plan.LifecycleStatus)
            && plan.LifecycleStatus is not PlanLifecycleStatus.Staged;

    /// <summary>
    /// Best-effort removal of the pending proposal from transient storage.
    /// Collection succeeds even if delete fails — the pending file is stale state
    /// and will be ignored on next load (revision mismatch).
    /// </summary>
    private void CleanupPending(string planId)
    {
        if (_pendingStore is null) return;
        try
        {
            _pendingStore.Delete(planId);
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"PlanCollectionService: pending cleanup failed for '{planId}': {ex.Message}");
        }
    }
}

/// <summary>Outcome codes for <see cref="PlanCollectionService.Collect"/>.</summary>
internal enum CollectionOutcome
{
    /// <summary>Plan was freshly collected into the PlanStore.</summary>
    Collected,

    /// <summary>Plan with the same revision already exists — no-op.</summary>
    AlreadyCollected,

    /// <summary>An existing plan with a different revision blocks the incoming stale proposal.</summary>
    StaleRevisionRejected,

    /// <summary>An active (executing/interrupted/blocked) plan prevents collection.</summary>
    ActivePlanBlocked,
}
