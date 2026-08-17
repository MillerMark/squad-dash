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
/// One recovery decision model shared by the transcript, Inbox, and Plan Viewer. Evidence is
/// investigated through links on the recovery surface; an action never opens a second copy of
/// the same evidence and no recovery action launches a redundant AI assessment.
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
            if (!string.IsNullOrWhiteSpace(task?.AmendmentGateId))
            {
                return new PlanRecoveryPrimaryActionPresentation(
                    PlanRecoveryPrimaryActionKind.ApproveAmendment,
                    "Approve Amendment & Continue",
                    "Accept the host-recorded amendment commit shown on this card, approve its checkpoint, and continue. No additional AI assessment runs.",
                    "accept-step-complete");
            }

            return new PlanRecoveryPrimaryActionPresentation(
                PlanRecoveryPrimaryActionKind.AcceptStep,
                $"{stepLabel} Is Complete",
                $"Accept the host-recorded commit shown on this card, mark only {stepLabel} complete, and continue. No additional AI assessment runs.",
                "accept-step-complete");
        }

        if (PlanVerificationRecoveryPolicy.CanResume(plan, taskId))
        {
            return new PlanRecoveryPrimaryActionPresentation(
                PlanRecoveryPrimaryActionKind.ResumeVerification,
                "Resume Verification & Continue",
                "Resume verification from the saved task handoff. Implementation work will not be repeated and no repository assessment will run.",
                "resume-verification");
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
            PlanRecoveryPrimaryActionKind.ContinueWorking,
            $"Continue Working on {stepLabel}",
            $"Keep {stepLabel} incomplete and continue from the preserved repository state. No preliminary AI assessment runs.",
            "continue-working");
    }
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

    internal static bool ShouldShowGenericReason(PlanRecoveryDecisionEvidence? assessment) =>
        assessment is null;

    internal static string FormatStepLabel(string? displayStepLabel, string fallbackTitle)
    {
        var label = string.IsNullOrWhiteSpace(displayStepLabel)
            ? fallbackTitle.Trim()
            : displayStepLabel.Trim();
        return int.TryParse(label, out var stepNumber)
            ? $"Step {stepNumber}"
            : label;
    }

    internal static bool ShouldPromptForCommitReview(bool explicitStepAcceptance) =>
        !explicitStepAcceptance;

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
            "Human Review Required",
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
        var evidence = plan.InterruptionData?.TaskCommitEvidence;
        if (evidence is not null &&
            string.Equals(evidence.TaskId, taskId, StringComparison.Ordinal))
        {
            var isAmendment = plan.Tasks.Any(task =>
                string.Equals(task.TaskId, taskId, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(task.AmendmentGateId));
            return new PlanRecoveryPresentation(
                isAmendment
                    ? "Amendment Stopped After Producing Committed Work"
                    : "Task Stopped After Producing Committed Work",
                isAmendment
                    ? "SquadDash captured a host-validated amendment commit before execution stopped."
                    : "SquadDash captured a host-validated commit from this task before execution stopped.",
                evidence,
                isAmendment
                    ? "Use the commit link above to investigate. Approve the amendment if it satisfies the requested change."
                    : "Use the commit link above to investigate, then confirm whether this step is complete.",
                "Retry Task Anyway…",
                RetryIsWarning: true);
        }

        if (hasPreservedWork)
        {
            return new PlanRecoveryPresentation(
                "Task Stopped With Preserved Work",
                "SquadDash preserved the task's uncommitted files and will verify that they have not changed before continuing.",
                CommitEvidence: null,
                "Recommended: continue the preserved work rather than starting the task again.",
                "Continue Preserved Work",
                RetryIsWarning: false);
        }

        return new PlanRecoveryPresentation(
            "SquadDash Could Not Confirm Whether This Task Finished",
            "The application stopped before the task result was recorded. The repository has changed since the task began, but those changes may include unrelated work.",
            CommitEvidence: null,
            "Recommended: assess the current work before continuing.",
            "Retry Task Anyway…",
            RetryIsWarning: true);
    }
}
