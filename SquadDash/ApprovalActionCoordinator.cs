using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

// ─── Versioned snapshot token ────────────────────────────────────────────────

/// <summary>
/// Immutable token capturing the exact state of an approval request at click time.
/// Compared against the current state to detect stale clicks.
/// </summary>
internal sealed record ApprovalClickToken(
    string PlanId,
    string PlanRevision,
    int RequestVersion,
    IReadOnlyList<string> GateIds)
{
    /// <summary>
    /// Returns true when this token matches the current coordinator state for the plan.
    /// </summary>
    internal bool Matches(ApprovalClickToken other)
    {
        if (!string.Equals(PlanId, other.PlanId, StringComparison.Ordinal)) return false;
        if (!string.Equals(PlanRevision, other.PlanRevision, StringComparison.Ordinal)) return false;
        if (RequestVersion != other.RequestVersion) return false;
        if (GateIds.Count != other.GateIds.Count) return false;
        for (int i = 0; i < GateIds.Count; i++)
        {
            if (!string.Equals(GateIds[i], other.GateIds[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}

/// <summary>Result of an approval click attempt.</summary>
internal enum ApprovalClickResult
{
    /// <summary>Approval succeeded and was applied.</summary>
    Approved,
    /// <summary>The click was stale — plan revision, version, or gate IDs have changed.</summary>
    StaleRejected,
    /// <summary>The approval was already resolved (e.g. by another surface).</summary>
    AlreadyResolved,
    /// <summary>The durable plan transition could not be persisted, so no approval was recorded.</summary>
    PersistenceFailed,
}

/// <summary>
/// Event args raised when an approval action completes, enabling cross-surface invalidation.
/// </summary>
internal sealed class ApprovalResolvedEventArgs : EventArgs
{
    internal string PlanId { get; }
    internal IReadOnlyList<string> ResolvedGateIds { get; }
    internal bool AllGatesResolved { get; }
    internal string? ResolutionNote { get; }

    internal ApprovalResolvedEventArgs(
        string planId,
        IReadOnlyList<string> resolvedGateIds,
        bool allGatesResolved,
        string? resolutionNote)
    {
        PlanId = planId;
        ResolvedGateIds = resolvedGateIds;
        AllGatesResolved = allGatesResolved;
        ResolutionNote = resolutionNote;
    }
}

// ─── Per-plan live state ─────────────────────────────────────────────────────

/// <summary>
/// Mutable per-plan approval state maintained by <see cref="ApprovalActionCoordinator"/>.
/// Protected by the coordinator's per-plan lock.
/// </summary>
internal sealed class ApprovalPlanState
{
    internal string PlanRevision { get; set; }
    internal int RequestVersion { get; set; }
    internal List<string> ActiveGateIds { get; set; }
    internal HashSet<string> ResolvedGateIds { get; }
    internal bool IsFullyResolved => ActiveGateIds.Count == 0;

    internal ApprovalPlanState(string planRevision, IEnumerable<string> gateIds)
    {
        PlanRevision = planRevision;
        RequestVersion = 1;
        ActiveGateIds = gateIds.ToList();
        ResolvedGateIds = new HashSet<string>(StringComparer.Ordinal);
    }

    internal ApprovalClickToken BuildToken(string planId) =>
        new(planId, PlanRevision, RequestVersion, ActiveGateIds.ToList());
}

// ─── Coordinator ─────────────────────────────────────────────────────────────

/// <summary>
/// Coordinates approval actions across all plan surfaces (Inbox, transcript, plans panel,
/// plan viewer). Ensures one versioned approval-request snapshot per plan, validates
/// stale clicks, and raises cross-surface invalidation events.
/// All mutations are serialized under a per-plan async lock.
/// </summary>
internal sealed class ApprovalActionCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ApprovalPlanState> _states = new(StringComparer.Ordinal);

    /// <summary>
    /// Raised on the calling thread when one or more gates are resolved.
    /// UI surfaces subscribe to disable controls and refresh content.
    /// </summary>
    internal event EventHandler<ApprovalResolvedEventArgs>? ApprovalResolved;

    /// <summary>
    /// Raised when a new gate is appended to an already-tracked plan, or when
    /// a resolved plan receives a concurrent gate arrival. Surfaces should refresh.
    /// </summary>
    internal event EventHandler<string>? ApprovalRefreshNeeded;

    // ── Registration ─────────────────────────────────────────────────────

    /// <summary>
    /// Registers or updates the approval state for a plan with the given gates.
    /// Returns the click token that surfaces should capture at render time.
    /// </summary>
    internal async Task<ApprovalClickToken> RegisterAsync(
        string planId,
        string planRevision,
        IReadOnlyList<string> activeGateIds,
        CancellationToken cancellationToken = default)
    {
        var sem = GetLock(planId);
        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_states.TryGetValue(planId, out var existing))
            {
                existing.PlanRevision = planRevision;
                existing.ActiveGateIds = activeGateIds.ToList();
                existing.RequestVersion++;
                return existing.BuildToken(planId);
            }
            else
            {
                var state = new ApprovalPlanState(planRevision, activeGateIds);
                _states[planId] = state;
                return state.BuildToken(planId);
            }
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Restores the exact durable request version used by rendered Inbox/transcript actions.
    /// Unlike <see cref="RegisterAsync"/>, this does not invent a new version during restart.
    /// </summary>
    internal async Task<ApprovalClickToken> RestoreAsync(
        string planId,
        string planRevision,
        int requestVersion,
        IReadOnlyList<string> activeGateIds,
        CancellationToken cancellationToken = default)
    {
        var sem = GetLock(planId);
        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = new ApprovalPlanState(planRevision, activeGateIds)
            {
                RequestVersion = requestVersion,
            };
            _states[planId] = state;
            return state.BuildToken(planId);
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Returns the current click token for a plan, or null if not registered.
    /// Thread-safe snapshot read.
    /// </summary>
    internal ApprovalClickToken? GetCurrentToken(string planId)
    {
        return _states.TryGetValue(planId, out var state) ? state.BuildToken(planId) : null;
    }

    // ── Click validation & resolution ────────────────────────────────────

    /// <summary>
    /// Validates a click token against the current state and, if valid,
    /// resolves the specified gate IDs. Returns the result indicating whether
    /// approval proceeded, was rejected as stale, or was already resolved.
    /// </summary>
    internal async Task<ApprovalClickResult> TryApproveAsync(
        ApprovalClickToken clickToken,
        IReadOnlyList<string> gateIdsToResolve,
        string? resolutionNote = null,
        Func<bool>? persistResolution = null,
        CancellationToken cancellationToken = default)
    {
        var sem = GetLock(clickToken.PlanId);
        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_states.TryGetValue(clickToken.PlanId, out var state))
                return ApprovalClickResult.StaleRejected;

            var currentToken = state.BuildToken(clickToken.PlanId);

            // Verify the click was issued against the current snapshot
            if (!clickToken.Matches(currentToken))
                return ApprovalClickResult.StaleRejected;

            // Check if all requested gates are still active
            var toResolve = new List<string>();
            foreach (var gateId in gateIdsToResolve)
            {
                if (state.ResolvedGateIds.Contains(gateId))
                    continue; // already resolved
                if (!state.ActiveGateIds.Contains(gateId))
                    return ApprovalClickResult.StaleRejected; // gate no longer active
                toResolve.Add(gateId);
            }

            if (toResolve.Count == 0)
                return ApprovalClickResult.AlreadyResolved;

            // The plan file is authoritative. Do not mutate the coordinator's live state or
            // invalidate other surfaces unless the host first persisted the plan transition.
            if (persistResolution is not null && !persistResolution())
                return ApprovalClickResult.PersistenceFailed;

            // Apply resolution
            foreach (var gateId in toResolve)
            {
                state.ActiveGateIds.Remove(gateId);
                state.ResolvedGateIds.Add(gateId);
            }
            state.RequestVersion++;

            var allResolved = state.IsFullyResolved;

            // Raise cross-surface event outside the lock would risk races,
            // but the event is synchronous and expected to be dispatched to UI thread
            ApprovalResolved?.Invoke(this, new ApprovalResolvedEventArgs(
                clickToken.PlanId, toResolve, allResolved, resolutionNote));

            return ApprovalClickResult.Approved;
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Appends a new gate to an existing plan's tracked state. If the plan was fully
    /// resolved, this restores it as active. Raises <see cref="ApprovalRefreshNeeded"/>.
    /// </summary>
    internal async Task AppendGateAsync(
        string planId,
        string planRevision,
        string gateId,
        CancellationToken cancellationToken = default)
    {
        var sem = GetLock(planId);
        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_states.TryGetValue(planId, out var state))
            {
                if (state.ActiveGateIds.Contains(gateId))
                    return;
                state.ActiveGateIds.Add(gateId);
                state.PlanRevision = planRevision;
                state.RequestVersion++;
            }
            else
            {
                _states[planId] = new ApprovalPlanState(planRevision, [gateId]);
            }
        }
        finally
        {
            sem.Release();
        }

        ApprovalRefreshNeeded?.Invoke(this, planId);
    }

    /// <summary>
    /// Removes tracking state for a plan. Called when a plan is archived or completed.
    /// </summary>
    internal void Unregister(string planId)
    {
        _states.TryRemove(planId, out _);
    }

    /// <summary>
    /// Returns whether a plan has any active (unresolved) gates tracked by this coordinator.
    /// </summary>
    internal bool HasActiveGates(string planId) =>
        _states.TryGetValue(planId, out var state) && state.ActiveGateIds.Count > 0;

    /// <summary>
    /// Returns the active gate IDs for a plan, or empty if not tracked.
    /// </summary>
    internal IReadOnlyList<string> GetActiveGateIds(string planId) =>
        _states.TryGetValue(planId, out var state) ? state.ActiveGateIds.ToList() : [];

    // ── Test seams ───────────────────────────────────────────────────────

    /// <summary>Clears all state. For testing only.</summary>
    internal void ClearAll()
    {
        _states.Clear();
        _locks.Clear();
    }

    // ── Private ──────────────────────────────────────────────────────────

    private SemaphoreSlim GetLock(string planId) =>
        _locks.GetOrAdd(planId, static _ => new SemaphoreSlim(1, 1));
}
