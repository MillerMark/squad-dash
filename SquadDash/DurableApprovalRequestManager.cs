using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SquadDash;

// ─── Persisted state model ───────────────────────────────────────────────────

/// <summary>
/// Tracks the lifecycle of a single resolved approval checkpoint.
/// </summary>
internal sealed record ResolvedCheckpointEntry(
    [property: JsonPropertyName("gateId")] string GateId,
    [property: JsonPropertyName("resolvedAt")] DateTimeOffset ResolvedAt,
    [property: JsonPropertyName("resolutionNote")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ResolutionNote = null);

/// <summary>
/// Durable per-plan approval state embedded in the inbox message attachment.
/// Serialized as the <see cref="InboxAttachment.Content"/> of the
/// <c>approval-gate</c> attachment type.
/// </summary>
internal sealed record DurableApprovalState(
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("activeGateIds")] IReadOnlyList<string> ActiveGateIds,
    [property: JsonPropertyName("resolvedCheckpoints")] IReadOnlyList<ResolvedCheckpointEntry> ResolvedCheckpoints,
    [property: JsonPropertyName("lastNotifiedAt")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? LastNotifiedAt = null,
    [property: JsonPropertyName("archived")] bool Archived = false,
    [property: JsonPropertyName("version")] int Version = 1);

// ─── Manager ─────────────────────────────────────────────────────────────────

/// <summary>
/// Maintains exactly one durable approval Inbox message per plan for its entire
/// lifecycle.  Serializes all mutations with a per-plan async lock and writes via
/// <see cref="InboxStore"/> (which uses atomic file replacement).
/// </summary>
internal sealed class DurableApprovalRequestManager
{
    internal const string AttachmentType = "approval-gate";
    private const string MessageFrom = "SquadDash";

    private static readonly JsonSerializerOptions StateSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly InboxStore _inbox;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _planLocks = new(StringComparer.Ordinal);

    internal DurableApprovalRequestManager(InboxStore inbox)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Appends a newly ready checkpoint (gate) to the plan's single inbox message.
    /// Creates the message if it doesn't exist; unarchives if archived.
    /// Returns the stable message ID.
    /// </summary>
    internal async Task<string> AppendCheckpointAsync(
        Plan plan,
        PlanApprovalGate gate,
        ApprovalReviewSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var sem = GetLock(plan.PlanId);
        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var messageId = BuildMessageId(plan.PlanId);
            var existing = _inbox.GetById(messageId);

            if (existing is not null)
            {
                var state = DeserializeState(existing);
                if (state is not null && state.ActiveGateIds.Contains(gate.GateId, StringComparer.Ordinal))
                    return messageId; // already tracked — idempotent

                var updatedGateIds = state is not null
                    ? state.ActiveGateIds.Append(gate.GateId).Distinct(StringComparer.Ordinal).ToList()
                    : new List<string> { gate.GateId };

                var newState = (state ?? new DurableApprovalState(plan.PlanId, [], []))
                    with
                    {
                        ActiveGateIds = updatedGateIds,
                        Archived = false,
                        Version = (state?.Version ?? 0) + 1,
                    };

                var updated = existing with
                {
                    Read = false,
                    Body = BuildBody(plan, updatedGateIds, newState.ResolvedCheckpoints),
                    Actions = BuildActions(plan, updatedGateIds),
                    Attachments = BuildAttachments(newState, snapshot),
                    Priority = "high",
                };
                _inbox.Save(updated);
            }
            else
            {
                var state = new DurableApprovalState(
                    plan.PlanId,
                    [gate.GateId],
                    [],
                    LastNotifiedAt: null,
                    Archived: false,
                    Version: 1);

                var message = new InboxMessage
                {
                    Id = messageId,
                    Subject = $"Approval needed: {plan.Title}",
                    From = MessageFrom,
                    Timestamp = DateTimeOffset.UtcNow,
                    Read = false,
                    Priority = "high",
                    Body = BuildBody(plan, [gate.GateId], []),
                    Attachments = BuildAttachments(state, snapshot),
                    Actions = BuildActions(plan, [gate.GateId]),
                };
                _inbox.Save(message);
            }

            return messageId;
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Refreshes evidence (snapshot) and task/commit data for an existing approval message.
    /// No-op if the message doesn't exist.
    /// </summary>
    internal async Task RefreshEvidenceAsync(
        Plan plan,
        ApprovalReviewSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var sem = GetLock(plan.PlanId);
        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var messageId = BuildMessageId(plan.PlanId);
            var existing = _inbox.GetById(messageId);
            if (existing is null) return;

            var state = DeserializeState(existing);
            if (state is null) return;

            var updated = existing with
            {
                Body = BuildBody(plan, state.ActiveGateIds, state.ResolvedCheckpoints),
                Actions = BuildActions(plan, state.ActiveGateIds),
                Attachments = BuildAttachments(state, snapshot),
            };
            _inbox.Save(updated);
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Resolves a checkpoint: moves the gate ID from active to resolved history.
    /// If no active gates remain, marks the message read, removes actions, and archives it.
    /// </summary>
    internal async Task ResolveCheckpointAsync(
        Plan plan,
        string gateId,
        string? resolutionNote = null,
        CancellationToken cancellationToken = default)
    {
        var sem = GetLock(plan.PlanId);
        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var messageId = BuildMessageId(plan.PlanId);
            var existing = _inbox.GetById(messageId);
            if (existing is null) return;

            var state = DeserializeState(existing);
            if (state is null) return;

            var remainingGates = state.ActiveGateIds
                .Where(id => !string.Equals(id, gateId, StringComparison.Ordinal))
                .ToList();

            var newResolved = state.ResolvedCheckpoints.ToList();
            if (state.ActiveGateIds.Contains(gateId, StringComparer.Ordinal))
            {
                newResolved.Add(new ResolvedCheckpointEntry(gateId, DateTimeOffset.UtcNow, resolutionNote));
            }

            var newState = state with
            {
                ActiveGateIds = remainingGates,
                ResolvedCheckpoints = newResolved,
                Archived = remainingGates.Count == 0,
                Version = state.Version + 1,
            };

            if (remainingGates.Count == 0)
            {
                // No active approvals remain — archive
                var archived = existing with
                {
                    Read = true,
                    Actions = [],
                    Body = BuildBody(plan, [], newState.ResolvedCheckpoints),
                    Attachments = BuildAttachments(newState, snapshot: null),
                };
                _inbox.Save(archived);
            }
            else
            {
                var updated = existing with
                {
                    Body = BuildBody(plan, remainingGates, newState.ResolvedCheckpoints),
                    Actions = BuildActions(plan, remainingGates),
                    Attachments = BuildAttachments(newState, snapshot: null),
                };
                _inbox.Save(updated);
            }
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Records that a notification was sent, preventing duplicates.
    /// Returns true if notification should proceed (first time for this version).
    /// </summary>
    internal async Task<bool> TryMarkNotifiedAsync(
        string planId,
        CancellationToken cancellationToken = default)
    {
        var sem = GetLock(planId);
        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var messageId = BuildMessageId(planId);
            var existing = _inbox.GetById(messageId);
            if (existing is null) return false;

            var state = DeserializeState(existing);
            if (state is null) return false;

            // Dedup: if already notified for this version, skip
            if (state.LastNotifiedAt is not null)
                return false;

            var newState = state with { LastNotifiedAt = DateTimeOffset.UtcNow };
            var updated = existing with
            {
                Attachments = BuildAttachments(newState, snapshot: null, existingAttachments: existing.Attachments),
            };
            _inbox.Save(updated);
            return true;
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Restores state from persisted inbox messages on startup.
    /// Returns plan IDs that have active approval requests.
    /// </summary>
    internal IReadOnlyList<string> RestoreActivePlanIds()
    {
        var result = new List<string>();
        foreach (var message in _inbox.LoadAll())
        {
            var state = DeserializeState(message);
            if (state is not null && !state.Archived && state.ActiveGateIds.Count > 0)
                result.Add(state.PlanId);
        }
        return result;
    }

    /// <summary>
    /// Returns the current durable state for a plan, or null if no message exists.
    /// </summary>
    internal DurableApprovalState? GetState(string planId)
    {
        var messageId = BuildMessageId(planId);
        var existing = _inbox.GetById(messageId);
        return existing is not null ? DeserializeState(existing) : null;
    }

    /// <summary>
    /// Returns whether the message for this plan is currently archived.
    /// </summary>
    internal bool IsArchived(string planId)
    {
        var state = GetState(planId);
        return state?.Archived ?? false;
    }

    // ── Identity ─────────────────────────────────────────────────────────────

    /// <summary>Stable message ID for a plan — never changes across the plan lifecycle.</summary>
    internal static string BuildMessageId(string planId) => $"approval-gate-{planId}";

    // ── Helpers ──────────────────────────────────────────────────────────────

    private SemaphoreSlim GetLock(string planId) =>
        _planLocks.GetOrAdd(planId, static _ => new SemaphoreSlim(1, 1));

    private static DurableApprovalState? DeserializeState(InboxMessage message)
    {
        var attachment = message.Attachments.FirstOrDefault(
            a => string.Equals(a.Type, AttachmentType, StringComparison.OrdinalIgnoreCase));
        if (attachment is null || string.IsNullOrWhiteSpace(attachment.Content))
            return null;
        try
        {
            return JsonSerializer.Deserialize<DurableApprovalState>(attachment.Content, StateSerializerOptions);
        }
        catch (JsonException ex)
        {
            SquadDashTrace.Write(TraceCategory.Inbox,
                $"Durable approval state could not be parsed: {ex.Message}");
            return null;
        }
    }

    private static string SerializeState(DurableApprovalState state) =>
        JsonSerializer.Serialize(state, StateSerializerOptions);

    private static IReadOnlyList<InboxAttachment> BuildAttachments(
        DurableApprovalState state,
        ApprovalReviewSnapshot? snapshot,
        IReadOnlyList<InboxAttachment>? existingAttachments = null)
    {
        var attachments = new List<InboxAttachment>();

        // State attachment — always first
        attachments.Add(new InboxAttachment
        {
            Type = AttachmentType,
            Label = "Approval state",
            Content = SerializeState(state),
        });

        // Snapshot attachment — carry forward existing if not replaced
        if (snapshot is not null)
        {
            attachments.Add(new InboxAttachment
            {
                Type = "approval-snapshot",
                Label = "Review evidence",
                Content = JsonSerializer.Serialize(snapshot, StateSerializerOptions),
            });
        }
        else if (existingAttachments is not null)
        {
            var existingSnapshot = existingAttachments.FirstOrDefault(
                a => string.Equals(a.Type, "approval-snapshot", StringComparison.OrdinalIgnoreCase));
            if (existingSnapshot is not null)
                attachments.Add(existingSnapshot);
        }

        return attachments;
    }

    internal static string BuildBody(
        Plan plan,
        IReadOnlyList<string> activeGateIds,
        IReadOnlyList<ResolvedCheckpointEntry> resolvedCheckpoints)
    {
        var parts = new List<string>();

        parts.Add($"**{plan.Title}**  ");
        parts.Add($"Progress: {plan.Progress.CompletedCount}/{plan.Progress.TotalCount} tasks");

        if (activeGateIds.Count > 0)
        {
            parts.Add("");
            parts.Add($"**{activeGateIds.Count} checkpoint(s) awaiting approval:**");
            foreach (var gateId in activeGateIds)
            {
                var gate = plan.ApprovalGates.FirstOrDefault(
                    g => string.Equals(g.GateId, gateId, StringComparison.Ordinal));
                var reason = gate?.Message ?? gateId;
                parts.Add($"- 🔒 `{gateId}`: {reason}");
            }
        }

        if (resolvedCheckpoints.Count > 0)
        {
            parts.Add("");
            parts.Add($"**{resolvedCheckpoints.Count} resolved checkpoint(s):**");
            foreach (var cp in resolvedCheckpoints)
            {
                var note = cp.ResolutionNote is not null ? $" — {cp.ResolutionNote}" : "";
                parts.Add($"- ✅ `{cp.GateId}` resolved {cp.ResolvedAt:yyyy-MM-dd HH:mm}{note}");
            }
        }

        if (activeGateIds.Count == 0)
        {
            parts.Add("");
            parts.Add("All checkpoints resolved. This message has been archived.");
        }

        return string.Join("\n", parts);
    }

    internal static IReadOnlyList<InboxAction> BuildActions(
        Plan plan,
        IReadOnlyList<string> activeGateIds)
    {
        if (activeGateIds.Count == 0)
            return [];

        var actions = new List<InboxAction>();
        foreach (var gateId in activeGateIds)
        {
            actions.Add(new InboxAction
            {
                Label = $"Approve: {gateId}",
                RouteMode = "done",
                Hint = $"Approve checkpoint {gateId} and unblock downstream tasks.",
            });
        }
        return actions;
    }

    // ── Test seam ────────────────────────────────────────────────────────────

    /// <summary>Clears per-plan locks. For testing only.</summary>
    internal void ClearLocks() => _planLocks.Clear();
}
