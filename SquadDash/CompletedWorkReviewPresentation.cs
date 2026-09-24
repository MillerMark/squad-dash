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

        var testSummary = BuildVerificationSummary(evidence.Verification);

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
        if (!string.IsNullOrWhiteSpace(testSummary))
            evidenceParts.Add(testSummary);

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
            _ => "The step was not recorded as complete.",
        };
        var stopReason = plan.InterruptionData?.Reason ?? "Plan execution stopped unexpectedly.";
        var technicalDetailLines = new List<string>();
        if (!AreEquivalentNarratives(stopReason, evidence.Summary))
            technicalDetailLines.Add($"Stop reason: {stopReason}");
        technicalDetailLines.AddRange([
            $"Task ID: {taskId}",
            $"Execution attempt: {evidence.ExecutionAttemptId ?? "unknown"}",
            $"Comparison baseline: {evidence.BaselineCommit}",
            $"Recorded task commit: {evidence.Commit}",
            $"Reported verification command: {evidence.Verification?.Command ?? "not provided"}",
        ]);
        var technicalDetails = string.Join(Environment.NewLine, technicalDetailLines);

        return new CompletedWorkReviewPresentation(
            StopReason: stopReason,
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

    internal static string BuildCopyText(
        CompletedWorkReviewPresentation review,
        bool includeHeading = true) =>
        string.Join(Environment.NewLine, new[]
        {
            includeHeading ? review.Heading : null,
            review.Outcome,
            review.EvidenceSummary,
            review.Uncertainty,
            review.AcceptanceEffect,
            "Technical details:",
            review.TechnicalDetails,
        }.Where(line => !string.IsNullOrWhiteSpace(line)));

    internal static string BuildCompactCopyText(CompletedWorkReviewPresentation review)
    {
        var lines = new List<string>();
        if (review.Commit is not null)
        {
            lines.Add(string.IsNullOrWhiteSpace(review.TestSummary)
                ? $"Commit {review.Commit.ShortSha}"
                : $"Commit {review.Commit.ShortSha} · {review.TestSummary}");
        }
        if (review.ChangedFiles.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"Changed files ({review.ChangedFiles.Count})");
            lines.AddRange(review.ChangedFiles.Take(8).Select(path => $"• {GetChangedFileDisplayName(path)}"));
            if (review.ChangedFiles.Count > 8)
                lines.Add($"… and {review.ChangedFiles.Count - 8} more");
        }
        return string.Join(Environment.NewLine, lines);
    }

    internal static string GetChangedFileDisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        var normalized = path.Trim().Replace('\\', '/').TrimEnd('/');
        var separator = normalized.LastIndexOf('/');
        return separator >= 0 && separator < normalized.Length - 1
            ? normalized[(separator + 1)..]
            : normalized;
    }

    internal static string? BuildVerificationSummary(DecomposeStepVerification? verification)
    {
        if (verification is null) return null;
        if (string.Equals(verification.Status, "passed", StringComparison.OrdinalIgnoreCase) &&
            verification.Tests is { } tests)
        {
            return tests.Skipped > 0
                ? $"Tests passed: {tests.Passed} · Skipped: {tests.Skipped}"
                : $"Tests passed: {tests.Passed}";
        }

        return string.Equals(verification.Status, "passed", StringComparison.OrdinalIgnoreCase)
            ? "Verification passed"
            : string.IsNullOrWhiteSpace(verification.Status)
                ? null
                : $"Verification status: {verification.Status.Trim()}";
    }

    private static bool AreEquivalentNarratives(string left, string right)
    {
        static string Normalize(string value) => string.Join(
            ' ',
            CommitReferenceText.Shorten(value).Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
    }

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
        return "Verify this step is complete to continue the plan.";
    }
}
