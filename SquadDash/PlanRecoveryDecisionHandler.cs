using System;
using System.Collections.Generic;
using System.Linq;

namespace SquadDash;

/// <summary>
/// Abstraction for the prompt queue, enabling tests to verify that blocked decisions
/// do not enqueue prompts without depending on the concrete <see cref="PromptQueue"/>.
/// </summary>
internal interface IPromptEnqueuer
{
    void Enqueue(string text, string? sourceTag = null);
}

/// <summary>
/// Testable decision handler that wraps <see cref="PlanRecoveryProvenanceService"/>
/// and enforces authoritative recovery gating: callers must not advance when recovery
/// is rejected. Extracted from MainWindow to enable host-orchestration integration testing.
/// </summary>
internal sealed class PlanRecoveryDecisionHandler
{
    private readonly PlanRecoveryProvenanceService _service;
    private readonly InboxStore? _inboxStore;
    private readonly IPromptEnqueuer? _promptEnqueuer;

    internal PlanRecoveryDecisionHandler(PlanRecoveryProvenanceService service)
        : this(service, inboxStore: null, promptEnqueuer: null) { }

    internal PlanRecoveryDecisionHandler(
        PlanRecoveryProvenanceService service,
        InboxStore? inboxStore,
        IPromptEnqueuer? promptEnqueuer = null)
    {
        _service = service;
        _inboxStore = inboxStore;
        _promptEnqueuer = promptEnqueuer;
    }

    /// <summary>
    /// Result of a recovery decision, including whether advancement is permitted
    /// and a user-visible message summarizing the outcome.
    /// </summary>
    internal sealed record RecoveryDecision(
        bool Allowed,
        PlanRecoveryProvenanceService.RecoveryResult Result,
        string UserMessage);

    /// <summary>
    /// Attempts a fresh-attempt recovery for the given task. Returns a decision
    /// indicating whether the caller may proceed with creating a new execution attempt.
    /// </summary>
    internal RecoveryDecision HandleFreshAttemptDecision(
        string planId,
        string taskId,
        string? previousAttemptCommit)
    {
        var result = _service.ApplyFreshAttemptRecovery(planId, taskId, previousAttemptCommit);
        return BuildDecision(result, "fresh-attempt", taskId);
    }

    /// <summary>
    /// Attempts an envelope-repair recovery for the given task. Returns a decision
    /// indicating whether the caller may proceed with sending a repair prompt.
    /// </summary>
    internal RecoveryDecision HandleRepairDecision(
        string planId,
        string taskId,
        string? previousAttemptCommit)
    {
        var result = _service.ApplyEnvelopeRepair(planId, taskId, previousAttemptCommit);
        return BuildDecision(result, "envelope-repair", taskId);
    }

    private RecoveryDecision BuildDecision(
        PlanRecoveryProvenanceService.RecoveryResult result,
        string recoveryKind,
        string taskId)
    {
        if (result.Applied)
        {
            var provenanceSummary = BuildAppliedProvenanceSummary(result, taskId);
            var message = $"⚙ Recovery ({recoveryKind}) applied for task '{taskId}'. {provenanceSummary}";
            SquadDashTrace.Write(TraceCategory.General,
                $"PlanRecoveryDecisionHandler: {recoveryKind} recovery approved for task '{taskId}'.");
            PublishInboxMessage(taskId, recoveryKind, applied: true, provenanceSummary, blockReason: null, result);
            return new RecoveryDecision(Allowed: true, Result: result, UserMessage: message);
        }
        else
        {
            var chainSummary = BuildBlockedProvenanceSummary(result, taskId);
            var message = $"⛔ Recovery ({recoveryKind}) blocked for task '{taskId}': " +
                          $"{result.BlockReason}{chainSummary}";
            SquadDashTrace.Write(TraceCategory.General,
                $"PlanRecoveryDecisionHandler: {recoveryKind} recovery BLOCKED for task '{taskId}': {result.BlockReason}");
            PublishInboxMessage(taskId, recoveryKind, applied: false, chainSummary, result.BlockReason, result);
            return new RecoveryDecision(Allowed: false, Result: result, UserMessage: message);
        }
    }

    private void PublishInboxMessage(
        string taskId,
        string recoveryKind,
        bool applied,
        string provenanceSummary,
        string? blockReason,
        PlanRecoveryProvenanceService.RecoveryResult result)
    {
        if (_inboxStore is null)
            return;

        try
        {
            var subject = applied
                ? $"Recovery applied: {taskId}"
                : $"Recovery blocked: {taskId}";

            var body = applied
                ? BuildAppliedInboxBody(taskId, recoveryKind, provenanceSummary, result)
                : BuildBlockedInboxBody(taskId, recoveryKind, blockReason, provenanceSummary, result);

            var inboxMessage = new InboxMessage
            {
                Id = $"recovery-{recoveryKind}-{taskId}-{DateTimeOffset.UtcNow.Ticks}",
                Subject = subject,
                From = "SquadDash Recovery",
                Timestamp = DateTimeOffset.UtcNow,
                Body = body,
                Priority = applied ? "low" : "high",
            };

            _inboxStore.Save(inboxMessage);
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write(TraceCategory.General,
                $"PlanRecoveryDecisionHandler: failed to publish inbox message: {ex.Message}");
        }
    }

    private static string BuildAppliedInboxBody(
        string taskId,
        string recoveryKind,
        string provenanceSummary,
        PlanRecoveryProvenanceService.RecoveryResult result)
    {
        var lines = new List<string>
        {
            $"Recovery ({recoveryKind}) was applied for task '{taskId}'.",
            "",
        };

        if (result.Plan is not null)
        {
            var task = result.Plan.Tasks.FirstOrDefault(t =>
                string.Equals(t.TaskId, taskId, StringComparison.Ordinal));
            if (task is not null)
            {
                var content = ProofProvenancePresenter.BuildForTask(task);
                if (content is not null)
                {
                    lines.Add($"**Source:** {content.SourceLabel}");
                    if (content.CommitShortSha is not null)
                        lines.Add($"**Commit:** {content.CommitShortSha}");
                    lines.Add($"**Evidence kind:** {content.SourceKind}");
                }

                if (task.ProvenanceChain is { Entries.Count: > 0 } chain)
                {
                    lines.Add("");
                    lines.Add($"**Chain summary:** {chain.BuildSummary()}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(provenanceSummary))
        {
            lines.Add("");
            lines.Add(provenanceSummary);
        }

        return string.Join("\n", lines);
    }

    private static string BuildBlockedInboxBody(
        string taskId,
        string recoveryKind,
        string? blockReason,
        string provenanceSummary,
        PlanRecoveryProvenanceService.RecoveryResult result)
    {
        var lines = new List<string>
        {
            $"Recovery ({recoveryKind}) was **blocked** for task '{taskId}'.",
            "",
            $"**Reason:** {blockReason ?? "Unknown"}",
        };

        if (result.Plan is not null)
        {
            var task = result.Plan.Tasks.FirstOrDefault(t =>
                string.Equals(t.TaskId, taskId, StringComparison.Ordinal));
            if (task?.ProvenanceChain is { Entries.Count: > 0 } chain)
            {
                lines.Add("");
                lines.Add($"**Chain summary:** {chain.BuildSummary()}");
            }
        }

        if (!string.IsNullOrWhiteSpace(provenanceSummary))
        {
            lines.Add("");
            lines.Add(provenanceSummary);
        }

        return string.Join("\n", lines);
    }

    private static string BuildAppliedProvenanceSummary(
        PlanRecoveryProvenanceService.RecoveryResult result,
        string taskId)
    {
        if (result.Plan is null) return string.Empty;

        var task = result.Plan.Tasks.FirstOrDefault(t =>
            string.Equals(t.TaskId, taskId, StringComparison.Ordinal));
        if (task is null) return string.Empty;

        var provenanceContent = ProofProvenancePresenter.BuildForTask(task);
        if (provenanceContent is not null)
            return $"Evidence: {provenanceContent.SourceLabel}" +
                   (provenanceContent.CommitShortSha is not null ? $" ({provenanceContent.CommitShortSha})" : "") +
                   ".";

        if (task.ProvenanceChain is { Entries.Count: > 0 })
            return $"Provenance: {task.ProvenanceChain.BuildSummary()}";

        return string.Empty;
    }

    private static string BuildBlockedProvenanceSummary(
        PlanRecoveryProvenanceService.RecoveryResult result,
        string taskId)
    {
        if (result.Plan is null) return string.Empty;

        var task = result.Plan.Tasks.FirstOrDefault(t =>
            string.Equals(t.TaskId, taskId, StringComparison.Ordinal));
        if (task?.ProvenanceChain is { Entries.Count: > 0 } chain)
            return $" Prior attempts: {chain.BuildSummary()}";

        return string.Empty;
    }
}
