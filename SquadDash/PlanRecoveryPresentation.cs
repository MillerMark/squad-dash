namespace SquadDash;

internal sealed record PlanRecoveryPresentation(
    string Heading,
    string Explanation,
    PlanTaskCommitEvidence? CommitEvidence,
    string Recommendation,
    string RetryLabel,
    bool RetryIsWarning);

/// <summary>
/// Converts durable recovery provenance into calm, state-specific user language. It never infers
/// task ownership from commit timestamps, authors, or a coincidental position in branch history.
/// </summary>
internal static class PlanRecoveryPresentationBuilder
{
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

        return normalized;
    }

    internal static string? BuildCompactTestSummary(DecomposeStepVerification? verification)
    {
        if (verification is null) return null;

        var label = verification.Command?.Contains("--filter", StringComparison.OrdinalIgnoreCase) == true
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
                    ? "Amendment stopped after producing committed work."
                    : "Task stopped after producing committed work.",
                isAmendment
                    ? "SquadDash captured a host-validated amendment commit before execution stopped."
                    : "SquadDash captured a host-validated commit from this task before execution stopped.",
                evidence,
                isAmendment
                    ? "Recommended: review the amendment once; accepting it also approves the checkpoint that requested it."
                    : "Recommended: review and accept this commit before continuing.",
                "Retry Task Anyway…",
                RetryIsWarning: true);
        }

        if (hasPreservedWork)
        {
            return new PlanRecoveryPresentation(
                "Task stopped with preserved work.",
                "SquadDash preserved the task's uncommitted files and will verify that they have not changed before continuing.",
                CommitEvidence: null,
                "Recommended: continue the preserved work rather than starting the task again.",
                "Continue Preserved Work",
                RetryIsWarning: false);
        }

        return new PlanRecoveryPresentation(
            "SquadDash could not confirm whether this task finished.",
            "The application stopped before the task result was recorded. The repository has changed since the task began, but those changes may include unrelated work.",
            CommitEvidence: null,
            "Recommended: assess the current work before continuing.",
            "Retry Task Anyway…",
            RetryIsWarning: true);
    }
}
