using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SquadDash;

/// <summary>
/// Owns the lifecycle and identity-resolution of all agent transcript threads —
/// creation, aliasing, key lookup, status updates, and finalization.
/// </summary>
internal sealed class AgentThreadRegistry {

    // ── Core state ──────────────────────────────────────────────────────────

    private readonly Dictionary<string, TranscriptThreadState> _agentThreadsByKey
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, TranscriptThreadState> _agentThreadsByToolCallId
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, BackgroundAgentLaunchInfo> _agentLaunchesByToolCallId
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<TranscriptThreadState> _agentThreadOrder = [];

    private readonly Dictionary<string, ToolTranscriptEntry> _toolEntries
        = new(StringComparer.Ordinal);

    // ── Exposed collection accessors (read-only) ─────────────────────────────

    internal IReadOnlyDictionary<string, TranscriptThreadState> ThreadsByKey
        => _agentThreadsByKey;

    internal IReadOnlyDictionary<string, TranscriptThreadState> ThreadsByToolCallId
        => _agentThreadsByToolCallId;

    internal IReadOnlyDictionary<string, BackgroundAgentLaunchInfo> LaunchesByToolCallId
        => _agentLaunchesByToolCallId;

    internal IReadOnlyList<TranscriptThreadState> ThreadOrder
        => _agentThreadOrder;

    internal IReadOnlyDictionary<string, ToolTranscriptEntry> ToolEntries
        => _toolEntries;

    // ── Mutation methods ─────────────────────────────────────────────────────

    internal void ClearAll() {
        _agentThreadsByKey.Clear();
        _agentThreadsByToolCallId.Clear();
        _agentLaunchesByToolCallId.Clear();
        _agentThreadOrder.Clear();
        _toolEntries.Clear();
    }

    internal void RemoveThreads(IEnumerable<TranscriptThreadState> threads) {
        var toRemove = threads as IReadOnlyCollection<TranscriptThreadState>
            ?? threads.ToArray();

        foreach (var key in _agentThreadsByKey
            .Where(e => toRemove.Contains(e.Value))
            .Select(e => e.Key)
            .ToArray())
            _agentThreadsByKey.Remove(key);

        foreach (var key in _agentThreadsByToolCallId
            .Where(e => toRemove.Contains(e.Value))
            .Select(e => e.Key)
            .ToArray())
            _agentThreadsByToolCallId.Remove(key);

        foreach (var thread in toRemove)
            _agentThreadOrder.Remove(thread);
    }

    internal bool TryGetToolEntry(string key, out ToolTranscriptEntry entry)
        => _toolEntries.TryGetValue(key, out entry!);

    internal void SetToolEntry(string key, ToolTranscriptEntry entry)
        => _toolEntries[key] = entry;

    // ── Constructor delegates ────────────────────────────────────────────────

    private readonly Action<TranscriptThreadState, string?> _beginTranscriptTurn;
    private readonly Action<TranscriptThreadState> _finalizeCurrentTurnResponse;
    private readonly Action<TranscriptThreadState> _collapseCurrentTurnThinking;
    private readonly Action<ToolTranscriptEntry> _renderToolEntry;
    private readonly Action _updateToolSpinnerState;
    private readonly Action _syncActiveToolName;
    private readonly Action<TranscriptThreadState> _syncThreadChip;
    private readonly Action<TranscriptThreadState> _syncTaskToolTranscriptLink;
    private readonly Action<TranscriptThreadState, string> _appendText;
    private readonly Action _syncAgentCards;
    private readonly Action _syncAgentCardsWithThreads;
    private readonly Func<TeamAgentDescriptor[]> _getKnownTeamAgentDescriptors;
    private readonly Action _updateTranscriptThreadBadge;
    private readonly Func<TranscriptThreadState, bool> _isThreadActiveForDisplay;
    private readonly Action<TranscriptThreadState, string> _observeBackgroundAgentActivity;
    private readonly Func<TranscriptThreadState, IReadOnlyList<TranscriptTurnRecord>, Task> _renderConversationHistory;
    private readonly Func<SquadBackgroundAgentInfo, string> _resolveBackgroundAgentDisplayLabel;
    private readonly Func<TranscriptThreadState, string> _buildAgentLabel;

    internal AgentThreadRegistry(
        Action<TranscriptThreadState, string?> beginTranscriptTurn,
        Action<TranscriptThreadState> finalizeCurrentTurnResponse,
        Action<TranscriptThreadState> collapseCurrentTurnThinking,
        Action<ToolTranscriptEntry> renderToolEntry,
        Action updateToolSpinnerState,
        Action syncActiveToolName,
        Action<TranscriptThreadState> syncThreadChip,
        Action<TranscriptThreadState> syncTaskToolTranscriptLink,
        Action<TranscriptThreadState, string> appendText,
        Action syncAgentCards,
        Action syncAgentCardsWithThreads,
        Func<TeamAgentDescriptor[]> getKnownTeamAgentDescriptors,
        Action updateTranscriptThreadBadge,
        Func<TranscriptThreadState, bool> isThreadActiveForDisplay,
        Action<TranscriptThreadState, string> observeBackgroundAgentActivity,
        Func<TranscriptThreadState, IReadOnlyList<TranscriptTurnRecord>, Task> renderConversationHistory,
        Func<SquadBackgroundAgentInfo, string> resolveBackgroundAgentDisplayLabel,
        Func<TranscriptThreadState, string> buildAgentLabel) {

        _beginTranscriptTurn              = beginTranscriptTurn;
        _finalizeCurrentTurnResponse      = finalizeCurrentTurnResponse;
        _collapseCurrentTurnThinking      = collapseCurrentTurnThinking;
        _renderToolEntry                  = renderToolEntry;
        _updateToolSpinnerState           = updateToolSpinnerState;
        _syncActiveToolName               = syncActiveToolName;
        _syncThreadChip                   = syncThreadChip;
        _syncTaskToolTranscriptLink       = syncTaskToolTranscriptLink;
        _appendText                       = appendText;
        _syncAgentCards                   = syncAgentCards;
        _syncAgentCardsWithThreads        = syncAgentCardsWithThreads;
        _getKnownTeamAgentDescriptors     = getKnownTeamAgentDescriptors;
        _updateTranscriptThreadBadge      = updateTranscriptThreadBadge;
        _isThreadActiveForDisplay         = isThreadActiveForDisplay;
        _observeBackgroundAgentActivity   = observeBackgroundAgentActivity;
        _renderConversationHistory        = renderConversationHistory;
        _resolveBackgroundAgentDisplayLabel = resolveBackgroundAgentDisplayLabel;
        _buildAgentLabel                  = buildAgentLabel;
    }

    // ── Thread creation ──────────────────────────────────────────────────────

    internal TranscriptThreadState GetOrCreateAgentThread(SquadSdkEvent evt) {
        var toolCallId = evt.ParentToolCallId ?? evt.ToolCallId;
        var thread = GetOrCreateAgentThread(
            toolCallId,
            evt.AgentId,
            evt.AgentName,
            evt.AgentDisplayName,
            evt.AgentDescription,
            evt.Status,
            evt.Prompt,
            evt.StartedAt);
        TryApplyOriginMetadataFromParentToolCall(thread, evt.ParentToolCallId);

        if (!string.IsNullOrWhiteSpace(evt.Type) &&
            evt.Type.StartsWith("subagent_", StringComparison.OrdinalIgnoreCase)) {
            thread.WasObservedAsBackgroundTask = true;
        }

        return thread;
    }

    internal TranscriptThreadState GetOrCreateAgentThread(
        string? toolCallId,
        string? agentId,
        string? agentName,
        string? agentDisplayName,
        string? agentDescription,
        string? status,
        string? prompt,
        string? startedAt) {

        var threadKey = !string.IsNullOrWhiteSpace(toolCallId)
            ? toolCallId.Trim()
            : !string.IsNullOrWhiteSpace(agentId)
                ? "agent:" + agentId.Trim()
                : "agent:" + Guid.NewGuid().ToString("N");

        var created = false;
        if (!_agentThreadsByKey.TryGetValue(threadKey, out var thread)) {
            thread = string.IsNullOrWhiteSpace(toolCallId)
                ? FindExistingAgentThread(toolCallId, agentId, agentName, agentDisplayName)
                : null;
        }

        if (thread is null) {
            thread = new TranscriptThreadState(
                threadKey,
                TranscriptThreadKind.Agent,
                ResolveThreadDisplayName(agentDisplayName, agentName, agentId),
                ParseTimestamp(startedAt));
            _agentThreadOrder.Add(thread);
            created = true;
        }

        _agentThreadsByKey[threadKey] = thread;

        if (!string.IsNullOrWhiteSpace(toolCallId))
            _agentThreadsByToolCallId[toolCallId.Trim()] = thread;

        if (!string.IsNullOrWhiteSpace(toolCallId))
            thread.ToolCallId = toolCallId.Trim();
        if (!string.IsNullOrWhiteSpace(agentId))
            thread.AgentId = agentId.Trim();
        if (!string.IsNullOrWhiteSpace(agentName))
            thread.AgentName = agentName.Trim();
        if (!string.IsNullOrWhiteSpace(agentDisplayName))
            thread.AgentDisplayName = agentDisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(agentDescription))
            thread.AgentDescription = agentDescription.Trim();
        if (!string.IsNullOrWhiteSpace(prompt))
            thread.Prompt = prompt.Trim();

        if (!string.IsNullOrWhiteSpace(toolCallId) &&
            _agentLaunchesByToolCallId.TryGetValue(toolCallId.Trim(), out var launchInfo)) {
            ApplyBackgroundLaunchInfo(thread, launchInfo);
        }

        if (!string.IsNullOrWhiteSpace(toolCallId) ||
            !string.IsNullOrWhiteSpace(agentId) ||
            !string.IsNullOrWhiteSpace(agentName) ||
            !string.IsNullOrWhiteSpace(agentDisplayName) ||
            !string.IsNullOrWhiteSpace(agentDescription) ||
            !string.IsNullOrWhiteSpace(status) ||
            !string.IsNullOrWhiteSpace(prompt)) {
            thread.IsPlaceholderThread = false;
        }

        NormalizeThreadAgentIdentity(thread);
        AliasThreadKeys(thread, toolCallId, agentId, agentName, agentDisplayName);
        _syncTaskToolTranscriptLink(thread);

        thread.Title = ResolveThreadDisplayName(thread.AgentDisplayName, thread.AgentName, thread.AgentId);
        if (!string.IsNullOrWhiteSpace(status))
            thread.StatusText = HumanizeThreadStatus(status);

        if (created)
            _syncAgentCards();

        return thread;
    }

    internal TranscriptThreadState GetOrCreateAgentDisplayThread(AgentStatusCard agentCard) {
        var existingThread = agentCard.Threads
            .Where(thread => !thread.IsPlaceholderThread)
            .OrderByDescending(GetThreadLastActivityAt)
            .ThenByDescending(thread => thread.StartedAt)
            .FirstOrDefault();
        if (existingThread is not null)
            return existingThread;

        var placeholderKey = "placeholder-thread:" + agentCard.AccentStorageKey;
        if (_agentThreadsByKey.TryGetValue(placeholderKey, out var existingPlaceholder))
            return existingPlaceholder;

        var placeholderThread = new TranscriptThreadState(
            placeholderKey,
            TranscriptThreadKind.Agent,
            agentCard.Name,
            DateTimeOffset.Now) {
            AgentDisplayName = agentCard.Name,
            AgentDescription = agentCard.RoleText,
            AgentType = agentCard.RoleText,
            AgentCardKey = agentCard.AccentStorageKey,
            IsPlaceholderThread = true,
            StatusText = string.Empty,
            DetailText = string.Empty
        };

        _agentThreadsByKey[placeholderKey] = placeholderThread;
        _agentThreadOrder.Add(placeholderThread);
        AliasThreadKeys(placeholderThread, toolCallId: null, agentId: null, agentName: null, agentDisplayName: agentCard.Name);
        _syncAgentCardsWithThreads();
        return placeholderThread;
    }

    // ── Thread lookup ────────────────────────────────────────────────────────

    internal TranscriptThreadState? FindAgentThread(SquadBackgroundAgentInfo agent) {
        if (!string.IsNullOrWhiteSpace(agent.ToolCallId) &&
            _agentThreadsByToolCallId.TryGetValue(agent.ToolCallId.Trim(), out var byToolCallId)) {
            return byToolCallId;
        }

        if (!string.IsNullOrWhiteSpace(agent.AgentId)) {
            var agentId = agent.AgentId.Trim();
            var byAgentId = _agentThreadOrder.FirstOrDefault(thread =>
                string.Equals(thread.AgentId, agentId, StringComparison.OrdinalIgnoreCase));
            if (byAgentId is not null)
                return byAgentId;
        }

        var byAlias = FindExistingAgentThread(
            agent.ToolCallId,
            agent.AgentId,
            agent.AgentName,
            agent.AgentDisplayName);
        if (byAlias is not null)
            return byAlias;

        var label = _resolveBackgroundAgentDisplayLabel(agent);
        return _agentThreadOrder.FirstOrDefault(thread =>
            string.Equals(_buildAgentLabel(thread), label, StringComparison.OrdinalIgnoreCase));
    }

    internal TranscriptThreadState? FindExistingAgentThread(
        string? toolCallId,
        string? agentId,
        string? agentName,
        string? agentDisplayName) {

        var expectedAgentCardKey = ResolveExpectedAgentCardKey(agentId, agentName, agentDisplayName);

        if (!string.IsNullOrWhiteSpace(toolCallId)) {
            var normalizedToolCallId = toolCallId.Trim();
            if (_agentThreadsByToolCallId.TryGetValue(normalizedToolCallId, out var byToolCallId) &&
                ThreadMatchesExpectedAgent(byToolCallId, expectedAgentCardKey))
                return byToolCallId;
        }

        if (!string.IsNullOrWhiteSpace(agentId)) {
            var normalizedAgentId = agentId.Trim();
            if (_agentThreadsByKey.TryGetValue("agent:" + normalizedAgentId, out var byAgentKey) &&
                ThreadMatchesExpectedAgent(byAgentKey, expectedAgentCardKey))
                return byAgentKey;

            var byAgentId = _agentThreadOrder.FirstOrDefault(thread =>
                ThreadMatchesExpectedAgent(thread, expectedAgentCardKey) &&
                (string.Equals(thread.AgentId, normalizedAgentId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(thread.AgentName, normalizedAgentId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(thread.AgentCardKey, normalizedAgentId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(thread.AgentDisplayName, HumanizeAgentName(normalizedAgentId), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(thread.Title, HumanizeAgentName(normalizedAgentId), StringComparison.OrdinalIgnoreCase)));
            if (byAgentId is not null)
                return byAgentId;
        }

        if (AgentThreadIdentityPolicy.CanReuseByAgentName(agentName, expectedAgentCardKey)) {
            var normalizedAgentName = agentName!.Trim();
            var humanizedAgentName = HumanizeAgentName(normalizedAgentName);
            var byAgentName = _agentThreadOrder.FirstOrDefault(thread =>
                ThreadMatchesExpectedAgent(thread, expectedAgentCardKey) &&
                (string.Equals(thread.AgentName, normalizedAgentName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(thread.AgentDisplayName, humanizedAgentName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(thread.Title, humanizedAgentName, StringComparison.OrdinalIgnoreCase)));
            if (byAgentName is not null)
                return byAgentName;
        }

        if (AgentThreadIdentityPolicy.CanReuseByDisplayName(agentDisplayName, expectedAgentCardKey)) {
            var normalizedDisplayName = agentDisplayName!.Trim();
            return _agentThreadOrder.FirstOrDefault(thread =>
                ThreadMatchesExpectedAgent(thread, expectedAgentCardKey) &&
                (string.Equals(thread.AgentDisplayName, normalizedDisplayName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(thread.Title, normalizedDisplayName, StringComparison.OrdinalIgnoreCase)));
        }

        return null;
    }

    // ── Identity & aliasing ──────────────────────────────────────────────────

    private string? ResolveExpectedAgentCardKey(
        string? agentId,
        string? agentName,
        string? agentDisplayName) =>
        AgentThreadIdentityPolicy.ResolveExpectedAgentCardKey(
            agentId,
            agentName,
            agentDisplayName,
            _getKnownTeamAgentDescriptors());

    private static bool ThreadMatchesExpectedAgent(TranscriptThreadState thread, string? expectedAgentCardKey) =>
        AgentThreadIdentityPolicy.ThreadMatchesExpectedAgent(
            thread.AgentCardKey,
            thread.IsPlaceholderThread,
            expectedAgentCardKey);

    internal void AliasThreadKeys(
        TranscriptThreadState thread,
        string? toolCallId,
        string? agentId,
        string? agentName,
        string? agentDisplayName) {
        if (!string.IsNullOrWhiteSpace(toolCallId))
            _agentThreadsByKey[toolCallId.Trim()] = thread;

        foreach (var key in EnumerateThreadAliases(thread, agentId, agentName, agentDisplayName))
            _agentThreadsByKey[key] = thread;
    }

    private IEnumerable<string> EnumerateThreadAliases(
        TranscriptThreadState thread,
        string? agentId,
        string? agentName,
        string? agentDisplayName) {
        foreach (var candidate in new[] { agentId, thread.AgentId, thread.AgentCardKey }) {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            yield return "agent:" + candidate.Trim();
            yield return "agent:" + HumanizeAgentName(candidate.Trim());
        }

        foreach (var candidate in new[] { agentName, thread.AgentName }) {
            if (!AgentThreadIdentityPolicy.CanAliasByAgentName(candidate, thread.AgentCardKey))
                continue;

            var normalizedCandidate = candidate!.Trim();
            yield return "agent:" + normalizedCandidate;
            yield return "agent:" + HumanizeAgentName(normalizedCandidate);
        }

        foreach (var candidate in new[] { agentDisplayName, thread.AgentDisplayName }) {
            if (!AgentThreadIdentityPolicy.CanAliasByDisplayName(candidate, thread.AgentCardKey))
                continue;

            var normalizedCandidate = candidate!.Trim();
            yield return "agent:" + normalizedCandidate;
            yield return "agent:" + HumanizeAgentName(normalizedCandidate);
        }
    }

    internal void NormalizeThreadAgentIdentity(TranscriptThreadState thread) {
        var roster = _getKnownTeamAgentDescriptors();
        if (roster.Length == 0)
            return;

        var previousAgentCardKey = thread.AgentCardKey;
        var normalizedAgentCardKey = AgentThreadIdentityPolicy.NormalizeAgentCardKey(
            new AgentThreadIdentitySnapshot(
                thread.Title,
                thread.AgentId,
                thread.AgentName,
                thread.AgentDisplayName,
                thread.AgentCardKey,
                thread.IsPlaceholderThread),
            roster);

        if (!string.Equals(previousAgentCardKey, normalizedAgentCardKey, StringComparison.OrdinalIgnoreCase)) {
            thread.AgentCardKey = normalizedAgentCardKey;
            SquadDashTrace.Write(
                "Threads",
                $"Normalized thread identity threadId={thread.ThreadId} previousCard={previousAgentCardKey ?? "(none)"} currentCard={normalizedAgentCardKey ?? "(none)"} title={thread.Title}");
        }

        PromoteRosterDisplayIdentity(thread, roster, normalizedAgentCardKey);
    }

    internal void NormalizeInactiveThreadState(TranscriptThreadState thread) {
        if (thread.IsPlaceholderThread || _isThreadActiveForDisplay(thread) || IsTerminalBackgroundStatus(thread.StatusText))
            return;

        if (!string.IsNullOrWhiteSpace(thread.StatusText))
            thread.StatusText = string.Empty;

        _syncThreadChip(thread);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    internal void UpdateAgentThreadLifecycle(
        TranscriptThreadState thread,
        SquadSdkEvent evt,
        string statusText,
        string detailText) {
        if (!string.IsNullOrWhiteSpace(evt.AgentId))
            thread.AgentId = evt.AgentId;
        if (!string.IsNullOrWhiteSpace(evt.ParentToolCallId ?? evt.ToolCallId))
            thread.ToolCallId = evt.ParentToolCallId ?? evt.ToolCallId;
        if (!string.IsNullOrWhiteSpace(evt.AgentName))
            thread.AgentName = evt.AgentName;
        if (!string.IsNullOrWhiteSpace(evt.AgentDisplayName))
            thread.AgentDisplayName = evt.AgentDisplayName;
        if (!string.IsNullOrWhiteSpace(evt.AgentDescription))
            thread.AgentDescription = evt.AgentDescription;
        TryApplyOriginMetadataFromParentToolCall(thread, evt.ParentToolCallId);

        if (!string.IsNullOrWhiteSpace(thread.ToolCallId) &&
            _agentLaunchesByToolCallId.TryGetValue(thread.ToolCallId.Trim(), out var launchInfo)) {
            ApplyBackgroundLaunchInfo(thread, launchInfo);
        }

        thread.Title = ResolveThreadDisplayName(thread.AgentDisplayName, thread.AgentName, thread.AgentId);
        thread.StatusText = statusText;
        thread.DetailText = detailText;
        thread.IsCurrentBackgroundRun = !IsTerminalBackgroundStatus(statusText);
        if (DateTimeOffset.TryParse(evt.StartedAt, out var startedAt))
            thread.StartedAt = startedAt;
        if (DateTimeOffset.TryParse(evt.FinishedAt, out var finishedAt))
            thread.CompletedAt = finishedAt;
        else if (string.Equals(statusText, "Completed", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(statusText, "Failed", StringComparison.OrdinalIgnoreCase)) {
            thread.CompletedAt = DateTimeOffset.Now;
        }
    }

    internal void EnsureAgentThreadTurnStarted(TranscriptThreadState thread) {
        if (thread.CurrentTurn is not null)
            return;

        var prompt = thread.Prompt;
        if (string.IsNullOrWhiteSpace(prompt))
            prompt = "Background task";

        _beginTranscriptTurn(thread, prompt);
    }

    internal void FinalizeAgentThread(TranscriptThreadState thread) {
        CompleteOutstandingAgentTools(thread);
        _finalizeCurrentTurnResponse(thread);
        _collapseCurrentTurnThinking(thread);
        thread.ResponseStreamed = false;
        thread.CompletedAt ??= DateTimeOffset.Now;
        if (IsTerminalBackgroundStatus(thread.StatusText))
            thread.IsCurrentBackgroundRun = false;
        _syncThreadChip(thread);
    }

    internal void CompleteOutstandingAgentTools(TranscriptThreadState thread) {
        if (thread.CurrentTurn is null)
            return;

        var success = thread.StatusText.Trim() switch {
            "Completed" => true,
            "Failed" => false,
            "Cancelled" => false,
            _ => (bool?)null
        };
        if (!success.HasValue)
            return;

        var finishedAt = thread.CompletedAt ?? DateTimeOffset.Now;
        foreach (var entry in thread.CurrentTurn.ToolEntries.Where(entry => !entry.IsCompleted)) {
            entry.IsCompleted = true;
            entry.Success = success.Value;
            entry.FinishedAt ??= finishedAt;
            entry.DetailContent = ToolTranscriptFormatter.BuildDetailContent(new ToolTranscriptDetail(
                entry.Descriptor,
                entry.ArgsJson,
                entry.OutputText,
                entry.StartedAt,
                entry.FinishedAt,
                entry.ProgressText,
                entry.IsCompleted,
                entry.Success));
            _renderToolEntry(entry);
        }

        _updateToolSpinnerState();
        if (thread.Kind == TranscriptThreadKind.Coordinator)
            _syncActiveToolName();
    }

    internal void SyncBackgroundAgentThreads(IReadOnlyList<SquadBackgroundAgentInfo> agents) {
        foreach (var agent in agents) {
            if (SilentBackgroundAgentPolicy.ShouldSuppressThread(agent.AgentId, agent.AgentName, agent.AgentDisplayName))
                continue;

            var thread = GetOrCreateAgentThread(
                agent.ToolCallId,
                agent.AgentId,
                agent.AgentName,
                agent.AgentDisplayName,
                agent.Description,
                agent.Status,
                agent.Prompt,
                agent.StartedAt);

            thread.WasObservedAsBackgroundTask = true;
            thread.AgentType = agent.AgentType ?? thread.AgentType;
            thread.Prompt ??= agent.Prompt;
            thread.LatestIntent = agent.LatestIntent ?? thread.LatestIntent;
            thread.RecentActivity = agent.RecentActivity ?? thread.RecentActivity;
            thread.ErrorText = agent.Error ?? thread.ErrorText;
            thread.StatusText = HumanizeThreadStatus(agent.Status);
            thread.DetailText = BuildBackgroundAgentDetail(agent, thread);
            thread.IsCurrentBackgroundRun = !IsTerminalBackgroundStatus(thread.StatusText);

            if (thread.IsSelected)
                _updateTranscriptThreadBadge();

            if (DateTimeOffset.TryParse(agent.CompletedAt, out var completedAt))
                thread.CompletedAt = completedAt;
            if (DateTimeOffset.TryParse(agent.StartedAt, out var startedAt))
                thread.StartedAt = startedAt;

            if (!string.IsNullOrWhiteSpace(agent.LatestResponse)) {
                EnsureAgentThreadTurnStarted(thread);
                var currentResponse = GetSanitizedTurnResponseText(thread.CurrentTurn);
                if (string.IsNullOrWhiteSpace(currentResponse)) {
                    _appendText(thread, agent.LatestResponse!);
                    _finalizeCurrentTurnResponse(thread);
                    thread.ResponseStreamed = false;
                }

                thread.LatestResponse = SanitizeResponseTextOrNull(agent.LatestResponse);
            }

            if (string.Equals(agent.Status, "completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(agent.Status, "failed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(agent.Status, "cancelled", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(agent.Status, "killed", StringComparison.OrdinalIgnoreCase)) {
                FinalizeAgentThread(thread);
                _observeBackgroundAgentActivity(thread, "background_snapshot");
            }

            _syncThreadChip(thread);
        }
    }

    // ── Launch info ──────────────────────────────────────────────────────────

    internal void CaptureBackgroundAgentLaunchInfo(SquadSdkEvent evt) {
        if (!string.Equals(evt.ToolName, "task", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(evt.ToolCallId)) {
            return;
        }

        var launchInfo = BackgroundAgentLaunchInfoResolver.TryResolve(
            evt.ToolCallId,
            evt.Args,
            _getKnownTeamAgentDescriptors());
        if (launchInfo is null)
            return;

        _agentLaunchesByToolCallId[launchInfo.ToolCallId] = launchInfo;

        if (_agentThreadsByToolCallId.TryGetValue(launchInfo.ToolCallId, out var existingThread)) {
            ApplyBackgroundLaunchInfo(existingThread, launchInfo);
            _syncAgentCardsWithThreads();
        }
    }

    internal void ApplyBackgroundLaunchInfo(TranscriptThreadState thread, BackgroundAgentLaunchInfo launchInfo) {
        if (!string.IsNullOrWhiteSpace(launchInfo.TaskName)) {
            if (string.IsNullOrWhiteSpace(thread.AgentId) ||
                AgentThreadIdentityPolicy.IsGenericLooseIdentity(thread.AgentId)) {
                thread.AgentId = launchInfo.TaskName;
            }

            if (string.IsNullOrWhiteSpace(thread.AgentName) ||
                AgentThreadIdentityPolicy.IsGenericLooseIdentity(thread.AgentName)) {
                thread.AgentName = launchInfo.TaskName;
            }
        }

        if (!string.IsNullOrWhiteSpace(launchInfo.DisplayName))
            thread.AgentDisplayName = launchInfo.DisplayName;
        if (!string.IsNullOrWhiteSpace(launchInfo.AccentKey))
            thread.AgentCardKey = launchInfo.AccentKey;
        if (!string.IsNullOrWhiteSpace(launchInfo.Description) && string.IsNullOrWhiteSpace(thread.AgentDescription))
            thread.AgentDescription = launchInfo.Description;
        if (!string.IsNullOrWhiteSpace(launchInfo.AgentType) && string.IsNullOrWhiteSpace(thread.AgentType))
            thread.AgentType = launchInfo.AgentType;
        if (!string.IsNullOrWhiteSpace(launchInfo.Prompt) && string.IsNullOrWhiteSpace(thread.Prompt))
            thread.Prompt = launchInfo.Prompt;

        thread.Title = ResolveThreadDisplayName(thread.AgentDisplayName, thread.AgentName, thread.AgentId);

        NormalizeThreadAgentIdentity(thread);
        AliasThreadKeys(thread, thread.ToolCallId, thread.AgentId, thread.AgentName, thread.AgentDisplayName);
        _syncTaskToolTranscriptLink(thread);
    }

    internal void ApplyOriginMetadata(
        TranscriptThreadState thread,
        TranscriptThreadState originThread,
        string? parentToolCallId) {
        var originDisplayName = ResolveThreadDisplayName(
            originThread.AgentDisplayName,
            originThread.AgentName,
            originThread.AgentId);
        if (!string.IsNullOrWhiteSpace(originDisplayName))
            thread.OriginAgentDisplayName = originDisplayName;
        if (!string.IsNullOrWhiteSpace(parentToolCallId))
            thread.OriginParentToolCallId = parentToolCallId.Trim();
    }

    internal void TryApplyOriginMetadataFromParentToolCall(
        TranscriptThreadState thread,
        string? parentToolCallId) {
        if (string.IsNullOrWhiteSpace(parentToolCallId))
            return;

        var normalizedParentToolCallId = parentToolCallId.Trim();
        if (!_toolEntries.TryGetValue(normalizedParentToolCallId, out var parentToolEntry))
            return;

        ApplyOriginMetadata(thread, parentToolEntry.Turn.OwnerThread, normalizedParentToolCallId);
    }

    // ── Restore from persistence ─────────────────────────────────────────────

    // Returns the list of (thread, turns) pairs that need rendering, in start-time order.
    // Callers are responsible for rendering them sequentially to avoid O(n²) dispatcher
    // queue stacking: when all renders fire concurrently each one waits for all previous
    // renders to complete, so the k-th thread waits O(k) × render_time.
    internal IReadOnlyList<(TranscriptThreadState Thread, IReadOnlyList<TranscriptTurnRecord> Turns)>
        RestorePersistedAgentThreads(IReadOnlyList<TranscriptThreadRecord> threads) {
        var pendingRenders = new List<(TranscriptThreadState, IReadOnlyList<TranscriptTurnRecord>)>();
        foreach (var record in threads.OrderBy(thread => thread.StartedAt)) {
            if (_agentThreadsByKey.ContainsKey(record.ThreadId))
                continue;

            var thread = new TranscriptThreadState(
                record.ThreadId,
                TranscriptThreadKind.Agent,
                record.Title,
                record.StartedAt) {
                AgentId = record.AgentId,
                ToolCallId = record.ToolCallId,
                AgentName = record.AgentName,
                AgentDisplayName = record.AgentDisplayName,
                AgentDescription = record.AgentDescription,
                AgentType = record.AgentType,
                AgentCardKey = record.AgentCardKey,
                OriginAgentDisplayName = record.OriginAgentDisplayName,
                OriginParentToolCallId = record.OriginParentToolCallId,
                Prompt = record.Prompt,
                LatestResponse = SanitizeResponseTextOrNull(record.LatestResponse),
                LastCoordinatorAnnouncedResponse = SanitizeResponseTextOrNull(record.LastCoordinatorAnnouncedResponse),
                LatestIntent = record.LatestIntent,
                RecentActivity = record.RecentActivity.ToArray(),
                ErrorText = record.ErrorText,
                StatusText = record.StatusText,
                DetailText = record.DetailText,
                CompletedAt = record.CompletedAt,
                IsCurrentBackgroundRun = false
            };

            thread.SavedTurns.AddRange(record.Turns);
            if (IsTerminalBackgroundStatus(thread.StatusText)) {
                thread.LastObservedActivityAt = record.CompletedAt ?? record.StartedAt;
            }
            else {
                thread.LastObservedActivityAt = null;
                thread.StatusText = string.Empty;
                // Reset StartedAt so the card doesn't briefly show a stale elapsed time
                // (e.g. "Running (8h 12m 32s)") from the previous session. The first
                // SDK snapshot for a new run will overwrite this with the real start time
                // within a few seconds.
                thread.StartedAt = DateTimeOffset.Now;
            }

            NormalizeThreadAgentIdentity(thread);
            NormalizeInactiveThreadState(thread);
            _agentThreadsByKey[record.ThreadId] = thread;
            _agentThreadOrder.Add(thread);

            if (!string.IsNullOrWhiteSpace(record.ToolCallId))
                _agentThreadsByToolCallId[record.ToolCallId] = thread;

            _syncTaskToolTranscriptLink(thread);

            // Collect for sequential rendering by caller — do NOT fire here.
            if (record.Turns.Count > 0)
                pendingRenders.Add((thread, record.Turns));
        }
        return pendingRenders;
    }

    // ── Static helpers (used here and by BackgroundTaskPresenter) ────────────

    internal static string HumanizeAgentName(string agentName) =>
        AgentNameHumanizer.Humanize(agentName);

    internal static string ResolveThreadDisplayName(
        string? agentDisplayName,
        string? agentName,
        string? agentId) {
        if (!string.IsNullOrWhiteSpace(agentDisplayName))
            return agentDisplayName.Trim();

        if (!string.IsNullOrWhiteSpace(agentName))
            return HumanizeAgentName(agentName);

        if (!string.IsNullOrWhiteSpace(agentId))
            return HumanizeAgentName(agentId);

        return "Background Agent";
    }

    internal static string ResolveSecondaryTranscriptDisplayName(
        TranscriptThreadState thread,
        string fallbackDisplayName) {
        if (!HasRosterBackedIdentity(thread) &&
            IsGeneralPurposeAgent(thread) &&
            TryExtractFirstName(thread.OriginAgentDisplayName) is { Length: > 0 } firstName) {
            return $"{firstName}'s GPA";
        }

        return fallbackDisplayName;
    }

    private static bool IsGeneralPurposeAgent(TranscriptThreadState thread) {
        return MatchesGeneralPurpose(thread.AgentDisplayName) ||
               MatchesGeneralPurpose(thread.AgentName) ||
               MatchesGeneralPurpose(thread.Title);
    }

    private static bool MatchesGeneralPurpose(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return string.Equals(trimmed, "General Purpose Agent", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "general-purpose", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "general purpose", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "GPA", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryExtractFirstName(string? displayName) {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        var trimmed = displayName.Trim();
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : parts[0];
    }

    internal static string HumanizeThreadStatus(string? status) {
        if (string.IsNullOrWhiteSpace(status))
            return string.Empty;

        return status.Trim().ToLowerInvariant() switch {
            "running" => "Running",
            "idle" => "Waiting",
            "completed" => "Completed",
            "failed" => "Failed",
            "cancelled" => "Cancelled",
            "killed" => "Cancelled",
            _ => HumanizeAgentName(status)
        };
    }

    internal static bool IsTerminalBackgroundStatus(string? statusText) {
        if (string.IsNullOrWhiteSpace(statusText))
            return false;

        return statusText.Trim() switch {
            "Completed" => true,
            "Failed" => true,
            "Cancelled" => true,
            _ => false
        };
    }

    internal static DateTimeOffset GetThreadLastActivityAt(TranscriptThreadState thread) {
        if (thread.LastObservedActivityAt is { } lastObservedActivityAt)
            return lastObservedActivityAt;

        if (thread.CompletedAt is { } completedAt)
            return completedAt;

        return thread.StartedAt;
    }

    internal static bool HasPersistableThreadContent(TranscriptThreadState thread) {
        if (thread.IsPlaceholderThread)
            return false;

        return thread.SavedTurns.Count > 0 ||
               thread.CurrentTurn is not null ||
               !string.IsNullOrWhiteSpace(thread.Prompt) ||
               !string.IsNullOrWhiteSpace(thread.DetailText) ||
               !string.IsNullOrWhiteSpace(thread.LatestResponse);
    }

    internal static bool HasMeaningfulThreadTranscript(TranscriptThreadState thread) {
        if (thread.IsPlaceholderThread)
            return false;

        return thread.SavedTurns.Count > 0 ||
               thread.CurrentTurn is not null ||
               !string.IsNullOrWhiteSpace(thread.LatestResponse) ||
               !string.IsNullOrWhiteSpace(thread.Prompt) ||
               !string.IsNullOrWhiteSpace(thread.DetailText) ||
               !string.IsNullOrWhiteSpace(thread.AgentDescription);
    }

    internal static bool HasRosterBackedIdentity(TranscriptThreadState thread) =>
        AgentThreadIdentityPolicy.HasRosterBackedIdentity(thread.AgentCardKey);

    internal static BackgroundTaskThreadSnapshot CreateBackgroundTaskThreadSnapshot(TranscriptThreadState thread) {
        return new BackgroundTaskThreadSnapshot(
            thread.ThreadId,
            thread.Title,
            thread.ToolCallId,
            thread.AgentId,
            thread.AgentCardKey,
            thread.StatusText,
            thread.WasObservedAsBackgroundTask,
            thread.IsPlaceholderThread,
            thread.StartedAt,
            thread.LastObservedActivityAt,
            thread.CompletedAt);
    }

    internal static string BuildBackgroundAgentDetail(
        SquadBackgroundAgentInfo agent,
        TranscriptThreadState thread) {
        if (!string.IsNullOrWhiteSpace(agent.Error))
            return agent.Error.Trim();

        if (agent.RecentActivity is { Length: > 0 })
            return agent.RecentActivity[^1];

        if (!string.IsNullOrWhiteSpace(agent.LatestIntent))
            return agent.LatestIntent.Trim();

        if (!string.IsNullOrWhiteSpace(agent.LatestResponse))
            return BuildThreadPreview(agent.LatestResponse);

        if (!string.IsNullOrWhiteSpace(thread.DetailText))
            return thread.DetailText;

        return agent.Description?.Trim() ?? string.Empty;
    }

    internal static string BuildThreadCompletionDetail(TranscriptThreadState thread, SquadSdkEvent evt) {
        var response = GetSanitizedTurnResponseText(thread.CurrentTurn);
        if (!string.IsNullOrWhiteSpace(response))
            return BuildThreadPreview(response);

        if (evt.TotalToolCalls is { } toolCalls && evt.DurationMs is { } durationMs)
            return $"{toolCalls} tools in {TimeSpan.FromMilliseconds(durationMs):m\\:ss}";

        if (!string.IsNullOrWhiteSpace(thread.AgentDescription))
            return thread.AgentDescription!;

        return "Background work completed.";
    }

    // ── Private static helpers ───────────────────────────────────────────────

    private static DateTimeOffset ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.Now;

    private static void PromoteRosterDisplayIdentity(
        TranscriptThreadState thread,
        IReadOnlyList<TeamAgentDescriptor> roster,
        string? normalizedAgentCardKey) {
        if (string.IsNullOrWhiteSpace(normalizedAgentCardKey))
            return;

        var rosterCard = roster.FirstOrDefault(card =>
            string.Equals(card.AccentKey, normalizedAgentCardKey.Trim(), StringComparison.OrdinalIgnoreCase));
        if (rosterCard is null)
            return;

        if (string.IsNullOrWhiteSpace(thread.AgentDisplayName) ||
            AgentThreadIdentityPolicy.IsGenericLooseIdentity(thread.AgentDisplayName)) {
            thread.AgentDisplayName = rosterCard.DisplayName;
        }

        if (string.IsNullOrWhiteSpace(thread.AgentName) ||
            AgentThreadIdentityPolicy.IsGenericLooseIdentity(thread.AgentName)) {
            thread.AgentName = rosterCard.AccentKey;
        }
    }

    private static string GetSanitizedTurnResponseText(TranscriptTurnView? turn) =>
        ToolTranscriptFormatter.StripSystemNotifications(
            turn?.ResponseTextBuilder.ToString()).TrimEnd();

    private static string? SanitizeResponseTextOrNull(string? text) {
        var sanitized = ToolTranscriptFormatter.StripSystemNotifications(text).TrimEnd();
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private static string BuildThreadPreview(string text) {
        // Collapse whitespace
        var sanitized = ToolTranscriptFormatter.StripSystemNotifications(text).TrimEnd();
        // Remove quick-reply suffix
        if (QuickReplyOptionParser.TryExtract(sanitized, out var body, out _))
            sanitized = body;
        var collapsed = string.IsNullOrWhiteSpace(sanitized)
            ? string.Empty
            : string.Join(" ", sanitized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length <= 120)
            return collapsed;
        return collapsed[..117] + "...";
    }
}
