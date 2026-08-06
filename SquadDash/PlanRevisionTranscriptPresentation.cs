namespace SquadDash;

internal static class PlanRevisionTranscriptPresentation
{
    internal static string BuildNoChanges(int preservedLockedTaskCount) =>
        preservedLockedTaskCount > 0
            ? $"No changes found in the plan revision. Preserved {preservedLockedTaskCount} completed or active tasks."
            : "No changes found in the plan revision.";

    internal static string BuildApplied(int revisionNumber, int appliedChangeCount, int preservedLockedTaskCount)
    {
        var summary = $"✅ Plan updated to revision {revisionNumber}. " +
                      $"Applied {appliedChangeCount} downstream changes.";
        return preservedLockedTaskCount > 0
            ? summary + $" Preserved {preservedLockedTaskCount} completed or active tasks."
            : summary;
    }
}
