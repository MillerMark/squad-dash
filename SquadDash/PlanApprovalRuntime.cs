using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

internal sealed record ApprovalRuntimeAdvanceResult(
    Plan UpdatedPlan,
    IReadOnlyList<PlanApprovalGate> NewlyReadyGates,
    ApprovalReviewSnapshot? ReviewSnapshot,
    ApprovalClickToken? ClickToken,
    string? MessageId,
    bool MustStop,
    string? NextUngatedTaskId);

internal sealed record ApprovalRuntimeResolutionResult(
    ApprovalClickResult Result,
    Plan? UpdatedPlan,
    bool ShouldResume,
    ApprovalClickToken? NextClickToken = null);

internal sealed record ApprovalRuntimeReworkResult(
    ApprovalClickResult Result,
    Plan? UpdatedPlan,
    ApprovalClickToken? NextClickToken = null);

/// <summary>
/// Host-owned integration point for approval scheduling, durable Inbox state, stale-action
/// validation, and restart restoration. UI surfaces consume its versioned click token instead
/// of independently changing plan gates.
/// </summary>
internal sealed class PlanApprovalRuntime
{
    private readonly DurableApprovalRequestManager _requests;
    private readonly ApprovalActionCoordinator _actions;
    private readonly Func<Plan, PlanApprovalGate, CancellationToken, Task<ApprovalReviewSnapshot>> _buildSnapshot;

    internal PlanApprovalRuntime(
        DurableApprovalRequestManager requests,
        ApprovalActionCoordinator actions,
        Func<Plan, PlanApprovalGate, CancellationToken, Task<ApprovalReviewSnapshot>> buildSnapshot)
    {
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _buildSnapshot = buildSnapshot ?? throw new ArgumentNullException(nameof(buildSnapshot));
    }

    internal ApprovalActionCoordinator Actions => _actions;
    internal DurableApprovalRequestManager Requests => _requests;

    /// <summary>
    /// Opens the approval window for newly ready gates while preserving any unrelated runnable
    /// work. The plan is changed to AwaitingApproval only when no ungated eligible task remains.
    /// </summary>
    internal async Task<ApprovalRuntimeAdvanceResult> AdvanceAsync(
        Plan plan,
        CancellationToken cancellationToken = default)
    {
        var initialStates = ApprovalGateReadinessEvaluator.EvaluateGates(plan);
        var newlyReadyIds = initialStates
            .Where(state => state.IsReady)
            .Select(state => state.GateId)
            .Where(id => plan.ApprovalGates.Any(gate =>
                string.Equals(gate.GateId, id, StringComparison.Ordinal) &&
                gate.Status == PlanGateStatus.Pending))
            .ToArray();

        var updated = plan;
        var snapshots = new List<ApprovalReviewSnapshot>();
        var newlyReadyGates = new List<PlanApprovalGate>();
        string? messageId = null;

        foreach (var gateId in newlyReadyIds)
        {
            updated = PlanStoreUpdater.ApplyGateReady(updated, gateId);
            var gate = updated.ApprovalGates.First(g =>
                string.Equals(g.GateId, gateId, StringComparison.Ordinal));
            var snapshot = await _buildSnapshot(updated, gate, cancellationToken).ConfigureAwait(false);
            snapshots.Add(snapshot);
            newlyReadyGates.Add(gate);
            messageId = await _requests.AppendCheckpointAsync(updated, gate, snapshot, cancellationToken)
                .ConfigureAwait(false);
        }

        var activeGateIds = updated.ApprovalGates
            .Where(gate => gate.Status == PlanGateStatus.AwaitingApproval)
            .Select(gate => gate.GateId)
            .ToArray();

        // A later checkpoint may become ready while an earlier review window remains open.
        // Rebuild the aggregate from every active boundary so the one Inbox message never drops
        // earlier commits/tasks when its evidence is atomically replaced.
        if (newlyReadyIds.Length > 0)
        {
            foreach (var gate in updated.ApprovalGates.Where(gate =>
                         gate.Status == PlanGateStatus.AwaitingApproval &&
                         snapshots.All(snapshot => !string.Equals(
                             snapshot.GateId, gate.GateId, StringComparison.Ordinal))))
            {
                snapshots.Add(await _buildSnapshot(updated, gate, cancellationToken).ConfigureAwait(false));
            }
        }

        ApprovalReviewSnapshot? combinedSnapshot = snapshots.Count switch
        {
            0 => null,
            1 => snapshots[0],
            _ => CombineSnapshots(snapshots),
        };
        if (combinedSnapshot is not null)
            await _requests.RefreshEvidenceAsync(updated, combinedSnapshot, cancellationToken)
                .ConfigureAwait(false);

        ApprovalClickToken? token = null;
        var durableState = _requests.GetState(updated.PlanId);
        if (durableState is not null && activeGateIds.Length > 0)
        {
            token = await _actions.RestoreAsync(
                updated.PlanId,
                updated.Revision,
                durableState.Version,
                activeGateIds,
                cancellationToken).ConfigureAwait(false);
        }

        var currentStates = ApprovalGateReadinessEvaluator.EvaluateGates(updated);
        var mustStop = activeGateIds.Length > 0 &&
                       ApprovalGateReadinessEvaluator.ShouldStopForApproval(updated, currentStates);
        if (mustStop)
        {
            updated = PlanStoreUpdater.ApplyFullStopAtGates(updated, activeGateIds);
            if (combinedSnapshot is null)
            {
                foreach (var activeGate in updated.ApprovalGates.Where(gate =>
                             gate.Status == PlanGateStatus.AwaitingApproval))
                {
                    snapshots.Add(await _buildSnapshot(updated, activeGate, cancellationToken)
                        .ConfigureAwait(false));
                }
                combinedSnapshot = snapshots.Count == 1
                    ? snapshots[0]
                    : CombineSnapshots(snapshots);
                await _requests.RefreshEvidenceAsync(updated, combinedSnapshot, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return new ApprovalRuntimeAdvanceResult(
            updated,
            newlyReadyGates,
            combinedSnapshot,
            token,
            messageId,
            mustStop,
            ApprovalGateReadinessEvaluator.SelectNextUngatedTask(updated, currentStates));
    }

    /// <summary>Restores exact action versions and creates missing legacy Inbox requests.</summary>
    internal async Task RestoreAsync(
        IEnumerable<Plan> plans,
        CancellationToken cancellationToken = default)
    {
        foreach (var plan in plans)
        {
            var activeGates = plan.ApprovalGates
                .Where(gate => gate.Status == PlanGateStatus.AwaitingApproval)
                .ToArray();
            if (activeGates.Length == 0)
            {
                var staleState = _requests.GetState(plan.PlanId);
                if (staleState is not null)
                {
                    foreach (var staleGateId in staleState.ActiveGateIds)
                    {
                        await _requests.ResolveCheckpointAsync(
                            plan,
                            staleGateId,
                            "Reconciled from the authoritative plan state after restart.",
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                _actions.Unregister(plan.PlanId);
                continue;
            }

            var state = _requests.GetState(plan.PlanId);
            if (state is not null)
            {
                var activeIds = activeGates.Select(gate => gate.GateId).ToHashSet(StringComparer.Ordinal);
                foreach (var staleGateId in state.ActiveGateIds.Where(id => !activeIds.Contains(id)).ToArray())
                {
                    await _requests.ResolveCheckpointAsync(
                        plan,
                        staleGateId,
                        "Reconciled from the authoritative plan state after restart.",
                        cancellationToken).ConfigureAwait(false);
                }
                state = _requests.GetState(plan.PlanId);
            }

            var durableIds = state?.ActiveGateIds.ToHashSet(StringComparer.Ordinal)
                             ?? new HashSet<string>(StringComparer.Ordinal);
            foreach (var gate in activeGates.Where(gate => !durableIds.Contains(gate.GateId)))
            {
                var snapshot = await _buildSnapshot(plan, gate, cancellationToken).ConfigureAwait(false);
                await _requests.AppendCheckpointAsync(plan, gate, snapshot, cancellationToken)
                        .ConfigureAwait(false);
            }
            state = _requests.GetState(plan.PlanId);

            if (state is not null)
            {
                // Rebuild the human-facing evidence on every restore. Older releases may
                // have persisted only a terse body even though their snapshot attachment
                // contains the complete review evidence.
                var restoredSnapshots = new List<ApprovalReviewSnapshot>();
                foreach (var gate in activeGates)
                {
                    restoredSnapshots.Add(await _buildSnapshot(plan, gate, cancellationToken)
                        .ConfigureAwait(false));
                }
                var restoredSnapshot = restoredSnapshots.Count == 1
                    ? restoredSnapshots[0]
                    : CombineSnapshots(restoredSnapshots);
                await _requests.RefreshEvidenceAsync(plan, restoredSnapshot, cancellationToken)
                    .ConfigureAwait(false);

                await _actions.RestoreAsync(
                    plan.PlanId,
                    plan.Revision,
                    state.Version,
                    state.ActiveGateIds,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Applies one versioned approval across all gates represented by the captured snapshot.
    /// The supplied persistence callback runs before live action state is invalidated.
    /// </summary>
    internal async Task<ApprovalRuntimeResolutionResult> ApproveAsync(
        ApprovalClickToken clickToken,
        Plan currentPlan,
        string? note,
        Func<Plan, bool> persistPlan,
        IReadOnlyList<string>? gateIdsToResolve = null,
        string? resolvedBy = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(clickToken.PlanId, currentPlan.PlanId, StringComparison.Ordinal) ||
            !string.Equals(clickToken.PlanRevision, currentPlan.Revision, StringComparison.Ordinal))
            return new ApprovalRuntimeResolutionResult(ApprovalClickResult.StaleRejected, null, false);

        var resolutionIds = gateIdsToResolve ?? clickToken.GateIds;
        if (resolutionIds.Count == 0 ||
            resolutionIds.Any(id => !clickToken.GateIds.Contains(id, StringComparer.Ordinal)) ||
            resolutionIds.Any(id => !currentPlan.ApprovalGates.Any(gate =>
                string.Equals(gate.GateId, id, StringComparison.Ordinal) &&
                gate.Status == PlanGateStatus.AwaitingApproval)))
            return new ApprovalRuntimeResolutionResult(ApprovalClickResult.StaleRejected, null, false);

        var wasPaused = currentPlan.LifecycleStatus == PlanLifecycleStatus.AwaitingApproval;
        var updated = currentPlan;
        foreach (var gateId in resolutionIds)
            updated = PlanStoreUpdater.ApplyGateApproved(updated, gateId, note, resolvedBy);

        var result = await _actions.TryApproveAsync(
            clickToken,
            resolutionIds,
            note,
            persistResolution: () => persistPlan(updated),
            cancellationToken).ConfigureAwait(false);
        if (result != ApprovalClickResult.Approved)
            return new ApprovalRuntimeResolutionResult(result, null, false);

        foreach (var gateId in resolutionIds)
            await _requests.ResolveCheckpointAsync(updated, gateId, note, cancellationToken)
                .ConfigureAwait(false);

        ApprovalClickToken? nextToken = null;
        var remainingState = _requests.GetState(updated.PlanId);
        if (remainingState is { ActiveGateIds.Count: > 0 })
        {
            nextToken = await _actions.RestoreAsync(
                updated.PlanId,
                updated.Revision,
                remainingState.Version,
                remainingState.ActiveGateIds,
                cancellationToken).ConfigureAwait(false);
        }

        return new ApprovalRuntimeResolutionResult(
            result,
            updated,
            wasPaused && updated.LifecycleStatus == PlanLifecycleStatus.Executing,
            nextToken);
    }

    /// <summary>Atomically sends selected reviewed tasks back for another attempt.</summary>
    internal async Task<ApprovalRuntimeReworkResult> RequestReworkAsync(
        ApprovalClickToken clickToken,
        Plan currentPlan,
        string gateId,
        IReadOnlyCollection<string> taskIds,
        string instructions,
        Func<Plan, bool> persistPlan,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(clickToken.PlanId, currentPlan.PlanId, StringComparison.Ordinal) ||
            !string.Equals(clickToken.PlanRevision, currentPlan.Revision, StringComparison.Ordinal) ||
            !clickToken.GateIds.Contains(gateId, StringComparer.Ordinal))
            return new ApprovalRuntimeReworkResult(ApprovalClickResult.StaleRejected, null);

        var updated = PlanStoreUpdater.ApplyGateReworkRequested(
            currentPlan,
            gateId,
            taskIds,
            instructions);
        if (ReferenceEquals(updated, currentPlan))
            return new ApprovalRuntimeReworkResult(ApprovalClickResult.StaleRejected, null);

        var result = await _actions.TryRequestReworkAsync(
            clickToken,
            gateId,
            () => persistPlan(updated),
            cancellationToken).ConfigureAwait(false);
        if (result != ApprovalClickResult.Approved)
            return new ApprovalRuntimeReworkResult(result, null);

        await _requests.RecordReworkAsync(updated, gateId, instructions, cancellationToken)
            .ConfigureAwait(false);
        ApprovalClickToken? nextToken = null;
        var remainingState = _requests.GetState(updated.PlanId);
        if (remainingState is { ActiveGateIds.Count: > 0 })
        {
            nextToken = await _actions.RestoreAsync(
                updated.PlanId,
                updated.Revision,
                remainingState.Version,
                remainingState.ActiveGateIds,
                cancellationToken).ConfigureAwait(false);
        }
        return new ApprovalRuntimeReworkResult(result, updated, nextToken);
    }

    /// <summary>Atomically adds bounded work to the reviewed boundary without reopening it.</summary>
    internal async Task<ApprovalRuntimeReworkResult> RequestAmendmentAsync(
        ApprovalClickToken clickToken,
        Plan currentPlan,
        string gateId,
        IReadOnlyCollection<string>? relatedTaskIds,
        string title,
        string instructions,
        Func<Plan, bool> persistPlan,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(clickToken.PlanId, currentPlan.PlanId, StringComparison.Ordinal) ||
            !string.Equals(clickToken.PlanRevision, currentPlan.Revision, StringComparison.Ordinal) ||
            !clickToken.GateIds.Contains(gateId, StringComparer.Ordinal))
            return new ApprovalRuntimeReworkResult(ApprovalClickResult.StaleRejected, null);

        var updated = PlanStoreUpdater.ApplyGateAmendmentRequested(
            currentPlan, gateId, relatedTaskIds, title, instructions);
        if (ReferenceEquals(updated, currentPlan))
            return new ApprovalRuntimeReworkResult(ApprovalClickResult.StaleRejected, null);

        var result = await _actions.TryRequestReworkAsync(
            clickToken,
            gateId,
            () => persistPlan(updated),
            cancellationToken).ConfigureAwait(false);
        if (result != ApprovalClickResult.Approved)
            return new ApprovalRuntimeReworkResult(result, null);

        await _requests.RecordReworkAsync(updated, gateId, instructions, cancellationToken)
            .ConfigureAwait(false);
        ApprovalClickToken? nextToken = null;
        var remainingState = _requests.GetState(updated.PlanId);
        if (remainingState is { ActiveGateIds.Count: > 0 })
        {
            nextToken = await _actions.RestoreAsync(
                updated.PlanId,
                updated.Revision,
                remainingState.Version,
                remainingState.ActiveGateIds,
                cancellationToken).ConfigureAwait(false);
        }
        return new ApprovalRuntimeReworkResult(result, updated, nextToken);
    }

    private static ApprovalReviewSnapshot CombineSnapshots(IReadOnlyList<ApprovalReviewSnapshot> snapshots)
    {
        var first = snapshots[0];
        return first with
        {
            GateId = string.Join(", ", snapshots.Select(snapshot => snapshot.GateId)),
            GateReason = string.Join("; ", snapshots.Select(snapshot => snapshot.GateReason)),
            AfterTaskIds = snapshots.SelectMany(snapshot => snapshot.AfterTaskIds)
                .Distinct(StringComparer.Ordinal).ToArray(),
            BeforeTaskIds = snapshots.SelectMany(snapshot => snapshot.BeforeTaskIds)
                .Distinct(StringComparer.Ordinal).ToArray(),
            CompletedTasks = snapshots.SelectMany(snapshot => snapshot.CompletedTasks)
                .DistinctBy(task => task.TaskId, StringComparer.Ordinal).ToArray(),
            DownstreamTasks = snapshots.SelectMany(snapshot => snapshot.DownstreamTasks)
                .DistinctBy(task => task.TaskId, StringComparer.Ordinal).ToArray(),
            AllChangedFiles = snapshots.SelectMany(snapshot => snapshot.AllChangedFiles)
                .DistinctBy(file => (file.CommitSha, file.FilePath)).ToArray(),
            BuiltAt = DateTimeOffset.UtcNow,
        };
    }
}
