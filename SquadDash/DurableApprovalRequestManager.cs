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
    string? ResolutionNote = null,
    [property: JsonPropertyName("disposition")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Disposition = null);

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
    internal const string ApprovalRouteMode = "approval-gate";
    internal const string ApprovalReviewRouteMode = "approval-gate-review";
    internal const string ApprovalAddressUnapprovedRouteMode = "approval-gate-address-unapproved";
    internal const string RunbookAttachmentType = "inbox-message-ref";
    internal const string OperatorGuideAttachmentType = "approval-operator-guide";
    internal const string ApprovalRunbookLink = "app://open-approval-runbook";
    internal const string RunbookAttachmentLabel = "Open full validation runbook";
    internal const string GeneratedInstructionsAttachmentLabel = "Open full test instructions";
    internal const string ProvenanceAttachmentLabel = "Technical details";
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
                        LastNotifiedAt = null,
                        Archived = false,
                        Version = (state?.Version ?? 0) + 1,
                    };
                var reviewSnapshot = MergeSnapshots(DeserializeSnapshot(existing), snapshot);

                var updated = existing with
                {
                    // A newly available checkpoint should bring the plan's single
                    // aggregated request back to the top of the Inbox instead of
                    // leaving it buried at the time of the first checkpoint.
                    Timestamp = DateTimeOffset.UtcNow,
                    Read = false,
                    Body = BuildApprovalBody(plan, updatedGateIds, newState.ResolvedCheckpoints, reviewSnapshot),
                    Actions = BuildApprovalActions(plan, newState),
                    Attachments = BuildAttachments(newState, reviewSnapshot, plan: plan),
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
                    Body = BuildApprovalBody(plan, [gate.GateId], [], snapshot),
                    Attachments = BuildAttachments(state, snapshot, plan: plan),
                    Actions = BuildApprovalActions(plan, state),
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
            var reviewSnapshot = MergeSnapshots(DeserializeSnapshot(existing), snapshot);
            var body = BuildApprovalBody(
                plan,
                state.ActiveGateIds,
                state.ResolvedCheckpoints,
                reviewSnapshot,
                existing.Attachments);
            var presentationChanged = !string.Equals(body, existing.Body, StringComparison.Ordinal);

            var updated = existing with
            {
                Timestamp = presentationChanged ? DateTimeOffset.UtcNow : existing.Timestamp,
                Read = presentationChanged ? false : existing.Read,
                Body = body,
                Actions = BuildApprovalActions(plan, state, existing.Attachments),
                Attachments = BuildAttachments(state, reviewSnapshot, plan: plan),
            };
            _inbox.Save(updated);
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Rebuilds the human-facing approval request from the authoritative plan while preserving
    /// its existing evidence snapshot and action version. Used when a reviewer clarifies an
    /// awaiting gate's message, question, or human proof contract.
    /// </summary>
    internal async Task RefreshFromPlanAsync(
        Plan plan,
        CancellationToken cancellationToken = default)
    {
        var sem = GetLock(plan.PlanId);
        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = _inbox.GetById(BuildMessageId(plan.PlanId));
            if (existing is null) return;

            var state = DeserializeState(existing);
            if (state is null) return;
            var snapshot = DeserializeSnapshot(existing);
            var body = BuildApprovalBody(
                plan,
                state.ActiveGateIds,
                state.ResolvedCheckpoints,
                snapshot,
                existing.Attachments);
            var presentationChanged = !string.Equals(body, existing.Body, StringComparison.Ordinal);
            _inbox.Save(existing with
            {
                Timestamp = presentationChanged ? DateTimeOffset.UtcNow : existing.Timestamp,
                Read = presentationChanged ? false : existing.Read,
                Body = body,
                Actions = BuildApprovalActions(plan, state, existing.Attachments),
                Attachments = BuildAttachments(
                    state, snapshot, existing.Attachments, plan),
            });
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
            var reviewSnapshot = DeserializeSnapshot(existing);

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
                    Body = BuildApprovalBody(plan, [], newState.ResolvedCheckpoints, existingAttachments: existing.Attachments),
                    Attachments = BuildAttachments(newState, snapshot: null, existingAttachments: existing.Attachments, plan: plan),
                };
                _inbox.Save(archived);
            }
            else
            {
                var updated = existing with
                {
                    Read = true,
                    Body = BuildApprovalBody(plan, remainingGates, newState.ResolvedCheckpoints, reviewSnapshot, existing.Attachments),
                    Actions = BuildApprovalActions(plan, newState, existing.Attachments),
                    Attachments = BuildAttachments(newState, snapshot: null, existingAttachments: existing.Attachments, plan: plan),
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
    /// Closes the current approval request because reviewed work was sent back for another
    /// attempt. The stable message is reused when the checkpoint becomes ready again.
    /// </summary>
    internal async Task RecordReworkAsync(
        Plan plan,
        string gateId,
        string instructions,
        CancellationToken cancellationToken = default)
    {
        var sem = GetLock(plan.PlanId);
        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = _inbox.GetById(BuildMessageId(plan.PlanId));
            if (existing is null) return;
            var state = DeserializeState(existing);
            if (state is null || !state.ActiveGateIds.Contains(gateId, StringComparer.Ordinal)) return;
            var reviewSnapshot = DeserializeSnapshot(existing);

            var remaining = state.ActiveGateIds
                .Where(id => !string.Equals(id, gateId, StringComparison.Ordinal))
                .ToArray();
            var history = state.ResolvedCheckpoints.Append(new ResolvedCheckpointEntry(
                gateId,
                DateTimeOffset.UtcNow,
                instructions,
                "rework-requested")).ToArray();
            var updatedState = state with
            {
                ActiveGateIds = remaining,
                ResolvedCheckpoints = history,
                Archived = remaining.Length == 0,
                LastNotifiedAt = null,
                Version = state.Version + 1,
            };
            _inbox.Save(existing with
            {
                Read = remaining.Length == 0,
                Body = BuildApprovalBody(plan, remaining, history, reviewSnapshot, existing.Attachments),
                Actions = BuildApprovalActions(plan, updatedState, existing.Attachments),
                Attachments = BuildAttachments(
                    updatedState,
                    snapshot: null,
                    existingAttachments: existing.Attachments,
                    plan: plan),
            });
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

    private static ApprovalReviewSnapshot? DeserializeSnapshot(InboxMessage message)
    {
        var attachment = message.Attachments.FirstOrDefault(
            item => string.Equals(item.Type, "approval-snapshot", StringComparison.OrdinalIgnoreCase));
        if (attachment is null || string.IsNullOrWhiteSpace(attachment.Content))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ApprovalReviewSnapshot>(attachment.Content, StateSerializerOptions);
        }
        catch (JsonException ex)
        {
            SquadDashTrace.Write(TraceCategory.Inbox,
                $"Approval review snapshot could not be parsed: {ex.Message}");
            return null;
        }
    }

    private static ApprovalReviewSnapshot MergeSnapshots(
        ApprovalReviewSnapshot? existing,
        ApprovalReviewSnapshot current)
    {
        if (existing is null)
            return current;

        return current with
        {
            CompletedTasks = existing.CompletedTasks.Concat(current.CompletedTasks)
                .GroupBy(item => item.TaskId, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray(),
            DownstreamTasks = existing.DownstreamTasks.Concat(current.DownstreamTasks)
                .GroupBy(item => item.TaskId, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray(),
            AllChangedFiles = existing.AllChangedFiles.Concat(current.AllChangedFiles)
                .GroupBy(item => $"{item.CommitSha}\0{item.FilePath}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToArray(),
            IndependentWork = existing.IndependentWork.Concat(current.IndependentWork)
                .GroupBy(item => item.TaskId, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray(),
        };
    }

    private IReadOnlyList<InboxAttachment> BuildAttachments(
        DurableApprovalState state,
        ApprovalReviewSnapshot? snapshot,
        IReadOnlyList<InboxAttachment>? existingAttachments = null,
        Plan? plan = null)
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
            if (plan is not null)
                snapshot = snapshot with
                {
                    HasOperatorInstructions = HasOperatorInstructions(
                        plan,
                        plan.ApprovalGates.Where(gate => state.ActiveGateIds.Contains(
                            gate.GateId, StringComparer.Ordinal)).ToArray(),
                        _inbox),
                };
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

        // The approval message is the operator's single working surface. Preserve the concise
        // AI-authored guide as hidden host state so later refreshes can rebuild that surface
        // without requiring the source kit to remain visible as a second Inbox item.
        var runbooks = plan is null
            ? []
            : FindBoundManualTestKits(plan, state.ActiveGateIds, _inbox);
        if (runbooks.Count > 0)
        {
            attachments.Add(new InboxAttachment
            {
                Type = OperatorGuideAttachmentType,
                Label = "Approval operator guide",
                Content = BuildCombinedOperatorGuide(plan!, state.ActiveGateIds, runbooks),
                PlanGroupId = plan!.PlanId,
                PlanRevision = plan.Revision,
            });

            // A long file-backed runbook is optional troubleshooting/reference material. It is
            // deliberately not the route to the required steps; those are in the message body.
            foreach (var runbook in runbooks)
            {
                var runbookFile = runbook.Attachments.FirstOrDefault(attachment =>
                    string.Equals(attachment.Type, "file", StringComparison.OrdinalIgnoreCase) &&
                    attachment.Path?.EndsWith(".md", StringComparison.OrdinalIgnoreCase) == true &&
                    (attachment.Label.Contains("runbook", StringComparison.OrdinalIgnoreCase) ||
                     attachment.Label.Contains("instruction", StringComparison.OrdinalIgnoreCase)));
                if (runbookFile is not null)
                {
                    attachments.Add(runbookFile with
                    {
                        Type = "file",
                        Label = "Detailed troubleshooting (optional)",
                        PlanGroupId = plan!.PlanId,
                        PlanRevision = plan.Revision,
                    });
                }
            }
        }
        else if (plan is not null && BuildGeneratedInstructions(plan, state.ActiveGateIds) is { } instructions)
        {
            attachments.Add(new InboxAttachment
            {
                Type = OperatorGuideAttachmentType,
                Label = "Approval operator guide",
                Content = instructions,
                PlanGroupId = plan.PlanId,
                PlanRevision = plan.Revision,
            });
        }
        else if (existingAttachments is not null)
        {
            attachments.AddRange(existingAttachments.Where(attachment =>
                string.Equals(attachment.Type, OperatorGuideAttachmentType, StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(attachment.Type, "file", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(attachment.Label, "Detailed troubleshooting (optional)", StringComparison.Ordinal))));
        }

        if (plan is not null && snapshot is not null &&
            BuildReviewEvidence(plan, state.ActiveGateIds, snapshot) is { } evidence)
        {
            attachments.Add(new InboxAttachment
            {
                Type = "text",
                Label = ProvenanceAttachmentLabel,
                Content = evidence,
                PlanGroupId = plan.PlanId,
                PlanRevision = plan.Revision,
            });
        }
        else if (existingAttachments is not null)
        {
            var existingEvidence = existingAttachments.FirstOrDefault(attachment =>
                string.Equals(attachment.Label, ProvenanceAttachmentLabel, StringComparison.Ordinal));
            if (existingEvidence is not null)
                attachments.Add(existingEvidence);
        }

        // The state and snapshot entries above are persistence envelopes and are deliberately
        // hidden from the Inbox attachment strip. The plan graph remains secondary navigation.
        if (plan is not null)
        {
            attachments.Add(new InboxAttachment
            {
                Type = DecomposePlanInbox.AttachmentType,
                Label = "View plan and dependencies",
                PlanGroupId = plan.PlanId,
                PlanRevision = plan.Revision,
                Content = JsonSerializer.Serialize(PendingDecomposePlanAdapter.FromPlan(plan), StateSerializerOptions),
            });
        }
        else if (existingAttachments is not null)
        {
            var existingPlan = existingAttachments.FirstOrDefault(
                a => string.Equals(a.Type, DecomposePlanInbox.AttachmentType, StringComparison.OrdinalIgnoreCase));
            if (existingPlan is not null)
                attachments.Add(existingPlan);
        }

        return attachments;
    }

    internal static bool HasOperatorInstructions(
        Plan plan,
        IReadOnlyList<PlanApprovalGate> gates,
        InboxStore? inbox = null)
    {
        // Recipes generate setup/open/observe/cleanup steps; directly reachable placements
        // without recipes only repeat the observation and are not operator instructions.
        return gates.Any(gate => gate.ProofRequirements?.Any(
                   requirement => requirement.VerificationRecipe is not null) == true) ||
               (inbox is not null && FindBoundManualTestKits(
                   plan, gates.Select(gate => gate.GateId).ToArray(), inbox).Count > 0);
    }

    private static IReadOnlyList<InboxMessage> FindBoundManualTestKits(
        Plan plan,
        IReadOnlyList<string> activeGateIds,
        InboxStore inbox)
    {
        var boundCandidates = inbox.LoadAll(
                includeApprovalKitSources: true,
                includeArchive: true)
            .Select(message => (Message: message, Binding: ManualTestKitApprovalBinding.Read(message)))
            .Where(candidate => candidate.Binding is not null &&
                string.Equals(candidate.Binding.PlanId, plan.PlanId, StringComparison.Ordinal) &&
                string.Equals(candidate.Binding.PlanRevision, plan.Revision, StringComparison.Ordinal))
            .Select(candidate => (
                candidate.Message,
                Binding: candidate.Binding!,
                Error: ManualTestKitApprovalBinding.ValidateSourceBodyForGates(
                    plan,
                    candidate.Binding!.GateIds,
                    candidate.Message.Body)))
            .ToArray();
        foreach (var rejected in boundCandidates.Where(candidate => candidate.Error is not null))
        {
            SquadDashTrace.Write(
                TraceCategory.Inbox,
                $"APPROVAL_SETUP_ERROR: ignored invalid stored approval procedure source id={rejected.Message.Id}: {rejected.Error}");
        }
        var candidates = boundCandidates
            .Where(candidate => candidate.Error is null)
            .Select(candidate => (candidate.Message, candidate.Binding))
            .ToArray();

        // LoadAll is newest-first. Select one authoritative delivery for each active gate while
        // allowing one kit to cover several gates without producing duplicate links.
        return activeGateIds
            .Select(gateId => candidates.FirstOrDefault(candidate =>
                candidate.Binding!.GateIds.Contains(gateId, StringComparer.Ordinal)).Message)
            .Where(message => message is not null)
            .Cast<InboxMessage>()
            .DistinctBy(message => message.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? BuildGeneratedInstructions(
        Plan plan,
        IReadOnlyList<string> activeGateIds)
    {
        var parts = new List<string>();
        foreach (var gateId in activeGateIds.OrderBy(gateId => GetGateOrder(plan, gateId)))
        {
            var gate = plan.ApprovalGates.FirstOrDefault(candidate =>
                string.Equals(candidate.GateId, gateId, StringComparison.Ordinal));
            if (gate is null)
                continue;

            var recipes = gate.ProofRequirements?
                .Where(requirement => requirement.VerificationRecipe is not null)
                .ToArray() ?? [];
            var workflows = recipes.GroupBy(requirement =>
            {
                var recipe = requirement.VerificationRecipe!;
                return (recipe.Setup.Trim(), recipe.Open.Trim(), recipe.Cleanup.Trim());
            });
            foreach (var workflow in workflows)
            {
                var requirements = workflow.ToArray();
                var recipe = requirements[0].VerificationRecipe!;
                if (parts.Count > 0)
                    parts.Add(string.Empty);
                parts.Add(requirements.Length == 1
                    ? $"## {PlanHumanReviewChecklist.ResolveDisplayTitle(
                        requirements[0].Title,
                        requirements[0].RequirementId)}"
                    : "## Shared verification workflow");
                parts.Add($"1. **Set up:** {recipe.Setup}");
                parts.Add($"2. **Open and perform:** {recipe.Open}");
                foreach (var requirement in requirements)
                {
                    var observeLabel = requirements.Length == 1
                        ? "3. **Observe:**"
                        : $"**Observe — {PlanHumanReviewChecklist.ResolveDisplayTitle(
                            requirement.Title,
                            requirement.RequirementId)}:**";
                    parts.Add($"{observeLabel} {requirement.VerificationRecipe!.Observe}");
                    parts.Add($"{{{{approval-check:{requirement.RequirementId}}}}}");
                }
                if (!string.IsNullOrWhiteSpace(recipe.Cleanup))
                    parts.Add($"4. **Clean up:** {recipe.Cleanup}");
            }

            foreach (var requirement in gate.ProofRequirements ?? [])
            {
                if (requirement.VerificationRecipe is not null ||
                    !string.Equals(requirement.AccessMode, HumanVerificationAccessMode.DirectlyReachable,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                if (parts.Count > 0)
                    parts.Add(string.Empty);
                parts.Add($"## {PlanHumanReviewChecklist.ResolveDisplayTitle(
                    requirement.Title, requirement.RequirementId)}");
                parts.Add($"**Observe in the running app:** {requirement.Description}");
                parts.Add($"{{{{approval-check:{requirement.RequirementId}}}}}");
            }
        }

        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    private static string? BuildReviewEvidence(
        Plan plan,
        IReadOnlyList<string> activeGateIds,
        ApprovalReviewSnapshot snapshot)
    {
        var parts = new List<string> { "# Provenance & implementation evidence" };
        AppendReviewEvidence(parts, plan, activeGateIds, snapshot);
        return parts.Count == 1 ? null : string.Join("\n", parts);
    }

    /// <summary>True for attachments intended for people rather than approval persistence.</summary>
    internal static bool IsPresentationAttachment(InboxAttachment attachment) =>
        !string.Equals(attachment.Type, AttachmentType, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(attachment.Type, "approval-snapshot", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(attachment.Type, RunbookAttachmentType, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(attachment.Type, OperatorGuideAttachmentType, StringComparison.OrdinalIgnoreCase) &&
        !ManualTestKitApprovalBinding.IsBindingAttachment(attachment);

    private string BuildApprovalBody(
        Plan plan,
        IReadOnlyList<string> activeGateIds,
        IReadOnlyList<ResolvedCheckpointEntry> resolvedCheckpoints,
        ApprovalReviewSnapshot? snapshot = null,
        IReadOnlyList<InboxAttachment>? existingAttachments = null)
    {
        var operatorGuide = ResolveOperatorGuide(plan, activeGateIds, existingAttachments);
        return BuildBody(plan, activeGateIds, resolvedCheckpoints, snapshot, operatorGuide);
    }

    private string? ResolveOperatorGuide(
        Plan plan,
        IReadOnlyList<string> activeGateIds,
        IReadOnlyList<InboxAttachment>? existingAttachments = null)
    {
        var kits = FindBoundManualTestKits(plan, activeGateIds, _inbox);
        return kits.Count > 0
            ? BuildCombinedOperatorGuide(plan, activeGateIds, kits)
            : existingAttachments?.FirstOrDefault(attachment => string.Equals(
                attachment.Type,
                OperatorGuideAttachmentType,
                StringComparison.OrdinalIgnoreCase))?.Content
              ?? BuildGeneratedInstructions(plan, activeGateIds);
    }

    private static string BuildCombinedOperatorGuide(
        Plan plan,
        IReadOnlyList<string> activeGateIds,
        IReadOnlyList<InboxMessage> kits)
    {
        // FindBoundManualTestKits validates every proof marker for each bound gate.
        // Generate only for sibling gates outside that authoritative coverage.
        var coveredGateIds = kits
            .SelectMany(kit => ManualTestKitApprovalBinding.Read(kit)!.GateIds)
            .ToHashSet(StringComparer.Ordinal);
        var uncoveredGateIds = activeGateIds
            .Where(gateId => !coveredGateIds.Contains(gateId))
            .ToArray();
        var parts = kits.Select(kit => kit.Body).ToList();
        if (BuildGeneratedInstructions(plan, uncoveredGateIds) is { } generated)
            parts.Add(generated);
        return string.Join("\n\n", parts);
    }

    private IReadOnlyList<InboxAction> BuildApprovalActions(
        Plan plan,
        DurableApprovalState state,
        IReadOnlyList<InboxAttachment>? existingAttachments = null)
    {
        var guide = ResolveOperatorGuide(plan, state.ActiveGateIds, existingAttachments);
        if (string.IsNullOrWhiteSpace(guide))
            return BuildActions(plan, state);

        var gates = state.ActiveGateIds
            .Select(gateId => plan.ApprovalGates.FirstOrDefault(gate =>
                string.Equals(gate.GateId, gateId, StringComparison.Ordinal)))
            .Where(gate => gate is not null)
            .Cast<PlanApprovalGate>()
            .ToArray();
        var checklist = PlanHumanReviewChecklist.Build(plan, gates);
        var composed = ManualTestKitChecklistComposer.Compose(guide, checklist);
        var setupValid = !composed.Contains(
            ManualTestKitChecklistComposer.SetupErrorMarker,
            StringComparison.Ordinal);
        return BuildActions(plan, state, setupValid);
    }

    internal static string BuildBody(
        Plan plan,
        IReadOnlyList<string> activeGateIds,
        IReadOnlyList<ResolvedCheckpointEntry> resolvedCheckpoints,
        ApprovalReviewSnapshot? snapshot = null,
        string? operatorGuide = null)
    {
        var parts = new List<string>();

        parts.Add($"**Approval requested: {plan.Title}**");

        if (activeGateIds.Count > 0)
        {
            parts.Add("");
            if (!string.IsNullOrWhiteSpace(operatorGuide))
            {
                var gates = activeGateIds
                    .Select(gateId => plan.ApprovalGates.FirstOrDefault(gate =>
                        string.Equals(gate.GateId, gateId, StringComparison.Ordinal)))
                    .Where(gate => gate is not null)
                    .Cast<PlanApprovalGate>()
                    .ToArray();
                var checklist = PlanHumanReviewChecklist.Build(plan, gates);
                parts.Add("Complete each short check below. Use the links at the step where they appear, then check the observed result.");
                parts.Add("");
                parts.Add(ManualTestKitChecklistComposer.Compose(operatorGuide.Trim(), checklist));
            }
            else
            {
                parts.Add(activeGateIds.Count == 1
                    ? "Review this checkpoint:"
                    : $"Review these {activeGateIds.Count} checkpoints:");
                foreach (var gateId in activeGateIds.OrderBy(gateId => GetGateOrder(plan, gateId)))
                {
                    var gate = plan.ApprovalGates.FirstOrDefault(
                        g => string.Equals(g.GateId, gateId, StringComparison.Ordinal));
                    var reason = gate?.Message ?? gateId;
                    var stepLabel = gate is null ? null : ResolveGateStepLabel(plan, gate);
                    parts.Add(string.IsNullOrWhiteSpace(stepLabel)
                        ? $"- {reason}"
                        : $"- **Step {stepLabel}:** {reason}");
                    if (gate is not null)
                    {
                        var reviewItems = PlanHumanReviewChecklist.Build(plan, [gate]);
                        if (reviewItems.Count > 0)
                        {
                            parts.Add("");
                            parts.Add("**Confirm each observed result**");
                            foreach (var item in reviewItems)
                                parts.Add($"- {(item.IsChecked ? "☑" : item.WasPreviouslyChecked ? "?" : "☐")} {item.Text}");
                        }
                    }
                }
            }
        }

        if (resolvedCheckpoints.Count > 0)
        {
            parts.Add("");
            parts.Add($"**{resolvedCheckpoints.Count} resolved checkpoint(s):**");
            foreach (var cp in resolvedCheckpoints)
            {
                var note = cp.ResolutionNote is not null ? $" — {cp.ResolutionNote}" : "";
                if (string.Equals(cp.Disposition, "rework-requested", StringComparison.Ordinal))
                {
                    parts.Add($"- ↩ `{cp.GateId}` changes requested {cp.ResolvedAt:yyyy-MM-dd HH:mm}{note}");
                    continue;
                }
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

    private static void AppendReviewEvidence(
        List<string> parts,
        Plan plan,
        IReadOnlyList<string> activeGateIds,
        ApprovalReviewSnapshot? snapshot)
    {
        if (snapshot is null)
            return;

        var activeGates = activeGateIds
            .Select(gateId => plan.ApprovalGates.FirstOrDefault(gate =>
                string.Equals(gate.GateId, gateId, StringComparison.Ordinal)))
            .Where(gate => gate is not null)
            .Cast<PlanApprovalGate>()
            .ToArray();
        var reviewTaskIds = activeGates
            .SelectMany(gate => gate.AfterTaskIds)
            .ToHashSet(StringComparer.Ordinal);
        var downstreamTaskIds = activeGates
            .SelectMany(gate => gate.BeforeTaskIds)
            .ToHashSet(StringComparer.Ordinal);
        var completedTasks = snapshot.CompletedTasks
            .Where(task => reviewTaskIds.Contains(task.TaskId))
            .OrderBy(task => GetTaskOrder(plan, task.TaskId))
            .ToArray();

        if (completedTasks.Length > 0)
        {
            parts.Add("");
            parts.Add("**Completed work ready for review**");
            foreach (var task in completedTasks)
            {
                var durableTask = plan.Tasks.FirstOrDefault(candidate =>
                    string.Equals(candidate.TaskId, task.TaskId, StringComparison.Ordinal));
                var heading = string.IsNullOrWhiteSpace(durableTask?.DisplayStepLabel)
                    ? task.Title
                    : $"Step {durableTask.DisplayStepLabel}: {task.Title}";
                parts.Add($"- **{heading}**");
                if (!string.IsNullOrWhiteSpace(task.CompletionSummary))
                    parts.Add($"  - Handoff: {task.CompletionSummary}");
                if (!string.IsNullOrWhiteSpace(task.VerificationSummary))
                    parts.Add($"  - Verification: {task.VerificationSummary}");
                foreach (var commit in task.Commits)
                {
                    var verification = commit.VerificationPassed switch
                    {
                        true => " · verification passed",
                        false => " · verification failed",
                        _ => string.Empty,
                    };
                    parts.Add($"  - Commit `{commit.Link.ShortSha}` — {commit.Link.Subject}{verification}");
                }
            }
        }

        var reviewCommitIds = completedTasks
            .SelectMany(task => task.Commits)
            .Select(commit => commit.Link.FullSha)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changedFiles = snapshot.AllChangedFiles
            .Where(file => reviewCommitIds.Contains(file.CommitSha))
            .ToArray();
        if (changedFiles.Length > 0)
        {
            parts.Add("");
            parts.Add($"**Changed files ({changedFiles.Length})**");
            foreach (var file in changedFiles)
                parts.Add($"- `{file.FilePath}` · {file.Status} · +{file.Insertions}/-{file.Deletions}");
        }

        var downstreamTasks = snapshot.DownstreamTasks
            .Where(task => downstreamTaskIds.Contains(task.TaskId))
            .OrderBy(task => GetTaskOrder(plan, task.TaskId))
            .ToArray();
        if (downstreamTasks.Length > 0)
        {
            parts.Add("");
            parts.Add("**Approval allows this work to continue**");
            foreach (var task in downstreamTasks)
                parts.Add($"- {task.Title}");
        }
    }

    private static int GetTaskOrder(Plan plan, string taskId)
    {
        for (var index = 0; index < plan.Tasks.Count; index++)
        {
            if (string.Equals(plan.Tasks[index].TaskId, taskId, StringComparison.Ordinal))
                return index;
        }
        return int.MaxValue;
    }

    private static int GetGateOrder(Plan plan, string gateId)
    {
        var gate = plan.ApprovalGates.FirstOrDefault(candidate =>
            string.Equals(candidate.GateId, gateId, StringComparison.Ordinal));
        return gate is null
            ? int.MaxValue
            : gate.AfterTaskIds.Select(taskId => GetTaskOrder(plan, taskId)).DefaultIfEmpty(int.MaxValue).Min();
    }

    private static string? ResolveGateStepLabel(Plan plan, PlanApprovalGate gate) =>
        gate.AfterTaskIds
            .Select(taskId => plan.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, taskId, StringComparison.Ordinal))?.DisplayStepLabel)
            .FirstOrDefault(label => !string.IsNullOrWhiteSpace(label));

    internal static IReadOnlyList<InboxAction> BuildActions(
        Plan plan,
        IReadOnlyList<string> activeGateIds)
    {
        if (activeGateIds.Count == 0)
            return [];

        return BuildActions(plan, new DurableApprovalState(
            plan.PlanId, activeGateIds, [], Version: 1));
    }

    internal static IReadOnlyList<InboxAction> BuildActions(
        Plan plan,
        DurableApprovalState state)
        => BuildActions(plan, state, approvalSetupValid: true);

    private static IReadOnlyList<InboxAction> BuildActions(
        Plan plan,
        DurableApprovalState state,
        bool approvalSetupValid)
    {
        if (state.ActiveGateIds.Count == 0)
            return [];

        var payload = new ApprovalInboxActionPayload(
            plan.PlanId,
            plan.Revision,
            state.Version,
            state.ActiveGateIds);
        var actions = new List<InboxAction>();

        var activeGates = state.ActiveGateIds
            .Select(gateId => plan.ApprovalGates.FirstOrDefault(gate =>
                string.Equals(gate.GateId, gateId, StringComparison.Ordinal)))
            .Where(gate => gate is not null)
            .Cast<PlanApprovalGate>()
            .ToArray();
        var checklist = PlanHumanReviewChecklist.Build(plan, activeGates);
        if (approvalSetupValid && checklist.Count > 0 && checklist.Any(item => !item.IsChecked))
        {
            actions.Add(new InboxAction
            {
                Label = PlanReviewActionPresentation.AddressUnapprovedItemsLabel,
                RouteMode = ApprovalAddressUnapprovedRouteMode,
                Prompt = JsonSerializer.Serialize(payload, StateSerializerOptions),
                Hint = PlanReviewActionPresentation.AddressUnapprovedItemsToolTip,
            });
        }

        if (approvalSetupValid && (checklist.Count == 0 || checklist.All(item => item.IsChecked)))
        {
            actions.Add(new InboxAction
            {
                Label = PlanCheckpointPrimaryActionPresentationResolver.Resolve(
                    plan, state.ActiveGateIds).Label,
                RouteMode = ApprovalRouteMode,
                Prompt = JsonSerializer.Serialize(payload, StateSerializerOptions),
                Hint = "Record final approval and continue the plan. Checking the last item does not continue automatically.",
            });
        }

        return actions;
    }

    /// <summary>Reads the durable approval state embedded in an Inbox message.</summary>
    internal static DurableApprovalState? ReadState(InboxMessage message) => DeserializeState(message);

    // ── Test seam ────────────────────────────────────────────────────────────

    /// <summary>Clears per-plan locks. For testing only.</summary>
    internal void ClearLocks() => _planLocks.Clear();
}

internal sealed record ApprovalInboxActionPayload(
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("planRevision")] string PlanRevision,
    [property: JsonPropertyName("requestVersion")] int RequestVersion,
    [property: JsonPropertyName("gateIds")] IReadOnlyList<string> GateIds)
{
    internal ApprovalClickToken ToClickToken() =>
        new(PlanId, PlanRevision, RequestVersion, GateIds);

    internal static bool TryParse(string? json, out ApprovalInboxActionPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            payload = JsonSerializer.Deserialize<ApprovalInboxActionPayload>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return payload is not null &&
                   !string.IsNullOrWhiteSpace(payload.PlanId) &&
                   !string.IsNullOrWhiteSpace(payload.PlanRevision) &&
                   payload.RequestVersion > 0 &&
                   payload.GateIds.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
