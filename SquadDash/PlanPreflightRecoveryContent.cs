using System.Linq;

namespace SquadDash;

/// <summary>Pure presentation text for the contextual plan-preflight recovery card.</summary>
internal sealed record PlanPreflightRecoveryContent(
    string Title,
    string Summary,
    string ChangedFilesSummary,
    string RecoveryGuidance,
    string TechnicalDetails,
    string? ClipboardTechnicalDetails = null)
{
    internal string ClipboardText =>
        $"{Title}\n\n{Summary}\n\n{RecoveryGuidance}\n\n{ClipboardTechnicalDetails ?? TechnicalDetails}";

    private const int MaxVisibleChangedPaths = 3;

    private static string FormatChangedPaths(IReadOnlyList<string> changedPaths, bool capForDisplay)
    {
        if (changedPaths.Count == 0)
            return "No changed paths were reported.";

        var visibleCount = capForDisplay
            ? Math.Min(MaxVisibleChangedPaths, changedPaths.Count)
            : changedPaths.Count;
        var lines = changedPaths
            .Take(visibleCount)
            .Select(path => $"• {path}")
            .ToList();
        var remaining = changedPaths.Count - visibleCount;
        if (remaining > 0)
            lines.Add($"+ {remaining} more {(remaining == 1 ? "file" : "files")}");
        return string.Join("\n", lines);
    }

    internal static PlanPreflightRecoveryContent From(PlanPreflightBlockedException exception)
    {
        if (exception.RequiresRepositoryInitialization)
        {
            var repositoryTarget = string.IsNullOrWhiteSpace(exception.TargetBranch)
                ? "the selected plan action"
                : $"branch '{exception.TargetBranch}'";
            return new PlanPreflightRecoveryContent(
                "Git repository required",
                "This workspace does not have an active Git branch, so SquadDash paused before starting the plan. " +
                "No plan work was started.",
                string.Empty,
                "Select Initialize repository and start plan. SquadDash will initialize Git, create the initial " +
                "commit, and then continue the interrupted plan action automatically.",
                $"Condition: {exception.Condition}\nTarget: {repositoryTarget}");
        }

        var target = string.IsNullOrWhiteSpace(exception.TargetBranch)
            ? "the requested plan"
            : $"branch '{exception.TargetBranch}'";
        var count = exception.ChangedPaths.Count;
        var files = FormatChangedPaths(exception.ChangedPaths, capForDisplay: true);
        var allFiles = FormatChangedPaths(exception.ChangedPaths, capForDisplay: false);
        var details =
            $"Condition: {exception.Condition}\n" +
            $"Target: {target}\n" +
            $"Changed files: {count}\n\n{files}";
        var clipboardDetails =
            $"Condition: {exception.Condition}\n" +
            $"Target: {target}\n" +
            $"Changed files: {count}\n\n{allFiles}";

        return new PlanPreflightRecoveryContent(
            "Plan not started",
            $"SquadDash could not prepare {target} because {count} uncommitted " +
            $"{(count == 1 ? "file prevents" : "files prevent")} a safe branch switch. " +
            "No plan work was started.",
            files,
            "Review these changes, then commit or stash them and select Retry. " +
            "SquadDash will not discard or carry uncommitted work automatically.",
            details,
            clipboardDetails);
    }

    internal static PlanPreflightRecoveryContent FromRework(
        PlanPreflightBlockedException exception,
        string planTitle,
        string taskTitle)
    {
        var count = exception.ChangedPaths.Count;
        var files = FormatChangedPaths(exception.ChangedPaths, capForDisplay: true);
        var allFiles = FormatChangedPaths(exception.ChangedPaths, capForDisplay: false);
        var details =
            $"Condition: {exception.Condition}\n" +
            $"Plan: {planTitle}\n" +
            $"Reopened task: {taskTitle}\n" +
            $"Changed files: {count}\n\n{files}";
        var clipboardDetails =
            $"Condition: {exception.Condition}\n" +
            $"Plan: {planTitle}\n" +
            $"Reopened task: {taskTitle}\n" +
            $"Changed files: {count}\n\n{allFiles}";

        return new PlanPreflightRecoveryContent(
            "Rework ready — execution paused",
            $"SquadDash reopened “{taskTitle}”, but no new task work started because {count} uncommitted " +
            $"{(count == 1 ? "file prevents" : "files prevent")} safe plan execution.",
            files,
            "Review these changes, then commit or stash them and select Resume Rework. " +
            "The change request and reopened task are already preserved; resuming will not submit them again.",
            details,
            clipboardDetails);
    }

    internal static PlanPreflightRecoveryContent FromAmendment(
        PlanPreflightBlockedException exception,
        string planTitle,
        string amendmentTitle)
    {
        var count = exception.ChangedPaths.Count;
        var files = FormatChangedPaths(exception.ChangedPaths, capForDisplay: true);
        var allFiles = FormatChangedPaths(exception.ChangedPaths, capForDisplay: false);
        var details =
            $"Condition: {exception.Condition}\n" +
            $"Plan: {planTitle}\n" +
            $"Amendment: {amendmentTitle}\n" +
            $"Changed files: {count}\n\n{files}";
        var clipboardDetails =
            $"Condition: {exception.Condition}\n" +
            $"Plan: {planTitle}\n" +
            $"Amendment: {amendmentTitle}\n" +
            $"Changed files: {count}\n\n{allFiles}";

        return new PlanPreflightRecoveryContent(
            "Amendment ready — execution paused",
            $"SquadDash added “{amendmentTitle}” without reopening completed tasks, but no amendment " +
            $"work started because {count} uncommitted {(count == 1 ? "file prevents" : "files prevent")} safe plan execution.",
            files,
            "Review these changes, then commit or stash them and select Resume Amendment. " +
            "The completed tasks and amendment instructions are already preserved.",
            details,
            clipboardDetails);
    }

    internal static PlanPreflightRecoveryContent FromPreservedWork(
        PlanPreflightBlockedException exception,
        string taskId)
    {
        var count = exception.ChangedPaths.Count;
        var files = FormatChangedPaths(exception.ChangedPaths, capForDisplay: true);
        var allFiles = FormatChangedPaths(exception.ChangedPaths, capForDisplay: false);
        var details =
            $"Condition: {exception.Condition}\n" +
            $"Task: {taskId}\n" +
            $"Changed files: {count}\n\n{files}";
        var clipboardDetails =
            $"Condition: {exception.Condition}\n" +
            $"Task: {taskId}\n" +
            $"Changed files: {count}\n\n{allFiles}";

        return new PlanPreflightRecoveryContent(
            "Waiting for uncommitted changes",
            $"SquadDash preserved the recovery assessment for {taskId}, but {count} uncommitted " +
            $"{(count == 1 ? "file must" : "files must")} be resolved before the remaining work can resume. " +
            "The plan remains stopped.",
            files,
            "Commit the preserved work. SquadDash is watching the repository and will resume the remaining work " +
            "automatically after the workspace is clean and HEAD advances. Select Continue Preserved Work only if " +
            "you intentionally want to resume without creating that commit.",
            details,
            clipboardDetails);
    }
}

internal static class PlanRecoveryCommitReadiness
{
    internal static bool RequiresCommit(int preservedPathCount, bool allowUncommittedPreservedWork) =>
        preservedPathCount > 0 && !allowUncommittedPreservedWork;

    internal static bool IsReady(bool workspaceClean, string? headBeforeWait, string? currentHead) =>
        workspaceClean &&
        !string.IsNullOrWhiteSpace(headBeforeWait) &&
        !string.IsNullOrWhiteSpace(currentHead) &&
        !string.Equals(headBeforeWait.Trim(), currentHead.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Classifies interruptions that can resume without assessing uncertain task work.</summary>
internal static class PlanRecoveryResumePolicy
{
    internal const string ReworkPreflightReasonPrefix =
        "Rework was prepared, but no new task work started because workspace preflight was blocked.";
    internal const string AmendmentPreflightReasonPrefix =
        "An approval amendment was prepared, but no amendment work started because workspace preflight was blocked.";
    internal const string AcceptedWorkContinuationReasonPrefix =
        "Completed work was accepted and no new task work has started.";

    internal static string BuildReworkPreflightReason(string details) =>
        $"{ReworkPreflightReasonPrefix} {details}".Trim();

    internal static string BuildAmendmentPreflightReason(string details) =>
        $"{AmendmentPreflightReasonPrefix} {details}".Trim();

    internal static string BuildAcceptedWorkContinuationReason(string details) =>
        $"{AcceptedWorkContinuationReasonPrefix} {details}".Trim();

    internal static bool HasPendingRework(Plan plan) => plan.Tasks.Any(task =>
        task.Status == PlanTaskStatus.Pending &&
        task.AttemptHistory?.LastOrDefault()?.Disposition is { } disposition &&
        string.Equals(disposition, "changes-requested", StringComparison.OrdinalIgnoreCase));

    internal static bool HasPendingAmendment(Plan plan) => plan.Tasks.Any(task =>
        task.Status == PlanTaskStatus.Pending && !string.IsNullOrWhiteSpace(task.AmendmentGateId));

    internal static bool IsReworkPreflightPause(Plan plan) =>
        plan.LifecycleStatus == PlanLifecycleStatus.Interrupted &&
        plan.InterruptionData?.Reason.StartsWith(
            ReworkPreflightReasonPrefix,
            StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsAmendmentPreflightPause(Plan plan) =>
        plan.LifecycleStatus == PlanLifecycleStatus.Interrupted &&
        plan.InterruptionData?.Reason.StartsWith(
            AmendmentPreflightReasonPrefix,
            StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsAcceptedWorkContinuation(Plan plan)
    {
        if (plan.LifecycleStatus != PlanLifecycleStatus.Interrupted ||
            plan.Progress.ExecutingTaskId is not null ||
            plan.InterruptionData is not { } interruption ||
            string.IsNullOrWhiteSpace(interruption.LastCompletedTaskId) ||
            string.IsNullOrWhiteSpace(interruption.InterruptedTaskId) ||
            string.Equals(interruption.LastCompletedTaskId, interruption.InterruptedTaskId, StringComparison.Ordinal))
            return false;

        return plan.Tasks.Any(task =>
                   string.Equals(task.TaskId, interruption.LastCompletedTaskId, StringComparison.Ordinal) &&
                   task.Status == PlanTaskStatus.Complete) &&
               plan.Tasks.Any(task =>
                   string.Equals(task.TaskId, interruption.InterruptedTaskId, StringComparison.Ordinal) &&
                   task.Status == PlanTaskStatus.Pending);
    }

    internal static bool IsSafelyResumable(Plan plan) =>
        plan.LifecycleStatus == PlanLifecycleStatus.Interrupted &&
        (plan.InterruptionData?.Reason.StartsWith(
             "Paused by user",
             StringComparison.OrdinalIgnoreCase) == true ||
         IsReworkPreflightPause(plan) ||
         IsAmendmentPreflightPause(plan) ||
         IsAcceptedWorkContinuation(plan));
}
