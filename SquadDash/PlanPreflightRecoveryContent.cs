using System.Linq;

namespace SquadDash;

/// <summary>Pure presentation text for the contextual plan-preflight recovery card.</summary>
internal sealed record PlanPreflightRecoveryContent(
    string Title,
    string Summary,
    string ChangedFilesSummary,
    string RecoveryGuidance,
    string TechnicalDetails)
{
    internal string ClipboardText =>
        $"{Title}\n\n{Summary}\n\n{RecoveryGuidance}\n\n{TechnicalDetails}";

    internal static PlanPreflightRecoveryContent From(PlanPreflightBlockedException exception)
    {
        var target = string.IsNullOrWhiteSpace(exception.TargetBranch)
            ? "the requested plan"
            : $"branch '{exception.TargetBranch}'";
        var count = exception.ChangedPaths.Count;
        var files = count == 0
            ? "No changed paths were reported."
            : string.Join("\n", exception.ChangedPaths.Select(path => $"• {path}"));
        var details =
            $"Condition: {exception.Condition}\n" +
            $"Target: {target}\n" +
            $"Changed files: {count}\n\n{files}";

        return new PlanPreflightRecoveryContent(
            "Plan not started",
            $"SquadDash could not prepare {target} because {count} uncommitted " +
            $"{(count == 1 ? "file prevents" : "files prevent")} a safe branch switch. " +
            "No plan work was started.",
            files,
            "Review these changes, then commit or stash them and select Retry. " +
            "SquadDash will not discard or carry uncommitted work automatically.",
            details);
    }

    internal static PlanPreflightRecoveryContent FromRework(
        PlanPreflightBlockedException exception,
        string planTitle,
        string taskTitle)
    {
        var count = exception.ChangedPaths.Count;
        var files = count == 0
            ? "No changed paths were reported."
            : string.Join("\n", exception.ChangedPaths.Select(path => $"• {path}"));
        var details =
            $"Condition: {exception.Condition}\n" +
            $"Plan: {planTitle}\n" +
            $"Reopened task: {taskTitle}\n" +
            $"Changed files: {count}\n\n{files}";

        return new PlanPreflightRecoveryContent(
            "Rework ready — execution paused",
            $"SquadDash reopened “{taskTitle}”, but no new task work started because {count} uncommitted " +
            $"{(count == 1 ? "file prevents" : "files prevent")} safe plan execution.",
            files,
            "Review these changes, then commit or stash them and select Resume Rework. " +
            "The change request and reopened task are already preserved; resuming will not submit them again.",
            details);
    }

    internal static PlanPreflightRecoveryContent FromAmendment(
        PlanPreflightBlockedException exception,
        string planTitle,
        string amendmentTitle)
    {
        var count = exception.ChangedPaths.Count;
        var files = count == 0
            ? "No changed paths were reported."
            : string.Join("\n", exception.ChangedPaths.Select(path => $"• {path}"));
        var details =
            $"Condition: {exception.Condition}\n" +
            $"Plan: {planTitle}\n" +
            $"Amendment: {amendmentTitle}\n" +
            $"Changed files: {count}\n\n{files}";

        return new PlanPreflightRecoveryContent(
            "Amendment ready — execution paused",
            $"SquadDash added “{amendmentTitle}” without reopening completed tasks, but no amendment " +
            $"work started because {count} uncommitted {(count == 1 ? "file prevents" : "files prevent")} safe plan execution.",
            files,
            "Review these changes, then commit or stash them and select Resume Amendment. " +
            "The completed tasks and amendment instructions are already preserved.",
            details);
    }
}

/// <summary>Classifies interruptions that can resume without assessing uncertain task work.</summary>
internal static class PlanRecoveryResumePolicy
{
    internal const string ReworkPreflightReasonPrefix =
        "Rework was prepared, but no new task work started because workspace preflight was blocked.";
    internal const string AmendmentPreflightReasonPrefix =
        "An approval amendment was prepared, but no amendment work started because workspace preflight was blocked.";

    internal static string BuildReworkPreflightReason(string details) =>
        $"{ReworkPreflightReasonPrefix} {details}".Trim();

    internal static string BuildAmendmentPreflightReason(string details) =>
        $"{AmendmentPreflightReasonPrefix} {details}".Trim();

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

    internal static bool IsSafelyResumable(Plan plan) =>
        plan.LifecycleStatus == PlanLifecycleStatus.Interrupted &&
        (plan.InterruptionData?.Reason.StartsWith(
             "Paused by user",
             StringComparison.OrdinalIgnoreCase) == true ||
         IsReworkPreflightPause(plan) ||
         IsAmendmentPreflightPause(plan));
}
