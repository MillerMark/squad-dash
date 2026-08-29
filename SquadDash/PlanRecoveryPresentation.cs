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
    internal static PlanRecoveryPrimaryActionPresentation Build(Plan plan, string taskId)
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
                                   !string.IsNullOrWhiteSpace(evidence.BaselineCommit);

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
    internal static PlanApprovalGate? FindCombinableGate(Plan plan, string taskId)
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
                    candidate.Status is PlanTaskStatus.Complete or PlanTaskStatus.Superseded)) &&
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

    internal static string BuildUnattributedReviewLabel(string stepLabel) =>
        $"Review repository changes not attributed to {stepLabel}";

    internal static string UnattributedReviewHint(string stepLabel) =>
        $"Open commits since the last accepted step. SquadDash could not attribute them to {stepLabel}; they may include unrelated work. Reviewing does not change plan state.";

    internal static bool ShouldShowGenericReason(PlanRecoveryDecisionEvidence? assessment) =>
        assessment is null;

    internal static string FormatStepLabel(string? displayStepLabel, string? fallbackTitle)
    {
        var label = !string.IsNullOrWhiteSpace(displayStepLabel)
            ? displayStepLabel.Trim()
            : !string.IsNullOrWhiteSpace(fallbackTitle)
                ? fallbackTitle.Trim()
                : "Step";
        return int.TryParse(label, out var stepNumber)
            ? $"Step {stepNumber}"
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
        var report = task?.VerificationHistory?.LastOrDefault(candidate =>
            !string.Equals(candidate.Verdict, PlanTaskVerificationVerdict.Accepted, StringComparison.Ordinal));
        if (task?.Status != PlanTaskStatus.HumanReviewRequired &&
            report?.Verdict != PlanTaskVerificationVerdict.HumanReviewRequired)
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
                    text,
                    ClassifyAnalysisSentence(text, inconclusiveReview))));
            foreach (var missing in report.MissingOrOverstatedWork)
                if (!string.IsNullOrWhiteSpace(missing)) bullets.Add(new PlanHumanReviewAnalysisItem(
                    missing.Trim(),
                    inconclusiveReview
                        ? PlanHumanReviewAnalysisSeverity.Warning
                        : ClassifyAnalysisSentence(missing)));
            bullets.AddRange(SplitAnalysisSentences(report.TestAssessment).Select(text =>
                new PlanHumanReviewAnalysisItem(
                    text,
                    ClassifyAnalysisSentence(text, inconclusiveReview))));
        }
        if (bullets.Count == 0)
            bullets.Add(new PlanHumanReviewAnalysisItem(
                SummarizeReason(plan.InterruptionData?.Reason),
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

        return new PlanHumanReviewCardPresentation(
            "Review Required",
            question,
            distinct);
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
