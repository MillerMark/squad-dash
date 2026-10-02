namespace SquadDash;

internal sealed record PlanRecoveryPresentation(
    string Heading,
    string Explanation,
    PlanTaskCommitEvidence? CommitEvidence,
    string Recommendation,
    string RetryLabel,
    bool RetryIsWarning);

internal sealed record PlanHumanReviewCardPresentation(
    string Title,
    string? Question,
    IReadOnlyList<PlanHumanReviewAnalysisItem> AnalysisBullets,
    PlanPreviousReviewPresentation? PreviousReview = null);

internal sealed record PlanPreviousReviewPresentation(
    string EvaluatedCommit,
    string CurrentCommit,
    IReadOnlyList<PlanHumanReviewAnalysisItem> AnalysisBullets);

internal enum PlanHumanReviewAnalysisSeverity
{
    Information,
    Warning,
    Error,
}

internal sealed record PlanHumanReviewAnalysisItem(
    string Text,
    PlanHumanReviewAnalysisSeverity Severity);

internal sealed record PlanAttentionCardPresentation(
    string Category,
    string Title,
    string Explanation,
    string RecordedReason,
    bool ShowDiagnosticsActions);

internal static class PlanAttentionCardPresentationBuilder
{
    internal static PlanAttentionCardPresentation Build(
        Plan plan,
        string taskId,
        PlanHumanReviewCardPresentation? humanReview,
        bool hasHumanChecklist)
    {
        var hasHumanQuestion = !string.IsNullOrWhiteSpace(humanReview?.Question);
        if (humanReview is not null && (hasHumanChecklist || hasHumanQuestion))
        {
            return new PlanAttentionCardPresentation(
                PlanAttentionCategory.HumanVerificationRequired,
                "Human Verification Is Required",
                "Automated verification is complete. This step requires a specific observation or judgment that SquadDash cannot perform.",
                humanReview.Question?.Trim() ?? "Complete the verification checklist below.",
                ShowDiagnosticsActions: false);
        }

        var interruption = plan.InterruptionData;
        var category = PlanAttentionCategory.IsValid(interruption?.AttentionCategory)
            ? interruption!.AttentionCategory!
            : PlanAttentionCategory.UnexpectedState;
        var recordedReason = string.IsNullOrWhiteSpace(interruption?.AttentionSummary)
            ? interruption?.Reason ?? "SquadDash stopped the plan without recording a recovery explanation."
            : interruption!.AttentionSummary!.Trim();
        var currentEvidence = interruption?.TaskCommitEvidence is { } evidence &&
                              string.Equals(evidence.TaskId, taskId, StringComparison.Ordinal)
            ? evidence
            : null;
        var currentReview = PlanRecoveryPresentationBuilder.ResolveCurrentReviewReport(plan, taskId);
        var friendlyReason = PlanRecoveryPresentationBuilder.HumanizePlanText(plan, recordedReason);

        return category switch
        {
            PlanAttentionCategory.AutomaticWorkExhausted when currentEvidence is not null && currentReview is null =>
                new PlanAttentionCardPresentation(
                    category,
                    "Step Needs Verification",
                    "SquadDash preserved the latest work but could not confirm this step as complete.",
                    $"Current work is preserved in commit {currentEvidence.Commit}. " +
                    (PlanRecoveryPresentationBuilder.BuildReviewProvenanceNotice(plan, taskId) is null
                        ? "No independent review is recorded for this commit, so it still needs verification."
                        : "The available independent review applies to earlier evidence, so this commit still needs verification."),
                    ShowDiagnosticsActions: false),
            PlanAttentionCategory.AutomaticWorkExhausted => new PlanAttentionCardPresentation(
                category,
                "Automatic Work Is Exhausted",
                "SquadDash stopped automatic work after the permitted attempts to avoid repeating the same unsuccessful work.",
                friendlyReason,
                ShowDiagnosticsActions: false),
            PlanAttentionCategory.HumanVerificationRequired => new PlanAttentionCardPresentation(
                PlanAttentionCategory.UnexpectedState,
                "Unexpected State Encountered",
                "SquadDash recorded a human-verification state without a concrete question or checklist, so it stopped instead of requesting an unsupported approval.",
                friendlyReason,
                ShowDiagnosticsActions: true),
            PlanAttentionCategory.HumanInputRequired => new PlanAttentionCardPresentation(
                category,
                "Human Input Is Required",
                "Plan execution reached a decision that only you can make. Existing work and plan state are preserved.",
                friendlyReason,
                ShowDiagnosticsActions: false),
            _ => new PlanAttentionCardPresentation(
                PlanAttentionCategory.UnexpectedState,
                "Unexpected State Encountered",
                "SquadDash encountered an unexpected state and stopped the plan to preserve its current work.",
                friendlyReason,
                ShowDiagnosticsActions: true),
        };
    }

    internal static string BuildCopyText(
        PlanAttentionCardPresentation presentation,
        string planTitle,
        string stepLabel,
        string taskTitle,
        string? technicalDetails = null)
    {
        var lines = new List<string>
        {
            presentation.Title,
            $"Plan: {planTitle}",
            $"{stepLabel}: {taskTitle}",
            string.Empty,
            presentation.Explanation,
            string.Empty,
            $"What happened: {presentation.RecordedReason}",
        };
        if (!string.IsNullOrWhiteSpace(technicalDetails))
        {
            lines.Add(string.Empty);
            lines.Add("Technical details:");
            lines.Add(technicalDetails.Trim());
        }
        return string.Join(Environment.NewLine, lines);
    }
}

internal enum PlanRecoveryPrimaryActionKind
{
    AcceptStep,
    AcceptUnattributedStep,
    AcceptRejectedNoOp,
    ApproveAmendment,
    ResumeVerification,
    RetryVerification,
    ContinueWorking,
}

internal sealed record PlanRecoveryPrimaryActionPresentation(
    PlanRecoveryPrimaryActionKind Kind,
    string Label,
    string Hint,
    string ProtocolAction);

internal enum PlanRecoverySurfaceActionKind
{
    AcceptStep,
    ResumeVerification,
    ContinueWorking,
    ReviseRemainingPlan,
    EndPlan,
}

internal sealed record PlanRecoverySurfaceActionPresentation(
    PlanRecoverySurfaceActionKind Kind,
    string Label,
    string Hint,
    string ProtocolAction,
    QuickReplyTone Tone = QuickReplyTone.Default,
    PlanRecoveryPrimaryActionKind? PrimaryKind = null);

internal sealed record PlanRecoveryActionSetPresentation(
    string DecisionPrompt,
    IReadOnlyList<PlanRecoverySurfaceActionPresentation> Actions)
{
    internal PlanRecoverySurfaceActionPresentation? Primary => Actions.FirstOrDefault(action =>
        action.Kind is PlanRecoverySurfaceActionKind.AcceptStep or
            PlanRecoverySurfaceActionKind.ResumeVerification or
            PlanRecoverySurfaceActionKind.ContinueWorking);
}

/// <summary>
/// The canonical action list for every interrupted-plan surface. Transcript, Inbox, and Plan
/// Viewer render these definitions; none of them independently decides whether to offer retry,
/// acceptance, replanning, or destructive termination.
/// </summary>
internal static class PlanRecoveryActionSetBuilder
{
    internal static PlanRecoveryActionSetPresentation BuildConservativeFallback(string stepLabel)
    {
        // A legacy/developer-preview message has no durable interruption category, so it
        // cannot safely offer completion, verification, or termination. It still obtains its
        // controls from this same canonical builder rather than inventing surface-local labels.
        return new PlanRecoveryActionSetPresentation(
            "Continue from the preserved work or revise the remaining plan.",
            [
                new PlanRecoverySurfaceActionPresentation(
                    PlanRecoverySurfaceActionKind.ContinueWorking,
                    $"Continue Working on {stepLabel}",
                    $"Continue {stepLabel} from the preserved work without redoing completed work.",
                    "continue-working",
                    PrimaryKind: PlanRecoveryPrimaryActionKind.ContinueWorking),
                new PlanRecoverySurfaceActionPresentation(
                    PlanRecoverySurfaceActionKind.ReviseRemainingPlan,
                    "✎ Revise Remaining Plan…",
                    "Replace the blocked task with smaller approved steps.",
                    "replan-failed-task"),
            ]);
    }

    internal static PlanRecoveryActionSetPresentation Build(
        Plan plan,
        string taskId,
        string? currentHead = null,
        Func<string, string, bool>? isAncestor = null,
        bool includeEndPlan = true,
        PlanTaskCorrectionActivityState correctionActivity = PlanTaskCorrectionActivityState.None)
    {
        var task = plan.Tasks.FirstOrDefault(candidate =>
            string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal));
        var index = plan.Tasks.ToList().FindIndex(candidate =>
            string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal));
        var stepLabel = PlanRecoveryPresentationBuilder.FormatStepLabel(
            task?.DisplayStepLabel ?? (index >= 0 ? (index + 1).ToString() : null),
            task?.Title ?? task?.Description ?? taskId);
        var actions = new List<PlanRecoverySurfaceActionPresentation>();

        if (correctionActivity != PlanTaskCorrectionActivityState.None)
        {
            return new PlanRecoveryActionSetPresentation(
                correctionActivity == PlanTaskCorrectionActivityState.Running
                    ? $"SquadDash is continuing the preserved repair for {stepLabel}."
                    : $"A correction for {stepLabel} is ready to run.",
                actions);
        }

        var primary = PlanRecoveryActionPresentationBuilder.Build(
            plan, taskId, currentHead, isAncestor);
        var hasAcceptedReview = PlanRecoveryPresentationBuilder.HasCurrentAcceptedReview(plan, taskId);
        var hasCurrentEvidence = plan.InterruptionData?.TaskCommitEvidence is { } evidence &&
                                 string.Equals(evidence.TaskId, taskId, StringComparison.Ordinal);
        var hasDurableCorrection = plan.InterruptionData?.RecoveryCorrection is not null;
        var category = plan.InterruptionData?.AttentionCategory;

        if (hasDurableCorrection)
        {
            actions.Add(new PlanRecoverySurfaceActionPresentation(
                PlanRecoverySurfaceActionKind.ContinueWorking,
                "Continue Existing Repair",
                $"Resume the correction already preserved for {stepLabel}; inspect and retain its existing edits before doing any additional work.",
                "request-corrections",
                PrimaryKind: PlanRecoveryPrimaryActionKind.ContinueWorking));
        }
        else if (primary.Kind is
                     PlanRecoveryPrimaryActionKind.AcceptStep or
                     PlanRecoveryPrimaryActionKind.AcceptUnattributedStep or
                     PlanRecoveryPrimaryActionKind.AcceptRejectedNoOp or
                     PlanRecoveryPrimaryActionKind.ApproveAmendment)
        {
            actions.Add(new PlanRecoverySurfaceActionPresentation(
                PlanRecoverySurfaceActionKind.AcceptStep,
                primary.Label,
                primary.Hint,
                primary.ProtocolAction,
                PrimaryKind: primary.Kind));
            actions.Add(new PlanRecoverySurfaceActionPresentation(
                PlanRecoverySurfaceActionKind.ContinueWorking,
                primary.Kind == PlanRecoveryPrimaryActionKind.AcceptStep
                    ? $"Continue Working on {stepLabel}…"
                    : $"Continue Working on {stepLabel}",
                $"Keep {stepLabel} incomplete and continue from its preserved repository state.",
                primary.Kind == PlanRecoveryPrimaryActionKind.AcceptStep
                    ? "request-corrections"
                    : "continue-working",
                PrimaryKind: PlanRecoveryPrimaryActionKind.ContinueWorking));
        }
        else if (primary.Kind is PlanRecoveryPrimaryActionKind.ResumeVerification or
                     PlanRecoveryPrimaryActionKind.RetryVerification)
        {
            actions.Add(new PlanRecoverySurfaceActionPresentation(
                PlanRecoverySurfaceActionKind.ResumeVerification,
                primary.Label,
                primary.Hint,
                primary.ProtocolAction,
                PrimaryKind: primary.Kind));
        }
        else
        {
            actions.Add(new PlanRecoverySurfaceActionPresentation(
                PlanRecoverySurfaceActionKind.ContinueWorking,
                $"Continue Working on {stepLabel}",
                $"Start one bounded continuation from the preserved repository state for {stepLabel}.",
                "continue-working",
                PrimaryKind: PlanRecoveryPrimaryActionKind.ContinueWorking));
        }

        actions.Add(new PlanRecoverySurfaceActionPresentation(
            PlanRecoverySurfaceActionKind.ReviseRemainingPlan,
            "✎ Revise Remaining Plan…",
            "Replace the blocked task with smaller, dependency-aware steps.",
            "replan-failed-task"));
        if (includeEndPlan && PlanRecoveryControlPolicy.HasActionableInterruption(plan))
        {
            actions.Add(new PlanRecoverySurfaceActionPresentation(
                PlanRecoverySurfaceActionKind.EndPlan,
                "End Plan",
                "Stop this incomplete plan without discarding its history or repository work.",
                "end-plan",
                QuickReplyTone.Destructive));
        }

        var prompt = hasDurableCorrection
            ? $"A preserved repair for {stepLabel} was interrupted. Continue that repair without repeating completed work."
            : category == PlanAttentionCategory.AutomaticWorkExhausted && hasCurrentEvidence && !hasAcceptedReview
                ? $"The preserved work for {stepLabel} still needs current verification."
                : "Choose how to continue from the preserved plan state.";
        return new PlanRecoveryActionSetPresentation(prompt, actions);
    }
}

internal static class PlanRecoveryLifecycleControlPolicy
{
    /// <summary>
    /// A recovery action set owns the user's next decision. Showing the ordinary pause/resume
    /// toolbar beside it creates two controls that mutate different lifecycle layers.
    /// </summary>
    internal static bool ShouldShowOrdinaryControls(
        Plan? plan,
        PlanRecoveryActionSetPresentation? recoveryActions) =>
        plan is null ||
        !PlanRecoveryControlPolicy.HasActionableInterruption(plan) ||
        recoveryActions is null ||
        recoveryActions.Actions.Count == 0;
}

/// <summary>
/// Distinguishes an ordinary amendment checkpoint, which the user can explicitly approve while
/// accepting its reviewed commit, from a human-proof checkpoint whose observations must remain
/// visible and unchecked until the reviewer actually performs them.
/// </summary>
internal static class PlanAmendmentGateAcceptancePolicy
{
    internal const string CombinedApprovalNote =
        "Approved while accepting the reviewed amendment commit.";

    internal static bool CanCombineWithTaskAcceptance(Plan plan, PlanTask task)
    {
        if (string.IsNullOrWhiteSpace(task.AmendmentGateId)) return false;
        var gate = plan.ApprovalGates.FirstOrDefault(candidate =>
            string.Equals(candidate.GateId, task.AmendmentGateId, StringComparison.Ordinal));
        return gate is not null &&
               gate.Status is PlanGateStatus.Pending or PlanGateStatus.AwaitingApproval &&
               gate.ProofRequirements is not { Count: > 0 };
    }

    internal static bool IsLegacyImplicitHumanProofApproval(Plan plan, PlanApprovalGate gate) =>
        gate.Status == PlanGateStatus.Approved &&
        gate.ProofRequirements is { Count: > 0 } &&
        string.Equals(gate.ResolutionNote, CombinedApprovalNote, StringComparison.Ordinal) &&
        plan.Tasks.Any(task =>
            task.Status == PlanTaskStatus.Complete &&
            string.Equals(task.AmendmentGateId, gate.GateId, StringComparison.Ordinal));
}

/// <summary>
/// One recovery decision model shared by the transcript, Inbox, and Plan Viewer. Evidence is
/// investigated through links on the recovery surface; an action never opens a second copy of
/// the same evidence. The separate, explicitly requested assessment action is advisory only.
/// </summary>
internal static class PlanRecoveryActionPresentationBuilder
{
    internal static PlanRecoveryPrimaryActionPresentation Build(
        Plan plan,
        string taskId,
        string? currentHead = null,
        Func<string, string, bool>? isAncestor = null)
    {
        var task = plan.Tasks.FirstOrDefault(candidate =>
            string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal));
        var taskIndex = plan.Tasks.ToList().FindIndex(candidate =>
            string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal));
        var stepLabel = PlanRecoveryPresentationBuilder.FormatStepLabel(
            task?.DisplayStepLabel ?? (taskIndex >= 0 ? (taskIndex + 1).ToString() : null),
            task?.Title ?? task?.Description ?? taskId);
        var evidence = plan.InterruptionData?.TaskCommitEvidence;
        var hasExactTaskEvidence = evidence is not null &&
                                    string.Equals(evidence.TaskId, taskId, StringComparison.Ordinal) &&
                                    !string.IsNullOrWhiteSpace(evidence.Commit) &&
                                    !string.IsNullOrWhiteSpace(evidence.BaselineCommit) &&
                                    !string.IsNullOrWhiteSpace(currentHead) &&
                                    string.Equals(evidence.Commit, currentHead, StringComparison.OrdinalIgnoreCase);
        var hasAdvancedSinceEvidence = evidence is not null &&
                                       string.Equals(evidence.TaskId, taskId, StringComparison.Ordinal) &&
                                       !string.IsNullOrWhiteSpace(evidence.Commit) &&
                                       !string.IsNullOrWhiteSpace(currentHead) &&
                                       !string.Equals(evidence.Commit, currentHead, StringComparison.OrdinalIgnoreCase) &&
                                       isAncestor is not null &&
                                       isAncestor(evidence.Commit, currentHead);

        if (hasExactTaskEvidence)
        {
            if (task is not null &&
                PlanAmendmentGateAcceptancePolicy.CanCombineWithTaskAcceptance(plan, task))
            {
                return new PlanRecoveryPrimaryActionPresentation(
                    PlanRecoveryPrimaryActionKind.ApproveAmendment,
                    plan.IsPaused ? "Approve Amendment" : "Approve Amendment & Continue",
                    plan.IsPaused
                        ? "Accept the host-recorded amendment commit and approve its checkpoint while keeping the plan paused."
                        : "Accept the host-recorded amendment commit shown on this card, approve its checkpoint, and continue. No additional AI assessment runs.",
                    "accept-step-complete");
            }

            return new PlanRecoveryPrimaryActionPresentation(
                PlanRecoveryPrimaryActionKind.AcceptStep,
                plan.IsPaused ? $"{stepLabel} Is Complete" : $"{stepLabel} Is Complete & Continue",
                plan.IsPaused
                    ? $"Accept the host-recorded commit, mark only {stepLabel} complete, and keep the plan paused."
                    : $"Accept the host-recorded commit shown on this card, mark only {stepLabel} complete, and continue. No additional AI assessment runs.",
                "accept-step-complete");
        }

        if (hasAdvancedSinceEvidence)
        {
            return new PlanRecoveryPrimaryActionPresentation(
                PlanRecoveryPrimaryActionKind.AcceptUnattributedStep,
                plan.IsPaused ? $"{stepLabel} Is Complete" : $"{stepLabel} Is Complete & Continue",
                plan.IsPaused
                    ? $"After reviewing the repository changes, explicitly mark {stepLabel} complete while keeping the plan paused. Commit attribution remains marked as uncertain. Newer commits are on this branch since the recorded task result. Refresh the card before accepting."
                    : $"After reviewing the repository changes, explicitly mark {stepLabel} complete and continue. Commit attribution remains marked as uncertain. Newer commits are on this branch since the recorded task result. Refresh the card before accepting.",
                "accept-step-complete");
        }

        if (PlanVerificationRecoveryPolicy.CanResume(plan, taskId))
        {
            return new PlanRecoveryPrimaryActionPresentation(
                PlanRecoveryPrimaryActionKind.ResumeVerification,
                $"Resume Verification for {stepLabel}",
                $"Send the preserved candidate for {stepLabel} directly to independent verification. Implementation work will not be repeated, replaced, or reassessed first.",
                "resume-verification");
        }

        if (RejectedVerifiedNoOpRecoveryPolicy.CanAccept(plan, taskId))
        {
            return new PlanRecoveryPrimaryActionPresentation(
                PlanRecoveryPrimaryActionKind.AcceptRejectedNoOp,
                plan.IsPaused ? $"Accept {stepLabel} as Complete" : $"Accept {stepLabel} as Complete & Continue",
                $"Accept the reported no-op for {stepLabel} after SquadDash confirms the current snapshot, branch ancestry, and worktree state. Intervening commits remain unattributed to this step.",
                "accept-step-complete");
        }

        if (PlanTaskExecutionPolicy.IsVerificationOnly(task))
        {
            return new PlanRecoveryPrimaryActionPresentation(
                PlanRecoveryPrimaryActionKind.RetryVerification,
                "Retry Verification",
                "Run this non-mutating verification step again against the current branch. No implementation task or preliminary repository assessment runs.",
                "continue-working");
        }

        return new PlanRecoveryPrimaryActionPresentation(
            PlanRecoveryPrimaryActionKind.AcceptUnattributedStep,
            plan.IsPaused ? $"{stepLabel} Is Complete" : $"{stepLabel} Is Complete & Continue",
            plan.IsPaused
                ? $"After reviewing the repository changes, explicitly mark {stepLabel} complete while keeping the plan paused. Commit attribution remains marked as uncertain."
                : $"After reviewing the repository changes, explicitly mark {stepLabel} complete and continue. Commit attribution remains marked as uncertain.",
            "accept-step-complete");
    }
}

/// <summary>
/// Offers a bounded human override only when SquadDash explicitly rejected a verified-no-op
/// result. The click path still revalidates the current repository snapshot before acceptance.
/// Ordinary failed or inconclusive verification continues to offer retry/replan only.
/// </summary>
internal static class RejectedVerifiedNoOpRecoveryPolicy
{
    internal static bool CanAccept(Plan plan, string taskId) =>
        plan.LifecycleStatus == PlanLifecycleStatus.Interrupted &&
        string.Equals(plan.InterruptionData?.InterruptedTaskId, taskId, StringComparison.Ordinal) &&
        plan.InterruptionData?.Reason.Contains("verified-no-op", StringComparison.OrdinalIgnoreCase) == true;
}

internal sealed record PlanRecoveryAssessmentCardPresentation(
    string Finding,
    string Recommendation);

internal static class PlanRecoveryAssessmentCardPresentationBuilder
{
    internal static PlanRecoveryAssessmentCardPresentation Build(
        PlanRecoveryDecisionEvidence assessment,
        string stepLabel)
    {
        var classification = assessment.Classification?.Trim().ToLowerInvariant();
        return classification switch
        {
            PlanRecoveryClassification.Complete when string.Equals(
                assessment.CompletionKind,
                DecomposeStepCompletionKind.VerifiedNoOp,
                StringComparison.Ordinal) => new(
                $"AI found that {stepLabel} was already satisfied at the inspected snapshot. {assessment.Summary}",
                $"Recommendation: review the no-op evidence, then accept {stepLabel} as already satisfied if it matches your intent."),
            PlanRecoveryClassification.Complete => new(
                $"AI found that {stepLabel} appears complete. {assessment.Summary}",
                $"Recommendation: review the evidence, then mark {stepLabel} complete if it matches your intent."),
            PlanRecoveryClassification.Partial => new(
                $"AI found partial work for {stepLabel}. {assessment.Summary}",
                $"Recommendation: continue working on {stepLabel} using the remaining-work findings below."),
            PlanRecoveryClassification.NotStarted => new(
                $"AI did not find work satisfying {stepLabel}. {assessment.Summary}",
                $"Recommendation: continue working on {stepLabel}."),
            _ => new(
                $"AI could not confidently determine whether {stepLabel} is complete. {assessment.Summary}",
                $"Recommendation: review the repository evidence before deciding whether to complete or continue {stepLabel}."),
        };
    }
}

/// <summary>
/// Separates accepting committed recovery evidence from preparing future execution. A human hold
/// may accept the reviewed step in any worktree state because Resume owns the later safety check.
/// </summary>
internal static class PlanRecoveryAcceptancePolicy
{
    internal static bool RequiresCleanWorktreeBeforeAcceptance(Plan plan) => !plan.IsPaused;

    internal static bool ShouldAdvanceAfterAcceptance(Plan plan) => !plan.IsPaused;
}

/// <summary>
/// Identifies the narrow case where an inconclusive automated verification can be resolved by
/// the task's already-approved human-only proof checkpoint. Explicit rework findings never enter
/// this path, and ambiguous/multi-gate boundaries remain separate.
/// </summary>
internal static class PlanHumanReviewGatePolicy
{
    internal static PlanApprovalGate? FindCombinableGate(
        Plan plan, string taskId, bool allowPendingReviewSiblings = false)
    {
        var task = plan.Tasks.FirstOrDefault(candidate =>
            string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal));
        if (task?.Status != PlanTaskStatus.HumanReviewRequired)
            return null;

        var candidates = plan.ApprovalGates.Where(gate =>
            gate.Status is PlanGateStatus.Pending or PlanGateStatus.AwaitingApproval &&
            gate.AfterTaskIds.Contains(taskId, StringComparer.Ordinal) &&
            gate.AfterTaskIds.All(afterTaskId =>
                string.Equals(afterTaskId, taskId, StringComparison.Ordinal) ||
                plan.Tasks.Any(candidate =>
                    string.Equals(candidate.TaskId, afterTaskId, StringComparison.Ordinal) &&
                    (candidate.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded ||
                     allowPendingReviewSiblings && candidate.Status == PlanTaskStatus.HumanReviewRequired))) &&
            gate.BeforeTaskIds.All(beforeTaskId => plan.Tasks.Any(candidate =>
                string.Equals(candidate.TaskId, beforeTaskId, StringComparison.Ordinal) &&
                candidate.Status == PlanTaskStatus.Pending)) &&
            gate.ProofRequirements is { Count: > 0 } &&
            gate.ProofRequirements.All(requirement =>
                PlanProofCapabilityPolicy.IsHumanOnly(requirement.ProofType))).ToArray();

        return candidates.Length == 1 ? candidates[0] : null;
    }
}

/// <summary>
/// Converts durable recovery provenance into calm, state-specific user language. It never infers
/// task ownership from commit timestamps, authors, or a coincidental position in branch history.
/// </summary>
internal static class PlanRecoveryPresentationBuilder
{
    internal const string AssessmentStoppedMessage = "Assessment finished — plan still stopped.";

    internal static string BuildCommittedReviewLabel(
        string stepLabel,
        string? baselineCommit,
        string? terminalCommit) =>
        $"View committed changes ({BaselineLabel(baselineCommit)} → {TerminalLabel(terminalCommit)})";

    internal static string CommittedReviewHint(
        string stepLabel,
        string? baselineCommit,
        string? terminalCommit) =>
        $"Open the local change viewer for {stepLabel}, comparing {BaselineLabel(baselineCommit)} → {TerminalLabel(terminalCommit)}. Reviewing does not change the plan.";

    internal static string BuildUnattributedReviewLabel(string stepLabel, string? baselineCommit = null) =>
        string.IsNullOrWhiteSpace(baselineCommit)
            ? $"View workspace changes for {stepLabel}"
            : $"View workspace changes ({ShortSha(baselineCommit)} → workspace)";

    internal static string UnattributedReviewHint(string stepLabel, string? baselineCommit = null) =>
        string.IsNullOrWhiteSpace(baselineCommit)
            ? $"Open workspace changes associated with {stepLabel}. Exact task attribution is unavailable; reviewing does not change plan state."
            : $"Open workspace changes compared with recorded baseline {ShortSha(baselineCommit)}. SquadDash could not attribute every change to {stepLabel}; reviewing does not change plan state.";

    private static string ShortSha(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Length > 7 ? value[..7] : value;

    private static string BaselineLabel(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "baseline unavailable" : ShortSha(value);

    private static string TerminalLabel(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "current commit unavailable" : ShortSha(value);

    internal static bool ShouldShowGenericReason(PlanRecoveryDecisionEvidence? assessment) =>
        assessment is null;

    internal static string FormatStepLabel(string? displayStepLabel, string? fallbackTitle)
    {
        var label = !string.IsNullOrWhiteSpace(displayStepLabel)
            ? displayStepLabel.Trim()
            : !string.IsNullOrWhiteSpace(fallbackTitle)
                ? fallbackTitle.Trim()
                : "Step";
        return char.IsDigit(label[0])
            ? $"Step {label}"
            : label;
    }

    internal static IReadOnlyList<PlanEvidenceCommit> ResolveTaskEvidence(Plan plan, PlanTask task)
    {
        if (task.Commits is { Count: > 0 }) return task.Commits;
        if (!string.IsNullOrWhiteSpace(task.Commit))
            return [new PlanEvidenceCommit(
                task.Commit, PlanRecoveryCommitRelation.Task, "Accepted terminal commit for this step.")];

        var interruption = plan.InterruptionData;
        if (string.Equals(interruption?.InterruptedTaskId, task.TaskId, StringComparison.Ordinal) &&
            interruption?.RecoveryAssessment?.Commits is { Count: > 0 } recoveryCommits)
            return recoveryCommits;

        return [];
    }

    internal static string BuildStatusMessage(bool hasCommittedWork) =>
        hasCommittedWork
            ? "Plan execution stopped unexpectedly after producing committed work. Recovery is available."
            : "Plan execution stopped unexpectedly. Recovery is available.";

    internal static string SummarizeReason(string? reason)
    {
        var normalized = string.IsNullOrWhiteSpace(reason)
            ? "SquadDash could not determine why execution stopped."
            : reason.Trim();

        if (normalized.Contains("Missing verification result", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Missing scrutiny result", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("could not produce a trustworthy structured verdict", StringComparison.OrdinalIgnoreCase))
        {
            return "Independent verification did not return the required structured result after two attempts. " +
                   "Test adequacy could not be independently classified.";
        }

        if (normalized.Contains("launched more than one generic primary worker", StringComparison.OrdinalIgnoreCase))
        {
            return "An earlier SquadDash build stopped after observing an additional helper worker. " +
                   "The completed primary worker's commit was preserved, and additional helpers are now advisory rather than fatal.";
        }

        if (normalized.Contains("evaluatedCommit and noOpEvidence are only valid", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("noOpEvidence is only valid for a verified-no-op", StringComparison.OrdinalIgnoreCase))
        {
            return "The task returned both changed-code details and details meant for a no-changes-needed result. " +
                   "SquadDash preserved the source changes but could not safely record the step as complete.";
        }

        return FormatProtocolIdentifiers(normalized);
    }

    internal static string FormatProtocolIdentifiers(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            text,
            @"(?<![`A-Za-z0-9_])([A-Z][A-Z0-9]*(?:_[A-Z0-9]+){2,})(?![`A-Za-z0-9_])",
            "`$1`",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    internal static string? BuildCompactTestSummary(DecomposeStepVerification? verification)
    {
        if (verification is null) return null;

        var label = verification.Command?.Contains("build", StringComparison.OrdinalIgnoreCase) == true
            ? "Build"
            : verification.Command?.Contains("--filter", StringComparison.OrdinalIgnoreCase) == true
                ? "Focused tests"
                : "Tests";
        var summary = verification.Summary?.Trim().TrimEnd('.');
        if (string.Equals(verification.Status, "passed", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(summary)
                ? $"{label} passed."
                : $"{label}: {summary}.";

        return string.IsNullOrWhiteSpace(summary)
            ? $"{label}: {verification.Status}."
            : $"{label}: {summary}.";
    }

    internal static PlanHumanReviewCardPresentation? BuildHumanReviewCard(
        Plan plan,
        string taskId)
    {
        var task = plan.Tasks.FirstOrDefault(candidate =>
            string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal));
        var report = ResolveCurrentReviewReport(plan, taskId);
        var previousReport = report is null ? ResolveHistoricalReviewReport(plan, taskId) : null;
        if (task?.Status != PlanTaskStatus.HumanReviewRequired &&
            report?.Verdict != PlanTaskVerificationVerdict.HumanReviewRequired &&
            previousReport is null)
            return null;

        var gate = plan.ApprovalGates
            .Where(candidate => candidate.Status is PlanGateStatus.Pending or PlanGateStatus.AwaitingApproval &&
                                candidate.AfterTaskIds.Contains(taskId, StringComparer.Ordinal))
            .OrderByDescending(candidate => candidate.ProofRequirements?.Count ?? 0)
            .FirstOrDefault();
        var question = gate is null ? null : PlanProofCapabilityPolicy.ResolveHumanQuestion(gate);
        var bullets = new List<PlanHumanReviewAnalysisItem>();
        if (report is not null)
        {
            var inconclusiveReview = string.Equals(
                report.Verdict,
                PlanTaskVerificationVerdict.HumanReviewRequired,
                StringComparison.Ordinal);
            bullets.AddRange(SplitAnalysisSentences(report.Summary).Select(text =>
                new PlanHumanReviewAnalysisItem(
                    HumanizePlanText(plan, text),
                    ClassifyAnalysisSentence(text, inconclusiveReview))));
            foreach (var missing in report.MissingOrOverstatedWork)
                if (!string.IsNullOrWhiteSpace(missing)) bullets.Add(new PlanHumanReviewAnalysisItem(
                    HumanizePlanText(plan, missing.Trim()),
                    inconclusiveReview
                        ? PlanHumanReviewAnalysisSeverity.Warning
                        : ClassifyAnalysisSentence(missing)));
            bullets.AddRange(SplitAnalysisSentences(report.TestAssessment).Select(text =>
                new PlanHumanReviewAnalysisItem(
                    HumanizePlanText(plan, text),
                    ClassifyAnalysisSentence(text, inconclusiveReview))));
        }
        if (report is null && previousReport is null)
            bullets.Add(new PlanHumanReviewAnalysisItem(
                HumanizePlanText(plan, SummarizeReason(plan.InterruptionData?.Reason)),
                PlanHumanReviewAnalysisSeverity.Warning));

        var distinct = new List<PlanHumanReviewAnalysisItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bullet in bullets)
        {
            var normalized = string.Join(' ', bullet.Text.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).TrimEnd('.', '!', '?');
            if (normalized.Length == 0 || !seen.Add(normalized)) continue;
            distinct.Add(bullet with { Text = bullet.Text.Trim() });
        }

        PlanPreviousReviewPresentation? previousReview = null;
        var currentCommit = ResolveDisplayedCommit(plan, taskId);
        if (previousReport is not null && !string.IsNullOrWhiteSpace(currentCommit))
        {
            var previousItems = BuildReviewAnalysisItems(plan, previousReport);
            previousReview = new PlanPreviousReviewPresentation(
                previousReport.EvaluatedCommit,
                currentCommit,
                previousItems);
        }

        return new PlanHumanReviewCardPresentation(
            "Review Required",
            question,
            distinct,
            previousReview);
    }

    internal static string? BuildReviewProvenanceNotice(Plan plan, string taskId)
    {
        var commit = ResolveDisplayedCommit(plan, taskId);
        if (string.IsNullOrWhiteSpace(commit)) return null;
        if (ResolveCurrentReviewReport(plan, taskId) is not null) return null;
        var report = ResolveHistoricalReviewReport(plan, taskId);
        if (report is null) return null;
        var reportCommit = report.EvaluatedCommit;
        var source = string.IsNullOrWhiteSpace(reportCommit) ? "an unknown earlier commit" : $"commit {reportCommit}";
        return $"A previous review reflects {source}; current evidence is commit {commit}. " +
               "The earlier conclusions are kept separate because they do not verify the current commit.";
    }

    private static string? ResolveDisplayedCommit(Plan plan, string taskId) =>
        plan.InterruptionData?.TaskCommitEvidence is { } evidence &&
        string.Equals(evidence.TaskId, taskId, StringComparison.Ordinal)
            ? evidence.Commit
            : plan.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, taskId, StringComparison.Ordinal))?.Handoff?.Commit;

    internal static PlanTaskVerificationReport? ResolveCurrentReviewReport(Plan plan, string taskId)
    {
        var history = plan.Tasks.FirstOrDefault(task =>
            string.Equals(task.TaskId, taskId, StringComparison.Ordinal))?.VerificationHistory;
        var commit = ResolveDisplayedCommit(plan, taskId);
        if (string.IsNullOrWhiteSpace(commit))
            return history?.LastOrDefault();
        return history?.LastOrDefault(report =>
            string.Equals(report.EvaluatedCommit, commit, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool HasCurrentAcceptedReview(Plan plan, string taskId) =>
        string.Equals(
            ResolveCurrentReviewReport(plan, taskId)?.Verdict,
            PlanTaskVerificationVerdict.Accepted,
            StringComparison.Ordinal);

    private static PlanTaskVerificationReport? ResolveHistoricalReviewReport(Plan plan, string taskId)
    {
        var currentCommit = ResolveDisplayedCommit(plan, taskId);
        return plan.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, taskId, StringComparison.Ordinal))?
            .VerificationHistory?
            .LastOrDefault(report =>
                !string.Equals(report.EvaluatedCommit, currentCommit, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<PlanHumanReviewAnalysisItem> BuildReviewAnalysisItems(
        Plan plan,
        PlanTaskVerificationReport report)
    {
        var inconclusive = string.Equals(
            report.Verdict,
            PlanTaskVerificationVerdict.HumanReviewRequired,
            StringComparison.Ordinal);
        var items = new List<PlanHumanReviewAnalysisItem>();
        items.AddRange(SplitAnalysisSentences(report.Summary).Select(text =>
            new PlanHumanReviewAnalysisItem(
                HumanizePlanText(plan, text),
                ClassifyAnalysisSentence(text, inconclusive))));
        items.AddRange(report.MissingOrOverstatedWork
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => new PlanHumanReviewAnalysisItem(
                HumanizePlanText(plan, text.Trim()),
                PlanHumanReviewAnalysisSeverity.Warning)));
        items.AddRange(SplitAnalysisSentences(report.TestAssessment).Select(text =>
            new PlanHumanReviewAnalysisItem(
                HumanizePlanText(plan, text),
                ClassifyAnalysisSentence(text, inconclusive))));
        return items;
    }

    internal static string HumanizePlanText(Plan plan, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;

        var result = text;
        foreach (var task in plan.Tasks.OrderByDescending(candidate => candidate.TaskId.Length))
        {
            var stepLabel = FormatStepLabel(task.DisplayStepLabel, task.Title ?? task.Description);
            result = result.Replace($"Task {task.TaskId}", stepLabel, StringComparison.Ordinal);
            result = result.Replace($"task {task.TaskId}", stepLabel, StringComparison.Ordinal);
            result = ReplaceReference(result, task.TaskId, stepLabel);

            var rawLabel = task.DisplayStepLabel?.Trim();
            if (string.IsNullOrWhiteSpace(rawLabel) ||
                !char.IsDigit(rawLabel[0]) ||
                rawLabel.All(char.IsDigit))
                continue;

            var position = 0;
            while ((position = FindReference(result, rawLabel, position)) >= 0)
            {
                var alreadyPrefixed = position >= 5 &&
                    string.Equals(result[(position - 5)..position], "Step ", StringComparison.OrdinalIgnoreCase);
                if (alreadyPrefixed)
                {
                    position += rawLabel.Length;
                    continue;
                }

                result = result[..position] + "Step " + result[position..];
                position += 5 + rawLabel.Length;
            }
        }

        return result;

        static string ReplaceReference(string value, string reference, string replacement)
        {
            var position = 0;
            while ((position = FindReference(value, reference, position)) >= 0)
            {
                value = value[..position] + replacement + value[(position + reference.Length)..];
                position += replacement.Length;
            }
            return value;
        }

        static int FindReference(string value, string reference, int startIndex)
        {
            var position = startIndex;
            while ((position = value.IndexOf(reference, position, StringComparison.Ordinal)) >= 0)
            {
                var beforeIsIdentifier = position > 0 && char.IsLetterOrDigit(value[position - 1]);
                var after = position + reference.Length;
                var afterIsIdentifier = after < value.Length && char.IsLetterOrDigit(value[after]);
                if (!beforeIsIdentifier && !afterIsIdentifier)
                    return position;
                position += reference.Length;
            }
            return -1;
        }
    }

    private static PlanHumanReviewAnalysisSeverity ClassifyAnalysisSentence(
        string text,
        bool inconclusiveReview = false)
    {
        if (text.Contains("passes", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("passed", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("supported", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("succeeded", StringComparison.OrdinalIgnoreCase))
            return PlanHumanReviewAnalysisSeverity.Information;
        if (text.Contains("human observation", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("human review", StringComparison.OrdinalIgnoreCase) ||
            inconclusiveReview)
            return PlanHumanReviewAnalysisSeverity.Warning;
        return PlanHumanReviewAnalysisSeverity.Error;
    }

    internal static IReadOnlyList<string> SplitAnalysisSentences(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var normalized = string.Join(' ', text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return System.Text.RegularExpressions.Regex.Split(
                normalized,
                @"(?<=[.!?])\s+(?=[`*_]*[A-Z0-9])",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
            .Select(sentence => sentence.Trim())
            .ToArray();
    }

    internal static PlanRecoveryPresentation Build(Plan plan, string taskId, bool hasPreservedWork)
    {
        var task = plan.Tasks.FirstOrDefault(candidate =>
            string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal));
        var taskIndex = plan.Tasks.ToList().FindIndex(candidate =>
            string.Equals(candidate.TaskId, taskId, StringComparison.Ordinal));
        var stepLabel = FormatStepLabel(
            task?.DisplayStepLabel ?? (taskIndex >= 0 ? (taskIndex + 1).ToString() : null),
            task?.Title ?? task?.Description ?? taskId);
        var evidence = plan.InterruptionData?.TaskCommitEvidence;
        if (evidence is not null &&
            string.Equals(evidence.TaskId, taskId, StringComparison.Ordinal))
        {
            var amendmentTask = plan.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, taskId, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(task.AmendmentGateId));
            var isAmendment = amendmentTask is not null;
            var combinesAmendmentGate = amendmentTask is not null &&
                PlanAmendmentGateAcceptancePolicy.CanCombineWithTaskAcceptance(plan, amendmentTask);
            return new PlanRecoveryPresentation(
                isAmendment
                    ? "Amendment Stopped After Producing Committed Work"
                    : "Task Stopped After Producing Committed Work",
                isAmendment
                    ? "SquadDash captured a host-validated amendment commit before execution stopped."
                    : "SquadDash captured a host-validated commit from this task before execution stopped.",
                evidence,
                combinesAmendmentGate
                    ? "Use the commit link above to investigate. Approve the amendment if it satisfies the requested change."
                    : isAmendment
                        ? "Review the commit if needed, then accept the amendment task; its human-proof checkpoint will open separately."
                    : "Review the commit if needed, then accept the step, continue working on it, or revise the remaining plan.",
                "Retry Task Anyway…",
                RetryIsWarning: true);
        }

        if (hasPreservedWork)
        {
            return new PlanRecoveryPresentation(
                "Task Stopped With Preserved Work",
                "SquadDash preserved the task's uncommitted files and will verify that they have not changed before continuing.",
                CommitEvidence: null,
                "Recommended: resume from the uncommitted files so the task agent can finish and commit them rather than starting over.",
                "Resume With Uncommitted Files…",
                RetryIsWarning: true);
        }

        return new PlanRecoveryPresentation(
            "SquadDash Could Not Confirm Whether This Task Finished",
            "The application stopped before the task result was recorded. The repository has changed since the task began, but those changes may include unrelated work.",
            CommitEvidence: null,
            $"Recommended: review the current work, then decide whether to mark {stepLabel} complete or continue working on it.",
            "Retry Task Anyway…",
            RetryIsWarning: true);
    }
}
