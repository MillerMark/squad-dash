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
/// </list>
/// </para>
/// </summary>
internal sealed class PlanCollectionService
{
    private readonly PlanStore _planStore;

    internal PlanCollectionService(PlanStore planStore)
    {
        _planStore = planStore;
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
    /// error outcome when the revision is stale.
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
            // Idempotent: same revision means already collected — return as-is.
            if (string.Equals(existing.Revision, incomingRevision, StringComparison.Ordinal))
            {
                SquadDashTrace.Write(TraceCategory.General,
                    $"PlanCollectionService: idempotent collect for '{planId}' (revision {incomingRevision}).");
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

        return new CollectionResult(plan, CollectionOutcome.Collected);
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
}
