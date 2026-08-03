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
    internal static PlanRecoveryPresentation Build(Plan plan, string taskId, bool hasPreservedWork)
    {
        var evidence = plan.InterruptionData?.TaskCommitEvidence;
        if (evidence is not null &&
            string.Equals(evidence.TaskId, taskId, StringComparison.Ordinal))
        {
            return new PlanRecoveryPresentation(
                "Task stopped after producing committed work.",
                "SquadDash captured a host-validated commit from this task before execution stopped.",
                evidence,
                "Recommended: review and accept this commit before continuing.",
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
            "Task ownership needs review.",
            "SquadDash did not capture definitive task-owned commit evidence before stopping. Commits after the recorded task baseline must be reviewed rather than attributed automatically.",
            CommitEvidence: null,
            "Recommended: review the completed work before deciding whether to rerun the task.",
            "Retry Task Anyway…",
            RetryIsWarning: true);
    }
}
