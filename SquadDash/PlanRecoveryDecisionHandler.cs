namespace SquadDash;

/// <summary>
/// Testable decision handler that wraps <see cref="PlanRecoveryProvenanceService"/>
/// and enforces authoritative recovery gating: callers must not advance when recovery
/// is rejected. Extracted from MainWindow to enable host-orchestration integration testing.
/// </summary>
internal sealed class PlanRecoveryDecisionHandler
{
    private readonly PlanRecoveryProvenanceService _service;

    internal PlanRecoveryDecisionHandler(PlanRecoveryProvenanceService service)
    {
        _service = service;
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

    private static RecoveryDecision BuildDecision(
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
            return new RecoveryDecision(Allowed: true, Result: result, UserMessage: message);
        }
        else
        {
            var chainSummary = BuildBlockedProvenanceSummary(result, taskId);
            var message = $"⛔ Recovery ({recoveryKind}) blocked for task '{taskId}': " +
                          $"{result.BlockReason}{chainSummary}";
            SquadDashTrace.Write(TraceCategory.General,
                $"PlanRecoveryDecisionHandler: {recoveryKind} recovery BLOCKED for task '{taskId}': {result.BlockReason}");
            return new RecoveryDecision(Allowed: false, Result: result, UserMessage: message);
        }
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
