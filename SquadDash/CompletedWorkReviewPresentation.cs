namespace SquadDash;

/// <summary>
/// Immutable detail record for a commit in the completed-work review.
/// </summary>
internal sealed record CompletedWorkCommitDetail(
    string Sha,
    string ShortSha,
    string Summary,
    string? VerificationStatus,
    string? VerificationCommand,
    string? VerificationSummary);

/// <summary>
/// Immutable model describing a completed-work review for a single interrupted plan task.
/// Built from durable plan state and assessment data — never infers ownership from timing.
/// </summary>
internal sealed record CompletedWorkReviewPresentation(
    string StopReason,
    string TaskTitle,
    string TaskId,
    CompletedWorkCommitDetail? Commit,
    IReadOnlyList<string> ChangedFiles,
    string? TestSummary,
    IReadOnlyList<string> DownstreamTasks,
    string AcceptanceEffect,
    string? RetryRiskWarning,
    string StepLabel,
    string Heading,
    string Outcome,
    string EvidenceSummary,
    string Uncertainty,
    string ComparisonLabel,
    string ComparisonHint,
    string TechnicalDetails);

/// <summary>
/// Pure builder that converts durable plan + interruption provenance into a
/// <see cref="CompletedWorkReviewPresentation"/> for display in the transcript,
/// Inbox viewer, and Plan Viewer.
/// </summary>
internal static class CompletedWorkReviewPresentationBuilder
{
    internal static CompletedWorkReviewPresentation? Build(Plan plan, string taskId)
    {
        var evidence = plan.InterruptionData?.TaskCommitEvidence;
        if (evidence is null ||
            !string.Equals(evidence.TaskId, taskId, StringComparison.Ordinal))
            return null;

        var task = plan.Tasks.FirstOrDefault(t =>
            string.Equals(t.TaskId, taskId, StringComparison.Ordinal));
        var taskTitle = task?.Title ?? task?.Description ?? taskId;

        var commitDetail = new CompletedWorkCommitDetail(
            evidence.Commit,
            evidence.Commit.Length > 7 ? evidence.Commit[..7] : evidence.Commit,
            evidence.Summary,
            evidence.Verification?.Status,
            evidence.Verification?.Command,
            evidence.Verification?.Summary);

        var changedFiles = evidence.ChangedFiles ?? plan.InterruptionData?.AffectedPaths ?? [];

        string? testSummary = null;
        if (evidence.Verification is { } verification)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(verification.Status))
                parts.Add(string.Equals(verification.Status, "passed", StringComparison.OrdinalIgnoreCase)
                    ? "Tests passed"
                    : $"Tests {verification.Status}");
            if (!string.IsNullOrWhiteSpace(verification.Summary))
                parts.Add(verification.Summary);
            testSummary = parts.Count > 0 ? string.Join(" — ", parts) : null;
        }

        var downstream = FindDownstreamTasks(plan, taskId);

        var acceptanceEffect = BuildAcceptanceEffect(taskTitle, downstream);

        string? retryRiskWarning = null;
        if (!string.IsNullOrWhiteSpace(evidence.Commit))
        {
            retryRiskWarning = "Retrying or resuming this task may repeat work that is already committed. " +
                               "Review the commit evidence before choosing to retry.";
        }

        var stepLabel = PlanRecoveryPresentationBuilder.FormatStepLabel(
            task?.DisplayStepLabel,
            taskTitle);
        var shortCommit = ShortSha(evidence.Commit);
        var evidenceParts = new List<string> { $"Commit {shortCommit}" };
        if (changedFiles.Count > 0)
            evidenceParts.Add($"{changedFiles.Count} changed {(changedFiles.Count == 1 ? "file" : "files")}");
        if (evidence.Verification is { } reportedVerification)
        {
            var reportedStatus = string.Equals(
                reportedVerification.Status, "passed", StringComparison.OrdinalIgnoreCase)
                ? "worker-reported checks passed"
                : $"worker-reported checks: {reportedVerification.Status}";
            evidenceParts.Add(string.IsNullOrWhiteSpace(reportedVerification.Summary)
                ? reportedStatus
                : $"{reportedStatus} — {reportedVerification.Summary.Trim().TrimEnd('.')}");
        }

        var matchingReview = task?.VerificationHistory?.LastOrDefault(report =>
            string.Equals(report.EvaluatedCommit, evidence.Commit, StringComparison.OrdinalIgnoreCase));
        var uncertainty = matchingReview?.Verdict switch
        {
            PlanTaskVerificationVerdict.Accepted =>
                "Independent review accepted this commit, but the plan did not record the final step transition.",
            PlanTaskVerificationVerdict.HumanReviewRequired =>
                "Automated review could not decide; the remaining question requires human judgment.",
            PlanTaskVerificationVerdict.ReworkRequired =>
                "Independent review found work that still needs correction.",
            _ => "Independent completion review did not finish, so the step remains unconfirmed.",
        };
        var technicalDetails = string.Join(Environment.NewLine, new[]
        {
            $"Stop reason: {plan.InterruptionData?.Reason ?? "Plan execution stopped unexpectedly."}",
            $"Task ID: {taskId}",
            $"Execution attempt: {evidence.ExecutionAttemptId ?? "unknown"}",
            $"Comparison baseline: {evidence.BaselineCommit}",
            $"Recorded task commit: {evidence.Commit}",
            $"Reported verification command: {evidence.Verification?.Command ?? "not provided"}",
        });

        return new CompletedWorkReviewPresentation(
            StopReason: plan.InterruptionData?.Reason ?? "Plan execution stopped unexpectedly.",
            TaskTitle: taskTitle,
            TaskId: taskId,
            Commit: commitDetail,
            ChangedFiles: changedFiles,
            TestSummary: testSummary,
            DownstreamTasks: downstream,
            AcceptanceEffect: acceptanceEffect,
            RetryRiskWarning: retryRiskWarning,
            StepLabel: stepLabel,
            Heading: $"{stepLabel} has committed work, but completion is unconfirmed",
            Outcome: evidence.Summary,
            EvidenceSummary: string.Join(" · ", evidenceParts),
            Uncertainty: uncertainty,
            ComparisonLabel: PlanRecoveryPresentationBuilder.BuildCommittedReviewLabel(
                stepLabel,
                evidence.BaselineCommit,
                evidence.Commit),
            ComparisonHint: PlanRecoveryPresentationBuilder.CommittedReviewHint(
                stepLabel,
                evidence.BaselineCommit,
                evidence.Commit),
            TechnicalDetails: technicalDetails);
    }

    internal static string BuildCopyText(CompletedWorkReviewPresentation review) =>
        string.Join(Environment.NewLine, new[]
        {
            review.Heading,
            review.Outcome,
            review.EvidenceSummary,
            review.Uncertainty,
            review.AcceptanceEffect,
            "Technical details:",
            review.TechnicalDetails,
        });

    private static string ShortSha(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Length > 7 ? value[..7] : value;

    private static IReadOnlyList<string> FindDownstreamTasks(Plan plan, string taskId)
    {
        var dependents = new List<string>();
        foreach (var task in plan.Tasks)
        {
            if (task.DependsOn.Any(dep =>
                    string.Equals(dep, taskId, StringComparison.Ordinal)) &&
                task.Status is PlanTaskStatus.Pending or PlanTaskStatus.Executing)
            {
                dependents.Add(task.Title ?? task.Description);
            }
        }
        return dependents;
    }

    private static string BuildAcceptanceEffect(string taskTitle, IReadOnlyList<string> downstream)
    {
        var effect = $"Accepting marks \"{taskTitle}\" as complete based on the committed evidence.";
        if (downstream.Count > 0)
        {
            var names = string.Join(", ", downstream.Select(d => $"\"{d}\""));
            effect += $" This unblocks {downstream.Count} downstream " +
                      (downstream.Count == 1 ? "task" : "tasks") +
                      $": {names}.";
        }
        else
        {
            effect += " The plan will continue to the next eligible task.";
        }
        return effect;
    }
}
