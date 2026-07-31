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
}
